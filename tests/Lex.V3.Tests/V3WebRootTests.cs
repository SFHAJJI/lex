using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests;

/// <summary>
/// The live pages served by the API on its own origin (Decision 95, ruling 3): only the files the web
/// directory held when it was opened, each with the page's own reviewed Content-Security-Policy plus
/// <c>frame-ancestors 'none'</c>, HSTS, <c>Referrer-Policy: no-referrer</c>, <c>nosniff</c> and
/// <c>no-store</c>; every page under its interface language (R61), the root and a law's address with no
/// language sent to the reader's; every other path left to the API's routing.
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
        Directory.CreateDirectory(Path.Combine(directory, "en"));
        Directory.CreateDirectory(Path.Combine(directory, "fr"));
        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            indexHtml ?? $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"{PageCsp}\"></head><body>index</body></html>");
        File.WriteAllText(Path.Combine(directory, "fr", "index.html"), "<!doctype html><p>accueil</p>");
        File.WriteAllText(Path.Combine(directory, "en", "search.html"), "<!doctype html><p>search</p>");
        File.WriteAllText(Path.Combine(directory, "en", "client-live.js"), "void 0;");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        File.WriteAllBytes(Path.Combine(directory, "fonts", "inter-400-latin.woff2"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(directory, "evaluation-card.json"), "{\"schema\":\"lex-v3-evaluation-card/1\"}");
        return directory;
    }

    private static DefaultHttpContext Request(string method, string rawTarget, string? acceptLanguage = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        if (acceptLanguage is not null)
        {
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static JsonDocument SiteTable() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SitePaths", "site-paths.json")));

    private static void AssertPageHeaders(V3WebRoot root, HttpContext context, string target)
    {
        var headers = context.Response.Headers;
        Assert.AreEqual(root.ContentSecurityPolicy, headers.ContentSecurityPolicy.ToString(), target);
        Assert.AreEqual("max-age=31536000; includeSubDomains", headers.StrictTransportSecurity.ToString(), target);
        Assert.AreEqual("no-referrer", headers["Referrer-Policy"].ToString(), target);
        Assert.AreEqual("nosniff", headers.XContentTypeOptions.ToString(), target);
        Assert.AreEqual("no-store", headers.CacheControl.ToString(), target);
    }

    [TestMethod]
    public void NoDirectoryOrNoEnglishHomeIsNoWebRoot()
    {
        Assert.IsNull(V3WebRoot.Open(Path.Combine(Path.GetTempPath(), $"lex-v3-web-absent-{Guid.NewGuid():N}")));
        var empty = Path.Combine(Path.GetTempPath(), $"lex-v3-web-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "index.html"), "a root page is no longer the home page");
        try
        {
            Assert.IsNull(V3WebRoot.Open(empty), "without en/index.html there is no web root");
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
                new[]
                {
                    "/en/", "/en/index.html", "/fr/", "/fr/index.html", "/en/search.html", "/en/client-live.js", "/styles.css",
                    "/fonts/inter-400-latin.woff2", "/evaluation-card.json",
                },
                root.Paths.ToArray());

            foreach (var (target, type, body) in new[]
            {
                ("/en/", "text/html; charset=utf-8", "index"),
                ("/fr/", "text/html; charset=utf-8", "accueil"),
                ("/en/search.html", "text/html; charset=utf-8", "search"),
                ("/en/client-live.js", "text/javascript; charset=utf-8", "void 0;"),
                ("/styles.css", "text/css; charset=utf-8", "body{}"),
                ("/evaluation-card.json", "application/json; charset=utf-8", "lex-v3-evaluation-card/1"),
                ("/en/search.html?query=x", "text/html; charset=utf-8", "search"),
            })
            {
                var context = Request("GET", target);
                Assert.IsTrue(await root.TryServeAsync(context, target, CancellationToken.None), target);
                Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, target);
                Assert.AreEqual(type, context.Response.ContentType, target);
                StringAssert.Contains(Body(context), body, target);
                AssertPageHeaders(root, context, target);
            }

            var font = Request("GET", "/fonts/inter-400-latin.woff2");
            Assert.IsTrue(await root.TryServeAsync(font, "/fonts/inter-400-latin.woff2", CancellationToken.None));
            Assert.AreEqual("font/woff2", font.Response.ContentType);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ((MemoryStream)font.Response.Body).ToArray());

            var head = Request("HEAD", "/en/search.html");
            Assert.IsTrue(await root.TryServeAsync(head, "/en/search.html", CancellationToken.None));
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
    public async Task TheRootSendsAReaderToTheLanguageTheBrowserPrefersElseFrench()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            foreach (var (target, acceptLanguage, location) in new[]
            {
                ("/", (string?)null, "/fr/"),
                ("/", "en-GB,en;q=0.9", "/en/"),
                ("/", "de-DE,de;q=0.9,en;q=0.5,fr;q=0.4", "/en/"),
                ("/index.html", "fr-LU", "/fr/"),
                ("/en", null, "/en/"),
                ("/lu-legilux/loi-1991-08-10-n3?from=search", "en", "/en/lu-legilux/loi-1991-08-10-n3?from=search"),
            })
            {
                var context = Request("GET", target, acceptLanguage);
                Assert.IsTrue(await root.TryServeAsync(context, target, CancellationToken.None), target);
                Assert.AreEqual(StatusCodes.Status302Found, context.Response.StatusCode, target);
                Assert.AreEqual(location, context.Response.Headers.Location.ToString(), $"{target} for {acceptLanguage}");
                Assert.AreEqual("Accept-Language", context.Response.Headers.Vary.ToString(), $"{target}: a cache must not hand one reader's language to another");
                Assert.AreEqual("", Body(context), target);
                AssertPageHeaders(root, context, target);
            }

            var post = Request("POST", "/");
            Assert.IsTrue(await root.TryServeAsync(post, "/", CancellationToken.None));
            Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, post.Response.StatusCode, "the root is a page, read with GET or HEAD");
            Assert.AreEqual(0, post.Response.Headers.Location.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TheLanguageAReaderStartsInIsTheSitesOwnTables()
    {
        using var table = SiteTable();
        var rows = table.RootElement.GetProperty("languages").EnumerateArray().ToArray();
        Assert.IsGreaterThanOrEqualTo(10, rows.Length, "the table web/scripts/permalinks.mjs is held to");
        foreach (var row in rows)
        {
            var accept = row.GetProperty("accept").GetString();
            Assert.AreEqual(row.GetProperty("language").GetString(), V3WebRoot.NegotiateLanguage(accept), accept);
        }

        Assert.AreEqual("fr", V3WebRoot.NegotiateLanguage(null), "French when the browser names no language");
    }

    [TestMethod]
    public async Task APathTheRootDoesNotHoldIsNotClaimedAndAnotherMethodIsRefused()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            File.WriteAllText(Path.Combine(directory, "late.html"), "added after the root was opened");
            foreach (var target in new[]
            {
                "/late.html", "/../en/index.html", "/fonts/../en/index.html", "/%2e%2e/en/index.html", "/EN/", "/api/v3/coverage", "/health/ready",
                "/en/search.html/", "//en/", "/search.html", "/de/", "/en/index",
            })
            {
                var context = Request("GET", target);
                Assert.IsFalse(await root.TryServeAsync(context, target, CancellationToken.None), $"{target} is not a file the root recorded");
                Assert.AreEqual(0, context.Response.Headers.Count, $"{target}: the response is left untouched for the routing that follows");
            }

            var post = Request("POST", "/en/search.html");
            Assert.IsTrue(await root.TryServeAsync(post, "/en/search.html", CancellationToken.None));
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
    public async Task ALawsAddressIsServedThePageThatReadsItOrSentToTheReadersLanguageAsTheSitesOwnTableSays()
    {
        var directory = NewWebDirectory();
        foreach (var page in new[] { "en/reading.html", "en/dossier.html", "fr/reading.html", "fr/dossier.html" })
        {
            File.WriteAllText(Path.Combine(directory, page), $"<!doctype html><p>{page}</p>");
        }

        try
        {
            var root = V3WebRoot.Open(directory)!;
            using var table = SiteTable();
            var rows = table.RootElement.GetProperty("paths").EnumerateArray().ToArray();
            Assert.IsGreaterThan(40, rows.Length, "the table web/scripts/permalinks.mjs is held to");
            foreach (var row in rows)
            {
                var path = row.GetProperty("path").GetString()!;
                var page = row.GetProperty("page").ValueKind == JsonValueKind.Null ? null : row.GetProperty("page").GetString();
                var redirect = row.GetProperty("redirect").ValueKind == JsonValueKind.Null ? null : row.GetProperty("redirect").GetString();
                Assert.AreEqual(page is null ? null : "/" + page, V3WebRoot.PageForSitePath(path), path);
                Assert.AreEqual(redirect, V3WebRoot.RedirectFor(path, string.Empty, null)?.Location, path);

                var context = Request("GET", path);
                var served = await root.TryServeAsync(context, path, CancellationToken.None);
                if (page is not null)
                {
                    Assert.IsTrue(served, path);
                    Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, path);
                    Assert.AreEqual("text/html; charset=utf-8", context.Response.ContentType, path);
                    Assert.AreEqual($"<!doctype html><p>{page}</p>", Body(context), path);
                    AssertPageHeaders(root, context, path);
                }
                else if (redirect is not null)
                {
                    Assert.IsTrue(served, path);
                    Assert.AreEqual(StatusCodes.Status302Found, context.Response.StatusCode, path);
                    Assert.AreEqual(redirect, context.Response.Headers.Location.ToString(), path);
                }
                else
                {
                    Assert.AreEqual(root.Paths.Contains(path), served, $"{path}: neither served nor sent, so served only as a file the root recorded");
                }
            }

            var post = Request("POST", "/en/lu-legilux/loi-2017-03-14-a439");
            Assert.IsTrue(await root.TryServeAsync(post, "/en/lu-legilux/loi-2017-03-14-a439", CancellationToken.None));
            Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, post.Response.StatusCode, "a law's address is a page, read with GET or HEAD");

            var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, null, root);
            var permalink = Request("GET", $"/fr/eu-eurlex/32016R0679/fra/2016-05-04--{new string('a', 64)}");
            await handler.HandleAsync(permalink, CancellationToken.None);
            Assert.AreEqual(StatusCodes.Status200OK, permalink.Response.StatusCode);
            Assert.AreEqual("<!doctype html><p>fr/reading.html</p>", Body(permalink), "the API serves a permalink the reading page beside its routes");
            var minted = Request("GET", $"/eu-eurlex/32016R0679/fra/2016-05-04--{new string('a', 64)}", "en-US,en;q=0.9");
            await handler.HandleAsync(minted, CancellationToken.None);
            Assert.AreEqual(StatusCodes.Status302Found, minted.Response.StatusCode);
            Assert.AreEqual($"/en/eu-eurlex/32016R0679/fra/2016-05-04--{new string('a', 64)}", minted.Response.Headers.Location.ToString(),
                "a permalink as the API mints it goes to the reader's language");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ALawsAddressIsNotClaimedByARootWithoutThePageThatReadsIt()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            foreach (var target in new[] { "/en/lu-legilux/loi-2017-03-14-a439", $"/en/eu-eurlex/32016R0679/fra/2016-05-04--{new string('a', 64)}", "/fr/eu-eurlex/32016R0679" })
            {
                var context = Request("GET", target);
                Assert.IsFalse(await root.TryServeAsync(context, target, CancellationToken.None), $"{target}: this root holds no reading or dossier page");
                Assert.AreEqual(0, context.Response.Headers.Count, target);
            }
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
            var start = Request("GET", "/");
            await handler.HandleAsync(start, CancellationToken.None);
            Assert.AreEqual(StatusCodes.Status302Found, start.Response.StatusCode, "the root sends a reader to a language");
            Assert.AreEqual("/fr/", start.Response.Headers.Location.ToString());

            var page = Request("GET", "/en/");
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
