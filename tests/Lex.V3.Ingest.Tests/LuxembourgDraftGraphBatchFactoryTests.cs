using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The batches are a pure function of the proven inventory, and they reassemble it exactly once.
/// </summary>
[TestClass]
public sealed class LuxembourgDraftGraphBatchFactoryTests
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

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:3f21c8a4-9d76-4b20-8e15-6c0a7d93b482", new string('a', 64));

    private static readonly RepeatedEnumerationInterpretationProfile Profile =
        LuxembourgInitialDraftInventoryDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRow Subject(string iri)
    {
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            RepeatedEnumerationRdfTerm.Iri(iri),
            RepeatedEnumerationRdfTerm.Literal(
                LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
            RepeatedEnumerationRdfTerm.Literal(
                "1", "http://www.w3.org/2001/XMLSchema#integer", null),
            RepeatedEnumerationRdfTerm.Literal(iri, null, null),
            RepeatedEnumerationRdfTerm.Literal(
                LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static LuxembourgInitialDraftInventoryResult Inventory(int subjects) =>
        LuxembourgInitialDraftInventoryProducer.DecodeRows(
            Enumerable.Range(0, subjects).Select(index => Subject(Prefix + index.ToString("D5"))).ToArray(),
            Profile,
            InventoryProof,
            "2026-09-10T07:29:37.8950843Z");

    /// <summary>Every inventory member lands in exactly one batch, and nothing else does.</summary>
    /// <remarks>
    /// The property the whole terminal cover rests on. It is asserted over a population that does
    /// NOT divide by capacity, because an exact multiple is the one case where an off-by-one in the
    /// chunking is invisible.
    /// </remarks>
    [TestMethod]
    public void EveryInventoryMemberLandsInExactlyOneBatch()
    {
        var inventory = Inventory(127);
        var batches = LuxembourgDraftGraphBatchFactory.AssignBatches(inventory);

        Assert.HasCount(3, batches, "127 members at capacity 50 is two full batches and a short one.");
        Assert.HasCount(LuxembourgDraftGraphDiscoveryPlan.BatchCapacity, batches[0].Drafts);
        Assert.HasCount(LuxembourgDraftGraphDiscoveryPlan.BatchCapacity, batches[1].Drafts);
        Assert.HasCount(27, batches[2].Drafts, "the final batch is short, which is ordinary.");

        var flattened = batches.SelectMany(static value => value.Drafts).ToArray();
        CollectionAssert.AreEqual(
            inventory.AddressableInOrder().ToArray(), flattened,
            "the batches must reassemble the population, in order, exactly once.");
        Assert.AreEqual(
            flattened.Length, flattened.Distinct(StringComparer.Ordinal).Count(),
            "no member may appear in two batches.");
    }

    /// <summary>The same inventory assigns the same batches, so a sweep is reproducible.</summary>
    [TestMethod]
    public void TheSameInventoryAssignsTheSameBatches()
    {
        var first = LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(Inventory(127));
        var second = LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(Inventory(127));

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.AreEqual(
            first.Count, first.Distinct(StringComparer.Ordinal).Count(),
            "each batch must have its own key, or a duplicate cannot be told from a repeat.");
    }

    /// <summary>A different population assigns different keys.</summary>
    /// <remarks>
    /// Without this the expected key set would be the same for every inventory, and a cover
    /// reconciling against it would be comparing a constant with itself.
    /// </remarks>
    [TestMethod]
    public void ADifferentPopulationAssignsDifferentKeys()
    {
        var wide = LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(Inventory(127));
        var narrow = LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(Inventory(126));

        Assert.IsNotEmpty(wide, "a population of 127 assigns batches.");
        Assert.AreNotEqual(
            wide[^1], narrow[^1],
            "the last batch differs by a member, so its key must differ.");
    }

    /// <summary>A refused inventory assigns no batches at all.</summary>
    /// <remarks>
    /// It must THROW rather than return an empty list. An empty batch set reads downstream as a
    /// sweep that covered everything, which is the false absence this family exists to prevent
    /// arriving through the batching stage instead of through a row.
    /// </remarks>
    [TestMethod]
    public void ARefusedInventoryAssignsNoBatches()
    {
        var refused = LuxembourgInitialDraftInventoryProducer.DecodeRows(
            [Subject(Prefix + "00001"), Subject(Prefix + "00001")],
            Profile,
            InventoryProof,
            "2026-09-10T07:29:37.8950843Z");

        Assert.AreNotEqual(LuxembourgInitialDraftInventoryRefusal.None, refused.Refusal);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => LuxembourgDraftGraphBatchFactory.AssignBatches(refused));
    }

    /// <summary>An exactly-divisible population produces no empty trailing batch.</summary>
    [TestMethod]
    public void AnExactlyDivisiblePopulationProducesNoEmptyTrailingBatch()
    {
        var batches = LuxembourgDraftGraphBatchFactory.AssignBatches(
            Inventory(LuxembourgDraftGraphDiscoveryPlan.BatchCapacity * 2));

        Assert.HasCount(2, batches);
        Assert.IsFalse(batches.Any(static value => value.Drafts.Count is 0));
    }
}
