using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Tests;

/// <summary>
/// What the API's web root says to search engines (the specification of record's R67; the plan's item 3.6), in both states
/// of the release's switch: robots.txt, the sitemap and its alternates, <c>X-Robots-Tag</c>, the pages' robots element, and
/// a law's page opened only for a work the indexing record lists (R92). The rules are the site's own table, which the live
/// server is held to as well (<c>web/test/search-engines.test.mjs</c>).
/// </summary>
[TestClass]
public sealed class V3WebRootSearchEnginesTests
{
    private const string PageCsp = "default-src &#x27;none&#x27;; script-src &#x27;self&#x27;; connect-src &#x27;self&#x27;";

    private static JsonDocument SiteTable() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SitePaths", "site-paths.json")));

    private static JsonElement Engines(JsonDocument table) => table.RootElement.GetProperty("searchEngines");

    /// <summary>A web root whose pages name each robots element, as the build writes them, and whose renderer names the kept-out one.</summary>
    private static string NewWebDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-web-engines-{Guid.NewGuid():N}");
        foreach (var folder in new[] { "en", "fr", "not-found", "ssr" })
        {
            Directory.CreateDirectory(Path.Combine(directory, folder));
        }

        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"{PageCsp}\">{V3WebRoot.KeptOutMeta}</head><body>home</body></html>");
        File.WriteAllText(Path.Combine(directory, "fr", "index.html"), $"<!doctype html><html><head>{V3WebRoot.KeptOutMeta}</head><body>accueil</body></html>");
        File.WriteAllText(Path.Combine(directory, "en", "search.html"), $"<!doctype html><html><head>{V3WebRoot.NeverIndexedMeta}</head><body>search</body></html>");
        File.WriteAllText(Path.Combine(directory, "en", "dossier.html"), $"<!doctype html><html><head>{V3WebRoot.NeverIndexedMeta}</head><body>dossier as built</body></html>");
        File.WriteAllText(Path.Combine(directory, "en", "reading.html"), $"<!doctype html><html><head>{V3WebRoot.NeverIndexedMeta}</head><body>reading as built</body></html>");
        File.WriteAllText(Path.Combine(directory, "not-found", "en.html"), $"<!doctype html><html><head>{V3WebRoot.NeverIndexedMeta}</head><body>404</body></html>");
        File.WriteAllText(Path.Combine(directory, "not-found", "fr.html"), $"<!doctype html><html><head>{V3WebRoot.NeverIndexedMeta}</head><body>404</body></html>");
        File.WriteAllText(Path.Combine(directory, "styles.css"), "body{}");
        // The renderer writes the marker the switch opens for an answer, and the one it never opens for a refusal.
        File.WriteAllText(Path.Combine(directory, "ssr", "en.js"), $$"""
            globalThis.lexSsr = { render(kind, envelope, path) {
              const value = JSON.parse(envelope);
              const meta = value.refusal ? '{{V3WebRoot.NeverIndexedMeta.Replace("'", "\\'", StringComparison.Ordinal)}}' : '{{V3WebRoot.KeptOutMeta.Replace("'", "\\'", StringComparison.Ordinal)}}';
              return '<!doctype html><html><head>' + meta + '</head><body>' + kind + ' ' + path + '</body></html>';
            } };
            """);
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

    private static V3WebRoot.IndexingRecord Record(JsonDocument table) =>
        V3WebRoot.ReadIndexingRecord(Engines(table).GetProperty("record").GetRawText())!;

    [TestMethod]
    public void TheSwitchIsTheReleasesOneVariableKeptOutUnlessItSaysIndexedExactly()
    {
        using var table = SiteTable();
        var engines = Engines(table);
        Assert.AreEqual(V3WebRoot.SearchEnginesVariable, engines.GetProperty("variable").GetString());
        var states = engines.GetProperty("states").EnumerateArray().ToArray();
        Assert.IsGreaterThanOrEqualTo(6, states.Length);
        foreach (var row in states)
        {
            var value = row.GetProperty("value").ValueKind == JsonValueKind.Null ? null : row.GetProperty("value").GetString();
            Assert.AreEqual(row.GetProperty("state").GetString(), V3WebRoot.SearchEnginesState(value), value ?? "(none)");
        }

        Assert.AreEqual(V3WebRoot.NeverIndexedMeta, engines.GetProperty("meta").GetProperty("never").GetString());
        Assert.AreEqual(V3WebRoot.KeptOutMeta, engines.GetProperty("meta").GetProperty("keptOut").GetString());
        Assert.AreEqual(V3WebRoot.XRobotsTag, engines.GetProperty("xRobotsTag").GetString());
        Assert.AreEqual(engines.GetProperty("robots").GetProperty("kept-out").GetString(), V3WebRoot.RobotsTxt(V3WebRoot.KeptOut));
        Assert.AreEqual(engines.GetProperty("robots").GetProperty("indexed").GetString(), V3WebRoot.RobotsTxt(V3WebRoot.Indexed));
    }

    [TestMethod]
    public void TheIndexingRecordIsReadWholeOrRefusedWholeAndTheSitemapIsTheTablesOwn()
    {
        using var table = SiteTable();
        var engines = Engines(table);
        foreach (var row in engines.GetProperty("admitted").EnumerateArray())
        {
            Assert.IsNotNull(V3WebRoot.ReadIndexingRecord(row.GetProperty("record").GetRawText()), row.GetProperty("what").GetString());
        }

        var refused = engines.GetProperty("refused").EnumerateArray().ToArray();
        Assert.IsGreaterThanOrEqualTo(10, refused.Length);
        foreach (var row in refused)
        {
            Assert.IsNull(V3WebRoot.ReadIndexingRecord(row.GetProperty("record").GetRawText()), row.GetProperty("what").GetString());
        }

        foreach (var text in new[] { "", "not json", "null", "42", "\"lex-v3-indexing-record/1\"" })
        {
            Assert.IsNull(V3WebRoot.ReadIndexingRecord(text), text);
        }

        Assert.IsNotNull(V3WebRoot.ReadIndexingRecord("\uFEFF" + engines.GetProperty("record").GetRawText()), "a byte-order mark is no part of it");
        var record = Record(table);
        Assert.AreEqual(engines.GetProperty("record").GetProperty("built_at").GetString(), record.BuiltAt);
        Assert.AreEqual(engines.GetProperty("sitemap").GetString(), V3WebRoot.SitemapXml(record), "the sitemap, byte for byte the live server's");
        Assert.AreEqual(engines.GetProperty("sitemapWithoutRecord").GetString(), V3WebRoot.SitemapXml(null));

        // A record whose sitemap would hold more addresses than one file may is refused, not cut.
        var many = ((V3WebRoot.SitemapMaximumUrls - (2 * 2)) / (2 * 2)) + 1;
        string Works(int skip) => string.Join(",", Enumerable.Range(skip, many - skip).Select(static index => $"{{\"address\":\"/lu-legilux/w{index:D6}\",\"languages\":[\"fra\"]}}"));
        Assert.IsNull(V3WebRoot.ReadIndexingRecord($"{{\"schema\":\"lex-v3-indexing-record/1\",\"built_at\":null,\"works\":[{Works(0)}]}}"), "over the limit");
        Assert.IsNotNull(V3WebRoot.ReadIndexingRecord($"{{\"schema\":\"lex-v3-indexing-record/1\",\"built_at\":null,\"works\":[{Works(1)}]}}"), "at it");
    }

    [TestMethod]
    public void APageWithTheKeptOutMarkerIsOpenedOnlyIndexedAndALawsPageOnlyForAWorkTheRecordLists()
    {
        using var table = SiteTable();
        var record = Record(table);
        var rows = Engines(table).GetProperty("pages").EnumerateArray().ToArray();
        Assert.IsGreaterThanOrEqualTo(10, rows.Length);
        var page = $"<!doctype html><html><head>{V3WebRoot.KeptOutMeta}</head><body></body></html>";
        var never = $"<!doctype html><html><head>{V3WebRoot.NeverIndexedMeta}</head></html>";
        foreach (var row in rows)
        {
            var path = row.GetProperty("path").GetString()!;
            var opened = row.GetProperty("opened");
            Assert.AreEqual(opened.GetProperty("kept-out").GetBoolean(), V3WebRoot.IndexablePage(V3WebRoot.KeptOut, path, record), $"{path}, kept out");
            Assert.AreEqual(opened.GetProperty("indexed").GetBoolean(), V3WebRoot.IndexablePage(V3WebRoot.Indexed, path, record), $"{path}, indexed");
            Assert.AreEqual(opened.GetProperty("indexed, with no record").GetBoolean(), V3WebRoot.IndexablePage(V3WebRoot.Indexed, path, null), $"{path}, no record");
            var served = V3WebRoot.ServedPage(page, V3WebRoot.Indexed, path, record);
            Assert.AreEqual(opened.GetProperty("indexed").GetBoolean(), served.Opened, path);
            Assert.AreEqual(served.Opened ? "<!doctype html><html><head></head><body></body></html>" : page, served.Html, path);
            Assert.AreEqual((never, false), V3WebRoot.ServedPage(never, V3WebRoot.Indexed, path, record), $"{path}: never opened");
        }
    }

    [TestMethod]
    public async Task TheWebRootSaysTheSwitchsStateInBothStatesAsTheSitesOwnTableSays()
    {
        using var table = SiteTable();
        var engines = Engines(table);
        var directory = NewWebDirectory();
        var recordFile = Path.Combine(directory, "..", $"{Path.GetFileName(directory)}-record.json");
        File.WriteAllText(recordFile, engines.GetProperty("record").GetRawText());
        try
        {
            var rows = table.RootElement.GetProperty("paths").EnumerateArray().Where(static row => row.TryGetProperty("searchEngines", out _)).ToArray();
            CollectionAssert.AreEqual(new[] { "/robots.txt", "/sitemap.xml" }, rows.Select(static row => row.GetProperty("path").GetString()).ToArray());
            foreach (var (state, switchValue, record) in new (string, string?, V3WebRoot.IndexingRecord?)[]
            {
                (V3WebRoot.KeptOut, null, null),
                (V3WebRoot.KeptOut, "Indexed", V3WebRoot.ReadIndexingRecordFile(recordFile)),
                (V3WebRoot.Indexed, "indexed", V3WebRoot.ReadIndexingRecordFile(recordFile)),
            })
            {
                var root = V3WebRoot.Open(directory, switchValue, record)!;
                Assert.AreEqual(state, root.SearchEngines);
                foreach (var row in rows)
                {
                    var path = row.GetProperty("path").GetString()!;
                    var expected = row.GetProperty("searchEngines").GetProperty(state);
                    var context = Request("GET", path);
                    var claimed = await root.TryServeAsync(context, path, CancellationToken.None);
                    Assert.AreEqual(expected.GetProperty("claimed").GetBoolean(), claimed, $"{state} {path}");
                    if (!claimed)
                    {
                        // An address this root does not hold: the API's 404 page answers it, as any other.
                        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => DateTimeOffset.UnixEpoch, null, root);
                        var notFound = Request("GET", path);
                        await handler.HandleAsync(notFound, CancellationToken.None);
                        Assert.AreEqual(expected.GetProperty("status").GetInt32(), notFound.Response.StatusCode, $"{state} {path}");
                        Assert.AreEqual(V3WebRoot.XRobotsTag, notFound.Response.Headers["X-Robots-Tag"].ToString(), $"{state} {path}: the 404 page is kept out");
                        continue;
                    }

                    Assert.AreEqual(expected.GetProperty("status").GetInt32(), context.Response.StatusCode, $"{state} {path}");
                    Assert.AreEqual(expected.GetProperty("type").GetString(), context.Response.ContentType, $"{state} {path}");
                    Assert.AreEqual(V3WebRoot.XRobotsTag, context.Response.Headers["X-Robots-Tag"].ToString(), $"{state} {path}");
                    Assert.AreEqual(root.ContentSecurityPolicy, context.Response.Headers.ContentSecurityPolicy.ToString(), $"{state} {path}: every header a page carries");
                    var body = expected.GetProperty("body").GetString() == "robots"
                        ? engines.GetProperty("robots").GetProperty(state).GetString()
                        : engines.GetProperty("sitemap").GetString();
                    Assert.AreEqual(body, Body(context), $"{state} {path}");
                    Assert.AreEqual((long)Encoding.UTF8.GetByteCount(body!), context.Response.ContentLength, $"{state} {path}");
                    var head = Request("HEAD", path);
                    Assert.IsTrue(await root.TryServeAsync(head, path, CancellationToken.None));
                    Assert.AreEqual(string.Empty, Body(head), $"{state} {path}: HEAD carries no body");
                }

                var indexed = state == V3WebRoot.Indexed;
                foreach (var (path, opened) in new[]
                {
                    ("/en/", indexed),
                    ("/fr/index.html", indexed),
                    ("/en/search.html", false),
                    ("/en/lu-legilux/loi-1991-08-10-n3", false),
                    ("/styles.css", false),
                })
                {
                    var context = Request("GET", path);
                    Assert.IsTrue(await root.TryServeAsync(context, path, CancellationToken.None), path);
                    var body = Body(context);
                    Assert.AreEqual(opened ? 0 : 1, context.Response.Headers["X-Robots-Tag"].Count, $"{state} {path}");
                    Assert.AreEqual(!opened, body.Contains(V3WebRoot.KeptOutMeta, StringComparison.Ordinal) || body.Contains(V3WebRoot.NeverIndexedMeta, StringComparison.Ordinal) || path.EndsWith(".css", StringComparison.Ordinal), $"{state} {path}");
                    Assert.AreEqual((long)Encoding.UTF8.GetByteCount(body), context.Response.ContentLength, $"{state} {path}: its length as served");
                }

                // A law's page rendered from an answer: opened with the site indexed only for a work the record lists, and a
                // refusal never.
                foreach (var (path, refusal, opened) in new[]
                {
                    ("/en/lu-legilux/loi-1991-08-10-n3", false, indexed),
                    ("/en/lu-legilux/loi-1991-08-10-n3/fra/art_15", false, indexed),
                    ("/en/eu-eurlex/32016R0679/eng", false, indexed),
                    ("/en/lu-legilux/loi-2017-03-14-a439", false, false),
                    ("/en/lu-legilux/no-such-work", true, false),
                })
                {
                    var context = Request("GET", path);
                    var answer = refusal ? "{\"result\":null,\"refusal\":{\"code\":\"identifier_unknown\"}}"u8.ToArray() : "{\"result\":{},\"refusal\":null}"u8.ToArray();
                    Assert.IsTrue(await root.TryServeAsync(context, path, CancellationToken.None, (_, _) => answer, static () => "2026-10-17"), path);
                    var body = Body(context);
                    StringAssert.Contains(body, path, $"{state} {path}: rendered");
                    Assert.AreEqual(opened ? 0 : 1, context.Response.Headers["X-Robots-Tag"].Count, $"{state} {path}");
                    Assert.AreEqual(!opened, body.Contains("<meta name=\"robots\"", StringComparison.Ordinal), $"{state} {path}");
                    Assert.AreEqual((long)Encoding.UTF8.GetByteCount(body), context.Response.ContentLength, $"{state} {path}");
                }

                // Every other answer of this root is kept out: a redirect, a move.
                foreach (var path in new[] { "/", "/lu-legilux/loi-1991-08-10-n3", "/en/eu-eurlex/32016R0679/eng/2016-05-04--" + new string('a', 64) })
                {
                    var context = Request("GET", path);
                    Assert.IsTrue(await root.TryServeAsync(context, path, CancellationToken.None), path);
                    Assert.AreEqual(V3WebRoot.XRobotsTag, context.Response.Headers["X-Robots-Tag"].ToString(), $"{state} {path}");
                }
            }

            // Indexed with no record: the home pages are opened, no law's page, and the sitemap lists none.
            var unrecorded = V3WebRoot.Open(directory, "indexed")!;
            Assert.IsFalse(unrecorded.HoldsIndexingRecord);
            var sitemap = Request("GET", "/sitemap.xml");
            Assert.IsTrue(await unrecorded.TryServeAsync(sitemap, "/sitemap.xml", CancellationToken.None));
            Assert.AreEqual(engines.GetProperty("sitemapWithoutRecord").GetString(), Body(sitemap));
            var law = Request("GET", "/en/lu-legilux/loi-1991-08-10-n3");
            Assert.IsTrue(await unrecorded.TryServeAsync(law, "/en/lu-legilux/loi-1991-08-10-n3", CancellationToken.None, (_, _) => "{\"result\":{}}"u8.ToArray()));
            Assert.AreEqual(V3WebRoot.XRobotsTag, law.Response.Headers["X-Robots-Tag"].ToString());
            Assert.IsNull(V3WebRoot.ReadIndexingRecordFile(Path.Combine(directory, "no-such-record.json")), "no file: no record");
        }
        finally
        {
            File.Delete(recordFile);
            Directory.Delete(directory, recursive: true);
        }
    }
}
