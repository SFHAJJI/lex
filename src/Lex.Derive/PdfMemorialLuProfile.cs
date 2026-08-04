using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "pdf-memorial-lu/1": one act, cut out of a whole issue of the official
/// gazette.
///
/// Why this exists (D49). For 176 consolidations Legilux offers no XML and no per-act PDF. What it
/// points at is the Mémorial issue the text appeared in, which is the entire day's Journal officiel
/// and may hold several unrelated acts. Measured across all 176: every one carries a real text
/// layer, so nothing here is OCR. The whole difficulty is knowing where our act begins and ends.
///
/// The gazette answers that itself, in its own typography:
///
///     Sommaire                                  <- table of contents, the front matter
///        Texte coordonné du 29 octobre 1992 de la loi du 19 février 1973 ...
///     ...
///     Texte coordonné                           <- the consolidated text starts here
///     Art. 1er. Le Grand-Duc réglementera ...
///
/// So: find where OUR act is named, take the consolidated-text marker at or after it, and stop at
/// the next act's heading. From there the work is identical to pdf-lu/1 and is shared with it.
///
/// MATCHING IS DONE ON DE-SPACED TEXT. The gazette tracks out its headings, and extraction turns
/// that into "T exte coordonn é" and "1 9 f é v r i e r". Any needle containing spaces fails on
/// exactly the documents we need. Removing every space and accent from both sides defeats it,
/// which took the location rate from 86% to 95% across the corpus.
///
/// CONFIDENCE is lower again than pdf-lu/1, and that is the point of it being its own profile.
/// There the risk was reading an article boundary wrong inside one known act. Here the risk is
/// taking the wrong act entirely, so when the act cannot be located the profile extracts NOTHING
/// rather than return a confident guess.
/// </summary>
public static class PdfMemorialLuProfile
{
    public const string ProfileId = "pdf-memorial-lu/1";

    private static readonly string[] Months =
        ["janvier", "fevrier", "mars", "avril", "mai", "juin",
         "juillet", "aout", "septembre", "octobre", "novembre", "decembre"];

