using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

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
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
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
            var boundResult = new V3PlatformOperationResult(
                bound,
                "work_resolution",
                result.RootElement);
            Assert.AreEqual("resolve", boundResult.OperationId);
            Assert.AreEqual("lex-v3-resolve-result/1", boundResult.Schema);
            Assert.AreEqual(
                V3OperationRegistry.Reviewed.Operation("resolve").ResultSchemaSha256,
                boundResult.SchemaSha256);
            return boundResult;
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
            "{\"operation_id\":\"resolve\",\"parameters\":{},}",
            OversizedRequest(),
            DeepRequest(),
        };
        var calls = 0;

        foreach (var json in invalid)
        {
            await Assert.ThrowsAsync<JsonException>(async () =>
                await host.CreateMcpSuccessAsync(
                    Encoding.UTF8.GetBytes(json),
                    "req_host",
                    Context(),
                    _ =>
                    {
                        calls++;
                        throw new AssertFailedException("The operation must not run.");
                    },
                    CancellationToken.None));
        }

        Assert.AreEqual(0, calls);
    }

    private static string OversizedRequest() =>
        "{\"operation_id\":\"resolve\",\"parameters\":{\"value\":\"" +
        new string('x', V3PlatformHost.MaximumRequestBytes) +
        "\"}}";

    private static string DeepRequest()
    {
        var request = new StringBuilder("{\"operation_id\":\"resolve\",\"parameters\":");
        for (var index = 0; index < 32; index++)
        {
            request.Append("{\"nested\":");
        }

        request.Append("{}");
        request.Append('}', 32);
        request.Append('}');
        return request.ToString();
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
                bound => new V3PlatformOperationResult(bound, "quote", value.RootElement),
                CancellationToken.None));
    }

    [TestMethod]
    public void ReviewedSchemaDocumentsExecuteAgainstRequestAndResultPayloads()
    {
        var schemas = V3PlatformSchemaDocuments.Reviewed;
        var operation = V3OperationRegistry.Reviewed.Operation("resolve");
        using var request = JsonDocument.Parse(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\"eli/example\"}}");
        using var result = JsonDocument.Parse(
            "{\"operation_id\":\"resolve\",\"object_type\":\"work_resolution\",\"value\":{}}");

        schemas.ValidateRequest(operation, request.RootElement);
        schemas.ValidateResult(operation, result.RootElement);
    }

    [TestMethod]
    public void ResultSchemaCanRejectIndependentlyOfRegistryIdentityBinding()
    {
        var schemas = V3PlatformSchemaDocuments.Reviewed;
        var operation = V3OperationRegistry.Reviewed.Operation("resolve");
        using var invalid = JsonDocument.Parse(
            "{\"operation_id\":\"resolve\",\"object_type\":\"work_resolution\"}");

        Assert.ThrowsExactly<JsonException>(() => schemas.ValidateResult(operation, invalid.RootElement));
    }

    [TestMethod]
    public async Task HostInvokesRequestAndResultSchemaValidationAroundExecution()
    {
        var schemas = new RecordingSchemaDocuments();
        var host = new V3PlatformHost(schemas);
        using var result = JsonDocument.Parse("{\"work_id\":\"eli/example\"}");

        await host.CreateMcpSuccessAsync(
            Encoding.UTF8.GetBytes(
                "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\"eli/example\"}}"),
            "req_schema_calls",
            Context(),
            bound =>
            {
                Assert.AreEqual(1, schemas.RequestCalls);
                Assert.AreEqual(0, schemas.ResultCalls);
                return new V3PlatformOperationResult(bound, "work_resolution", result.RootElement);
            },
            CancellationToken.None);

        Assert.AreEqual(1, schemas.RequestCalls);
        Assert.AreEqual(1, schemas.ResultCalls);
        Assert.AreEqual(0, schemas.RefusalCalls);
    }

    [TestMethod]
    public async Task HostInvokesRefusalSchemaValidationAfterExecution()
    {
        var schemas = new RecordingSchemaDocuments();
        var host = new V3PlatformHost(schemas);
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");

        await host.CreateMcpRefusalAsync(
            Encoding.UTF8.GetBytes("{\"operation_id\":\"resolve\",\"parameters\":{}}"),
            "req_refusal_schema_calls",
            RefusalContext(),
            bound => new V3PlatformOperationRefusal(
                bound,
                "no_corpus_mounted",
                helpful.RootElement),
            CancellationToken.None);

        Assert.AreEqual(1, schemas.RequestCalls);
        Assert.AreEqual(0, schemas.ResultCalls);
        Assert.AreEqual(1, schemas.RefusalCalls);
    }

    [TestMethod]
    public async Task KnownRefusalUsesMandatoryHelpfulPayloadAndPreservesRestMcpBytes()
    {
        var host = new V3PlatformHost();
        var request = Encoding.UTF8.GetBytes(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\"eli/example\"}}");
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");

        var restContext = new DefaultHttpContext();
        await using var restBody = new MemoryStream();
        restContext.Response.Body = restBody;
        await host.WriteRestRefusalAsync(
            restContext.Response,
            request,
            "req_refusal",
            RefusalContext(),
            bound => new V3PlatformOperationRefusal(
                bound,
                "no_corpus_mounted",
                helpful.RootElement),
            CancellationToken.None);
        var mcp = await host.CreateMcpRefusalAsync(
            request,
            "req_refusal",
            RefusalContext(),
            bound => new V3PlatformOperationRefusal(
                bound,
                "no_corpus_mounted",
                helpful.RootElement),
            CancellationToken.None);

        CollectionAssert.AreEqual(restBody.ToArray(), mcp.JsonUtf8);
        var envelope = V3EnvelopeJson.ParseAndVerify(mcp.JsonUtf8, V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        Assert.AreEqual("lu", envelope.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
    }

    [TestMethod]
    public async Task RealResolveRouteReadsThePostBodyAndReachesTheReviewedHost()
    {
        var request = Encoding.UTF8.GetBytes(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\"eli/example\"}}");
        var context = RouteContext(request);
        var called = false;

        await V3ResolveRestRoute.WriteSuccessAsync(
            context,
            new V3PlatformHost(),
            "req_route",
            Context(),
            bound =>
            {
                called = true;
                using var value = JsonDocument.Parse(
                    $"{{\"work_id\":{JsonSerializer.Serialize(bound.Parameters.GetProperty("identifier").GetString())}}}");
                return new V3PlatformOperationResult(bound, "work_resolution", value.RootElement);
            },
            CancellationToken.None);

        Assert.IsTrue(called);
        context.Response.Body.Position = 0;
        var envelope = V3EnvelopeJson.ParseAndVerify(
            ((MemoryStream)context.Response.Body).ToArray(),
            V3OperationRegistry.Reviewed);
        Assert.AreEqual("eli/example", envelope.Result!.Value.GetProperty("work_id").GetString());
    }

    [TestMethod]
    public async Task SyntheticPreviewHandlerCannotAnswerTheRealResolveRoute()
    {
        var context = RouteContext(
            Encoding.UTF8.GetBytes("{\"operation_id\":\"resolve\",\"parameters\":{}}"));

        await SyntheticApiHandler.HandleAsync(
            context,
            SyntheticApiState.Unavailable,
            CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task RealResolveRouteWritesAReviewedDomainRefusal()
    {
        var context = RouteContext(
            Encoding.UTF8.GetBytes("{\"operation_id\":\"resolve\",\"parameters\":{}}"));
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");

        await V3ResolveRestRoute.WriteRefusalAsync(
            context,
            new V3PlatformHost(),
            "req_route_refusal",
            RefusalContext(),
            bound => new V3PlatformOperationRefusal(
                bound,
                "no_corpus_mounted",
                helpful.RootElement),
            CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(
            ((MemoryStream)context.Response.Body).ToArray(),
            V3OperationRegistry.Reviewed);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
    }

    [TestMethod]
    public async Task RealResolveRouteFailsClosedOnTransportDriftBeforeExecution()
    {
        var calls = 0;
        foreach (var mutate in new Action<DefaultHttpContext>[]
                 {
                     context => context.Request.Method = HttpMethods.Get,
                     context => context.Features.Get<IHttpRequestFeature>()!.RawTarget =
                         V3ResolveRestRoute.RawTarget + "?operation=resolve",
                     context => context.Request.ContentLength = V3PlatformHost.MaximumRequestBytes + 1L,
                 })
        {
            var context = RouteContext(
                Encoding.UTF8.GetBytes("{\"operation_id\":\"resolve\",\"parameters\":{}}"));
            mutate(context);

            await Assert.ThrowsAsync<JsonException>(async () =>
                await V3ResolveRestRoute.WriteSuccessAsync(
                    context,
                    new V3PlatformHost(),
                    "req_route",
                    Context(),
                    _ =>
                    {
                        calls++;
                        throw new AssertFailedException("transport drift must not execute the operation");
                    },
                    CancellationToken.None));
        }

        Assert.AreEqual(0, calls);
    }

    private static V3EnvelopeContext Context() => new(
        PublisherId.LuLegilux,
        "success",
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(DateTimeOffset.Parse("2026-09-17T00:00:00Z"), "current"));

    private static V3EnvelopeContext RefusalContext() => new(
        PublisherId.LuLegilux,
        "refusal",
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(DateTimeOffset.Parse("2026-09-17T00:00:00Z"), "current"));

    private static DefaultHttpContext RouteContext(byte[] request)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(request);
        context.Response.Body = new MemoryStream();
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = V3ResolveRestRoute.RawTarget;
        return context;
    }

    private sealed class RecordingSchemaDocuments : IV3PlatformSchemaDocuments
    {
        public int RequestCalls { get; private set; }

        public int ResultCalls { get; private set; }

        public int RefusalCalls { get; private set; }

        public void ValidateRequest(V3OperationDefinition operation, JsonElement document) => RequestCalls++;

        public void ValidateResult(V3OperationDefinition operation, JsonElement document) => ResultCalls++;

        public void ValidateRefusal(JsonElement document) => RefusalCalls++;
    }
}
