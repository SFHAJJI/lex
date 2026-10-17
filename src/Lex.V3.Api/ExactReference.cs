using System.Globalization;
using System.Text.RegularExpressions;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Api;

/// <summary>How a reference names its act.</summary>
internal enum ReferenceActForm
{
    /// <summary>A Luxembourg ELI (<c>http://data.legilux.public.lu/eli/etat/leg/…</c>, or the public site's, or <c>eli/…</c>).</summary>
    LuxembourgEli,

    /// <summary>A CELEX number, consolidated forms included, as typed or in an EUR-Lex, Publications Office or EU ELI address.</summary>
    Celex,

    /// <summary>A Mémorial A or Journal officiel A citation: its number and year (and, when written, the issue's date).</summary>
    Memorial,

    /// <summary>A historical identifier of the publisher's catalogue (<c>1790A0001Z</c>).</summary>
    HistoricalLegalId,

    /// <summary>An address of this site for law, in any form R64's reader accepts: a permalink or a coordinate.</summary>
    Permalink,

    /// <summary>A Luxembourg act by its type and date (<c>loi du 10 août 1991</c>), with any title words after it.</summary>
    TypeAndDate,

    /// <summary>An EU act by its conventional name (<c>règlement (UE) 2016/679</c>, <c>Directive 2006/112/EC</c>), read to its CELEX.</summary>
    EuropeName,

    /// <summary>A Luxembourg act by its title (<c>Code du travail</c>), which must start with an instrument's word.</summary>
    Title,
}

/// <summary>
/// The act a reference names, as read: the form, the text as typed, and the parts that form carries. Every part the text holds
/// is kept (R90): an ELI's version segment, a Mémorial issue's date, a type and date's title words.
/// </summary>
internal sealed record ReferenceAct(
    ReferenceActForm Form,
    string AsTyped,
    string? Eli = null,
    string? Celex = null,
    int? MemorialYear = null,
    string? MemorialNumber = null,
    CitationDate? MemorialDate = null,
    string? HistoricalLegalId = null,
    string? Address = null,
    string? LuxembourgType = null,
    CitationDate? ActDate = null,
    string? TitleWords = null,
    string? Title = null,
    bool DateNotRead = false)
{
    /// <summary>The publisher whose index holds the act this form names.</summary>
    public bool IsEurope => Form is ReferenceActForm.Celex or ReferenceActForm.EuropeName ||
        (Form == ReferenceActForm.Permalink && Address!.StartsWith("/eu-eurlex/", StringComparison.Ordinal));

    /// <summary>The identifier the act is printed by in the recognition: the normalised identifier, or the act as typed.</summary>
    public string Label => Form switch
    {
        ReferenceActForm.LuxembourgEli => Eli!,
        ReferenceActForm.Celex or ReferenceActForm.EuropeName => Celex!,
        ReferenceActForm.HistoricalLegalId => HistoricalLegalId!,
        ReferenceActForm.Permalink => Address!,
        _ => AsTyped,
    };
}

/// <summary>
/// One exact reference read from a query (the plan's item 4.2; the specification of record's R59 and R90): its act, and the
/// article and date it names, each as typed and as read. <see cref="DateNotRead"/> is a date written in a date's shape that names
/// no day of the calendar (or two dates that disagree): the reference is then not opened, and its date is never dropped.
/// </summary>
internal sealed record ExactReference(
    string Text,
    ReferenceAct Act,
    string? ArticleAsTyped,
    ArticleNumber? Article,
    string? DateAsTyped,
    CitationDate? Date,
    string? DateNotRead)
{
    /// <summary>The reference's kind on the wire: the act's form for a bare identifier, else a citation.</summary>
    public string Kind => ArticleAsTyped is null && DateAsTyped is null
        ? Act.Form switch
        {
            ReferenceActForm.LuxembourgEli => "eli",
            ReferenceActForm.Celex => "celex",
            ReferenceActForm.Memorial => "memorial",
            ReferenceActForm.HistoricalLegalId => "historical_legal_id",
            ReferenceActForm.Permalink => "permalink",
            _ => "citation",
        }
        : "citation";

    /// <summary>The act's form on the wire.</summary>
    public string ActForm => Act.Form switch
    {
        ReferenceActForm.LuxembourgEli => "eli",
        ReferenceActForm.Celex => "celex",
        ReferenceActForm.Memorial => "memorial",
        ReferenceActForm.HistoricalLegalId => "historical_legal_id",
        ReferenceActForm.Permalink => "permalink",
        ReferenceActForm.TypeAndDate => "type_and_date",
        ReferenceActForm.EuropeName => "eu_act_name",
        _ => "title",
    };
}

