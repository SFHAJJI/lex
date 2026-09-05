using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// D1-06c-LU item 6: closed, named outcomes for one document-fetch GET against
/// legilux.public.lu, carrying the real observed HTTP status rather than a generic wrapper. The
/// numbered outcomes are grounded in live behaviour observed 2026-09-04 with User-Agent Lex/0.1,
/// GET only (Decision 22: HEAD returns 403 host-wide on this host and is never used):
/// <list type="bullet">
/// <item>a real filestore XML document returned HTTP 200, Content-Type application/xml (SHA-256
/// 9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d, 19,986 bytes retained);</item>
/// <item>a deliberately nonexistent filestore path returned HTTP 404, Content-Type
/// application/json, a JSON body {"timestamp","status":404,"error":"Not Found",
/// "message":"No message available","path":...}. The observation actually RETAINED in this tree is
/// 204 bytes, SHA-256 b4e140344eddc8e62e8500c6479fb9b5a2807d47f16fe904e5d0c08204580bab, taken
/// 2026-09-04T22:20Z, held at tests/Lex.V3.Ingest.Tests/Fixtures/LuDocumentFetch as the 404
/// body.</item>
/// </list>
/// <see cref="Gone"/> and <see cref="RetryExhausted"/> are not directly observed here; they mirror
/// v2's own already-proven ladder for this exact publisher, reproduced rather than referenced.
/// <para>
/// The 404 body's digest is PER FETCH, not a constant: the office's JSON error carries a live
/// timestamp field and echoes the requested path, so a fresh fetch of the same nonexistent path
/// produces different bytes and a different digest. Three observations of that one endpoint shape
/// were taken. The first two, 209 bytes and then 234 bytes
/// (efd7f3ff4dd45f9a9a303fad9353892c244154d940e24db8b1e480b7b8f4312c), were superseded and are NOT
/// retained: no such bytes exist in this tree, and an earlier revision of this remark wrongly
/// claimed the 234-byte one was. Only the 204-byte observation named above is retained. Its digest
/// names THOSE bytes so the fixture cannot be swapped; it is never a claim that a fresh fetch
/// reproduces it, and no test asserts that.
/// </para>
/// </summary>
public enum LuxembourgDocumentGetOutcomeKind
{
    [JsonStringEnumMemberName("retrieved")]
    Retrieved = 1,

    [JsonStringEnumMemberName("not_found")]
    NotFound = 2,

    [JsonStringEnumMemberName("gone")]
    Gone = 3,

    // robots_disallowed, 4, is removed. Nothing in production ever produced it: a robots refusal
    // never reaches a status to classify, so the route maps it one level up, at
    // LuxembourgDocumentGetAttemptRefusal.RobotsDisallowed, which the adapter turns into the
    // object's own CorpusAcquisitionRefusalReason.RobotsDisallowed. Its only caller was its own
    // test. A declared member nothing can produce is the defect this slice keeps removing.

    [JsonStringEnumMemberName("retry_exhausted")]
    RetryExhausted = 5,

    [JsonStringEnumMemberName("unexpected_publisher_status")]
    UnexpectedPublisherStatus = 6,
}

/// <summary>
/// One typed result of a document-fetch GET attempt. <see cref="ObservedStatus"/> is the real wire
/// status; <see cref="Detail"/> always names what happened in prose, never leaving a refusal to a
/// bare status code.
/// </summary>
public sealed record LuxembourgDocumentGetOutcome
{
    private LuxembourgDocumentGetOutcome(
        LuxembourgDocumentGetOutcomeKind kind,
        int observedStatus,
        string detail)
    {
        Kind = kind;
        ObservedStatus = observedStatus;
        Detail = detail;
    }

    public LuxembourgDocumentGetOutcomeKind Kind { get; }

    public int ObservedStatus { get; }

    public string Detail { get; }

    public static LuxembourgDocumentGetOutcome Retrieved() => new(
        LuxembourgDocumentGetOutcomeKind.Retrieved,
        200,
        "The Legilux www host returned the document body.");

    public static LuxembourgDocumentGetOutcome NotFound() => new(
        LuxembourgDocumentGetOutcomeKind.NotFound,
        404,
        "The Legilux www host returned HTTP 404 for this document.");

    public static LuxembourgDocumentGetOutcome Gone() => new(
        LuxembourgDocumentGetOutcomeKind.Gone,
        410,
        "The Legilux www host returned HTTP 410 for this document.");

    public static LuxembourgDocumentGetOutcome RetryExhausted(int observedStatus) => new(
        LuxembourgDocumentGetOutcomeKind.RetryExhausted,
        RequireRetryableStatus(observedStatus),
        $"A retryable publisher response {observedStatus} exhausted the acquisition policy.");

    public static LuxembourgDocumentGetOutcome UnexpectedPublisherStatus(int observedStatus) => new(
        LuxembourgDocumentGetOutcomeKind.UnexpectedPublisherStatus,
        RequireHttpStatus(observedStatus),
        $"The Legilux www host returned an unexpected HTTP {observedStatus}.");

    /// <summary>
    /// Classifies one real observed, successful-transport HTTP status into its named outcome. Not
    /// a generic wrapper: everything this method can return is one of the closed members above,
    /// each carrying its own fixed detail text; only <see cref="UnexpectedPublisherStatus"/> and
    /// <see cref="RetryExhausted"/> also carry the numeric status, and both still name what that
    /// status means for this route rather than passing it through unlabelled.
    /// </summary>
    /// <param name="status">The terminal status the route actually completed at.</param>
    /// <param name="retryAllowanceSpent">
    /// Whether this object's fetch has already spent every application attempt its own profile
    /// allows. D1-06c-LU-2 made this a parameter rather than an assumption, because the claim was
    /// otherwise untrue: the six retryable statuses used to map to
    /// <see cref="RetryExhausted"/> on the FIRST observation of one, which would have named a
    /// retry policy that never ran. <c>LuxembourgRepeatedEnumerationExecutor.RunDocumentGetAsync</c>
    /// really does re-attempt a retryable status up to
    /// <c>OfficialMachineQuerySourceProfile.MaximumAttempts</c> (the session's own
    /// <c>PlanItem.IsRetryable</c> admits exactly these six), so when it passes true here the name
    /// is earned. Passing false for a retryable status is refused outright rather than quietly
    /// downgraded, so no caller can produce the unearned claim by accident.
    /// </param>
    public static LuxembourgDocumentGetOutcome FromObservedStatus(int status, bool retryAllowanceSpent)
    {
        if (status is 408 or 429 or 500 or 502 or 503 or 504)
        {
            return retryAllowanceSpent
                ? RetryExhausted(status)
                : throw new ArgumentException(
                    $"HTTP {status} is retryable on this route, so it cannot be classified as a " +
                    "terminal outcome until this object's own retry allowance is spent.",
                    nameof(retryAllowanceSpent));
        }

        return status switch
        {
            200 => Retrieved(),
            404 => NotFound(),
            410 => Gone(),
            _ => UnexpectedPublisherStatus(status),
        };
    }

    private static int RequireHttpStatus(int status) => status is >= 100 and <= 599
        ? status
        : throw new ArgumentOutOfRangeException(nameof(status));

    private static int RequireRetryableStatus(int status) => status is 408 or 429 or 500 or 502 or 503 or 504
        ? status
        : throw new ArgumentOutOfRangeException(nameof(status));
}
