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
    public void AQueryOfMoreWordsThanASearchMatchesIsRefusedNeverCut()
    {
        // The review of #1045: a 64th word would have been left out of the check, so an article could match without it.
        var words = Enumerable.Range(0, SearchWords.MaxWords + 1).Select(static index => "w" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => Texts().Rank(null, words, null));
        Assert.AreEqual(long.MaxValue, SearchWords.EveryWord(SearchWords.MaxWords), "63 words: every bit of the mask");
        Assert.AreEqual(3L, SearchWords.EveryWord(2));
    }

    [TestMethod]
    public void AHeadingThatSharesOneCommonWordWithTheQueryIsCountedAsOneWordNotTheRarest()
    {
        // 2026-11-14, Release 6's candidate: "Quand la garantie d'un contrat d'assurance prend-elle effet ?" quoted art. 9, whose
        // heading holds "assurance" alone, over art. 37 ("Prise d'effet de la garantie"), whose text never repeats the act's
        // subject. The ranking says how much of the query each heading holds, so the best-hit decision can refuse such a lead.
        var texts = new FoldedSearchTexts.Builder();
        (string Work, string Article, string Text)[] articles =
        [
            ("loi-1997-07-27-n10", "art_9", "Article 9Proposition d'assurance, police présignée et note de couverture1.La garantie prend effet à la signature du contrat d'assurance."),
            ("loi-1997-07-27-n10", "art_37", "Article 37Prise d'effet de la garantie1.La garantie prend effet aux date et heure indiquées dans les conditions particulières."),
            ("loi-1997-07-27-n10", "art_10", "Article 10Information du preneur d'assurance1.Avant la conclusion du contrat d'assurance, l'assureur informe le preneur."),
            ("loi-1997-07-27-n10", "art_16", "Article 16Preuve et contenu du contrat1.Le contrat d'assurance est prouvé par écrit."),
        ];
        var hits = articles.Select((article, index) => new LuxembourgIndexSearchHit(
            article.Work, "2025-11-25", new string((char)('k' + index), 64), new string((char)('k' + index), 63) + "0", article.Article, null)).ToArray();
        var numbers = articles.Select(article => texts.Add(article.Text)).ToArray();
        var ranked = new LuxembourgSearchTexts(hits, numbers, texts.Build()).Rank(null, SearchWords.Terms("garantie d'un contrat d'assurance prend-elle effet"), null);
        Assert.AreEqual("art_9", ranked[0].Hit.PublisherId, "the only article holding every word");
        Assert.AreEqual(1, ranked[0].Rank.HeadingWords, "its heading holds one query word, assurance");
        Assert.IsFalse(ranked[0].Rank.HeadingHoldsRarest, "and not the query's rarest");
        Assert.AreEqual((2, false), SearchWords.HeadingHolds(0b101, [1.0, 3.0, 2.0]), "two words held, the rarest (the second) not among them");
        Assert.AreEqual((1, true), SearchWords.HeadingHolds(0b010, [1.0, 3.0, 2.0]));
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
