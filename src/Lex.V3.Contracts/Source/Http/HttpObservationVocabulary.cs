using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

public enum HttpObservationKind
{
    [JsonStringEnumMemberName("response_complete_body")]
    ResponseCompleteBody = 1,

    [JsonStringEnumMemberName("response_partial_body")]
    ResponsePartialBody = 2,

    [JsonStringEnumMemberName("revalidation_304")]
    Revalidation304 = 3,

    [JsonStringEnumMemberName("response_without_body")]
    ResponseWithoutBody = 4,

    [JsonStringEnumMemberName("transport_failure_before_body")]
    TransportFailureBeforeBody = 5,

    [JsonStringEnumMemberName("policy_rejection")]
    PolicyRejection = 6,
}

public enum HttpStatusDisposition
{
    [JsonStringEnumMemberName("derivable_status")]
    DerivableStatus = 1,

    [JsonStringEnumMemberName("redirect_observed")]
    RedirectObserved = 2,

    [JsonStringEnumMemberName("revalidation_reference_only")]
    RevalidationReferenceOnly = 3,

    [JsonStringEnumMemberName("semantic_no_entity_status")]
    SemanticNoEntityStatus = 4,

    [JsonStringEnumMemberName("range_not_approved")]
    RangeNotApproved = 5,

    [JsonStringEnumMemberName("non_derivable_status")]
    NonDerivableStatus = 6,
}

public readonly record struct HttpTransferFacts(
    bool PolicyRejected,
    bool HeadersComplete,
    bool TransferComplete,
    int? StatusCode,
    long ReceivedByteCount);

public static class HttpTransferClassifier
{
    public static HttpObservationKind Classify(HttpTransferFacts facts)
    {
        if (facts.ReceivedByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(facts));
        }

        if (facts.PolicyRejected)
        {
            return HttpObservationKind.PolicyRejection;
        }

        if (!facts.HeadersComplete)
        {
            return HttpObservationKind.TransportFailureBeforeBody;
        }

        if (!facts.TransferComplete)
        {
            return HttpObservationKind.ResponsePartialBody;
        }

        if (facts.StatusCode is null)
        {
            throw new ArgumentException(
                "A completed response must carry an HTTP status.", nameof(facts));
        }

        if (facts.StatusCode == 304)
        {
            return HttpObservationKind.Revalidation304;
        }

        if (facts.StatusCode is 204 or 205 || facts.ReceivedByteCount == 0)
        {
            return HttpObservationKind.ResponseWithoutBody;
        }

        return HttpObservationKind.ResponseCompleteBody;
    }
}

public static class HttpStatusClassifier
{
    public static HttpStatusDisposition Classify(int statusCode, bool hasContentRange)
    {
        if (statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        if (statusCode == 206 || hasContentRange)
        {
            return HttpStatusDisposition.RangeNotApproved;
        }

        return statusCode switch
        {
            200 => HttpStatusDisposition.DerivableStatus,
            301 or 302 or 303 or 307 or 308 => HttpStatusDisposition.RedirectObserved,
            304 => HttpStatusDisposition.RevalidationReferenceOnly,
            204 or 205 => HttpStatusDisposition.SemanticNoEntityStatus,
            _ => HttpStatusDisposition.NonDerivableStatus,
        };
    }
}

public static class OutboundCrawlerIdentity
{
    public static string Token { get; } =
        "Lex/0.1 (+https://github.com/SFHAJJI/lex)";
}
