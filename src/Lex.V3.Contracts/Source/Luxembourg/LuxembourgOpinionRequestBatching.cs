using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The proven <c>OpinionRequest</c> population a graph run partitions.
/// </summary>
/// <remarks>
/// Minted by the run that enumerated it, never assembled beside it. A citation a caller could write
/// out of its own arguments would make every conclusion downstream circular: the batches, the
/// coverage and the reconciliation all rest on this naming a population some enumeration actually
/// delivered.
/// </remarks>
public sealed record LuxembourgOpinionRequestInventoryCitation
{
    private LuxembourgOpinionRequestInventoryCitation(
        string familyKey,
        SourceArtifactRef acquisitionRunRef,
        string selectionDigest,
        int subjectCount)
    {
        FamilyKey = familyKey;
        AcquisitionRunRef = acquisitionRunRef;
        SelectionDigest = selectionDigest;
        SubjectCount = subjectCount;
    }

    /// <summary>Which family's inventory this is.</summary>
    public string FamilyKey { get; }

    /// <summary>The run that enumerated the population.</summary>
    public SourceArtifactRef AcquisitionRunRef { get; }

    /// <summary>The digest of the population this inventory hands to batching.</summary>
    public string SelectionDigest { get; }

    /// <summary>
    /// That population's size, as observed.
    /// </summary>
    /// <remarks>
    /// A dated observation of this endpoint, never a constant. A governed diagnostic counted 7,751
    /// instances at 2026-09-11T11:57:05Z; what any given run enumerates is what that run saw.
    /// </remarks>
    public int SubjectCount { get; }

    // NO OBSERVED-AT HERE, DELIBERATELY. It was a caller-supplied string copied into a property
    // documented as when the enumeration was observed - a provenance claim the caller is not
    // entitled to make, since nothing on this door binds it to the run. The proof carries no
    // instant, so there is no evidence to derive one from. A reader needing the observation time
    // takes it from the run's own retained receipt through AcquisitionRunRef.

    /// <summary>
    /// Mints a citation over a population an inventory run actually enumerated.
    /// </summary>
    /// <remarks>
    /// The proof is required and then BOUND: to this family's partition and interpretation profile,
    /// to the rows it proves were delivered, and to the population those rows name. A proof of some
    /// other honest enumeration authorizes nothing here.
    /// </remarks>
    public static LuxembourgOpinionRequestInventoryCitation MintedOver(
        AbsenceFamilyEnumerationProof proof,
        IReadOnlyList<RepeatedEnumerationRow> deliveredRows,
        IReadOnlyList<string> addressablePopulation)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(deliveredRows);
        ArgumentNullException.ThrowIfNull(addressablePopulation);

        LuxembourgProvenRequestDelivery.RequireThisFamilysInventory(proof);
        LuxembourgProvenRequestDelivery.Bind(proof, deliveredRows, addressablePopulation);

        // A population repeating a subject digests identically to one naming it once, and only the
        // count separates them.
        var canonical = LuxembourgOpinionRequestGraphDiscoveryPlan
            .RequestedPartitionMembers(addressablePopulation);
        if (canonical.Count != addressablePopulation.Count)
        {
            throw new ArgumentException(
                "An inventory population names each subject once, so no citation is minted over a "
                    + "list that repeats one.",
                nameof(addressablePopulation));
        }

        return new LuxembourgOpinionRequestInventoryCitation(
            proof.FamilyKey,
            proof.AcquisitionRunRef,
            LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(addressablePopulation),
            addressablePopulation.Count);
    }
}

/// <summary>
/// Ties a citation to the enumeration its proof proves, not merely to some enumeration.
/// </summary>
/// <remarks>
/// <para>
/// REQUIRING A PROOF IS NOT BINDING TO ONE. A proof makes a citation impossible to write out of
/// nothing and nothing more: an honest proof of an unrelated family, passed beside a caller-chosen
/// population, would otherwise mint a citation for subjects no enumeration delivered.
/// </para>
/// <para>
/// So the rows are bound to the proof by canonical-key DIGEST rather than by type - rows are
/// publicly constructible, so substituting them would mean finding a key set hashing to a digest
/// fixed before the call - and the population is read from the proven KEY rather than from the
/// terms, because only the keys are covered by that digest.
/// </para>
/// </remarks>
internal static class LuxembourgProvenRequestDelivery
{
    private static readonly RepeatedEnumerationInterpretationProfile InventoryProfile =
        LuxembourgOpinionRequestInventoryDiscoveryPlan.Create().CreateDeliveryProfile();

