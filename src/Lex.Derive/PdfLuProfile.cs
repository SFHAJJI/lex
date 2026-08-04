using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "pdf-lu/1": a Legilux consolidated act served ONLY as PDF.
///
/// Why this exists (D49). Legilux offers XML for 2,892 of its consolidations and PDF only for
/// 1,611. XML is what carries article boundaries, so a PDF-only version arrives as a complete
/// dated record with no wording, and the reader is refused on every date. For the born-digital
/// consolidated PDFs the wording is genuinely recoverable: they are typeset by Antenna House, so
/// they carry a real font layer, article markers at line starts, and structural headings. No OCR
/// is involved and none should be: this profile reads text that is already in the file.
///
/// It is NOT for the Mémorial gazette scans, where the act sits inside a whole official-gazette
/// issue and locating it needs layout analysis. That is a separate profile and a separate tier.
///
/// CONFIDENCE. Text from here is read out of a page-description format, not out of publisher
/// structure. The article boundaries are ours, inferred from typography, where akn-lu/1 gets them
/// from the publisher's own markup. The profile id is therefore the tier marker, and it is
/// recorded per version, because one work can be XML on one date and PDF-only on another.
///
/// DETERMINISM is a contract, exactly as for the XML profiles: same bytes in, same bytes out.
/// No clock, no culture-dependent formatting, ordinal comparisons throughout, and the PDF reader
/// is version-pinned. A reader upgrade that changes output is a new profile id, never a silent
/// re-derive of documents nobody touched.
/// </summary>
public static class PdfLuProfile
{
    public const string ProfileId = "pdf-lu/1";

