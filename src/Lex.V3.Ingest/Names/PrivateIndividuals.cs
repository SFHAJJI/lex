using System.Text.RegularExpressions;

namespace Lex.V3.Ingest.Names;

/// <summary>
/// The specification of record's R92(1), measured: which classes of acts name private individuals, and a conservative
/// detector of named private individuals in served text. A class is read from what an act is (a Luxembourg act's title, the
/// publisher's own words; an EU act's operative text, since the EU index holds no title); the detector reads served text for
/// a person's particulars next to a person's name. Both answer only class names and reason codes: nothing here returns,
/// records or prints a name or any other personal data, and the measurement that uses them keeps only work identifiers,
/// classes, counts and these codes.
/// </summary>
/// <remarks>
/// <para>
/// The handling (a driver decision, STATUS-DATA.md): the owner's standing order names no person and gives no contact channel
/// on the site, so R92's first option (text with names kept out of search, with an objection contact) is not available. A work
/// of a class that names private individuals, or whose served text or title the detector finds a named private individual
/// in, is pointed to the publisher: kept out of the indexing record (search engines, the sitemap, examples), and its text and
/// titles are not served (the names record, <see cref="NamesRecord"/>). Its identity, hashes and official link stay (R92(2)).
/// </para>
/// <para>
/// Conservative means privacy first: a class is withheld whole, whether or not a given act of it shows a name in its title;
/// a public office holder named with an honorific is counted as a named person too (a false positive costs a law page, a
/// false negative a person's particulars). A name alone in a lower-case office phrase ("Monsieur le Ministre") is no name.
/// </para>
/// </remarks>
public static class PrivateIndividuals
{
    /// <summary>The version of the classes and the detector below; a record names the version that measured it.</summary>
    public const string MeasureVersion = "lex-v3-names-measure/1";

    /// <summary>The one reason a work is withheld under R92: it names private individuals.</summary>
    public const string Reason = "names_private_individuals";

    // ---- Classes. ----

    /// <summary>A Luxembourg act that grants naturalisation or nationality to named people (R92's own example).</summary>
    public const string LuxembourgNaturalisation = "lu_naturalisation";

    /// <summary>A Luxembourg act that grants a title of nobility.</summary>
    public const string LuxembourgNobility = "lu_nobility";

    /// <summary>A Luxembourg act that grants a pension, an allowance, an indemnity or a dispensation to named people.</summary>
    public const string LuxembourgGrantToNamedPerson = "lu_grant_to_named_person";

    /// <summary>A Luxembourg act that transfers property to or from named people (a sale, an exchange, a gift, an acquisition).</summary>
    public const string LuxembourgPropertyOfNamedPerson = "lu_property_of_named_person";

    /// <summary>A Luxembourg act about a named person's civil status (a change of name, a dispensation to marry, a legitimation).</summary>
    public const string LuxembourgCivilStatusOfNamedPerson = "lu_civil_status_of_named_person";

    /// <summary>Any other Luxembourg act whose title names a person.</summary>
    public const string LuxembourgTitleNamesPerson = "lu_title_names_person";

    /// <summary>A Luxembourg act of no class above whose served text the detector finds a named private individual in.</summary>
    public const string LuxembourgTextNamesPerson = "lu_text_names_person";

    /// <summary>Any other Luxembourg act: general legislation.</summary>
    public const string LuxembourgGeneral = "lu_general";

    /// <summary>An EU act of restrictive measures (sanctions) that freezes the assets of the persons its annexes list (R92's other example).</summary>
    public const string EuropeRestrictiveMeasures = "eu_restrictive_measures";

    /// <summary>An EU act of no class above whose served text the detector finds a named private individual in.</summary>
    public const string EuropeTextNamesPerson = "eu_text_names_person";

    /// <summary>Any other EU act: general legislation.</summary>
    public const string EuropeGeneral = "eu_general";

    /// <summary>
    /// The classes whose acts name private individuals, so every act of them is withheld: those read from what an act is, and
    /// the two a work is recorded under when only the detector found a name in its served text.
    /// </summary>
    public static readonly IReadOnlyList<string> NamingClasses = Array.AsReadOnly(new[]
    {
        EuropeRestrictiveMeasures,
        EuropeTextNamesPerson,
        LuxembourgCivilStatusOfNamedPerson,
        LuxembourgGrantToNamedPerson,
        LuxembourgNaturalisation,
        LuxembourgNobility,
        LuxembourgPropertyOfNamedPerson,
        LuxembourgTextNamesPerson,
        LuxembourgTitleNamesPerson,
    });

