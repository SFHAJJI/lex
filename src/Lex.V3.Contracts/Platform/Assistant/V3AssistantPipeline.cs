using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// What one operation answered a step: the operation, the parameters code bound, and its result or its refusal. The Api
/// produces it by running the step through the same dispatch REST and MCP use; the pipeline reads it and never anything else.
/// </summary>
public sealed record V3StepOutcome(
    string OperationId,
    string ParametersJson,
    string? ObjectType,
    string? Verdict,
    JsonElement? Result,
    string? RefusalCode,
    JsonElement? Payload)
{
    public bool Refused => RefusalCode is not null;

    /// <summary>The SHA-256 of the result's or the refusal payload's JSON, as the operation wrote it; never of its context.</summary>
    public string ResultSha256
    {
        get
        {
            var body = Refused ? Payload : Result;
            var prefix = Refused ? "refusal:" + RefusalCode + ":" : "result:" + ObjectType + ":";
            var bytes = body is null ? System.Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(body.Value);
            var all = new byte[Encoding.UTF8.GetByteCount(prefix) + bytes.Length];
            var written = Encoding.UTF8.GetBytes(prefix, all);
            bytes.CopyTo(all, written);
            return Convert.ToHexStringLower(SHA256.HashData(all));
        }
    }
}

/// <summary>Runs one operation of the registry with the parameters code bound, through the existing dispatch.</summary>
public delegate V3StepOutcome V3StepExecutor(string operationId, string parametersJson);

/// <summary>What the pipeline knows about where it answers: today in Luxembourg (R13), the pinned snapshot and the search languages.</summary>
public sealed record V3AssistantEnvironment(string Today, string SnapshotId, string SnapshotSha256, IReadOnlyList<string> SearchLanguages);

/// <summary>
/// One question to the assistant: its text, the answering locale when the request names one (a staged input of ask, so null
/// under ask/1, where the question's language decides), and the typed context chips of a follow-up (R45(e)).
/// </summary>
public sealed record V3AssistantRequest(string Question, V3AnswerLocale? AnswerLocale = null, IReadOnlyList<V3ContextChip>? Context = null);

/// <summary>Why ask answered what it answered, as telemetry may count it (R32: buckets only).</summary>
public enum V3AskOutcomeReason
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("answered")]
    Answered = 1,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("switched_off")]
    SwitchedOff = 2,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("question_rejected")]
    QuestionRejected = 3,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("locale_unavailable")]
    LocaleUnavailable = 4,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("planner_failed")]
    PlannerFailed = 5,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("plan_refused")]
    PlanRefused = 6,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("gate_failed")]
    GateFailed = 7,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("cap_reached")]
    CapReached = 8,
}

/// <summary>What ask answers when the assistant runs: an answer, or one of the two presentation results (a type apart, R45(a)).</summary>
public abstract class V3AskOutcome
{
    private protected V3AskOutcome()
    {
    }

    public abstract V3AskOutcomeReason Reason { get; }
}

/// <summary>An answer, with its canonical bytes.</summary>
public sealed class V3AskAnswered(V3AnswerDossier dossier) : V3AskOutcome
{
    public V3AnswerDossier Dossier { get; } = dossier ?? throw new ArgumentNullException(nameof(dossier));

    public override V3AskOutcomeReason Reason => V3AskOutcomeReason.Answered;
}

/// <summary>A presentation result, never a legal verdict, with the reason it was answered.</summary>
public sealed class V3AskPresented(V3AskPresentationResult result, V3AskOutcomeReason reason, V3PlanRefusal? planRefusal = null) : V3AskOutcome
{
    public V3AskPresentationResult Result { get; } = result;

    public override V3AskOutcomeReason Reason { get; } = reason;

    public V3PlanRefusal? PlanRefusal { get; } = planRefusal;
}

/// <summary>A replay's outcome: the answer again, or that the mounted snapshot is not the one the trace pins.</summary>
public sealed record V3ReplayResult(V3AskOutcome? Outcome, bool SnapshotMismatch);

/// <summary>
/// The assistant's pipeline (docs/ANSWER-DOSSIER-1.md §3): intake, parsers, the planner (which only plans), the validator,
/// execution through the existing operations, at most two follow-ups, the verdict computed by code, the composer, and the
/// gates in the code path. Nothing is written anywhere: the answer and its trace are returned to the caller (R32).
/// </summary>
public static class V3AssistantPipeline
{
    /// <summary>The most quotations one answer carries (R45(f)).</summary>
    public const int MaximumQuotes = 5;

    /// <summary>The longest quotation, in UTF-16 units; a longer article is quoted from its start and says it continues.</summary>
    public const int MaximumQuoteLength = 8000;

    /// <summary>The most rows a search or a changes list is asked for (R45(f)).</summary>
    public const int ResultRowCap = 10;

    /// <summary>The most candidates a clarifying question lists.</summary>
    public const int MaximumCandidates = 10;

