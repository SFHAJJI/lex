using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the bounded canary is allowed to do next, decided from the inventory it already has.
/// </summary>
/// <remarks>
/// <para>
/// A DECISION EXPRESSED AS DATA, NOT AS A BRANCH IN THE RUNNER.
/// <see cref="BatchOrdinalsToAcquire"/> is empty unless the inventory is a clean whole-class proof,
/// so a reader who does not trust the runner can force its acquisition loop to execute and it still
/// sends nothing. The alternative - an <c>if</c> inside the live test - is a promise that has to be
/// believed, and this is the one place where being believed is not good enough.
/// </para>
/// <para>
/// The same shape the relationship canary used for its cross-check gate, for the same reason.
/// </para>
/// </remarks>
/// <param name="Verdict">The canary's own word for what it found, for the retained report.</param>
/// <param name="BatchOrdinalsToAcquire">
/// Which batches the runner may acquire. At most one, and empty whenever the inventory did not
/// deliver a complete proven class within its share of the ceiling.
/// </param>
/// <param name="Reason">Why, in a sentence a reader of the evidence index can act on.</param>
public sealed record LuxembourgOpinionRequestCanaryDecision(
    string Verdict,
    IReadOnlyList<int> BatchOrdinalsToAcquire,
    string Reason);

/// <summary>
/// The frozen bounded canary: one inventory run and at most one inventory-issued batch, under one
/// shared ceiling.
/// </summary>
/// <remarks>
/// <para>
/// THE CEILING IS SHARED AND ENFORCED, not divided. A budget per run would be honoured twice and
/// bound nothing over the pair, which is the defect the wire budget was rebuilt to remove. The batch
/// therefore receives whatever the inventory did not spend, and stops when that runs out.
/// </para>
/// <para>
/// EXHAUSTION IS A FINDING, NEVER A CANARY. A run that reached its ceiling measured a magnitude
/// nobody had measured before and did not demonstrate the path; reporting it as a success would
/// convert the one honest outcome of an under-budgeted attempt into a false acceptance.
/// </para>
/// </remarks>
public static class LuxembourgOpinionRequestCanaryPlan
{
    /// <summary>The ceiling agreed for the bounded canary attempt, over both runs together.</summary>
    public const int WireCeiling = 250;

    /// <summary>The one batch a canary may acquire, if the inventory earns it.</summary>
    private const int FirstBatch = 0;

    /// <summary>
    /// Whether the inventory earned a batch.
    /// </summary>
    /// <remarks>
    /// FAIL-CLOSED ON EVERY SHAPE, including shapes that should not occur: a delivered inventory
    /// with no citation or no addressable member is not a thing this family can produce, and if it
    /// ever did, acquiring against it would be acquiring against something nobody proved. Listing
    /// those cases as refusals is cheaper than trusting that they cannot happen.
    /// </remarks>
    public static LuxembourgOpinionRequestCanaryDecision AfterInventory(
        LuxembourgOpinionRequestInventoryResult inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        // EXHAUSTION IS CHECKED FIRST, BEFORE REFUSAL AND BEFORE DELIVERY. A run that reached the
        // ceiling measured a magnitude whichever way it ended, and both other outcomes hide it: a
        // run refused BY the ceiling reads as an ordinary refusal, and a run that delivered its
        // rows on its last reservation reads as a clean inventory that simply has no batch. Clause
        // 6 wants the magnitude, so the magnitude wins the verdict.
        if (inventory.WireBudget.Exhausted)
        {
            return Stop(
                "BudgetExhaustedDuringInventory",
                $"the inventory reached the whole ceiling of {inventory.WireBudget.Limit} wire "
                    + "requests, so no batch can be acquired. This is a magnitude finding: the "
                    + "class is larger than this ceiling was set for.");
        }

        if (!inventory.Delivered)
        {
            return Stop(
                "InventoryRefused",
                $"the inventory refused with {inventory.Refusal}, so no batch it would have issued "
                    + "exists to acquire.");
        }

        if (inventory.Citation is null)
        {
            return Stop(
                "InventoryCarriedNoCitation",
                "a delivered inventory with no citation issues no batches, and a batch acquired "
                    + "against an absent citation would cite nothing.");
        }

        if (inventory.AddressableInOrder().Count == 0)
        {
            return Stop(
                "InventoryEnumeratedNothing",
                "the publisher reported an empty class, so there is no batch to acquire and no "
                    + "coverage to complete.");
        }

        return new LuxembourgOpinionRequestCanaryDecision(
            "ProceedToOneBatch",
            [FirstBatch],
            $"the inventory proved {inventory.AddressableInOrder().Count} addressable members "
                + $"within {inventory.WireBudget.Spent} of {inventory.WireBudget.Limit} wire "
                + "requests, so one batch may be acquired with the remainder.");
    }

    /// <summary>
    /// What the finished attempt was, once the batch it was allowed has been tried.
    /// </summary>
    /// <remarks>
    /// The verdict is NOT read off the batch alone. A batch that refused because the shared ceiling
    /// ran out is a different finding from a batch that refused on its own contents, and collapsing
    /// them would lose the only thing an under-budgeted attempt actually measured.
    /// </remarks>
    public static string Conclude(
        LuxembourgOpinionRequestCanaryDecision decision,
        LuxembourgOpinionRequestGraphResult? batch)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.BatchOrdinalsToAcquire.Count == 0 || batch is null)
        {
            return decision.Verdict;
        }

        // EXHAUSTION BEFORE COMPLETION, for the reason AfterInventory checks it first. A batch can
        // deliver on its very last reservation: the reservation is taken before the send, so the
        // final attempt succeeding leaves Spent == Limit and Delivered true at the same moment.
        // Reading completion first reported that as CanaryCompleted and threw away the finding -
        // the class sits exactly at the ceiling, so the next batch would have had nothing. I had
        // already handled this shape one function up for the inventory and did not carry the
        // reasoning down.
        if (batch.WireBudget.Exhausted)
        {
            return "BudgetExhaustedDuringBatch";
        }

        return batch.Delivered ? "CanaryCompleted" : "BatchRefused";
    }

    private static LuxembourgOpinionRequestCanaryDecision Stop(string verdict, string reason) =>
        new(verdict, [], reason);
}
