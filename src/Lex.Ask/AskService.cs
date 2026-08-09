using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using Lex.Mcp;

namespace Lex.Ask;

// The /ask playground: a server-side agent loop over the Lex tools (D31/F10: the only
// non-deterministic component, in its own assembly so the deterministic web tier carries
// no AI code). The model (Azure OpenAI chat completions, v1 surface) composes answers ONLY
// from tool output; disabled unless the endpoint and either managed identity or a legacy key are configured.
// Stateless per request; capped per IP and per day.
public sealed class AskService(McpCore core)
{
    internal sealed class WorkResolutionGuard
    {
        internal sealed record GuardClarification(
            AgentClarification Display,
            IReadOnlyList<GuardChoice> Choices);
        internal sealed record GuardChoice(string Label, string Value);

        private const string ChoicePrefix = "Clarification choice: ";
        private const string NoChoice = "none of these";

        private readonly HashSet<string> _resolved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _prior = new(StringComparer.Ordinal);
        private readonly List<(string Work, string Title)> _candidates = [];
        private bool _searchObserved;
        private bool _workIndependentAnswerObserved;
        private bool _currentAuthorityObserved;
        private bool _priorContextUsed;

        public IReadOnlyCollection<string> ResolvedWorks => _resolved;

        public void ObserveSearch(JsonNode result, bool isRawUserQuery = true)
            => ObserveSearch(result, isRawUserQuery, allowDirectProvisionAuthority: true,
                collectCandidates: true);

        public void ObserveCurrentUserSearch(JsonNode result, bool hasPriorContext)
            => ObserveSearch(result, isRawUserQuery: true,
                allowDirectProvisionAuthority: !hasPriorContext,
                collectCandidates: !hasPriorContext);

        public void ObservePriorUserSearch(JsonNode result)
            => ObserveSearch(result, isRawUserQuery: true, allowDirectProvisionAuthority: false,
                collectCandidates: false);

        private void ObserveSearch(JsonNode result, bool isRawUserQuery,
            bool allowDirectProvisionAuthority, bool collectCandidates)
        {
            // A preflight that produced no usable envelope is still an observed search. Work-
            // specific tools must fail closed rather than treating an empty/malformed result as
            // if no authority check had run.
            _searchObserved = true;
            var latestCandidates = new List<(string Work, string Title)>();
            foreach (var response in result is JsonArray array
                         ? array.OfType<JsonObject>()
                         : result is JsonObject single ? [single] : [])
            {
                if (response["query_plan"] is not JsonObject plan) continue;
                var status = plan["global_work_resolution_status"]?.GetValue<string>()
                             ?? plan["work_resolution_status"]?.GetValue<string>();
                var resolutions = plan["global_work_resolutions"] as JsonArray
                                  ?? plan["work_resolutions"] as JsonArray;
                if (isRawUserQuery && resolutions is not null)
                    foreach (var resolution in resolutions.OfType<JsonObject>()
                                 .Where(item => item["status"]?.GetValue<string>() == "resolved"))
                        foreach (var candidate in resolution["candidates"]?.AsArray() ?? [])
                            if (candidate?.GetValue<string>() is { } work)
                            {
                                _resolved.Add(WorkKey(work));
                                _currentAuthorityObserved = true;
                            }
                // A problem description with no named law may select a work only from an actual
                // provision hit. Standalone work discovery has no anchor and stays a candidate
                // until the user confirms it.
                if (allowDirectProvisionAuthority && status == "not_requested"
                    && response["hits"] is JsonArray hits)
                    foreach (var hit in hits.OfType<JsonObject>()
                                 .Where(HasDirectProvisionEvidence))
                        if (hit["lex_id"]?.GetValue<string>() is { } lexId)
                        {
                            _resolved.Add(WorkKey(lexId));
                            _currentAuthorityObserved = true;
                        }
                if (collectCandidates && response["hits"] is JsonArray candidateHits)
                    foreach (var hit in candidateHits.OfType<JsonObject>()
                                 .Where(item => item["anchor"]?.GetValue<string>() is not { Length: > 0 }))
                        if (hit["lex_id"]?.GetValue<string>() is { } lexId)
                            AddCandidate(latestCandidates, WorkKey(lexId),
                                hit["title"]?.GetValue<string>() ?? "");
            }
            foreach (var candidate in latestCandidates.AsEnumerable().Reverse())
            {
                _candidates.RemoveAll(item => item.Work == candidate.Work);
                _candidates.Insert(0, candidate);
            }
            if (_candidates.Count > 8) _candidates.RemoveRange(8, _candidates.Count - 8);
        }

        public void AuthorizePriorWorks(IEnumerable<string> works)
        {
            foreach (var work in works.Select(WorkKey).Where(IsCandidateWork))
            {
                _resolved.Add(work);
                _prior.Add(work);
            }
        }

