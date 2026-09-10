using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Guard 7: an omitted or duplicated batch prevents terminal completion.
/// </summary>
/// <remarks>
/// Each batch proves its own enumeration and completes its own matrix; none of them can say whether
/// the batches together are the class. These are the assertions that make a partial sweep unreadable
/// as a whole one.
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphBatchCoverTests
{
    private const string Prefix = "http://data.legilux.public.lu/eli/dl/pl/2000/";
    private const string ObservedAt = "2026-09-10T07:29:37.8950843Z";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:3f21c8a4-9d76-4b20-8e15-6c0a7d93b482", new string('a', 64));

    private static readonly RepeatedEnumerationInterpretationProfile Profile =
        LuxembourgInitialDraftInventoryDiscoveryPlan.Create().CreateDeliveryProfile();

    private static readonly string[] Asked = [.. LuxembourgDraftGraphDiscoveryPlan.AskedAbout];

    private static RepeatedEnumerationRow Subject(string iri)
    {
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            RepeatedEnumerationRdfTerm.Iri(iri),
            RepeatedEnumerationRdfTerm.Literal(LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", "http://www.w3.org/2001/XMLSchema#integer", null),
            RepeatedEnumerationRdfTerm.Literal(iri, null, null),
            RepeatedEnumerationRdfTerm.Literal(LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static LuxembourgInitialDraftInventoryResult Inventory(int subjects) =>
        LuxembourgInitialDraftInventoryProducer.DecodeRows(
            Enumerable.Range(0, subjects).Select(index => Subject(Prefix + index.ToString("D5"))).ToArray(),
            Profile, Evidence, "legilux-initial-draft-inventory", ObservedAt);

    /// <summary>One delivered batch: every draft answers statusDraft and nothing else.</summary>
    private static LuxembourgDraftPropertyCoverage Batch(
        IReadOnlyList<string> drafts,
        LuxembourgInitialDraftInventoryCitation inventory)
    {
        var rows = drafts
            .Select(draft => new LuxembourgDraftPropertyRecordView(
                draft, LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri, "urn:status:" + draft, "iri"))
            .ToArray();

        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            drafts,
            Asked,
            rows,
            new LuxembourgDraftBatchCitation(
                Evidence,
                LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts),
                drafts.Count,
                rows.Length,
                LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts),
                ObservedAt),
            inventory,
            0,
            LuxembourgDraftGraphDiscoveryPlan.PredicatesNotDeclaredOnTheDraft,
            out var refusal,
            out var detail);

        Assert.IsNotNull(coverage, $"{refusal}: {detail}");
        return coverage;
    }

    private static (LuxembourgInitialDraftInventoryResult Inventory,
        List<LuxembourgDraftPropertyCoverage> Batches) Swept(int subjects)
    {
        var inventory = Inventory(subjects);
        var batches = LuxembourgDraftGraphBatchFactory.AssignBatches(inventory)
            .Select(drafts => Batch(drafts, inventory.Citation!))
            .ToList();
        return (inventory, batches);
    }

    private static LuxembourgDraftGraphBatchCoverRefusal RefusalOf(
        LuxembourgInitialDraftInventoryResult inventory,
        IReadOnlyList<LuxembourgDraftPropertyCoverage> batches)
    {
        var cover = LuxembourgDraftGraphBatchCover.TryCreate(inventory, batches, out var refusal, out _);
        Assert.IsNull(cover, "a refused cover mints nothing.");
        return refusal;
    }

    /// <summary>A complete sweep covers every subject's every property exactly once.</summary>
    [TestMethod]
    public void ACompleteSweepCoversEverySubjectsEveryProperty()
    {
        var (inventory, batches) = Swept(127);

        var cover = LuxembourgDraftGraphBatchCover.TryCreate(
            inventory, batches, out var refusal, out var detail);

        Assert.IsNotNull(cover, $"{refusal}: {detail}");
        Assert.AreEqual(127, cover.SubjectCount);
        Assert.AreEqual(3, cover.Batches.Count);
        Assert.AreEqual(127 * Asked.Length, cover.CoveredPairCount);
        Assert.AreEqual(
            cover.CoveredPairCount,
            cover.PresentPairCount + cover.DerivedAbsenceCount + cover.UnresolvedGapCount
                + (cover.UnconfirmedDraftCount * Asked.Length),
            "every pair in the sweep is accounted for exactly once.");
    }

    /// <summary>An omitted batch prevents terminal completion, and is named.</summary>
    /// <remarks>
    /// The refusal guard 7 exists for. Without it a sweep that dropped a batch would publish every
    /// absence it did derive and stay silent about the subjects it never asked about.
    /// </remarks>
    [TestMethod]
    public void AnOmittedBatchPreventsTerminalCompletion()
    {
        var (inventory, batches) = Swept(127);
        batches.RemoveAt(1);

        var cover = LuxembourgDraftGraphBatchCover.TryCreate(
            inventory, batches, out var refusal, out var detail);

        Assert.IsNull(cover);
        Assert.AreEqual(LuxembourgDraftGraphBatchCoverRefusal.BatchOmittedFromSweep, refusal);
        StringAssert.Contains(
            detail!, LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(
                LuxembourgDraftGraphBatchFactory.AssignBatches(inventory)[1]),
            "the refusal names the batch that is missing, so it can be run.");
    }

    /// <summary>A duplicated batch prevents terminal completion.</summary>
    [TestMethod]
    public void ADuplicatedBatchPreventsTerminalCompletion()
    {
        var (inventory, batches) = Swept(127);
        batches.Add(batches[0]);

        Assert.AreEqual(
            LuxembourgDraftGraphBatchCoverRefusal.BatchDeliveredTwice,
            RefusalOf(inventory, batches));
    }

    /// <summary>A batch the inventory never assigned is not part of its cover.</summary>
    [TestMethod]
    public void ABatchOutsideTheInventoryIsRefused()
    {
        var (inventory, batches) = Swept(127);
        batches.Add(Batch([Prefix + "99999"], inventory.Citation!));

        Assert.AreEqual(
            LuxembourgDraftGraphBatchCoverRefusal.BatchOutsideTheInventoryCover,
            RefusalOf(inventory, batches));
    }

    /// <summary>Batches citing two different inventories are not a cover of either.</summary>
    [TestMethod]
    public void BatchesSpanningTwoInventoriesAreRefused()
    {
        var (inventory, batches) = Swept(127);
        var other = Inventory(126);

        batches[0] = Batch(
            LuxembourgDraftGraphBatchFactory.AssignBatches(inventory)[0], other.Citation!);

        Assert.AreEqual(
            LuxembourgDraftGraphBatchCoverRefusal.BatchesSpanMoreThanOneInventory,
            RefusalOf(inventory, batches));
    }

    /// <summary>A refused inventory has no population to cover.</summary>
    [TestMethod]
    public void ARefusedInventoryHasNoCover()
    {
        var refused = LuxembourgInitialDraftInventoryProducer.DecodeRows(
            [Subject(Prefix + "00001"), Subject(Prefix + "00001")],
            Profile, Evidence, "legilux-initial-draft-inventory", ObservedAt);

        Assert.AreEqual(
            LuxembourgDraftGraphBatchCoverRefusal.InventoryNotProven,
            RefusalOf(refused, []));
    }

    /// <summary>An empty delivery over a non-empty inventory is an omission, not a cover.</summary>
    /// <remarks>
    /// The shape that would otherwise read as "the sweep found nothing", which is the largest
    /// possible false absence: no batch ran, so nothing is known about any subject.
    /// </remarks>
    [TestMethod]
    public void DeliveringNoBatchesAtAllIsAnOmission()
    {
        Assert.AreEqual(
            LuxembourgDraftGraphBatchCoverRefusal.BatchOmittedFromSweep,
            RefusalOf(Inventory(127), []));
    }
}
