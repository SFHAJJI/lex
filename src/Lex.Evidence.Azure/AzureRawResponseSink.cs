using Azure.Core;
using Lex.Law;
using System.Globalization;
using System.Security.Cryptography;

namespace Lex.Evidence.Azure;

public enum EvidenceRetentionLane
{
    Nightly90Days = 1,
    EvidenceReleaseIndefinite = 2,
}

public sealed class AzureRawResponseSink : IRawResponseSink
{
    internal static readonly TimeSpan OverallDeadline = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan RetentionClockTolerance = TimeSpan.FromMinutes(1);
    internal const int MaximumAttempts = 3;

    private readonly IAzureRawEvidenceStore _store;
    private readonly EvidenceRetentionLane _retentionLane;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _overallDeadline;

    public AzureRawResponseSink(
        Uri containerUri,
        TokenCredential credential,
        EvidenceRetentionLane retentionLane)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _retentionLane = RequireRetentionLane(retentionLane);
        _store = new AzureBlobRawEvidenceStore(
            RequireSafeContainerUri(containerUri), credential);
        _timeProvider = TimeProvider.System;
        _overallDeadline = OverallDeadline;
    }

    internal AzureRawResponseSink(
        IAzureRawEvidenceStore store,
        EvidenceRetentionLane retentionLane,
        TimeProvider? timeProvider = null,
        TimeSpan? overallDeadline = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _retentionLane = RequireRetentionLane(retentionLane);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _overallDeadline = overallDeadline ?? OverallDeadline;
        if (_overallDeadline <= TimeSpan.Zero
            || _overallDeadline > OverallDeadline)
            throw new ArgumentOutOfRangeException(nameof(overallDeadline));
    }

    public async Task<EvidenceRef> CaptureAsync(
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        await CaptureVerifiedAsync(
            request, response, body, cancellationToken).ConfigureAwait(false);
        throw new NotSupportedException(
            "EvidenceRef issuance awaits the reviewed Lex.Law authority boundary.");
    }

    internal async Task<AzureVerifiedEvidence> CaptureVerifiedAsync(
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(body);
        if (!response.BodyComplete)
            throw new InvalidDataException(
                "Incomplete publisher bytes cannot become durable evidence.");
        bool canRead;
        try
        {
            canRead = body.CanRead;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Publisher response inspection was canceled.",
                innerException: null,
                token: cancellationToken);
        }
        catch
        {
            throw new IOException(
                "Publisher response bytes could not be inspected.");
        }
        if (!canRead)
            throw new InvalidDataException("The publisher body is not readable.");

        using var deadline = new CancellationTokenSource(
            _overallDeadline, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadline.Token);
        var token = linked.Token;

        try
        {
            return await CaptureWithinDeadlineAsync(
                request, response, body, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Raw evidence capture was canceled by the caller.",
                innerException: null,
                token: cancellationToken);
        }
        catch (OperationCanceledException)
            when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Raw evidence capture exceeded its two-minute deadline.");
        }
    }

    private async Task<AzureVerifiedEvidence> CaptureWithinDeadlineAsync(
        SourceRequestIdentity request,
        BoundedResponseMetadata response,
        Stream body,
        CancellationToken token)
    {
        await using var buffered = await BufferBodyAsync(
            body, request.MaximumResponseBytes, token).ConfigureAwait(false);
        var blobName = BlobName(_retentionLane, request.RequestId, buffered.Sha256);
        var metadata = Metadata(
            _retentionLane, request.RequestId, buffered.Sha256, buffered.Length);
        var retention = RetentionRequest(
            _retentionLane, response.FetchedAt, _timeProvider.GetUtcNow());
        AzureEvidenceObjectVersion? version = null;
        var createAttempted = false;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (version is null)
                {
                    if (!createAttempted)
                    {
                        createAttempted = true;
                        buffered.Content.Position = 0;
                        try
                        {
                            version = await _store.CreateOnlyAsync(
                                blobName,
                                buffered.Content,
                                metadata,
                                retention,
                                token).ConfigureAwait(false);
                        }
                        catch (AzureEvidenceStoreException error)
                            when (error.Kind is AzureEvidenceStoreFailureKind.AlreadyExists
                                or AzureEvidenceStoreFailureKind.Ambiguous)
                        {
                            version = await _store.ResolveCurrentVersionAsync(
                                blobName, token).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        version = await _store.ResolveCurrentVersionAsync(
                            blobName, token).ConfigureAwait(false);
                    }
                }

                var readback = await _store.ReadbackAsync(
                    blobName, version, token).ConfigureAwait(false);
                Exception? verificationFailure = null;
                try
                {
                    await VerifyReadbackAsync(
                        readback, version, buffered, metadata, token)
                        .ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    verificationFailure = error;
                    throw;
                }
                finally
                {
                    try
                    {
                        await readback.DisposeAsync().ConfigureAwait(false);
                    }
                    catch when (verificationFailure is not null)
                    {
                        // The definitive verification failure has precedence.
                    }
                }

                var retentionFacts = await _store.ReadRetentionAsync(
                    blobName, version, token).ConfigureAwait(false);
                VerifyRetention(retention, version, retentionFacts);

                return new AzureVerifiedEvidence(
                    request.RequestId, buffered.Sha256, buffered.Length);
            }
            catch (AzureEvidenceStoreException error)
                when (error.Kind is AzureEvidenceStoreFailureKind.Ambiguous)
            {
                // The same blob name and, once known, the same version are retried.
                if (attempt == MaximumAttempts) break;
            }
            catch (AzureEvidenceStoreException error)
                when (error.Kind is AzureEvidenceStoreFailureKind.Rejected)
            {
                throw new IOException(
                    "Azure rejected the raw evidence operation.");
            }
            catch (AzureEvidenceStoreException error)
                when (error.Kind is AzureEvidenceStoreFailureKind.AlreadyExists)
            {
                throw new IOException(
                    "Azure returned an invalid conflict after evidence creation.");
            }
        }

        throw new IOException(
            "Raw evidence could not be verified after three attempts.");
    }

    private static async Task<BufferedBody> BufferBodyAsync(
        Stream body,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var content = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        try
        {
            while (true)
            {
                var read = await body.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                if (length > maximumBytes - read)
                    throw new InvalidDataException(
                        "The publisher body exceeds its declared byte bound.");
                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                length += read;
            }

            content.Position = 0;
            return new BufferedBody(
                content,
                length,
                Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch (OperationCanceledException)
        {
            await content.DisposeAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(
                    "Publisher response buffering was canceled.",
                    innerException: null,
                    token: cancellationToken);
            throw new IOException(
                "Publisher response bytes could not be buffered.");
        }
        catch (InvalidDataException)
        {
            await content.DisposeAsync().ConfigureAwait(false);
            throw new InvalidDataException(
                "Publisher response bytes could not be buffered.");
        }
        catch
        {
            await content.DisposeAsync().ConfigureAwait(false);
            throw new IOException(
                "Publisher response bytes could not be buffered.");
        }
    }

    private static async Task VerifyReadbackAsync(
        AzureEvidenceReadback readback,
        AzureEvidenceObjectVersion expectedVersion,
        BufferedBody expectedBody,
        IReadOnlyDictionary<string, string> expectedMetadata,
        CancellationToken cancellationToken)
    {
        bool canRead;
        try
        {
            canRead = readback.Content.CanRead;
        }
        catch (Exception error)
        {
            throw SafeReadbackFailure(error, cancellationToken);
        }

        if (!canRead
            || readback.ContentLength != expectedBody.Length
            || !string.Equals(readback.VersionId, expectedVersion.VersionId,
                StringComparison.Ordinal)
            || !string.Equals(readback.ETag, expectedVersion.ETag,
                StringComparison.Ordinal)
            || !MetadataMatches(readback.Metadata, expectedMetadata))
            throw new InvalidDataException(
                "Remote evidence properties did not match the captured body.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        while (true)
        {
            int read;
            try
            {
                read = await readback.Content.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error)
            {
                throw SafeReadbackFailure(error, cancellationToken);
            }

            if (read == 0) break;
            if (length > expectedBody.Length - read)
                throw new InvalidDataException(
                    "Remote evidence readback exceeded the captured length.");
            hash.AppendData(buffer, 0, read);
            length += read;
        }

        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (length != expectedBody.Length
            || !string.Equals(digest, expectedBody.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Remote evidence bytes did not match the captured body.");
    }

    private static bool MetadataMatches(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
    {
        if (actual.Count != expected.Count) return false;
        foreach (var pair in expected)
        {
            var match = actual.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, pair.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (match.Key is null
                || !string.Equals(match.Value, pair.Value, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static Exception SafeReadbackFailure(
        Exception error,
        CancellationToken cancellationToken) =>
        error is OperationCanceledException
            && cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(
                    "Azure evidence readback was canceled.",
                    innerException: null,
                    token: cancellationToken)
                : new AzureEvidenceStoreException(
                    AzureEvidenceStoreFailureKind.Ambiguous);

    private static void VerifyRetention(
        AzureEvidenceRetentionRequest expected,
        AzureEvidenceObjectVersion version,
        AzureEvidenceRetentionFacts actual)
    {
        if (!string.Equals(actual.VersionId, version.VersionId,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Azure returned retention facts for a different blob version.");

        if (expected.Lane == EvidenceRetentionLane.Nightly90Days)
        {
            if (expected.ImmutableUntil is null
                || expected.ImmutableUntilMaximum is null
                || actual.ImmutableUntil is null
                || actual.ImmutableUntil < expected.ImmutableUntil
                || actual.ImmutableUntil > expected.ImmutableUntilMaximum
                || actual.ImmutabilityMode != "Locked")
                throw new InvalidDataException(
                    "Azure did not confirm the nightly immutability policy.");
            return;
        }

        if (!actual.HasLegalHold)
            throw new InvalidDataException(
                "Azure did not confirm the release evidence legal hold.");
    }

    private static Uri RequireSafeContainerUri(Uri? value)
    {
        if (value is null
            || !value.IsAbsoluteUri
            || !string.Equals(value.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(value.Host)
            || !string.IsNullOrEmpty(value.UserInfo)
            || !string.IsNullOrEmpty(value.Query)
            || !string.IsNullOrEmpty(value.Fragment))
            throw new ArgumentException(
                "The evidence container must be an absolute HTTPS URI without credentials, query, or fragment.",
                nameof(value));
        return value;
    }

    private static EvidenceRetentionLane RequireRetentionLane(
        EvidenceRetentionLane value) => value switch
        {
            EvidenceRetentionLane.Nightly90Days => value,
            EvidenceRetentionLane.EvidenceReleaseIndefinite => value,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static AzureEvidenceRetentionRequest RetentionRequest(
        EvidenceRetentionLane lane,
        DateTimeOffset fetchedAt,
        DateTimeOffset observedNow) => lane switch
        {
            EvidenceRetentionLane.Nightly90Days => new(
                lane,
                CeilingToWholeUtcSecond(fetchedAt.AddDays(90)),
                CeilingToWholeUtcSecond(
                    observedNow.AddDays(90).Add(RetentionClockTolerance))),
            EvidenceRetentionLane.EvidenceReleaseIndefinite =>
                new(lane, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(lane)),
        };

    private static DateTimeOffset CeilingToWholeUtcSecond(
        DateTimeOffset value)
    {
        var remainder = value.Ticks % TimeSpan.TicksPerSecond;
        if (remainder == 0) return value;
        return value.AddTicks(TimeSpan.TicksPerSecond - remainder);
    }

    private static string BlobName(
        EvidenceRetentionLane lane,
        string requestId,
        string objectSha256) =>
        $"{LaneToken(lane)}/{requestId}/{objectSha256}";

    private static IReadOnlyDictionary<string, string> Metadata(
        EvidenceRetentionLane lane,
        string requestId,
        string objectSha256,
        long byteLength) => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema"] = "lex-raw-response-1",
            ["request_id"] = requestId,
            ["object_sha256"] = objectSha256,
            ["byte_length"] = byteLength.ToString(CultureInfo.InvariantCulture),
            ["retention_lane"] = LaneToken(lane),
        };

    private static string LaneToken(EvidenceRetentionLane lane) => lane switch
    {
        EvidenceRetentionLane.Nightly90Days => "nightly_90d",
        EvidenceRetentionLane.EvidenceReleaseIndefinite =>
            "evidence_release_indefinite",
        _ => throw new ArgumentOutOfRangeException(nameof(lane)),
    };

    private sealed class BufferedBody(
        MemoryStream content, long length, string sha256) : IAsyncDisposable
    {
        public MemoryStream Content { get; } = content;
        public long Length { get; } = length;
        public string Sha256 { get; } = sha256;
        public ValueTask DisposeAsync() => Content.DisposeAsync();
    }

}

internal sealed record AzureVerifiedEvidence(
    string RequestId,
    string ObjectSha256,
    long ByteLength);

internal enum AzureEvidenceStoreFailureKind
{
    AlreadyExists,
    Ambiguous,
    Rejected,
}

internal sealed class AzureEvidenceStoreException(
    AzureEvidenceStoreFailureKind kind) : IOException("Evidence storage operation failed.")
{
    public AzureEvidenceStoreFailureKind Kind { get; } = kind;
}

internal sealed record AzureEvidenceObjectVersion(string VersionId, string ETag);

internal sealed class AzureEvidenceReadback(
    Stream content,
    long contentLength,
    IReadOnlyDictionary<string, string> metadata,
    string versionId,
    string etag) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public long ContentLength { get; } = contentLength;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public string VersionId { get; } = versionId;
    public string ETag { get; } = etag;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Content.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            throw new AzureEvidenceStoreException(
                AzureEvidenceStoreFailureKind.Ambiguous);
        }
    }
}

internal sealed record AzureEvidenceRetentionRequest(
    EvidenceRetentionLane Lane,
    DateTimeOffset? ImmutableUntil,
    DateTimeOffset? ImmutableUntilMaximum);

internal sealed record AzureEvidenceRetentionFacts(
    string VersionId,
    DateTimeOffset? ImmutableUntil,
    string? ImmutabilityMode,
    bool HasLegalHold);

internal interface IAzureRawEvidenceStore
{
    Task<AzureEvidenceObjectVersion> CreateOnlyAsync(
        string blobName,
        Stream content,
        IReadOnlyDictionary<string, string> metadata,
        AzureEvidenceRetentionRequest retention,
        CancellationToken cancellationToken);

    Task<AzureEvidenceObjectVersion> ResolveCurrentVersionAsync(
        string blobName,
        CancellationToken cancellationToken);

    Task<AzureEvidenceReadback> ReadbackAsync(
        string blobName,
        AzureEvidenceObjectVersion version,
        CancellationToken cancellationToken);

    Task<AzureEvidenceRetentionFacts> ReadRetentionAsync(
        string blobName,
        AzureEvidenceObjectVersion version,
        CancellationToken cancellationToken);
}
