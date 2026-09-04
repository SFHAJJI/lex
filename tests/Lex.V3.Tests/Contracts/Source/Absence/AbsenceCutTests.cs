using Lex.V3.Contracts.Source.Absence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// D1-03, R3.3 lines 508 to 516: what a cut must carry, how cut_start and cut_end are derived, and
/// the temporal statements a cut is not allowed to make.
/// </summary>
[TestClass]
public sealed class AbsenceCutTests
{
    [TestMethod]
    public void CutStartAndCutEndAreTheExtremesOfTheFreshObservations()
    {
        var cut = AbsenceFixtures.Cut(
            "run-1",
            AbsenceFixtures.Base,
            [AbsenceFixtures.OtherUri],
            observations:
            [
                AbsenceFixtures.Observation("obs-b", AbsenceFixtures.Base + TimeSpan.FromHours(2), "family-b"),
                AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family-a"),
                AbsenceFixtures.Observation("obs-c", AbsenceFixtures.Base + TimeSpan.FromHours(1), "family-c"),
            ]);

        Assert.AreEqual(AbsenceFixtures.Base, cut.CutStart());
        Assert.AreEqual(AbsenceFixtures.Base + TimeSpan.FromHours(2), cut.CutEnd());
    }

    /// <summary>
    /// The extremes are taken over every observation, so two observations sharing the minimum
    /// timestamp need no tie-break. R3.3 forbids an identifier from breaking an equal timestamp, and
    /// a derivation that picked "the cut_start observation" would have had to.
    /// </summary>
    [TestMethod]
    public void EqualTimestampsAreNotSeparatedByIdentity()
    {
        var wide = AbsenceFixtures.Observation(
            "obs-wide", AbsenceFixtures.Base, "family-wide", skew: TimeSpan.FromHours(1));
        var narrow = AbsenceFixtures.Observation(
            "obs-narrow", AbsenceFixtures.Base, "family-narrow", skew: TimeSpan.Zero);

        var oneOrder = AbsenceFixtures.Cut(
            "run-1", AbsenceFixtures.Base, [AbsenceFixtures.OtherUri], observations: [wide, narrow]);
        var otherOrder = AbsenceFixtures.Cut(
            "run-2", AbsenceFixtures.Base, [AbsenceFixtures.OtherUri], observations: [narrow, wide]);

        Assert.AreEqual(oneOrder.EarliestPossibleStart(), otherOrder.EarliestPossibleStart());
        Assert.AreEqual(oneOrder.LatestPossibleEnd(), otherOrder.LatestPossibleEnd());
        Assert.AreEqual(
            AbsenceFixtures.Base - TimeSpan.FromHours(1),
            oneOrder.EarliestPossibleStart(),
            "the widest uncertainty did not decide the earliest possible start");
    }

    /// <summary>
    /// A declared precision has to be true of the value. Each precision is exercised with a value
    /// one tick finer than it admits, and with the aligned value, so the check is shown refusing and
    /// admitting rather than only refusing.
    /// </summary>
    [TestMethod]
    public void ADeclaredPrecisionMustMatchTheValue()
    {
        foreach (var precision in Enum.GetValues<AbsenceTimestampPrecision>())
        {
            var width = AbsenceTiming.WidthOf(precision);
            var aligned = new DateTimeOffset(
                AbsenceFixtures.Base.UtcTicks - (AbsenceFixtures.Base.UtcTicks % width.Ticks),
                TimeSpan.Zero);

            Assert.IsNotNull(
                AbsenceFamilyObservation.TryCreate(
                    "obs", "family", aligned, precision, "clock", TimeSpan.Zero,
                    AbsenceObservationProvenance.FreshlyExecuted, out var admitted),
                $"{precision} refused a value aligned to its own width");
            Assert.AreEqual(AbsenceFamilyObservationRefusal.None, admitted);

            Assert.IsNull(
                AbsenceFamilyObservation.TryCreate(
                    "obs", "family", aligned.AddTicks(1), precision, "clock", TimeSpan.Zero,
                    AbsenceObservationProvenance.FreshlyExecuted, out var refused),
                $"{precision} admitted a value one tick finer than it declares");
            Assert.AreEqual(
                AbsenceFamilyObservationRefusal.TimestampFinerThanDeclaredPrecision, refused);
        }
    }

    /// <summary>
    /// A wrapper, a cache replay, a stale row and an incomplete row can never become an observation
    /// at all, so no cut can hold one. R3.3 names each of these as insufficient for a fresh family
    /// response.
    /// </summary>
    [TestMethod]
    public void OnlyAFreshlyExecutedObservationCanExist()
    {
        foreach (var provenance in Enum.GetValues<AbsenceObservationProvenance>())
        {
            var observation = AbsenceFamilyObservation.TryCreate(
                "obs", "family", AbsenceFixtures.Base, AbsenceTimestampPrecision.Second,
                "clock", TimeSpan.Zero, provenance, out var refusal);

            if (provenance == AbsenceObservationProvenance.FreshlyExecuted)
            {
                Assert.IsNotNull(observation);
                Assert.AreEqual(AbsenceFamilyObservationRefusal.None, refusal);
                continue;
            }

            Assert.IsNull(observation, $"{provenance} became an observation");
            Assert.AreEqual(AbsenceFamilyObservationRefusal.ProvenanceNotFreshlyExecuted, refusal);
        }
    }

