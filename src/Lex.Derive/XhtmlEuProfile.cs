using System.Text;
using System.Xml.Linq;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "xhtml-eu/1": EUR-Lex / Cellar consolidated XHTML -> per-provision
/// Markdown + structure. Deterministic (no LLM, no clock). Anchors are Cellar's own
/// eli-subdivision ids (art_1, anx_I, ...) — publisher-minted, never re-minted.
/// Coverage: articles + annexes; recitals/citations blocks are out of scope for v1
/// (recorded in extraction notes). Confidence is "heuristic": Cellar's HTML classes are
/// stable but undocumented, unlike the AKN schema.
/// </summary>
public static class XhtmlEuProfile
{
    public const string ProfileId = "xhtml-eu/1";

    private sealed record Draft(
        string Anchor, string Type, string? Num, string? Heading,
        List<string> Path, string Body, List<Citation> Citations);

    public static Extraction Extract(string xhtml, string lexIdBase)
    {
        var doc = XDocument.Parse(xhtml, LoadOptions.None);
        var root = doc.Root ?? throw new InvalidDataException("empty XHTML document");
        var notes = new List<string>();

        var subdivisions = root.Descendants()
            .Where(e => e.Name.LocalName == "div" && HasClass(e, "eli-subdivision"))
            .Where(e => ((string?)e.Attribute("id")) is { } id &&
                        (id.StartsWith("art_", StringComparison.Ordinal) ||
                         id.StartsWith("anx", StringComparison.Ordinal)))
            // outermost only: annex containers can nest article-like subdivisions
            .ToList();
        subdivisions = subdivisions
            .Where(e => !e.Ancestors().Any(a => subdivisions.Contains(a)))
            .ToList();

        List<Draft> drafts;
        if (subdivisions.Count > 0)
        {
            notes.Add("boundaries: publisher eli-subdivision fragment identifiers (structural-in-presentation)");
            drafts = subdivisions.Select(el =>
            {
                var id = (string)el.Attribute("id")!;
                var citations = new List<Citation>();
                return new Draft(
                    Anchor: id,
                    Type: id.StartsWith("anx", StringComparison.Ordinal) ? "annex" : "article",
                    Num: MdUtil.NullIfEmpty(TextOfFirst(el, "title-article-norm")),
                    Heading: MdUtil.NullIfEmpty(TextOfFirst(el, "stitle-article-norm")),
                    Path: ContainerPath(el),
                    Body: RenderBody(el, citations),
                    Citations: citations);
            }).ToList();
        }
        else
        {
            // Older flat consolidation format: no subdivision wrappers; articles are marked
            // by p.title-article-norm and annexes by p.title-annex-1. Anchors derive
            // deterministically from the publisher's own numbering (ELI fragment convention).
            notes.Add("boundaries: flat format segmented on title-article-norm / title-annex-1; anchors derived from publisher numbering (art_N / anx_N)");
            drafts = FlatExtract(root, notes);
        }

        return Assemble(drafts, notes);
    }

    private static List<Draft> FlatExtract(XElement root, List<string> notes)
    {
        var firstTitle = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "p" && ClassOf(e).Contains("title-article-norm"));
        if (firstTitle is null) return [];
        var stream = firstTitle.Parent!.Elements().ToList();

        var drafts = new List<Draft>();
        string? level1 = null, level2 = null;
        string? curAnchor = null, curType = null, curNum = null, curHeading = null;
        List<string> curPath = [];
        List<string> curBlocks = [];
        List<Citation> curCites = [];

        void Flush()
        {
            if (curAnchor is null) return;
            drafts.Add(new Draft(curAnchor, curType!, curNum, curHeading, curPath,
                string.Join("\n\n", curBlocks.Where(b => b.Length > 0)), curCites));
            curAnchor = null; curNum = null; curHeading = null; curBlocks = []; curCites = [];
        }

        foreach (var e in stream)
        {
            var cls = ClassOf(e);
            if (e.Name.LocalName == "p" && cls.Contains("title-article-norm"))
            {
                Flush();
                curNum = MdUtil.NullIfEmpty(Inline(e, null));
                curType = "article";
                var slug = MdUtil.Slug(curNum ?? "article");
                curAnchor = slug.StartsWith("article_", StringComparison.Ordinal) ? "art_" + slug["article_".Length..] : slug;
                curPath = new[] { level1, level2 }.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
            }
            else if (e.Name.LocalName == "p" && cls.Contains("stitle-article-norm"))
            {
                if (curAnchor is not null && curHeading is null) curHeading = MdUtil.NullIfEmpty(Inline(e, null));
            }
            else if (e.Name.LocalName == "p" && cls.Any(c => c.StartsWith("title-annex", StringComparison.Ordinal)))
            {
                Flush();
                curHeading = MdUtil.NullIfEmpty(Inline(e, null));
                curType = "annex";
                var slug = MdUtil.Slug(curHeading ?? "annex");
                curAnchor = slug.StartsWith("annex_", StringComparison.Ordinal) ? "anx_" + slug["annex_".Length..] : slug;
                curPath = [];
                level1 = null; level2 = null;
            }
            else if (e.Name.LocalName == "p" && cls.Contains("title-division-1"))
            {
                Flush();
                level1 = MdUtil.NullIfEmpty(Inline(e, null)); level2 = null;
            }
            else if (e.Name.LocalName == "p" && cls.Contains("title-division-2"))
            {
                // division heading text pairs with the preceding division-1 number
                if (curAnchor is null) level2 = MdUtil.NullIfEmpty(Inline(e, null));
                else RenderBlock(e, curBlocks, curCites);
            }
            else if (curAnchor is not null)
            {
                RenderBlock(e, curBlocks, curCites);
            }
            // content before the first article (preamble, recitals) is out of v1 scope
        }
        Flush();

