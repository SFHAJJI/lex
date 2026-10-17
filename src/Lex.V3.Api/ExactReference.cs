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
/// is kept (R90): an ELI's version segment and language, a Mémorial issue's date, a type and date's title words, an EU act's own
/// date. An EU act's name can be read to more than one CELEX (a number and a year that could be either): <see cref="CelexCandidates"/>
/// lists them in the order they are tried, and <see cref="Celex"/> is the one tried first until the index chooses.
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
    bool DateNotRead = false,
    string? ActDateAsTyped = null,
    IReadOnlyList<string>? CelexCandidates = null,
    string? EuropeLanguage = null,
    CitationDate? EuropeVersionDate = null)
{
    /// <summary>The publisher whose index holds the act this form names.</summary>
    public bool IsEurope => Form is ReferenceActForm.Celex or ReferenceActForm.EuropeName ||
        (Form == ReferenceActForm.Permalink && Address!.StartsWith("/eu-eurlex/", StringComparison.Ordinal));

    /// <summary>
    /// The identifier the act is printed by in the recognition: the normalised identifier, an EU act's name with the CELEX it
    /// was read to, or the act as typed.
    /// </summary>
    public string Label => Form switch
    {
        ReferenceActForm.LuxembourgEli => Eli!,
        ReferenceActForm.Celex => Celex!,
        ReferenceActForm.EuropeName => $"{AsTyped} (CELEX {Celex})",
        ReferenceActForm.HistoricalLegalId => HistoricalLegalId!,
        ReferenceActForm.Permalink => Address!,
        _ => AsTyped,
    };
}

/// <summary>
/// One exact reference read from a query (the plan's item 4.2; the specification of record's R59 and R90): its act, and the
/// article and date it names, each as typed and as read. <see cref="DateNotRead"/> is a date written in a date's shape that names
/// no day of the calendar (or two dates that disagree): the reference is then not opened, and its date is never dropped.
/// <see cref="ArticlePart"/> is what the citation names inside the article (<c>al. 3</c>, <c>paragraphe 1</c>), kept as typed:
/// the article is opened, and the part is said with it. <see cref="AsTitle"/> is the other reading of a text whose last date is
/// earlier than its act's own: the whole text as the act's title, which the index tries first.
/// </summary>
internal sealed record ExactReference(
    string Text,
    ReferenceAct Act,
    string? ArticleAsTyped,
    ArticleNumber? Article,
    string? DateAsTyped,
    CitationDate? Date,
    string? DateNotRead,
    string? ArticlePart = null,
    ExactReference? AsTitle = null)
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

    /// <summary>The article as the recognition prints it: its number as the act's language prints it, and the part it names.</summary>
    public string? PrintedArticle(bool europe) => Article is null
        ? null
        : (europe ? Article.Key : Article.Printed) + (ArticlePart is null ? string.Empty : ", " + ArticlePart);
}

