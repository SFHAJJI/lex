using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One article as the index holds it: the publisher's identifier, the text it serves, that text's digest and its typed pieces.</summary>
public sealed record V4ArticleSource(string PublisherId, string Text, string TextSha256, string TokensJson);

/// <summary>
/// One line of an act (brick B, <c>line_df</c>): a piece of an article at a position inside the article's stored text. The
/// lines of an article, in order, cover that text; a quote of a line is "these characters at this position", which is what the
/// gate <c>QuoteBound</c> checks.
/// </summary>
/// <param name="Id">The article's identifier, a hash and the line's number in the article ("art_39#7").</param>
/// <param name="Kind">label, heading, marker or paragraph.</param>
/// <param name="Point">The numbered point the line stands under ("3.", "(1) a)"), or null.</param>
/// <param name="Offset">Where the line starts in the article's stored text.</param>
/// <param name="Length">How many UTF-16 units it holds there, with no white space at either end.</param>
public sealed record V4Line(
    string Id,
    string Article,
    int Index,
    string Kind,
    string? Point,
    int Offset,
    int Length,
    string Text,
    string Folded,
    IReadOnlyList<V4Word> Words);

/// <summary>One article of an act's outline (brick B, <c>toc_df</c>): its label and heading as published, and its lines.</summary>
/// <param name="Status">in_force, empty (no text), or unbound (its pieces do not give back its text: quoted whole, never by line).</param>
/// <param name="Source">How the outline row was read: publisher_structure or text_pattern.</param>
public sealed record V4Article(
    string Id,
    int Position,
    string? Label,
    string? Heading,
    string HeadingFolded,
    IReadOnlyList<V4Word> HeadingWords,
    string Status,
    string Source,
    int FirstLine,
    int LineCount,
    string TextSha256,
    int TextLength);

/// <summary>A term an act defines (brick B, <c>definitions_df</c>): the term as the law writes it and the lines that define it.</summary>
/// <param name="Method">term_colon (a line "Term: ..." under "on entend par") or on_entend_par (a sentence "on entend par X ...").</param>
public sealed record V4Definition(string Term, string TermFolded, string Article, int FirstLine, int LastLine, string Method);

/// <summary>A reference made in the text in plain words (brick B, <c>cross_ref_df</c>).</summary>
/// <param name="Kind">article (an article of the same act), external (an article of another act) or unnamed_cases ("les cas prévus par la loi").</param>
public sealed record V4CrossReference(string FromLine, string FromArticle, string Kind, string? Target, bool Resolved);

/// <summary>What the build counted on an act (brick B, <c>parsing_summary</c>), with no model: every number goes to the quality page.</summary>
public sealed record V4ParsingSummary(
    int Articles,
    int EmptyArticles,
    int UnboundArticles,
    int Lines,
    int Headings,
    int Markers,
    int Paragraphs,
    int JoinedPieces,
    int Definitions,
    int ReferencesResolved,
    int ReferencesUnresolved,
    int ReferencesExternal,
    IReadOnlyList<string> NumberingNotFromOne,
    IReadOnlyList<string> LabelsNotCanonical,
    string Levels);

