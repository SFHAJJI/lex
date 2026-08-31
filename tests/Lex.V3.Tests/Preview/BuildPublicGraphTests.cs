using System.Security.Cryptography;
using System.Text;
using Lex.V3.Api;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class BuildPublicGraphTests
{
    private const string EnvironmentBinding = "s0-05-preview";
    private const string IssuerId = "s0-05-issuer";
    private const string KeyId = "s0-05-key";
    private const string BuilderSourceSha256 =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public async Task SignedGraphUsesTheRuntimeCandidateNamesAndPassesAdmission()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfo();

        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var fileNames = Directory.EnumerateFiles(root.Path, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedNames = new[]
        {
            "artifact.json",
            $"control.{graph.ControlSha256}.json",
            $"derived_text.{graph.Build.DerivedSha256}.txt",
            $"source_transport.{graph.Build.SourceSha256}.bin",
            $"sqlite_index.{graph.Build.SqliteSha256}.sqlite3",
        }.Order(StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(expectedNames, fileNames);
        var verifier = new SyntheticSliceArtifactVerifier(
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            SyntheticSliceSchemaExporter.ExportSchemaTable(),
            new TestTrustStore(IssuerId, KeyId, publicKey));
        var verification = await verifier.VerifyAsync(
            new ContentAddressedSyntheticCandidate(root.Path),
            CancellationToken.None);
        Assert.IsTrue(verification.Verified, verification.Failure?.ToString());
        Assert.AreEqual(graph.ControlSha256, verification.ControlSha256);
        Assert.AreEqual(graph.ManifestSha256, verification.ManifestSha256);
    }

    [TestMethod]
    public void NoKeyUnsignedRebuildMatchesEveryDeterministicMember()
    {
        using var root = new BuildTestDirectory();
        using var rebuildRoot = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);

        var rebuilt = SyntheticPublicGraphBuilder.VerifyUnsignedGraph(
            root.Path,
            rebuildRoot.Path,
            BuilderSourceSha256);

        Assert.AreEqual(graph.ControlSha256, rebuilt.ControlSha256);
        Assert.AreEqual(graph.Build.SourceSha256, rebuilt.Build.SourceSha256);
        Assert.AreEqual(graph.Build.DerivedSha256, rebuilt.Build.DerivedSha256);
        Assert.AreEqual(graph.Build.SqliteSha256, rebuilt.Build.SqliteSha256);
        Assert.AreEqual(graph.Build.BuildIdentity, rebuilt.Build.BuildIdentity);
    }

    [TestMethod]
    public void GraphContainsNeitherPrivateKeyBytesNorPrivatePemMarkers()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportPkcs8PrivateKey();
        try
        {
            var graph = SyntheticPublicGraphBuilder.BuildAndSign(
                root.Path,
                key,
                EnvironmentBinding,
                IssuerId,
                KeyId,
                BuilderSourceSha256);

            foreach (var path in graph.PublicMemberPaths)
            {
                var bytes = File.ReadAllBytes(path);
                Assert.AreEqual(-1, bytes.AsSpan().IndexOf(privateKey), Path.GetFileName(path));
                Assert.IsFalse(
                    Encoding.ASCII.GetString(bytes).Contains("PRIVATE KEY", StringComparison.Ordinal),
                    Path.GetFileName(path));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    [TestMethod]
    public void UnsignedRebuildRejectsAChangedContentAddressedMember()
    {
        using var root = new BuildTestDirectory();
        using var rebuildRoot = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var sourcePath = graph.PublicMemberPaths.Single(path =>
            Path.GetFileName(path).StartsWith("source_transport.", StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(sourcePath);
        bytes[0] ^= 1;
        File.WriteAllBytes(sourcePath, bytes);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            SyntheticPublicGraphBuilder.VerifyUnsignedGraph(
                root.Path,
                rebuildRoot.Path,
                BuilderSourceSha256));
    }

    [TestMethod]
    public void UnsignedRebuildRejectsAnUnexpectedBuilderSourceIdentity()
    {
        using var root = new BuildTestDirectory();
        using var rebuildRoot = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            SyntheticPublicGraphBuilder.VerifyUnsignedGraph(
                root.Path,
                rebuildRoot.Path,
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
    }

    [TestMethod]
    public async Task CandidateFreeVariantChangesTheIndexLineageAndStillPassesAdmission()
    {
        using var canonicalRoot = new BuildTestDirectory();
        using var candidateFreeRoot = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var canonical = SyntheticPublicGraphBuilder.BuildAndSign(
            canonicalRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var candidateFree = SyntheticPublicGraphBuilder.BuildAndSign(
            candidateFreeRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256,
            includeCandidate: false);

        Assert.AreEqual(canonical.Build.SourceSha256, candidateFree.Build.SourceSha256);
        Assert.AreEqual(canonical.Build.DerivedSha256, candidateFree.Build.DerivedSha256);
        Assert.AreNotEqual(canonical.Build.LogicalRowsSha256, candidateFree.Build.LogicalRowsSha256);
        Assert.AreNotEqual(canonical.Build.BuildIdentity, candidateFree.Build.BuildIdentity);
        Assert.AreNotEqual(canonical.Build.SqliteSha256, candidateFree.Build.SqliteSha256);
        Assert.AreNotEqual(canonical.ControlSha256, candidateFree.ControlSha256);
        var verifier = new SyntheticSliceArtifactVerifier(
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            SyntheticSliceSchemaExporter.ExportSchemaTable(),
            new TestTrustStore(IssuerId, KeyId, publicKey));
        var verification = await verifier.VerifyAsync(
            new ContentAddressedSyntheticCandidate(candidateFreeRoot.Path),
            CancellationToken.None);

        Assert.IsTrue(verification.Verified, verification.Failure?.ToString());
    }

    private sealed class TestTrustStore(
        string issuerId,
        string keyId,
        byte[] subjectPublicKeyInfo) : IPreviewTrustStore
    {
        public bool ContainsIssuer(string candidateIssuerId) =>
            string.Equals(candidateIssuerId, issuerId, StringComparison.Ordinal);

        public bool TryGetSubjectPublicKeyInfo(
            string candidateIssuerId,
            string candidateKeyId,
            out ReadOnlyMemory<byte> publicKey)
        {
            if (string.Equals(candidateIssuerId, issuerId, StringComparison.Ordinal) &&
                string.Equals(candidateKeyId, keyId, StringComparison.Ordinal))
            {
                publicKey = subjectPublicKeyInfo;
                return true;
            }

            publicKey = default;
            return false;
        }
    }
}
