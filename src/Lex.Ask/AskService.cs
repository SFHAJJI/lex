using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Ask;

// The /ask playground: a server-side agent loop over the Lex tools (D31/F10: the only
// non-deterministic component, in its own assembly so the deterministic web tier carries
// no AI code). The model (Azure OpenAI chat completions, v1 surface) composes answers ONLY
// from tool output; disabled unless AOAI_ENDPOINT + AOAI_KEY are configured.
// Stateless per request; capped per IP and per day.
public sealed class AskService(McpCore core)
{
    /// <summary>OTel source for the agent loop; registered by the host when tracing is configured.</summary>
    public const string ActivitySourceName = "Lex.Ask";
    private static readonly System.Diagnostics.ActivitySource Activity = new(ActivitySourceName);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly string? _endpoint = Environment.GetEnvironmentVariable("AOAI_ENDPOINT")?.TrimEnd('/');
    private readonly string? _key = Environment.GetEnvironmentVariable("AOAI_KEY");
    private readonly string _deployment = Environment.GetEnvironmentVariable("AOAI_CHAT_DEPLOYMENT") ?? "gpt-5-mini";
    private readonly int _perIpDaily = EnvInt("ASK_PER_IP_DAILY", 25);
    private readonly int _globalDaily = EnvInt("ASK_GLOBAL_DAILY", 400);
    private readonly ConcurrentDictionary<string, int> _counters = new();
    private readonly SemaphoreSlim _gate = new(4);

    public bool Enabled => !string.IsNullOrEmpty(_endpoint) && !string.IsNullOrEmpty(_key);

    private static int EnvInt(string name, int dflt)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : dflt;

    private const int MaxHistory = 24;
    private const int MaxMessageChars = 4000;
    private const int MaxToolRounds = 8;
    private const int MaxToolResultChars = 20000;

    private static string SystemPrompt(string host) => $"""
        You are the answer layer of Lex, a point-in-time retrieval system for consolidated
        regulatory text (Luxembourg via Legilux, EU via EUR-Lex). You have nine read-only tools
        over signed indexes. Every version carries publisher-asserted validity dates and hashes.
        Today's date (UTC) is {DateTime.UtcNow:yyyy-MM-dd}: use it for "today"/"current" questions
        (one as_of call with this date — do not probe multiple dates).

        Rules, in order of priority:
        1. Ground every factual claim about the law in tool output from THIS conversation.
           When the document is unknown, call search first (add as_of date when the user names one) —
           hits are ARTICLE-level and carry the anchor. Then: as_of for the state on a date
           (long documents: mode=outline first, then mode=select with the anchors you need —
           never pull mode=full on a code); article_history(work, anchor) for "what did Article X
           say over its life / when did it change / was it renumbered"; timeline for whole-document
           versions; diff for what changed between two dates; in_force_on for what applied on a
           date; changes_in_period(from_date, to_date) for ACROSS-the-corpus questions ("what
           changed between 2025 and 2026", "which laws changed most during the pandemic" —
           add order=by_churn to rank by how often each moved); coverage for what Lex holds.
           changes_in_period is DIFFERENT from search: its counts and rankings ARE the answer
           to "which/how many laws changed" questions. Report them directly with the titles and
           permalinks it returned. Do NOT call as_of on each result to confirm a count — the
           count comes from the index, and fetching each work wastes the budget and answers
           nothing. Only fetch a work's text if the user asks what a specific law now SAYS.
           Search hits are POINTERS, never evidence of content:
           after search identifies the work, you MUST call as_of / timeline / diff before answering
           anything about what the text says or how it changed. Never repeat a search that found
           nothing: retry ONCE with the official name or a synonym (e.g. "DORA" -> "digital
           operational resilience", acronyms -> full titles, or the CELEX number like 32022r2554
           for EU acts), then move to the right tool or answer honestly that Lex has no match.
           Typical flow: one search -> pick the best hit's work -> as_of / timeline / diff -> answer.
           You have at most 2 searches per question; coverage is only for questions about what
           Lex holds. Include no URL in your answer that was not returned by a tool in this
           conversation.
        2. Cite what you used: document title, lex_id, validity interval (valid_from -> valid_to),
           and the "permalink" URL returned by the tools, copied VERBATIM — never construct or
           edit URLs yourself. Quote only the relevant provisions.
        3. Lex answers what the rule WAS, never what it means: no interpretation, no legal advice,
           no compliance conclusions. If asked for advice, give the grounded text and say that
           interpretation is out of scope.
        4. Honest refusals: when a tool answers no_version_for_date, outside_observed_window,
           unknown_work or text_withheld, or coverage shows a gap, say plainly what Lex does not
           hold and link the official source. Be PRECISE about which of these it is: "Lex does not
           have this law" and "Lex has this law but not its text" are different statements, and
           claiming the first when the second is true is as wrong as inventing text. A work that
           search returned, or that timeline/in_force_on answers for, IS held — even when its
           text_available is false. In that case say: Lex holds N version(s) with their dates and
           provenance, but stores no text for it, and give the official link. Never fill a gap from general knowledge without
           labelling that part explicitly as not grounded in Lex.
        5. Consolidated texts have no legal effect; only the Journal officiel / Official Journal
           is authentic. Mention this when quoting text verbatim.
        6. Answer in the user's language (French or English). Be compact.
        """;