/// <summary>
/// The tables of one act at one version (brick B): outline, lines, definitions, references and the build's summary. "You open
/// the PDF once. After that, you only work with tables." [5B] Built by code from the typed pieces the index keeps; nothing is
/// downloaded and no model is consulted.
/// </summary>
public sealed class V4ActTables
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex LabelPattern = new(@"^\s*art\w{0,6}\.?\s*(?:[0-9]|[LRDA]\.?\s*[0-9])", Options | RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex CanonicalLabel = new(@"^\s*(?:Article|Art\.?)\s", Options, Timeout);
    private static readonly Regex MarkerPattern = new(@"^(?:\(\d{1,3}\)|\d{1,3}\.|\d{1,3}°|[a-z]\)|\([a-z]\)|[ivxl]{1,5}\)|[-–—•])$", Options, Timeout);
    private static readonly Regex NumericMarker = new(@"^(?:\((\d{1,3})\)|(\d{1,3})\.|(\d{1,3})°)$", Options, Timeout);
    private static readonly Regex InlineMarker = new(@"^\s*(\(\d{1,3}\)|\d{1,3}\.|\d{1,3}°)\s+\S", Options, Timeout);
    private static readonly Regex TermColon = new(@"^\s*[«""“]?\s*(?<term>[^:«»""“”]{2,110}?)\s*[»""”]?\s*:\s*(?<rest>.*)$", Options | RegexOptions.Singleline, Timeout);
    private static readonly Regex OnEntendPar = new(
        @"on entend par\s+(?:[«""]\s*)?(?<term>[^»"":,;.]{3,80}?)(?:\s*[»""])?\s+(?:tout|toute|toutes|tous|un|une|le|la|les|l')",
        Options, Timeout);
    private static readonly Regex OpensList = new(@"on entend par\s*:\s*$", Options, Timeout);
    private static readonly Regex ArticleMention = new(
        @"\barticles?\s+(?<first>\d{1,3}(?:er)?(?:-\d{1,3})?(?:\s?(?:bis|ter|quater))?)(?<more>(?:\s*(?:,|et|a|ou)\s+\d{1,3}(?:er)?(?:-\d{1,3})?)*)(?<tail>[^.;]{0,60})",
        Options, Timeout);
    private static readonly Regex MoreNumbers = new(@"\d{1,3}(?:er)?(?:-\d{1,3})?", Options, Timeout);
    private static readonly Regex ExternalTail = new(
        @"^\s*(?:,\s*(?:point|paragraphe|alinea|lettre)[^,]{0,30})*\s*,?\s*(?:de la|du|de l'|des)\s+(?:loi|code|reglement|directive|convention|arrete|traite)\b",
        Options, Timeout);
    private static readonly Regex UnnamedCases = new(@"\bcas prevus? par la (?:presente )?loi\b", Options, Timeout);

    private static readonly HashSet<string> OrdinalSuffixes = new(StringComparer.Ordinal) { "er", "re", "ere", "e", "eme", "nd", "nde", "de" };

    private readonly Dictionary<string, int> _articleIndex;
    private readonly Dictionary<string, V4Line> _lineById;

    private V4ActTables(
        string work,
        string stateSha256,
        string date,
        string language,
        List<V4Article> articles,
        List<V4Line> lines,
        List<V4Definition> definitions,
        List<V4CrossReference> references,
        V4ParsingSummary summary)
    {
        Work = work;
        StateSha256 = stateSha256;
        Date = date;
        Language = language;
        Articles = articles.AsReadOnly();
        Lines = lines.AsReadOnly();
        Definitions = definitions.AsReadOnly();
        References = references.AsReadOnly();
        Summary = summary;
        _articleIndex = articles.Select(static (article, index) => (article.Id, index)).ToDictionary(static pair => pair.Id, static pair => pair.index, StringComparer.Ordinal);
        _lineById = lines.ToDictionary(static line => line.Id, StringComparer.Ordinal);
    }

    /// <summary>The act's address on this site ("/lu-legilux/loi-1997-07-27-n10").</summary>
    public string Work { get; }

    /// <summary>The digest of the version these tables were built from: the version the evidence step reads on the same date.</summary>
    public string StateSha256 { get; }

    /// <summary>The date the version applies from.</summary>
    public string Date { get; }

    public string Language { get; }

    public IReadOnlyList<V4Article> Articles { get; }

    public IReadOnlyList<V4Line> Lines { get; }

    public IReadOnlyList<V4Definition> Definitions { get; }

    public IReadOnlyList<V4CrossReference> References { get; }

    public V4ParsingSummary Summary { get; }

    public V4Article? Article(string id) => _articleIndex.TryGetValue(id, out var index) ? Articles[index] : null;

    public V4Line? Line(string id) => _lineById.GetValueOrDefault(id);

    /// <summary>The lines of an article, in order.</summary>
    public IEnumerable<V4Line> LinesOf(V4Article article)
    {
        ArgumentNullException.ThrowIfNull(article);
        for (var at = 0; at < article.LineCount; at++)
        {
            yield return Lines[article.FirstLine + at];
        }
    }

    /// <summary>The lines of an article that are its text: neither its label, nor its heading, nor a bare marker.</summary>
    public IEnumerable<V4Line> TextLinesOf(V4Article article) => LinesOf(article).Where(static line => line.Kind == "paragraph");

    private sealed record Piece(int Offset, int Length, bool Reference);

    /// <summary>
    /// Builds the tables of one version of one act from its articles, in the publisher's order. An article whose pieces do
    /// not give back its stored text exactly is kept whole (status unbound) and is never quoted by line.
    /// </summary>
    public static V4ActTables Build(string work, string stateSha256, string date, string language, IReadOnlyList<V4ArticleSource> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(work);
        ArgumentNullException.ThrowIfNull(stateSha256);
        ArgumentNullException.ThrowIfNull(date);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(sources);
        var articles = new List<V4Article>(sources.Count);
        var lines = new List<V4Line>();
        var joined = 0;
        var numbering = new List<string>();
        var labels = new List<string>();
        for (var position = 0; position < sources.Count; position++)
        {
            var source = sources[position];
            var first = lines.Count;
            if (source.Text.Length == 0)
            {
                articles.Add(new V4Article(source.PublisherId, position, null, null, string.Empty, [], "empty", "publisher_structure", first, 0, source.TextSha256, 0));
                continue;
            }

            var pieces = PiecesOf(source);
            if (pieces is null)
            {
                var whole = Trimmed(source.Text, 0, source.Text.Length);
                lines.Add(NewLine(source, 0, "paragraph", null, whole.Offset, whole.Length));
                articles.Add(new V4Article(source.PublisherId, position, null, null, string.Empty, [], "unbound", "text_pattern", first, 1, source.TextSha256, source.Text.Length));
                continue;
            }

            var runs = RunsOf(source.Text, pieces, ref joined);
            string? label = null;
            string? heading = null;
            string? point = null;
            string? item = null;
            var firstNumber = true;
            for (var at = 0; at < runs.Count; at++)
            {
                var (offset, length, single) = runs[at];
                var span = Trimmed(source.Text, offset, length);
                if (span.Length == 0)
                {
                    continue;
                }

                var text = source.Text.Substring(span.Offset, span.Length);
                string kind;
                if (at == 0 && LabelPattern.IsMatch(text))
                {
                    kind = "label";
                    label = text;
                    if (!CanonicalLabel.IsMatch(text))
                    {
                        labels.Add(source.PublisherId);
                    }
                }
                else if (MarkerPattern.IsMatch(text))
                {
                    kind = "marker";
                    if (NumericMarker.Match(text) is { Success: true } numeric)
                    {
                        point = text;
                        item = null;
                        if (firstNumber)
                        {
                            firstNumber = false;
                            var number = numeric.Groups[1].Success ? numeric.Groups[1].Value : numeric.Groups[2].Success ? numeric.Groups[2].Value : numeric.Groups[3].Value;
                            if (number != "1")
                            {
                                numbering.Add(source.PublisherId);
                            }
                        }
                    }
                    else
                    {
                        item = text;
                    }
                }
                else if (label is not null && heading is null && lines.Count == first + 1 && single && IsHeading(text))
                {
                    kind = "heading";
                    heading = text;
                }
                else
                {
                    kind = "paragraph";
                    if (InlineMarker.Match(text) is { Success: true } inline)
                    {
                        point = inline.Groups[1].Value;
                        item = null;
                        firstNumber = false;
                    }
                }

                var under = kind is "label" or "heading" ? null : point is null ? item : item is null ? point : point + " " + item;
                lines.Add(NewLine(source, lines.Count - first, kind, under, span.Offset, span.Length));
            }

            var headingFolded = heading is null ? string.Empty : V4Text.Fold(heading);
            articles.Add(new V4Article(
                source.PublisherId, position, label, heading, headingFolded, V4Text.Words(headingFolded), "in_force", "text_pattern",
                first, lines.Count - first, source.TextSha256, source.Text.Length));
        }

        var articleIds = articles.Select(static article => article.Id).ToHashSet(StringComparer.Ordinal);
        var definitions = DefinitionsOf(articles, lines);
        var references = ReferencesOf(lines, articleIds);
        var summary = new V4ParsingSummary(
            articles.Count,
            articles.Count(static article => article.Status == "empty"),
            articles.Count(static article => article.Status == "unbound"),
            lines.Count,
            lines.Count(static line => line.Kind == "heading"),
            lines.Count(static line => line.Kind == "marker"),
            lines.Count(static line => line.Kind == "paragraph"),
            joined,
            definitions.Count,
            references.Count(static reference => reference.Kind == "article" && reference.Resolved),
            references.Count(static reference => reference.Kind == "article" && !reference.Resolved),
            references.Count(static reference => reference.Kind == "external"),
            numbering.AsReadOnly(),
            labels.AsReadOnly(),
            "article");
        return new V4ActTables(work, stateSha256, date, language, articles, lines, definitions, references, summary);
    }

    private static V4Line NewLine(V4ArticleSource source, int index, string kind, string? point, int offset, int length)
    {
        var text = source.Text.Substring(offset, length);
        var folded = V4Text.Fold(text);
        return new V4Line(
            source.PublisherId + "#" + index.ToString(CultureInfo.InvariantCulture), source.PublisherId, index, kind, point, offset, length, text, folded, V4Text.Words(folded));
    }

    private static (int Offset, int Length) Trimmed(string text, int offset, int length)
    {
        var start = offset;
        var end = offset + length;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return (start, end - start);
    }

    /// <summary>The article's text and reference pieces with their positions, or null when they do not concatenate to its stored text.</summary>
    private static List<Piece>? PiecesOf(V4ArticleSource source)
    {
        var pieces = new List<Piece>();
        var at = 0;
        try
        {
            using var document = JsonDocument.Parse(source.TokensJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var token in document.RootElement.EnumerateArray())
            {
                var kind = token.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
                if (kind is not ("text" or "reference"))
                {
                    continue;
                }

                var value = token.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String ? textElement.GetString()! : string.Empty;
                if (value.Length == 0)
                {
                    continue;
                }

                if (at + value.Length > source.Text.Length || string.CompareOrdinal(source.Text, at, value, 0, value.Length) != 0)
                {
                    return null;
                }

                pieces.Add(new Piece(at, value.Length, kind == "reference"));
                at += value.Length;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return at == source.Text.Length ? pieces : null;
    }

    /// <summary>
    /// The pieces joined into lines. A reference the publisher marked is inside a sentence, and so is the text after it; an
    /// ordinal's suffix ("1" then "er") belongs to its number; a piece that meets the one before it at a space or a closing
    /// mark, while that one has not ended its sentence, continues it (a word the publisher set in italics). Everything else
    /// starts a line.
    /// </summary>
    private static List<(int Offset, int Length, bool Single)> RunsOf(string text, List<Piece> pieces, ref int joined)
    {
        var runs = new List<(int Offset, int Length, bool Single)>();
        var afterReference = false;
        var afterSuffix = false;
        foreach (var piece in pieces)
        {
            var value = text.AsSpan(piece.Offset, piece.Length);
            var continues = false;
            if (runs.Count > 0)
            {
                var (offset, length, _) = runs[^1];
                var current = text.AsSpan(offset, length);
                var isLabel = runs.Count == 1 && LabelPattern.IsMatch(text.Substring(offset, Math.Min(length, 40)));
                var suffix = IsOrdinalSuffix(value) && current.Length > 0 && char.IsAsciiDigit(current[^1]);
                if (isLabel)
                {
                    continues = suffix || OnlyPunctuation(value);
                }
                else if (piece.Reference || afterReference || suffix)
                {
                    continues = true;
                }
                else if (EndsSentence(current))
                {
                    continues = false;
                }
                else
                {
                    continues = char.IsWhiteSpace(current[^1]) || char.IsWhiteSpace(value[0]) || value[0] is ',' or ';' or '.' or ':' or ')' or '»' or '”' or '"' or '/'
                        || (afterSuffix && char.IsLower(value[0]));
                }

                afterSuffix = suffix;
            }

            if (continues)
            {
                var (offset, length, _) = runs[^1];
                runs[^1] = (offset, length + piece.Length, false);
                joined++;
            }
            else
            {
                runs.Add((piece.Offset, piece.Length, !piece.Reference));
                afterSuffix = false;
            }

            afterReference = piece.Reference;
        }

        return runs;
    }

    private static bool IsOrdinalSuffix(ReadOnlySpan<char> value)
    {
        if (value.Length is < 1 or > 3)
        {
            return false;
        }

        Span<char> folded = stackalloc char[value.Length];
        for (var at = 0; at < value.Length; at++)
        {
            folded[at] = char.ToLowerInvariant(value[at]) switch { 'è' or 'é' => 'e', var other => other };
        }

        return OrdinalSuffixes.Contains(folded.ToString());
    }

    private static bool OnlyPunctuation(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!(char.IsPunctuation(character) || char.IsWhiteSpace(character) || character is '-' or '–' or '—'))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    private static bool EndsSentence(ReadOnlySpan<char> current)
    {
        var end = current.Length;
        while (end > 0 && (char.IsWhiteSpace(current[end - 1]) || current[end - 1] is '»' or '”' or '"' or ')'))
        {
            end--;
        }

        return end > 0 && current[end - 1] is '.' or ';' or ':' or '!' or '?';
    }

    private static bool IsHeading(string text) =>
        text.Length <= 200 && text[^1] is not ('.' or ';' or ':' or ',') && (char.IsUpper(text[0]) || char.IsAsciiDigit(text[0]) || text[0] is '«' or '"');

    /// <summary>
    /// The terms the act defines. A line that says "on entend par" and ends on a colon opens a list: each following line that
    /// reads "Term: ..." defines a term, and a line that does not continues the definition before it. A sentence that says
    /// "on entend par X tout ..." defines X on its own line.
    /// </summary>
    private static List<V4Definition> DefinitionsOf(List<V4Article> articles, List<V4Line> lines)
    {
        var definitions = new List<V4Definition>();
        foreach (var article in articles)
        {
            if (article.Status != "in_force")
            {
                continue;
            }

            var open = false;
            bool? quoted = null;
            (string Term, int First, int Last)? current = null;
            void Close()
            {
                if (current is { } definition)
                {
                    Add(definitions, definition.Term, article.Id, definition.First, definition.Last, "term_colon");
                    current = null;
                }
            }

            for (var at = 0; at < article.LineCount; at++)
            {
                var line = lines[article.FirstLine + at];
                if (line.Kind is "label" or "heading")
                {
                    continue;
                }

                if (line.Folded.Contains("on entend par", StringComparison.Ordinal))
                {
                    Close();
                    if (OpensList.IsMatch(line.Folded))
                    {
                        open = true;
                        quoted = null;
                        continue;
                    }

                    if (OnEntendPar.Match(line.Folded) is { Success: true } said)
                    {
                        var term = line.Text.Substring(said.Groups["term"].Index, said.Groups["term"].Length).Trim();
                        Add(definitions, term, article.Id, line.Index, line.Index, "on_entend_par");
                    }

                    continue;
                }

                if (!open)
                {
                    continue;
                }

                if (line.Kind == "marker")
                {
                    // A numbered point closes the list of terms ("(2) ..." after the definitions of "(1)"); a letter does not.
                    if (NumericMarker.IsMatch(line.Text) && current is not null)
                    {
                        Close();
                        open = false;
                    }

                    continue;
                }

                // The first term of a list says how the act writes its terms: between quotation marks, or with a capital.
                // A line of another form continues the definition before it ("dans une assurance de dommages: ...").
                var startsQuoted = line.Text[0] is '«' or '"' or '“';
                if (TermColon.Match(line.Text) is { Success: true } match && LooksLikeTerm(match.Groups["term"].Value) &&
                    (quoted is null ? startsQuoted || char.IsUpper(line.Text[0]) : quoted.Value ? startsQuoted : char.IsUpper(line.Text[0])))
                {
                    Close();
                    quoted ??= startsQuoted;
                    current = (match.Groups["term"].Value.Trim(), line.Index, line.Index);
                }
                else if (current is { } running)
                {
                    current = (running.Term, running.First, line.Index);
                }
            }

            Close();
        }

        return definitions;
    }

    private static bool LooksLikeTerm(string term)
    {
        var trimmed = term.Trim();
        // A term is short and starts with a capital, a quotation mark or a letter the act quotes; a sentence with a colon in it is not one.
        return trimmed.Length is >= 2 and <= 110 && V4Text.Words(V4Text.Fold(trimmed)).Count is >= 1 and <= 14 && !trimmed.Contains(';', StringComparison.Ordinal);
    }

    private static void Add(List<V4Definition> definitions, string term, string article, int first, int last, string method)
    {
        // "Preneur ou preneur d'assurance" names one definition under two terms; each is a key of its own, and so is the whole.
        var terms = new List<string> { term };
        var parts = term.Split(" ou ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            terms.AddRange(parts);
        }

        foreach (var written in terms.Distinct(StringComparer.Ordinal))
        {
            var folded = string.Join(' ', V4Text.Words(V4Text.Fold(written)).Select(static word => word.Text));
            if (folded.Length >= 3 && !definitions.Any(existing => existing.TermFolded == folded && existing.Article == article && existing.FirstLine == first))
            {
                definitions.Add(new V4Definition(written, folded, article, first, last, method));
            }
        }
    }

    /// <summary>
    /// The references the text makes in plain words: to an article of this act (resolved when the act holds it, listed and not
    /// dropped when it does not), to an article of another act, and to "the cases provided by the law", which names none.
    /// </summary>
    private static List<V4CrossReference> ReferencesOf(List<V4Line> lines, HashSet<string> articleIds)
    {
        var references = new List<V4CrossReference>();
        foreach (var line in lines)
        {
            if (line.Kind != "paragraph")
            {
                continue;
            }

            if (UnnamedCases.IsMatch(line.Folded))
            {
                references.Add(new V4CrossReference(line.Id, line.Article, "unnamed_cases", null, false));
            }

            foreach (Match match in ArticleMention.Matches(line.Folded))
            {
                var external = ExternalTail.IsMatch(match.Groups["tail"].Value);
                var numbers = new List<string> { match.Groups["first"].Value };
                numbers.AddRange(MoreNumbers.Matches(match.Groups["more"].Value).Select(static more => more.Value));
                foreach (var number in numbers)
                {
                    var target = "art_" + number.Replace(" ", string.Empty, StringComparison.Ordinal);
                    if (external)
                    {
                        references.Add(new V4CrossReference(line.Id, line.Article, "external", null, false));
                    }
                    else if (target != line.Article)
                    {
                        references.Add(new V4CrossReference(line.Id, line.Article, "article", target, articleIds.Contains(target)));
                    }
                }
            }
        }

        return references;
    }

    /// <summary>The lines joined back, as a check a build can run: whether every bound article's lines lie inside its text in order.</summary>
    public bool LinesAreInOrder()
    {
        foreach (var article in Articles)
        {
            var at = 0;
            foreach (var line in LinesOf(article))
            {
                if (line.Offset < at || line.Offset + line.Length > article.TextLength)
                {
                    return false;
                }

                at = line.Offset + line.Length;
            }
        }

        return true;
    }

    /// <summary>The build's summary as a short text for a log or a test.</summary>
    public override string ToString() => new StringBuilder()
        .Append(Work).Append(' ').Append(Date).Append(": ")
        .Append(Summary.Articles.ToString(CultureInfo.InvariantCulture)).Append(" articles, ")
        .Append(Summary.Lines.ToString(CultureInfo.InvariantCulture)).Append(" lines, ")
        .Append(Summary.Definitions.ToString(CultureInfo.InvariantCulture)).Append(" definitions")
        .ToString();
}
