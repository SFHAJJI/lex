using System.Text;
using System.Security.Cryptography;
using Lex.V3.Contracts.Canon;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Canon;

[TestClass]
public sealed class Canon2AliasArtifactTests
{
    [TestMethod]
    public void EmptyArtifactIsAFormatValueNotACompletenessClaim()
    {
        var artifact = Canon2AliasArtifact.TryCreate([], out var refusal, out var detail);

        Assert.IsNotNull(artifact);
        Assert.AreEqual(Canon2AliasArtifactRefusal.None, refusal, detail);
        Assert.AreEqual(0, artifact.Entries.Count);
    }

    [TestMethod]
    public void CanonicalBytesDoNotDependOnInputOrder()
    {
        var first = RequireArtifact(Entry("old-b", "new-b", "article-2"), Entry("old-a", "new-a", "article-1"));
        var second = RequireArtifact(Entry("old-a", "new-a", "article-1"), Entry("old-b", "new-b", "article-2"));

        var firstBytes = Write(first, out var firstDigest);
        var secondBytes = Write(second, out var secondDigest);

        CollectionAssert.AreEqual(firstBytes, secondBytes);
        Assert.AreEqual(firstDigest, secondDigest);
        StringAssert.StartsWith(Encoding.UTF8.GetString(firstBytes), "{\"schema\":\"lex-canon-aliases/1\",\"entries\":[");
    }

    [TestMethod]
    public void NonEmptyCanonicalBytesAndDigestArePinned()
    {
        var bytes = Write(RequireArtifact(Entry("old-a", "new-a", "article-1")), out var digest);

        Assert.AreEqual(
            "{\"schema\":\"lex-canon-aliases/1\",\"entries\":[{\"source_coordinate\":{\"publisher\":\"luxembourg\",\"version_key\":\"work-key|fra|2026-01-01\",\"anchor\":\"article-1\"},\"target_coordinate\":{\"publisher\":\"luxembourg\",\"version_key\":\"work-key|fra|2026-01-01\",\"anchor\":\"article-1\"},\"source_identity\":\"old-a\",\"canonicalized_to\":\"new-a\"}]}\n",
            Encoding.UTF8.GetString(bytes));
        Assert.AreEqual("15afeb7c1575d5b6fc470e066f965445ea3c3379af74a6e951c2f02e507efb93", digest);
    }

    [TestMethod]
    public void CoordinateDriftIsRejectedAtTheEntryDoor()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Canon2AliasEntry(
            Coordinate("article-1"),
            Coordinate("article-2"),
            "old-a",
            "new-a"));
    }

    [TestMethod]
    public void SelfEdgeIsRejectedAtTheEntryDoor()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Canon2AliasEntry(
            Coordinate("article-1"),
            Coordinate("article-1"),
            "same",
            "same"));
    }

    [TestMethod]
    public void IdentityTokensAreScopedToTheirCoordinates()
    {
        var artifact = Canon2AliasArtifact.TryCreate(
            [
                Entry("old-a", "middle", "article-1"),
                Entry("old-a", "new-b", "article-2"),
                Entry("old-c", "middle", "article-3"),
                Entry("middle", "old-c", "article-4"),
            ],
            out var refusal,
            out var detail);

        Assert.IsNotNull(artifact, $"{refusal}: {detail}");
        Assert.AreEqual(4, artifact.Entries.Count);
    }

    [TestMethod]
    public void TwoSourcesAtOneCoordinateAreRefusedAsACollision()
    {
        var artifact = Canon2AliasArtifact.TryCreate(
            [Entry("old-a", "new-a", "article-1"), Entry("old-b", "new-b", "article-1")],
            out var refusal,
            out _);

        Assert.IsNull(artifact);
        Assert.AreEqual(Canon2AliasArtifactRefusal.CoordinateCollision, refusal);
    }

    [TestMethod]
    public void VerifiedReaderRejectsTamperingAndReopensExactBytes()
    {
        var artifact = RequireArtifact(Entry("old-a", "new-a", "article-1"));
        var bytes = Write(artifact, out var digest);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            digest);

        var verified = VerifiedCanon2AliasArtifact.ParseAndVerify(artifactRef, bytes);
        Assert.AreEqual("new-a", verified.Artifact.Entries.Single().CanonicalizedTo);

        var tampered = bytes.ToArray();
        tampered[^2] ^= 1;
        Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedCanon2AliasArtifact.ParseAndVerify(artifactRef, tampered));
    }

    [TestMethod]
    public void VerifiedReaderRejectsValidNonCanonicalBytesWithMatchingDigest()
    {
        const string nonCanonical =
            "{ \"entries\": [{\"canonicalized_to\":\"new-a\",\"source_identity\":\"old-a\",\"target_coordinate\":{\"anchor\":\"article-1\",\"version_key\":\"work-key|fra|2026-01-01\",\"publisher\":\"luxembourg\"},\"source_coordinate\":{\"anchor\":\"article-1\",\"version_key\":\"work-key|fra|2026-01-01\",\"publisher\":\"luxembourg\"}}], \"schema\": \"lex-canon-aliases/1\" }";
        var bytes = Encoding.UTF8.GetBytes(nonCanonical);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            ComputeArtifactDigest(bytes));

        Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedCanon2AliasArtifact.ParseAndVerify(artifactRef, bytes));
    }

    private static Canon2AliasArtifact RequireArtifact(params Canon2AliasEntry[] entries)
    {
        var artifact = Canon2AliasArtifact.TryCreate(entries, out var refusal, out var detail);
        Assert.IsNotNull(artifact, $"{refusal}: {detail}");
        return artifact;
    }

    private static Canon2AliasEntry Entry(string source, string target, string anchor) =>
        new(Coordinate(anchor), Coordinate(anchor), source, target);

    private static Canon2AliasCoordinate Coordinate(string anchor) =>
        new("luxembourg", "work-key|fra|2026-01-01", anchor);

    private static byte[] Write(Canon2AliasArtifact artifact, out string digest)
    {
        using var stream = new MemoryStream();
        digest = Canon2AliasArtifactCanonicalWriter.Write(stream, artifact);
        return stream.ToArray();
    }

    private static string ComputeArtifactDigest(ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(Canon2AliasArtifact.SchemaId + "\n"));
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
