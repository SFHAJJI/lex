using Lex.V3.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.References;

/// <summary>
/// How a query is read as an exact reference before any word is searched (the plan's item 4.2; R59; R90's resolver-first
/// order and its protected tokens): each kind of reference, the dates a citation writes (read strictly, never near), and the
/// queries that are no reference, which are searched as words as before.
/// </summary>
[TestClass]
public sealed class ExactReferenceReaderTests
{
    private static ExactReference Read(string query) =>
        ExactReferenceReader.Read(query) ?? throw new AssertFailedException($"\"{query}\" was not read as a reference");

    [TestMethod]
    [DataRow("2021-03-15", "2021-03-15")]
    [DataRow("15/03/2021", "2021-03-15")]
    [DataRow("15.03.2021", "2021-03-15")]
    [DataRow("15-03-2021", "2021-03-15")]
    [DataRow("5/3/2021", "2021-03-05")]
    [DataRow("15 mars 2021", "2021-03-15")]
    [DataRow("1er mars 2021", "2021-03-01")]
    [DataRow("1 mars 2021", "2021-03-01")]
    [DataRow("10 août 1991", "1991-08-10")]
    [DataRow("10 aout 1991", "1991-08-10")]
    [DataRow("15 March 2021", "2021-03-15")]
    [DataRow("March 15, 2021", "2021-03-15")]
    [DataRow("March 15 2021", "2021-03-15")]
    [DataRow("1st March 2021", "2021-03-01")]
    [DataRow("15. März 2021", "2021-03-15")]
    [DataRow("29 février 2024", "2024-02-29")]
    public void ACitationDateIsReadInEachFormItIsWrittenIn(string written, string date)
    {
        Assert.IsTrue(CitationDates.IsDateShaped(written), written);
        Assert.AreEqual(date, CitationDates.Read(written)?.Date, written);
    }

    [TestMethod]
    [DataRow("31 février 2021")]
    [DataRow("29 février 2021")]
    [DataRow("2021-02-29")]
    [DataRow("31/04/2021")]
    [DataRow("15/13/2021")]
    [DataRow("03/15/2021")]
    [DataRow("2021-13-01")]
    [DataRow("0 mars 2021")]
    [DataRow("32 March 2021")]
    public void ADateInADatesShapeThatNamesNoCalendarDayIsNeverReadAsANearOne(string written)
    {
        Assert.IsTrue(CitationDates.IsDateShaped(written), written);
        Assert.IsNull(CitationDates.Read(written), written);
    }

    [TestMethod]
    [DataRow("2021")]
    [DataRow("15 foo 2021")]
    [DataRow("15/03/21")]
    [DataRow("mars 2021")]
    [DataRow("article 5 2021")]
    public void ATextInNoDateShapeIsNoDate(string written)
    {
        Assert.IsFalse(CitationDates.IsDateShaped(written), written);
        Assert.IsNull(CitationDates.Read(written), written);
    }

    [TestMethod]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3", "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3")]
    [DataRow("https://legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo", "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo")]
    [DataRow("data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr", "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr")]
    [DataRow("eli/etat/leg/rgd/2021/07/23/a571/jo", "http://data.legilux.public.lu/eli/etat/leg/rgd/2021/07/23/a571/jo")]
    [DataRow("/eli/etat/leg/code/travail/20210315/fr/", "http://data.legilux.public.lu/eli/etat/leg/code/travail/20210315/fr")]
    public void ALuxembourgEliIsReadInEachFormThePublisherWritesIt(string written, string eli)
    {
        var reference = Read(written);
        Assert.AreEqual(ReferenceActForm.LuxembourgEli, reference.Act.Form, written);
        Assert.AreEqual(eli, reference.Act.Eli, written);
        Assert.AreEqual("eli", reference.Kind);
        Assert.IsNull(reference.Article);
        Assert.IsNull(reference.DateAsTyped);
    }

    [TestMethod]
    [DataRow("32016R0679", "32016R0679")]
    [DataRow("32016r0679", "32016R0679")]
    [DataRow("CELEX:32016R0679", "32016R0679")]
    [DataRow("celex 32016R0679", "32016R0679")]
    [DataRow("CELEX n° 32016R0679", "32016R0679")]
    [DataRow("02016R0679-20160504", "02016R0679-20160504")]
    [DataRow("https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679", "32016R0679")]
    [DataRow("https://eur-lex.europa.eu/legal-content/FR/ALL/?uri=CELEX%3A32006L0112&qid=1", "32006L0112")]
    [DataRow("http://publications.europa.eu/resource/celex/32016R0679", "32016R0679")]
    [DataRow("http://data.europa.eu/eli/reg/2016/679/oj", "32016R0679")]
    [DataRow("http://data.europa.eu/eli/dir/2006/112/oj", "32006L0112")]
    [DataRow("Regulation (EU) 2016/679", "32016R0679")]
    [DataRow("règlement (UE) 2016/679", "32016R0679")]
    [DataRow("Directive 2006/112/EC", "32006L0112")]
    [DataRow("directive 2006/112/CE", "32006L0112")]
    [DataRow("Regulation (EC) No 261/2004", "32004R0261")]
    [DataRow("règlement (CE) n° 261/2004", "32004R0261")]
    [DataRow("directive 89/391/CEE", "31989L0391")]
    [DataRow("Décision 2011/833/UE", "32011D0833")]
    public void AnEuActIsReadToItsCelexFromEachFormItIsWrittenIn(string written, string celex)
    {
        var reference = Read(written);
        Assert.IsTrue(reference.Act.IsEurope, written);
        Assert.AreEqual(celex, reference.Act.Celex, written);
    }

