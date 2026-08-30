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

/// <summary>
/// A closed identity for one physical publisher request. Persisted URIs contain
/// no user info, query, or fragment. Their full raw targets are bound only by SHA-256.
/// </summary>
public sealed record SourceRequestIdentity
{
    public const int MaximumUriLength = 8192;
    public const int MaximumOrdinal = 999_999;
    public const int MaximumPhysicalAttempt = 16;

    private SourceRequestIdentity(
        string publisher,
        string channel,
        SourceRequestMethod method,
        string requestUri,
        string requestUriSha256,
        string? requestBodySha256,
        int ordinal,
        long maximumResponseBytes,
        int physicalAttempt,
        int redirectHop)
    {
        Publisher = TransportEvidenceValidation.RequireToken(
            publisher, 64, nameof(publisher));
        Channel = TransportEvidenceValidation.RequireToken(
            channel, 64, nameof(channel), allowUnderscore: true);
        if (!Enum.IsDefined(method))
            throw new InvalidDataException("Request method is not supported.");
        Method = method;
        RequestUri = TransportEvidenceValidation.RequireRedactedHttpsUri(
            requestUri, MaximumUriLength, nameof(requestUri));
        RequestUriSha256 = CodeIdentity.RequireSha256(
            requestUriSha256, nameof(requestUriSha256));
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
        if (maximumResponseBytes is < 1 or > EvidenceRef.MaximumByteLength)
            throw new InvalidDataException(
                "Maximum response bytes is outside its allowed bound.");
        MaximumResponseBytes = maximumResponseBytes;
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
    public string RequestUriSha256 { get; }
    public string? RequestBodySha256 { get; }
    public int Ordinal { get; }
    public long MaximumResponseBytes { get; }
    public int PhysicalAttempt { get; }
    public int RedirectHop { get; }

    public static SourceRequestIdentity Create(
        string publisher,
        string channel,
        SourceRequestMethod method,
        string requestUri,
        string? requestBodySha256,
        int ordinal,
        long maximumResponseBytes,
        int physicalAttempt = 1,
        int redirectHop = 0)
    {
        var redacted = TransportEvidenceValidation.RedactHttpsUri(
            requestUri, MaximumUriLength, nameof(requestUri));
        return new SourceRequestIdentity(
            publisher,
            channel,
            method,
            redacted,
            TransportEvidenceValidation.Sha256(requestUri),
            requestBodySha256,
            ordinal,
            maximumResponseBytes,
            physicalAttempt,
            redirectHop);
    }

    /// <summary>Reconstructs validated redacted metadata read from a private receipt.</summary>
    public static SourceRequestIdentity RestorePersisted(
        string publisher,
        string channel,
        SourceRequestMethod method,
        string requestUri,
        string requestUriSha256,
        string? requestBodySha256,
        int ordinal,
        long maximumResponseBytes,
        int physicalAttempt,
        int redirectHop) => new(
        publisher,
        channel,
        method,
        requestUri,
        requestUriSha256,
        requestBodySha256,
        ordinal,
        maximumResponseBytes,
        physicalAttempt,
        redirectHop);

    private string ComputeRequestId()
    {
        var canonical = string.Join('\n',
            "lex-source-request/2",
            Publisher,
            Channel,
            Method == SourceRequestMethod.Get ? "GET" : "POST",
            RequestUri,
            RequestUriSha256,
            RequestBodySha256 ?? string.Empty,
            Ordinal.ToString(CultureInfo.InvariantCulture),
            MaximumResponseBytes.ToString(CultureInfo.InvariantCulture),
            PhysicalAttempt.ToString(CultureInfo.InvariantCulture),
            RedirectHop.ToString(CultureInfo.InvariantCulture));
        return TransportEvidenceValidation.Sha256(canonical);
    }
}

/// <summary>Bounded response facts retained beside exact transport bytes.</summary>
public sealed record BoundedResponseMetadata
{
    private BoundedResponseMetadata(
        int statusCode,
        string? contentType,
        string? charset,
        string? entityTag,
        DateTimeOffset? lastModified,
        DateTimeOffset fetchedAt,
        string effectiveSourceUri,
        string effectiveSourceUriSha256,
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
        EffectiveSourceUri = TransportEvidenceValidation.RequireRedactedHttpsUri(
            effectiveSourceUri,
            SourceRequestIdentity.MaximumUriLength,
            nameof(effectiveSourceUri));
        EffectiveSourceUriSha256 = CodeIdentity.RequireSha256(
            effectiveSourceUriSha256, nameof(effectiveSourceUriSha256));
        BodyComplete = bodyComplete;
    }

