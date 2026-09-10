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

    private static LuxembourgDraftBatchCitation Batch(IReadOnlyList<string> drafts, long rows) =>
        new(Ref("batch"), LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts), drafts.Count, rows);

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
            drafts, Asked, rows, batch, inventory, out var refusal, out _);
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
        Assert.AreEqual(155, coverage.DerivedAbsences.Count, "250 - 95, by distinct pair set.");
        Assert.AreEqual(250, coverage.CoveredPairCount, "50 drafts x 5 properties.");
        Assert.AreEqual(
            258, coverage.PublisherRowCount + coverage.DerivedAbsences.Count,
            "103 publisher rows plus 155 derived absences.");

        Assert.AreNotEqual(
            250 - coverage.PublisherRowCount, coverage.DerivedAbsences.Count,
            "row arithmetic would have given 147 and under-counted by the eight multi-value rows.");
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
        Assert.AreEqual(4, coverage.DerivedAbsences.Count, "the other four properties are absent.");
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
                var values = coverage.ValuesFor(draft, predicate);
                var absence = coverage.DerivedAbsenceFor(draft, predicate);
                Assert.IsTrue(
                    values.Count > 0 ^ absence is not null,
                    $"{draft} {predicate} must be present or absent, exactly one.");
            }
        }

        Assert.AreEqual(
            coverage.CoveredPairCount,
            coverage.PresentPairCount + coverage.DerivedAbsences.Count);
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

        Assert.HasCount(12, coverage.DerivedAbsences, "three drafts x four unanswered properties.");
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
            8, coverage.DerivedAbsences.Count,
            "two confirmed drafts x four unanswered properties, and NOTHING for the silent one.");
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
            coverage.PresentPairCount + coverage.DerivedAbsences.Count
                + (coverage.DraftsOfUnconfirmedClass.Count * Asked.Length));
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
            Ref("batch"), LuxembourgDraftPropertyCoverage.SelectionDigestFor(Drafts(4)), 3, 0);

        Assert.AreEqual(
            LuxembourgDraftPropertyCoverageRefusal.RequestedBatchNotRetained,
            RefusalOf(drafts, [], wrong, Inventory()));
    }
}
