namespace Lex.V3.TestSupport;

/// <summary>
/// R90's protected tokens (article numbers, dates, identifiers, negations and quoted phrases), as a gold set in French and
/// English for the exact-reference phase of search (the plan's item 4.2): each case names the act, the article key and the
/// date that must survive the phase, or none for a query that is no reference. A reference keeps each in what it recognised;
/// a query that is no reference is searched as typed, so the word search keeps every token. <c>ExactReferenceReaderTests</c>
/// holds the reader to it and <c>V3CorpusReferenceMountTests</c> the whole search, which is why it lives here.
/// </summary>
public static class ProtectedTokenGoldSet
{
    public static readonly (string Query, string? Act, string? Article, string? Date)[] Cases =
    [
        ("Code du travail, article L. 121-6, 15 mars 2021", "Code du travail", "L.121-6", "2021-03-15"),
        ("Code du travail, article L.121-6, 15 March 2021", "Code du travail", "L.121-6", "2021-03-15"),
        ("article 1er de la loi du 10 août 1991", "loi du 10 août 1991", "1", null),
        ("article premier de la loi du 10 août 1991", "loi du 10 août 1991", "1", null),
        ("article 42ter de la loi du 10 août 1991", "loi du 10 août 1991", "42ter", null),
        ("article 42 de la loi du 10 août 1991", "loi du 10 août 1991", "42", null),
        ("article 42-1 de la loi du 10 août 1991", "loi du 10 août 1991", "42-1", null),
        ("article 5 de la loi du 10 août 1991 au 1er janvier 2020", "loi du 10 août 1991", "5", "2020-01-01"),
        ("Article 6 of Regulation (EU) 2016/679, as at 25 May 2018", "Regulation (EU) 2016/679", "6", "2018-05-25"),
        ("Article 38l of Regulation (EU) No 600/2014", "Regulation (EU) No 600/2014", "38l", null),
        ("32016R0679", "32016R0679", null, null),
        ("02016R0679-20160504", "02016R0679-20160504", null, null),
        ("Mém. A n° 571 de 2021", "Mém. A n° 571 de 2021", null, null),
        ("1790A0001Z", "1790A0001Z", null, null),
        ("http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3", "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3", null, null),
        ("loi du 10 août 1991, 15/03/2021", "loi du 10 août 1991", null, "2021-03-15"),
        ("Que disait l'art. L. 121-6 le 15 mars 2021?", null, null, null),
        ("pas de congé sans solde", null, null, null),
        ("not in force before 2018", null, null, null),
        ("« garantie locative »", null, null, null),
    ];
}
