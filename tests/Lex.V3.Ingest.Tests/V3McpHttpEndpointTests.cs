using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The MCP streamable HTTP endpoint, <c>POST /mcp</c>, through the real request delegate: one
/// JSON-RPC message in, one JSON document out (a notification answers 202 with no body), the tool
/// calls answering exactly the envelopes the REST routes answer for the same requests, and the
/// transport's own refusals (method, size) below the protocol.
/// </summary>
[TestClass]
public sealed class V3McpHttpEndpointTests
{
    private static string RawTarget => V3ApiHandler.McpRawTarget;

    [TestMethod]
    public async Task InitializeAndToolsListAnswerJsonWithTheProtocolVersionHeader()
    {
        var initialize = await PostAsync(null, JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18" } }));
        Assert.AreEqual(StatusCodes.Status200OK, initialize.Response.StatusCode);
        StringAssert.StartsWith(initialize.Response.ContentType, "application/json");
        Assert.AreEqual(V3McpJsonRpc.ProtocolVersion, initialize.Response.Headers["MCP-Protocol-Version"].ToString());
        using var initialized = JsonDocument.Parse(ResponseBytes(initialize));
        Assert.AreEqual(V3McpJsonRpc.ProtocolVersion, initialized.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

        var list = await PostAsync(null, JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/list" }));
        Assert.AreEqual(StatusCodes.Status200OK, list.Response.StatusCode);
        using var listed = JsonDocument.Parse(ResponseBytes(list));
        CollectionAssert.AreEqual(
            V3RestRouteBinding.Served.Select(static binding => binding.OperationId).ToArray(),
            listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(static tool => tool.GetProperty("name").GetString()).ToArray(),
            "the endpoint lists one tool per served REST operation.");
    }

    [TestMethod]
    public async Task ANotificationIsAcceptedWithNoBody()
    {
        var context = await PostAsync(null, JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/initialized" }));

        Assert.AreEqual(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.AreEqual(0, ResponseBytes(context).Length);
    }

    [TestMethod]
    public async Task EveryToolCallAnswersTheEnvelopeTheRestRouteAnswersForTheSameRequest()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";
        var calls = new (string Operation, object Arguments)[]
        {
            ("resolve", new { identifier = fixture.ExpressionIri }),
            ("as_of", new { identifier = work, date = fixture.ApplicabilityDate, language = "fra" }),
            ("timeline", new { identifier = work }),
            ("article_history", new { identifier = work, anchor = "art_1er" }),
            ("diff", new { identifier = work, date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
            ("changes_in_period", new { identifier = work, date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
            ("in_force_on", new { identifier = work, date = fixture.ApplicabilityDate }),
            ("search", new { query = "loyer", language = "fra", identifier = work }),
            ("coverage", new { }),
            ("provenance", new { identifier = work, date = fixture.ApplicabilityDate }),
            ("dossier", new { identifier = work }),
            ("citation", new { identifier = work, date = fixture.ApplicabilityDate }),
            ("cited_by", new { identifier = work }),
            ("verify", new { identifier = fixture.Permalink }),
            ("relations", new { identifier = work }),
            ("evidence_bundle", new { identifier = work, date = fixture.ApplicabilityDate, language = "fra" }),
            ("classification", new { identifier = work, language = "fra" }),
            ("manifestation", new { identifier = work, language = "fra" }),
            ("status_on", new { identifier = work, date = fixture.ApplicabilityDate, language = "fra" }),
            ("browse", new { language = "fra" }),
        };
        CollectionAssert.AreEquivalent(
            V3RestRouteBinding.Served.Select(static binding => binding.OperationId).ToArray(),
            calls.Select(static call => call.Operation).ToArray(),
            "every served operation is called once here.");

        foreach (var (operation, arguments) in calls)
        {
            const string traceIdentifier = "mcp-endpoint-same-request";
            var mcp = await PostAsync(mount, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = operation, method = "tools/call", @params = new { name = operation, arguments },
            }), traceIdentifier);
            Assert.AreEqual(StatusCodes.Status200OK, mcp.Response.StatusCode, operation);
            using var rpc = JsonDocument.Parse(ResponseBytes(mcp));
            Assert.IsFalse(rpc.RootElement.TryGetProperty("error", out var rpcError), operation + ": " + (rpcError.ValueKind == JsonValueKind.Undefined ? "" : rpcError.ToString()));
            var structured = rpc.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.AreEqual(V3Verdicts.Answer, structured.GetProperty("verdict").GetString(), operation + ": the fixture answers this call; a refusal on both paths would still compare equal.");

            var rest = await PostAsync(mount, JsonSerializer.Serialize(new { operation_id = operation, parameters = arguments }), traceIdentifier,
                V3RestRouteBinding.Served.Single(binding => binding.OperationId == operation).RawTarget);
            Assert.AreEqual(StatusCodes.Status200OK, rest.Response.StatusCode, operation);
            using var envelope = JsonDocument.Parse(ResponseBytes(rest));
            Assert.AreEqual(
                JsonSerializer.Serialize(envelope.RootElement),
                JsonSerializer.Serialize(structured),
                $"{operation}: the MCP tool call and the REST route answered different envelopes for the same request.");
            _ = V3EnvelopeJson.ParseAndVerify(ResponseBytes(rest), V3OperationRegistry.Reviewed);
        }
    }

    [TestMethod]
    public async Task AToolCallWithNoCorpusMountedRefusesAsRestDoes()
    {
        var context = await PostAsync(null, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "timeline", arguments = new { identifier = "/lu-legilux/loi-1991-08-10-n3" } },
        }));

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        using var rpc = JsonDocument.Parse(ResponseBytes(context));
        var structured = rpc.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.AreEqual(V3Verdicts.Refuse, structured.GetProperty("verdict").GetString());
        Assert.AreEqual("no_corpus_mounted", structured.GetProperty("refusal").GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task AParseErrorIsAJsonRpcErrorOverHttp200AndAnInvalidToolArgumentIsInvalidParams()
    {
        var malformed = await PostAsync(null, "{ not json");
        Assert.AreEqual(StatusCodes.Status200OK, malformed.Response.StatusCode);
        using var parse = JsonDocument.Parse(ResponseBytes(malformed));
        Assert.AreEqual(-32700, parse.RootElement.GetProperty("error").GetProperty("code").GetInt32());

        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        var invalid = await PostAsync(mount, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 6, method = "tools/call", @params = new { name = "as_of", arguments = new { identifier = "/lu-legilux/x" } },
        }));
        Assert.AreEqual(StatusCodes.Status200OK, invalid.Response.StatusCode);
        using var rpc = JsonDocument.Parse(ResponseBytes(invalid));
        Assert.AreEqual(-32602, rpc.RootElement.GetProperty("error").GetProperty("code").GetInt32(), "a missing required date never reaches the mount.");
    }

    [TestMethod]
    public async Task AGetIsMethodNotAllowedAndAnOversizedBodyIsRefusedBeforeParsingAndBothNameTheProtocolVersion()
    {
        var get = await SendAsync(null, HttpMethods.Get, Array.Empty<byte>());
        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, get.Response.StatusCode);
        Assert.AreEqual(V3McpJsonRpc.ProtocolVersion, get.Response.Headers["MCP-Protocol-Version"].ToString());

        var oversized = await SendAsync(null, HttpMethods.Post, Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"pad\":\"" + new string('x', V3PlatformHost.MaximumRequestBytes) + "\"}"));
        Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, oversized.Response.StatusCode);
        Assert.AreEqual(V3McpJsonRpc.ProtocolVersion, oversized.Response.Headers["MCP-Protocol-Version"].ToString());
    }

