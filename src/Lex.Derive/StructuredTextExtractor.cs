using System.Xml;
using HtmlAgilityPack;

namespace Lex.Derive;

/// <summary>
/// Selects an extraction profile from the publisher document itself. File extensions are
/// not reliable here: Cellar serves XHTML as both .xml and .html, while Legilux serves
/// Akoma Ntoso as .xml. Adding another publisher therefore does not require another
/// publisher-name conditional in the derivation loop.
/// </summary>
public static class StructuredTextExtractor
{
    public sealed record Result(Extraction Extraction, string ProfileId);

    public static Result Extract(string source, string lexIdBase)
    {
        if (LooksLikeAkomaNtoso(source))
            return new Result(AknLuProfile.Extract(source, lexIdBase), AknLuProfile.ProfileId);

        try
        {
            var extraction = XhtmlEuProfile.Extract(source, lexIdBase);
            if (extraction.Provisions.Count > 0)
                return new Result(extraction, XhtmlEuProfile.ProfileId);
        }
        catch (XmlException)
        {
            // Fall through to the tolerant profile. Legacy EUR-Lex HTML is not XML and
            // rejecting it here would silently remove historical law from the search layer.
        }

        return new Result(
            TolerantHtmlEuProfile.Extract(source, lexIdBase),
            TolerantHtmlEuProfile.ProfileId);
    }

    private static bool LooksLikeAkomaNtoso(string source)
    {
        // Only inspect markup names. This is deliberately a bounded format sniff, not a
        // publisher heuristic and not a legal-text transformation.
        var span = source.AsSpan(0, Math.Min(source.Length, 16_384));
        return span.Contains("<akomaNtoso", StringComparison.OrdinalIgnoreCase)
               || span.Contains(":akomaNtoso", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Extraction profile "html-eu-tolerant/1". It repairs the DOM of legacy, real-world
/// EUR-Lex HTML (unquoted attributes, HTML void elements and named entities) with a pinned
/// tolerant parser, then delegates provision semantics to the frozen XHTML profile.
/// The source evidence bytes are never rewritten.
/// </summary>
public static class TolerantHtmlEuProfile
{
    public const string ProfileId = "html-eu-tolerant/1";

    public static Extraction Extract(string html, string lexIdBase)
    {
        var doc = new HtmlDocument
        {
            OptionAutoCloseOnEnd = true,
            OptionCheckSyntax = false,
            OptionFixNestedTags = true,
            OptionOutputAsXml = true,
            OptionWriteEmptyNodes = true,
        };
        doc.LoadHtml(html);

        // HtmlAgilityPack's XML serialization is deterministic for a fixed input and package
        // version. The canonical extractor remains responsible for boundaries and rendering.
        var normalized = doc.DocumentNode.WriteTo();
        var structured = XhtmlEuProfile.Extract(normalized, lexIdBase);
        if (structured.Provisions.Count > 0) return structured;

        // Some historical Cellar resources are already article-level treaty fragments or
        // declarations and contain no article wrapper at all. Preserve their authoritative
        // wording as one document-level provision instead of omitting them from retrieval.
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        foreach (var noise in body.SelectNodes(".//script|.//style") ?? Enumerable.Empty<HtmlNode>())
            noise.Remove();
        var text = MdUtil.CollapseWs(HtmlEntity.DeEntitize(body.InnerText));
        if (text.Length == 0) return structured;

        const string anchor = "document";
        var prefix = $"<a id=\"{anchor}\"></a>\n\n### Document\n\n";
        var markdown = prefix + text + "\n";
        var provision = new Provision(
            Anchor: anchor,
            Eli: null,
            Type: "document",
            Num: null,
            Heading: null,
            Path: [],
            ArticleValidFrom: null,
            TextMd: text,
            TextSha256: MdUtil.Sha256Hex(text),
            MdStart: prefix.Length,
            MdEnd: prefix.Length + text.Length,
            Citations: []);
        return new Extraction(
            MdUtil.ToCodepointSpans(markdown, [provision]),
            markdown,
            ["boundaries: document-level fallback; publisher markup exposes no provision boundary"]);
    }
}