    [TestMethod]
    public void AnObservationRefusesAnUnusableTemporalStatement()
    {
        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            AbsenceTimestampPrecision.Second, "clock", TimeSpan.Zero,
            AbsenceObservationProvenance.FreshlyExecuted, out var offset));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.TimestampNotUtc, offset);

        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family", AbsenceFixtures.Base, AbsenceTimestampPrecision.Second, "clock",
            TimeSpan.FromSeconds(-1), AbsenceObservationProvenance.FreshlyExecuted, out var skew));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.SkewNegative, skew);

        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family", AbsenceFixtures.Base, AbsenceTimestampPrecision.Second, "  ",
            TimeSpan.Zero, AbsenceObservationProvenance.FreshlyExecuted, out var clock));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.ClockSourceInvalid, clock);

        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family", AbsenceFixtures.Base, (AbsenceTimestampPrecision)77, "clock",
            TimeSpan.Zero, AbsenceObservationProvenance.FreshlyExecuted, out var precision));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.PrecisionUndefined, precision);

        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family", AbsenceFixtures.Base, AbsenceTimestampPrecision.Second, "clock",
            TimeSpan.Zero, (AbsenceObservationProvenance)77, out var provenance));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.ProvenanceUndefined, provenance);

        // Aligned to the microsecond width first, so the refusal below is the overflow guard and
        // not the precision guard standing in front of it.
        var microsecondWidth = AbsenceTiming.WidthOf(AbsenceTimestampPrecision.Microsecond).Ticks;
        var nearMaximum = new DateTimeOffset(
            DateTimeOffset.MaxValue.UtcTicks
                - (DateTimeOffset.MaxValue.UtcTicks % microsecondWidth)
                - microsecondWidth,
            TimeSpan.Zero);
        Assert.IsNotNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family", nearMaximum, AbsenceTimestampPrecision.Microsecond, "clock",
            TimeSpan.Zero, AbsenceObservationProvenance.FreshlyExecuted, out var alignedAtMaximum));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.None, alignedAtMaximum);

        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", "family", nearMaximum, AbsenceTimestampPrecision.Microsecond, "clock",
            TimeSpan.FromHours(1), AbsenceObservationProvenance.FreshlyExecuted, out var overflow));
        Assert.AreEqual(
            AbsenceFamilyObservationRefusal.UncertaintyIntervalNotRepresentable, overflow);
    }

    [TestMethod]
    public void ACutRefusesAnEmptyOrRepeatedObservationList()
    {
        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run", AbsenceApplicableSet.ObservedRootSet,
            [], [], AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri], out var empty));
        Assert.AreEqual(AbsenceCutRefusal.ObservationsEmpty, empty);

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run", AbsenceApplicableSet.ObservedRootSet,
            [
                AbsenceFixtures.Observation("obs", AbsenceFixtures.Base, "family-a"),
                AbsenceFixtures.Observation("obs", AbsenceFixtures.Base, "family-b"),
            ],
            [AbsenceFixtures.Proof("family-a"), AbsenceFixtures.Proof("family-b")],
            AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri], out var duplicateId));
        Assert.AreEqual(AbsenceCutRefusal.DuplicateObservationId, duplicateId);

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run", AbsenceApplicableSet.ObservedRootSet,
            [
                AbsenceFixtures.Observation("obs-a", AbsenceFixtures.Base, "family"),
                AbsenceFixtures.Observation("obs-b", AbsenceFixtures.Base, "family"),
            ],
            [AbsenceFixtures.Proof("family")],
            AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri], out var duplicateFamily));
        Assert.AreEqual(AbsenceCutRefusal.DuplicateFamilyKey, duplicateFamily);
    }

    [TestMethod]
    public void AnObservationRefusesAnUnboundedIdentity()
    {
        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "  ", "family", AbsenceFixtures.Base, AbsenceTimestampPrecision.Second, "clock",
            TimeSpan.Zero, AbsenceObservationProvenance.FreshlyExecuted, out var observationId));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.ObservationIdInvalid, observationId);

        Assert.IsNull(AbsenceFamilyObservation.TryCreate(
            "obs", new string('x', 257), AbsenceFixtures.Base, AbsenceTimestampPrecision.Second,
            "clock", TimeSpan.Zero, AbsenceObservationProvenance.FreshlyExecuted, out var familyKey));
        Assert.AreEqual(AbsenceFamilyObservationRefusal.FamilyKeyInvalid, familyKey);
    }

    [TestMethod]
    public void ACutRefusesAnUnboundedRunIdentityOrAnUndefinedVocabularyMember()
    {
        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "  ", AbsenceApplicableSet.ObservedRootSet,
            [AbsenceFixtures.Observation("obs", AbsenceFixtures.Base)],
            [AbsenceFixtures.Proof()],
            AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri], out var runId));
        Assert.AreEqual(AbsenceCutRefusal.RunIdInvalid, runId);

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run", (AbsenceApplicableSet)77,
            [AbsenceFixtures.Observation("obs", AbsenceFixtures.Base)],
            [AbsenceFixtures.Proof()],
            AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri], out var applicableSet));
        Assert.AreEqual(AbsenceCutRefusal.ApplicableSetUndefined, applicableSet);
    }

    [TestMethod]
    public void ACutRefusesAnObservedKeyThatIsNotACanonicalPublisherUri()
    {
        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run", AbsenceApplicableSet.ObservedRootSet,
            [AbsenceFixtures.Observation("obs", AbsenceFixtures.Base)],
            [AbsenceFixtures.Proof()],
            AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            ["not a uri"], out var invalid));
        Assert.AreEqual(AbsenceCutRefusal.ObservedKeyInvalid, invalid);

        Assert.IsNull(AbsenceCut.TryCreateComplete(
            "run", AbsenceApplicableSet.ObservedRootSet,
            [AbsenceFixtures.Observation("obs", AbsenceFixtures.Base)],
            [AbsenceFixtures.Proof()],
            AbsenceFixtures.Artifact('e'), AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri, AbsenceFixtures.OtherUri], out var duplicate));
        Assert.AreEqual(AbsenceCutRefusal.DuplicateObservedKey, duplicate);
    }

    [TestMethod]
    public void ACutAnswersMembershipOnTheExactCanonicalKey()
    {
        var cut = AbsenceFixtures.Cut(
            "run-1", AbsenceFixtures.Base, [AbsenceFixtures.RootUri, AbsenceFixtures.ThirdUri]);

        Assert.IsTrue(cut.Observed(AbsenceFixtures.RootUri));
        Assert.IsTrue(cut.Observed(AbsenceFixtures.ThirdUri));
        Assert.IsFalse(cut.Observed(AbsenceFixtures.OtherUri));
        Assert.IsFalse(
            cut.Observed(AbsenceFixtures.RootUri + "/"),
            "membership matched a key that is not the exact canonical URI");
    }
    /// <summary>
    /// THE RELEASE GATE. A complete cut refuses a proof whose run is held without an enforced
    /// retention floor, and admits the identical proof when that run is floored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RULING lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6, and this is where the
    /// durability requirement now lives. It used to sit in
    /// <c>RepeatedEnumerationDeliveryReceipt.RequireFlooredRun</c>, which threw so that no proof
    /// existed at all for an unfloored run; that refused the family, which refused the run, which
    /// meant a store publishing no enforcement could acquire nothing. Acquisition now records the
    /// class each artifact observed and continues, and the claim that genuinely depends on
    /// immutability is the cut.
    /// </para>
    /// <para>
    /// The two halves are the whole assertion. Refusing the unfloored proof alone would pass for a
    /// cut that refuses everything; admitting the floored one alongside it is what makes the refusal
    /// about the class rather than about the fixture.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ACompleteCutRefusesAProofHeldWithoutAnEnforcedFloor()
    {
        var observation = AbsenceFixtures.Observation("obs", AbsenceFixtures.Base, "family-a");
        var unfloored = AbsenceFixtures.UnflooredProof("family-a");
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, unfloored.RetainedFloor);

        var refused = AbsenceCut.TryCreateComplete(
            "run",
            AbsenceApplicableSet.ObservedRootSet,
            [observation],
            [unfloored],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var refusal);

        Assert.IsNull(refused, "a release may not rest on evidence that need not survive.");
        Assert.AreEqual(AbsenceCutRefusal.EnumerationProofNotFloored, refusal);

        // The identical shape with a floored proof is admitted, so the refusal above is about the
        // custody class and nothing else.
        var floored = AbsenceFixtures.Proof("family-a");
        Assert.AreEqual(CustodyMembership.Floored, floored.RetainedFloor);
        var admitted = AbsenceCut.TryCreateComplete(
            "run",
            AbsenceApplicableSet.ObservedRootSet,
            [observation],
            [floored],
            AbsenceFixtures.Artifact('e'),
            AbsenceFixtures.ObservedSet("1"),
            [AbsenceFixtures.OtherUri],
            out var admittedRefusal);

        Assert.AreEqual(AbsenceCutRefusal.None, admittedRefusal);
        Assert.IsNotNull(admitted);
        Assert.AreEqual(AbsenceRunCompletion.EnumerationComplete, admitted!.Completion);
    }

}
