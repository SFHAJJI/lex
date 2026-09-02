using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ContractControlTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void RequestContractBindsTheOnlyTwoProductTargetsAndReadinessTarget()
    {
        var contract = SyntheticResolveRequestContract.V1;
        var independentDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                SyntheticResolveRequestContract.DigestDomain + "\0" + contract.CanonicalDescriptor())))
            .ToLowerInvariant();

        Assert.AreEqual("lex-v3-synthetic-resolve-request/1", contract.ContractId);
        Assert.AreEqual("GET", contract.Method);
        Assert.AreEqual(2048, contract.MaximumApplicationRawTargetBytes);
        CollectionAssert.AreEqual(
            new[]
            {
                "/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview",
                "/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview",
            },
            contract.ProductRawTargets.ToArray());
        Assert.AreEqual("GET", contract.ReadinessMethod);
        Assert.AreEqual("/health/ready", contract.ReadinessTarget);
        Assert.AreEqual(
            "{\"contract_id\":\"lex-v3-synthetic-resolve-request/1\",\"method\":\"GET\"," +
            "\"maximum_application_raw_target_bytes\":2048," +
            "\"product_raw_targets\":[\"/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview\"," +
            "\"/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview\"]," +
            "\"readiness_method\":\"GET\",\"readiness_target\":\"/health/ready\"}",
            contract.CanonicalDescriptor());
        Assert.AreEqual(independentDigest, contract.Sha256);
    }

    [TestMethod]
    public void ScopeDigestBindsTheOnlyCompleteSyntheticMember()
    {
        var scope = SyntheticSliceScope.CompleteLu;
        var independentDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                SyntheticSliceScope.DigestDomain + "\0" + scope.CanonicalDescriptor())))
            .ToLowerInvariant();

        Assert.AreEqual(PublisherId.LuLegilux, scope.Publisher);
        Assert.IsTrue(scope.Complete);
        Assert.AreEqual(PreviewUpstreamHealth.NotApplicableSynthetic, scope.UpstreamHealth);
        CollectionAssert.AreEqual(new[] { "eli/synthetic-preview" }, scope.EnumeratedMembers.ToArray());
        Assert.AreEqual(independentDigest, scope.Sha256);
    }

    [TestMethod]
    public void BlobDescriptorsEnforceClosedKindMediaAndBounds()
    {
        _ = new SyntheticSliceBlobDescriptor(
            SyntheticSliceBlobKind.SourceTransport,
            Digest,
            1,
            "application/octet-stream");
        _ = new SyntheticSliceBlobDescriptor(
            SyntheticSliceBlobKind.DerivedText,
            Digest,
            1,
            "text/plain;charset=utf-8");
        _ = new SyntheticSliceBlobDescriptor(
            SyntheticSliceBlobKind.SqliteIndex,
            Digest,
            1,
            "application/vnd.sqlite3");

        Assert.ThrowsExactly<ArgumentException>(() => new SyntheticSliceBlobDescriptor(
            SyntheticSliceBlobKind.SourceTransport,
            Digest,
            1,
            "text/plain"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SyntheticSliceBlobDescriptor(
            SyntheticSliceBlobKind.SqliteIndex,
            Digest,
            SyntheticSliceContractLimits.MaximumSqliteBytes + 1L,
            "application/vnd.sqlite3"));
    }

    [TestMethod]
    public void ControlBindsTheReusedCatalogRegistryAndObjectSetSchema()
    {
        var control = CreateControl(CreateBlobs());

        Assert.AreEqual(V3SchemaIds.SyntheticSliceControl, control.Schema);
        Assert.AreSame(PreviewRefusalRegistry.StageZero, control.RefusalRegistry);
        Assert.AreEqual(V3SchemaIds.PreviewObjectSet, control.ObjectSetSchema.Schema);
        Assert.AreEqual(SyntheticResolveRequestContract.V1.Sha256,
            control.OperationCatalog.Entries[0].Request.Sha256);
        Assert.AreEqual(V3SchemaIds.SyntheticResolveEnvelope,
            control.OperationCatalog.Entries[0].Success.Schema);

        var reversed = CreateBlobs().Reverse().ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => CreateControl(reversed));

        var roundTrip = ContractJson.Deserialize<SyntheticSliceControl>(
            ContractJson.Serialize(control));
        Assert.AreEqual(control.IndexStamp.BuildId, roundTrip.IndexStamp.BuildId);
        Assert.AreEqual(control.ResolveRequestContract.Sha256, roundTrip.ResolveRequestContract.Sha256);
    }

    [TestMethod]
    public void ArtifactRequiresItsOwnAndControlSchemaBindingsFromTheClosedTable()
    {
        var table = CreateSchemaTable();
        var artifact = new SyntheticSliceArtifactManifest(
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaResourceIds.SyntheticSliceArtifact,
            Digest,
            "synthetic_preview",
            synthetic: true,
            "synthetic_test",
            new PreviewEnvironment("preview", "s0-05-preview"),
            new PreviewIssuer("preview_attestor", "s0-05-issuer", "s0-05-key"),
            table,
            new SyntheticSliceControlDescriptor(
                V3SchemaIds.SyntheticSliceControl,
                V3SchemaResourceIds.SyntheticSliceControl,
                Digest,
                Digest,
                1,
                "application/json"),
            new PreviewAttestation(
                "preview_mechanics_only",
                "ECDSA-P256-SHA256",
                "ieee-p1363",
                new string('A', 86)));

        Assert.AreSame(table, artifact.SchemaTable);
        Assert.IsTrue(artifact.Synthetic);

        Assert.ThrowsExactly<ArgumentException>(() => new SyntheticSliceArtifactManifest(
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaResourceIds.SyntheticSliceArtifact,
            new string('a', 64),
            "synthetic_preview",
            synthetic: true,
            "synthetic_test",
            artifact.Environment,
            artifact.Issuer,
            table,
            artifact.Control,
            artifact.Attestation));
    }

    private static SyntheticSliceControl CreateControl(
        IReadOnlyList<SyntheticSliceBlobDescriptor> blobs) => new(
        V3SchemaIds.SyntheticSliceControl,
        V3SchemaResourceIds.SyntheticSliceControl,
        SyntheticResolveRequestContract.V1,
        SyntheticSliceOperationCatalog.Create(Digest),
        PreviewRefusalRegistry.StageZero,
        CreateSchemaTable().Members.Single(static member =>
            member.Schema == V3SchemaIds.PreviewObjectSet),
        SyntheticNormalizationProfile.PlainV1,
        SyntheticSliceScope.CompleteLu,
        new PreviewSnapshotReference("s0-05-snapshot", Digest),
        new ComponentIdentity("s0-05-builder", Digest),
        new SyntheticSliceIndexStamp(
            "lex-v3-synthetic-sqlite/1",
            Digest,
            "3.50.4",
            "2025-07-30 19:33:53 abcdef",
            Digest,
            Digest,
            SyntheticSliceScope.CompleteLu.Sha256,
            Digest),
        blobs);

    private static SyntheticSliceBlobDescriptor[] CreateBlobs() =>
        new[]
        {
            new SyntheticSliceBlobDescriptor(
                SyntheticSliceBlobKind.SourceTransport,
                Digest,
                1,
                "application/octet-stream"),
            new SyntheticSliceBlobDescriptor(
                SyntheticSliceBlobKind.DerivedText,
                Digest,
                1,
                "text/plain;charset=utf-8"),
            new SyntheticSliceBlobDescriptor(
                SyntheticSliceBlobKind.SqliteIndex,
                Digest,
                1,
                "application/vnd.sqlite3"),
        };

    private static SyntheticSliceSchemaTable CreateSchemaTable() => new(
        SyntheticSliceSchemaGraph.SchemaIds
            .Select(schema => new SyntheticSliceSchemaMember(
                schema,
                V3SchemaResourceIds.ForWireSchema(schema),
                Digest,
                100))
            .ToArray());
}
