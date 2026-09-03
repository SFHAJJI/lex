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

        // Header-terminated statuses are classified before any header-dependent rule: a 300 is a
        // negotiation response, a 204 has no entity and a 304 is a revalidation reference, even
        // when they also carry an irrelevant range header. A 205 is framed normally and stays
        // under the range rule.
        switch (statusCode)
        {
            case 300:
                return HttpStatusDisposition.NegotiationChoiceOffered;
            case 204:
                return HttpStatusDisposition.SemanticNoEntityStatus;
            case 304:
                return HttpStatusDisposition.RevalidationReferenceOnly;
        }

        if (statusCode == 206 || hasContentRange)
        {
            return HttpStatusDisposition.RangeNotApproved;
        }

        return statusCode switch
        {
            200 => HttpStatusDisposition.DerivableStatus,
            301 or 302 or 303 or 307 or 308 => HttpStatusDisposition.RedirectObserved,
            205 => HttpStatusDisposition.SemanticNoEntityStatus,
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
