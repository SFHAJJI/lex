using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class SourceSchemaTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly string CanonicalKeyDigest = Sha256("cellar:work:example");

    [TestMethod]
    public void ExporterPublishesExactlySixDeterministicDraft202012Schemas()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                SourceCoreSchemaIds.Common,
                SourceCoreSchemaIds.SourceObjectRef,
                SourceCoreSchemaIds.SourceProfileTopology,
                SourceCoreSchemaIds.MachineQueryPlan,
                SourceCoreSchemaIds.MachineQueryRenderReceipt,
                SourceCoreSchemaIds.MachineRequestEvidence,
            },
            SourceCoreSchemaExporter.AllSchemaIds.ToArray());

        var resourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schemaId in SourceCoreSchemaExporter.AllSchemaIds)
        {
            var first = SourceCoreSchemaExporter.ExportUtf8(schemaId);
            var second = SourceCoreSchemaExporter.ExportUtf8(schemaId);
            CollectionAssert.AreEqual(first, second, schemaId);
            Assert.IsGreaterThan(0, first.Length, schemaId);
            Assert.AreNotEqual((byte)0xef, first[0], $"{schemaId} must not carry a BOM.");
            Assert.AreEqual((byte)'\n', first[^1], $"{schemaId} must end in one LF.");
            Assert.IsFalse(first.AsSpan().Contains((byte)'\r'), $"{schemaId} must use LF only.");

            using var document = JsonDocument.Parse(first, StrictDocumentOptions());
            Assert.AreEqual(
                "https://json-schema.org/draft/2020-12/schema",
                document.RootElement.GetProperty("$schema").GetString());
            var resourceId = document.RootElement.GetProperty("$id").GetString();
            Assert.IsNotNull(resourceId);
            Assert.AreEqual(SourceCoreSchemaResourceIds.ForWireSchema(schemaId), resourceId);
            Assert.IsTrue(resourceIds.Add(resourceId), $"Duplicate schema resource ID {resourceId}.");
        }

        Assert.AreEqual("source-common.schema.json", SourceCoreSchemaExporter.FileNameFor(SourceCoreSchemaIds.Common));
        Assert.AreEqual("source-object-ref.schema.json", SourceCoreSchemaExporter.FileNameFor(SourceCoreSchemaIds.SourceObjectRef));
        Assert.AreEqual("source-profile-topology.schema.json", SourceCoreSchemaExporter.FileNameFor(SourceCoreSchemaIds.SourceProfileTopology));
        Assert.AreEqual("machine-query-plan.schema.json", SourceCoreSchemaExporter.FileNameFor(SourceCoreSchemaIds.MachineQueryPlan));
        Assert.AreEqual("machine-query-render-receipt.schema.json", SourceCoreSchemaExporter.FileNameFor(SourceCoreSchemaIds.MachineQueryRenderReceipt));
        Assert.AreEqual("machine-request-evidence.schema.json", SourceCoreSchemaExporter.FileNameFor(SourceCoreSchemaIds.MachineRequestEvidence));
        Assert.ThrowsExactly<ArgumentException>(() => SourceCoreSchemaExporter.FileNameFor("unknown/1"));
        Assert.ThrowsExactly<ArgumentException>(() => SourceCoreSchemaExporter.ExportUtf8("unknown/1"));
    }

    [TestMethod]
    public void CommonSchemaDefinesTheThreeSharedClosedShapes()
    {
        using var document = JsonDocument.Parse(
            SourceCoreSchemaExporter.ExportUtf8(SourceCoreSchemaIds.Common),
            StrictDocumentOptions());
        var definitions = document.RootElement.GetProperty("$defs");

        CollectionAssert.AreEquivalent(
            new[] { "source_artifact_ref", "source_registry_member_ref", "source_object_key_ref" },
            definitions.EnumerateObject().Select(static property => property.Name).ToArray());
    }

    [TestMethod]
    public void ContractJsonDocumentsValidateAgainstTheirGeneratedSchemas()
    {
        var registry = RegistryWithCommon();
        var objectRef = CreateObjectRef();
        var topology = new SourceProfileTopology(
            SourceCoreSchemaIds.SourceProfileTopology,
            Artifact("8f47a9ed-8d4b-450c-b814-42d0398cc8eb"),
            new SourceRegistryMemberRef(
                Artifact("bb86e4c4-775d-45ac-90e8-f0f6b39c47cb"),
                "single_publisher_store"));
        var plan = CreateQueryPlan();
        var planRef = MachineQueryPlanIdentity.Create(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            plan);
        var receipt = CreateRenderReceipt(plan, planRef);
        var receiptRef = MachineQueryRenderReceiptIdentity.Create(
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            receipt);
        var evidence = MachineRequestEvidence.FromReceipt(
            planRef,
            receiptRef,
            receipt,
            Artifact("77777777-7777-4777-8777-777777777777"));

        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.SourceObjectRef, objectRef, registry).IsValid);
        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.SourceProfileTopology, topology, registry).IsValid);
        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.MachineQueryPlan, plan, registry).IsValid);
        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.MachineQueryRenderReceipt, receipt, registry).IsValid);
        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.MachineRequestEvidence, evidence, registry).IsValid);
    }

    [TestMethod]
    public void MachineQuerySchemaRejectsMethodCardinalityAndRendererIdentityDrift()
    {
        var valid = JsonNode.Parse(ContractJson.Serialize(CreateQueryPlan()))!.AsObject();
        foreach (var mutation in new Action<JsonObject>[]
                 {
                     root => root.Remove("renderer_source_ref"),
                     root => root.Remove("expected_request_target_sha256"),
                     root => root["renderer_source_ref"]!["sha256"] = "ABC",
                     root => root["expected_request_target_length"] = 0,
                     root => root["expected_request_target_sha256"] = "ABC",
                     root => root["query_family_ref"]!["member_key"] = "SELECT * WHERE { ?s ?p ?o }",
                     root => root["partition_binding"]!["member_key"] = "cursor=user%20text",
                     root => root["method"] = "HEAD",
                     root => root["response_cardinality"]!["kind"] = "unknown",
                     root => root["response_cardinality"]!["row_limit"] = 0,
                     root => root["response_cardinality"]!["kind"] = "opaque_body",
                     root => root["response_cardinality"]!["row_limit"] = null,
                     root => root["method"] = "GET",
                 })
        {
            var candidate = valid.DeepClone().AsObject();
            mutation(candidate);
            Assert.IsFalse(
                Evaluate(SourceCoreSchemaIds.MachineQueryPlan, candidate, RegistryWithCommon()).IsValid,
                candidate.ToJsonString());
        }
    }

    [TestMethod]
    public void MachineQuerySchemaRejectsRuntimeRejectedBaseTargets()
    {
        var valid = JsonNode.Parse(ContractJson.Serialize(CreateQueryPlan()))!.AsObject();
        foreach (var target in MachineQueryValidationVectors.RuntimeRejectedTargets)
        {
            var candidate = valid.DeepClone().AsObject();
            candidate["target_origin_and_path"] = target;
            Assert.IsFalse(
                Evaluate(SourceCoreSchemaIds.MachineQueryPlan, candidate, RegistryWithCommon()).IsValid,
                target);
        }
    }

    [TestMethod]
    public void MachineQueryReceiptSchemaRejectsMediaTypesTheRuntimeRejects()
    {
        var plan = CreateQueryPlan();
        var planRef = MachineQueryPlanIdentity.Create(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            plan);
        var receipt = CreateRenderReceipt(plan, planRef);
        var valid = JsonNode.Parse(ContractJson.Serialize(receipt))!.AsObject();

        foreach (var mediaType in MachineQueryValidationVectors.RuntimeRejectedMediaTypes)
        {
            var candidate = valid.DeepClone().AsObject();
            candidate["content_type"]!["member_key"] = mediaType;
            Assert.IsFalse(
                Evaluate(
                    SourceCoreSchemaIds.MachineQueryRenderReceipt,
                    candidate,
                    RegistryWithCommon()).IsValid,
                mediaType);
        }
    }

    [TestMethod]
    public void ObjectSchemaRejectsMissingUnknownAndDriftedBoundaryValues()
    {
        var valid = JsonNode.Parse(ContractJson.Serialize(CreateObjectRef()))!.AsObject();
        foreach (var mutation in new Action<JsonObject>[]
                 {
                     root => root.Remove("canonical_key"),
                     root => root["unknown_member"] = true,
                     root => root["authority"] = "eurlex",
                     root => root["schema"] = "lex-v3-source-object-ref/2",
                     root => root["publisher_uri"] = "not-a-uri",
                     root => root["publisher_uri"] = "https://publisher.example/café",
                     root => root["publisher_uri"] = "https://publisher.example/%ZZ",
                     root => root["publisher_uri"] = "https://publisher.example/%",
                     root => root["publisher_uri"] = "https://publisher.example/%2",
                     root => root["canonical_key_sha256"] = "ABC",
                     root => root["identity_profile_ref"]!["resource_id"] = "urn:uuid:not-a-uuid",
                     root => root["identity_profile_ref"]!["sha256"] = Digest + "0",
                 })
        {
            var candidate = valid.DeepClone().AsObject();
            mutation(candidate);
            var registry = RegistryWithCommon();
            Assert.IsFalse(
                Evaluate(SourceCoreSchemaIds.SourceObjectRef, candidate, registry).IsValid,
                candidate.ToJsonString());
        }
    }

    [TestMethod]
    public void CheckedSchemaFilesAreExactExporterBytes()
    {
        foreach (var schemaId in SourceCoreSchemaExporter.AllSchemaIds)
        {
            var path = Path.Combine(
                RepositoryRoot(),
                "schemas",
                "v3-source",
                "core",
                SourceCoreSchemaExporter.FileNameFor(schemaId));
            CollectionAssert.AreEqual(
                SourceCoreSchemaExporter.ExportUtf8(schemaId),
                File.ReadAllBytes(path),
                schemaId);
        }
    }

    private static SourceObjectRef CreateObjectRef()
    {
        var registry = Artifact("9d38da80-ad24-4e93-ad14-0214ca37ac40");
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(registry, "work"),
            "http://publications.europa.eu/resource/cellar/11111111-1111-1111-1111-111111111111",
            "cellar:work:example",
            CanonicalKeyDigest,
            Artifact("8f47a9ed-8d4b-450c-b814-42d0398cc8eb"),
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(registry, "collection"),
                "http://publications.europa.eu/resource/cellar",
                "cellar:collection",
                Sha256("cellar:collection")));
    }

    private static MachineQueryPlan CreateQueryPlan()
    {
        const string body = "query=ASK%20%7B%7D";
        var parameterSet = Artifact("44444444-4444-4444-8444-444444444444");
        return new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            new SourceRegistryMemberRef(
                Artifact("11111111-1111-4111-8111-111111111111"),
                "jolux-resource-page"),
            Artifact("22222222-2222-4222-8222-222222222222"),
            Artifact("33333333-3333-4333-8333-333333333333"),
            HttpRequestMethod.Post,
            "https://data.legilux.public.lu/sparqlendpoint",
            Encoding.ASCII.GetByteCount("/sparqlendpoint"),
            Sha256("/sparqlendpoint"),
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                rowLimit: 500,
                expectedPartitionRowCount: 13_207_454,
                expectedPartitionRowCountEvidenceRef:
                    Artifact("99999999-9999-4999-8999-999999999999")),
            new SourceRegistryMemberRef(
                Artifact("88888888-8888-4888-8888-888888888888"),
                "application/x-www-form-urlencoded"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            parameterSet,
            new SourceRegistryMemberRef(parameterSet, "complete-keyset-page"),
            Encoding.UTF8.GetByteCount(body),
            Sha256(body));
    }

    private static MachineQueryRenderReceipt CreateRenderReceipt(
        MachineQueryPlan plan,
        SourceArtifactRef planRef)
    {
        const string body = "query=ASK%20%7B%7D";
        const string target = "/sparqlendpoint";
        return new MachineQueryRenderReceipt(
            MachineQueryRenderReceipt.SchemaId,
            planRef,
            MachineQueryPlan.SchemaId,
            plan.RendererProfileRef,
            plan.RendererSourceRef,
            plan.OrderedParameterSet,
            plan.ContentType,
            plan.Charset,
            plan.InputMode,
            plan.Method,
            Encoding.ASCII.GetByteCount(target),
            Sha256(target),
            Encoding.UTF8.GetByteCount(body),
            Sha256(body));
    }

    private static SourceArtifactRef Artifact(string id) => new($"urn:uuid:{id}", Digest);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static SchemaRegistry RegistryWithCommon()
    {
        var registry = new SchemaRegistry();
        registry.Register(ParseSchema(SourceCoreSchemaIds.Common, registry));
        return registry;
    }

    private static JsonSchema ParseSchema(string schemaId, SchemaRegistry registry) =>
        JsonSchema.FromText(
            Encoding.UTF8.GetString(SourceCoreSchemaExporter.ExportUtf8(schemaId)),
            new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry });

    private static EvaluationResults Evaluate(
        string schemaId,
        object value,
        SchemaRegistry registry)
    {
        var json = value is JsonNode node ? node.ToJsonString() : ContractJson.Serialize(value);
        using var document = JsonDocument.Parse(json);
        return ParseSchema(schemaId, registry).Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
    }

    private static JsonDocumentOptions StrictDocumentOptions() => new()
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new AssertFailedException("Unable to find the V3 repository root.");
    }
}
