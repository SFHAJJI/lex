using System.Globalization;
using System.Text.Json.Nodes;

namespace Lex.Mcp;

internal static class McpInputPolicy
{
    internal const int MaximumAnchorLength = LegalOperationCatalog.MaximumAnchorLength;

    public static void Validate(string tool, JsonObject arguments)
    {
        var operation = LegalOperationCatalog.TryGet(tool)
            ?? throw new ArgumentException($"Unknown tool '{tool}'.", nameof(tool));
        var allowed = operation.McpArguments.ToDictionary(
            argument => argument.Name, StringComparer.Ordinal);
        if (arguments.Count > allowed.Count)
            throw new ArgumentException($"Too many arguments for {tool}.", nameof(arguments));

        foreach (var (name, node) in arguments)
        {
            if (!allowed.TryGetValue(name, out var rule))
                throw new ArgumentException($"Unsupported argument '{name}' for {tool}.", name);
            if (node is not JsonValue value)
                throw new ArgumentException($"{name} must be a scalar value.", name);
            if (rule.Kind == LegalArgumentKind.Integer)
            {
                if (!value.TryGetValue<int>(out var integer))
                    throw new ArgumentException($"{name} must be an integer.", name);
                if (integer < rule.Minimum || integer > rule.Maximum)
                    throw new ArgumentOutOfRangeException(name,
                        $"{name} must be between {rule.Minimum} and {rule.Maximum}.");
                continue;
            }
            if (!value.TryGetValue<string>(out var text))
                throw new ArgumentException($"{name} must be a string.", name);
            if (string.IsNullOrWhiteSpace(text)
                || text.Length < rule.MinimumLength || text.Length > rule.MaximumLength)
                throw new ArgumentException(
                    $"{name} must contain {rule.MinimumLength} to {rule.MaximumLength} characters.",
                    name);
            if (rule.IsDate && !DateOnly.TryParseExact(
                    text, LegalOperationCatalog.IsoDateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                throw new ArgumentException($"{name} must be an ISO date (YYYY-MM-DD).", name);
            if (rule.AllowedValues is not null
                && !rule.AllowedValues.Contains(text, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"{name} must be one of: {string.Join(", ", rule.AllowedValues)}.", name);
            if (rule.MaximumListValues is { } maximum)
                ValidateList(name, text, maximum,
                    rule.MaximumListItemLength ?? LegalOperationCatalog.MaximumStringLength);
        }

        foreach (var required in operation.McpRequired)
            if (!arguments.ContainsKey(required))
                throw new ArgumentException($"{required} is required.", required);
        foreach (var coupling in operation.ConditionalRequirements ?? [])
            if (String(arguments, coupling.WhenArgument) == coupling.WhenValue
                && String(arguments, coupling.RequiredArgument) is null)
                throw new ArgumentException(
                    $"{coupling.RequiredArgument} is required when "
                    + $"{coupling.WhenArgument}={coupling.WhenValue}.",
                    coupling.RequiredArgument);
        foreach (var coupling in operation.AllowedValuesCouplings ?? [])
        {
            if (String(arguments, coupling.Argument) is null) continue;
            var value = String(arguments, coupling.WhenArgument);
            if (value is null || !coupling.AllowedValues.Contains(value, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"{coupling.Argument} is supported only when {coupling.WhenArgument} is one "
                    + $"of: {string.Join(", ", coupling.AllowedValues)}.", coupling.Argument);
        }
        if (operation.DateRange is { } range
            && Date(arguments, range.FromArgument) is { } from
            && Date(arguments, range.ToArgument) is { } to
            && from > to)
            throw new ArgumentException(
                $"{range.FromArgument} must not follow {range.ToArgument}.", range.FromArgument);
        if (operation.PublisherAuthorityArgument is { } authority)
            RequirePublisherAuthority(arguments, authority);
    }

    private static string? String(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static DateOnly? Date(JsonObject arguments, string name) =>
        DateOnly.TryParseExact(String(arguments, name), LegalOperationCatalog.IsoDateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static void ValidateList(
        string name,
        string value,
        int maximum,
        int maximumItemLength)
    {
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