        var allTitles = root.Descendants().Count(e => e.Name.LocalName == "p" && ClassOf(e).Contains("title-article-norm"));
        var captured = drafts.Count(d => d.Type == "article");
        if (captured < allTitles)
            notes.Add($"flat extraction captured {captured}/{allTitles} article titles (some outside the main stream)");
        return drafts;
    }

    private static Extraction Assemble(List<Draft> drafts, List<string> notes)
    {
        var md = new StringBuilder();
        var provisions = new List<Provision>();
        var lastPath = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var d in drafts)
        {
            var anchor = d.Anchor;
            if (!seen.Add(anchor))
            {
                var i = 2;
                while (!seen.Add(anchor = $"{d.Anchor}__{i}")) i++;
                notes.Add($"duplicate anchor '{d.Anchor}' disambiguated as '{anchor}'");
            }

            for (var i = 0; i < d.Path.Count; i++)
            {
                if (i < lastPath.Count && lastPath[i] == d.Path[i]) continue;
                md.Append('\n').Append(new string('#', Math.Min(2 + i, 5))).Append(' ').Append(d.Path[i]).Append('\n');
            }
            lastPath = d.Path.ToList();

            md.Append("\n<a id=\"").Append(anchor).Append("\"></a>\n\n");
            var title = d.Num is null && d.Heading is null ? anchor
                : string.Join(" — ", new[] { d.Num, d.Heading }.Where(s => !string.IsNullOrEmpty(s)));
            md.Append("### ").Append(title).Append("\n\n");

            var start = md.Length;
            md.Append(d.Body);
            var end = md.Length;
            md.Append('\n');

            provisions.Add(new Provision(
                Anchor: anchor, Eli: null, Type: d.Type, Num: d.Num, Heading: d.Heading,
                Path: d.Path, ArticleValidFrom: null,
                TextMd: d.Body, TextSha256: MdUtil.Sha256Hex(d.Body),
                MdStart: start, MdEnd: end, Citations: d.Citations));
        }

        if (provisions.Count == 0)
            notes.Add("no articles/annexes found; document not extracted");

        var markdown = md.ToString();
        return new Extraction(MdUtil.ToCodepointSpans(markdown, provisions), markdown, notes);
    }

    // ---------------------------------------------------------------- structure

    private static List<string> ContainerPath(XElement el)
    {
        // chapters/sections are div#cpt_*/#sct_* ancestors carrying title-division-N paragraphs
        var path = new List<string>();
        foreach (var anc in el.Ancestors().Where(a => a.Name.LocalName == "div"
                     && ((string?)a.Attribute("id")) is { } aid
                     && (aid.StartsWith("cpt_", StringComparison.Ordinal) || aid.StartsWith("sct_", StringComparison.Ordinal))))
        {
            var parts = anc.Elements()
                .Where(c => c.Name.LocalName == "p" &&
                            ClassOf(c).Any(x => x.StartsWith("title-division", StringComparison.Ordinal)))
                .Select(c => MdUtil.CollapseWs(PlainText(c)))
                .Where(s => s.Length > 0)
                .ToList();
            if (parts.Count > 0) path.Insert(0, string.Join(" — ", parts));
        }
        return path;
    }

    private static string TextOfFirst(XElement el, string cls)
    {
        var hit = el.Descendants().FirstOrDefault(d => ClassOf(d).Contains(cls));
        return hit is null ? "" : MdUtil.CollapseWs(PlainText(hit));
    }

    // ---------------------------------------------------------------- rendering

    private static string RenderBody(XElement provision, List<Citation> citations)
    {
        var blocks = new List<string>();
        foreach (var child in provision.Elements())
            RenderBlock(child, blocks, citations);
        return string.Join("\n\n", blocks.Where(b => b.Length > 0));
    }

    private static void RenderBlock(XElement el, List<string> blocks, List<Citation> citations)
    {
        var cls = ClassOf(el);
        switch (el.Name.LocalName)
        {
            case "p" when cls.Contains("title-article-norm") || cls.Contains("stitle-article-norm"):
                return;                                    // already in num/heading
            case "div" when cls.Contains("eli-title"):
                return;
            case "p" when cls.Contains("modref"):
            {
                // amendment arrows (▼M6 etc.) are provenance, not text: structured citations only
                foreach (var a in el.Descendants().Where(d => d.Name.LocalName == "a"))
                {
                    var href = (string?)a.Attribute("href");
                    var label = MdUtil.CollapseWs((string?)a.Attribute("title") ?? PlainText(a));
                    if (href is not null && label.Length > 0) citations.Add(new Citation(href, label));
                }
                return;
            }
            case "p":
            {
                var t = Inline(el, citations);
                if (t.Length > 0) blocks.Add(t);
                return;
            }
            case "div" when cls.Contains("grid-container"):
            {
                // point lists: column-1 = "(a)", column-2 = text
                var cols = el.Elements().Where(c => c.Name.LocalName == "div").ToList();
                var marker = cols.FirstOrDefault(c => ClassOf(c).Contains("grid-list-column-1"));
                var content = cols.FirstOrDefault(c => ClassOf(c).Contains("grid-list-column-2"));
                var inner = new List<string>();
                if (content is not null) foreach (var c in content.Elements()) RenderBlock(c, inner, citations);
                var text = string.Join(" ", inner.Where(s => s.Length > 0));
                if (text.Length == 0 && content is not null) text = Inline(content, citations);
                var m = marker is null ? "-" : Inline(marker, citations);
                if (text.Length > 0) blocks.Add($"{m} {text}");
                return;
            }
            case "div" when cls.Contains("norm") && cls.Contains("inline-element"):
            {
                var t = Inline(el, citations);
                if (t.Length > 0) blocks.Add(t);
                return;
            }
            case "div" when cls.Contains("norm"):
            {
                // paragraph: optional no-parag number span + content
                var numSpan = el.Elements().FirstOrDefault(c => c.Name.LocalName == "span" && ClassOf(c).Contains("no-parag"));
                var inner = new List<string>();
                foreach (var c in el.Elements().Where(c => c != numSpan)) RenderBlock(c, inner, citations);
                if (inner.Count == 0)
                {
                    var t = Inline(el, citations);
                    if (t.Length > 0) inner.Add(t);
                }
                if (inner.Count > 0 && numSpan is not null)
                {
                    var n = MdUtil.CollapseWs(PlainText(numSpan)).TrimEnd();
                    if (inner[0].StartsWith('|') || inner[0].StartsWith('-') || inner[0].StartsWith("1."))
                        inner.Insert(0, $"**{n}**");
                    else
                        inner[0] = $"**{n}** {inner[0]}";
                }
                blocks.AddRange(inner);
                return;
            }
            case "table":
                blocks.Add(RenderTable(el, citations));
                return;
            case "hr" or "br" or "script" or "style":
                return;
            default:
                if (el.HasElements) foreach (var c in el.Elements()) RenderBlock(c, blocks, citations);
                else
                {
                    var t = Inline(el, citations);
                    if (t.Length > 0) blocks.Add(t);
                }
                return;
        }
    }

    private static string RenderTable(XElement table, List<Citation> citations)
    {
        var rows = new List<string[]>();
        foreach (var tr in table.Descendants().Where(d => d.Name.LocalName == "tr"))
        {
            var cells = tr.Elements().Where(c => c.Name.LocalName is "td" or "th")
                .Select(td => Inline(td, citations).Replace("|", "\\|")).ToArray();
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

    private static string Inline(XElement el, List<Citation>? citations)
    {
        var sb = new StringBuilder();
        void Walk(XNode node)
        {
            switch (node)
            {
                case XText t:
                    sb.Append(t.Value);
                    break;
                case XElement e:
                    var cls = ClassOf(e);
                    switch (e.Name.LocalName)
                    {
                        case "script" or "style": return;
                        case "a":
                        {
                            var text = MdUtil.CollapseWs(PlainText(e));
                            var href = (string?)e.Attribute("href");
                            if (text.Length > 0 && href is not null) citations?.Add(new Citation(href, text));
                            sb.Append(text);
                            return;
                        }
                        case "span" when cls.Contains("boldface"):
                            sb.Append("**"); foreach (var c in e.Nodes()) Walk(c); sb.Append("**");
                            return;
                        case "span" when cls.Contains("italics"):
                            sb.Append('*'); foreach (var c in e.Nodes()) Walk(c); sb.Append('*');
                            return;
                        default:
                            foreach (var c in e.Nodes()) Walk(c);
                            return;
                    }
            }
        }
        foreach (var n in el.Nodes()) Walk(n);
        return MdUtil.CollapseWs(sb.ToString());
    }

    private static string PlainText(XElement el)
        => string.Concat(el.DescendantNodes().OfType<XText>().Select(t => t.Value));

    private static string[] ClassOf(XElement el)
        => ((string?)el.Attribute("class"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static bool HasClass(XElement el, string cls) => ClassOf(el).Contains(cls);
}
