using System.Text.Json.Nodes;

namespace Lex.Ask;

/// <summary>
/// Converts untrusted planner arguments into the exact bounded arguments frozen in an
/// <see cref="OperationPlan"/>. MCP performs its own validation too; this earlier boundary keeps
/// invalid model output from becoming an attempted legal operation.
/// </summary>
internal static class OperationArguments
{
    private const int MaximumStringLength = 1_000;
    private const int MaximumWorkQueryLength = 900;
    private const int MaximumArticleNumberLength = 64;
    private const int MaximumAnchorLength = 512;
    private const int MaximumShortLength = 64;
    private const int MaximumLanguageLength = 16;
    private const int MaximumDateLength = 10;
    private const int MaximumListValues = 50;
    private const int MaximumOffset = 100_000;

    /// <summary>The shortest trimmed value this boundary accepts for any string argument; the
    /// planner schema emits it as <c>minLength</c>.</summary>
    public const int MinimumStringLength = 1;

    /// <summary>The clarification option bounds, emitted as <c>minItems</c>, <c>maxItems</c> and
    /// the item <c>maxLength</c>.</summary>
    public const int MinimumOptionCount = 2;

    public const int MaximumOptionCount = 4;

    public const int MaximumOptionLength = 100;

    /// <summary>The one date format this boundary parses, and the JSON Schema assertion that
    /// matches it structurally. <c>"format": "date"</c> is an annotation most planners ignore, so
    /// the pattern carries the constraint: four-digit year, zero-padded month 01-12, zero-padded
    /// day 01-31, ASCII hyphens, nothing else. Calendar validity (2026-02-30, 2026-02-29 in a
    /// non-leap year) is not expressible in a regex and stays with <see cref="Date"/>.</summary>
    public const string IsoDateFormat = "yyyy-MM-dd";

