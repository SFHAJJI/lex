using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum HttpObservationTimestampPrecision
{
    [JsonStringEnumMemberName("millisecond")]
    Millisecond = 1,
}

public enum HttpObservationClockSource
{
    [JsonStringEnumMemberName("system_utc")]
    SystemUtc = 1,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpOrigin
{
    [JsonConstructor]
    public HttpOrigin(string scheme, string host, int effectivePort)
    {
        if (scheme is not ("http" or "https"))
        {
            throw new ArgumentException("An HTTP origin scheme must be exact lowercase http or https.", nameof(scheme));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var labels = host.Split('.', StringSplitOptions.None);
        if (host.Length > 253 ||
            !string.Equals(host, host.ToLowerInvariant(), StringComparison.Ordinal) ||
            labels.Any(static label =>
                label.Length is 0 or > 63 ||
                label[0] == '-' || label[^1] == '-' ||
                label.Any(static character =>
                    character is not (>= 'a' and <= 'z') and
                    not (>= '0' and <= '9') and not '-')) ||
            IPAddress.TryParse(host, out _))
        {
            throw new ArgumentException(
                "An HTTP origin host must be one exact lowercase DNS name, not an address.",
                nameof(host));
        }

        if (effectivePort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(effectivePort));
        }

        Scheme = scheme;
        Host = host;
        EffectivePort = effectivePort;
    }

    public string Scheme { get; }

    public string Host { get; }

    public int EffectivePort { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OutboundCrawlerIdentityEvidence
{
    [JsonConstructor]
    public OutboundCrawlerIdentityEvidence(
        string schema,
        string token)
    {
        if (!string.Equals(schema, OutboundCrawlerIdentity.Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The crawler identity must declare {OutboundCrawlerIdentity.Schema}.",
                nameof(schema));
        }

        if (!string.Equals(token, OutboundCrawlerIdentity.Token, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The crawler identity token is fixed by its versioned public policy.",
                nameof(token));
        }

        Schema = schema;
        Token = token;
    }

    public string Schema { get; }

    public string Token { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpRequestTemplate
{
    [JsonConstructor]
    public HttpRequestTemplate(
        string requestedUri,
        HttpRequestMethod method,
        SourceArtifactRef runIdentity,
        SourceArtifactRef adapterIdentity,
        SourceArtifactRef requestPolicyIdentity,
        SourceArtifactRef representationRequestKeyIdentity,
        OutboundCrawlerIdentityEvidence outboundCrawlerIdentity,
        HttpOrigin origin,
        MachineQueryRenderReceipt renderReceipt)
    {
        RequestedUri = HttpRequestEvidence.RequireCanonicalRequestUri(
            requestedUri,
            nameof(requestedUri));
        Method = SourceCoreValidation.RequireDefined(method, nameof(method));
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        AdapterIdentity = adapterIdentity ?? throw new ArgumentNullException(nameof(adapterIdentity));
        RequestPolicyIdentity = requestPolicyIdentity
            ?? throw new ArgumentNullException(nameof(requestPolicyIdentity));
        RepresentationRequestKeyIdentity = representationRequestKeyIdentity
            ?? throw new ArgumentNullException(nameof(representationRequestKeyIdentity));
        OutboundCrawlerIdentity = outboundCrawlerIdentity
            ?? throw new ArgumentNullException(nameof(outboundCrawlerIdentity));
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        RenderReceipt = renderReceipt ?? throw new ArgumentNullException(nameof(renderReceipt));

        var requestTargetBytes = Encoding.ASCII.GetBytes(new Uri(RequestedUri).PathAndQuery);
        if (RenderReceipt.Method != Method ||
            RenderReceipt.RequestTargetLength != requestTargetBytes.LongLength ||
            !string.Equals(
                RenderReceipt.RequestTargetSha256,
                MachineQueryValidation.Sha256(requestTargetBytes),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The HTTP request tuple differs from its machine query render receipt.",
                nameof(renderReceipt));
        }

        var parsed = new Uri(RequestedUri, UriKind.Absolute);
        if (!string.Equals(parsed.Scheme, Origin.Scheme, StringComparison.Ordinal) ||
            !string.Equals(parsed.Host, Origin.Host, StringComparison.Ordinal) ||
            parsed.Port != Origin.EffectivePort)
        {
            throw new ArgumentException(
                "The retained origin must equal the requested URI scheme, host and effective port.",
                nameof(origin));
        }
    }

    public string RequestedUri { get; }

    public HttpRequestMethod Method { get; }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef AdapterIdentity { get; }

    public SourceArtifactRef RequestPolicyIdentity { get; }

    public SourceArtifactRef RepresentationRequestKeyIdentity { get; }

    public OutboundCrawlerIdentityEvidence OutboundCrawlerIdentity { get; }

    public HttpOrigin Origin { get; }

    public MachineQueryRenderReceipt RenderReceipt { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpRequestEvidence
{
    [JsonConstructor]
    private HttpRequestEvidence(
        string requestedUri,
        HttpRequestMethod method,
        string observedAtUtc,
        HttpObservationTimestampPrecision timestampPrecision,
        HttpObservationClockSource clockSource,
        SourceArtifactRef runIdentity,
        SourceArtifactRef adapterIdentity,
        SourceArtifactRef requestPolicyIdentity,
        SourceArtifactRef representationRequestKeyIdentity,
        OutboundCrawlerIdentityEvidence outboundCrawlerIdentity,
        HttpOrigin origin,
        MachineQueryRenderReceipt renderReceipt)
        : this(
            new HttpRequestTemplate(
                requestedUri,
                method,
                runIdentity,
                adapterIdentity,
                requestPolicyIdentity,
                representationRequestKeyIdentity,
                outboundCrawlerIdentity,
                origin,
                renderReceipt),
            RequireTimestamp(observedAtUtc, nameof(observedAtUtc)),
            SourceCoreValidation.RequireDefined(timestampPrecision, nameof(timestampPrecision)),
            SourceCoreValidation.RequireDefined(clockSource, nameof(clockSource)))
    {
    }

    private HttpRequestEvidence(
        HttpRequestTemplate template,
        string observedAtUtc,
        HttpObservationTimestampPrecision timestampPrecision,
        HttpObservationClockSource clockSource)
    {
        RequestedUri = template.RequestedUri;
        Method = template.Method;
        ObservedAtUtc = observedAtUtc;
        TimestampPrecision = timestampPrecision;
        ClockSource = clockSource;
        RunIdentity = template.RunIdentity;
        AdapterIdentity = template.AdapterIdentity;
        RequestPolicyIdentity = template.RequestPolicyIdentity;
        RepresentationRequestKeyIdentity = template.RepresentationRequestKeyIdentity;
        OutboundCrawlerIdentity = template.OutboundCrawlerIdentity;
        Origin = template.Origin;
        RenderReceipt = template.RenderReceipt;
    }

    public string RequestedUri { get; }

    public HttpRequestMethod Method { get; }

    public string ObservedAtUtc { get; }

    public HttpObservationTimestampPrecision TimestampPrecision { get; }

    public HttpObservationClockSource ClockSource { get; }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef AdapterIdentity { get; }

    public SourceArtifactRef RequestPolicyIdentity { get; }

    public SourceArtifactRef RepresentationRequestKeyIdentity { get; }

    public OutboundCrawlerIdentityEvidence OutboundCrawlerIdentity { get; }

    public HttpOrigin Origin { get; }

    public MachineQueryRenderReceipt RenderReceipt { get; }

    public static HttpRequestEvidence CreateAtSend(
        HttpRequestTemplate template,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return CreateAtSend(template, timeProvider.GetUtcNow());
    }

    internal static HttpRequestEvidence CreateAtSend(
        HttpRequestTemplate template,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(template);
        var timestamp = observedAtUtc
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return new HttpRequestEvidence(
            template,
            timestamp,
            HttpObservationTimestampPrecision.Millisecond,
            HttpObservationClockSource.SystemUtc);
    }

    private static string RequireTimestamp(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedTimestamp) ||
            !string.Equals(
                value,
                parsedTimestamp.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An HTTP observation timestamp must use the exact millisecond UTC Z form.",
                parameterName);
        }

        return value;
    }

    internal static string RequireCanonicalRequestUri(string value, string parameterName) =>
        MachineQueryValidation.RequireRenderedRequestTarget(value, parameterName);
}
