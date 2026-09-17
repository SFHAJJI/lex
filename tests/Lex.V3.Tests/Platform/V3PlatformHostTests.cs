using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3PlatformHostTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public async Task RestAndMcpHostBoundariesEmitIdenticalRegistryBoundBytes()
    {
        var host = new V3PlatformHost();
        var request = Encoding.UTF8.GetBytes(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\"arrêté & co\"}}");

        var restContext = new DefaultHttpContext();
        await using var restBody = new MemoryStream();
        restContext.Response.Body = restBody;
        await host.WriteRestSuccessAsync(
            restContext.Response,
            request,
            "req_host",
            Context(),
            Execute,
            CancellationToken.None);
        var mcp = await host.CreateMcpSuccessAsync(
            request,
            "req_host",
            Context(),
            Execute,
            CancellationToken.None);

        CollectionAssert.AreEqual(restBody.ToArray(), mcp.JsonUtf8);
        Assert.AreEqual("application/json;charset=utf-8", restContext.Response.ContentType);
        Assert.AreEqual("application/json;charset=utf-8", mcp.ContentType);
        var envelope = V3EnvelopeJson.ParseAndVerify(mcp.JsonUtf8, V3OperationRegistry.Reviewed);
        Assert.AreEqual("arrêté & co", envelope.Result!.Value.GetProperty("work_id").GetString());

        static V3PlatformOperationResult Execute(V3PlatformOperationRequest bound)
        {
            Assert.AreEqual("resolve", bound.OperationId);
            Assert.AreEqual("lex-v3-resolve-request/1", bound.Schema);
            Assert.AreEqual(
                V3OperationRegistry.Reviewed.Operation("resolve").RequestSchemaSha256,
                bound.SchemaSha256);
            Assert.AreEqual("arrêté & co", bound.Parameters.GetProperty("identifier").GetString());
            using var result = JsonDocument.Parse("{\"work_id\":\"arrêté & co\"}");
            return new V3PlatformOperationResult("work_resolution", result.RootElement);
        }
    }

    [TestMethod]
    public async Task RequestsFailClosedBeforeTheOperationRuns()
    {
        var host = new V3PlatformHost();
        var invalid = new[]
        {
            "{\"operation_id\":\"unknown\",\"parameters\":{}}",
            "{\"operation_id\":\"resolve\",\"parameters\":{},\"extra\":true}",
            "{\"operation_id\":\"resolve\",\"parameters\":[]}",
            "{\"operation_id\":\"resolve\",\"operation_id\":\"search\",\"parameters\":{}}",
            "{\"operation_id\":\"resolve\",\"parameters\":{}}{}",
        };
        var calls = 0;

        foreach (var json in invalid)
        {
            await Assert.ThrowsExactlyAsync<JsonException>(async () =>
                await host.CreateMcpSuccessAsync(
                    Encoding.UTF8.GetBytes(json),
                    "req_host",
                    Context(),
                    _ =>
                    {
                        calls++;
                        using var result = JsonDocument.Parse("{}");
                        return new V3PlatformOperationResult("work_resolution", result.RootElement);
                    },
                    CancellationToken.None));
        }

        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task ResultObjectTypeCannotEscapeTheBoundOperationSchema()
    {
        var host = new V3PlatformHost();
        using var value = JsonDocument.Parse("{}");

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await host.CreateMcpSuccessAsync(
                Encoding.UTF8.GetBytes("{\"operation_id\":\"resolve\",\"parameters\":{}}"),
                "req_host",
                Context(),
                _ => new V3PlatformOperationResult("quote", value.RootElement),
                CancellationToken.None));
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
