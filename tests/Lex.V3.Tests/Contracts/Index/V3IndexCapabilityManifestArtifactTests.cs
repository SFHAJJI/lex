using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Index;

[TestClass]
public sealed class V3IndexCapabilityManifestArtifactTests
{
    private const string IndexDigest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ArtifactResourceId = "urn:uuid:8eac41ab-50cd-4dab-9f2c-c7425f1ef657";

    [TestMethod]
    public void WriterIsCanonicalAndReaderReopensTheExactManifest()
    {
        var manifest = Create(
            Cell("search", "fts_title", "title", "fra", 2020, 2024, 41),
            Cell("resolve", "objects", "identity", "fra", 2010, 2019, 17));

        using var first = new MemoryStream();
        var digest = V3IndexCapabilityManifestArtifact.Write(first, manifest);
        using var second = new MemoryStream();
        var secondDigest = V3IndexCapabilityManifestArtifact.Write(second, manifest);

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.AreEqual(digest, secondDigest);
        Assert.AreEqual(
            "{\"schema\":\"lex-v3-index-capability-manifest/1\",\"publisher\":\"lu-legilux\","
            + "\"index_sha256\":\"" + IndexDigest + "\",\"period_granularity\":\"day\",\"cells\":["
            + "{\"operation\":\"resolve\",\"column\":\"objects\",\"field\":\"identity\","
            + "\"language\":\"fra\",\"period_from\":\"2010-01-01\",\"period_to\":\"2019-12-31\","
            + "\"population\":17},{\"operation\":\"search\",\"column\":\"fts_title\","
            + "\"field\":\"title\",\"language\":\"fra\",\"period_from\":\"2020-01-01\","
            + "\"period_to\":\"2024-12-31\",\"population\":41}]}\n",
            Encoding.UTF8.GetString(first.ToArray()));

        var reopened = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(ArtifactResourceId, digest),
            first.ToArray(),
            PublisherId.LuLegilux,
            IndexDigest);

        Assert.AreEqual(manifest.Publisher, reopened.Publisher);
        Assert.AreEqual(manifest.IndexSha256, reopened.IndexSha256);
        CollectionAssert.AreEqual(manifest.Cells.ToArray(), reopened.Cells.ToArray());
    }

    [TestMethod]
    public void ReaderRefusesDigestSchemaGranularityAndUnknownMemberDrift()
    {
        var bytes = Write(Create(Cell("search", "fts_title", "title", "fra", 2020, 2024, 41)), out _);

        Assert.ThrowsExactly<ArgumentException>(() => V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(ArtifactResourceId, new string('a', 64)),
            bytes,
            PublisherId.LuLegilux,
            IndexDigest));
        AssertInvalid(Replace(bytes, V3IndexCapabilityManifestArtifact.SchemaId, "wrong-schema"));
        AssertInvalid(Replace(bytes, "\"period_granularity\":\"day\"", "\"period_granularity\":\"month\""));
        AssertInvalid(Replace(bytes, "\"population\":41", "\"population\":41,\"extra\":true"));
        AssertInvalid(Replace(bytes, "\"cells\":[{", "\"cells\":[null,{"));

        void AssertInvalid(byte[] candidate)
        {
            var candidateDigest = V3IndexCapabilityManifestArtifact.ComputeSha256(candidate);
            Assert.ThrowsExactly<ArgumentException>(() => V3IndexCapabilityManifestArtifact.ParseAndVerify(
                new SourceArtifactRef(ArtifactResourceId, candidateDigest),
                candidate,
                PublisherId.LuLegilux,
                IndexDigest));
        }
    }

    [TestMethod]
    public void ReaderRefusesNoncanonicalBytesEvenWhenTheirDigestMatches()
    {
        var bytes = Write(Create(Cell("search", "fts_title", "title", "fra", 2020, 2024, 41)), out _);
        var noncanonical = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(
            "{\"schema\"", "{ \"schema\"", StringComparison.Ordinal));
        var digest = V3IndexCapabilityManifestArtifact.ComputeSha256(noncanonical);

        Assert.ThrowsExactly<ArgumentException>(() => V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(ArtifactResourceId, digest),
            noncanonical,
            PublisherId.LuLegilux,
            IndexDigest));
    }

    [TestMethod]
    public void ReaderRefusesPublisherIndexAndCellInvariantDrift()
    {
        var bytes = Write(Create(Cell("search", "fts_title", "title", "fra", 2020, 2024, 41)), out _);

        AssertInvalid(Replace(bytes, "\"publisher\":\"lu-legilux\"", "\"publisher\":\"eu-eurlex\""));
        AssertInvalid(Replace(bytes, IndexDigest, new string('b', 64)));
        AssertInvalid(Replace(bytes, "\"population\":41", "\"population\":0"));
        AssertInvalid(Replace(bytes, "\"period_from\":\"2020-01-01\"", "\"period_from\":\"2025-01-01\""));

        void AssertInvalid(byte[] candidate)
        {
            var digest = V3IndexCapabilityManifestArtifact.ComputeSha256(candidate);
            Assert.ThrowsExactly<ArgumentException>(() => V3IndexCapabilityManifestArtifact.ParseAndVerify(
                new SourceArtifactRef(ArtifactResourceId, digest),
                candidate,
                PublisherId.LuLegilux,
                IndexDigest));
        }
    }

    private static byte[] Write(V3IndexCapabilityManifest manifest, out string digest)
    {
        using var stream = new MemoryStream();
        digest = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
        return stream.ToArray();
    }

    private static byte[] Replace(byte[] bytes, string oldValue, string newValue) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(oldValue, newValue, StringComparison.Ordinal));

    private static V3IndexCapabilityManifest Create(params V3IndexCapabilityCell[] cells)
    {
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            IndexDigest,
            cells,
            out var manifest,
            out var refusal), refusal.ToString());
        return manifest!;
    }

    private static V3IndexCapabilityCell Cell(
        string operation,
        string column,
        string field,
        string language,
        int fromYear,
        int toYear,
        long population) =>
        new(
            PublisherId.LuLegilux,
            IndexDigest,
            operation,
            column,
            field,
            language,
            new DateOnly(fromYear, 1, 1),
            new DateOnly(toYear, 12, 31),
            population);
}
