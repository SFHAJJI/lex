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

    public static Extraction Extract(string xml, string lexIdBase)
    {
        var doc = XDocument.Parse(xml, LoadOptions.None);
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

        // frontmatter is the writer's job; the extraction is pure document content
        var md = new StringBuilder();
        var provisions = new List<Provision>();
        var lastPath = new List<string>();
        var seenAnchors = new HashSet<string>(StringComparer.Ordinal);
        var ordinal = 0;

        foreach (var (el, type) in provisionSources)
        {
            ordinal++;
            var scl = ReadSclMeta(el);
            var anchor = (string?)el.Attribute("id");
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

        if (provisions.Count == 0)
            notes.Add("no article/annex elements found; document not extracted (profile coverage gap)");

        var markdown = md.ToString();
        // spans as Unicode codepoint offsets (portable for non-.NET consumers)
        var converted = ToCodepointSpans(markdown, provisions);
        return new Extraction(converted, markdown, notes);
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
