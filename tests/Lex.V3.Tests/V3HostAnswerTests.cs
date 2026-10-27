using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests;

/// <summary>
/// What the one-server host answers beside the law, driven through the real handler with no corpus mounted: the security
/// headers on every answer, not only on pages (the specification of record's shell line; ruling 3; the plan's item 10.7); the
/// published key set at its stable address (R116); and the per-client limit at the handler (R109, R46): who is counted, the
/// typed refusal over REST and MCP, the page renders' own budget. Over a mount, <c>V3ClientLimitTests</c> holds the refusal
/// in the mounted corpus's context.
/// </summary>
[TestClass]
public sealed class V3HostAnswerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static V3ApiHandler Handler(V3WebRoot? webRoot = null, V3ClientLimiter? limiter = null) =>
        new(null, new V3PlatformHost(), static () => Now, null, webRoot,
            limiter ?? new V3ClientLimiter(new V3ClientLimiterTests.ManualTime(Now), static () => new byte[32]));

    private static DefaultHttpContext Request(string method, string rawTarget, string body = "", string? from = "203.0.113.77", string? forwarded = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext { TraceIdentifier = "host-answer" };
        context.Request.Method = method;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        if (from is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(from);
        }

        if (forwarded is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwarded;
        }

        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<DefaultHttpContext> AskAsync(V3ApiHandler handler, string method, string rawTarget, string body = "", string? from = "203.0.113.77", string? forwarded = null)
    {
        var context = Request(method, rawTarget, body, from, forwarded);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static byte[] Bytes(HttpContext context) => ((MemoryStream)context.Response.Body).ToArray();

    private static string Coverage => "{\"operation_id\":\"coverage\",\"parameters\":{}}";

    private static string McpCoverage => "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"coverage\",\"arguments\":{}}}";

    private static void AssertHostHeaders(HttpContext context, string what, string? policy = null)
    {
        var headers = context.Response.Headers;
        Assert.AreEqual("max-age=31536000; includeSubDomains", headers.StrictTransportSecurity.ToString(), what);
        Assert.AreEqual("no-referrer", headers["Referrer-Policy"].ToString(), what);
        Assert.AreEqual("nosniff", headers.XContentTypeOptions.ToString(), what);
        Assert.AreEqual(policy ?? "default-src 'none'; frame-ancestors 'none'", headers.ContentSecurityPolicy.ToString(), what);
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), .. parts]);
    }

    [TestMethod]
    public async Task EveryAnswerThatIsNoPageCarriesHstsNoReferrerNosniffAndAPolicyThatAllowsNothing()
    {
        var handler = Handler();
        foreach (var (method, target, body, status) in new[]
        {
            ("POST", "/api/v3/coverage", Coverage, StatusCodes.Status200OK),
            ("POST", "/api/v3/coverage", "{", StatusCodes.Status400BadRequest),
            ("GET", "/api/v3/coverage", "", StatusCodes.Status405MethodNotAllowed),
            ("POST", "/api/v3/transposition", "{}", StatusCodes.Status404NotFound),
            ("POST", "/api/v3/no-such-operation", "{}", StatusCodes.Status404NotFound),
            ("POST", "/mcp", McpCoverage, StatusCodes.Status200OK),
            ("GET", "/mcp", "", StatusCodes.Status405MethodNotAllowed),
            ("GET", "/health/ready", "", StatusCodes.Status503ServiceUnavailable),
            ("GET", "/.well-known/lex-keys.json", "", StatusCodes.Status200OK),
            ("GET", "/no-such-page", "", StatusCodes.Status404NotFound),
        })
        {
            var context = await AskAsync(handler, method, target, body);
            var what = $"{method} {target}";
            Assert.AreEqual(status, context.Response.StatusCode, what);
            AssertHostHeaders(context, what);
        }
    }

    [TestMethod]
    public async Task APageKeepsItsOwnPolicyOverTheHosts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "en"));
        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            "<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#x27;self&#x27;\"></head><body>home</body></html>");
        Directory.CreateDirectory(Path.Combine(directory, "fr"));
        File.WriteAllText(Path.Combine(directory, "fr", "index.html"), "<!doctype html><p>accueil</p>");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        try
        {
            var root = V3WebRoot.Open(directory)!;
            var handler = Handler(root);
            foreach (var target in new[] { "/en/", "/styles.css", "/" })
            {
                var context = await AskAsync(handler, "GET", target);
                AssertHostHeaders(context, target, "default-src 'self'; frame-ancestors 'none'");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheKeySetIsServedAtItsStableAddressAsTheRepositoryHoldsIt()
    {
        var committed = await File.ReadAllBytesAsync(RepositoryFile("src", "Lex.V3.Api", "well-known", "lex-keys.json"));
        CollectionAssert.AreEqual(committed, V3KeySet.Document, "the server is built with the committed bytes");
        var handler = Handler();
        foreach (var target in new[] { "/.well-known/lex-keys.json", "/.well-known/lex-keys.json?fresh=1" })
        {
            var served = await AskAsync(handler, "GET", target);
            Assert.AreEqual(StatusCodes.Status200OK, served.Response.StatusCode, target);
            Assert.AreEqual("application/json; charset=utf-8", served.Response.ContentType, target);
            Assert.AreEqual("no-store", served.Response.Headers.CacheControl.ToString(), target);
            CollectionAssert.AreEqual(committed, Bytes(served), target);
        }

        var head = await AskAsync(handler, "HEAD", "/.well-known/lex-keys.json");
        Assert.AreEqual(StatusCodes.Status200OK, head.Response.StatusCode);
        Assert.AreEqual(committed.Length, head.Response.ContentLength);
        Assert.IsEmpty(Bytes(head), "HEAD carries no body");
        var post = await AskAsync(handler, "POST", "/.well-known/lex-keys.json", "{}");
        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, post.Response.StatusCode);
        Assert.AreEqual("GET, HEAD", post.Response.Headers.Allow.ToString());
        Assert.AreEqual(StatusCodes.Status404NotFound, (await AskAsync(handler, "GET", "/.well-known/lex-keys.jsonx")).Response.StatusCode, "only the one address");
        Assert.IsTrue((await AskAsync(handler, "GET", "/.well-known/lex-keys.json", from: null)).Response.StatusCode == StatusCodes.Status200OK, "with no client address too");
    }

    [TestMethod]
    public void TheKeySetNamesTheReleaseKeyWithItsIdentityRoleValidityAndStatusAndItsIdIsTheKeysOwnDigest()
    {
        using var document = JsonDocument.Parse(V3KeySet.Document);
        var root = document.RootElement;
        Assert.AreEqual("lex-v3-keys/1", root.GetProperty("schema").GetString());
        Assert.AreEqual("https://law.soufien.lu", root.GetProperty("site").GetString());
        var keys = root.GetProperty("keys").EnumerateArray().ToArray();
        Assert.HasCount(1, keys, "the release key; a second signer's key joins it once it exists (O16)");
        var key = keys[0];
        Assert.AreEqual("lex-v3-release", key.GetProperty("identity").GetString(), "the signer every release names");
        Assert.AreEqual("release", key.GetProperty("role").GetString());
        Assert.AreEqual("active", key.GetProperty("status").GetString());
        var from = DateOnly.ParseExact(key.GetProperty("valid_from").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var until = DateOnly.ParseExact(key.GetProperty("valid_until").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(from.AddYears(1), until, "a key is replaced within a year (R116)");

        var pem = File.ReadAllText(RepositoryFile("src", "Lex.V3.Api", "well-known", "lex-v3-release.pem"));
        Assert.AreEqual(pem, key.GetProperty("public_key_pem").GetString(), "the key set's key is the committed public key");
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        Assert.AreEqual(256, ecdsa.KeySize);
        Assert.AreEqual(ECCurve.NamedCurves.nistP256.Oid.Value, ecdsa.ExportParameters(false).Curve.Oid.Value, "P-256");
        Assert.AreEqual("sha256:" + Convert.ToHexStringLower(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo())), key.GetProperty("key_id").GetString(),
            "the key id is the SHA-256 of the key's SubjectPublicKeyInfo, as the set's rule says");
        Assert.Contains("SubjectPublicKeyInfo", root.GetProperty("key_id_rule").GetString()!);
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(V3KeySet.Document), "only public keys");
    }

    [TestMethod]
    public async Task OverSixtyAMinuteAnOperationIsRefusedRateLimitedWith429AndRetryAfterOverRestAndTheSameEnvelopeOverMcp()
    {
        var handler = Handler();
        for (var index = 0; index < V3ClientLimiter.ApiPerMinute; index++)
        {
            var answered = await AskAsync(handler, "POST", "/api/v3/coverage", Coverage);
            Assert.AreEqual(StatusCodes.Status200OK, answered.Response.StatusCode, $"request {index + 1}");
        }

        var refused = await AskAsync(handler, "POST", "/api/v3/coverage", Coverage);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, refused.Response.StatusCode);
        Assert.AreEqual("1", refused.Response.Headers.RetryAfter.ToString());
        Assert.AreEqual("application/json;charset=utf-8", refused.Response.ContentType);
        AssertHostHeaders(refused, "the refusal");
        var envelope = V3EnvelopeJson.ParseAndVerify(Bytes(refused), V3OperationRegistry.Reviewed);
        Assert.AreEqual("rate_limited", envelope.Refusal!.Code);
        Assert.AreEqual("{\"retry_after\":1,\"retryable\":true}", envelope.Refusal.HelpfulPayload.GetRawText());

        // A malformed request keeps its own problem, over the limit too: a refusal is only ever an operation's.
        var malformed = await AskAsync(handler, "POST", "/api/v3/coverage", "{");
        Assert.AreEqual(StatusCodes.Status400BadRequest, malformed.Response.StatusCode);
        Assert.AreEqual(string.Empty, malformed.Response.Headers.RetryAfter.ToString());

        // MCP: the tool call answers the same envelope in a JSON-RPC result, status 200, with Retry-After.
        var mcp = await AskAsync(handler, "POST", "/mcp", McpCoverage);
        Assert.AreEqual(StatusCodes.Status200OK, mcp.Response.StatusCode);
        Assert.AreEqual("1", mcp.Response.Headers.RetryAfter.ToString());
        using var answer = JsonDocument.Parse(Bytes(mcp));
        var structured = answer.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.IsFalse(answer.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(), "a reviewed refusal is not a tool error");
        using (var rest = JsonDocument.Parse(Bytes(refused)))
        {
            Assert.IsTrue(JsonElement.DeepEquals(rest.RootElement, structured), "REST and MCP answer one envelope");
        }

        // The protocol's own methods hold no law and are answered as usual.
        var initialize = await AskAsync(handler, "POST", "/mcp", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}");
        using (var initialized = JsonDocument.Parse(Bytes(initialize)))
        {
            Assert.IsTrue(initialized.RootElement.TryGetProperty("result", out _));
        }

        // Another client is answered.
        Assert.AreEqual(StatusCodes.Status200OK, (await AskAsync(handler, "POST", "/api/v3/coverage", Coverage, from: "203.0.113.78")).Response.StatusCode);
    }

    [TestMethod]
    public async Task TheProbesAndJourneysOnTheirOwnMachineAreNotCountedAndTheIngressesClientIs()
    {
        var handler = Handler();
        foreach (var local in new[] { "127.0.0.1", "::1", "172.17.0.1" })
        {
            for (var index = 0; index < V3ClientLimiter.ApiPerMinute + 10; index++)
            {
                var answered = await AskAsync(handler, "POST", "/api/v3/coverage", Coverage, from: local);
                Assert.AreEqual(StatusCodes.Status200OK, answered.Response.StatusCode, $"{local}, request {index + 1}: a caller on the host itself names no client");
            }
        }

        for (var index = 0; index < V3ClientLimiter.ApiPerMinute; index++)
        {
            Assert.AreEqual(StatusCodes.Status200OK, (await AskAsync(handler, "POST", "/api/v3/coverage", Coverage, from: "100.100.0.7", forwarded: "198.51.100.1, 203.0.113.9")).Response.StatusCode);
        }

        Assert.AreEqual(StatusCodes.Status429TooManyRequests,
            (await AskAsync(handler, "POST", "/api/v3/coverage", Coverage, from: "100.100.0.8", forwarded: "198.51.100.2, 203.0.113.9")).Response.StatusCode,
            "through the ingress, the client is the address it forwards, whatever the caller wrote before it");
        Assert.AreEqual(StatusCodes.Status429TooManyRequests,
            (await AskAsync(handler, "POST", "/api/v3/coverage", Coverage, from: "203.0.113.9")).Response.StatusCode, "the same client met directly");
        Assert.AreEqual(StatusCodes.Status200OK, (await AskAsync(handler, "POST", "/api/v3/coverage", Coverage, from: null)).Response.StatusCode, "no address, no client");
    }

    [TestMethod]
    public async Task TheSitesFilesAreNotCountedAndItsRenderedPagesHaveTheirOwnBudgetPastWhichThePageIsServedAsBuilt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-host-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "en"));
        Directory.CreateDirectory(Path.Combine(directory, "ssr"));
        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            "<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#x27;self&#x27;\"></head><body>home</body></html>");
        File.WriteAllText(Path.Combine(directory, "en", "dossier.html"), "<!doctype html><p>the dossier page as built</p>");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        File.WriteAllText(Path.Combine(directory, "ssr", "en.js"), """
            globalThis.lexSsr = { render(kind, envelope, path) {
              return '<!doctype html><p>rendered ' + kind + ' ' + JSON.parse(envelope).refusal.code + '</p>';
            } };
            """);
        try
        {
            var handler = Handler(V3WebRoot.Open(directory)!);
            for (var index = 0; index < V3ClientLimiter.ApiPerMinute * 2; index++)
            {
                Assert.AreEqual(StatusCodes.Status200OK, (await AskAsync(handler, "GET", "/styles.css")).Response.StatusCode, "a file of the site is never counted");
            }

            const string dossier = "/en/lu-legilux/loi-1991-08-10-n3";
            for (var index = 0; index < V3ClientLimiter.RendersPerMinute; index++)
            {
                var page = await AskAsync(handler, "GET", dossier);
                Assert.AreEqual("<!doctype html><p>rendered dossier no_corpus_mounted</p>", Encoding.UTF8.GetString(Bytes(page)), $"render {index + 1}");
            }

            var asBuilt = await AskAsync(handler, "GET", dossier);
            Assert.AreEqual(StatusCodes.Status200OK, asBuilt.Response.StatusCode, "no page is refused");
            Assert.AreEqual("<!doctype html><p>the dossier page as built</p>", Encoding.UTF8.GetString(Bytes(asBuilt)), "past the budget, the page as built, which asks for itself");
            Assert.AreEqual(StatusCodes.Status200OK, (await AskAsync(handler, "POST", "/api/v3/coverage", Coverage)).Response.StatusCode, "the API's budget is its own");
            Assert.AreEqual("<!doctype html><p>rendered dossier no_corpus_mounted</p>", Encoding.UTF8.GetString(Bytes(await AskAsync(handler, "GET", dossier, from: "127.0.0.1"))),
                "a caller on the host itself is never counted");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
