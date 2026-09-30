using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using static Lex.V3.Ingest.Tests.EuAcquisitionTestFixture;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class ChunkedDerivedArtifactTests
{
    private const string Kind = "test-derived-canonical/1";

    [TestMethod]
    public async Task OrderedChunksReopenExactlyAcrossBoundariesAndRepeatedSeeks()
    {
        var store = new EuInMemoryCustodyStore();
        var bytes = Payload(ChunkedDerivedArtifact.ChunkSize * 2 + 19);
        var (receipt, chunks) = await Write(store, bytes);
        Assert.HasCount(3, chunks);
        Assert.IsTrue(chunks.All(value => value.Reference.ByteLength <= ChunkedDerivedArtifact.ChunkSize));
        var opened = await ChunkedDerivedArtifact.OpenAsync(store, receipt.Reference.ContentSha256, Kind, CancellationToken.None);
        Assert.AreEqual((long)bytes.Length, opened.ByteLength);
        Assert.AreEqual(CustodyDigest.Of(bytes), opened.ContentSha256);
        using var stream = opened.OpenRead();
        Assert.AreEqual(ContentDerivedIdentity.DeriveUuidUrn(Kind, bytes),
            ContentDerivedIdentity.DeriveUuidUrnFromStream(Kind, stream));
        stream.Position = ChunkedDerivedArtifact.ChunkSize - 7;
        var across = new byte[31];
        stream.ReadExactly(across);
        CollectionAssert.AreEqual(bytes.AsSpan(ChunkedDerivedArtifact.ChunkSize - 7, 31).ToArray(), across);
        stream.Seek(-19, SeekOrigin.End);
        var tail = new byte[19];
        stream.ReadExactly(tail);
        CollectionAssert.AreEqual(bytes[^19..], tail);
        Assert.AreEqual(-1, stream.ReadByte());
        Assert.ThrowsExactly<IOException>(() => stream.Seek(1, SeekOrigin.End));
    }

    [TestMethod]
    public async Task EqualRepeatedChunksRemainInTheOrderedSequence()
    {
        var store = new EuInMemoryCustodyStore();
        var bytes = new byte[ChunkedDerivedArtifact.ChunkSize * 2];
        var (receipt, chunks) = await Write(store, bytes);
        Assert.HasCount(2, chunks);
        Assert.AreEqual(chunks[0].Reference.ContentSha256, chunks[1].Reference.ContentSha256);
        var opened = await ChunkedDerivedArtifact.OpenAsync(store, receipt.Reference.ContentSha256, Kind, CancellationToken.None);
        using var stream = opened.OpenRead();
        Assert.AreEqual(ContentDerivedIdentity.DeriveUuidUrn(Kind, bytes),
            ContentDerivedIdentity.DeriveUuidUrnFromStream(Kind, stream));
    }

    [TestMethod]
    public async Task MissingAndReorderedChunksCannotBeOpenedAsTheOriginalContent()
    {
        var store = new EuInMemoryCustodyStore();
        var bytes = Payload(ChunkedDerivedArtifact.ChunkSize * 2 + 19);
        var (receipt, _) = await Write(store, bytes);
        var raw = await store.ReadByDigestAsync(receipt.Reference.ContentSha256, CancellationToken.None);
        var missing = JsonNode.Parse(raw.Span)!.AsObject();
        missing["chunks"]!.AsArray().RemoveAt(0);
        var missingReceipt = await store.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(missing),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ChunkedDerivedArtifact.OpenAsync(
            store, missingReceipt.Reference.ContentSha256, Kind, CancellationToken.None));
        var swapped = JsonNode.Parse(raw.Span)!.AsObject();
        var array = swapped["chunks"]!.AsArray();
        var first = array[0]!.DeepClone();
        array[0] = array[1]!.DeepClone();
        array[1] = first;
        var swappedReceipt = await store.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(swapped),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ChunkedDerivedArtifact.OpenAsync(
            store, swappedReceipt.Reference.ContentSha256, Kind, CancellationToken.None));
        StringAssert.Contains(exception.Message, "complete ordered content digest");
    }

    [TestMethod]
    public async Task AReceiptForAnotherChunkCannotCertifyThisPayload()
    {
        var store = new EuInMemoryCustodyStore();
        var (receipt, _) = await Write(store, Payload(ChunkedDerivedArtifact.ChunkSize + 19));
        var raw = await store.ReadByDigestAsync(receipt.Reference.ContentSha256, CancellationToken.None);
        var changed = JsonNode.Parse(raw.Span)!.AsObject();
        var chunks = changed["chunks"]!.AsArray();
        chunks[0]!["receiptSha256"] = chunks[1]!["receiptSha256"]!.DeepClone();
        var forged = await store.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(changed),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ChunkedDerivedArtifact.OpenAsync(
            store, forged.Reference.ContentSha256, Kind, CancellationToken.None));
        StringAssert.Contains(exception.Message, "does not bind its exact payload");
    }

    [TestMethod]
    public async Task FailedChunkHoldReleasesTheBlockedProducerWithoutPublishingARoot()
    {
        var attempts = 0;
        var store = new EuInMemoryCustodyStore(failWriteDigest: (_, _) => ++attempts == 3);
        var bytes = Payload(ChunkedDerivedArtifact.ChunkSize * 5 + 7);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(async () =>
            await Write(store, bytes).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(3, store.CreateCallCount);
    }

    [TestMethod]
    public async Task ANewReadPassRechecksCustodyInsteadOfReusingThePreviousChunk()
    {
        var revoke = false;
        var store = new EuInMemoryCustodyStore(loseBytesAfterWriteDigest: (_, _) => revoke);
        var bytes = Payload(111);
        var (receipt, _) = await Write(store, bytes);
        var opened = await ChunkedDerivedArtifact.OpenAsync(store, receipt.Reference.ContentSha256, Kind, CancellationToken.None);
        using var stream = opened.OpenRead();
        Assert.AreEqual(bytes[0], (byte)stream.ReadByte());
        revoke = true;
        await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        stream.Position = 0;
        Assert.ThrowsExactly<CustodyRequiredException>(() => stream.ReadByte());
    }

    [TestMethod]
    public async Task AnUnenforcedChunkDoesNotBecomeFlooredBecauseItsRootIsFloored()
    {
        var bytes = Payload(ChunkedDerivedArtifact.ChunkSize + 9);
        var firstDigest = CustodyDigest.Of(bytes.AsSpan(0, ChunkedDerivedArtifact.ChunkSize));
        var store = new EuInMemoryCustodyStore(unenforceDigest: digest => digest == firstDigest);
        var (root, chunks) = await Write(store, bytes);
        Assert.AreEqual(CustodyMembership.Floored, CustodyMembershipClassifier.Classify(root));
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, CustodyMembershipClassifier.Classify(chunks[0]));
        Assert.AreEqual(CustodyMembership.Floored, CustodyMembershipClassifier.Classify(chunks[1]));
        var reopened = await ChunkedDerivedArtifact.OpenAsync(store, root.Reference.ContentSha256, Kind, CancellationToken.None);
        Assert.HasCount(2, reopened.ChunkReceipts);
        Assert.AreEqual(CustodyMembership.RetainedUnenforced, CustodyMembershipClassifier.Classify(reopened.ChunkReceipts[0]));
        Assert.AreEqual(CustodyMembership.Floored, CustodyMembershipClassifier.Classify(reopened.ChunkReceipts[1]));
    }

    private static Task<(DurableBlobWriteReceipt RootReceipt, IReadOnlyList<DurableBlobWriteReceipt> ChunkReceipts)>
        Write(ICustodyStore store, byte[] bytes) => ChunkedDerivedArtifact.WriteAsync(store, Kind, stream =>
        {
            // Vary the producer's writes independently of storage chunk boundaries.
            for (var offset = 0; offset < bytes.Length; offset += 7919)
                stream.Write(bytes.AsSpan(offset, Math.Min(7919, bytes.Length - offset)));
            return CustodyDigest.Of(bytes);
        }, CancellationToken.None);

    private static byte[] Payload(int count) => Enumerable.Range(0, count)
        .Select(index => (byte)((index / ChunkedDerivedArtifact.ChunkSize + index % 251) % 256)).ToArray();
}