    /// <summary>The proof must be of this family's inventory, read as this family defines it.</summary>
    internal static void RequireThisFamilysInventory(AbsenceFamilyEnumerationProof proof)
    {
        if (!string.Equals(
                proof.FamilyKey,
                LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKey,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "This proof is of " + proof.FamilyKey + ", not "
                    + LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKey
                    + ", so it proves nothing about this family's inventory.",
                nameof(proof));
        }

        try
        {
            RepeatedEnumerationInterpretationProfileIdentity.Validate(
                proof.InterpretationProfileRef, InventoryProfile);
        }
        catch (ArgumentException inner)
        {
            throw new ArgumentException(
                "This proof was read under another interpretation profile, so it does not evidence "
                    + "an enumeration of this family as this family is defined.",
                nameof(proof),
                inner);
        }
    }

    internal static void Bind(
        AbsenceFamilyEnumerationProof proof,
        IReadOnlyList<RepeatedEnumerationRow> deliveredRows,
        IReadOnlyList<string> namedSubjects)
    {
        if (deliveredRows.Count != proof.DeliveredRowCount)
        {
            throw new ArgumentException(
                "This delivery carries " + deliveredRows.Count + " rows and its own proof proves "
                    + proof.DeliveredRowCount + ", so they are not the same enumeration.",
                nameof(deliveredRows));
        }

        var digest = EnumerationDeliveryComparison.Digest(
            EnumerationDeliveryComparison.CanonicalKeySetSchema,
            deliveredRows.Select(static row => row.CanonicalKey));
        if (!string.Equals(digest, proof.CanonicalKeyDigest, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "These rows are not the ones this proof proves were delivered: their canonical keys "
                    + "digest to " + digest + " and the proof carries " + proof.CanonicalKeyDigest
                    + ". An honest proof of another enumeration authorizes nothing here.",
                nameof(deliveredRows));
        }

        // FROM BOTH PROVEN KEYS, AND AS AN EXACT SET. Only the canonical keys are covered by the
        // digest above; Terms is an independently settable list. This family keys on key_1 - the
        // subject, as STR(?request) - and key_2, the kind marker.
        //
        // Reading only key_1 and checking subset membership proved nothing about COMPLETENESS: a
        // caller naming three of four proven subjects minted a citation for a population no run
        // enumerated. There is no early return for an empty caller list either, because an empty
        // population beside a non-empty delivery is that same defect at its largest.
        var addressable = new HashSet<string>(StringComparer.Ordinal);
        var nonAddressable = new List<string>();
        foreach (var row in deliveredRows)
        {
            if (row.CanonicalKey.Count < 2 ||
                row.CanonicalKey[0].Value is not { } subject ||
                row.CanonicalKey[1].Value is not { } kind)
            {
                throw new ArgumentException(
                    "A delivered row carries no subject and kind key, so it names no population "
                        + "member.",
                    nameof(deliveredRows));
            }

            // A NON-ADDRESSABLE MEMBER STOPS THE INVENTORY rather than vanishing from it. The plan
            // reports a blank-node member instead of filtering it precisely so this door can
            // refuse: its label is scoped to the response that carried it, so no exact membership
            // list exists over an identity that does not survive its own delivery.
            if (string.Equals(
                    kind,
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind,
                    StringComparison.Ordinal))
            {
                nonAddressable.Add(subject);
                continue;
            }

            if (!string.Equals(
                    kind,
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A delivered row carries an unknown subject kind, so its addressability is "
                        + "undecided.",
                    nameof(deliveredRows));
            }

            addressable.Add(subject);
        }

        if (nonAddressable.Count is not 0)
        {
            throw new ArgumentException(
                "This delivery carries a non-addressable member, so no exact membership list exists "
                    + "over it: " + string.Join(", ", nonAddressable.Take(3))
                    + (nonAddressable.Count > 3 ? ", ..." : string.Empty),
                nameof(deliveredRows));
        }

        var named = new HashSet<string>(namedSubjects, StringComparer.Ordinal);
        if (named.Count != namedSubjects.Count ||
            !named.SetEquals(addressable))
        {
            var omitted = addressable.Except(named, StringComparer.Ordinal).Take(3).ToArray();
            var invented = named.Except(addressable, StringComparer.Ordinal).Take(3).ToArray();
            throw new ArgumentException(
                "This population is not the addressable inventory this proof proves: it names "
                    + namedSubjects.Count + " subject(s) against " + addressable.Count
                    + " delivered"
                    + (omitted.Length is 0 ? string.Empty : "; omitted " + string.Join(", ", omitted))
                    + (invented.Length is 0 ? string.Empty : "; invented " + string.Join(", ", invented))
                    + ".",
                nameof(namedSubjects));
        }
    }
}

