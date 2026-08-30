using Azure;
using Azure.Core;
using Azure.Storage.Blobs.Models;
using Lex.Evidence.Azure;
using Lex.Law;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Lex.Tests;

public sealed class AzureRawResponseSinkTests
{
    [Fact]
    public void Azure_client_is_primary_only_with_SDK_retries_and_content_logging_disabled()
    {
        var options = AzureBlobRawEvidenceStore.CreateClientOptions();

        Assert.Equal(0, options.Retry.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Retry.NetworkTimeout);
        Assert.Null(options.GeoRedundantSecondaryUri);
        Assert.False(options.Diagnostics.IsLoggingContentEnabled);
        Assert.Empty(options.Diagnostics.LoggedHeaderNames);
        Assert.Empty(options.Diagnostics.LoggedQueryParameters);
    }

    [Fact]
    public void Azure_nightly_policy_is_version_level_unlocked_until_the_requested_instant()
    {
        var until = new DateTimeOffset(
            2026, 11, 28, 4, 0, 0, TimeSpan.Zero);

        var policy = AzureBlobRawEvidenceStore.CreateImmutabilityPolicy(until);

        Assert.Equal(until, policy.ExpiresOn);
        Assert.Equal(BlobImmutabilityPolicyMode.Unlocked, policy.PolicyMode);
    }

    [Theory]
    [InlineData("http://account.blob.core.windows.net/evidence")]
    [InlineData("https://user:password@account.blob.core.windows.net/evidence")]
    [InlineData("https://account.blob.core.windows.net/evidence?sig=credential-secret")]
    [InlineData("https://account.blob.core.windows.net/evidence#fragment")]
    public void Container_URI_rejects_non_https_credentials_queries_and_fragments(
        string uri)
    {
        Assert.Throws<ArgumentException>(() => new AzureRawResponseSink(
            new Uri(uri),
            new NeverCredential(),
            EvidenceRetentionLane.Nightly90Days));
    }

    [Fact]
    public void Azure_upload_is_create_only_and_readback_is_ETag_conditioned()
    {
        var metadata = new Dictionary<string, string>
        {
            ["schema"] = "lex-raw-response-1",
        };

        var upload = AzureBlobRawEvidenceStore.CreateUploadOptions(metadata);
        var download = AzureBlobRawEvidenceStore.CreateDownloadOptions("\"etag-1\"");

        Assert.Equal(ETag.All, upload.Conditions.IfNoneMatch);
        Assert.Equal(metadata, upload.Metadata);
        Assert.Equal(EvidenceRef.MaximumByteLength,
            upload.TransferOptions.InitialTransferSize);
        Assert.Equal(EvidenceRef.MaximumByteLength,
            upload.TransferOptions.MaximumTransferSize);
        Assert.Equal(1, upload.TransferOptions.MaximumConcurrency);
        Assert.Equal(new ETag("\"etag-1\""), download.Conditions.IfMatch);
    }