    private bool TryCount(string ip, out string reason)
    {
        var day = DateTime.UtcNow.ToString("yyyyMMdd");
        if (_counters.Count > 5000)
            foreach (var k in _counters.Keys.Where(k => !k.StartsWith(day, StringComparison.Ordinal)).ToArray())
                _counters.TryRemove(k, out _);
        var g = _counters.AddOrUpdate($"{day}|_global", 1, (_, v) => v + 1);
        var p = _counters.AddOrUpdate($"{day}|{ip}", 1, (_, v) => v + 1);
        reason = g > _globalDaily ? "The shared daily budget for this public playground is used up — come back tomorrow, or connect your own AI via /ai (the MCP endpoint has no cap)."
               : p > _perIpDaily ? "Daily question limit reached for your address — come back tomorrow, or connect your own AI via /ai."
               : "";
        return reason.Length == 0;
    }

    private JsonArray OpenAiTools()
    {
        var arr = new JsonArray();
        foreach (var t in core.ToolDefs().OfType<JsonObject>())
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t["name"]!.DeepClone(),
                    ["description"] = t["description"]!.DeepClone(),
                    ["parameters"] = t["inputSchema"]!.DeepClone(),
                },
            });
        return arr;
    }

    // Truncate oversized tool results without ever producing invalid JSON: shrink the largest
    // string fields (law text) first, then fall back to a wrapped preview.
    private static string TruncateResult(JsonNode node)
    {
        var raw = node.ToJsonString();
        if (raw.Length <= MaxToolResultChars) return raw;
        void ShrinkStrings(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var k in o.Select(p => p.Key).ToArray())
                        if (o[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 4000)
                            o[k] = s[..4000] + " …[truncated — full text at the permalink]";
                        else ShrinkStrings(o[k]);
                    break;
                case JsonArray a:
                    foreach (var item in a) ShrinkStrings(item);
                    break;
            }
        }
        var clone = node.DeepClone();
        ShrinkStrings(clone);
        raw = clone.ToJsonString();
        return raw.Length <= MaxToolResultChars
            ? raw
            : new JsonObject { ["truncated_preview"] = raw[..MaxToolResultChars] }.ToJsonString();
    }

    // Pull a compact evidence summary out of a tool result: overall status + the cited
    // documents (any object carrying a lex_id), for the evidence list in the UI and the
    // grounding check in evals. The cap only bounds the summary, not the model's context.
    private static readonly System.Text.Json.JsonSerializerOptions UiJson = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The reply body: prose, the raw trace, and the merged rendering directive.</summary>
    private static JsonObject Body(string reply, JsonArray trace, List<UiEffect> effects)
    {
        var body = new JsonObject { ["reply"] = reply, ["trace"] = trace };
        var merged = UiEffect.Merge(effects);
        // A turn that used tools and produced nothing to render is a refusal — the most
        // characteristic thing this product does. It gets a view like any other answer,
        // rather than silently degrading to a wall of prose.
        if (merged.IsEmpty && trace.Count > 0)
            merged = new UiEffect(Gap: new GapView(
                Status: "no_result",
                Work: null, Date: null,
                Explanation: "Lex found nothing matching that in what it holds. This is a limit of the corpus, not a hedge — see coverage for exactly what is and is not held.",
                Available: []));
        if (!merged.IsEmpty)
            body["ui"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(merged, UiJson));
        return body;
    }

    private static (string? Status, JsonArray Docs) Summarize(JsonNode result)
    {
        string? status = null;
        var docs = new JsonArray();

        // Pinpoints: the exact provision text the tool returned, quotable next to the
        // claim it grounds (anti-misgrounding: a reader can compare the answer against
        // source text without leaving the page).
        static JsonObject BuildDoc(JsonObject source, JsonArray? provisions)
        {
            var d = new JsonObject
            {
                ["lex_id"] = source["lex_id"]?.DeepClone(),
                ["title"] = source["title"]?.DeepClone(),
                ["valid_from"] = source["valid_from"]?.DeepClone(),
                ["valid_to"] = source["valid_to"]?.DeepClone(),
                ["permalink"] = source["permalink"]?.DeepClone(),
            };
            if (provisions is not null)
            {
                var pins = new JsonArray();
                foreach (var p in provisions.OfType<JsonObject>().Take(2))
                {
                    var text = (p["text"] ?? p["text_md"])?.GetValue<string>();
                    if (text is null && p["anchor"] is null) continue;
                    pins.Add(new JsonObject
                    {
                        ["anchor"] = p["anchor"]?.DeepClone(),
                        ["quote"] = text is null ? null : text.Length > 280 ? text[..280] + "…" : text,
                        ["permalink"] = p["permalink"]?.DeepClone(),
                    });
                }
                if (pins.Count > 0) d["pinpoints"] = pins;
            }
            else if (source["snippet"] is not null)   // provision-level search hit
            {
                d["anchor"] = source["anchor"]?.DeepClone();
                d["snippet"] = source["snippet"]?.DeepClone();
                d["provision_id"] = source["provision_id"]?.DeepClone();
            }
            return d;
        }

        void Walk(JsonNode? n)
        {
            if (docs.Count >= 24 && status is not null) return;
            switch (n)
            {
                case JsonObject o:
                    status ??= (o["envelope"]?["status"] ?? o["status"])?.GetValue<string>();
                    // article_history has no lex_id at all: it is keyed by work + anchor, and its
                    // evidence is the list of states. Without this it produced ZERO evidence rows
                    // and its own permalinks read as ungrounded when the model cited them.
                    if (o["anchor"] is not null && o["states"] is JsonArray states && docs.Count < 24)
                    {
                        var first = states.OfType<JsonObject>().FirstOrDefault();
                        var last = states.OfType<JsonObject>().LastOrDefault();
                        var pins = new JsonArray(states.OfType<JsonObject>().Take(6).Select(s =>
                            (JsonNode)new JsonObject
                            {
                                ["anchor"] = o["anchor"]?.DeepClone(),
                                ["quote"] = $"in force {s["valid_from"]} → {s["valid_to"] ?? "open"} (text sha {(s["text_sha256"]?.GetValue<string>() ?? "")[..Math.Min(12, (s["text_sha256"]?.GetValue<string>() ?? "").Length)]})",
                                ["permalink"] = s["permalink"]?.DeepClone(),
                            }).ToArray());
                        docs.Add(new JsonObject
                        {
                            ["lex_id"] = o["work"]?.DeepClone(),
                            ["title"] = $"{o["work"]} · {o["anchor"]} — {o["distinct_texts"]} distinct text(s)",
                            ["valid_from"] = first?["valid_from"]?.DeepClone(),
                            ["valid_to"] = last?["valid_to"]?.DeepClone(),
                            ["permalink"] = first?["permalink"]?.DeepClone(),
                            ["pinpoints"] = pins,
                        });
                        break;
                    }
                    // changes_in_period rows are works, not versions: they carry "work" rather
                    // than "lex_id", and the counts ARE the evidence.
                    if (o["work"] is not null && o["versions_in_period"] is not null && docs.Count < 24)
                    {
                        docs.Add(new JsonObject
                        {
                            ["lex_id"] = o["work"]?.DeepClone(),
                            ["title"] = o["title"]?.DeepClone(),
                            ["valid_from"] = o["first_change"]?.DeepClone(),
                            ["valid_to"] = o["last_change"]?.DeepClone(),
                            // permalink is the row's canonical link (as for every other tool);
                            // the diff is an extra affordance, not a replacement — dropping it
                            // made legitimately-cited URLs look ungrounded.
                            ["permalink"] = o["permalink"]?.DeepClone(),
                            ["diff_permalink"] = o["diff_permalink"]?.DeepClone(),
                            ["snippet"] = $"{o["versions_in_period"]} new version(s) in the window, {o["versions_total"]} in all",
                        });
                        break;
                    }
                    // as_of shape: provisions ride as a SIBLING of document — pair them
                    if (o["document"] is JsonObject docObj && docObj["lex_id"] is not null && docs.Count < 24)
                    {
                        docs.Add(BuildDoc(docObj, o["provisions"] as JsonArray));
                        foreach (var p in o) if (p.Key is not ("document" or "provisions")) Walk(p.Value);
                    }
                    else if (o["lex_id"] is not null && docs.Count < 24)
                        docs.Add(BuildDoc(o, o["provisions"] as JsonArray));
                    else foreach (var p in o) Walk(p.Value);
                    break;
                case JsonArray a:
                    foreach (var item in a) Walk(item);
                    break;
            }
        }
        Walk(result);
        return (status, docs);
    }

    public async Task<(int Status, JsonObject Body)> AskAsync(JsonArray history, string ip, string host, CancellationToken ct)
    {
        if (!Enabled)
            return (503, new JsonObject { ["error"] = "The playground is not enabled on this deployment. Connect your own AI instead: /ai." });
        if (history.Count is 0 or > MaxHistory)
            return (400, new JsonObject { ["error"] = $"Send 1–{MaxHistory} messages." });

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(host) } };
        foreach (var m in history)
        {
            var role = m?["role"]?.GetValue<string>();
            var content = m?["content"]?.GetValue<string>() ?? "";
            if (role is not ("user" or "assistant")) return (400, new JsonObject { ["error"] = "Roles must be user/assistant." });
            if (content.Length > MaxMessageChars) return (400, new JsonObject { ["error"] = $"Messages are capped at {MaxMessageChars} characters." });
            messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        if (!TryCount(ip, out var why)) return (429, new JsonObject { ["error"] = why });

        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(20)))
            return (503, new JsonObject { ["error"] = "The playground is busy — try again in a moment." });
        try
        {
            using var askSpan = Activity.StartActivity("ask");
            askSpan?.SetTag("gen_ai.request.model", _deployment);
            var trace = new JsonArray();
            var searchCalls = 0;
            var worksFound = new Dictionary<string, string>(StringComparer.Ordinal);
            var textToolUsed = false;
            // D31 shape: effects are collected across every tool call in the turn and merged
            // into ONE payload, so a single reply can carry prose plus more than one view.
            var effects = new List<UiEffect>();
            // Reasoning shares the completion budget: over a large tool result the model can
            // spend all of it thinking and return an empty message. When that happens we retry
            // the same conversation once at lower effort, which leaves room to actually write.
            var effort = "high";
            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var req = new JsonObject
                {
                    ["model"] = _deployment,
                    ["messages"] = messages.DeepClone(),
                    ["tools"] = OpenAiTools(),
                    ["tool_choice"] = round == MaxToolRounds ? "none" : "auto",
                    ["max_completion_tokens"] = 16000,
                    ["reasoning_effort"] = effort,
                };
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/openai/v1/chat/completions")
                { Content = new StringContent(req.ToJsonString(), Encoding.UTF8, "application/json") };
                httpReq.Headers.Add("api-key", _key);
                using var resp = await _http.SendAsync(httpReq, ct);
                var respText = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[ask] upstream {(int)resp.StatusCode}: {respText[..Math.Min(500, respText.Length)]}");
                    return (502, new JsonObject { ["error"] = "The model upstream returned an error — try again shortly." });
                }
                var parsed = JsonNode.Parse(respText);
                if (parsed?["usage"]?["total_tokens"] is { } tt)
                    Console.Error.WriteLine($"[ask] ip={ip} round={round} tokens={tt}");
                var choice = parsed?["choices"]?[0];
                var msg = choice?["message"];
                var toolCalls = msg?["tool_calls"] as JsonArray;

                if (toolCalls is { Count: > 0 })
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = msg!["content"]?.DeepClone(),
                        ["tool_calls"] = toolCalls.DeepClone(),
                    });
                    foreach (var tc in toolCalls.OfType<JsonObject>())
                    {
                        var id = tc["id"]?.GetValue<string>() ?? "";
                        var fn = tc["function"];
                        var name = fn?["name"]?.GetValue<string>() ?? "";
                        var argsRaw = fn?["arguments"]?.GetValue<string>() ?? "{}";
                        string result;
                        var entry = new JsonObject { ["tool"] = name };
                        try
                        {
                            var args = JsonNode.Parse(argsRaw) as JsonObject ?? [];
                            entry["args"] = args.DeepClone();
                            // Deterministic routing guard: a mini model can churn on search;
                            // after two searches the tool redirects it to the state tools — and
                            // hands back the works it has ALREADY found, because "you are out of
                            // searches" without saying what was found is what made it give up.
                            if (name == "search" && ++searchCalls > 2)
                            {
                                entry["status"] = "search_budget_exhausted";
                                trace.Add(entry);
                                var err = new JsonObject
                                {
                                    ["error"] = "search budget for this question is exhausted; do NOT search again. Call as_of / timeline / diff / article_history with one of the works already found below, or answer honestly from what you have.",
                                };
                                if (worksFound.Count > 0)
                                    err["works_already_found"] = new JsonArray(worksFound.Take(8)
                                        .Select(w => (JsonNode)new JsonObject { ["work"] = w.Key, ["title"] = w.Value }).ToArray());
                                messages.Add(new JsonObject
                                {
                                    ["role"] = "tool", ["tool_call_id"] = id, ["content"] = err.ToJsonString(),
                                });
                                continue;
                            }
                            using var toolSpan = Activity.StartActivity("tool");
                            toolSpan?.SetTag("gen_ai.tool.name", name);
                            var node = core.CallTool(name, args);
                            var (st, docs) = Summarize(node);
                            if (name is "as_of" or "timeline" or "diff" or "article_history" or "in_force_on")
                                textToolUsed = true;
                            toolSpan?.SetTag("lex.status", st ?? "ok");
                            toolSpan?.SetTag("lex.docs", docs.Count);
                            entry["status"] = st;
                            entry["docs"] = docs;
                            var eff = UiMapper.From(name, args, node);
                            if (!eff.IsEmpty) effects.Add(eff);
                            // Remember every work any tool surfaced: the work id is what the state
                            // tools need, and it is the thing the model most often loses track of.
                            foreach (var d in docs.OfType<JsonObject>())
                                if (d["lex_id"]?.GetValue<string>() is { } lid)
                                {
                                    var parts = lid.Split(':');
                                    if (parts.Length >= 2)
                                        worksFound.TryAdd($"{parts[0]}:{parts[1]}", d["title"]?.GetValue<string>() ?? "");
                                }
                            // coverage answers "what does Lex hold" — it is not an answer to
                            // "what did this text say". Observed failure: the model searches,
                            // finds the right work, then calls coverage and stops. When that
                            // happens the harness hands the work ids straight back.
                            if (name == "coverage" && !textToolUsed && worksFound.Count > 0)
                            {
                                var wrapped = new JsonObject
                                {
                                    ["coverage"] = node.DeepClone(),
                                    ["next_step_required"] = "coverage describes what is held; it does not contain the text of any law. You have already located the works below — call as_of (mode=select with anchors when you want specific articles), timeline, diff or article_history on one of them before answering anything about what a text said.",
                                    ["works_already_found"] = new JsonArray(worksFound.Take(8)
                                        .Select(w => (JsonNode)new JsonObject { ["work"] = w.Key, ["title"] = w.Value }).ToArray()),
                                };
                                node = wrapped;
                            }
                            result = TruncateResult(node);
                        }
                        catch (Exception ex)
                        {
                            entry["status"] = "error";
                            result = new JsonObject { ["error"] = ex.Message }.ToJsonString();
                        }
                        trace.Add(entry);
                        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result });
                    }
                    continue;
                }

                var reply = msg?["content"]?.GetValue<string>() ?? "";
                if (reply.Length == 0 && effort == "high")
                {
                    // Budget spent on reasoning, nothing written. Same evidence, less thinking.
                    Console.Error.WriteLine("[ask] empty reply at high effort — retrying at medium");
                    effort = "medium";
                    continue;
                }
                if (reply.Length == 0)
                    reply = trace.Count > 0
                        ? "I retrieved the evidence below but could not compose an answer — try asking for a narrower slice (a single law, or a shorter period)."
                        : "I could not produce an answer — try rephrasing.";
                return (200, Body(reply, trace, effects));
            }
            return (200, Body("Tool budget for one question exhausted — try a narrower question.", trace, effects));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (499, new JsonObject { ["error"] = "Request cancelled." });
        }
        catch (TaskCanceledException)
        {
            return (504, new JsonObject { ["error"] = "The model took too long — try a narrower question." });
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[ask] upstream unreachable: {ex.Message}");
            return (502, new JsonObject { ["error"] = "The model upstream is unreachable right now — try again shortly." });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ask] {ex}");
            return (500, new JsonObject { ["error"] = "Unexpected error in the playground." });
        }
        finally
        {
            _gate.Release();
        }
    }
}