/// <summary>
/// One batch of a proven inventory, issued by that inventory and not assemblable beside it.
/// </summary>
/// <remarks>
/// <para>
/// The members and the citation are ONE value, verified rather than trusted: the citation digests
/// its own population, so <see cref="Over"/> recomputes that digest against the list it is handed.
/// A caller holding a real citation and a different subject list cannot produce an assignment at
/// all, which is what makes membership structural instead of checked downstream.
/// </para>
/// <para>
/// EVERY MEMBER IN EXACTLY ONE BATCH, recomputed after chunking. The reviewer disposition requires
/// it, and four lines of obviously-right chunking is exactly the code that is quietly wrong after a
/// later edit.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestBatchAssignment
{
    private LuxembourgOpinionRequestBatchAssignment(
        IReadOnlyList<string> requests,
        LuxembourgOpinionRequestInventoryCitation inventory,
        int ordinal,
        string partitionKey)
    {
        Requests = Array.AsReadOnly(requests.ToArray());
        Inventory = inventory;
        Ordinal = ordinal;
        PartitionKey = partitionKey;
    }

    /// <summary>This batch's members, taken from the inventory's own population.</summary>
    public IReadOnlyList<string> Requests { get; }

    /// <summary>The proven inventory that issued this batch.</summary>
    public LuxembourgOpinionRequestInventoryCitation Inventory { get; }

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
    public static IReadOnlyList<LuxembourgOpinionRequestBatchAssignment> Over(
        IReadOnlyList<string> population,
        LuxembourgOpinionRequestInventoryCitation inventory)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(inventory);

        if (population.Count != inventory.SubjectCount ||
            !string.Equals(
                LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(population),
                inventory.SelectionDigest,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "These requests are not the population this inventory citation names, so no batch "
                    + "of them is a partition of it.",
                nameof(population));
        }

        // CANONICALIZED ONCE, BEFORE CHUNKING. The citation digests the SET, so it accepts any
        // permutation of its own population - and chunking the caller's order then gave one proven
        // inventory as many partitions as there are orderings, each with different batch partition
        // keys. One inventory has one partition.
        var ordered = LuxembourgOpinionRequestGraphDiscoveryPlan.RequestedPartitionMembers(population);

        var assignments = new List<LuxembourgOpinionRequestBatchAssignment>();
        for (var index = 0;
             index < ordered.Count;
             index += LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity)
        {
            var requests = Array.AsReadOnly(ordered
                .Skip(index)
                .Take(LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity)
                .ToArray());

            assignments.Add(new LuxembourgOpinionRequestBatchAssignment(
                requests,
                inventory,
                assignments.Count,
                LuxembourgOpinionRequestGraphDiscoveryPlan.PartitionKeyFor(requests)));
        }

        var flattened = assignments.SelectMany(static value => value.Requests).ToArray();
        if (flattened.Length != ordered.Count ||
            !flattened.SequenceEqual(ordered, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The batches do not reassemble the inventory population exactly once, so the sweep "
                    + "they describe is not the class this inventory proved.");
        }

        return Array.AsReadOnly(assignments.ToArray());
    }
}
