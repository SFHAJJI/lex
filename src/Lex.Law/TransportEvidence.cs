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
/// Physical-attempt and redirect-hop values are bounded reported coordinates.
/// The private journal does not enforce retry or redirect policy, stable chain
/// identity, ordering, or send count. A publisher session must do so before send.
/// </summary>
public sealed record SourceRequestIdentity
{
    public const int MaximumUriLength = 8192;
    public const int MaximumOrdinal = 999_999;
    public const int MaximumPhysicalAttemptCoordinate = 16;
    public const int MaximumRedirectHopCoordinate = 16;

    private readonly RecordedSourceRequest _recorded;

    private SourceRequestIdentity(RecordedSourceRequest recorded)
    {
        _recorded = recorded;
    }

    public string RequestId => _recorded.RequestId;
    public string Publisher => _recorded.Publisher;
    public string Channel => _recorded.Channel;
    public SourceRequestMethod Method => _recorded.Method;
    public string RequestUri => _recorded.RequestUri;
    public string RequestUriSha256 => _recorded.RequestUriSha256;
    public string? RequestBodySha256 => _recorded.RequestBodySha256;
    public int Ordinal => _recorded.Ordinal;
    public long MaximumResponseBytes => _recorded.MaximumResponseBytes;
    /// <summary>
    /// An individually bounded caller-reported coordinate, not a send count.
    /// The publisher session must enforce retry policy before sending.
    /// </summary>
    public int PhysicalAttempt => _recorded.PhysicalAttempt;

    /// <summary>
    /// An individually bounded caller-reported coordinate, not a redirect policy.
    /// The publisher session must enforce redirect policy before sending.
    /// </summary>
    public int RedirectHop => _recorded.RedirectHop;

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
        var requestUriSha256 = TransportEvidenceValidation.Sha256(requestUri);
        var requestId = TransportEvidenceValidation.ComputeRequestId(
            publisher,
            channel,
            method,
            redacted,
            requestUriSha256,
            requestBodySha256,
            ordinal,
            maximumResponseBytes,
            physicalAttempt,
            redirectHop);
        return new SourceRequestIdentity(
            RecordedSourceRequest.FromPersistedClaim(
                requestId,
                publisher,
                channel,
                method,
                redacted,
                requestUriSha256,
                requestBodySha256,
                ordinal,
                maximumResponseBytes,
                physicalAttempt,
                redirectHop));
    }

    public RecordedSourceRequest ToRecordedClaim() => _recorded;
}

