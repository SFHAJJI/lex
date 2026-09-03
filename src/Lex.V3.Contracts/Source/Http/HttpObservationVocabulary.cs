using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

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

    [JsonStringEnumMemberName("negotiation_choice_offered")]
    NegotiationChoiceOffered = 7,
}

public static class HttpStatusClassifier
{
    internal static HttpStatusDisposition Classify(int statusCode, bool hasContentRange)
    {
        if (statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        // A 300 is a negotiation response even if it also carries irrelevant range headers.
        if (statusCode == 300)
        {
            return HttpStatusDisposition.NegotiationChoiceOffered;
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
    public static string Schema { get; } = "outbound_crawler_identity/1";

    public static string Token { get; } =
        "Lex/0.1 (+https://github.com/SFHAJJI/lex)";
}