    /// The publisher's own words for each instrument, as they appear in a gazette heading.
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["loi"] = "loi",
        ["rgd"] = "reglementgrand-ducal",
        ["rmin"] = "reglementministeriel",
        ["amin"] = "arreteministeriel",
        ["agd"] = "arretegrand-ducal",
        ["rgc"] = "reglementdugouvernementenconseil",
        ["agc"] = "arretedugouvernementenconseil",
        ["code"] = "code",
    };

    /// Where a consolidated text starts. De-spaced, so the tracked-out setting still matches.
    private const string Marker = "textecoordonn";

    /// The start of a DIFFERENT act, which is where ours ends.
    private static readonly Regex NextAct = new(
        @"^[ \t]*(Loi|R[èe]glement grand-ducal|Arr[êe]t[ée] grand-ducal|R[èe]glement minist[ée]riel|Arr[êe]t[ée] minist[ée]riel)\s+du\s+\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <param name="lexIdBase">"lu-legilux:loi-1973-02-19-n1:1992-03-30". The work slug in the
    /// middle carries the act's kind and date, which is exactly what has to be found in the
    /// issue, so no extra plumbing is needed to tell this profile what it is looking for.</param>
    public static Extraction Extract(byte[] pdf, string lexIdBase) =>
        FromPages(PdfLuProfile.ReadPages(pdf), lexIdBase);

    public static Extraction FromPages(IReadOnlyList<string> pages, string lexIdBase)
    {
        var notes = new List<string>();
        if (pages.Count == 0)
        {
            notes.Add("pdf carries no extractable text layer; not extracted (profile coverage gap)");
            return new Extraction([], "", notes);
        }

        var furniture = PdfLuProfile.Furniture(pages);
        var lines = pages
            .SelectMany(p => p.Split('\n'))
            .Select(l => l.TrimEnd())
            .Where(l => !furniture.Contains(PdfLuProfile.Normalise(l)))
            .ToList();

        var needles = Needles(lexIdBase);
        if (needles.Count == 0)
        {
            notes.Add($"cannot derive the act's identity from '{lexIdBase}'; not extracted");
            return new Extraction([], "", notes);
        }

        // Fold each line once; every search below runs over the folded form.
        var folded = lines.Select(Fold).ToList();

        // Where is our act named? Not the Sommaire entry if we can help it: the LAST mention
        // before a consolidated-text marker is the act's own heading, the earlier ones are the
        // table of contents pointing at it.
        var named = new List<int>();
        var used = needles[0];
        foreach (var n in needles)
        {
            named = Enumerable.Range(0, folded.Count)
                .Where(i => folded[i].Contains(n, StringComparison.Ordinal)).ToList();
            if (named.Count > 0) { used = n; break; }
        }
        if (named.Count == 0)
        {
            notes.Add($"the act is not named in this issue (looked for '{string.Join("' or '", needles)}'); not extracted");
            return new Extraction([], "", notes);
        }
        if (!ReferenceEquals(used, needles[0]))
            notes.Add("located by date alone; the issue does not name the instrument as the ELI does");

        // Where the consolidated text begins.
        //
        // Taking the first marker after the act is named looks right and is not: an issue carries
        // several "texte coordonné" markers, in the Sommaire, in running heads, and before each
        // act, and picking the wrong one starts the reader in the middle of the law. Measured over
        // the corpus, 24 of 171 versions came out beginning at Art. 32, Art. 14, Art. 5.
        //
        // A consolidated act always opens at Article 1. So a candidate start is only accepted when
        // the first article after it IS Article 1, which is a fact about law rather than about
        // this publisher's typography.
        var markers = Enumerable.Range(0, folded.Count)
            .Where(i => folded[i].Contains(Marker, StringComparison.Ordinal)).ToList();
        var candidates = markers.Select(i => i + 1).ToList();
        // A heading without its marker still starts a text, so the first article after each
        // mention of the act is a candidate too.
        candidates.AddRange(named);
        candidates = candidates.Where(i => i < lines.Count).Distinct().OrderBy(i => i).ToList();

        var start = -1;
        foreach (var pass in new[] { true, false })          // first insist the act is named above
            foreach (var cand in candidates)
            {
                if (pass && !named.Any(n => n <= cand)) continue;
                var firstArt = Enumerable.Range(cand, lines.Count - cand)
                    .FirstOrDefault(i => PdfLuProfile.ArticleHead.IsMatch(lines[i]), -1);
                if (firstArt < 0) continue;
                var num = PdfLuProfile.ArticleHead.Match(lines[firstArt]).Groups[1].Value;
                if (num is not ("1" or "1er")) continue;
                start = firstArt;
                break;
            }
        if (start < 0)
        {
            notes.Add("no consolidated text starting at Article 1 could be located in this issue; "
                    + "not extracted (profile coverage gap)");
            return new Extraction([], "", notes);
        }

        // And it ends where the next act begins, if one does.
        var end = lines.Count;
        for (var i = start; i < lines.Count; i++)
        {
            if (!NextAct.IsMatch(lines[i])) continue;
            // Only past our own first article: the heading block itself often repeats the act's
            // own title, and stopping there would yield nothing.
            if (!lines.Take(i).Skip(start).Any(l => PdfLuProfile.ArticleHead.IsMatch(l))) continue;
            end = i;
            notes.Add($"stopped at the next act's heading, {i - start} line(s) in");
            break;
        }

        notes.Add($"took lines {start}..{end} of {lines.Count} from the gazette issue");
        var slice = lines.Skip(start).Take(end - start).ToList();
        var extraction = PdfLuProfile.ParseBody(slice, notes);
        if (extraction.Provisions.Count == 0)
            notes.Add("no article found inside the located slice (profile coverage gap)");
        return extraction;
    }

    /// <summary>
    /// The ways this act may be named in a gazette, most specific first.
    ///
    /// "lu-legilux:loi-1973-02-19-n1:1992-03-30" -> "loidu19fevrier1973", then "du19fevrier1973".
    /// The date alone is the fallback because the issue does not always use the same word for the
    /// instrument as the ELI does, and a date plus a year is already a strong enough signal inside
    /// one issue of one day's journal.
    /// </summary>
    public static IReadOnlyList<string> Needles(string lexIdBase)
    {
        var parts = lexIdBase.Split(':');
        var slug = parts.Length >= 2 ? parts[1] : lexIdBase;
        var m = Regex.Match(slug, @"^([a-z]+)-(\d{4})-(\d{2})-(\d{2})");
        if (!m.Success) return [];
        var month = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return [];
        var day = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        // French writes the first of the month as "1er", never "1". Getting this wrong cost the
        // reglement du Gouvernement en Conseil du 1er mars 1974 and every other day-one act.
        var d = day == 1 ? "1er" : day.ToString(CultureInfo.InvariantCulture);
        var date = $"du {d} {Months[month - 1]} {m.Groups[2].Value}";
        var kind = Kinds.GetValueOrDefault(m.Groups[1].Value);
        return kind is null ? [Fold(date)] : [Fold($"{kind} {date}"), Fold(date)];
    }

    /// Lowercase, strip accents, remove every space. See the note on tracked-out headings.
    internal static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
