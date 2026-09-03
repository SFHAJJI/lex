using Lex.V3.Contracts.Source.Absence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// D1-03, R3.3 lines 495 to 516, with Decision 20: three completed runs of absence, absence
/// surfaced as unconfirmed from the first miss, an append-only generation chain, and a
/// configuration A to B to A return that must not reconnect a prior streak.
/// </summary>
[TestClass]
public sealed class AbsenceHistoryLedgerTests
{
    /// <summary>
    /// Twenty-one hours apart. The floor is twenty hours between the predecessor's latest possible
    /// end and the successor's earliest possible start, so a spacing chosen at exactly twenty would
    /// sit on the wrong side of the second condition for every nonzero precision or skew. The exact
    /// boundary is tested on its own below.
    /// </summary>
    private static DateTimeOffset At(int index) =>
        AbsenceFixtures.Base + TimeSpan.FromHours(21 * index);

    private static AbsenceHistoryLedger.CutReceipt Append(
        AbsenceHistoryLedger ledger,
        AbsenceCut cut,
        AbsenceAppendDisposition expected)
    {
        var receipt = ledger.TryAppend(cut, out var refusal);
        Assert.IsNotNull(receipt, $"{cut.RunId} was refused as {refusal}");
        Assert.AreEqual(AbsenceLedgerRefusal.None, refusal);
        Assert.AreEqual(expected, receipt.Disposition, $"{cut.RunId} took the wrong disposition");
        return receipt;
    }

    private static AbsenceCut Absent(string runId, int index, string? observedSetTail = null) =>
        AbsenceFixtures.Cut(runId, At(index), [AbsenceFixtures.OtherUri], observedSetTail: observedSetTail);

    private static AbsenceCut Present(string runId, int index) =>
        AbsenceFixtures.Cut(runId, At(index), [AbsenceFixtures.RootUri, AbsenceFixtures.OtherUri]);

    /// <summary>
    /// The two numbers R3.3 and Decision 20 state, transcribed as literals. Everything else in this
    /// file reads them from the contract, so without this pin a change to either constant would
    /// move every fixture with it and break nothing.
    /// </summary>
    [TestMethod]
    public void TheFloorIsTwentyHoursAndThreeCompletedRunsAreRequired()
    {
        Assert.AreEqual(TimeSpan.FromHours(20), AbsenceTiming.MinimumSeparation);
        Assert.AreEqual(3, AbsenceTiming.AdvancingCutsRequired);
    }

    [TestMethod]
    public void TheFirstGenerationIsInitialTrackingWithNoPredecessor()
    {
        var ledger = AbsenceFixtures.Ledger();

        Assert.HasCount(1, ledger.Generations);
        Assert.AreEqual(AbsenceHistoryGenerationCause.InitialTracking, ledger.CurrentGeneration.Cause);
        Assert.IsNull(ledger.CurrentGeneration.PredecessorId);
        Assert.AreEqual(1, ledger.CurrentGeneration.Ordinal);
        Assert.AreEqual(AbsenceState.NoEvidenceUnderCurrentGeneration, ledger.State());
        Assert.AreEqual(0, ledger.CurrentStreakLength());
    }

