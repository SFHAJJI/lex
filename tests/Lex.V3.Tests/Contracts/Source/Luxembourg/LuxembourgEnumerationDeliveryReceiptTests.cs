using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// <see cref="LuxembourgEnumerationDeliveryReceipt"/> is dialect-agnostic: it walks the refs a
/// <see cref="EnumerationDeliveryComparison"/> already exposes and says nothing about SPARQL
/// shape. So these tests build the comparison with the existing, exhaustively-exercised
/// <see cref="RepeatedEnumerationDeliveryProofTests.Fixture"/> rather than hand-rolling a second
/// evidence-resolution harness for Luxembourg alone.
/// </summary>
[TestClass]
public sealed class LuxembourgEnumerationDeliveryReceiptTests
{
    [TestMethod]
    public void EveryDigestHeldProducesAFlooredReceiptRestatingNothing()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, [], out var refusal);

        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreSame(delivery, receipt.Delivery);
        Assert.AreEqual(CustodyMembership.Floored, receipt.RetainedFloor);
        Assert.AreEqual(0, receipt.UnenforcedMemberDigests.Count);
        Assert.AreSame(delivery, receipt.RequireFlooredRun());
        foreach (var observation in AllObservations(delivery))
        {
            Assert.AreEqual(
                CustodyMembership.Floored,
                receipt.RetainedMembership[observation.QueryPlanRef.Sha256]);
            Assert.AreEqual(
                CustodyMembership.Floored,
                receipt.RetainedMembership[observation.QueryInputRef.Sha256]);
            Assert.AreEqual(
                CustodyMembership.Floored,
                receipt.RetainedMembership[observation.RenderReceiptRef.Sha256]);
            Assert.AreEqual(
                CustodyMembership.Floored,
                receipt.RetainedMembership[observation.LogicalRequestRef.Sha256]);
            Assert.AreEqual(
                CustodyMembership.Floored,
                receipt.RetainedMembership[observation.HttpEvidenceRef.Sha256]);
        }
    }

    [TestMethod]
    public void AMembershipMapFromAnotherRunIsNotThisRunsCustody()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery,
            new Dictionary<string, CustodyMembership>(StringComparer.Ordinal),
            new Dictionary<string, CustodyMembership>(StringComparer.Ordinal),
            [],
            out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    [DataRow(0, DisplayName = "count A")]
    [DataRow(1, DisplayName = "first page of A")]
    [DataRow(2, DisplayName = "count B")]
    [DataRow(3, DisplayName = "first page of B")]
    public void EveryObservationIsConsulted(int observationIndex)
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var missing = AllObservations(delivery)[observationIndex];
        Assert.IsTrue(session.Remove(missing.QueryPlanRef.Sha256));

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, [], out var refusal);

        Assert.IsNull(receipt, $"observation {observationIndex} was not actually consulted");
        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    public void AMissingHttpEvidenceDigestIsRefusedToo()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        Assert.IsTrue(executor.Remove(delivery.CountA.HttpEvidenceRef.Sha256));

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, [], out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    public void OneObjectCannotBeHeldUnderTwoFloors()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);

        // The same digest, present in both maps, disagreeing about its own floor.
        var contested = delivery.CountA.QueryPlanRef.Sha256;
        executor[contested] = CustodyMembership.RetainedUnenforced;

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, [], out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest, refusal);
    }

    [TestMethod]
    public void RequireFlooredRunNamesEveryUnenforcedDigest()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var unenforcedDigest = delivery.PagesB.Pages[0].Evidence.LogicalRequestRef.Sha256;
        session[unenforcedDigest] = CustodyMembership.RetainedUnenforced;

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, [], out var refusal);

        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, receipt.RetainedFloor);
        CollectionAssert.Contains(receipt.UnenforcedMemberDigests.ToArray(), unenforcedDigest);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => receipt.RequireFlooredRun());
        StringAssert.Contains(thrown.Message, unenforcedDigest);
    }

    [TestMethod]
    public void AReadOnlyDigestDoesNotLowerTheFloor()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var readOnlyDigests = new[] { new string('7', 64), new string('8', 64) };

        var receipt = LuxembourgEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, readOnlyDigests, out var refusal);

        Assert.AreEqual(LuxembourgEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(
            CustodyMembership.Floored,
            receipt.RetainedFloor,
            "digests reported only as read-only must never fold into the retained floor");
        CollectionAssert.AreEqual(readOnlyDigests, receipt.VerifiedWithoutCustodyClaim.ToArray());
        Assert.AreEqual(0, receipt.UnenforcedMemberDigests.Count);
    }

    // internal rather than private: LuxembourgPartitionCoverTests builds its leaf receipts the
    // same way, and a second copy of this plumbing is a second place for it to drift.
    internal static (
        Dictionary<string, CustodyMembership> Session,
        Dictionary<string, CustodyMembership> Executor) FullMembership(
        EnumerationDeliveryComparison delivery,
        CustodyMembership membership)
    {
        var session = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        var executor = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        foreach (var observation in AllObservations(delivery))
        {
            session[observation.QueryPlanRef.Sha256] = membership;
            session[observation.QueryInputRef.Sha256] = membership;
            session[observation.RenderReceiptRef.Sha256] = membership;
            session[observation.LogicalRequestRef.Sha256] = membership;
            executor[observation.HttpEvidenceRef.Sha256] = membership;
        }

        return (session, executor);
    }

    internal static RepeatedEnumerationEvidenceRefs[] AllObservations(EnumerationDeliveryComparison delivery) =>
        new[] { delivery.CountA }
            .Concat(delivery.PagesA.Pages.Select(static page => page.Evidence))
            .Append(delivery.CountB)
            .Concat(delivery.PagesB.Pages.Select(static page => page.Evidence))
            .ToArray();
}
