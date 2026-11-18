using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One item of a bricks answer: a quoted extract of a step, and what it is in the answer.</summary>
/// <param name="Role">definition, rule, case, value, item, passage or nearest.</param>
public sealed record V4AnswerItem(string Step, int Extract, string Role);

/// <summary>A typed value of an answer: the exact words at a position of a step's article text.</summary>
public sealed record V4AnswerValue(string Step, int Offset, int Length);

/// <summary>A note of an answer on the question's own words.</summary>
/// <param name="Code">law_term (the reader's word, and the law's word for it) or word_not_in_act (a word of the question that is nowhere in the act).</param>
public sealed record V4AnswerNote(string Code, V4Span Span, string? Concept);

/// <summary>A row of the sweep an absence shows as its proof: a keyword of the dictionary and where the act holds it.</summary>
public sealed record V4RecordSweep(int Keyword, int Lines, int Articles);

/// <summary>What a detector found, as the plan lines show it.</summary>
public sealed record V4RecordDetector(string Id, int Lines, int Articles);

/// <summary>A concept or a role the question's words were read as.</summary>
public sealed record V4RecordConcept(string Concept, int Keyword, V4Span Span, string Source);

/// <summary>One pass of the loop (brick H): "Every iteration must produce an IterationRecord." [13]</summary>
public sealed record V4RecordPass(int Number, string Trigger, string Changed, string Outcome);

/// <summary>
/// The bricks record of a plan (<c>lex-v4-bricks/1</c>): how the question was read, which route code chose and why, what
/// retrieval found, what the answer is made of. It travels in the plan, so in the trace: "for question X, the parsed
/// structure was Y" [6C]. It holds tokens, numbers and positions only, never a sentence: no model's words can ride in it,
/// and the composer turns it into fixed wordings and quotations.
/// </summary>
public sealed class V4PlanRecord
{
    public const string Version = "lex-v4-bricks/1";

    /// <summary>The largest record read, in UTF-8 bytes.</summary>
    public const int MaximumBytes = 24 * 1024;

    /// <summary>The routes of the dispatcher (brick E).</summary>
    public static readonly IReadOnlyList<string> Routes =
        ["greeting", "own_case", "definition", "fact", "rule", "list", "open", "absence", "not_held", "clarify", "operations"];

    /// <summary>The kinds an answer can be of: the kinds of the dictionary, and the two ends that are not an answer of a kind.</summary>
    public static readonly IReadOnlyList<string> AnswerKinds =
        ["definition", "rule", "duration", "date", "amount", "list", "reference", "text", "candidates", "absence", "greeting", "not_held", "operations"];

    public static readonly IReadOnlyList<string> ItemRoles = ["definition", "rule", "case", "value", "item", "passage", "nearest"];

    /// <summary>The caveats an answer can carry: a closed list of codes, each with its fixed wording (ours: no model sentence is shown).</summary>
    public static readonly IReadOnlyList<string> Caveats =
        ["conditions_apply", "left_to_contract", "cases_from_sweep", "general_and_special", "model_unavailable", "not_defined", "vocabulary_incomplete", "several_by_scope", "list_from_sweep"];

    /// <summary>The ways an answer is "not found" (brick I, four in place of one).</summary>
    public static readonly IReadOnlyList<string> Absences = ["terms_nowhere", "words_not_found", "no_answer_in_lines", "act_not_held", "text_not_held"];

    private readonly JsonElement _raw;

    private V4PlanRecord(JsonElement raw)
    {
        _raw = raw.Clone();
    }

    /// <summary>The record as it was read, for the canonical bytes of the plan.</summary>
    public JsonElement Raw => _raw;

    public string Route { get; private init; } = "open";

    public string Kind { get; private init; } = "text";

    /// <summary>The kind of answer the question asked for (a kind of the dictionary), or empty when the route reads none.</summary>
    public string Asked { get; private init; } = string.Empty;

    /// <summary>The date the act was read on: "today", or the reference of a date the parser read in the question ("d1").</summary>
    public string DateRef { get; private init; } = "today";

    /// <summary>The act read, as its address on this site, or null when the route reads none.</summary>
    public string? Work { get; private init; }

    public int Lines { get; private init; }

    public int Articles { get; private init; }

    public IReadOnlyList<V4RecordConcept> Concepts { get; private init; } = [];

    public IReadOnlyList<V4RecordConcept> Roles { get; private init; } = [];

    /// <summary>Whether the question held a cue of R15 and was read as a question on the rule (decision 4(i)).</summary>
    public bool RuleQuestion { get; private init; }

    public IReadOnlyList<V4RecordDetector> Detectors { get; private init; } = [];

    public IReadOnlyList<V4RecordSweep> Sweep { get; private init; } = [];

    public IReadOnlyList<V4RecordPass> Passes { get; private init; } = [];

    /// <summary>The steps of the pipeline a model was called for (question, arbiter, extraction), in order; empty when none was.</summary>
    public IReadOnlyList<string> ModelSteps { get; private init; } = [];

    public bool Found { get; private init; }

