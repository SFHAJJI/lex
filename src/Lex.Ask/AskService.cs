using System.Collections.Concurrent;
using System.Diagnostics;
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
public sealed class AskService
{
    private readonly McpCore core;
    private readonly IOperationPlanner? _planner;
    private readonly IOperationSynthesizer? _synthesizer;
    private readonly AskAdmissionController _admission;
    private readonly TimeSpan _plannerDeadline;
    private readonly TimeSpan _firstResultDeadline;
    private readonly Func<string, JsonObject, CancellationToken, ValueTask<JsonNode>> _legalTool;

    internal static readonly TimeSpan DefaultPlannerDeadline = TimeSpan.FromSeconds(12);
    internal static readonly TimeSpan DefaultFirstResultDeadline = TimeSpan.FromSeconds(25);

    public AskService(McpCore core)
    {
        this.core = core;
        _admission = DefaultAdmission();
        _plannerDeadline = DefaultPlannerDeadline;
        _firstResultDeadline = DefaultFirstResultDeadline;
        _legalTool = core.CallToolAsync;
    }

    internal AskService(
        McpCore core,
        IOperationPlanner planner,
        IOperationSynthesizer? synthesizer = null,
        AskAdmissionController? admission = null,
        TimeSpan? plannerDeadline = null,
        TimeSpan? firstResultDeadline = null,
        Func<string, JsonObject, CancellationToken, ValueTask<JsonNode>>? legalTool = null)
    {
        this.core = core;
        _planner = planner;
        _synthesizer = synthesizer;
        _admission = admission ?? DefaultAdmission();
        _plannerDeadline = plannerDeadline ?? DefaultPlannerDeadline;
        _firstResultDeadline = firstResultDeadline ?? DefaultFirstResultDeadline;
        _legalTool = legalTool ?? core.CallToolAsync;
        if (_plannerDeadline <= TimeSpan.Zero || _firstResultDeadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(plannerDeadline), "Assistant deadlines must be positive.");
    }

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
        private readonly HashSet<string> _current = new(StringComparer.Ordinal);
        private readonly List<(string Work, string Title)> _candidates = [];
        private bool _searchObserved;
        private bool _workIndependentAnswerObserved;
        private bool _currentAuthorityObserved;
        private bool _priorContextUsed;

        public IReadOnlyCollection<string> ResolvedWorks => _resolved;
        public IReadOnlyCollection<string> CurrentResolvedWorks => _current;

        public void ObserveSearch(JsonNode result, bool isRawUserQuery = true)
            => ObserveSearch(result, isRawUserQuery, allowDirectProvisionAuthority: true,
                collectCandidates: true, markCurrent: true);

        public void ObserveCurrentUserSearch(JsonNode result, bool hasPriorContext)
            => ObserveSearch(result, isRawUserQuery: true,
                allowDirectProvisionAuthority: !hasPriorContext,
                collectCandidates: !hasPriorContext, markCurrent: true);

        public void ObservePriorUserSearch(JsonNode result)
            => ObserveSearch(result, isRawUserQuery: true, allowDirectProvisionAuthority: false,
                collectCandidates: false, markCurrent: false);

        public void ObserveFocusedSearch(JsonNode result, bool hasPriorContext)
            => ObserveSearch(result, isRawUserQuery: false,
                allowDirectProvisionAuthority: !hasPriorContext,
                collectCandidates: true, markCurrent: false);