    // "Art. 1er.", "Art. 13bis.", "Art. 7-1.", "Art.4." at the start of a line. Anchored at line
    // start on purpose: cross-references to articles are frequent mid-sentence and are not
    // boundaries. The hyphen form is real: Legilux inserts "Art. 7-1" between 7 and 8 exactly as
    // it writes "Chapitre IV-1", and capturing only the leading digits collided it with Art. 7.
    private static readonly Regex ArticleHead = new(
        @"^[ 	]*Art\.?[  ]*(\d+(?:-\d+)*(?:er|bis|ter|quater|quinquies|sexies|septies|octies|novies|decies)?)[  ]*\.?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Structural containers, as the publisher sets them: "Titre Ier — Du pouvoir judiciaire",
    // "Chapitre IV-1. — De la chambre de l'application des peines". Both the numbering and the
    // dash are required, because "Section 3 du present reglement" is an ordinary cross-reference
    // inside a body, and mistaking one for a heading would cut an article in half.
    private static readonly Regex ContainerHead = new(
        @"^[ 	]*(Livre|Titre|Chapitre|Section|Partie)[  ]+[IVXLCDM0-9][^
]{0,12}?[  ]*[—–][  ]*[^
]{1,110}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Extraction Extract(byte[] pdf, string lexIdBase) => FromPages(ReadPages(pdf));

    /// <summary>
    /// The segmentation, over already-extracted page text. Split out from the byte entry point so
    /// it can be frozen by tests without committing a binary fixture: reading glyphs is PdfPig's
    /// job and it is version-pinned, while deciding where an article starts is ours and is the
    /// part that can quietly go wrong.
    /// </summary>
    public static Extraction FromPages(IReadOnlyList<string> pages)
    {
        var notes = new List<string>();
        if (pages.Count == 0)
        {
            notes.Add("pdf carries no extractable text layer; not extracted (profile coverage gap)");
            return new Extraction([], "", notes);
        }

        var furniture = Furniture(pages);
        if (furniture.Count > 0)
            notes.Add($"removed {furniture.Count} repeated running header/footer line(s)");

        var lines = pages
            .SelectMany(p => p.Split('\n'))
            .Select(l => l.TrimEnd())
            .Where(l => !furniture.Contains(Normalise(l)))
            .ToList();

        // Everything before the first article is the publisher's front matter: the "Texte
        // consolidé" notice, the disclaimer that consolidation has no legal value, and the list of
        // amending acts. Real content starts at the first article and not before.
        var firstArticle = lines.FindIndex(l => ArticleHead.IsMatch(l));
        if (firstArticle < 0)
        {
            notes.Add("no article heading found; document not extracted (profile coverage gap)");
            return new Extraction([], "", notes);
        }
        // Cut at the first STRUCTURAL line, not at the first article. A code opens with its Titre
        // and Chapitre above Article 1, so cutting at the article threw away the headings that
        // articles 1 to 9 belong under, and they came out with no path at all. The container
        // pattern is strict enough that the amendment list above it matches nothing.
        var firstContainer = lines.FindIndex(l => ContainerHead.IsMatch(l));
        var body0 = firstContainer >= 0 && firstContainer < firstArticle ? firstContainer : firstArticle;
        if (body0 > 0) notes.Add($"dropped {body0} line(s) of front matter");

        var md = new StringBuilder();
        var provisions = new List<Provision>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        var pathLevels = new List<int>();
        var lastPath = new List<string>();

        // One pass: containers update the current path, articles open a provision, everything
        // else is body text for whichever article is open.
        var open = false;
        string? num = null, anchor = null, heading = null;
        var body = new StringBuilder();
        var start = 0;

        void Close()
        {
            if (!open) return;
            var text = MdUtil.CollapseWs(body.ToString()).Trim();
            md.Append(text);
            var end = md.Length;
            provisions.Add(new Provision(
                Anchor: anchor!, Eli: null, Type: "article", Num: num, Heading: heading,
                Path: path.ToList(), ArticleValidFrom: null,
                TextMd: text, TextSha256: MdUtil.Sha256Hex(text),
                MdStart: start, MdEnd: end, Citations: []));
            md.Append('\n');
            body.Clear();
            open = false;
        }

        foreach (var line in lines.Skip(body0))
        {
            var art = ArticleHead.Match(line);
            if (art.Success)
            {
                Close();
                num = $"Art. {art.Groups[1].Value}.";
                // Same anchor shape as akn-lu/1 so permalinks survive a later switch to XML.
                var baseAnchor = $"art_{art.Groups[1].Value.ToLowerInvariant()}";
                anchor = baseAnchor;
                var n = 2;
                while (!seen.Add(anchor)) anchor = $"{baseAnchor}__{n++}";

                for (var i = 0; i < path.Count; i++)
                {
                    if (i < lastPath.Count && lastPath[i] == path[i]) continue;
                    md.Append('\n').Append(new string('#', Math.Min(2 + i, 5))).Append(' ').Append(path[i]).Append('\n');
                }
                lastPath = path.ToList();

                // The publisher often sets the rubric on the same line as the number:
                // "Art. 6. Sanctions à l'égard des personnes physiques". That is the heading, not
                // the opening sentence. A paragraph marker "(1)", or a long run of prose, means
                // there is no rubric and the body simply starts on this line.
                var rest = line[art.Length..].Trim();
                heading = rest.Length is > 0 and <= 120 && !rest.StartsWith('(') ? rest : null;

                md.Append("\n<a id=\"").Append(anchor).Append("\"></a>\n\n");
                md.Append("### ").Append(heading is null ? num : $"{num} {heading}").Append("\n\n");
                start = md.Length;
                open = true;
                if (heading is null && rest.Length > 0) body.Append(rest).Append('\n');
                continue;
            }

            var cont = ContainerHead.Match(line);
            if (cont.Success)
            {
                // A container heading ends the article above it. Guarding this on "no article is
                // open" was wrong: containers always sit BETWEEN articles, so the test was never
                // true after the first one and every path came out empty.
                Close();
                // Depth by kind, so a Chapitre nests under the Titre it follows rather than
                // replacing it. Only recognised between articles: the same words appear inside
                // article bodies as ordinary cross-references.
                var depth = cont.Groups[1].Value switch
                {
                    "Livre" or "Partie" => 0,
                    "Titre" => 1,
                    "Chapitre" => 2,
                    _ => 3,
                };
                // Nesting is by LEVEL, not by list position. Treating depth as an index assumed
                // every level is present, so in an act that opens at Titre with no Livre above it,
                // Chapitre II appended after Chapitre I instead of replacing it. Drop every
                // trailing entry at this level or deeper, then append.
                while (pathLevels.Count > 0 && pathLevels[^1] >= depth)
                {
                    pathLevels.RemoveAt(pathLevels.Count - 1);
                    path.RemoveAt(path.Count - 1);
                }
                pathLevels.Add(depth);
                path.Add(MdUtil.CollapseWs(line).Trim());
                continue;
            }

            if (open && line.Trim().Length > 0) body.Append(line.Trim()).Append('\n');
        }
        Close();

        if (provisions.Count == 0)
            notes.Add("no provisions extracted (profile coverage gap)");

        var markdown = md.ToString();
        return new Extraction(MdUtil.ToCodepointSpans(markdown, provisions), markdown, notes);
    }

    private static IReadOnlyList<string> ReadPages(byte[] pdf)
    {
        var pages = new List<string>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
            pages.Add(ContentOrderTextExtractor.GetText(page) ?? "");
        return pages;
    }

    /// <summary>
    /// Running headers and footers, found rather than guessed: a line repeated on more than half
    /// the pages is furniture, not law. On a 43-page act the header appears 43 times and no
    /// sentence of the act appears twice.
    /// </summary>
    private static HashSet<string> Furniture(IReadOnlyList<string> pages)
    {
        if (pages.Count < 4) return [];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in pages)
            foreach (var l in p.Split('\n').Select(Normalise).Where(l => l.Length > 8).Distinct(StringComparer.Ordinal))
                counts[l] = counts.GetValueOrDefault(l) + 1;
        var threshold = pages.Count / 2;
        return counts.Where(kv => kv.Value > threshold).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// Page numbers differ per page, so they are erased before a line is compared for repetition.
    private static string Normalise(string line) =>
        Regex.Replace(MdUtil.CollapseWs(line).Trim(), @"\d+", "#").ToLowerInvariant();
}
