using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.Temporal;

namespace Lex.Law;

public enum SourceRequestMethod
{
    Get = 1,
    Post = 2,
}

/// <summary>A closed identity for one publisher request whose response can be replayed.</summary>
public sealed record SourceRequestIdentity
{
    public const int MaximumUriLength = 8192;
    public const int MaximumOrdinal = 999_999;
    public const int MaximumPhysicalAttempt = 16;

    public SourceRequestIdentity(
        string publisher,
        string channel,
        SourceRequestMethod method,
        string requestUri,
        string? requestBodySha256,
        int ordinal,
        int physicalAttempt = 1,
        int redirectHop = 0)
    {
        Publisher = TransportEvidenceValidation.RequireToken(
            publisher, 64, nameof(publisher));
        Channel = TransportEvidenceValidation.RequireToken(
            channel, 64, nameof(channel), allowUnderscore: true);
        if (!Enum.IsDefined(method))
            throw new InvalidDataException("Request method is not supported.");
        Method = method;
        RequestUri = TransportEvidenceValidation.RequireHttpsUri(
            requestUri, MaximumUriLength, nameof(requestUri));
        if (method == SourceRequestMethod.Get)
        {
            if (requestBodySha256 is not null)
                throw new InvalidDataException(
                    "A GET request identity cannot carry a request body digest.");
            RequestBodySha256 = null;
        }
        else
        {
            RequestBodySha256 = CodeIdentity.RequireSha256(
                requestBodySha256, nameof(requestBodySha256));
        }

        if (ordinal is < 0 or > MaximumOrdinal)
            throw new InvalidDataException(
                $"Request ordinal must be between 0 and {MaximumOrdinal}.");
        Ordinal = ordinal;
        if (physicalAttempt is < 1 or > MaximumPhysicalAttempt)
            throw new InvalidDataException(
                $"Physical attempt must be between 1 and {MaximumPhysicalAttempt}.");
        PhysicalAttempt = physicalAttempt;
        if (redirectHop is < 0 or > MaximumPhysicalAttempt)
            throw new InvalidDataException(
                $"Redirect hop must be between 0 and {MaximumPhysicalAttempt}.");
        RedirectHop = redirectHop;
        RequestId = ComputeRequestId();
    }

    public string RequestId { get; }
    public string Publisher { get; }
    public string Channel { get; }
    public SourceRequestMethod Method { get; }
    public string RequestUri { get; }
    public string? RequestBodySha256 { get; }
    public int Ordinal { get; }
    public int PhysicalAttempt { get; }
    public int RedirectHop { get; }

    private string ComputeRequestId()
    {
        var canonical = string.Join('\n',
            "lex-source-request/1",
            Publisher,
            Channel,
            Method == SourceRequestMethod.Get ? "GET" : "POST",
            RequestUri,
            RequestBodySha256 ?? string.Empty,
            Ordinal.ToString(CultureInfo.InvariantCulture),
            PhysicalAttempt.ToString(CultureInfo.InvariantCulture),
            RedirectHop.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Bounded response facts retained beside exact transport bytes.</summary>
public sealed record BoundedResponseMetadata
{
    public BoundedResponseMetadata(
        int statusCode,
        string? contentType,
        string? charset,
        string? entityTag,
        DateTimeOffset? lastModified,
        DateTimeOffset fetchedAt,
        string effectiveSourceUri,
        bool bodyComplete)
    {
        if (statusCode is < 100 or > 599)
            throw new InvalidDataException(
                "Response status code must be between 100 and 599.");
        StatusCode = statusCode;
        ContentType = TransportEvidenceValidation.RequireOptionalHeaderValue(
            contentType, 256, nameof(contentType));
        Charset = TransportEvidenceValidation.RequireOptionalHeaderValue(
            charset, 64, nameof(charset));
        EntityTag = TransportEvidenceValidation.RequireOptionalHeaderValue(
            entityTag, 512, nameof(entityTag));
        LastModified = TransportEvidenceValidation.RequireOptionalUtc(
            lastModified, nameof(lastModified));
        FetchedAt = TransportEvidenceValidation.RequireUtc(
            fetchedAt, nameof(fetchedAt));
        EffectiveSourceUri = TransportEvidenceValidation.RequireHttpsUri(
            effectiveSourceUri, SourceRequestIdentity.MaximumUriLength,
            nameof(effectiveSourceUri));
        BodyComplete = bodyComplete;
    }

    public int StatusCode { get; }
    public string? ContentType { get; }
    public string? Charset { get; }
    public string? EntityTag { get; }
    public DateTimeOffset? LastModified { get; }
    public DateTimeOffset FetchedAt { get; }
    public string EffectiveSourceUri { get; }
    public bool BodyComplete { get; }
}

/// <summary>A content-addressed reference. Bytes are opened only by a verified bundle.</summary>
public sealed record EvidenceRef
{
    public const long MaximumByteLength = 128L * 1024 * 1024;

    public EvidenceRef(string requestId, string objectSha256, long byteLength)
    {
        RequestId = CodeIdentity.RequireSha256(requestId, nameof(requestId));
        ObjectSha256 = CodeIdentity.RequireSha256(
            objectSha256, nameof(objectSha256));
        if (byteLength is < 0 or > MaximumByteLength)
            throw new InvalidDataException(
                "Evidence byte length is outside its allowed bound.");
        ByteLength = byteLength;
    }

    public string RequestId { get; }
    public string ObjectSha256 { get; }
    public long ByteLength { get; }
}

/// <summary>
/// Persists one physical HTTP response before returning. A successful return means the
/// exact bytes were uploaded to private storage, read back, and verified.
/// </summary>
public interface IRawResponseSink
{
    Task<EvidenceRef> CaptureAsync(
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        Stream body,
        CancellationToken cancellationToken = default);
}

/// <summary>The only byte-opening boundary available to publisher parsers.</summary>
public interface IVerifiedResponseSet
{
    Stream OpenBody(EvidenceRef evidence);
}

internal static class TransportEvidenceValidation
{
    public static string RequireToken(
        string? value,
        int maximumLength,
        string field,
        bool allowUnderscore = false)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength
            || value[0] is not (>= 'a' and <= 'z')
            || value.Any(character =>
                character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'
                && !(allowUnderscore && character == '_')))
            throw new InvalidDataException(
                $"{field} must be a bounded lowercase ASCII token.");
        return value;
    }

    public static string RequireHttpsUri(
        string? value, int maximumLength, string field)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException(
                $"{field} must be a bounded absolute HTTPS URI without user info or fragment.");
        return uri.AbsoluteUri;
    }

    public static string? RequireOptionalHeaderValue(
        string? value, int maximumLength, string field)
    {
        if (value is null) return null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength
            || value.Any(character => character is < ' ' or > '~'))
            throw new InvalidDataException(
                $"{field} must be a bounded visible ASCII value when present.");
        return value;
    }

    public static DateTimeOffset RequireUtc(DateTimeOffset value, string field)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new InvalidDataException($"{field} must use UTC.");
        return value;
    }

    public static DateTimeOffset? RequireOptionalUtc(
        DateTimeOffset? value, string field) =>
        value is null ? null : RequireUtc(value.Value, field);
}
