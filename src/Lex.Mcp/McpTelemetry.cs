using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lex.Mcp;

internal static class McpTelemetry
{
    public const string ActivitySourceName = "Lex.Mcp";
    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Regex Digest = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> AggregateLanguages = new(StringComparer.Ordinal)
    {
        "de", "en", "fr", "lb", "pt",
    };

    public static Activity? StartTool(string tool, string? digest)
    {
        var activity = Source.StartActivity("lex.tool", ActivityKind.Internal);
        SetStartTags(activity, tool, digest);
        return activity;
    }

    internal static void SetStartTags(Activity? activity, string tool, string? digest)
    {
        activity?.SetTag("lex.tool",
            LegalOperationCatalog.ToolNames.Contains(tool, StringComparer.Ordinal)
                ? tool : "unknown");
        if (digest is not null && Digest.IsMatch(digest))
            activity?.SetTag("lex.digest", digest);
    }

    internal static void SetLanguageTag(Activity? activity, JsonObject arguments)
    {
        if (arguments["language"] is not JsonValue value
            || !value.TryGetValue<string>(out var language))
            return;
        activity?.SetTag("lex.language",
            AggregateLanguages.Contains(language) ? language : "other");
    }

    internal static void SetResultTags(Activity? activity, JsonNode result)
    {
        var status = Status(result);
        activity?.SetTag("lex.status", status);
        if (status == "failed") activity?.SetStatus(ActivityStatusCode.Error);
        if (ReturnedRows(result) is not { } returned) return;
        activity?.SetTag("lex.hit_count_bucket", HitCountBucket(returned));
        activity?.SetTag("lex.zero_hit", returned == 0);
        if (RetrievalMode(result) is { } mode)
            activity?.SetTag("lex.retrieval_mode", mode);
    }

    internal static void SetFailure(Activity? activity, string status)
    {
        if (status is not ("cancelled" or "invalid_request" or "failed"))
            throw new ArgumentOutOfRangeException(nameof(status));
        activity?.SetTag("lex.status", status);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    internal static string HitCountBucket(int returned) => returned switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(returned)),
        0 => "0",
        1 => "1",
        <= 5 => "2-5",
        <= 10 => "6-10",
        <= 25 => "11-25",
        <= 50 => "26-50",
        _ => "51+",
    };

    private static string Status(JsonNode result)
    {
        var statuses = Objects(result)
            .Select(item => item["envelope"]?["status"] ?? item["status"])
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .OfType<string>()
            .ToArray();
        if (statuses.Any(status => !McpStatus.All.Contains(status, StringComparer.Ordinal)))
            return "failed";
        var distinct = statuses.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length switch
        {
            0 => result is JsonArray { Count: 0 } ? McpStatus.NoResult : McpStatus.Ok,
            1 => distinct[0],
            _ => "mixed",
        };
    }

    private static int? ReturnedRows(JsonNode result)
    {
        JsonObject[] objects = result switch
        {
            JsonObject item => [item],
            JsonArray items when items.All(item => item is JsonObject) =>
                items.Cast<JsonObject>().ToArray(),
            _ => [],
        };
        if (objects.Length == 0) return null;

        var global = objects.Select(item => Returned(item, "global_response_row_set")).ToArray();
        var local = objects.Select(item => Returned(item, "response_row_set")).ToArray();
        if (global.Any(item => item.State == ReturnedState.Invalid)
            || local.Any(item => item.State == ReturnedState.Invalid)
            || HasMixedPresence(local))
            return null;

        if (global.Any(item => item.State == ReturnedState.Valid))
        {
            if (global.Any(item => item.State != ReturnedState.Valid)) return null;
            var values = global.Select(item => item.Value).Distinct().ToArray();
            if (values.Length != 1
                || local.Any(item => item.State != ReturnedState.Valid)
                || CheckedSum(local.Select(item => item.Value)) is not { } localTotal
                || localTotal != values[0])
                return null;
            return values[0];
        }

        var returned = local
            .Where(item => item.State == ReturnedState.Valid)
            .Select(item => item.Value)
            .ToArray();
        if (returned.Length == 0) return null;
        // Most multi-publisher envelopes repeat one global count on every publisher row.
        // A varied set is the local-count contract and must be summed.
        if (returned.Distinct().Count() == 1) return returned[0];
        return CheckedSum(returned);
    }

    private static int? CheckedSum(IEnumerable<int> values)
    {
        try
        {
            return values.Aggregate(0, (sum, value) => checked(sum + value));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool HasMixedPresence(ReturnedValue[] values) =>
        values.Any(item => item.State == ReturnedState.Valid)
        && values.Any(item => item.State == ReturnedState.Missing);

    private static ReturnedValue Returned(JsonObject item, string property)
    {
        if (!item.TryGetPropertyValue(property, out var rowSet))
            return new(ReturnedState.Missing, 0);
        if (rowSet is not JsonObject objectValue
            || !objectValue.TryGetPropertyValue("returned", out var value)
            || value is not JsonValue scalar
            || !scalar.TryGetValue<int>(out var returned)
            || returned < 0)
            return new(ReturnedState.Invalid, 0);
        return new(ReturnedState.Valid, returned);
    }

    private enum ReturnedState { Missing, Valid, Invalid }
    private readonly record struct ReturnedValue(ReturnedState State, int Value);

    private static string? RetrievalMode(JsonNode result)
    {
        var modes = Objects(result)
            .Select(item => item["retrieval_mode"])
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(value => value is "keyword" or "hybrid")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return modes.Length == 1 ? modes[0] : null;
    }

    private static IEnumerable<JsonObject> Objects(JsonNode result) => result switch
    {
        JsonObject item => [item],
        JsonArray items => items.OfType<JsonObject>(),
        _ => [],
    };
}
