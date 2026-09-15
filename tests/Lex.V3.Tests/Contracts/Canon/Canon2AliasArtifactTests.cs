using System.Text;
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
    public void DuplicateSourceIsRefused()
    {
        var artifact = Canon2AliasArtifact.TryCreate(
            [Entry("old-a", "new-a", "article-1"), Entry("old-a", "new-b", "article-1")],
            out var refusal,
            out _);

        Assert.IsNull(artifact);
        Assert.AreEqual(Canon2AliasArtifactRefusal.DuplicateSource, refusal);
    }

    [TestMethod]
    public void DuplicateTargetIsRefused()
    {
        var artifact = Canon2AliasArtifact.TryCreate(
            [Entry("old-a", "new-a", "article-1"), Entry("old-b", "new-a", "article-2")],
            out var refusal,
            out _);

        Assert.IsNull(artifact);
        Assert.AreEqual(Canon2AliasArtifactRefusal.DuplicateTarget, refusal);
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
    public void ChainsAndCyclesAreRefused()
    {
        var chain = Canon2AliasArtifact.TryCreate(
            [Entry("old-a", "middle", "article-1"), Entry("middle", "new-b", "article-2")],
            out var chainRefusal,
            out _);
        var cycle = Canon2AliasArtifact.TryCreate(
            [Entry("old-a", "old-b", "article-1"), Entry("old-b", "old-a", "article-2")],
            out var cycleRefusal,
            out _);

        Assert.IsNull(chain);
        Assert.AreEqual(Canon2AliasArtifactRefusal.NonDirectGraph, chainRefusal);
        Assert.IsNull(cycle);
        Assert.AreEqual(Canon2AliasArtifactRefusal.NonDirectGraph, cycleRefusal);
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
}
