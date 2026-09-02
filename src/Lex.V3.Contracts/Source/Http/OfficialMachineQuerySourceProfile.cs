using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum OfficialMachineQuerySourceProfileId
{
    [JsonStringEnumMemberName("luxembourg_sparql")]
    LuxembourgSparql = 1,

    [JsonStringEnumMemberName("european_union_sparql")]
    EuropeanUnionSparql = 2,
}

public enum RobotsPolicyFreshness
{
    [JsonStringEnumMemberName("current")]
    Current = 1,

    [JsonStringEnumMemberName("expired")]
    Expired = 2,
}

public enum OfficialHttpAcquisitionOutcomeKind
{
    [JsonStringEnumMemberName("executed_observation")]
    ExecutedObservation = 1,

    [JsonStringEnumMemberName("publisher_denial")]
    PublisherDenial = 2,

    [JsonStringEnumMemberName("local_safety_refusal")]
    LocalSafetyRefusal = 3,

    [JsonStringEnumMemberName("operational_failure")]
    OperationalFailure = 4,

    [JsonStringEnumMemberName("integrity_failure")]
    IntegrityFailure = 5,
}

public enum OfficialHttpOperationalFailureReason
{
    [JsonStringEnumMemberName("network_failure")]
    NetworkFailure = 1,

    [JsonStringEnumMemberName("publisher_server_failure")]
    PublisherServerFailure = 2,

    [JsonStringEnumMemberName("robots_policy_expired")]
    RobotsPolicyExpired = 3,

    [JsonStringEnumMemberName("source_profile_stale")]
    SourceProfileStale = 4,
}

public enum OfficialHttpPacingScope
{
    [JsonStringEnumMemberName("process_actual_network_origin")]
    ProcessActualNetworkOrigin = 1,
}

public enum OfficialMachineQueryRetryCondition
{
    [JsonStringEnumMemberName("request_timeout")]
    RequestTimeout = 1,

    [JsonStringEnumMemberName("transport_failure")]
    TransportFailure = 2,

    [JsonStringEnumMemberName("http_408")]
    Http408 = 3,

    [JsonStringEnumMemberName("http_429")]
    Http429 = 4,

    [JsonStringEnumMemberName("http_500")]
    Http500 = 5,

    [JsonStringEnumMemberName("http_502")]
    Http502 = 6,

    [JsonStringEnumMemberName("http_503")]
    Http503 = 7,

    [JsonStringEnumMemberName("http_504")]
    Http504 = 8,
}

public enum RobotsRevalidationMode
{
    /// <summary>
    /// Fetch the full policy on every generation. This deliberately creates publisher load until
    /// Decision 41 supplies a typed predecessor and validator binding; it is not merely a missing
    /// optimization.
    /// </summary>
    [JsonStringEnumMemberName("full_get_without_validators")]
    FullGetWithoutValidators = 1,
}

public sealed record RobotsPolicyRouteStep
{
    internal RobotsPolicyRouteStep(
        string requestedUri,
        int expectedStatusCode,
        string? expectedLocation)
    {
        RequestedUri = HttpRequestEvidence.RequireCanonicalRequestUri(
            requestedUri,
            nameof(requestedUri));
        if (expectedStatusCode is not (200 or 301))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedStatusCode));
        }

        if ((expectedStatusCode == 301) != (expectedLocation is not null))
        {
            throw new ArgumentException(
                "An exact 301 route step requires one Location and a terminal 200 requires none.",
                nameof(expectedLocation));
        }

        ExpectedStatusCode = expectedStatusCode;
        ExpectedLocation = expectedLocation is null
            ? null
            : HttpRequestEvidence.RequireCanonicalRequestUri(
                expectedLocation,
                nameof(expectedLocation));
    }

    public string RequestedUri { get; }

    public int ExpectedStatusCode { get; }

    public string? ExpectedLocation { get; }
}

public sealed class RobotsPolicyRoute
{
    internal RobotsPolicyRoute(HttpOrigin initialAuthority, params RobotsPolicyRouteStep[] steps)
    {
        InitialAuthority = initialAuthority ?? throw new ArgumentNullException(nameof(initialAuthority));
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Length == 0)
        {
            throw new ArgumentException("A frozen robots route requires at least one step.", nameof(steps));
        }

        var snapshot = steps.ToArray();
        if (snapshot.Any(static step => step is null))
        {
            throw new ArgumentException("A frozen robots route cannot contain a null step.", nameof(steps));
        }

        var first = new Uri(snapshot[0].RequestedUri, UriKind.Absolute);
        if (!string.Equals(first.Scheme, initialAuthority.Scheme, StringComparison.Ordinal) ||
            !string.Equals(first.Host, initialAuthority.Host, StringComparison.Ordinal) ||
            first.Port != initialAuthority.EffectivePort)
        {
            throw new ArgumentException(
                "A robots route must begin at the initial authority whose policy it governs.",
                nameof(steps));
        }

        for (var index = 0; index < snapshot.Length - 1; index++)
        {
            if (snapshot[index].ExpectedStatusCode != 301 ||
                !string.Equals(
                    snapshot[index].ExpectedLocation,
                    snapshot[index + 1].RequestedUri,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every nonterminal robots route step must be the exact 301 Location of the next step.",
                    nameof(steps));
            }
        }

