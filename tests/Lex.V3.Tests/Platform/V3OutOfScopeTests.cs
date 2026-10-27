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
