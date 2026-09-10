using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why a set of delivered batches is not a cover of the proven inventory.</summary>
public enum LuxembourgDraftGraphBatchCoverRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The inventory itself was not proven, so there is no population to cover.</summary>
    [JsonStringEnumMemberName("inventory_not_proven")]
    InventoryNotProven = 1,

    /// <summary>
    /// An expected batch has no delivered coverage, so part of the class was never swept.
    /// </summary>
    /// <remarks>
    /// The refusal that makes a partial sweep unreadable as a whole one. Without it, a sweep that
    /// dropped a batch would report every absence it did derive and say nothing about the subjects
    /// it never asked about - which is the false absence arriving at the largest possible scale.
    /// </remarks>
    [JsonStringEnumMemberName("batch_omitted_from_sweep")]
    BatchOmittedFromSweep = 2,

    /// <summary>Two delivered coverages name the same batch.</summary>
    [JsonStringEnumMemberName("batch_delivered_twice")]
    BatchDeliveredTwice = 3,

    /// <summary>A delivered coverage names a batch this inventory never assigned.</summary>
    /// <remarks>
    /// <para>
    /// NOT REDUNDANT WITH THE ONE-INVENTORY CHECK, and reachable with every citation matching. An
    /// assignment verifies its population against a canonical digest, so a permutation of the proven
    /// population is accepted as that population - rightly, since it is the same subjects. It does
    /// fall on different batch boundaries, so its batches carry keys this inventory never assigned
    /// while every member and every citation remains legitimate.
    /// </para>
    /// <para>
    /// MEASURED, not assumed: with this refusal deleted, such a delivery is neither counted nor
    /// refused. The reconciliation below selects the expected keys out of the delivered map, so an
    /// unexpected batch is silently DISCARDED, the pair total still balances, and a cover is minted
    /// reporting a clean sweep over work it threw away. The pair-count check does not catch it.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("batch_outside_the_inventory_cover")]
    BatchOutsideTheInventoryCover = 4,

    /// <summary>The delivered coverages do not all cite one inventory.</summary>
    [JsonStringEnumMemberName("batches_span_more_than_one_inventory")]
    BatchesSpanMoreThanOneInventory = 5,

    /// <summary>The pairs the batches account for are not the pairs the inventory implies.</summary>
    [JsonStringEnumMemberName("covered_pairs_do_not_equal_the_inventory_sum")]
    CoveredPairsDoNotEqualTheInventorySum = 6,
}

/// <summary>
/// One proven inventory, swept by delivered batches that cover it exactly once.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS WHERE A PARTIAL SWEEP STOPS BEING READABLE AS A WHOLE ONE. Each batch proves its own
/// enumeration and completes its own matrix; none of them can say whether the batches together are
/// the class. A run that dropped a batch would otherwise publish every absence it did derive and
/// stay silent about the subjects it never asked about, which is the false absence S2-A03 forbids
/// arriving at the largest possible scale.
/// </para>
/// <para>
/// The expected batch keys come from the INVENTORY, through
/// <see cref="LuxembourgDraftGraphBatchFactory"/>, and never from the batches that came back. That
/// direction is the whole mechanism: a set collected from the deliveries would agree with itself no
/// matter which batches were missing.
/// </para>
/// <para>
/// It also binds every batch to one inventory. Absences derived against two different proven
/// populations are not a cover of either, and a citation that varies across the batches means the
/// sweep is reconciling deliveries that were never about the same class.
/// </para>
/// </remarks>
public sealed class LuxembourgDraftGraphBatchCover
{
    private LuxembourgDraftGraphBatchCover(
        LuxembourgInitialDraftInventoryCitation inventory,
        IReadOnlyList<LuxembourgDraftPropertyCoverage> batches)
    {
        Inventory = inventory;
        Batches = batches;
    }

    /// <summary>The proven inventory these batches cover.</summary>
    public LuxembourgInitialDraftInventoryCitation Inventory { get; }

    /// <summary>Every delivered batch, in the inventory's own assignment order.</summary>
    public IReadOnlyList<LuxembourgDraftPropertyCoverage> Batches { get; }

    /// <summary>Subjects the inventory proved.</summary>
    public int SubjectCount => Inventory.SubjectCount;

    /// <summary>Pairs the sweep accounts for, which must be every subject's every property.</summary>
    public int CoveredPairCount => Batches.Sum(static value => value.CoveredPairCount);

    /// <summary>Pairs the publisher delivered a value for, across the sweep.</summary>
    public int PresentPairCount => Batches.Sum(static value => value.PresentPairCount);

    /// <summary>Pairs this code concluded the publisher holds nothing for, across the sweep.</summary>
    public int DerivedAbsenceCount => Batches.Sum(static value => value.DerivedAbsences.Count);

