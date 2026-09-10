using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

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
            Profile, InventoryProof, ObservedAt);

    /// <summary>One delivered batch: every draft answers statusDraft and nothing else.</summary>
    private static LuxembourgDraftPropertyCoverage Batch(
        LuxembourgDraftBatchAssignment assignment,
        IReadOnlyList<string>? asked = null)
    {
        var drafts = assignment.Drafts;
        var rows = drafts
            .Select(draft => new LuxembourgDraftPropertyRecordView(
                draft, LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri, "urn:status:" + draft, "iri"))
            .ToArray();

        var coverage = LuxembourgDraftPropertyCoverage.TryComplete(
            assignment,
            asked ?? Asked,
            rows,
            LuxembourgDraftBatchCitation.ForDelivery(
                InventoryProof,
                LuxembourgDraftPropertyCoverage.SelectionDigestFor(drafts),
                drafts.Count,
                rows.Length,
                LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts),
                ObservedAt),
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
            .Select(static assignment => Batch(assignment))
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

            CollectionAssert.AreEqual(
                assigned[ordinal].Drafts.ToArray(), request.BatchDrafts.ToArray());
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
            Profile, InventoryProof, ObservedAt);

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
            detail!, LuxembourgDraftGraphBatchFactory.AssignBatches(inventory)[1].PartitionKey,
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

    /// <summary>
    /// A citation that came back equal is the same inventory, whatever instance carries it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE-INVENTORY CHECK MUST NOT BE REFERENCE EQUALITY. The citation is a record, and it
    /// crosses a receipt boundary: a coverage rebuilt from retained evidence carries a citation that
    /// is value-equal to the inventory's own and is necessarily a different instance. Comparing by
    /// reference would refuse every honest reconstructed delivery as spanning two inventories.
    /// </para>
    /// <para>
    /// That is the failure this codebase has already shipped once - a family that could refuse
    /// correctly and never succeed - so it is asserted in the direction that can catch it: the cover
    /// must be MINTED, not refused. Swapping the comparison for ReferenceEquals kills no other test.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AValueEqualCitationFromAnotherInstanceIsStillThisInventory()
    {
        var inventory = Inventory(127);
        var rebuilt = Inventory(127);

        Assert.AreEqual(
            inventory.Citation, rebuilt.Citation, "the same population mints an equal citation.");
        Assert.IsFalse(
            ReferenceEquals(inventory.Citation, rebuilt.Citation),
            "and a different instance carries it, or this proves nothing.");

        var batches = LuxembourgDraftGraphBatchFactory.AssignBatches(rebuilt)
            .Select(static assignment => Batch(assignment))
            .ToList();

        var cover = LuxembourgDraftGraphBatchCover.TryCreate(
            inventory, batches, out var refusal, out var detail);

        Assert.IsNotNull(cover, $"an equal citation is the same inventory: {refusal}: {detail}");
        Assert.AreEqual(127, cover.SubjectCount);
    }

    /// <summary>
    /// A cover reads in the inventory's assignment order, not the order deliveries arrived.
    /// </summary>
    /// <remarks>
    /// Both halves were promised in prose and asserted by nothing: the order, and that the
    /// reconciled list cannot be edited afterwards. Order matters because a receipt read in arrival
    /// order is not reproducible from the same inventory on a later run, and immutability matters
    /// because every total on the cover recomputes from this list.
    /// </remarks>
    [TestMethod]
    public void ACoverIsOrderedByTheInventoryAndCannotBeEditedAfterwards()
    {
        var (inventory, batches) = Swept(127);
        batches.Reverse();

        var cover = LuxembourgDraftGraphBatchCover.TryCreate(inventory, batches, out var refusal, out var detail);
        Assert.IsNotNull(cover, $"{refusal}: {detail}");

        CollectionAssert.AreEqual(
            LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(inventory).ToArray(),
            cover.Batches.Select(static value => value.Batch.PartitionKey).ToArray(),
            "deliveries arrived reversed and the cover still reads in the inventory's own order.");

        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<LuxembourgDraftPropertyCoverage>)cover.Batches).Clear(),
            "and a reconciled cover cannot be emptied by whoever holds it.");
    }

    /// <summary>
    /// A batch complete over a narrower question does not add up to the inventory's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CHECK THE COVER EXISTS FOR, and until this test it was asserted by nothing: every other
    /// case here returns at an earlier guard, and the complete sweep satisfies it. The comment
    /// calling it "recomputed independently" was an unverified promise.
    /// </para>
    /// <para>
    /// It is reachable because a batch is completed against the predicates it was ASKED, which
    /// TryComplete takes as a parameter, while the cover recomputes the total from the family's own
    /// AskedAbout. So a batch can be internally perfect - proven enumeration, matching key, one
    /// inventory, nothing omitted or duplicated - and still account for fewer pairs than the class
    /// requires. That is a partial sweep wearing a complete batch's receipt, which is the exact
    /// false absence at scale this guard is here to refuse.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABatchCompletedOverFewerPredicatesDoesNotCoverTheInventory()
    {
        var (inventory, batches) = Swept(127);
        var narrower = Asked[..^1];
        Assert.AreEqual(
            Asked[0], narrower[0],
            "the delivered rows' predicate must still be asked about, or this fails for that reason.");

        batches[2] = Batch(LuxembourgDraftGraphBatchFactory.AssignBatches(inventory)[2], narrower);

        Assert.AreEqual(
            LuxembourgDraftGraphBatchCoverRefusal.CoveredPairsDoNotEqualTheInventorySum,
            RefusalOf(inventory, batches));
    }

    /// <summary>A batch the inventory never assigned is not part of its cover.</summary>
    [TestMethod]
    public void ABatchOutsideTheInventoryIsRefused()
    {
        var (inventory, batches) = Swept(127);

        // THE ROUTE THAT ACTUALLY REACHES THIS REFUSAL, and the reason it is not redundant with the
        // one-inventory check beside it. An assignment verifies its population against a CANONICAL
        // digest, so a permutation of the proven population is accepted as that population - rightly,
        // because it is the same subjects, and every absence derived over it is about a proven one.
        // What it is not is the same PARTITIONING: chunked from a different starting point it falls
        // on different boundaries, so its batches carry keys this inventory never assigned. Every
        // member is legitimate, every citation matches, and the sweep is still not this inventory's
        // cover - which no per-batch check can see, because each such batch is internally perfect.
        var population = inventory.AddressableInOrder();
        var rotated = population.Skip(1).Concat(population.Take(1)).ToArray();
        var elsewhere = LuxembourgDraftBatchAssignment.Over(rotated, inventory.Citation!);

        CollectionAssert.DoesNotContain(
            LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(inventory).ToArray(),
            elsewhere[0].PartitionKey,
            "a rotated population must fall on boundaries this inventory never assigned, or the "
                + "refusal below is being reached by some other route and this proves nothing.");

        batches.Add(Batch(elsewhere[0]));

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

        batches[0] = Batch(LuxembourgDraftGraphBatchFactory.AssignBatches(other)[0]);

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
            Profile, InventoryProof, ObservedAt);

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
