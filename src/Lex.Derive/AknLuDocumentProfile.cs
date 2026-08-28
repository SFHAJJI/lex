using System.Xml.Linq;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "akn-lu-document/1": an official Legilux Akoma Ntoso body whose
/// publisher markup exposes no article or annex boundary becomes one document-level provision.
///
/// This profile is intentionally narrower than <see cref="AknLuProfile"/>. It runs only after
/// the frozen article profile returns no provisions, uses the publisher's body element as the
/// sole boundary, and never turns metadata, preface notes, or a whole collection into legal text.
/// </summary>
public static class AknLuDocumentProfile
{
    public const string ProfileId = "akn-lu-document/1";

    public static Extraction Extract(string xml, string lexIdBase)
    {
        var document = StrictPublisherXml.Parse(xml);
        var root = document.Root ?? throw new InvalidDataException("empty XML document");
        var akn = root.Name.Namespace;
        var body = root.Descendants()
            .FirstOrDefault(element => element.Name.Namespace == akn && element.Name.LocalName == "body");
        if (body is null || string.IsNullOrWhiteSpace(body.Value))
            return new Extraction([], "", ["no non-empty AKN body found; document not extracted"]);

        // Feed a synthetic boundary to the frozen renderer. The source bytes remain untouched and
        // the distinct profile id makes the generated boundary explicit in every derived record.
        var documentProvision = new XElement(akn + "article",
            new XAttribute("id", "document"),
            new XElement(akn + "heading", "Document"),
            body.Nodes());
        body.ReplaceNodes(documentProvision);

        var rendered = AknLuProfile.Extract(
            document.ToString(SaveOptions.DisableFormatting), lexIdBase);
        var provisions = rendered.Provisions
            .Select(provision => provision with { Type = "document", Num = null, Heading = null })
            .ToList();
        if (provisions.Count != 1)
            return new Extraction([], "", ["document boundary did not produce exactly one provision; document not extracted"]);

        return new Extraction(
            provisions,
            rendered.Markdown,
            [.. rendered.Notes, "publisher markup exposes no article or annex boundary; used the official AKN body as one document-level provision"]);
    }
}
