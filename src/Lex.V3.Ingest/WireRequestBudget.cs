namespace Lex.V3.Ingest;

/// <summary>
/// A hard ceiling on the wire requests one run may send, enforced before each attempt.
/// </summary>
/// <remarks>
/// <para>
/// THE CEILING HAS TO LIVE IN THE PATH, NOT IN A PLAN. A number computed outside the executor is a
/// prediction: a pass reads the publisher's count and then continues paging on its own until a short
/// page or its page bound, so a figure derived from an expected row count bounds nothing. This is
/// checked immediately before each attempt is sent, which is the only position that lands between
/// learning a count and sending the pages that count implies.
/// </para>
/// <para>
/// IT COUNTS ATTEMPTS, NOT BOUND REQUESTS. The source profile permits several attempts per request,
/// so a budget expressed in bound requests understates its own limit by that factor at exactly the
/// boundary it exists to hold.
/// </para>
/// <para>
/// ROBOTS IS THE RUN'S FIRST REQUEST, and is spent at construction. A session issues exactly one
/// robots fetch before any product request - ordinal 0, with products from 1 - and a failure there
/// refuses the session outright. Counting it later would let a run send a product request it could
/// not have reached without having already spent one.
/// </para>
/// <para>
/// PUBLISHER-NEUTRAL, like the glue that enforces it. Nothing here knows which family is running.
/// </para>
/// </remarks>
public sealed class WireRequestBudget
{
    private int _spent;

    private WireRequestBudget(int limit)
    {
        Limit = limit;
        _spent = 1;
    }

    /// <summary>Every wire request this run may send, its robots fetch and retries included.</summary>
    public int Limit { get; }

    /// <summary>What this run has sent so far, including the robots fetch.</summary>
    public int Spent => Volatile.Read(ref _spent);

    /// <summary>Whether this run has reached its ceiling.</summary>
    public bool Exhausted => Spent >= Limit;

    /// <summary>
    /// A budget for one run.
    /// </summary>
    /// <param name="limit">
    /// At least two. A run that cannot send one product request after its robots fetch has no honest
    /// shape, and a budget of one would refuse every run before it began.
    /// </param>
    public static WireRequestBudget OfWireRequests(int limit)
    {
        if (limit < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                "A run's budget covers its robots fetch and at least one product request.");
        }

        return new WireRequestBudget(limit);
    }

    /// <summary>
    /// Reserves one attempt, or refuses because this run has reached its ceiling.
    /// </summary>
    /// <remarks>
    /// Reserved BEFORE the attempt is sent. Counting afterwards would record that too much was sent
    /// rather than stop it being sent, which is the difference between a receipt and a ceiling.
    /// </remarks>
    internal bool TryReserveAttempt()
    {
        while (true)
        {
            var spent = Volatile.Read(ref _spent);
            if (spent >= Limit)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _spent, spent + 1, spent) == spent)
            {
                return true;
            }
        }
    }
}
