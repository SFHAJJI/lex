using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// <see cref="RepeatedEnumerationDeliveryReceipt"/> is dialect-agnostic: it walks the refs a
/// <see cref="EnumerationDeliveryComparison"/> already exposes and says nothing about SPARQL
/// shape. So these tests build the comparison with the existing, exhaustively-exercised
/// <see cref="RepeatedEnumerationDeliveryProofTests.Fixture"/> rather than hand-rolling a second
/// evidence-resolution harness for Luxembourg alone.
/// </summary>
[TestClass]
public sealed class RepeatedEnumerationDeliveryReceiptTests
{
    [TestMethod]
    public void EveryDigestHeldProducesAFlooredReceiptRestatingNothing()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, Custody(delivery), out var refusal);

        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreSame(delivery, receipt.Delivery);
        Assert.AreEqual(CustodyMembership.Floored, receipt.RetainedFloor);
        Assert.AreEqual(0, receipt.UnenforcedMemberDigests.Count);
        Assert.AreSame(delivery, receipt.Delivery);
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

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery,
            new Dictionary<string, CustodyMembership>(StringComparer.Ordinal),
            new Dictionary<string, CustodyMembership>(StringComparer.Ordinal),
            Custody(delivery),
            out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
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

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, Custody(delivery), out var refusal);

        Assert.IsNull(receipt, $"observation {observationIndex} was not actually consulted");
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    public void AMissingHttpEvidenceDigestIsRefusedToo()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        Assert.IsTrue(executor.Remove(delivery.CountA.HttpEvidenceRef.Sha256));

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, Custody(delivery), out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    public void OneObjectCannotBeHeldUnderTwoFloors()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);

        // The same digest, present in both maps, disagreeing about its own floor.
        var contested = delivery.CountA.QueryPlanRef.Sha256;
        executor[contested] = CustodyMembership.RetainedUnenforced;

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, Custody(delivery), out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.MembershipDisagreesOnADigest, refusal);
    }

    [TestMethod]
    public void TheReceiptNamesEveryUnenforcedDigestAndStillMintsAProofCarryingThatClass()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var unenforcedDigest = delivery.PagesB.Pages[0].Evidence.LogicalRequestRef.Sha256;
        session[unenforcedDigest] = CustodyMembership.RetainedUnenforced;

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, Custody(delivery), out var refusal);

        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, receipt.RetainedFloor);
        CollectionAssert.Contains(receipt.UnenforcedMemberDigests.ToArray(), unenforcedDigest);

        // RULING lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6. The run says WHICH of the
        // three this member is and carries on. There used to be a RequireFlooredRun accessor here
        // that threw naming this digest; it lost its last caller with that ruling and was removed
        // rather than left sitting unreferenced. The class travels on the proof instead.
        var proof = receipt.TryProveFamilyEnumeration(delivery.PartitionKey, out var proofRefusal);
        Assert.IsNotNull(proof, $"an unfloored run still proves its enumeration: {proofRefusal}");
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, proof.RetainedFloor);
    }

    [TestMethod]
    public void AnUnflooredResponseBodyLowersTheFloorAndSoTheProofsClass()
    {
        // Objection 4. The response body and its durable write receipt used to be reported as
        // "verified without a custody claim", outside RetainedFloor, so the floor could read as
        // Floored while the publisher's own answer sat unfloored. They are members now.
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        var unflooredBody = custody[0].ResponseBodySha256;
        custody[0] = custody[0] with { ResponseBodyMembership = CustodyMembership.RetainedUnenforced };

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out var refusal);

        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, receipt.RetainedFloor);
        CollectionAssert.Contains(receipt.UnenforcedMemberDigests.ToArray(), unflooredBody);
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            receipt.TryProveFamilyEnumeration(delivery.PartitionKey, out _)!.RetainedFloor,
            "the publisher's own answer sitting unfloored must reach the proof's own class.");
    }

    [TestMethod]
    public void AnUnflooredDurableWriteReceiptLowersTheFloorToo()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        var writeReceiptDigest = custody[0].DurableWriteReceiptSha256;
        session[writeReceiptDigest] = CustodyMembership.RetainedUnenforced;

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out var refusal);

        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.None, refusal);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, receipt.RetainedFloor);
        CollectionAssert.Contains(receipt.UnenforcedMemberDigests.ToArray(), writeReceiptDigest);
    }

    [TestMethod]
    public void AnObservationWhoseBodyIsNotStatedIsRefusedRatherThanLeftOffTheFloor()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        custody.RemoveAt(0);

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    public void CustodyFromAnotherObservationCannotStandInForThisOne()
    {
        // Same evidence digest, different reference tuple: the receipt must not accept it, or a
        // caller could pair one run's comparison with another run's bodies.
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        custody[0] = custody[0] with
        {
            References = custody[0].References with { QueryPlanRef = custody[1].References.QueryPlanRef },
        };

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld, refusal);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "on a body")]
    [DataRow(false, DisplayName = "in the session map")]
    public void AMembershipNoWriteReceiptCanProduceIsRefusedRatherThanFolded(bool onTheBody)
    {
        // CustodyMembership.ReadOnce is what CustodyMembershipClassifier never answers. It used to
        // be a silent case in this file's floor switch, reachable by nobody and killable by no
        // test. It is a refusal now, which is a branch a caller can actually reach.
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        if (onTheBody)
        {
            custody[0] = custody[0] with { ResponseBodyMembership = CustodyMembership.ReadOnce };
        }
        else
        {
            session[delivery.CountA.QueryPlanRef.Sha256] = CustodyMembership.ReadOnce;
        }

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.MembershipIsNotReceiptDerived, refusal);
    }

    /// <summary>
    /// Every receipt mints a proof, and the proof CARRIES the run's custody class. Where that class
    /// is required is <see cref="AbsenceCut.TryCreateComplete"/>, the release, not here.
    /// </summary>
    /// <remarks>
    /// RULING lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6 reversed this test's own
    /// former rule. It used to assert that a floored receipt is the ONLY kind that mints a proof,
    /// and it named the mutation "change TryProveFamilyEnumeration to read Delivery instead of
    /// RequireFlooredRun" as the one it killed. That is now the intended behaviour: refusing the
    /// proof refuses the family, and an adapter refuses a run with an unproven family, so the old
    /// rule meant a store publishing no enforcement could acquire nothing at all. The load-bearing
    /// assertion moved with the rule, to
    /// <c>AbsenceCutTests.ACompleteCutRefusesAProofHeldWithoutAnEnforcedFloor</c>.
    /// </remarks>
    [TestMethod]
    public void EveryReceiptMintsAProofAndTheProofCarriesTheRunsCustodyClass()
    {
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var familyKey = delivery.PartitionKey;

        var (floored, flooredExecutor) = FullMembership(delivery, CustodyMembership.Floored);
        var flooredReceipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, floored, flooredExecutor, Custody(delivery), out _)!;
        Assert.AreEqual(CustodyMembership.Floored, flooredReceipt.RetainedFloor);
        var proof = flooredReceipt.TryProveFamilyEnumeration(familyKey, out var proofRefusal);
        Assert.IsNotNull(proof, $"a floored run must be able to prove its enumeration: {proofRefusal}");
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.None, proofRefusal);
        Assert.AreEqual(familyKey, proof.FamilyKey);

        // The same delivery, the same comparison, one member held without an enforced floor.
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        var unflooredBody = custody[0].ResponseBodySha256;
        custody[0] = custody[0] with { ResponseBodyMembership = CustodyMembership.RetainedUnenforced };
        var unflooredReceipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out _)!;
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, unflooredReceipt.RetainedFloor);

        var unflooredProof = unflooredReceipt.TryProveFamilyEnumeration(familyKey, out var unflooredRefusal);
        Assert.IsNotNull(unflooredProof, $"the enumeration is still proven: {unflooredRefusal}");
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.None, unflooredRefusal);
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            unflooredProof.RetainedFloor,
            "the proof must say which of the three its run was, not say durable.");
        Assert.AreEqual(
            CustodyMembership.Floored,
            proof.RetainedFloor,
            "and a floored run must still say Floored, or the stamp says nothing.");
        _ = unflooredBody;
    }

    [TestMethod]
    public void AProofIsRefusedForAFamilyThisDeliveryDidNotEnumerate()
    {
        // The bridge passes the caller's family key straight to the Absence factory rather than
        // reading it off the delivery, so this is the assertion that the factory's own
        // partition check is reachable through it.
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, Custody(delivery), out _)!;

        Assert.IsNull(receipt.TryProveFamilyEnumeration("some_other_family", out var refusal));
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.PartitionIsNotThisFamily, refusal);
    }

    [TestMethod]
    public void TwoObservationsCannotDisagreeAboutOneResponseBodysCustody()
    {
        // Found by a surviving mutation, and a defect rather than only a test gap. Two
        // observations really can name ONE body digest: a publisher that answers both passes
        // identically sends byte-identical bodies, and an empty terminal page is the everyday
        // case. The response-body path checked the two input maps and not what it had already
        // recorded, so the second observation silently overwrote the first and a run holding one
        // object under two different floors issued a receipt anyway, against this type's own
        // stated rule that a membership disagreement on one digest refuses.
        //
        // Every member goes through the same Record call now, so there is no kind of member for
        // which the rule quietly does not hold.
        var delivery = new RepeatedEnumerationDeliveryProofTests.Fixture().Create("a,b", "a,b");
        var (session, executor) = FullMembership(delivery, CustodyMembership.Floored);
        var custody = Custody(delivery);
        var shared = custody[0].ResponseBodySha256;

        // The same body, twice, under two different floors.
        custody[1] = custody[1] with
        {
            ResponseBodySha256 = shared,
            ResponseBodyMembership = CustodyMembership.RetainedUnenforced,
        };

        var receipt = RepeatedEnumerationDeliveryReceipt.TryCreate(
            delivery, session, executor, custody, out var refusal);

        Assert.IsNull(receipt);
        Assert.AreEqual(RepeatedEnumerationReceiptRefusal.MembershipDisagreesOnADigest, refusal);
    }

    /// <summary>
    /// One custody entry per observation, with a body digest and a write-receipt digest derived
    /// from the observation's own evidence digest so every entry is distinct and reproducible.
    /// </summary>
    internal static List<RepeatedEnumerationObservationCustody> Custody(EnumerationDeliveryComparison delivery) =>
        AllObservations(delivery)
            .Select(static references => new RepeatedEnumerationObservationCustody(
                references,
                BodyDigestFor(references),
                CustodyMembership.Floored,
                WriteReceiptDigestFor(references)))
            .ToList();

    internal static string BodyDigestFor(RepeatedEnumerationEvidenceRefs references) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("body:" + references.HttpEvidenceRef.Sha256)));

    internal static string WriteReceiptDigestFor(RepeatedEnumerationEvidenceRefs references) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("write-receipt:" + references.HttpEvidenceRef.Sha256)));

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
            session[WriteReceiptDigestFor(observation)] = membership;
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
