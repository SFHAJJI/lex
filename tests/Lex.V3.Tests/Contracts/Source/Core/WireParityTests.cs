using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class WireParityTests
{
    private static readonly string[] ObjectMembers =
    {
        "schema",
        "authority",
        "entity_kind",
        "publisher_uri",
        "canonical_key",
        "canonical_key_sha256",
        "identity_profile_ref",
        "parent_key_ref",
    };

    [TestMethod]
    public void SchemaAndResourceIdentitiesAreExactClosedAndLowercase()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "lex-v3-source-common/1",
                "lex-v3-source-object-ref/1",
                "lex-v3-source-profile-topology/1",
            },
            new[]
            {
                SourceCoreSchemaIds.Common,
                SourceCoreSchemaIds.SourceObjectRef,
                SourceCoreSchemaIds.SourceProfileTopology,
            });

        var resources = new[]
        {
            SourceCoreSchemaResourceIds.Common,
            SourceCoreSchemaResourceIds.SourceObjectRef,
            SourceCoreSchemaResourceIds.SourceProfileTopology,
        };
        Assert.HasCount(3, resources.Distinct(StringComparer.Ordinal));
        foreach (var resource in resources)
        {
            Assert.AreEqual(resource.ToLowerInvariant(), resource);
            StringAssert.StartsWith(resource, "urn:uuid:");
            Assert.IsTrue(Guid.TryParseExact(resource[9..], "D", out _));
        }
    }

    [TestMethod]
    public void AuthorityWireVocabularyIsExact()
    {
        AssertWireValue(SourceAuthority.Jolux, "jolux");
        AssertWireValue(SourceAuthority.Cellar, "cellar");
        foreach (var invalid in new[] { "JOLUX", "Cellar", "unknown", "jolux ", "0" })
        {
            var json = invalid == "0" ? invalid : JsonValue.Create(invalid)!.ToJsonString();
            Assert.ThrowsExactly<JsonException>(() =>
                ContractJson.Deserialize<SourceAuthority>(json));
        }

        Assert.ThrowsExactly<JsonException>(() => ContractJson.Serialize((SourceAuthority)999));
    }

    [TestMethod]
    public void SourceObjectWireShapeIsStrictSnakeCaseAndLossless()
    {
        var source = ObjectRef(withParent: true);
        var json = ContractJson.Serialize(source);
        var node = JsonNode.Parse(json)!.AsObject();

        CollectionAssert.AreEquivalent(
            ObjectMembers,
            node.Select(static property => property.Key).ToArray());
        Assert.AreEqual("jolux", node["authority"]!.GetValue<string>());
        Assert.AreEqual("law", node["entity_kind"]!["member_key"]!.GetValue<string>());
        Assert.AreEqual(
            "work",
            node["parent_key_ref"]!["entity_kind"]!["member_key"]!.GetValue<string>());

        var roundTrip = ContractJson.Deserialize<SourceObjectRef>(json);
        Assert.AreEqual(source, roundTrip);
        Assert.AreEqual(source.ParentKeyRef, roundTrip.ParentKeyRef);
    }

    [TestMethod]
    [DynamicData(nameof(ObjectMemberCases))]
    public void SourceObjectRejectsEveryMissingMember(string member)
    {
        var original = JsonNode.Parse(ContractJson.Serialize(ObjectRef(withParent: false)))!
            .AsObject();
        var missing = JsonNode.Parse(original.ToJsonString())!.AsObject();
        missing.Remove(member);
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(missing.ToJsonString()));
    }

    [TestMethod]
    [DynamicData(nameof(RequiredObjectMemberCases))]
    public void SourceObjectRejectsEveryNullRequiredMember(string member)
    {
        var nullMember = JsonNode.Parse(ContractJson.Serialize(ObjectRef(withParent: false)))!
            .AsObject();
        nullMember[member] = null;
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(nullMember.ToJsonString()));
    }

    [TestMethod]
    public void SourceObjectRejectsUnknownDuplicateAndCaseDriftedProperties()
    {
        var json = ContractJson.Serialize(ObjectRef(withParent: false));
        var unknown = JsonNode.Parse(json)!.AsObject();
        unknown["legacy_id"] = "v2";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(unknown.ToJsonString()));

        var duplicate = json.Insert(json.Length - 1, ",\"authority\":\"jolux\"");
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(duplicate));

        var caseDrift = JsonNode.Parse(json)!.AsObject();
        caseDrift["publisherUri"] = caseDrift["publisher_uri"]!.DeepClone();
        caseDrift.Remove("publisher_uri");
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(caseDrift.ToJsonString()));
    }

    [TestMethod]
    public void OptionalParentMustBeExplicitlyPresentAndMayBeNull()
    {
        var node = JsonNode.Parse(ContractJson.Serialize(ObjectRef(withParent: false)))!
            .AsObject();
        Assert.IsNull(node["parent_key_ref"]);
        Assert.IsNull(ContractJson.Deserialize<SourceObjectRef>(node.ToJsonString()).ParentKeyRef);

        node.Remove("parent_key_ref");
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(node.ToJsonString()));
    }

    [TestMethod]
    [DataRow("publisher_uri", "relative/path")]
    [DataRow("publisher_uri", "https://publisher.example/item?query=forbidden")]
    [DataRow("canonical_key_sha256", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [DataRow("canonical_key_sha256", "not-a-sha256")]
    [DataRow("schema", "lex-v3-source-object-ref/2")]
    public void SourceObjectJsonCannotBypassTypedIdentityInvariants(
        string member,
        string invalidValue)
    {
        var node = JsonNode.Parse(ContractJson.Serialize(ObjectRef(withParent: false)))!
            .AsObject();
        node[member] = invalidValue;

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceObjectRef>(node.ToJsonString()));
    }

    [TestMethod]
    public void SinglePublisherStoreTopologyRoundTripsWithoutVocabularyLoss()
    {
        var identityProfile = ArtifactRef('a', '1');
        var topologyRegistry = ArtifactRef('c', '3');
        var topology = new SourceProfileTopology(
            SourceCoreSchemaIds.SourceProfileTopology,
            identityProfile,
            new SourceRegistryMemberRef(topologyRegistry, "single_publisher_store"));

        var json = ContractJson.Serialize(topology);
        var node = JsonNode.Parse(json)!.AsObject();
        CollectionAssert.AreEquivalent(
            new[] { "schema", "identity_profile_ref", "topology" },
            node.Select(static property => property.Key).ToArray());
        Assert.AreEqual(
            "single_publisher_store",
            node["topology"]!["member_key"]!.GetValue<string>());
        Assert.AreEqual(topology, ContractJson.Deserialize<SourceProfileTopology>(json));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("identity_profile_ref")]
    [DataRow("topology")]
    public void TopologyRejectsMissingAndNullRequiredMembers(string member)
    {
        var topology = new SourceProfileTopology(
            SourceCoreSchemaIds.SourceProfileTopology,
            ArtifactRef('a', '1'),
            new SourceRegistryMemberRef(ArtifactRef('c', '3'), "single_publisher_store"));
        var original = JsonNode.Parse(ContractJson.Serialize(topology))!.AsObject();
        var missing = JsonNode.Parse(original.ToJsonString())!.AsObject();
        missing.Remove(member);
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceProfileTopology>(missing.ToJsonString()));

        var nullMember = JsonNode.Parse(original.ToJsonString())!.AsObject();
        nullMember[member] = null;
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceProfileTopology>(nullMember.ToJsonString()));
    }

    [TestMethod]
    public void TopologyRejectsUnknownSchemaEmptyMemberAndUnknownProperty()
    {
        var valid = new SourceProfileTopology(
            SourceCoreSchemaIds.SourceProfileTopology,
            ArtifactRef('a', '1'),
            new SourceRegistryMemberRef(ArtifactRef('c', '3'), "single_publisher_store"));
        var node = JsonNode.Parse(ContractJson.Serialize(valid))!.AsObject();

        node["schema"] = "lex-v3-source-profile-topology/2";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceProfileTopology>(node.ToJsonString()));

        node = JsonNode.Parse(ContractJson.Serialize(valid))!.AsObject();
        node["topology"]!["member_key"] = string.Empty;
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceProfileTopology>(node.ToJsonString()));

        node = JsonNode.Parse(ContractJson.Serialize(valid))!.AsObject();
        node["topology_mode"] = "single_publisher_store";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<SourceProfileTopology>(node.ToJsonString()));
    }

    public static IEnumerable<object[]> ObjectMemberCases() =>
        ObjectMembers.Select(static member => new object[] { member });

    public static IEnumerable<object[]> RequiredObjectMemberCases() =>
        ObjectMembers
            .Where(static member => !string.Equals(
                member, "parent_key_ref", StringComparison.Ordinal))
            .Select(static member => new object[] { member });

    private static SourceObjectRef ObjectRef(bool withParent)
    {
        const string key = "jolux|law|2026-01-01|a1";
        var registry = ArtifactRef('b', '2');
        var parent = withParent
            ? new SourceObjectKeyRef(
                new SourceRegistryMemberRef(registry, "work"),
                "https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01",
                "jolux|work|2026-01-01",
                Sha256("jolux|work|2026-01-01"))
            : null;
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(registry, "law"),
            "https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            key,
            Sha256(key),
            ArtifactRef('a', '1'),
            parent);
    }

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        new string(digestFill, 64));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void AssertWireValue(SourceAuthority authority, string wireValue)
    {
        Assert.AreEqual($"\"{wireValue}\"", ContractJson.Serialize(authority));
        Assert.AreEqual(
            authority,
            ContractJson.Deserialize<SourceAuthority>($"\"{wireValue}\""));
    }
}
