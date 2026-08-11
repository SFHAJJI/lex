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

    /// <summary>Every action this boundary accepts; the planner schema is generated from it.</summary>
    public static IEnumerable<string> Actions => Allowed.Keys;

    /// <summary>The exact argument names <see cref="Normalize"/> accepts for one action.</summary>
    public static IReadOnlyCollection<string> AllowedFor(string action) =>
        Allowed.TryGetValue(action, out var allowed)
            ? allowed.ToArray()
            : throw new InvalidDataException(
                $"Unknown legal operation or application action '{action}'.");

    public static JsonObject Normalize(string action, JsonObject proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
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
                if (value is not JsonArray options || options.Count is < 2 or > 4)
                    throw new InvalidDataException("Clarification options must contain two to four labels.");
                var bounded = new JsonArray();
                foreach (var item in options)
                {
                    if (item is not JsonValue option || !option.TryGetValue<string>(out var label))
                        throw new InvalidDataException("Every clarification option must be a string.");
                    label = RequiredString(label, "clarification option", 100);
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
            var maximum = name switch
            {
                "work_query" => MaximumWorkQueryLength,
                "article_number" => MaximumArticleNumberLength,
                "language" => 16,
                "date" or "as_of" or "from_date" or "to_date" => 10,
                "anchor" => MaximumAnchorLength,
                "publisher" or "jurisdiction" or "mode" or "retrieval_mode"
                    or "time_scope" or "fuzzy" or "order" => 64,
                _ => MaximumStringLength,
            };
            if (text.Length == 0 || text.Length > maximum)
                throw new InvalidDataException(
                    $"Argument '{name}' must contain 1 to {maximum} characters.");
            normalized[name] = text;
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
        var hasWork = Text(arguments, "work") is not null
            || Text(arguments, "work_query") is not null
            || Text(arguments, "lex_id") is not null;
        if ((action is "navigate" or "as_of" or "diff" or "timeline" or "article_history"
                or "cited_by" or "provenance") && !hasWork)
            throw new InvalidDataException($"Operation '{action}' requires a work identity.");
        if (action == "search") Require(arguments, "query");
        if (action == "as_of") Date(arguments, "date");
        if (action == "diff")
        {
            var from = Date(arguments, "from_date");
            var to = Date(arguments, "to_date");
            if (from > to) throw new InvalidDataException("diff from_date must not follow to_date.");
        }
        if (action == "in_force_on") Date(arguments, "date");
        if (action == "changes_in_period")
        {
            var from = Date(arguments, "from_date");
            var to = Date(arguments, "to_date");
            if (from > to)
                throw new InvalidDataException("changes_in_period from_date must not follow to_date.");
        }
        if (action == "article_history"
            && Text(arguments, "anchor") is null && Text(arguments, "article_number") is null)
            throw new InvalidDataException("article_history requires an anchor or article_number.");
        if (action == "clarification")
        {
            Require(arguments, "question");
            if (arguments["options"] is not JsonArray)
                throw new InvalidDataException("clarification requires bounded options.");
        }

        Enum(arguments, "retrieval_mode", "keyword", "hybrid");
        Enum(arguments, "fuzzy", "auto", "off");
        Enum(arguments, "time_scope", "all_versions", "as_of");
        Enum(arguments, "mode", "full", "outline", "select");
        Enum(arguments, "order", "by_date", "by_churn");
        if (Text(arguments, "time_scope") == "as_of") Date(arguments, "as_of");
        if (Text(arguments, "mode") == "select"
            && Text(arguments, "anchors") is null
            && Text(arguments, "article_number") is null)
            throw new InvalidDataException("as_of mode=select requires anchors.");

        CountList(arguments, "anchors", 50, MaximumAnchorLength);
        CountList(arguments, "works", 50);

        var maximumLimit = action switch
        {
            "search" => 50,
            "in_force_on" or "cited_by" or "changes_in_period" => 100,
            _ => 200,
        };
        Bound(arguments, "limit", 1, maximumLimit);
        Bound(arguments, "offset", 0, 100_000);
    }

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private static string RequiredString(string value, string name, int maximum)
    {
        value = value.Trim();
        if (value.Length is 0 || value.Length > maximum)
            throw new InvalidDataException($"{name} must contain 1 to {maximum} characters.");
        return value;
    }

    private static string Require(JsonObject arguments, string name) =>
        Text(arguments, name) ?? throw new InvalidDataException($"Argument '{name}' is required.");

    private static string? Text(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static DateOnly Date(JsonObject arguments, string name) =>
        DateOnly.TryParseExact(Text(arguments, name), "yyyy-MM-dd", out var date)
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
