using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// D1-03, R3.3: advancement is conditional on a cut that proves complete enumeration, so
/// completeness must be demonstrated by evidence and not asserted by whoever writes the cut.
/// </summary>
/// <remarks>
/// Every delivery comparison in this file is a real one, assembled by
/// <see cref="AbsenceEnumerationProofFixture"/> from the full retained evidence tuple that
/// <c>Source.Core</c> verifies. A stub would let these tests pass against a proof that proves
/// nothing, which is the exact failure the file exists to close.
/// </remarks>
[TestClass]
public sealed class AbsenceEnumerationProofTests
{
    /// <summary>
    /// The admitting case, first, so every refusal below is shown against a delivery that is
    /// otherwise good. Without this a guard that refused everything would look identical.
    /// </summary>
    [TestMethod]
    public void AVerifiedDeliveryOfThisFamilyBecomesAProof()
    {
        var delivery = AbsenceEnumerationProofFixture.Delivery("lu_root_family");

        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, delivery.Outcome);
        Assert.AreEqual(
            RepeatedEnumerationThresholdAssessment.BelowMaximum, delivery.ThresholdAssessment);

        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "lu_root_family", delivery, CustodyMembership.Floored, out var refusal);

        Assert.IsNotNull(proof);
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.None, refusal);
        Assert.AreEqual("lu_root_family", proof.FamilyKey);
        Assert.AreEqual(2, proof.DeliveredRowCount, "the proof did not retain the delivered rows");
        Assert.AreEqual(delivery.RunIdentity, proof.AcquisitionRunRef);
        Assert.AreEqual(delivery.CanonicalKeyDigestA, proof.CanonicalKeyDigest);
    }

    /// <summary>
    /// A perfectly good delivery of some other partition is not a proof about this family. This is
    /// the failure that a caller-supplied family-to-partition map would have made invisible.
    /// </summary>
    [TestMethod]
    public void ADeliveryOfAnotherPartitionIsNotAProofOfThisFamily()
    {
        var delivery = AbsenceEnumerationProofFixture.Delivery("lu_other_family");

        Assert.AreEqual(
            EnumerationDeliveryOutcome.EqualSelections,
            delivery.Outcome,
            "the fixture must fail only on the partition, or this proves nothing about the check");

        Assert.IsNull(AbsenceFamilyEnumerationProof.TryCreate(
            "lu_root_family", delivery, CustodyMembership.Floored, out var refusal));
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.PartitionIsNotThisFamily, refusal);
    }

    /// <summary>
    /// Two passes at different page limits that returned different rows describe no single
    /// enumeration, so neither of them is the family's.
    /// </summary>
    [TestMethod]
    public void PassesThatDisagreedProveNothing()
    {
        var delivery = AbsenceEnumerationProofFixture.DeliveryWithDisagreeingPasses();

        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, delivery.Outcome);
        Assert.AreEqual(
            RepeatedEnumerationThresholdAssessment.BelowMaximum,
            delivery.ThresholdAssessment,
            "the threshold must be in bounds, or the refusal below could be the other guard");
        Assert.AreEqual(
            delivery.DeliveredRowCountA,
            delivery.DeliveredRowCountB,
            "the passes must differ only in row identity, not in how many rows they delivered");

        Assert.IsNull(AbsenceFamilyEnumerationProof.TryCreate(
            "lu_root_family", delivery, CustodyMembership.Floored, out var refusal));
        Assert.AreEqual(
            AbsenceFamilyEnumerationProofRefusal.PassesDeliveredDifferentSelections, refusal);
    }

    /// <summary>
    /// The threshold is a separate condition and this is the fixture that shows why. Both passes
    /// agreed exactly, over a selection that reached the endpoint's maximum deliverable row count.
    /// A silent truncation truncates both passes the same way, so agreement is not evidence of a
    /// whole enumeration when the cap was reached.
    /// </summary>
    [TestMethod]
    public void AgreementAtTheRowCapIsAgreementAboutATruncation()
    {
        var delivery = AbsenceEnumerationProofFixture.DeliveryAtTheRowCap();

        Assert.AreEqual(
            EnumerationDeliveryOutcome.EqualSelections,
            delivery.Outcome,
            "the passes must agree, or the refusal below could be the outcome guard instead");
        Assert.AreEqual(
            RepeatedEnumerationThresholdAssessment.PartitionRequired, delivery.ThresholdAssessment);

        Assert.IsNull(AbsenceFamilyEnumerationProof.TryCreate(
            "lu_root_family", delivery, CustodyMembership.Floored, out var refusal));
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.SelectionReachedTheRowCap, refusal);
    }

    [TestMethod]
    public void AProofRefusesAnUnboundedFamilyIdentity()
    {
        Assert.IsNull(AbsenceFamilyEnumerationProof.TryCreate(
            "  ", AbsenceEnumerationProofFixture.Delivery(), CustodyMembership.Floored, out var refusal));
        Assert.AreEqual(AbsenceFamilyEnumerationProofRefusal.FamilyKeyInvalid, refusal);
    }

    /// <summary>
    /// A complete cut is unreachable without one proof per observed family. This is the whole
    /// point: not a check that runs after construction, but a parameter list a caller holding no
    /// proof cannot satisfy.
    /// </summary>
    [TestMethod]
    public void ACompleteCutCannotBeBuiltWithoutAProofForEveryObservedFamily()
    {
        AbsenceFamilyObservation[] observations =
        [
            AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a"),
            AbsenceFixtures.Observation("obs-b", AbsenceFixtures.Base, "family-b"),
        ];

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            observations,
            [AbsenceFixtures.Proof("family-a")],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var missing));
        Assert.AreEqual(AbsenceCutRefusal.FamilyEnumerationProofMissing, missing);

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            observations,
            [],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var none));
        Assert.AreEqual(AbsenceCutRefusal.FamilyEnumerationProofMissing, none);

        var admitted = AbsenceCut.TryCreateComplete(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            observations,
            [AbsenceFixtures.Proof("family-a"), AbsenceFixtures.Proof("family-b")],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var refusal);

        Assert.IsNotNull(admitted);
        Assert.AreEqual(AbsenceCutRefusal.None, refusal);
        Assert.AreEqual(AbsenceRunCompletion.EnumerationComplete, admitted.Completion);
        Assert.AreEqual(2, admitted.EnumerationProofs.Count);
    }

    /// <summary>
    /// Padding the proof list with proofs about families this run never observed does not make a
    /// cut complete. Each of these proofs is real; none of them is about this run's coverage.
    /// </summary>
    [TestMethod]
    public void AProofAboutAFamilyThisRunDidNotObserveIsRefused()
    {
        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            [AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a")],
            [AbsenceFixtures.Proof("family-a"), AbsenceFixtures.Proof("family-b")],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var foreign));
        Assert.AreEqual(AbsenceCutRefusal.EnumerationProofFamilyNotObserved, foreign);

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            [AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a")],
            [AbsenceFixtures.Proof("family-a"), AbsenceFixtures.Proof("family-a")],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var duplicate));
        Assert.AreEqual(AbsenceCutRefusal.DuplicateEnumerationProofFamily, duplicate);
    }

    /// <summary>
    /// A cut is one run. Proofs assembled from two acquisition runs are two runs' evidence stapled
    /// together, and a cut built from them would state a coverage no single run ever had.
    /// </summary>
    [TestMethod]
    public void ProofsFromTwoAcquisitionRunsDoNotMakeOneCut()
    {
        var here = AbsenceFixtures.Proof("family-a");
        var elsewhere = AbsenceFixtures.Proof("family-b", runSeed: 931);

        Assert.AreNotEqual(
            here.AcquisitionRunRef,
            elsewhere.AcquisitionRunRef,
            "the fixture built both proofs in the same run, so this test would pass either way");

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            [
                AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a"),
                AbsenceFixtures.Observation("obs-b", AbsenceFixtures.Base, "family-b"),
            ],
            [here, elsewhere],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var refusal));
        Assert.AreEqual(AbsenceCutRefusal.EnumerationProofsSpanMoreThanOneRun, refusal);
    }

    /// <summary>
    /// A partial cut carries no proofs and takes none. Its silences prove nothing whatever its
    /// families delivered, so there is nothing for a proof to say here and no parameter to say it
    /// in.
    /// </summary>
    [TestMethod]
    public void APartialCutHoldsNoProofsAndStillRecordsItsPositives()
    {
        var cut = AbsenceCut.TryCreatePartial(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            [AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a")],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.RootUri],
            out var refusal);

        Assert.IsNotNull(cut);
        Assert.AreEqual(AbsenceCutRefusal.None, refusal);
        Assert.AreEqual(AbsenceRunCompletion.Partial, cut.Completion);
        Assert.AreEqual(0, cut.EnumerationProofs.Count);
        Assert.IsTrue(cut.Observed(AbsenceFixtures.RootUri));
    }

    /// <summary>
    /// The end of the chain, stated as a ledger outcome rather than as a construction refusal: an
    /// unproven enumeration cannot advance an absence history, because the cut that would carry it
    /// cannot be built. Before this binding the same caller advanced the streak by passing an
    /// enum member.
    /// </summary>
    [TestMethod]
    public void AnUnprovenEnumerationCannotAdvanceAnAbsenceHistory()
    {
        var ledger = AbsenceFixtures.Ledger();
        var observations = new[]
        {
            AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a"),
        };

        Assert.IsNull(
            AbsenceCut.TryCreateComplete(
                "run-1",
                AbsenceApplicableSet.ObservedRootSet,
                observations,
                [],
                AbsenceFixtures.Artifact('e'),
                AbsenceFixtures.ObservedSet("1"),
                [AbsenceFixtures.OtherUri],
                out _),
            "an unproven enumeration still minted a complete cut");

        // The only cut such a caller can build is the partial one, and a partial run that did not
        // observe the subject neither advances nor breaks.
        var partial = AbsenceCut.TryCreatePartial(
            "run-1",
            AbsenceApplicableSet.ObservedRootSet,
            observations,
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out _);
        Assert.IsNotNull(partial);

        var receipt = ledger.TryAppend(partial, out var refusal);

        Assert.IsNotNull(receipt);
        Assert.AreEqual(AbsenceLedgerRefusal.None, refusal);
        Assert.AreEqual(AbsenceAppendDisposition.PartialRunNoEffect, receipt.Disposition);
        Assert.AreEqual(0, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.NoEvidenceUnderCurrentGeneration, ledger.State());
    }
}
