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

    /// <summary>The table's answer to one of the server's own questions, or null; each question asked is counted in <paramref name="asked"/>.</summary>
    private static Func<string, IReadOnlyDictionary<string, string>, byte[]?> TableAnswers(JsonDocument table, List<string>? asked = null)
    {
        var answers = Engines(table).GetProperty("answers").EnumerateArray()
            .ToDictionary(
                static answer => $"{answer.GetProperty("operation").GetString()} {string.Join(",", answer.GetProperty("parameters").EnumerateObject().Select(static pair => $"{pair.Name}={pair.Value.GetString()}"))}",
                static answer => Encoding.UTF8.GetBytes(answer.GetProperty("envelope").GetRawText()),
                StringComparer.Ordinal);
        return (operation, parameters) =>
        {
            var key = $"{operation} {string.Join(",", parameters.Select(static pair => $"{pair.Key}={pair.Value}"))}";
            asked?.Add(key);
            return answers.TryGetValue(key, out var envelope) ? envelope : null;
        };
    }

    /// <summary>The record of the works the table's API holds, and the mounted build's time, as the API asks them at startup.</summary>
    private static (V3WebRoot.IndexingRecord? Record, string? Lastmod) Held(JsonDocument table) => V3WebRoot.SearchEngineFacts(Record(table), TableAnswers(table));

    /// <summary>A table's sitemap files, by their paths.</summary>
    private static Dictionary<string, string> Files(JsonElement files) =>
        files.EnumerateObject().ToDictionary(static file => file.Name, static file => file.Value.GetString()!, StringComparer.Ordinal);

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
        // Kept out, robots.txt never blocks the pages: a crawler must fetch a page to read its noindex (review of #976, F1).
        Assert.DoesNotContain("Disallow: /\n", V3WebRoot.RobotsKeptOut.Replace("Disallow: /api/", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("Sitemap:", V3WebRoot.RobotsKeptOut);
        Assert.AreEqual($"{V3WebRoot.RobotsKeptOut}\nSitemap: https://law.soufien.lu/sitemap.xml\n", V3WebRoot.RobotsIndexed, "the same rules, and the sitemap named");
    }

    [TestMethod]
    public void TheIndexingRecordIsReadWholeOrRefusedWholeWhateverItsSizeAndTheSitemapIsTheTablesOwn()
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
        var (held, lastmod) = Held(table);
        Assert.AreEqual(engines.GetProperty("lastmod").GetString(), lastmod);
        var expected = Files(engines.GetProperty("sitemap"));
        var files = V3WebRoot.SitemapFiles(held, lastmod);
        CollectionAssert.AreEqual(expected.Keys.ToArray(), files.Keys.ToArray(), "the index and its parts, in order");
        foreach (var (path, text) in expected)
        {
            Assert.AreEqual(text, files[path], $"{path}, byte for byte the live server's");
        }

        CollectionAssert.AreEquivalent(Files(engines.GetProperty("sitemapWithoutRecord")), V3WebRoot.SitemapFiles(null, lastmod).ToDictionary(static file => file.Key, static file => file.Value));
        var split = engines.GetProperty("split");
        var parts = V3WebRoot.SitemapFiles(held, lastmod, split.GetProperty("partSize").GetInt32());
        CollectionAssert.AreEqual(Files(split.GetProperty("files")).ToArray(), parts.ToArray(), "parts of at most partSize addresses (review of #976, F3)");
        var byBytes = V3WebRoot.SitemapFiles(held, lastmod, maximumBytes: 2000);
        Assert.IsGreaterThan(2, byBytes.Count, "split by bytes too");
        foreach (var (path, text) in byBytes.Skip(1))
        {
            Assert.IsLessThanOrEqualTo(2000, text.Length, path);
        }

        // A record whose sitemap holds more addresses than one file may is read whole (review of #976, F3: R92's gate does not
        // hang on a sitemap's size), and every work it lists opened.
        var many = ((V3WebRoot.SitemapMaximumUrls - (2 * 2)) / (2 * 2)) + 1;
        var works = string.Join(",", Enumerable.Range(0, many).Select(static index => $"{{\"address\":\"/lu-legilux/w{index:D6}\",\"languages\":[\"fra\"]}}"));
        var large = V3WebRoot.ReadIndexingRecord($"{{\"schema\":\"lex-v3-indexing-record/1\",\"built_at\":null,\"works\":[{works}]}}");
        Assert.IsNotNull(large, "over one sitemap file's limit: read whole");
        Assert.HasCount(many, large.Works);
        Assert.IsTrue(V3WebRoot.IndexablePage(V3WebRoot.Indexed, $"/en/lu-legilux/w{many - 1:D6}", large));
    }

    [TestMethod]
    public void TheServersOwnQuestionsNameTheMountedBuildAndTheListedWorksTheApiHolds()
    {
        using var table = SiteTable();
        var engines = Engines(table);
        var builds = engines.GetProperty("builds").EnumerateArray().ToArray();
        Assert.IsGreaterThanOrEqualTo(5, builds.Length);
        foreach (var row in builds)
        {
            var envelope = row.GetProperty("envelope").ValueKind == JsonValueKind.Null ? null : Encoding.UTF8.GetBytes(row.GetProperty("envelope").GetRawText());
            var builtAt = row.GetProperty("builtAt").ValueKind == JsonValueKind.Null ? null : row.GetProperty("builtAt").GetString();
            Assert.AreEqual(builtAt, V3WebRoot.MountedBuiltAt(envelope), row.GetProperty("what").GetString());
        }

        foreach (var row in engines.GetProperty("holds").EnumerateArray())
        {
            var envelope = row.GetProperty("envelope").ValueKind == JsonValueKind.Null ? null : Encoding.UTF8.GetBytes(row.GetProperty("envelope").GetRawText());
            Assert.AreEqual(row.GetProperty("held").GetBoolean(), V3WebRoot.DossierHolds(envelope), row.GetProperty("what").GetString());
        }

        Assert.AreEqual("32016R0679", V3WebRoot.DossierIdentifierOf("/eu-eurlex/32016R0679"), "an EU act is asked by its CELEX");
        Assert.AreEqual("/lu-legilux/loi-1991-08-10-n3", V3WebRoot.DossierIdentifierOf("/lu-legilux/loi-1991-08-10-n3"));

        // Asked once each: the coverage, and each listed work's dossier; a work the API does not answer is dropped (F5).
        var asked = new List<string>();
        var (held, lastmod) = V3WebRoot.SearchEngineFacts(Record(table), TableAnswers(table, asked));
        Assert.AreEqual(engines.GetProperty("lastmod").GetString(), lastmod);
        CollectionAssert.AreEqual(engines.GetProperty("held").EnumerateArray().Select(static work => work.GetString()).ToArray(), held!.Works.Select(static work => work.Address).ToArray());
        CollectionAssert.AreEqual(
            engines.GetProperty("answers").EnumerateArray()
                .Select(static answer => $"{answer.GetProperty("operation").GetString()} {string.Join(",", answer.GetProperty("parameters").EnumerateObject().Select(static pair => $"{pair.Name}={pair.Value.GetString()}"))}")
                .ToArray(),
            asked.ToArray());
        var (none, noneLastmod) = V3WebRoot.SearchEngineFacts(null, TableAnswers(table));
        Assert.IsNull(none);
        Assert.AreEqual(lastmod, noneLastmod, "no record: still the mounted build");
        var (unanswered, unansweredLastmod) = V3WebRoot.SearchEngineFacts(Record(table), static (_, _) => null);
        Assert.IsEmpty(unanswered!.Works, "no answer: nothing held");
        Assert.IsNull(unansweredLastmod);
    }

    [TestMethod]
    public void APageWithTheKeptOutMarkerIsOpenedOnlyIndexedAndALawsPageOnlyForAWorkTheRecordLists()
    {
        using var table = SiteTable();
        var record = Held(table).Record;
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
            CollectionAssert.AreEqual(new[] { "/robots.txt", "/sitemap.xml", "/sitemap-1.xml", "/sitemap-2.xml" }, rows.Select(static row => row.GetProperty("path").GetString()).ToArray());
            // The record as the API holds it at startup: read from its file, each listed work its dossier answers (F5).
            var (held, lastmod) = V3WebRoot.SearchEngineFacts(V3WebRoot.ReadIndexingRecordFile(recordFile), TableAnswers(table));
            foreach (var (state, switchValue, record) in new (string, string?, V3WebRoot.IndexingRecord?)[]
            {
                (V3WebRoot.KeptOut, null, null),
                (V3WebRoot.KeptOut, "Indexed", held),
                (V3WebRoot.Indexed, "indexed", held),
            })
            {
                var root = V3WebRoot.Open(directory, switchValue, record, lastmod)!;
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
                        : engines.GetProperty("sitemap").GetProperty(expected.GetProperty("body").GetString()!).GetString();
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
                    // Listed, but the API's dossier does not answer it: never opened, even rendered from an answer (F5).
                    ("/en/lu-legilux/no-such-work", false, false),
                    ("/en/lu-legilux/another-missing-work", true, false),
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

                // A method refused carries it too (review of #976, F6).
                foreach (var path in new[] { "/robots.txt", "/en/", "/sitemap.xml" })
                {
                    var post = Request("POST", path);
                    if (await root.TryServeAsync(post, path, CancellationToken.None))
                    {
                        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, post.Response.StatusCode, $"{state} {path}");
                        Assert.AreEqual(V3WebRoot.XRobotsTag, post.Response.Headers["X-Robots-Tag"].ToString(), $"{state} POST {path}");
                    }
                    else
                    {
                        Assert.IsFalse(indexed && path == "/sitemap.xml", $"{state} POST {path}: the sitemap is this root's with the site indexed");
                    }
                }

                // A redirect or a move: kept out, the header; indexed, none (it is no page: the page it leads to says what it says).
                var redirects = engines.GetProperty("redirects");
                var wanted = redirects.GetProperty(state).ValueKind == JsonValueKind.Null ? string.Empty : redirects.GetProperty(state).GetString();
                foreach (var path in redirects.GetProperty("paths").EnumerateArray().Select(static value => value.GetString()!))
                {
                    var context = Request("GET", path);
                    Assert.IsTrue(await root.TryServeAsync(context, path, CancellationToken.None), path);
                    Assert.IsTrue(context.Response.StatusCode is StatusCodes.Status301MovedPermanently or StatusCodes.Status302Found, path);
                    Assert.AreEqual(wanted, context.Response.Headers["X-Robots-Tag"].ToString(), $"{state} {path}");
                }
            }

            // Indexed with no record: the home pages are opened, no law's page, and the sitemap lists none.
            var unrecorded = V3WebRoot.Open(directory, "indexed", null, lastmod)!;
            Assert.IsFalse(unrecorded.HoldsIndexingRecord);
            foreach (var file in engines.GetProperty("sitemapWithoutRecord").EnumerateObject())
            {
                var sitemap = Request("GET", file.Name);
                Assert.IsTrue(await unrecorded.TryServeAsync(sitemap, file.Name, CancellationToken.None), file.Name);
                Assert.AreEqual(file.Value.GetString(), Body(sitemap), file.Name);
            }
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
