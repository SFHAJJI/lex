using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

/// <summary>
/// The checked-in list of what Lex never holds (the plan's item 6.10): each class names an instrument by exact identifier forms
/// only, so no held act is ever refused as out of scope, and each says its official place and who to ask, an institution or an
/// official service and never a person (the owner's decision of 2026-10-19).
/// </summary>
[TestClass]
public sealed class V3OutOfScopeTests
{
    [TestMethod]
    public void EveryClassRecognisesItsOwnExamplesAndJ5sCircular()
    {
        foreach (var entry in V3OutOfScope.Classes)
        {
            Assert.IsNotEmpty(entry.Examples, entry.Class);
            foreach (var example in entry.Examples)
            {
                Assert.AreEqual(entry.Class, V3OutOfScope.Recognise(example)?.Class, example);
                Assert.AreEqual(entry.Class, V3OutOfScope.Recognise("  " + example.ToUpperInvariant() + " ")?.Class, example);
            }
        }

        // J5 (05-user-journeys.md): the circular asked for by its name, also with a no-break space and the number sign.
        Assert.AreEqual("cssf_circular", V3OutOfScope.Recognise("Circulaire CSSF 20/747")?.Class);
        Assert.AreEqual("cssf_circular", V3OutOfScope.Recognise("Circulaire CSSF n° 20/747")?.Class);
        Assert.AreEqual("cssf_circular", V3OutOfScope.Recognise("circular of the CSSF 12/552")?.Class);
        Assert.AreEqual("eu_case_law", V3OutOfScope.Recognise("Case C-131/12")?.Class);
        Assert.AreEqual("eu_case_law", V3OutOfScope.Recognise("C-311/18 P")?.Class);
        Assert.AreEqual("cnpd_decision", V3OutOfScope.Recognise("Délibération de la CNPD n° 12FR/2021")?.Class);
    }

    [TestMethod]
    public void NoHeldActOrIdentifierLexServesIsOutOfScope()
    {
        string?[] held =
        [
            null,
            "",
            "   ",
            // The regulator's regulations are admitted (R78), and a CSSF regulation is numbered with a hyphen.
            "Règlement CSSF n° 12-02",
            "Règlement CSSF N° 15-07 du 26 novembre 2015",
            // A held act whose title names a body on the list.
            "Loi du 1er août 2018 portant organisation de la Commission nationale pour la protection des données",
            "loi CNPD",
            "CSSF",
            // Two circulars, a text with a circular inside it, and a circular number with no CSSF: none names one instrument exactly.
            "Circulaire CSSF 20/747 et 21/779",
            "que dit la circulaire CSSF 20/747 ?",
            "Circulaire 20/747",
            // The identifiers Lex serves: Luxembourg coordinates, ELIs, Memorial citations, CELEX of legislation, an EU coordinate.
            "/lu-legilux/loi-2017-03-14-a439",
            "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo",
            "Mémorial A n° 123 du 20 mars 2017",
            "32016R0679",
            "02016R0679-20160504",
            "/eu-eurlex/32016R0679",
            // A Luxembourg court's ECLI, which the list does not hold yet, and an EU court's ECLI without its number.
            "ECLI:LU:CSJ:2020:123",
            "ECLI:EU:C:2014",
            new string('C', V3OutOfScope.MaximumIdentifierLength + 1),
        ];
        foreach (var identifier in held)
        {
            Assert.IsNull(V3OutOfScope.Recognise(identifier), identifier ?? "null");
        }
    }