    /// <summary>
    /// Decision 20 exactly: absent_unconfirmed from the first miss, confirmed only on the third
    /// completed run. The intermediate states are asserted, not just the endpoint, because a rule
    /// that only checked the third cut would pass against an implementation that surfaced nothing
    /// until then, which is the one-run tombstone defect Decision 20 was written to correct.
    /// </summary>
    [TestMethod]
    public void ThreeAdvancingAbsentCompleteCutsConfirmAbsenceAndTwoDoNot()
    {
        var ledger = AbsenceFixtures.Ledger();

        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(1, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.AbsentUnconfirmed, ledger.State());

        Append(ledger, Absent("run-2", 1), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(2, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.AbsentUnconfirmed, ledger.State());

        Append(ledger, Absent("run-3", 2), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(3, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.AbsentConfirmed, ledger.State());

        var first = ledger.Receipts[0];
        var third = ledger.Receipts[2];
        Assert.IsTrue(
            third.Cut.CutStart() - first.Cut.CutEnd() >= TimeSpan.FromHours(40),
            "three advancing cuts spanned less than forty hours");
    }

    /// <summary>
    /// The exact boundary of the second condition. At sixty seconds past the twenty hour mark the
    /// raw timestamps are already far enough apart and only the uncertainty intervals refuse, which
    /// is the whole reason R3.3 states a second condition.
    /// </summary>
    [TestMethod]
    public void TheUncertaintyIntervalDecidesWhereTheRawTimestampsAlreadyAgree()
    {
        // Twenty hours is written out rather than read from AbsenceTiming, and that is the point.
        // A fixture that derives its expectation from the constant under test moves with it: the
        // first version of this file did exactly that, and lowering the floor from twenty hours to
        // one broke nothing.
        var skew = TimeSpan.FromSeconds(30);
        var width = TimeSpan.FromSeconds(1);
        var earliestAdmissible =
            AbsenceFixtures.Base + TimeSpan.FromHours(20) + width + skew + skew;

        foreach (var (offset, expected) in new[]
        {
            (TimeSpan.FromSeconds(-1), AbsenceAppendDisposition.SeparationFloorNotMet),
            (TimeSpan.Zero, AbsenceAppendDisposition.StreakAdvanced),
        })
        {
            var ledger = AbsenceFixtures.Ledger();
            Append(
                ledger,
                AbsenceFixtures.Cut("run-1", AbsenceFixtures.Base, [AbsenceFixtures.OtherUri]),
                AbsenceAppendDisposition.StreakAdvanced);

            var candidate = AbsenceFixtures.Cut(
                "run-2", earliestAdmissible + offset, [AbsenceFixtures.OtherUri]);

            Assert.IsTrue(
                candidate.CutStart() >= AbsenceFixtures.Base + TimeSpan.FromHours(20),
                "the fixture no longer satisfies the raw timestamp condition, so it proves nothing " +
                "about the interval condition");

            Append(ledger, candidate, expected);
        }
    }

    /// <summary>
    /// Why there is no separate test for R3.3's first timing condition: under this interval model
    /// the second condition implies the first, so no fixture can fail one while passing the other.
    /// These are the two facts the implication rests on, asserted across every precision and a
    /// range of skews so a change to the interval derivation is caught here rather than by a test
    /// that could never have failed.
    /// </summary>
    [TestMethod]
    public void TheIntervalOfACutAlwaysContainsItsRawTimestampRange()
    {
        foreach (var precision in Enum.GetValues<AbsenceTimestampPrecision>())
        {
            foreach (var seconds in new[] { 0, 1, 3600 })
            {
                var width = AbsenceTiming.WidthOf(precision);
                var at = new DateTimeOffset(
                    AbsenceFixtures.Base.UtcTicks - (AbsenceFixtures.Base.UtcTicks % width.Ticks),
                    TimeSpan.Zero);
                var cut = AbsenceFixtures.Cut(
                    "run-1",
                    at,
                    [AbsenceFixtures.OtherUri],
                    observations:
                    [
                        AbsenceFixtures.Observation(
                            "obs-a", at, "family-a", precision: precision,
                            skew: TimeSpan.FromSeconds(seconds)),
                        AbsenceFixtures.Observation(
                            "obs-b", at + width, "family-b", precision: precision,
                            skew: TimeSpan.FromSeconds(seconds)),
                    ]);

                Assert.IsTrue(
                    cut.EarliestPossibleStart() <= cut.CutStart(),
                    $"{precision} with {seconds}s skew put the earliest possible start after cut_start");
                Assert.IsTrue(
                    cut.LatestPossibleEnd() >= cut.CutEnd(),
                    $"{precision} with {seconds}s skew put the latest possible end before cut_end");
            }
        }
    }

    /// <summary>
    /// A cut inside the floor enters the ledger and is retained, but it does not count, and it does
    /// not become the predecessor the next floor is measured against.
    /// </summary>
    [TestMethod]
    public void ACutInsideTheFloorIsRetainedWithoutAdvancing()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);

        // Nineteen hours, not one. A one hour gap is refused by the interval condition whatever the
        // floor is, so it would have proved the floor exists without proving it is twenty hours.
        var tooSoon = AbsenceFixtures.Cut(
            "run-2", At(0) + TimeSpan.FromHours(19), [AbsenceFixtures.OtherUri]);
        Append(ledger, tooSoon, AbsenceAppendDisposition.SeparationFloorNotMet);

        Assert.AreEqual(1, ledger.CurrentStreakLength());
        Assert.HasCount(2, ledger.Receipts);

        var next = Append(ledger, Absent("run-3", 1), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(
            "run-1",
            next.PrecedingAdvancingCutId,
            "the floor was measured against a cut that never advanced");
        Assert.AreEqual("run-2", next.PrecedingAbsentCutId);
        Assert.AreEqual("run-2", next.PrecedingCompleteCutId);
    }

    [TestMethod]
    public void AClockSourceChangeDoesNotAdvance()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);

