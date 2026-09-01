using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

public static class HttpObservationSchemaIds
{
    public const string HttpObservation = "http_observation/1";
}

public static class HttpObservationWireKinds
{
    public const string ResponseCompleteBody = "response_complete_body";
    public const string ResponsePartialBody = "response_partial_body";
    public const string Revalidation304 = "revalidation_304";
    public const string ResponseWithoutBody = "response_without_body";
    public const string TransportFailureBeforeBody = "transport_failure_before_body";
    public const string PolicyRejection = "policy_rejection";
}

public enum HttpObservationKind
{
    [JsonStringEnumMemberName(HttpObservationWireKinds.ResponseCompleteBody)]
    ResponseCompleteBody = 1,

    [JsonStringEnumMemberName(HttpObservationWireKinds.ResponsePartialBody)]
    ResponsePartialBody = 2,

    [JsonStringEnumMemberName(HttpObservationWireKinds.Revalidation304)]
    Revalidation304 = 3,

    [JsonStringEnumMemberName(HttpObservationWireKinds.ResponseWithoutBody)]
    ResponseWithoutBody = 4,

    [JsonStringEnumMemberName(HttpObservationWireKinds.TransportFailureBeforeBody)]
    TransportFailureBeforeBody = 5,

    [JsonStringEnumMemberName(HttpObservationWireKinds.PolicyRejection)]
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
            if (facts.HeadersComplete || facts.TransferComplete ||
                facts.StatusCode is not null || facts.ReceivedByteCount != 0)
            {
                throw new ArgumentException(
                    "A pre-request policy rejection cannot carry transport or response evidence.",
                    nameof(facts));
            }

            return HttpObservationKind.PolicyRejection;
        }

        if (!facts.HeadersComplete)
        {
            if (facts.TransferComplete || facts.StatusCode is not null || facts.ReceivedByteCount != 0)
            {
                throw new ArgumentException(
                    "Failure before complete headers cannot carry response or entity evidence.",
                    nameof(facts));
            }

            return HttpObservationKind.TransportFailureBeforeBody;
        }

        if (facts.StatusCode is null or < 100 or > 599)
        {
            throw new ArgumentException(
                "Complete response headers must carry one valid HTTP status.", nameof(facts));
        }

        if (!facts.TransferComplete)
        {
            return HttpObservationKind.ResponsePartialBody;
        }

        if (facts.StatusCode is 204 or 205 or 304 && facts.ReceivedByteCount != 0)
        {
            throw new ArgumentException(
                "A completed semantic no-body or 304 response cannot carry entity octets.",
                nameof(facts));
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
    public static HttpStatusDisposition Classify(
        int statusCode,
        HttpResponseMetadata responseMetadata)
    {
        ArgumentNullException.ThrowIfNull(responseMetadata);

        if (statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        if (statusCode == 206 || responseMetadata.ContentRange is not null)
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
    public static string Schema { get; } = "outbound_crawler_identity/1";

    public static string Token { get; } =
        "Lex/0.1 (+https://github.com/SFHAJJI/lex)";
}
