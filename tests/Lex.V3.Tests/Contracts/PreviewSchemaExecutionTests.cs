using System.Text.Json.Nodes;
using Json.Schema;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
[DoNotParallelize]
public sealed class PreviewSchemaExecutionTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void PayloadCompoundSchemaValidatesASchemaOnlyActiveRefusalWithoutExternalResolution()
    {
        var schema = BuildPayloadSchema(ReadPayloadSchema());
        var result = schema.Evaluate(
            ToElement(CreateActivePayload()),
            EvaluationOptions());

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    [DataRow("operation_catalog")]
    [DataRow("refusal_registry")]
    [DataRow("object_set")]
    [DataRow("envelope")]
    public void RemovingAnyEmbeddedResourceMakesTheGraphUnresolvable(string resourceName)
    {
        var root = ReadPayloadSchema();
        root["$defs"]!.AsObject().Remove(resourceName);
        var originalFetch = SchemaRegistry.Global.Fetch;
        SchemaRegistry.Global.Fetch = static (_, _) => null!;
        try
        {
            Assert.ThrowsExactly<RefResolutionException>(() =>
            {
                var schema = BuildPayloadSchema(root);
                schema.Evaluate(ToElement(CreateActivePayload()), EvaluationOptions());
            });
        }
        finally
        {
            SchemaRegistry.Global.Fetch = originalFetch;
        }
    }

    [TestMethod]
    public void AbsoluteCompoundResourcesIgnoreUnrelatedRetrievalBases()
    {
        var root = ReadPayloadSchema();
        var payload = ToElement(CreateActivePayload());
        foreach (var baseUri in new[]
                 {
                     new Uri("https://one.invalid/unrelated/schema.json"),
                     new Uri("file:///unrelated/two/schema.json"),
                 })
        {
            var schema = BuildPayloadSchema(root, baseUri);
            Assert.IsTrue(schema.Evaluate(payload, EvaluationOptions()).IsValid);
        }
    }

    [TestMethod]
    public void RelativeResourceIdentityOrReferenceCannotResolveUnderAnUnrelatedBase()
    {
        foreach (var mutate in new Action<JsonObject>[]
                 {
                     root => root["properties"]!["operation_catalog"]!["$ref"] =
                         "relative-operation-catalog/1",
                     root => root["$defs"]!["operation_catalog"]!["$id"] =
                         "relative-operation-catalog/1",
                 })
        {
            var root = ReadPayloadSchema();
            mutate(root);
            var originalFetch = SchemaRegistry.Global.Fetch;
            SchemaRegistry.Global.Fetch = static (_, _) => null!;
            try
            {
                Assert.ThrowsExactly<RefResolutionException>(() =>
                {
                    var schema = BuildPayloadSchema(
                        root,
                        new Uri("https://unrelated.invalid/schema.json"));
                    schema.Evaluate(ToElement(CreateActivePayload()), EvaluationOptions());
                });
            }
            finally
            {
                SchemaRegistry.Global.Fetch = originalFetch;
            }
        }
    }

    [TestMethod]
    public void NonEmptyEnvelopeProbeCannotBecomeAStageZeroPayload()
    {
        var root = JsonNode.Parse(ContractJson.Serialize(CreateActivePayload()))!.AsObject();
        root["operation_catalog"] = JsonNode.Parse(
            ContractJson.Serialize(PreviewOperationCatalog.StageZero));
        var schema = BuildPayloadSchema(ReadPayloadSchema());

        Assert.IsTrue(schema.Evaluate(ToElement(root), EvaluationOptions()).IsValid);
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
            ContractJson.Deserialize<PreviewPayload>(root.ToJsonString()));
    }

    [TestMethod]
    public void MutatingAnExecutableConstraintRejectsTheSameTypedPayload()
    {
        var root = ReadPayloadSchema();
        root["properties"]!["schema"]!["const"] = "wrong-schema";
        var schema = BuildPayloadSchema(root);
        var result = schema.Evaluate(
            ToElement(CreateActivePayload()),
            EvaluationOptions());

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void SchemaAndRuntimeRejectTheSameManifestIdentifierOverflow()
    {
        var manifest = new PreviewArtifactManifest(
            V3SchemaIds.PreviewArtifact,
            V3SchemaResourceIds.PreviewArtifact,
            PreviewSchemaExporter.ComputeSha256(
                PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewArtifact)),
            "synthetic_preview",
            synthetic: true,
            "synthetic_test",
            new PreviewEnvironment("preview", "preview-slot"),
            new PreviewIssuer("preview_attestor", "preview-issuer", "preview-key"),
            PreviewSchemaExporter.ExportContractSet(),
            new PreviewPayloadDescriptor(
                V3SchemaIds.PreviewPayload,
                V3SchemaResourceIds.PreviewPayload,
                PreviewSchemaExporter.ComputeSha256(
                    PreviewSchemaExporter.ExportUtf8(V3SchemaIds.PreviewPayload)),
                Digest,
                0,
                "application/json"),
            new PreviewAttestation(
                "preview_mechanics_only",
                "ECDSA-P256-SHA256",
                "ieee-p1363",
                new string('A', 86)));
        var node = JsonNode.Parse(ContractJson.Serialize(manifest))!.AsObject();
        var schema = BuildSchema(V3SchemaIds.PreviewArtifact);

        Assert.IsTrue(schema.Evaluate(ToElement(node), EvaluationOptions()).IsValid);
        node["issuer"]!["issuer_id"] = new string('a', 257);
        Assert.IsFalse(schema.Evaluate(ToElement(node), EvaluationOptions()).IsValid);
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
            ContractJson.Deserialize<PreviewArtifactManifest>(node.ToJsonString()));
    }

    [TestMethod]
    public void SchemaAndRuntimeRejectTheSamePrivacyFieldMutations()
    {
        var schema = BuildPayloadSchema(ReadPayloadSchema());
        foreach (var mutate in new Action<JsonObject>[]
                 {
                     root => root["envelopes"]![0]!["context"]!["request_ref"] =
                         "req_can_i_be_fired_while_sick_0000",
                     root => root["envelopes"]![0]!["refusal"]!["requested_coordinate"] =
                         "can I be fired while sick",
                     root => root["envelopes"]![0]!["refusal"]!["official_search_actions"]![0]!["uri"] =
                         "https://legilux.public.lu/search?q=health",
                     root => root["envelopes"]![0]!["refusal"]!["official_search_actions"]![0]!["kind"] =
                         "other_action",
                     root =>
                     {
                         root["envelopes"]![0]!["refusal"]!["official_search_actions"]![0]!
                             .AsObject()
                             .Remove("kind");
                     },
                 })
        {
            var root = JsonNode.Parse(ContractJson.Serialize(CreateActivePayload()))!.AsObject();
            mutate(root);
            Assert.IsFalse(schema.Evaluate(ToElement(root), EvaluationOptions()).IsValid);
            Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
                ContractJson.Deserialize<PreviewPayload>(root.ToJsonString()));
        }
    }

    [TestMethod]
    public void SchemaAndRuntimeAgreeOnAstralTitleScalarLimitAndForbiddenSeparators()
    {
        var schema = BuildPayloadSchema(ReadPayloadSchema());
        var atLimit = string.Concat(Enumerable.Repeat("😀", 512));
        var root = JsonNode.Parse(ContractJson.Serialize(CreateActivePayload()))!.AsObject();
        var candidate = JsonNode.Parse(ContractJson.Serialize(new HeldRecordCandidate(
            "preview:held:lu-legilux",
            atLimit,
            PublisherId.LuLegilux)));
        root["envelopes"]![0]!["refusal"]!["possible_held_records"] =
            new JsonArray(candidate);

        Assert.IsTrue(schema.Evaluate(ToElement(root), EvaluationOptions()).IsValid);
        _ = ContractJson.Deserialize<PreviewPayload>(root.ToJsonString());

        foreach (var invalid in new[]
                 {
                     atLimit + "😀", " ", "\u2003", "a\u0085b", "a\u2028b", "a\u2029b",
                 })
        {
            root["envelopes"]![0]!["refusal"]!["possible_held_records"]![0]!["title"] = invalid;
            Assert.IsFalse(schema.Evaluate(ToElement(root), EvaluationOptions()).IsValid, invalid);
            Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
                ContractJson.Deserialize<PreviewPayload>(root.ToJsonString()), invalid);
        }
    }

    private static JsonSchema BuildPayloadSchema(JsonObject root, Uri? baseUri = null) =>
        JsonSchema.FromText(
            root.ToJsonString(),
            new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new SchemaRegistry(),
            },
            baseUri);

    private static JsonSchema BuildSchema(string schemaId) => JsonSchema.FromText(
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "schemas",
            "v3-preview",
            PreviewSchemaExporter.FileNameFor(schemaId))),
        new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
        });

    private static EvaluationOptions EvaluationOptions() => new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    private static System.Text.Json.JsonElement ToElement<T>(T value)
    {
        var json = value is JsonNode node ? node.ToJsonString() : ContractJson.Serialize(value);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static PreviewPayload CreateActivePayload()
    {
        var catalog = new PreviewOperationCatalog(
            V3SchemaIds.PreviewOperationCatalog,
            "preview-catalog",
            new[]
            {
                new PreviewOperationDescriptor(
                    "resolve",
                    new ContractReference("preview-request/test", Digest),
                    new ContractReference("preview-success/test", Digest),
                    new[] { RefusalCode.IdentifierUnknown },
                    "identifier_ordinal",
                    "preview_mechanics_only",
                    "rest/preview",
                    "mcp/preview",
                    "html/preview"),
            });
        var registry = PreviewRefusalRegistry.StageZero;
        var objectSet = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "preview-objects",
            Array.Empty<PreviewObject>());
        var context = new PreviewEnvelopeContext(
            "req_0123456789abcdef0123456789abcdef",
            new PreviewOperationReference(
                "resolve",
                catalog.CatalogId,
                PreviewSchemaExporter.ComputeDocumentSha256(catalog)),
            new PreviewRefusalRegistryReference(
                registry.RegistryId,
                V3SchemaIds.PreviewRefusalRegistry,
                PreviewSchemaExporter.ComputeDocumentSha256(registry)),
            new PreviewSnapshotReference("preview-snapshot", Digest),
            new PreviewArtifactReference("preview-artifact"),
            "preview-index/1",
            new ComponentIdentity("preview-runtime", Digest),
            new ComponentIdentity("preview-builder", Digest),
            PreviewCapabilityState.MechanicsOnly,
            new PreviewFreshness(
                DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
                PreviewUpstreamHealth.NotApplicableSynthetic),
            "synthetic-preview-no-jurisdiction",
            PreviewProvisionality.All,
            PreviewSourceContext.SyntheticTest);
        var refusal = IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier });

        return new PreviewPayload(
            V3SchemaIds.PreviewPayload,
            catalog,
            registry,
            objectSet,
            new PreviewEnvelope[] { PreviewRefusalEnvelope.Create(context, refusal) });
    }

    private static JsonObject ReadPayloadSchema() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "schemas",
            "v3-preview",
            PreviewSchemaExporter.FileNameFor(V3SchemaIds.PreviewPayload))))?.AsObject()
        ?? throw new AssertFailedException("The payload schema is not a JSON object.");

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