        var moved = AbsenceFixtures.Cut(
            "run-2",
            At(1),
            [AbsenceFixtures.OtherUri],
            observations: [AbsenceFixtures.Observation("run-2-obs-1", At(1), clockSource: "other-ntp")]);

        Append(ledger, moved, AbsenceAppendDisposition.ClockSourceChanged);
        Assert.AreEqual(1, ledger.CurrentStreakLength());
    }

    /// <summary>
    /// R3.3's own worked example: absent A, present B, absent C, absent D is a two cut C to D
    /// streak, never a three cut A, C, D result.
    /// </summary>
    [TestMethod]
    public void AnInterveningPresenceBreaksConsecutiveness()
    {
        var ledger = AbsenceFixtures.Ledger();

        Append(ledger, Absent("run-a", 0), AbsenceAppendDisposition.StreakAdvanced);
        Append(ledger, Present("run-b", 1), AbsenceAppendDisposition.PresenceBreakRecorded);

        Assert.AreEqual(AbsenceState.Present, ledger.State());
        Assert.AreEqual(0, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceHistoryGenerationCause.PresenceBreak, ledger.CurrentGeneration.Cause);

        Append(ledger, Absent("run-c", 2), AbsenceAppendDisposition.StreakAdvanced);
        Append(ledger, Absent("run-d", 3), AbsenceAppendDisposition.StreakAdvanced);

        Assert.AreEqual(2, ledger.CurrentStreakLength());
        Assert.AreEqual(
            AbsenceState.AbsentUnconfirmed,
            ledger.State(),
            "the pre-presence cut was counted, so a two cut streak confirmed absence");
    }

    /// <summary>
    /// Reappearance never deletes the earlier absence receipts. The digests are checked, not just
    /// the count, because a ledger that overwrote a receipt in place would keep the count.
    /// </summary>
    [TestMethod]
    public void ReappearanceRetainsEveryEarlierReceiptAndObservedSetDigest()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-a", 0, observedSetTail: "a1"), AbsenceAppendDisposition.StreakAdvanced);
        Append(ledger, Present("run-b", 1), AbsenceAppendDisposition.PresenceBreakRecorded);
        Append(ledger, Absent("run-c", 2, observedSetTail: "c1"), AbsenceAppendDisposition.StreakAdvanced);

        Assert.HasCount(3, ledger.Receipts);
        Assert.IsTrue(ledger.Receipts[0].Advanced);
        Assert.IsFalse(ledger.Receipts[0].Observed);
        Assert.IsTrue(ledger.Receipts[1].Observed);

        CollectionAssert.AreEqual(
            new[] { "run-a", "run-b", "run-c" },
            ledger.Receipts.Select(static receipt => receipt.Cut.RunId).ToArray());

        Assert.AreEqual(
            3,
            ledger.Receipts.Select(static receipt => receipt.ObservedSetRef.Sha256)
                .Distinct(StringComparer.Ordinal).Count(),
            "the retained observed-set digests collapsed, so one cut's evidence was lost");
    }

    [TestMethod]
    public void APartialRunWithoutAPositiveNeitherAdvancesNorBreaks()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);

        var partial = AbsenceFixtures.Cut(
            "run-2", At(1), [AbsenceFixtures.OtherUri], completion: AbsenceRunCompletion.Partial);
        Append(ledger, partial, AbsenceAppendDisposition.PartialRunNoEffect);

        Assert.AreEqual(1, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.AbsentUnconfirmed, ledger.State());
        Assert.HasCount(1, ledger.Generations);

        var next = Append(ledger, Absent("run-3", 2), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(
            "run-1",
            next.PrecedingCompleteCutId,
            "a partial run was named as the preceding enumeration_complete cut");
    }

    /// <summary>
    /// A trustworthy positive in a partial run breaks the streak although the partial run can never
    /// advance one.
    /// </summary>
    [TestMethod]
    public void APositiveInAPartialRunBreaksTheStreak()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);
        Append(ledger, Absent("run-2", 1), AbsenceAppendDisposition.StreakAdvanced);

        var partialPositive = AbsenceFixtures.Cut(
            "run-3",
            At(2),
            [AbsenceFixtures.RootUri],
            completion: AbsenceRunCompletion.Partial);
        Append(ledger, partialPositive, AbsenceAppendDisposition.PresenceBreakRecorded);

        Assert.AreEqual(0, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.Present, ledger.State());
        Assert.AreEqual(AbsenceHistoryGenerationCause.PresenceBreak, ledger.CurrentGeneration.Cause);
    }

    /// <summary>
    /// Ordinary membership changes elsewhere in the corpus, and the observed-set digest changes they
    /// produce, do not reset, freeze or restart this subject's history.
    /// </summary>
    [TestMethod]
    public void UnrelatedMembershipChangesDoNotResetAnUnrelatedHistory()
    {
        var ledger = AbsenceFixtures.Ledger();

        var cuts = new[]
        {
            AbsenceFixtures.Cut("run-1", At(0), [AbsenceFixtures.OtherUri], observedSetTail: "a1"),
            AbsenceFixtures.Cut(
                "run-2", At(1),
                [AbsenceFixtures.OtherUri, AbsenceFixtures.ThirdUri],
                observedSetTail: "b2"),
            AbsenceFixtures.Cut("run-3", At(2), [AbsenceFixtures.ThirdUri], observedSetTail: "c3"),
        };

        Assert.AreEqual(
            3,
            cuts.Select(static cut => cut.ObservedSetRef.Sha256).Distinct(StringComparer.Ordinal).Count(),
            "the fixture no longer varies the observed-set digest, so it proves nothing");

        foreach (var cut in cuts)
        {
            Append(ledger, cut, AbsenceAppendDisposition.StreakAdvanced);
        }

        Assert.AreEqual(3, ledger.CurrentStreakLength());
        Assert.AreEqual(AbsenceState.AbsentConfirmed, ledger.State());
        Assert.HasCount(1, ledger.Generations);
    }

    /// <summary>
    /// The property O4 calls out: a configuration that changes and changes back must not let the old
    /// streak resume. The control in the same test is what makes it mean anything. Without it, a
    /// third cut that failed to advance for any unrelated reason would produce the same green.
    /// </summary>
    [TestMethod]
    public void AConfigurationReturnToAnEarlierPolicyDoesNotReconnectThePriorStreak()
    {
        var control = AbsenceFixtures.Ledger();
        Append(control, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);
        Append(control, Absent("run-2", 1), AbsenceAppendDisposition.StreakAdvanced);
        Append(control, Absent("run-3", 2), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(
            AbsenceState.AbsentConfirmed,
            control.State(),
            "the control did not confirm, so the A to B to A result below would prove nothing");

        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);
        Append(ledger, Absent("run-2", 1), AbsenceAppendDisposition.StreakAdvanced);
        Assert.AreEqual(2, ledger.CurrentStreakLength());

        var toB = ledger.TryTransitionComparisonPolicy(
            AbsenceFixtures.Policy('b', AbsenceComparisonPolicyMember.RootDefinitionDigest),
            "evt-to-b",
            out var toBRefusal);
        Assert.IsNotNull(toB, $"the move to B was refused as {toBRefusal}");
        Assert.AreEqual(AbsenceHistoryGenerationCause.ComparisonPolicyChanged, toB.Cause);
        Assert.AreEqual(0, ledger.CurrentStreakLength());

        var backToA = ledger.TryTransitionComparisonPolicy(
            AbsenceFixtures.Policy(), "evt-back-to-a", out var backRefusal);
        Assert.IsNotNull(backToA, $"the return to A was refused as {backRefusal}");

        var first = ledger.Generations[0];
        Assert.IsTrue(
            backToA.Policy.SameConfigurationAs(first.Policy),
            "the fixture did not actually return to the earlier configuration");
        Assert.AreNotEqual(
            first.Id.Value,
            backToA.Id.Value,
            "the return to a byte-identical configuration reproduced the earlier generation identity");
        Assert.AreEqual(3, backToA.Ordinal);
        Assert.AreEqual(toB.Id.Value, backToA.PredecessorId?.Value);

        Append(ledger, Absent("run-3", 2), AbsenceAppendDisposition.StreakAdvanced);

        Assert.AreEqual(
            1,
            ledger.CurrentStreakLength(),
            "the earlier streak reconnected across the A to B to A return");
        Assert.AreEqual(AbsenceState.AbsentUnconfirmed, ledger.State());

        Assert.HasCount(3, ledger.Receipts);
        Assert.IsTrue(
            ledger.Receipts.Take(2).All(static receipt => receipt.Advanced),
            "the earlier advancing receipts were rewritten rather than preserved");
    }

    [TestMethod]
    public void ALedgerRefusesAnUndefinedAxisOrAnUnboundedTrackingEvent()
    {
        Assert.IsNull(AbsenceHistoryLedger.TryOpen(
            AbsenceFixtures.Subject(), (AbsenceApplicableSet)77, AbsenceFixtures.Policy(),
            "track-1", out var axis));
        Assert.AreEqual(AbsenceLedgerRefusal.ApplicableSetUndefined, axis);

        Assert.IsNull(AbsenceHistoryLedger.TryOpen(
            AbsenceFixtures.Subject(), AbsenceApplicableSet.ObservedRootSet, AbsenceFixtures.Policy(),
            "  ", out var trackingEvent));
        Assert.AreEqual(AbsenceLedgerRefusal.EventIdInvalid, trackingEvent);

        var ledger = AbsenceFixtures.Ledger();
        Assert.IsNull(ledger.TryTransitionComparisonPolicy(
            AbsenceFixtures.Policy('b', AbsenceComparisonPolicyMember.AdapterDigest),
            "  ",
            out var transitionEvent));
        Assert.AreEqual(AbsenceLedgerRefusal.EventIdInvalid, transitionEvent);
    }

    [TestMethod]
    public void ATransitionToTheSameConfigurationIsRefused()
    {
        var ledger = AbsenceFixtures.Ledger();

        Assert.IsNull(
            ledger.TryTransitionComparisonPolicy(AbsenceFixtures.Policy(), "evt-2", out var refusal));
        Assert.AreEqual(AbsenceLedgerRefusal.ComparisonPolicyUnchanged, refusal);
        Assert.HasCount(1, ledger.Generations);

        Assert.IsNull(ledger.TryTransitionComparisonPolicy(
            AbsenceFixtures.Policy('b', AbsenceComparisonPolicyMember.AdapterDigest),
            "track-1",
            out var reused));
        Assert.AreEqual(AbsenceLedgerRefusal.EventIdReused, reused);
        Assert.HasCount(1, ledger.Generations);
    }

    [TestMethod]
    public void AReplayedRunOrObservationIdentityIsRefused()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);

        Assert.IsNull(ledger.TryAppend(Absent("run-1", 1), out var runRefusal));
        Assert.AreEqual(AbsenceLedgerRefusal.RunIdReused, runRefusal);

        var freshRunReplayedObservation = AbsenceFixtures.Cut(
            "run-2",
            At(1),
            [AbsenceFixtures.OtherUri],
            observations: [AbsenceFixtures.Observation("run-1-obs-1", At(1))]);
        Assert.IsNull(ledger.TryAppend(freshRunReplayedObservation, out var observationRefusal));
        Assert.AreEqual(AbsenceLedgerRefusal.ObservationIdReused, observationRefusal);

        Assert.HasCount(1, ledger.Receipts, "a refused cut was retained anyway");
        Assert.AreEqual(1, ledger.CurrentStreakLength());
    }

    [TestMethod]
    public void ACutOnADifferentApplicableSetIsRefused()
    {
        var ledger = AbsenceFixtures.Ledger();
        var familyCut = AbsenceFixtures.Cut(
            "run-1", At(0), [AbsenceFixtures.OtherUri],
            applicableSet: AbsenceApplicableSet.NormalizedFamilySet);

        Assert.IsNull(ledger.TryAppend(familyCut, out var refusal));
        Assert.AreEqual(AbsenceLedgerRefusal.CutAxisNotApplicable, refusal);
        Assert.IsEmpty(ledger.Receipts);
    }

    /// <summary>
    /// Replacement detection runs before absence, so a frozen subject stops advancing while its
    /// cuts are still retained.
    /// </summary>
    [TestMethod]
    public void AFrozenSubjectRetainsItsCutsWithoutAdvancing()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Absent("run-1", 0), AbsenceAppendDisposition.StreakAdvanced);

        var classification = AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(),
            "cut-old",
            "cut-new",
            [AbsenceFixtures.RootUri],
            [AbsenceFixtures.ThirdUri],
            out var classificationRefusal);
        Assert.IsNotNull(classification, $"the fixture classification was refused as {classificationRefusal}");
        Assert.AreEqual(
            AbsenceReplacementDisposition.ReplacementCandidateOneToOne, classification.Disposition);

        Assert.IsTrue(ledger.TryRecordReplacementClassification(classification, out var recordRefusal));
        Assert.AreEqual(AbsenceLedgerRefusal.None, recordRefusal);
        Assert.AreEqual(AbsenceState.FrozenPendingReplacementReview, ledger.State());

        Append(ledger, Absent("run-2", 1), AbsenceAppendDisposition.FrozenPendingReplacementReview);
        Assert.HasCount(2, ledger.Receipts);
        Assert.AreEqual(1, ledger.CurrentStreakLength());
    }

    [TestMethod]
    public void AClassificationThatDoesNotNameThisSubjectIsRefused()
    {
        var ledger = AbsenceFixtures.Ledger();

        var elsewhere = AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(),
            "cut-old",
            "cut-new",
            [AbsenceFixtures.OtherUri],
            [AbsenceFixtures.ThirdUri],
            out _);
        Assert.IsNotNull(elsewhere);

        Assert.IsFalse(ledger.TryRecordReplacementClassification(elsewhere, out var refusal));
        Assert.AreEqual(AbsenceLedgerRefusal.ClassificationOutsideThisSubject, refusal);
        Assert.IsEmpty(ledger.ReplacementClassifications);
        Assert.AreEqual(AbsenceState.NoEvidenceUnderCurrentGeneration, ledger.State());
    }

    /// <summary>
    /// The cause of every generation is derived from the opening event and the predecessor, so the
    /// chain is a record of what happened rather than of what a caller said happened.
    /// </summary>
    [TestMethod]
    public void EveryGenerationCauseIsDerivedFromItsOpeningEvent()
    {
        var ledger = AbsenceFixtures.Ledger();
        Append(ledger, Present("run-1", 0), AbsenceAppendDisposition.PresenceBreakRecorded);
        ledger.TryTransitionComparisonPolicy(
            AbsenceFixtures.Policy('c', AbsenceComparisonPolicyMember.SelectionQueryDigest),
            "evt-c",
            out _);

        CollectionAssert.AreEqual(
            new[]
            {
                AbsenceHistoryGenerationCause.InitialTracking,
                AbsenceHistoryGenerationCause.PresenceBreak,
                AbsenceHistoryGenerationCause.ComparisonPolicyChanged,
            },
            ledger.Generations.Select(static generation => generation.Cause).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                AbsenceGenerationOpeningEventKind.TrackingStarted,
                AbsenceGenerationOpeningEventKind.TrustworthyPositiveObservation,
                AbsenceGenerationOpeningEventKind.ComparisonPolicyTransition,
            },
            ledger.Generations.Select(static generation => generation.OpeningEventKind).ToArray());

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            ledger.Generations.Select(static generation => generation.Ordinal).ToArray());

        Assert.AreEqual(
            3,
            ledger.Generations.Select(static generation => generation.Id.Value)
                .Distinct(StringComparer.Ordinal).Count(),
            "two generations of one subject share an identity");
    }
}
