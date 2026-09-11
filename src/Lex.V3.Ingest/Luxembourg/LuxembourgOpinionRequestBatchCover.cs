using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why a set of delivered batches is not a cover of the proven inventory.</summary>
/// <remarks>
/// <para>
/// THREE MEMBERS, WHERE THE DRAFT FAMILY'S COVER HAS SIX. The other three are not omissions and not
/// oversights; none of them is representable here, and a refusal nothing can reach reads as defence
/// while being an untested claim.
/// </para>
/// <para>
/// <c>inventory_not_proven</c>: the draft cover takes a result that may carry no citation. A
/// <see cref="LuxembourgOpinionRequestInventoryCitation"/> cannot exist without the proof it was
/// minted over, so there is no unproven inventory to pass.
/// </para>
/// <para>
/// <c>batch_outside_the_inventory_cover</c>: in the draft family a permutation of the proven
/// population is accepted as that population and then falls on different batch boundaries, so a
/// legitimate delivery can carry keys the inventory never assigned. That hazard was removed here
/// when <see cref="LuxembourgOpinionRequestBatchAssignment.Over"/> began canonicalising once before
/// chunking: one citation now has exactly one partition, and every coverage citing this inventory
/// carries a key from it. It would become reachable again the moment batching stopped being
/// deterministic in the citation alone.
/// </para>
/// <para>
/// <c>covered_pairs_do_not_equal_the_inventory_sum</c>: reachable in the draft family because its
/// coverage takes the asked predicates as a PARAMETER, so a batch can be completed over fewer of
/// them. This family's coverage takes its requests from the assignment and its predicates from the
/// plan, so the pair total is the inventory's by construction. It would become reachable again if
/// either were ever accepted from a caller - which is the defect #556 was repaired for.
/// </para>
/// </remarks>
public enum LuxembourgOpinionRequestBatchCoverRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// An expected batch has no delivered coverage, so part of the class was never swept.
    /// </summary>
    /// <remarks>
    /// THE REFUSAL THAT MAKES A PARTIAL SWEEP UNREADABLE AS A WHOLE ONE. Without it, a sweep that
    /// dropped a batch would report every absence it did derive and say nothing about the subjects
    /// it never asked about - the false absence S2-A03 forbids, arriving at the largest scale this
    /// family can produce it.
    /// </remarks>
    [JsonStringEnumMemberName("batch_omitted_from_sweep")]
    BatchOmittedFromSweep = 1,

    /// <summary>Two delivered coverages name the same batch.</summary>
    [JsonStringEnumMemberName("batch_delivered_twice")]
    BatchDeliveredTwice = 2,

    /// <summary>The delivered coverages do not all cite one inventory.</summary>
    [JsonStringEnumMemberName("batches_span_more_than_one_inventory")]
    BatchesSpanMoreThanOneInventory = 3,
}

