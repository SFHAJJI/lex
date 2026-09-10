using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The completed matrix: every requested pair represented exactly once, present or derived-absent.
/// </summary>
/// <remarks>
/// These are the guards the owner ruling names, asserted where they are enforced. The measured
/// shape they are calibrated against is the retained fifty-draft delivery under #417 - 103 rows
/// over 95 distinct pairs, eight of the rows being one draft's nine transposition targets.
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftPropertyCoverageTests
{
    private const string Draft = "http://data.legilux.public.lu/eli/dl/pl/2000/";
    private static readonly string[] Asked = [.. LuxembourgDraftGraphDiscoveryPlan.AskedAbout];

    private static SourceArtifactRef Ref(string seed) => new(
        "urn:uuid:" + Guid.NewGuid().ToString("D"),
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))));

    private static IReadOnlyList<string> Drafts(int count) =>
        LuxembourgDraftGraphDiscoveryPlan.RequestedPartitionMembers(
            Enumerable.Range(0, count).Select(index => Draft + index.ToString("D3")).ToArray());

    private static LuxembourgInitialDraftInventoryCitation Inventory() =>
        new("legilux-initial-draft-inventory", Ref("inventory"), "7753-subjects");

    private const string ObservedAt = "2026-09-10T13:50:31.0000000Z";

    private static LuxembourgDraftBatchCitation Batch(IReadOnlyList<string> drafts, long rows) =>
        new(Ref("batch"), LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count,
            rows, LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts), ObservedAt);

    private static LuxembourgDraftPropertyRecordView Row(string draft, string predicate, string value) =>
        new(draft, predicate, value, "iri");

    private static LuxembourgDraftPropertyCoverage Complete(
        IReadOnlyList<string> drafts,
        IReadOnlyList<LuxembourgDraftPropertyRecordView> rows,
        LuxembourgDraftBatchCitation? batch = null,
        LuxembourgInitialDraftInventoryCitation? inventory = null)
    {
        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            drafts, Asked, rows,
            batch ?? Batch(drafts, rows.Count),
            inventory ?? Inventory(),
            0,
            LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft,
            out var refusal, out var detail);
        Assert.IsNotNull(coverage, $"{refusal}: {detail}");
        return coverage;
    }

    private static LuxembourgDraftPropertyCoverageRefusal RefusalOf(
        IReadOnlyList<string> drafts,
        IReadOnlyList<LuxembourgDraftPropertyRecordView> rows,
        LuxembourgDraftBatchCitation? batch,
        LuxembourgInitialDraftInventoryCitation? inventory)
    {
        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            drafts, Asked, rows, batch, inventory, 0,
            LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft,
            out var refusal, out _);
        Assert.IsNull(coverage, "a refused completion mints nothing.");
        return refusal;
    }

    /// <summary>
    /// THE ARITHMETIC THE RULING CORRECTS: coverage counts distinct pairs, never rows.
    /// </summary>
    /// <remarks>
    /// Calibrated on the retained delivery. Fifty drafts and five properties are 250 pairs; the
    /// publisher delivered 103 rows covering 95 of them, because one draft transposes nine
    /// directives and those nine rows are ONE pair. So 155 pairs are absent and 258 records exist.
    /// <para>
    /// Deriving absences as 250 - 103 would give 147 and leave eight pairs represented by nothing.
    /// The test asserts the right number AND that the wrong one is different, so an implementation
    /// that regressed to row arithmetic cannot pass by coincidence.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void CoverageIsCountedInDistinctPairsAndNeverInRows()
    {
        var drafts = Drafts(50);
        var rows = new List<LuxembourgDraftPropertyRecordView>();

        // THE MEASURED DISTRIBUTION, not an invented one. From the retained delivery:
        //   statusDraft               50 rows / 50 pairs
        //   hasResultingLegalResource 42 rows / 42 pairs
        //   draftTransposes           11 rows /  3 pairs  (one draft carries nine values)
        //                            103 rows / 95 pairs
        foreach (var draft in drafts)
        {
            rows.Add(Row(draft, LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri, "urn:status:" + draft));
        }

        foreach (var draft in drafts.Take(42))
        {
            rows.Add(Row(
                draft, LuxembourgDraftGraphDiscoveryPlan.ResultingLegalResourcePredicateIri,
                "urn:act:" + draft));
        }

        var transposes = LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri;
        rows.Add(Row(drafts[0], transposes, "urn:directive:a"));
        rows.Add(Row(drafts[1], transposes, "urn:directive:b"));
        for (var index = 0; index < 9; index++)
        {
            rows.Add(Row(drafts[2], transposes, "urn:directive:" + index));
        }

        var coverage = Complete(drafts, rows);

        Assert.AreEqual(103, coverage.PublisherRowCount, "rows the publisher delivered.");
        Assert.AreEqual(95, coverage.PresentPairCount, "distinct pairs those rows cover.");
        Assert.AreEqual(250, coverage.CoveredPairCount, "50 drafts x 5 properties.");

        // referralDate is declared on OpinionRequest, so its fifty pairs are unresolved gaps rather
        // than absences. An earlier canary derived all fifty as absences and every one was false.
        Assert.AreEqual(50, coverage.UnresolvedGaps.Count, "one per draft, for referralDate.");
        Assert.AreEqual(
            105, coverage.DerivedAbsences.Count, "250 - 95 present - 50 unresolved, by pair set.");

        Assert.AreEqual(
            coverage.CoveredPairCount,
            coverage.PresentPairCount + coverage.DerivedAbsences.Count + coverage.UnresolvedGaps.Count,
            "every asked pair is present, absent or unresolved - exactly one of the three.");

        Assert.AreNotEqual(
            250 - coverage.PublisherRowCount, coverage.DerivedAbsences.Count,
            "row arithmetic would have under-counted by the eight multi-value rows.");
    }

    /// <summary>Every value of a multi-valued property is kept, and it is still one pair.</summary>
    /// <remarks>
    /// Measured, not invented: pl/2000/119 transposes nine directives, and it is the entire reason
    /// the delivered row count and the covered pair count differ. A design that collapsed values to
    /// force one row per pair would destroy exactly this draft.
    /// </remarks>
    [TestMethod]
    public void AMultiValuedPairKeepsEveryValueAndStillCountsAsOnePair()
    {
        var drafts = Drafts(1);
        var transposes = LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri;
        var rows = Enumerable.Range(0, 9)
            .Select(index => Row(drafts[0], transposes, "urn:directive:" + index))
            .ToArray();

        var coverage = Complete(drafts, rows);

        Assert.AreEqual(9, coverage.PublisherRowCount);
        Assert.AreEqual(1, coverage.PresentPairCount);
        Assert.HasCount(9, coverage.ValuesFor(drafts[0], transposes), "every value is retained.");
        Assert.AreEqual(3, coverage.DerivedAbsences.Count, "three of the other four are absent.");
        Assert.AreEqual(1, coverage.UnresolvedGaps.Count, "and referralDate is unresolved, not absent.");
    }

    /// <summary>Every requested pair is represented exactly once, and never twice.</summary>
    [TestMethod]
    public void EveryRequestedPairIsRepresentedExactlyOnce()
    {
        var drafts = Drafts(7);
        var rows = drafts
            .Select(draft => Row(draft, Asked[0], "urn:status:" + draft))
            .ToArray();

        var coverage = Complete(drafts, rows);

        foreach (var draft in drafts)
        {
            foreach (var predicate in Asked)
            {
                if (LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft
                        .ContainsKey(predicate))
                {
                    Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                        () => coverage.ValuesFor(draft, predicate),
                        "an unresolved pair answers neither way.");
                    continue;
                }

                var values = coverage.ValuesFor(draft, predicate);
                var absence = coverage.DerivedAbsenceFor(draft, predicate);
                Assert.IsTrue(
                    values.Count > 0 ^ absence is not null,
                    $"{draft} {predicate} must be present or absent, exactly one.");
            }
        }

        Assert.AreEqual(
            coverage.CoveredPairCount,
            coverage.PresentPairCount + coverage.DerivedAbsences.Count + coverage.UnresolvedGaps.Count);
    }

    /// <summary>No absence may be derived from an enumeration nobody proved.</summary>
    /// <remarks>
    /// The first guard, and structurally the most important: the batch citation is minted from the
    /// enumeration proof, so a caller holding none has nothing to pass. An absence derived without
    /// one would assert emptiness from a run that may have been truncated or refused.
    /// </remarks>
    [TestMethod]
    public void NoAbsenceCanBeDerivedWithoutTheEnumerationThatProvesIt()
    {
        var drafts = Drafts(2);
        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.MatrixCompletionOverUnprovenEnumeration,
            RefusalOf(drafts, [], null, Inventory()));
    }

    /// <summary>An absence names a corpus, so the inventory must be cited.</summary>
    [TestMethod]
    public void NoAbsenceCanBeDerivedWithoutTheInventoryItPartitions()
    {
        var drafts = Drafts(2);
        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.InventoryEvidenceNotSupplied,
            RefusalOf(drafts, [], Batch(drafts, 0), null));
    }

    /// <summary>Every derived absence carries the batch and inventory that make it readable.</summary>
    [TestMethod]
    public void EveryDerivedAbsenceCitesItsBatchAndItsInventory()
    {
        var drafts = Drafts(3);
        var rows = drafts.Select(draft => Row(draft, Asked[0], "urn:status:" + draft)).ToArray();
        var batch = Batch(drafts, rows.Length);
        var inventory = Inventory();

        var coverage = Complete(drafts, rows, batch, inventory);

        Assert.HasCount(
            9, coverage.DerivedAbsences, "three drafts x three absent properties (referralDate is a gap).");
        Assert.HasCount(3, coverage.UnresolvedGaps);
        foreach (var absence in coverage.DerivedAbsences)
        {
            Assert.AreSame(batch, absence.Batch, "the exact batch enumeration, not a copy of its shape.");
            Assert.AreSame(inventory, absence.Inventory);
            Assert.AreEqual(
                LuxembourgDraftPropertyAbsenceReason.EnumeratedAndNotHeld, absence.Reason,
                "an absence with no stated reason is not readable as a fact.");
            Assert.AreEqual(
                LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), absence.Batch.SelectionDigest,
                "the digest proves which pairs were asked about.");
        }
    }

    /// <summary>A delivered draft outside the requested batch refuses the whole matrix.</summary>
    /// <remarks>
    /// Publisher drift is recorded or refused, never converted into absence. A subject we did not
    /// ask about arriving in the answer means the batch is not what it says, and completing a
    /// matrix over it would derive 250 absences from a delivery about something else.
    /// </remarks>
    [TestMethod]
    public void ADeliveredDraftOutsideTheRequestedBatchRefuses()
    {
        var drafts = Drafts(3);
        var stranger = Row("http://data.legilux.public.lu/eli/dl/pl/1999/999", Asked[0], "urn:x");

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.DeliveredDraftNotRequested,
            RefusalOf(drafts, [stranger], Batch(drafts, 1), Inventory()));
    }

    /// <summary>Every present row is consumed exactly once, counted against the proof's own total.</summary>
    [TestMethod]
    public void APresentRowMustBeConsumedExactlyOnce()
    {
        var drafts = Drafts(2);
        var rows = new[] { Row(drafts[0], Asked[0], "urn:a") };

        // The citation says two rows were delivered; only one was decoded.
        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.PresentRowNotConsumedExactlyOnce,
            RefusalOf(drafts, rows, Batch(drafts, 2), Inventory()));
    }

    /// <summary>
    /// A pair outside the matrix throws rather than answering with an empty list.
    /// </summary>
    /// <remarks>
    /// The false-absence guard in its sharpest form. Returning empty for an unasked pair would let
    /// a caller read "the publisher holds nothing here" out of a question this run never put - the
    /// exact confusion S2-A03 forbids, arriving through an accessor rather than through a row.
    /// </remarks>
    [TestMethod]
    public void APairOutsideTheMatrixThrowsRatherThanAnsweringEmpty()
    {
        var drafts = Drafts(2);
        var coverage = Complete(
            drafts, drafts.Select(draft => Row(draft, Asked[0], "urn:status:" + draft)).ToArray());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => coverage.ValuesFor("http://data.legilux.public.lu/eli/dl/pl/1888/1", Asked[0]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => coverage.ValuesFor(coverage.RequestedDrafts[0], "http://example.invalid/never-asked"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => coverage.DerivedAbsenceFor(coverage.RequestedDrafts[0], "http://example.invalid/never-asked"));
    }

    /// <summary>
    /// A draft that delivered nothing is recorded, never turned into five absences.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUBLISHER DRIFT MUST NOT BECOME ABSENCE, and this is the route it would take. The query joins
    /// the class triple before the value triple, so a subject that has left <c>InitialDraft</c>
    /// delivers zero rows - indistinguishable here from a subject still in the class holding none of
    /// the five properties. Deriving absences for it would assert five facts about a subject the run
    /// never confirmed it was looking at.
    /// </para>
    /// <para>
    /// So its pairs are represented by <c>DraftsOfUnconfirmedClass</c> instead, and reading either a
    /// value or an absence for it throws rather than answering empty. A draft that delivered even
    /// one row is confirmed, because the class triple was satisfied for it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ADraftThatDeliveredNothingIsRecordedRatherThanTurnedIntoAbsences()
    {
        var drafts = Drafts(3);

        // Two drafts answer; the third is silent.
        var rows = drafts.Take(2)
            .Select(draft => Row(draft, Asked[0], "urn:status:" + draft))
            .ToArray();

        var coverage = Complete(drafts, rows);

        CollectionAssert.AreEqual(
            new[] { drafts[2] }, coverage.DraftsOfUnconfirmedClass.ToArray(),
            "the silent draft is named, not silently absorbed.");
        Assert.AreEqual(
            6, coverage.DerivedAbsences.Count,
            "two confirmed drafts x three absent properties, and NOTHING for the silent one.");
        Assert.AreEqual(2, coverage.UnresolvedGaps.Count, "one per CONFIRMED draft only.");
        Assert.IsFalse(
            coverage.DerivedAbsences.Any(value =>
                string.Equals(value.DraftIri, drafts[2], StringComparison.Ordinal)),
            "no absence may be derived for a draft whose class this delivery cannot confirm.");

        foreach (var predicate in Asked)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => coverage.ValuesFor(drafts[2], predicate),
                "reading a value for it must not answer empty.");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => coverage.DerivedAbsenceFor(drafts[2], predicate),
                "and neither must reading an absence.");
        }

        // Every pair is still accounted for: 2 present + 8 absences + 5 unconfirmed = 15.
        Assert.AreEqual(15, coverage.CoveredPairCount);
        Assert.AreEqual(
            coverage.CoveredPairCount,
            coverage.PresentPairCount + coverage.DerivedAbsences.Count + coverage.UnresolvedGaps.Count
                + (coverage.DraftsOfUnconfirmedClass.Count * Asked.Length));
    }

    /// <summary>
    /// A predicate declared on another class is an unresolved gap, never a derived absence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DELIVERY CAN ONLY EVIDENCE THE ABSENCE OF SOMETHING IT COULD HAVE CARRIED.
    /// <c>referralDate</c> is declared on <c>jolux:OpinionRequest</c>, not on the draft, so a
    /// draft-property acquisition was never able to answer it either way. Its silence is not
    /// evidence.
    /// </para>
    /// <para>
    /// MEASURED BEFORE IT WAS RULED, and by two independent routes: a broad acquisition over ten
    /// proven drafts returned seventeen distinct predicates with this among none of them, and an
    /// earlier canary derived fifty of these as absences - every one false, and false differently
    /// from the parliamentDraftUrl fifty, which the question dropped rather than never asked.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void APredicateDeclaredOnAnotherClassIsAnUnresolvedGapNotAnAbsence()
    {
        var drafts = Drafts(2);
        var referral = LuxembourgDraftGraphDiscoveryPlan.ReferralDatePredicateIri;
        var rows = drafts.Select(draft => Row(draft, Asked[0], "urn:status:" + draft)).ToArray();

        var coverage = Complete(drafts, rows);

        Assert.IsFalse(
            coverage.DerivedAbsences.Any(value =>
                string.Equals(value.PredicateIri, referral, StringComparison.Ordinal)),
            "no absence may be derived for a property this delivery could not have carried.");

        Assert.HasCount(2, coverage.UnresolvedGaps);
        foreach (var gap in coverage.UnresolvedGaps)
        {
            Assert.AreEqual(referral, gap.PredicateIri);
            Assert.AreEqual(
                LuxembourgDraftPropertyGapReason.DeclaredOnAnotherClass, gap.Reason);
            Assert.AreEqual(
                LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri, gap.DeclaredOnClassIri,
                "the gap names where an answer would have to come from.");
            CollectionAssert.Contains(drafts.ToArray(), gap.DraftIri);
        }

        // IT IS ASKED ABOUT AND STILL NOT ADMISSIBLE FROM A DRAFT TRIPLE, and I had that wrong: I
        // wrote that a direct triple would be an E8 fact. It would be ontology drift. Admitting one
        // because its IRI sits in the accepted vocabulary would widen this family's authority to a
        // class it never proved the subject holds, which S2-A05 requires to fail closed into typed
        // evidence instead.
        CollectionAssert.Contains(
            LuxembourgDraftGraphDiscoveryPlan.AskedAbout.ToArray(), referral);
        CollectionAssert.DoesNotContain(
            LuxembourgDraftGraphDiscoveryPlan.DirectlyAdmissiblePredicates.ToArray(), referral);
    }

    /// <summary>
    /// A proof whose own partition key names other drafts cannot evidence this batch's absences.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS CHECK WAS VACUOUS UNTIL THE KEY DIGESTED THE BATCH. Every batch of this family bound the
    /// same constant member key, so the delivery's key and a locally recomputed one agreed for every
    /// batch in the sweep no matter which drafts it named - a guard comparing a constant with
    /// itself, which reads as protection and proves nothing. I left it out rather than write it
    /// that way, and it is here now because the key finally varies.
    /// </para>
    /// <para>
    /// The key travels out with the bound request and back through the retained delivery, so this
    /// compares what was actually sent against what this run believes it asked.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AProofWhosePartitionKeyNamesOtherDraftsCannotEvidenceThisBatch()
    {
        var drafts = Drafts(3);
        var someoneElses = new LuxembourgDraftBatchCitation(
            Ref("batch"), LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count, 0,
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(Drafts(4)), ObservedAt);

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.AbsenceEvidenceNotFromThisRun,
            RefusalOf(drafts, [], someoneElses, Inventory()));

        // And the keys really do differ, so the test above is not passing by accident.
        Assert.AreNotEqual(
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts),
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(Drafts(4)));
    }

    /// <summary>An absence that cannot be dated cannot be derived.</summary>
    /// <remarks>
    /// An absence is true of a corpus AT AN INSTANT, and publisher drift after the run silently
    /// turns it false. Without the observation time nothing records when it was true, so the record
    /// reads as timeless - which is a stronger claim than the run can support.
    /// </remarks>
    [TestMethod]
    public void AnAbsenceThatCannotBeDatedIsNotDerived()
    {
        var drafts = Drafts(3);
        var undated = new LuxembourgDraftBatchCitation(
            Ref("batch"), LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count, 0,
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts), "   ");

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.AbsenceEvidenceNotFromThisRun,
            RefusalOf(drafts, [], undated, Inventory()));
    }

    /// <summary>Every derived absence carries the instant its batch was observed.</summary>
    [TestMethod]
    public void EveryDerivedAbsenceCarriesItsObservationInstant()
    {
        var drafts = Drafts(2);
        var rows = drafts.Select(draft => Row(draft, Asked[0], "urn:status:" + draft)).ToArray();

        var coverage = Complete(drafts, rows);

        Assert.IsNotEmpty(coverage.DerivedAbsences);
        foreach (var absence in coverage.DerivedAbsences)
        {
            Assert.AreEqual(ObservedAt, absence.Batch.ObservedAt);
        }
    }

    /// <summary>The selection digest changes when the batch changes.</summary>
    /// <remarks>
    /// Without this the citation on an absence would be the same for every batch in the sweep, and
    /// "derived from this batch" would be a claim no reader could check - a guard comparing a
    /// constant with itself.
    /// </remarks>
    [TestMethod]
    public void TheSelectionDigestDistinguishesOneBatchFromAnother()
    {
        var first = LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(50));
        var second = LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(49));

        Assert.AreNotEqual(first, second);
        Assert.AreEqual(
            first, LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(50)),
            "and it is stable for one batch.");
    }

    /// <summary>A citation naming a different selection than the batch in hand refuses.</summary>
    [TestMethod]
    public void ACitationThatDoesNotMatchTheRequestedBatchRefuses()
    {
        var drafts = Drafts(3);
        var wrong = new LuxembourgDraftBatchCitation(
            Ref("batch"), LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(4)), 3, 0,
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts), ObservedAt);

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.RequestedBatchNotRetained,
            RefusalOf(drafts, [], wrong, Inventory()));
    }
}
