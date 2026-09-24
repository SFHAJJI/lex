using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S4-A01 slice 1: <see cref="V3McpJsonRpc"/>, the pure JSON-RPC dispatcher over one operation
/// (<c>resolve</c>). No stdio, no process — every test calls <see cref="V3McpJsonRpc.HandleAsync"/>
/// directly with a request payload and reads the response payload back.
/// </summary>
[TestClass]
public sealed class V3McpJsonRpcTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);

    private static byte[] Request(object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload);

    private static async Task<JsonDocument> HandleAsync(
        object payload, V3CorpusMount? mount = null, string requestReference = "mcp_test_ref")
    {
        var bytes = await V3McpJsonRpc.HandleAsync(
            Request(payload), new V3PlatformHost(), mount, static () => ObservedAt, requestReference, CancellationToken.None);
        Assert.IsNotNull(bytes, "A request carrying an id must always be answered.");
        return JsonDocument.Parse(bytes);
    }

    private static Task<byte[]?> RawHandleAsync(object payload) =>
        V3McpJsonRpc.HandleAsync(
            Request(payload), new V3PlatformHost(), null, static () => ObservedAt, "mcp_test_ref", CancellationToken.None);

    private static async Task<JsonDocument> HandleJsonAsync(string payload)
    {
        var bytes = await V3McpJsonRpc.HandleAsync(
            Encoding.UTF8.GetBytes(payload), new V3PlatformHost(), null, static () => ObservedAt, "mcp_test_ref", CancellationToken.None);
        Assert.IsNotNull(bytes, "An invalid Request must receive an error response.");
        return JsonDocument.Parse(bytes);
    }

    private static JsonElement RestOutcomeBytesAsJson(V3CorpusMount? mount, string identifier, string requestReference)
    {
        var host = new V3PlatformHost();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var requestBytes = Request(new { operation_id = "resolve", parameters = new { identifier } });
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute = mount is null
            ? request => V3PlatformOperationOutcome.Refused(
                V3ApiHandler.UnmountedLuxembourgContext(static () => ObservedAt),
                V3ApiHandler.NoCorpusMounted(request))
            : request => mount.Resolve(request, ObservedAt);
        host.WriteRestOutcomeAsync(context.Response, requestBytes, requestReference, execute, CancellationToken.None)
            .GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        return document.RootElement.Clone();
    }

    [TestMethod]
    public async Task InitializeAnswersTheCurrentProtocolVersionAndDeclaresTheToolsCapability()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18" } });

        Assert.AreEqual("2.0", response.RootElement.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(1, response.RootElement.GetProperty("id").GetInt32());
        var result = response.RootElement.GetProperty("result");
        Assert.AreEqual(V3McpJsonRpc.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
        Assert.IsTrue(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.AreEqual(V3McpJsonRpc.ServerName, result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task ToolsListListsExactlyOneToolNamedResolve()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = "a", method = "tools/list" });

        var tools = response.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.HasCount(1, tools);
        Assert.AreEqual("resolve", tools[0].GetProperty("name").GetString());
        Assert.IsTrue(tools[0].TryGetProperty("description", out _));
        var inputSchema = tools[0].GetProperty("inputSchema");
        Assert.AreEqual("object", inputSchema.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            new[] { "identifier" },
            inputSchema.GetProperty("required").EnumerateArray().Select(static v => v.GetString()).ToArray());
    }

    /// <summary>The tool's declared input schema is exactly the reviewed request schema's own "parameters" shape, not a hand-drifted copy.</summary>
    [TestMethod]
    public async Task TheResolveToolsInputSchemaIsExactlyTheReviewedRequestSchemasParametersShape()
    {
        var reviewed = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot(), "schemas", "v3-platform", "resolve-request.schema.json")));
        var expectedParameters = reviewed.RootElement.GetProperty("properties").GetProperty("parameters");

        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        var inputSchema = response.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("inputSchema");

        Assert.AreEqual(
            JsonSerializer.Serialize(expectedParameters),
            JsonSerializer.Serialize(inputSchema),
            "The tool's inputSchema literal in V3McpJsonRpc.cs no longer matches resolve-request.schema.json's own parameters shape.");
    }

    [TestMethod]
    public async Task AToolsCallForResolveAnswersWithTheSameEnvelopeTheRestRouteWouldGiveForTheSameRequest()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        const string requestReference = "mcp_test_ref_resolve";

        using var response = await HandleAsync(
            new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "resolve", arguments = new { identifier = fixture.ExpressionIri } } },
            mount,
            requestReference);

        var result = response.RootElement.GetProperty("result");
        Assert.IsFalse(result.GetProperty("isError").GetBoolean());
        var structured = result.GetProperty("structuredContent");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.AreEqual(JsonSerializer.Serialize(structured), text, "content[0].text and structuredContent must carry the same envelope.");

        var restEnvelope = RestOutcomeBytesAsJson(mount, fixture.ExpressionIri, requestReference);
        Assert.AreEqual(
            JsonSerializer.Serialize(restEnvelope),
            JsonSerializer.Serialize(structured),
            "The MCP tool call answered a different envelope than the REST route would for the same operation, request and requestReference.");
        Assert.AreEqual(V3Verdicts.Answer, structured.GetProperty("verdict").GetString());
    }

    [TestMethod]
    public async Task AnUnknownIdentifierRefusesOverMcpExactlyAsRestDoes()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        const string requestReference = "mcp_test_ref_unknown";
        const string identifier = "eli/unknown-mcp-probe";

        using var response = await HandleAsync(
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "resolve", arguments = new { identifier } } },
            mount,
            requestReference);

        var structured = response.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.AreEqual(V3Verdicts.Refuse, structured.GetProperty("verdict").GetString());
        Assert.IsFalse(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(), "A reviewed refusal is not a tool execution error.");

        var restEnvelope = RestOutcomeBytesAsJson(mount, identifier, requestReference);
        Assert.AreEqual(JsonSerializer.Serialize(restEnvelope), JsonSerializer.Serialize(structured));
    }

    [TestMethod]
    public async Task AToolsCallWithNoCorpusMountedRefusesExactlyAsTheRestRouteDoesWithNoCorpusMounted()
    {
        const string requestReference = "mcp_test_ref_no_corpus";
        using var response = await HandleAsync(
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "resolve", arguments = new { identifier = "anything" } } },
            mount: null,
            requestReference);

        var structured = response.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.AreEqual(V3Verdicts.Refuse, structured.GetProperty("verdict").GetString());
        Assert.IsFalse(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        var restEnvelope = RestOutcomeBytesAsJson(null, "anything", requestReference);
        Assert.AreEqual(JsonSerializer.Serialize(restEnvelope), JsonSerializer.Serialize(structured));
    }

    [TestMethod]
    public async Task AToolsCallForAnyOtherNameIsAJsonRpcInvalidParamsErrorAndNothingRuns()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "search", arguments = new { } } });

        Assert.IsFalse(response.RootElement.TryGetProperty("result", out _));
        var error = response.RootElement.GetProperty("error");
        Assert.AreEqual(-32602, error.GetProperty("code").GetInt32());
        StringAssert.Contains(error.GetProperty("message").GetString(), "search");
    }

    [TestMethod]
    public async Task AToolNameIsMatchedExactlyAndACaseVariantIsUnknown()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "Resolve", arguments = new { identifier = "x" } } });

        Assert.IsFalse(response.RootElement.TryGetProperty("result", out _), "The tool name is not matched case-insensitively.");
        Assert.AreEqual(-32602, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task AToolsCallForResolveWithNoArgumentsPropertyAtAllIsAnInvalidParamsErrorNotACrashOrAFalseSuccess()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "resolve" } });

        Assert.IsFalse(response.RootElement.TryGetProperty("result", out _));
        var error = response.RootElement.GetProperty("error");
        Assert.AreEqual(-32602, error.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task AnUnknownMethodIsMethodNotFound()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 9, method = "resources/list" });

        Assert.AreEqual(-32601, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.AreEqual(9, response.RootElement.GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task AWrongJsonRpcVersionOrAMissingMethodIsInvalidRequest()
    {
        using var noVersion = await HandleAsync(new { jsonrpc = "1.0", id = 1, method = "initialize" });
        Assert.AreEqual(-32600, noVersion.RootElement.GetProperty("error").GetProperty("code").GetInt32());

        using var noMethod = await HandleAsync(new { jsonrpc = "2.0", id = 1 });
        Assert.AreEqual(-32600, noMethod.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    /// <summary>
    /// A JSON array is not a Request object, so it cannot be a Notification either -- §4.1 defines a
    /// Notification only for a Request object with no id member. It is simply Invalid Request, and it
    /// must be answered, with id: null since there is no object to read an id from. This is also the
    /// one branch TryGetProperty is not legal to call without checking ValueKind first; a reordering
    /// of the checks that puts the id probe ahead of the object check throws here instead of refusing.
    /// </summary>
    [TestMethod]
    public async Task AValidJsonDocumentThatIsNotAnObjectIsInvalidRequestNotACrashAndNotASuppressedNotification()
    {
        var response = await RawHandleAsync(new[] { 1, 2, 3 });
        Assert.IsNotNull(response, "not a Request object, so not a Notification -- it must be answered");
        using var document = JsonDocument.Parse(response);
        Assert.AreEqual(-32600, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("id").ValueKind);
    }

    [TestMethod]
    public async Task MalformedJsonIsAParseError()
    {
        // A document that does not parse as JSON at all: an id (if any) cannot be read either, so
        // JSON-RPC 2.0 §5's carve-out applies and a reply with id: null is correct here.
        var bytes = await V3McpJsonRpc.HandleAsync(
            "{not json"u8.ToArray(), new V3PlatformHost(), null, static () => ObservedAt, "ref", CancellationToken.None);
        Assert.IsNotNull(bytes, "A document that fails to parse at all is the one case that always gets a reply.");
        using var response = JsonDocument.Parse(bytes);

        Assert.AreEqual(-32700, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, response.RootElement.GetProperty("id").ValueKind);
    }

    /// <summary>
    /// JSON-RPC 2.0 §4.1: a Request object with no <c>id</c> member is a Notification, and the server
    /// MUST NOT reply to one -- not with a result, not with an error, not even with <c>id: null</c>.
    /// MCP's own lifecycle depends on this: <c>notifications/initialized</c>, sent right after a
    /// successful <c>initialize</c>, carries no id.
    /// </summary>
    [TestMethod]
    public async Task ARequestWithNoIdIsANotificationAndReceivesNoResponseAtAll()
    {
        var response = await RawHandleAsync(new { jsonrpc = "2.0", method = "tools/list" });
        Assert.IsNull(response, "A Notification (no id member) must not be answered, per JSON-RPC 2.0 §4.1.");
    }

    [TestMethod]
    public async Task ANotificationWithAnUnknownMethodOrInvalidParamsStillReceivesNoResponse()
    {
        Assert.IsNull(await RawHandleAsync(new { jsonrpc = "2.0", method = "notifications/initialized" }), "the exact MCP lifecycle notification");
        Assert.IsNull(await RawHandleAsync(new { jsonrpc = "2.0", method = "tools/call", @params = new { name = "search" } }), "a notification cannot report an invalid tool call either -- the client has no request to match the error to");
    }

    [TestMethod]
    public async Task AnObjectWithoutIdMustStillBeAValidRequestBeforeItIsSuppressedAsANotification()
    {
        foreach (var invalidRequest in new[]
                 {
                     """{"jsonrpc":"1.0","method":"tools/list"}""",
                     """{"jsonrpc":"2.0","method":1,"params":"bar"}""",
                     """{"jsonrpc":"2.0"}""",
                     """{"method":"tools/list"}""",
                 })
        {
            using var response = await HandleJsonAsync(invalidRequest);
            Assert.AreEqual(-32600, response.RootElement.GetProperty("error").GetProperty("code").GetInt32(), invalidRequest);
            Assert.AreEqual(JsonValueKind.Null, response.RootElement.GetProperty("id").ValueKind, invalidRequest);
        }
    }

    [TestMethod]
    public async Task ForbiddenIdKindsAreInvalidRequestsAndAreNeverEchoed()
    {
        foreach (var forbiddenId in new[] { "{}", "[]", "true", "false" })
        {
            using var response = await HandleJsonAsync($$"""{"jsonrpc":"2.0","id":{{forbiddenId}},"method":"tools/list"}""");
            Assert.AreEqual(-32600, response.RootElement.GetProperty("error").GetProperty("code").GetInt32(), forbiddenId);
            Assert.AreEqual(JsonValueKind.Null, response.RootElement.GetProperty("id").ValueKind, forbiddenId);
            Assert.IsFalse(response.RootElement.TryGetProperty("result", out _), forbiddenId);
        }
    }

    [TestMethod]
    public async Task AWellFormedRequestWithAnIdStillReceivesAResponseWhoseIdMatches()
    {
        using var response = await HandleAsync(new { jsonrpc = "2.0", id = 42, method = "tools/list" });
        Assert.AreEqual(42, response.RootElement.GetProperty("id").GetInt32());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
