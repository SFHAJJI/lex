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
/// ROBOTS IS RESERVED WHERE IT IS SENT, NOT CHARGED WHERE THE BUDGET IS BUILT. A session issues
/// exactly one robots fetch before any product request - ordinal 0, with products from 1 - so
/// robots is charged once per SESSION, by whoever is about to open one. The first head of this
/// slice charged it once at construction instead, which is right for exactly one session and wrong
/// by one for every session after it: reusing a budget across N runs put N robots fetches on the
/// wire while charging one. Measured on the transport's own send count, two runs sharing a budget
/// sent 10 and counted 9.
/// </para>
/// <para>
/// SO THIS COUNTS WIRE REQUESTS AND KNOWS NOTHING ELSE. It does not know which request is robots,
/// which is a count, which is a page, or which run is asking. A counter that had to be told a role
/// would have to be told the truth, and the defect above was a role assumed rather than observed.
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
    }

    /// <summary>Every wire request this run may send, its robots fetch and retries included.</summary>
    public int Limit { get; }

    /// <summary>
    /// Every wire request reserved against this budget so far, robots fetches included.
    /// </summary>
    /// <remarks>
    /// Reserved, not sent: a reservation is taken before its request goes out and is never returned
    /// if that request then fails. A ceiling that refunded failures would let a run retry its way
    /// past the number it was given.
    /// </remarks>
    public int Spent => Volatile.Read(ref _spent);

    /// <summary>Whether this run has reached its ceiling.</summary>
    public bool Exhausted => Spent >= Limit;

    /// <summary>
    /// A budget for one run.
    /// </summary>
    /// <param name="limit">
    /// At least two. A run that cannot send one product request after its robots fetch has no honest
    /// shape, and a budget of one would refuse every run before it began. This is a floor on the
    /// number, not a claim about what the two requests are for - nothing here can tell robots from
    /// a page.
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
    /// Reserved BEFORE the request is sent. Counting afterwards would record that too much was sent
    /// rather than stop it being sent, which is the difference between a receipt and a ceiling. The
    /// same call reserves a session's robots fetch: to this type they are one wire request each.
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