    public static V3AskOutcome Answer(
        V3AssistantRequest request,
        IAssistantPlanner planner,
        V3StepExecutor execute,
        V3AssistantEnvironment environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(environment);

        var intake = V3AssistantParsers.Intake(request.Question);
        if (intake is null)
        {
            return new V3AskPresented(V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.QuestionRejected);
        }

        var locale = request.AnswerLocale ?? V3AssistantParsers.LocaleFor(V3AssistantParsers.LanguageOf(intake.Text));
        if (locale is null)
        {
            return new V3AskPresented(V3AskPresentationResult.LocalizationUnavailable, V3AskOutcomeReason.LocaleUnavailable);
        }

        var chips = (request.Context ?? []).ToArray();
        if (chips.Length > 2 || chips.Any(static chip => chip is null || chip.Kind is not ("work" or "date") || string.IsNullOrWhiteSpace(chip.Value)))
        {
            return new V3AskPresented(V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.QuestionRejected);
        }

        var dates = V3AssistantParsers.DatesIn(intake.Text);
        var context = new V3PlanContext(intake, dates, V3AssistantParsers.RangesIn(intake.Text, dates), chips, environment.SearchLanguages);
        V3PlannerOutput output;
        try
        {
            output = planner.Plan(new V3PlannerInput(intake.Masked, locale.Value, context.References(), environment.SearchLanguages), cancellationToken)
                ?? throw new InvalidOperationException("The planner answered nothing.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A planner that fails, times out or is cancelled fails closed: the switched-off card, and search keeps working.
            return new V3AskPresented(V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.PlannerFailed);
        }

        var validation = V3AssistantPlanValidator.Validate(output.PlanJson ?? string.Empty, context);
        if (!validation.Admitted)
        {
            return new V3AskPresented(V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.PlanRefused, validation.Refusal);
        }

        var dossier = Run(validation.Plan!, planner.PlannerId, context, locale.Value, execute, environment);
        return dossier is null
            ? new V3AskPresented(V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.GateFailed)
            : new V3AskAnswered(dossier);
    }

    /// <summary>
    /// Replays a trace against the mounted snapshot: the same question, locale, context, plan and day, through the same
    /// operations, each run as on the trace's day (an operation may read "today", as search's exact-reference phase does,
    /// so <paramref name="executeOn"/> hands the executor for a day). The answer is byte-identical to the one the trace came
    /// with, or the snapshot is not the pinned one.
    /// </summary>
    public static V3ReplayResult Replay(string traceJson, Func<string, V3StepExecutor> executeOn, V3AssistantEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(traceJson);
        ArgumentNullException.ThrowIfNull(executeOn);
        ArgumentNullException.ThrowIfNull(environment);
        using var document = JsonDocument.Parse(traceJson);
        var trace = document.RootElement;
        if (!string.Equals(trace.GetProperty("schema").GetString(), V3AnswerTrace.Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException("Not a lex-v3-answer-trace/1.", nameof(traceJson));
        }

        if (!string.Equals(trace.GetProperty("snapshot").GetProperty("snapshot_sha256").GetString(), environment.SnapshotSha256, StringComparison.Ordinal))
        {
            return new V3ReplayResult(null, SnapshotMismatch: true);
        }

        var locale = trace.GetProperty("answer_locale").GetString() == "fr" ? V3AnswerLocale.Fr : V3AnswerLocale.En;
        var chips = trace.GetProperty("context").EnumerateArray()
            .Select(static chip => new V3ContextChip(chip.GetProperty("kind").GetString()!, chip.GetProperty("value").GetString()!))
            .ToArray();
        var planner = new ReplayPlanner(trace.GetProperty("planner_id").GetString()!, trace.GetProperty("plan").GetRawText());
        var day = trace.GetProperty("today").GetString()!;
        var pinned = environment with { Today = day };
        return new V3ReplayResult(
            Answer(new V3AssistantRequest(trace.GetProperty("question").GetString()!, locale, chips), planner, executeOn(day), pinned, CancellationToken.None),
            SnapshotMismatch: false);
    }

    /// <summary>The planner a replay uses: it answers the trace's plan, and is named as the trace's planner.</summary>
    private sealed class ReplayPlanner(string plannerId, string planJson) : IAssistantPlanner
    {
        public string PlannerId { get; } = plannerId;

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken) => new(planJson, 0);
    }

    // ---- Execution --------------------------------------------------------------------------------------------------

    private sealed record Executed(V3PlanStep Step, V3StepOutcome? Outcome, IReadOnlyDictionary<string, (V3AnswerSlotKind Kind, string Value)> Bound, bool UsedToday);

    private static V3AnswerDossier? Run(
        V3AssistantPlan plan,
        string plannerId,
        V3PlanContext context,
        V3AnswerLocale locale,
        V3StepExecutor execute,
        V3AssistantEnvironment environment)
    {
        var executed = new List<Executed>();
        foreach (var step in plan.Steps)
        {
            var (json, bound, usedToday) = BindParameters(step, context, environment.Today, executed);
            executed.Add(new Executed(step, execute(step.OperationId, json), bound, usedToday));
        }

        foreach (var followUp in plan.FollowUps)
        {
            var earlier = executed.Single(item => item.Step.StepId == followUp.After).Outcome!;
            var fires = followUp.Condition switch
            {
                V3FollowUpCondition.OnSingleWork => SoleWork(earlier) is not null,
                V3FollowUpCondition.OnSingleArticle => SoleWork(earlier) is not null && SoleArticle(earlier) is not null,
                _ => false,
            };
            if (!fires)
            {
                executed.Add(new Executed(followUp, null, new Dictionary<string, (V3AnswerSlotKind, string)>(), false));
                continue;
            }

            var (json, bound, usedToday) = BindParameters(followUp, context, environment.Today, executed);
            executed.Add(new Executed(followUp, execute(followUp.OperationId, json), bound, usedToday));
        }

        var trace = new V3AnswerTrace(
            context.Intake.Text,
            locale,
            context.Context,
            plannerId,
            plan,
            environment.Today,
            environment.SnapshotId,
            environment.SnapshotSha256,
            V3OperationRegistry.Reviewed.Sha256,
            executed.Select(static item => item.Outcome is null
                ? new V3TraceStep(item.Step.StepId, item.Step.OperationId, false, null, "not_fired", null, null, null, null)
                : new V3TraceStep(
                    item.Step.StepId,
                    item.Step.OperationId,
                    true,
                    item.Outcome.ParametersJson,
                    item.Outcome.Refused ? "refusal" : "result",
                    item.Outcome.ObjectType,
                    item.Outcome.Verdict,
                    item.Outcome.RefusalCode,
                    item.Outcome.ResultSha256)).ToArray());

        var dossier = Compose(plan, context, locale, executed, environment, trace);
        return V3AnswerGates.Check(dossier, executed.Where(static item => item.Outcome is not null).ToDictionary(static item => item.Step.StepId, static item => item.Outcome!, StringComparer.Ordinal))
            ? dossier
            : null;
    }

    private static (string Json, Dictionary<string, (V3AnswerSlotKind Kind, string Value)> Bound, bool UsedToday) BindParameters(
        V3PlanStep step,
        V3PlanContext context,
        string today,
        IReadOnlyList<Executed> executed)
    {
        var values = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var bound = new Dictionary<string, (V3AnswerSlotKind, string)>(StringComparer.Ordinal);
        var usedToday = false;
        foreach (var (name, source) in step.Arguments)
        {
            string value;
            V3AnswerSlotKind kind;
            switch (source.Kind)
            {
                case V3ArgumentSourceKind.Span:
                    value = context.Intake.Text[source.Span!.Start..source.Span.End];
                    kind = name == "query" ? V3AnswerSlotKind.QuestionSpan : V3AnswerSlotKind.IdentifierText;
                    break;
                case V3ArgumentSourceKind.Reference:
                    var referenceKind = name switch
                    {
                        "date" => source.Ref!.StartsWith('c') ? "context_date" : "date",
                        "date_from" => "range_from",
                        "date_to" => "range_to",
                        _ => "context_work",
                    };
                    value = context.Value(source.Ref!, referenceKind)!;
                    kind = name == "identifier" ? V3AnswerSlotKind.IdentifierText : V3AnswerSlotKind.CalendarDate;
                    break;
                case V3ArgumentSourceKind.Result:
                    var earlier = executed.Single(item => item.Step.StepId == source.Step).Outcome!;
                    value = (source.Pick == V3ResultPick.SoleWork ? SoleWork(earlier) : SoleArticle(earlier))!;
                    kind = source.Pick == V3ResultPick.SoleWork ? V3AnswerSlotKind.IdentifierText : V3AnswerSlotKind.Anchor;
                    break;
                case V3ArgumentSourceKind.Choice:
                    value = source.Choice!;
                    kind = V3AnswerSlotKind.LanguageCode;
                    break;
                case V3ArgumentSourceKind.Today:
                    value = today;
                    kind = V3AnswerSlotKind.CalendarDate;
                    usedToday = true;
                    break;
                default:
                    throw new InvalidOperationException("Not an argument source.");
            }

            values[name] = value;
            bound[name] = (kind, value);
        }

        // R45(f): code caps result sizes.
        if (step.OperationId is "search" or "changes_in_period")
        {
            values["limit"] = ResultRowCap;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in values)
            {
                if (value is int number)
                {
                    writer.WriteNumber(name, number);
                }
                else
                {
                    writer.WriteString(name, (string)value);
                }
            }

            writer.WriteEndObject();
        }

        return (Encoding.UTF8.GetString(stream.ToArray()), bound, usedToday);
    }