    public int StatusCode { get; }
    public string? ContentType { get; }
    public string? Charset { get; }
    public string? EntityTag { get; }
    public DateTimeOffset? LastModified { get; }
    public DateTimeOffset FetchedAt { get; }
    public string EffectiveSourceUri { get; }
    public string EffectiveSourceUriSha256 { get; }
    public bool BodyComplete { get; }

    public static BoundedResponseMetadata Create(
        int statusCode,
        string? contentType,
        string? charset,
        string? entityTag,
        DateTimeOffset? lastModified,
        DateTimeOffset fetchedAt,
        string effectiveSourceUri,
        bool bodyComplete)
    {
        var redacted = TransportEvidenceValidation.RedactHttpsUri(
            effectiveSourceUri,
            SourceRequestIdentity.MaximumUriLength,
            nameof(effectiveSourceUri));
        return new BoundedResponseMetadata(
            statusCode,
            contentType,
            charset,
            entityTag,
            lastModified,
            fetchedAt,
            redacted,
            TransportEvidenceValidation.Sha256(effectiveSourceUri),
            bodyComplete);
    }

    /// <summary>Reconstructs validated redacted metadata read from a private receipt.</summary>
    public static BoundedResponseMetadata RestorePersisted(
        int statusCode,
        string? contentType,
        string? charset,
        string? entityTag,
        DateTimeOffset? lastModified,
        DateTimeOffset fetchedAt,
        string effectiveSourceUri,
        string effectiveSourceUriSha256,
        bool bodyComplete) => new(
        statusCode,
        contentType,
        charset,
        entityTag,
        lastModified,
        fetchedAt,
        effectiveSourceUri,
        effectiveSourceUriSha256,
        bodyComplete);

    public BoundedResponseMetadata MarkBodyIncomplete() => BodyComplete
        ? new BoundedResponseMetadata(
            StatusCode,
            ContentType,
            Charset,
            EntityTag,
            LastModified,
            FetchedAt,
            EffectiveSourceUri,
            EffectiveSourceUriSha256,
            bodyComplete: false)
        : this;
}

/// <summary>
/// A durable content reference. Only the authenticated durable sink implementation in
/// this assembly can issue one after create-only upload and full remote readback.
/// </summary>
public sealed record EvidenceRef
{
    public const long MaximumByteLength = 128L * 1024 * 1024;

    internal EvidenceRef(string requestId, string objectSha256, long byteLength)
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
/// Persists one physical response before returning. A successful return requires an
/// authenticated create-only remote upload followed by full SHA-256 and length readback.
/// Local staging types deliberately cannot implement this transition.
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

    public static string RedactHttpsUri(
        string? value, int maximumLength, string field)
    {
        var uri = ParseHttpsUri(value, maximumLength, field);
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    public static string RequireRedactedHttpsUri(
        string? value, int maximumLength, string field)
    {
        var uri = ParseHttpsUri(value, maximumLength, field);
        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException(
                $"{field} must not persist user info, query, or fragment.");
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

    public static string Sha256(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Uri ParseHttpsUri(
        string? value, int maximumLength, string field)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host))
            throw new InvalidDataException(
                $"{field} must be a bounded absolute HTTPS URI.");
        return uri;
    }
}