/// <summary>
/// A non-authoritative request claim restored from a journal. Validation proves
/// bounded syntax and internal identifier consistency only, never that a request ran.
/// It cannot be converted into a live request identity.
/// </summary>
public sealed record RecordedSourceRequest
{
    private RecordedSourceRequest(
        string requestId,
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
        RequestId = CodeIdentity.RequireSha256(requestId, nameof(requestId));
        Publisher = TransportEvidenceValidation.RequireToken(
            publisher, 64, nameof(publisher));
        Channel = TransportEvidenceValidation.RequireToken(
            channel, 64, nameof(channel), allowUnderscore: true);
        if (!Enum.IsDefined(method))
            throw new InvalidDataException("Request method is not supported.");
        Method = method;
        RequestUri = TransportEvidenceValidation.RequireRedactedHttpsUri(
            requestUri, SourceRequestIdentity.MaximumUriLength, nameof(requestUri));
        RequestUriSha256 = CodeIdentity.RequireSha256(
            requestUriSha256, nameof(requestUriSha256));
        if (method == SourceRequestMethod.Get)
        {
            if (requestBodySha256 is not null)
                throw new InvalidDataException(
                    "A GET request claim cannot carry a request body digest.");
            RequestBodySha256 = null;
        }
        else
        {
            RequestBodySha256 = CodeIdentity.RequireSha256(
                requestBodySha256, nameof(requestBodySha256));
        }
        if (ordinal is < 0 or > SourceRequestIdentity.MaximumOrdinal)
            throw new InvalidDataException(
                $"Request ordinal must be between 0 and {SourceRequestIdentity.MaximumOrdinal}.");
        Ordinal = ordinal;
        if (maximumResponseBytes is < 1 or > EvidenceRef.MaximumByteLength)
            throw new InvalidDataException(
                "Maximum response bytes is outside its allowed bound.");
        MaximumResponseBytes = maximumResponseBytes;
        if (physicalAttempt is < 1
            or > SourceRequestIdentity.MaximumPhysicalAttemptCoordinate)
            throw new InvalidDataException(
                "Physical attempt coordinate must be between 1 and "
                + $"{SourceRequestIdentity.MaximumPhysicalAttemptCoordinate}.");
        PhysicalAttempt = physicalAttempt;
        if (redirectHop is < 0
            or > SourceRequestIdentity.MaximumRedirectHopCoordinate)
            throw new InvalidDataException(
                "Redirect hop coordinate must be between 0 and "
                + $"{SourceRequestIdentity.MaximumRedirectHopCoordinate}.");
        RedirectHop = redirectHop;
        var expectedRequestId = TransportEvidenceValidation.ComputeRequestId(
            Publisher,
            Channel,
            Method,
            RequestUri,
            RequestUriSha256,
            RequestBodySha256,
            Ordinal,
            MaximumResponseBytes,
            PhysicalAttempt,
            RedirectHop);
        if (RequestId != expectedRequestId)
            throw new InvalidDataException(
                "Recorded request ID does not match its claimed fields.");
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
    /// <summary>
    /// A bounded journal claim only. The journal does not enforce retry ordering,
    /// stable original-request chain identity, or send count.
    /// </summary>
    public int PhysicalAttempt { get; }

    /// <summary>
    /// A bounded journal claim only. The journal does not enforce redirect policy
    /// or ordering. The publisher session must enforce policy before sending.
    /// </summary>
    public int RedirectHop { get; }

    public static RecordedSourceRequest FromPersistedClaim(
        string requestId,
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
        requestId,
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

}

/// <summary>Bounded response facts retained beside exact transport bytes.</summary>
public sealed record BoundedResponseMetadata
{
    private readonly RecordedResponseMetadata _recorded;

    private BoundedResponseMetadata(RecordedResponseMetadata recorded)
    {
        _recorded = recorded;
    }

    public int StatusCode => _recorded.StatusCode;
    public string? ContentType => _recorded.ContentType;
    public string? Charset => _recorded.Charset;
    public string? EntityTag => _recorded.EntityTag;
    public DateTimeOffset? LastModified => _recorded.LastModified;
    public DateTimeOffset FetchedAt => _recorded.FetchedAt;
    public string EffectiveSourceUri => _recorded.EffectiveSourceUri;
    public string EffectiveSourceUriSha256 => _recorded.EffectiveSourceUriSha256;
    public bool BodyComplete => _recorded.BodyComplete;

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
            RecordedResponseMetadata.FromPersistedClaim(
                statusCode,
                contentType,
                charset,
                entityTag,
                lastModified,
                fetchedAt,
                redacted,
                TransportEvidenceValidation.Sha256(effectiveSourceUri),
                bodyComplete));
    }

    public RecordedResponseMetadata ToRecordedClaim() => _recorded;

    public BoundedResponseMetadata MarkBodyIncomplete() => BodyComplete
        ? new BoundedResponseMetadata(_recorded.MarkBodyIncomplete())
        : this;
}

/// <summary>
/// Non-authoritative response facts restored from a journal. Validation proves
/// bounded syntax only, never that a publisher emitted this response.
/// It cannot be converted into live response metadata.
/// </summary>
public sealed record RecordedResponseMetadata
{
    private RecordedResponseMetadata(
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

    public static RecordedResponseMetadata FromPersistedClaim(
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

    public RecordedResponseMetadata MarkBodyIncomplete() => BodyComplete
        ? new RecordedResponseMetadata(
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
/// A durable content reference. No issuer is implemented in the local-staging slice.
/// A durable sink may issue one only after create-only upload and full remote readback
/// match the pre-upload local length and digest. Once remote creation succeeds, a
/// readback failure remains one unverified object and must not mint another remote name.
/// </summary>
public sealed record EvidenceRef
{
    public const long MaximumByteLength = 128L * 1024 * 1024;

    private EvidenceRef(string requestId, string objectSha256, long byteLength)
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
/// This slice deliberately supplies no implementation or issuance factory. The durable
/// sink integration must add a separately reviewed non-public issuance capability and
/// preserve one remote object identity across readback retries.
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
    public static string ComputeRequestId(
        string publisher,
        string channel,
        SourceRequestMethod method,
        string requestUri,
        string requestUriSha256,
        string? requestBodySha256,
        int ordinal,
        long maximumResponseBytes,
        int physicalAttempt,
        int redirectHop) => Sha256(string.Join('\n',
        "lex-source-request/2",
        publisher,
        channel,
        method == SourceRequestMethod.Get ? "GET" : "POST",
        requestUri,
        requestUriSha256,
        requestBodySha256 ?? string.Empty,
        ordinal.ToString(CultureInfo.InvariantCulture),
        maximumResponseBytes.ToString(CultureInfo.InvariantCulture),
        physicalAttempt.ToString(CultureInfo.InvariantCulture),
        redirectHop.ToString(CultureInfo.InvariantCulture)));

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