    // ---- Reading typed results --------------------------------------------------------------------------------------

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool True(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static JsonElement[] Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

    private static JsonElement? Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    /// <summary>
    /// Whether an identifier or a result is of an EU act, whose dated texts are wordings and never versions (R21): an address
    /// under /eu-eurlex/, a CELEX, or a result whose publisher is eu-eurlex.
    /// </summary>
    internal static bool IsEurope(string? identifier, JsonElement? result = null) =>
        (result is { } value && Text(value, "publisher") == "eu-eurlex")
        || (identifier is not null && (identifier.StartsWith("/eu-eurlex/", StringComparison.Ordinal) || CelexPattern.IsMatch(identifier)));

    // A plain pattern, not a source-generated one: the generator adds types of its own to the assembly, which the
    // closed-surface census would then have to pin (as V3WebRoot says of its own).
    private static readonly System.Text.RegularExpressions.Regex CelexPattern = new(
        "^[0-9][0-9]{4}[A-Z]{1,2}[0-9]{3,4}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A work's address on this site, from its publisher and key (an EU act by its CELEX as one segment).</summary>
    internal static string WorkAddress(string publisher, string work) =>
        publisher == "eu-eurlex" ? "/eu-eurlex/" + work.Replace("/", "%2F", StringComparison.Ordinal) : "/lu-legilux/" + work;

    private static (string Publisher, string Work)[] HitWorks(JsonElement result) =>
        Items(result, "hits")
            .Select(hit => (Text(hit, "publisher") ?? Text(result, "publisher") ?? "lu-legilux", Text(hit, "work_key") ?? Text(hit, "celex")))
            .Where(static pair => pair.Item2 is not null)
            .Select(static pair => (pair.Item1, pair.Item2!))
            .Distinct()
            .ToArray();

    /// <summary>The one work an outcome names, as an identifier the operations read, or null when it names none or several.</summary>
    internal static string? SoleWork(V3StepOutcome outcome)
    {
        if (outcome.Refused || outcome.Result is not { } result)
        {
            return null;
        }

        switch (outcome.OperationId)
        {
            case "search":
                if (Member(result, "reference") is { } reference && Text(reference, "outcome") == "opens" &&
                    Member(reference, "opens") is { } opens && Text(opens, "work") is { } opened)
                {
                    return WorkAddress(Text(opens, "publisher") ?? "lu-legilux", opened);
                }

                var works = HitWorks(result);
                return works.Length == 1 ? WorkAddress(works[0].Publisher, works[0].Work) : null;
            case "resolve":
                return Text(result, "work_identifier") is not null || Text(result, "publisher_work_id") is not null
                    ? Text(result, "requested_identifier")
                    : null;
            case "dossier" or "timeline" or "as_of" or "evidence_bundle" or "article_history":
                return Text(result, "requested_identifier");
            default:
                return null;
        }
    }

    /// <summary>The one article of the one work an outcome names (its publisher identifier), or null.</summary>
    internal static string? SoleArticle(V3StepOutcome outcome)
    {
        if (outcome.Refused || outcome.Result is not { } result || outcome.OperationId != "search")
        {
            return null;
        }

        if (Member(result, "reference") is { } reference && Text(reference, "outcome") == "opens" && Member(reference, "opens") is { } opens)
        {
            return Text(opens, "article_id");
        }

        if (HitWorks(result).Length != 1)
        {
            return null;
        }

        var articles = Items(result, "hits").Select(hit => Text(hit, "publisher_id")).Where(static id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
        return articles.Length == 1 ? articles[0] : null;
    }

    private static IEnumerable<V3Quote> QuotesOf(string stepId, V3StepOutcome outcome, string article, Func<string> nextId)
    {
        if (outcome.Refused || outcome.Result is not { } result || outcome.OperationId != "evidence_bundle")
        {
            yield break;
        }

        var publisher = Text(result, "publisher") ?? "lu-legilux";
        var europe = publisher == "eu-eurlex";
        var work = europe
            ? WorkAddress(publisher, Text(result, "celex") ?? Text(result, "requested_identifier") ?? "unknown")
            : WorkAddress(publisher, Text(result, "work_key") ?? "unknown");
        var journal = (Member(result, "as_published") is { } asPublished ? Text(asPublished, "journal_officiel_iri") : null)
            ?? (Member(result, "citation_facts") is { } facts ? Text(facts, "journal_officiel_iri") : null);
        foreach (var container in Items(result, europe ? "wordings" : "states"))
        {
            foreach (var item in Items(container, "articles"))
            {
                if (Text(item, "publisher_id") != article || Text(item, "text") is not { Length: > 0 } text || Text(item, "text_sha256") is not { } sha)
                {
                    continue;
                }

                var length = text.Length <= MaximumQuoteLength ? text.Length : MaximumQuoteLength - (char.IsHighSurrogate(text[MaximumQuoteLength - 1]) ? 1 : 0);
                var corrigenda = Items(container, "corrigenda").Length > 0 || Items(result, "corrigenda").Length > 0;
                yield return new V3Quote(
                    nextId(),
                    stepId,
                    publisher,
                    work,
                    article,
                    Text(item, "language") ?? Text(container, "language") ?? "fra",
                    text[..length],
                    0,
                    length,
                    length < text.Length,
                    sha,
                    Text(container, europe ? "wording_sha256" : "state_sha256") ?? string.Empty,
                    Text(container, europe ? "wording_date" : "applicability_date") ?? string.Empty,
                    Text(container, europe ? "next_date" : "applicable_to"),
                    europe ? "wording_state" : Text(container, "date_role") ?? "applicability",
                    Text(container, "permalink") ?? string.Empty,
                    Text(item, "article_permalink") ?? Text(container, "permalink") ?? string.Empty,
                    Text(item, "official_source") ?? journal ?? string.Empty,
                    journal,
                    Text(container, europe ? "kind" : "state_kind") ?? string.Empty,
                    True(container, "provisional"),
                    True(item, "validity_conflict"),
                    Text(item, "article_valid_from"),
                    corrigenda,
                    Items(container, "derogations").Length > 0,
                    True(container, "pre_application"));
            }
        }
    }

    // ---- The verdict (code decides, biased to refusal) --------------------------------------------------------------

    private static readonly HashSet<string> ClarifyCodes = new(StringComparer.Ordinal) { "ambiguous_identifier", "ambiguous_version" };

    private enum Finding
    {
        Quote,
        Held,
        Clarify,
        NotHeld,
    }

    private static Finding FindingOf(Executed item, bool quoted)
    {
        var outcome = item.Outcome!;
        if (quoted)
        {
            return Finding.Quote;
        }

        if (outcome.Refused)
        {
            return ClarifyCodes.Contains(outcome.RefusalCode!) ? Finding.Clarify : Finding.NotHeld;
        }

        if (outcome.OperationId == "search")
        {
            var result = outcome.Result!.Value;
            if (Member(result, "reference") is { } reference && Text(reference, "outcome") == "opens")
            {
                return Finding.Clarify;
            }

            return Items(result, "hits").Length == 0 ? Finding.NotHeld : Finding.Clarify;
        }

        return Finding.Held;
    }

    // ---- The composer ------------------------------------------------------------------------------------------------

    private static V3AnswerDossier Compose(
        V3AssistantPlan plan,
        V3PlanContext context,
        V3AnswerLocale locale,
        IReadOnlyList<Executed> executed,
        V3AssistantEnvironment environment,
        V3AnswerTrace trace)
    {
        var question = context.Intake.Text;
        var quoteNumber = 0;
        string NextQuoteId() => "q" + (++quoteNumber).ToString(CultureInfo.InvariantCulture);

        var applications = plan.Parts.ToDictionary(
            static part => part.PartId,
            part =>
            {
                var cues = V3AssistantParsers.ApplicationCues(question, part.Span.Start, part.Span.End);
                return (Cues: cues, Application: cues.Count > 0 || part.ProposedVerdict == V3AssistantVerdict.Refuse);
            },
            StringComparer.Ordinal);
        var whole = applications.Values.All(static value => value.Application);
        var mixed = plan.Parts.Count == 2 && applications.Values.Select(static value => value.Application).Distinct().Count() == 2;

        var parts = new List<V3DossierPart>();
        var quotesSoFar = 0;
        foreach (var part in plan.Parts)
        {
            var (cues, application) = applications[part.PartId];
            var items = executed.Where(item => item.Step.PartId == part.PartId && item.Outcome is not null).ToArray();
            var law = new List<V3DossierBlock>();
            var found = new List<V3DossierBlock>();
            var findings = new List<Finding>();
            var firstDate = (string?)null;
            var publishers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var outcome = item.Outcome!;
                var quotes = item.Step.OperationId == "evidence_bundle" && item.Bound.TryGetValue("article", out var article)
                    ? QuotesOf(item.Step.StepId, outcome, article.Value, NextQuoteId).Take(Math.Max(0, MaximumQuotes - quotesSoFar)).ToArray()
                    : [];
                quotesSoFar += quotes.Length;

                // A step a fired follow-up read further is answered by that follow-up (R35: it adds information); its own
                // result is not offered again beside it.
                var followed = items.Any(other => other.Step.IsFollowUp && other.Step.After == item.Step.StepId && other.Outcome is { Refused: false });
                findings.Add(followed ? Finding.Held : FindingOf(item, quotes.Length > 0));
                foreach (var quote in quotes)
                {
                    firstDate ??= item.Bound.TryGetValue("date", out var date) ? date.Value : quote.From;
                    publishers.Add(quote.Publisher);
                    law.AddRange(QuoteBlocks(quote, item.Bound.TryGetValue("date", out var asked) ? asked.Value : quote.From, locale));
                }

                if (quotes.Length == 0 && !followed)
                {
                    found.AddRange(OutcomeBlocks(item, locale, publishers));
                }
            }

            V3AssistantVerdict verdict;
            var blocks = new List<V3DossierBlock>();
            if (application)
            {
                verdict = V3AssistantVerdict.Refuse;
                // The descriptive maximum is the law the plan found, verbatim and dated; nothing else of an application part
                // is shown (S4-A05: the assistant never applies the law to personal facts).
                var quoted = law.OfType<V3QuoteBlock>().Any();
                if (whole)
                {
                    // R45(b): open with the refusal only when the whole question asks for application.
                    blocks.Add(quoted && firstDate is not null
                        ? Claim(V3AnswerTemplates.BoundaryWholeNoAdviser, locale, Date("date", firstDate))
                        : Claim(V3AnswerTemplates.BoundaryWholeNoText, locale));
                    blocks.AddRange(law);
                }
                else
                {
                    // R45(b): lead with the law, verbatim and dated, then one boundary line and the handoff.
                    blocks.AddRange(law);
                    blocks.Add(Claim(V3AnswerTemplates.BoundaryLine, locale));
                    blocks.Add(Claim(V3AnswerTemplates.BoundaryNoAdviser, locale));
                }

                blocks.AddRange(Handoffs(V3HandoffRole.Information, locale));
            }
            else
            {
                verdict = findings.Contains(Finding.Quote) || findings.Contains(Finding.Held) ? V3AssistantVerdict.Answer
                    : findings.Contains(Finding.Clarify) ? V3AssistantVerdict.Clarify
                    : V3AssistantVerdict.Point;
                blocks.AddRange(law);
                blocks.AddRange(found);
                if (verdict == V3AssistantVerdict.Point)
                {
                    if (!found.OfType<V3ClaimBlock>().Any() && !found.OfType<V3RefusalBlock>().Any())
                    {
                        blocks.Add(Claim(V3AnswerTemplates.PointNotHeld, locale));
                    }

                    blocks.AddRange(OfficialSources(publishers, locale));
                }
            }

            parts.Add(new V3DossierPart(part.PartId, part.Span, application, cues, verdict, blocks));
        }

        var overall = mixed ? V3AssistantVerdict.Split
            : whole ? V3AssistantVerdict.Refuse
            : parts.Any(static part => part.Verdict == V3AssistantVerdict.Answer) ? V3AssistantVerdict.Answer
            : parts.Any(static part => part.Verdict == V3AssistantVerdict.Clarify) ? V3AssistantVerdict.Clarify
            : V3AssistantVerdict.Point;

        var label = Claim(overall switch
        {
            V3AssistantVerdict.Answer => V3AnswerTemplates.VerdictAnswer,
            V3AssistantVerdict.Point => V3AnswerTemplates.VerdictPoint,
            V3AssistantVerdict.Clarify => V3AnswerTemplates.VerdictClarify,
            V3AssistantVerdict.Refuse => V3AnswerTemplates.VerdictRefuse,
            V3AssistantVerdict.Split => V3AnswerTemplates.VerdictSplit,
            _ => throw new InvalidOperationException("answer_with_enrichment is never emitted."),
        }, locale);
        var notices = new List<V3AnswerClaim>();
        if (V3AnswerTemplates.Preview)
        {
            // The owner's order of 2026-10-28: while the people's reviews are not done, every answer says it is a preview.
            notices.Add(V3AnswerClaim.Bind(V3AnswerTemplates.NoticePreview, locale));
        }

        notices.Add(V3AnswerClaim.Bind(V3AnswerTemplates.NoticeAssembledByModel, locale));
        notices.Add(V3AnswerClaim.Bind(V3AnswerTemplates.NoticeNotLegalAdvice, locale));

        return new V3AnswerDossier(overall, locale, label.Claim, notices, PlanLines(plan, question, locale, executed, environment.Today), parts, trace);
    }

    private static V3ClaimBlock Claim(string templateId, V3AnswerLocale locale, params V3AnswerFact[] facts) =>
        new(V3AnswerClaim.Bind(templateId, locale, facts));

    private static V3AnswerFact Date(string name, string value) => new(name, V3AnswerSlotKind.CalendarDate, value);

    private static V3AnswerFact Fact(string name, V3AnswerSlotKind kind, string value) => new(name, kind, value);

    private static IEnumerable<V3DossierBlock> QuoteBlocks(V3Quote quote, string askedDate, V3AnswerLocale locale)
    {
        var europe = quote.Publisher == "eu-eurlex";
        var original = europe && quote.StateKind == "original_wording";
        yield return Claim(V3AnswerTemplates.LawQuoteHeading, locale,
            Fact("work", V3AnswerSlotKind.WorkAddress, quote.Work), Fact("anchor", V3AnswerSlotKind.Anchor, quote.Anchor), Date("date", askedDate));
        yield return new V3QuoteBlock(quote);
        if (quote.Shortened)
        {
            yield return Claim(V3AnswerTemplates.LawQuoteShortened, locale, Fact("uri", V3AnswerSlotKind.Uri, quote.ArticlePermalink));
        }

        if (!string.IsNullOrEmpty(quote.From))
        {
            yield return original
                ? Claim(V3AnswerTemplates.LawEuOriginalWording, locale, Date("from", quote.From))
                : europe
                    ? quote.To is { } euTo ? Claim(V3AnswerTemplates.LawIntervalEu, locale, Date("from", quote.From), Date("to", euTo)) : Claim(V3AnswerTemplates.LawIntervalEuOpen, locale, Date("from", quote.From))
                    : quote.DateRole == "entry_into_force"
                        ? Claim(V3AnswerTemplates.LawDatedByEntryIntoForce, locale, Date("date", quote.From))
                        : quote.To is { } to ? Claim(V3AnswerTemplates.LawIntervalLu, locale, Date("from", quote.From), Date("to", to)) : Claim(V3AnswerTemplates.LawIntervalLuOpen, locale, Date("from", quote.From));
        }

        yield return Claim(V3AnswerTemplates.LawContentHash, locale, Fact("hash", V3AnswerSlotKind.ContentHash, quote.ArticleTextSha256));
        if (V3AnswerTemplates.IsUri(quote.OfficialSource))
        {
            yield return Claim(V3AnswerTemplates.LawOfficialSource, locale, Fact("uri", V3AnswerSlotKind.Uri, quote.OfficialSource));
        }

        if (V3AnswerTemplates.IsUri(quote.ArticlePermalink))
        {
            yield return Claim(V3AnswerTemplates.LawPermalink, locale, Fact("uri", V3AnswerSlotKind.Uri, quote.ArticlePermalink));
        }

        foreach (var (flag, claim) in V3AnswerGates.RequiredDisclosures(quote).Select(flag => (flag, DisclosureClaim(flag, quote, locale))))
        {
            yield return new V3DisclosureBlock(flag, quote.QuoteId, claim);
        }
    }

    private static V3AnswerClaim DisclosureClaim(V3DisclosureFlag flag, V3Quote quote, V3AnswerLocale locale) => flag switch
    {
        V3DisclosureFlag.Authenticity when quote.Publisher == "eu-eurlex" => V3AnswerClaim.Bind(
            quote.StateKind == "original_wording" ? V3AnswerTemplates.DisclosureAuthenticityEuOriginal : V3AnswerTemplates.DisclosureAuthenticityEu, locale),
        V3DisclosureFlag.Authenticity => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureAuthenticityLu, locale,
            Fact("uri", V3AnswerSlotKind.Uri, V3AnswerTemplates.IsUri(quote.JournalOfficiel ?? string.Empty) ? quote.JournalOfficiel! : quote.OfficialSource)),
        V3DisclosureFlag.NoLegalEffect => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureNoLegalEffect, locale),
        V3DisclosureFlag.Provisional => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureProvisional, locale, Date("date", quote.From)),
        V3DisclosureFlag.ValidityConflict => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureValidityConflict, locale, Date("date", quote.ArticleValidFrom!)),
        V3DisclosureFlag.Corrigendum => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureCorrigendum, locale),
        V3DisclosureFlag.PreApplication => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosurePreApplication, locale),
        V3DisclosureFlag.Derogation => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureDerogation, locale),
        V3DisclosureFlag.TitleDistrust => V3AnswerClaim.Bind(V3AnswerTemplates.DisclosureTitleDistrust, locale),
        _ => throw new InvalidOperationException("Not a disclosure flag."),
    };

    private static IEnumerable<V3DossierBlock> OutcomeBlocks(Executed item, V3AnswerLocale locale, HashSet<string> publishers)
    {
        var outcome = item.Outcome!;
        var identifier = item.Bound.TryGetValue("identifier", out var bound) ? bound.Value : null;
        var date = item.Bound.TryGetValue("date", out var boundDate) ? boundDate.Value : null;
        if (outcome.Refused)
        {
            var payload = outcome.Payload ?? default;
            var code = outcome.RefusalCode!;
            foreach (var block in RefusalBlocks(item.Step.StepId, code, payload, identifier, date, locale))
            {
                yield return block;
            }

            yield break;
        }

        var result = outcome.Result!.Value;
        if (Text(result, "publisher") is { } publisher)
        {
            publishers.Add(publisher);
        }

        switch (outcome.OperationId)
        {
            case "search":
                if (Member(result, "reference") is { } reference && Text(reference, "outcome") == "opens" && Member(reference, "opens") is { } opens && Text(opens, "work") is { } work)
                {
                    var address = WorkAddress(Text(opens, "publisher") ?? "lu-legilux", work);
                    yield return Claim(V3AnswerTemplates.ClarifyWhichArticleOfAct, locale, Fact("work", V3AnswerSlotKind.WorkAddress, address));
                    yield return Action("dossier", new() { ["identifier"] = address }, V3AnswerTemplates.ActionOpenWork, locale, Fact("work", V3AnswerSlotKind.WorkAddress, address));
                    yield break;
                }

                var hits = Items(result, "hits");
                if (hits.Length == 0)
                {
                    yield return Claim(V3AnswerTemplates.PointNoHit, locale, Fact("query", V3AnswerSlotKind.QuestionSpan, item.Bound["query"].Value));
                    yield break;
                }

                var works = HitWorks(result);
                if (works.Length > 1)
                {
                    yield return Claim(V3AnswerTemplates.ClarifyWhichAct, locale);
                    foreach (var (hitPublisher, hitWork) in works.Take(MaximumCandidates))
                    {
                        var address = WorkAddress(hitPublisher, hitWork);
                        yield return Action("dossier", new() { ["identifier"] = address }, V3AnswerTemplates.ActionOpenWork, locale, Fact("work", V3AnswerSlotKind.WorkAddress, address));
                    }

                    yield break;
                }

                var only = WorkAddress(works[0].Publisher, works[0].Work);
                yield return Claim(V3AnswerTemplates.ClarifyWhichArticle, locale, Fact("work", V3AnswerSlotKind.WorkAddress, only));
                var readOn = date ?? Text(hits[0], "applicability_date");
                foreach (var anchor in hits.Select(hit => Text(hit, "publisher_id")).Where(static id => id is not null).Distinct(StringComparer.Ordinal).Take(MaximumCandidates))
                {
                    if (readOn is null)
                    {
                        continue;
                    }

                    yield return Action("evidence_bundle", new() { ["identifier"] = only, ["date"] = readOn, ["article"] = anchor! }, V3AnswerTemplates.ActionReadArticle, locale,
                        Fact("anchor", V3AnswerSlotKind.Anchor, anchor!), Fact("work", V3AnswerSlotKind.WorkAddress, only), Date("date", readOn));
                }

                yield break;
            case "as_of":
                foreach (var state in Items(result, "states").Concat(Items(result, "wordings")).Take(MaximumCandidates))
                {
                    var from = Text(state, "applicability_date") ?? Text(state, "wording_date");
                    if (from is null || identifier is null)
                    {
                        continue;
                    }

                    yield return Action("as_of", new() { ["identifier"] = identifier, ["date"] = from },
                        IsEurope(identifier, result) ? V3AnswerTemplates.ActionReadStateEu : V3AnswerTemplates.ActionReadState, locale,
                        Fact("work", V3AnswerSlotKind.IdentifierText, identifier), Date("date", from));
                }

                yield break;
            default:
                if (identifier is not null && identifier.StartsWith('/'))
                {
                    yield return Action(outcome.OperationId, new() { ["identifier"] = identifier }, V3AnswerTemplates.ActionOpenWork, locale, Fact("work", V3AnswerSlotKind.WorkAddress, identifier));
                }

                yield break;
        }
    }

    private static IEnumerable<V3DossierBlock> RefusalBlocks(string stepId, string code, JsonElement payload, string? identifier, string? date, V3AnswerLocale locale)
    {
        var requested = Text(payload, "requested_identifier") ?? identifier;
        switch (code)
        {
            case "no_version_for_date" when requested is not null && (Text(payload, "requested_date") ?? date) is { } requestedDate:
                var europe = IsEurope(requested);
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(
                    europe ? V3AnswerTemplates.RefusalNoVersionForDateEu : V3AnswerTemplates.RefusalNoVersionForDate, locale,
                    Fact("work", V3AnswerSlotKind.IdentifierText, requested), Date("date", requestedDate)));
                foreach (var nearest in new[] { Text(payload, "nearest_earlier"), Text(payload, "nearest_later") })
                {
                    if (nearest is not null)
                    {
                        yield return Action("as_of", new() { ["identifier"] = requested, ["date"] = nearest },
                            europe ? V3AnswerTemplates.ActionReadStateEu : V3AnswerTemplates.ActionReadState, locale,
                            Fact("work", V3AnswerSlotKind.IdentifierText, requested), Date("date", nearest));
                    }
                }

                yield break;
            case "identifier_unknown" when requested is not null:
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.RefusalIdentifierUnknown, locale,
                    Fact("identifier", V3AnswerSlotKind.IdentifierText, requested)));
                yield break;
            case "text_withheld":
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.RefusalTextWithheld, locale));
                yield break;
            case "text_not_available":
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.RefusalTextNotAvailable, locale));
                yield break;
            case "out_of_corpus_scope":
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.RefusalOutOfCorpusScope, locale));
                yield break;
            case "anchor_not_in_version" when Text(payload, "requested_anchor") is { } anchor:
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.RefusalAnchorNotInVersion, locale,
                    Fact("anchor", V3AnswerSlotKind.Anchor, anchor)));
                foreach (var nearest in Items(payload, "nearest_anchors").Select(static value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).Where(static value => !string.IsNullOrWhiteSpace(value)).Take(MaximumCandidates))
                {
                    if (requested is not null && date is not null)
                    {
                        yield return Action("evidence_bundle", new() { ["identifier"] = requested, ["date"] = date, ["article"] = nearest! }, V3AnswerTemplates.ActionCandidate, locale,
                            Fact("candidate", V3AnswerSlotKind.IdentifierText, nearest!));
                    }
                }

                yield break;
            case "ambiguous_identifier":
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.ClarifyWhichIdentifier, locale));
                foreach (var candidate in Candidates(payload))
                {
                    yield return Action("resolve", new() { ["identifier"] = candidate }, V3AnswerTemplates.ActionCandidate, locale, Fact("candidate", V3AnswerSlotKind.IdentifierText, candidate));
                }

                yield break;
            case "ambiguous_version" when (Text(payload, "requested_date") ?? date) is { } ambiguousDate:
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(
                    IsEurope(requested) ? V3AnswerTemplates.ClarifyWhichWordingEu : V3AnswerTemplates.ClarifyWhichState, locale, Date("date", ambiguousDate)));
                foreach (var candidate in Candidates(payload))
                {
                    yield return Action("as_of", new() { ["identifier"] = candidate, ["date"] = ambiguousDate }, V3AnswerTemplates.ActionCandidate, locale, Fact("candidate", V3AnswerSlotKind.IdentifierText, candidate));
                }

                yield break;
            default:
                yield return new V3RefusalBlock(stepId, code, payload, V3AnswerClaim.Bind(V3AnswerTemplates.RefusalOther, locale, Fact("code", V3AnswerSlotKind.RefusalCode, code)));
                yield break;
        }
    }

    private static IEnumerable<string> Candidates(JsonElement payload) =>
        Items(payload, "candidates")
            .Select(static value => value.ValueKind == JsonValueKind.String ? value.GetString() : Text(value, "identifier") ?? Text(value, "permalink"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Take(MaximumCandidates);

    private static V3ActionBlock Action(string operationId, Dictionary<string, string> parameters, string templateId, V3AnswerLocale locale, params V3AnswerFact[] facts) =>
        new(operationId, parameters, V3AnswerClaim.Bind(templateId, locale, facts));

    private static IEnumerable<V3DossierBlock> Handoffs(V3HandoffRole role, V3AnswerLocale locale) =>
        V3HandoffRegistry.OfRole(role).Where(static entry => entry.Theme == "general").Select(entry => (V3DossierBlock)new V3HandoffBlock(entry, V3AnswerClaim.Bind(
            role switch
            {
                V3HandoffRole.OfficialSource => V3AnswerTemplates.HandoffOfficialSource,
                V3HandoffRole.Information => V3AnswerTemplates.HandoffInformation,
                V3HandoffRole.Advice => V3AnswerTemplates.HandoffAdvice,
                V3HandoffRole.Complaint => V3AnswerTemplates.HandoffComplaint,
                _ => V3AnswerTemplates.HandoffMediation,
            },
            locale,
            Fact("name", V3AnswerSlotKind.EntryName, entry.Name),
            Fact("uri", V3AnswerSlotKind.Uri, entry.Link))));

    private static IEnumerable<V3DossierBlock> OfficialSources(HashSet<string> publishers, V3AnswerLocale locale)
    {
        var wanted = publishers.Count == 0
            ? new[] { "legilux", "eur-lex" }
            : publishers.Select(static publisher => publisher == "eu-eurlex" ? "eur-lex" : "legilux").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var id in wanted)
        {
            var entry = V3HandoffRegistry.Get(id);
            yield return new V3HandoffBlock(entry, V3AnswerClaim.Bind(V3AnswerTemplates.HandoffOfficialSource, locale,
                Fact("name", V3AnswerSlotKind.EntryName, entry.Name), Fact("uri", V3AnswerSlotKind.Uri, entry.Link)));
        }
    }

    private static List<V3AnswerClaim> PlanLines(V3AssistantPlan plan, string question, V3AnswerLocale locale, IReadOnlyList<Executed> executed, string today)
    {
        var lines = new List<V3AnswerClaim> { V3AnswerClaim.Bind(V3AnswerTemplates.PlanHeading, locale) };
        if (plan.Parts.Count == 2)
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanSplit, locale,
                Fact("first", V3AnswerSlotKind.QuestionSpan, question[plan.Parts[0].Span.Start..plan.Parts[0].Span.End]),
                Fact("second", V3AnswerSlotKind.QuestionSpan, question[plan.Parts[1].Span.Start..plan.Parts[1].Span.End])));
        }

        if (executed.Any(static item => item.UsedToday))
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, locale, Date("date", today)));
        }

        foreach (var item in executed.Where(static item => item.Outcome is not null))
        {
            if (item.Step.IsFollowUp)
            {
                lines.Add(V3AnswerClaim.Bind(item.Step.Condition == V3FollowUpCondition.OnSingleArticle
                    ? V3AnswerTemplates.PlanFollowUpSingleArticle
                    : V3AnswerTemplates.PlanFollowUpSingleWork, locale));
            }

            lines.Add(StepLine(item, locale));
        }

        return lines;
    }

    private static V3AnswerClaim StepLine(Executed item, V3AnswerLocale locale)
    {
        V3AnswerFact Bound(string name, string? placeholder = null) => new(placeholder ?? name, item.Bound[name].Kind, item.Bound[name].Value);
        V3AnswerFact Identifier() => new("identifier", V3AnswerSlotKind.IdentifierText, item.Bound["identifier"].Value);
        return item.Step.OperationId switch
        {
            "search" when item.Bound.ContainsKey("date") => V3AnswerClaim.Bind(V3AnswerTemplates.PlanSearchDated, locale, Bound("language"), Bound("date"), Bound("query")),
            "search" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanSearch, locale, Bound("language"), Bound("query")),
            "resolve" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanResolve, locale, Identifier()),
            "dossier" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanDossier, locale, Identifier()),
            "timeline" => V3AnswerClaim.Bind(IsEurope(item.Bound["identifier"].Value, item.Outcome?.Result) ? V3AnswerTemplates.PlanTimelineEu : V3AnswerTemplates.PlanTimeline, locale, Identifier()),
            "as_of" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanAsOf, locale, Identifier(), Bound("date")),
            "evidence_bundle" when item.Bound.ContainsKey("article") => V3AnswerClaim.Bind(V3AnswerTemplates.PlanEvidenceBundleArticle, locale, Bound("article", "anchor"), Identifier(), Bound("date")),
            "evidence_bundle" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanEvidenceBundle, locale, Identifier(), Bound("date")),
            "article_history" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanArticleHistory, locale, Bound("anchor"), Identifier()),
            "diff" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanDiff, locale, Identifier(), Bound("date_from"), Bound("date_to")),
            "changes_in_period" => V3AnswerClaim.Bind(V3AnswerTemplates.PlanChangesInPeriod, locale, Bound("date_from"), Bound("date_to")),
            _ => throw new InvalidOperationException($"No plan line for {item.Step.OperationId}."),
        };
    }
}