/// <summary>
/// Reads a query as an exact reference before any word is searched (resolver first, S4-A03; R90). It reads only what it can read
/// exactly, and anything else is no reference (null): the query is then searched as words, as before. What it reads:
/// <list type="bullet">
/// <item>an identifier alone: a Luxembourg or EU ELI, a CELEX (consolidated forms included, also inside an EUR-Lex or
/// Publications Office address), a historical identifier, a Mémorial or Journal officiel A citation, or an address of this site
/// for law;</item>
/// <item>an act by its type and date (<c>loi du 10 août 1991</c>) or an EU act by its conventional name
/// (<c>règlement (UE) 2016/679</c>), alone;</item>
/// <item>a citation: an article of an act (<c>Code du travail, article L. 121-6</c>; <c>article 5 de la loi du 10 août
/// 1991</c>; <c>Article 6 of Regulation (EU) 2016/679</c>), with or without a date (<c>, 15 mars 2021</c>; <c>au 15 mars
/// 2021</c>; <c>on 15 March 2021</c>), or an act and a date; the act of a citation may also be a title, which must start with an
/// instrument's word (code, constitution, loi, règlement, arrêté, convention, traité).</item>
/// </list>
/// A bare title is no reference: the title ladder answers it, as before. The article number is read by the fixed table
/// (<see cref="ArticleNumbers"/>), the dates by <see cref="CitationDates"/>, and nothing is folded that the act's own index does
/// not fold.
/// </summary>
internal static class ExactReferenceReader
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // A date clause at the end: after a comma (with or without a preposition), or after a preposition alone.
    private const string Preposition =
        @"(?:au|le|à\s+la\s+date\s+du|a\s+la\s+date\s+du|en\s+date\s+du|dans\s+sa\s+version\s+(?:du|au|applicable\s+au)|version\s+(?:du|au)|" +
        @"on|at|as\s+at|as\s+of|as\s+on|in\s+the\s+version\s+of|am|zum|stand)";

    private static readonly Regex DateClause = new(
        @"^(?<head>.+?)(?:\s*,\s*(?:" + Preposition + @"\s+)?|\s+" + Preposition + @"\s+)(?<date>" + CitationDates.Shape + @")$",
        Options);

    // A citation word, then the number, then "of" and the act.
    private static readonly Regex ArticleFirst = new(
        @"^(?:l['’]\s*)?(?<word>articles?|art\.|art|artikels?)\s+(?<rest>.+)$", Options);

    private static readonly Regex OfWord = new(
        @"^(?:,\s*)?(?:de\s+la|du|de\s+l['’]|des|de|d['’]|of\s+the|of|der|des|vom)\s*(?<act>.+)$", Options);

    // The act, then a citation word and the number.
    private static readonly Regex ActFirst = new(
        @"^(?<act>.+?)\s*,?\s+(?<word>articles?|art\.|art|artikels?)\s+(?<number>.+)$", Options);

    private static readonly Regex LuxembourgEli = new(
        @"^(?:(?:https?://)?(?:data\.|www\.)?legilux\.public\.lu)?/?eli/etat/(?<path>[A-Za-z0-9_./-]+?)/?$", Options);

    private static readonly Regex EuropeEli = new(
        @"^(?:https?://)?(?:data\.)?europa\.eu/eli/(?<type>reg|dir|dec|reg_impl|reg_del|dir_impl|dir_del|dec_impl|dec_del)/(?<year>[0-9]{4})/(?<number>[0-9]{1,4})(?:/oj)?/?$",
        Options);

    private static readonly Regex CelexInAddress = new(
        @"^(?:https?://)?(?:[a-z0-9-]+\.)*europa\.eu.*?(?:[?&]uri=celex(?::|%3a)|/resource/celex/)(?<celex>[0-9A-Za-z()/._-]+?)(?:[&#].*)?$", Options);

    private static readonly Regex CelexPrefixed = new(@"^(?:celex\s*(?::|n°|no\.?|number)?\s*)(?<celex>\S+)$", Options);

    private static readonly Regex CelexLabelled = new(@"^celex\s*(?::|n°|no\.?|number)?\s+(?<celex>\S+)$", Options);

    private static readonly Regex HistoricalId = new(@"^[0-9]{4}A[0-9]{4,5}[A-Z]?$", Options);

    private static readonly Regex Memorial = new(
        @"^(?:journal\s+officiel(?:\s+du\s+grand[-\s]duch[ée]\s+de\s+luxembourg)?\s*,?\s*)?" +
        @"(?:m[ée]morial|m[ée]m\.?|j\.\s?o\.|jo)\s*,?\s*a\s*[,\-–]?\s*(?:(?:n°|no\.?|nr\.?|n\.|num[ée]ro)\s*)?" +
        @"(?<number>[0-9]{1,4})" +
        @"(?:\s*/\s*(?<slashyear>[0-9]{4})|\s*,?\s*(?:(?:de|du|of|from|vom)\s+)?(?<when>[0-9]{4}|" + CitationDates.Shape + @"))$",
        Options);

    private static readonly Regex TypeAndDate = new(
        @"^(?<type>loi|r[èe]glement\s+grand[-\s]ducal|arr[êe]t[ée]\s+grand[-\s]ducal|r[èe]glement\s+minist[ée]riel|arr[êe]t[ée]\s+minist[ée]riel)" +
        @"(?:\s+modifi[ée]e?)?\s+du\s+(?<date>" + CitationDates.Shape + @")(?<rest>(?:\s.*)?)$",
        Options);

    private static readonly Regex EuropeName = new(
        @"^(?<type>r[èe]glement|regulation|directive|d[ée]cision)(?:\s+(?:d['’]ex[ée]cution|d[ée]l[ée]gu[ée]e?|implementing|delegated))?" +
        @"\s*(?:\((?<org>UE|EU|CE|EC|CEE|EEC|Euratom)\)\s*)?(?<no>(?:n°|no\.?|nr\.?)\s*)?(?<a>[0-9]{1,4})\s*/\s*(?<b>[0-9]{1,4})" +
        @"(?:\s*/\s*(?<suffix>UE|EU|CE|EC|CEE|EEC|Euratom))?$",
        Options);

    private static readonly string[] InstrumentWords = ["code", "constitution", "loi", "reglement", "arrete", "convention", "traite"];

    /// <summary>The ELI types of the Luxembourg acts a type and date names.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LuxembourgTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["loi"] = "loi",
        ["reglement grand ducal"] = "rgd",
        ["arrete grand ducal"] = "agd",
        ["reglement ministeriel"] = "rmin",
        ["arrete ministeriel"] = "amin",
    };

    /// <summary>The reference <paramref name="query"/> is, or null when it is no exact reference.</summary>
    public static ExactReference? Read(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = Clean(query);
        if (text.Length == 0)
        {
            return null;
        }

        // A date clause at the end, then a citation with an article, or an act and the date. A date earlier than a type and
        // date's own date is not a date to read the act at (no version of an act applies before it is made): it is part of
        // the act's title ("… signée à Genève, le 23 septembre 1936"), and the whole text is read again as one act.
        (ReferenceAct Act, string? ArticleAsTyped, ArticleNumber? Article)? found = null;
        string? dateAsTyped = null;
        if (DateClause.Match(text) is { Success: true } clause && CitationDates.IsDateShaped(clause.Groups["date"].Value))
        {
            var head = clause.Groups["head"].Value.Trim().TrimEnd(',').Trim();
            dateAsTyped = clause.Groups["date"].Value.Trim();
            found = Citation(head) ?? (Act(head, allowTitle: true) is { } dated ? (dated, null, null) : null);
            if (found is { } candidate && candidate.Act.ActDate is { } own &&
                CitationDates.Read(dateAsTyped) is { } reading && string.CompareOrdinal(reading.Date, own.Date) < 0)
            {
                found = null;
            }

            if (found is null)
            {
                dateAsTyped = null;
            }
        }

        // An identifier alone, an act by its type and date or EU name alone, or a citation with no date.
        found ??= Citation(text) ?? (Act(text, allowTitle: false) is { } bare ? (bare, null, null) : null);
        if (found is not { } reference)
        {
            return null;
        }

        var date = dateAsTyped is null ? null : CitationDates.Read(dateAsTyped);
        var notRead = (dateAsTyped is not null && date is null) || reference.Act.DateNotRead
            ? "date_not_a_calendar_date"
            : null;
        return new ExactReference(text, reference.Act, reference.ArticleAsTyped, reference.Article, dateAsTyped, date, notRead);
    }

    /// <summary>A citation of an article of an act: the act, the article number as typed and as read.</summary>
    private static (ReferenceAct Act, string? ArticleAsTyped, ArticleNumber? Article)? Citation(string head)
    {
        // "article 5 de la loi du …", "art. L. 121-6 du Code du travail", "Article 6 of Regulation (EU) 2016/679".
        if (ArticleFirst.Match(head) is { Success: true } first)
        {
            var words = first.Groups["rest"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var take = Math.Min(4, words.Length - 1); take >= 1; take--)
            {
                var numberText = string.Join(' ', words[..take]).TrimEnd(',');
                if (ArticleNumbers.FromCitation(numberText) is not { } number ||
                    OfWord.Match(string.Join(' ', words[take..])) is not { Success: true } of ||
                    Act(of.Groups["act"].Value.Trim(), allowTitle: true) is not { } act)
                {
                    continue;
                }

                return (act, numberText, number);
            }
        }

        // "Code du travail, article L. 121-6", "32016R0679 article 6", "loi du 10 août 1991, art. 5".
        if (ActFirst.Match(head) is { Success: true } last &&
            ArticleNumbers.FromCitation(last.Groups["number"].Value.Trim()) is { } cited &&
            Act(last.Groups["act"].Value.Trim().TrimEnd(',').Trim(), allowTitle: true) is { } named)
        {
            return (named, last.Groups["number"].Value.Trim(), cited);
        }

        return null;
    }

    /// <summary>The act <paramref name="text"/> names, or null. A title is read only when <paramref name="allowTitle"/>.</summary>
    internal static ReferenceAct? Act(string text, bool allowTitle)
    {
        var value = Clean(text);
        if (value.Length == 0)
        {
            return null;
        }

        // A CELEX written after its label ("CELEX: 32016R0679", "celex n° 32016R0679") is the CELEX alone.
        if (CelexLabelled.Match(value) is { Success: true } labelled && Identifier(labelled.Groups["celex"].Value) is
                { Form: ReferenceActForm.Celex } labelledCelex)
        {
            return labelledCelex with { AsTyped = value };
        }

        if (!value.Contains(' ', StringComparison.Ordinal) && Identifier(value) is { } identifier)
        {
            return identifier;
        }

        if (Memorial.Match(value) is { Success: true } memorial)
        {
            var number = memorial.Groups["number"].Value.TrimStart('0') is { Length: > 0 } digits ? digits : "0";
            var year = memorial.Groups["slashyear"].Success ? memorial.Groups["slashyear"].Value : memorial.Groups["when"].Value;
            CitationDate? issued = null;
            if (year.Length != 4 || !year.All(char.IsAsciiDigit))
            {
                // The issue's date: read strictly, and its year is the number's year. A date in a date's shape that names no
                // day is kept as not read, never dropped.
                if (!CitationDates.IsDateShaped(year))
                {
                    return null;
                }

                issued = CitationDates.Read(year);
                if (issued is null)
                {
                    return new ReferenceAct(ReferenceActForm.Memorial, value, MemorialNumber: number, DateNotRead: true);
                }

                year = issued.Date[..4];
            }

            return new ReferenceAct(
                ReferenceActForm.Memorial, value,
                MemorialYear: int.Parse(year, NumberStyles.None, CultureInfo.InvariantCulture),
                MemorialNumber: number,
                MemorialDate: issued);
        }

        if (TypeAndDate.Match(value) is { Success: true } typed && CitationDates.IsDateShaped(typed.Groups["date"].Value) &&
            LuxembourgTypes.TryGetValue(ArticleNumbers.Fold(typed.Groups["type"].Value), out var eliType))
        {
            var actDate = CitationDates.Read(typed.Groups["date"].Value);
            var rest = typed.Groups["rest"].Value.Trim().TrimEnd(',').Trim();
            return new ReferenceAct(
                ReferenceActForm.TypeAndDate, value,
                LuxembourgType: eliType,
                ActDate: actDate,
                TitleWords: rest.Length == 0 ? null : rest,
                DateNotRead: actDate is null);
        }

        if (EuropeName.Match(value) is { Success: true } named && EuropeNameCelex(named) is { } celex)
        {
            return new ReferenceAct(ReferenceActForm.EuropeName, value, Celex: celex);
        }

        if (allowTitle)
        {
            var folded = ArticleNumbers.Fold(value);
            var firstWord = folded.Split(' ', 2)[0];
            if (InstrumentWords.Contains(firstWord, StringComparer.Ordinal) && folded.Length > firstWord.Length)
            {
                return new ReferenceAct(ReferenceActForm.Title, value, Title: value);
            }
        }

        return null;
    }

    /// <summary>A single-token identifier, or null.</summary>
    private static ReferenceAct? Identifier(string token)
    {
        if (IsSiteAddress(token))
        {
            return new ReferenceAct(ReferenceActForm.Permalink, token, Address: SitePath(token));
        }

        if (LuxembourgEli.Match(token) is { Success: true } eli)
        {
            var path = eli.Groups["path"].Value.Trim('/');
            return new ReferenceAct(ReferenceActForm.LuxembourgEli, token, Eli: "http://data.legilux.public.lu/eli/etat/" + path);
        }

        if (EuropeEli.Match(token) is { Success: true } europeEli)
        {
            var letter = europeEli.Groups["type"].Value.ToLowerInvariant()[..3] switch
            {
                "reg" => 'R',
                "dir" => 'L',
                _ => 'D',
            };
            var number = int.Parse(europeEli.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            return new ReferenceAct(ReferenceActForm.Celex, token,
                Celex: $"3{europeEli.Groups["year"].Value}{letter}{number.ToString("0000", CultureInfo.InvariantCulture)}");
        }

        var candidate = token;
        if (CelexInAddress.Match(token) is { Success: true } address)
        {
            candidate = Uri.UnescapeDataString(address.Groups["celex"].Value);
        }
        else if (CelexPrefixed.Match(token) is { Success: true } prefixed)
        {
            candidate = prefixed.Groups["celex"].Value;
        }

        candidate = candidate.ToUpperInvariant();
        if (OfficialIdentifier.ProfileOf(candidate) is not null && candidate.Length >= 6 && candidate[1..5].All(char.IsAsciiDigit))
        {
            return new ReferenceAct(ReferenceActForm.Celex, token, Celex: candidate);
        }

        if (HistoricalId.IsMatch(token))
        {
            return new ReferenceAct(ReferenceActForm.HistoricalLegalId, token, HistoricalLegalId: token.ToUpperInvariant());
        }

        return null;
    }

    /// <summary>
    /// The CELEX an EU act's conventional name reads to: sector 3, the year, R for a regulation, L for a directive, D for a
    /// decision, and the number on four digits. The year is the part a regulation numbered "No n/yyyy" writes last, and every
    /// other name writes first (2016/679, 2006/112/EC); a two-digit year is of the 1900s.
    /// </summary>
    private static string? EuropeNameCelex(Match named)
    {
        var type = ArticleNumbers.Fold(named.Groups["type"].Value);
        var letter = type switch
        {
            "reglement" or "regulation" => 'R',
            "directive" => 'L',
            _ => 'D',
        };
        var a = named.Groups["a"].Value;
        var b = named.Groups["b"].Value;
        var numberFirst = letter == 'R' && named.Groups["no"].Success;
        var (yearText, numberText) = numberFirst ? (b, a) : (a, b);
        if (yearText.Length == 2)
        {
            yearText = "19" + yearText;
        }

        if (yearText.Length != 4 || numberText.Length > 4 ||
            !int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 1952 or > 2099 ||
            !int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
        {
            return null;
        }

        return $"3{year.ToString(CultureInfo.InvariantCulture)}{letter}{number.ToString("0000", CultureInfo.InvariantCulture)}";
    }

    private const string SiteOrigin = "https://law.soufien.lu";

    /// <summary>An address of this site for law: a path under <c>/lu-legilux/</c> or <c>/eu-eurlex/</c>, under an interface language or not, as a path or under this origin.</summary>
    private static bool IsSiteAddress(string token)
    {
        var path = SitePath(token);
        return path.StartsWith("/lu-legilux/", StringComparison.Ordinal) || path.StartsWith("/eu-eurlex/", StringComparison.Ordinal);
    }

    /// <summary>The path of a site address with this origin and the interface language taken off, as typed otherwise.</summary>
    private static string SitePath(string token)
    {
        var path = token.StartsWith(SiteOrigin + "/", StringComparison.Ordinal) ? token[SiteOrigin.Length..] : token;
        if (path.Length > 4 && path[0] == '/' && path[3] == '/' && V3WebRoot.InterfaceLanguages.Contains(path[1..3], StringComparer.Ordinal))
        {
            path = path[3..];
        }

        return path;
    }

    /// <summary>The query with its ends cleaned: spaces, a final full stop, question mark or semicolon, and enclosing quotation marks.</summary>
    private static string Clean(string text)
    {
        var value = text.Trim();
        value = value.TrimEnd('.', '?', '!', ';', ' ', ' ').Trim();
        if (value.Length >= 2 && (value[0], value[^1]) is ('«', '»') or ('"', '"') or ('“', '”') or ('\'', '\''))
        {
            value = value[1..^1].Trim();
        }

        return Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant);
    }
}
