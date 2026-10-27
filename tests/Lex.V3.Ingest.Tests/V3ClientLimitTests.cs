using System.Net;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The per-client limit over a mounted corpus (R109): the refusal <c>rate_limited</c> is an envelope of the mount like any of
/// its refusals (its snapshot, freshness and commits), the same over REST (429, <c>Retry-After</c>) and MCP, and the mount is
/// not asked. Without a mount, and the limit itself, are held in Lex.V3.Tests (<c>V3HostAnswerTests</c>,
/// <c>V3ClientLimiterTests</c>); every operation's sample is the refusal census's.
/// </summary>
[TestClass]
public sealed class V3ClientLimitTests
{
    private sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ObservedAt;
    }

    private static async Task<DefaultHttpContext> PostAsync(V3ApiHandler handler, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext { TraceIdentifier = "client-limit" };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.77");
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    [TestMethod]
    public async Task OverAMountTheRefusalIsTheMountsOwnEnvelopeOverRestAndMcpAlike()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount, null,
            new V3ClientLimiter(new StoppedClock(), static () => new byte[32]));
        var work = $"/lu-legilux/{fixture.WorkKey}";
        var body = JsonSerializer.Serialize(new { operation_id = "dossier", parameters = new { identifier = work } });

        DefaultHttpContext? answered = null;
        for (var index = 0; index < V3ClientLimiter.ApiPerMinute; index++)
        {
            answered = await PostAsync(handler, "/api/v3/dossier", body);
            Assert.AreEqual(StatusCodes.Status200OK, answered.Response.StatusCode, $"request {index + 1}");
        }

        var refused = await PostAsync(handler, "/api/v3/dossier", body);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, refused.Response.StatusCode);
        Assert.AreEqual("1", refused.Response.Headers.RetryAfter.ToString());
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(refused), V3OperationRegistry.Reviewed);
        Assert.AreEqual("rate_limited", envelope.Refusal!.Code);

        using var answer = JsonDocument.Parse(ResponseBytes(answered!));
        using var refusal = JsonDocument.Parse(ResponseBytes(refused));
        var asked = answer.RootElement.GetProperty("context");
        var limited = refusal.RootElement.GetProperty("context");
        Assert.AreEqual("refusal", limited.GetProperty("status").GetString());
        foreach (var member in new[] { "snapshot", "freshness", "commits", "publisher", "jurisdiction", "timeline_semantics" })
        {
            Assert.IsTrue(JsonElement.DeepEquals(asked.GetProperty(member), limited.GetProperty(member)), $"the refusal names the mount's {member}, as its answer does");
        }

        Assert.IsTrue(limited.GetProperty("snapshot").GetProperty("snapshot_id").GetString()!.StartsWith("corpus-", StringComparison.Ordinal), "the mounted corpus, not the unmounted refusal's");

        var call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "dossier", arguments = new { identifier = work } } });
        var mcp = await PostAsync(handler, "/mcp", call);
        Assert.AreEqual(StatusCodes.Status200OK, mcp.Response.StatusCode, "a tool result keeps the dispatcher's status");
        Assert.AreEqual("1", mcp.Response.Headers.RetryAfter.ToString());
        using var tool = JsonDocument.Parse(ResponseBytes(mcp));
        Assert.IsTrue(JsonElement.DeepEquals(refusal.RootElement, tool.RootElement.GetProperty("result").GetProperty("structuredContent")),
            "REST and MCP answer one envelope (the launch contract)");
    }
}
