using System;
using System.Collections.Generic;
using System.Linq;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The four guards batching the witness introduced, one test each, so a mutation to any one of
/// them reddens THAT guard's own assertion rather than a neighbour's.
/// </summary>
/// <remarks>
/// Batching the witness is what makes the run see a consolidated state's own watermark instead of
/// only its root's, and the pack has more objects than one page's VALUES block can carry. Each
/// guard below exists because batching created a way to be wrong that did not exist when the
/// traversal was unbounded.
/// </remarks>
[TestClass]
public sealed class EuWatermarkWitnessBatchGuardTests
{
    private const string Boundary = "2026-09-03T12:10:17.601+02:00";
    private const string KeyA = "http://publications.europa.eu/resource/cellar/aaaaaaaa";

    /// <summary>
    /// GUARD ONE: the batch's identity is IN the partition key, so two batches sitting at one
    /// watermark cannot collide on one key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the silent one. The key used to derive from the boundary POSITION alone, and the
    /// pack needs more than one batch, so two batches reading from the same boundary would have
    /// minted the SAME partition key while carrying different objects. It compounds with the tie:
    /// a tie group sharing one watermark can be SPLIT ACROSS TWO BATCHES, and colliding crossings
    /// would make the crossing proof compare the wrong pages against each other and still report
    /// success.
    /// </para>
    /// <para>
    /// The assertion is on the BOUND INPUT'S partition key rather than on a digest field, because
    /// the key is what the delivery machinery actually partitions on; asserting the field would
    /// test that a property exists rather than that it reaches the thing that uses it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TwoBatchesAtOneWatermarkDoNotShareAPartitionKey()
    {
        var first = Freeze([PackObject(0)]);
        var second = Freeze([PackObject(1)]);
        var position = At(Boundary, KeyA);

        var firstKey = PartitionKeyOf(first, position);
        var secondKey = PartitionKeyOf(second, position);

        Assert.AreNotEqual(
            firstKey,
            secondKey,
            "two batches reading from the SAME boundary minted the SAME partition key. The pack "
                + "needs more than one batch, so this is reachable on every real run, and a "
                + "collision here makes the boundary crossing compare pages from different "
                + "batches while reporting success.");
    }

    /// <summary>
    /// GUARD TWO: the tie is evaluated WITHIN a batch, so one batch's boundary group is re-read
    /// and accounted against that batch and not against a sibling's.
    /// </summary>
    /// <remarks>
    /// The ordering tuple and the inclusive boundary rule are unchanged and sound: the second term
    /// is the canonical entry key, which is a unique cellar IRI, so the order is total. What
    /// batching adds is that the SAME watermark now appears in several batches, and the boundary
    /// re-read must therefore be a property of a batch. That is exactly what carrying the batch
    /// identity into the key achieves, which is why this asserts the two batches' pages at one
    /// boundary are distinguishable rather than asserting the comparison itself again.
    /// </remarks>
    [TestMethod]
    public void OneWatermarkInTwoBatchesYieldsTwoDistinguishableBoundaryReads()
    {
        // MULTI-MEMBER BATCHES ON PURPOSE. A one-member batch cannot express this defect at all:
        // with a single object, binding each slot to its own member and binding every slot to the
        // first member produce the SAME text, so a mutation collapsing the batch survives. That
        // is exactly what happened when this test was first written with one object each.
        var first = Freeze([PackObject(0), PackObject(1)]);
        var second = Freeze([PackObject(2), PackObject(3)]);
        var position = At(Boundary, KeyA);

        // Same boundary, same ordering tuple, same boundary rule: only the batch differs.
        Assert.AreEqual(first.StartPosition.WatermarkLexical, second.StartPosition.WatermarkLexical);
        var firstBody = RenderedBodyOf(first, position);
        var secondBody = RenderedBodyOf(second, position);

        Assert.AreNotEqual(
            firstBody,
            secondBody,
            "two batches re-reading the same boundary sent IDENTICAL query text, so the tie group "
                + "at that watermark would be read once and accounted against whichever batch "
                + "happened to be compared. The re-read has to be per batch.");

        // EVERY member of a batch has to reach the page, or the boundary group is re-read over a
        // narrower set than the batch claims and the accounting is against objects never asked
        // about.
        StringAssert.Contains(
            firstBody,
            PackObject(0),
            "the first batch's first member is missing from its own page.");
        StringAssert.Contains(
            firstBody,
            PackObject(1),
            "the first batch's SECOND member is missing from its own page, so the batch was "
                + "collapsed to one object while still reporting a traversal over two.");
        StringAssert.Contains(secondBody, PackObject(2));
        StringAssert.Contains(
            secondBody,
            PackObject(3),
            "the second batch's SECOND member is missing from its own page.");
    }