    /// <summary>
    /// The review of #998, F4: the forms its script asked, both ways. An identifier is read as typed, so a dash, a "CELEX:" before
    /// it, an EUR-Lex address and the full stop after it do not hide what it names; and the forms a circular, a CNPD decision
    /// or a case is cited in are each recognised, while every held or legitimate form the script listed stays unrefused.
    /// </summary>
    [TestMethod]
    public void TheReviewsFormsAreReadAsTypedAndNoHeldFormIsRefused()
    {
        (string Identifier, string Class)[] outOfScope =
        [
            ("Circulaire CSSF 20/747", "cssf_circular"),
            ("circulaire cssf 20/747", "cssf_circular"),
            ("Circular CSSF 22/806", "cssf_circular"),
            ("CSSF circular 22/806", "cssf_circular"),
            ("CSSF 20/747", "cssf_circular"),
            ("Circulaire CSSF\u00a020/747", "cssf_circular"),
            ("Circulaire CSSF n° 12/552", "cssf_circular"),
            ("Circulaire CSSF 12/552 telle que modifiée", "cssf_circular"),
            ("Circulaire CSSF 12/552, telle que modifiée par la circulaire CSSF 21/785", "cssf_circular"),
            ("CSSF Circular 12/552 as amended", "cssf_circular"),
            ("Circulaire CSSF 20/747.", "cssf_circular"),
            ("Circ. CSSF 20/747", "cssf_circular"),
            ("Circulaire IML 98/143", "cssf_circular"),
            ("Circulaire de l\u2019IML 98/143", "cssf_circular"),
            ("Circulaire CSSF-CPDI 25/37", "cssf_circular"),
            ("CSSF-Rundschreiben 20/747", "cssf_circular"),
            ("Délibération CNPD n° 12/2023", "cnpd_decision"),
            ("CNPD decision 3/2021", "cnpd_decision"),
            ("Délibération n° 12/2023 de la CNPD", "cnpd_decision"),
            ("Délibération n° 36FR/2021 de la CNPD", "cnpd_decision"),
            ("ECLI:EU:C:2014:317", "eu_case_law"),
            ("ecli:eu:c:2014:317", "eu_case_law"),
            ("ECLI:EU:C:2014:317 ", "eu_case_law"),
            ("ECLI:EU:C:2014:317.", "eu_case_law"),
            ("62012CJ0131", "eu_case_law"),
            ("62012CJ0131(01)", "eu_case_law"),
            ("62019CC0311", "eu_case_law"),
            ("CELEX:62012CJ0131", "eu_case_law"),
            ("celex: 62012CJ0131", "eu_case_law"),
            ("https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:62012CJ0131", "eu_case_law"),
            ("https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX%3A62012CJ0131&qid=1", "eu_case_law"),
            ("C-131/12", "eu_case_law"),
            ("C\u2011131/12", "eu_case_law"),
            ("C\u2013131/12", "eu_case_law"),
            ("C\u2212131/12", "eu_case_law"),
            ("Case C-131/12", "eu_case_law"),
            ("Affaire C-131/12", "eu_case_law"),
            ("Case C-131/12 Google Spain", "eu_case_law"),
            ("C-131/12, Google Spain and Google", "eu_case_law"),
            ("C-131/12 P", "eu_case_law"),
            ("T-123/19", "eu_case_law"),
            ("C-1/16", "eu_case_law"),
            ("Joined Cases C-293/12 and C-594/12", "eu_case_law"),
            ("C-293/12 et C-594/12", "eu_case_law"),
            ("Affaires jointes C-293/12 et C-594/12, Digital Rights Ireland", "eu_case_law"),
            ("https://curia.europa.eu/juris/liste.jsf?num=C-131/12", "eu_case_law"),
            ("RESA_2020_123.456", "resa"),
            ("RESA 2020/123456", "resa"),
            ("RESA", "resa"),
            ("Recueil électronique des sociétés et associations", "resa"),
        ];
        foreach (var (identifier, expected) in outOfScope)
        {
            Assert.AreEqual(expected, V3OutOfScope.Recognise(identifier)?.Class, identifier);
        }

        string[] held =
        [
            "eli/etat/leg/loi/2016/12/23/a1/jo",
            "http://data.legilux.public.lu/eli/etat/leg/loi/1993/04/05/n1/jo",
            "eli/etat/leg/rcssf/2012/12/14/n1/jo",
            "32016R0679",
            "02016R0679-20160504",
            "32019L1937",
            "52012PC0011",
            "celex:32016R0679",
            "CELEX:32016R0679.",
            "https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32016R0679",
            "Mémorial A n° 123 de 2016",
            "Mémorial A 123/2016",
            "loi du 5 avril 1993 relative au secteur financier",
            "loi du 1er août 2018 portant organisation de la Commission nationale pour la protection des données",
            "Règlement CSSF n° 12-02",
            "Règlement CSSF N° 12-02 du 14 décembre 2012 relatif à la lutte contre le blanchiment",
            "CSSF Regulation No 16-07",
            "Règlement CSSF 22/05",
            "Règlement (UE) 2016/679",
            "Directive 2006/112/CE",
            "1790A0001Z",
            "Code du travail",
            "loi modifiée du 10 août 1915 concernant les sociétés commerciales",
            "C(2020) 1234",
            "Décision (UE) 2020/1234",
            "Avis du Conseil d'Etat",
            "resa",
            "Resale",
            "RESA 2016",
            "Loi du 27 mai 2016 portant réforme du régime de publication légale relatif aux sociétés et associations",
            "règlement grand-ducal du 22 décembre 2016",
            "TVA",
            // A numbered "délibération" that does not name the CNPD is not the CNPD's: a commune numbers its own.
            "Délibération n° 36FR/2021",
            "Délibération n° 12/2023",
            // A company's number in the trade register names a company, not a publication; a Luxembourg court's ECLI is not
            // listed yet; a circular of another body; a case number with no court letter.
            "B123456",
            "ECLI:LU:CSJ:2020:123",
            "Circulaire ministérielle 20/747",
            "131/12",
        ];
        foreach (var identifier in held)
        {
            Assert.IsNull(V3OutOfScope.Recognise(identifier), identifier);
        }
    }

    [TestMethod]
    public void EveryClassNamesItsOfficialPlaceAndAnInstitutionToAskNeverAPerson()
    {
        CollectionAssert.AreEqual(
            new[] { "cssf_circular", "cnpd_decision", "eu_case_law", "resa" },
            V3OutOfScope.Classes.Select(static entry => entry.Class).ToArray());
        foreach (var entry in V3OutOfScope.Classes)
        {
            Assert.IsTrue(entry.Jurisdiction is "lu" or "eu", entry.Class);
            StringAssert.StartsWith(entry.OfficialSource, "https://", entry.Class);
            StringAssert.StartsWith(entry.WhoToAsk.Link, "https://", entry.Class);
            Assert.AreEqual("information", entry.WhoToAsk.Role, entry.Class);
            // One sentence, ending with its full stop, and an institution named by its official name with its abbreviation.
            Assert.AreEqual(1, entry.Reason.Count(static character => character == '.'), entry.Class);
            StringAssert.EndsWith(entry.Reason, ".", entry.Class);
            StringAssert.Matches(entry.WhoToAsk.Name, new System.Text.RegularExpressions.Regex(@"\([A-Z]{3,4}\)$"), entry.Class);
        }

        Assert.AreEqual(64, V3OutOfScope.Sha256.Length);
        Assert.AreEqual(V3OutOfScope.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(V3OutOfScope.CanonicalUtf8())));
    }
}