        private void ObserveSearch(JsonNode result, bool isRawUserQuery,
            bool allowDirectProvisionAuthority, bool collectCandidates, bool markCurrent)
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
                                if (markCurrent) _current.Add(WorkKey(work));
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
                            if (markCurrent) _current.Add(WorkKey(lexId));
                            _currentAuthorityObserved = true;
                        }
                if (collectCandidates && response["hits"] is JsonArray candidateHits)
                    foreach (var hit in candidateHits.OfType<JsonObject>()
                                 // A bare article intent may return real article rows from many
                                 // works. They are useful clarification candidates, but without
                                 // direct lexical/semantic evidence they cannot authorize a work.
                                 .Where(item => !HasDirectProvisionEvidence(item)))
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
                _current.Add(value);
                _currentAuthorityObserved = true;
            }
        }

        public static bool IsExplicitNonSelection(string query) =>
            string.Equals(query.Trim(), NoChoice, StringComparison.Ordinal)
            || string.Equals(query.Trim(), ChoicePrefix + NoChoice, StringComparison.Ordinal);

        public void ObserveWorkIndependentAnswer() => _workIndependentAnswerObserved = true;

        public GuardClarification? ClarificationFor(string? attemptedWork, string locale = "en")
        {
            if (_workIndependentAnswerObserved || _currentAuthorityObserved || _priorContextUsed
                || _candidates.Count == 0)
                return null;
            var attempted = attemptedWork is null ? null : WorkKey(attemptedWork);
            var ordered = _candidates
                .OrderByDescending(candidate => candidate.Work == attempted)
                .Take(3)
                .ToList();
            var choices = ordered.Select((candidate, index) => new GuardChoice(
                CandidateOption(candidate.Title, candidate.Work, index + 1),
                candidate.Work)).ToList();
            choices.Add(new GuardChoice(
                locale == "fr"
                    ? "Aucun de ceux-ci; je vais préciser ma demande"
                    : "None of these; I will add more details",
                NoChoice));
            var clarification = new AgentClarification(
                locale == "fr"
                    ? "Lex a trouvé plusieurs instruments possibles sans preuve directe dans une disposition. Lequel doit-il utiliser ?"
                    : "Lex found possible instruments but no direct provision evidence. Which instrument should it use?",
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

        internal static string WorkKey(string value)
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
    private AgentAnswerFinalizer? _answerFinalizer;

    public bool Enabled => _planner is not null || (!string.IsNullOrEmpty(_endpoint)
                           && (_useManagedIdentity || !string.IsNullOrEmpty(_key)));

    private static int EnvInt(string name, int dflt)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : dflt;

    private static AskAdmissionController DefaultAdmission() => new(
        TimeProvider.System,
        EnvInt("ASK_PER_IP_DAILY", 25),
        EnvInt("ASK_GLOBAL_DAILY", 400),
        EnvInt("ASK_CONCURRENT", 4));

    private static void Diagnostic(string code)
    {
        var safe = code.Length <= 80
                   && code.All(character => char.IsAsciiLetterOrDigit(character)
                       || character == '_')
            ? code
            : "invalid_diagnostic_code";
        Console.Error.WriteLine($"[ask] {safe}");
    }

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

    internal static bool HasUnscopedArticleIntent(JsonNode result) =>
        (result is JsonArray responses
            ? responses.OfType<JsonObject>()
            : result is JsonObject response ? [response] : [])
        .Any(item => item["query_plan"] is JsonObject plan
            && plan["article_number"]?.GetValue<string>() is { Length: > 0 }
            && plan["has_strong_work_match"]?.GetValue<bool>() != true);

    internal static void ApplyWorkspaceDefaults(string tool, JsonObject args)
    {
        // The public MCP keeps an omitted class filter meaningful. The application assistant,
        // however, controls the same "What changed" workspace as a human reader, whose default
        // population is legal instruments rather than thematic shelves. Bind that default before
        // execution so the reported aggregate, typed effect and reloaded workspace are identical.
        if (tool == "changes_in_period"
            && args["source_class"] is null
            && args["document_type"] is null)
            args["source_class"] = "!RECUEIL,!CODE_RECUEIL";
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
        4. Honest refusals: when a tool answers no_version_for_date, unknown_work,
           text_withheld or text_not_available, or coverage shows a gap, say plainly what Lex does not
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

    private static string AdmissionReason(AskAdmissionFailure failure) => failure switch
    {
        AskAdmissionFailure.PerClientQuota =>
            "Daily question limit reached for your address. Come back tomorrow, or use the separately bounded MCP endpoint.",
        AskAdmissionFailure.GlobalQuota =>
            "The shared daily budget for this public playground is used up. Come back tomorrow, or use the separately bounded MCP endpoint.",
        AskAdmissionFailure.Busy =>
            "The shared playground is busy. Try again shortly.",
        _ => "The request could not be admitted.",
    };

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

    private static string PlannerPrompt(string host) => $"""
        You plan operations for Lex, a read-only point-in-time legal retrieval product at https://{host}.
        Return one complete ordered operation for every legal operation the user requested. The
        application freezes and validates the entire list before any legal tool runs. Do not answer
        the question and do not call legal tools yourself.

        Choose only these operations:
        - search: find laws or provisions by topic and open the matching search workspace.
        - as_of: exact publisher text for one law on one date.
        - diff: compare one law, optionally one article, between two dates.
        - timeline: list the versions of one law.
        - article_history: history of one held article.
        - changes_in_period: corpus-wide laws changed during a window. Use order=by_churn for
          "changed most". This operation is work-independent and must never be replaced by search.
        - in_force_on: publisher states covering a date.
        - coverage: what Lex holds and lacks.
        - cited_by: provisions that refer to one law.
        - provenance: proof chain for one law or version.
        - legal_boundary: the user asks for legal advice, a compliance conclusion, application
          to their facts, a recommendation, or help evading a rule. Use reason only.
        - clarification: the request has no identifiable law, date, topic, or operation. Supply
          one question and two to four concrete options. Do not use it after a law resolves.

        For a named law, put the user's exact name, acronym or identifier in work_query. Never invent
        a canonical work id. The application resolves it deterministically. Put a mentioned article
        number in article_number. Dates are ISO YYYY-MM-DD. Expand a bare year to its full inclusive
        calendar boundary. For as_of with no date use {DateTime.UtcNow:yyyy-MM-dd}. Preserve the
        user's operation order. Set synthesis=true only when the user explicitly asks you to
        summarize or describe the accepted results; ordinary lookup and comparison use deterministic
        application replies. A compound request must remain multiple operations.
        "Which Luxembourg and EU laws changed most in 2024" is one changes_in_period operation with
        from_date=2024-01-01, to_date=2024-12-31 and order=by_churn. "Compare Article 92 of CRR
        between 2020 and 2024" is one diff with work_query=CRR, article_number=92,
        from_date=2020-01-01 and to_date=2024-12-31.
        """;

    private static JsonArray PlannerTools() =>
    [
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "submit_operation_plan",
                ["description"] = "Submit the complete ordered legal operation plan.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["operations"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["minItems"] = 1,
                            ["maxItems"] = OperationPlan.MaximumOperations,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["tool"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["enum"] = new JsonArray(
                                            "search", "as_of", "diff", "timeline",
                                            "article_history", "changes_in_period", "in_force_on",
                                            "coverage", "cited_by", "provenance",
                                            "legal_boundary", "clarification"),
                                    },
                                    ["arguments"] = PlannerArgumentSchema(),
                                },
                                ["required"] = new JsonArray("tool", "arguments"),
                            },
                        },
                        ["synthesis"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "True only when the user explicitly asks for a descriptive synthesis of the legal operation results.",
                        },
                    },
                    ["required"] = new JsonArray("operations"),
                },
            },
        },
    ];

    private static JsonObject PlannerArgumentSchema()
    {
        JsonObject S() => new() { ["type"] = "string" };
        JsonObject I() => new() { ["type"] = "integer" };
        var properties = new JsonObject();
        foreach (var name in new[]
                 {
                     "query", "publisher", "jurisdiction", "document_type", "source_class",
                     "hierarchy", "act_form", "binding_status", "domain", "language",
                     "retrieval_mode", "time_scope", "as_of", "fuzzy", "works", "work",
                     "work_query", "article_number", "date", "mode", "anchors", "from_date",
                     "to_date", "anchor", "lex_id", "order", "reason", "question",
                 })
            properties[name] = S();
        properties["limit"] = I();
        properties["offset"] = I();
        properties["options"] = new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 2,
            ["maxItems"] = 4,
            ["items"] = S(),
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
    }

    private async Task<(OperationPlan Plan, ModelTokenUsage Usage)> PlanOperationsAsync(
        JsonArray history,
        string host,
        string requestId,
        string locale,
        CancellationToken ct)
    {
        if (_planner is not null)
        {
            var proposed = await _planner.PlanAsync(history, host, requestId, ct);
            return (OperationPlan.Create(
                requestId, locale, proposed.Operations, proposed.SynthesisRequested), default);
        }

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = PlannerPrompt(host) },
        };
        foreach (var message in history)
            messages.Add(message?.DeepClone());
        var req = new JsonObject
        {
            ["model"] = _deployment,
            ["messages"] = messages,
            ["tools"] = PlannerTools(),
            ["tool_choice"] = new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "submit_operation_plan" },
            },
            ["max_completion_tokens"] = 4000,
            ["reasoning_effort"] = "medium",
        };
        using var httpReq = new HttpRequestMessage(
            HttpMethod.Post, $"{_endpoint}/openai/v1/chat/completions")
        {
            Content = new StringContent(req.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (_useManagedIdentity)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct);
            httpReq.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }
        else
            httpReq.Headers.Add("api-key", _key);

        using var response = await _http.SendAsync(httpReq, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Planning upstream returned HTTP {(int)response.StatusCode}.");
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(responseText);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException("The planner returned malformed response JSON.", ex);
        }
        var calls = parsed?["choices"]?[0]?["message"]?["tool_calls"] as JsonArray;
        var call = calls?.OfType<JsonObject>().SingleOrDefault(item =>
            item["function"]?["name"] is JsonValue name
            && name.TryGetValue<string>(out var value)
            && value == "submit_operation_plan")
            ?? throw new InvalidDataException("The planner did not submit exactly one operation plan.");
        var raw = call["function"]?["arguments"] is JsonValue argumentValue
                  && argumentValue.TryGetValue<string>(out var rawArguments)
            ? rawArguments
            : throw new InvalidDataException(
                "The planner returned no string operation-plan arguments.");
        JsonObject plan;
        try
        {
            plan = JsonNode.Parse(raw) as JsonObject
                ?? throw new InvalidDataException(
                    "The planner returned malformed operation-plan arguments.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException(
                "The planner returned malformed operation-plan JSON.", ex);
        }
        var operations = plan["operations"] as JsonArray
            ?? throw new InvalidDataException("The planner returned no operation list.");
        var synthesis = false;
        if (plan["synthesis"] is JsonValue synthesisValue
            && !synthesisValue.TryGetValue<bool>(out synthesis))
            throw new InvalidDataException("The synthesis flag must be boolean.");
        var usage = new ModelTokenUsage(
            parsed?["usage"]?["prompt_tokens"]?.GetValue<long>() ?? 0,
            parsed?["usage"]?["completion_tokens"]?.GetValue<long>() ?? 0);
        return (OperationPlan.FromPlannerOutput(requestId, locale, operations, synthesis), usage);
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
                Status: McpStatus.NoResult,
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
        var view = UiEffect.Merge(parts);
        // A standalone catalogue ranking is already rendered with a source on every row. Keep
        // the answer and any coverage disclosure in the user's language, but do not duplicate
        // the workspace as a second list of raw URLs in the chat.
        if (view is
            {
                Ranking: not null,
                Provision: null, Diff: null, History: null, Timeline: null,
                InForce: null, CitedBy: null, Gap: null,
            })
            return AgentAnswerFinalizer.Render(grounded with { Permalinks = [] });
        if (synthesisFailed
            && grounded.Status == AgentAnswerStatus.Refusal
            && parts.All(part => part.Gap is null))
        {
            if (view.Diff is { Status: McpStatus.ProfilesDiffer })
                return "Lex cannot produce a reliable comparison for those dates because the two versions use incompatible extraction profiles. The reason and both verified publisher versions are open below.";
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

    public sealed record AskProgressCallbacks(
        Func<Step, CancellationToken, ValueTask>? Step = null,
        Func<JsonObject, CancellationToken, ValueTask>? OperationResult = null,
        Func<string, CancellationToken, ValueTask>? Synthesis = null);

    public sealed record AskOutcome(int Status, JsonObject Body, bool RetainForReplay)
    {
        public void Deconstruct(out int status, out JsonObject body)
            => (status, body) = (Status, Body);
    }

    private static async ValueTask NotifyProgress(Func<ValueTask> callback)
    {
        try { await callback(); }
        catch (OperationCanceledException) { Diagnostic("progress_transport_disconnected"); }
        catch (IOException) { Diagnostic("progress_transport_disconnected"); }
        catch (ObjectDisposedException) { Diagnostic("progress_transport_disconnected"); }
    }

    public async Task<AskOutcome> AskAsync(
        JsonArray history,
        string ip,
        string host,
        CancellationToken ct,
        AskProgressCallbacks? progress = null,
        string? requestId = null)
    {
        if (!Enabled)
            return new AskOutcome(503, new JsonObject
            {
                ["error"] = "The playground is not enabled on this deployment. Connect your own AI instead: /ai.",
            }, false);
        if (history.Count is 0 or > MaxHistory)
            return new AskOutcome(400,
                new JsonObject { ["error"] = $"Send 1 to {MaxHistory} messages." }, false);

        var userQueries = new List<string>();
        string? lastRole = null;
        foreach (var message in history)
        {
            if (message is not JsonObject item
                || item["role"] is not JsonValue roleValue
                || !roleValue.TryGetValue<string>(out var role)
                || item["content"] is not JsonValue contentValue
                || !contentValue.TryGetValue<string>(out var content))
                return new AskOutcome(400,
                    new JsonObject { ["error"] = "Every message requires string role and content fields." },
                    false);
            lastRole = role;
            if (role is not ("user" or "assistant"))
                return new AskOutcome(400,
                    new JsonObject { ["error"] = "Roles must be user/assistant." }, false);
            var limit = role == "user" ? MaxUserMessageChars : MaxAssistantMessageChars;
            if (content.Length is 0 || content.Length > limit)
                return new AskOutcome(400, new JsonObject
                {
                    ["error"] = role == "user"
                        ? "Questions are capped at 1,000 characters."
                        : "Assistant history messages are capped at 4,000 characters.",
                }, false);
            if (role == "user") userQueries.Add(content);
        }
        if (userQueries.Count == 0)
            return new AskOutcome(400,
                new JsonObject { ["error"] = "The last message must be from the user." }, false);
        var rawUserQuery = userQueries[^1];
        if (lastRole != "user")
            return new AskOutcome(400,
                new JsonObject { ["error"] = "The last message must be from the user." }, false);
        if (WorkResolutionGuard.IsExplicitNonSelection(rawUserQuery))
            return new AskOutcome(200, Body(
                "No instrument was selected. Add an official title or identifier when you want to try again.",
                [], []), false);
        var admission = _admission.TryAdmit(ip);
        if (!admission.Accepted)
            return new AskOutcome(429,
                new JsonObject { ["error"] = AdmissionReason(admission.Failure) }, false);
        using var admissionLease = admission.Lease!;

        OperationRun? run = null;
        requestId ??= Guid.NewGuid().ToString("N");
        var requestLocale = RequestLocale(rawUserQuery);
        try
        {
            using var firstResult = CancellationTokenSource.CreateLinkedTokenSource(ct);
            firstResult.CancelAfter(_firstResultDeadline);
            using var planner = CancellationTokenSource.CreateLinkedTokenSource(firstResult.Token);
            planner.CancelAfter(_plannerDeadline);
            var planningWatch = Stopwatch.StartNew();
            var (plan, planningUsage) = await PlanOperationsAsync(
                history, host, requestId, requestLocale, planner.Token);
            planningWatch.Stop();
            run = OperationRun.Start(plan);
            var (status, body) = await ExecutePlanAsync(
                plan, run, userQueries, rawUserQuery, planningUsage,
                planningWatch.Elapsed.TotalMilliseconds, progress, firstResult.Token,
                () => firstResult.CancelAfter(Timeout.InfiniteTimeSpan),
                () => ct.IsCancellationRequested
                    ? TransportOutcome.Cancelled : TransportOutcome.TimedOut);
            return new AskOutcome(status, body, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run?.CompletePending(TransportOutcome.Cancelled);
            return new AskOutcome(499,
                new JsonObject { ["error"] = "Request cancelled." }, true);
        }
        catch (TaskCanceledException)
        {
            run?.CompletePending(TransportOutcome.TimedOut);
            return new AskOutcome(504,
                new JsonObject { ["error"] = "The operation timed out. Try a narrower question." }, true);
        }
        catch (OperationCanceledException)
        {
            run?.CompletePending(TransportOutcome.TimedOut);
            return new AskOutcome(504,
                new JsonObject { ["error"] = "The operation timed out. Try a narrower question." }, true);
        }
        catch (HttpRequestException)
        {
            run?.CompletePending(TransportOutcome.UpstreamFailed);
            Diagnostic("planning_upstream_unreachable");
            return new AskOutcome(502,
                new JsonObject { ["error"] = "The planning service is unavailable right now." }, true);
        }
        catch (InvalidDataException)
        {
            run?.CompletePendingLegal(LegalOutcome.InvalidRequest);
            Diagnostic("invalid_operation_plan");
            var explanation = requestLocale == "fr"
                ? "Cette demande ne correspond pas à une opération juridique valide."
                : "This request does not map to a valid legal operation.";
            var effect = new UiEffect(Gap: new GapView(
                "invalid_request", null, null, explanation, []));
            var body = Body(explanation,
                new JsonArray(new JsonObject
                {
                    ["phase"] = "operation_plan",
                    ["request_id"] = requestId,
                    ["status"] = "invalid_request",
                }),
                [effect]);
            body["operations"] = new JsonArray(new JsonObject
            {
                ["operation_id"] = $"{requestId}:invalid",
                ["order"] = 0,
                ["result_class"] = null,
                ["disposition"] = "gap",
                ["legal_outcome"] = "invalid_request",
                ["transport_outcome"] = "completed",
                ["effects"] = new JsonArray("gap"),
                ["ui"] = JsonNode.Parse(
                    System.Text.Json.JsonSerializer.Serialize(effect, UiJson)),
            });
            return new AskOutcome(200, body, true);
        }
        catch (Exception)
        {
            run?.CompletePending(TransportOutcome.UpstreamFailed);
            Diagnostic("unexpected_failure");
            return new AskOutcome(500,
                new JsonObject { ["error"] = "Unexpected error in the playground." }, true);
        }
    }

    internal static string RequestLocale(string query)
    {
        var value = $" {query.ToLowerInvariant()} ";
        var distinctive = new[]
        {
            " quel ", " quelle ", " quels ", " quelles ", " loi ", " lois ",
            " vigueur ", " modifié ", " modifie ", " changements ",
            " affiche ", " affichez ", " montrer ", " montrez ", " couverture ",
            " dois ", " dois-je ", " puis-je ", " respecter ", " comparez ",
            " trouvez ", " recherche ", " chronologie ", " instrument ",
        };
        var common = new[]
        {
            " le ", " la ", " les ", " un ", " une ", " des ", " du ", " de ",
            " entre ", " article ", " articles ",
        };
        return distinctive.Any(marker => value.Contains(marker, StringComparison.Ordinal))
               || common.Count(marker => value.Contains(marker, StringComparison.Ordinal)) >= 2
               || query.Any(character => "àâçéèêëîïôùûüÿœ".Contains(char.ToLowerInvariant(character)))
            ? "fr"
            : "en";
    }

    private async Task<(int Status, JsonObject Body)> ExecutePlanAsync(
        OperationPlan plan,
        OperationRun run,
        IReadOnlyList<string> userQueries,
        string rawUserQuery,
        ModelTokenUsage modelUsage,
        double planningMilliseconds,
        AskProgressCallbacks? progress,
        CancellationToken ct,
        Action firstResultObserved,
        Func<TransportOutcome> cancellationOutcome)
    {
        var trace = new JsonArray
        {
            new JsonObject
            {
                ["phase"] = "operation_plan",
                ["request_id"] = plan.RequestId,
                ["locale"] = plan.Locale,
                ["duration_ms"] = planningMilliseconds,
                ["operations"] = new JsonArray(plan.Operations.Select(operation =>
                    (JsonNode)new JsonObject
                    {
                        ["operation_id"] = operation.OperationId,
                        ["order"] = operation.UserOrder,
                        ["tool"] = operation.Tool,
                        ["result_class"] = operation.ResultClass is { } resultClass
                            ? ContractName(resultClass) : null,
                        ["disposition"] = operation.Disposition is { } disposition
                            ? ContractName(disposition) : null,
                        ["arguments"] = JsonNode.Parse(operation.Arguments.GetRawText()),
                    }).ToArray()),
            },
        };
        var effects = Enumerable.Range(0, plan.Operations.Length)
            .Select(_ => new UiEffect()).ToList();
        var executedArguments = Enumerable.Range(0, plan.Operations.Length)
            .Select(_ => (JsonObject?)null).ToList();
        WorkResolutionGuard.GuardClarification? clarification = null;
        AgentClarification? applicationClarification = null;
        int? terminalTransportStatus = null;
        var mcpMilliseconds = 0d;

        async ValueTask Report(OperationExecution execution)
        {
            firstResultObserved();
            if (progress?.OperationResult is null) return;
            var result = execution.Result ?? throw new InvalidDataException(
                $"Operation '{execution.Request.OperationId}' did not reach a terminal result.");
            await NotifyProgress(() => progress.OperationResult(
                OperationReply(execution.Request, result, effects[result.UserOrder]), ct));
        }

        foreach (var execution in run.Executions.OrderBy(item => item.Request.UserOrder))
        {
            if (ct.IsCancellationRequested)
            {
                var outcome = cancellationOutcome();
                foreach (var cancelled in run.CompletePending(outcome))
                {
                    effects[cancelled.UserOrder] = TransportGap(plan.Locale, cancelled.TransportOutcome);
                    await Report(run.Executions[cancelled.UserOrder]);
                }
                terminalTransportStatus = outcome == TransportOutcome.Cancelled ? 499 : 504;
                break;
            }
            var operation = execution.Request;
            var arguments = JsonNode.Parse(operation.Arguments.GetRawText())?.AsObject()
                ?? throw new InvalidDataException("Planned arguments are not an object.");
            try
            {
                if (operation.Disposition is { } disposition)
                {
                    executedArguments[operation.UserOrder] = arguments.DeepClone().AsObject();
                    if (disposition == ApplicationDisposition.LegalBoundary)
                    {
                        execution.CompleteLegal(LegalOutcome.LegalBoundary);
                        effects[operation.UserOrder] = new UiEffect(Gap: new GapView(
                            "legal_boundary", null, null,
                            plan.Locale == "fr"
                                ? "Lex peut restituer des textes vérifiés, mais ne peut pas donner d'avis juridique, conclure à la conformité ni appliquer le droit à vos faits."
                                : "Lex can retrieve verified legal text, but it cannot give legal advice, decide compliance, or apply law to your facts.",
                            []));
                    }
                    else if (disposition == ApplicationDisposition.Clarification)
                    {
                        var question = String(arguments, "question")
                            ?? throw new InvalidDataException("A clarification requires a question.");
                        var options = arguments["options"]?.AsArray()
                            .Select(item => item?.GetValue<string>() ?? "").ToArray()
                            ?? throw new InvalidDataException("A clarification requires options.");
                        applicationClarification = AgentAnswerContract.Validate(
                            new AgentAnswerDraft(
                                AgentAnswerStatus.Clarify, question, [], [], null,
                                new AgentClarification(question, options)), []).Clarification;
                        execution.CompleteLegal(LegalOutcome.NeedsClarification);
                        effects[operation.UserOrder] = new UiEffect(Gap: new GapView(
                            "needs_clarification", null, null, question, options));
                    }
                    else
                    {
                        execution.CompleteLegal(LegalOutcome.InvalidRequest);
                        effects[operation.UserOrder] = new UiEffect(Gap: new GapView(
                            "invalid_request", null, null,
                            plan.Locale == "fr"
                                ? "Cette demande ne correspond pas à une opération juridique valide."
                                : "This request does not map to a valid legal operation.",
                            []));
                    }
                    await Report(execution);
                    continue;
                }
                if (operation.RequiresWorkResolution)
                {
                    var prepared = await ResolveWorkOperationAsync(
                        run, operation, arguments, userQueries, plan.Locale, trace,
                        elapsed => mcpMilliseconds += elapsed, ct);
                    if (prepared.Arguments is null)
                    {
                        execution.CompleteLegal(LegalOutcome.NeedsClarification);
                        clarification ??= prepared.Clarification;
                        effects[operation.UserOrder] = new UiEffect(Gap: new GapView(
                            "needs_clarification", null, null,
                            plan.Locale == "fr"
                                ? "Lex a besoin d'un instrument précis avant de poursuivre cette opération."
                                : "Lex needs a specific instrument before it can continue this operation.",
                            prepared.Clarification?.Choices.Select(choice => choice.Value).ToArray()
                                ?? []));
                        await Report(execution);
                        continue;
                    }
                    arguments = prepared.Arguments;
                }

                JsonNode result;
                string status;
                executedArguments[operation.UserOrder] = arguments.DeepClone().AsObject();
                if (operation.Tool == "navigate")
                {
                    result = new JsonObject { ["status"] = McpStatus.Ok };
                    status = McpStatus.Ok;
                }
                else
                {
                    using var span = Activity.StartActivity("legal-operation");
                    span?.SetTag("lex.operation.id", operation.OperationId);
                    span?.SetTag("gen_ai.tool.name", operation.Tool);
                    var mcpWatch = Stopwatch.StartNew();
                    result = await _legalTool(operation.Tool, arguments, ct);
                    mcpWatch.Stop();
                    mcpMilliseconds += mcpWatch.Elapsed.TotalMilliseconds;
                    status = LegalOperationPolicy.StatusForResult(result);
                    span?.SetTag("lex.status", status);
                }

                var effect = UiMapper.From(operation, arguments, result, plan.Locale);
                effects[operation.UserOrder] = effect;
                if (operation.Tool == "navigate")
                    execution.CompleteLegal(LegalOutcome.Succeeded, result.AsObject());
                else
                    execution.Complete(status, result);
                var (summaryStatus, docs) = Summarize(result);
                trace.Add(new JsonObject
                {
                    ["phase"] = "primary",
                    ["operation_id"] = operation.OperationId,
                    ["tool"] = operation.Tool,
                    ["args"] = arguments.DeepClone(),
                    ["status"] = summaryStatus ?? LegalOperationPolicy.StatusForResult(result),
                    ["docs"] = docs,
                });
                await Report(execution);
                if (progress?.Step is not null)
                    await NotifyProgress(() => progress.Step(
                        Describe(operation.Tool, arguments, effect, docs), ct));
            }
            catch (InvalidDataException)
            {
                if (execution.State != OperationExecutionState.Pending) throw;
                execution.CompleteLegal(LegalOutcome.InvalidRequest);
                effects[operation.UserOrder] = new UiEffect(Gap: new GapView(
                    "invalid_request", null, null,
                    plan.Locale == "fr"
                        ? "Les paramètres de cette opération juridique ne sont pas valides."
                        : "The parameters for this legal operation are invalid.",
                    []));
                trace.Add(new JsonObject
                {
                    ["phase"] = "primary",
                    ["operation_id"] = operation.OperationId,
                    ["tool"] = operation.Tool,
                    ["status"] = "invalid_request",
                    ["detail"] = "operation_arguments_invalid",
                });
                await Report(execution);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var outcome = cancellationOutcome();
                foreach (var cancelled in run.CompletePending(outcome))
                {
                    effects[cancelled.UserOrder] = TransportGap(plan.Locale, cancelled.TransportOutcome);
                    await Report(run.Executions[cancelled.UserOrder]);
                }
                terminalTransportStatus = outcome == TransportOutcome.Cancelled ? 499 : 504;
                break;
            }
            catch (Exception)
            {
                if (execution.State == OperationExecutionState.Pending)
                    execution.CompleteTransport(TransportOutcome.UpstreamFailed);
                effects[operation.UserOrder] = TransportGap(
                    plan.Locale, TransportOutcome.UpstreamFailed);
                trace.Add(new JsonObject
                {
                    ["phase"] = "primary",
                    ["operation_id"] = operation.OperationId,
                    ["tool"] = operation.Tool,
                    ["status"] = "upstream_failed",
                });
                await Report(execution);
                terminalTransportStatus ??= 502;
            }
        }

        if (clarification is not null)
            run.CompletePendingLegal(LegalOutcome.NeedsClarification);
        var results = run.Executions.Select(item => item.Result
            ?? throw new InvalidDataException(
                $"Operation '{item.Request.OperationId}' did not reach a terminal result."))
            .OrderBy(item => item.UserOrder).ToArray();
        var displayedClarification = clarification?.Display ?? applicationClarification;
        var deterministicReply = displayedClarification?.Question
            ?? OperationAnswerPolicy.Render(plan.Locale, results, effects);
        var reply = deterministicReply;
        double? synthesisMilliseconds = null;
        if (plan.SynthesisRequested && displayedClarification is null
            && terminalTransportStatus is null)
        {
            var synthesisWatch = Stopwatch.StartNew();
            if (progress?.Synthesis is not null)
                await NotifyProgress(() => progress.Synthesis("started", ct));
            var evidence = new AgentEvidenceLedger();
            foreach (var result in results.Where(item => item.Payload is not null))
            {
                var operation = plan.Operations[result.UserOrder];
                if (operation.Disposition is not null) continue;
                var payload = JsonNode.Parse(result.Payload!.Value.GetRawText());
                if (payload is null) continue;
                var (status, docs) = Summarize(payload);
                evidence.Observe(operation.Tool,
                    status ?? LegalOperationPolicy.StatusForResult(payload),
                    docs, payload, executedArguments[result.UserOrder]);
            }
            try
            {
                AgentFinalization finalized;
                if (_synthesizer is not null)
                    finalized = await _synthesizer.SynthesizeAsync(
                        rawUserQuery, deterministicReply, evidence.Evidence, ct);
                else if (!string.IsNullOrWhiteSpace(_endpoint))
                    finalized = await Finalizer().FinalizeAsync(
                        rawUserQuery, deterministicReply, evidence.Evidence, ct);
                else
                    throw new InvalidOperationException(
                        "No synthesis service is configured.");
                reply = ReplyFor(finalized.Draft, effects, finalized.SynthesisFailed);
                modelUsage = modelUsage.Add(finalized.Usage);
                if (progress?.Synthesis is not null)
                    await NotifyProgress(() => progress.Synthesis("completed", ct));
            }
            catch (Exception ex) when (ex is OperationCanceledException
                                       or HttpRequestException
                                       or InvalidDataException
                                       or InvalidOperationException)
            {
                Diagnostic("optional_synthesis_unavailable");
                reply = deterministicReply + (plan.Locale == "fr"
                    ? " La synthèse descriptive facultative n'est pas disponible; les résultats vérifiés restent affichés."
                    : " The optional descriptive synthesis is unavailable; the verified results remain open.");
                if (progress?.Synthesis is not null)
                    await NotifyProgress(() => progress.Synthesis(
                        "unavailable", CancellationToken.None));
            }
            synthesisWatch.Stop();
            synthesisMilliseconds = synthesisWatch.Elapsed.TotalMilliseconds;
        }
        var body = Body(reply, trace, effects,
            displayedClarification, clarification?.Choices);
        body["model_usage"] = new JsonObject
        {
            ["input_tokens"] = modelUsage.InputTokens,
            ["output_tokens"] = modelUsage.OutputTokens,
            ["total_tokens"] = modelUsage.TotalTokens,
        };
        body["model_identity"] = new JsonObject
        {
            ["resource_host"] = Uri.TryCreate(_endpoint, UriKind.Absolute, out var modelEndpoint)
                ? modelEndpoint.IdnHost : "unconfigured",
            ["deployment"] = _deployment,
        };
        body["timing"] = new JsonObject
        {
            ["planner_ms"] = planningMilliseconds,
            ["mcp_ms"] = mcpMilliseconds,
            ["synthesis_ms"] = synthesisMilliseconds,
        };
        body["operations"] = new JsonArray(results.Select(result =>
        {
            var effect = effects[result.UserOrder];
            return (JsonNode)OperationReply(
                plan.Operations[result.UserOrder], result, effect);
        }).ToArray());
        if (terminalTransportStatus is { } failedStatus)
            body["error"] = failedStatus switch
            {
                499 => "The assistant request was cancelled.",
                504 => "The operation timed out. Try a narrower question.",
                _ => "A legal operation failed upstream.",
            };
        return (terminalTransportStatus ?? 200, body);
    }

    private static JsonObject OperationReply(
        RequestedOperation request,
        OperationResult result,
        UiEffect effect) => new()
        {
            ["operation_id"] = result.OperationId,
            ["order"] = result.UserOrder,
            ["tool"] = request.Tool,
            ["result_class"] = result.ResultClass is { } resultClass
            ? ContractName(resultClass) : null,
            ["disposition"] = result.Disposition is { } disposition
            ? ContractName(disposition) : null,
            ["legal_outcome"] = ContractName(result.LegalOutcome),
            ["transport_outcome"] = ContractName(result.TransportOutcome),
            ["effects"] = new JsonArray(result.Effects.Select(item =>
                (JsonNode)ContractName(item)).ToArray()),
            ["ui"] = effect.IsEmpty ? null : JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(effect, UiJson)),
        };

    private static UiEffect TransportGap(string locale, TransportOutcome outcome) =>
        new(Gap: new GapView(
            ContractName(outcome), null, null,
            (locale, outcome) switch
            {
                ("fr", TransportOutcome.Cancelled) => "Cette opération a été annulée avant son évaluation juridique.",
                ("fr", TransportOutcome.TimedOut) => "Cette opération a dépassé le délai avant son évaluation juridique.",
                ("fr", TransportOutcome.UpstreamFailed) => "Le service nécessaire à cette opération est indisponible.",
                ("fr", TransportOutcome.OverQuota) => "Le quota ne permet pas d'exécuter cette opération.",
                (_, TransportOutcome.Cancelled) => "This operation was cancelled before legal evaluation.",
                (_, TransportOutcome.TimedOut) => "This operation timed out before legal evaluation.",
                (_, TransportOutcome.UpstreamFailed) => "The service required for this operation is unavailable.",
                (_, TransportOutcome.OverQuota) => "The quota does not allow this operation to run.",
                _ => "This operation was not evaluated.",
            },
            []));

    private sealed record PreparedOperation(
        JsonObject? Arguments,
        WorkResolutionGuard.GuardClarification? Clarification);

    private async Task<PreparedOperation> ResolveWorkOperationAsync(
        OperationRun run,
        RequestedOperation operation,
        JsonObject plannedArguments,
        IReadOnlyList<string> userQueries,
        string locale,
        JsonArray trace,
        Action<double> recordMcpMilliseconds,
        CancellationToken cancellationToken)
    {
        var rawUserQuery = userQueries[^1];
        var guard = new WorkResolutionGuard();
        guard.ObserveUserConfirmation(rawUserQuery);
        var workQuery = String(plannedArguments, "work_query")
            ?? String(plannedArguments, operation.Tool == "provenance" ? "lex_id" : "work")
            ?? rawUserQuery;
        var article = String(plannedArguments, "article_number");
        async Task<JsonNode> Search(string query, string phase, Action<JsonNode> observe)
        {
            var searchArguments = new JsonObject
            {
                ["query"] = query,
                ["retrieval_mode"] = "keyword",
                ["fuzzy"] = "auto",
                ["limit"] = 8,
            };
            Copy(plannedArguments, searchArguments,
                "publisher", "jurisdiction", "source_class", "hierarchy", "domain", "language");
            var mcpWatch = Stopwatch.StartNew();
            var result = await _legalTool("search", searchArguments, cancellationToken);
            mcpWatch.Stop();
            recordMcpMilliseconds(mcpWatch.Elapsed.TotalMilliseconds);
            var resultStatus = LegalOperationPolicy.StatusForResult(result);
            run.ObserveSupportingCall(operation.OperationId, SupportingCallRole.WorkResolution,
                "search", searchArguments, resultStatus, result);
            observe(result);
            var (status, docs) = Summarize(result);
            trace.Add(new JsonObject
            {
                ["phase"] = phase,
                ["operation_id"] = operation.OperationId,
                ["tool"] = "search",
                ["args"] = searchArguments.DeepClone(),
                ["status"] = status ?? resultStatus,
                ["docs"] = docs,
            });
            return result;
        }

        var priorWorks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var priorQuery in userQueries.SkipLast(1).Reverse().Take(MaxContextResolutionQueries))
        {
            var priorGuard = new WorkResolutionGuard();
            await Search(priorQuery, "prior_work_resolution", priorGuard.ObservePriorUserSearch);
            if (priorGuard.ResolvedWorks.Count == 0) continue;
            priorWorks.UnionWith(priorGuard.ResolvedWorks);
            break;
        }
        guard.AuthorizePriorWorks(priorWorks);
        var carriesPriorSubject = priorWorks.Count > 0 && IsAnaphoricWorkReference(rawUserQuery);
        var resolution = await Search(rawUserQuery, "work_resolution",
            result => guard.ObserveCurrentUserSearch(result, hasPriorContext: carriesPriorSubject));
        JsonNode? focused = null;
        var resolutionQuery = article is null ? workQuery : $"{workQuery} Article {article}";
        if (guard.CurrentResolvedWorks.Count != 1
            && !string.Equals(resolutionQuery, rawUserQuery, StringComparison.OrdinalIgnoreCase))
            focused = await Search(resolutionQuery, "focused_work_resolution",
                result => guard.ObserveFocusedSearch(result, hasPriorContext: carriesPriorSubject));
        var plannedWork = String(plannedArguments,
            operation.Tool == "provenance" ? "lex_id" : "work");
        var focusedCandidates = CandidateWorks(focused).Distinct().ToArray();
        var focusedCurrent = focusedCandidates.Where(guard.CurrentResolvedWorks.Contains).ToArray();
        var focusedPrior = focusedCandidates.Where(priorWorks.Contains).ToArray();
        var focusedDirect = focusedCandidates
            .Where(guard.ResolvedWorks.Contains)
            .Where(work => !priorWorks.Contains(work))
            .ToArray();
        var selected = guard.CurrentResolvedWorks.Count == 1
            ? guard.CurrentResolvedWorks.Single()
            : focusedCurrent is [var currentWork] ? currentWork
            : guard.CurrentResolvedWorks.Count > 1 ? null
            : focusedDirect is [var directWork] ? directWork
            : !carriesPriorSubject ? null
            : focusedPrior is [var priorWork] ? priorWork
            : priorWorks.Count == 1 ? priorWorks.Single()
            : plannedWork is not null
              && guard.ResolvedWorks.Contains(WorkResolutionGuard.WorkKey(plannedWork))
                ? WorkResolutionGuard.WorkKey(plannedWork)
            : focusedCandidates.Where(guard.ResolvedWorks.Contains).ToArray()
                is [var focusedWork] ? focusedWork
            : guard.ResolvedWorks.Count == 1 ? guard.ResolvedWorks.Single()
            : null;
        if (selected is null)
        {
            var candidate = guard.ClarificationFor(plannedWork, locale);
            if (candidate is not null) return new PreparedOperation(null, candidate);
            var display = AgentAnswerContract.Validate(new AgentAnswerDraft(
                AgentAnswerStatus.Clarify,
                locale == "fr"
                    ? "Indiquez le titre officiel ou l'identifiant de l'instrument."
                    : "Provide the official title or identifier of the instrument.",
                [], [], null,
                new AgentClarification(
                    locale == "fr"
                        ? "Quel instrument Lex doit-il utiliser ?"
                        : "Which instrument should Lex use?",
                    [])), []).Clarification!;
            return new PreparedOperation(null,
                new WorkResolutionGuard.GuardClarification(display, []));
        }

        var actual = plannedArguments.DeepClone().AsObject();
        actual.Remove("work_query");
        actual.Remove("article_number");
        if (operation.Tool == "provenance")
            actual["lex_id"] = plannedWork is not null
                && WorkResolutionGuard.WorkKey(plannedWork) == selected ? plannedWork : selected;
        else
            actual["work"] = selected;
        if (article is not null)
        {
            var anchor = ArticleAnchor(focused ?? resolution, selected, article)
                ?? "art_" + new string(article.ToLowerInvariant()
                    .Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
            if (operation.Tool == "as_of")
            {
                actual["mode"] = "select";
                actual["anchors"] = anchor;
            }
            else
                actual["anchor"] = anchor;
        }
        return new PreparedOperation(actual, null);
    }

    private static bool IsAnaphoricWorkReference(string query)
    {
        var value = $" {query.ToLowerInvariant()} ";
        return new[]
        {
            " it ", " its ", " that law ", " that act ", " that one ",
            " this law ", " this act ", " this one ", " the same law ",
            " celui-ci ", " celle-ci ", " cette loi ", " cet acte ",
            " ce texte ", " le même ", " la même ",
        }.Any(marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private static IEnumerable<string> CandidateWorks(JsonNode? result)
    {
        if (result is null) yield break;
        foreach (var response in result is JsonArray array
                     ? array.OfType<JsonObject>()
                     : result is JsonObject item ? [item] : [])
        {
            foreach (var resolution in response["query_plan"]?["global_work_resolutions"]?.AsArray()
                         .OfType<JsonObject>() ?? [])
                foreach (var candidate in resolution["candidates"]?.AsArray() ?? [])
                    if (candidate?.GetValue<string>() is { } work)
                        yield return WorkResolutionGuard.WorkKey(work);
            foreach (var hit in response["hits"]?.AsArray().OfType<JsonObject>() ?? [])
                if (hit["lex_id"]?.GetValue<string>() is { } lexId)
                    yield return WorkResolutionGuard.WorkKey(lexId);
        }
    }

    private static string? ArticleAnchor(JsonNode result, string work, string article)
    {
        var normalized = new string(article.Where(char.IsLetterOrDigit).ToArray());
        foreach (var response in result is JsonArray array
                     ? array.OfType<JsonObject>()
                     : result is JsonObject item ? [item] : [])
            foreach (var hit in response["hits"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var lexId = hit["lex_id"]?.GetValue<string>();
                if (lexId is null || WorkResolutionGuard.WorkKey(lexId) != work) continue;
                var candidate = hit["anchor"]?.GetValue<string>();
                var number = hit["provision_num"]?.GetValue<string>() ?? candidate;
                var comparable = new string((number ?? "").Where(char.IsLetterOrDigit).ToArray());
                if (candidate is { Length: > 0 }
                    && comparable.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        return null;
    }

    private static string? String(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.Trim()
            : null;

    private static void Copy(JsonObject source, JsonObject target, params string[] names)
    {
        foreach (var name in names)
            if (source[name] is { } value)
                target[name] = value.DeepClone();
    }

    private static string ContractName<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private async Task<(int Status, JsonObject Body)> LegacyAskAsync(JsonArray history, string ip, string host,
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

        var admission = _admission.TryAdmit(ip);
        if (!admission.Accepted)
            return (429, new JsonObject { ["error"] = AdmissionReason(admission.Failure) });
        using var admissionLease = admission.Lease!;
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
                catch (Exception)
                {
                    // Conversation context is optional. A stale earlier query must never
                    // prevent the current, independently valid question from running.
                    Diagnostic("prior_resolution_skipped");
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
            // An article number without a law is already a complete, deterministic ambiguity:
            // many instruments contain that article. Do not spend a model round rediscovering
            // the same boundary or turn it into a generic evidence refusal.
            if (HasUnscopedArticleIntent(rawResult)
                && resolutionGuard.ClarificationFor(null) is { } articleClarification)
                return (200, Body(articleClarification.Display.Question, trace, [],
                    articleClarification.Display, articleClarification.Choices));
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
                    Diagnostic($"upstream_status_{(int)resp.StatusCode}");
                    return (502, new JsonObject { ["error"] = "The model upstream returned an error. Try again shortly." });
                }
                var parsed = JsonNode.Parse(respText);
                if (parsed?["usage"]?["total_tokens"] is { } tt)
                    Diagnostic($"round_{round}_tokens_{tt}");
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
                            ApplyWorkspaceDefaults(name, args);
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
                                    ["role"] = "tool",
                                    ["tool_call_id"] = id,
                                    ["content"] = err.ToJsonString(),
                                });
                                continue;
                            }
                            if (listRendered && name is "as_of" or "timeline" or "diff" or "article_history")
                            {
                                entry["status"] = "already_rendered";
                                trace.Add(entry);
                                messages.Add(new JsonObject
                                {
                                    ["role"] = "tool",
                                    ["tool_call_id"] = id,
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
                                    ["role"] = "tool",
                                    ["tool_call_id"] = id,
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
                            toolSpan?.SetTag("lex.status", st ?? McpStatus.Ok);
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
                        catch (Exception)
                        {
                            entry["status"] = "error";
                            result = new JsonObject
                            {
                                ["error"] = "The legal operation failed without exposing internal details.",
                            }.ToJsonString();
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
                    Diagnostic("empty_reply_retry");
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
        catch (HttpRequestException)
        {
            Diagnostic("upstream_unreachable");
            return (502, new JsonObject { ["error"] = "The model upstream is unreachable right now. Try again shortly." });
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            Diagnostic($"agent_upstream_status_{ex.Status}");
            return (502, new JsonObject { ["error"] = "The model upstream returned an error. Try again shortly." });
        }
        catch (Exception)
        {
            Diagnostic("legacy_unexpected_failure");
            return (500, new JsonObject { ["error"] = "Unexpected error in the playground." });
        }
    }

    private AgentAnswerFinalizer Finalizer() =>
        LazyInitializer.EnsureInitialized(ref _answerFinalizer, () => new AgentAnswerFinalizer(
            _endpoint!, _deployment, _credential, _useManagedIdentity ? null : _key));
}