    /// <summary>Every class, in ordinal order.</summary>
    public static readonly IReadOnlyList<string> Classes = Array.AsReadOnly(
        NamingClasses.Append(LuxembourgGeneral).Append(EuropeGeneral).Order(StringComparer.Ordinal).ToArray());

    // ---- Reason codes: why a work is withheld, never what it says. ----

    /// <summary>The act's class names private individuals (its title, for Luxembourg; its operative text, for the EU).</summary>
    public const string ClassCode = "class";

    /// <summary>A birth date given as a date, next to a person.</summary>
    public const string BirthDateCode = "birth_date";

    /// <summary>A place of birth given as a place.</summary>
    public const string BirthPlaceCode = "birth_place";

    /// <summary>A passport or identity card number.</summary>
    public const string IdentityDocumentCode = "identity_document";

    /// <summary>A national identification number, labelled as one.</summary>
    public const string NationalNumberCode = "national_identification_number";

    /// <summary>A person's place of residence ("demeurant à", "residing at").</summary>
    public const string DomicileCode = "domicile";

    /// <summary>A person named after an honorific (M., Mme, sieur, Mr, Mrs).</summary>
    public const string PersonNamedCode = "person_named";

    /// <summary>Every reason code, in ordinal order.</summary>
    public static readonly IReadOnlyList<string> ReasonCodes = Array.AsReadOnly(new[]
    {
        BirthDateCode, BirthPlaceCode, ClassCode, DomicileCode, IdentityDocumentCode, NationalNumberCode, PersonNamedCode,
    }.Order(StringComparer.Ordinal).ToArray());

    /// <summary>
    /// How far apart two particulars of one person may stand in one article, in characters: a list entry ("Monsieur …, né le
    /// … à …, demeurant à …") is well within it; a general provision's two unrelated phrases rarely are.
    /// </summary>
    public const int Window = 300;

    private const string Upper = "A-ZÀÂÄÇÉÈÊËÎÏÔÖÙÛÜŸ";
    private const string Lower = "a-zàâäçéèêëîïôöùûüÿœæ";
    private const string MonthsFrench = "janvier|février|fevrier|mars|avril|mai|juin|juillet|août|aout|septembre|octobre|novembre|décembre|decembre";
    private const string MonthsEnglish = "January|February|March|April|May|June|July|August|September|October|November|December";

    // A capitalised name token, and one in capitals ("Dupont", "DUPONT", "Jean-Pierre", "O'Neil").
    private const string NameToken = "[" + Upper + "][" + Lower + "'’]+(?:-[" + Upper + "][" + Lower + "'’]+)?";
    private const string CapitalToken = "[" + Upper + "]{2,}(?:-[" + Upper + "]{2,})?";

    // An honorific that is followed by a person's given name and surname.
    private const string Honorific = @"(?:M\.|MM\.|Mme|Mmes|Mlle|Mlles|Madame|Mesdames|Monsieur|Messieurs|Mademoiselle|Mesdemoiselles|Mr\.?|Mrs\.?|Ms\.?|Miss)";

    // An old honorific of the Mémorial ("au sieur Dupont"), followed by a surname alone.
    private const string OldHonorific = "(?:sieurs?|dames?|demoiselles?|veuve|époux|épouse|consorts|feue?)";

    // Words after an honorific that name an office, not a person ("Monsieur le Ministre" has a lower-case article; these
    // catch "Monsieur Ministre" and "Mr President").
    private const string Office =
        "(?!(?:Ministre|Président|Présidente|Premier|Première|Bourgmestre|Directeur|Directrice|Procureur|Juge|Gouverneur|" +
        "Conseiller|Conseillère|Secrétaire|Commissaire|Rapporteur|Rapporteure|Médiateur|Médiatrice|President|Minister|" +
        "Chairman|Chairwoman|Chair|Speaker|Commissioner|Governor|Secretary|Director|Judge|Prosecutor)\b)";

