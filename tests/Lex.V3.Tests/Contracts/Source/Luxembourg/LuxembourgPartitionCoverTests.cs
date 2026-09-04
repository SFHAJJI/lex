using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The reconciliation of a chain's leaves into one coverage claim. Like the receipt it reconciles,
/// <see cref="LuxembourgPartitionCover"/> is dialect-agnostic, so these tests build its leaf and
/// root receipts from the same EU-dialect <see cref="RepeatedEnumerationDeliveryProofTests.Fixture"/>
/// used by <c>RepeatedEnumerationDeliveryReceiptTests</c>.
/// </summary>
[TestClass]
public sealed class LuxembourgPartitionCoverTests
{
    [TestMethod]
    public void ACoverWithAMissingLeafProofIsNotACover()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("a,b", "a,b"));

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafReceiptMissing, refusal);
    }

    [TestMethod]
    public void ANullEntryInTheLeafReceiptListIsAlsoAMissingProof()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("a,b", "a,b"));

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left, null!], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafReceiptMissing, refusal);
    }

    [TestMethod]
    public void ALeafProofPairedWithTheWrongRangeIsRefused()
    {
        var chain = TwoLeafChain();
        // Both receipts are individually valid leaf proofs; "left"'s proof is handed in as if it
        // covered "right". This is the only joint between the chain (range identity) and the
        // proofs (partition key), so nothing else in the reconciliation can catch it.
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("a,b", "a,b"));
        var alsoClaimsLeft = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("c,d", "c,d"));

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left, alsoClaimsLeft], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafPartitionKeyMismatch, refusal);
    }

    [TestMethod]
    public void ARootProofPairedWithTheWrongRangeIsRefused()
    {
        // TryCreate has two LeafPartitionKeyMismatch call sites: one per leaf (driven above by
        // ALeafProofPairedWithTheWrongRangeIsRefused, which always supplies rootReceipt: null) and
        // one for the root. Before this test, only the leaf site was driven; a mutation deleting
        // the root's own `rootReceipt.Delivery.PartitionKey != chain.RootRange.PartitionId` check
        // survived every test in this file, because every root receipt any of them supplied
        // already carried the chain's real root key ("root").
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left", expectedCount: 2).Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right", expectedCount: 3).Create("a,b,c", "a,b,c"));
        // A structurally valid root proof, correct in every way except that it claims a partition
        // key the chain's root range does not have ("root" is TwoLeafChain's actual root key).
        var wrongRangeRoot = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "not-root", expectedCount: 5).Create("a,b,c,d,e", "a,b,c,d,e"));

        var cover = LuxembourgPartitionCover.TryCreate(chain, [left, right], wrongRangeRoot, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafPartitionKeyMismatch, refusal);
    }

    [TestMethod]
    public void TwoPassesThatDisagreeCannotJoinACover()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("a,b", "a,b"));
        // Pass A serves three rows, pass B serves two, against one shared declared count of two:
        // deliveredA (3) disagrees with selectedA (2), so Core's own outcome classification is
        // DifferentSelections before this guard is ever reached.
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "right")
            .Create("a,b,c", "a,b"));
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, right.Delivery.Outcome);

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left, right], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafSelectionsDiffer, refusal);
    }

    [TestMethod]
    public void ALeafAtThePublisherCeilingIsNotALeaf()
    {
        var chain = TwoLeafChain();
        // Both leaves share one interpretation profile (maximumDeliverableRows: 2), so a mutation
        // that drops the threshold conjunct cannot be masked by LeafProfileDiffers firing instead:
        // "left" is safely below that shared ceiling at a selected count of 1.
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left",
            maximumDeliverableRows: 2,
            expectedCount: 1).Create("a", "a"));
        // A unit-test-scaled stand-in for the real 1,000,000-row publisher ceiling: what matters to
        // this guard is only that ThresholdAssessment lands on PartitionRequired while the two
        // passes still agree exactly, not the literal ceiling value (which is exercised against the
        // real LuxembourgQueryPlan elsewhere). maximumDeliverableRows: 2 with a selected count of 2
        // gives exactly that: AssessThreshold(2, max: 2) is PartitionRequired by the closed "count <
        // maximum" rule.
        var saturated = new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right",
            maximumDeliverableRows: 2,
            expectedCount: 2).Create("a,b", "a,b");
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, saturated.Outcome);
        Assert.AreEqual(RepeatedEnumerationThresholdAssessment.PartitionRequired, saturated.ThresholdAssessment);
        var right = Receipt(saturated);

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left, right], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafPartitionRequired, refusal);
    }

    [TestMethod]
    public void LeavesFromTwoRunsAreNotOneObservation()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right",
            runIdentitySeed: 950).Create("a,b", "a,b"));
        Assert.AreNotEqual(left.Delivery.RunIdentity, right.Delivery.RunIdentity);

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left, right], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafRunIdentityDiffers, refusal);
    }

    [TestMethod]
    public void LeavesUnderTwoInterpretationsAreNotOneCover()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left",
            maximumDeliverableRows: 100).Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right",
            maximumDeliverableRows: 200).Create("a,b", "a,b"));
        Assert.AreNotEqual(left.Delivery.InterpretationProfileRef, right.Delivery.InterpretationProfileRef);
        // Isolate the profile difference: same run for both, so LeafRunIdentityDiffers cannot fire.
        Assert.AreEqual(left.Delivery.RunIdentity, right.Delivery.RunIdentity);

        var cover = LuxembourgPartitionCover.TryCreate(
            chain, [left, right], rootReceipt: null, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.LeafProfileDiffers, refusal);
    }

    [TestMethod]
    public void TheLeavesMustAddUpToTheRootTheyDivide()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left", expectedCount: 2).Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right", expectedCount: 3).Create("a,b,c", "a,b,c"));
        // The root's declared (selected) count is 5, matching the leaf sum, but pass A only
        // actually delivered 4 rows. If the reconciliation compared SelectedRowCountA instead of
        // DeliveredRowCountA it would wrongly accept this root; it must use what was actually
        // delivered.
        var root = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "root", expectedCount: 5).Create("a,b,c,d", "a,b,c,d,e"));
        Assert.AreEqual(5, root.Delivery.SelectedRowCountA);
        Assert.AreEqual(4, root.Delivery.DeliveredRowCountA);

        var cover = LuxembourgPartitionCover.TryCreate(chain, [left, right], root, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.RootCountDoesNotEqualTheLeafSum, refusal);
    }

    [TestMethod]
    public void APerfectTilingWithAWrongSumIsStillRefused()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left", expectedCount: 2).Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right", expectedCount: 3).Create("a,b,c", "a,b,c"));
        // Every structural condition this reconciliation checks is satisfied: both leaves agree,
        // neither is saturated, one run, one profile, the chain tiles the root by construction.
        // The root's own two passes agree with each other (EqualSelections) at 6, which is simply
        // not 2 + 3. Only the arithmetic guard can catch this.
        var root = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "root", expectedCount: 6).Create("a,b,c,d,e,f", "a,b,c,d,e,f"));
        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, root.Delivery.Outcome);
        Assert.AreEqual(6, root.Delivery.DeliveredRowCountA);

        var cover = LuxembourgPartitionCover.TryCreate(chain, [left, right], root, out var refusal);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverRefusal.RootCountDoesNotEqualTheLeafSum, refusal);
    }

    [TestMethod]
    public void AMatchingRootProducesARootCountVerifiedCover()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left", expectedCount: 2).Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right", expectedCount: 3).Create("a,b,c", "a,b,c"));
        var root = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "root", expectedCount: 5).Create("a,b,c,d,e", "a,b,c,d,e"));

        var cover = LuxembourgPartitionCover.TryCreate(chain, [left, right], root, out var refusal);

        Assert.AreEqual(LuxembourgPartitionCoverRefusal.None, refusal);
        Assert.IsNotNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverBasis.RootCountVerified, cover.Basis);
        Assert.AreEqual(5, cover.LeafDeliveredRowCountSum);
        Assert.AreEqual(CustodyMembership.Floored, cover.RetainedFloor);
        Assert.AreSame(chain, cover.Chain);
        CollectionAssert.AreEqual(new[] { left, right }, cover.LeafReceipts.ToArray());
    }

    [TestMethod]
    public void AbsentRootLeavesTheCoverAsLeafTilingOnly()
    {
        var chain = TwoLeafChain();
        var left = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "left", expectedCount: 2).Create("a,b", "a,b"));
        var right = Receipt(new RepeatedEnumerationDeliveryProofTests.Fixture(
            partitionKey: "right", expectedCount: 3).Create("a,b,c", "a,b,c"));

        var cover = LuxembourgPartitionCover.TryCreate(chain, [left, right], rootReceipt: null, out var refusal);

        Assert.AreEqual(LuxembourgPartitionCoverRefusal.None, refusal);
        Assert.IsNotNull(cover);
        Assert.AreEqual(LuxembourgPartitionCoverBasis.LeafTilingOnly, cover.Basis);
        Assert.AreEqual(5, cover.LeafDeliveredRowCountSum);
    }

    [TestMethod]
    public void TheRetainedFloorIsTheWeakestAcrossLeaves()
    {
        var chain = TwoLeafChain();
        var leftDelivery = new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "left")
            .Create("a,b", "a,b");
        var rightDelivery = new RepeatedEnumerationDeliveryProofTests.Fixture(partitionKey: "right")
            .Create("c,d", "c,d");
        var (leftSession, leftExecutor) = RepeatedEnumerationDeliveryReceiptTests.FullMembership(
            leftDelivery, CustodyMembership.Floored);
        var (rightSession, rightExecutor) = RepeatedEnumerationDeliveryReceiptTests.FullMembership(
            rightDelivery, CustodyMembership.RetainedUnenforced);
        var left = RepeatedEnumerationDeliveryReceipt.TryCreate(
            leftDelivery, leftSession, leftExecutor,
            RepeatedEnumerationDeliveryReceiptTests.Custody(leftDelivery), out _)!;
        var right = RepeatedEnumerationDeliveryReceipt.TryCreate(
            rightDelivery, rightSession, rightExecutor,
            Unfloored(RepeatedEnumerationDeliveryReceiptTests.Custody(rightDelivery)), out _)!;
        Assert.AreEqual(CustodyMembership.Floored, left.RetainedFloor);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, right.RetainedFloor);

        var cover = LuxembourgPartitionCover.TryCreate(chain, [left, right], rootReceipt: null, out var refusal);

        Assert.AreEqual(LuxembourgPartitionCoverRefusal.None, refusal);
        Assert.IsNotNull(cover);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, cover.RetainedFloor);
    }

    /// <summary>The custody list restated at the weaker membership, to match an unfloored run.</summary>
    private static List<RepeatedEnumerationObservationCustody> Unfloored(List<RepeatedEnumerationObservationCustody> custody) =>
        custody
            .Select(static entry => entry with
            {
                ResponseBodyMembership = CustodyMembership.RetainedUnenforced,
            })
            .ToList();

    private static LuxembourgPartitionChain TwoLeafChain() =>
        LuxembourgPartitionChain.Root(Range("root", "a", "z"))
            .SplitLeaf("root", Cursor("m"), "left", "right");

    private static RepeatedEnumerationDeliveryReceipt Receipt(EnumerationDeliveryComparison delivery)
    {
        var (session, executor) = RepeatedEnumerationDeliveryReceiptTests.FullMembership(
            delivery, CustodyMembership.Floored);
        return RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor,
            RepeatedEnumerationDeliveryReceiptTests.Custody(delivery), out var refusal)
            ?? throw new AssertFailedException($"The fixture's own receipt was refused: {refusal}.");
    }

    private static LuxembourgQueryPartitionRange Range(string id, string start, string end) =>
        new(id, Cursor(start), Cursor(end));

    private static LuxembourgQueryCursor Cursor(string key1) => new(key1, "", "", "", "", "");
}
