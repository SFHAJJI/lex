using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Azure;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class AzureBlobCustodyConfigurationJournalTests
{
    private const string ServiceUri = "https://stlexv3custody.blob.core.windows.net/";
    private const string ResourceEtag = "\"resource-etag\"";
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 1, 8, 9, 10, TimeSpan.Zero);
    private static readonly Guid ManagedIdentityClientId =
        Guid.Parse("caecb92d-1f9c-43ec-8798-d9e83d02c4bc");
    private static readonly Guid NightlyPolicyKey =
        Guid.Parse("e21680d3-badd-4a46-9293-c5d0b34f0300");
    private static readonly Guid LegalHoldPolicyKey =
        Guid.Parse("dc31db8e-3909-48e5-b60d-a86364175e30");
    private static readonly Guid SubscriptionId =
        Guid.Parse("7b937a55-7a06-47de-acd6-2a78e43d7782");
    private static readonly Guid FirstRequestId =
        Guid.Parse("7e9f7c8e-4f47-4c39-bd39-10844679e12f");
    private static readonly Guid SecondRequestId =
        Guid.Parse("47375022-5d48-4944-9760-b3b9af6c39db");

    [TestMethod]
    public async Task FirstAppendCreatesFullAnchorAndRequestWithExactBoundedReadback()
    {
        var harness = new Harness();
        var receipt = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);

        await harness.Journal.AppendAsync(receipt, CancellationToken.None);

        var prefix = TuplePrefix(receipt);
        var anchor = harness.Nightly.Blobs[$"{prefix}/anchor.json"];
        var request = harness.Nightly.Blobs[$"{prefix}/requests/{FirstRequestId:N}.json"];
        var expected = Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt));
        foreach (var blob in new[] { anchor, request })
        {
            Assert.AreEqual(1, blob.UploadAttempts);
            Assert.AreEqual(ETag.All, blob.UploadOptions!.Conditions!.IfNoneMatch);
            Assert.AreEqual("application/json", blob.UploadOptions.HttpHeaders!.ContentType);
            Assert.AreEqual(
                StorageChecksumAlgorithm.StorageCrc64,
                blob.UploadOptions.TransferValidation!.ChecksumAlgorithm);
            CollectionAssert.AreEqual(expected, blob.Content);
            Assert.AreEqual(1, blob.DownloadOptions.Count);
            Assert.AreEqual(
                blob.UploadResponseEtag,
                blob.DownloadOptions.Single().Conditions!.IfMatch);
        }

        AssertOrdered(
            harness.Events,
            "nightly.upload.anchor.json",
            "nightly.download.anchor.json",
            $"nightly.upload.{FirstRequestId:N}.json",
            $"nightly.download.{FirstRequestId:N}.json");
    }

    [TestMethod]
    public async Task SameRequestIsIdempotentAndAnotherRequestPreservesBothFullReceipts()
    {
        var harness = new Harness();
        var first = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);
        var second = Receipt(CustodyClass.NightlyFloor90d, SecondRequestId);

        await harness.Journal.AppendAsync(first, CancellationToken.None);
        await harness.Journal.AppendAsync(first, CancellationToken.None);
        await harness.Journal.AppendAsync(second, CancellationToken.None);

        var prefix = TuplePrefix(first);
        Assert.AreEqual(3, harness.Nightly.Blobs.Values.Count(blob => blob.Present));
        AssertExactReceipt(
            harness.Nightly.Blobs[$"{prefix}/anchor.json"], first);
        AssertExactReceipt(
            harness.Nightly.Blobs[$"{prefix}/requests/{FirstRequestId:N}.json"], first);
        AssertExactReceipt(
            harness.Nightly.Blobs[$"{prefix}/requests/{SecondRequestId:N}.json"], second);
    }

    [TestMethod]
    public async Task ExistingRequestWithConflictingBytesFailsClosed()
    {
        var harness = new Harness();
        var receipt = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);
        await harness.Journal.AppendAsync(receipt, CancellationToken.None);
        var request = harness.Nightly.Blobs[
            $"{TuplePrefix(receipt)}/requests/{FirstRequestId:N}.json"];
        request.Seed(Encoding.UTF8.GetBytes("{\"conflict\":true}"));

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Journal.AppendAsync(receipt, CancellationToken.None));
    }

    [TestMethod]
    public async Task CorruptNewRequestReadbackCannotReturnSuccess()
    {
        var harness = new Harness();
        harness.Nightly.ConfigureNewBlob = blob =>
        {
            if (blob.Name.Contains("/requests/", StringComparison.Ordinal))
            {
                blob.AfterUpload = candidate =>
                    candidate.DownloadBytes = [.. candidate.Content, 0x20];
            }
        };
        var receipt = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Journal.AppendAsync(receipt, CancellationToken.None));

        Assert.AreEqual(
            1,
            harness.Nightly.Blobs[
                $"{TuplePrefix(receipt)}/requests/{FirstRequestId:N}.json"].UploadAttempts);
    }

    [TestMethod]
    [DataRow("resource_etag")]
    [DataRow("managed_identity")]
    [DataRow("resource_id")]
    [DataRow("policy_etag")]
    [DataRow("retention_days")]
    public async Task ConflictingFactAtSameTupleFailsBeforeItsRequestWrite(string changedFact)
    {
        var harness = new Harness();
        var first = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);
        var conflicting = Receipt(
            CustodyClass.NightlyFloor90d,
            SecondRequestId,
            resourceEtag: changedFact == "resource_etag"
                ? "\"changed-resource-etag\""
                : ResourceEtag,
            managedIdentityClientId: changedFact == "managed_identity"
                ? Guid.Parse("f270fa75-1adb-46ed-9fb1-a3fb390c1469")
                : ManagedIdentityClientId,
            resourceIdSuffix: changedFact == "resource_id" ? "nightly-other" : "nightly",
            policyEtag: changedFact == "policy_etag" ? "\"changed-policy-etag\"" : "\"policy-etag\"",
            retentionDays: changedFact == "retention_days" ? 181 : 180);
        await harness.Journal.AppendAsync(first, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Journal.AppendAsync(conflicting, CancellationToken.None));

        var requestName = $"{TuplePrefix(conflicting)}/requests/{SecondRequestId:N}.json";
        Assert.IsFalse(
            harness.Nightly.Blobs.TryGetValue(requestName, out var request)
            && request.UploadAttempts != 0);
        AssertExactReceipt(
            harness.Nightly.Blobs[$"{TuplePrefix(first)}/anchor.json"], first);
    }

    [TestMethod]
    public async Task WrongConfiguredPolicyKeyIsRejectedBeforeAzure()
    {
        var harness = new Harness();
        var receipt = Receipt(
            CustodyClass.NightlyFloor90d,
            FirstRequestId,
            policyKey: Guid.Parse("6822ca9c-5bc4-4532-8318-6474cf0e4552"));

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Journal.AppendAsync(receipt, CancellationToken.None));

        Assert.AreEqual(0, harness.Nightly.Blobs.Count);
        Assert.AreEqual(0, harness.LegalHold.Blobs.Count);
        Assert.AreEqual(0, harness.Events.Count);
    }

    [TestMethod]
    public async Task LegalHoldReceiptUsesItsOwnContainerAndClosedLaneToken()
    {
        var harness = new Harness();
        var receipt = Receipt(CustodyClass.LegalHoldEvidence, FirstRequestId);

        await harness.Journal.AppendAsync(receipt, CancellationToken.None);

        Assert.AreEqual(0, harness.Nightly.Blobs.Count);
        Assert.IsTrue(harness.LegalHold.Blobs.ContainsKey(
            $"{TuplePrefix(receipt)}/anchor.json"));
        StringAssert.EndsWith(TuplePrefix(receipt), "/legal_hold_evidence");
    }

    [TestMethod]
    [DataRow("short")]
    [DataRow("extra")]
    [DataRow("malformed")]
    [DataRow("duplicate")]
    [DataRow("etag")]
    [DataRow("vanish")]
    public async Task CorruptAnchorReadbackCannotCreateARequestOrReturnSuccess(string attack)
    {
        var harness = new Harness();
        var receipt = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);
        harness.Nightly.ConfigureNewBlob = blob =>
        {
            if (!blob.Name.EndsWith("/anchor.json", StringComparison.Ordinal))
            {
                return;
            }

            if (attack == "malformed")
            {
                blob.Seed([0xff]);
            }
            else if (attack == "duplicate")
            {
                var canonical = ContractJson.Serialize(receipt);
                var duplicate = canonical.Replace(
                    $"\"arm_resource_etag\":{System.Text.Json.JsonSerializer.Serialize(ResourceEtag)}",
                    $"\"arm_resource_etag\":\"hidden-conflict\",\"arm_resource_etag\":{System.Text.Json.JsonSerializer.Serialize(ResourceEtag)}",
                    StringComparison.Ordinal);
                Assert.AreNotEqual(canonical, duplicate);
                blob.Seed(Encoding.UTF8.GetBytes(duplicate));
            }
            else
            {
                blob.AfterUpload = candidate => ApplyReadbackAttack(candidate, attack);
            }
        };

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Journal.AppendAsync(receipt, CancellationToken.None));

        var requestName = $"{TuplePrefix(receipt)}/requests/{FirstRequestId:N}.json";
        Assert.IsFalse(
            harness.Nightly.Blobs.TryGetValue(requestName, out var request)
            && request.UploadAttempts != 0);
    }

    [TestMethod]
    public async Task CallerCancellationAfterAnchorUploadCannotReturnSuccess()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Nightly.ConfigureNewBlob = blob =>
        {
            if (blob.Name.EndsWith("/anchor.json", StringComparison.Ordinal))
            {
                blob.AfterUpload = _ => cancellation.Cancel();
            }
        };
        var receipt = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            harness.Journal.AppendAsync(receipt, cancellation.Token));

        var requestName = $"{TuplePrefix(receipt)}/requests/{FirstRequestId:N}.json";
        Assert.IsFalse(
            harness.Nightly.Blobs.TryGetValue(requestName, out var request)
            && request.UploadAttempts != 0);
    }

    [TestMethod]
    public async Task CallerCancellationAfterRequestUploadCannotReturnSuccess()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Nightly.ConfigureNewBlob = blob =>
        {
            if (blob.Name.Contains("/requests/", StringComparison.Ordinal))
            {
                blob.AfterUpload = _ => cancellation.Cancel();
            }
        };
        var receipt = Receipt(CustodyClass.NightlyFloor90d, FirstRequestId);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            harness.Journal.AppendAsync(receipt, cancellation.Token));

        Assert.AreEqual(
            1,
            harness.Nightly.Blobs[
                $"{TuplePrefix(receipt)}/requests/{FirstRequestId:N}.json"].UploadAttempts);
    }

    private static void ApplyReadbackAttack(FakeBlockBlobClient blob, string attack)
    {
        switch (attack)
        {
            case "short":
                blob.DownloadBytes = blob.Content[..^1];
                break;
            case "extra":
                blob.DownloadBytes = [.. blob.Content, 0x20];
                break;
            case "etag":
                blob.DownloadResponseEtag = new ETag("\"wrong-download-etag\"");
                break;
            case "vanish":
                blob.Present = false;
                break;
            default:
                Assert.Fail($"Unknown attack {attack}.");
                break;
        }
    }

    private static void AssertExactReceipt(
        FakeBlockBlobClient blob,
        AzureCustodyConfigurationReceipt receipt) =>
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt)),
            blob.Content);

    private static void AssertOrdered(
        List<string> events,
        params string[] expected)
    {
        var previous = -1;
        foreach (var item in expected)
        {
            var current = events.IndexOf(item);
            Assert.IsTrue(current > previous, $"Expected {item} after prior boundary.");
            previous = current;
        }
    }

    private static string TuplePrefix(AzureCustodyConfigurationReceipt receipt)
    {
        var lane = receipt.CustodyClass switch
        {
            CustodyClass.NightlyFloor90d => "nightly_floor_90d",
            CustodyClass.LegalHoldEvidence => "legal_hold_evidence",
            _ => throw new ArgumentOutOfRangeException(nameof(receipt)),
        };
        return $"_configuration/v1/{receipt.PolicyKey:N}/{receipt.ObservedAt.UtcTicks:D19}/{lane}";
    }

    private static AzureCustodyConfigurationReceipt Receipt(
        CustodyClass custodyClass,
        Guid requestId,
        Guid? policyKey = null,
        string resourceEtag = ResourceEtag,
        Guid? managedIdentityClientId = null,
        string? resourceIdSuffix = null,
        string policyEtag = "\"policy-etag\"",
        int retentionDays = 180) => new(
            AzureCustodySchemaIds.ConfigurationReceipt,
            policyKey ?? (custodyClass == CustodyClass.NightlyFloor90d
                ? NightlyPolicyKey
                : LegalHoldPolicyKey),
            custodyClass,
            ObservedAt,
            $"/subscriptions/{SubscriptionId:D}/resourceGroups/rg-lex-v3/providers/Microsoft.Storage/storageAccounts/stlexv3custody/blobServices/default/containers/"
                + (resourceIdSuffix
                    ?? (custodyClass == CustodyClass.NightlyFloor90d ? "nightly" : "legal-hold")),
            "2025-06-01",
            resourceEtag,
            requestId.ToString("D"),
            managedIdentityClientId ?? ManagedIdentityClientId,
            "None",
            immutableStorageWithVersioningEnabled: false,
            migrationState: null,
            immutabilityPolicyEtag: custodyClass == CustodyClass.NightlyFloor90d
                ? policyEtag
                : null,
            immutabilityPolicyState: custodyClass == CustodyClass.NightlyFloor90d
                ? "Locked"
                : null,
            retentionDays: custodyClass == CustodyClass.NightlyFloor90d ? retentionDays : null,
            protectedAppendWrites: false,
            protectedAppendWritesAll: false,
            activeLegalHold: custodyClass == CustodyClass.LegalHoldEvidence,
            protectedBlockBlobAppends: false);

    private static AzureBlobCustodyOptions Options() => new(
        new Uri(ServiceUri),
        "staging",
        "nightly",
        "legal-hold",
        ManagedIdentityClientId,
        NightlyPolicyKey,
        LegalHoldPolicyKey,
        SubscriptionId,
        "rg-lex-v3");

    private sealed class Harness
    {
        public Harness()
        {
            Events = [];
            Nightly = new FakeBlobContainerClient("nightly", Events);
            LegalHold = new FakeBlobContainerClient("legal_hold", Events);
            Journal = new AzureBlobCustodyConfigurationReceiptJournal(
                Options(), Nightly, LegalHold);
        }

        public List<string> Events { get; }

        public FakeBlobContainerClient Nightly { get; }

        public FakeBlobContainerClient LegalHold { get; }

        public AzureBlobCustodyConfigurationReceiptJournal Journal { get; }
    }

    private sealed class FakeBlobContainerClient : BlobContainerClient
    {
        private readonly string _label;
        private readonly List<string> _events;

        public FakeBlobContainerClient(string label, List<string> events)
        {
            _label = label;
            _events = events;
        }

        public Dictionary<string, FakeBlockBlobClient> Blobs { get; } =
            new(StringComparer.Ordinal);

        public Action<FakeBlockBlobClient>? ConfigureNewBlob { get; set; }

        protected override BlockBlobClient GetBlockBlobClientCore(string blobName)
        {
            if (!Blobs.TryGetValue(blobName, out var blob))
            {
                blob = new FakeBlockBlobClient(_label, blobName, _events);
                ConfigureNewBlob?.Invoke(blob);
                Blobs.Add(blobName, blob);
            }

            return blob;
        }
    }

    private sealed class FakeBlockBlobClient : BlockBlobClient
    {
        private readonly string _label;
        private readonly List<string> _events;

        public FakeBlockBlobClient(string label, string name, List<string> events)
        {
            _label = label;
            Name = name;
            _events = events;
            Uri = new Uri($"https://storage.example.test/{label}/{name}");
        }

        public override string Name { get; }

        public override Uri Uri { get; }

        public bool Present { get; set; }

        public byte[] Content { get; private set; } = [];

        public byte[]? DownloadBytes { get; set; }

        public ETag Etag { get; private set; } = new("\"journal-etag\"");

        public ETag UploadResponseEtag { get; private set; }

        public ETag? DownloadResponseEtag { get; set; }

        public int UploadAttempts { get; private set; }

        public BlobUploadOptions? UploadOptions { get; private set; }

        public List<BlobDownloadOptions> DownloadOptions { get; } = [];

        public List<BlobRequestConditions?> PropertiesConditions { get; } = [];

        public Action<FakeBlockBlobClient>? AfterUpload { get; set; }

        public void Seed(byte[] bytes)
        {
            Content = bytes.ToArray();
            Present = true;
        }

        public override async Task<Response<BlobContentInfo>> UploadAsync(
            Stream content,
            BlobUploadOptions options,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.upload.{Name[(Name.LastIndexOf('/') + 1)..]}");
            cancellationToken.ThrowIfCancellationRequested();
            UploadAttempts++;
            UploadOptions = options;
            if (Present && options.Conditions?.IfNoneMatch == ETag.All)
            {
                throw new RequestFailedException(412, "Create-only object already exists.");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Content = buffer.ToArray();
            Present = true;
            UploadResponseEtag = Etag;
            AfterUpload?.Invoke(this);
            return Response.FromValue(
                BlobsModelFactory.BlobContentInfo(
                    UploadResponseEtag,
                    ObservedAt,
                    contentHash: null,
                    encryptionKeySha256: null,
                    blobSequenceNumber: 0),
                new FakeResponse());
        }

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions options,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.download.{Name[(Name.LastIndexOf('/') + 1)..]}");
            cancellationToken.ThrowIfCancellationRequested();
            DownloadOptions.Add(options);
            if (!Present)
            {
                throw new RequestFailedException(404, "Journal object disappeared.");
            }

            if (options.Conditions?.IfMatch is { } expected && !expected.Equals(Etag))
            {
                throw new RequestFailedException(412, "Journal object changed.");
            }

            var bytes = DownloadBytes ?? Content;
            var details = BlobsModelFactory.BlobDownloadDetails(
                contentLength: bytes.LongLength,
                eTag: DownloadResponseEtag ?? Etag);
            return Task.FromResult(Response.FromValue(
                BlobsModelFactory.BlobDownloadStreamingResult(
                    new MemoryStream(bytes, writable: false),
                    details),
                new FakeResponse()));
        }

        public override Task<Response<BlobProperties>> GetPropertiesAsync(
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"{_label}.properties.{Name[(Name.LastIndexOf('/') + 1)..]}");
            cancellationToken.ThrowIfCancellationRequested();
            PropertiesConditions.Add(conditions);
            if (!Present)
            {
                throw new RequestFailedException(404, "Journal object disappeared.");
            }

            if (conditions?.IfMatch is { } expected && !expected.Equals(Etag))
            {
                throw new RequestFailedException(412, "Journal object changed.");
            }

            return Task.FromResult(Response.FromValue(
                BlobsModelFactory.BlobProperties(
                    contentLength: Content.LongLength,
                    eTag: Etag,
                    blobType: BlobType.Block),
                new FakeResponse()));
        }
    }

    private sealed class FakeResponse : Response
    {
        private Stream? _contentStream;
        private string _clientRequestId = string.Empty;

        public override int Status => 200;

        public override string ReasonPhrase => "Synthetic response";

        public override Stream? ContentStream
        {
            get => _contentStream;
            set => _contentStream = value;
        }

        public override string ClientRequestId
        {
            get => _clientRequestId;
            set => _clientRequestId = value;
        }

        public override void Dispose()
        {
            _contentStream?.Dispose();
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(
            string name,
            [NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(
            string name,
            [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }
}