    private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A person named: an honorific with a given name and a surname, or an old honorific with a surname.</summary>
    private static readonly Regex PersonNamed = Pattern(
        $@"\b{Honorific}\s+(?:(?:le|la)\s+[{Lower}]+\s+)?{Office}(?:{NameToken}|[{Upper}]\.(?:-?[{Upper}]\.)*)\s+(?:(?:de|du|des|van|von|der|den|le|la|di|da|dos|del)\s+)?(?:{NameToken}|{CapitalToken})" +
        $@"|\b{OldHonorific}\s+{Office}(?:{NameToken}|{CapitalToken})");

    /// <summary>A birth date given as a date: "né le 1er janvier 1950", "born on 1 January 1950", "date of birth: 01.01.1950".</summary>
    private static readonly Regex BirthDate = Pattern(
        $@"\bnée?s?\s+le\s+(?:\d{{1,2}}(?:er)?\s+(?:{MonthsFrench})\s+\d{{4}}|\d{{1,2}}[./-]\d{{1,2}}[./-]\d{{2,4}})" +
        $@"|\bborn\s+(?:on\s+)?\d{{1,2}}(?:st|nd|rd|th)?\s+(?:{MonthsEnglish})\s+\d{{4}}" +
        $@"|\b(?:date of birth|date de naissance|DOB|D\.O\.B\.)\s*[:,]?\s*(?:\d{{1,2}}[./-]\d{{1,2}}[./-]\d{{2,4}}|\d{{4}}-\d{{2}}-\d{{2}}|\d{{1,2}}(?:er|st|nd|rd|th)?\.?\s+(?:{MonthsFrench}|{MonthsEnglish})\s+\d{{4}})",
        RegexOptions.IgnoreCase);

    /// <summary>A place of birth given as a place: "né à Esch", "born in Vienna", "place of birth: Paris".</summary>
    private static readonly Regex BirthPlace = Pattern(
        $@"\bnée?s?\s+(?:le\s+[^,;:\n]{{1,30}}?\s+)?à\s+[{Upper}][{Lower}]" +
        $@"|\bborn\s+in\s+[A-Z][a-z]" +
        $@"|\b(?:[Pp]lace of birth|[Ll]ieu de naissance|POB)\s*[:,]?\s*[{Upper}][{Lower}]");

    /// <summary>A passport or identity card number.</summary>
    private static readonly Regex IdentityDocument = Pattern(
        @"\b(?i:passeport|passport|carte d'identité|carte d’identité|identity card|ID card)\s*(?i:n[°ºo]\.?|num[ée]ro|number|no\.?|nr\.?)\s*:?\s*[A-Z0-9]{2,}[A-Z0-9 ]{2,}\d");

    /// <summary>A national identification number labelled as one ("matricule 1950010112345", "national identification number: …").</summary>
    private static readonly Regex NationalNumber = Pattern(
        @"\b(?:matricule|numéro d'identification(?: national)?|numéro d’identification(?: national)?|national identification number|personal identification number|identification number)\s*(?:n[°ºo]\.?)?\s*:?\s*\d[\d .]{7,}\d",
        RegexOptions.IgnoreCase);

    /// <summary>A person's residence given as a place ("demeurant à Esch", "residing at …"); "résidant au Luxembourg" is a general rule's.</summary>
    private static readonly Regex Domicile = Pattern(
        $@"\b(?:demeurant|domiciliée?s?|résidant|residing|resident)\s+(?:à|at|in)\s+[{Upper}0-9]");

    /// <summary>
    /// The particulars the detector reads, each with its code, and whether it alone (with a name, or with another) names a
    /// person: a birth date, a place of birth, an identity document and a national number are strong; a residence is strong
    /// only next to a named person.
    /// </summary>
    private static readonly (string Code, Regex Pattern, bool Strong)[] Particulars =
    [
        (BirthDateCode, BirthDate, true),
        (BirthPlaceCode, BirthPlace, true),
        (IdentityDocumentCode, IdentityDocument, true),
        (NationalNumberCode, NationalNumber, true),
        (DomicileCode, Domicile, false),
    ];