        public bool Allows(string tool, JsonObject args)
        {
            if (!_searchObserved) return true;
            var work = tool == "provenance"
                ? args["lex_id"]?.GetValue<string>()
                : args["work"]?.GetValue<string>();
            if (work is null || tool is not (
                    "as_of" or "timeline" or "diff" or "article_history" or "cited_by" or "provenance"))
                return true;
            var key = WorkKey(work);
            var allowed = _resolved.Contains(key);
            if (allowed && _prior.Contains(key)) _priorContextUsed = true;
            return allowed;
        }

        public void ObserveUserConfirmation(string query)
        {
            var trimmed = query.Trim();
            var value = trimmed.StartsWith(ChoicePrefix, StringComparison.Ordinal)
                ? trimmed[ChoicePrefix.Length..].Trim()
                : trimmed;
            if (IsCandidateWork(value))
            {
                _resolved.Add(value);
                _currentAuthorityObserved = true;
            }
        }

        public static bool IsExplicitNonSelection(string query) =>
            string.Equals(query.Trim(), NoChoice, StringComparison.Ordinal)
            || string.Equals(query.Trim(), ChoicePrefix + NoChoice, StringComparison.Ordinal);

        public void ObserveWorkIndependentAnswer() => _workIndependentAnswerObserved = true;

        public GuardClarification? ClarificationFor(string? attemptedWork)
        {
            if (_workIndependentAnswerObserved || _currentAuthorityObserved || _priorContextUsed
                || _candidates.Count == 0)
                return null;
            var attempted = attemptedWork is null ? null : WorkKey(attemptedWork);
            var ordered = _candidates
                .OrderByDescending(candidate => candidate.Work == attempted)
                .Take(4)
                .ToList();
            var choices = ordered.Select((candidate, index) => new GuardChoice(
                CandidateOption(candidate.Title, candidate.Work, index + 1),
                candidate.Work)).ToList();
            if (choices.Count == 1)
                choices.Add(new GuardChoice(
                    "None of these; I will add more details", NoChoice));
            var clarification = new AgentClarification(
                "Lex found possible instruments but no direct provision evidence. Which instrument should it use?",
                choices.Select(choice => choice.Label).ToArray());
            var display = AgentAnswerContract.Validate(new AgentAnswerDraft(
                AgentAnswerStatus.Clarify, clarification.Question, [], [], null, clarification), []).Clarification!;
            return new GuardClarification(display, choices);
        }

        private static void AddCandidate(
            List<(string Work, string Title)> candidates, string work, string title)
        {
            if (!IsCandidateWork(work) || candidates.Any(candidate => candidate.Work == work)) return;
            candidates.Add((work, title.Trim()));
        }

        private static bool HasDirectProvisionEvidence(JsonObject hit)
        {
            if (hit["anchor"]?.GetValue<string>() is not { Length: > 0 }) return false;
            var reasons = hit["match_reasons"] as JsonArray;
            return reasons?.Any(reason => reason?.GetValue<string>() is
                "keyword" or "fuzzy" or "semantic") == true;
        }

        private static bool IsCandidateWork(string work) =>
            work.Length is > 0 and <= 1_000
            && !work.Contains("://", StringComparison.OrdinalIgnoreCase)
            && (work.StartsWith("eu-eurlex:", StringComparison.Ordinal)
                || work.StartsWith("lu-legilux:", StringComparison.Ordinal))
            && work.Count(character => character == ':') == 1;

        private static string CandidateOption(string title, string work, int ordinal)
        {
            if (title.Contains("://", StringComparison.OrdinalIgnoreCase)) title = "";
            var shortWork = work.Length <= 28 ? work : work[..25] + "...";
            var suffix = $" [{ordinal}: {shortWork}]";
            if (string.IsNullOrWhiteSpace(title)) return $"Instrument{suffix}";
            var boundedTitle = title[..Math.Min(title.Length, 100 - suffix.Length)].TrimEnd();
            return boundedTitle + suffix;
        }

        private static string WorkKey(string value)
        {
            var parts = value.Split(':');
            return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : value;
        }
    }

    /// <summary>OTel source for the agent loop; registered by the host when tracing is configured.</summary>
    public const string ActivitySourceName = "Lex.Ask";
    private static readonly System.Diagnostics.ActivitySource Activity = new(ActivitySourceName);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly string? _endpoint = Environment.GetEnvironmentVariable("AOAI_ENDPOINT")?.TrimEnd('/');
    private readonly string? _key = Environment.GetEnvironmentVariable("AOAI_KEY");
    private readonly bool _useManagedIdentity =
        Environment.GetEnvironmentVariable("AOAI_USE_MANAGED_IDENTITY") == "1";
    private readonly TokenCredential _credential = new DefaultAzureCredential();
    private readonly string _deployment = Environment.GetEnvironmentVariable("AOAI_CHAT_DEPLOYMENT") ?? "gpt-5-mini";
    private readonly int _perIpDaily = EnvInt("ASK_PER_IP_DAILY", 25);
    private readonly int _globalDaily = EnvInt("ASK_GLOBAL_DAILY", 400);
    private readonly ConcurrentDictionary<string, int> _counters = new();
    private readonly SemaphoreSlim _gate = new(4);
    private AgentAnswerFinalizer? _answerFinalizer;

