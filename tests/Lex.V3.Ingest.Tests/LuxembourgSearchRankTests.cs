using Lex.V3.Contracts.Index;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The ranked lanes of a Luxembourg search (<see cref="SearchWords.Rule"/>), over articles shaped as the publisher's files hold
/// them: a heading glued to its text, and labour-code articles whose only "de" is inside "décide" or "demandeur" (the live
/// site's case of 2026-11-13, where those came first). The words lane ranks by the heading's words, then the text's.
/// </summary>
[TestClass]
public sealed class LuxembourgSearchRankTests
{
    private static readonly (string Work, string Article, string Text)[] Articles =
    [
        ("code-travail", "art_l-121-1", "Art. L. 121-1.Le salarié demandeur décide de la date; le contrat de travail prévoit un délai."),
        ("code-travail", "art_l-121-2", "Art. L. 121-2.Le délai de prescription du contrat d'assurance du salarié est fixé par la loi."),
        ("code-travail", "art_l-122-4", "Art. L. 122-4.L'employeur décide; l'assurance du salarié est prise en charge par le contrat."),
        ("code-travail", "art_l-123-1", "Art. L. 123-1.Le demandeur d'emploi décide de son assurance."),
        ("loi-1997-07-27-n10", "art_44", "Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans."),
        ("loi-1997-07-27-n10", "art_45", "Article 45Suspension et interruption de la prescription1.La prescription court contre les mineurs."),
        ("loi-1997-07-27-n10", "art_46", "Article 46Point de départ1.Le délai de prescription de l'action dérivant du contrat d'assurance court du jour de l'événement."),
        ("loi-1997-07-27-n10", "art_70", "Article 70Créanciers privilégiés et hypothécaires1.Dans la mesure où l'indemnité due en vertu du contrat d'assurance."),
        ("loi-1997-07-27-n10", "art_93-1", "Article 93-1Contrats distinctsLa couverture en protection juridique doit faire l'objet d'un contrat d'assurance distinct; le délai de prescription est celui de l'article 44."),
    ];

    private static LuxembourgSearchTexts Texts()
    {
        var builder = new FoldedSearchTexts.Builder();
        var hits = Articles.Select((article, index) => new LuxembourgIndexSearchHit(
            article.Work, "2025-11-25", new string((char)('a' + index), 64), new string((char)('a' + index), 63) + "0", article.Article, null)).ToArray();
        var numbers = Articles.Select(article => builder.Add(article.Text)).ToArray();
        return new LuxembourgSearchTexts(hits, numbers, builder.Build());
    }

    private static string[] Ranked(string query) =>
        Texts().Rank(null, SearchWords.Terms(query), null).Select(static entry => entry.Hit.PublisherId).ToArray();

    [TestMethod]
    public void TheArticleWhoseHeadingHoldsTheRarestWordsComesFirstAndNoWordMatchesInsideAnother()
    {
        // Every article holding the four words, as whole words or a plural: the heading with the two rarest first, then the
        // heading with contrats (a plural of contrat, which more articles hold), then the headings with none, in the index's
        // order. The labour-code articles holding "de" only inside "décide" or "demandeur", or not every word, are not hits.
        var ranked = Ranked("délai de prescription contrat d'assurance");
        CollectionAssert.AreEqual(new[] { "art_44", "art_93-1", "art_l-121-2", "art_46" }, ranked, string.Join(", ", ranked));
    }

    [TestMethod]
    public void AQuestionsWordsRankTheSameArticleFirst()
    {
        Assert.AreEqual("art_44", Ranked("délai de prescription des actions dérivant d'un contrat d'assurance")[0]);
    }

    [TestMethod]
    public void TheStrictLaneIsThePhraseOnWordBoundsHeadingFirst()
    {
        var strict = Texts().Rank("délai de prescription", [], null).Select(static entry => (entry.Hit.PublisherId, entry.Rank.PhraseInHeading)).ToArray();
        CollectionAssert.AreEqual(new[] { ("art_44", true), ("art_l-121-2", false), ("art_46", false), ("art_93-1", false) }, strict,
            "the article whose heading holds the phrase first, then the index's order");
        Assert.AreEqual(0, Texts().Rank("de la date", [], "loi-1997-07-27-n10").Count, "one work's scope");
        Assert.AreEqual(0, Texts().Rank("mande", [], null).Count, "a phrase never matches inside a word");
    }
}