    public const string IsoDatePattern =
        @"^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> Allowed =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["search"] = Set("query", "publisher", "jurisdiction", "document_type",
                "source_class", "hierarchy", "act_form", "binding_status", "domain",
                "language", "retrieval_mode", "time_scope", "as_of", "fuzzy", "works", "limit"),
            ["as_of"] = Set("work", "work_query", "article_number", "date", "language",
                "mode", "anchors"),
            ["timeline"] = Set("work", "work_query", "limit", "offset"),
            ["in_force_on"] = Set("date", "publisher", "jurisdiction", "document_type",
                "source_class", "hierarchy", "act_form", "binding_status", "domain",
                "language", "limit", "offset"),
            ["diff"] = Set("work", "work_query", "article_number", "from_date", "to_date",
                "language", "anchor"),
            ["article_history"] = Set("work", "work_query", "article_number", "anchor", "language"),
            ["provenance"] = Set("lex_id", "work_query", "language"),
            ["coverage"] = Set("publisher"),
            ["cited_by"] = Set("work", "work_query", "limit"),
            ["changes_in_period"] = Set("from_date", "to_date", "publisher", "jurisdiction",
                "document_type", "source_class", "hierarchy", "act_form", "binding_status",
                "domain", "language", "order", "limit", "offset"),
            ["navigate"] = Set("work", "work_query", "article_number", "date", "language"),
            ["legal_boundary"] = Set("reason"),
            ["clarification"] = Set("question", "options"),
            ["gap"] = Set("reason"),
        };

    /// <summary>
    /// Arguments validated against a closed value set; the planner schema is generated from it,
    /// exactly as the argument names are generated from <see cref="Allowed"/>. Shown an open
    /// string, the model picks a plausible synonym ("current", "semantic", "churn") and the whole
    /// plan aborts on the first one.
    ///
    /// Global by name: each of these names belongs to exactly one operation (retrieval_mode,
    /// time_scope and fuzzy to search, mode to as_of, order to changes_in_period), which is why
    /// <see cref="Validate"/> may apply the whole table unconditionally. If a future operation
    /// reuses a name with different values, key this by (action, name) and make Validate switch
    /// on action in the same change.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedValues =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["retrieval_mode"] = ["keyword", "hybrid"],
            ["time_scope"] = ["all_versions", "as_of"],
            ["fuzzy"] = ["auto", "off"],
            ["mode"] = ["full", "outline", "select"],
            ["order"] = ["by_date", "by_churn"],
        };

    /// <summary>Conditional couplings the value set alone cannot express. Advertising a value
    /// makes the model likelier to pick it, and these two fail a second gate when picked
    /// alone.</summary>
    private static readonly IReadOnlyDictionary<string, string> ValueGuidance =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["time_scope"] = "all_versions (default) or as_of; as_of requires as_of=YYYY-MM-DD",
            ["mode"] = "full (default), outline or select; select requires anchors or article_number",
        };

    /// <summary>The arguments <see cref="Validate"/> demands outright, per action. Declaration
    /// order is the order they are checked in and the order the planner schema emits them, so it
    /// stays byte-identical between processes. Defaults injected by <see cref="ApplyDefaults"/>
    /// are deliberately absent: requiring them would refuse a plan this boundary completes.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Required =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["search"] = ["query"],
            ["as_of"] = ["date"],
            ["in_force_on"] = ["date"],
            ["diff"] = ["from_date", "to_date"],
            ["changes_in_period"] = ["from_date", "to_date"],
            ["clarification"] = ["question", "options"],
        };

    /// <summary>The "at least one of these" gates, per action. Every one of them is a plain
    /// per-action literal rather than a conditional, because the planner schema is generated per
    /// tool; each becomes an <c>anyOf</c> of one-name <c>required</c> clauses.</summary>
    private static readonly IReadOnlyDictionary<string, RequiredChoice[]> Choices =
        new Dictionary<string, RequiredChoice[]>(StringComparer.Ordinal)
        {
            ["navigate"] = [WorkIdentity("navigate")],
            ["as_of"] = [WorkIdentity("as_of")],
            ["diff"] = [WorkIdentity("diff")],
            ["timeline"] = [WorkIdentity("timeline")],
            ["cited_by"] = [WorkIdentity("cited_by")],
            ["provenance"] = [WorkIdentity("provenance")],
            ["article_history"] =
            [
                WorkIdentity("article_history"),
                new(["anchor", "article_number"],
                    "article_history requires an anchor or article_number."),
            ],
        };

    private static readonly HashSet<string> DateArguments =
        Set("date", "as_of", "from_date", "to_date");

    /// <summary>Every action this boundary accepts; the planner schema is generated from it.</summary>
    public static IEnumerable<string> Actions => Allowed.Keys;

    /// <summary>One "at least one of <paramref name="Names"/>" gate and the refusal it throws.</summary>
    public sealed record RequiredChoice(IReadOnlyList<string> Names, string Message);

    /// <summary>True when the argument is a JSON integer rather than a string.</summary>
    public static bool IsInteger(string name) => name is "limit" or "offset";

    /// <summary>True when the argument is parsed with <see cref="IsoDateFormat"/>.</summary>
    public static bool IsDate(string name) => DateArguments.Contains(name);

    /// <summary>The longest trimmed value <see cref="Normalize"/> accepts for one string
    /// argument; the planner schema emits it as <c>maxLength</c>. Read by both, so the two
    /// cannot drift.</summary>
    public static int MaximumLengthFor(string name) => name switch
    {
        "work_query" => MaximumWorkQueryLength,
        "article_number" => MaximumArticleNumberLength,
        "language" => MaximumLanguageLength,
        "date" or "as_of" or "from_date" or "to_date" => MaximumDateLength,
        "anchor" => MaximumAnchorLength,
        "publisher" or "jurisdiction" or "mode" or "retrieval_mode"
            or "time_scope" or "fuzzy" or "order" => MaximumShortLength,
        _ => MaximumStringLength,
    };

    /// <summary>The inclusive range <see cref="Normalize"/> accepts for one integer argument of
    /// one action; the planner schema emits it as <c>minimum</c> and <c>maximum</c>.</summary>
    public static (int Minimum, int Maximum) IntegerBoundsFor(string action, string name) =>
        name switch
        {
            "limit" => (1, action switch
            {
                "search" => 50,
                "in_force_on" or "cited_by" or "changes_in_period" => 100,
                _ => 200,
            }),
            "offset" => (0, MaximumOffset),
            _ => throw new InvalidDataException($"Argument '{name}' is not an integer."),
        };

    /// <summary>The arguments <see cref="Normalize"/> demands for one action; the planner schema
    /// emits them as <c>required</c>.</summary>
    public static IReadOnlyList<string> RequiredFor(string action) =>
        Required.TryGetValue(action, out var required) ? required : [];

    /// <summary>The "at least one of" gates for one action, narrowed to the names that action
    /// actually accepts: the work identity is work or work_query everywhere except provenance,
    /// which takes lex_id or work_query and has no work argument at all.</summary>
    public static IReadOnlyList<RequiredChoice> RequiredChoicesFor(string action)
    {
        if (!Allowed.TryGetValue(action, out var allowed))
            throw new InvalidDataException(
                $"Unknown legal operation or application action '{action}'.");
        return Choices.TryGetValue(action, out var choices)
            ? choices.Select(choice => choice with
            {
                Names = choice.Names.Where(allowed.Contains).ToArray(),
            }).ToArray()
            : [];
    }

    /// <summary>The exact values <see cref="Normalize"/> accepts for one argument, or null when
    /// the argument is not value-closed at this boundary.</summary>
    public static IReadOnlyList<string>? AllowedValuesFor(string name) =>
        AllowedValues.TryGetValue(name, out var values) ? values : null;

    /// <summary>Guidance the planner schema attaches to a value-closed argument, or null.</summary>
    public static string? GuidanceFor(string name) =>
        ValueGuidance.TryGetValue(name, out var guidance) ? guidance : null;

    /// <summary>The exact argument names <see cref="Normalize"/> accepts for one action.</summary>
    public static IReadOnlyCollection<string> AllowedFor(string action) =>
        Allowed.TryGetValue(action, out var allowed)
            ? allowed.ToArray()
            : throw new InvalidDataException(
                $"Unknown legal operation or application action '{action}'.");

    public static JsonObject Normalize(
        string action, JsonObject proposed, CorpusVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        vocabulary ??= CorpusVocabulary.Unconstrained;
        if (!Allowed.TryGetValue(action, out var allowed))
            throw new InvalidDataException($"Unknown legal operation or application action '{action}'.");
        var unexpected = proposed.Select(item => item.Key).Where(key => !allowed.Contains(key)).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidDataException(
                $"Operation '{action}' contains unsupported argument '{unexpected[0]}'.");

        var normalized = new JsonObject();
        foreach (var (name, value) in proposed)
        {
            if (value is null) continue;
            if (name == "options")
            {
                if (value is not JsonArray options
                    || options.Count < MinimumOptionCount || options.Count > MaximumOptionCount)
                    throw new InvalidDataException("Clarification options must contain two to four labels.");
                var bounded = new JsonArray();
                foreach (var item in options)
                {
                    if (item is not JsonValue option || !option.TryGetValue<string>(out var label))
                        throw new InvalidDataException("Every clarification option must be a string.");
                    label = RequiredString(label, "clarification option", MaximumOptionLength);
                    bounded.Add(label);
                }
                normalized[name] = bounded;
                continue;
            }
            if (name is "limit" or "offset")
            {
                if (value is not JsonValue number || !number.TryGetValue<int>(out var integer))
                    throw new InvalidDataException($"Argument '{name}' must be an integer.");
                normalized[name] = integer;
                continue;
            }
            if (value is not JsonValue textValue || !textValue.TryGetValue<string>(out var text))
                throw new InvalidDataException($"Argument '{name}' must be a string.");
            text = text.Trim();
            // The trim happens before the measurement, which JSON Schema cannot express, so
            // maxLength there is marginally stricter than this gate. Stricter is safe; looser is
            // the outage. See MaximumLengthFor, which the planner schema reads.
            var maximum = MaximumLengthFor(name);
            if (text.Length < MinimumStringLength || text.Length > maximum)
                throw new InvalidDataException(
                    $"Argument '{name}' must contain {MinimumStringLength} to {maximum} characters.");
            // "LU-Legilux" and "lu" name mounted things; the selectors behind MCP match publisher
            // ordinally, so the mounted spelling is restored before the plan is frozen. A value
            // nothing mounted matches is left alone on purpose (see CorpusVocabulary.Canonical).
            normalized[name] = vocabulary.Canonical(name, text) ?? text;
        }

        ApplyDefaults(action, normalized);
        Validate(action, normalized);
        return normalized;
    }

    private static void ApplyDefaults(string action, JsonObject arguments)
    {
        switch (action)
        {
            case "search":
                arguments["retrieval_mode"] ??= "keyword";
                arguments["fuzzy"] ??= "auto";
                arguments["limit"] ??= 10;
                break;
            case "as_of":
                arguments["mode"] ??= arguments["article_number"] is null ? "full" : "select";
                break;
            case "timeline":
                arguments["limit"] ??= 100;
                arguments["offset"] ??= 0;
                break;
            case "in_force_on":
                arguments["limit"] ??= 50;
                arguments["offset"] ??= 0;
                break;
            case "cited_by":
                arguments["limit"] ??= 50;
                break;
            case "changes_in_period":
                arguments["source_class"] ??= "!RECUEIL,!CODE_RECUEIL";
                arguments["order"] ??= "by_date";
                arguments["limit"] ??= 20;
                arguments["offset"] ??= 0;
                break;
        }
    }

    private static void Validate(string action, JsonObject arguments)
    {
        foreach (var choice in RequiredChoicesFor(action))
            if (!choice.Names.Any(name => Text(arguments, name) is not null))
                throw new InvalidDataException(choice.Message);
        foreach (var name in RequiredFor(action))
        {
            // A date argument is required and format-checked by the same call, which is why a
            // missing one is reported as "must be an ISO date" rather than "is required".
            if (IsDate(name)) Date(arguments, name);
            else if (name == "options")
            {
                if (arguments[name] is not JsonArray)
                    throw new InvalidDataException($"{action} requires bounded options.");
            }
            else Require(arguments, name);
        }
        if (action is "diff" or "changes_in_period")
        {
            var from = Date(arguments, "from_date");
            var to = Date(arguments, "to_date");
            if (from > to)
                throw new InvalidDataException($"{action} from_date must not follow to_date.");
        }

        foreach (var (name, values) in AllowedValues) Enum(arguments, name, values);
        // Both couplings are value-dependent rather than action-dependent, so no per-tool schema
        // can carry them; the ValueGuidance descriptions tell the model instead.
        if (Text(arguments, "time_scope") == "as_of") Date(arguments, "as_of");
        if (Text(arguments, "mode") == "select"
            && Text(arguments, "anchors") is null
            && Text(arguments, "article_number") is null)
            throw new InvalidDataException("as_of mode=select requires anchors.");

        CountList(arguments, "anchors", MaximumListValues, MaximumAnchorLength);
        CountList(arguments, "works", MaximumListValues);

        foreach (var name in new[] { "limit", "offset" })
        {
            var (minimum, maximum) = IntegerBoundsFor(action, name);
            Bound(arguments, name, minimum, maximum);
        }
    }

    private static RequiredChoice WorkIdentity(string action) =>
        new(["work", "work_query", "lex_id"],
            $"Operation '{action}' requires a work identity.");

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private static string RequiredString(string value, string name, int maximum)
    {
        value = value.Trim();
        if (value.Length < MinimumStringLength || value.Length > maximum)
            throw new InvalidDataException(
                $"{name} must contain {MinimumStringLength} to {maximum} characters.");
        return value;
    }

    private static string Require(JsonObject arguments, string name) =>
        Text(arguments, name) ?? throw new InvalidDataException($"Argument '{name}' is required.");

    private static string? Text(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static DateOnly Date(JsonObject arguments, string name) =>
        DateOnly.TryParseExact(Text(arguments, name), IsoDateFormat, out var date)
            ? date
            : throw new InvalidDataException($"Argument '{name}' must be an ISO date.");

    private static void Enum(JsonObject arguments, string name, params string[] allowed)
    {
        if (Text(arguments, name) is not { } value) return;
        if (!allowed.Contains(value, StringComparer.Ordinal))
            throw new InvalidDataException($"Argument '{name}' has an unsupported value.");
    }

    private static void Bound(JsonObject arguments, string name, int minimum, int maximum)
    {
        if (arguments[name] is not JsonValue value || !value.TryGetValue<int>(out var number)) return;
        if (number < minimum || number > maximum)
            throw new InvalidDataException(
                $"Argument '{name}' must be between {minimum} and {maximum}.");
    }

    private static void CountList(
        JsonObject arguments,
        string name,
        int maximum,
        int maximumItemLength = MaximumStringLength)
    {
        if (Text(arguments, name) is not { } value) return;
        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        if (items.Length is 0 || items.Length > maximum)
            throw new InvalidDataException(
                $"Argument '{name}' must contain 1 to {maximum} values.");
        if (items.Any(item => item.Length > maximumItemLength))
            throw new InvalidDataException(
                $"Every '{name}' value must contain at most {maximumItemLength} characters.");
    }
}
