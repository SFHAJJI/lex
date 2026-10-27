using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests;

/// <summary>
/// The site's 404 page, served by the API (the specification of record's R60: "A 404 page offers search"; plan item 6.9):
/// a page request nothing claims (a GET or HEAD whose path is not under <c>/api/</c>, <c>/mcp</c> or <c>/health/</c>) is
/// answered with status 404 and the built 404 page in the reader's language, with every header a page carries; a machine's
/// path, and any other method, keeps the platform's JSON problem. The choice is the site's own table, which the live server
/// is held to as well (<c>web/test/live-not-found.test.mjs</c>).
/// </summary>
[TestClass]
public sealed class V3WebRootNotFoundTests
{
    private const string PageCsp = "default-src &#x27;none&#x27;; script-src &#x27;self&#x27;; connect-src &#x27;self&#x27;";

    private static readonly IReadOnlyDictionary<string, string> NotFoundBodies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["not-found/en.html"] = "<!doctype html><p>There is no page at this address</p>",
        ["not-found/fr.html"] = "<!doctype html><p>Il n’y a aucune page à cette adresse</p>",
    };

    private static string NewWebDirectory(bool withNotFoundPages = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-web-404-{Guid.NewGuid():N}");
        foreach (var folder in new[] { "en", "fr", "not-found", "maintenance" })
        {
            Directory.CreateDirectory(Path.Combine(directory, folder));
        }

        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"{PageCsp}\"></head><body>index</body></html>");
        File.WriteAllText(Path.Combine(directory, "fr", "index.html"), "<!doctype html><p>accueil</p>");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        if (withNotFoundPages)
        {
            foreach (var (page, body) in NotFoundBodies)
            {
                File.WriteAllText(Path.Combine(directory, page), body, new UTF8Encoding(false));
            }
        }

        File.WriteAllText(Path.Combine(directory, "maintenance", "en.html"), "<!doctype html><p>Lex is being updated</p>");
        File.WriteAllText(Path.Combine(directory, "maintenance", "fr.html"), "<!doctype html><p>Lex est en cours de mise à jour</p>");
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

    private static JsonElement[] Table()
    {
        using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SitePaths", "not-found.json")));
        return table.RootElement.GetProperty("requests").EnumerateArray().Select(static row => row.Clone()).ToArray();
    }

    private static string? Text(JsonElement row, string name) =>
        row.GetProperty(name).ValueKind == JsonValueKind.Null ? null : row.GetProperty(name).GetString();

    private static V3ApiHandler ReleaseServer(V3WebRoot root) =>
        new(null, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, null, root);

    private static async Task<DefaultHttpContext> AskAsync(V3ApiHandler handler, string method, string rawTarget, string? acceptLanguage = null)
    {
        var context = Request(method, rawTarget, acceptLanguage);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

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
    public void WhichRequestIsAPagesAndInWhichLanguageItIsAnsweredIsTheSitesOwnTable()
    {
        var rows = Table();
        Assert.IsGreaterThanOrEqualTo(20, rows.Length, "the table web/scripts/not-found.mjs is held to");
        Assert.IsGreaterThanOrEqualTo(8, rows.Count(static row => Text(row, "page") is null), "machines' paths and other methods are in it");
        foreach (var row in rows)
        {
            var method = Text(row, "method")!;
            var path = Text(row, "path")!;
            var page = Text(row, "page");
            var chosen = V3WebRoot.NotFoundPageFor(method, path, Text(row, "acceptLanguage"));
            Assert.AreEqual(page, chosen?.Page, $"{method} {path}");
            Assert.AreEqual(page is not null, V3WebRoot.IsPageRequest(method, path), $"{method} {path}");
            if (page is not null)
            {
                Assert.AreEqual(row.GetProperty("negotiated").GetBoolean(), chosen!.Value.Negotiated, $"{method} {path}");
            }
        }
    }

    [TestMethod]
    public async Task TheBuildsUnnamedPagesAreNeverServedAtTheirOwnAddress()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            CollectionAssert.AreEquivalent(new[] { "not-found/en.html", "not-found/fr.html" }, root.NotFoundPages.ToArray());
            Assert.IsFalse(root.Paths.Any(static path => path.StartsWith("/not-found/", StringComparison.Ordinal) || path.StartsWith("/maintenance/", StringComparison.Ordinal)),
                string.Join(", ", root.Paths));
            foreach (var target in new[] { "/not-found/en.html", "/not-found/fr.html", "/maintenance/en.html", "/maintenance/fr.html" })
            {
                var context = Request("GET", target);
                Assert.IsFalse(await root.TryServeAsync(context, target, CancellationToken.None), $"{target} is no file the root serves");

                var asked = await AskAsync(ReleaseServer(root), "GET", target, "en");
                Assert.AreEqual(StatusCodes.Status404NotFound, asked.Response.StatusCode, $"{target}: a 404, never the page itself");
                Assert.AreEqual(NotFoundBodies["not-found/en.html"], Body(asked), target);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheApiAnswersEachPageRequestNothingClaimsWithThe404PageAndEachMachinesPathWithItsProblem()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            foreach (var row in Table())
            {
                var method = Text(row, "method")!;
                var path = Text(row, "path")!;
                var page = Text(row, "page");
                var label = $"{method} {path}";
                var context = await AskAsync(ReleaseServer(root), method, path, Text(row, "acceptLanguage"));
                if (page is null)
                {
                    Assert.AreEqual("application/problem+json", context.Response.ContentType, $"{label}: the platform's problem, not a page");
                    using var problem = JsonDocument.Parse(Body(context));
                    Assert.AreEqual("unknown_route", problem.RootElement.GetProperty("code").GetString(), label);
                    // An API answer is not a page: it carries the host's policy for answers that are none, never a page's (V3HostHeaders).
                    Assert.AreEqual(V3HostHeaders.MachineContentSecurityPolicy, context.Response.Headers.ContentSecurityPolicy.ToString(), $"{label}: an API answer is not a page");
                    continue;
                }

                Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode, label);
                Assert.AreEqual("text/html; charset=utf-8", context.Response.ContentType, label);
                Assert.AreEqual(Encoding.UTF8.GetByteCount(NotFoundBodies[page]), context.Response.ContentLength, label);
                Assert.AreEqual(method == "HEAD" ? string.Empty : NotFoundBodies[page], Body(context), label);
                AssertPageHeaders(root, context, label);
                Assert.AreEqual(row.GetProperty("negotiated").GetBoolean() ? "Accept-Language" : string.Empty, context.Response.Headers.Vary.ToString(),
                    $"{label}: the answer depends on Accept-Language exactly when the path names no language");
            }

            // The query is no part of the path, and the page never repeats the address asked.
            var queried = await AskAsync(ReleaseServer(root), "GET", "/fr/no-such-page?q=%3Cscript%3E");
            Assert.AreEqual(StatusCodes.Status404NotFound, queried.Response.StatusCode);
            Assert.AreEqual(NotFoundBodies["not-found/fr.html"], Body(queried));

            // A page the root holds, a redirect and a law's address are still theirs.
            Assert.AreEqual(StatusCodes.Status200OK, (await AskAsync(ReleaseServer(root), "GET", "/fr/")).Response.StatusCode);
            Assert.AreEqual(StatusCodes.Status302Found, (await AskAsync(ReleaseServer(root), "GET", "/", "en")).Response.StatusCode);
            Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, (await AskAsync(ReleaseServer(root), "POST", "/fr/")).Response.StatusCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ThePreviewsRoutesStayThePreviewsAndAPageRequestBesideThemIsThe404Page()
    {
        var directory = NewWebDirectory();
        try
        {
            var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, null, V3WebRoot.Open(directory));
            var page = await AskAsync(handler, "GET", "/en/no-such-page");
            Assert.AreEqual(StatusCodes.Status404NotFound, page.Response.StatusCode);
            Assert.AreEqual(NotFoundBodies["not-found/en.html"], Body(page));

            var ready = await AskAsync(handler, "GET", "/health/ready");
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ready.Response.StatusCode, "the preview's readiness, as before");
            StringAssert.Contains(Body(ready), "urn:lex:v3:preview:unavailable");
            var resolve = await AskAsync(handler, "GET", "/api/v3-preview/resolve");
            Assert.AreNotEqual("text/html; charset=utf-8", resolve.Response.ContentType, "the preview's route is a machine's");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task AWebRootWithNo404PagesLeavesEveryPathNothingClaimsToTheUnknownRoute()
    {
        var directory = NewWebDirectory(withNotFoundPages: false);
        try
        {
            var root = V3WebRoot.Open(directory)!;
            Assert.IsEmpty(root.NotFoundPages);
            var context = await AskAsync(ReleaseServer(root), "GET", "/fr/no-such-page");
            Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.AreEqual("application/problem+json", context.Response.ContentType);
            using var problem = JsonDocument.Parse(Body(context));
            Assert.AreEqual("unknown_route", problem.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
