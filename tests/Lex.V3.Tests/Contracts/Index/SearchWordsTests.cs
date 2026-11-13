using System.Text;
using Lex.V3.Contracts.Index;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Index;

/// <summary>
/// How a search matches words (<see cref="SearchWords.Rule"/>), from what the live site showed on 2026-11-13: "délai de
/// prescription contrat d'assurance" listed labour-code articles first because "de" matched inside "décide" and "demandeur",
/// and the publisher's files glue a heading to its article ("Article 44Délai de prescription1.Le délai").
/// </summary>
[TestClass]
public sealed class SearchWordsTests
{
    private static byte[] Folded(string text) => Encoding.UTF8.GetBytes(TextFolding.Fold(SearchWords.Segment(text)));

    [TestMethod]
    public void AWordMatchesAWholeWordAndNeverAPartOfOne()
    {
        var text = Folded("L'employeur décide; le demandeur dépose sa demande.");
        Assert.IsFalse(SearchWords.HoldsBounded(text, "de"u8), "de is inside décide, demandeur and demande only");
        Assert.IsTrue(SearchWords.HoldsBounded(text, "demandeur"u8));
        Assert.IsFalse(SearchWords.HoldsBounded(text, "mande"u8));
        Assert.IsTrue(SearchWords.HoldsBounded(Folded("le délai de prescription"), "delai de prescription"u8), "a phrase on word bounds");
        Assert.IsFalse(SearchWords.HoldsBounded(Folded("les délais de prescriptions"), "delai de prescription"u8), "a phrase is whole words at both ends");
    }

    [TestMethod]
    public void AQuerysWordsDropFrenchGermanAndEnglishStopwordsAndKeepTheRest()
    {
        CollectionAssert.AreEqual(new[] { "delai", "prescription", "contrat", "assurance" }, SearchWords.Terms("délai de prescription contrat d'assurance").ToArray());
        CollectionAssert.AreEqual(new[] { "delai", "prescription", "actions", "derivant", "contrat", "assurance" },
            SearchWords.Terms("délai de prescription des actions dérivant d'un contrat d'assurance").ToArray());
        CollectionAssert.AreEqual(new[] { "right", "erasure" }, SearchWords.Terms("What does the GDPR article say about the right to erasure").Where(static word => word != "gdpr").ToArray());
        CollectionAssert.AreEqual(new[] { "kundigung", "frist" }, SearchWords.Terms("die Frist der Kündigung").Reverse().ToArray());
        CollectionAssert.AreEqual(new[] { "de", "la" }, SearchWords.Terms("de la").ToArray(), "a query of stopwords alone keeps its words");
        // A token holding a digit is protected (R90): kept whole, its punctuation trimmed, its glued words set apart as the text's are.
        CollectionAssert.AreEqual(new[] { "code", "travail", "121-6", "15", "mars", "2021" },
            SearchWords.Terms("Code du travail, article L. 121-6, 15 mars 2021").ToArray());
        CollectionAssert.AreEqual(new[] { "1 er", "loi", "relative", "oeuvres", "orphelines" },
            SearchWords.Terms("article 1er de la loi relative aux œuvres orphelines").ToArray());
        var text = Folded("Article 1erObjetLa présente loi. Voir l'article L. 121-6, alinéa 2.");
        Assert.IsTrue(SearchWords.HoldsWord(text, SearchWords.Forms("1 er")));
        Assert.IsTrue(SearchWords.HoldsWord(text, SearchWords.Forms("121-6")));
        Assert.IsFalse(SearchWords.HoldsWord(Folded("l'article L. 121-61"), SearchWords.Forms("121-6")), "121-6 is never 121-61");
    }

    [TestMethod]
    public void AWordMatchesItsPluralAndItsSingularFromFourLetters()
    {
        var text = Folded("Toute action dérivant du contrat d'assurance.");
        Assert.IsTrue(SearchWords.HoldsWord(text, SearchWords.Forms("actions")));
        Assert.IsTrue(SearchWords.HoldsWord(Folded("les actions"), SearchWords.Forms("action")));
        Assert.IsFalse(SearchWords.HoldsWord(text, SearchWords.Forms("act")), "a word of three letters is matched as it is");
    }

    [TestMethod]
    public void GluedWordsAreSetApartAndAnArticlesHeadingIsReadFromItsText()
    {
        const string glued = "Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.";
        Assert.AreEqual("Article 44 Délai de prescription 1. Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.", SearchWords.Segment(glued));
        Assert.AreEqual("Délai de prescription", SearchWords.HeadingOf(glued));
        Assert.AreEqual("Obligation de déclaration", SearchWords.HeadingOf("Article 11Obligation de déclarationLe preneur d'assurance a l'obligation de déclarer exactement."));
        Assert.AreEqual("Garanties accessoires", SearchWords.HeadingOf("Art. 74.Garanties accessoiresMême lorsque le sinistre se produit en dehors des biens assurés."));
        Assert.AreEqual("Actions en cessation ou en interdiction", SearchWords.HeadingOf("Article 62-11Actions en cessation ou en interdictionLes mesures visées au livre 3."));
        Assert.AreEqual("Transparency obligations for providers and deployers of certain AI systems",
            SearchWords.HeadingOf("Article 50 Transparency obligations for providers and deployers of certain AI systems 1. Providers shall ensure that AI systems."));
        Assert.AreEqual(string.Empty, SearchWords.HeadingOf("Le contrat est formé dès l'acceptation."), "no article number, no heading");
        // What follows the number is the article's text, not a heading: a paragraph's number, or a sentence that ends first.
        Assert.AreEqual(string.Empty, SearchWords.HeadingOf("Art. 15.(1)L’Assemblée générale annuelle se tient dans la première quinzaine du mois de juillet.(2)L’ordre du jour"));
        Assert.AreEqual(string.Empty, SearchWords.HeadingOf("Art. 3.Le présent règlement s'applique aux avocats. Il entre en vigueur."));
        Assert.AreEqual(string.Empty, SearchWords.HeadingOf("Art. 3.Le présent règlement s'applique aux avocats.Il entre en vigueur."));
        Assert.AreEqual(string.Empty, SearchWords.HeadingOf("Article 7 X" + new string('x', 159) + "Le texte."), "longer than a heading is");
        Assert.AreEqual("S.A.R.L. capital", SearchWords.Segment("S.A.R.L. capital"), "an acronym's stops are no glue");
    }

    [TestMethod]
    public void RarityIsHigherForAWordFewerArticlesHold()
    {
        Assert.IsTrue(SearchWords.Rarity(1000, 3) > SearchWords.Rarity(1000, 300));
        Assert.IsTrue(SearchRank.Compare(new SearchRank(2, 1, false), new SearchRank(1, 9, false)) < 0, "the heading's words rank before the text's");
        Assert.IsTrue(SearchRank.Compare(new SearchRank(1, 5, false), new SearchRank(1, 3, false)) < 0, "then the text's");
    }
}
