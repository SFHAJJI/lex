namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The Legilux SPARQL gateway's read timeout, and what one acquisition run does about it: a bounded retry of the same
/// plan item, then a split of the leaf, and a stop for the whole run when the publisher is not serving pages at all.
/// </summary>
/// <remarks>
/// <para>
/// THE ENVELOPE. When a query runs past about 45 seconds, the gateway in front of the Legilux Virtuoso endpoint answers
/// HTTP 500 with exactly one 120-byte JSON body:
/// <c>{"meta":"error","id":"&lt;uuid&gt;","title":"Read timed out","code":"error.unknown","data":null}</c>, its id a fresh
/// lowercase UUID per response (retained in the 2026-09-30 count probe and population preflight custody, for example
/// <c>79282f1f294d538e86d19d118e951f2ee21b2c334112c05daf910874b0d57210</c>). It is matched by that template, byte for
/// byte, never by digest, since every response's id differs. Any other body, status or endpoint is not this envelope
/// and refuses as before.
/// </para>
/// <para>
/// THE POLICY. A page (either pass) or a second-pass COUNT that answers the envelope is sent again as the same plan
/// item, at most <see cref="MaximumSendsPerRequest"/> sends in all, each retry after the profile's own maximum retry
/// delay on the run's clock (the session then adds its own backoff and origin pacing). A first-pass COUNT is never
/// retried: it splits at once, as it always has, because a split costs a few cheap counts while a retry repeats a
/// query that already ran 45 seconds. When the sends are spent, the leaf asks to be split (the existing
/// <c>PartitionRequired</c> path), so smaller leaves prove the range instead.
/// </para>
/// <para>
/// THE BREAKER. One instance per executor, which is one per acquisition run. It counts envelopes since the last
/// admitted page; a COUNT that succeeds does not reset it, since it does not show that pages can be served. At
/// <see cref="ConsecutiveLimit"/> no retry or split follows: the leaf refuses, the cover stops, and every later pass of
/// this run refuses without sending, so an outage costs a handful of heavy queries and the run ends with a typed
/// refusal, to be resumed after the publisher recovers.
/// </para>
/// <para>
/// Every send, retries included, is reserved from the run's one wire budget before it goes out, and every executed
/// attempt is retained as its own route before the retry is decided, exactly as for the Publications Office retries.
/// </para>
/// </remarks>
internal sealed class LuxembourgGatewayTimeouts
{
    /// <summary>Sends of one plan item in all: the first and two retries, below the profile's four attempts.</summary>
    internal const int MaximumSendsPerRequest = 3;

    /// <summary>Envelopes since the last admitted page at which the run stops sending Luxembourg SPARQL.</summary>
    internal const int ConsecutiveLimit = 8;

    private readonly TimeProvider _clock;
    private int _consecutive;

    public LuxembourgGatewayTimeouts(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Envelopes answered since the last admitted page.</summary>
    internal int Consecutive => Volatile.Read(ref _consecutive);

    /// <summary>True once <see cref="ConsecutiveLimit"/> envelopes have come without an admitted page between them.</summary>
    internal bool IsOpen => Consecutive >= ConsecutiveLimit;

    internal void RecordTimeout() => Interlocked.Increment(ref _consecutive);

    internal void RecordPageAdmitted() => Interlocked.Exchange(ref _consecutive, 0);

    /// <summary>Whether a request sent <paramref name="sendsSoFar"/> times may be sent again.</summary>
    internal bool MayRetry(int sendsSoFar) => sendsSoFar < MaximumSendsPerRequest && !IsOpen;

    /// <summary>The wait before a retry, on the run's clock.</summary>
    internal Task CoolDownAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, _clock, cancellationToken);

    /// <summary>Whether <paramref name="body"/> is exactly the gateway's read-timeout envelope.</summary>
    internal static bool IsEnvelope(ReadOnlySpan<byte> body)
    {
        var prefix = "{\"meta\":\"error\",\"id\":\""u8;
        var suffix = "\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}"u8;
        const int IdLength = 36;
        if (body.Length != prefix.Length + IdLength + suffix.Length || !body.StartsWith(prefix) || !body.EndsWith(suffix))
            return false;
        var id = body.Slice(prefix.Length, IdLength);
        for (var index = 0; index < IdLength; index++)
        {
            var value = id[index];
            var valid = index is 8 or 13 or 18 or 23
                ? value == (byte)'-'
                : value is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f';
            if (!valid) return false;
        }

        return true;
    }
}
