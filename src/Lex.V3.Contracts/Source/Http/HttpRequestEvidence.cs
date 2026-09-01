using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum HttpRequestMethod
{
    [JsonStringEnumMemberName("GET")]
    Get = 1,

    [JsonStringEnumMemberName("POST")]
    Post = 2,
}

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
public sealed record HttpRequestEvidence
{
    [JsonConstructor]
    public HttpRequestEvidence(
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
        SourceArtifactRef queryPlanIdentity)
    {
        RequestedUri = RequireCanonicalRequestUri(requestedUri, nameof(requestedUri));
        Method = SourceCoreValidation.RequireDefined(method, nameof(method));
        ArgumentException.ThrowIfNullOrWhiteSpace(observedAtUtc);
        if (!DateTimeOffset.TryParseExact(
                observedAtUtc,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedTimestamp) ||
            !string.Equals(
                observedAtUtc,
                parsedTimestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An HTTP observation timestamp must use the exact millisecond UTC Z form.",
                nameof(observedAtUtc));
        }

        ObservedAtUtc = observedAtUtc;
        TimestampPrecision = SourceCoreValidation.RequireDefined(
            timestampPrecision,
            nameof(timestampPrecision));
        ClockSource = SourceCoreValidation.RequireDefined(clockSource, nameof(clockSource));
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        AdapterIdentity = adapterIdentity ?? throw new ArgumentNullException(nameof(adapterIdentity));
        RequestPolicyIdentity = requestPolicyIdentity
            ?? throw new ArgumentNullException(nameof(requestPolicyIdentity));
        RepresentationRequestKeyIdentity = representationRequestKeyIdentity
            ?? throw new ArgumentNullException(nameof(representationRequestKeyIdentity));
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

    public SourceArtifactRef QueryPlanIdentity { get; }

    internal static string RequireCanonicalRequestUri(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 4096 ||
            value.Any(static character => character is < '!' or > '~') ||
            !(value.StartsWith("http://", StringComparison.Ordinal) ||
              value.StartsWith("https://", StringComparison.Ordinal)) ||
            HasAuthorityUserInfoMarker(value) ||
            HasUnsafePathAlias(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            parsed.Query is not ("" or "?locale=en") ||
            !string.Equals(value, parsed.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A request URI must be the exact canonical HTTP target and carry no unrestricted query.",
                parameterName);
        }

        _ = new HttpOrigin(parsed.Scheme, parsed.Host, parsed.Port);
        return value;
    }

    private static bool HasAuthorityUserInfoMarker(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }

        return value.AsSpan(authorityStart, authorityEnd - authorityStart).Contains('@');
    }

    private static bool HasUnsafePathAlias(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = value.IndexOf('/', authorityStart);
        if (pathStart < 0)
        {
            return false;
        }

        var queryStart = value.IndexOfAny(['?', '#'], pathStart);
        var path = queryStart < 0
            ? value[pathStart..]
            : value[pathStart..queryStart];
        return path.Contains('\\') || HasEncodedPathAlias(path);
    }

    private static bool HasEncodedPathAlias(string path)
    {
        var candidate = path;
        while (true)
        {
            var decodedAny = false;
            var decoded = new StringBuilder(candidate.Length);
            for (var index = 0; index < candidate.Length; index++)
            {
                if (candidate[index] == '%' &&
                    index + 2 < candidate.Length &&
                    TryDecodeHexByte(candidate[index + 1], candidate[index + 2], out var value))
                {
                    decodedAny = true;
                    var character = (char)value;
                    if (character is '.' or '/' or '\\')
                    {
                        return true;
                    }

                    decoded.Append(character);
                    index += 2;
                }
                else
                {
                    decoded.Append(candidate[index]);
                }
            }

            if (!decodedAny)
            {
                return false;
            }

            candidate = decoded.ToString();
        }
    }

    private static bool TryDecodeHexByte(char first, char second, out byte value)
    {
        var high = HexValue(first);
        var low = HexValue(second);
        if (high < 0 || low < 0)
        {
            value = 0;
            return false;
        }

        value = (byte)((high << 4) | low);
        return true;
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };
}
