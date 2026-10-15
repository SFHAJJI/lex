using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The server the one-server image runs, which ships no synthetic preview (plan 0.7): its readiness is whether it holds
/// the law, which the platform's readiness probe and monitoring ask, and the preview's routes are unknown to it, as any
/// path nothing claims is.
/// </summary>
[TestClass]
public sealed class V3ApiReadinessTests
{
    private static DefaultHttpContext Request(string method, string rawTarget)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static async Task<DefaultHttpContext> AskAsync(V3ApiHandler handler, string method, string rawTarget)
    {
        var context = Request(method, rawTarget);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static V3ApiHandler ReleaseServer(V3CorpusMount? mount) =>
        new(null, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, mount);

    [TestMethod]
    public async Task TheServerIsReadyWhileItHoldsTheLawAndNotOtherwise()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var method in new[] { "GET", "HEAD" })
        {
            var ready = await AskAsync(ReleaseServer(mount), method, "/health/ready");
            Assert.AreEqual(StatusCodes.Status204NoContent, ready.Response.StatusCode, $"{method}: the law is mounted");
            Assert.AreEqual(string.Empty, Body(ready));
            Assert.AreEqual("no-store", ready.Response.Headers.CacheControl.ToString());
            Assert.AreEqual("nosniff", ready.Response.Headers.XContentTypeOptions.ToString());

            var notReady = await AskAsync(ReleaseServer(null), method, "/health/ready");
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, notReady.Response.StatusCode, $"{method}: no law is mounted, so the server is not ready");
            Assert.AreEqual("no-store", notReady.Response.Headers.CacheControl.ToString());
            Assert.AreEqual("text/plain; charset=utf-8", notReady.Response.ContentType);
            Assert.AreEqual(method == "GET" ? "No law is mounted.\n" : string.Empty, Body(notReady), "a reason a person reads, and a status monitoring reads");
        }

        var post = await AskAsync(ReleaseServer(mount), "POST", "/health/ready");
        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, post.Response.StatusCode);
        Assert.AreEqual("GET, HEAD", post.Response.Headers.Allow.ToString());
        Assert.AreEqual(StatusCodes.Status404NotFound, (await AskAsync(ReleaseServer(mount), "GET", "/health/ready?x=1")).Response.StatusCode,
            "only the route itself, as the probe asks it");
    }

    [TestMethod]
    public async Task WithNoPreviewThePreviewsRoutesAndAnyPathNothingClaimsAreUnknownRoutes()
    {
        foreach (var target in new[] { "/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview", "/api/v3-preview/resolve", "/nothing-here" })
        {
            var context = await AskAsync(ReleaseServer(null), "GET", target);
            Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode, target);
            Assert.AreEqual("application/problem+json", context.Response.ContentType, target);
            var body = Body(context);
            using var problem = JsonDocument.Parse(body);
            Assert.AreEqual("unknown_route", problem.RootElement.GetProperty("code").GetString(), target);
            Assert.DoesNotContain("preview", body, target);
            Assert.DoesNotContain("synthetic", body, target);
        }

        // With the preview present, as in the tests' build and the S0-05 image, its routes are as they were.
        var preview = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, null);
        var ready = await AskAsync(preview, "GET", "/health/ready");
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ready.Response.StatusCode);
        StringAssert.Contains(Body(ready), "urn:lex:v3:preview:unavailable");
    }
}