    public bool Enabled => !string.IsNullOrEmpty(_endpoint)
                           && (_useManagedIdentity || !string.IsNullOrEmpty(_key));

    private static int EnvInt(string name, int dflt)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : dflt;

    private const int MaxHistory = 24;
    private const int MaxUserMessageChars = 1000;
    private const int MaxAssistantMessageChars = 4000;
    private const int MaxContextResolutionQueries = 3;
    private const int MaxToolRounds = 8;
    private const int MaxToolResultChars = 20000;

    internal static IReadOnlyList<string> ResolvePriorUserWorks(
        IReadOnlyList<string> userQueries, Func<string, JsonNode?> resolve)
    {
        var prior = new WorkResolutionGuard();
        foreach (var query in userQueries.SkipLast(1).Reverse().Take(MaxContextResolutionQueries))
        {
            if (WorkResolutionGuard.IsExplicitNonSelection(query)) continue;
            prior.ObserveUserConfirmation(query);
            if (prior.ResolvedWorks.Count == 0 && resolve(query) is { } result)
                prior.ObservePriorUserSearch(result);
            if (prior.ResolvedWorks.Count > 0) break;
        }
        return prior.ResolvedWorks.Order(StringComparer.Ordinal).ToArray();
    }

    private static string SystemPrompt(string host, int toolCount) => $"""
        You are the answer layer of Lex, a point-in-time retrieval system for consolidated
        regulatory text (Luxembourg via Legilux, EU via EUR-Lex). You have {toolCount} read-only tools
        over signed indexes. Every version carries publisher-asserted timeline coordinates, explicit
        timeline_semantics and hashes. Legilux publisher_applicability dates may be described as in
        force; EUR-Lex official_consolidation_state dates identify wording states and are NOT
        entry-into-force or application dates.
        Today's date (UTC) is {DateTime.UtcNow:yyyy-MM-dd}: use it for "today"/"current" questions
        (one as_of call with this date — do not probe multiple dates).

        Rules, in order of priority:
        1. Ground every factual claim about the law in tool output from THIS conversation.
           When the document is unknown, call search first (add as_of date when the user names one) —
           hits are ARTICLE-level and carry the anchor. Then: as_of for the state on a date
           (long documents: mode=outline first, then mode=select with the anchors you need —
           never pull mode=full on a code); article_history(work, anchor) for "what did Article X
           say over its life / when did it change / was it renumbered"; timeline for whole-document
           versions; diff for what changed between two dates (for a named article, pass the
           held anchor returned by search so the workspace opens that article); in_force_on for the publisher state
           covering a date (legal applicability only when timeline_semantics says so);
           changes_in_period(from_date, to_date) for ACROSS-the-corpus questions ("what
           changed between 2025 and 2026", "which laws changed most during the pandemic" —
           add order=by_churn to rank by how often each moved); coverage for what Lex holds.
           changes_in_period is DIFFERENT from search: its counts and rankings ARE the answer
           to "which/how many laws changed" questions. Report them directly with the titles and
           permalinks it returned. Do NOT call as_of on each result to confirm a count — the
           count comes from the index, and fetching each work wastes the budget and answers
           nothing. Only fetch a work's text if the user asks what a specific law now SAYS.
           Search hits are POINTERS, never evidence of content:
           after search identifies the work, you MUST call as_of / timeline / diff before answering
           anything about what the text says or how it changed. Never repeat the same search.
           Read query_plan before selecting a work. `resolved` work_resolutions are deterministic
           resolutions of the user's raw words and their work_constraints may be used together
           for multi-law questions. Check global_work_resolution_status before the publisher-local
           status. If either relevant status is ambiguous, unresolved, or
           unavailable, do not select a candidate automatically: state the candidates or the
           availability problem and ask for confirmation when needed. `not_requested` means the
           user described a problem without naming a law; use direct provision evidence and
           discovery as candidates rather than claiming a missing law. Do not replace resolved
           works with a law name you generated. If article_number is present
           but no hit carries article_intent, do not substitute another article; report that the
           requested provision was not found in the selected scope.
           Retry ONCE with the official name or a synonym (e.g. "DORA" -> "digital
           operational resilience", acronyms -> full titles, or the CELEX number like 32022r2554
           for EU acts), then move to the right tool or answer honestly that Lex has no match.
           Typical flow: one search -> pick the best hit's work -> as_of / timeline / diff -> answer.
           You have at most 2 searches per question; coverage is only for questions about what
           Lex holds. Include no URL in your answer that was not returned by a tool in this
           conversation.
           Interpret a bare year range as the complete inclusive calendar window: 1 January of
           the first year through 31 December of the last year, unless the user states other dates.
        2. Cite what you used: document title, lex_id, publisher timeline interval
           (valid_from -> valid_to), timeline_semantics,
           and the "permalink" URL returned by the tools, copied VERBATIM — never construct or
           edit URLs yourself. Quote only the relevant provisions.
        3. Lex answers what the rule WAS, never what it means: no interpretation, no legal advice,
           no compliance conclusions. If asked for advice, give the grounded text and say that
           interpretation is out of scope.
        4. Honest refusals: when a tool answers no_version_for_date, outside_observed_window,
           unknown_work, text_withheld or text_not_available, or coverage shows a gap, say plainly what Lex does not
           hold and link the official source. Be PRECISE about which of these it is: "Lex does not
           have this law" and "Lex has this law but not its text" are different statements, and
           claiming the first when the second is true is as wrong as inventing text. A work that
           search returned, or that timeline/in_force_on answers for, IS held — even when its
           text_available is false. In that case say: Lex holds N version(s) with their dates and
           provenance, but stores no text for it, and give the official link. Never fill a gap from general knowledge without
           labelling that part explicitly as not grounded in Lex.
        5. Consolidated texts have no legal effect; only the Journal officiel / Official Journal
           is authentic. Mention this when quoting text verbatim.
        5b. The workspace behind you has controls, and your tool arguments set them. Use
           jurisdiction, hierarchy, domain, source_class, act_form, binding_status and language
           whenever the question names that scope. Never translate an EU request into Luxembourg
           document classes or a Luxembourg request into EU act forms. "!RECUEIL,!CODE_RECUEIL"
           means legal instruments while excluding Luxembourg thematic shelves; exact source
           classes and normalized hierarchy values come from the tool schemas and coverage.
           Use cited_by(work) for "what refers to this law",
           "who amended it", "what depends on it": it reads the publisher's own cross-references
           backwards and no search phrasing can answer it.
        6. Answer in the user's language (French or English). Be compact. Never use an em dash
           or an en dash: use a comma, a colon or a full stop. No exceptions.
        7. ACT, never ask permission for routine retrieval. Do not reply with "shall I…" or an offer
           to look something up: call the tool and answer. When a question is vague (a period
           without exact dates, a law without a date) choose the most reasonable reading,
           SAY which reading you used in one clause, and give the answer. This does not override
           rule 1: ask the user to choose when work resolution is ambiguous or unresolved. You
           may also ask for a genuinely missing subject (a question with no identifiable law,
           date or topic at all).
        """;

