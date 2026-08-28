using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "akn-lu/1": Legilux Akoma Ntoso 3.0 XML -> clean per-provision
/// Markdown + structure. Deterministic by contract (same bytes in -> same bytes out):
/// no LLM, no wall clock, no culture-dependent formatting. The verify chain
/// (provision text_sha256 -> verbatim-file sha256) depends on this determinism.
/// Coverage: articles + attached annex documents; publisher-minted anchors are reused,
/// never re-minted. scl:* metadata is read for ELI/dates/titles and otherwise dropped;
/// hyperlinks become structured citations, never inline noise.
/// </summary>
public static class AknLuProfile
{
    public const string ProfileId = "akn-lu/1";

    private static readonly string[] ContainerNames =
        ["book", "part", "title", "chapter", "section", "subsection"];
    private const string OfficialLegiluxWIdPrefix = "/eli/etat/leg/";

    public static Extraction Extract(string xml, string lexIdBase) =>
        ExtractCore(xml, lexIdBase, preservePublisherStructuralEmpties: false);

    internal static Extraction ExtractWithPublisherStructuralEmpties(
        string xml, string lexIdBase) =>
        ExtractCore(xml, lexIdBase, preservePublisherStructuralEmpties: true);

    private static Extraction ExtractCore(
        string xml, string lexIdBase, bool preservePublisherStructuralEmpties)
    {
        var doc = StrictPublisherXml.Parse(xml);
        var root = doc.Root ?? throw new InvalidDataException("empty XML document");
        var akn = root.Name.Namespace;
        var notes = new List<string>();

        // ---- collect provisions in document order: articles, then annex/attached docs
        var provisionSources = new List<(XElement El, string Type)>();
        foreach (var el in root.Descendants())
        {
            if (el.Name.Namespace != akn) continue;
            if (el.Name.LocalName == "article")
                provisionSources.Add((el, "article"));
            else if (el.Name.LocalName is "doc" or "attachment" && el.Descendants().Any(d => d.Name.LocalName == "mainBody"))
                provisionSources.Add((el, "annex"));
        }
        // an <attachment> wrapping a <doc> would double-count: keep outermost only
        provisionSources = provisionSources
            .Where(p => !provisionSources.Any(q => q.El != p.El && q.El.Descendants().Contains(p.El)))
            .ToList();
        Dictionary<string, int>? officialIds = null;
        Dictionary<string, int>? officialWIds = null;
        HashSet<string>? documentTargets = null;
        if (preservePublisherStructuralEmpties)
        {
            var allElements = root.DescendantsAndSelf().ToArray();
            officialIds = allElements
                .Select(element => (string?)element.Attribute("id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            officialWIds = allElements
                .Select(element => (string?)element.Attribute("wId"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            documentTargets = allElements
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName is "id" or "eId")
                .Select(attribute => attribute.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);
        }

        // frontmatter is the writer's job; the extraction is pure document content
        var md = new StringBuilder();
        var provisions = new List<Provision>();
        var lastPath = new List<string>();
        var seenAnchors = new HashSet<string>(StringComparer.Ordinal);
        List<PublisherStructuralEmptyArticle>? publisherStructuralEmptyArticles =
            preservePublisherStructuralEmpties ? [] : null;
        var ordinal = 0;

        foreach (var (el, type) in provisionSources)
        {
            ordinal++;
            var scl = ReadSclMeta(el);
            var officialAnchor = (string?)el.Attribute("id");
            var officialWId = (string?)el.Attribute("wId");
            var anchor = officialAnchor;
            if (string.IsNullOrWhiteSpace(anchor))
                anchor = type == "annex" ? Slug(AnnexTitle(el, akn) ?? $"annex_{ordinal}") : $"{type}_{ordinal}";
            if (!seenAnchors.Add(anchor))
            {
                var baseAnchor = anchor;
                var suffix = 2;
                while (!seenAnchors.Add(anchor = $"{baseAnchor}__{suffix}")) suffix++;
                notes.Add($"duplicate anchor '{baseAnchor}' disambiguated as '{anchor}'");
            }

            var path = type == "article" ? ContainerPath(el, akn) : [];
            var num = type == "article" ? NullIfEmpty(InlineText(el.Elements().FirstOrDefault(c => c.Name.LocalName == "num"), akn, null, plain: true)) : null;
            var heading = NullIfEmpty(type == "article"
                ? InlineText(el.Elements().FirstOrDefault(c => c.Name.LocalName == "heading"), akn, null, plain: true)
                : AnnexTitle(el, akn) ?? "");

            var citations = new List<Citation>();
            var body = RenderBody(el, akn, citations, type);
            var publisherStructuralEmpty = preservePublisherStructuralEmpties
                && type == "article"
                && !string.IsNullOrWhiteSpace(officialAnchor)
                && officialIds!.GetValueOrDefault(officialAnchor) == 1
                && !string.IsNullOrWhiteSpace(officialWId)
                && officialWId.StartsWith(OfficialLegiluxWIdPrefix, StringComparison.Ordinal)
                && officialWIds!.GetValueOrDefault(officialWId) == 1
                && num is null
                && heading is null
                && body.Length == 0
                && string.IsNullOrWhiteSpace(el.Value)
                && HasOnlyKnownEmptyStructure(el, akn, documentTargets!)
                    ? new PublisherStructuralEmptyArticle(officialAnchor, officialWId)
                    : null;
            if (publisherStructuralEmpty is not null)
            {
                publisherStructuralEmptyArticles!.Add(publisherStructuralEmpty);
                continue;
            }

            // ---- assemble markdown: emit container headings on path change, then the provision
            for (var i = 0; i < path.Count; i++)
            {
                if (i < lastPath.Count && lastPath[i] == path[i]) continue;
                md.Append('\n').Append(new string('#', Math.Min(2 + i, 5))).Append(' ').Append(path[i]).Append('\n');
            }
            lastPath = path.ToList();

            md.Append("\n<a id=\"").Append(anchor).Append("\"></a>\n\n");
            var title = num is null && heading is null
                ? anchor
                : string.Join(" ", new[] { num, heading }.Where(s => !string.IsNullOrEmpty(s)));
            md.Append("### ").Append(title).Append('\n').Append('\n');

            var start = md.Length;
            md.Append(body);
            var end = md.Length;
            md.Append('\n');

            provisions.Add(new Provision(
                Anchor: anchor,
                Eli: scl.VersionedEli ?? scl.Eli,
                Type: type,
                Num: num,
                Heading: heading,
                Path: path,
                ArticleValidFrom: scl.DateApplicability,
                TextMd: body,
                TextSha256: Sha256Hex(body),
                MdStart: start,
                MdEnd: end,
                Citations: citations));
        }

        if (publisherStructuralEmptyArticles?.Count > 0)
            notes.Add($"{publisherStructuralEmptyArticles.Count} official publisher-structural empty article(s) preserved outside searchable provisions");

        if (provisions.Count == 0 && (publisherStructuralEmptyArticles?.Count ?? 0) == 0)
            notes.Add("no article/annex elements found; document not extracted (profile coverage gap)");

        var markdown = md.ToString();
        // spans as Unicode codepoint offsets (portable for non-.NET consumers)
        var converted = ToCodepointSpans(markdown, provisions);
        return new Extraction(converted, markdown, notes, publisherStructuralEmptyArticles);
    }

    // ---------------------------------------------------------------- scl metadata

    private sealed record SclMeta(string? Eli, string? VersionedEli, string? Title, string? DateApplicability);

    private static SclMeta ReadSclMeta(XElement provision)
    {
        string? eli = null, versionedEli = null, title = null, date = null;
        foreach (var j in provision.Descendants().Where(d => d.Name.LocalName == "jolux"))
        {
            // stop at nested provisions' metadata: only blocks whose nearest article/doc ancestor is this one
            var owner = j.Ancestors().FirstOrDefault(a => a.Name.LocalName is "article" or "doc" or "attachment");
            if (owner != provision) continue;
            var name = j.Attributes().FirstOrDefault(a => a.Name.LocalName == "name")?.Value;
            var value = j.Value.Trim();
            switch (name)
            {
                case "uriThis":
                    var inLegalResource = j.Parent?.Name.LocalName == "JOLUXLegalResource";
                    if (inLegalResource) versionedEli = value; else eli = value;
                    break;
                case "title": title ??= value; break;
                case "dateApplicability": date ??= value; break;
            }
        }
        return new SclMeta(eli, versionedEli, title, date);
    }

    // ---------------------------------------------------------------- structure

    private static List<string> ContainerPath(XElement article, XNamespace akn)
    {
        var path = new List<string>();
        foreach (var anc in article.Ancestors().Where(a => a.Name.Namespace == akn && ContainerNames.Contains(a.Name.LocalName)))
        {
            var num = InlineText(anc.Elements().FirstOrDefault(c => c.Name.LocalName == "num"), akn, null);
            var heading = InlineText(anc.Elements().FirstOrDefault(c => c.Name.LocalName == "heading"), akn, null);
            var label = string.Join(" — ", new[] { num, heading }.Where(s => !string.IsNullOrEmpty(s)));
            if (label.Length > 0) path.Insert(0, label!);
        }
        return path;
    }

    private static string? AnnexTitle(XElement docEl, XNamespace akn)
    {
        var lt = docEl.Descendants().FirstOrDefault(d => d.Name.LocalName == "longTitle");
        return lt is null ? null : NullIfEmpty(InlineText(lt, akn, null, plain: true));
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    // ---------------------------------------------------------------- rendering

    private static string RenderBody(XElement provision, XNamespace akn, List<Citation> citations, string type)
    {
        var blocks = new List<string>();
        // article: children after num/heading; annex doc: its mainBody
        IEnumerable<XElement> content;
        if (type == "annex")
            content = provision.Descendants().Where(d => d.Name.LocalName == "mainBody").Take(1);
        else
            content = provision.Elements().Where(c =>
                c.Name.Namespace == akn && c.Name.LocalName is not ("num" or "heading"));

        foreach (var el in content) RenderBlock(el, akn, blocks, citations, "");
        return string.Join("\n\n", blocks.Where(b => b.Length > 0));
    }

    private static bool HasOnlyKnownEmptyStructure(
        XElement article, XNamespace akn, IReadOnlySet<string> documentTargets)
    {
        if (!HasOnlyAttributes(article, "id", "wId")) return false;

        var children = article.Elements().ToArray();
        var index = 0;
        if (index >= children.Length || !Is(children[index], akn, "num")
            || !IsEmptyInline(children[index], akn, "u"))
            return false;
        index++;
        if (index < children.Length && Is(children[index], akn, "heading"))
        {
            if (!IsEmptyInline(children[index], akn, "b", wrapperRequired: true)) return false;
            index++;
        }
        if (index == children.Length) return false;

        var bodyName = children[index].Name.LocalName;
        if (bodyName is not ("alinea" or "paragraph")) return false;
        for (; index < children.Length; index++)
        {
            var child = children[index];
            if (!Is(child, akn, bodyName)) return false;
            if (bodyName == "alinea"
                    ? !IsEmptyAlinea(child, akn, documentTargets)
                    : !IsEmptyParagraph(child, akn, documentTargets))
                return false;
        }
        return true;
    }

    private static bool IsEmptyParagraph(
        XElement paragraph, XNamespace akn, IReadOnlySet<string> documentTargets)
    {
        if (!HasOnlyAttributes(paragraph, "id")
            || string.IsNullOrWhiteSpace((string?)paragraph.Attribute("id")))
            return false;
        var children = paragraph.Elements().ToArray();
        if (children.Length < 2 || !Is(children[0], akn, "num")
            || !IsEmptyInline(children[0], akn, "u"))
            return false;
        return children.Skip(1).All(child => Is(child, akn, "alinea")
            && IsEmptyAlinea(child, akn, documentTargets));
    }

    private static bool IsEmptyAlinea(
        XElement alinea, XNamespace akn, IReadOnlySet<string> documentTargets)
    {
        if (alinea.HasAttributes) return false;
        var content = alinea.Elements().ToArray();
        return content.Length == 1 && Is(content[0], akn, "content")
            && !content[0].HasAttributes
            && content[0].Elements().Any()
            && content[0].Elements().All(p => Is(p, akn, "p")
                && IsEmptyParagraphText(p, akn, documentTargets));
    }

    private static bool IsEmptyParagraphText(
        XElement paragraph, XNamespace akn, IReadOnlySet<string> documentTargets)
    {
        if (paragraph.HasAttributes) return false;
        foreach (var noteRef in paragraph.Elements())
        {
            if (!Is(noteRef, akn, "noteRef") || noteRef.HasElements
                || !string.IsNullOrWhiteSpace(noteRef.Value)
                || !HasOnlyAttributes(noteRef, "href", "marker"))
                return false;
            var href = (string?)noteRef.Attribute("href") ?? "";
            var marker = (string?)noteRef.Attribute("marker") ?? "";
            if (href.Length <= 2 || !href.StartsWith("#M", StringComparison.Ordinal)
                || href.AsSpan(2).ContainsAnyExceptInRange('0', '9')
                || marker.Length == 0
                || marker.AsSpan().ContainsAnyExceptInRange('0', '9')
                || documentTargets.Contains(href[1..]))
                return false;
        }
        return true;
    }

    private static bool IsEmptyInline(
        XElement element, XNamespace akn, string wrapper, bool wrapperRequired = false)
    {
        if (element.HasAttributes) return false;
        var children = element.Elements().ToArray();
        if (children.Length > 1 || (wrapperRequired && children.Length != 1)) return false;
        return children.All(child => Is(child, akn, wrapper)
            && !child.HasAttributes && !child.Elements().Any());
    }

    private static bool Is(XElement element, XNamespace akn, string localName)
        => element.Name.Namespace == akn && element.Name.LocalName == localName;

    private static bool HasOnlyAttributes(XElement element, params string[] names)
    {
        var attributes = element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
            .ToArray();
        return attributes.Length == names.Length
            && names.All(name => attributes.Count(attribute => attribute.Name.NamespaceName.Length == 0
                && attribute.Name.LocalName == name) == 1);
    }

    private static void RenderBlock(XElement el, XNamespace akn, List<string> blocks, List<Citation> citations, string listIndent)
    {
        if (el.Name.Namespace != akn) return;   // scl:* and friends never render
        switch (el.Name.LocalName)
        {
            case "eop" or "num":
                return;
            case "paragraph":
            {
                var num = InlineText(el.Elements().FirstOrDefault(c => c.Name.LocalName == "num"), akn, citations);
                var inner = new List<string>();
                foreach (var c in el.Elements().Where(c => c.Name.LocalName != "num"))
                    RenderBlock(c, akn, inner, citations, listIndent);
                if (inner.Count > 0 && !string.IsNullOrEmpty(num))
                {
                    // never glue the number onto a table/list block — it breaks the markdown
                    if (inner[0].StartsWith('|') || inner[0].StartsWith('-') || inner[0].StartsWith("1."))
                        inner.Insert(0, $"**{num}**");
                    else
                        inner[0] = $"**{num}** {inner[0]}";
                }
                blocks.AddRange(inner);
                return;
            }
            case "alinea" or "content" or "mainBody" or "blockList" or "div":
                foreach (var c in el.Elements()) RenderBlock(c, akn, blocks, citations, listIndent);
                return;
            case "p" or "heading":
            {
                var t = InlineText(el, akn, citations);
                if (t.Length > 0) blocks.Add(listIndent.Length > 0 ? listIndent + t : t);
                return;
            }
            case "table":
                blocks.Add(RenderTable(el, akn, citations));
                return;
            case "ol" or "ul":
            {
                var lines = new List<string>();
                var n = 0;
                foreach (var li in el.Elements().Where(c => c.Name.LocalName == "li"))
                {
                    n++;
                    var marker = el.Name.LocalName == "ol" ? $"{n}." : "-";
                    var liBlocks = new List<string>();
                    foreach (var c in li.Elements()) RenderBlock(c, akn, liBlocks, citations, "");
                    var liText = liBlocks.Count > 0 ? string.Join(" ", liBlocks) : InlineText(li, akn, citations);
                    lines.Add($"{listIndent}{marker} {liText}");
                }
                if (lines.Count > 0) blocks.Add(string.Join("\n", lines));
                return;
            }
            default:
                // unknown block container: recurse if it has element children, else inline-render
                if (el.HasElements) foreach (var c in el.Elements()) RenderBlock(c, akn, blocks, citations, listIndent);
                else
                {
                    var t = InlineText(el, akn, citations);
                    if (t.Length > 0) blocks.Add(t);
                }
                return;
        }
    }

    private static string RenderTable(XElement table, XNamespace akn, List<Citation> citations)
    {
        var rows = new List<string[]>();
        foreach (var tr in table.Descendants().Where(d => d.Name.LocalName == "tr"))
        {
            var cells = tr.Elements().Where(c => c.Name.LocalName is "td" or "th")
                .Select(td => CellText(td, akn, citations)).ToArray();
            if (cells.Length > 0) rows.Add(cells);
        }
        if (rows.Count == 0) return "";
        var width = rows.Max(r => r.Length);
        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Concat(Enumerable.Repeat("", width - rows[i].Length));
            sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
            if (i == 0) sb.Append("|").Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static string CellText(XElement cell, XNamespace akn, List<Citation> citations)
        => InlineText(cell, akn, citations).Replace("|", "\\|");

    /// <summary>Inline rendering: collapses whitespace, keeps b/i emphasis, folds sup/sub
    /// into plain text, records ref links as citations and emits their text only.</summary>
    private static string InlineText(XElement? el, XNamespace akn, List<Citation>? citations, bool plain = false)
    {
        if (el is null) return "";
        var sb = new StringBuilder();
        void Walk(XNode node)
        {
            switch (node)
            {
                case XText t:
                    sb.Append(t.Value);
                    break;
                case XElement e when e.Name.Namespace != akn:
                    return;                      // scl:* inline metadata never renders
                case XElement e:
                    switch (e.Name.LocalName)
                    {
                        case "eop": return;
                        case "mod": return;      // amendment markers are structural, not text
                        case "docNumber":        // amendment source note: keep, parenthesised by source
                        case "sup" or "sub":
                            foreach (var c in e.Nodes()) Walk(c);
                            return;
                        case "b":
                            if (!plain) sb.Append("**");
                            foreach (var c in e.Nodes()) Walk(c);
                            if (!plain) sb.Append("**");
                            return;
                        case "i":
                            if (!plain) sb.Append('*');
                            foreach (var c in e.Nodes()) Walk(c);
                            if (!plain) sb.Append('*');
                            return;
                        case "ref":
                        {
                            var text = string.Concat(e.DescendantNodes().OfType<XText>().Select(t => t.Value));
                            text = CollapseWs(text);
                            citations?.Add(new Citation((string?)e.Attribute("href"), text));
                            sb.Append(text);
                            return;
                        }
                        default:
                            foreach (var c in e.Nodes()) Walk(c);
                            return;
                    }
            }
        }
        foreach (var n in el.Nodes()) Walk(n);
        return CollapseWs(sb.ToString());
    }

    private static string CollapseWs(string s) => MdUtil.CollapseWs(s);
    private static string Slug(string s) => MdUtil.Slug(s);
    private static string Sha256Hex(string text) => MdUtil.Sha256Hex(text);
    private static List<Provision> ToCodepointSpans(string markdown, List<Provision> provisions)
        => MdUtil.ToCodepointSpans(markdown, provisions);
}

public static class AknLuProfileV2
{
    public const string ProfileId = "akn-lu/2";

    public static Extraction Extract(string xml, string lexIdBase) =>
        AknLuProfile.ExtractWithPublisherStructuralEmpties(xml, lexIdBase);
}
