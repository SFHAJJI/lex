using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Whether a set of delivered batches is the class, which no single batch can know.
/// </summary>
/// <remarks>
/// Each batch proves its own enumeration and completes its own matrix. A sweep that dropped one
/// would publish every absence it did derive and stay silent about the subjects it never asked
/// about, and every batch in it would still be perfectly honest. This is the door that refuses.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestBatchCoverTests
{
    private const string ReferralDate =
        LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;

    private const string RdfType = LuxembourgOpinionRequestGraphDiscoveryPlan.RdfTypePredicateIri;

    private const string RequestClass =
        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri;

    /// <summary>Every batch delivered, and the sweep reads as one class.</summary>
    [TestMethod]
    public void ACompleteSweepCoversEverySubjectExactlyOnce()
    {
        var (population, inventory, batches) = Swept(Capacity + 1);

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, batches, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestBatchCoverRefusal.None, refusal, detail);
        Assert.IsNotNull(cover);
        Assert.AreEqual(2, cover.Batches.Count, "fifty-one subjects is two batches.");
        Assert.AreEqual(population.Count, cover.SubjectCount);
        Assert.AreEqual(
            population.Count * LuxembourgOpinionRequestCoverage.AskedPredicates.Count,
            cover.CoveredPairCount,
            "every subject's every asked property is accounted for somewhere in the sweep.");
        Assert.AreEqual(
            population.Count, cover.DerivedAbsenceCount,
            "every subject was typed by the publisher and held no referral date.");
        Assert.AreEqual(0, cover.UnresolvedGapCount);
        Assert.AreEqual(0, cover.UnconfirmedRoleCount);
        Assert.AreEqual(population.Count, cover.RetainedRowCount, "one type row each.");
    }

    /// <summary>
    /// A sweep missing a batch is refused, and the missing batch is named.
    /// </summary>
    /// <remarks>
    /// THE CASE THIS DOOR EXISTS FOR. Both surviving batches are honest and complete; what is wrong
    /// is what is not there. A refusal that said only how many were missing would leave whoever
    /// reads it unable to run the ones that are.
    /// </remarks>
    [TestMethod]
    public void ABatchOmittedFromTheSweepIsRefusedAndNamed()
    {
        var (population, inventory, batches) = Swept(Capacity + 1);
        var expectedKeys = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory)
            .Select(static value => value.PartitionKey)
            .ToArray();

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, [batches[0]], out var refusal, out var detail);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgOpinionRequestBatchCoverRefusal.BatchOmittedFromSweep, refusal);
        StringAssert.Contains(
            detail ?? string.Empty, expectedKeys[1],
            "the batch that was never delivered is named, not counted.");
    }

    /// <summary>A batch delivered twice is not two batches.</summary>
    [TestMethod]
    public void ABatchDeliveredTwiceIsRefused()
    {
        var (population, inventory, batches) = Swept(Capacity + 1);

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, [batches[0], batches[0], batches[1]], out var refusal, out _);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgOpinionRequestBatchCoverRefusal.BatchDeliveredTwice, refusal);
    }

    /// <summary>
    /// Batches derived against two proven populations are not a cover of either.
    /// </summary>
    /// <remarks>
    /// Both inventories are real and both batches are honest; they are simply not about the same
    /// class. An absence derived against one population says nothing about the other.
    /// </remarks>
    [TestMethod]
    public void BatchesCitingTwoInventoriesAreRefused()
    {
        var (population, inventory, batches) = Swept(2);
        var (_, _, elsewhere) = Swept(2, offset: 500);

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, [batches[0], elsewhere[0]], out var refusal, out _);

        Assert.IsNull(cover);
        Assert.AreEqual(
            LuxembourgOpinionRequestBatchCoverRefusal.BatchesSpanMoreThanOneInventory, refusal);
    }

    /// <summary>
    /// The expected batches come from the inventory, so a population it never proved cannot be swept.
    /// </summary>
    /// <remarks>
    /// THE DIRECTION IS THE WHOLE MECHANISM. If the expected set were collected from the deliveries,
    /// it would agree with itself whatever arrived. It is derived through the assignment door, which
    /// refuses a population this citation does not digest - so a caller cannot widen or narrow the
    /// class a cover is measured against.
    /// </remarks>
    [TestMethod]
    public void TheExpectedBatchesComeFromTheInventoryAndNotFromTheDeliveries()
    {
        var (population, inventory, batches) = Swept(2);

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestBatchCover.TryCreate(
                [.. population, Request(900)], inventory, batches, out _, out _),
            "a population the citation does not digest is not this inventory's.");
    }

    /// <summary>
    /// A cover reads in the inventory's own assignment order, not in arrival order.
    /// </summary>
    [TestMethod]
    public void ACoverIsOrderedByTheInventoryRatherThanByArrival()
    {
        var (population, inventory, batches) = Swept(Capacity + 1);
        var expectedKeys = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory)
            .Select(static value => value.PartitionKey)
            .ToArray();

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, [batches[1], batches[0]], out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestBatchCoverRefusal.None, refusal, detail);
        Assert.IsNotNull(cover);
        CollectionAssert.AreEqual(
            expectedKeys,
            cover.Batches.Select(static value => value.Batch.PartitionKey).ToArray(),
            "the sweep reads in the order it was meant to run.");
    }

    /// <summary>
    /// A sweep whose subjects the publisher never typed derives no absence and hides nothing.
    /// </summary>
    /// <remarks>
    /// The aggregate has to carry the gaps, not average them away: a cover reporting only its
    /// absence count would read as a clean sweep over subjects whose role was never established.
    /// </remarks>
    [TestMethod]
    public void ASweepOverUntypedSubjectsReportsGapsRatherThanAbsences()
    {
        var (population, inventory, batches) = Swept(2, typed: false);

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, batches, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestBatchCoverRefusal.None, refusal, detail);
        Assert.IsNotNull(cover);
        Assert.AreEqual(0, cover.DerivedAbsenceCount, "no role, no readable silence.");
        Assert.AreEqual(population.Count, cover.UnresolvedGapCount);
        Assert.AreEqual(population.Count, cover.UnconfirmedRoleCount);
        Assert.AreEqual(
            population.Count * LuxembourgOpinionRequestCoverage.AskedPredicates.Count,
            cover.CoveredPairCount,
            "and every pair is still accounted for, as a gap.");
    }

    private const int Capacity = LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity;

    /// <summary>
    /// One proven inventory and a completed matrix for each of its batches.
    /// </summary>
    /// <remarks>
    /// Every batch is delivered honestly: a type row per subject unless <paramref name="typed"/>
    /// says otherwise, and no referral date, so a complete sweep is all absences and a complete
    /// untyped sweep is all gaps.
    /// </remarks>
    private static (IReadOnlyList<string> Population,
        LuxembourgOpinionRequestInventoryCitation Inventory,
        LuxembourgOpinionRequestCoverage[] Batches) Swept(
        int subjects,
        bool typed = true,
        int offset = 0)
    {
        var population = Enumerable.Range(offset, subjects).Select(Request)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var inventory = Inventory(population);

        var batches = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory)
            .Select(assignment =>
            {
                var rows = typed
                    ? assignment.Requests
                        .Select(request => (request, RdfType, (string?)RequestClass, true))
                        .ToArray()
                    : [];
                var (proof, delivered) = AbsenceFixtures.OpinionRequestGraphRows(
                    assignment.PartitionKey, rows);
                var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
                    proof, delivered, assignment, out var refusal, out var detail);
                Assert.IsNotNull(coverage, $"{refusal}: {detail}");
                return coverage;
            })
            .ToArray();

        return (population, inventory, batches);
    }

    private static LuxembourgOpinionRequestInventoryCitation Inventory(IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