    private bool TryCount(string ip, out string reason)
    {
        var day = DateTime.UtcNow.ToString("yyyyMMdd");
        if (_counters.Count > 5000)
            foreach (var k in _counters.Keys.Where(k => !k.StartsWith(day, StringComparison.Ordinal)).ToArray())
                _counters.TryRemove(k, out _);
        var g = _counters.AddOrUpdate($"{day}|_global", 1, (_, v) => v + 1);
        var p = _counters.AddOrUpdate($"{day}|{ip}", 1, (_, v) => v + 1);
        reason = g > _globalDaily ? "The shared daily budget for this public playground is used up. Come back tomorrow, or connect your own AI via /ai (the MCP endpoint has no cap)."
               : p > _perIpDaily ? "Daily question limit reached for your address. Come back tomorrow, or connect your own AI via /ai."
               : "";
        return reason.Length == 0;
    }

    /// <summary>
    /// The tools, minus any the turn has finished with.
    ///
    /// Search twice with hits and the useful move is to open what you found; searching a third
    /// time is a loop, and it happened in one run out of three when asked for a single named
    /// article. Rejecting the call afterwards still costs a full round of the model emitting it,
    /// so the tool is withdrawn instead, the same way the list tools are withdrawn once a list is
    /// on screen. Structure beats hoping the model reasons its way out.
    /// </summary>
    private JsonArray OpenAiTools(bool withSearch = true)
    {
        var arr = new JsonArray();
        foreach (var t in core.ToolDefs().OfType<JsonObject>())
        {
            if (!withSearch && t["name"]?.GetValue<string>() == "search") continue;
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
        }
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
                            o[k] = s[..4000] + " …[truncated, full text at the permalink]";
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
    private static JsonObject Body(
        string reply,
        JsonArray trace,
        List<UiEffect> effects,
        AgentClarification? clarification = null,
        IReadOnlyList<WorkResolutionGuard.GuardChoice>? clarificationChoices = null)
    {
        var body = new JsonObject { ["reply"] = reply, ["trace"] = trace };
        if (clarification is not null)
        {
            var serialized = JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(clarification, UiJson))!.AsObject();
            if (clarificationChoices is not null)
                serialized["choices"] = new JsonArray(clarificationChoices.Select(choice =>
                    (JsonNode)new JsonObject
                    {
                        ["label"] = choice.Label,
                        ["value"] = choice.Value,
                    }).ToArray());
            body["clarification"] = serialized;
        }
        var merged = UiEffect.Merge(effects);
        // A turn that used tools and produced nothing to render is a refusal — the most
        // characteristic thing this product does. It gets a view like any other answer,
        // rather than silently degrading to a wall of prose.
        if (clarification is null && merged.IsEmpty && trace.Count > 0)
            merged = new UiEffect(Gap: new GapView(
                Status: "no_result",
                Work: null, Date: null,
                Explanation: "Lex found nothing matching that in what it holds. This is a limit of the corpus, not a hedge. See coverage for exactly what is and is not held.",
                Available: []));
        if (!merged.IsEmpty)
            body["ui"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(merged, UiJson));
        return body;
    }

