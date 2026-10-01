using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class CompressedFileSystemCustodyTests
{
    [TestMethod]
    public async Task BothCustodyObligationsAndUnenforcedProtectionHoldInEveryLane()
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-obligations-");
        try
        {
            var store = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            foreach (var lane in Enum.GetValues<CustodyClass>())
            {
                var outcome = await CustodyStoreConformance.RunObligationsAsync(store, lane, CancellationToken.None);
                Assert.IsNull(outcome.Failure, outcome.Failure);
                Assert.IsNull(outcome.Refusal, outcome.Refusal);
                Assert.AreEqual(CustodyProtection.NotEnforced, outcome.DeclaredProtection);
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("random")]
    [DataRow("repetitive")]
    public async Task OriginalBytesAndReferencesSurviveCompressionAndASeparateReader(string kind)
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-roundtrip-");
        try
        {
            var bytes = kind == "empty" ? [] : new byte[200_003];
            if (kind == "random") new Random(17).NextBytes(bytes);
            if (kind == "repetitive") Array.Fill(bytes, (byte)'a');
            var store = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
            Assert.AreEqual(CustodyDigest.Of(bytes), receipt.Reference.ContentSha256);
            Assert.AreEqual(bytes.LongLength, receipt.Reference.ByteLength);
            Assert.AreEqual(CustodyVerificationProfile.FileSystemUnenforced1, receipt.PolicyEvidence.VerificationProfile);
            Assert.AreEqual(CustodyProtection.NotEnforced, receipt.PolicyEvidence.Protection);
            var path = ObjectPath(root, receipt.Reference.ContentSha256);
            var encoded = await File.ReadAllBytesAsync(path);
            CollectionAssert.AreEqual("LEXBR01\n"u8.ToArray(), encoded[..8]);
            Assert.AreEqual(bytes.LongLength, BinaryPrimitives.ReadInt64BigEndian(encoded.AsSpan(8, 8)));
            if (kind == "repetitive") Assert.IsLessThan(bytes.Length / 10, encoded.Length);
            var reader = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            CollectionAssert.AreEqual(bytes, (await reader.ReadAsync(receipt.Reference, CancellationToken.None)).ToArray());
            CollectionAssert.AreEqual(bytes, (await reader.ReadByDigestAsync(receipt.Reference.ContentSha256, CancellationToken.None)).ToArray());
        }
        finally { root.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task RepeatedAndConcurrentCreatesDoNotOverwriteTheAddress()
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-idempotent-");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(new string('a', 80_000));
            var store = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None)));
            Assert.IsTrue(results.All(r => r.Reference == results[0].Reference));
            var path = ObjectPath(root, results[0].Reference.ContentSha256);
            var before = await File.ReadAllBytesAsync(path);
            var timestamp = File.GetLastWriteTimeUtc(path);
            await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
            Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally { root.Delete(recursive: true); }
    }

    [TestMethod]
    [DataRow("magic")]
    [DataRow("oversized-header")]
    [DataRow("short-length")]
    [DataRow("long-length")]
    [DataRow("truncated-header")]
    [DataRow("truncated-payload")]
    [DataRow("changed-content")]
    [DataRow("trailing-byte")]
    [DataRow("second-stream")]
    public async Task CorruptObjectsRefuseBothReadDoorsAndIdempotentCreate(string fault)
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-corrupt-");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(new string('a', 4000));
            var store = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
            var path = ObjectPath(root, receipt.Reference.ContentSha256);
            var encoded = await File.ReadAllBytesAsync(path);
            switch (fault)
            {
                case "magic": encoded[0] ^= 1; break;
                case "oversized-header": BinaryPrimitives.WriteInt64BigEndian(encoded.AsSpan(8), CustodyBounds.MaxObjectBytes + 1L); break;
                case "short-length": BinaryPrimitives.WriteInt64BigEndian(encoded.AsSpan(8), bytes.Length - 1L); break;
                case "long-length": BinaryPrimitives.WriteInt64BigEndian(encoded.AsSpan(8), bytes.Length + 1L); break;
                case "truncated-header": encoded = encoded[..8]; break;
                case "truncated-payload": encoded = encoded[..^1]; break;
                case "changed-content": encoded = Encode(Encoding.UTF8.GetBytes(new string('b', bytes.Length))); break;
                case "trailing-byte": encoded = [.. encoded, 0]; break;
                case "second-stream": encoded = [.. encoded, .. encoded[16..]]; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault));
            }
            await File.WriteAllBytesAsync(path, encoded);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.ReadAsync(receipt.Reference, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.ReadByDigestAsync(receipt.Reference.ContentSha256, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None));
            CollectionAssert.AreEqual(encoded, await File.ReadAllBytesAsync(path));
            Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.partial"));
        }
        finally { root.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task AValidFirstLaneCannotHideACorruptSecondLane()
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-lanes-");
        try
        {
            var store = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            var bytes = "a retained object"u8.ToArray();
            var first = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
            await store.CreateAsync(bytes, CustodyClass.LegalHoldEvidence, CancellationToken.None);
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "legal-hold-evidence", first.Reference.ContentSha256 + ".br"), Encode("a substituted obj"u8.ToArray()));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.ReadByDigestAsync(first.Reference.ContentSha256, CancellationToken.None));
        }
        finally { root.Delete(recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PrivateCopyAndCancellationBeforePublishRemainEffective(bool cancel)
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-publish-");
        try
        {
            var caller = "unchanged bytes"u8.ToArray();
            var original = caller.ToArray();
            using var cancellation = new CancellationTokenSource();
            var store = new FileSystemCustodyStore(root.FullName, TimeProvider.System, () =>
            {
                caller[0] = (byte)'X';
                if (cancel) cancellation.Cancel();
            }, brotli: true);
            if (cancel)
            {
                await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.CreateAsync(caller, CustodyClass.NightlyFloor90d, cancellation.Token));
                Assert.IsEmpty(Directory.GetFiles(Path.Combine(root.FullName, "nightly-floor-90d")));
            }
            else
            {
                var receipt = await store.CreateAsync(caller, CustodyClass.NightlyFloor90d, cancellation.Token);
                Assert.AreEqual(CustodyDigest.Of(original), receipt.Reference.ContentSha256);
                CollectionAssert.AreEqual(original, (await store.ReadAsync(receipt.Reference, CancellationToken.None)).ToArray());
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task ADirectoryAtTheCompressedAddressIsNeverReplaced()
    {
        var root = Directory.CreateTempSubdirectory("lex-brotli-occupied-");
        try
        {
            var bytes = "preserve occupied path"u8.ToArray();
            var path = ObjectPath(root, CustodyDigest.Of(bytes));
            Directory.CreateDirectory(path);
            var store = FileSystemCustodyStore.WithBrotliCompression(root.FullName);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None));
            Assert.IsTrue(Directory.Exists(path));
        }
        finally { root.Delete(recursive: true); }
    }

    private static string ObjectPath(DirectoryInfo root, string digest) =>
        Path.Combine(root.FullName, "nightly-floor-90d", digest + ".br");

    private static byte[] Encode(byte[] bytes)
    {
        using var output = new MemoryStream();
        output.Write("LEXBR01\n"u8);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.LongLength);
        output.Write(length);
        using (var compressed = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressed.Write(bytes);
        return output.ToArray();
    }
}
