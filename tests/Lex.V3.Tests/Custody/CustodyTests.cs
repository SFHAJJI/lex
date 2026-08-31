using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Custody;

/// <summary>
/// The bytes-before-decode custody slice.
/// </summary>
/// <remarks>
/// The property under test is an ordering, not a return value, so most of these assert what did
/// not happen. A decoder that runs and then throws is indistinguishable from one that never ran
/// if you only inspect the exception, and the case that matters is exactly the one where the
/// decode fails, because a body that will not parse is the body whose original bytes are needed.
/// </remarks>
[TestClass]
public sealed class CustodyTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("<akn>the transport body</akn>");

    private static readonly string BodyDigest = CustodyDigest.Of(Body);

    /// <summary>A store that records what it was asked and can be told to refuse.</summary>
    private sealed class RecordingStore : ICustodyStore
    {
        private readonly Exception? _refusal;
        private readonly Func<ReadOnlyMemory<byte>, CustodyClass, DurableBlobWriteReceipt>? _lie;

        public RecordingStore(
            Exception? refusal = null,
            Func<ReadOnlyMemory<byte>, CustodyClass, DurableBlobWriteReceipt>? lie = null)
        {
            _refusal = refusal;
            _lie = lie;
        }

        public int Calls { get; private set; }

        public List<string> Order { get; } = [];

        public DurableBlobWriteReceipt Create(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass)
        {
            Calls++;
            Order.Add("store");
            if (_refusal is not null) throw _refusal;
            if (_lie is not null) return _lie(bytes, custodyClass);
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                new DurableBlobRef(
                    CustodySchemaIds.DurableBlobRef,
                    CustodyDigest.Of(bytes.Span),
                    bytes.Length,
                    custodyClass),
                new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
                retentionEnforced: false);
        }
    }

    [TestMethod]
    public void TheDecoderIsUnreachableWhenTheStoreRefuses()
    {
        var store = new RecordingStore(refusal: new IOException("the lane is unavailable"));
        var decoderRan = false;

        Assert.ThrowsExactly<CustodyRequiredException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store,
            _ => { decoderRan = true; return 0; }));

        Assert.IsFalse(decoderRan, "the decoder ran on bytes nothing was holding");
        Assert.AreEqual(1, store.Calls);
    }

    /// <summary>
    /// A withdrawn caller and a refusing store need different answers. Wrapping cancellation as a
    /// custody failure would report an incident every time the process shut down mid-write.
    /// </summary>
    [TestMethod]
    public void CancellationIsNotReportedAsACustodyFailure()
    {
        var store = new RecordingStore(refusal: new OperationCanceledException());
        var decoderRan = false;

        Assert.ThrowsExactly<OperationCanceledException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store,
            _ => { decoderRan = true; return 0; }));

        Assert.IsFalse(decoderRan, "the decoder ran after a cancelled custody write");
    }

    [TestMethod]
    public void TheStoreIsWrittenBeforeTheDecoderRuns()
    {
        var store = new RecordingStore();

        var result = BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store,
            bytes => { store.Order.Add("decode"); return bytes.Length; });

        CollectionAssert.AreEqual(new[] { "store", "decode" }, store.Order);
        Assert.AreEqual(Body.Length, result.Value);
        Assert.AreEqual(BodyDigest, result.Receipt.Reference.ContentSha256);
    }

    /// <summary>
    /// A decode that throws still leaves the bytes held. This is the case the ordering exists for.
    /// </summary>
    [TestMethod]
    public void AFailedDecodeStillLeavesTheBytesHeld()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);

            Assert.ThrowsExactly<FormatException>(() => BytesBeforeDecode.Decode<int>(
                Body, CustodyClass.NightlyFloor90d, store,
                _ => throw new FormatException("this body does not parse")));

            var held = Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest);
            Assert.IsTrue(File.Exists(held), "the bytes were lost with the decode that failed");
            CollectionAssert.AreEqual(Body, File.ReadAllBytes(held));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void AnOversizeBodyIsRefusedBeforeTheStoreIsTouched()
    {
        var store = new OversizeClaimingStore();
        var decoderRan = false;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store,
            _ => { decoderRan = true; return 0; },
            maxObjectBytes: 4));

        Assert.IsFalse(decoderRan);
        Assert.AreEqual(0, store.Calls, "the store was reached for a body over the bound");
    }

    [TestMethod]
    public void TheAdmittedBoundCannotBeRaisedByACaller()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, new OversizeClaimingStore(), _ => 0,
            maxObjectBytes: CustodyBounds.MaxObjectBytes + 1));
    }

    private sealed class OversizeClaimingStore : ICustodyStore
    {
        public int Calls { get; private set; }

        public DurableBlobWriteReceipt Create(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass)
        {
            Calls++;
            throw new InvalidOperationException("the store must not be reached");
        }
    }

    [TestMethod]
    public void AReceiptDescribingOtherBytesIsRefused()
    {
        var store = new RecordingStore(lie: (_, custodyClass) => new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                CustodyDigest.Of("other bytes entirely"u8),
                Body.Length,
                custodyClass),
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            retentionEnforced: false));
        var decoderRan = false;

        Assert.ThrowsExactly<CustodyIntegrityException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store,
            _ => { decoderRan = true; return 0; }));

        Assert.IsFalse(decoderRan);
    }

    [TestMethod]
    public void AReceiptUnderADifferentCustodyClassIsRefused()
    {
        var store = new RecordingStore(lie: (bytes, _) => new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                CustodyDigest.Of(bytes.Span),
                bytes.Length,
                CustodyClass.EvidenceIndefinite),
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            retentionEnforced: false));

        Assert.ThrowsExactly<CustodyIntegrityException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store, _ => 0));
    }

    [TestMethod]
    public void AContentAddressMustBeALowercaseSha256()
    {
        foreach (var bad in new[]
                 {
                     BodyDigest.ToUpperInvariant(),
                     BodyDigest[..63],
                     BodyDigest + "0",
                     "not hexadecimal at all, but exactly sixty four characters long ..",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new DurableBlobRef(
                    CustodySchemaIds.DurableBlobRef, bad, 1, CustodyClass.NightlyFloor90d),
                bad);
        }
    }

    [TestMethod]
    public void AWrongSchemaOrNegativeLengthOrLocalInstantIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new DurableBlobRef(
            "lex-v3-durable-blob-ref/2", BodyDigest, 1, CustodyClass.NightlyFloor90d));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, BodyDigest, -1, CustodyClass.NightlyFloor90d));

        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, BodyDigest, 1, CustodyClass.NightlyFloor90d);

        Assert.ThrowsExactly<ArgumentException>(() => new DurableBlobWriteReceipt(
            "lex-v3-durable-blob-write-receipt/2", reference,
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            retentionEnforced: false));

        Assert.ThrowsExactly<ArgumentException>(() => new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt, reference,
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.FromHours(2)),
            retentionEnforced: false));
    }

    /// <summary>
    /// A capability contract that names a provider has chosen one. This is the same guard the
    /// Facts package carries, for the same reason and against the same list.
    /// </summary>
    [TestMethod]
    public void NoCustodyMemberNamesAStorageCoordinate()
    {
        string[] forbidden =
        [
            "account", "container", "bucket", "region", "endpoint", "locator",
            "url", "uri", "path", "file_path", "blob_name", "version_id",
        ];

        var receipt = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, BodyDigest, Body.Length,
                CustodyClass.EvidenceIndefinite),
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            retentionEnforced: false);

        using var document = JsonDocument.Parse(ContractJson.Serialize(receipt));
        foreach (var name in MemberNames(document.RootElement))
        {
            Assert.IsFalse(
                forbidden.Contains(name, StringComparer.Ordinal),
                $"a custody member is named {name}, which chooses a provider");
        }
    }

    [TestMethod]
    public void AReceiptSurvivesItsOwnRoundTrip()
    {
        var receipt = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, BodyDigest, Body.Length,
                CustodyClass.EvidenceIndefinite),
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            retentionEnforced: false);

        var restored = ContractJson.Deserialize<DurableBlobWriteReceipt>(
            ContractJson.Serialize(receipt));

        Assert.AreEqual(receipt.Reference.ContentSha256, restored.Reference.ContentSha256);
        Assert.AreEqual(receipt.Reference.ByteLength, restored.Reference.ByteLength);
        Assert.AreEqual(CustodyClass.EvidenceIndefinite, restored.Reference.CustodyClass);
        Assert.AreEqual(receipt.WrittenAt, restored.WrittenAt);
    }

    [TestMethod]
    public void HoldingTheSameAddressTwiceIsIdempotentAndDoesNotOverwrite()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            var first = store.Create(Body, CustodyClass.NightlyFloor90d);
            var written = File.GetLastWriteTimeUtc(
                Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest));

            var second = store.Create(Body, CustodyClass.NightlyFloor90d);

            Assert.AreEqual(first.Reference.ContentSha256, second.Reference.ContentSha256);
            Assert.AreEqual(
                written,
                File.GetLastWriteTimeUtc(
                    Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest)),
                "the second create rewrote an object that already existed");
            Assert.HasCount(
                1, Directory.GetFiles(Path.Combine(root.FullName, "nightly-floor-90d")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void AnAddressHoldingTheWrongBytesIsDetectedRatherThanTrusted()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            store.Create(Body, CustodyClass.NightlyFloor90d);

            // Substitute the held object behind its own name, which is the attack the content
            // address exists to make detectable.
            File.WriteAllBytes(
                Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest),
                Encoding.UTF8.GetBytes("<akn>a different body under the same name</akn>"));

            Assert.ThrowsExactly<CustodyIntegrityException>(
                () => store.Create(Body, CustodyClass.NightlyFloor90d));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void AZeroLengthBodyIsHeldRatherThanSkipped()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            var receipt = store.Create(ReadOnlyMemory<byte>.Empty, CustodyClass.NightlyFloor90d);

            Assert.AreEqual(0, receipt.Reference.ByteLength);
            Assert.AreEqual(CustodyDigest.Of([]), receipt.Reference.ContentSha256);
            Assert.IsTrue(File.Exists(Path.Combine(
                root.FullName, "nightly-floor-90d", receipt.Reference.ContentSha256)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void TheTwoCustodyClassesAreHeldApart()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);
            store.Create(Body, CustodyClass.NightlyFloor90d);
            store.Create(Body, CustodyClass.EvidenceIndefinite);

            Assert.IsTrue(File.Exists(Path.Combine(root.FullName, "nightly-floor-90d", BodyDigest)));
            Assert.IsTrue(File.Exists(Path.Combine(root.FullName, "evidence-indefinite", BodyDigest)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A C# enum admits any integer of its underlying type, so a closed vocabulary is closed only
    /// if something checks. Before this, <c>(CustodyClass)47</c> reached a store and a decoder.
    /// </summary>
    [TestMethod]
    public void AnUndefinedCustodyClassIsRefusedBeforeAnythingIsReached()
    {
        var undefined = (CustodyClass)47;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, BodyDigest, Body.Length, undefined));

        var store = new OversizeClaimingStore();
        var decoderRan = false;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BytesBeforeDecode.Decode(
            Body, undefined, store, _ => { decoderRan = true; return 0; }));

        Assert.IsFalse(decoderRan);
        Assert.AreEqual(0, store.Calls, "an undefined class reached the store");
    }

    /// <summary>
    /// A detected substitution is a security incident, not unavailability. I fixed the
    /// cancellation case an hour before noticing the same wrapper swallowed this one.
    /// </summary>
    [TestMethod]
    public void AnIntegrityFailureIsNotReportedAsUnavailability()
    {
        var store = new RecordingStore(
            refusal: new CustodyIntegrityException("the held object is not what it claims"));
        var decoderRan = false;

        Assert.ThrowsExactly<CustodyIntegrityException>(() => BytesBeforeDecode.Decode(
            Body, CustodyClass.NightlyFloor90d, store,
            _ => { decoderRan = true; return 0; }));

        Assert.IsFalse(decoderRan, "the decoder ran after a detected substitution");
    }

    /// <summary>
    /// The caller may still own the backing array. Without a copy at entry, the bytes that were
    /// held, hashed and decoded are three claims about memory somebody else can still write to.
    /// </summary>
    [TestMethod]
    public void TheInputIsFrozenAgainstACallerThatKeepsWriting()
    {
        var caller = (byte[])Body.Clone();
        var expected = CustodyDigest.Of(caller);

        // A store that mutates the caller's array mid-flight, which is what a concurrent caller
        // looks like from in here.
        var store = new RecordingStore(lie: (bytes, custodyClass) =>
        {
            caller[0] = (byte)'X';
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                new DurableBlobRef(
                    CustodySchemaIds.DurableBlobRef,
                    CustodyDigest.Of(bytes.Span),
                    bytes.Length,
                    custodyClass),
                new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
                retentionEnforced: false);
        });

        var result = BytesBeforeDecode.Decode(
            caller, CustodyClass.NightlyFloor90d, store, bytes => CustodyDigest.Of(bytes.Span));

        Assert.AreEqual(expected, result.Receipt.Reference.ContentSha256);
        Assert.AreEqual(expected, result.Value, "the decoder saw bytes the caller changed");
        Assert.AreEqual((byte)'X', caller[0], "the test did not actually mutate the caller array");
    }

    /// <summary>
    /// An interrupted create must leave an ignorable temporary, never a truncated object holding a
    /// content address that every later create can only report as corruption.
    /// </summary>
    [TestMethod]
    public void AnInterruptedCreateNeverPublishesATruncatedAddress()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var directory = Path.Combine(root.FullName, "nightly-floor-90d");
            Directory.CreateDirectory(directory);

            // Exactly what an interrupted create leaves behind under the repaired store.
            File.WriteAllBytes(
                Path.Combine(directory, $"{BodyDigest}.0123456789abcdef.partial"),
                Encoding.UTF8.GetBytes("<akn>half a bo"));

            var store = new FileSystemCustodyStore(root.FullName);
            var receipt = store.Create(Body, CustodyClass.NightlyFloor90d);

            Assert.AreEqual(BodyDigest, receipt.Reference.ContentSha256);
            CollectionAssert.AreEqual(
                Body, File.ReadAllBytes(Path.Combine(directory, BodyDigest)));
            // The planted temporary survives, and that is correct: it belongs to an earlier
            // interrupted attempt, possibly another process still writing it, and sweeping it
            // would be the store deciding it knows better. What matters is that it neither
            // blocked this create nor became the content address. This create leaves none of its
            // own, so exactly the one planted file remains.
            Assert.HasCount(
                1, Directory.GetFiles(directory, "*.partial"),
                "a completed create left its own temporary behind");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// This store enforces no floor, so it may never issue a receipt that says it does.
    /// </summary>
    [TestMethod]
    public void TheFilesystemStoreNeverClaimsRetentionItDoesNotEnforce()
    {
        var root = Directory.CreateTempSubdirectory("lex-custody-");
        try
        {
            var store = new FileSystemCustodyStore(root.FullName);

            foreach (var custodyClass in Enum.GetValues<CustodyClass>())
            {
                var receipt = store.Create(Body, custodyClass);
                Assert.IsFalse(
                    receipt.RetentionEnforced,
                    $"the store claimed an enforced floor for {custodyClass}");
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
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
                        yield return nested;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in MemberNames(item))
                        yield return nested;

                break;
        }
    }
}
