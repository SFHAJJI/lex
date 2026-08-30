using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Artifacts;

[TestClass]
public sealed class ProductionAdmissionTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public async Task PreviewSchemaIsRejectedWithoutOpeningPayload()
    {
        var header = CreateProductionShapedHeader();
        header["schema"] = V3SchemaIds.PreviewArtifact;
        header["synthetic"] = true;
        header["evidence_class"] = "synthetic_preview";
        header["source_kind"] = "synthetic_test";
        var candidate = new SpyArtifactCandidate(ToUtf8(header), "payload"u8.ToArray());

        var result = await CreateAdmission().InspectAsync(candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.PreviewSchemaForbidden, result.Failure.Code);
        Assert.AreEqual(0, candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task EverySyntheticMarkerRejectsIndependentlyBeforePayload()
    {
        var attacks = new (Action<JsonObject> Mutate, ArtifactAdmissionFailureCode Expected)[]
        {
            (root => root["synthetic"] = true, ArtifactAdmissionFailureCode.SyntheticFlagForbidden),
            (root => root["evidence_class"] = "synthetic_preview", ArtifactAdmissionFailureCode.SyntheticEvidenceForbidden),
            (root => root["source_kind"] = "synthetic_test", ArtifactAdmissionFailureCode.SyntheticSourceForbidden),
            (root => root["environment"]!["class"] = "preview", ArtifactAdmissionFailureCode.EnvironmentForbidden),
            (root => root["environment"]!["binding"] = "/subscriptions/test/preview", ArtifactAdmissionFailureCode.EnvironmentForbidden),
            (root => root["issuer"]!["role"] = "preview_attestor", ArtifactAdmissionFailureCode.IssuerRoleForbidden),
            (root => root["issuer"]!["role"] = "migration_inventory", ArtifactAdmissionFailureCode.IssuerRoleForbidden),
        };

        foreach (var attack in attacks)
        {
            var header = CreateProductionShapedHeader();
            attack.Mutate(header);
            var candidate = new SpyArtifactCandidate(ToUtf8(header), "payload"u8.ToArray());

            var result = await CreateAdmission().InspectAsync(candidate, CancellationToken.None);

            Assert.AreEqual(attack.Expected, result.Failure.Code);
            Assert.AreEqual(0, candidate.PayloadOpenCount);
        }
    }

    [TestMethod]
    public async Task StageZeroHasNoAcceptedReleaseSchema()
    {
        var candidate = new SpyArtifactCandidate(
            ToUtf8(CreateProductionShapedHeader()),
            "payload"u8.ToArray());

        var result = await CreateAdmission().InspectAsync(candidate, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.ReleaseSchemaUnsupported, result.Failure.Code);
        Assert.AreEqual(0, candidate.PayloadOpenCount);
    }

    [TestMethod]
    public async Task DuplicateAndUnknownMembersFailBeforeMarkerClassification()
    {
        var ordinary = Encoding.UTF8.GetString(ToUtf8(CreateProductionShapedHeader()));
        var duplicateFirst = ordinary.Replace(
            "\"schema\":",
            "\"schema\":\"first\",\"schema\":",
            StringComparison.Ordinal);
        var duplicateLast = ordinary.Replace(
            "\"evidence_class\":",
            "\"schema\":\"last\",\"evidence_class\":",
            StringComparison.Ordinal);
        var escapedDuplicateFirst = ordinary.Replace(
            "\"schema\":",
            "\"\\u0073chema\":\"first\",\"schema\":",
            StringComparison.Ordinal);
        var escapedDuplicateLast = ordinary.Replace(
            "\"evidence_class\":",
            "\"\\u0073chema\":\"last\",\"evidence_class\":",
            StringComparison.Ordinal);

        foreach (var json in new[]
                 {
                     duplicateFirst,
                     duplicateLast,
                     escapedDuplicateFirst,
                     escapedDuplicateLast,
                 })
        {
            var candidate = new SpyArtifactCandidate(Encoding.UTF8.GetBytes(json), Array.Empty<byte>());
            var result = await CreateAdmission().InspectAsync(candidate, CancellationToken.None);
            Assert.AreEqual(ArtifactAdmissionFailureCode.DuplicateMember, result.Failure.Code);
            Assert.AreEqual(0, candidate.PayloadOpenCount);
        }

        var unknownLast = ordinary[..^1] + ",\"public_key\":\"attacker supplied\"}";
        var unknownFirst = "{\"public_key\":\"attacker supplied\"," + ordinary[1..];
        foreach (var json in new[] { unknownFirst, unknownLast })
        {
            var candidate = new SpyArtifactCandidate(Encoding.UTF8.GetBytes(json), Array.Empty<byte>());
            var result = await CreateAdmission().InspectAsync(candidate, CancellationToken.None);
            Assert.AreEqual(ArtifactAdmissionFailureCode.UnknownMember, result.Failure.Code);
            Assert.AreEqual(0, candidate.PayloadOpenCount);
        }
    }

    [TestMethod]
    public async Task EveryNonProductionGraphIdentityFailsBeforePayloadReaderConstruction()
    {
        foreach (var graphSchema in new[]
                 {
                     V3SchemaIds.PreviewPayload,
                     "lex-v3-migration-payload/1",
                     "lex-corpus/5",
                     "unknown-graph/1",
                 })
        {
            var header = CreateProductionShapedHeader();
            header["payload"]!["schema"] = graphSchema;
            var candidate = new SpyArtifactCandidate(ToUtf8(header), "not-json"u8.ToArray());

            var result = await CreateAdmission().InspectAsync(candidate, CancellationToken.None);

            Assert.AreEqual(ArtifactAdmissionFailureCode.ReleaseSchemaUnsupported, result.Failure.Code);
            Assert.AreEqual(0, candidate.PayloadOpenCount, graphSchema);
        }
    }

    [TestMethod]
    public async Task ManifestByteBoundFailsAtBoundaryPlusOne()
    {
        var atBoundary = new SpyArtifactCandidate(
            new byte[PreviewContractLimits.MaximumManifestBytes],
            Array.Empty<byte>());
        var overBoundary = new SpyArtifactCandidate(
            new byte[PreviewContractLimits.MaximumManifestBytes + 1],
            Array.Empty<byte>());

        var atResult = await CreateAdmission().InspectAsync(atBoundary, CancellationToken.None);
        var overResult = await CreateAdmission().InspectAsync(overBoundary, CancellationToken.None);

        Assert.AreEqual(ArtifactAdmissionFailureCode.MalformedHeader, atResult.Failure.Code);
        Assert.AreEqual(ArtifactAdmissionFailureCode.HeaderTooLarge, overResult.Failure.Code);
        Assert.AreEqual(0, atBoundary.PayloadOpenCount);
        Assert.AreEqual(0, overBoundary.PayloadOpenCount);
    }

    private static ProductionArtifactAdmission CreateAdmission() =>
        ProductionArtifactAdmission.CreateStageZero("/subscriptions/test/production");

    private static JsonObject CreateProductionShapedHeader() => new()
    {
        ["schema"] = "lex-v3-release-artifact/test-only",
        ["schema_resource"] = "urn:uuid:00000000-0000-0000-0000-000000000001",
        ["schema_sha256"] = Digest,
        ["evidence_class"] = "official_release",
        ["synthetic"] = false,
        ["source_kind"] = "official_publisher",
        ["environment"] = new JsonObject
        {
            ["class"] = "production",
            ["binding"] = "/subscriptions/test/production",
        },
        ["issuer"] = new JsonObject
        {
            ["role"] = "release_attestor",
            ["issuer_id"] = "release-issuer",
            ["key_id"] = "release-key",
        },
        ["contract_set"] = new JsonObject
        {
            ["envelope"] = Contract(V3SchemaIds.PreviewEnvelope),
            ["object_set"] = Contract(V3SchemaIds.PreviewObjectSet),
            ["operation_catalog"] = Contract(V3SchemaIds.PreviewOperationCatalog),
            ["refusal_registry"] = Contract(V3SchemaIds.PreviewRefusalRegistry),
        },
        ["payload"] = new JsonObject
        {
            ["schema"] = "lex-v3-release-payload/test-only",
            ["schema_resource"] = "urn:uuid:00000000-0000-0000-0000-000000000002",
            ["schema_sha256"] = Digest,
            ["sha256"] = Digest,
            ["bytes"] = 0,
            ["media_type"] = "application/json",
        },
        ["attestation"] = new JsonObject
        {
            ["purpose"] = "release",
            ["algorithm"] = "ECDSA-P256-SHA256",
            ["signature_format"] = "ieee-p1363",
            ["signature"] = new string('A', 86),
        },
    };

    private static JsonObject Contract(string schema) => new()
    {
        ["schema"] = schema,
        ["schema_resource"] = V3SchemaResourceIds.ForWireSchema(schema),
        ["sha256"] = Digest,
    };

    private static byte[] ToUtf8(JsonObject value) =>
        Encoding.UTF8.GetBytes(value.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

    private sealed class SpyArtifactCandidate(byte[] manifest, byte[] payload) : IArtifactCandidate
    {
        public int PayloadOpenCount { get; private set; }

        public ValueTask<Stream> OpenAdmissionManifestAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<Stream>(new MemoryStream(manifest, writable: false));

        public ValueTask<Stream> OpenPayloadAsync(CancellationToken cancellationToken)
        {
            PayloadOpenCount++;
            return ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false));
        }
    }
}
