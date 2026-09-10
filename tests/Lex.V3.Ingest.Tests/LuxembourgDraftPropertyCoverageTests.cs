using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

using Lex.V3.Tests.Contracts.Source.Absence;

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

    /// <summary>
    /// A REAL enumeration proof, because the citation doors now require one.
    /// </summary>
    /// <remarks>
    /// The run reference and the family key used to be handed to the producer as loose values, which
    /// is how a citation could state an identity instead of carrying one. Both now come off the
    /// proof, whose only door refuses anything but two independently agreeing, custody-verified
    /// passes. <c>AbsenceFixtures.Proof</c> is the same builder the contract tests use and is
    /// memoised, so this costs one assembly for the whole run rather than one per test.
    /// </remarks>
    private static AbsenceFamilyEnumerationProof InventoryProof =>
        AbsenceFixtures.Proof("legilux-initial-draft-inventory");
    private const string Draft = "http://data.legilux.public.lu/eli/dl/pl/2000/";
    private static readonly string[] Asked = [.. LuxembourgDraftGraphDiscoveryPlan.AskedAbout];

    /// <summary>A run reference that is a function of its seed, so one seed is one run.</summary>
    /// <remarks>
    /// It used to mint a fresh GUID per call, which meant <c>Inventory(drafts)</c> built a DIFFERENT
    /// citation each time it was called and equality against a retained one failed on the resource id
    /// alone. Nothing about the fixture wanted that: a test naming the same seed twice means the same
    /// run both times.
    /// </remarks>
    private static SourceArtifactRef Ref(string seed)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed));
        return new(
            "urn:uuid:" + new Guid(digest.AsSpan(0, 16)).ToString("D"),
            Convert.ToHexStringLower(digest));
    }

    private static IReadOnlyList<string> Drafts(int count) =>
        LuxembourgDraftGraphDiscoveryPlan.RequestedPartitionMembers(
            Enumerable.Range(0, count).Select(index => Draft + index.ToString("D3")).ToArray());

    /// <summary>A citation over exactly these drafts, as the enumerating run would mint it.</summary>
    /// <remarks>
    /// The digest is over the population, so a citation can no longer be paired with a different
    /// draft list: <see cref="LuxembourgDraftBatchAssignment.Over"/> recomputes it and refuses. A
    /// test that wants a mismatch has to build one deliberately, which is what
    /// <c>AnAssignmentCannotBeBuiltForDraftsTheCitationDoesNotName</c> does.
    /// </remarks>
    private static LuxembourgInitialDraftInventoryCitation Inventory(IReadOnlyList<string> drafts) =>
        LuxembourgInitialDraftInventoryCitation.MintedOver(
            InventoryProof,
            drafts,
            "2026-09-10T07:29:37.8950843Z");

    private static LuxembourgDraftBatchAssignment Assignment(IReadOnlyList<string> drafts) =>
        LuxembourgDraftBatchAssignment.Over(drafts, Inventory(drafts))[0];

    private const string ObservedAt = "2026-09-10T13:50:31.0000000Z";

    private static LuxembourgDraftBatchCitation Batch(IReadOnlyList<string> drafts, long rows) =>
        LuxembourgDraftBatchCitation.ForDelivery(
            InventoryProof, LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count,
            rows, LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts), ObservedAt);

    private static LuxembourgDraftPropertyRecordView Row(string draft, string predicate, string value) =>
        new(draft, predicate, value, "iri");

    private static LuxembourgDraftPropertyCoverage Complete(
        IReadOnlyList<string> drafts,
        IReadOnlyList<LuxembourgDraftPropertyRecordView> rows,
        LuxembourgDraftBatchCitation? batch = null)
    {
        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            Assignment(drafts), Asked, rows,
            batch ?? Batch(drafts, rows.Count),
            0,
            LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft,
            out var refusal, out var detail);
        Assert.IsNotNull(coverage, $"{refusal}: {detail}");
        return coverage;
    }

    private static LuxembourgDraftPropertyCoverageRefusal RefusalOf(
        IReadOnlyList<string> drafts,
        IReadOnlyList<LuxembourgDraftPropertyRecordView> rows,
        LuxembourgDraftBatchCitation? batch)
    {
        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            Assignment(drafts), Asked, rows, batch, 0,
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
            RefusalOf(drafts, [], null));
    }

    /// <summary>
    /// A completed coverage does not change when the caller edits the lists it was built from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REPRODUCED BY THE REVIEWER FROM OUTSIDE THIS ASSEMBLY. The private constructor stored the
    /// asked predicates and the delivered rows BY REFERENCE, while <c>CoveredPairCount</c> and
    /// <c>PublisherRowCount</c> read those live collections and the terminal cover sums those
    /// properties. Removing one asked predicate and clearing the delivered rows after
    /// <c>TryComplete</c> had already succeeded moved a minted coverage from five covered pairs to
    /// four and from one publisher row to none - after every completeness check had run and passed.
    /// </para>
    /// <para>
    /// The positional index is the sharper half. Values are looked up by INDEX into the delivered
    /// rows, so a caller removing a row does not merely change a total: it repoints every lookup
    /// past it, and a pair answers with another pair's value. That is a false publisher assertion,
    /// not an arithmetic slip.
    /// </para>
    /// <para>
    /// Asserted unconditionally on both counts and on every published collection, because a
    /// conditional probe would pass against a defect that reports itself read-only and still writes
    /// through the indexer.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ACompletedCoverageDoesNotMoveWhenItsInputsAreEdited()
    {
        var drafts = Drafts(3);
        var asked = new List<string>(Asked);
        var rows = drafts.Select(draft => Row(draft, Asked[0], "urn:status:" + draft)).ToList();

        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            Assignment(drafts), asked, rows, Batch(drafts, rows.Count), 0,
            LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft,
            out var refusal, out var detail);
        Assert.IsNotNull(coverage, $"{refusal}: {detail}");

        var coveredPairs = coverage.CoveredPairCount;
        var publisherRows = coverage.PublisherRowCount;
        var presentPairs = coverage.PresentPairCount;
        var firstValue = coverage.ValuesFor(drafts[0], Asked[0])[0];

        // The caller still owns these. It may do whatever it likes with them.
        asked.RemoveAt(asked.Count - 1);
        rows.Clear();

        Assert.AreEqual(coveredPairs, coverage.CoveredPairCount, "covered pairs are of the completed batch.");
        Assert.AreEqual(publisherRows, coverage.PublisherRowCount, "so is the delivered row count.");
        Assert.AreEqual(presentPairs, coverage.PresentPairCount, "and the present pair count.");
        Assert.AreEqual(
            firstValue, coverage.ValuesFor(drafts[0], Asked[0])[0],
            "and a value lookup still answers with its own pair's row.");

        // Nor through the collections the coverage itself publishes.
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<string>)coverage.AskedPredicates)[0] = "http://example.invalid/forged");
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<string>)coverage.RequestedDrafts)[0] = "http://example.invalid/forged");
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((ICollection<LuxembourgDraftPropertyObservedAbsence>)coverage.DerivedAbsences).Clear());
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((ICollection<LuxembourgDraftPropertyUnresolvedGap>)coverage.UnresolvedGaps).Clear());
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((ICollection<string>)coverage.DraftsOfUnconfirmedClass).Clear());
    }

    /// <summary>
    /// An assignment's members cannot be edited after its key was computed over them.
    /// </summary>
    /// <remarks>
    /// The digest check and the partition key both run at construction, so a writable member list
    /// would let a caller pass the check, take the key, and then swap a draft - leaving a batch whose
    /// own key describes a population it no longer holds. The same applies to the returned batch
    /// list, which is what the cover's expected key set is derived from.
    /// </remarks>
    [TestMethod]
    public void AnAssignmentsMembersAndBatchListCannotBeMutated()
    {
        var proven = Drafts(3);
        var assignments = LuxembourgDraftBatchAssignment.Over(proven, Inventory(proven));

        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<string>)assignments[0].Drafts)[0] = "http://example.invalid/forged",
            "a member cannot be swapped after the key was taken over it.");
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<LuxembourgDraftBatchAssignment>)assignments).Add(assignments[0]),
            "and the sweep cannot gain a batch the inventory never issued.");

        Assert.AreEqual(
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(proven), assignments[0].PartitionKey,
            "the key still names the members it was computed over.");
    }

    /// <summary>
    /// An assignment cannot be built for drafts the inventory citation does not name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BYPASS THIS REPLACED. The coverage factory used to take the draft list and the inventory
    /// citation as two independent values, so a caller holding an inventory genuinely proven for one
    /// draft could ask for a different one and receive derived absences and gaps for a subject the
    /// proven population never contained - no run request, no terminal cover, no guard reached.
    /// </para>
    /// <para>
    /// It is not refused now, it is unrepresentable: there is no conclusion without an assignment,
    /// and no assignment unless the members are the ones the citation digests. The check recomputes
    /// the digest the enumerating run minted rather than comparing a value with itself.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnAssignmentCannotBeBuiltForDraftsTheCitationDoesNotName()
    {
        var proven = Drafts(3);
        var somethingElse = new[] { "http://data.legilux.public.lu/eli/dl/pl/1999/999" };

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgDraftBatchAssignment.Over(somethingElse, Inventory(proven)),
            "a citation for one population cannot issue a batch of another.");

        // Nor by matching only the count, which a length check alone would have accepted.
        var sameSizeDifferentDrafts = proven.Select(static value => value + "-elsewhere").ToArray();
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgDraftBatchAssignment.Over(sameSizeDifferentDrafts, Inventory(proven)),
            "three drafts are not these three drafts.");

        // THE CASE THE DIGEST ALONE CANNOT SEE, found by mutation: deleting the count check killed
        // no test. The digest canonicalises, which means it DEDUPLICATES, so a population carrying
        // a draft twice digests identically to the population carrying it once. Only the count
        // separates them, and without it a batch would be issued with a member repeated - a subject
        // counted twice in a sweep that is supposed to cover the class exactly once.
        var duplicated = proven.Concat(proven.Take(1)).ToArray();
        Assert.AreEqual(
            LuxembourgDraftGraphDiscoveryPlan.SelectionDigestFor(proven),
            LuxembourgDraftGraphDiscoveryPlan.SelectionDigestFor(duplicated),
            "the digests must actually agree, or this is testing something else.");
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgDraftBatchAssignment.Over(duplicated, Inventory(proven)),
            "a repeated member is not this population, however it digests.");

        // WHAT THIS DELIBERATELY DOES NOT REFUSE. The digest canonicalises, so a permutation of the
        // proven population is the same population and is accepted - correctly, because membership
        // is what this check is for and every member is still the inventory's own. A permutation
        // does change where the batch boundaries fall, and that is caught where it is actually
        // visible: the terminal cover, whose expected keys come from the inventory's own ordering.
        // See LuxembourgDraftGraphBatchCoverTests.ABatchOutsideTheInventoryIsRefused.
        var permuted = LuxembourgDraftBatchAssignment.Over(proven.Reverse().ToArray(), Inventory(proven));
        CollectionAssert.AreEquivalent(
            proven.ToArray(),
            permuted.SelectMany(static value => value.Drafts).ToArray(),
            "the same subjects, whatever the order they arrived in.");

        // And the honest pairing still works.
        var assignment = LuxembourgDraftBatchAssignment.Over(proven, Inventory(proven))[0];
        CollectionAssert.AreEqual(proven.ToArray(), assignment.Drafts.ToArray());
    }

    /// <summary>Every derived absence carries the batch and inventory that make it readable.</summary>
    [TestMethod]
    public void EveryDerivedAbsenceCitesItsBatchAndItsInventory()
    {
        var drafts = Drafts(3);
        var rows = drafts.Select(draft => Row(draft, Asked[0], "urn:status:" + draft)).ToArray();
        var batch = Batch(drafts, rows.Length);
        var coverage = Complete(drafts, rows, batch);

        Assert.HasCount(
            9, coverage.DerivedAbsences, "three drafts x three absent properties (referralDate is a gap).");
        Assert.HasCount(3, coverage.UnresolvedGaps);
        foreach (var absence in coverage.DerivedAbsences)
        {
            Assert.AreSame(batch, absence.Batch, "the exact batch enumeration, not a copy of its shape.");
            Assert.AreEqual(Inventory(drafts), absence.Inventory);
            Assert.AreEqual(
                LuxembourgDraftPropertyAbsenceReason.EnumeratedAndNotHeld, absence.Reason,
                "an absence with no stated reason is not readable as a fact.");
            // Deliberately NOT re-asserting equality here: the fixture built this citation's digest
            // with the same call on the same drafts, so comparing them passes for any implementation
            // of TryComplete, including one returning the empty string. That the digest distinguishes
            // one batch from another is proved where it can fail, in
            // TheSelectionDigestDistinguishesOneBatchFromAnother.
            Assert.AreNotEqual(
                LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(4)), absence.Batch.SelectionDigest,
                "a different selection would carry a different digest.");
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
            RefusalOf(drafts, [stranger], Batch(drafts, 1)));
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
            RefusalOf(drafts, rows, Batch(drafts, 2)));
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
        var someoneElses = LuxembourgDraftBatchCitation.ForDelivery(
            InventoryProof, LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count, 0,
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(Drafts(4)), ObservedAt);

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.AbsenceEvidenceNotFromThisRun,
            RefusalOf(drafts, [], someoneElses));

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
        var undated = LuxembourgDraftBatchCitation.ForDelivery(
            InventoryProof, LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count, 0,
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts), "   ");

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.AbsenceEvidenceNotFromThisRun,
            RefusalOf(drafts, [], undated));
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
        var wrong = LuxembourgDraftBatchCitation.ForDelivery(
            InventoryProof, LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(4)), 3, 0,
            LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts), ObservedAt);

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.RequestedBatchNotRetained,
            RefusalOf(drafts, [], wrong));
    }
}