    /// <summary>
    /// GUARD THREE: a batch larger than the capacity is REFUSED, because it cannot be bound at all.
    /// </summary>
    /// <remarks>
    /// Every member travels as its own parameter and the plan's parameter ceiling is fixed, so an
    /// oversized batch is not merely discouraged: there is no query it could render into. Refusing
    /// at the freeze is what stops that becoming a partial send.
    /// </remarks>
    [TestMethod]
    public void ABatchLargerThanTheCapacityIsRefusedRatherThanTruncated()
    {
        var oversized = Enumerable
            .Range(0, EuWatermarkWitnessPlan.BatchCapacity + 1)
            .Select(PackObject)
            .ToArray();

        var plan = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            At(Boundary, KeyA),
            oversized,
            out var refusal);

        Assert.IsNull(plan, "an unbindable batch must not produce a plan.");
        Assert.AreEqual(
            EuWatermarkPlanRefusal.BatchAboveCapacity,
            refusal,
            "a batch of " + oversized.Length + " against a capacity of "
                + EuWatermarkWitnessPlan.BatchCapacity + " must refuse BY NAME rather than being "
                + "silently truncated, which would send a witness over fewer objects than the "
                + "caller asked about while reporting a clean traversal.");
    }

    /// <summary>
    /// GUARD FOUR: a batch member that is not a canonical pack object, or that repeats one, is
    /// REFUSED.
    /// </summary>
    /// <remarks>
    /// A duplicate would deliver one object's rows twice into a count the terminal equation
    /// checks, and a non-canonical member would make the batch digest, and therefore the partition
    /// key, depend on spelling rather than on identity.
    /// </remarks>
    [TestMethod]
    public void ABatchMemberThatIsNotCanonicalOrThatRepeatsIsRefused()
    {
        var duplicated = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            At(Boundary, KeyA),
            [PackObject(0), PackObject(0)],
            out var duplicateRefusal);

        Assert.IsNull(duplicated);
        Assert.AreEqual(
            EuWatermarkPlanRefusal.BatchMemberNotCanonicalOrDuplicated,
            duplicateRefusal,
            "a repeated member would deliver one object's rows twice into the count the terminal "
                + "equation checks.");

        var notAnIri = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            At(Boundary, KeyA),
            ["not-a-cellar-object"],
            out var shapeRefusal);

        Assert.IsNull(notAnIri);
        Assert.AreEqual(
            EuWatermarkPlanRefusal.BatchMemberNotCanonicalOrDuplicated,
            shapeRefusal,
            "a member that does not reduce to a canonical pack object would make the batch digest "
                + "depend on spelling rather than on identity.");
    }

    private static string PackObject(int ordinal) =>
        "http://publications.europa.eu/resource/cellar/"
        + ordinal.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)
        + "-0000-4000-8000-000000000000";

    private static EuWatermarkWitnessPlan Freeze(IReadOnlyList<string> batch) =>
        EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            At(Boundary, KeyA),
            batch,
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture batch refused as {refusal}");

    private static string PartitionKeyOf(EuWatermarkWitnessPlan plan, EuWatermarkCursor position) =>
        Bind(plan, position).InputArtifact.PartitionBinding.MemberKey;

    private static string RenderedBodyOf(EuWatermarkWitnessPlan plan, EuWatermarkCursor position) =>
        System.Text.Encoding.UTF8.GetString(Bind(plan, position).Request.CopyRequestBody().ToArray());

    private static EuWatermarkWitnessBoundQuery Bind(
        EuWatermarkWitnessPlan plan, EuWatermarkCursor position) =>
        plan.TryBindPage(
            position,
            "urn:uuid:11111111-1111-4111-8111-111111111111",
            "urn:uuid:22222222-2222-4222-8222-222222222222",
            MachineQueryRendererSource.Open(
                new SourceArtifactRef(
                    "urn:uuid:33333333-3333-4333-8333-333333333333",
                    System.Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes("witness-batch-guard-renderer")))),
                System.Text.Encoding.UTF8.GetBytes("witness-batch-guard-renderer")),
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture bind refused as {refusal}");

    private static EuWatermarkCursor At(string watermarkLexical, string canonicalEntryKey) =>
        EuWatermarkCursor.TryOpen(watermarkLexical, canonicalEntryKey, out var refusal)
        ?? throw new InvalidOperationException($"the fixture cursor refused as {refusal}");
}
