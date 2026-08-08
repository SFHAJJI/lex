using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lex.Derive;

/// <summary>
/// Extraction profile "pdf-memorial-lu/2". It is a narrow successor to the frozen v1 profile
/// for official gazette texts whose real text layer uses older article typography. It never runs
/// unless v1 returned no provisions, and it refuses the issue unless the requested act can be
/// identified independently of its article text.
/// </summary>
public static class PdfMemorialLuProfileV2
{
    public const string ProfileId = "pdf-memorial-lu/2";

    private const string Marker = "textecoordonn";
    private const int IdentityWindow = 6;

    private static readonly Regex ArticleHead = new(
        "^[ \\t\\u00ab\\ufffd\\\"]*(?:Art\\.?|Article)[ \\t\\u00a0]*"
        + "(?<number>premier|Ier|unique|\\d+(?:-\\d+)*(?:er|bis|ter|quater|quinquies|sexies|septies|octies|novies|decies)?)"
        + "[ \\t\\u00a0]*\\.?[ \\t\\u00a0]*(?<separator>[-\\u2013\\u2014])?[ \\t]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AmbiguousElevenHead = new(
        "^[ \\t\\u00ab\\ufffd\\\"]*Article[ \\t\\u00a0]+II"
        + "[ \\t\\u00a0]*\\.?[ \\t\\u00a0]*(?<separator>[-\\u2013\\u2014])?[ \\t]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NextAct = new(
        @"^[ \t]*(Loi|R[èe]glement grand-ducal|Arr[êe]t[ée] grand-ducal|R[èe]glement minist[ée]riel|Arr[êe]t[ée] minist[ée]riel)\s+du\s+\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FoldedNextAct = new(
        @"^(loi|reglementgrand-ducal|arretegrand-ducal|reglementministeriel|arreteministeriel)du\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.Ordinal)
    {
        "version", "consolidee", "applicable", "loi", "reglement", "grand", "ducal", "arrete",
        "portant", "approbation", "modification", "coordination", "dispositions", "legales",
        "reglementaires", "matiere", "concernant", "relative", "relatif", "fixant", "regime",
        "entre", "dans", "pour", "avec", "sans", "sous", "date", "texte", "etat", "luxembourg"
    };

    public static Extraction Extract(byte[] pdf, string lexIdBase, string? workTitle) =>
        FromPages(PdfLuProfile.ReadPages(pdf), lexIdBase, workTitle);

    public static Extraction FromPages(
        IReadOnlyList<string> pages,
        string lexIdBase,
        string? workTitle)
    {
        var notes = new List<string>();
        if (pages.Count == 0)
        {
            notes.Add("pdf carries no extractable text layer; not extracted (profile coverage gap)");
            return new Extraction([], "", notes);
        }

        var furniture = PdfLuProfile.Furniture(pages);
        var lines = pages
            .SelectMany(page => page.Split('\n'))
            .Select(line => line.TrimEnd())
            // Generic furniture normalization erases digits, so standalone "Article 1",
            // "Article 2", ... all collapse to "article #" and look like a repeated header.
            // A recognized legal boundary is never furniture in this profile.
            .Where(line => ArticleHead.IsMatch(line)
                           || AmbiguousElevenHead.IsMatch(line)
                           || !furniture.Contains(PdfLuProfile.Normalise(line)))
            .ToList();
        var folded = lines.Select(PdfMemorialLuProfile.Fold).ToList();

        var strongNeedle = PdfMemorialLuProfile.Needles(lexIdBase).FirstOrDefault();
        var identity = strongNeedle is null
            ? []
            : FindWindows(folded, window => window.Contains(strongNeedle, StringComparison.Ordinal));
        var identityNote = "publisher heading contains the requested instrument kind and date";
        var identityFromTitle = false;

        if (identity.Count == 0)
        {
            identityFromTitle = true;
            var titleTokens = TitleTokens(workTitle);
            identity = FindWindows(folded, window => MatchesTitle(window, titleTokens));
            identityNote = "publisher heading matches a distinctive title fingerprint";
        }

        if (identity.Count == 0)
        {
            notes.Add("the requested act's identity could not be verified in this gazette issue; not extracted");
            return new Extraction([], "", notes);
        }
        notes.Add(identityNote);

        var markers = Enumerable.Range(0, folded.Count)
            .Where(index => folded[index].Contains(Marker, StringComparison.Ordinal))
            .Where(marker => identity.Any(found => found <= marker && marker - found < IdentityWindow + 4))
            .ToList();

        // Work from the latest verified marker backwards. A gazette commonly prints the target in
        // its contents, then an amending act, then the actual consolidated text. The latest marker
        // is the one beside the text; choosing the first can return a quotation inside the amendment.
        foreach (var marker in identityFromTitle ? Enumerable.Empty<int>() : markers.OrderDescending())
        {
            var nextMarker = markers.Where(other => other > marker).DefaultIfEmpty(lines.Count).Min();
            var start = FindFirstArticle(lines, marker + 1, nextMarker);
            if (start < 0 || !IsFirstArticle(lines[start])) continue;
            var attempt = ParseArticleSlice(lines, start, new List<string>(notes));
            if (!HasRepeatedArticleIdentity(attempt)) return attempt;
            notes.Add("repeated article identity makes provision segmentation ambiguous; used one document boundary");
            return ParseDocumentSlice(lines, marker, start, notes,
                "verified gazette section exposed as one document because an approving act and its annex restart article numbering");
        }

        // Some old official publications use a distinctive title but no separate coordinated-text
        // marker. Only a title identity close to Article 1 is accepted.
        foreach (var found in identity.Order())
        {
            var start = FindFirstArticle(lines, found, Math.Min(lines.Count, found + 120));
            if (start < 0 || !IsFirstArticle(lines[start])) continue;
            var attempt = ParseArticleSlice(lines, start, new List<string>(notes));
            if (!HasRepeatedArticleIdentity(attempt)) return attempt;
            notes.Add("repeated article identity makes provision segmentation ambiguous; used one document boundary");
            return ParseDocumentSlice(lines, found, start, notes,
                "verified gazette section exposed as one document because article numbering restarts");
        }

        // The 1999 classified-establishments nomenclature is an official consolidated table, not
        // an article-shaped act. A document boundary is allowed only when a verified target marker
        // is followed by its unmistakable publisher table heading.
        foreach (var marker in markers.OrderDescending())
        {
            var table = Enumerable.Range(marker, lines.Count - marker)
                .FirstOrDefault(index => IsNomenclatureHeader(folded[index]), -1);
            if (table < 0) continue;
            return ParseDocumentSlice(lines, marker, table, notes,
                "publisher text is an articleless nomenclature; exposed as one document-level provision");
        }

        notes.Add("the requested act was identified, but no supported Article 1 or nomenclature boundary was found; not extracted");
        return new Extraction([], "", notes);
    }

    private static List<int> FindWindows(
        IReadOnlyList<string> folded,
        Func<string, bool> matches)
    {
        var found = new List<int>();
        for (var start = 0; start < folded.Count; start++)
        {
            var window = new StringBuilder();
            for (var length = 0; length < IdentityWindow && start + length < folded.Count; length++)
            {
                window.Append(folded[start + length]);
                if (matches(window.ToString()))
                {
                    found.Add(start);
                    break;
                }
            }
        }
        return found;
    }

    private static IReadOnlyList<string> TitleTokens(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];
        return Regex.Matches(title, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)
            .Select(match => PdfMemorialLuProfile.Fold(match.Value))
            .Where(token => token.Length >= 5 && !TitleStopWords.Contains(token) && !token.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool MatchesTitle(string foldedWindow, IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3) return false;
        var matched = tokens.Where(token => foldedWindow.Contains(token, StringComparison.Ordinal)).ToList();
        return matched.Count >= 2
               && matched.Sum(token => token.Length) >= 15
               && matched.Any(token => token.Length >= 8);
    }

    private static int FindFirstArticle(IReadOnlyList<string> lines, int start, int end)
    {
        for (var index = start; index < end; index++)
            if (ArticleHead.IsMatch(lines[index]))
                return index;
        return -1;
    }

    private static bool IsFirstArticle(string line)
    {
        var match = ArticleHead.Match(line);
        if (!match.Success) return false;
        return NormaliseNumber(match.Groups["number"].Value) is "1" or "1er";
    }

    private static Extraction ParseArticleSlice(IReadOnlyList<string> lines, int start, List<string> notes)
    {
        var end = FindEnd(lines, start);
        var normalized = NormaliseArticleLines(lines.Skip(start).Take(end - start).ToList());
        notes.Add($"took lines {start}..{end} of {lines.Count} from the verified gazette section");
        notes.Add("older article typography normalized in the derived DOM; official source bytes remain unchanged");
        return PdfLuProfile.ParseBody(normalized, notes);
    }

    private static IEnumerable<string> NormaliseArticleHead(string line)
    {
        var match = ArticleHead.Match(line);
        if (!match.Success) return [line];
        var number = NormaliseNumber(match.Groups["number"].Value);
        if (number == "unique") return [line];
        return NormaliseArticleHead(line, match, number);
    }

    private static IEnumerable<string> NormaliseArticleHead(string line, Match match, string number)
    {
        var rest = line[match.Length..].Trim();
        if (rest.Length == 0) return [$"Art. {number}."];
        // A publisher dash explicitly separates a rubric ("Article 2 - Organismes") from the
        // article number. Without it, the remainder is the opening legal sentence, not a heading.
        return match.Groups["separator"].Success
            ? [$"Art. {number}. {rest}"]
            : [$"Art. {number}.", rest];
    }

    private static List<string> NormaliseArticleLines(IReadOnlyList<string> lines)
    {
        var normalized = new List<string>(lines.Count);
        var seen = new HashSet<int>();
        var highest = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var ambiguousEleven = AmbiguousElevenHead.Match(lines[index]);
            if (ambiguousEleven.Success && highest == 10)
            {
                var nextArticle = lines.Skip(index + 1).Select(ArticleNumber)
                    .FirstOrDefault(value => value is not null);
                if (nextArticle == 12)
                {
                    // Some old fonts yield the two digits in "Article 11" as capital I glyphs.
                    // The surrounding Arabic sequence proves the value; an unconstrained Roman
                    // conversion would misread amendment wrappers such as Article II.
                    normalized.AddRange(NormaliseArticleHead(lines[index], ambiguousEleven, "11"));
                    seen.Add(11);
                    highest = 11;
                    continue;
                }
            }

            var number = ArticleNumber(lines[index]);
            if (number is not null && number < highest && seen.Contains(number.Value))
            {
                var next = lines.Skip(index + 1).Select(ArticleNumber).FirstOrDefault(value => value is not null);
                if (next > highest)
                {
                    // This is an amendment instruction inside the open article (for example Art.
                    // 39 quoting "Art. 5" before the real Art. 40), not a second provision. A
                    // leading newline keeps the visible official words while preventing the
                    // frozen parser from treating the quotation as a boundary.
                    normalized.Add("\n" + lines[index]);
                    continue;
                }
            }

            normalized.AddRange(NormaliseArticleHead(lines[index]));
            if (number is null) continue;
            seen.Add(number.Value);
            highest = Math.Max(highest, number.Value);
        }
        return normalized;
    }