/// <summary>
/// Reads a query as an exact reference before any word is searched (resolver first, S4-A03; R90). It reads only what it can read
/// exactly, and anything else is no reference (null): the query is then searched as words, as before. What it reads:
/// <list type="bullet">
/// <item>an identifier alone: a Luxembourg or EU ELI, a CELEX (consolidated forms included, also inside an EUR-Lex or
/// Publications Office address), a historical identifier, a Mémorial or Journal officiel A citation, or an address of this site
/// for law;</item>
/// <item>an act by its type and date (<c>loi du 10 août 1991</c>, <c>la loi du …</c>) or an EU act by its conventional name
/// (<c>règlement (UE) 2016/679</c>, <c>Règlement (UE) 2016/679 du Parlement européen et du Conseil du 27 avril 2016</c>), alone;</item>
/// <item>a citation: an article of an act (<c>Code du travail, article L. 121-6</c>; <c>article 5 de la loi du 10 août
/// 1991</c>; <c>art. 2, loi du …</c>; <c>Article 6 of Regulation (EU) 2016/679</c>; <c>article 2, alinéa 3, de la loi du …</c>),
/// with or without a date (<c>, 15 mars 2021</c>; <c>au 15 mars 2021</c>; <c>on 15 March 2021</c>), or an act and a date; the act
/// of a citation may also be a title, which must start with an instrument's word (code, constitution, loi, règlement, arrêté,
/// convention, traité) and which the index matches only by its own title, else the query is searched as words.</item>
/// </list>
/// A bare title is no reference: the title ladder answers it, as before. A text in an EU act's name's shape that reads to no CELEX
/// is no reference either, never a Luxembourg title. The article number is read by the fixed table (<see cref="ArticleNumbers"/>),
/// the dates by <see cref="CitationDates"/>, and nothing is folded that the act's own index does not fold.
/// </summary>
internal static class ExactReferenceReader
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // A date clause at the end: after a comma (with or without a preposition), or after a preposition alone.
    private const string Preposition =
        @"(?:au|le|à\s+la\s+date\s+du|a\s+la\s+date\s+du|en\s+date\s+du|dans\s+sa\s+version\s+(?:du|au|applicable\s+au)|version\s+(?:du|au)|" +
        @"on|at|as\s+at|as\s+of|as\s+on|in\s+the\s+version\s+of|am|zum|stand)";

    private static readonly Regex DateClause = new(
        @"^(?<head>.+?)(?:\s*,\s*(?:(?<prep>" + Preposition + @")\s+)?|\s+(?<prep>" + Preposition + @")\s+)(?<date>" + CitationDates.Shape + @")$",
        Options);

    // A citation word, then the number, then "of" and the act.
    private static readonly Regex ArticleFirst = new(
        @"^(?:l['’]\s*)?(?<word>articles?|art\.|art|artikels?)\s+(?<rest>.+)$", Options);

    // Each word whole ("der", never "de" and "r"), the elided forms with their apostrophe.
    private const string OfWords = @"(?:de\s+l['’]|d['’]|(?:de\s+la|des|der|du|de|of\s+the|of|vom)(?!\p{L}))";

    // The act after the article: after "of" (de la, du, of the …), or after a comma alone ("art. 2, loi du …").
    private static readonly Regex OfWord = new(
        @"^(?:,\s*(?:" + OfWords + @"\s*)?|" + OfWords + @"\s*)(?<act>.+)$", Options);

    // The act, then a citation word and the number.
    private static readonly Regex ActFirst = new(
        @"^(?<act>.+?)\s*,?\s+(?<word>articles?|art\.|art|artikels?)\s+(?<number>.+)$", Options);

    // A part of an article: an alinéa, a paragraph, a point or a letter, and its number or letter.
    private static readonly Regex ArticlePartWord = new(
        @"^(?:al\.|alinéa|alinea|par\.|paragraphe|paragraph|para\.|§|point|pt\.|lettre|litt?\.|subparagraph|sub-paragraph)$", Options);

    private static readonly Regex ArticlePartValue = new(@"^\(?[0-9]{1,3}[a-z]?(?:er|°)?\)?$|^\(?[a-z]\)?$", Options);

    private static readonly Regex LuxembourgEli = new(
        @"^(?:(?:https?://)?(?:data\.|www\.)?legilux\.public\.lu)?/?eli/etat/(?<path>[A-Za-z0-9_./-]+?)/?$", Options);

    // An EU ELI, on the Publications Office's host or EUR-Lex's: the act, a consolidated version's date, the Official Journal's
    // form and an expression's language.
    private static readonly Regex EuropeEli = new(
        @"^(?:https?://)?(?:data\.europa\.eu|(?:www\.)?eur-lex\.europa\.eu)/eli/(?<type>reg|dir|dec|reg_impl|reg_del|dir_impl|dir_del|dec_impl|dec_del)" +
        @"/(?<year>[0-9]{4})/(?<number>[0-9]{1,4})(?:/(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}))?(?:/oj)?(?:/(?<language>[a-z]{3}))?/?$",
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

    // A type and date, then title words (after a space or a comma).
    private static readonly Regex TypeAndDate = new(
        @"^(?<type>loi|r[èe]glement\s+grand[-\s]ducal|arr[êe]t[ée]\s+grand[-\s]ducal|r[èe]glement\s+minist[ée]riel|arr[êe]t[ée]\s+minist[ée]riel)" +
        @"(?:\s+modifi[ée]e?)?\s+du\s+(?<date>" + CitationDates.Shape + @")(?<rest>(?:[\s,].*)?)$",
        Options);

    private const string EuropeType =
        @"(?<type>r[èe]glement|regulation|directive|d[ée]cision)(?:\s+(?:d['’]ex[ée]cution|d[ée]l[ée]gu[ée]e?|implementing|delegated))?";

    private const string EuropeOrg = @"(?:\((?<org>UE|EU|CE|EC|CEE|EEC|Euratom)\)\s*)?";

    private static readonly Regex EuropeName = new(
        @"^" + EuropeType + @"\s*" + EuropeOrg + @"(?<no>(?:n°|no\.?|nr\.?)\s*)?(?<a>[0-9]{1,4})\s*/\s*(?<b>[0-9]{1,4})" +
        @"(?:\s*/\s*(?<suffix>UE|EU|CE|EC|CEE|EEC|Euratom))?" +
        @"(?:\s*,?\s+(?:du|of\s+the|of)\s+(?:Parlement\s+europ[ée]en\s+et\s+du\s+Conseil|European\s+Parliament\s+and\s+of\s+the\s+Council|Conseil|Council|Commission))?" +
        @"(?:\s*,?\s+(?:du|of)\s+(?<actdate>" + CitationDates.Shape + @"))?$",
        Options);

    // The beginning of an EU act's name: a text in this shape is read as an EU act's name or as no reference, never as a
    // Luxembourg title.
    private static readonly Regex EuropeNameShape = new(
        @"^" + EuropeType + @"\s*(?:\((?:UE|EU|CE|EC|CEE|EEC|Euratom)\)|(?:n°|no\.?|nr\.?)\s*[0-9]|[0-9]{1,4}\s*/\s*[0-9])", Options);

    // An article before an act's type or name ("la loi du …", "le règlement (UE) …", "l'arrêté grand-ducal du …").
    private static readonly Regex LeadingArticle = new(@"^(?:la|le|les|the)\s+|^l['’]\s*", Options);

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

    private sealed record Cited(ReferenceAct Act, string? ArticleAsTyped, ArticleNumber? Article, string? Part);

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
        // date's own date, after a comma or "le" alone, may be part of the act's title ("… signée à Genève, le 23 septembre
        // 1936"): the whole text read as one act is then the other reading (AsTitle), which the index tries first, and the date
        // asked stays this one's; after a preposition that asks for a version ("au", "à la date du", "as at", "on"), it is the
        // date asked alone.
        if (DateClause.Match(text) is { Success: true } clause && CitationDates.IsDateShaped(clause.Groups["date"].Value))
        {
            var head = clause.Groups["head"].Value.Trim().TrimEnd(',').Trim();
            var dateAsTyped = clause.Groups["date"].Value.Trim();
            if ((Citation(head) ?? (Act(head, allowTitle: true) is { } dated ? new Cited(dated, null, null, null) : null)) is { } found)
            {
                var date = CitationDates.Read(dateAsTyped);
                var asked = clause.Groups["prep"].Success && ArticleNumbers.Fold(clause.Groups["prep"].Value) != "le";
                ExactReference? asTitle = null;
                if (!asked && found.Act.ActDate is { } own && date is not null && string.CompareOrdinal(date.Date, own.Date) < 0 &&
                    Whole(text) is { Act.TitleWords: not null } whole)
                {
                    asTitle = whole;
                }

                return Build(text, found, dateAsTyped, date) with { AsTitle = asTitle };
            }
        }

        // An identifier alone, an act by its type and date or EU name alone, or a citation with no date.
        return Whole(text);
    }

    /// <summary>The whole text read as a citation with no date, or as one act.</summary>
    private static ExactReference? Whole(string text) =>
        (Citation(text) ?? (Act(text, allowTitle: false) is { } bare ? new Cited(bare, null, null, null) : null)) is { } found
            ? Build(text, found, null, null)
            : null;

    private static ExactReference Build(string text, Cited found, string? dateAsTyped, CitationDate? date)
    {
        var notRead = (dateAsTyped is not null && date is null) || found.Act.DateNotRead ? "date_not_a_calendar_date" : null;
        return new ExactReference(text, found.Act, found.ArticleAsTyped, found.Article, dateAsTyped, date, notRead, found.Part);
    }

    /// <summary>A citation of an article of an act: the act, the article number as typed and as read, and a part of the article.</summary>
    private static Cited? Citation(string head)
    {
        // "article 5 de la loi du …", "art. L. 121-6 du Code du travail", "Article 6 of Regulation (EU) 2016/679", "art. 2, loi du …",
        // "article 2, alinéa 3, de la loi du …".
        if (ArticleFirst.Match(head) is { Success: true } first)
        {
            var words = first.Groups["rest"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var take = Math.Min(4, words.Length - 1); take >= 1; take--)
            {
                var numberText = string.Join(' ', words[..take]).TrimEnd(',');
                if (ArticleNumbers.FromCitation(numberText) is not { } number)
                {
                    continue;
                }

                var next = take;
                var comma = words[take - 1].EndsWith(',');
                var part = ReadPart(words, ref next, ref comma);
                var rest = (comma ? ", " : string.Empty) + string.Join(' ', words[next..]);
                if (OfWord.Match(rest) is not { Success: true } of || Act(of.Groups["act"].Value.Trim(), allowTitle: true) is not { } act)
                {
                    continue;
                }

                return new Cited(act, numberText, number, part);
            }
        }

        // "Code du travail, article L. 121-6", "32016R0679 article 6", "loi du 10 août 1991, art. 5", "loi du … art. 2 al. 3".
        if (ActFirst.Match(head) is { Success: true } last &&
            Act(last.Groups["act"].Value.Trim().TrimEnd(',').Trim(), allowTitle: true) is { } named)
        {
            var words = last.Groups["number"].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var take = Math.Min(4, words.Length); take >= 1; take--)
            {
                var numberText = string.Join(' ', words[..take]).TrimEnd(',');
                if (ArticleNumbers.FromCitation(numberText) is not { } cited)
                {
                    continue;
                }

                var next = take;
                var comma = false;
                var part = ReadPart(words, ref next, ref comma);
                if (next == words.Length)
                {
                    return new Cited(named, numberText, cited, part);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The parts of an article a citation names after its number ("al. 3", "alinéa 3", "paragraphe 1", "§ 1", "point 2"), as
    /// typed, read from <paramref name="next"/> on; null when there are none. <paramref name="comma"/> says whether a comma
    /// closes what was read.
    /// </summary>
    private static string? ReadPart(string[] words, ref int next, ref bool comma)
    {
        var parts = new List<string>();
        while (next + 1 < words.Length + 1 && next < words.Length)
        {
            var word = words[next].TrimStart(',').TrimEnd(',');
            if (!ArticlePartWord.IsMatch(word) || next + 1 >= words.Length)
            {
                break;
            }

            var value = words[next + 1];
            if (!ArticlePartValue.IsMatch(value.TrimEnd(',')))
            {
                break;
            }

            parts.Add(word + " " + value.TrimEnd(','));
            comma = value.EndsWith(',');
            next += 2;
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
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
                    return new ReferenceAct(ReferenceActForm.Memorial, value, MemorialNumber: number, DateNotRead: true, ActDateAsTyped: year);
                }

                year = issued.Date[..4];
            }

            return new ReferenceAct(
                ReferenceActForm.Memorial, value,
                MemorialYear: int.Parse(year, NumberStyles.None, CultureInfo.InvariantCulture),
                MemorialNumber: number,
                MemorialDate: issued);
        }

        // An article before a type and date or an EU act's name is not part of it ("la loi du 12 novembre 2011").
        var unarticled = LeadingArticle.Replace(value, string.Empty);
        if (TypeAndDate.Match(unarticled) is { Success: true } typed && CitationDates.IsDateShaped(typed.Groups["date"].Value) &&
            LuxembourgTypes.TryGetValue(ArticleNumbers.Fold(typed.Groups["type"].Value), out var eliType))
        {
            var actDate = CitationDates.Read(typed.Groups["date"].Value);
            var rest = typed.Groups["rest"].Value.Trim().Trim(',').Trim();
            return new ReferenceAct(
                ReferenceActForm.TypeAndDate, value,
                LuxembourgType: eliType,
                ActDate: actDate,
                TitleWords: rest.Length == 0 ? null : rest,
                DateNotRead: actDate is null,
                ActDateAsTyped: typed.Groups["date"].Value);
        }

        if (EuropeNameShape.IsMatch(unarticled))
        {
            // An EU act's name reads to its CELEX, or the text is no reference: it never falls to the Luxembourg title path.
            if (EuropeName.Match(unarticled) is not { Success: true } named || EuropeNameCelexes(named) is not { Count: > 0 } celexes)
            {
                return null;
            }

            var europeActDate = named.Groups["actdate"].Success ? CitationDates.Read(named.Groups["actdate"].Value) : null;
            return new ReferenceAct(ReferenceActForm.EuropeName, value, Celex: celexes[0], CelexCandidates: celexes,
                ActDate: europeActDate,
                DateNotRead: named.Groups["actdate"].Success && europeActDate is null,
                ActDateAsTyped: named.Groups["actdate"].Success ? named.Groups["actdate"].Value : null);
        }

        if (allowTitle)
        {
            var folded = ArticleNumbers.Fold(unarticled);
            var firstWord = folded.Split(' ', 2)[0];
            if (InstrumentWords.Contains(firstWord, StringComparer.Ordinal) && folded.Length > firstWord.Length)
            {
                return new ReferenceAct(ReferenceActForm.Title, value, Title: unarticled);
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
            CitationDate? version = null;
            if (europeEli.Groups["date"].Success)
            {
                version = CitationDates.Read(europeEli.Groups["date"].Value);
                if (version is null)
                {
                    return null;
                }
            }

            return new ReferenceAct(ReferenceActForm.Celex, token,
                Celex: $"3{europeEli.Groups["year"].Value}{letter}{number.ToString("0000", CultureInfo.InvariantCulture)}",
                EuropeLanguage: europeEli.Groups["language"].Success ? europeEli.Groups["language"].Value.ToLowerInvariant() : null,
                EuropeVersionDate: version);
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
    /// The CELEXes an EU act's conventional name can read to, in the order they are tried: sector 3, the year, R for a
    /// regulation, L for a directive, D for a decision, and the number on four digits. The year is read first when the name is
    /// an Union act's ("(UE)", "(EU)") or its first part is a year from 2015 on (the numbering since 2015: 2016/679, 2019/2088);
    /// a regulation numbered "n°"/"No" before 2015 writes its number first (No 261/2004); every other name writes its year first
    /// (2006/112/EC). The other order is tried after the first, so "(UE) n° 600/2014" and "(CE) 593/2008" read as their acts. A
    /// two-digit year is of the 1900s. A pair in which neither order is a year and a number reads to nothing.
    /// </summary>
    private static IReadOnlyList<string> EuropeNameCelexes(Match named)
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
        var union = named.Groups["org"].Success && ArticleNumbers.Fold(named.Groups["org"].Value) is "ue" or "eu";
        var yearFirst = union ||
            (a.Length == 4 && int.Parse(a, NumberStyles.None, CultureInfo.InvariantCulture) >= 2015) ||
            !(letter == 'R' && named.Groups["no"].Success);
        var orders = yearFirst ? new[] { (a, b), (b, a) } : new[] { (b, a), (a, b) };
        var celexes = new List<string>();
        foreach (var (yearText, numberText) in orders)
        {
            var year = yearText.Length == 2 ? "19" + yearText : yearText;
            if (year.Length != 4 || numberText.Length > 4 ||
                !int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y is < 1952 or > 2099 ||
                !int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
            {
                continue;
            }

            var celex = $"3{y.ToString(CultureInfo.InvariantCulture)}{letter}{n.ToString("0000", CultureInfo.InvariantCulture)}";
            if (!celexes.Contains(celex, StringComparer.Ordinal))
            {
                celexes.Add(celex);
            }
        }

        return celexes;
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
