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

    [Theory]
    [InlineData("", "\"etag-1\"")]
    [InlineData("version-1", "")]
    public async Task Invalid_download_identity_is_disposed_before_wrapper_ownership(
        string versionId,
        string etag)
    {
        var content = new TrackingStream([1]);

        await Assert.ThrowsAsync<AzureEvidenceStoreException>(() =>
            AzureBlobRawEvidenceStore.TakeReadbackOwnershipAsync(
                content,
                1,
                new Dictionary<string, string>(),
                versionId,
                etag));

        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task Invalid_download_identity_wins_over_disposal_failure()
    {
        var error = await Assert.ThrowsAsync<AzureEvidenceStoreException>(() =>
            AzureBlobRawEvidenceStore.TakeReadbackOwnershipAsync(
                new ThrowingDisposeStream([1]),
                1,
                new Dictionary<string, string>(),
                versionId: "",
                etag: "\"etag-1\""));

        Assert.Equal(AzureEvidenceStoreFailureKind.Rejected, error.Kind);
    }

    [Theory]
    [InlineData("http://account.blob.core.windows.net/evidence")]
    [InlineData("https://user:password@account.blob.core.windows.net/evidence")]
    [InlineData("https://account.blob.core.windows.net/evidence?sig=credential-secret")]
    [InlineData("https://account.blob.core.windows.net/evidence#fragment")]
    [InlineData("https://attacker.example/evidence")]
    [InlineData("https://127.0.0.1/evidence")]
    [InlineData("https://[::1]/evidence")]
    [InlineData("https://account.blob.core.windows.net.attacker.example/evidence")]
    [InlineData("https://account.blob.core.windows.net:444/evidence")]
    [InlineData("https://account.privatelink.blob.core.windows.net/evidence")]
    [InlineData("https://account.blob.core.windows.net/evidence/extra")]
    [InlineData("https://account.blob.core.windows.net/evidence/")]
    [InlineData("https://account.blob.core.windows.net/ev")]
    [InlineData("https://account.blob.core.windows.net/-evidence")]
    [InlineData("https://account.blob.core.windows.net/evidence-")]
    [InlineData("https://account.blob.core.windows.net/evidence--private")]
    [InlineData("https://ab.blob.core.windows.net/evidence")]
    public void Container_URI_rejects_untrusted_hosts_and_non_Azure_container_shapes(
        string uri)
    {
        Assert.Throws<ArgumentException>(() => new AzureRawResponseSink(
            new Uri(uri),
            new NeverCredential(),
            EvidenceRetentionLane.Nightly90Days));
    }

    [Fact]
    public void Container_URI_accepts_one_exact_public_cloud_blob_container()
    {
        _ = new AzureRawResponseSink(
            new Uri("https://account123.blob.core.windows.net/evidence-private"),
            new NeverCredential(),
            EvidenceRetentionLane.Nightly90Days);
    }

    [Fact]
    public void Azure_upload_is_create_only_and_readback_is_ETag_conditioned()
    {
        var metadata = new Dictionary<string, string>
        {
            ["schema"] = "lex-raw-response-1",
        };

        var upload = AzureBlobRawEvidenceStore.CreateUploadOptions(
            metadata, EvidenceRetentionLane.Nightly90Days);
        var download = AzureBlobRawEvidenceStore.CreateDownloadOptions("\"etag-1\"");

        Assert.Equal(ETag.All, upload.Conditions.IfNoneMatch);
        Assert.Equal(metadata, upload.Metadata);
        Assert.Equal(EvidenceRef.MaximumByteLength,
            upload.TransferOptions.InitialTransferSize);
        Assert.Equal(EvidenceRef.MaximumByteLength,
            upload.TransferOptions.MaximumTransferSize);
        Assert.Equal(1, upload.TransferOptions.MaximumConcurrency);
        Assert.Null(upload.ImmutabilityPolicy);
        Assert.False(upload.LegalHold);
        Assert.Equal(new ETag("\"etag-1\""), download.Conditions.IfMatch);
    }

    [Fact]
    public void Release_upload_attaches_legal_hold_atomically()
    {
        var upload = AzureBlobRawEvidenceStore.CreateUploadOptions(
            new Dictionary<string, string>(),
            EvidenceRetentionLane.EvidenceReleaseIndefinite);

        Assert.True(upload.LegalHold);
        Assert.Null(upload.ImmutabilityPolicy);
    }

    [Fact]
    public void Invalid_retention_is_rejected_before_the_SDK_boundary()
    {
        Assert.Throws<InvalidDataException>(() =>
            AzureBlobRawEvidenceStore.CreateUploadOptions(
                new Dictionary<string, string>(),
                (EvidenceRetentionLane)999));
    }

    [Fact]
    public void Nightly_upload_fails_closed_without_a_container_policy()
    {
        var error = Assert.Throws<AzureEvidenceStoreException>(() =>
            AzureBlobRawEvidenceStore.RequireNightlyContainerPolicy(false));

        Assert.Equal(AzureEvidenceStoreFailureKind.Rejected, error.Kind);
    }

    [Theory]
    [InlineData(404, true, false, (int)AzureEvidenceStoreFailureKind.Ambiguous)]
    [InlineData(404, false, false, (int)AzureEvidenceStoreFailureKind.Rejected)]
    [InlineData(412, false, true, (int)AzureEvidenceStoreFailureKind.AlreadyExists)]
    [InlineData(412, false, false, (int)AzureEvidenceStoreFailureKind.Rejected)]
    [InlineData(500, false, false, (int)AzureEvidenceStoreFailureKind.Ambiguous)]
    [InlineData(403, false, false, (int)AzureEvidenceStoreFailureKind.Rejected)]
    public void Azure_failure_mapping_is_bounded_and_sanitized(
        int status,
        bool missingIsAmbiguous,
        bool isCreate,
        int expectedKind)
    {
        const string secret = "credential-secret-in-sdk-error";

        var mapped = Assert.IsType<AzureEvidenceStoreException>(
            AzureBlobRawEvidenceStore.MapFailure(
                new RequestFailedException(status, secret),
                CancellationToken.None,
                missingIsAmbiguous,
                isCreate));

        Assert.Equal((AzureEvidenceStoreFailureKind)expectedKind, mapped.Kind);
        Assert.DoesNotContain(secret, mapped.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_cancellation_is_propagated_only_for_a_canceled_supplied_token()
    {
        const string secret = "sdk-cancellation-secret";
        var uncanceled = AzureBlobRawEvidenceStore.MapFailure(
            new OperationCanceledException(secret), CancellationToken.None);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var canceled = new OperationCanceledException(secret);

        var propagated = AzureBlobRawEvidenceStore.MapFailure(
            canceled, source.Token);

        Assert.Equal(
            AzureEvidenceStoreFailureKind.Ambiguous,
            Assert.IsType<AzureEvidenceStoreException>(uncanceled).Kind);
        var safeCancellation = Assert.IsType<OperationCanceledException>(propagated);
        Assert.NotSame(canceled, safeCancellation);
        Assert.Equal(source.Token, safeCancellation.CancellationToken);
        Assert.DoesNotContain(
            secret, safeCancellation.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, uncanceled.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incomplete_response_is_rejected_before_any_Azure_call()
    {
        var store = new RecordingStore();
        var sink = NightlySink(store);
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
        var sink = NightlySink(store);

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
        var sink = NightlySink(store);
        const string secret = "private=query-and-body-secret";

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(),
                Response(),
                new ThrowingReadStream(_ => new IOException(secret))));

        Assert.Equal("Publisher response bytes could not be buffered.", error.Message);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Successful_capture_has_no_existence_preflight_and_uses_safe_deterministic_projection()
    {
        var bytes = Encoding.UTF8.GetBytes("raw-body-secret");
        var store = new RecordingStore();
        var sink = NightlySink(store);

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

        await NightlySink(first)
            .CaptureVerifiedAsync(Request(), Response(), new MemoryStream(bytes));
        await NightlySink(second)
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
        var sink = NightlySink(store);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(
            ["create", "resolve", "readback", "retention"], store.Calls);
        Assert.Single(store.BlobNames.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Ambiguous_preflight_retries_before_the_single_create()
    {
        var store = new RecordingStore();
        store.PreflightFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        var sink = NightlySink(store);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(2, store.PreflightCount);
        Assert.Equal(1, store.CreateCount);
        Assert.Equal(["create", "readback", "retention"], store.Calls);
    }

    [Fact]
    public async Task Readback_retry_never_recreates_or_renames_a_known_version()
    {
        var store = new RecordingStore();
        store.ReadbackFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        var sink = NightlySink(store);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(
            ["create", "readback", "readback", "retention"], store.Calls);
        Assert.Equal(1, store.CreateCount);
        Assert.Single(store.BlobNames.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Ambiguous_create_with_missing_version_never_recreates()
    {
        var store = new RecordingStore();
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        store.ResolveFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Ambiguous));
        var sink = NightlySink(store);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([4, 5, 6]));

        Assert.Equal(
            ["create", "resolve", "resolve", "readback", "retention"],
            store.Calls);
        Assert.Equal(1, store.CreateCount);
        Assert.Single(store.BlobNames.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Deferred_readback_stream_errors_are_sanitized_and_bounded()
    {
        const string secret = "azure-uri-query-secret";
        var store = new RecordingStore
        {
            ReadbackStreamFactory = () =>
                new ThrowingReadStream(_ => new IOException(secret)),
        };
        var sink = NightlySink(store);

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([4, 5, 6])));

        Assert.Equal(1, store.CreateCount);
        Assert.Equal(3, store.ReadbackCount);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hostile_publisher_cancellation_without_caller_cancellation_is_sanitized()
    {
        const string secret = "hostile-publisher-cancellation-secret";
        var sink = NightlySink(new RecordingStore());

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(),
                new ThrowingReadStream(_ =>
                    new OperationCanceledException(secret))));

        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hostile_publisher_CanRead_failure_is_sanitized()
    {
        const string secret = "hostile-publisher-can-read-secret";
        var sink = NightlySink(new RecordingStore());

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(),
                new ThrowingCanReadStream(() => new IOException(secret))));

        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deferred_invalid_data_and_uncanceled_cancellation_are_sanitized()
    {
        const string secret = "publisher-query-secret";
        foreach (var exception in new Exception[]
        {
            new InvalidDataException(secret),
            new OperationCanceledException(secret),
        })
        {
            var store = new RecordingStore
            {
                ReadbackStreamFactory = () =>
                    new ThrowingReadStream(_ => exception),
            };
            var sink = NightlySink(store);

            var error = await Assert.ThrowsAsync<IOException>(() =>
                sink.CaptureVerifiedAsync(
                    Request(), Response(), new MemoryStream([4, 5, 6])));

            Assert.Equal(3, store.ReadbackCount);
            Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Deferred_cancellation_propagates_only_after_the_supplied_token_is_canceled()
    {
        const string secret = "hostile-read-cancellation-secret";
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore
        {
            ReadbackStreamFactory = () => new ThrowingReadStream(token =>
            {
                cancellation.Cancel();
                return new OperationCanceledException(
                    secret, innerException: null, token);
            }),
        };
        var sink = NightlySink(store);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([4, 5, 6]),
                cancellation.Token));

        Assert.Equal(1, store.ReadbackCount);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Deferred_readback_mapping_propagates_the_canceled_supplied_token()
    {
        const string secret = "hostile-read-cancellation-secret";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = AzureRawResponseSink.SafeReadbackFailure(
            new OperationCanceledException(secret), cancellation.Token);

        var safeCancellation = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(cancellation.Token, safeCancellation.CancellationToken);
        Assert.DoesNotContain(
            secret, safeCancellation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canceled_deferred_CanRead_failure_is_sanitized()
    {
        const string secret = "hostile-can-read-cancellation-secret";
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore
        {
            ReadbackStreamFactory = () => new ThrowingCanReadStream(() =>
            {
                cancellation.Cancel();
                return new OperationCanceledException(secret);
            }),
        };
        var sink = NightlySink(store);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([4, 5, 6]),
                cancellation.Token));

        Assert.Equal(1, store.ReadbackCount);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remote_byte_digest_mismatch_fails_closed()
    {
        var store = new RecordingStore { RemoteBytes = [9, 9, 9] };
        var sink = NightlySink(store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(["create", "readback"], store.Calls);
    }

    [Fact]
    public async Task Byte_mismatch_wins_over_readback_disposal_failure()
    {
        var store = new RecordingStore
        {
            RemoteBytes = [9, 9, 9],
            ReadbackStreamFactory = () => new ThrowingDisposeStream([9, 9, 9]),
        };
        var sink = NightlySink(store);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(
            "Remote evidence bytes did not match the captured body.",
            error.Message);
    }

    [Fact]
    public async Task Remote_length_mismatch_fails_closed()
    {
        var store = new RecordingStore { ReportedLength = 999 };
        var sink = NightlySink(store);

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
        var sink = NightlySink(store);

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
        var sink = NightlySink(store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1, 2, 3])));

        Assert.Equal(["create", "readback"], store.Calls);
    }

    [Fact]
    public async Task Nightly_lane_requests_and_verifies_ninety_day_immutability()
    {
        var store = new RecordingStore
        {
            VersionCreatedAt = new DateTimeOffset(
                2026, 8, 30, 4, 0, 0, TimeSpan.Zero),
        };
        var sink = NightlySink(store);

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([1]));

        Assert.Equal(
            new DateTimeOffset(2026, 11, 28, 4, 0, 0, TimeSpan.Zero),
            store.RetentionRequests.Single().ImmutableUntil);
        Assert.Equal(
            EvidenceRetentionLane.Nightly90Days,
            store.RetentionRequests.Single().Lane);
    }

    [Fact]
    public async Task Nightly_expiry_is_ceiled_to_the_next_whole_UTC_second()
    {
        var fetchedAt = new DateTimeOffset(
            2026, 8, 30, 4, 0, 0, TimeSpan.Zero).AddTicks(1);
        var store = new RecordingStore { VersionCreatedAt = fetchedAt };
        var sink = NightlySink(store);

        await sink.CaptureVerifiedAsync(
            Request(), Response(fetchedAt: fetchedAt), new MemoryStream([1]));

        Assert.Equal(
            new DateTimeOffset(2026, 11, 28, 4, 0, 1, TimeSpan.Zero),
            store.RetentionRequests.Single().ImmutableUntil);
    }

    [Fact]
    public async Task Delayed_create_accepts_inherited_expiry_from_server_creation_time()
    {
        var createdAt = new DateTimeOffset(
            2026, 8, 30, 4, 10, 0, TimeSpan.Zero);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) => new(
                version.VersionId,
                createdAt.AddDays(90),
                "Locked",
                false),
        };
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(
                createdAt));

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([1]));
    }

    [Fact]
    public async Task Nightly_lane_rejects_server_time_that_widens_the_local_ceiling()
    {
        var createdAt = new DateTimeOffset(
            3000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) => new(
                version.VersionId,
                createdAt.AddDays(90),
                "Locked",
                false),
        };
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Nightly_lane_classifies_unbounded_server_creation_time()
    {
        var store = new RecordingStore
        {
            VersionCreatedAt = DateTimeOffset.MaxValue,
        };
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Nightly_lane_accepts_one_second_of_server_clock_lag()
    {
        var createdAt = new DateTimeOffset(
            2026, 8, 30, 3, 59, 59, TimeSpan.Zero);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) => new(
                version.VersionId,
                createdAt.AddDays(90),
                "Locked",
                false),
        };
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero)));

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([1]));
    }

    [Fact]
    public async Task Existing_nightly_object_uses_its_original_creation_window()
    {
        var createdAt = new DateTimeOffset(
            2026, 8, 29, 4, 0, 0, TimeSpan.Zero);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) => new(
                version.VersionId,
                createdAt.AddDays(90),
                "Locked",
                false),
        };
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.AlreadyExists));
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero)));

        await sink.CaptureVerifiedAsync(
            Request(), Response(), new MemoryStream([1]));

        Assert.Equal(1, store.CreateCount);
        Assert.Equal(1, store.Calls.Count(call => call == "resolve"));
    }

    [Fact]
    public async Task Expired_existing_nightly_object_fails_closed()
    {
        var observedNow = new DateTimeOffset(
            2026, 8, 30, 4, 0, 0, TimeSpan.Zero);
        var createdAt = observedNow.AddDays(-91);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) => new(
                version.VersionId,
                createdAt.AddDays(90),
                "Locked",
                false),
        };
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.AlreadyExists));
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(observedNow));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Nightly_lane_rejects_expiry_reached_after_retention_read()
    {
        var createdAt = new DateTimeOffset(
            2026, 8, 30, 4, 0, 0, TimeSpan.Zero);
        var expiry = createdAt.AddDays(90);
        var timeProvider = new AdvancingTimeProvider(createdAt);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) =>
            {
                timeProvider.AdvanceTo(expiry);
                return new AzureEvidenceRetentionFacts(
                    version.VersionId, expiry, "Locked", false);
            },
        };
        var sink = new AzureRawResponseSink(
            store, EvidenceRetentionLane.Nightly90Days, timeProvider);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unlocked")]
    public async Task Nightly_lane_rejects_missing_or_unlocked_immutability_mode(
        string? mode)
    {
        var store = new RecordingStore
        {
            RetentionFactsFactory = (version, request) => new(
                version.VersionId, request.ImmutableUntil, mode, false),
        };
        var sink = NightlySink(store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Fact]
    public async Task Nightly_lane_rejects_missing_expiry()
    {
        var store = new RecordingStore
        {
            RetentionFactsFactory = (version, _) => new(
                version.VersionId, null, "Locked", false),
        };
        var sink = NightlySink(store);

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
                "Locked",
                false),
        };
        var sink = NightlySink(store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nightly_lane_rejects_expiry_beyond_one_minute_or_unbounded(
        bool maximumValue)
    {
        var createdAt = new DateTimeOffset(
            2026, 8, 30, 4, 0, 0, TimeSpan.Zero);
        var store = new RecordingStore
        {
            VersionCreatedAt = createdAt,
            RetentionFactsFactory = (version, _) => new(
                version.VersionId,
                maximumValue
                    ? DateTimeOffset.MaxValue
                    : createdAt.AddDays(90).AddMinutes(1).AddSeconds(1),
                "Locked",
                false),
        };
        var sink = new AzureRawResponseSink(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(createdAt));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));

        Assert.Equal(
            createdAt.AddDays(90).AddMinutes(1),
            store.RetentionRequests.Single().ImmutableUntilMaximum);
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
        var sink = NightlySink(store);

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
        var sink = NightlySink(store);

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

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));

        Assert.Contains("two-minute deadline", error.Message,
            StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(1, store.CreateCount);
    }

    [Fact]
    public async Task Caller_cancellation_remains_safe_and_tied_to_the_caller_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sink = NightlySink(new RecordingStore());

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1]),
                cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Conflict_after_version_is_known_is_a_definite_failure()
    {
        var store = new RecordingStore();
        store.ReadbackFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.AlreadyExists));
        var sink = NightlySink(store);

        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.CaptureVerifiedAsync(
                Request(), Response(), new MemoryStream([1])));

        Assert.Contains("invalid conflict", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, store.ReadbackCount);
    }

    [Fact]
    public async Task Names_metadata_and_diagnostics_never_expose_sensitive_inputs()
    {
        var store = new RecordingStore();
        store.CreateFailures.Enqueue(new AzureEvidenceStoreException(
            AzureEvidenceStoreFailureKind.Rejected));
        var sink = NightlySink(store);
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

    private static AzureRawResponseSink NightlySink(
        IAzureRawEvidenceStore store) => new(
            store,
            EvidenceRetentionLane.Nightly90Days,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 30, 4, 0, 1, TimeSpan.Zero)));

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
        string? entityTag = "\"publisher-etag\"",
        DateTimeOffset? fetchedAt = null) =>
        BoundedResponseMetadata.Create(
            200,
            "application/xml",
            "utf-8",
            entityTag,
            lastModified: null,
            fetchedAt
                ?? new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero),
            "https://publisher.invalid/file.xml?private=query",
            bodyComplete);

    private sealed class RecordingStore : IAzureRawEvidenceStore
    {
        private AzureEvidenceObjectVersion Version =>
            new(
                "version-1",
                "\"etag-1\"",
                VersionCreatedAt);

        public List<string> Calls { get; } = [];
        public List<string> BlobNames { get; } = [];
        public List<AzureEvidenceRetentionRequest> RetentionRequests { get; } = [];
        public Queue<Exception> CreateFailures { get; } = new();
        public Queue<Exception> PreflightFailures { get; } = new();
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
        public DateTimeOffset VersionCreatedAt { get; set; } =
            new(2026, 8, 30, 4, 0, 1, TimeSpan.Zero);
        public Func<Stream>? ReadbackStreamFactory { get; set; }
        public Func<AzureEvidenceObjectVersion,
            AzureEvidenceRetentionRequest,
            AzureEvidenceRetentionFacts>? RetentionFactsFactory { get; set; }

        public int CallCount => Calls.Count;
        public int CreateCount => Calls.Count(call => call == "create");
        public int ReadbackCount => Calls.Count(call => call == "readback");
        public int PreflightCount { get; private set; }

        public Task VerifyCreatePrerequisitesAsync(
            EvidenceRetentionLane retentionLane,
            CancellationToken cancellationToken)
        {
            PreflightCount++;
            if (PreflightFailures.TryDequeue(out var failure)) throw failure;
            return Task.CompletedTask;
        }

        public async Task<AzureEvidenceObjectVersion> CreateOnlyAsync(
            string blobName,
            Stream content,
            IReadOnlyDictionary<string, string> metadata,
            EvidenceRetentionLane retentionLane,
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
            return Version;
        }

        public Task<AzureEvidenceObjectVersion> ResolveCurrentVersionAsync(
            string blobName,
            CancellationToken cancellationToken)
        {
            Calls.Add("resolve");
            BlobNames.Add(blobName);
            if (ResolveFailures.TryDequeue(out var failure)) throw failure;
            return Task.FromResult(Version);
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

        public Task<AzureEvidenceRetentionFacts> ReadRetentionAsync(
            string blobName,
            AzureEvidenceObjectVersion version,
            AzureEvidenceRetentionRequest expectedRetention,
            CancellationToken cancellationToken)
        {
            Calls.Add("retention");
            BlobNames.Add(blobName);
            RetentionRequests.Add(expectedRetention);
            var retention = expectedRetention;
            var facts = RetentionFactsFactory?.Invoke(version, retention)
                ?? (retention.Lane == EvidenceRetentionLane.Nightly90Days
                    ? new AzureEvidenceRetentionFacts(
                        version.VersionId,
                        retention.ImmutableUntil,
                        "Locked",
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class AdvancingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void AdvanceTo(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class ThrowingReadStream(
        Func<CancellationToken, Exception> errorFactory) : Stream
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
            throw errorFactory(CancellationToken.None);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(errorFactory(cancellationToken));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingDisposeStream(byte[] bytes) : MemoryStream(bytes)
    {
        protected override void Dispose(bool disposing) =>
            throw new IOException("hostile-disposal-secret");
    }

    private sealed class ThrowingCanReadStream(Func<Exception> errorFactory)
        : Stream
    {
        public override bool CanRead => throw errorFactory();
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
