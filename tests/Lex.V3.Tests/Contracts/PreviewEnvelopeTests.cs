using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class PreviewEnvelopeTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void PreviewSchemaGraphAndContractSetAreExact()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                V3SchemaIds.PreviewArtifact,
                V3SchemaIds.PreviewPayload,
                V3SchemaIds.PreviewEnvelope,
                V3SchemaIds.PreviewObjectSet,
                V3SchemaIds.PreviewOperationCatalog,
                V3SchemaIds.PreviewRefusalRegistry,
            },
            PreviewSchemaGraph.SchemaIds.ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                V3SchemaIds.PreviewEnvelope,
                V3SchemaIds.PreviewObjectSet,
                V3SchemaIds.PreviewOperationCatalog,
                V3SchemaIds.PreviewRefusalRegistry,
            },
            PreviewSchemaGraph.ContractSetSchemaIds.ToArray());
    }

    [TestMethod]
    public void RefusalEnvelopeCannotCarrySuccessOrMismatchedStatus()
    {
        var envelope = PreviewRefusalEnvelope.Create(CreateContext(), CreateRefusal());
        var json = ContractJson.Serialize<PreviewEnvelope>(envelope);

        StringAssert.Contains(json, "\"branch\":\"refusal\"");
        StringAssert.Contains(json, "\"status\":\"identifier_unknown\"");
        StringAssert.Contains(json, "\"refusal\"");
        Assert.IsFalse(json.Contains("\"result\"", StringComparison.Ordinal));

        var wrongStatus = JsonNode.Parse(json)!.AsObject();
        wrongStatus["status"] = "ok";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<PreviewEnvelope>(wrongStatus.ToJsonString()));

        var addedResult = JsonNode.Parse(json)!.AsObject();
        addedResult["result"] = new JsonObject { ["object_set_sha256"] = Digest };
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<PreviewEnvelope>(addedResult.ToJsonString()));
    }

    [TestMethod]
    public void SuccessEnvelopeCannotCarryARefusal()
    {
        var envelope = PreviewSuccessEnvelope.Create(
            CreateContext(),
            new PreviewObjectSetReference("preview-objects", Digest));
        var json = ContractJson.Serialize<PreviewEnvelope>(envelope);

        StringAssert.Contains(json, "\"branch\":\"success\"");
        StringAssert.Contains(json, "\"status\":\"ok\"");
        StringAssert.Contains(json, "\"result\"");
        Assert.IsFalse(json.Contains("\"refusal\"", StringComparison.Ordinal));

        var addedRefusal = JsonNode.Parse(json)!.AsObject();
        addedRefusal["refusal"] = JsonNode.Parse(ContractJson.Serialize(CreateRefusal()));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<PreviewEnvelope>(addedRefusal.ToJsonString()));
    }

    [TestMethod]
    public void PreviewEnvelopeCarriesNoRawQueryOrClientIdentifier()
    {
        var json = ContractJson.Serialize<PreviewEnvelope>(
            PreviewRefusalEnvelope.Create(CreateContext(), CreateRefusal()));

        Assert.IsFalse(json.Contains("query", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("user_agent", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("ip_address", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("remote_address", StringComparison.OrdinalIgnoreCase));
    }

    private static PreviewEnvelopeContext CreateContext() => new(
        requestRef: "req_0123456789abcdef0123456789abcdef",
        operation: new PreviewOperationReference("resolve", "preview-catalog", Digest),
        refusalRegistry: new PreviewRefusalRegistryReference(
            "preview-registry",
            V3SchemaIds.PreviewRefusalRegistry,
            Digest),
        snapshot: new PreviewSnapshotReference("preview-snapshot", Digest),
        artifact: new PreviewArtifactReference("preview-artifact"),
        indexFormat: "preview-index/1",
        runtime: new ComponentIdentity("lex-v3-preview-runtime", Digest),
        builder: new ComponentIdentity("lex-v3-preview-builder", Digest),
        capabilities: PreviewCapabilityState.MechanicsOnly,
        freshness: new PreviewFreshness(
            DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
            PreviewUpstreamHealth.NotApplicableSynthetic),
        jurisdiction: "synthetic-preview-no-jurisdiction",
        provisionality: PreviewProvisionality.All,
        source: PreviewSourceContext.SyntheticTest);

    private static IdentifierUnknownRefusal CreateRefusal() => IdentifierUnknownRefusal.Create(
        IdentifierFamily.Eli,
        "eli/synthetic-preview",
        new[] { PublisherId.LuLegilux },
        Array.Empty<HeldRecordCandidate>(),
        new[]
        {
            PublisherSearchAction.Create(PublisherId.LuLegilux),
        },
        new[] { WhatWouldAnswerAction.CorrectedIdentifier });
}