        if (snapshot[^1].ExpectedStatusCode != 200 || snapshot[^1].ExpectedLocation is not null)
        {
            throw new ArgumentException("A frozen robots route must terminate in an exact 200.", nameof(steps));
        }

        Steps = Array.AsReadOnly(snapshot);
    }

    public HttpOrigin InitialAuthority { get; }

    public IReadOnlyList<RobotsPolicyRouteStep> Steps { get; }
}

/// <summary>
/// One exact, non-resource machine-query channel. It records policy; only the transport may turn
/// the policy and live observations into a request result.
/// </summary>
public sealed class OfficialMachineQuerySourceProfile
{
    internal const string CanonicalizationIdentity = "official-machine-query-source-profile/1";
    private static readonly TimeSpan RobotsAgeCeiling = TimeSpan.FromHours(24);

    private OfficialMachineQuerySourceProfile(
        OfficialMachineQuerySourceProfileId id,
        string requestTarget,
        string requestContentType,
        RobotsPolicyRoute robotsRoute)
    {
        Id = id;
        RequestTarget = requestTarget;
        RequestContentType = requestContentType;
        RobotsRoute = robotsRoute;
        RetryConditions = Array.AsReadOnly(new[]
        {
            OfficialMachineQueryRetryCondition.RequestTimeout,
            OfficialMachineQueryRetryCondition.TransportFailure,
            OfficialMachineQueryRetryCondition.Http408,
            OfficialMachineQueryRetryCondition.Http429,
            OfficialMachineQueryRetryCondition.Http500,
            OfficialMachineQueryRetryCondition.Http502,
            OfficialMachineQueryRetryCondition.Http503,
            OfficialMachineQueryRetryCondition.Http504,
        });
        ProfileSha256 = ComputeSha256();
    }

    public OfficialMachineQuerySourceProfileId Id { get; }

    public string RequestTarget { get; }

    public HttpRequestMethod Method => HttpRequestMethod.Post;

    public string RequestContentType { get; }

    public MachineQueryCharset RequestCharset => MachineQueryCharset.Utf8;

    public string Accept => "application/sparql-results+json";

    public string CrawlerUserAgent => OutboundCrawlerIdentity.Token;

    public string RobotsProductToken => "Lex";

    public string RobotsParserIdentity => "robots-exclusion-policy/1";

    public RobotsPolicyRoute RobotsRoute { get; }

    public TimeSpan MinimumRequestInterval => TimeSpan.FromMilliseconds(1_500);

    public OfficialHttpPacingScope PacingScope =>
        OfficialHttpPacingScope.ProcessActualNetworkOrigin;

    /// <summary>Four sends total, not four retries after an initial send.</summary>
    public int MaximumAttempts => 4;

    public IReadOnlyList<OfficialMachineQueryRetryCondition> RetryConditions { get; }

    public TimeSpan InitialRetryDelay => TimeSpan.FromSeconds(1);

    public TimeSpan MaximumRetryDelay => TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(60);

    public long MaximumResponseBytes => CustodyBounds.MaxObjectBytes;

    /// <summary>
    /// The RFC 9309 section 2.4 standards ceiling, not a cache-performance tuning value.
    /// </summary>
    public TimeSpan MaximumRobotsPolicyAge => RobotsAgeCeiling;

    public string RobotsFreshnessBasis => "rfc9309_2_4";

    public RobotsRevalidationMode RobotsRevalidation =>
        RobotsRevalidationMode.FullGetWithoutValidators;

    public string ProfileSha256 { get; }

