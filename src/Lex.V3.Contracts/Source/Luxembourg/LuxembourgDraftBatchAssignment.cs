namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// One batch of a proven inventory, issued by that inventory and not assemblable beside it.
/// </summary>
/// <remarks>
/// <para>
/// THIS EXISTS BECAUSE CLOSING ONE DOOR TWICE WAS NOT ENOUGH. The batch members and the inventory
/// citation used to travel as two independent values into the coverage factory, so a caller could
/// take an inventory genuinely proven for one draft, ask for a completely different draft, and
/// receive derived absences and unresolved gaps for a subject the proven population never contained.
/// Making the run request's constructor private closed that route through the producer and left this
/// one open, because the coverage factory is public and took the pair directly.
/// </para>
/// <para>
/// The pair is now one value, and it is VERIFIED rather than trusted. The inventory citation already
/// digests its own addressable population, so <see cref="Over"/> can check that the population it is
/// handed is the one the citation names - not by comparing a value with itself, but by recomputing
/// the digest the enumerating run minted. A caller holding a real citation and a different draft list
/// cannot produce an assignment at all.
/// </para>
/// <para>
/// That is what makes membership structural rather than checked downstream: there is no conclusion
/// to read until an assignment exists, and no assignment unless the members are the inventory's own.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftBatchAssignment
{
    private LuxembourgDraftBatchAssignment(
        IReadOnlyList<string> drafts,
        LuxembourgInitialDraftInventoryCitation inventory,
        int ordinal,
        string partitionKey)
    {
        Drafts = Array.AsReadOnly(drafts.ToArray());
        Inventory = inventory;
        Ordinal = ordinal;
        PartitionKey = partitionKey;
    }

    /// <summary>This batch's members, taken from the inventory's own population.</summary>
    public IReadOnlyList<string> Drafts { get; }

    /// <summary>The proven inventory that issued this batch.</summary>
    public LuxembourgInitialDraftInventoryCitation Inventory { get; }

    /// <summary>Which of the inventory's batches this is.</summary>
    public int Ordinal { get; }

    /// <summary>This batch's own partition key, digesting its members.</summary>
    public string PartitionKey { get; }

    /// <summary>
    /// Every batch of one proven inventory, or nothing at all.
    /// </summary>
    /// <param name="population">
    /// The inventory's addressable population, in its own order. It must be the population the
    /// citation digests; anything else is refused rather than batched.
    /// </param>
    /// <param name="inventory">That population's citation, minted by the run that enumerated it.</param>
    public static IReadOnlyList<LuxembourgDraftBatchAssignment> Over(
        IReadOnlyList<string> population,
        LuxembourgInitialDraftInventoryCitation inventory)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(inventory);

        // THE CHECK THAT MAKES THIS WORTH HAVING. The citation was minted over this population by
        // the enumerating run, so recomputing its digest asks whether these really are that run's
        // subjects. A caller pairing a real citation with a different list fails here.
        if (population.Count != inventory.SubjectCount ||
            !string.Equals(
                LuxembourgDraftGraphDiscoveryPlan.SelectionDigestFor(population),
                inventory.SelectionDigest,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "These drafts are not the population this inventory citation names, so no batch of "
                    + "them is a partition of it.",
                nameof(population));
        }

        var assignments = new List<LuxembourgDraftBatchAssignment>();
        for (var index = 0; index < population.Count; index += LuxembourgDraftGraphDiscoveryPlan.BatchCapacity)
        {
            var drafts = Array.AsReadOnly(population
                .Skip(index)
                .Take(LuxembourgDraftGraphDiscoveryPlan.BatchCapacity)
                .ToArray());

            assignments.Add(new LuxembourgDraftBatchAssignment(
                drafts,
                inventory,
                assignments.Count,
                LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor(drafts)));
        }

        // RECOMPUTED, NOT TRUSTED. The chunking is four lines and obviously right, which is exactly
        // the kind of code that is quietly wrong after a later edit.
        var flattened = assignments.SelectMany(static value => value.Drafts).ToArray();
        if (flattened.Length != population.Count ||
            !flattened.SequenceEqual(population, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The batches do not reassemble the inventory population exactly once, so the sweep "
                    + "they describe is not the class this inventory proved.");
        }

        return Array.AsReadOnly(assignments.ToArray());
    }
}
