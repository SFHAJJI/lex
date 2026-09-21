using Lex.V3.Contracts.Evaluation;

namespace Lex.V3.Tests.Evaluation;

/// <summary>
/// The generator and the derangement behind the shuffled controls. The expected sequences are computed by an independent
/// implementation of SplitMix64 and of the same grouping rule, so a change to either would be a change to what a control
/// shuffles, and it fails here first.
/// </summary>
[TestClass]
public sealed class SeededShuffleTests
{
    [TestMethod]
    [DataRow(0UL, 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL)]
    [DataRow(1UL, 0x910A2DEC89025CC1UL, 0xBEEB8DA1658EEC67UL, 0xF893A2EEFB32555EUL)]
    [DataRow(0xDEADBEEFUL, 0x4ADFB90F68C9EB9BUL, 0xDE586A3141A10922UL, 0x021FBC2F8E1CFC1DUL)]
    public void TheGeneratorReproducesTheReferenceSplitMix64Sequence(ulong seed, ulong first, ulong second, ulong third)
    {
        var random = new SplitMix64(seed);

        Assert.AreEqual(first, random.Next());
        Assert.AreEqual(second, random.Next());
        Assert.AreEqual(third, random.Next());
    }

    [TestMethod]
    public void ABoundedDrawIsTheReferenceSequenceAndStaysInsideItsBound()
    {
        var ten = new SplitMix64(42);
        CollectionAssert.AreEqual(new[] { 3, 1, 8, 4, 0, 2, 5, 8 }, Enumerable.Range(0, 8).Select(_ => ten.NextBelow(10)).ToArray());

        var six = new SplitMix64(7);
        CollectionAssert.AreEqual(new[] { 3, 0, 0, 3, 4, 3, 4, 0 }, Enumerable.Range(0, 8).Select(_ => six.NextBelow(6)).ToArray());

        var one = new SplitMix64(9);
        Assert.IsTrue(Enumerable.Range(0, 20).All(_ => one.NextBelow(1) == 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SplitMix64(1).NextBelow(0));

        var wide = new SplitMix64(3);
        var draws = Enumerable.Range(0, 600).Select(_ => wide.NextBelow(5)).ToArray();
        Assert.IsTrue(draws.All(draw => draw is >= 0 and < 5));
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, draws.Distinct().ToArray(), "every value in the bound is reachable");
    }

    [TestMethod]
    public void TheSameSeedGivesTheSameSequenceAndAnotherSeedADifferentOne()
    {
        var first = new SplitMix64(11);
        var again = new SplitMix64(11);
        var other = new SplitMix64(12);

        Assert.AreEqual(first.Next(), again.Next());
        Assert.AreNotEqual(new SplitMix64(11).Next(), other.Next());
    }

    [TestMethod]
    public void TheDerangementOfSixKeysInThreeGroupsIsTheReferencePermutationAndKeepsNoKey()
    {
        var keys = new[] { "a", "a", "b", "b", "c", "c" };

        var result = SeededDerangement.ByKey(keys, 1);

        CollectionAssert.AreEqual(new[] { 2, 3, 5, 4, 1, 0 }, result);
        Assert.IsTrue(Enumerable.Range(0, keys.Length).All(index => keys[result[index]] != keys[index]));
    }

    [TestMethod]
    public void TheSeedDecidesWhichMemberOfAGroupReceivesWhichAndTheGroupsStillSwap()
    {
        var keys = new[] { "a", "a", "a", "b", "b", "b" };

        CollectionAssert.AreEqual(new[] { 4, 3, 5, 1, 0, 2 }, SeededDerangement.ByKey(keys, 5));
        CollectionAssert.AreEqual(new[] { 4, 5, 3, 2, 0, 1 }, SeededDerangement.ByKey(keys, 6));
        CollectionAssert.AreEqual(SeededDerangement.ByKey(keys, 5), SeededDerangement.ByKey(keys, 5), "the same seed repeats");
    }

    [TestMethod]
    public void EveryResultIsAPermutationThatMovesValuesAndKeepsTheirCounts()
    {
        var keys = new[] { "a", "b", "b", "c", "c", "c", "d", "e", "e", "f" };

        for (ulong seed = 1; seed <= 30; seed++)
        {
            var result = SeededDerangement.ByKey(keys, seed);

            CollectionAssert.AreEquivalent(Enumerable.Range(0, keys.Length).ToArray(), result, "each item is received exactly once");
            CollectionAssert.AreEqual(
                keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                result.Select(index => keys[index]).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                "the values are only moved, so their counts are what they were");
            Assert.IsTrue(Enumerable.Range(0, keys.Length).All(index => keys[result[index]] != keys[index]), $"seed {seed}");
        }
    }

    [TestMethod]
    public void WhereOneKeyIsMoreThanHalfTheItemsExactlyTwiceItsSizeLessTheTotalMustKeepIt()
    {
        var keys = new[] { "x", "x", "x", "x", "x", "y", "y", "y" };

        for (ulong seed = 1; seed <= 20; seed++)
        {
            var result = SeededDerangement.ByKey(keys, seed);

            Assert.AreEqual(2, Enumerable.Range(0, keys.Length).Count(index => keys[result[index]] == keys[index]), $"seed {seed}");
        }

        CollectionAssert.AreEqual(new[] { 5, 2, 7, 4, 6, 3, 1, 0 }, SeededDerangement.ByKey(keys, 3));
    }

    [TestMethod]
    public void OneKeyAloneMovesNothingAndTheEmptyAndSingleCasesAreDefined()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, SeededDerangement.ByKey(["k", "k", "k", "k"], 9));
        CollectionAssert.AreEqual(Array.Empty<int>(), SeededDerangement.ByKey([], 9));
        CollectionAssert.AreEqual(new[] { 0 }, SeededDerangement.ByKey(["only"], 9));
        Assert.ThrowsExactly<ArgumentNullException>(() => SeededDerangement.ByKey(null!, 1));
    }
}
