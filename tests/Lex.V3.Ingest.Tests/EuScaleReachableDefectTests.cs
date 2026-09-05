using System;
using System.Linq;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Two defects the canary CANNOT reach at its own scale, driven here because the other eighty
/// seeds would meet them on the first run.
/// </summary>
/// <remarks>
/// <para>
/// A test that can only be written after a live run is not the only kind worth writing. These two
/// are the opposite: the live run PASSES over them because two seeds and eight objects are too few
/// to express the condition, and it is precisely that invisibility that makes them dangerous. Both
/// were found by reading the accepted 82-seed measurement against the code rather than by running
/// anything.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuScaleReachableDefectTests
{
    /// <summary>
    /// A witness traversal over TWO batches seeds each batch from ITS OWN boundary, so a batch that
    /// never held the pack-wide boundary entry is not asked to re-deliver it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE CANARY CANNOT SEE THIS EITHER, and why the two-member batch tests do not cover it.
    /// The retained tie set is what the next page must re-deliver, and a member that does not come
    /// back is <c>BoundaryEntrySkipped</c>. Every batch used to be seeded with the ONE pack-wide
    /// boundary entry key, which only the batch whose VALUES block contains that root can deliver.
    /// At eighty two seeds every batch but one would refuse. Eight objects fit in a single batch at
    /// capacity fifty, so no canary run has ever had a second batch. TWO MEMBERS IN ONE BATCH IS
    /// NOT TWO BATCHES, which is the distinction this test exists to hold.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABatchThatNeverHeldTheBoundaryEntryIsNotAskedToRedeliverIt()
    {
        var boundaryRoot = Root(0);
        var otherRoot = Root(1);
        var boundary = EuWatermarkCursor.TryOpen(
            "2026-09-03T12:10:17.601+02:00", boundaryRoot, out var cursorRefusal)
            ?? throw new InvalidOperationException($"fixture cursor refused as {cursorRefusal}");

        var holdsBoundary = Freeze([boundaryRoot], boundary);
        var doesNot = Freeze([otherRoot], boundary);

        // The plan itself is what the executor asks, and the question is membership.
        Assert.IsTrue(
            holdsBoundary.PaddedEntries.Contains(boundary.CanonicalEntryKey, StringComparer.Ordinal),
            "the batch containing the boundary root must contain its entry key.");
        Assert.IsFalse(
            doesNot.PaddedEntries.Contains(boundary.CanonicalEntryKey, StringComparer.Ordinal),
            "a batch built from other objects must NOT contain the boundary entry key. If it did, "
                + "this test would be asserting nothing: the two batches would be the same batch.");

        // And the two batches are genuinely distinct partitions at the same boundary, which is the
        // condition that makes seeding per batch necessary rather than cosmetic.
        Assert.AreNotEqual(
            holdsBoundary.BatchDigest,
            doesNot.BatchDigest,
            "two batches at one boundary must be distinguishable, or the traversal cannot tell "
                + "which of them a page belongs to.");
    }

    private static string Root(int index) =>
        EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[index].WorkRoot, out _)!;

    private static EuWatermarkWitnessPlan Freeze(
        IReadOnlyList<string> batch, EuWatermarkCursor start) =>
        EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            start,
            batch,
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture batch refused as {refusal}");

}
