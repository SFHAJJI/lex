using Lex.V3.Api;
using Lex.V3.TestSupport;
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
    [DataRow("2012-1-5", "2012-01-05")]
    [DataRow("2012-01-5", "2012-01-05")]
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
    [DataRow("règlement (UE) n° 2019/2088", "32019R2088")]
    [DataRow("Règlement (UE) n° 2016/679", "32016R0679")]
    [DataRow("Regulation (EU) No 2016/679", "32016R0679")]
    [DataRow("Regulation (EU) No 600/2014", "32014R0600")]
    [DataRow("règlement (UE) n° 600/2014", "32014R0600")]
    [DataRow("règlement (CE) 593/2008", "32008R0593")]
    [DataRow("Règlement (CE) n° 593/2008", "32008R0593")]
    [DataRow("règlement (UE) n° 2019/1150", "32019R1150")]
    [DataRow("Règlement (UE) 2016/679 du 27 avril 2016", "32016R0679")]
    [DataRow("Règlement (UE) 2016/679 du Parlement européen et du Conseil du 27 avril 2016", "32016R0679")]
    [DataRow("Regulation (EU) 2016/679 of the European Parliament and of the Council of 27 April 2016", "32016R0679")]
    [DataRow("le règlement (UE) 2016/679", "32016R0679")]
    [DataRow("la directive 2006/112/CE", "32006L0112")]
    [DataRow("http://data.europa.eu/eli/reg/2016/679/oj/eng", "32016R0679")]
    [DataRow("https://eur-lex.europa.eu/eli/reg/2016/679/oj", "32016R0679")]
    [DataRow("eur-lex.europa.eu/eli/reg/2016/679/oj", "32016R0679")]
    [DataRow("http://data.europa.eu/eli/dir/2006/112/2016-06-01", "32006L0112")]
    public void AnEuActIsReadToItsCelexFromEachFormItIsWrittenIn(string written, string celex)
    {
        var reference = Read(written);
        Assert.IsTrue(reference.Act.IsEurope, written);
        Assert.AreEqual(celex, reference.Act.Celex, written);
    }

    [TestMethod]
    public void AnEuNameWhoseNumberAndYearCouldBeEitherReadsToBothOrdersAndNoneFallsToALuxembourgTitle()
    {
        // (UE) or a first part from 2015 on: the year first; the other order is tried after it.
        CollectionAssert.AreEqual(new[] { "32019R2088", "32088R2019" }, Read("règlement (UE) n° 2019/2088").Act.CelexCandidates!.ToArray());
        CollectionAssert.AreEqual(new[] { "32020R2021", "32021R2020" }, Read("règlement 2020/2021").Act.CelexCandidates!.ToArray());
        CollectionAssert.AreEqual(new[] { "32004R0261" }, Read("Regulation (EC) No 261/2004").Act.CelexCandidates!.ToArray());
        Assert.AreEqual("2016-04-27", Read("Règlement (UE) 2016/679 du 27 avril 2016").Act.ActDate!.Date, "the act's own date is kept");
        // An expression's ELI names its language; a consolidated version's, its date.
        Assert.AreEqual("eng", Read("http://data.europa.eu/eli/reg/2016/679/oj/eng").Act.EuropeLanguage);
        Assert.AreEqual("2016-06-01", Read("http://data.europa.eu/eli/dir/2006/112/2016-06-01").Act.EuropeVersionDate!.Date);
        // A text in an EU name's shape that reads to no CELEX is no reference, never a Luxembourg title.
        foreach (var text in new[] { "règlement (UE) n° 5/6", "article 6 du règlement (UE) n° 99/99999", "Règlement (UE) 2016/679 relatif à la protection des données" })
        {
            Assert.IsNull(ExactReferenceReader.Read(text), text);
        }
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
        // A date in the title earlier than the act's own date, after a comma or "le": two readings, the title's (tried first by
        // the index, which takes it when its title words name an act of the day) and the date asked's.
        var convention = Read("Loi du 28 novembre 1961 portant approbation de la Convention Eurocontrol, signée à Bruxelles, le 13 décembre 1960");
        Assert.AreEqual("1961-11-28", convention.Act.ActDate!.Date);
        Assert.AreEqual("1960-12-13", convention.Date!.Date, "the date asked, if the title words name no act");
        Assert.IsNotNull(convention.AsTitle);
        Assert.IsNull(convention.AsTitle.Date);
        StringAssert.Contains(convention.AsTitle.Act.TitleWords, "le 13 décembre 1960");
        Assert.IsNull(Read("article 5 de la loi du 10 août 1991 au 1er janvier 1990").AsTitle, "after au, the date asked alone");

        // An article before the type, and an article after a comma alone.
        Assert.AreEqual("1991-08-10", Read("la loi du 10 août 1991").Act.ActDate!.Date);
        Assert.AreEqual("2", Read("art. 2, loi du 10 août 1991").Article!.Key);
    }

    [TestMethod]
    [DataRow("Code du travail, article L. 121-6, 15 mars 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, art. L.121-6, 15/03/2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, article L 121-6, 2021-03-15", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, article L. 121-6 le 15 mars 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("Code du travail, article L. 121-6, on March 15, 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
    [DataRow("art. L. 121-6 du Code du travail", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", null)]
    [DataRow("article 5 de la loi du 10 août 1991 au 15 mars 2021", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", "5", "2021-03-15")]
    [DataRow("article 5 de la loi du 10 août 1991 au 1er janvier 1990", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", "5", "1990-01-01")]
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
    [DataRow("article 6 du règlement (UE) 2016/679 du 27 avril 2016", nameof(ReferenceActForm.EuropeName), "règlement (UE) 2016/679 du 27 avril 2016", "6", null)]
    [DataRow("article 6 du règlement (UE) n° 2016/679", nameof(ReferenceActForm.EuropeName), "règlement (UE) n° 2016/679", "6", null)]
    [DataRow("art. 2, loi du 10 août 1991, 15 mars 2024", nameof(ReferenceActForm.TypeAndDate), "loi du 10 août 1991", "2", "2024-03-15")]
    // The citation generator's usual form of a version dated by its entry into force (S7a; R21) asks for that version.
    [DataRow("Art. 1er de la loi du 3 novembre 1798, dans sa version publiée, entrée en vigueur le 7 novembre 1798", nameof(ReferenceActForm.TypeAndDate), "loi du 3 novembre 1798", "1", "1798-11-07")]
    [DataRow("Code du travail, article L. 121-6, as published, entry into force on March 15, 2021", nameof(ReferenceActForm.Title), "Code du travail", "L.121-6", "2021-03-15")]
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
    [DataRow("loi du 10 août 1991 art. 2 al. 3", "2", "al. 3")]
    [DataRow("article 2, alinéa 3, de la loi du 10 août 1991", "2", "alinéa 3")]
    [DataRow("article 6, paragraphe 1, du règlement (UE) 2016/679", "6", "paragraphe 1")]
    [DataRow("article 6 § 1 du règlement (UE) 2016/679", "6", "§ 1")]
    [DataRow("art. 5, al. 2, point 3, de la loi du 10 août 1991", "5", "al. 2, point 3")]
    public void APartOfAnArticleIsKeptAsTypedAndTheArticleIsTheOneCited(string written, string article, string part)
    {
        var reference = Read(written);
        Assert.AreEqual(article, reference.Article!.Key, written);
        Assert.AreEqual(part, reference.ArticlePart, written);
        StringAssert.EndsWith(reference.PrintedArticle(europe: false), ", " + part, written);
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

    [TestMethod]
    public void NoProtectedTokenOfTheGoldSetIsDropped()
    {
        foreach (var (query, act, article, date) in ProtectedTokenGoldSet.Cases)
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
