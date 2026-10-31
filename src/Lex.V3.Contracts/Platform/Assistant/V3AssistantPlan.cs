using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// An opaque reference a planner is given instead of a value (R33: only the question and opaque references leave the
/// server): a date or range the parser read (<c>d1</c>, <c>r1.from</c>, <c>r1.to</c>), or a context chip carried from the
/// last answer (<c>c1</c>, <c>c2</c>, whose values the planner never sees).
/// </summary>
/// <param name="Kind">date, range_from, range_to, context_work or context_date.</param>
/// <param name="Start">Where the reference is in the question, or -1 for a context chip.</param>
public sealed record V3PlannerReference(string Ref, string Kind, int Start, int End);

/// <summary>A typed context chip a follow-up question carries from the last trace (R45(e)): a work or a date, removable.</summary>
/// <param name="Kind">work or date.</param>
public sealed record V3ContextChip(string Kind, string Value);

/// <summary>
/// Everything a planner is given, and nothing else exists on it: the masked question, the answer locale, the opaque
/// references, the operations it may plan and the search languages it may choose (R33). There is no address, user agent,
/// cookie, history or earlier question here, and no way to add one.
/// </summary>
public sealed class V3PlannerInput
{
    public V3PlannerInput(
        string maskedQuestion,
        V3AnswerLocale answerLocale,
        IReadOnlyList<V3PlannerReference> references,
        IReadOnlyList<string> searchLanguages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(maskedQuestion);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(searchLanguages);
        MaskedQuestion = maskedQuestion;
        AnswerLocale = answerLocale;
        References = references.ToList().AsReadOnly();
        SearchLanguages = searchLanguages.ToList().AsReadOnly();
    }

    public string MaskedQuestion { get; }

    public V3AnswerLocale AnswerLocale { get; }

    public ReadOnlyCollection<V3PlannerReference> References { get; }

    public ReadOnlyCollection<string> SearchLanguages { get; }

    /// <summary>The operations a plan may name, in the order the grammar lists them.</summary>
    public ReadOnlyCollection<string> Operations { get; } = V3AssistantPlanValidator.AllowedOperations;
}

/// <summary>What a planner answers: the plan as JSON text, untrusted, and what it reports the call cost (for the budget, R40).</summary>
public sealed record V3PlannerOutput(string PlanJson, long CostMicroEuros);

/// <summary>
/// The one model seam (R31): a planner proposes a split, a verdict per part and a typed plan over opaque references, and
/// nothing else. Code validates the plan, runs it through the existing operations and decides the verdict. The one
/// implementation in src is the Azure OpenAI planner (Lex.V3.Api), wired only when the environment names its deployment and
/// the switch is on (R30, R33); the tests implement it deterministically.
/// </summary>
public interface IAssistantPlanner
{
    /// <summary>The planner's identity, written into every trace.</summary>
    string PlannerId { get; }

    V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken);
}

/// <summary>A span [start, end) of the question.</summary>
public sealed record V3PlanSpan(int Start, int End);

/// <summary>Where one argument's value comes from. Exactly one source; never a free-text value.</summary>
public sealed record V3ArgumentSource(
    V3ArgumentSourceKind Kind,
    V3PlanSpan? Span = null,
    string? Ref = null,
    string? Step = null,
    V3ResultPick? Pick = null,
    string? Choice = null);

/// <summary>One part of the question (at most two, R31's split) and the verdict the planner proposes for it.</summary>
public sealed record V3PlanPart(string PartId, V3PlanSpan Span, V3AssistantVerdict ProposedVerdict);

/// <summary>One operation of the plan. A follow-up names the step it follows and the typed condition that fires it (R35).</summary>
public sealed record V3PlanStep(
    string StepId,
    string PartId,
    string OperationId,
    IReadOnlyDictionary<string, V3ArgumentSource> Arguments,
    string? After = null,
    V3FollowUpCondition? Condition = null)
{
    public bool IsFollowUp => After is not null;
}

