using System.Text;
using System.Xml.Linq;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "fmx4-eu/1": Publications Office Formex 4 consolidated XML
/// (CONS.ACT) -> per-provision Markdown + structure. Deterministic (no LLM, no clock).
/// Formex is the Office's own production format: ARTICLE/PARAG carry publisher-minted
/// IDENTIFIER attributes and titles, so boundaries are structural, not presentational —
/// confidence is "schema" (unlike xhtml-eu/1's heuristic CSS classes).
/// Anchor continuity: anchors are minted from the publisher's printed numbering exactly
/// like xhtml-eu/1's flat path (art_N from "Article N", anx_&lt;roman&gt; from "ANNEX I"), so a
/// work switching profile keeps every permalink and history state.
/// Coverage: articles + annexes; preamble/final provisions are out of scope for v1.
/// </summary>
public static class Fmx4EuProfile
{
    public const string ProfileId = "fmx4-eu/1";

    private sealed record Draft(
        string Anchor, string Type, string? Num, string? Heading,
        List<string> Path, string Body, List<Citation> Citations);

    public static Extraction Extract(string xml, string lexIdBase)
    {
        var doc = StrictPublisherXml.Parse(xml);
        var root = doc.Root ?? throw new InvalidDataException("empty Formex document");
        var notes = new List<string>
        {
            "boundaries: Formex ARTICLE / CONS.ANNEX elements (publisher structural markup)",
        };
        var footnotes = root.Descendants().Count(e => e.Name.LocalName == "NOTE" &&
            e.Ancestors().Any(a => a.Name.LocalName is "ARTICLE" or "CONS.ANNEX" or "ANNEX"));

        var drafts = new List<Draft>();

        foreach (var el in root.Descendants())
        {
            if (el.Name.LocalName == "ARTICLE")
            {
                var citations = new List<Citation>();
                var num = MdUtil.NullIfEmpty(MdUtil.CollapseWs(PlainText(el.Element("TI.ART"))));
                var heading = MdUtil.NullIfEmpty(MdUtil.CollapseWs(PlainText(el.Element("STI.ART"))));
                var slug = MdUtil.Slug(num ?? "article");
                var anchor = slug.StartsWith("article_", StringComparison.Ordinal) ? "art_" + slug["article_".Length..] : slug;
                drafts.Add(new Draft(anchor, "article", num, heading,
                    DivisionPath(el), RenderBody(el, citations), citations));
            }
            else if (el.Name.LocalName is "CONS.ANNEX" or "ANNEX")
            {
                var citations = new List<Citation>();
                var heading = MdUtil.NullIfEmpty(MdUtil.CollapseWs(PlainText(
                    el.Element("TITLE")?.Element("TI"))));
                var slug = MdUtil.Slug(heading ?? "annex");
                var anchor = slug.StartsWith("annex_", StringComparison.Ordinal) ? "anx_" + slug["annex_".Length..] : slug;
                drafts.Add(new Draft(anchor, "annex", null, heading,
                    [], RenderBody(el, citations), citations));
            }
        }

        if (footnotes > 0) notes.Add($"footnotes omitted from provision text: {footnotes}");
        return Assemble(drafts, notes);
    }

    // ---------------------------------------------------------------- structure

    private static List<string> DivisionPath(XElement el)
    {
        var path = new List<string>();
        foreach (var anc in el.Ancestors().Where(a => a.Name.LocalName == "DIVISION"))
        {
            var title = anc.Element("TITLE");
            if (title is null) continue;
            var parts = new[] { title.Element("TI"), title.Element("STI") }
                .Where(t => t is not null)
                .Select(t => MdUtil.CollapseWs(PlainText(t)))
                .Where(s => s.Length > 0)
                .ToList();
            if (parts.Count > 0) path.Insert(0, string.Join(" — ", parts));
        }
        return path;
    }

