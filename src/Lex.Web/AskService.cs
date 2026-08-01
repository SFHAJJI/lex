using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Web;

// The /ask playground: a server-side agent loop over the seven Lex tools.
// The model (Azure OpenAI chat completions, v1 surface) composes answers ONLY from tool
// output; Lex.Web itself stays deterministic — this class is additive and disabled unless
// AOAI_ENDPOINT + AOAI_KEY are configured. Stateless per request; capped per IP and per day.
public sealed class AskService(McpCore core)
{
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
    private const int MaxToolRounds = 6;
    private const int MaxToolResultChars = 20000;

    private static string SystemPrompt(string host) => $"""
        You are the answer layer of Lex, a point-in-time retrieval system for consolidated
        regulatory text (Luxembourg via Legilux, EU via EUR-Lex). You have seven read-only tools
        over signed indexes. Every version carries publisher-asserted validity dates and hashes.

        Rules, in order of priority:
        1. Ground every factual claim about the law in tool output from THIS conversation.
           When the document is unknown, call search first (add as_of date when the user names one),
           then as_of for the exact state. Use timeline for "how did it change", diff for "what
           changed between two dates", in_force_on for "what applied on date X", coverage for
           "what do you hold".
        2. Cite what you used: document title, lex_id, validity interval (valid_from -> valid_to),
           and a permalink https://{host}/PUBLISHER/WORK/DATE where PUBLISHER is e.g. lu-legilux,
           WORK is the part of the lex_id between the first and second ":" and DATE is ISO.
           Quote only the relevant provisions.
        3. Lex answers what the rule WAS, never what it means: no interpretation, no legal advice,
           no compliance conclusions. If asked for advice, give the grounded text and say that
           interpretation is out of scope.
        4. Honest refusals: when a tool answers no_version_for_date, outside_observed_window,
           unknown_work or text_withheld, or coverage shows a gap, say plainly what Lex does not
           hold and link the official source. Never fill a gap from general knowledge without
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

    public async Task<(int Status, JsonObject Body)> AskAsync(JsonArray history, string ip, string host)
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
            var trace = new JsonArray();
            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var req = new JsonObject
                {
                    ["model"] = _deployment,
                    ["messages"] = messages.DeepClone(),
                    ["tools"] = OpenAiTools(),
                    ["tool_choice"] = round == MaxToolRounds ? "none" : "auto",
                    ["max_completion_tokens"] = 3000,
                    ["reasoning_effort"] = "low",
                };
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/openai/v1/chat/completions")
                { Content = new StringContent(req.ToJsonString(), Encoding.UTF8, "application/json") };
                httpReq.Headers.Add("api-key", _key);
                using var resp = await _http.SendAsync(httpReq);
                var respText = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[ask] upstream {(int)resp.StatusCode}: {respText[..Math.Min(500, respText.Length)]}");
                    return (502, new JsonObject { ["error"] = "The model upstream returned an error — try again shortly." });
                }
                var choice = JsonNode.Parse(respText)?["choices"]?[0];
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
                        try
                        {
                            var args = JsonNode.Parse(argsRaw) as JsonObject ?? [];
                            trace.Add(new JsonObject { ["tool"] = name, ["args"] = args.DeepClone() });
                            result = core.CallTool(name, args).ToJsonString();
                        }
                        catch (Exception ex)
                        {
                            result = new JsonObject { ["error"] = ex.Message }.ToJsonString();
                        }
                        if (result.Length > MaxToolResultChars)
                            result = result[..MaxToolResultChars] + "\" …(truncated — retrieve the full text at the cited permalink)\"}";
                        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result });
                    }
                    continue;
                }

                var reply = msg?["content"]?.GetValue<string>() ?? "";
                if (reply.Length == 0) reply = "I could not produce an answer — try rephrasing.";
                return (200, new JsonObject { ["reply"] = reply, ["trace"] = trace });
            }
            return (200, new JsonObject { ["reply"] = "Tool budget for one question exhausted — try a narrower question.", ["trace"] = trace });
        }
        catch (TaskCanceledException)
        {
            return (504, new JsonObject { ["error"] = "The model took too long — try a narrower question." });
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