    [TestMethod]
    [DataRow("Mém. A n° 571 de 2021", 2021, "571", null)]
    [DataRow("Mémorial A n° 152 du 2 août 2011", 2011, "152", "2011-08-02")]
    [DataRow("Mémorial A - N° 58 du 27 août 1991", 1991, "58", "1991-08-27")]
    [DataRow("Journal officiel du Grand-Duché de Luxembourg, Mémorial A, n° 571 du 26 juillet 2021", 2021, "571", "2021-07-26")]
    [DataRow("JO A 571/2021", 2021, "571", null)]
    [DataRow("Mem. A no 571 2021", 2021, "571", null)]
    public void AMemorialCitationIsReadToItsIssueAndKeepsItsDate(string written, int year, string number, string? date)
    {
        var reference = Read(written);
        Assert.AreEqual(ReferenceActForm.Memorial, reference.Act.Form, written);
        Assert.AreEqual("memorial", reference.Kind);
        Assert.AreEqual(year, reference.Act.MemorialYear, written);
        Assert.AreEqual(number, reference.Act.MemorialNumber, written);
        Assert.AreEqual(date, reference.Act.MemorialDate?.Date, written);
        Assert.IsNull(reference.DateNotRead, written);
    }

    [TestMethod]
    public void AHistoricalIdentifierAndThisSitesAddressesAreRead()
    {
        Assert.AreEqual("1790A0001Z", Read("1790A0001Z").Act.HistoricalLegalId);
        Assert.AreEqual("2013A3498A", Read("2013a3498a").Act.HistoricalLegalId);
        Assert.AreEqual("historical_legal_id", Read("2013A34980").Kind);

        var digest = new string('a', 64);
        foreach (var (written, address) in new[]
                 {
                     ("/lu-legilux/loi-1991-08-10-n3", "/lu-legilux/loi-1991-08-10-n3"),
                     ("/fr/lu-legilux/loi-1991-08-10-n3", "/lu-legilux/loi-1991-08-10-n3"),
                     ($"https://law.soufien.lu/en/lu-legilux/loi-1991-08-10-n3/2024-02-01--{digest}/fra/art_5",
                         $"/lu-legilux/loi-1991-08-10-n3/2024-02-01--{digest}/fra/art_5"),
                     ("/eu-eurlex/32016R0679", "/eu-eurlex/32016R0679"),
                     ($"/eu-eurlex/32016R0679/2016-04-27--{digest}/eng/006", $"/eu-eurlex/32016R0679/2016-04-27--{digest}/eng/006"),
                 })
        {
            var reference = Read(written);
            Assert.AreEqual(ReferenceActForm.Permalink, reference.Act.Form, written);
            Assert.AreEqual("permalink", reference.Kind, written);
            Assert.AreEqual(address, reference.Act.Address, written);
        }
    }

    [TestMethod]
    public void ALuxembourgActByItsTypeAndDateKeepsItsTitleWords()
    {
        var loi = Read("loi du 10 août 1991");
        Assert.AreEqual(ReferenceActForm.TypeAndDate, loi.Act.Form);
        Assert.AreEqual("loi", loi.Act.LuxembourgType);
        Assert.AreEqual("1991-08-10", loi.Act.ActDate!.Date);
        Assert.IsNull(loi.Act.TitleWords);
        Assert.AreEqual("citation", loi.Kind);

        var titled = Read("loi modifiée du 10 août 1991 sur la profession d'avocat");
        Assert.AreEqual("sur la profession d'avocat", titled.Act.TitleWords);

        Assert.AreEqual("rgd", Read("règlement grand-ducal du 3 mars 2017").Act.LuxembourgType);
        Assert.AreEqual("rgd", Read("Règlement grand-ducal du 13 octobre 2017").Act.LuxembourgType);
        Assert.AreEqual("agd", Read("arrêté grand-ducal du 1er juin 2000").Act.LuxembourgType);
        Assert.AreEqual("2000-06-01", Read("arrêté grand-ducal du 1er juin 2000").Act.ActDate!.Date);

        // A date in the title earlier than the act's own date is the title's, never a date to read the act at.
        var convention = Read("Loi du 28 novembre 1961 portant approbation de la Convention Eurocontrol, signée à Bruxelles, le 13 décembre 1960");
        Assert.AreEqual("1961-11-28", convention.Act.ActDate!.Date);
        Assert.IsNull(convention.Date);
        StringAssert.Contains(convention.Act.TitleWords, "le 13 décembre 1960");
    }