    private static int? ArticleNumber(string line)
    {
        var match = ArticleHead.Match(line);
        if (!match.Success) return null;
        var number = NormaliseNumber(match.Groups["number"].Value);
        var digits = Regex.Match(number, @"^\d+", RegexOptions.CultureInvariant);
        return digits.Success
               && int.TryParse(digits.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string NormaliseNumber(string value) => value.ToLowerInvariant() switch
    {
        "ier" or "premier" => "1er",
        var number => number,
    };

    private static Extraction ParseDocumentSlice(
        IReadOnlyList<string> lines,
        int start,
        int boundaryProbeStart,
        List<string> notes,
        string boundaryNote)
    {
        var end = FindEnd(lines, boundaryProbeStart);
        var text = string.Join('\n', lines.Skip(start).Take(end - start)
            .Select(MdUtil.CollapseWs)
            .Where(line => line.Length > 0));
        const string anchor = "document";
        const string prefix = "<a id=\"document\"></a>\n\n### Document\n\n";
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
        notes.Add($"took lines {start}..{end} of {lines.Count} from the verified gazette section");
        notes.Add(boundaryNote);
        return new Extraction(MdUtil.ToCodepointSpans(markdown, [provision]), markdown, notes);
    }

    private static int FindEnd(IReadOnlyList<string> lines, int contentStart)
    {
        var firstArticle = ArticleNumber(lines[contentStart]);
        var highestArticle = firstArticle ?? 0;
        var sawArticleOne = firstArticle == 1;
        var previousArticle = contentStart;
        for (var index = contentStart + 1; index < lines.Count; index++)
        {
            var folded = PdfMemorialLuProfile.Fold(lines[index]);
            if (NextAct.IsMatch(lines[index])
                || folded.StartsWith("tableaudeconcordance", StringComparison.Ordinal)
                || folded.StartsWith("tabledeconcordance", StringComparison.Ordinal))
                return index;

            var article = ArticleNumber(lines[index]);
            if (article is null) continue;
            if (article == 1 && sawArticleOne && highestArticle >= 2)
            {
                // A second instrument may follow in the same issue without spaces in its heading.
                // Accept that boundary only when the numbering reset is independently visible;
                // lowercase "loi du ..." lines inside an article are ordinary amendment text.
                for (var candidate = previousArticle + 1; candidate < index; candidate++)
                    if (FoldedNextAct.IsMatch(PdfMemorialLuProfile.Fold(lines[candidate])))
                        return candidate;
            }
            if (article == 1) sawArticleOne = true;
            highestArticle = Math.Max(highestArticle, article.Value);
            previousArticle = index;
        }
        return lines.Count;
    }

    private static bool IsNomenclatureHeader(string foldedLine) =>
        foldedLine.Contains("designation", StringComparison.Ordinal)
        && foldedLine.Contains("classification", StringComparison.Ordinal)
        && foldedLine.Contains("classe", StringComparison.Ordinal);

    private static bool HasRepeatedArticleIdentity(Extraction extraction) =>
        extraction.Provisions.Any(provision => Regex.IsMatch(
            provision.Anchor, @"__\d+$", RegexOptions.CultureInvariant));
}