    /// <summary>
    /// Classifies retained UTC evidence against the RFC ceiling. This pure classification is not
    /// request admission; the transport owns both timestamps and the consume-and-send boundary.
    /// </summary>
    public RobotsPolicyFreshness EvaluateRobotsPolicyFreshness(
        DateTimeOffset observedAt,
        DateTimeOffset requestAt)
    {
        var age = requestAt.ToUniversalTime() - observedAt.ToUniversalTime();
        if (age < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestAt),
                "A request cannot precede the policy observation it relies on.");
        }

        return age < MaximumRobotsPolicyAge
            ? RobotsPolicyFreshness.Current
            : RobotsPolicyFreshness.Expired;
    }

    internal static OfficialMachineQuerySourceProfile LuxembourgSparql() => new(
        OfficialMachineQuerySourceProfileId.LuxembourgSparql,
        "https://data.legilux.public.lu/sparqlendpoint",
        "application/x-www-form-urlencoded",
        new RobotsPolicyRoute(
            new HttpOrigin("https", "data.legilux.public.lu", 443),
            new RobotsPolicyRouteStep(
                "https://data.legilux.public.lu/robots.txt",
                200,
                null)));

    internal static OfficialMachineQuerySourceProfile EuropeanUnionSparql() => new(
        OfficialMachineQuerySourceProfileId.EuropeanUnionSparql,
        "https://publications.europa.eu/webapi/rdf/sparql",
        "application/sparql-query",
        new RobotsPolicyRoute(
            new HttpOrigin("https", "publications.europa.eu", 443),
            new RobotsPolicyRouteStep(
                "https://publications.europa.eu/robots.txt",
                301,
                "https://op.europa.eu/robots.txt"),
            new RobotsPolicyRouteStep(
                "https://op.europa.eu/robots.txt",
                200,
                null)));

    private string ComputeSha256()
    {
        var lines = new List<string>
        {
            $"schema={CanonicalizationIdentity}",
            $"id={ProfileIdToken(Id)}",
            $"request_target={RequestTarget}",
            $"method=POST",
            $"request_content_type={RequestContentType}",
            $"request_charset=utf-8",
            $"accept={Accept}",
            $"crawler_user_agent={CrawlerUserAgent}",
            $"robots_product_token={RobotsProductToken}",
            $"robots_parser_identity={RobotsParserIdentity}",
            $"robots_freshness_basis={RobotsFreshnessBasis}",
            $"robots_maximum_age_ms={MaximumRobotsPolicyAge.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}",
            $"robots_revalidation=full_get_without_validators",
            $"minimum_request_interval_ms={MinimumRequestInterval.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}",
            $"pacing_scope=process_actual_network_origin",
            $"maximum_attempts={MaximumAttempts.ToString(CultureInfo.InvariantCulture)}",
            $"initial_retry_delay_ms={InitialRetryDelay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}",
            $"maximum_retry_delay_ms={MaximumRetryDelay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}",
            $"request_timeout_ms={RequestTimeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}",
            $"maximum_response_bytes={MaximumResponseBytes.ToString(CultureInfo.InvariantCulture)}",
            $"initial_origin={RobotsRoute.InitialAuthority.Scheme}://{RobotsRoute.InitialAuthority.Host}:{RobotsRoute.InitialAuthority.EffectivePort.ToString(CultureInfo.InvariantCulture)}",
        };

        lines.AddRange(RetryConditions.Select(
            static condition => $"retry_condition={RetryConditionToken(condition)}"));
        for (var index = 0; index < RobotsRoute.Steps.Count; index++)
        {
            var step = RobotsRoute.Steps[index];
            lines.Add($"route_{index.ToString(CultureInfo.InvariantCulture)}_uri={step.RequestedUri}");
            lines.Add($"route_{index.ToString(CultureInfo.InvariantCulture)}_status={step.ExpectedStatusCode.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"route_{index.ToString(CultureInfo.InvariantCulture)}_location={step.ExpectedLocation ?? string.Empty}");
        }

        var canonicalBytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
    }

    private static string ProfileIdToken(OfficialMachineQuerySourceProfileId id) => id switch
    {
        OfficialMachineQuerySourceProfileId.LuxembourgSparql => "luxembourg_sparql",
        OfficialMachineQuerySourceProfileId.EuropeanUnionSparql => "european_union_sparql",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static string RetryConditionToken(OfficialMachineQueryRetryCondition condition) =>
        condition switch
        {
            OfficialMachineQueryRetryCondition.RequestTimeout => "request_timeout",
            OfficialMachineQueryRetryCondition.TransportFailure => "transport_failure",
            OfficialMachineQueryRetryCondition.Http408 => "http_408",
            OfficialMachineQueryRetryCondition.Http429 => "http_429",
            OfficialMachineQueryRetryCondition.Http500 => "http_500",
            OfficialMachineQueryRetryCondition.Http502 => "http_502",
            OfficialMachineQueryRetryCondition.Http503 => "http_503",
            OfficialMachineQueryRetryCondition.Http504 => "http_504",
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };
}

public static class OfficialMachineQuerySourceProfiles
{
    internal static OfficialMachineQuerySourceProfile Resolve(
        OfficialMachineQuerySourceProfileId id) => id switch
        {
            OfficialMachineQuerySourceProfileId.LuxembourgSparql =>
                OfficialMachineQuerySourceProfile.LuxembourgSparql(),
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql =>
                OfficialMachineQuerySourceProfile.EuropeanUnionSparql(),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

    /// <summary>
    /// Derives the only matching profile from a fully bound query request. A caller never selects
    /// a broader regional policy or supplies request-authority facts.
    /// </summary>
    public static OfficialMachineQuerySourceProfile ResolveFor(BoundMachineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.RequestedUri switch
        {
            "https://data.legilux.public.lu/sparqlendpoint" =>
                Resolve(OfficialMachineQuerySourceProfileId.LuxembourgSparql),
            "https://publications.europa.eu/webapi/rdf/sparql" =>
                Resolve(OfficialMachineQuerySourceProfileId.EuropeanUnionSparql),
            _ => throw new ArgumentException(
                "The bound machine request does not target an admitted official query channel.",
                nameof(request)),
        };

        _ = request.CopyVerifiedRequestBody();
        var receipt = request.RenderReceipt;
        if (receipt.Method != profile.Method ||
            receipt.Charset != profile.RequestCharset ||
            !string.Equals(
                receipt.ContentType?.MemberKey,
                profile.RequestContentType,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The bound machine request representation differs from its exact source profile.",
                nameof(request));
        }

        return profile;
    }
}
