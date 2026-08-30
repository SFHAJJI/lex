using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class PreviewSchemaInvariantTests
{
    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";
    private const string Sha256Pattern = "^[0-9a-f]{64}$";

    [TestMethod]
    public void ArtifactSchemaPinsEverySyntheticMarkerAndBound()
    {
        var artifact = ReadSchema(V3SchemaIds.PreviewArtifact);

        AssertRoot(artifact, V3SchemaIds.PreviewArtifact);
        AssertConst(Property(artifact, "schema"), V3SchemaIds.PreviewArtifact);
        AssertConst(Property(artifact, "schema_resource"), V3SchemaResourceIds.PreviewArtifact);
        AssertSha256(Property(artifact, "schema_sha256"));
        AssertConst(Property(artifact, "evidence_class"), "synthetic_preview");
        AssertConst(Property(artifact, "synthetic"), true);
        AssertConst(Property(artifact, "source_kind"), "synthetic_test");
        AssertConst(Property(Property(artifact, "environment"), "class"), "preview");
        AssertConst(Property(Property(artifact, "issuer"), "role"), "preview_attestor");

        var contractSet = Property(artifact, "contract_set");
        AssertContractReference(Property(contractSet, "envelope"), V3SchemaIds.PreviewEnvelope);
        AssertContractReference(Property(contractSet, "object_set"), V3SchemaIds.PreviewObjectSet);
        AssertContractReference(
            Property(contractSet, "operation_catalog"),
            V3SchemaIds.PreviewOperationCatalog);
        AssertContractReference(
            Property(contractSet, "refusal_registry"),
            V3SchemaIds.PreviewRefusalRegistry);

        var payload = Property(artifact, "payload");
        AssertConst(Property(payload, "schema"), V3SchemaIds.PreviewPayload);
        AssertConst(Property(payload, "schema_resource"), V3SchemaResourceIds.PreviewPayload);
        AssertSha256(Property(payload, "schema_sha256"));
        AssertSha256(Property(payload, "sha256"));
        AssertConst(Property(payload, "media_type"), "application/json");

        var bytes = Property(payload, "bytes");
        Assert.AreEqual("integer", StringKeyword(bytes, "type"));
        Assert.AreEqual(0, IntKeyword(bytes, "minimum"));
        Assert.AreEqual(PreviewContractLimits.MaximumPayloadBytes, IntKeyword(bytes, "maximum"));

        var attestation = Property(artifact, "attestation");
        AssertConst(Property(attestation, "purpose"), "preview_mechanics_only");
        AssertConst(Property(attestation, "algorithm"), "ECDSA-P256-SHA256");
        AssertConst(Property(attestation, "signature_format"), "ieee-p1363");

        var signature = Property(attestation, "signature");
        Assert.AreEqual(86, IntKeyword(signature, "minLength"));
        Assert.AreEqual(86, IntKeyword(signature, "maxLength"));
        Assert.AreEqual("^[A-Za-z0-9_-]{86}$", StringKeyword(signature, "pattern"));
    }

    [TestMethod]
    public void StandaloneSchemasPinTheirExactRootIdentities()
    {
        foreach (var schemaId in new[]
                 {
                     V3SchemaIds.PreviewPayload,
                     V3SchemaIds.PreviewObjectSet,
                     V3SchemaIds.PreviewOperationCatalog,
                     V3SchemaIds.PreviewRefusalRegistry,
                 })
        {
            var root = ReadSchema(schemaId);
            AssertRoot(root, schemaId);
            AssertConst(Property(root, "schema"), schemaId);
        }

        var envelope = ReadSchema(V3SchemaIds.PreviewEnvelope);
        AssertRoot(envelope, V3SchemaIds.PreviewEnvelope);
        foreach (var branch in Array(envelope, "anyOf").Select(static node => node!.AsObject()))
        {
            AssertConst(Property(branch, "schema"), V3SchemaIds.PreviewEnvelope);
        }
    }

    [TestMethod]
    public void PayloadSchemaPinsAbsenceAndCollectionBounds()
    {
        var payload = ReadSchema(V3SchemaIds.PreviewPayload);
        Assert.AreEqual(
            PreviewContractLimits.MaximumEnvelopes,
            IntKeyword(Property(payload, "envelopes"), "maxItems"));

        var standaloneRefusal = EnvelopeBranch(ReadSchema(V3SchemaIds.PreviewEnvelope), "refusal");
        AssertConst(
            Property(Property(standaloneRefusal, "refusal"), "asserts_absence_of_law"),
            false);

        var standaloneObjectSet = ReadSchema(V3SchemaIds.PreviewObjectSet);
        Assert.AreEqual(
            PreviewContractLimits.MaximumObjects,
            IntKeyword(Property(standaloneObjectSet, "objects"), "maxItems"));
        AssertHeldPublicBodyConditional(standaloneObjectSet);
    }

    [TestMethod]
    public void ExportedContractSetHashesTheExactCheckedSchemas()
    {
        var contractSet = PreviewSchemaExporter.ExportContractSet();
        var artifactSchemaBytes = PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewArtifact);
        var payloadSchemaBytes = PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewPayload);
        var manifest = new PreviewArtifactManifest(
            V3SchemaIds.PreviewArtifact,
            V3SchemaResourceIds.PreviewArtifact,
            PreviewSchemaExporter.ComputeSha256(artifactSchemaBytes),
            "synthetic_preview",
            true,
            "synthetic_test",
            new PreviewEnvironment("preview", "synthetic-preview"),
            new PreviewIssuer("preview_attestor", "preview-issuer", "preview-key"),
            contractSet,
            new PreviewPayloadDescriptor(
                V3SchemaIds.PreviewPayload,
                V3SchemaResourceIds.PreviewPayload,
                PreviewSchemaExporter.ComputeSha256(payloadSchemaBytes),
                PreviewSchemaExporter.ComputeSha256(System.Array.Empty<byte>()),
                0,
                "application/json"),
            new PreviewAttestation(
                "preview_mechanics_only",
                "ECDSA-P256-SHA256",
                "ieee-p1363",
                new string('A', 86)));
        var boundHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [V3SchemaIds.PreviewArtifact] = manifest.SchemaSha256,
            [V3SchemaIds.PreviewPayload] = manifest.Payload.SchemaSha256,
            [V3SchemaIds.PreviewEnvelope] = manifest.ContractSet.Envelope.Sha256,
            [V3SchemaIds.PreviewObjectSet] = manifest.ContractSet.ObjectSet.Sha256,
            [V3SchemaIds.PreviewOperationCatalog] = manifest.ContractSet.OperationCatalog.Sha256,
            [V3SchemaIds.PreviewRefusalRegistry] = manifest.ContractSet.RefusalRegistry.Sha256,
        };

        foreach (var schemaId in PreviewSchemaGraph.SchemaIds)
        {
            var checkedBytes = File.ReadAllBytes(SchemaPath(schemaId));
            var checkedDigest = PreviewSchemaExporter.ComputeSha256(checkedBytes);
            Assert.AreEqual(
                boundHashes[schemaId],
                checkedDigest,
                $"The manifest must bind the checked {schemaId} bytes.");
            CollectionAssert.AreEqual(
                checkedBytes,
                PreviewSchemaExporter.ExportUtf8(schemaId),
                $"The checked {schemaId} schema must be the exact reproducible export.");

            Assert.IsGreaterThan(0, checkedBytes.Length);
            var mutated = checkedBytes.ToArray();
            mutated[mutated.Length / 2] ^= 0x01;
            var mutatedDigest = PreviewSchemaExporter.ComputeSha256(mutated);
            Assert.AreNotEqual(
                checkedDigest,
                mutatedDigest,
                $"Changing one {schemaId} byte must change its digest.");
            Assert.AreNotEqual(
                boundHashes[schemaId],
                mutatedDigest,
                $"The manifest must not bind the mutated {schemaId} bytes.");
        }
    }

    [TestMethod]
    public void PayloadBundlesExactAuthoritativeSchemasAsClosedResources()
    {
        var payload = ReadSchema(V3SchemaIds.PreviewPayload);
        var definitions = payload["$defs"]?.AsObject()
            ?? throw new AssertFailedException("The payload compound schema has no embedded resources.");
        var references = new (string Key, string SchemaId, JsonObject Schema)[]
        {
            ("operation_catalog", V3SchemaIds.PreviewOperationCatalog, Property(payload, "operation_catalog")),
            ("refusal_registry", V3SchemaIds.PreviewRefusalRegistry, Property(payload, "refusal_registry")),
            ("object_set", V3SchemaIds.PreviewObjectSet, Property(payload, "object_set")),
            ("envelope", V3SchemaIds.PreviewEnvelope, Property(payload, "envelopes")["items"]!.AsObject()),
        };

        CollectionAssert.AreEqual(
            references.Select(static item => item.Key).ToArray(),
            definitions.Select(static item => item.Key).ToArray());
        foreach (var (key, schemaId, reference) in references)
        {
            Assert.AreEqual(
                V3SchemaResourceIds.ForWireSchema(schemaId),
                StringKeyword(reference, "$ref"));
            Assert.AreEqual(
                1,
                reference.Count,
                $"The payload property must resolve {schemaId} through the embedded resource.");

            var generated = JsonNode.Parse(PreviewSchemaExporter.ExportUtf8(schemaId));
            Assert.IsTrue(
                JsonNode.DeepEquals(generated, definitions[key]),
                $"The embedded {schemaId} resource must equal its exact standalone export.");
        }
    }

    private static void AssertHeldPublicBodyConditional(JsonObject objectSet)
    {
        var objectBranch = Array(Property(objectSet, "objects")["items"]!.AsObject(), "anyOf")[0]!.AsObject();
        var conditionals = Array(objectBranch, "allOf");
        Assert.HasCount(3, conditionals);

        var heldPublic = conditionals[0]!.AsObject();
        var condition = Property(heldPublic["if"]!.AsObject(), "body_holding_state");
        AssertConst(condition, "held_public");

        var whenHeld = heldPublic["then"]!.AsObject();
        Assert.AreEqual(1, IntKeyword(Property(whenHeld, "body"), "minLength"));
        AssertSha256(Property(whenHeld, "body_sha256"));
        AssertConst(Property(whenHeld, "body_holding_disposition"), "synthetic_fixture");

        var withheld = conditionals[1]!.AsObject()["then"]!.AsObject();
        AssertNullConst(Property(withheld, "body"));
        AssertSha256(Property(withheld, "body_sha256"));
        AssertConst(Property(withheld, "body_holding_disposition"), "synthetic_fixture_withheld");

        var notHeld = conditionals[2]!.AsObject()["then"]!.AsObject();
        AssertNullConst(Property(notHeld, "body"));
        AssertNullConst(Property(notHeld, "body_sha256"));
        AssertConst(Property(notHeld, "body_holding_disposition"), "unknown_pending_evidence");
    }

    private static JsonObject EnvelopeBranch(JsonObject envelope, string branchName) =>
        Array(envelope, "anyOf")
            .Select(static node => node!.AsObject())
            .Single(branch =>
                string.Equals(
                    StringKeyword(Property(branch, "branch"), "const"),
                    branchName,
                    StringComparison.Ordinal));

    private static void AssertContractReference(JsonObject reference, string schemaId)
    {
        AssertConst(Property(reference, "schema"), schemaId);
        AssertConst(
            Property(reference, "schema_resource"),
            V3SchemaResourceIds.ForWireSchema(schemaId));
        AssertSha256(Property(reference, "sha256"));
    }

    private static void AssertSha256(JsonObject schema)
    {
        Assert.AreEqual(64, IntKeyword(schema, "minLength"));
        Assert.AreEqual(64, IntKeyword(schema, "maxLength"));
        Assert.AreEqual(Sha256Pattern, StringKeyword(schema, "pattern"));
    }

    private static void AssertRoot(JsonObject schema, string schemaId)
    {
        Assert.AreEqual(
            V3SchemaResourceIds.ForWireSchema(schemaId),
            StringKeyword(schema, "$id"));
        Assert.AreEqual(Draft202012, StringKeyword(schema, "$schema"));
    }

    private static void AssertConst(JsonObject schema, string expected) =>
        Assert.AreEqual(expected, StringKeyword(schema, "const"));

    private static void AssertConst(JsonObject schema, bool expected) =>
        Assert.AreEqual(expected, schema["const"]!.GetValue<bool>());

    private static void AssertNullConst(JsonObject schema)
    {
        Assert.IsTrue(schema.ContainsKey("const"));
        Assert.IsNull(schema["const"]);
    }

    private static JsonObject Property(JsonObject schema, string propertyName) =>
        schema["properties"]?.AsObject()[propertyName]?.AsObject()
        ?? throw new AssertFailedException($"Schema property '{propertyName}' is missing.");

    private static JsonArray Array(JsonObject schema, string propertyName) =>
        schema[propertyName]?.AsArray()
        ?? throw new AssertFailedException($"Schema array '{propertyName}' is missing.");

    private static int IntKeyword(JsonObject schema, string keyword) =>
        schema[keyword]?.GetValue<int>()
        ?? throw new AssertFailedException($"Schema integer keyword '{keyword}' is missing.");

    private static string StringKeyword(JsonObject schema, string keyword) =>
        schema[keyword]?.GetValue<string>()
        ?? throw new AssertFailedException($"Schema string keyword '{keyword}' is missing.");

    private static JsonObject ReadSchema(string schemaId) =>
        JsonNode.Parse(File.ReadAllText(SchemaPath(schemaId)))?.AsObject()
        ?? throw new AssertFailedException($"Schema '{schemaId}' is not a JSON object.");

    private static string SchemaPath(string schemaId) =>
        Path.Combine(RepositoryRoot(), "schemas", "v3-preview", PreviewSchemaExporter.FileNameFor(schemaId));

    private static string RepositoryRoot()
    {
        foreach (var startingPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "schemas", "v3-preview")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new AssertFailedException("Could not locate the V3 repository root.");
    }
}
