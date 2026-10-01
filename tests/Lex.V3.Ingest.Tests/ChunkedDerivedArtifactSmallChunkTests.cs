using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using static Lex.V3.Ingest.Tests.EuAcquisitionTestFixture;

namespace Lex.V3.Ingest.Tests;

public sealed partial class ChunkedDerivedArtifactTests
{
    [TestMethod]
    public async Task ShortRandomReadsLoadOneSmallChunkAfterCompleteVerification()
    {
        var bytes = Payload(ChunkedDerivedArtifact.ChunkSize * 2 + 19);
        var largeStore = new ReadCountingStore(new EuInMemoryCustodyStore());
        var smallStore = new ReadCountingStore(new EuInMemoryCustodyStore());
        var (largeRoot, _) = await Write(largeStore, bytes);
        var (smallRoot, _) = await Write(smallStore, bytes, smallChunks: true);
        var large = await ChunkedDerivedArtifact.OpenAsync(largeStore, largeRoot.Reference.ContentSha256, Kind, CancellationToken.None);
        var small = await ChunkedDerivedArtifact.OpenAsync(smallStore, smallRoot.Reference.ContentSha256, Kind, CancellationToken.None);
        Assert.AreEqual(large.CanonicalSha256, small.CanonicalSha256);
        Assert.AreEqual(large.ContentSha256, small.ContentSha256);
        Assert.AreEqual(large.ByteLength, small.ByteLength);
        largeStore.ResetReads();
        smallStore.ResetReads();
        using var largeStream = large.OpenRead();
        using var smallStream = small.OpenRead();
        var actual = new byte[17];
        for (var index = 0; index < 100; index++)
        {
            var offset = index * ChunkedDerivedArtifact.SmallChunkSize + 13;
            foreach (var stream in new[] { largeStream, smallStream })
            {
                stream.Seek(offset, SeekOrigin.Begin);
                stream.ReadExactly(actual);
                CollectionAssert.AreEqual(bytes.AsSpan(offset, actual.Length).ToArray(), actual);
            }
        }
        Assert.AreEqual(100L * ChunkedDerivedArtifact.ChunkSize, largeStore.ReadBytes);
        Assert.AreEqual(100L * ChunkedDerivedArtifact.SmallChunkSize, smallStore.ReadBytes);
        Assert.AreEqual(100, smallStore.ReadCalls);
    }

    [TestMethod]
    [DataRow("lex-v3-derived-byte-sequence/1", 65536)]
    [DataRow("lex-v3-derived-byte-sequence/2", 4194304)]
    [DataRow("lex-v3-derived-byte-sequence/2", 0)]
    [DataRow("lex-v3-derived-byte-sequence/2", 65535)]
    [DataRow("lex-v3-derived-byte-sequence/3", 65536)]
    public async Task SchemaAndChunkSizeMustNameAnExactSupportedPair(string schema, int chunkSize)
    {
        var store = new EuInMemoryCustodyStore();
        var (root, _) = await Write(store, Payload(71), smallChunks: true);
        var raw = await store.ReadByDigestAsync(root.Reference.ContentSha256, CancellationToken.None);
        var changed = JsonNode.Parse(raw.Span)!.AsObject();
        changed["schema"] = schema;
        changed["chunkSize"] = chunkSize;
        var forged = await store.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(changed),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            ChunkedDerivedArtifact.OpenAsync(store, forged.Reference.ContentSha256, Kind, CancellationToken.None));
        StringAssert.Contains(error.Message, "schema, kind or chunk size");
    }

    [TestMethod]
    public async Task CorruptionInAnUnreadSmallTailRefusesBeforeAnyStreamIsReturned()
    {
        var store = new ReadCountingStore(new EuInMemoryCustodyStore());
        var bytes = Payload(ChunkedDerivedArtifact.SmallChunkSize * 2 + 19);
        var (root, chunks) = await Write(store, bytes, smallChunks: true);
        store.CorruptDigest = chunks[^1].Reference.ContentSha256;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            ChunkedDerivedArtifact.OpenAsync(store, root.Reference.ContentSha256, Kind, CancellationToken.None));
    }

    [TestMethod]
    public async Task SmallChunksReopenThroughASeparateCompressedStore()
    {
        var directory = Directory.CreateTempSubdirectory("lex-v3-small-chunks-");
        try
        {
            var bytes = Payload(ChunkedDerivedArtifact.SmallChunkSize * 3 + 37);
            var writer = FileSystemCustodyStore.WithBrotliCompression(directory.FullName);
            var (root, chunks) = await Write(writer, bytes, smallChunks: true);
            Assert.HasCount(4, chunks);
            var reader = FileSystemCustodyStore.WithBrotliCompression(directory.FullName);
            var rootBytes = await reader.ReadByDigestAsync(root.Reference.ContentSha256, CancellationToken.None);
            Assert.IsTrue(ChunkedDerivedArtifact.IsRoot(rootBytes.Span, out var kind));
            Assert.AreEqual(Kind, kind);
            var opened = await ChunkedDerivedArtifact.OpenAsync(reader, root.Reference.ContentSha256, Kind, CancellationToken.None);
            using var stream = opened.OpenRead();
            using var reconstructed = new MemoryStream();
            stream.CopyTo(reconstructed);
            CollectionAssert.AreEqual(bytes, reconstructed.ToArray());
            Assert.IsTrue(opened.ChunkReceipts.All(receipt =>
                CustodyMembershipClassifier.Classify(receipt) == CustodyMembership.RetainedUnenforced));
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task DefaultWriterStillEmitsTheExactLegacyRootShape()
    {
        var store = new EuInMemoryCustodyStore();
        var bytes = Payload(19);
        var (root, chunks) = await Write(store, bytes);
        var digest = CustodyDigest.Of(bytes);
        var expected = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "lex-v3-derived-byte-sequence/1", kind = Kind,
            canonicalSha256 = digest, contentSha256 = digest, byteLength = (long)bytes.Length,
            chunkSize = 4194304,
            chunks = chunks.Select(receipt => new
            {
                sha256 = receipt.Reference.ContentSha256,
                byteLength = (int)receipt.Reference.ByteLength,
                receiptSha256 = CustodyDigest.Of(DurableBlobWriteReceiptDigest.CanonicalBytes(receipt)),
            }),
        });
        var retained = await store.ReadByDigestAsync(root.Reference.ContentSha256, CancellationToken.None);
        CollectionAssert.AreEqual(expected, retained.ToArray());
    }

    private sealed class ReadCountingStore(ICustodyStore inner) : ICustodyStore
    {
        private long _readBytes;
        private int _readCalls;
        internal long ReadBytes => Interlocked.Read(ref _readBytes);
        internal int ReadCalls => Volatile.Read(ref _readCalls);
        internal string? CorruptDigest { get; set; }
        internal void ResetReads()
        {
            Interlocked.Exchange(ref _readBytes, 0);
            Interlocked.Exchange(ref _readCalls, 0);
        }
        public Task<DurableBlobWriteReceipt> CreateAsync(ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass, CancellationToken cancellationToken) =>
            inner.CreateAsync(bytes, custodyClass, cancellationToken);
        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);
        public async Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string digest, CancellationToken cancellationToken)
        {
            var bytes = await inner.ReadByDigestAsync(digest, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _readBytes, bytes.Length);
            Interlocked.Increment(ref _readCalls);
            if (digest == CorruptDigest)
            {
                var changed = bytes.ToArray();
                changed[0] ^= 1;
                return changed;
            }
            return bytes;
        }
    }
}
