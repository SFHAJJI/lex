using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Decision 81: the first EU cut's watermark starts at the census bound, computed under the same
/// tie-safe ordering the rest of the witness uses, never a weaker date-only maximum.
/// </summary>
[TestClass]
public sealed class EuFirstCutWatermarkBootstrapTests
{

    [TestMethod]
    public void AnEmptyCensusRefusesRatherThanInventingASentinel()
    {
        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            Array.Empty<(string, string)>(), out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.NoCensusObservations, refusal);
    }

    [TestMethod]
    public void TheMaximumWatermarkWinsRegardlessOfInputOrder()
    {
        var observations = new (string, string)[]
        {
            ("2026-09-01T00:00:00.000+02:00", "a"),
            ("2026-09-03T00:00:00.000+02:00", "c"),
            ("2026-09-02T00:00:00.000+02:00", "b"),
        };

        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(observations, out var refusal);

        Assert.IsNotNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.None, refusal);
        Assert.AreEqual("2026-09-03T00:00:00.000+02:00", result!.WatermarkLexical);
        Assert.AreEqual("c", result.CanonicalEntryKey);
    }

    [TestMethod]
    public void ATieAtTheMaximumWatermarkBreaksOnTheEntryKeyOrdinally()
    {
        // Same watermark, two different roots at exactly the boundary: the tie-safe tuple order
        // (watermark first, then entry key) must pick the ordinally larger key, never an arbitrary
        // or input-order-dependent one.
        var observations = new (string, string)[]
        {
            ("2026-09-03T00:00:00.000+02:00", "aaa"),
            ("2026-09-03T00:00:00.000+02:00", "zzz"),
            ("2026-09-01T00:00:00.000+02:00", "should-lose-on-watermark"),
        };

        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(observations, out _);

        Assert.AreEqual("2026-09-03T00:00:00.000+02:00", result!.WatermarkLexical);
        Assert.AreEqual("zzz", result.CanonicalEntryKey);
    }

    [TestMethod]
    public void ATieAtTheMaximumWatermarkKeepsTheLargerKeyEvenWhenItIsEncounteredFirst()
    {
        // ATieAtTheMaximumWatermarkBreaksOnTheEntryKeyOrdinally above always puts the larger key
        // second in iteration order, so it cannot tell a correct strict "cursor.CompareTo(maximum) >
        // 0" comparison apart from a naive "last one wins" implementation: both would return the same
        // (later, larger) key. Putting the larger key first and a smaller one after is the case that
        // tells them apart -- a naive last-wins implementation would return the smaller, later key,
        // while the real strict-greater-than comparison keeps the first (larger) one, because the
        // second cursor never compares greater than it.
        var observations = new (string, string)[]
        {
            ("2026-09-03T00:00:00.000+02:00", "zzz"),
            ("2026-09-03T00:00:00.000+02:00", "aaa"),
        };

        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(observations, out var refusal);

        Assert.IsNotNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.None, refusal);
        Assert.AreEqual("zzz", result!.CanonicalEntryKey);
    }

    [TestMethod]
    public void ASingleEntryCensusReturnsThatEntry()
    {
        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            new[] { ("2026-09-03T00:00:00.000+02:00", "only") }, out var refusal);
        Assert.IsNotNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.None, refusal);
        Assert.AreEqual("only", result!.CanonicalEntryKey);
    }

    [TestMethod]
    public void ABlankWatermarkRefusesAsAnInvalidCensusEntry()
    {
        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            new[] { ("", "a") }, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.InvalidCensusEntry, refusal);
    }

    [TestMethod]
    public void ABlankEntryKeyRefusesAsAnInvalidCensusEntry()
    {
        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            new[] { ("2026-09-03T00:00:00.000+02:00", "") }, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.InvalidCensusEntry, refusal);
    }

    [TestMethod]
    public void ADuplicateTupleRefuses()
    {
        var entry = ("2026-09-03T00:00:00.000+02:00", "a");
        var result = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            new[] { entry, entry }, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuFirstCutWatermarkBootstrapRefusal.DuplicateCensusEntry, refusal);
    }

    [TestMethod]
    public void TheResultFeedsDirectlyIntoTheWitnessPlanAsItsStartPosition()
    {
        // The whole point: this cursor is exactly what EuWatermarkWitnessPlan.TryFreeze's own doc
        // comment says it has nothing to supply for a first cut.
        var start = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            new[] { ("2026-09-03T00:00:00.000+02:00", "a") }, out _)!;

        var plan = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.MinimumPageLimit,
            start,
            out var planRefusal);

        Assert.IsNotNull(plan);
        Assert.AreEqual(EuWatermarkPlanRefusal.None, planRefusal);
        Assert.AreSame(start, plan!.StartPosition);
    }

    // No ConstructionSurface pin here: EuFirstCutWatermarkBootstrap is a static utility class with
    // no instances of its own (like EuScopeSnapshotReduction and EuScopeVocabulary), and the type
    // ConstructionSurface.Of guards is the type whose instances a caller could mint through some
    // door other than the one intended -- there is no such door here, since nothing ever holds an
    // instance of this type at all. What TryComputeStartPosition actually produces is a
    // EuWatermarkCursor, whose own construction surface is pinned where that type is declared.
}