    public bool Complete { get; private init; }

    public IReadOnlyList<V4AnswerItem> Items { get; private init; } = [];

    public IReadOnlyList<V4AnswerValue> Values { get; private init; } = [];

    public IReadOnlyList<string> AnswerCaveats { get; private init; } = [];

    public IReadOnlyList<V4AnswerNote> Notes { get; private init; } = [];

    /// <summary>Further articles of the act the answer offers to read, by their identifier.</summary>
    public IReadOnlyList<string> More { get; private init; } = [];

    /// <summary>For a defined term: on how many lines and in how many articles the act uses it; null otherwise.</summary>
    public (int Lines, int Articles)? Usage { get; private init; }

    /// <summary>The way the answer is "not found", or null.</summary>
    public string? Absence { get; private init; }

    /// <summary>The EU act of the corpus index the question names and Lex does not hold, or null.</summary>
    public string? EuAct { get; private init; }

    private static bool IsToken(string value) =>
        value.Length is >= 1 and <= 120 && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '#' or '.' or '/' or ':' or '+' or '-');

    private static bool IsName(string value) =>
        value.Length is >= 1 and <= 32 && value.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_');

    /// <summary>
    /// The structural rule every record meets before anything of it is read: objects, arrays, whole numbers, booleans and
    /// short tokens of letters, digits and a few signs. A string with a space in it is refused, so no sentence can be carried.
    /// </summary>
    private static bool TokensOnly(JsonElement element, int depth, ref int budget)
    {
        if (--budget < 0 || depth > 8)
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    if (!IsName(member.Name) || !TokensOnly(member.Value, depth + 1, ref budget))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (!TokensOnly(item, depth + 1, ref budget))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.String:
                return IsToken(element.GetString()!);
            case JsonValueKind.Number:
                return element.TryGetInt32(out var number) && number is >= 0 and <= 100_000_000;
            case JsonValueKind.True or JsonValueKind.False:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads a record, or answers null with why it is refused. The record is untrusted (a replay hands one in): its form is
    /// checked first, then every value the composer reads is checked against its closed list, the question's bounds and the
    /// plan's own steps.
    /// </summary>
    public static V4PlanRecord? Read(JsonElement element, V3PlanContext context, IReadOnlyList<V3PlanStep> steps, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(steps);
        refusal = null;
        var budget = 6000;
        if (element.ValueKind != JsonValueKind.Object || element.GetRawText().Length > MaximumBytes || !TokensOnly(element, 0, ref budget))
        {
            refusal = "the bricks record is not an object of tokens within its size";
            return null;
        }

        try
        {
            if (Text(element, "version") != Version)
            {
                refusal = "the bricks record is not lex-v4-bricks/1";
                return null;
            }

            if (Text(element, "dictionary") != V4Dictionary.Insurance.Sha256 || Text(element, "corpus_index") != V4CorpusIndex.Insurance.Sha256)
            {
                refusal = "the bricks record was made with another dictionary or corpus index than this build's";
                return null;
            }

            var route = Text(element, "route") ?? string.Empty;
            var answer = element.GetProperty("answer");
            var kind = Text(answer, "kind") ?? string.Empty;
            if (!Routes.Contains(route) || !AnswerKinds.Contains(kind))
            {
                refusal = "the bricks record names a route or a kind of answer outside the closed lists";
                return null;
            }

            var asked = Text(element, "kind") ?? string.Empty;
            var dateRef = Text(element, "date") ?? "today";
            if ((asked.Length > 0 && !V4Dictionary.KindOrder.Contains(asked)) || (dateRef != "today" && context.Value(dateRef, "date") is null))
            {
                refusal = "the bricks record names a kind of answer the dictionary does not hold, or a date the question does not write";
                return null;
            }

            if (steps.Sum(static step => step.Extracts?.Count ?? 0) > V3AssistantPipeline.MaximumQuotes)
            {
                refusal = "the plan quotes more extracts than an answer carries quotations";
                return null;
            }

            var question = context.Intake.Text;
            V4Span Span(JsonElement span)
            {
                var start = span[0].GetInt32();
                var end = span[1].GetInt32();
                if (span.GetArrayLength() != 2 || start < 0 || end > question.Length || start >= end || context.Intake.Touches(start, end))
                {
                    throw new InvalidDataException("a span of the bricks record is not a span of the question off its masks");
                }

                return new V4Span(start, end);
            }

            List<V4RecordConcept> ConceptsOf(string name, bool roles) => ArrayOf(element, name).Select(item =>
            {
                var concept = V4Dictionary.Insurance.Concept(Text(item, "id") ?? string.Empty);
                var keyword = V4Dictionary.Insurance.Keyword(item.GetProperty("keyword").GetInt32());
                if (concept is null || keyword is null || keyword.Concept != concept.Id || (concept.Kind == "role") != roles)
                {
                    throw new InvalidDataException("the bricks record names a concept or a keyword the dictionary does not hold");
                }

                return new V4RecordConcept(concept.Id, keyword.Row, Span(item.GetProperty("span")), Text(item, "source") ?? "dictionary");
            }).ToList();

            var items = ArrayOf(answer, "items").Select(item =>
            {
                var step = steps.FirstOrDefault(candidate => candidate.StepId == Text(item, "step"));
                var extract = item.GetProperty("extract").GetInt32();
                var role = Text(item, "role") ?? string.Empty;
                if (step?.Extracts is null || extract >= step.Extracts.Count || !ItemRoles.Contains(role))
                {
                    throw new InvalidDataException("an item of the bricks record names no extract of the plan, or a role outside the closed list");
                }

                return new V4AnswerItem(step.StepId, extract, role);
            }).ToList();

            var values = ArrayOf(answer, "values").Select(item =>
            {
                var step = steps.FirstOrDefault(candidate => candidate.StepId == Text(item, "step"));
                var offset = item.GetProperty("offset").GetInt32();
                var length = item.GetProperty("length").GetInt32();
                if (step?.Extracts is null || length is < 1 or > 80 || !step.Extracts.Any(extract => offset >= extract.Offset && offset + length <= extract.Offset + extract.Length))
                {
                    throw new InvalidDataException("a value of the bricks record is not inside a quoted extract");
                }

                return new V4AnswerValue(step.StepId, offset, length);
            }).ToList();

            var caveats = ArrayOf(answer, "caveats").Select(static item => item.GetString()!).ToList();
            var absence = Text(answer, "absence");
            if (caveats.Any(caveat => !Caveats.Contains(caveat)) || (absence is not null && !Absences.Contains(absence)))
            {
                refusal = "the bricks record names a caveat or an absence outside the closed lists";
                return null;
            }

            var notes = ArrayOf(answer, "notes").Select(item =>
            {
                var code = Text(item, "code");
                var concept = Text(item, "concept");
                if (code is not ("law_term" or "word_not_in_act") || (code == "law_term" && V4Dictionary.Insurance.Concept(concept ?? string.Empty) is null))
                {
                    throw new InvalidDataException("a note of the bricks record is outside the closed list");
                }

                return new V4AnswerNote(code, Span(item.GetProperty("span")), concept);
            }).ToList();

            var euAct = Text(answer, "eu_act");
            if (euAct is not null && V4CorpusIndex.Insurance.EuAct(euAct) is null)
            {
                refusal = "the bricks record names an EU act the corpus index does not hold";
                return null;
            }

            var sweep = ArrayOf(element, "sweep").Select(static item =>
            {
                var keyword = item.GetProperty("keyword").GetInt32();
                if (V4Dictionary.Insurance.Keyword(keyword) is null)
                {
                    throw new InvalidDataException("the sweep names a keyword the dictionary does not hold");
                }

                return new V4RecordSweep(keyword, item.GetProperty("lines").GetInt32(), item.GetProperty("articles").GetInt32());
            }).ToList();

            var more = ArrayOf(answer, "more").Select(static item => item.GetString()!).ToList();
            var work = Text(element, "work");
            if (more.Count > V3AssistantPipeline.MaximumCandidates || more.Any(static article => !V3AssistantPlanValidator.IsArticleId(article)) ||
                (work is not null && !V3AssistantPlanValidator.IsWorkAddress(work)) || (more.Count > 0 && work is null))
            {
                refusal = "the bricks record offers articles that are not article identifiers of a named act";
                return null;
            }

            return new V4PlanRecord(element)
            {
                Route = route,
                Kind = kind,
                Asked = asked,
                DateRef = dateRef,
                Work = work,
                Lines = Number(element, "lines"),
                Articles = Number(element, "articles"),
                Concepts = ConceptsOf("concepts", roles: false),
                Roles = ConceptsOf("roles", roles: true),
                RuleQuestion = element.TryGetProperty("rule_question", out var rule) && rule.ValueKind == JsonValueKind.True,
                Detectors = ArrayOf(element, "detectors").Select(static item => new V4RecordDetector(Text(item, "id") ?? string.Empty, Number(item, "lines"), Number(item, "articles"))).ToList(),
                Sweep = sweep,
                Passes = ArrayOf(element, "passes").Select(static item => new V4RecordPass(Number(item, "n"), Text(item, "trigger") ?? string.Empty, Text(item, "changed") ?? string.Empty, Text(item, "outcome") ?? string.Empty)).ToList(),
                ModelSteps = ArrayOf(element, "model_calls").Select(static item => Text(item, "step") ?? string.Empty).ToList(),
                Found = answer.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.True,
                Complete = answer.TryGetProperty("complete", out var complete) && complete.ValueKind == JsonValueKind.True,
                Items = items,
                Values = values,
                AnswerCaveats = caveats,
                Notes = notes,
                More = more,
                Usage = answer.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object ? (Number(usage, "lines"), Number(usage, "articles")) : null,
                Absence = absence,
                EuAct = euAct,
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            refusal = exception is InvalidDataException ? exception.Message : "the bricks record lacks a member or holds one of another type";
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static JsonElement[] ArrayOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
}
