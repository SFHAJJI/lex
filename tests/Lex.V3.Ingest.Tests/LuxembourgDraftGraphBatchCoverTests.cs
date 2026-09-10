using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
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

    /// <summary>
    /// A batch outside the inventory cannot be run at all, so it can mint no conclusions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PRODUCTION PATH, NOT THE COVER. The cover refuses a batch outside the inventory only if
    /// someone hands it the coverage - after the per-batch absences and gaps already exist and look
    /// authoritative. The run request used to take the draft list and the inventory citation as two
    /// independent values, so an inventory proven for one draft could be paired with another and the
    /// run would mint derived conclusions for a subject the proven population never contained.
    /// </para>
    /// <para>
    /// That is the false absence S2-A03 forbids, reached around the outside of every guard built to
    /// stop it, and I claimed the boundary was structural while the door stood open. It is
    /// structural now: the only way to name a batch is by ordinal into the batches the proven
    /// inventory itself assigns, so the members and the citation cannot disagree.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABatchOutsideTheInventoryCannotBeRunAtAll()
    {
        var inventory = Inventory(60);
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var source = LuxembourgAcquisitionTestFixture.BuildRendererSource(9701);

        // Every batch a request can name comes out of the inventory it cites.
        var assigned = LuxembourgDraftGraphBatchFactory.AssignBatches(inventory);
        for (var ordinal = 0; ordinal < assigned.Count; ordinal++)
        {
            var request = LuxembourgDraftGraphRunRequest.ForBatch(
                plan, inventory, ordinal, "urn:uuid:5c2f1a08-7d63-4e91-bf20-9a4c8e3d7016", source);

            CollectionAssert.AreEqual(assigned[ordinal].ToArray(), request.BatchDrafts.ToArray());
            Assert.AreEqual(inventory.Citation, request.Inventory, "one inventory, not two values.");
        }

        // And there is no ordinal that names drafts the inventory never contained.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LuxembourgDraftGraphRunRequest.ForBatch(
                plan, inventory, assigned.Count, "urn:uuid:5c2f1a08-7d63-4e91-bf20-9a4c8e3d7016", source));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LuxembourgDraftGraphRunRequest.ForBatch(
                plan, inventory, -1, "urn:uuid:5c2f1a08-7d63-4e91-bf20-9a4c8e3d7016", source));
    }

    /// <summary>A refused inventory cannot be swept, so it mints no conclusions either.</summary>
    [TestMethod]
    public void ARefusedInventoryCannotBeSwept()
    {
        var refused = LuxembourgInitialDraftInventoryProducer.DecodeRows(
            [Subject(Prefix + "00001"), Subject(Prefix + "00001")],
            Profile, Evidence, "legilux-initial-draft-inventory", ObservedAt);

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgDraftGraphRunRequest.ForBatch(
                LuxembourgDraftGraphDiscoveryPlan.Create(),
                refused,
                0,
                "urn:uuid:5c2f1a08-7d63-4e91-bf20-9a4c8e3d7016",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9702)));
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
