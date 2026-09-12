namespace Lex.V3.Ingest;

/// <summary>
/// What one run's wire budget stood at, captured immediately after that run and never afterwards.
/// </summary>
/// <remarks>
/// <para>
/// A VALUE, NOT THE BUDGET. <see cref="WireRequestBudget"/> is mutable and shared: a sweep hands one
/// instance to several runs, so a result that carried the budget itself would report whatever the
/// LAST run left behind, for every run, including runs that finished before it. Retained evidence
/// would then say something true about the sweep and false about the run it was attached to. This is
/// the reading taken at one moment, and it cannot change afterwards.
/// </para>
/// <para>
/// <see cref="Spent"/> IS A COUNT OF RESERVED WIRE ATTEMPTS, TAKEN IMMEDIATELY BEFORE SEND. It is
/// not a count of runs, not a count of bound requests, and not a count of responses. A reservation is
/// taken before its request goes out and is never returned if that request then fails, because a
/// ceiling that refunded failures would let a run retry its way past the number it was given. So
/// <see cref="Spent"/> is an upper bound on what reached the publisher, and reconciling it against
/// transport-observed sends is the check - a mismatch is a finding, not acceptance.
/// </para>
/// <para>
/// IT IS CUMULATIVE ACROSS A SHARED BUDGET, and that is the point rather than a caveat. Two runs
/// sharing one budget produce two snapshots whose <see cref="Spent"/> values are ordered, not
/// independent: the second includes everything the first reserved. A reader comparing a single
/// snapshot against a single run's own request count will be wrong by exactly what the earlier runs
/// spent, which is why the reconciliation identity subtracts rather than compares.
/// </para>
/// </remarks>
public sealed record WireBudgetSnapshot
{
    private WireBudgetSnapshot(int limit, int spent)
    {
        Limit = limit;
        Spent = spent;
    }

    /// <summary>The ceiling this run was given, including its robots fetch and every retry.</summary>
    public int Limit { get; }

    /// <summary>
    /// Wire attempts reserved against the budget by the end of this run, robots included, counted
    /// cumulatively over every run that has shared the same budget instance.
    /// </summary>
    public int Spent { get; }

    /// <summary>Whether the budget had reached its ceiling when this reading was taken.</summary>
    public bool Exhausted => Spent >= Limit;

    /// <summary>
    /// Reads a budget once.
    /// </summary>
    /// <remarks>
    /// Callers take this immediately after the executor run whose cost it describes. Taken later, it
    /// would fold in whatever a subsequent run on the same budget had spent; taken earlier, it would
    /// describe a run that had not happened yet.
    /// </remarks>
    public static WireBudgetSnapshot Of(WireRequestBudget budget)
    {
        // KEPT WHERE THE SIBLING GUARDS WERE REMOVED, and the difference is reachability rather
        // than taste. This is the public door: a caller compiling without nullable annotations
        // reaches it with a null nobody had to write `null!` for. The result constructors' guards
        // went because their only callers are internal factories with non-nullable parameters, so
        // nothing supported could ever drive them - a check no test can fail is an untested claim.
        ArgumentNullException.ThrowIfNull(budget);
        return new WireBudgetSnapshot(budget.Limit, budget.Spent);
    }
}
