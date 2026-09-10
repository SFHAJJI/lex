using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// Assigns the proven inventory to draft-graph batches, in process, from the inventory itself.
/// </summary>
/// <remarks>
/// <para>
/// THE BATCHES ARE A PURE FUNCTION OF THE PROVEN INVENTORY, and that is what makes membership
/// structural instead of checked. A caller who could hand in its own batch list could name a draft
/// the inventory never contained, and every absence derived over that batch would then be asserted
/// about a subject outside the proven population. There is no door here that takes a caller's
/// batches: the only input is a delivered inventory result.
/// </para>
/// <para>
/// It also makes the expected batch key set computable without running anything, which is what the
/// terminal cover reconciles against. An omitted batch and a duplicated batch are both detectable
/// only because the expected set is derived from the inventory rather than from the batches that
/// happen to come back.
/// </para>
/// <para>
/// Ordinal order, because the batches must be reproducible from the same inventory on a later run.
/// <c>AddressableInOrder</c> is already ordered and deduplicated and refuses a refused inventory, so
/// this neither re-sorts nor re-deduplicates: two derivations of "the list to batch" would be two
/// chances to disagree about what completeness means.
/// </para>
/// </remarks>
public static class LuxembourgDraftGraphBatchFactory
{
    /// <summary>Every batch this inventory must be swept in, in a deterministic order.</summary>
    /// <remarks>
    /// The final batch is short whenever the population does not divide by capacity, and that is
    /// ordinary: the query pads its parameter block to capacity and the padding collapses inside the
    /// <c>SELECT DISTINCT</c>, so a short batch asks about exactly its own members.
    /// </remarks>
    public static IReadOnlyList<LuxembourgDraftBatchAssignment> AssignBatches(
        LuxembourgInitialDraftInventoryResult inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        // AddressableInOrder throws on a refused inventory rather than returning an empty list, so
        // a refused enumeration cannot silently become "a population with no batches" - which would
        // read downstream as a sweep that covered everything.
        var population = inventory.AddressableInOrder();

        if (inventory.Citation is not { } citation)
        {
            throw new InvalidOperationException(
                "A refused inventory issues no batches, because nothing proved its population.");
        }

        // The chunking, the reassembly re-check and the population/citation binding all live on the
        // assignment itself, so the only way to obtain one is the way that verifies it.
        return LuxembourgDraftBatchAssignment.Over(population, citation);
    }

    /// <summary>
    /// The partition key of every batch this inventory must be swept in.
    /// </summary>
    /// <remarks>
    /// Derived from the inventory, never collected from the batches that came back - which is the
    /// whole of how an omitted batch is detectable. Each key digests its own batch's members, so two
    /// batches are distinguishable in their own receipts and a duplicate collapses onto one key.
    /// </remarks>
    public static IReadOnlyList<string> ExpectedPartitionKeys(
        LuxembourgInitialDraftInventoryResult inventory) =>
        Array.AsReadOnly(AssignBatches(inventory)
            .Select(static value => value.PartitionKey)
            .ToArray());
}