/// <summary>
/// One proven OpinionRequest inventory, swept by delivered batches that cover it exactly once.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS WHERE A PARTIAL SWEEP STOPS BEING READABLE AS A WHOLE ONE. Each batch proves its own
/// enumeration and completes its own matrix; none of them can say whether the batches together are
/// the class.
/// </para>
/// <para>
/// The expected batch keys come from the INVENTORY, through
/// <see cref="LuxembourgOpinionRequestBatchAssignment.Over"/>, and never from the batches that came
/// back. That direction is the whole mechanism: a set collected from the deliveries would agree with
/// itself no matter which batches were missing.
/// </para>
/// <para>
/// WHAT IT DOES NOT CHECK, AND WHY. The draft family's cover recomputes subjects x predicates and
/// compares it against the batches' own totals. Here that comparison is a value against a copy of
/// itself: a coverage takes its requests from the assignment this inventory issued and its
/// predicates from the plan, so the pair total cannot disagree. It is left out rather than written
/// as a check no test can fail; the enum's remarks name what would make it real again.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestBatchCover
{
    private LuxembourgOpinionRequestBatchCover(
        LuxembourgOpinionRequestInventoryCitation inventory,
        IReadOnlyList<LuxembourgOpinionRequestCoverage> batches)
    {
        Inventory = inventory;
        Batches = batches;
    }

    /// <summary>The proven inventory these batches cover.</summary>
    public LuxembourgOpinionRequestInventoryCitation Inventory { get; }

    /// <summary>Every delivered batch, in the inventory's own assignment order.</summary>
    public IReadOnlyList<LuxembourgOpinionRequestCoverage> Batches { get; }

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

    /// <summary>Subjects the publisher's own delivery did not type, across the sweep.</summary>
    public int UnconfirmedRoleCount =>
        Batches.Sum(static value => value.RequestsOfUnconfirmedRole.Count);

    /// <summary>Delivered rows this family asserts nothing from, across the sweep.</summary>
    public int RetainedRowCount => Batches.Sum(static value => value.RetainedRows.Count);

    /// <summary>
    /// Reconciles delivered batches against the inventory, or refuses without minting a cover.
    /// </summary>
    /// <param name="population">
    /// The inventory's addressable population. It is not trusted: <see
    /// cref="LuxembourgOpinionRequestBatchAssignment.Over"/> refuses any list the citation does not
    /// digest, so the expected batches below are the ones this inventory issues or none at all.
    /// </param>
    /// <param name="inventory">That population's citation, minted by the run that enumerated it.</param>
    /// <param name="deliveredBatches">The matrices this sweep actually completed.</param>
    public static LuxembourgOpinionRequestBatchCover? TryCreate(
        IReadOnlyList<string> population,
        LuxembourgOpinionRequestInventoryCitation inventory,
        IReadOnlyList<LuxembourgOpinionRequestCoverage> deliveredBatches,
        out LuxembourgOpinionRequestBatchCoverRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(deliveredBatches);
        detail = null;

        // FROM THE INVENTORY, NOT FROM THE DELIVERIES. A set collected from what came back would
        // agree with itself no matter which batches were missing. Over refuses a population this
        // citation does not digest, so this list is the inventory's own or the call does not return.
        var expected = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory)
            .Select(static value => value.PartitionKey)
            .ToArray();
        var byKey = new Dictionary<string, LuxembourgOpinionRequestCoverage>(StringComparer.Ordinal);
        foreach (var batch in deliveredBatches)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // ONE INVENTORY. Absences derived against two proven populations are not a cover of
            // either, and a citation that varies means these deliveries were never about one class.
            if (batch.Inventory != inventory)
            {
                refusal = LuxembourgOpinionRequestBatchCoverRefusal.BatchesSpanMoreThanOneInventory;
                detail = "A delivered batch cites a different inventory from the one being covered.";
                return null;
            }

            if (!byKey.TryAdd(batch.Batch.PartitionKey, batch))
            {
                refusal = LuxembourgOpinionRequestBatchCoverRefusal.BatchDeliveredTwice;
                detail = $"Two delivered coverages name {batch.Batch.PartitionKey}.";
                return null;
            }
        }

        // NAMED, NOT COUNTED. A refusal that says only how many batches are missing leaves whoever
        // reads it unable to run the ones that are.
        var omitted = expected.Where(value => !byKey.ContainsKey(value)).ToArray();
        if (omitted.Length is not 0)
        {
            refusal = LuxembourgOpinionRequestBatchCoverRefusal.BatchOmittedFromSweep;
            detail = $"{omitted.Length} of {expected.Length} batches were never delivered: "
                + string.Join(", ", omitted.Take(8));
            return null;
        }

        // Ordered by the inventory's own assignment, so a cover reads in the order the sweep was
        // meant to run rather than the order the deliveries happened to arrive.
        var ordered = expected.Select(value => byKey[value]).ToArray();

        refusal = LuxembourgOpinionRequestBatchCoverRefusal.None;
        return new LuxembourgOpinionRequestBatchCover(inventory, Array.AsReadOnly(ordered));
    }

    /// <summary>A one-line measured summary, in the units each number is actually in.</summary>
    public string Describe() =>
        $"subjects={SubjectCount} batches={Batches.Count} covered_pairs={CoveredPairCount} "
        + $"present_pairs={PresentPairCount} derived_absences={DerivedAbsenceCount} "
        + $"unresolved_gaps={UnresolvedGapCount} unconfirmed_role={UnconfirmedRoleCount} "
        + $"retained_rows={RetainedRowCount}";
}
