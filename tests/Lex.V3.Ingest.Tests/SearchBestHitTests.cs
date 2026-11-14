using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Index;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The best-hit decision (<see cref="V3CorpusMount.SearchBestHitRule"/>): the first hit of a ranked answer is quoted alone only
/// when it is clearly ahead. A relaxed hit whose heading shares one common word with the query is not (2026-11-14: art. 9 of
/// the 1997 insurance law was quoted for "Quand la garantie d'un contrat d'assurance prend-elle effet ?").
/// </summary>
[TestClass]
public sealed class SearchBestHitTests
{
    private static string Decision(params (string Lane, string Work, string Article, SearchRank Rank)[] ranked)
    {
        using var document = JsonSerializer.SerializeToDocument(V3CorpusMount.BestHit(ranked));
        return document.RootElement.GetProperty("decision").GetString()!;
    }

    [TestMethod]
    public void ARelaxedHitIsClearlyAheadOnlyWhenItsHeadingHoldsTwoQueryWordsOrTheRarest()
    {
        var other = ("relaxed", "loi-1997-07-27-n10", "art_10", new SearchRank(0, 9, false));
        Assert.AreEqual("close", Decision(("relaxed", "loi-1997-07-27-n10", "art_9", new SearchRank(1.4, 9, false, 1, false)), other),
            "one common word in the heading: the first three are offered");
        Assert.AreEqual("clearly_ahead", Decision(("relaxed", "loi-1997-07-27-n10", "art_44", new SearchRank(9.5, 14, false, 2, true)), other),
            "délai and prescription in the heading");
        Assert.AreEqual("clearly_ahead", Decision(("relaxed", "loi-1997-07-27-n10", "art_37", new SearchRank(4.1, 9, false, 1, true)), other),
            "the rarest word alone in the heading");
        Assert.AreEqual("close", Decision(("relaxed", "loi-1997-07-27-n10", "art_9", new SearchRank(1.4, 9, false, 1, false))),
            "a single hit with one common word in its heading is not clearly ahead either");
        Assert.AreEqual("clearly_ahead", Decision(("strict", "loi-1997-07-27-n10", "art_44", new SearchRank(1, 1, true)), ("strict", "x", "art_1", new SearchRank(0, 1, false))),
            "the strict lane is unchanged: the phrase in the heading leads");
        Assert.AreEqual("none", Decision());
    }
}