    [TestMethod]
    public async Task TheClientsOwnProtocolVersionHeaderIsNotReadAndEveryAnswerNamesTheServedOne()
    {
        // S4-A11: the API reads nothing about the caller, so a client naming another version is
        // served like any other and told the one version served, rather than refused on a header read.
        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        foreach (var version in new[] { "1999-01-01", V3McpJsonRpc.ProtocolVersion, null })
        {
            var context = await SendAsync(null, HttpMethods.Post, Encoding.UTF8.GetBytes(body), protocolVersion: version);
            Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, version ?? "(none)");
            Assert.AreEqual(V3McpJsonRpc.ProtocolVersion, context.Response.Headers["MCP-Protocol-Version"].ToString(), version ?? "(none)");
        }
    }

    [TestMethod]
    public async Task TheEndpointDoesNotShadowTheRestRoutesOrTheUnknownRouteAnswer()
    {
        var unknown = await SendAsync(null, HttpMethods.Post, Encoding.UTF8.GetBytes("{}"), "/mcp/");
        Assert.AreEqual(StatusCodes.Status404NotFound, unknown.Response.StatusCode, "only the exact target is the endpoint; /api/v3/ stays the REST namespace.");
    }

    private static Task<DefaultHttpContext> PostAsync(V3CorpusMount? mount, string body, string traceIdentifier = "mcp-endpoint", string? rawTarget = null) =>
        SendAsync(mount, HttpMethods.Post, Encoding.UTF8.GetBytes(body), rawTarget, traceIdentifier);

    private static async Task<DefaultHttpContext> SendAsync(
        V3CorpusMount? mount, string method, byte[] body, string? rawTarget = null, string traceIdentifier = "mcp-endpoint",
        string? protocolVersion = null)
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = traceIdentifier;
        context.Request.Method = method;
        if (protocolVersion is not null)
        {
            context.Request.Headers["MCP-Protocol-Version"] = protocolVersion;
        }

        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget ?? RawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }
}
