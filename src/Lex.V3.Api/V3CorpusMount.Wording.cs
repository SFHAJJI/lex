using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Api;

/// <summary>
/// Content /3's wording structure in a Luxembourg article's quote (slice S3b): the article's <c>segments</c>, read from its
/// stored token stream, so a reader can show quoted wording between quotation marks and the publisher's deleted wording
/// struck through, while <c>text</c> stays exactly the text the index searches and a quote cites.
/// </summary>
/// <remarks>
/// An article content /1 or /2 made has no quotation token and no deleted wording, so it gets no <c>segments</c> and its row,
/// and the answer that carries it, keep their bytes; an answer gains <c>segments_rule</c> only when one of its rows has
/// segments.
/// </remarks>
internal sealed partial class V3CorpusMount
{
    internal const string SegmentsRule =
        "segments, on an article whose wording holds quoted wording or wording the publisher marks deleted, is that wording in publisher order: " +
        "text (the text the article is quoted, cited and searched by; these segments' texts, concatenated, are exactly text), quotation_start and quotation_end around wording the act " +
        "quotes (quotation names the publisher's element, embeddedText, quotedText, embeddedStructure or quotedStructure; marks_in_wording is true " +
        "when the publisher writes the quotation marks in the wording itself, so a reader adds none, and marker is the publisher's own mark when it " +
        "gives one), and deleted_text, wording the publisher marks deleted in its text (a del or a struck span), which is never part of text, " +
        "text_sha256, a quote, search or a citation; the notes of an article hold no quoted or deleted wording, and a note whose reference the " +
        "publisher wrote without a marker is unmarked";

    /// <summary>
    /// The row unchanged when the article's tokens hold neither a quotation nor deleted wording; otherwise the row with its
    /// <c>segments</c> after <c>notes</c>. The texts of the <c>text</c> segments must concatenate to the row's text, or the
    /// stored stream and the searchable text disagree and the index is refused.
    /// </summary>
    internal static object WithSegments(object row, string tokensJson, string text, ref bool segmented)
    {
        var segments = ArticleSegments(tokensJson, text);
        if (segments is null)
        {
            return row;
        }

        segmented = true;
        var node = JsonSerializer.SerializeToNode(row)!.AsObject();
        var at = node.Select(static (pair, index) => (pair.Key, index)).First(static entry => entry.Key == "notes").index + 1;
        node.Insert(at, "segments", segments);
        return node;
    }

    /// <summary>The answer unchanged when none of its rows has segments; otherwise with <c>segments_rule</c> after <c>page_rule</c>.</summary>
    internal static object WithSegmentsRule(object answer, bool segmented)
    {
        if (!segmented)
        {
            return answer;
        }

        var node = JsonSerializer.SerializeToNode(answer)!.AsObject();
        var at = node.Select(static (pair, index) => (pair.Key, index)).First(static entry => entry.Key == "page_rule").index + 1;
        node.Insert(at, "segments_rule", SegmentsRule);
        return node;
    }

    /// <summary>
    /// The article's segments, or null when its stored tokens hold no quotation boundary and no deleted wording. Consecutive
    /// text and reference tokens are one <c>text</c> segment, consecutive deleted wording one <c>deleted_text</c> segment;
    /// notes and modification markers are not wording and are skipped.
    /// </summary>
    internal static JsonArray? ArticleSegments(string tokensJson, string text)
    {
        using var document = JsonDocument.Parse(tokensJson);
        var tokens = document.RootElement.EnumerateArray().ToArray();
        if (!tokens.Any(static token => token.GetProperty("kind").GetString() is "quotation_start" or "quotation_end" or "deleted_text"))
        {
            return null;
        }

        static string? StringOf(JsonElement token, string name) =>
            token.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var segments = new JsonArray();
        var pending = new StringBuilder();
        string? pendingKind = null;
        var inForce = new StringBuilder();
        void Flush()
        {
            if (pendingKind is not null)
            {
                segments.Add(new JsonObject { ["kind"] = pendingKind, ["text"] = pending.ToString() });
                pending.Clear();
                pendingKind = null;
            }
        }

        foreach (var token in tokens)
        {
            var kind = token.GetProperty("kind").GetString();
            switch (kind)
            {
                case "text" or "reference" or "deleted_text":
                    var segmentKind = kind == "deleted_text" ? "deleted_text" : "text";
                    if (pendingKind != segmentKind)
                    {
                        Flush();
                        pendingKind = segmentKind;
                    }
                    var value = StringOf(token, "text") ?? "";
                    pending.Append(value);
                    if (segmentKind == "text")
                    {
                        inForce.Append(value);
                    }
                    break;
                case "quotation_start" or "quotation_end":
                    Flush();
                    segments.Add(new JsonObject
                    {
                        ["kind"] = kind,
                        ["quotation"] = StringOf(token, "quotation"),
                        ["marks_in_wording"] = token.TryGetProperty("marks_in_wording", out var marks) && marks.ValueKind == JsonValueKind.True,
                        ["marker"] = StringOf(token, "marker"),
                    });
                    break;
            }
        }

        Flush();
        if (!string.Equals(inForce.ToString(), text, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "An article's stored text segments are not its searchable text; the index is not the one its build wrote.");
        }

        return segments;
    }
}