    [TestMethod]
    [DataRow("Code du travail, article L. 121-6, 15 mars 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, art. L.121-6, 15/03/2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, article L 121-6, 2021-03-15", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, article L. 121-6 le 15 mars 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, article L. 121-6, on March 15, 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("art. L. 121-6 du Code du travail", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", null)]
    [DataRow("article 5 de la loi du 10 août 1991 au 15 mars 2021", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", "5", "2021-03-15")]
    [DataRow("article premier de la loi du 10 août 1991", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", "1", null)]
    [DataRow("art. 1er du règlement grand-ducal du 13 octobre 2017", nameof(ReferenceActForm.TypeAndDate), "règlement grand-ducal du 13 octobre 2017", "1", null)]
    [DataRow("loi du 10 août 1991, art. 42ter", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", "42ter", null)]
    [DataRow("loi du 10 août 1991, 1er janvier 2020", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", null, "2020-01-01")]
    [DataRow("Article 6 of Regulation (EU) 2016/679", nameof(ReferenceActForm.EuropeName), "Regulation (EU) 2016/679", "6", null)]
    [DataRow("article 6 du règlement (UE) 2016/679, 15 mars 2021", nameof(ReferenceActForm.EuropeName), "règlement (UE) 2016/679", "6", "2021-03-15")]
    [DataRow("32016R0679, article 6", nameof(ReferenceActForm.Celex), "32016R0679", "6", null)]
    [DataRow("32016R0679 art. 38l", nameof(ReferenceActForm.Celex), "32016R0679", "38l", null)]
    [DataRow("articles 141 à 144 du Code de commerce", nameof(ReferenceActForm.Title), "Code de commerce", "141 à 144", null)]
    [DataRow("Loi sur le timbre, article XXVII", nameof(ReferenceActForm.Title), "Loi sur le timbre", "XXVII", null)]
    public void ACitationOfAnArticleOfAnActAtADateKeepsEachOfItsParts(
        string written, string form, string act, string? article, string? date)
    {
        var reference = Read(written);
        Assert.AreEqual("citation", reference.Kind, written);
        Assert.AreEqual(form, reference.Act.Form.ToString(), written);
        Assert.AreEqual(act, reference.Act.AsTyped, written);
        Assert.AreEqual(article, reference.Article?.Key, written);
        Assert.AreEqual(date, reference.Date?.Date, written);
        Assert.IsNull(reference.DateNotRead, written);
    }

    [TestMethod]
    [DataRow("Code du travail, article L. 121-6, 31 février 2021")]
    [DataRow("article 5 de la loi du 10 août 1991, 2021-02-29")]
    [DataRow("loi du 31 février 1991")]
    [DataRow("Mémorial A n° 152 du 30 février 2011")]
    public void ADateThatNamesNoCalendarDayIsKeptAsNotReadAndNeverDropped(string written)
    {
        var reference = Read(written);
        Assert.AreEqual("date_not_a_calendar_date", reference.DateNotRead, written);
    }

    [TestMethod]
    [DataRow("assemblée générale")]
    [DataRow("garantie locative")]
    [DataRow("personal data")]
    [DataRow("loi")]
    [DataRow("Code du travail")]
    [DataRow("Loi sur le timbre")]
    [DataRow("contrat de bail, article 5")]
    [DataRow("article 5 du contrat")]
    [DataRow("article 5")]
    [DataRow("Que disait l'art. L. 121-6 le 15 mars 2021?")]
    [DataRow("combien de jours de congé j'ai le droit quand mon père est décédé")]
    [DataRow("2016")]
    [DataRow("15 mars 2021")]
    [DataRow("Regulation (EU) 2016/679 of the European Parliament")]
    [DataRow("congé parental de 2021")]
    [DataRow("")]
    public void AQueryThatIsNoExactReferenceIsLeftToTheWordSearch(string query)
    {
        Assert.IsNull(ExactReferenceReader.Read(query), query);
    }

    /// <summary>
    /// R90's protected tokens, as a gold set in French and English: each case names the tokens that must survive the phase. A
    /// reference keeps each one in what it read (its act, article and date); a query that is no reference is searched as typed,
    /// so the word search keeps them. None is dropped either way.
    /// </summary>
    internal static readonly (string Query, string? Act, string? Article, string? Date)[] ProtectedTokenGoldSet =
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

    [TestMethod]
    public void NoProtectedTokenOfTheGoldSetIsDropped()
    {
        foreach (var (query, act, article, date) in ProtectedTokenGoldSet)
        {
            var reference = ExactReferenceReader.Read(query);
            if (act is null)
            {
                // Searched as typed: the strict lane matches the query as it is, so every token of it is searched.
                Assert.IsNull(reference, query);
                continue;
            }

            Assert.IsNotNull(reference, query);
            Assert.AreEqual(act, reference.Act.AsTyped, query);
            Assert.AreEqual(article, reference.Article?.Key, query);
            Assert.AreEqual(date, reference.Date?.Date, query);
            if (article is not null)
            {
                // The number as typed is kept beside its key, so the recognition prints what was read.
                Assert.IsNotNull(reference.ArticleAsTyped, query);
            }
        }
    }
}
