using System.Security.Cryptography;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class ContentAddressedCustodyReopeningTests
{
    [TestMethod]
    public async Task CheckedReadUsesOnlyLowercaseDigestAndFreezesReturnedMemory()
    {
        byte[] storeOwnedBytes = [0x00, 0x7f, 0x80, 0xff];
        var expected = storeOwnedBytes.ToArray();
        var digest = Sha256(expected);
        var store = new DigestOnlyStore(storeOwnedBytes);

        var restored = await CustodyRestore.ReadByDigestCheckedAsync(
            store,
            digest,
            CancellationToken.None);

        Assert.AreEqual(1, store.DigestReadCount);
        Assert.AreEqual(digest, store.RequestedDigest);
        CollectionAssert.AreEqual(expected, restored.ToArray());

        storeOwnedBytes.AsSpan().Fill(0x5a);
        CollectionAssert.AreEqual(expected, restored.ToArray());
    }

    [TestMethod]
    public async Task CheckedReadRejectsUppercaseAndMalformedDigestsBeforeStoreAccess()
    {
        var store = new DigestOnlyStore([]);
        string[] invalidDigests =
        [
            new('A', 64),
            new('0', 63),
            $"g{new string('0', 63)}",
        ];

        foreach (var invalidDigest in invalidDigests)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                CustodyRestore.ReadByDigestCheckedAsync(
                    store,
                    invalidDigest,
                    CancellationToken.None));
        }

        Assert.AreEqual(0, store.DigestReadCount);
    }

    [TestMethod]
    public async Task CheckedReadRejectsBytesWhoseHashDoesNotMatchDigest()
    {
        byte[] expectedBytes = [0x10, 0x20, 0x30];
        byte[] corruptBytes = [0x10, 0x20, 0x31];
        var store = new DigestOnlyStore(corruptBytes);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyRestore.ReadByDigestCheckedAsync(
                store,
                Sha256(expectedBytes),
                CancellationToken.None));

        Assert.AreEqual(1, store.DigestReadCount);
    }

    [TestMethod]
    public async Task CheckedReadRejectsMissingFileSystemArtifact()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSystemCustodyStore(directory.Root);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyRestore.ReadByDigestCheckedAsync(
                store,
                new string('0', 64),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task FileSystemStoreReopensDigestHeldInEitherCustodyClass()
    {
        byte[] bytes = [0x01, 0x02, 0x03, 0x04];
        var digest = Sha256(bytes);

        foreach (var custodyClass in Enum.GetValues<CustodyClass>())
        {
            using var directory = new TemporaryDirectory();
            var store = new FileSystemCustodyStore(directory.Root);
            await store.CreateAsync(bytes, custodyClass, CancellationToken.None);

            var restored = await CustodyRestore.ReadByDigestCheckedAsync(
                store,
                digest,
                CancellationToken.None);

            CollectionAssert.AreEqual(
                bytes,
                restored.ToArray(),
                $"Digest reopening failed for {custodyClass}.");
        }
    }

    [TestMethod]
    public async Task FileSystemStoreSafelyCollapsesIdenticalBytesHeldInBothCustodyClasses()
    {
        byte[] bytes = [0xde, 0xad, 0xbe, 0xef];
        var digest = Sha256(bytes);
        using var directory = new TemporaryDirectory();
        var store = new FileSystemCustodyStore(directory.Root);

        var nightly = await store.CreateAsync(
            bytes,
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        var legalHold = await store.CreateAsync(
            bytes,
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None);

        Assert.AreEqual(digest, nightly.Reference.ContentSha256);
        Assert.AreEqual(digest, legalHold.Reference.ContentSha256);

        var restored = await CustodyRestore.ReadByDigestCheckedAsync(
            store,
            digest,
            CancellationToken.None);

        CollectionAssert.AreEqual(bytes, restored.ToArray());
    }

    [TestMethod]
    public async Task FileSystemStoreRefusesACorruptSecondLaneOnItsOwnDigestAfterAValidFirstLane()
    {
        // The filesystem twin of the Azure property: every lane holding the digest is read and
        // verified, so a corrupt copy in the second lane refuses even though the first lane's copy
        // was valid. Corruption is applied to the retained file on disk, not through the store.
        byte[] bytes = [0xde, 0xad, 0xbe, 0xef];
        var digest = Sha256(bytes);
        using var directory = new TemporaryDirectory();
        var store = new FileSystemCustodyStore(directory.Root);
        _ = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        _ = await store.CreateAsync(bytes, CustodyClass.LegalHoldEvidence, CancellationToken.None);

        var copies = Directory.GetFiles(directory.Root, digest, SearchOption.AllDirectories);
        Assert.AreEqual(2, copies.Length, "both lanes hold the digest");
        var legalHold = copies.Single(path => path.Contains("legal-hold", StringComparison.Ordinal));
        var corrupt = bytes.ToArray();
        corrupt[0] ^= 0xff;
        await File.WriteAllBytesAsync(legalHold, corrupt);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            store.ReadByDigestAsync(digest, CancellationToken.None));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class DigestOnlyStore : ICustodyStore
    {
        private readonly ReadOnlyMemory<byte> _returnedBytes;

        public DigestOnlyStore(byte[] returnedBytes)
        {
            _returnedBytes = returnedBytes;
        }

        public int DigestReadCount { get; private set; }

        public string? RequestedDigest { get; private set; }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DigestReadCount++;
            RequestedDigest = contentSha256;
            return Task.FromResult(_returnedBytes);
        }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"lex-v3-digest-reopen-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
