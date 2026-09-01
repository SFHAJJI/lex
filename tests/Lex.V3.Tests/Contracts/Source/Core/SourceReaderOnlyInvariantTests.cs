using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class SourceReaderOnlyInvariantTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void ValidTypedFixturesPassRuntimeSchemasAndReaderOnlyRegistry()
    {
        var objectRef = ValidObject();
        var topology = ValidTopology();
        var objectNode = Node(objectRef);

        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.SourceObjectRef, objectNode).IsValid);
        Assert.IsTrue(Evaluate(SourceCoreSchemaIds.SourceProfileTopology, Node(topology)).IsValid);
        _ = ContractJson.Deserialize<SourceObjectRef>(objectNode.ToJsonString());
        _ = ContractJson.Deserialize<SourceProfileTopology>(Node(topology).ToJsonString());
    }

    [TestMethod]
    public void FourSchemaValidRuntimeInvalidCasesExactlyMatchTheRegistry()
    {
        var cases = new Dictionary<SourceObjectRefReaderOnlyInvariant, Action<JsonObject>>
        {
            [SourceObjectRefReaderOnlyInvariant.CanonicalKeySha256ExactBytes] =
                root => root["canonical_key_sha256"] = Digest,
            [SourceObjectRefReaderOnlyInvariant.CanonicalKeyUtf8Maximum4096Bytes] = root =>
            {
                var key = string.Concat(Enumerable.Repeat("😀", 2049));
                root["canonical_key"] = key;
                root["canonical_key_sha256"] = Sha256(key);
            },
            [SourceObjectRefReaderOnlyInvariant.ParentRegistryMatchesChild] = root =>
                root["parent_key_ref"]!["entity_kind"]!["registry_ref"]!["resource_id"] =
                    "urn:uuid:e3f64a03-f797-4a4d-af4a-45fc86c831ce",
            [SourceObjectRefReaderOnlyInvariant.ParentIsNotSelf] = root =>
                root["parent_key_ref"] = new JsonObject
                {
                    ["entity_kind"] = root["entity_kind"]!.DeepClone(),
                    ["publisher_uri"] = root["publisher_uri"]!.DeepClone(),
                    ["canonical_key"] = root["canonical_key"]!.DeepClone(),
                    ["canonical_key_sha256"] = root["canonical_key_sha256"]!.DeepClone(),
                },
        };

        CollectionAssert.AreEquivalent(
            SourceObjectRefReaderOnlyInvariants.All.ToArray(),
            cases.Keys.ToArray());
        foreach (var (expected, mutate) in cases)
        {
            var candidate = Node(ValidObject());
            mutate(candidate);
            Assert.IsTrue(Evaluate(SourceCoreSchemaIds.SourceObjectRef, candidate).IsValid, expected.ToString());
            Assert.ThrowsExactly<JsonException>(() =>
                ContractJson.Deserialize<SourceObjectRef>(candidate.ToJsonString()), expected.ToString());
        }
    }

    [TestMethod]
    public void TopologyAndNonrecursiveParentAttacksFailSchemaAndRuntime()
    {
        var cases = new Dictionary<string, (string SchemaId, JsonObject Value, Action<JsonObject> Mutate)>
        {
            ["topology_wrong_discriminator"] = (SourceCoreSchemaIds.SourceProfileTopology, Node(ValidTopology()),
                root => root["schema"] = "lex-v3-source-profile-topology/2"),
            ["topology_missing_member"] = (SourceCoreSchemaIds.SourceProfileTopology, Node(ValidTopology()),
                root => root.Remove("topology")),
            ["topology_extra_member"] = (SourceCoreSchemaIds.SourceProfileTopology, Node(ValidTopology()),
                root => root["unexpected"] = true),
            ["topology_invalid_member"] = (SourceCoreSchemaIds.SourceProfileTopology, Node(ValidTopology()),
                root => root["topology"]!["member_key"] = "not valid"),
            ["nested_parent_key_ref"] = (SourceCoreSchemaIds.SourceObjectRef, Node(ValidObject()),
                root => root["parent_key_ref"]!["parent_key_ref"] = root["parent_key_ref"]!.DeepClone()),
        };

        foreach (var (name, item) in cases)
        {
            item.Mutate(item.Value);
            Assert.IsFalse(Evaluate(item.SchemaId, item.Value).IsValid, name);
            if (item.SchemaId == SourceCoreSchemaIds.SourceObjectRef)
            {
                Assert.ThrowsExactly<JsonException>(() =>
                    ContractJson.Deserialize<SourceObjectRef>(item.Value.ToJsonString()), name);
            }
            else
            {
                Assert.ThrowsExactly<JsonException>(() =>
                    ContractJson.Deserialize<SourceProfileTopology>(item.Value.ToJsonString()), name);
            }
        }
    }

    private static SourceObjectRef ValidObject()
    {
        var registry = Artifact("9d38da80-ad24-4e93-ad14-0214ca37ac40");
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(registry, "work"),
            "http://publications.europa.eu/resource/cellar/11111111-1111-1111-1111-111111111111",
            "cellar:work:example",
            Sha256("cellar:work:example"),
            Artifact("8f47a9ed-8d4b-450c-b814-42d0398cc8eb"),
            new SourceObjectKeyRef(
                new SourceRegistryMemberRef(registry, "collection"),
                "http://publications.europa.eu/resource/cellar",
                "cellar:collection",
                Sha256("cellar:collection")));
    }

    private static SourceProfileTopology ValidTopology() => new(
        SourceCoreSchemaIds.SourceProfileTopology,
        Artifact("8f47a9ed-8d4b-450c-b814-42d0398cc8eb"),
        new SourceRegistryMemberRef(
            Artifact("bb86e4c4-775d-45ac-90e8-f0f6b39c47cb"),
            "single_publisher_store"));

    private static SourceArtifactRef Artifact(string id) => new($"urn:uuid:{id}", Digest);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static JsonObject Node<T>(T value) =>
        JsonNode.Parse(ContractJson.Serialize(value))!.AsObject();

    private static EvaluationResults Evaluate(string schemaId, JsonObject node)
    {
        var registry = new SchemaRegistry();
        var options = new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        registry.Register(JsonSchema.FromText(
            Encoding.UTF8.GetString(SourceCoreSchemaExporter.ExportUtf8(SourceCoreSchemaIds.Common)),
            options));
        var schema = JsonSchema.FromText(
            Encoding.UTF8.GetString(SourceCoreSchemaExporter.ExportUtf8(schemaId)),
            options);
        using var document = JsonDocument.Parse(node.ToJsonString());
        return schema.Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
    }
}
