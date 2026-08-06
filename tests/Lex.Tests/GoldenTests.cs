using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lex.Index;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lex.Tests;

/// <summary>
/// Golden files: the whole point of a refactor is that nothing changes.
///
/// Every route and every tool is rendered against a fixed fixture index and compared byte for
/// byte with a committed snapshot. A pure refactor moves zero bytes, so any diff here is a
/// regression by definition rather than by judgement, which is the only property that makes it
/// safe to move two thousand lines of code around.
///
/// Volatile values are normalised, not excluded: build timestamps, today's date and the corpus
/// commit change on every run and would otherwise make every file differ for the wrong reason.
/// Everything else, including whitespace, is compared exactly.
///
/// Set LEX_GOLDEN_UPDATE=1 to rewrite the snapshots after an INTENDED change, then read the diff
/// before committing it. That review is the safety mechanism; the tests only enforce it.
/// </summary>
public class GoldenTests : IClassFixture<GoldenTests.Site>
{
    private readonly Site _site;
    public GoldenTests(Site site) => _site = site;

    /// <summary>Every server-rendered page a reader can reach, plus the shapes that refuse.</summary>
    public static TheoryData<string, string> Pages() => new()
    {
        { "home",            "/" },
        { "browse",          "/browse" },
        { "browse-filtered", "/browse?type=LOI&text=yes&sort=versions" },
        { "browse-empty",    "/browse?type=NOSUCHTYPE" },
        { "browse-page2",    "/browse?page=2" },
        { "coverage",        "/coverage" },
        { "verify",          "/verify" },
        { "architecture",    "/architecture" },
        { "architecture-next", "/architecture/next" },
        { "benchmarks",      "/benchmarks" },
        { "built",           "/built" },
        { "decisions",       "/decisions" },
        { "about",           "/about" },
        { "stories",         "/stories" },
        { "find",            "/find" },
        { "developers",      "/developers" },
        { "ai",              "/ai" },
        { "how-it-works",    "/how-it-works" },
        { "search",          "/search?q=travail" },
        { "search-empty",    "/search?q=zzzznotalaw" },
        { "in-force-on",     "/in-force-on?date=2021-01-01" },
        { "changed",         "/changed?from=2019-01-01&to=2023-01-01" },
        { "work",            "/t-pub/w1" },
        { "work-asof",       "/t-pub/w1/2022-06-01" },
        { "work-notext",     "/t-pub/w2/2020-01-01" },
        { "work-unknown",    "/t-pub/nope" },
        { "work-baddate",    "/t-pub/w1/1200-01-01" },
        { "diff",            "/t-pub/w1/diff/2020-01-01/2022-01-01" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_page_renders_exactly_as_it_did(string name, string path)
    {
        var res = await _site.Client.GetAsync(path);
        var body = await res.Content.ReadAsStringAsync();
        Golden.Assert($"page-{name}", $"HTTP {(int)res.StatusCode}\n{Golden.Normalise(body)}");
    }

    [Fact]
    public async Task Coverage_keeps_cards_outside_coverage_tables()
    {
        var html = await _site.Client.GetStringAsync("/coverage");
        Assert.DoesNotMatch(new Regex("<table[^>]*>(?:(?!</table>).)*<div\\b",
            RegexOptions.Singleline | RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task Developer_search_contract_documents_every_public_filter()
    {
        var html = await _site.Client.GetStringAsync("/developers");
        foreach (var field in new[]
        {
            "jurisdiction", "retrieval_mode", "time_scope", "as_of", "fuzzy", "source_class",
            "hierarchy", "act_form", "binding_status", "domain", "language", "works",
        })
            Assert.Contains(field, html);
    }

    [Fact]
    public async Task Home_reserves_workspace_height_and_versions_immutable_assets()
    {
        var html = await _site.Client.GetStringAsync("/");
        Assert.Contains("classList.add('workspace-loading')", html);
        Assert.DoesNotContain("{assetVersion}", html);
        Assert.Matches("/app/workspace\\.css\\?v=[^\"']+", html);
        var script = Regex.Match(html, "/app/workspace\\.js\\?v=[^\"']+");
        Assert.True(script.Success);

        var asset = await _site.Client.GetAsync(script.Value);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.True(asset.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), asset.Headers.CacheControl?.MaxAge);
        Assert.Contains("immutable", asset.Headers.CacheControl?.Extensions.Select(x => x.Name) ?? []);
    }

    [Fact]
    public async Task Html_is_compressed_when_the_client_accepts_brotli()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptEncoding.ParseAdd("br");
        using var response = await _site.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Architecture_separates_live_target_and_unmeasured_claims()
    {
        var current = await _site.Client.GetStringAsync("/architecture");
        Assert.Contains("2</td>", current);              // mounted fixture works, not a hand-written live count
        Assert.Contains(IndexBuilder.SchemaVersion, current); // mounted fixture schema, not roadmap prose
        Assert.DoesNotContain("local compact semantic candidates", current);

        var next = await _site.Client.GetStringAsync("/architecture/next");
        Assert.Contains("local compact semantic candidates", next);
        Assert.Contains("gated", next);

        var benchmarks = await _site.Client.GetStringAsync("/benchmarks");
        Assert.Contains("Not measured yet", benchmarks);
        Assert.Contains("engineer-reviewed", benchmarks);
    }

    [Fact]
    public async Task Public_retrieval_judgments_are_downloadable_and_complete()
    {
        var json = await _site.Client.GetStringAsync("/benchmarks/cases.json");
        var cases = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();
        Assert.Equal(200, cases.Count);
        Assert.All(cases, c => Assert.Equal("engineer-reviewed", c!["review_status"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Latest_benchmark_is_not_published_without_a_verified_manifest()
    {
        var response = await _site.Client.GetAsync("/benchmarks/latest.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Architecture_registry_is_decision_complete_and_uses_known_statuses()
    {
        var registry = Lex.Web.ArchitectureProgram.Registry;
        Assert.Equal(["planned", "building", "gated", "shipped", "rejected"], registry.Statuses);
        Assert.All(registry.Milestones, m => Assert.Contains(m.Status, registry.Statuses));
        Assert.All(registry.Decisions, d =>
        {
            Assert.Contains(d.Status, registry.Statuses);
            Assert.False(string.IsNullOrWhiteSpace(d.Alternative));
            Assert.False(string.IsNullOrWhiteSpace(d.Reason));
            Assert.False(string.IsNullOrWhiteSpace(d.Cost));
        });
    }

    /// <summary>Every advertised tool, called with arguments the fixture can answer.</summary>
    public static TheoryData<string, string, string> Tools() => new()
    {
        { "as_of",             "as_of",             """{"work":"t-pub:w1","date":"2022-06-01"}""" },
        { "as_of-outline",     "as_of",             """{"work":"t-pub:w1","date":"2022-06-01","mode":"outline"}""" },
        { "as_of-select",      "as_of",             """{"work":"t-pub:w1","date":"2022-06-01","mode":"select","anchors":"art_1"}""" },
        { "as_of-miss",        "as_of",             """{"work":"t-pub:w1","date":"2022-06-01","mode":"select","anchors":"art_9999"}""" },
        { "as_of-unknown",     "as_of",             """{"work":"t-pub:nope","date":"2022-06-01"}""" },
        { "timeline",          "timeline",          """{"work":"t-pub:w1"}""" },
        { "in_force_on",       "in_force_on",       """{"date":"2022-06-01"}""" },
        { "diff",              "diff",              """{"work":"t-pub:w1","from_date":"2020-06-01","to_date":"2022-06-01"}""" },
        { "search",            "search",            """{"query":"thing"}""" },
        { "search-scoped",     "search",            """{"query":"thing","works":"t-pub:w2"}""" },
        { "article_history",   "article_history",   """{"work":"t-pub:w1","anchor":"art_1"}""" },
        { "provenance",        "provenance",        """{"lex_id":"t-pub:w1:2022-01-01"}""" },
        { "coverage",          "coverage",          """{}""" },
        { "cited_by",          "cited_by",          """{"work":"t-pub:w1"}""" },
        { "changes_in_period", "changes_in_period", """{"from_date":"2019-01-01","to_date":"2023-01-01"}""" },
    };

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task A_tool_answers_exactly_as_it_did(string name, string tool, string args)
    {
        // Built by concatenation rather than interpolation: the JSON ends in "}}}}" and a raw
        // interpolated literal cannot carry more closing braces than its $ count allows.
        var rpc = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":" """.TrimEnd()
                  + tool + "\",\"arguments\":" + args + "}}";
        var res = await _site.Client.PostAsync("/mcp",
            new StringContent(rpc, Encoding.UTF8, "application/json"));
        var body = await res.Content.ReadAsStringAsync();
        // A malformed request returns an empty body, and an empty snapshot would happily become
        // the baseline and then "pass" forever. The first version of this file did exactly that.
        Xunit.Assert.True(body.Length > 40, $"{tool} returned {body.Length} chars: {body}");
        Golden.Assert($"tool-{name}", Golden.Normalise(body));
    }

    [Fact]
    public async Task The_advertised_tool_list_renders_exactly_as_it_did()
    {
        var res = await _site.Client.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"));
        Golden.Assert("tools-list", Golden.Normalise(await res.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// initialize answers with a protocol revision the server actually speaks.
    ///
    /// It used to echo the client's request verbatim, so asking for "1999-01-01" got
    /// "1999-01-01" back and the handshake completed on a version that has never existed. The
    /// spec has the server answer with the requested version when it supports it, and otherwise
    /// with one it does support, so that the client can decide whether to go on. Echoing takes
    /// that decision away by lying about it.
    /// </summary>
    [Theory]
    [InlineData("2025-06-18", "2025-06-18")]   // supported, echoed back
    [InlineData("2024-11-05", "2024-11-05")]   // older but supported
    [InlineData("2025-11-25", "2025-11-25")]   // what mcp-proxy asks for
    [InlineData("1999-01-01", "2025-11-25")]   // nonsense, falls back to ours
    [InlineData("tomorrow", "2025-11-25")]     // not even a date
    public async Task Initialize_answers_with_a_protocol_version_we_actually_speak(
        string requested, string expected)
    {
        // Built as a JsonObject rather than a raw literal: this payload ends in "}}}", and a C#
        // raw interpolated string needs more '$' than it has consecutive closing braces. Quoting
        // it by hand is how you ship a test that fails for the wrong reason.
        var req = new System.Text.Json.Nodes.JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new System.Text.Json.Nodes.JsonObject
            {
                ["protocolVersion"] = requested,
                ["capabilities"] = new System.Text.Json.Nodes.JsonObject(),
                ["clientInfo"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["name"] = "probe", ["version"] = "1",
                },
            },
        };
        var res = await _site.Client.PostAsync("/mcp",
            new StringContent(req.ToJsonString(), Encoding.UTF8, "application/json"));
        var body = await res.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var actual = doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString();
        Xunit.Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Escaping happens exactly once, in the shell.
    ///
    /// Callers used to pass H(title) into a method that escaped it again, so the Constitution's
    /// tab and its Google result read "Gro&#223;herzogtums". The h1 hid it: that one interpolated
    /// raw, and the two mistakes cancelled out. Neither the fixture titles nor any snapshot
    /// contains a character that escapes, so nothing here would have noticed. This asserts the
    /// contract directly against a title built to break it.
    /// </summary>
    [Theory]
    [InlineData("Loi Grand-Ducale & Cie", "Loi Grand-Ducale &amp; Cie")]
    [InlineData("Verfassung des Großherzogtums", "Verfassung des Gro&#223;herzogtums")]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    public void A_title_is_escaped_once_in_the_tab_and_once_in_the_heading(string raw, string encoded)
    {
        var html = Lex.Web.PageShell.Page("https://golden.test", raw, "<p>body</p>");
        Xunit.Assert.Contains($"<title>{encoded}, Lex</title>", html);
        Xunit.Assert.Contains($"<h1>{encoded}</h1>", html);
        Xunit.Assert.Contains($"content=\"{encoded}, Lex\"", html);   // og:title
        // The tell for a second pass: an ampersand that has itself been escaped.
        Xunit.Assert.DoesNotContain("&amp;#", html);
        Xunit.Assert.DoesNotContain("&amp;amp;", html);
        Xunit.Assert.DoesNotContain("&amp;lt;", html);
    }

    /// <summary>
    /// The sitemap is the one page written for a machine that no human ever opens, which is
    /// exactly why it rots unwatched. Asserted as XML rather than as a snapshot: the corpus grows
    /// nightly, so the file is expected to change, but it must stay parseable, must name every
    /// work the catalogue holds, and must be the file robots.txt points at.
    /// </summary>
    [Fact]
    public async Task The_sitemap_lists_every_work_and_robots_points_at_it()
    {
        var res = await _site.Client.GetAsync("/sitemap.xml");
        Xunit.Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Xunit.Assert.Equal("application/xml", res.Content.Headers.ContentType?.MediaType);

        var doc = System.Xml.Linq.XDocument.Parse(await res.Content.ReadAsStringAsync());
        System.Xml.Linq.XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = doc.Root!.Elements(ns + "url").Select(u => u.Element(ns + "loc")!.Value).ToList();

        Xunit.Assert.All(locs, l => Xunit.Assert.StartsWith("https://golden.test/", l));
        Xunit.Assert.Equal(locs.Count, locs.Distinct().Count());
        Xunit.Assert.Contains("https://golden.test/", locs);
        Xunit.Assert.Contains("https://golden.test/browse", locs);
        // Every work in the fixture, addressed the way a reader reaches it.
        Xunit.Assert.Contains("https://golden.test/t-pub/w1", locs);

        // And every VERSION, which is where the law actually is. The first sitemap listed only
        // work pages, on the reasoning that versions were near-duplicates the work page links
        // anyway. They are not duplicates, each is a distinct legal state, and a work page is
        // navigation while a version page is the text someone searched for. Asserted separately
        // because the two sets are easy to conflate and only one of them carries content.
        var versions = locs.Where(l => l.StartsWith("https://golden.test/t-pub/w1/")).ToList();
        Xunit.Assert.NotEmpty(versions);
        Xunit.Assert.All(versions, v =>
            Xunit.Assert.Matches(@"^https://golden\.test/t-pub/w1/\d{4}-\d{2}-\d{2}$", v));

        var robots = await _site.Client.GetStringAsync("/robots.txt");
        Xunit.Assert.Contains("Sitemap: https://golden.test/sitemap.xml", robots);

        // lastmod is a last-modified time, so a future one is never valid. The first version of
        // this route used the work's latest valid_from, which is when a law takes EFFECT: 23 works
        // in the live corpus have a commencement date years out, one in 2030, and Search Console
        // rejected every one. Asserted against the tag rather than the source field, because the
        // bug was choosing the wrong field, not formatting it wrongly.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var lm in doc.Root!.Descendants(ns + "lastmod").Select(e => e.Value))
        {
            Xunit.Assert.True(DateOnly.TryParseExact(lm, "yyyy-MM-dd", out var d),
                $"lastmod '{lm}' is not YYYY-MM-DD");
            Xunit.Assert.True(d <= today, $"lastmod '{lm}' is in the future");
        }
    }

    /// <summary>
    /// A fixture corpus and the real app on top of it. Deterministic by construction: fixed dates,
    /// fixed hashes, a signing key generated once per run and normalised out of the snapshots.
    /// </summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        internal static readonly DateTimeOffset FixedNow =
            new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

        public readonly HttpClient Client;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-golden-{Guid.NewGuid():N}");

        public Site()
        {
            Directory.CreateDirectory(_dir);
            BuildFixtureIndex(Path.Combine(_dir, "index-t-pub.db"));
            Environment.SetEnvironmentVariable("LEX_INDEX_DIR", _dir);
            Environment.SetEnvironmentVariable("LEX_PUBLIC_BASE_URL", "https://golden.test");
            Client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
            });
        }

        private static void BuildFixtureIndex(string db)
        {
            var stamp = new Dictionary<string, string>
            {
                ["collection"] = "t-pub", ["publisher_name"] = "Test Publisher", ["tier"] = "A",
                ["history_begins"] = "publisher", ["built_at"] = "2026-01-01T00:00:00Z",
                ["corpus_commit"] = "goldenc", ["attribution"] = "Test attribution.",
            };
            DocRow Row(string group, string from, string? to, string kind, bool text, string title) =>
                new($"t-pub:{group}:{from}", "t-pub", group, $"urn:{group}", kind, "fr", from, to,
                    "publisher", "2026-01-01T00:00:00Z", Withdrawn: false, TextAvailable: text,
                    TextPublic: text, RecordSha: Sha($"{group}{from}"), BodySha: text ? Sha($"body{group}{from}") : null,
                    SourceUri: $"https://example.org/{group}/{from}", Title: title, TitleShort: title,
                    Body: null, PublicationDate: from, StatusNote: null);

            var docs = new[]
            {
                Row("w1", "2020-01-01", "2021-12-31", "LOI", true, "Loi sur la chose"),
                Row("w1", "2022-01-01", null, "LOI", true, "Loi sur la chose"),
                Row("w2", "2019-06-01", null, "RGD", false, "Reglement sans texte"),
            };
            ProvisionRow P(DocRow d, int seq, string anchor, string text) =>
                new($"{d.Key}|{d.Language}|{d.ValidFrom}", seq, anchor, $"{d.Key}#{anchor}", "article",
                    $"Art. {anchor[4..]}.", null, null, null, d.Title, text, Sha(text));
            var provisions = new[]
            {
                P(docs[0], 0, "art_1", "la chose s applique partout"),
                P(docs[0], 1, "art_2", "les peines sont legeres"),
                P(docs[1], 0, "art_1", "la chose s applique partout, revisee"),
                P(docs[1], 1, "art_2", "les peines sont legeres"),
            };
            IndexBuilder.Build(db, stamp, docs, provisions, [], [], StampSigner.CreateKeyPem());
        }

        private static string Sha(string s) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => utcNow;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
        }
    }
}

/// <summary>Snapshot comparison, and the normalisation that makes it meaningful.</summary>
internal static class Golden
{
    private static readonly string Dir = Path.Combine(RepoRoot(), "tests", "Lex.Tests", "golden");

    private static string RepoRoot()
    {
        var d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "Lex.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("Lex.slnx not found above the test binary.");
    }

    /// <summary>
    /// Replace what legitimately differs between two runs of the same code, and nothing else.
    /// Each pattern here is a value the app reads from the clock, the machine or a fresh key.
    /// </summary>
    public static string Normalise(string s)
    {
        s = Regex.Replace(s, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z", "<TIMESTAMP>");
        // Any date computed from the clock, expressed as its offset from today.
        //
        // Naming two offsets by hand was not enough: the pages also link "last month", so a
        // snapshot captured yesterday failed today by exactly one day. Every date within a
        // couple of years of now is rewritten as <TODAY-n>, which is stable whenever it was
        // captured. Dates further out are left alone: those come from the corpus and are the
        // facts under test.
        var today = GoldenTests.Site.FixedNow.UtcDateTime.Date;
        s = Regex.Replace(s, @"\d{4}-\d{2}-\d{2}", m =>
            DateOnly.TryParse(m.Value, out var d)
            && (today - d.ToDateTime(TimeOnly.MinValue)).TotalDays is var delta
            && delta is >= 0 and <= 800
                ? delta == 0 ? "<TODAY>" : $"<TODAY-{(int)delta}>"
                : m.Value);
        // A signing key is generated per test run, so every signature and every public key differs
        // between runs of identical code. Both arrive double-encoded, as JSON inside a JSON string,
        // so the quotes delimiting the value are backslash-escaped.
        //
        // Anchored on the key name rather than matched as base64: a generic base64 pattern also
        // matches a 64-character sha256, and those hashes are deterministic and must keep being
        // compared, since they are the whole provenance claim.
        s = Regex.Replace(s, @"((?:signature|public_key)\\"": \\"")(.*?)(\\"")",
                          "$1<KEY>$3", RegexOptions.Singleline);
        s = Regex.Replace(s, @"""(signature|public_key)""\s*:\s*""[^""]*""", @"""$1"": ""<KEY>""");
        s = Regex.Replace(s, @"-----BEGIN PUBLIC KEY-----.*?-----END PUBLIC KEY-----",
                          "<PEM>", RegexOptions.Singleline);
        // Line endings, in both encodings. System.Text.Json's indented writer uses the platform
        // newline, so a tool response serialised on Windows carries an ESCAPED \r\n inside the
        // JSON string while the same code on Linux emits \n. Normalising only the real newlines
        // made every tool snapshot fail in CI while passing locally, which is the worst shape a
        // test can have: green on the machine that wrote it.
        s = s.Replace("\\r\\n", "\\n");
        return s.Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    public static void Assert(string name, string actual)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, $"{name}.txt");

        if (Environment.GetEnvironmentVariable("LEX_GOLDEN_UPDATE") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, actual);
            return;
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (expected == actual) return;

        // A unified-ish first divergence, because a 60 KB page diff is unreadable in a test runner.
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
        throw new Xunit.Sdk.XunitException(
            $"""
            Golden mismatch in {name} at line {i + 1}.

            expected: {(i < e.Length ? e[i] : "<end of file>")}
              actual: {(i < a.Length ? a[i] : "<end of file>")}

            {expected.Length} vs {actual.Length} chars. If this change is intended, rerun with
            LEX_GOLDEN_UPDATE=1 and read the resulting diff before committing it.
            """);
    }
}