    private static Extraction Assemble(List<Draft> drafts, List<string> notes)
    {
        // Same document layout as xhtml-eu/1 (duplicated deliberately: that profile is
        // immutable and must never change bytes because this one evolves).
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
        switch (el.Name.LocalName)
        {
            case "TI.ART" or "STI.ART":
                return;                                    // already in num/heading
            case "TITLE" when el.Parent?.Name.LocalName is "CONS.ANNEX" or "ANNEX":
                return;                                    // annex title is the heading
            case "NOTE":                                   // counted at Extract level, never inline
                return;
            case "PARAG":
            {
                var numEl = el.Element("NO.PARAG");
                var n = numEl is null ? null : MdUtil.CollapseWs(PlainText(numEl)).TrimEnd();
                var inner = new List<string>();
                foreach (var c in el.Elements().Where(c => c.Name.LocalName != "NO.PARAG"))
                    RenderBlock(c, inner, citations);
                if (inner.Count > 0 && !string.IsNullOrEmpty(n))
                {
                    if (inner[0].StartsWith('|') || inner[0].StartsWith('-') || inner[0].StartsWith("1."))
                        inner.Insert(0, $"**{n}**");
                    else
                        inner[0] = $"**{n}** {inner[0]}";
                }
                blocks.AddRange(inner);
                return;
            }
            case "ALINEA" or "TXT" or "P" or "CONTENTS" or "GR.SEQ" when HasBlockChildren(el):
            {
                // mixed container: inline content (text nodes + inline children) first, then
                // ONLY the block children — inline children were already consumed above.
                var leading = InlineOwnText(el, citations);
                if (leading.Length > 0) blocks.Add(leading);
                foreach (var c in el.Elements().Where(IsBlockElement)) RenderBlock(c, blocks, citations);
                return;
            }
            case "ALINEA" or "TXT" or "P":
            {
                var t = Inline(el, citations);
                if (t.Length > 0) blocks.Add(t);
                return;
            }
            case "LIST":
            {
                foreach (var item in el.Elements().Where(c => c.Name.LocalName == "ITEM"))
                    RenderBlock(item, blocks, citations);
                return;
            }
            case "ITEM" or "NP":
            {
                // marker (NO.P "(a)" / "1.") + content; tables inside items stay separate blocks
                var np = el.Name.LocalName == "NP" ? el : el.Element("NP") ?? el;
                var markerEl = np.Element("NO.P");
                var marker = markerEl is null ? "-" : MdUtil.CollapseWs(PlainText(markerEl)).TrimEnd();
                var inner = new List<string>();
                foreach (var c in np.Elements().Where(c => c.Name.LocalName != "NO.P"))
                    RenderBlock(c, inner, citations);
                if (el.Name.LocalName == "ITEM")
                    foreach (var c in el.Elements().Where(c => c != np))
                        RenderBlock(c, inner, citations);
                var flowing = inner.Where(s => !s.StartsWith('|')).ToList();
                var tables = inner.Where(s => s.StartsWith('|')).ToList();
                var text = string.Join(" ", flowing.Where(s => s.Length > 0));
                if (text.Length > 0) blocks.Add($"{marker} {text}");
                else if (tables.Count > 0) blocks.Add(marker);
                blocks.AddRange(tables);
                return;
            }
            case "TBL":
            {
                var titleEl = el.Element("TITLE");
                if (titleEl is not null)
                {
                    var t = MdUtil.CollapseWs(PlainText(titleEl));
                    if (t.Length > 0) blocks.Add($"**{t}**");
                }
                var tbl = RenderTable(el, citations);
                if (tbl.Length > 0) blocks.Add(tbl);
                return;
            }
            case "TITLE":
            {
                var t = MdUtil.CollapseWs(PlainText(el));
                if (t.Length > 0) blocks.Add($"**{t}**");
                return;
            }
            case "QUOT.START" or "QUOT.END":
                return;                                    // inline-level; handled in Inline
            case "TOC" or "BIB.INSTANCE.CONS" or "BIB.DATA" or "INFO.CONSLEG" or "PUBLICATION.REF"
                 or "PREAMBLE" or "PREAMBLE.INIT" or "PREAMBLE.FINAL" or "GR.VISA" or "GR.CONSID":
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

    private static bool IsBlockElement(XElement el)
        => el.Name.LocalName is "LIST" or "TBL" or "PARAG" or "NP" or "GR.SEQ" or "ALINEA" or "TITLE";

    private static bool HasBlockChildren(XElement el)
        => el.Elements().Any(IsBlockElement);

    private static string RenderTable(XElement tbl, List<Citation> citations)
    {
        var rows = new List<string[]>();
        foreach (var tr in tbl.Descendants().Where(d => d.Name.LocalName == "ROW"))
        {
            var cells = tr.Elements().Where(c => c.Name.LocalName == "CELL")
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

    /// <summary>Inline text of an element's direct text nodes and inline children (block children rendered separately).</summary>
    private static string InlineOwnText(XElement el, List<Citation> citations)
    {
        var sb = new StringBuilder();
        foreach (var n in el.Nodes())
        {
            if (n is XText t) sb.Append(t.Value);
            else if (n is XElement e && !IsBlockElement(e))
                sb.Append(Inline(e, citations, raw: true));
        }
        return MdUtil.CollapseWs(sb.ToString());
    }

    private static string Inline(XElement el, List<Citation> citations, bool raw = false)
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
                    switch (e.Name.LocalName)
                    {
                        case "NOTE":
                            return;                        // footnote content never inline
                        case "INCL.ELEMENT":
                        {
                            // formula/graphic shipped as an image member: deterministic,
                            // auditable placeholder naming the evidence file
                            var kind = ((string?)e.Attribute("CONTENT"))?.ToLowerInvariant() ?? "image";
                            var fileRef = (string?)e.Attribute("FILEREF");
                            sb.Append('[').Append(kind).Append(" image");
                            if (fileRef is not null) sb.Append(": ").Append(fileRef);
                            sb.Append(']');
                            return;
                        }
                        case "QUOT.START" or "QUOT.END":
                        {
                            // CODE attribute is the hex Unicode codepoint of the publisher's quote char
                            var code = (string?)e.Attribute("CODE");
                            sb.Append(code is not null && int.TryParse(code,
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var cp)
                                ? char.ConvertFromUtf32(cp) : "\"");
                            return;
                        }
                        case "HT":
                        {
                            var type = ((string?)e.Attribute("TYPE"))?.ToUpperInvariant();
                            var (open, close) = type switch
                            {
                                "BOLD" => ("**", "**"),
                                "ITALIC" => ("*", "*"),
                                "SUB" => ("_", ""),
                                "SUP" => ("^", ""),
                                _ => ("", ""),
                            };
                            sb.Append(open); foreach (var c in e.Nodes()) Walk(c); sb.Append(close);
                            return;
                        }
                        case "IND":
                        {
                            var loc = ((string?)e.Attribute("LOC"))?.ToUpperInvariant();
                            sb.Append(loc == "SUP" ? "^" : "_");
                            foreach (var c in e.Nodes()) Walk(c);
                            return;
                        }
                        case "EXPR":
                        {
                            var type = ((string?)e.Attribute("TYPE"))?.ToUpperInvariant();
                            var (open, close) = type switch
                            {
                                "BRACE" => ("(", ")"),
                                "BRACKET" => ("[", "]"),
                                _ => ("", ""),
                            };
                            sb.Append(open); foreach (var c in e.Nodes()) Walk(c); sb.Append(close);
                            return;
                        }
                        default:
                            foreach (var c in e.Nodes()) Walk(c);
                            return;
                    }
            }
        }
        foreach (var n in el.Nodes()) Walk(n);
        var s = sb.ToString();
        return raw ? s : MdUtil.CollapseWs(s);
    }

    private static string PlainText(XElement? el)
        => el is null ? "" : string.Concat(el.DescendantNodes().OfType<XText>()
            .Where(t => !t.Ancestors().Any(a => a.Name.LocalName == "NOTE"))
            .Select(t => t.Value));
}
