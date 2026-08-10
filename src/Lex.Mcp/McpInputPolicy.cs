using System.Globalization;
using System.Text.Json.Nodes;

namespace Lex.Mcp;

internal static class McpInputPolicy
{
    internal const int MaximumAnchorLength = 512;
    private sealed record Rule(
        string[] Fields,
        string[] Required,
        IReadOnlyDictionary<string, (int Minimum, int Maximum)> Integers);

    private static readonly IReadOnlyDictionary<string, Rule> Rules =
        new Dictionary<string, Rule>(StringComparer.Ordinal)
        {
            ["as_of"] = R(["work", "date", "publisher", "language", "mode", "anchors"], ["work", "date"]),
            ["timeline"] = R(["work", "publisher", "limit", "offset"], ["work"],
                ("limit", 1, 200), ("offset", 0, 100_000)),
            ["in_force_on"] = R(
                ["date", "publisher", "jurisdiction", "document_type", "source_class",
                 "hierarchy", "act_form", "binding_status", "domain", "language", "limit", "offset"],
                ["date"], ("limit", 1, 100), ("offset", 0, 100_000)),
            ["diff"] = R(["work", "from_date", "to_date", "publisher", "language", "anchor"],
                ["work", "from_date", "to_date"]),
            ["search"] = R(
                ["query", "publisher", "jurisdiction", "document_type", "source_class",
                 "hierarchy", "act_form", "binding_status", "domain", "language",
                 "retrieval_mode", "time_scope", "as_of", "fuzzy", "works", "limit"],
                ["query"], ("limit", 1, 50)),
            ["article_history"] = R(["work", "publisher", "anchor", "language"], ["work", "anchor"]),
            ["provenance"] = R(["lex_id", "language"], ["lex_id"]),
            ["coverage"] = R(["publisher"], []),
            ["cited_by"] = R(["work", "limit"], ["work"], ("limit", 1, 100)),
            ["changes_in_period"] = R(
                ["from_date", "to_date", "publisher", "jurisdiction", "document_type",
                 "source_class", "hierarchy", "act_form", "binding_status", "domain",
                 "language", "order", "limit", "offset"],
                ["from_date", "to_date"], ("limit", 1, 100), ("offset", 0, 100_000)),
        };

    public static void Validate(string tool, JsonObject arguments)
    {
        if (!Rules.TryGetValue(tool, out var rule))
            throw new ArgumentException($"Unknown tool '{tool}'.", nameof(tool));
        if (arguments.Count > rule.Fields.Length)
            throw new ArgumentException($"Too many arguments for {tool}.", nameof(arguments));

        foreach (var (name, node) in arguments)
        {
            if (!rule.Fields.Contains(name, StringComparer.Ordinal))
                throw new ArgumentException($"Unsupported argument '{name}' for {tool}.", name);
            if (node is not JsonValue value)
                throw new ArgumentException($"{name} must be a scalar value.", name);
            if (rule.Integers.TryGetValue(name, out var bounds))
            {
                if (!value.TryGetValue<int>(out var integer))
                    throw new ArgumentException($"{name} must be an integer.", name);
                if (integer < bounds.Minimum || integer > bounds.Maximum)
                    throw new ArgumentOutOfRangeException(name,
                        $"{name} must be between {bounds.Minimum} and {bounds.Maximum}.");
                continue;
            }
            if (!value.TryGetValue<string>(out var text))
                throw new ArgumentException($"{name} must be a string.", name);
            var maximum = name is "language" ? 16
                : name is "date" or "as_of" or "from_date" or "to_date" ? 10
                : name is "anchor" ? MaximumAnchorLength
                : name is "publisher" or "jurisdiction" or "mode" or "retrieval_mode"
                    or "time_scope" or "fuzzy" or "order" ? 64
                : 1000;
            if (string.IsNullOrWhiteSpace(text) || text.Length > maximum)
                throw new ArgumentException($"{name} must contain 1 to {maximum} characters.", name);
        }

        foreach (var required in rule.Required)
            if (!arguments.ContainsKey(required))
                throw new ArgumentException($"{required} is required.", required);

        Date(arguments, "date");
        Date(arguments, "as_of");
        Date(arguments, "from_date");
        Date(arguments, "to_date");
        Enum(arguments, "mode", ["full", "outline", "select"]);
        Enum(arguments, "retrieval_mode", ["keyword", "hybrid"]);
        Enum(arguments, "time_scope", ["all_versions", "as_of"]);
        Enum(arguments, "fuzzy", ["auto", "off"]);
        Enum(arguments, "order", ["by_date", "by_churn"]);

        if (tool == "as_of"
            && String(arguments, "mode") == "select"
            && String(arguments, "anchors") is null)
            throw new ArgumentException("anchors is required when mode=select.", "anchors");
        if (tool == "as_of"
            && String(arguments, "anchors") is not null
            && String(arguments, "mode") is not ("outline" or "select"))
            throw new ArgumentException(
                "anchors is supported only when mode=outline or mode=select.", "anchors");
        if (tool == "search"
            && String(arguments, "time_scope") == "as_of"
            && String(arguments, "as_of") is null)
            throw new ArgumentException("as_of is required when time_scope=as_of.", "as_of");
        RequirePublisherAuthority(arguments, tool == "provenance" ? "lex_id" : "work");
        CountList(arguments, "anchors", 50, MaximumAnchorLength);
        CountList(arguments, "works", 50);
    }

    private static Rule R(string[] fields, string[] required,
        params (string Name, int Minimum, int Maximum)[] integers) =>
        new(fields, required, integers.ToDictionary(
            item => item.Name, item => (item.Minimum, item.Maximum), StringComparer.Ordinal));

    private static string? String(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void Date(JsonObject arguments, string name)
    {
        var value = String(arguments, name);
        if (value is null) return;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new ArgumentException($"{name} must be an ISO date (YYYY-MM-DD).", name);
    }

    private static void Enum(JsonObject arguments, string name, string[] allowed)
    {
        var value = String(arguments, name);
        if (value is not null && !allowed.Contains(value, StringComparer.Ordinal))
            throw new ArgumentException($"{name} must be one of: {string.Join(", ", allowed)}.", name);
    }

    private static void CountList(
        JsonObject arguments,
        string name,
        int maximum,
        int maximumItemLength = 1000)
    {
        var value = String(arguments, name);
        if (value is null) return;
        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        if (items.Length == 0)
            throw new ArgumentException($"{name} must contain at least one value.", name);
        if (items.Length > maximum)
            throw new ArgumentException($"{name} accepts at most {maximum} values.", name);
        if (items.Any(item => item.Length > maximumItemLength))
            throw new ArgumentException(
                $"Every {name} value must contain at most {maximumItemLength} characters.", name);
    }

    private static void RequirePublisherAuthority(JsonObject arguments, string name)
    {
        var work = String(arguments, name);
        if (work is null || arguments.ContainsKey("publisher")) return;
        if (work.Contains(':') && !work.Contains("://", StringComparison.Ordinal)) return;

        throw new ArgumentException(
            $"publisher is required when {name} is not a publisher-qualified lex_id.",
            "publisher");
    }
}