    internal static string ReplyFor(
        AgentAnswerDraft grounded,
        IEnumerable<UiEffect> effects,
        bool synthesisFailed = false)
    {
        var parts = effects.ToList();
        var outlines = parts.Select(part => part.Provision)
            .Where(view => view is { Provisions.Count: > 0 })
            .ToList();
        if (synthesisFailed
            && grounded.Status == AgentAnswerStatus.Refusal
            && parts.All(part => part.Gap is null))
        {
            var view = UiEffect.Merge(parts);
            if (view.Diff is not null)
                return "The requested comparison is open below.";
            if (view.History is not null)
                return "The selected article's history is open below.";
            if (view.Timeline is not null)
                return "The selected law's version timeline is open below.";
            if (view.Ranking is not null)
                return "The requested change ranking is open below.";
            if (view.InForce is not null)
                return "The publisher states covering the requested date are open below.";
            if (view.CitedBy is not null)
                return "The citing provisions are open below.";
            var textView = outlines.FirstOrDefault(item => item!.Provisions
                .Any(provision => !string.IsNullOrEmpty(provision.Text)));
            if (textView is not null)
                return textView.Subject.Anchor is { Length: > 0 }
                    ? "The exact publisher text for the selected article and date is open below."
                    : "The exact publisher text for the selected law and date is open below.";
            if (view.Workspace is not null)
                return "The matching catalogue results are open below.";
        }
        if (grounded.Status == AgentAnswerStatus.Refusal
            && outlines.Count > 0
            && outlines.SelectMany(view => view!.Provisions)
                .All(item => string.IsNullOrEmpty(item.Text))
            && parts.All(part => part.Gap is null))
            return "The selected instrument is open below. Choose a provision to inspect its exact text.";
        return AgentAnswerFinalizer.Render(grounded);
    }

