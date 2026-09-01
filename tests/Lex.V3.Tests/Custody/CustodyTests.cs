using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Custody;

/// <summary>The bytes-before-decode custody contract.</summary>
[TestClass]
public sealed class CustodyTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("<akn>the transport body</akn>");
    private static readonly string BodyDigest = CustodyDigest.Of(Body);
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task TheDecoderIsUnreachableWhenTheStoreRefuses()
    {
        var store = new RecordingStore(new IOException("the lane is unavailable"));
        var decoderRan = false;

        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => BytesBeforeDecode.DecodeAsync(
            Body,
            CustodyClass.NightlyFloor90d,
            store,
            _ => { decoderRan = true; return 0; }));

        Assert.IsFalse(decoderRan);
        Assert.AreEqual(1, store.CreateCalls);
    }

    [TestMethod]
    public async Task APreCancelledCallerReachesNeitherStoreNorDecoder()
    {
        var store = new RecordingStore();
        var decoderRan = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body,
                CustodyClass.NightlyFloor90d,
                store,
                _ => { decoderRan = true; return 0; },
                cancellationToken: cancellation.Token));

        Assert.IsFalse(decoderRan);
        Assert.AreEqual(0, store.CreateCalls);
    }

    [TestMethod]
    public async Task ProviderCancellationWithALiveCallerIsCustodyUnavailability()
    {
        var store = new RecordingStore(new OperationCanceledException("provider timeout"));
        var decoderRan = false;

        var failure = await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body,
                CustodyClass.NightlyFloor90d,
                store,
                _ => { decoderRan = true; return 0; }));

        Assert.IsInstanceOfType<OperationCanceledException>(failure.InnerException);
        Assert.IsFalse(decoderRan);
    }

    [TestMethod]
    public async Task TheStoreIsWrittenBeforeTheDecoderRuns()
    {
        var store = new RecordingStore();

        var result = await BytesBeforeDecode.DecodeAsync(
            Body,
            CustodyClass.NightlyFloor90d,
            store,
            bytes => { store.Order.Add("decode"); return bytes.Length; });

        CollectionAssert.AreEqual(new[] { "store", "decode" }, store.Order);
        Assert.AreEqual(Body.Length, result.Value);
        Assert.AreEqual(BodyDigest, result.Receipt.Reference.ContentSha256);
    }

    [TestMethod]
    public async Task AFailedDecodeStillLeavesTheBytesHeld()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);

            await Assert.ThrowsExactlyAsync<FormatException>(() =>
                BytesBeforeDecode.DecodeAsync<int>(
                    Body,
                    CustodyClass.NightlyFloor90d,
                    store,
                    _ => throw new FormatException("this body does not parse")));

            var held = Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest);
            Assert.IsTrue(File.Exists(held));
            CollectionAssert.AreEqual(Body, File.ReadAllBytes(held));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task AnOversizeBodyIsRefusedBeforeTheStoreIsTouched()
    {
        var store = new RecordingStore();
        var decoderRan = false;

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body,
                CustodyClass.NightlyFloor90d,
                store,
                _ => { decoderRan = true; return 0; },
                maxObjectBytes: 4));

        Assert.IsFalse(decoderRan);
        Assert.AreEqual(0, store.CreateCalls);
    }

    [TestMethod]
    public async Task AReceiptDescribingOtherBytesOrAnotherClassIsRefused()
    {
        var substituted = DifferentBytesSameLength(Body);
        var otherBytes = new RecordingStore(
            lie: (_, custodyClass) => UnenforcedReceipt(substituted, custodyClass));
        var otherClass = new RecordingStore(
            lie: (bytes, _) => UnenforcedReceipt(bytes.Span, CustodyClass.LegalHoldEvidence));

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body, CustodyClass.NightlyFloor90d, otherBytes, _ => 0));
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body, CustodyClass.NightlyFloor90d, otherClass, _ => 0));
    }

    [TestMethod]
    public async Task IntegrityAndPolicyFailuresKeepTheirOwnTypes()
    {
        var integrityStore = new RecordingStore(
            new CustodyIntegrityException("substitution"));
        var policyStore = new RecordingStore(
            new CustodyPolicyException("protection absent"));

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body, CustodyClass.NightlyFloor90d, integrityStore, _ => 0));
        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            BytesBeforeDecode.DecodeAsync(
                Body, CustodyClass.NightlyFloor90d, policyStore, _ => 0));
    }

    [TestMethod]
    public async Task TheInputIsFrozenAgainstACallerThatKeepsWriting()
    {
        var caller = (byte[])Body.Clone();
        var expected = CustodyDigest.Of(caller);
        var store = new RecordingStore(lie: (bytes, custodyClass) =>
        {
            caller[0] = (byte)'X';
            return UnenforcedReceipt(bytes.Span, custodyClass);
        });

        var result = await BytesBeforeDecode.DecodeAsync(
            caller,
            CustodyClass.NightlyFloor90d,
            store,
            bytes => CustodyDigest.Of(bytes.Span));

        Assert.AreEqual(expected, result.Receipt.Reference.ContentSha256);
        Assert.AreEqual(expected, result.Value);
        Assert.AreEqual((byte)'X', caller[0]);
    }

    [TestMethod]
    public async Task HoldingTheSameAddressTwiceIsIdempotentAndDoesNotOverwrite()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            var first = await store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None);
            var path = Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest);
            var written = File.GetLastWriteTimeUtc(path);

            var second = await store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

            Assert.AreEqual(first.Reference, second.Reference);
            Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
            Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task AnAddressHoldingWrongBytesIsDetectedRatherThanTrusted()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            await store.CreateAsync(Body, CustodyClass.NightlyFloor90d, CancellationToken.None);
            File.WriteAllBytes(
                Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest),
                DifferentBytesSameLength(Body));

            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ZeroLengthAndSeparateCustodyLanesArePreserved()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            var empty = await store.CreateAsync(
                ReadOnlyMemory<byte>.Empty,
                CustodyClass.NightlyFloor90d,
                CancellationToken.None);
            await store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None);
            await store.CreateAsync(
                Body, CustodyClass.LegalHoldEvidence, CancellationToken.None);

            Assert.AreEqual(0, empty.Reference.ByteLength);
            Assert.AreEqual(CustodyDigest.Of([]), empty.Reference.ContentSha256);
            Assert.IsTrue(File.Exists(Path.Combine(
                root.FullName, "nightly-floor-90d", BodyDigest)));
            Assert.IsTrue(File.Exists(Path.Combine(
                root.FullName, "legal-hold-evidence", BodyDigest)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task AnUndefinedCustodyClassIsRefusedBeforeAnythingIsReached()
    {
        var undefined = (CustodyClass)47;
        var store = new RecordingStore();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, BodyDigest, Body.Length, undefined));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            BytesBeforeDecode.DecodeAsync(Body, undefined, store, _ => 0));
        Assert.AreEqual(0, store.CreateCalls);
    }

    [TestMethod]
    public async Task AStalePartialDoesNotBlockAtomicPublication()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var directory = Path.Combine(root.FullName, "nightly-floor-90d");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(
                Path.Combine(directory, $"{BodyDigest}.0123456789abcdef.partial"),
                "half"u8.ToArray());

            var store = new FileSystemCustodyStore(root.FullName);
            var receipt = await store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

            Assert.AreEqual(BodyDigest, receipt.Reference.ContentSha256);
            CollectionAssert.AreEqual(Body, File.ReadAllBytes(Path.Combine(directory, BodyDigest)));
            Assert.HasCount(1, Directory.GetFiles(directory, "*.partial"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task CancellationBeforePublishLeavesNoAddressOrOwnPartial()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            using var cancellation = new CancellationTokenSource();
            var store = new FileSystemCustodyStore(
                root.FullName,
                TimeProvider.System,
                beforePublish: cancellation.Cancel);

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.CreateAsync(
                Body,
                CustodyClass.NightlyFloor90d,
                cancellation.Token));

            var directory = Path.Combine(root.FullName, "nightly-floor-90d");
            Assert.IsFalse(File.Exists(Path.Combine(directory, BodyDigest)));
            Assert.IsEmpty(Directory.GetFiles(directory, "*.partial"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ADirectoryAtAContentAddressIsAnIntegrityIncident()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var lane = Directory.CreateDirectory(
                Path.Combine(root.FullName, "nightly-floor-90d"));
            Directory.CreateDirectory(Path.Combine(lane.FullName, BodyDigest));
            var store = new FileSystemCustodyStore(root.FullName);

            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.CreateAsync(
                Body,
                CustodyClass.NightlyFloor90d,
                CancellationToken.None));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ALaneSymbolicLinkCannotRedirectCustodyOutsideItsLane()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root.FullName, "redirect-target"));
            try
            {
                Directory.CreateSymbolicLink(
                    Path.Combine(root.FullName, "nightly-floor-90d"),
                    target.FullName);
            }
            catch (IOException) when (OperatingSystem.IsWindows())
            {
                Assert.Inconclusive(
                    "This Windows host does not grant symbolic-link creation; Linux CI runs this proof.");
            }
            var store = new FileSystemCustodyStore(root.FullName);

            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.CreateAsync(
                Body,
                CustodyClass.NightlyFloor90d,
                CancellationToken.None));

            Assert.IsFalse(File.Exists(Path.Combine(target.FullName, BodyDigest)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task TheFilesystemStoreNeverClaimsRetentionItDoesNotEnforce()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            foreach (var custodyClass in Enum.GetValues<CustodyClass>())
            {
                var receipt = await store.CreateAsync(Body, custodyClass, CancellationToken.None);
                Assert.AreEqual(CustodyProtection.NotEnforced, receipt.PolicyEvidence.Protection);
                Assert.AreEqual(
                    CustodyVerificationProfile.FileSystemUnenforced1,
                    receipt.PolicyEvidence.VerificationProfile);
                Assert.IsNull(receipt.PolicyEvidence.PolicyKey);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void CustodyDocumentsAreClosedAndCarryNoStorageCoordinate()
    {
        var receipt = UnenforcedReceipt(Body, CustodyClass.LegalHoldEvidence);
        var json = ContractJson.Serialize(receipt);
        var restored = ContractJson.Deserialize<DurableBlobWriteReceipt>(json);

        Assert.AreEqual(receipt.Reference, restored.Reference);
        Assert.AreEqual(ObservedAt, restored.VerifiedAt);

        string[] forbidden =
        [
            "account", "container", "bucket", "region", "endpoint", "locator", "url", "uri",
            "path", "file_path", "blob_name", "version_id",
        ];
        using var document = JsonDocument.Parse(json);
        foreach (var name in MemberNames(document.RootElement))
        {
            Assert.IsFalse(forbidden.Contains(name, StringComparer.Ordinal), name);
        }
    }

    private static DurableBlobWriteReceipt UnenforcedReceipt(
        ReadOnlySpan<byte> bytes,
        CustodyClass custodyClass)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            CustodyDigest.Of(bytes),
            bytes.Length,
            custodyClass);
        var evidence = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.NotEnforced,
            ObservedAt,
            null);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            evidence);
    }

    private static byte[] DifferentBytesSameLength(ReadOnlySpan<byte> bytes)
    {
        var changed = bytes.ToArray();
        changed[0] ^= 0x01;
        return changed;
    }

    private static IEnumerable<string> MemberNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in MemberNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in MemberNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private sealed class RecordingStore : ICustodyStore
    {
        private readonly Exception? _failure;
        private readonly Func<ReadOnlyMemory<byte>, CustodyClass, DurableBlobWriteReceipt>? _lie;

        public RecordingStore(
            Exception? failure = null,
            Func<ReadOnlyMemory<byte>, CustodyClass, DurableBlobWriteReceipt>? lie = null)
        {
            _failure = failure;
            _lie = lie;
        }

        public int CreateCalls { get; private set; }

        public List<string> Order { get; } = [];

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            Order.Add("store");
            if (_failure is not null)
            {
                return Task.FromException<DurableBlobWriteReceipt>(_failure);
            }

            return Task.FromResult(
                _lie?.Invoke(bytes, custodyClass) ?? UnenforcedReceipt(bytes.Span, custodyClass));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
