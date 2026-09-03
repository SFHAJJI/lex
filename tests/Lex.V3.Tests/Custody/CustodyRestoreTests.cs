using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class CustodyRestoreTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("synthetic transport bytes");
    private static readonly Guid PolicyKey = Guid.Parse("9bc98c22-13c3-4e7c-88b9-3dc43a47a8a2");

    [TestMethod]
    public async Task ACreatedObjectRestoresTheExactBytesFromAFreshStore()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-read-");
        try
        {
            var writer = new FileSystemCustodyStore(root.FullName);
            var receipt = await writer.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None);

            var reader = new FileSystemCustodyStore(root.FullName);
            var restored = await CustodyRestore.ReadCheckedAsync(
                reader, receipt.Reference, CancellationToken.None);

            CollectionAssert.AreEqual(Body, restored.ToArray());
            Assert.AreEqual(
                CustodyVerificationProfile.FileSystemUnenforced1,
                receipt.PolicyEvidence.VerificationProfile);
            Assert.AreEqual(
                CustodyProtection.NotEnforced,
                receipt.PolicyEvidence.Protection,
                "a filesystem receipt claimed a retention control it does not enforce");
            Assert.IsNull(receipt.PolicyEvidence.ProtectedUntil);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task MissingOrSubstitutedBytesAreIntegrityFailuresAndReturnNothing()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-read-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            var receipt = await store.CreateAsync(
                Body, CustodyClass.NightlyFloor90d, CancellationToken.None);
            var heldPath = Path.Combine(
                root.FullName,
                "nightly-floor-90d",
                receipt.Reference.ContentSha256);

            File.WriteAllBytes(heldPath, DifferentBytesSameLength(Body));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
                () => CustodyRestore.ReadCheckedAsync(
                    store, receipt.Reference, CancellationToken.None));

            File.Delete(heldPath);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
                () => CustodyRestore.ReadCheckedAsync(
                    store, receipt.Reference, CancellationToken.None));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ACheckedRestoreRejectsAStoreThatReturnsOtherBytes()
    {
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var store = new LyingReadStore(DifferentBytesSameLength(Body));

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
            () => CustodyRestore.ReadCheckedAsync(store, reference, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancellationRemainsCancellationAndDoesNotReachTheStore()
    {
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var store = new LyingReadStore(Body);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => CustodyRestore.ReadCheckedAsync(store, reference, cancellation.Token));

        Assert.AreEqual(0, store.ReadCalls);
    }

    [TestMethod]
    public async Task ProviderCancellationWithALiveCallerIsCustodyUnavailability()
    {
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var store = new LyingReadStore(new OperationCanceledException("provider timeout"));

        var failure = await Assert.ThrowsExactlyAsync<CustodyRequiredException>(
            () => CustodyRestore.ReadCheckedAsync(store, reference, CancellationToken.None));

        Assert.IsInstanceOfType<OperationCanceledException>(failure.InnerException);
    }

    [TestMethod]
    public async Task CancellationDuringTheReturnedMemoryCopyCannotReturnVerifiedBytes()
    {
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        using var cancellation = new CancellationTokenSource();
        using var memory = new CancellingMemoryManager(Body, cancellation);
        var store = new LyingReadStore(memory.Memory);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            CustodyRestore.ReadCheckedAsync(store, reference, cancellation.Token));

        Assert.AreEqual(1, store.ReadCalls);
    }

    [TestMethod]
    public void PolicyEvidenceBindsExactBytesAndExactRemainingProtection()
    {
        var observed = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
        var nightly = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var other = ReferenceFor("other bytes"u8, CustodyClass.NightlyFloor90d);

        Assert.ThrowsExactly<ArgumentException>(() => new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            nightly,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.LockedTime,
            observed,
            observed.AddDays(90)));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            nightly,
            CustodyVerificationProfile.ImmutableObject1,
            PolicyKey,
            CustodyProtection.LockedTime,
            observed,
            observed.AddDays(90).AddTicks(-1)));

        var exactFloor = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            nightly,
            CustodyVerificationProfile.ImmutableObject1,
            PolicyKey,
            CustodyProtection.LockedTime,
            observed,
            observed.AddDays(90));

        Assert.AreEqual(observed.AddDays(90), exactFloor.ProtectedUntil);

        Assert.ThrowsExactly<ArgumentException>(() => new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            other,
            exactFloor));
    }

    [TestMethod]
    public void AnActiveLegalHoldIsAnObservationWithoutAFutureExpiryClaim()
    {
        var observed = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
        var keeper = ReferenceFor(Body, CustodyClass.LegalHoldEvidence);

        var evidence = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            keeper,
            CustodyVerificationProfile.ImmutableObject1,
            PolicyKey,
            CustodyProtection.ActiveLegalHold,
            observed,
            protectedUntil: null);

        Assert.IsNull(evidence.ProtectedUntil);
        Assert.ThrowsExactly<ArgumentException>(() => new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            keeper,
            CustodyVerificationProfile.ImmutableObject1,
            PolicyKey,
            CustodyProtection.ActiveLegalHold,
            observed,
            observed.AddYears(100)));
    }

    [TestMethod]
    public void APolicyAndCurrentReceiptRoundTripWithoutAProviderLocator()
    {
        var observed = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.NotEnforced,
            observed,
            protectedUntil: null);
        var receipt = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            policy);

        var json = ContractJson.Serialize(receipt);
        var restored = ContractJson.Deserialize<DurableBlobWriteReceipt>(json);

        Assert.AreEqual(
            CustodyVerificationProfile.FileSystemUnenforced1,
            restored.PolicyEvidence.VerificationProfile);
        Assert.AreEqual(reference, restored.PolicyEvidence.Reference);
        Assert.AreEqual(observed, restored.VerifiedAt());
        Assert.AreEqual("lex-v3-durable-blob-write-receipt/2", restored.Schema);
        foreach (var forbidden in new[]
                 {
                     "account", "container", "endpoint", "url", "uri", "path", "blob_name",
                     "store_binding",
                 })
        {
            Assert.IsFalse(
                json.Contains($"\"{forbidden}\"", StringComparison.OrdinalIgnoreCase),
                $"the receipt exposed provider coordinate {forbidden}");
        }
    }

    [TestMethod]
    public void RetiredReceiptFieldsCannotEnterTheV2Schema()
    {
        var observed = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.NotEnforced,
            observed,
            null);
        var valid = ContractJson.Serialize(new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            policy));

        foreach (var retiredMember in new[]
                 {
                     ",\"written_at\":\"2026-08-31T20:00:00+00:00\"",
                     ",\"retention_enforced\":false",
                 })
        {
            var injected = valid.Insert(valid.Length - 1, retiredMember);
            Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<DurableBlobWriteReceipt>(injected));
        }

        var retiredSchema = valid.Replace(
            "lex-v3-durable-blob-write-receipt/2",
            "lex-v3-durable-blob-write-receipt/1",
            StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<DurableBlobWriteReceipt>(retiredSchema));
    }

    [TestMethod]
    public void CustodyWireEnumsRejectNumericAliases()
    {
        var observed = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
        var reference = ReferenceFor(Body, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.NotEnforced,
            observed,
            null);

        var numericClass = ContractJson.Serialize(reference).Replace(
            "\"custody_class\":\"nightly_floor_90d\"",
            "\"custody_class\":0",
            StringComparison.Ordinal);
        var retiredClass = ContractJson.Serialize(reference).Replace(
            "\"custody_class\":\"nightly_floor_90d\"",
            "\"custody_class\":\"evidence_indefinite\"",
            StringComparison.Ordinal);
        var numericProfile = ContractJson.Serialize(policy).Replace(
            "\"verification_profile\":\"filesystem_unenforced/1\"",
            "\"verification_profile\":0",
            StringComparison.Ordinal);
        var numericProtection = ContractJson.Serialize(policy).Replace(
            "\"protection\":\"not_enforced\"",
            "\"protection\":0",
            StringComparison.Ordinal);

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<DurableBlobRef>(numericClass));
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<DurableBlobRef>(retiredClass));
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<CustodyPolicyEvidence>(numericProfile));
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<CustodyPolicyEvidence>(numericProtection));
    }

    [TestMethod]
    public void AReferenceAboveTheContractBoundCannotBeConstructed()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            new string('a', 64),
            CustodyBounds.MaxObjectBytes + 1,
            CustodyClass.NightlyFloor90d));
    }

    private static DurableBlobRef ReferenceFor(
        ReadOnlySpan<byte> bytes,
        CustodyClass custodyClass) =>
        new(
            CustodySchemaIds.DurableBlobRef,
            CustodyDigest.Of(bytes),
            bytes.Length,
            custodyClass);

    private static byte[] DifferentBytesSameLength(ReadOnlySpan<byte> bytes)
    {
        var changed = bytes.ToArray();
        changed[0] ^= 0x01;
        return changed;
    }

    private sealed class LyingReadStore : ICustodyStore
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly Exception? _failure;

        public LyingReadStore(ReadOnlyMemory<byte> bytes)
        {
            _bytes = bytes;
        }

        public LyingReadStore(Exception failure)
        {
            _failure = failure;
        }

        public int ReadCalls { get; private set; }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            if (_failure is not null)
            {
                return Task.FromException<ReadOnlyMemory<byte>>(_failure);
            }

            return Task.FromResult(_bytes);
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CancellingMemoryManager : MemoryManager<byte>
    {
        private readonly byte[] _bytes;
        private readonly CancellationTokenSource _cancellation;
        private int _spanReads;

        public CancellingMemoryManager(
            ReadOnlySpan<byte> bytes,
            CancellationTokenSource cancellation)
        {
            _bytes = bytes.ToArray();
            _cancellation = cancellation;
        }

        public override Span<byte> GetSpan()
        {
            if (Interlocked.Increment(ref _spanReads) > 1)
            {
                _cancellation.Cancel();
            }

            return _bytes;
        }

        public override MemoryHandle Pin(int elementIndex = 0) =>
            throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