    [Theory]
    [InlineData(404, true, (int)AzureEvidenceStoreFailureKind.Ambiguous)]
    [InlineData(404, false, (int)AzureEvidenceStoreFailureKind.Rejected)]
    [InlineData(412, false, (int)AzureEvidenceStoreFailureKind.AlreadyExists)]
    [InlineData(500, false, (int)AzureEvidenceStoreFailureKind.Ambiguous)]
    [InlineData(403, false, (int)AzureEvidenceStoreFailureKind.Rejected)]
    public void Azure_failure_mapping_is_bounded_and_sanitized(
        int status,
        bool missingIsAmbiguous,
        int expectedKind)
    {
        const string secret = "credential-secret-in-sdk-error";

        var mapped = Assert.IsType<AzureEvidenceStoreException>(
            AzureBlobRawEvidenceStore.MapFailure(
                new RequestFailedException(status, secret),
                missingIsAmbiguous));

        Assert.Equal((AzureEvidenceStoreFailureKind)expectedKind, mapped.Kind);
        Assert.DoesNotContain(secret, mapped.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incomplete_response_is_rejected_before_any_Azure_call()
    {
        var store = new RecordingStore();
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);
        var request = Request();
        var response = Response(bodyComplete: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureAsync(request, response, new MemoryStream([1, 2, 3])));

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Oversized_response_is_rejected_before_any_Azure_call()
    {
        var store = new RecordingStore();
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(maximumBytes: 2),
                Response(),
                new MemoryStream([1, 2, 3])));

        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Publisher_stream_errors_are_sanitized_before_any_Azure_call()
    {
        var store = new RecordingStore();
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);
        const string secret = "private=query-and-body-secret";

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new ThrowingReadStream(secret)));

        Assert.Equal("Publisher response bytes could not be buffered.", error.Message);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Successful_capture_has_no_existence_preflight_and_uses_safe_deterministic_projection()
    {
        var bytes = Encoding.UTF8.GetBytes("raw-body-secret");
        var store = new RecordingStore();
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        var verified = await sink.CaptureVerifiedAsync(
            Request(), Response(entityTag: "\"publisher-header-secret\""),
            new MemoryStream(bytes));

        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var expectedName = $"nightly_90d/{verified.RequestId}/{digest}";
        Assert.Equal(digest, verified.ObjectSha256);
        Assert.Equal(bytes.Length, verified.ByteLength);
        Assert.Equal(["create", "readback", "retention"], store.Calls);
        Assert.Equal(expectedName, store.BlobNames.Distinct().Single());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["schema"] = "lex-raw-response-1",
                ["request_id"] = verified.RequestId,
                ["object_sha256"] = digest,
                ["byte_length"] = bytes.Length.ToString(),
                ["retention_lane"] = "nightly_90d",
            },
            store.UploadMetadata);

        var projection = expectedName + string.Join(string.Empty,
            store.UploadMetadata!.Select(pair => pair.Key + pair.Value));
        Assert.DoesNotContain("private=query", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-body-secret", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("publisher-header-secret", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("publisher.invalid", projection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_input_maps_to_the_same_blob_name()
    {
        var bytes = Encoding.UTF8.GetBytes("same body");
        var first = new RecordingStore();
        var second = new RecordingStore();

        await new AzureRawResponseSink(first, EvidenceRetentionLane.Nightly90Days)
            .CaptureVerifiedAsync(Request(), Response(), new MemoryStream(bytes));
        await new AzureRawResponseSink(second, EvidenceRetentionLane.Nightly90Days)
            .CaptureVerifiedAsync(Request(), Response(), new MemoryStream(bytes));

        Assert.Equal(first.BlobNames[0], second.BlobNames[0]);
    }

    [Theory]
    [InlineData((int)AzureEvidenceStoreFailureKind.Ambiguous)]
    [InlineData((int)AzureEvidenceStoreFailureKind.AlreadyExists)]
    public async Task Uncertain_or_existing_create_resolves_and_reads_the_same_object(
        int kind)
    {
        var store = new RecordingStore();
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            (AzureEvidenceStoreFailureKind)kind));
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(
            ["create", "resolve", "readback", "retention"], store.Calls);
        Assert.Single(store.BlobNames.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Readback_retry_never_recreates_or_renames_a_known_version()
    {
        var store = new RecordingStore();
        store.ReadbackFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(
            ["create", "readback", "readback", "retention"], store.Calls);
        Assert.Equal(1, store.CreateCount);
        Assert.Single(store.BlobNames.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Ambiguous_create_with_missing_readback_retries_the_same_create_name()
    {
        var store = new RecordingStore();
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        store.ResolveFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(
            ["create", "resolve", "create", "readback", "retention"],
            store.Calls);
        Assert.Equal(2, store.CreateCount);
        Assert.Single(store.BlobNames.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Deferred_readback_stream_errors_are_sanitized_and_bounded()
    {
        const string secret = "azure-uri-query-secret";
        var store = new RecordingStore
        {
            ReadbackStreamFactory = () => new ThrowingReadStream(secret),
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([4, 5, 6])));

        Assert.Equal(1, store.CreateCount);
        Assert.Equal(3, store.ReadbackCount);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remote_byte_digest_mismatch_fails_closed()
    {
        var store = new RecordingStore { RemoteBytes = [9, 9, 9] };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(["create", "readback"], store.Calls);
    }

    [Fact]
    public async Task Remote_length_mismatch_fails_closed()
    {
        var store = new RecordingStore { ReportedLength = 999 };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(["create", "readback"], store.Calls);
    }

    [Fact]
    public async Task Remote_metadata_mismatch_fails_closed()
    {
        var store = new RecordingStore
        {
            RemoteMetadata = new Dictionary<string, string>
            {
                ["schema"] = "lex-raw-response-1",
                ["unexpected"] = "value",
            },
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(["create", "readback"], store.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Remote_version_or_ETag_mismatch_fails_closed(
        bool wrongVersion,
        bool wrongEtag)
    {
        var store = new RecordingStore
        {
            ReadbackVersionId = wrongVersion ? "version-other" : null,
            ReadbackETag = wrongEtag ? "\"etag-other\"" : null,
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(["create", "readback"], store.Calls);
    }

    [Fact]
    public async Task Nightly_lane_requests_and_verifies_ninety_day_immutability()
    {
        var store = new RecordingStore();
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([1]));

        Assert.Equal(
            new DateTimeOffset(2026, 11, 28, 4, 0, 0, TimeSpan.Zero),
            store.RetentionRequests.Single().ImmutableUntil);
        Assert.Equal(
            EvidenceRetentionLane.Nightly90Days,
            store.RetentionRequests.Single().Lane);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Mutable")]
    public async Task Nightly_lane_rejects_missing_or_unknown_immutability_mode(
        string? mode)
    {
        var store = new RecordingStore
        {
            RetentionFactsFactory = (version, request) => new(
                version.VersionId, request.ImmutableUntil, mode, false),
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Nightly_lane_rejects_shorter_retention()
    {
        var store = new RecordingStore
        {
            RetentionFactsFactory = (version, request) => new(
                version.VersionId,
                request.ImmutableUntil!.Value.AddSeconds(-1),
                "Unlocked",
                false),
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Release_lane_requests_and_verifies_an_indefinite_legal_hold()
    {
        var store = new RecordingStore();
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.EvidenceReleaseIndefinite);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([1]));

        var request = store.RetentionRequests.Single();
        Assert.Equal(EvidenceRetentionLane.EvidenceReleaseIndefinite, request.Lane);
        Assert.Null(request.ImmutableUntil);
    }

    [Fact]
    public async Task Release_lane_rejects_an_unconfirmed_legal_hold()
    {
        var store = new RecordingStore
        {
            RetentionFactsFactory = (version, _) => new(
                version.VersionId, null, null, false),
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.EvidenceReleaseIndefinite);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Retention_facts_for_another_version_fail_closed()
    {
        var store = new RecordingStore
        {
            RetentionFactsFactory = (_, request) => new(
                "version-other", request.ImmutableUntil, "Locked", false),
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Ambiguous_operations_are_bounded_to_three_attempts()
    {
        var store = new RecordingStore();
        for (var count = 0; count < AzureRawResponseSink.MaximumAttempts; count++)
            store.ReadbackFailures.Enqueue(new AzureEvidenceStoreException(
                AzureEvidenceStoreFailureKind.Ambiguous));
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));

        Assert.Equal(1, store.CreateCount);
        Assert.Equal(3, store.ReadbackCount);
        Assert.Equal(
            "Raw evidence could not be verified after three attempts.",
            error.Message);
    }

    [Fact]
    public async Task Capture_has_a_two_minute_production_deadline_and_honors_its_timer()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), AzureRawResponseSink.OverallDeadline);
        var store = new RecordingStore { BlockCreateUntilCancelled = true };
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            overallDeadline: TimeSpan.FromMilliseconds(25));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(1, store.CreateCount);
    }

    [Fact]
    public async Task Names_metadata_and_diagnostics_never_expose_sensitive_inputs()
    {
        var store = new RecordingStore();
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Rejected));
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days);
        const string bodySecret = "raw-body-secret";
        const string headerSecret = "publisher-header-secret";

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(),
                Response(entityTag: $"\"{headerSecret}\""),
                new MemoryStream(Encoding.UTF8.GetBytes(bodySecret))));
        var surfaced = error.ToString()
            + string.Join(string.Empty, store.BlobNames)
            + string.Join(string.Empty, store.UploadMetadata!
                .Select(pair => pair.Key + pair.Value));

        Assert.DoesNotContain("private=query", surfaced, StringComparison.Ordinal);
        Assert.DoesNotContain("publisher.invalid", surfaced, StringComparison.Ordinal);
        Assert.DoesNotContain(bodySecret, surfaced, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, surfaced, StringComparison.Ordinal);
    }

    private static SourceRequestIdentity Request(long maximumBytes = 1024) =>
        SourceRequestIdentity.Create(
            "legilux",
            "xml_body",
            SourceRequestMethod.Get,
            "https://publisher.invalid/file.xml?private=query",
            requestBodySha256: null,
            ordinal: 7,
            maximumBytes);

    private static BoundedResponseMetadata Response(
        bool bodyComplete = true,
        string? entityTag = "\"publisher-etag\"") =>
        BoundedResponseMetadata.Create(
            200,
            "application/xml",
            "utf-8",
            entityTag,
            lastModified: null,
            new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero),
            "https://publisher.invalid/file.xml?private=query",
            bodyComplete);

    private sealed class RecordingStore : IAzureRawEvidenceStore
    {
        private readonly AzureEvidenceObjectVersion _version =
            new("version-1", "\"etag-1\"");

        public List<string> Calls { get; } = [];
        public List<string> BlobNames { get; } = [];
        public List<AzureEvidenceRetentionRequest> RetentionRequests { get; } = [];
        public Queue<Exception> CreateFailures { get; } = new();
        public Queue<Exception> ResolveFailures { get; } = new();
        public Queue<Exception> ReadbackFailures { get; } = new();
        public IReadOnlyDictionary<string, string>? UploadMetadata { get; private set; }
        public byte[]? UploadedBytes { get; private set; }
        public byte[]? RemoteBytes { get; set; }
        public long? ReportedLength { get; set; }
        public IReadOnlyDictionary<string, string>? RemoteMetadata { get; set; }
        public string? ReadbackVersionId { get; set; }
        public string? ReadbackETag { get; set; }
        public bool BlockCreateUntilCancelled { get; set; }
        public Func<Stream>? ReadbackStreamFactory { get; set; }
        public Func<AzureEvidenceObjectVersion,
            AzureEvidenceRetentionRequest,
            AzureEvidenceRetentionFacts>? RetentionFactsFactory { get; set; }

        public int CallCount => Calls.Count;
        public int CreateCount => Calls.Count(call => call == "create");
        public int ReadbackCount => Calls.Count(call => call == "readback");

        public async Task<AzureEvidenceObjectVersion> CreateOnlyAsync(
            string blobName,
            Stream content,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            Calls.Add("create");
            BlobNames.Add(blobName);
            UploadMetadata = new Dictionary<string, string>(metadata);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            UploadedBytes = copy.ToArray();
            RemoteBytes ??= UploadedBytes;
            if (BlockCreateUntilCancelled)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (CreateFailures.TryDequeue(out var failure)) throw failure;
            return _version;
        }

        public Task<AzureEvidenceObjectVersion> ResolveCurrentVersionAsync(
            string blobName,
            CancellationToken cancellationToken)
        {
            Calls.Add("resolve");
            BlobNames.Add(blobName);
            if (ResolveFailures.TryDequeue(out var failure)) throw failure;
            return Task.FromResult(_version);
        }

        public Task<AzureEvidenceReadback> ReadbackAsync(
            string blobName,
            AzureEvidenceObjectVersion version,
            CancellationToken cancellationToken)
        {
            Calls.Add("readback");
            BlobNames.Add(blobName);
            if (ReadbackFailures.TryDequeue(out var failure)) throw failure;
            var bytes = RemoteBytes ?? UploadedBytes ?? [];
            return Task.FromResult(new AzureEvidenceReadback(
                ReadbackStreamFactory?.Invoke()
                    ?? new MemoryStream(bytes, writable: false),
                ReportedLength ?? bytes.Length,
                RemoteMetadata ?? UploadMetadata
                    ?? new Dictionary<string, string>(),
                ReadbackVersionId ?? version.VersionId,
                ReadbackETag ?? version.ETag));
        }

        public Task<AzureEvidenceRetentionFacts> ApplyAndReadRetentionAsync(
            string blobName,
            AzureEvidenceObjectVersion version,
            AzureEvidenceRetentionRequest retention,
            CancellationToken cancellationToken)
        {
            Calls.Add("retention");
            BlobNames.Add(blobName);
            RetentionRequests.Add(retention);
            var facts = RetentionFactsFactory?.Invoke(version, retention)
                ?? (retention.Lane == EvidenceRetentionLane.Nightly90Days
                    ? new AzureEvidenceRetentionFacts(
                        version.VersionId,
                        retention.ImmutableUntil,
                        "Unlocked",
                        false)
                    : new AzureEvidenceRetentionFacts(
                        version.VersionId,
                        null,
                        null,
                        true));
            return Task.FromResult(facts);
        }
    }

    private sealed class NeverCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Credential must not be used by the constructor.");

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AccessToken>(new InvalidOperationException(
                "Credential must not be used by the constructor."));
    }

    private sealed class ThrowingReadStream(string secret) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException(secret);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException(secret));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
