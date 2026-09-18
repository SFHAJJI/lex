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
        var invalid = new (string Json, V3TransportFailureKind Kind)[]
        {
            ("{", V3TransportFailureKind.MalformedJson),
            ("{\"operation_id\":\"unknown\",\"parameters\":{}}", V3TransportFailureKind.UnknownOperation),
            ("{\"operation_id\":\"resolve\",\"parameters\":{},\"extra\":true}", V3TransportFailureKind.RequestSchemaInvalid),
            ("{\"operation_id\":\"resolve\",\"parameters\":[]}", V3TransportFailureKind.ParametersNotObject),
            ("{\"operation_id\":\"resolve\",\"operation_id\":\"search\",\"parameters\":{}}", V3TransportFailureKind.DuplicateJsonMember),
            ("{\"operation_id\":\"resolve\",\"parameters\":{}}{}", V3TransportFailureKind.TrailingJsonContent),
            ("{\"operation_id\":\"resolve\",\"parameters\":{},}", V3TransportFailureKind.MalformedJson),
            (OversizedRequest(), V3TransportFailureKind.RequestTooLarge),
            (DeepRequest(), V3TransportFailureKind.RequestTooDeep),
        };
        var calls = 0;

        foreach (var (json, kind) in invalid)
        {
            var exception = await Assert.ThrowsExactlyAsync<V3TransportFailureException>(async () =>
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
            Assert.AreEqual(kind, exception.Kind);
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
                     context => context.Request.Body = new MemoryStream(
                         new byte[V3PlatformHost.MaximumRequestBytes + 1]),
                 })
        {
            var context = RouteContext(
                Encoding.UTF8.GetBytes("{\"operation_id\":\"resolve\",\"parameters\":{}}"));
            mutate(context);

            await Assert.ThrowsExactlyAsync<V3TransportFailureException>(async () =>
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

    [TestMethod]
    [DataRow("malformed", "malformed_json", StatusCodes.Status400BadRequest)]
    [DataRow("duplicate", "duplicate_json_member", StatusCodes.Status400BadRequest)]
    [DataRow("trailing", "trailing_json_content", StatusCodes.Status400BadRequest)]
    [DataRow("too_large", "request_too_large", StatusCodes.Status413PayloadTooLarge)]
    [DataRow("too_deep", "request_too_deep", StatusCodes.Status400BadRequest)]
    [DataRow("parameters", "parameters_not_object", StatusCodes.Status400BadRequest)]
    [DataRow("schema", "request_schema_invalid", StatusCodes.Status400BadRequest)]
    [DataRow("operation", "unknown_operation", StatusCodes.Status400BadRequest)]
    public async Task EveryCallerProtocolFailureUsesItsClosedTransportCode(
        string scenario,
        string expectedCode,
        int expectedStatus)
    {
        var body = scenario switch
        {
            "malformed" => "{",
            "duplicate" => "{\"operation_id\":\"resolve\",\"operation_id\":\"resolve\",\"parameters\":{}}",
            "trailing" => "{\"operation_id\":\"resolve\",\"parameters\":{}}{}",
            "too_large" => OversizedRequest(),
            "too_deep" => DeepRequest(),
            "parameters" => "{\"operation_id\":\"resolve\",\"parameters\":[]}",
            "schema" => "{\"operation_id\":\"resolve\",\"parameters\":{},\"extra\":true}",
            "operation" => "{\"operation_id\":\"unknown\",\"parameters\":{}}",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var context = RouteContext(Encoding.UTF8.GetBytes(body));
        var calls = 0;

        await V3ResolveRestRoute.HandleSuccessAsync(
            context,
            new V3PlatformHost(),
            "req_transport",
            Context(),
            _ =>
            {
                calls++;
                throw new AssertFailedException("A protocol failure must not execute the operation.");
            },
            CancellationToken.None);

        Assert.AreEqual(0, calls);
        AssertTransportProblem(context, expectedCode, expectedStatus);
    }

    [TestMethod]
    public async Task MethodAndRouteFailuresRemainBelowTheEnvelope()
    {
        var method = RouteContext(Encoding.UTF8.GetBytes("{}"));
        method.Request.Method = HttpMethods.Get;
        await V3ResolveRestRoute.HandleSuccessAsync(
            method,
            new V3PlatformHost(),
            "req_method",
            Context(),
            _ => throw new AssertFailedException("The operation must not run."),
            CancellationToken.None);
        AssertTransportProblem(method, "method_not_allowed", StatusCodes.Status405MethodNotAllowed);
        Assert.AreEqual(HttpMethods.Post, method.Response.Headers.Allow.ToString());

        var route = RouteContext(Encoding.UTF8.GetBytes("{}"));
        route.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/unknown";
        var application = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            DateTimeOffset.Parse("2026-09-18T00:00:00Z"));
        await application.HandleAsync(route, CancellationToken.None);
        AssertTransportProblem(route, "unknown_route", StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task SchemaInvalidResultCannotLeakAnEnvelopeOrPartialResult()
    {
        var context = RouteContext(Encoding.UTF8.GetBytes(
            "{\"operation_id\":\"resolve\",\"parameters\":{}}"));
        using var value = JsonDocument.Parse("{\"work_id\":\"eli/example\"}");

        await V3ResolveRestRoute.HandleSuccessAsync(
            context,
            new V3PlatformHost(new RejectingResultSchemaDocuments()),
            "req_invalid_result",
            Context(),
            bound => new V3PlatformOperationResult(bound, "work_resolution", value.RootElement),
            CancellationToken.None);

        AssertTransportProblem(
            context,
            "internal_response_invalid",
            StatusCodes.Status500InternalServerError);
        var body = ((MemoryStream)context.Response.Body).ToArray();
        using var problem = JsonDocument.Parse(body);
        Assert.IsFalse(problem.RootElement.TryGetProperty("result", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
    }

    [TestMethod]
    public async Task ApplicationRoutesRealResolveToReviewedDomainRefusalBeforeSyntheticPreview()
    {
        var context = RouteContext(Encoding.UTF8.GetBytes(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\"eli/example\"}}"));
        context.TraceIdentifier = "trace-real-resolve";
        var application = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            DateTimeOffset.Parse("2026-09-18T00:00:00Z"));

        await application.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        var envelope = V3EnvelopeJson.ParseAndVerify(bytes, V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        Assert.AreEqual("lu", envelope.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
    }

    private static void AssertTransportProblem(
        DefaultHttpContext context,
        string expectedCode,
        int expectedStatus)
    {
        Assert.AreEqual(expectedStatus, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.AreEqual(V3TransportResponse.Schema, root.GetProperty("schema").GetString());
        Assert.AreEqual(expectedCode, root.GetProperty("code").GetString());
        Assert.AreEqual(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.IsFalse(root.TryGetProperty("version", out _));
        Assert.IsFalse(root.TryGetProperty("operation_id", out _));
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

    private sealed class RejectingResultSchemaDocuments : IV3PlatformSchemaDocuments
    {
        public void ValidateRequest(V3OperationDefinition operation, JsonElement document)
        {
        }

        public void ValidateResult(V3OperationDefinition operation, JsonElement document) =>
            throw new JsonException("synthetic result-schema rejection");

        public void ValidateRefusal(JsonElement document)
        {
        }
    }
}
