using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests;

/// <summary>
/// The release's maintenance revision (the specification of record's R60: "a maintenance page shows during deployments";
/// R108, R115; the plan's item 10.14): with <c>LEX_V3_MAINTENANCE</c> exactly <c>on</c>, every page request is answered 503
/// with <c>Retry-After</c>, every header a page carries, and the built maintenance page in the reader's language, chosen as
/// the 404 page is; readiness is 204 only with a maintenance page in each interface language; every other request is the
/// transport problem <c>service_in_maintenance</c>. Off, the server answers as before, and the maintenance pages are never
/// served (<see cref="V3WebRootNotFoundTests"/>).
/// </summary>
[TestClass]
public sealed class V3WebRootMaintenanceTests
{
    private const string PageCsp = "default-src &#x27;none&#x27;; script-src &#x27;self&#x27;; connect-src &#x27;self&#x27;";

    private static readonly IReadOnlyDictionary<string, string> MaintenanceBodies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["maintenance/en.html"] = "<!doctype html><p>Lex is being updated</p><a href=\"https://legilux.public.lu/\">Legilux</a>",
        ["maintenance/fr.html"] = "<!doctype html><p>Lex est en cours de mise à jour</p><a href=\"https://eur-lex.europa.eu/\">EUR-Lex</a>",
    };

    private const string NotFoundEnglish = "<!doctype html><p>There is no page at this address</p>";

    /// <summary>A web root as the build writes it, with the maintenance pages of <paramref name="languages"/> (both when null).</summary>
    private static string NewWebDirectory(string[]? languages = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-web-maintenance-{Guid.NewGuid():N}");
        foreach (var folder in new[] { "en", "fr", "not-found", "maintenance" })
        {
            Directory.CreateDirectory(Path.Combine(directory, folder));
        }

        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"{PageCsp}\"></head><body>index</body></html>");
        File.WriteAllText(Path.Combine(directory, "fr", "index.html"), "<!doctype html><p>accueil</p>");
        File.WriteAllText(Path.Combine(directory, "en", "search.html"), "<!doctype html><p>search</p>");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        File.WriteAllText(Path.Combine(directory, "not-found", "en.html"), NotFoundEnglish, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "not-found", "fr.html"), "<!doctype html><p>Il n’y a aucune page à cette adresse</p>", new UTF8Encoding(false));
        foreach (var language in languages ?? V3WebRoot.InterfaceLanguages)
        {
            var page = $"maintenance/{language}.html";
            File.WriteAllText(Path.Combine(directory, page), MaintenanceBodies[page], new UTF8Encoding(false));
        }

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

    private static byte[] Bytes(HttpContext context) => ((MemoryStream)context.Response.Body).ToArray();

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(Bytes(context));

    private static async Task<DefaultHttpContext> InMaintenanceAsync(V3WebRoot? root, string method, string rawTarget, string? acceptLanguage = null)
    {
        var context = Request(method, rawTarget, acceptLanguage);
        await V3WebRoot.HandleMaintenanceAsync(root, context, CancellationToken.None);
        return context;
    }

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
        Assert.AreEqual("noindex", headers["X-Robots-Tag"].ToString(), target);
        Assert.AreEqual("300", headers.RetryAfter.ToString(), target);
    }

    /// <summary>The transport problem every request but a page's and readiness's gets in maintenance.</summary>
    private static void AssertInMaintenance(HttpContext context, string target)
    {
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode, target);
        Assert.AreEqual("application/problem+json", context.Response.ContentType, target);
        Assert.AreEqual("300", context.Response.Headers.RetryAfter.ToString(), target);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString(), target);
        Assert.AreEqual("nosniff", context.Response.Headers.XContentTypeOptions.ToString(), target);
        Assert.AreEqual(0, context.Response.Headers.ContentSecurityPolicy.Count, $"{target}: a machine's answer, not a page");
        using var problem = JsonDocument.Parse(Body(context));
        var root = problem.RootElement;
        Assert.AreEqual(V3TransportResponse.Schema, root.GetProperty("schema").GetString(), target);
        Assert.AreEqual("urn:lex:v3:transport:service_in_maintenance", root.GetProperty("type").GetString(), target);
        Assert.AreEqual("Service in maintenance", root.GetProperty("title").GetString(), target);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, root.GetProperty("status").GetInt32(), target);
        Assert.AreEqual("service_in_maintenance", root.GetProperty("code").GetString(), target);
    }

    [TestMethod]
    public void TheSwitchIsTheReleasesOneVariableOnOnlyWhenItSaysOnExactly()
    {
        // The variable's name is held to the template's (web/test/deploy-kit.test.mjs).
        foreach (var (value, isOn) in new (string? Value, bool IsOn)[]
                 {
                     ("on", true), (null, false), (string.Empty, false), ("On", false), ("ON", false), (" on", false), ("on ", false),
                     ("on\n", false), ("1", false), ("true", false), ("yes", false), ("off", false), ("maintenance", false),
                 })
        {
            Assert.AreEqual(isOn, V3WebRoot.MaintenanceState(value), value ?? "(none)");
        }
    }

    [TestMethod]
    public async Task APageRequestIsAnsweredWithTheMaintenancePageInItsLanguageWith503RetryAfterAndEveryHeaderAPageCarries()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            CollectionAssert.AreEquivalent(new[] { "maintenance/en.html", "maintenance/fr.html" }, root.MaintenancePages.ToArray());
            Assert.IsTrue(root.HoldsMaintenancePages);
            foreach (var (method, target, acceptLanguage, page, negotiated) in new (string Method, string Target, string? AcceptLanguage, string Page, bool Negotiated)[]
                     {
                         ("GET", "/fr/", null, "maintenance/fr.html", false),
                         ("GET", "/en/", null, "maintenance/en.html", false),
                         ("GET", "/en/search.html", "fr", "maintenance/en.html", false),
                         ("GET", "/fr/no-such-page?q=%3Cscript%3E", null, "maintenance/fr.html", false),
                         ("GET", "/en/lu-legilux/loi-2017-12-21-a1117", null, "maintenance/en.html", false),
                         ("GET", "/", "en", "maintenance/en.html", true),
                         ("GET", "/", null, "maintenance/fr.html", true),
                         ("GET", "/", "de, en;q=0.5", "maintenance/en.html", true),
                         ("GET", "/lu-legilux/loi-2017-12-21-a1117", "fr-LU", "maintenance/fr.html", true),
                         ("GET", "/robots.txt", "en", "maintenance/en.html", true),
                         ("GET", "/maintenance/fr.html", "en", "maintenance/en.html", true),
                         ("HEAD", "/en/", null, "maintenance/en.html", false),
                         ("HEAD", "/", "en", "maintenance/en.html", true),
                     })
            {
                var label = $"{method} {target} ({acceptLanguage ?? "no Accept-Language"})";
                var context = await InMaintenanceAsync(root, method, target, acceptLanguage);
                var built = File.ReadAllBytes(Path.Combine(directory, page));
                Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode, label);
                Assert.AreEqual("text/html; charset=utf-8", context.Response.ContentType, label);
                Assert.AreEqual(built.Length, context.Response.ContentLength, label);
                CollectionAssert.AreEqual(method == "HEAD" ? Array.Empty<byte>() : built, Bytes(context), $"{label}: the page as the build wrote it, byte for byte");
                AssertPageHeaders(root, context, label);
                Assert.AreEqual(negotiated ? "Accept-Language" : string.Empty, context.Response.Headers.Vary.ToString(),
                    $"{label}: the answer depends on Accept-Language exactly when the path names no language");
                Assert.AreEqual(0, context.Response.Headers.SetCookie.Count, $"{label}: no cookie");
            }

            // The request delegate the server runs is the same answer.
            var delegated = Request("GET", "/fr/");
            await V3WebRoot.CreateMaintenanceRequestDelegate(root)(delegated);
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, delegated.Response.StatusCode);
            Assert.AreEqual(MaintenanceBodies["maintenance/fr.html"], Body(delegated));

            // A root opened to search engines is still kept out in maintenance: the page is no page to index.
            var indexed = V3WebRoot.Open(directory, V3WebRoot.Indexed)!;
            var opened = await InMaintenanceAsync(indexed, "GET", "/en/");
            Assert.AreEqual("noindex", opened.Response.Headers["X-Robots-Tag"].ToString());
            Assert.AreEqual(MaintenanceBodies["maintenance/en.html"], Body(opened));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ReadinessIsReadyOnlyWithAMaintenancePageInEachInterfaceLanguage()
    {
        var whole = NewWebDirectory();
        var frenchOnly = NewWebDirectory(["fr"]);
        var none = NewWebDirectory([]);
        try
        {
            var root = V3WebRoot.Open(whole)!;
            foreach (var method in new[] { "GET", "HEAD" })
            {
                var ready = await InMaintenanceAsync(root, method, "/health/ready");
                Assert.AreEqual(StatusCodes.Status204NoContent, ready.Response.StatusCode, method);
                Assert.AreEqual("no-store", ready.Response.Headers.CacheControl.ToString(), method);
                Assert.AreEqual("nosniff", ready.Response.Headers.XContentTypeOptions.ToString(), method);
                Assert.IsEmpty(Bytes(ready), method);
            }

            var posted = await InMaintenanceAsync(root, "POST", "/health/ready");
            Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, posted.Response.StatusCode);
            Assert.AreEqual("GET, HEAD", posted.Response.Headers.Allow.ToString());

            // A maintenance revision without its pages is not ready, so its probe fails and it takes no traffic.
            var partial = V3WebRoot.Open(frenchOnly)!;
            Assert.IsFalse(partial.HoldsMaintenancePages);
            foreach (var (what, unready) in new (string What, V3WebRoot? Root)[] { ("one language's page", partial), ("no page", V3WebRoot.Open(none)), ("no web root", null) })
            {
                var answer = await InMaintenanceAsync(unready, "GET", "/health/ready");
                Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, answer.Response.StatusCode, what);
                Assert.AreEqual("text/plain; charset=utf-8", answer.Response.ContentType, what);
                Assert.AreEqual("No maintenance page is held in each interface language.\n", Body(answer), what);
                Assert.AreEqual("no-store", answer.Response.Headers.CacheControl.ToString(), what);
                var head = await InMaintenanceAsync(unready, "HEAD", "/health/ready");
                Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, head.Response.StatusCode, what);
                Assert.IsEmpty(Bytes(head), what);
            }

            // Where it holds one language's page only, that page is served and the other language's request is the problem.
            Assert.AreEqual(MaintenanceBodies["maintenance/fr.html"], Body(await InMaintenanceAsync(partial, "GET", "/fr/")));
            AssertInMaintenance(await InMaintenanceAsync(partial, "GET", "/en/"), "GET /en/ with no English page");
        }
        finally
        {
            foreach (var directory in new[] { whole, frenchOnly, none })
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task EveryOtherRequestIsTheTransportProblemServiceInMaintenanceWithRetryAfter()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            foreach (var (method, target) in new[]
                     {
                         ("POST", "/api/v3/coverage"), ("GET", "/api/v3/coverage"), ("POST", "/api/v3/resolve"), ("POST", "/api/v3/no-such-operation"),
                         ("GET", "/api/v3-preview/resolve"), ("GET", "/health/live"), ("POST", "/fr/"), ("DELETE", "/en/search.html"), ("PUT", "/"),
                         ("POST", "/mcp"), ("GET", "/mcp"),
                     })
            {
                var label = $"{method} {target}";
                var context = await InMaintenanceAsync(root, method, target, "en");
                AssertInMaintenance(context, label);
                Assert.AreEqual(target == "/mcp" ? V3McpJsonRpc.ProtocolVersion : string.Empty, context.Response.Headers["MCP-Protocol-Version"].ToString(),
                    $"{label}: the MCP endpoint names its protocol version on every answer, and nothing else does");
            }

            // With no web root, every request but readiness is the problem, pages' included.
            AssertInMaintenance(await InMaintenanceAsync(null, "GET", "/fr/"), "GET /fr/ with no web root");
            AssertInMaintenance(await InMaintenanceAsync(null, "POST", "/api/v3/coverage"), "POST /api/v3/coverage with no web root");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task OffTheServerAnswersAsBeforeAndNeverShowsTheMaintenancePage()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            Assert.IsFalse(root.Paths.Any(static path => path.StartsWith("/maintenance/", StringComparison.Ordinal)), string.Join(", ", root.Paths));
            var server = ReleaseServer(root);

            var home = await AskAsync(server, "GET", "/fr/");
            Assert.AreEqual(StatusCodes.Status200OK, home.Response.StatusCode);
            Assert.AreEqual("<!doctype html><p>accueil</p>", Body(home));
            Assert.AreEqual(0, home.Response.Headers.RetryAfter.Count, "a page served is not in maintenance");

            foreach (var target in new[] { "/en/no-such-page", "/maintenance/en.html", "/maintenance/fr.html" })
            {
                var missing = await AskAsync(server, "GET", target, "en");
                Assert.AreEqual(StatusCodes.Status404NotFound, missing.Response.StatusCode, target);
                Assert.AreEqual(NotFoundEnglish, Body(missing), $"{target}: the 404 page, never the maintenance page");
                Assert.AreEqual(0, missing.Response.Headers.RetryAfter.Count, target);
            }

            var ready = await AskAsync(server, "GET", "/health/ready");
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ready.Response.StatusCode, "with no mount, readiness says no law is mounted, as before");
            Assert.AreEqual("No law is mounted.\n", Body(ready));

            var unknown = await AskAsync(server, "POST", "/api/v3/no-such-operation");
            Assert.AreEqual(StatusCodes.Status404NotFound, unknown.Response.StatusCode);
            using var problem = JsonDocument.Parse(Body(unknown));
            Assert.AreEqual("unknown_route", problem.RootElement.GetProperty("code").GetString());
            Assert.AreEqual(0, unknown.Response.Headers.RetryAfter.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
