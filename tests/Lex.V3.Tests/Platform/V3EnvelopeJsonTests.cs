using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3EnvelopeJsonTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string RegistryDigest = "67c186981e546d736a4eef8fd994667e1fc11c89565c7fda6aaf79b7d4f7ddc1";
    private const string ExpectedSuccess = """{"context":{"freshness":{"observed_at":"2026-09-17T00:00:00.0000000Z","upstream_health":"current"},"jurisdiction":"lu","provisional":false,"publisher":"lu-legilux","snapshot":{"snapshot_id":"snapshot","snapshot_sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"},"status":"success","timeline_semantics":"publisher_applicability"},"object_type":"envelope","operation_id":"resolve","refusal":null,"registry_schema":"lex-v3-operation-registry/1","registry_sha256":"67c186981e546d736a4eef8fd994667e1fc11c89565c7fda6aaf79b7d4f7ddc1","request_ref":"req","result":{"object_type":"work_resolution","schema":"lex-v3-resolve-result/1","value":{"work_id":"lu-legilux:arr\u00EAt\u00E9 \u0026 co"}},"schema":"lex-v3-envelope/1","verdict":"answer","version":"v3"}""" + "\n";
    private const string ExpectedRefusal = """{"context":{"freshness":{"observed_at":"2026-09-17T00:00:00.0000000Z","upstream_health":"current"},"jurisdiction":"lu","provisional":false,"publisher":"lu-legilux","snapshot":{"snapshot_id":"snapshot","snapshot_sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"},"status":"refusal","timeline_semantics":"publisher_applicability"},"object_type":"envelope","operation_id":"resolve","refusal":{"code":"no_corpus_mounted","helpful_payload":{"required_corpus":"lu"},"schema":"lex-v3-refusal/1"},"registry_schema":"lex-v3-operation-registry/1","registry_sha256":"67c186981e546d736a4eef8fd994667e1fc11c89565c7fda6aaf79b7d4f7ddc1","request_ref":"req","result":null,"schema":"lex-v3-envelope/1","verdict":"refuse","version":"v3"}""" + "\n";
    private readonly V3OperationRegistry _registry = V3OperationRegistry.Reviewed;

    [TestMethod]
    public void RestAndMcpAreByteIdenticalAndRoundTripStrictly()
    {
        var envelope = Success();
        var rest = V3EnvelopeJson.ProjectRest(envelope, _registry);
        var mcp = V3EnvelopeJson.ProjectMcp(envelope, _registry);

        CollectionAssert.AreEqual(rest, mcp);
        var reopened = V3EnvelopeJson.ParseAndVerify(rest, _registry);
        Assert.AreEqual(envelope.OperationId, reopened.OperationId);
        CollectionAssert.AreEqual(rest, V3EnvelopeJson.ProjectRest(reopened, _registry));
    }

    [TestMethod]
    public void SuccessAndRefusalHaveLiteralCanonicalWireForms()
    {
        Assert.AreEqual(RegistryDigest, _registry.Sha256);
        Assert.AreEqual(ExpectedSuccess, Encoding.UTF8.GetString(V3EnvelopeJson.ProjectRest(Success(), _registry)));
        Assert.AreEqual(ExpectedRefusal, Encoding.UTF8.GetString(V3EnvelopeJson.ProjectMcp(Refusal(), _registry)));
    }

    [TestMethod]
    public void PayloadMemberOrderDoesNotChangeCanonicalProjection()
    {
        using var left = JsonDocument.Parse("{\"z\":1,\"a\":{\"y\":2,\"b\":3}}");
        using var right = JsonDocument.Parse("{\"a\":{\"b\":3,\"y\":2},\"z\":1}");
        var builder = new V3EnvelopeBuilder(_registry);

        CollectionAssert.AreEqual(
            V3EnvelopeJson.ProjectRest(builder.Success(
                "req", "resolve", Context(), V3Verdicts.Answer,
                "lex-v3-resolve-result/1", "work_resolution", left.RootElement), _registry),
            V3EnvelopeJson.ProjectMcp(builder.Success(
                "req", "resolve", Context(), V3Verdicts.Answer,
                "lex-v3-resolve-result/1", "work_resolution", right.RootElement), _registry));
    }

    [TestMethod]
    public void ReaderRejectsNonCanonicalOrBindingDrift()
    {
        var canonical = V3EnvelopeJson.ProjectRest(Success(), _registry);
        var nonCanonical = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("\"context\":", " \"context\":", StringComparison.Ordinal));
        Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(nonCanonical, _registry));

        var node = JsonNode.Parse(canonical)!.AsObject();
        node["registry_sha256"] = Digest;
        var drift = Encoding.UTF8.GetBytes(node.ToJsonString() + "\n");
        Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(drift, _registry));
    }

    [TestMethod]
    public void ReaderRejectsDuplicateMembers()
    {
        var duplicate = ExpectedSuccess.Replace(
            "\"schema\":\"lex-v3-envelope/1\"",
            "\"schema\":\"lex-v3-envelope/1\",\"schema\":\"lex-v3-envelope/1\"",
            StringComparison.Ordinal);

        var exception = Assert.ThrowsExactly<JsonException>(() =>
            V3EnvelopeJson.ParseAndVerify(Encoding.UTF8.GetBytes(duplicate), _registry));
        StringAssert.Contains(exception.Message, "Duplicate");
    }

    [TestMethod]
    public void UnknownProjectionFailsClosed()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            V3EnvelopeJson.Project(Success(), _registry, (V3EnvelopeProjectionKind)0));
    }

    private V3Envelope Success()
    {
        using var result = JsonDocument.Parse("{\"work_id\":\"lu-legilux:arrêté & co\"}");
        return new V3EnvelopeBuilder(_registry).Success(
            "req", "resolve", Context(), V3Verdicts.Answer,
            "lex-v3-resolve-result/1", "work_resolution", result.RootElement);
    }

    private V3Envelope Refusal()
    {
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");
        return new V3EnvelopeBuilder(_registry).Refusal(
            "req", "resolve", Context("refusal"), "lex-v3-refusal/1",
            "no_corpus_mounted", helpful.RootElement);
    }

    private static V3EnvelopeContext Context(string status = "success") => new(
        PublisherId.LuLegilux,
        status,
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(DateTimeOffset.Parse("2026-09-17T00:00:00Z"), "current"));
}
