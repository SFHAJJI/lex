using System.Net;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

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
        SourceArtifactRef identityRef,
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
        IdentityRef = identityRef ?? throw new ArgumentNullException(nameof(identityRef));
        Token = token;
    }

    public string Schema { get; }

    public SourceArtifactRef IdentityRef { get; }

    public string Token { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpRequestEvidence
{
    [JsonConstructor]
    public HttpRequestEvidence(
        string requestedUri,
        SourceRegistryMemberRef method,
        DateTimeOffset observedAtUtc,
        SourceRegistryMemberRef timestampPrecision,
        SourceRegistryMemberRef clockSource,
        SourceArtifactRef runIdentity,
        SourceArtifactRef adapterIdentity,
        SourceArtifactRef requestPolicyIdentity,
        OutboundCrawlerIdentityEvidence outboundCrawlerIdentity,
        HttpOrigin origin,
        SourceArtifactRef queryPlanIdentity)
    {
        RequestedUri = SourceCoreValidation.RequirePublisherUri(requestedUri, nameof(requestedUri));
        Method = method ?? throw new ArgumentNullException(nameof(method));
        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("An HTTP observation timestamp must be UTC.", nameof(observedAtUtc));
        }

        ObservedAtUtc = observedAtUtc;
        TimestampPrecision = timestampPrecision
            ?? throw new ArgumentNullException(nameof(timestampPrecision));
        ClockSource = clockSource ?? throw new ArgumentNullException(nameof(clockSource));
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        AdapterIdentity = adapterIdentity ?? throw new ArgumentNullException(nameof(adapterIdentity));
        RequestPolicyIdentity = requestPolicyIdentity
            ?? throw new ArgumentNullException(nameof(requestPolicyIdentity));
        OutboundCrawlerIdentity = outboundCrawlerIdentity
            ?? throw new ArgumentNullException(nameof(outboundCrawlerIdentity));
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        QueryPlanIdentity = queryPlanIdentity
            ?? throw new ArgumentNullException(nameof(queryPlanIdentity));

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

    public SourceRegistryMemberRef Method { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public SourceRegistryMemberRef TimestampPrecision { get; }

    public SourceRegistryMemberRef ClockSource { get; }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef AdapterIdentity { get; }

    public SourceArtifactRef RequestPolicyIdentity { get; }

    public OutboundCrawlerIdentityEvidence OutboundCrawlerIdentity { get; }

    public HttpOrigin Origin { get; }

    public SourceArtifactRef QueryPlanIdentity { get; }
}
