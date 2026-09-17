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
    public void UnknownProjectionFailsClosed()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            V3EnvelopeJson.Project(Success(), _registry, (V3EnvelopeProjectionKind)0));
    }

    private V3Envelope Success()
    {
        using var result = JsonDocument.Parse("{\"work_id\":\"lu-legilux:test\"}");
        return new V3EnvelopeBuilder(_registry).Success(
            "req", "resolve", Context(), V3Verdicts.Answer,
            "lex-v3-resolve-result/1", "work_resolution", result.RootElement);
    }

    private static V3EnvelopeContext Context() => new(
        PublisherId.LuLegilux,
        "success",
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(DateTimeOffset.Parse("2026-09-17T00:00:00Z"), "current"));
}