/// <summary>
/// A plan the validator admitted (<c>lex-v3-assistant-plan/1</c>): typed, bound, within the caps. It has one door,
/// <see cref="V3AssistantPlanValidator.Validate"/>, and carries its canonical bytes, which the trace records and a replay
/// hands back.
/// </summary>
public sealed class V3AssistantPlan
{
    private V3AssistantPlan(
        ReadOnlyCollection<V3PlanPart> parts,
        ReadOnlyCollection<V3PlanStep> steps,
        ReadOnlyCollection<V3PlanStep> followUps,
        byte[] canonicalUtf8)
    {
        Parts = parts;
        Steps = steps;
        FollowUps = followUps;
        _canonical = canonicalUtf8;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalUtf8));
    }

    private readonly byte[] _canonical;

    public ReadOnlyCollection<V3PlanPart> Parts { get; }

    public ReadOnlyCollection<V3PlanStep> Steps { get; }

    public ReadOnlyCollection<V3PlanStep> FollowUps { get; }

    public string Sha256 { get; }

    public byte[] CanonicalUtf8 => _canonical.ToArray();

    public string CanonicalJson => Encoding.UTF8.GetString(_canonical);

    internal static V3AssistantPlan Create(IReadOnlyList<V3PlanPart> parts, IReadOnlyList<V3PlanStep> steps, IReadOnlyList<V3PlanStep> followUps) =>
        new(parts.ToList().AsReadOnly(), steps.ToList().AsReadOnly(), followUps.ToList().AsReadOnly(), WriteCanonical(parts, steps, followUps));

    private static byte[] WriteCanonical(IReadOnlyList<V3PlanPart> parts, IReadOnlyList<V3PlanStep> steps, IReadOnlyList<V3PlanStep> followUps)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", V3AssistantPlanValidator.Schema);
            writer.WriteStartArray("parts");
            foreach (var part in parts)
            {
                writer.WriteStartObject();
                writer.WriteString("part_id", part.PartId);
                WriteSpan(writer, "span", part.Span);
                writer.WriteString("proposed_verdict", ContractWire.NameOf(part.ProposedVerdict));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("steps");
            foreach (var step in steps)
            {
                WriteStep(writer, step);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("follow_ups");
            foreach (var step in followUps)
            {
                WriteStep(writer, step);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteStep(Utf8JsonWriter writer, V3PlanStep step)
    {
        writer.WriteStartObject();
        writer.WriteString("step_id", step.StepId);
        if (step.IsFollowUp)
        {
            writer.WriteString("after", step.After);
            writer.WriteString("condition", ContractWire.NameOf(step.Condition!.Value));
        }
        else
        {
            writer.WriteString("part_id", step.PartId);
        }

        writer.WriteString("operation_id", step.OperationId);
        writer.WriteStartObject("arguments");
        foreach (var (name, source) in step.Arguments.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(name);
            switch (source.Kind)
            {
                case V3ArgumentSourceKind.Span:
                    WriteSpan(writer, "span", source.Span!);
                    break;
                case V3ArgumentSourceKind.Reference:
                    writer.WriteString("ref", source.Ref);
                    break;
                case V3ArgumentSourceKind.Result:
                    writer.WriteString("result", source.Step);
                    writer.WriteString("pick", ContractWire.NameOf(source.Pick!.Value));
                    break;
                case V3ArgumentSourceKind.Choice:
                    writer.WriteString("choice", source.Choice);
                    break;
                case V3ArgumentSourceKind.Today:
                    writer.WriteBoolean("today", true);
                    break;
                default:
                    throw new InvalidOperationException("Not an argument source kind.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteSpan(Utf8JsonWriter writer, string name, V3PlanSpan span)
    {
        writer.WriteStartArray(name);
        writer.WriteNumberValue(span.Start);
        writer.WriteNumberValue(span.End);
        writer.WriteEndArray();
    }
}

/// <summary>What the validator decided: a plan, or the reason it was refused (never both).</summary>
public sealed record V3PlanValidation(V3AssistantPlan? Plan, V3PlanRefusal? Refusal, string? Detail)
{
    public bool Admitted => Plan is not null;
}

/// <summary>
/// What a plan is checked against: the question's intake, the references the parsers produced, the context chips and the
/// search languages the mount holds.
/// </summary>
public sealed record V3PlanContext(
    V3QuestionIntake Intake,
    IReadOnlyList<V3ParsedDate> Dates,
    IReadOnlyList<V3ParsedRange> Ranges,
    IReadOnlyList<V3ContextChip> Context,
    IReadOnlyList<string> SearchLanguages)
{
    /// <summary>The opaque references a planner is given for this question, in a fixed order.</summary>
    public IReadOnlyList<V3PlannerReference> References()
    {
        var references = Dates.Select(static date => new V3PlannerReference(date.Ref, "date", date.Start, date.End)).ToList();
        foreach (var range in Ranges)
        {
            references.Add(new V3PlannerReference(range.Ref + ".from", "range_from", range.Start, range.End));
            references.Add(new V3PlannerReference(range.Ref + ".to", "range_to", range.Start, range.End));
        }

        for (var at = 0; at < Context.Count; at++)
        {
            references.Add(new V3PlannerReference(
                "c" + (at + 1).ToString(CultureInfo.InvariantCulture),
                Context[at].Kind == "work" ? "context_work" : "context_date",
                -1,
                -1));
        }

        return references;
    }

    /// <summary>The value an opaque reference names, or null when the reference does not exist or is of another kind.</summary>
    public string? Value(string reference, string kind)
    {
        var match = References().SingleOrDefault(candidate => string.Equals(candidate.Ref, reference, StringComparison.Ordinal));
        if (match is null || !string.Equals(match.Kind, kind, StringComparison.Ordinal))
        {
            return null;
        }

        return kind switch
        {
            "date" => Dates.Single(date => date.Ref == reference).Iso,
            "range_from" => Ranges.Single(range => range.Ref + ".from" == reference).From,
            "range_to" => Ranges.Single(range => range.Ref + ".to" == reference).To,
            "context_work" or "context_date" => Context[int.Parse(reference[1..], NumberStyles.None, CultureInfo.InvariantCulture) - 1].Value,
            _ => null,
        };
    }
}

/// <summary>
/// The validator (R31: code validates and decides; R45(f): question text is untrusted). It refuses any planner output that is
/// not a typed, bound plan within the caps, naming why; it never repairs one. docs/ANSWER-DOSSIER-1.md §4 is its grammar.
/// </summary>
public static class V3AssistantPlanValidator
{
    public const string Schema = "lex-v3-assistant-plan/1";

    /// <summary>The largest planner output read, in UTF-8 bytes.</summary>
    public const int MaximumPlanBytes = 16 * 1024;

    public const int MaximumParts = 2;
    public const int MaximumSteps = 4;

    /// <summary>R35: a frozen plan may declare at most two follow-up operations.</summary>
    public const int MaximumFollowUps = 2;

    public const int MaximumOperations = 6;
    public const int MaximumQueryLength = 400;
    public const int MaximumIdentifierLength = 300;

    /// <summary>The widest span of dates a diff may compare (R45(f)), in days: ten years.</summary>
    public const int MaximumDiffDays = 3653;

    /// <summary>The widest period changes_in_period may list (R45(f)), in days.</summary>
    public const int MaximumChangesDays = 366;

    private static readonly V3ArgumentSourceKind[] SpanOnly = [V3ArgumentSourceKind.Span];
    private static readonly V3ArgumentSourceKind[] IdentifierSources = [V3ArgumentSourceKind.Span, V3ArgumentSourceKind.Reference, V3ArgumentSourceKind.Result];
    private static readonly V3ArgumentSourceKind[] DateSources = [V3ArgumentSourceKind.Reference, V3ArgumentSourceKind.Today];
    private static readonly V3ArgumentSourceKind[] RangeSources = [V3ArgumentSourceKind.Reference];
    private static readonly V3ArgumentSourceKind[] ResultOnly = [V3ArgumentSourceKind.Result];
    private static readonly V3ArgumentSourceKind[] ChoiceOnly = [V3ArgumentSourceKind.Choice];

    /// <summary>
    /// The grammar: each operation a plan may name (all of them existing, read-only operations of the registry), the
    /// parameters it must bind, and the sources each parameter admits.
    /// </summary>
    private static readonly Dictionary<string, (string[] Required, Dictionary<string, V3ArgumentSourceKind[]> Admitted)> Grammar = new(StringComparer.Ordinal)
    {
        ["resolve"] = (["identifier"], new(StringComparer.Ordinal) { ["identifier"] = [V3ArgumentSourceKind.Span, V3ArgumentSourceKind.Reference] }),
        ["search"] = (["query", "language"], new(StringComparer.Ordinal) { ["query"] = SpanOnly, ["language"] = ChoiceOnly, ["date"] = DateSources }),
        ["dossier"] = (["identifier"], new(StringComparer.Ordinal) { ["identifier"] = IdentifierSources }),
        ["timeline"] = (["identifier"], new(StringComparer.Ordinal) { ["identifier"] = IdentifierSources }),
        ["as_of"] = (["identifier", "date"], new(StringComparer.Ordinal) { ["identifier"] = IdentifierSources, ["date"] = DateSources }),
        ["evidence_bundle"] = (["identifier", "date"], new(StringComparer.Ordinal) { ["identifier"] = IdentifierSources, ["date"] = DateSources, ["article"] = ResultOnly }),
        ["article_history"] = (["identifier", "anchor"], new(StringComparer.Ordinal) { ["identifier"] = IdentifierSources, ["anchor"] = ResultOnly }),
        ["diff"] = (["identifier", "date_from", "date_to"], new(StringComparer.Ordinal) { ["identifier"] = IdentifierSources, ["date_from"] = RangeSources, ["date_to"] = RangeSources }),
        ["changes_in_period"] = (["date_from", "date_to"], new(StringComparer.Ordinal) { ["date_from"] = RangeSources, ["date_to"] = RangeSources }),
    };

    /// <summary>The operations a follow-up may name: each reads one work the earlier step named.</summary>
    private static readonly HashSet<string> FollowUpOperations = new(StringComparer.Ordinal) { "dossier", "timeline", "as_of", "evidence_bundle", "article_history" };

    /// <summary>The operations a plan may name, in the grammar's order.</summary>
    public static ReadOnlyCollection<string> AllowedOperations { get; } = Grammar.Keys.ToList().AsReadOnly();

    /// <summary>The parameters an operation must bind in a plan.</summary>
    public static IReadOnlyList<string> RequiredParameters(string operationId) => Grammar[operationId].Required;

    /// <summary>The sources a parameter of an operation admits.</summary>
    public static IReadOnlyList<V3ArgumentSourceKind> AdmittedSources(string operationId, string parameter) => Grammar[operationId].Admitted[parameter];

    public static V3PlanValidation Validate(string planJson, V3PlanContext context)
    {
        ArgumentNullException.ThrowIfNull(planJson);
        ArgumentNullException.ThrowIfNull(context);
        if (Encoding.UTF8.GetByteCount(planJson) > MaximumPlanBytes)
        {
            return Refuse(V3PlanRefusal.TooLarge, "the planner's output is larger than 16 KiB");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(planJson, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            return Refuse(V3PlanRefusal.Malformed, "the planner's output is not one JSON document");
        }

        using (document)
        {
            try
            {
                return ValidateRoot(document.RootElement, context);
            }
            catch (PlanRefused refused)
            {
                return Refuse(refused.Refusal, refused.Message);
            }
        }
    }

    private static V3PlanValidation Refuse(V3PlanRefusal refusal, string detail) => new(null, refusal, detail);

    private sealed class PlanRefused(V3PlanRefusal refusal, string detail) : Exception(detail)
    {
        public V3PlanRefusal Refusal { get; } = refusal;
    }

    private static V3PlanValidation ValidateRoot(JsonElement root, V3PlanContext context)
    {
        Members(root, "the plan", ["schema", "parts", "steps", "follow_ups"], []);
        if (!string.Equals(String(root.GetProperty("schema"), "schema"), Schema, StringComparison.Ordinal))
        {
            throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "the plan is not lex-v3-assistant-plan/1");
        }

        var question = context.Intake.Text;
        var parts = new List<V3PlanPart>();
        foreach (var element in Array(root.GetProperty("parts"), "parts", 1, MaximumParts))
        {
            Members(element, "a part", ["part_id", "span", "proposed_verdict"], []);
            var id = String(element.GetProperty("part_id"), "part_id");
            if (!string.Equals(id, "p" + (parts.Count + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "parts are named p1 and p2, in order");
            }

            var span = PartSpan(element.GetProperty("span"), context);
            if (parts.Count > 0 && span.Start < parts[^1].Span.End)
            {
                throw new PlanRefused(V3PlanRefusal.SpanOutOfBounds, "the parts overlap or are out of order");
            }

            parts.Add(new V3PlanPart(id, span, Verdict(element.GetProperty("proposed_verdict"))));
        }

        var steps = new List<V3PlanStep>();
        foreach (var element in Array(root.GetProperty("steps"), "steps", 1, MaximumSteps))
        {
            Members(element, "a step", ["step_id", "part_id", "operation_id", "arguments"], []);
            var id = String(element.GetProperty("step_id"), "step_id");
            if (!string.Equals(id, "s" + (steps.Count + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "steps are named s1 to s4, in order");
            }

            var partId = String(element.GetProperty("part_id"), "part_id");
            var part = parts.SingleOrDefault(candidate => candidate.PartId == partId)
                ?? throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{id} names a part the plan does not hold");
            var operation = Operation(element.GetProperty("operation_id"));
            var arguments = Arguments(element.GetProperty("arguments"), operation, id, part, context, followUp: null, steps);
            steps.Add(new V3PlanStep(id, partId, operation, arguments));
        }

        var followUps = new List<V3PlanStep>();
        foreach (var element in Array(root.GetProperty("follow_ups"), "follow_ups", 0, MaximumFollowUps))
        {
            Members(element, "a follow-up", ["step_id", "after", "condition", "operation_id", "arguments"], []);
            var id = String(element.GetProperty("step_id"), "step_id");
            if (!string.Equals(id, "f" + (followUps.Count + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "follow-ups are named f1 and f2, in order");
            }

            var after = String(element.GetProperty("after"), "after");
            var earlier = steps.SingleOrDefault(step => step.StepId == after)
                ?? throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{id} follows a step the plan does not hold");
            var condition = Condition(element.GetProperty("condition"));
            var operation = Operation(element.GetProperty("operation_id"));
            if (!FollowUpOperations.Contains(operation))
            {
                throw new PlanRefused(V3PlanRefusal.OperationNotAllowed, $"a follow-up cannot run {operation}");
            }

            var part = parts.Single(candidate => candidate.PartId == earlier.PartId);
            var followUp = new V3PlanStep(id, earlier.PartId, operation, new Dictionary<string, V3ArgumentSource>(), after, condition);
            var arguments = Arguments(element.GetProperty("arguments"), operation, id, part, context, followUp, steps);
            followUps.Add(followUp with { Arguments = arguments });
        }

        if (steps.Count + followUps.Count > MaximumOperations)
        {
            throw new PlanRefused(V3PlanRefusal.CapExceeded, "a plan runs at most six operations");
        }

        return new V3PlanValidation(V3AssistantPlan.Create(parts, steps, followUps), null, null);
    }

    private static Dictionary<string, V3ArgumentSource> Arguments(
        JsonElement element,
        string operation,
        string stepId,
        V3PlanPart part,
        V3PlanContext context,
        V3PlanStep? followUp,
        IReadOnlyList<V3PlanStep> steps)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PlanRefused(V3PlanRefusal.Malformed, $"{stepId}'s arguments are not an object");
        }

        var (required, admitted) = Grammar[operation];
        var arguments = new Dictionary<string, V3ArgumentSource>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (!admitted.TryGetValue(member.Name, out var kinds))
            {
                throw new PlanRefused(V3PlanRefusal.UnknownMember, $"{operation} takes no argument '{member.Name}' in a plan");
            }

            var source = Source(member.Value, stepId);
            if (!kinds.Contains(source.Kind))
            {
                throw new PlanRefused(V3PlanRefusal.SourceNotAdmitted, $"{operation}'s '{member.Name}' does not take a {ContractWire.NameOf(source.Kind)} source");
            }

            CheckSource(source, member.Name, stepId, part, context, followUp);
            arguments[member.Name] = source;
        }

        var missing = required.Where(name => !arguments.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{stepId} leaves {string.Join(", ", missing)} unbound");
        }

        if (followUp is not null)
        {
            // R35: a follow-up adds information and never replaces the requested work, date or language.
            var earlier = steps.Single(step => step.StepId == followUp.After);
            if (arguments.TryGetValue("date", out var date) && earlier.Arguments.TryGetValue("date", out var earlierDate) && date != earlierDate)
            {
                throw new PlanRefused(V3PlanRefusal.FollowUpChangesRequest, $"{stepId} reads another date than {earlier.StepId}");
            }

            if (!arguments.TryGetValue("identifier", out var identifier) || identifier.Kind != V3ArgumentSourceKind.Result)
            {
                throw new PlanRefused(V3PlanRefusal.FollowUpChangesRequest, $"{stepId} must read the work {earlier.StepId} named");
            }
        }

        // R45(f): code caps the date ranges.
        if (arguments.TryGetValue("date_from", out var from) && arguments.TryGetValue("date_to", out var to))
        {
            var start = DateOnly.ParseExact(context.Value(from.Ref!, "range_from")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateOnly.ParseExact(context.Value(to.Ref!, "range_to")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(from.Ref![..from.Ref!.IndexOf('.', StringComparison.Ordinal)], to.Ref![..to.Ref!.IndexOf('.', StringComparison.Ordinal)], StringComparison.Ordinal))
            {
                throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{stepId} takes its two dates from two ranges");
            }

            var days = end.DayNumber - start.DayNumber;
            if (days > (operation == "diff" ? MaximumDiffDays : MaximumChangesDays))
            {
                throw new PlanRefused(V3PlanRefusal.CapExceeded, $"{stepId}'s range is wider than its cap");
            }
        }

        return arguments;
    }

    private static void CheckSource(
        V3ArgumentSource source,
        string parameter,
        string stepId,
        V3PlanPart part,
        V3PlanContext context,
        V3PlanStep? followUp)
    {
        switch (source.Kind)
        {
            case V3ArgumentSourceKind.Span:
                var span = source.Span!;
                if (span.Start < part.Span.Start || span.End > part.Span.End)
                {
                    throw new PlanRefused(V3PlanRefusal.SpanOutOfBounds, $"{stepId}'s {parameter} is not inside its part");
                }

                var limit = parameter == "query" ? MaximumQueryLength : MaximumIdentifierLength;
                if (span.End - span.Start > limit)
                {
                    throw new PlanRefused(V3PlanRefusal.CapExceeded, $"{stepId}'s {parameter} is longer than {limit.ToString(CultureInfo.InvariantCulture)} characters");
                }

                if (string.IsNullOrWhiteSpace(context.Intake.Text[span.Start..span.End]))
                {
                    throw new PlanRefused(V3PlanRefusal.SpanOutOfBounds, $"{stepId}'s {parameter} is blank");
                }

                // R33: an identifier the intake masked never becomes an argument, so it never reaches an operation.
                if (context.Intake.Touches(span.Start, span.End))
                {
                    throw new PlanRefused(V3PlanRefusal.SpanTouchesMask, $"{stepId}'s {parameter} touches a masked identifier");
                }

                break;
            case V3ArgumentSourceKind.Reference:
                var kind = parameter switch
                {
                    "date" => source.Ref!.StartsWith('c') ? "context_date" : "date",
                    "date_from" => "range_from",
                    "date_to" => "range_to",
                    "identifier" => "context_work",
                    _ => throw new PlanRefused(V3PlanRefusal.SourceNotAdmitted, $"{parameter} takes no reference"),
                };
                if (context.Value(source.Ref!, kind) is null)
                {
                    throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{stepId}'s {parameter} names the reference '{source.Ref}', which this question does not hold");
                }

                break;
            case V3ArgumentSourceKind.Result:
                if (followUp is null)
                {
                    throw new PlanRefused(V3PlanRefusal.SourceNotAdmitted, $"{stepId} reads an earlier result, which only a follow-up may (R35)");
                }

                if (!string.Equals(source.Step, followUp.After, StringComparison.Ordinal))
                {
                    throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{stepId} reads a result other than the step it follows");
                }

                var wanted = parameter == "identifier" ? V3ResultPick.SoleWork : V3ResultPick.SoleArticle;
                if (source.Pick != wanted)
                {
                    throw new PlanRefused(V3PlanRefusal.SourceNotAdmitted, $"{stepId}'s {parameter} takes the {ContractWire.NameOf(wanted)}");
                }

                if (wanted == V3ResultPick.SoleArticle && followUp.Condition != V3FollowUpCondition.OnSingleArticle)
                {
                    throw new PlanRefused(V3PlanRefusal.UnboundArgument, $"{stepId} reads an article on a condition that names none");
                }

                break;
            case V3ArgumentSourceKind.Choice:
                if (!context.SearchLanguages.Contains(source.Choice!, StringComparer.Ordinal) || !V3AnswerTemplates.LanguageCodes.Contains(source.Choice!, StringComparer.Ordinal))
                {
                    throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, $"{stepId}'s {parameter} is not a language this mount searches");
                }

                break;
            case V3ArgumentSourceKind.Today:
                break;
            default:
                throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "not an argument source");
        }
    }

    private static V3ArgumentSource Source(JsonElement element, string stepId)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PlanRefused(V3PlanRefusal.Malformed, $"{stepId}'s argument is not an object");
        }

        var names = element.EnumerateObject().Select(static member => member.Name).Order(StringComparer.Ordinal).ToArray();
        return names switch
        {
            ["span"] => new V3ArgumentSource(V3ArgumentSourceKind.Span, Span: RawSpan(element.GetProperty("span"))),
            ["ref"] => new V3ArgumentSource(V3ArgumentSourceKind.Reference, Ref: Token(element.GetProperty("ref"), "ref")),
            ["pick", "result"] => new V3ArgumentSource(V3ArgumentSourceKind.Result, Step: Token(element.GetProperty("result"), "result"), Pick: Pick(element.GetProperty("pick"))),
            ["choice"] => new V3ArgumentSource(V3ArgumentSourceKind.Choice, Choice: Token(element.GetProperty("choice"), "choice")),
            ["today"] when element.GetProperty("today").ValueKind == JsonValueKind.True => new V3ArgumentSource(V3ArgumentSourceKind.Today),
            _ => throw new PlanRefused(V3PlanRefusal.UnknownMember, $"{stepId}'s argument is not one of span, ref, result with pick, choice or today"),
        };
    }

    /// <summary>A part's span: inside the question. A part may hold a masked run; an argument's span may not.</summary>
    private static V3PlanSpan PartSpan(JsonElement element, V3PlanContext context)
    {
        var span = RawSpan(element);
        if (span.Start < 0 || span.End > context.Intake.Text.Length || span.Start >= span.End)
        {
            throw new PlanRefused(V3PlanRefusal.SpanOutOfBounds, "a part is not a span inside the question");
        }

        return span;
    }

    private static V3PlanSpan RawSpan(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2 ||
            element[0].ValueKind != JsonValueKind.Number || element[1].ValueKind != JsonValueKind.Number ||
            !element[0].TryGetInt32(out var start) || !element[1].TryGetInt32(out var end))
        {
            throw new PlanRefused(V3PlanRefusal.Malformed, "a span is [start, end], two whole numbers");
        }

        if (start < 0 || end < start)
        {
            throw new PlanRefused(V3PlanRefusal.SpanOutOfBounds, "a span's start is at least 0 and not after its end");
        }

        return new V3PlanSpan(start, end);
    }

    private static void Members(JsonElement element, string what, string[] required, string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PlanRefused(V3PlanRefusal.Malformed, $"{what} is not an object");
        }

        foreach (var member in element.EnumerateObject())
        {
            if (!required.Contains(member.Name, StringComparer.Ordinal) && !optional.Contains(member.Name, StringComparer.Ordinal))
            {
                throw new PlanRefused(V3PlanRefusal.UnknownMember, $"{what} carries '{member.Name}', which the grammar does not name");
            }
        }

        foreach (var name in required)
        {
            if (!element.TryGetProperty(name, out _))
            {
                throw new PlanRefused(V3PlanRefusal.Malformed, $"{what} lacks '{name}'");
            }
        }
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement element, string what, int minimum, int maximum)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new PlanRefused(V3PlanRefusal.Malformed, $"{what} is not an array");
        }

        var length = element.GetArrayLength();
        if (length < minimum || length > maximum)
        {
            throw new PlanRefused(V3PlanRefusal.CapExceeded, $"{what} holds {length.ToString(CultureInfo.InvariantCulture)}, outside {minimum.ToString(CultureInfo.InvariantCulture)} to {maximum.ToString(CultureInfo.InvariantCulture)}");
        }

        return element.EnumerateArray();
    }

    private static string String(JsonElement element, string what) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw new PlanRefused(V3PlanRefusal.Malformed, $"{what} is not a string");

    /// <summary>A short lowercase token: a model cannot smuggle prose through an id, a reference or a choice.</summary>
    private static string Token(JsonElement element, string what)
    {
        var value = String(element, what);
        if (value.Length is < 1 or > 16 || !value.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_'))
        {
            throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, $"{what} is not a token of the grammar");
        }

        return value;
    }

    private static string Operation(JsonElement element)
    {
        var operation = String(element, "operation_id");
        return Grammar.ContainsKey(operation)
            ? operation
            : throw new PlanRefused(V3PlanRefusal.OperationNotAllowed, "a plan names an operation outside its allowlist");
    }

    private static V3AssistantVerdict Verdict(JsonElement element) => String(element, "proposed_verdict") switch
    {
        "answer" => V3AssistantVerdict.Answer,
        "answer_with_enrichment" => V3AssistantVerdict.AnswerWithEnrichment,
        "point" => V3AssistantVerdict.Point,
        "clarify" => V3AssistantVerdict.Clarify,
        "refuse" => V3AssistantVerdict.Refuse,
        "split" => V3AssistantVerdict.Split,
        _ => throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "a proposed verdict is one of the six"),
    };

    private static V3FollowUpCondition Condition(JsonElement element) => String(element, "condition") switch
    {
        "on_single_work" => V3FollowUpCondition.OnSingleWork,
        "on_single_article" => V3FollowUpCondition.OnSingleArticle,
        _ => throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "a condition is on_single_work or on_single_article"),
    };

    private static V3ResultPick Pick(JsonElement element) => String(element, "pick") switch
    {
        "sole_work" => V3ResultPick.SoleWork,
        "sole_article" => V3ResultPick.SoleArticle,
        _ => throw new PlanRefused(V3PlanRefusal.OutsideClosedSet, "a pick is sole_work or sole_article"),
    };
}
