using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// D1-06c-LU item 6: closed, named outcomes for one document-fetch GET against
/// legilux.public.lu, carrying the real observed HTTP status rather than a generic wrapper.
/// <see cref="RobotsDisallowed"/> is typed per the scope ruling's item 4 ("typed RobotsDisallowed
/// refusals"), and the numbered outcomes are grounded in live behaviour observed 2026-09-04 with
/// User-Agent Lex/0.1, GET only (Decision 22: HEAD returns 403 host-wide on this host and is never
/// used):
/// <list type="bullet">
/// <item>a real filestore XML document returned HTTP 200, Content-Type application/xml (SHA-256
/// 9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d, 19,986 bytes retained);</item>
/// <item>a deliberately nonexistent filestore path returned HTTP 404, Content-Type
/// application/json, a JSON body {"timestamp","status":404,"error":"Not Found",
/// "message":"No message available","path":...} (SHA-256
/// efd7f3ff4dd45f9a9a303fad9353892c244154d940e24db8b1e480b7b8f4312c, 234 bytes retained).</item>
/// </list>
/// <see cref="Gone"/> and <see cref="RetryExhausted"/> are not directly observed here; they mirror
/// v2's own already-proven ladder for this exact publisher
/// (C:/lex, src/Lex.Sources.Legilux/LegiluxAdapter.cs FetchBody), reproduced rather than referenced
/// because Ingest/Luxembourg is out of this lane's path.
/// </summary>
public enum LuxembourgDocumentGetOutcomeKind
{
    [JsonStringEnumMemberName("retrieved")]
    Retrieved = 1,

    [JsonStringEnumMemberName("not_found")]
    NotFound = 2,

    [JsonStringEnumMemberName("gone")]
    Gone = 3,

    [JsonStringEnumMemberName("robots_disallowed")]
    RobotsDisallowed = 4,

    [JsonStringEnumMemberName("retry_exhausted")]
    RetryExhausted = 5,

    [JsonStringEnumMemberName("unexpected_publisher_status")]
    UnexpectedPublisherStatus = 6,
}

/// <summary>
/// One typed result of a document-fetch GET attempt. <see cref="ObservedStatus"/> is the real wire
/// status (0 for <see cref="LuxembourgDocumentGetOutcomeKind.RobotsDisallowed"/>, which never
/// reaches the network); <see cref="Detail"/> always names what happened in prose, never leaving a
/// refusal to a bare status code.
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

    /// <summary>
    /// The per-object robots refusal (scope ruling item 4): the individually disallowed documents,
    /// every *.docx (and *.svg, also disallowed on this host), and broad disallowed prefixes such
    /// as /eli/etat/adm/, all decided by actually parsing the live robots.txt, never by a
    /// hardcoded list of paths in this type.
    /// </summary>
    public static LuxembourgDocumentGetOutcome RobotsDisallowed(string requestedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        return new(
            LuxembourgDocumentGetOutcomeKind.RobotsDisallowed,
            0,
            $"legilux.public.lu robots.txt disallows '{requestedPath}' for the Lex product token.");
    }

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
    public static LuxembourgDocumentGetOutcome FromObservedStatus(int status) => status switch
    {
        200 => Retrieved(),
        404 => NotFound(),
        410 => Gone(),
        408 or 429 or 500 or 502 or 503 or 504 => RetryExhausted(status),
        _ => UnexpectedPublisherStatus(status),
    };

    private static int RequireHttpStatus(int status) => status is >= 100 and <= 599
        ? status
        : throw new ArgumentOutOfRangeException(nameof(status));

    private static int RequireRetryableStatus(int status) => status is 408 or 429 or 500 or 502 or 503 or 504
        ? status
        : throw new ArgumentOutOfRangeException(nameof(status));
}