    /// <summary>
    /// Names what a tool actually found, so the wait carries information rather than
    /// reassurance. Falls back to the tool's own name only when nothing was returned.
    /// </summary>
    private static Step Describe(string tool, JsonObject args, UiEffect eff, JsonArray docs)
    {
        string? T(JsonNode? n) => n?.GetValue<string>();
        var first = docs.OfType<JsonObject>().FirstOrDefault();
        var title = T(first?["title"]);
        var work = T(args["work"]) ?? T(first?["lex_id"]);
        var date = T(args["date"]) ?? T(args["as_of"]) ?? T(first?["valid_from"]);

        if (eff.Ranking is { } r)
            return new Step("found", $"{r.WorksChanged:n0} laws changed between {r.FromDate} and {r.ToDate}");
        if (eff.Provision is { } pv)
            return new Step("read", $"{title ?? work}, {pv.Provisions.Count} article(s) "
                + (pv.Subject.Work.StartsWith("eu-eurlex:", StringComparison.Ordinal)
                    ? $"in publisher version {pv.ValidFrom}" : $"as in force on {pv.ValidFrom}"),
                pv.Subject.Work, pv.ValidFrom, pv.Provisions.FirstOrDefault()?.Anchor);
        if (eff.History is { } h)
            return new Step("history", $"{h.Anchor} has had {h.DistinctTexts} distinct text(s)", h.Subject.Work, null, h.Anchor);
        if (eff.InForce is { } f)
            return new Step("found", $"{f.Total:n0} publisher states cover {f.Date}");
        if (eff.Diff is { } d)
            return new Step("diff", $"comparing {d.FromDate} with {d.ToDate}", d.Subject.Work, d.FromDate);
        if (tool == "search")
            return docs.Count == 0
                ? new Step("searched", $"no match for “{T(args["query"])}”")
                : new Step("searched", $"found {docs.Count} result(s): {title ?? work}", work, date);
        if (eff.Gap is { } g) return new Step("gap", g.Explanation);
        return new Step("step", tool.Replace('_', ' '));
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
                        ["text_sha256"] = p["text_sha256"]?.DeepClone(),
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
                        var consolidationDates = o["envelope"]?["timeline_semantics"]?.GetValue<string>()
                            == "official_consolidation_state";
                        var first = states.OfType<JsonObject>().FirstOrDefault();
                        var last = states.OfType<JsonObject>().LastOrDefault();
                        var pins = new JsonArray(states.OfType<JsonObject>().Take(6).Select(s =>
                            (JsonNode)new JsonObject
                            {
                                ["anchor"] = o["anchor"]?.DeepClone(),
                                ["quote"] = $"{(consolidationDates ? "publisher state" : "in force")} {s["valid_from"]} → {s["valid_to"] ?? (consolidationDates ? "latest held" : "open")} (text sha {(s["text_sha256"]?.GetValue<string>() ?? "")[..Math.Min(12, (s["text_sha256"]?.GetValue<string>() ?? "").Length)]})",
                                ["permalink"] = s["permalink"]?.DeepClone(),
                            }).ToArray());
                        docs.Add(new JsonObject
                        {
                            ["lex_id"] = o["work"]?.DeepClone(),
                            ["title"] = $"{o["work"]} · {o["anchor"]} · {o["distinct_texts"]} distinct text(s)",
                            ["valid_from"] = first?["valid_from"]?.DeepClone(),
                            ["valid_to"] = last?["valid_to"]?.DeepClone(),
                            ["permalink"] = first?["permalink"]?.DeepClone(),
                            ["pinpoints"] = pins,
                        });
                        break;
                    }
                    // cited_by rows are articles pointing AT a law, so the row's identity is the
                    // citing article rather than the cited work.
                    if (o["anchor"] is not null && o["work"] is not null && o["valid_from"] is not null
                        && o["num"] is not null && docs.Count < 24)
                    {
                        docs.Add(new JsonObject
                        {
                            ["lex_id"] = o["work"]?.DeepClone(),
                            ["title"] = o["title"]?.DeepClone(),
                            ["valid_from"] = o["valid_from"]?.DeepClone(),
                            ["permalink"] = o["permalink"]?.DeepClone(),
                            ["snippet"] = $"cites it at {o["num"]}",
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

    /// <summary>
    /// A step worth telling the reader about. CHI '26 (N=45, 26s and 45s waits) found
    /// content-bearing updates — ones that name a real object — beat progress-only cues on
    /// perceived speed, trust and cognitive load, with the payoff GROWING as the wait
    /// lengthens. So these carry entities, never activity labels: "Code du travail,
    /// 2019-03-01", not "searching…".
    /// </summary>
    public sealed record Step(string Kind, string Text, string? Work = null, string? Date = null, string? Anchor = null);

    public async Task<(int Status, JsonObject Body)> AskAsync(JsonArray history, string ip, string host,
        CancellationToken ct, Action<Step>? onStep = null)
    {
        if (!Enabled)
            return (503, new JsonObject { ["error"] = "The playground is not enabled on this deployment. Connect your own AI instead: /ai." });
        if (history.Count is 0 or > MaxHistory)
            return (400, new JsonObject { ["error"] = $"Send 1 to {MaxHistory} messages." });

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(host, core.ToolDefs().Count) } };
        string? rawUserQuery = null;
        var userQueries = new List<string>();
        foreach (var m in history)
        {
            var role = m?["role"]?.GetValue<string>();
            var content = m?["content"]?.GetValue<string>() ?? "";
            if (role is not ("user" or "assistant")) return (400, new JsonObject { ["error"] = "Roles must be user/assistant." });
            var messageLimit = role == "user" ? MaxUserMessageChars : MaxAssistantMessageChars;
            if (content.Length > messageLimit)
                return (400, new JsonObject
                {
                    ["error"] = role == "user"
                        ? "Questions are capped at 1,000 characters."
                        : "Stored assistant messages are capped at 4,000 characters.",
                });
            if (role == "user")
            {
                rawUserQuery = content;
                userQueries.Add(content);
            }
            messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }
        if (string.IsNullOrWhiteSpace(rawUserQuery))
            return (400, new JsonObject { ["error"] = "At least one user message is required." });
        if (WorkResolutionGuard.IsExplicitNonSelection(rawUserQuery))
            return (200, Body(
                "Please add another identifying detail, such as the topic, jurisdiction, official title, or identifier.",
                [], []));

        if (!TryCount(ip, out var why)) return (429, new JsonObject { ["error"] = why });

        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(20)))
            return (503, new JsonObject { ["error"] = "The playground is busy. Try again in a moment." });
        try
        {
            using var askSpan = Activity.StartActivity("ask");
            askSpan?.SetTag("gen_ai.request.model", _deployment);
            var trace = new JsonArray();
            var searchCalls = 0;
            var worksFound = new Dictionary<string, string>(StringComparer.Ordinal);
            var resolutionGuard = new WorkResolutionGuard();
            var evidence = new AgentEvidenceLedger();
            var textToolUsed = false;
            // D31 shape: effects are collected across every tool call in the turn and merged
            // into ONE payload, so a single reply can carry prose plus more than one view.
            var effects = new List<UiEffect>();
            var listRendered = false;

            // Requests are deliberately stateless, but the browser sends a bounded transcript.
            // Re-establish the most recent user-authored work identity through the deterministic
            // resolver. Never trust an earlier assistant answer as authority, and never carry
            // weak discovery candidates or problem-first provision hits into a new turn.
            var priorWorks = ResolvePriorUserWorks(userQueries, priorQuery =>
            {
                try
                {
                    return core.CallTool("search", new JsonObject
                    {
                        ["query"] = priorQuery,
                        ["retrieval_mode"] = "keyword",
                        ["fuzzy"] = "auto",
                        ["limit"] = 8,
                    });
                }
                catch (Exception ex)
                {
                    // Conversation context is optional. A stale earlier query must never
                    // prevent the current, independently valid question from running.
                    Console.Error.WriteLine($"[ask] prior user resolution skipped: {ex.Message}");
                    return null;
                }
            });
            resolutionGuard.AuthorizePriorWorks(priorWorks);
            if (priorWorks.Count > 0)
            {
                trace.Add(new JsonObject
                {
                    ["tool"] = "search",
                    ["phase"] = "prior_user_context",
                    ["status"] = "resolved",
                    ["works"] = new JsonArray(priorWorks.Select(work => (JsonNode)work).ToArray()),
                });
                foreach (var work in priorWorks) worksFound.TryAdd(work, "");
            }

            // Resolve the user's own words before a model can introduce a law name. This search
            // is the authority boundary for named works and also supplies direct provision
            // evidence for problem-first questions. Later model reformulations may improve recall,
            // but a name invented by the planner remains only a candidate.
            var rawArgs = new JsonObject
            {
                ["query"] = rawUserQuery,
                ["retrieval_mode"] = "keyword",
                ["fuzzy"] = "auto",
                ["limit"] = 8,
            };
            var rawResult = core.CallTool("search", rawArgs);
            resolutionGuard.ObserveCurrentUserSearch(rawResult, hasPriorContext: priorWorks.Count > 0);
            resolutionGuard.ObserveUserConfirmation(rawUserQuery);
            searchCalls = 1;
            var (rawStatus, rawDocs) = Summarize(rawResult);
            evidence.Observe("search", rawStatus, rawDocs, rawResult);
            trace.Add(new JsonObject
            {
                ["tool"] = "search",
                ["phase"] = "raw_user_resolution",
                ["args"] = rawArgs.DeepClone(),
                ["status"] = rawStatus,
                ["docs"] = rawDocs,
            });
            // This is an authority preflight, not a research action chosen for the question.
            // Keep it in the trace for auditability without presenting unrelated title matches
            // as findings to the reader.
            foreach (var d in rawDocs.OfType<JsonObject>())
                if (d["lex_id"]?.GetValue<string>() is { } lexId)
                {
                    var parts = lexId.Split(':');
                    if (parts.Length >= 2)
                        worksFound.TryAdd($"{parts[0]}:{parts[1]}", d["title"]?.GetValue<string>() ?? "");
                }
            messages.Insert(1, new JsonObject
            {
                ["role"] = "system",
                ["content"] = "Deterministic raw-user resolution ran before planning. "
                    + (priorWorks.Count > 0
                        ? "Treat resolved names in the current result as authoritative. Because conversational work context exists, current direct hits remain pointers until you either use that prior work or run one focused search for a genuinely new topic. "
                        : "Treat resolved names and direct provision hits in this result as authoritative for this turn. ")
                    + (priorWorks.Count > 0
                        ? "The most recent law identity deterministically resolved from earlier user-authored turns is also authorized as conversational context: "
                            + string.Join(", ", priorWorks) + ". "
                        : "No earlier user-authored law identity was deterministically resolved. ")
                    + "Names introduced by later reformulations are candidates only. Result:\n"
                    + TruncateResult(rawResult),
            });

            // Reasoning shares the completion budget: over a large tool result the model can
            // spend all of it thinking and return an empty message. When that happens we retry
            // the same conversation once at lower effort, which leaves room to actually write.
            //
            // It is also the entire latency budget. Measured live, one ordinary question spent
            // 3s reaching the first tool, 20s deciding on the second, and 76s writing the answer,
            // all at "high". Choosing a tool is routing and does not improve with deliberation;
            // composing a grounded answer from what came back does. So effort is set per round
            // rather than once, and a retry can still raise it when a round comes back empty.
            var retried = false;
            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var req = new JsonObject
                {
                    ["model"] = _deployment,
                    ["messages"] = messages.DeepClone(),
                    // Withdrawn once the search budget is spent, rather than left on the table
                    // and refused. The refusal already existed and still cost a full round of the
                    // model emitting the call, and it is what produced "I hit the tool budget" in
                    // an answer instead of the article the reader asked for.
                    ["tools"] = OpenAiTools(withSearch: searchCalls < 2),
                    // Once a list view is on screen the answer is a sentence, not more lookups.
                    // Rejecting the calls afterwards still costs a full round of the model
                    // emitting them; withdrawing the tools removes the temptation entirely.
                    ["tool_choice"] = round == MaxToolRounds || listRendered ? "none" : "auto",
                    ["max_completion_tokens"] = 16000,
                    // Low while the tools are still open, because the only decision on the table
                    // is which one to call. Medium once they are withdrawn and the model has to
                    // write, or after an empty round, which means it needs more room to think.
                    ["reasoning_effort"] =
                        retried || round == MaxToolRounds || listRendered ? "medium" : "low",
                };
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/openai/v1/chat/completions")
                { Content = new StringContent(req.ToJsonString(), Encoding.UTF8, "application/json") };
                if (_useManagedIdentity)
                {
                    var token = await _credential.GetTokenAsync(
                        new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct);
                    httpReq.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                }
                else
                {
                    httpReq.Headers.Add("api-key", _key);
                }
                using var resp = await _http.SendAsync(httpReq, ct);
                var respText = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[ask] upstream {(int)resp.StatusCode}: {respText[..Math.Min(500, respText.Length)]}");
                    return (502, new JsonObject { ["error"] = "The model upstream returned an error. Try again shortly." });
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
                            if (listRendered && name is "as_of" or "timeline" or "diff" or "article_history")
                            {
                                entry["status"] = "already_rendered";
                                trace.Add(entry);
                                messages.Add(new JsonObject
                                {
                                    ["role"] = "tool", ["tool_call_id"] = id,
                                    ["content"] = new JsonObject
                                    {
                                        ["already_rendered"] = true,
                                        ["note"] = "The list you just retrieved is ALREADY DISPLAYED to the user in full, with titles, counts and links. Do not fetch its entries one by one. Answer now, in one or two sentences, naming the top entries and their counts.",
                                    }.ToJsonString(),
                                });
                                continue;
                            }
                            if (!resolutionGuard.Allows(name, args))
                            {
                                entry["status"] = "work_resolution_required";
                                trace.Add(entry);
                                var attemptedWork = name == "provenance"
                                    ? args["lex_id"]?.GetValue<string>()
                                    : args["work"]?.GetValue<string>();
                                if (resolutionGuard.ClarificationFor(attemptedWork) is { } clarification)
                                    return (200, Body(clarification.Display.Question, trace, [],
                                        clarification.Display, clarification.Choices));
                                messages.Add(new JsonObject
                                {
                                    ["role"] = "tool", ["tool_call_id"] = id,
                                    ["content"] = new JsonObject
                                    {
                                        ["error"] = "work resolution is ambiguous, unresolved, or unavailable",
                                        ["required_action"] = "Ask the user to choose a returned candidate or provide an official identifier before calling a work-specific tool.",
                                    }.ToJsonString(),
                                });
                                continue;
                            }
                            using var toolSpan = Activity.StartActivity("tool");
                            toolSpan?.SetTag("gen_ai.tool.name", name);
                            var node = core.CallTool(name, args);
                            if (name == "search") resolutionGuard.ObserveSearch(node, isRawUserQuery: false);
                            var (st, docs) = Summarize(node);
                            evidence.Observe(name, st, docs, node, args);
                            if (name is "as_of" or "timeline" or "diff" or "article_history" or "in_force_on")
                                textToolUsed = true;
                            toolSpan?.SetTag("lex.status", st ?? "ok");
                            toolSpan?.SetTag("lex.docs", docs.Count);
                            entry["status"] = st;
                            entry["docs"] = docs;
                            var eff = UiMapper.From(name, args, node);
                            if (!eff.IsEmpty) effects.Add(eff);
                            if (eff.Ranking is not null || eff.InForce is not null)
                                resolutionGuard.ObserveWorkIndependentAnswer();
                            onStep?.Invoke(Describe(name, args, eff, docs));
                            // A list view is shown as-is; it needs no enrichment. Without this
                            // the model fetches every ranked row to "confirm" it — eleven calls
                            // and two minutes for a question already answered by the first.
                            if (eff.Ranking is not null || eff.InForce is not null) listRendered = true;
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
                if (resolutionGuard.ClarificationFor(null) is { } pendingClarification)
                    return (200, Body(pendingClarification.Display.Question, trace, [],
                        pendingClarification.Display, pendingClarification.Choices));
                if (reply.Length == 0 && !retried)
                {
                    // Nothing written. Same evidence, one more attempt with room to think.
                    Console.Error.WriteLine("[ask] empty reply — retrying at medium effort");
                    retried = true;
                    continue;
                }
                if (reply.Length == 0)
                    reply = trace.Count > 0
                        ? "I retrieved the evidence below but could not compose an answer. Try asking for a narrower slice (a single law, or a shorter period)."
                        : "I could not produce an answer. Try rephrasing.";
                var finalization = await Finalizer().FinalizeAsync(
                    rawUserQuery, reply, evidence.Evidence, ct);
                reply = ReplyFor(finalization.Draft, effects, finalization.SynthesisFailed);
                return (200, Body(reply, trace, effects, finalization.Draft.Clarification));
            }
            return (200, Body("Tool budget for one question exhausted. Try a narrower question.", trace, effects));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (499, new JsonObject { ["error"] = "Request cancelled." });
        }
        catch (TaskCanceledException)
        {
            return (504, new JsonObject { ["error"] = "The model took too long. Try a narrower question." });
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[ask] upstream unreachable: {ex.Message}");
            return (502, new JsonObject { ["error"] = "The model upstream is unreachable right now. Try again shortly." });
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            Console.Error.WriteLine($"[ask] agent upstream failed: {ex.Status}");
            return (502, new JsonObject { ["error"] = "The model upstream returned an error. Try again shortly." });
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

    private AgentAnswerFinalizer Finalizer() =>
        LazyInitializer.EnsureInitialized(ref _answerFinalizer, () => new AgentAnswerFinalizer(
            _endpoint!, _deployment, _credential, _useManagedIdentity ? null : _key));
}