    /// <summary>
    /// What the detector finds in one served text, as the reason codes of a named private individual, in ordinal order, or
    /// none. Positive when, within <see cref="Window"/> characters of one another: a particular and a person named; or two
    /// particulars of different kinds, one of them strong. A person named alone is no finding in served text (an act may name
    /// a public office holder); a label alone ("date de naissance", "lieu de naissance") is a general provision's, never a
    /// finding, and so is a residence alone ("résidant au Luxembourg").
    /// </summary>
    public static IReadOnlyList<string> DetectText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return [];
        }

        var particulars = new List<(string Code, int Start, bool Strong)>();
        foreach (var (code, pattern, strong) in Particulars)
        {
            foreach (Match match in pattern.Matches(text))
            {
                particulars.Add((code, match.Index, strong));
            }
        }

        if (particulars.Count == 0)
        {
            return [];
        }

        var people = PersonNamed.Matches(text).Select(static match => match.Index).ToArray();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var particular in particulars)
        {
            if (people.Any(person => Math.Abs(person - particular.Start) <= Window))
            {
                found.Add(particular.Code);
                found.Add(PersonNamedCode);
            }

            // Two particulars of different kinds, one of them strong: a birth date and a place of birth, or a birth date and a
            // residence, describe one person even when the list names them without an honorific ("Dupont, Jean, né le …").
            foreach (var other in particulars)
            {
                if ((particular.Strong || other.Strong) &&
                    !string.Equals(other.Code, particular.Code, StringComparison.Ordinal) &&
                    Math.Abs(other.Start - particular.Start) <= Window)
                {
                    found.Add(particular.Code);
                    found.Add(other.Code);
                }
            }
        }

        return found.ToArray();
    }

    /// <summary>A label of a person's particulars with no value: a general provision's ("les nom, prénoms, date et lieu de naissance").</summary>
    public const string BirthLabelSignal = "birth_label";

    private static readonly Regex BirthLabel = Pattern(
        @"\b(?:date|lieu) de naissance\b|\b(?:date|place) of birth\b|\bdates? et lieux? de naissance\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Every signal one text carries, findings or not, as codes in ordinal order: each particular the detector reads, a person
    /// named, and a particulars' label with no value (<see cref="BirthLabelSignal"/>). The measurement counts them to say what
    /// the detector saw and set aside; only <see cref="DetectText"/> decides.
    /// </summary>
    public static IReadOnlyList<string> SignalsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var signals = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (code, pattern, _) in Particulars)
        {
            if (pattern.IsMatch(text))
            {
                signals.Add(code);
            }
        }

        if (PersonNamed.IsMatch(text))
        {
            signals.Add(PersonNamedCode);
        }

        if (BirthLabel.IsMatch(text))
        {
            signals.Add(BirthLabelSignal);
        }

        return signals.ToArray();
    }

    /// <summary>Whether a text names a person after an honorific (a weak signal on its own; a title's class reads it).</summary>
    public static bool NamesPerson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return PersonNamed.IsMatch(text) || FavourOfPerson.IsMatch(text);
    }

    /// <summary>"En faveur de Jean Dupont", "en faveur du sieur Dupont": a title naming the person it applies to.</summary>
    private static readonly Regex FavourOfPerson = Pattern(
        $@"\ben faveur (?:de|du|des)\s+(?:{OldHonorific}\s+|{Honorific}\s+)?(?:{NameToken}|[{Upper}]\.(?:-?[{Upper}]\.)*)\s+(?:{NameToken}|{CapitalToken})");

    // ---- Luxembourg classes, from the publisher's title. ----

    private static readonly Regex NaturalisationTopic = Pattern(@"\bnaturalis|\bindigénat\b|\bnationalité\b", RegexOptions.IgnoreCase);

    // The grant forms of an individual act: "accordant la naturalisation", "qui confère la naturalisation", "conférant la
    // naturalisation.", "par laquelle la naturalisation est accordée", "pour accepter sa naturalisation", "astreinte à la
    // naturalisation", "accordant la nationalité … à". A general law ("sur les naturalisations", "sur la nationalité
    // luxembourgeoise", "sur l'indigénat") is none of these.
    private static readonly Regex NaturalisationGrant = Pattern(
        @"\b(?:accord\w*|confér\w*|confèr\w*|attribu\w*)\s+(?:au\s+\S+\s+)?(?:la\s+)?(?:naturalis\w*|nationalité)" +
        @"|\bnaturalis\w*\s+(?:luxembourgeoise\s+)?(?:est\s+)?accordée|\bsa\s+naturalis\w*|\bastreinte?\s+à\s+la\s+naturalis\w*",
        RegexOptions.IgnoreCase);

    private static readonly Regex NobilityTopic = Pattern(
        @"\bnoblesse\b|\banobli\w*|\btitres?\s+(?:de\s+)?(?:baron|baronne|comte|comtesse|chevalier|vicomte|marquis)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex GrantTopic = Pattern(
        @"\b(?:pensions?|rentes?|allocations?|indemnités?|gratifications?|secours|subsides?|récompenses?|dotations?|traitements?|dispenses?|remises?)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex PropertyTopic = Pattern(
        @"\b(?:ventes?|vendre|cessions?|céder|échanges?|échanger|donations?|acquisitions?|acquérir|aliénations?|aliéner|expropriations?|baux|bail|concessions?|legs|successions?)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex CivilStatusTopic = Pattern(
        @"\b(?:changement|modification|rectification)\s+(?:de|du|des)\s+(?:nom|noms|prénom|prénoms)\b|\blégitimation\b|\bdispense\s+d['’]âge\b|\bmariage\b",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// The class of a Luxembourg act from its titles (every title and short title the index or the catalogue holds of it), in
    /// this order: naturalisation (its topic and a grant form or a person named); nobility; a grant, a property transfer or a
    /// civil status (each its topic and a person named in the same title); any other title naming a person; else general.
    /// </summary>
    public static string LuxembourgClass(IEnumerable<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        var all = titles.Where(static title => !string.IsNullOrWhiteSpace(title)).ToArray();
        bool Any(Func<string, bool> test) => all.Any(test);
        if (Any(title => NaturalisationTopic.IsMatch(title) && (NaturalisationGrant.IsMatch(title) || NamesPerson(title))))
        {
            return LuxembourgNaturalisation;
        }

        if (Any(NobilityTopic.IsMatch))
        {
            return LuxembourgNobility;
        }

        if (Any(title => GrantTopic.IsMatch(title) && NamesPerson(title)))
        {
            return LuxembourgGrantToNamedPerson;
        }

        if (Any(title => PropertyTopic.IsMatch(title) && NamesPerson(title)))
        {
            return LuxembourgPropertyOfNamedPerson;
        }

        if (Any(title => CivilStatusTopic.IsMatch(title) && NamesPerson(title)))
        {
            return LuxembourgCivilStatusOfNamedPerson;
        }

        return Any(NamesPerson) ? LuxembourgTitleNamesPerson : LuxembourgGeneral;
    }

    // ---- EU classes, from the operative text (the EU index holds no title). ----

    private static readonly Regex AssetFreeze = Pattern(
        @"\b(?:shall be frozen|are frozen|sont gelés|sont gelées|seront gelés|seront gelées)\b", RegexOptions.IgnoreCase);

    private static readonly Regex ListedInAnnex = Pattern(
        @"\b(?:listed in Annex|set out in Annex|referred to in Annex|énumérés?\s+(?:à|dans)\s+l['’]annexe|énumérées?\s+(?:à|dans)\s+l['’]annexe|figurant\s+(?:à|dans)\s+l['’]annexe|mentionnée?s?\s+(?:à|dans)\s+l['’]annexe)",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Whether one article of an EU act is a restrictive measure's operative provision: it freezes funds of the persons an annex
    /// lists. The Treaties' own provisions on restrictive measures (TFEU Article 215) freeze nothing, so they are not.
    /// </summary>
    public static bool FreezesListedPersons(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AssetFreeze.IsMatch(text) && ListedInAnnex.IsMatch(text);
    }

    // ---- Identities. ----

    private static readonly Regex LuxembourgWork = new(
        "^(http://data\\.legilux\\.public\\.lu/eli/etat/leg/(?:[a-z_]+/[0-9]{4}/[0-9]{2}/[0-9]{2}/[^/]+|code/[^/]+|recueil/[^/]+))(?:/|\\z)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The Luxembourg work IRI a subject belongs to (an act's work, its Journal officiel resource, an expression, a
    /// consolidation): <c>…/eli/etat/leg/{type}/{yyyy}/{mm}/{dd}/{n}</c>, a code's <c>…/eli/etat/leg/code/{name}</c>, or null.
    /// </summary>
    public static string? LuxembourgWorkIriOf(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var match = LuxembourgWork.Match(subject);
        return match.Success ? match.Groups[1].Value : null;
    }
}
