using System.Text;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests;

/// <summary>
/// The live pages served by the API on its own origin (Decision 95, ruling 3): only the files the web
/// directory held when it was opened, each with the page's own reviewed Content-Security-Policy plus
/// <c>frame-ancestors 'none'</c>, HSTS, <c>Referrer-Policy: no-referrer</c>, <c>nosniff</c> and
/// <c>no-store</c>; every other path left to the API's routing.
/// </summary>
[TestClass]
public sealed class V3WebRootTests
{
    private const string PageCsp = "default-src &#x27;none&#x27;; script-src &#x27;self&#x27;; connect-src &#x27;self&#x27;";
    private const string DecodedCsp = "default-src 'none'; script-src 'self'; connect-src 'self'";

    private static string NewWebDirectory(string? indexHtml = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "fonts"));
        File.WriteAllText(Path.Combine(directory, "index.html"),
            indexHtml ?? $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"{PageCsp}\"></head><body>index</body></html>");
        File.WriteAllText(Path.Combine(directory, "search.html"), "<!doctype html><p>search</p>");
        File.WriteAllText(Path.Combine(directory, "client-live.js"), "void 0;");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        File.WriteAllBytes(Path.Combine(directory, "fonts", "inter-400-latin.woff2"), [1, 2, 3]);
        return directory;
    }

    private static DefaultHttpContext Request(string method, string rawTarget)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    [TestMethod]
    public void NoDirectoryOrNoIndexIsNoWebRoot()
    {
        Assert.IsNull(V3WebRoot.Open(Path.Combine(Path.GetTempPath(), $"lex-v3-web-absent-{Guid.NewGuid():N}")));
        var empty = Path.Combine(Path.GetTempPath(), $"lex-v3-web-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            Assert.IsNull(V3WebRoot.Open(empty));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [TestMethod]
    public async Task APageIsServedWithItsOwnPolicyFrameAncestorsHstsNoReferrerNosniffAndNoStore()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            Assert.AreEqual($"{DecodedCsp}; frame-ancestors 'none'", root.ContentSecurityPolicy, "the page's policy, entities decoded, plus the header-only directive");
            CollectionAssert.AreEquivalent(
                new[] { "/", "/index.html", "/search.html", "/client-live.js", "/styles.css", "/fonts/inter-400-latin.woff2" },
                root.Paths.ToArray());

            foreach (var (target, type, body) in new[]
            {
                ("/", "text/html; charset=utf-8", "index"),
                ("/search.html", "text/html; charset=utf-8", "search"),
                ("/client-live.js", "text/javascript; charset=utf-8", "void 0;"),
                ("/styles.css", "text/css; charset=utf-8", "body{}"),
                ("/search.html?query=x", "text/html; charset=utf-8", "search"),
            })
            {
                var context = Request("GET", target);
                Assert.IsTrue(await root.TryServeAsync(context, target, CancellationToken.None), target);
                Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, target);
                Assert.AreEqual(type, context.Response.ContentType, target);
                StringAssert.Contains(Body(context), body, target);
                var headers = context.Response.Headers;
                Assert.AreEqual(root.ContentSecurityPolicy, headers.ContentSecurityPolicy.ToString(), target);
                Assert.AreEqual("max-age=31536000; includeSubDomains", headers.StrictTransportSecurity.ToString(), target);
                Assert.AreEqual("no-referrer", headers["Referrer-Policy"].ToString(), target);
                Assert.AreEqual("nosniff", headers.XContentTypeOptions.ToString(), target);
                Assert.AreEqual("no-store", headers.CacheControl.ToString(), target);
            }

            var font = Request("GET", "/fonts/inter-400-latin.woff2");
            Assert.IsTrue(await root.TryServeAsync(font, "/fonts/inter-400-latin.woff2", CancellationToken.None));
            Assert.AreEqual("font/woff2", font.Response.ContentType);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ((MemoryStream)font.Response.Body).ToArray());

            var head = Request("HEAD", "/search.html");
            Assert.IsTrue(await root.TryServeAsync(head, "/search.html", CancellationToken.None));
            Assert.AreEqual(StatusCodes.Status200OK, head.Response.StatusCode);
            Assert.AreEqual("", Body(head), "HEAD carries the headers and no body");
            Assert.IsGreaterThan(0L, head.Response.ContentLength!.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task APathTheRootDoesNotHoldIsNotClaimedAndAnotherMethodIsRefused()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            File.WriteAllText(Path.Combine(directory, "late.html"), "added after the root was opened");
            foreach (var target in new[] { "/late.html", "/../index.html", "/fonts/../index.html", "/%2e%2e/index.html", "/INDEX.HTML", "/api/v3/coverage", "/health/ready", "/index.html/", "//index.html" })
            {
                var context = Request("GET", target);
                Assert.IsFalse(await root.TryServeAsync(context, target, CancellationToken.None), $"{target} is not a file the root recorded");
                Assert.AreEqual(0, context.Response.Headers.Count, $"{target}: the response is left untouched for the routing that follows");
            }

            var post = Request("POST", "/search.html");
            Assert.IsTrue(await root.TryServeAsync(post, "/search.html", CancellationToken.None));
            Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, post.Response.StatusCode);
            Assert.AreEqual("GET, HEAD", post.Response.Headers.Allow.ToString());
            Assert.AreEqual("", Body(post));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ADirectoryWithAPageWithoutItsPolicyOrAFileOfAnotherTypeIsRefused()
    {
        var unguarded = NewWebDirectory("<!doctype html><p>no policy</p>");
        var typed = NewWebDirectory();
        var named = NewWebDirectory();
        File.WriteAllText(Path.Combine(typed, "notes.txt"), "not a type this server serves");
        File.WriteAllText(Path.Combine(named, "Upper.html"), "a name outside the served pattern");
        try
        {
            StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => V3WebRoot.Open(unguarded)).Message, "no Content-Security-Policy");
            StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => V3WebRoot.Open(typed)).Message, "of a type this server does not serve");
            StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => V3WebRoot.Open(named)).Message, "a path this server does not serve");
        }
        finally
        {
            foreach (var directory in new[] { unguarded, typed, named }) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheApiServesThePagesBesideItsRoutesAndLeavesItsRoutesAsTheyWere()
    {
        var directory = NewWebDirectory();
        try
        {
            var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, null, V3WebRoot.Open(directory));
            var page = Request("GET", "/");
            await handler.HandleAsync(page, CancellationToken.None);
            Assert.AreEqual(StatusCodes.Status200OK, page.Response.StatusCode);
            StringAssert.Contains(Body(page), "index");
            StringAssert.Contains(page.Response.Headers.ContentSecurityPolicy.ToString(), "frame-ancestors 'none'");

            var api = Request("POST", "/api/v3/coverage");
            var payload = Encoding.UTF8.GetBytes("{\"operation_id\":\"coverage\",\"parameters\":{}}");
            api.Request.Body = new MemoryStream(payload);
            api.Request.ContentLength = payload.Length;
            api.Request.ContentType = "application/json";
            await handler.HandleAsync(api, CancellationToken.None);
            Assert.AreEqual(StatusCodes.Status200OK, api.Response.StatusCode, "the API route is the API's");
            StringAssert.Contains(Body(api), "no_corpus_mounted");
            Assert.AreEqual(0, api.Response.Headers.ContentSecurityPolicy.Count, "an API answer is not a page");

            var elsewhere = Request("GET", "/nothing-here.html");
            await handler.HandleAsync(elsewhere, CancellationToken.None);
            Assert.AreNotEqual(StatusCodes.Status200OK, elsewhere.Response.StatusCode, "a page the root does not hold is not served");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
