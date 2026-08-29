using System.Globalization;
using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Ask;

/// <summary>Fail-closed completeness proof for the exact cited_by producer shape.</summary>
internal static class CitedByResultPolicy
{
    internal const string EvidenceScope =
        "captured_cross_references_in_held_non_withdrawn_versions";

    internal static bool IsExact(JsonNode result)
    {
        JsonObject[] units;
        if (result is JsonObject single)
            units = [single];
        else if (result is JsonArray array)
        {
            units = array.OfType<JsonObject>().ToArray();
            if (units.Length == 0 || units.Length != array.Count) return false;
        }
        else
            return false;

        var publishers = new HashSet<string>(StringComparer.Ordinal);
        string? citedWork = null;
        (int Total, int Returned, int Maximum, bool Truncated)? publisherReceipt = null;
        (int Returned, int Maximum, bool Truncated)? responseReceipt = null;
        var citationRows = 0;

        foreach (var unit in units)
        {
            var envelope = unit["envelope"] as JsonObject;
            var publisher = Text(envelope, "publisher");
            if (!BoundedIdentifier(publisher) || !publishers.Add(publisher!)) return false;

            var work = Text(unit, "cited_work");
            if (string.IsNullOrWhiteSpace(work)) return false;
            citedWork ??= work;
            if (!string.Equals(citedWork, work, StringComparison.Ordinal)) return false;
            if (!string.Equals(Text(unit, "evidence_scope"), EvidenceScope,
                    StringComparison.Ordinal))
                return false;

            if (unit["citations"] is not JsonArray citations
                || citations.Any(item => item is not JsonObject))
                return false;
            if (citations.OfType<JsonObject>().Any(row =>
                    !BoundedText(row, "work", LegalOperationCatalog.MaximumStringLength)
                    || !CanonicalDate(row, "valid_from")
                    || !BoundedText(row, "anchor", LegalOperationCatalog.MaximumAnchorLength)))
                return false;
            var count = Integer(unit, "citing_articles");
            if (count is null or < 0 || count != citations.Count) return false;
            var status = Text(envelope, "status");
            if (count == 0 ? status != McpStatus.NoResult : status != McpStatus.Ok)
                return false;
            if (citationRows > int.MaxValue - count.Value) return false;
            citationRows += count.Value;

            var currentPublisherReceipt = PublisherReceipt(unit["publisher_result_set"]);
            var currentResponseReceipt = ResponseReceipt(unit["response_row_set"]);
            if (currentPublisherReceipt is null || currentResponseReceipt is null)
                return false;
            publisherReceipt ??= currentPublisherReceipt;
            responseReceipt ??= currentResponseReceipt;
            if (publisherReceipt != currentPublisherReceipt
                || responseReceipt != currentResponseReceipt)
                return false;
        }

        return publisherReceipt is { } publishersReceipt
            && !publishersReceipt.Truncated
            && publishersReceipt.Total == publishersReceipt.Returned
            && publishersReceipt.Returned == units.Length
            && publishersReceipt.Returned <= publishersReceipt.Maximum
            && responseReceipt is { } rowsReceipt
            && !rowsReceipt.Truncated
            && rowsReceipt.Returned == citationRows
            && rowsReceipt.Returned <= rowsReceipt.Maximum;
    }

    private static (int Total, int Returned, int Maximum, bool Truncated)? PublisherReceipt(
        JsonNode? node)
    {
        if (node is not JsonObject receipt) return null;
        var total = Integer(receipt, "total");
        var returned = Integer(receipt, "returned");
        var maximum = Integer(receipt, "maximum");
        var truncated = Boolean(receipt, "truncated");
        return total is >= 0 && returned is >= 0 && maximum is >= 0 && truncated is not null
            ? (total.Value, returned.Value, maximum.Value, truncated.Value)
            : null;
    }

    private static (int Returned, int Maximum, bool Truncated)? ResponseReceipt(JsonNode? node)
    {
        if (node is not JsonObject receipt) return null;
        var returned = Integer(receipt, "returned");
        var maximum = Integer(receipt, "maximum");
        var truncated = Boolean(receipt, "truncated");
        return returned is >= 0 && maximum is >= 0 && truncated is not null
            ? (returned.Value, maximum.Value, truncated.Value)
            : null;
    }

    private static string? Text(JsonObject? value, string key) =>
        value?[key] is JsonValue item && item.TryGetValue<string>(out var text) ? text : null;

    private static int? Integer(JsonObject value, string key) =>
        value[key] is JsonValue item && item.TryGetValue<int>(out var number) ? number : null;

    private static bool? Boolean(JsonObject value, string key) =>
        value[key] is JsonValue item && item.TryGetValue<bool>(out var fact) ? fact : null;

    private static bool BoundedIdentifier(string? value) =>
        value is { Length: > 0 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character)
                                  || character is '-' or '_');

    private static bool BoundedText(JsonObject value, string key, int maximum) =>
        Text(value, key) is { } text
        && !string.IsNullOrWhiteSpace(text)
        && text.Length <= maximum;

    private static bool CanonicalDate(JsonObject value, string key) =>
        DateOnly.TryParseExact(Text(value, key), LegalOperationCatalog.IsoDateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