    /// <summary>Pairs this sweep can resolve neither way, across the sweep.</summary>
    public int UnresolvedGapCount => Batches.Sum(static value => value.UnresolvedGaps.Count);

    /// <summary>Subjects a batch could not confirm are still in the class, across the sweep.</summary>
    public int UnconfirmedDraftCount =>
        Batches.Sum(static value => value.DraftsOfUnconfirmedClass.Count);

    /// <summary>
    /// Reconciles delivered batches against the inventory, or refuses without minting a cover.
    /// </summary>
    public static LuxembourgDraftGraphBatchCover? TryCreate(
        LuxembourgInitialDraftInventoryResult inventory,
        IReadOnlyList<LuxembourgDraftPropertyCoverage> deliveredBatches,
        out LuxembourgDraftGraphBatchCoverRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(deliveredBatches);
        detail = null;

        if (!inventory.Delivered || inventory.Citation is not { } citation)
        {
            refusal = LuxembourgDraftGraphBatchCoverRefusal.InventoryNotProven;
            detail = "A cover is a claim about a proven population, and this inventory was refused.";
            return null;
        }

        // FROM THE INVENTORY, NOT FROM THE DELIVERIES. A set collected from what came back would
        // agree with itself no matter which batches were missing.
        var expected = LuxembourgDraftGraphBatchFactory.ExpectedPartitionKeys(inventory);
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);

        var byKey = new Dictionary<string, LuxembourgDraftPropertyCoverage>(StringComparer.Ordinal);
        foreach (var batch in deliveredBatches)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // ONE INVENTORY. Absences derived against two proven populations are not a cover of
            // either, and a citation that varies means these deliveries were never about one class.
            if (batch.Inventory != citation)
            {
                refusal = LuxembourgDraftGraphBatchCoverRefusal.BatchesSpanMoreThanOneInventory;
                detail = "A delivered batch cites a different inventory from the one being covered.";
                return null;
            }

            if (!expectedSet.Contains(batch.Batch.PartitionKey))
            {
                refusal = LuxembourgDraftGraphBatchCoverRefusal.BatchOutsideTheInventoryCover;
                detail = $"A delivered batch names {batch.Batch.PartitionKey}, which this inventory "
                    + "never assigned.";
                return null;
            }

            if (!byKey.TryAdd(batch.Batch.PartitionKey, batch))
            {
                refusal = LuxembourgDraftGraphBatchCoverRefusal.BatchDeliveredTwice;
                detail = $"Two delivered coverages name {batch.Batch.PartitionKey}.";
                return null;
            }
        }

        // NAMED, NOT COUNTED. A refusal that says only how many batches are missing leaves whoever
        // reads it unable to run the ones that are.
        var omitted = expected.Where(value => !byKey.ContainsKey(value)).ToArray();
        if (omitted.Length is not 0)
        {
            refusal = LuxembourgDraftGraphBatchCoverRefusal.BatchOmittedFromSweep;
            detail = $"{omitted.Length} of {expected.Count} batches were never delivered: "
                + string.Join(", ", omitted.Take(8));
            return null;
        }

        // Ordered by the inventory's own assignment, so a cover reads in the order the sweep was
        // meant to run rather than the order the deliveries happened to arrive.
        var ordered = expected.Select(value => byKey[value]).ToArray();

        // RECOMPUTED INDEPENDENTLY. Every batch checked its own matrix; this checks that the
        // matrices together are the inventory's, which no batch could know.
        var expectedPairs = citation.SubjectCount * LuxembourgDraftGraphDiscoveryPlan.AskedAbout.Count;
        var coveredPairs = ordered.Sum(static value => value.CoveredPairCount);
        if (coveredPairs != expectedPairs)
        {
            refusal = LuxembourgDraftGraphBatchCoverRefusal.CoveredPairsDoNotEqualTheInventorySum;
            detail = $"The inventory implies {expectedPairs} pairs and the batches account for "
                + $"{coveredPairs}.";
            return null;
        }

        refusal = LuxembourgDraftGraphBatchCoverRefusal.None;
        return new LuxembourgDraftGraphBatchCover(citation, Array.AsReadOnly(ordered));
    }

    /// <summary>A one-line measured summary, in the units each number is actually in.</summary>
    public string Describe() =>
        $"subjects={SubjectCount} batches={Batches.Count} covered_pairs={CoveredPairCount} "
        + $"present_pairs={PresentPairCount} derived_absences={DerivedAbsenceCount} "
        + $"unresolved_gaps={UnresolvedGapCount} unconfirmed_drafts={UnconfirmedDraftCount}";
}
