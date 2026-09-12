using Lex.V3.Contracts.Source.Luxembourg;
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
/// The two independent accountings of what the canary sent, and whether they agree.
/// </summary>
/// <remarks>
/// <para>
/// TWO MECHANISMS, CHECKED AGAINST EACH OTHER. <see cref="FinalBudgetSpent"/> comes from the
/// reservations the budget granted; <see cref="RecordedProductRequests"/> comes from the counter the
/// glue increments after each attempt, plus one robots fetch per session opened. They are produced
/// by different code for different reasons, which is the only thing that makes their agreement
/// evidence rather than a restatement.
/// </para>
/// <para>
/// <see cref="Reconciles"/> IS COMPUTED, NOT LEFT TO THE READER. The first head of this canary put
/// both numbers in the evidence index side by side and said nothing about whether they matched, so
/// noticing a drift required doing arithmetic in JSON - which is how a drift goes unnoticed at an
/// acceptance gate.
/// </para>
/// </remarks>
public sealed record LuxembourgOpinionRequestCanaryReconciliation
{
    internal LuxembourgOpinionRequestCanaryReconciliation(
        int sessionsOpened,
        int recordedProductRequests,
        int finalBudgetSpent,
        int expectedIfEverySessionCompleted)
    {
        SessionsOpened = sessionsOpened;
        RecordedProductRequests = recordedProductRequests;
        FinalBudgetSpent = finalBudgetSpent;
        ExpectedIfEverySessionCompleted = expectedIfEverySessionCompleted;
    }

    /// <summary>Sessions the attempt opened, each of which sent one robots fetch.</summary>
    public int SessionsOpened { get; }

    /// <summary>Product attempts the producers recorded across those runs.</summary>
    public int RecordedProductRequests { get; }

    /// <summary>Reservations the shared budget granted, robots included.</summary>
    public int FinalBudgetSpent { get; }

    /// <summary>
    /// What <see cref="FinalBudgetSpent"/> must equal when every reservation was followed by a
    /// recorded attempt: the recorded product attempts plus one robots fetch per session.
    /// </summary>
    public int ExpectedIfEverySessionCompleted { get; }

    /// <summary>
    /// Whether the two accountings agree. DERIVED FROM THE COUNTS, never carried beside them.
    /// </summary>
    /// <remarks>
    /// It was a constructor parameter on the first head, which made it a claim a caller could state
    /// independently of the numbers it was supposed to summarise: a record reading
    /// <c>(spent 33, expected 32, reconciles true)</c> was expressible, and the verdict believed it.
    /// A boolean that can disagree with its own evidence is not a check, and the whole point of this
    /// record is to BE the check.
    /// </remarks>
    public bool Reconciles => FinalBudgetSpent == ExpectedIfEverySessionCompleted;
}

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
    public static LuxembourgOpinionRequestCanaryReconciliation Reconcile(
        LuxembourgOpinionRequestInventoryResult inventory,
        IReadOnlyList<LuxembourgOpinionRequestGraphResult> batches)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(batches);

        // ONE SESSION PER RUN THAT HAPPENED. The inventory always opens one; each batch that ran
        // opens one more, and the gate guarantees each had a reservation left to open it with.
        //
        // GENERALISED TO N RATHER THAN DUPLICATED FOR THE SWEEP. A second reconciliation for the
        // many-batch case would be a second accounting that has to agree with this one, and every
        // defect found in this slice has been two things that were supposed to agree and did not.
        // The canary is simply N = 1.
        var sessions = 1 + batches.Count;
        var recorded = inventory.ProductRequestCount + batches.Sum(static b => b.ProductRequestCount);
        var spent = batches.Count == 0
            ? inventory.WireBudget.Spent
            : batches[^1].WireBudget.Spent;
        var expected = recorded + sessions;

        return new LuxembourgOpinionRequestCanaryReconciliation(
            sessions, recorded, spent, expected);
    }

    public static string Conclude(
        LuxembourgOpinionRequestCanaryDecision decision,
        LuxembourgOpinionRequestInventoryResult inventory,
        IReadOnlyList<LuxembourgOpinionRequestGraphResult> batches)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(batches);

        if (decision.BatchOrdinalsToAcquire.Count == 0 || batches.Count == 0)
        {
            return decision.Verdict;
        }

        // THE SWEEP STOPPED SHORT OF WHAT IT WAS CLEARED FOR. Fewer batches came back than the gate
        // authorised, which means the ceiling ran out partway. Reported before any per-batch verdict
        // because a cover built over a truncated set is not a cover, whatever the batches say.
        if (batches.Count < decision.BatchOrdinalsToAcquire.Count)
        {
            return "SweepStoppedBeforeEveryBatch";
        }

        var batch = batches[^1];

        // DERIVED HERE, FROM THE RUNS BEING CONCLUDED. Taking a reconciliation as an argument made
        // the gate caller-certified twice over: the record could assert agreement its own counts
        // contradicted, and a record computed over one pair of runs could be handed to a verdict
        // about another. Both were reachable, and my own test helper demonstrated the second by
        // pairing an unrelated record with any result. A verdict that accepts its evidence from the
        // caller is not checking anything; it is repeating what it was told.
        var reconciliation = Reconcile(inventory, batches);

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

        // A MISMATCH OUTRANKS COMPLETION. A canary whose own two accountings disagree has not
        // demonstrated the path, whatever its rows say: the numbers that would evidence the run are
        // the numbers in dispute. Deliberately ranked BELOW exhaustion rather than above it - a
        // reader of an exhausted run still sees Reconciles on the retained record, so nothing is
        // hidden either way, and the reviewer asked for precedence over completion specifically.
        if (!reconciliation.Reconciles)
        {
            return "AccountingDidNotReconcile";
        }

        return batch.Delivered ? "CanaryCompleted" : "BatchRefused";
    }

    /// <summary>
    /// Every batch the inventory issues, when the same conditions that clear one batch are met.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DELEGATES TO <see cref="AfterInventory"/> RATHER THAN RESTATING ITS RULES. Every refusal,
    /// exhaustion, empty-class and missing-citation stop is inherited, so the two gates cannot come
    /// to different conclusions about the same inventory. The only thing that differs is how many
    /// ordinals come back once it opens, which is the only thing that should differ.
    /// </para>
    /// <para>
    /// The ordinals come from the assignment the citation issues, never from a count the caller
    /// supplies or from what a sweep managed to return.
    /// </para>
    /// </remarks>
    public static LuxembourgOpinionRequestCanaryDecision EveryBatchAfterInventory(
        LuxembourgOpinionRequestInventoryResult inventory)
    {
        var gated = AfterInventory(inventory);
        if (gated.BatchOrdinalsToAcquire.Count == 0)
        {
            return gated;
        }

        var issued = LuxembourgOpinionRequestBatchAssignment
            .Over(inventory.AddressableInOrder(), inventory.Citation!).Count;

        return gated with
        {
            BatchOrdinalsToAcquire = [.. Enumerable.Range(0, issued)],
            Reason = gated.Reason + $" The full sweep acquires all {issued} issued batches.",
        };
    }

    private static LuxembourgOpinionRequestCanaryDecision Stop(string verdict, string reason) =>
        new(verdict, [], reason);
}
