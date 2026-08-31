using System.Text;
using System.Text.Json;
using Json.Schema;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ContractSchemaExecutionTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void EnvelopeSchemaAcceptsBothTypedBranchesAndRejectsARequestReferenceMutation()
    {
        var schema = BuildSchema(V3SchemaIds.SyntheticResolveEnvelope);
        var context = CreateContext();
        var body = "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nThis text is synthetic and has no legal authority.\n";
        var result = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "s0-05-result",
            new PreviewObject[]
            {
                new PreviewSyntheticCoordinate(
                    "preview:coordinate:synthetic-preview",
                    synthetic: true,
                    "preview:work:synthetic-preview",
                    "preview:version:synthetic-preview",
                    "preview:article:1",
                    BodyHoldingState.HeldPublic,
                    PreviewBodyDispositionReason.SyntheticFixture,
                    body,
                    PreviewSchemaExporter.ComputeSha256(Encoding.UTF8.GetBytes(body))),
            });
        var success = SyntheticResolveSuccessEnvelope.Create(
            context,
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            result);
        var refusal = SyntheticResolveRefusalEnvelope.Create(
            context,
            SyntheticIdentifierUnknownRefusal.Create(
                new[]
                {
                    new SyntheticHeldRecordCandidate(
                        IdentifierFamily.Eli,
                        "eli/synthetic-preview",
                        PublisherId.LuLegilux),
                }));

        Assert.IsTrue(Evaluate(schema, ContractJson.Serialize<SyntheticResolveEnvelope>(success)).IsValid);
        Assert.IsTrue(Evaluate(schema, ContractJson.Serialize<SyntheticResolveEnvelope>(refusal)).IsValid);

        var mutated = ContractJson.Serialize<SyntheticResolveEnvelope>(success)
            .Replace(
                "req_0123456789abcdef0123456789abcdef",
                "req_0123456789ABCDEF0123456789ABCDEF",
                StringComparison.Ordinal);
        Assert.IsFalse(Evaluate(schema, mutated).IsValid);

        var missingTrustSentence = ContractJson.Serialize<SyntheticResolveEnvelope>(success)
            .Replace(
                "This text is synthetic and has no legal authority.",
                "Synthetic example only.",
                StringComparison.Ordinal);
        Assert.IsFalse(Evaluate(schema, missingTrustSentence).IsValid);
    }

    [TestMethod]
    public void ArtifactAndControlSchemasAcceptTheirExactTypedDocuments()
    {
        var table = SyntheticSliceSchemaExporter.ExportSchemaTable();
        var control = CreateControl(table);
        var artifact = new SyntheticSliceArtifactManifest(
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaResourceIds.SyntheticSliceArtifact,
            table.Members[0].Sha256,
            "synthetic_preview",
            synthetic: true,
            "synthetic_test",
            new PreviewEnvironment("preview", "s0-05-preview"),
            new PreviewIssuer("preview_attestor", "s0-05-issuer", "s0-05-key"),
            table,
            new SyntheticSliceControlDescriptor(
                V3SchemaIds.SyntheticSliceControl,
                V3SchemaResourceIds.SyntheticSliceControl,
                table.Members[1].Sha256,
                Digest,
                1,
                "application/json"),
            new PreviewAttestation(
                "preview_mechanics_only",
                "ECDSA-P256-SHA256",
                "ieee-p1363",
                new string('A', 86)));

        Assert.IsTrue(Evaluate(
            BuildSchema(V3SchemaIds.SyntheticSliceControl),
            ContractJson.Serialize(control)).IsValid);
        Assert.IsTrue(Evaluate(
            BuildSchema(V3SchemaIds.SyntheticSliceArtifact),
            ContractJson.Serialize(artifact)).IsValid);
    }

    private static JsonSchema BuildSchema(string schemaId)
    {
        var registry = new SchemaRegistry();
        var options = new BuildOptions { SchemaRegistry = registry };
        foreach (var reusedSchemaId in new[]
                 {
                     V3SchemaIds.PreviewOperationCatalog,
                     V3SchemaIds.PreviewRefusalRegistry,
                     V3SchemaIds.PreviewObjectSet,
                 })
        {
            registry.Register(JsonSchema.FromText(
                Encoding.UTF8.GetString(PreviewSchemaExporter.ExportUtf8(reusedSchemaId)),
                options));
        }

        return JsonSchema.FromText(
            Encoding.UTF8.GetString(SyntheticSliceSchemaExporter.ExportUtf8(schemaId)),
            options);
    }

    private static EvaluationResults Evaluate(JsonSchema schema, string json)
    {
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(
            document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    private static SyntheticResolveContext CreateContext() => new(
        "req_0123456789abcdef0123456789abcdef",
        new PreviewOperationReference("resolve", SyntheticSliceOperationCatalog.CatalogId, Digest),
        new PreviewRefusalRegistryReference(
            PreviewRefusalRegistry.StageZero.RegistryId,
            V3SchemaIds.PreviewRefusalRegistry,
            Digest),
        new PreviewSnapshotReference("s0-05-snapshot", Digest),
        new SyntheticSliceArtifactReference(Digest),
        new SyntheticSliceIndexReference(SyntheticSliceIndexStamp.SchemaIdentity, Digest, Digest),
        new ComponentIdentity("s0-05-runtime", Digest),
        new ComponentIdentity("s0-05-builder", Digest));

    private static SyntheticSliceControl CreateControl(SyntheticSliceSchemaTable table) => new(
        V3SchemaIds.SyntheticSliceControl,
        V3SchemaResourceIds.SyntheticSliceControl,
        SyntheticResolveRequestContract.V1,
        SyntheticSliceOperationCatalog.Create(table.Members[2].Sha256),
        PreviewRefusalRegistry.StageZero,
        table.Members[5],
        SyntheticNormalizationProfile.PlainV1,
        SyntheticSliceScope.CompleteLu,
        new PreviewSnapshotReference("s0-05-snapshot", Digest),
        new ComponentIdentity("s0-05-builder", Digest),
        new SyntheticSliceIndexStamp(
            SyntheticSliceIndexStamp.SchemaIdentity,
            Digest,
            "3.50.4",
            "2025-07-30 19:33:53 abcdef",
            Digest,
            Digest,
            SyntheticSliceScope.CompleteLu.Sha256,
            Digest),
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
        });
}
