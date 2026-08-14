using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.Index;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
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
        var renderedBody = body.Length == 0 ? "" : Golden.Normalise(body);
        Golden.Assert($"page-{name}", $"HTTP {(int)res.StatusCode}\n{renderedBody}");
    }

    [Fact]
    public async Task Coverage_keeps_cards_outside_coverage_tables()
    {
        var html = await _site.Client.GetStringAsync("/coverage");
        Assert.DoesNotMatch(new Regex("<table[^>]*>(?:(?!</table>).)*<div\\b",
            RegexOptions.Singleline | RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task About_cv_downloads_use_canonical_api_urls()
    {
        var html = await _site.Client.GetStringAsync("/about");

        Assert.Contains("https://api.soufien.lu/cv/en/download", html);
        Assert.Contains("https://api.soufien.lu/cv/fr/download", html);
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
    public async Task Navigation_groups_engineering_evidence_and_keeps_one_canonical_developer_page()
    {
        var home = await _site.Client.GetStringAsync("/");
        Assert.Contains("<details class=\"proofnav\"", home);
        Assert.Contains("<summary aria-expanded=\"false\">Check the work</summary>", home);
        foreach (var path in new[]
                 {
                     "/how-it-works", "/coverage", "/architecture", "/decisions",
                     "/benchmarks", "/verify", "/built", "/about",
                 })
            Assert.Contains($"href=\"{path}\"", home);
        Assert.Contains("href=\"/built\"><b>I want to inspect the engineering</b>", home);
        Assert.DoesNotContain("href=\"/about\"><b>I want to know who built this</b>", home);
        Assert.Contains("href=\"/developers#assistant\">Connect your own AI</a>", home);
        Assert.DoesNotContain("href=\"/ai\"", home);

        using var redirect = await _site.Client.GetAsync("/ai");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("/developers#assistant", redirect.Headers.Location?.OriginalString);
        var developers = await _site.Client.GetStringAsync("/developers");
        Assert.Contains("id=\"assistant\"", developers);

        using var askRedirect = await _site.Client.GetAsync("/ask");
        Assert.Equal(HttpStatusCode.MovedPermanently, askRedirect.StatusCode);
        Assert.Equal("/", askRedirect.Headers.Location?.OriginalString);
    }

    [Fact]
    public void Public_route_ledger_matches_the_mounted_product_endpoints()
    {
        var ledgerPath = Path.Combine(Golden.RepositoryRoot(), "docs", "public-route-ledger.md");
        var ledger = File.ReadLines(ledgerPath)
            .Select(line => Regex.Match(line, @"^\| (GET|POST) \| `([^`]+)` \|"))
            .Where(match => match.Success)
            .Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}")
            .ToHashSet(StringComparer.Ordinal);

        static string PublicPattern(string raw)
        {
            if (raw.StartsWith("/mcp", StringComparison.Ordinal)) return "/mcp";
            var segments = raw.Split('/');
            if (segments.Length > 1 && segments[1].StartsWith("{publisher:", StringComparison.Ordinal))
                segments[1] = "{publisher}";
            return string.Join('/', segments);
        }

        var mounted = _site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var raw = endpoint.RoutePattern.RawText ?? "";
                if (raw.StartsWith("/mcp", StringComparison.Ordinal))
                    return ["POST /mcp"];
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
                return methods
                    .Where(method => method is "GET" or "POST")
                    .Select(method => $"{method} {PublicPattern(raw)}");
            })
            .Where(route => route != "GET /site.js" && !route.StartsWith("GET /app/", StringComparison.Ordinal)
                         && !route.StartsWith("GET /fonts/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ledger.Order(), mounted.Order());
    }

    [Fact]
    public async Task Static_search_controls_keep_accessible_names_and_mobile_bounds()
    {
        var changed = await _site.Client.GetStringAsync("/changed?from=2019-01-01&to=2023-01-01");
        var search = await _site.Client.GetStringAsync("/search?q=travail");

        Assert.Contains("name=\"order\" aria-label=\"Change ranking\"", changed);
        Assert.Contains("form.inline select { min-width:0; max-width:100% }", search);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("kind")]
    [InlineData("publisher")]
    public async Task Static_search_returns_a_bounded_bad_request_for_invalid_input(string field)
    {
        var response = await _site.Client.GetAsync(
            "/search?" + (field == "q" ? "" : "q=thing&")
            + field + "=" + new string('x', 1001));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_request", body, StringComparison.Ordinal);
        Assert.InRange(body.Length, 1, 100_000);
    }

    [Fact]
    public async Task Static_search_renders_the_interval_arrow_without_mojibake()
    {
        var search = await _site.Client.GetStringAsync("/search?q=chose");

        Assert.Contains(" → ", search, StringComparison.Ordinal);
        Assert.DoesNotContain("â†’", search, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_golden_baseline_fails_outside_update_mode()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-golden-{Guid.NewGuid():N}.txt");

        var error = Assert.Throws<Xunit.Sdk.XunitException>(() =>
            Golden.AssertFile(missing, "actual", updateMode: false));

        Assert.Contains("Missing golden baseline", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task Architecture_wide_table_is_keyboard_scrollable()
    {
        var html = await _site.Client.GetStringAsync("/architecture");

        Assert.Contains("<table tabindex=\"0\" aria-label=\"Mounted index collections\">", html);
    }

    [Fact]
    public async Task Static_catalogue_and_wide_evidence_tables_remain_accessible()
    {
        var browse = await _site.Client.GetStringAsync("/browse");
        var scopedBrowse = await _site.Client.GetStringAsync("/browse?publisher=t-pub&type=LOI");
        var inForce = await _site.Client.GetStringAsync("/in-force-on?date=2021-01-01");
        var coverage = await _site.Client.GetStringAsync("/coverage");

        Assert.Contains(".filters .n { opacity:1;", browse);
        Assert.Contains("<details class=\"facetgroup\">", browse);
        Assert.Contains("<b>Record only:</b> identity and timeline held; no searchable provision text.", browse);
        Assert.Contains(">full text</span>", browse);
        Assert.Contains(">record only</span>", browse);
        Assert.Contains("Law <span class=\"mono raw\">LOI</span>", scopedBrowse);
        Assert.Contains(">full text</span>", scopedBrowse);
        Assert.Contains("href=\"/browse?publisher=t-pub\"", scopedBrowse);
        Assert.DoesNotContain("publisher=t-pub&amp;type=LOI\">Test Publisher", scopedBrowse);
        Assert.Contains("<select name=\"kind\" aria-label=\"Source class\">", inForce);
        Assert.Contains("<table tabindex=\"0\" aria-label=\"Text coverage by document type\">", coverage);

        var expected = new Dictionary<string, string[]>
        {
            ["/architecture/next"] = ["Architecture delivery milestones"],
            ["/decisions"] = ["Architecture decision register"],
            ["/built"] = ["Mounted index provenance", "Correctness evaluation layers"],
        };
        foreach (var (path, labels) in expected)
        {
            var html = await _site.Client.GetStringAsync(path);
            foreach (var label in labels)
                Assert.Contains($"<table tabindex=\"0\" aria-label=\"{label}\">", html);
        }
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
    public async Task Every_response_gets_the_browser_security_baseline()
    {
        using var response = await _site.Client.GetAsync("/");
        Assert.Equal("max-age=10886400; includeSubDomains; preload",
            response.Headers.GetValues("Strict-Transport-Security").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(), geolocation=(), microphone=(), payment=(), usb=()",
            response.Headers.GetValues("Permissions-Policy").Single());
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
        Assert.Contains("engineer-reviewed retrieval judgments", benchmarks);
    }

    [Fact]
    public async Task Architecture_delivery_page_marks_its_primary_navigation_parent_current()
    {
        var html = await _site.Client.GetStringAsync("/architecture/next");

        Assert.Contains("<a href=\"/architecture\" aria-current=\"page\">Architecture</a>", html);
    }

    [Fact]
    public async Task Attestation_distinguishes_manifest_trust_from_embedded_stamp_provenance()
    {
        var json = await _site.Client.GetStringAsync("/attestation.json");
        var attestation = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

        Assert.Contains("whole-artifact manifests", attestation["artifact_trust"]!.GetValue<string>());
        Assert.Contains("lex-artifacts/1", attestation["artifact_signature_binds"]!.GetValue<string>());
        Assert.Contains("canonical stamp text", attestation["embedded_stamp_signature_binds"]!.GetValue<string>());
        Assert.NotNull(attestation["signature_binds"]); // compatibility contract
    }

    [Fact]
    public async Task Public_retrieval_judgments_are_downloadable_and_complete()
    {
        var json = await _site.Client.GetStringAsync("/benchmarks/cases.json");
        var cases = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();
        Assert.Equal(200, cases.Count);
        Assert.All(cases, c =>
        {
            Assert.Equal("engineer-reviewed", c!["review_status"]!.GetValue<string>());
            Assert.Contains(c["split"]!.GetValue<string>(), new[] { "tuning", "holdout" });
            var collection = c["collection"]!.GetValue<string>();
            Assert.All(c["relevant_works"]!.AsArray(), work =>
                Assert.StartsWith(collection + ":", work!.GetValue<string>()));
        });
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
        var body = await McpJson(res);
        // A malformed request returns an empty body, and an empty snapshot would happily become
        // the baseline and then "pass" forever. The first version of this file did exactly that.
        Xunit.Assert.True(body.Length > 40, $"{tool} returned {body.Length} chars: {body}");
        Golden.Assert($"tool-{name}", Golden.Normalise(body));
    }

    [Fact]
    public async Task Static_search_and_mcp_use_the_same_current_retrieval_contract()
    {
        var page = await _site.Client.GetStringAsync("/search?q=chose");
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "search",
                ["arguments"] = new JsonObject
                {
                    ["query"] = "chose", ["limit"] = 15,
                    ["time_scope"] = "as_of", ["as_of"] = "2026-08-05",
                    ["retrieval_mode"] = "keyword", ["fuzzy"] = "auto",
                },
            },
        };
        var response = await _site.Client.PostAsync("/mcp",
            new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        var wire = JsonNode.Parse(await McpJson(response))!.AsObject();
        var text = wire["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "[]";
        var envelopes = JsonNode.Parse(text)!.AsArray();
        var hits = envelopes[0]?["hits"]!.AsArray() ?? [];

        Assert.NotEmpty(hits);
        Assert.All(hits.OfType<JsonObject>(), hit =>
            Assert.Contains(hit["provision_id"]!.GetValue<string>(), page, StringComparison.Ordinal));
        Assert.DoesNotContain("t-pub:w1:2020-01-01#", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_advertised_tool_list_renders_exactly_as_it_did()
    {
        var res = await _site.Client.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"));
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);
        Golden.Assert("tools-list", Golden.Normalise(await McpJson(res)));
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
    [InlineData("tomorrow", null)]              // malformed versions are protocol errors
    public async Task Initialize_answers_with_a_protocol_version_we_actually_speak(
        string requested, string? expected)
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
        var body = await McpJson(res);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        if (expected is null)
        {
            Xunit.Assert.True(doc.RootElement.TryGetProperty("error", out _), body);
            return;
        }
        var actual = doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString();
        Xunit.Assert.Equal(expected, actual);
    }

    private static async Task<string> McpJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") return body;

        var data = body.Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => line[6..])
            .LastOrDefault();
        if (data is null)
            throw new Xunit.Sdk.XunitException($"MCP SSE response carried no data event: {body}");

        // The SDK owns wire framing and may choose harmless JSON escaping or member ordering.
        // Canonicalize the envelope so existing snapshots continue proving that the tool payloads
        // themselves did not change during the transport migration.
        var parsed = System.Text.Json.Nodes.JsonNode.Parse(data)!.AsObject();
        var canonical = new System.Text.Json.Nodes.JsonObject
        {
            ["jsonrpc"] = parsed["jsonrpc"]?.DeepClone(),
            ["id"] = parsed["id"]?.DeepClone(),
        };
        if (parsed["result"] is { } result) canonical["result"] = result.DeepClone();
        if (parsed["error"] is { } error) canonical["error"] = error.DeepClone();
        return canonical.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            // The official SDK deliberately uses the framework's conservative encoder on the
            // wire. Re-emitting with the relaxed encoder restores the old snapshot spelling
            // (\" instead of \u0022 inside text blocks) without changing the decoded value.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Assistant_assets_are_emitted_only_when_the_page_requests_them(bool enabled)
    {
        var html = Lex.Web.PageShell.Page("https://golden.test", "Page", "<p>body</p>",
            canonicalPath: "/browse", assetVersion: "abc 123", assistant: enabled);

        Xunit.Assert.Equal(enabled, html.Contains("id=\"assistant-root\"", StringComparison.Ordinal));
        Xunit.Assert.Equal(enabled, html.Contains("workspace.js?v=abc%20123", StringComparison.Ordinal));
        Xunit.Assert.Equal(enabled, html.Contains("workspace.css?v=abc%20123", StringComparison.Ordinal));
        Xunit.Assert.Equal(enabled, html.Contains("class=\"assistant-enabled\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Actual_routes_mount_one_assistant_only_on_research_pages()
    {
        var researchPages = new[]
        {
            "/browse", "/find", "/search?q=travail", "/changed?from=2019-01-01&to=2023-01-01",
            "/in-force-on?date=2021-01-01", "/stories", "/t-pub/w1",
            "/t-pub/w1/2022-06-01", "/t-pub/w1/diff/2020-01-01/2022-01-01",
        };
        foreach (var path in researchPages)
        {
            var html = await _site.Client.GetStringAsync(path);
            Assert.Contains("class=\"assistant-enabled\"", html);
            Assert.Single(Regex.Matches(html, "id=\"assistant-root\"").Cast<Match>());
            Assert.Single(Regex.Matches(html, "/app/workspace\\.js\\?v=").Cast<Match>());
        }

        var home = await _site.Client.GetStringAsync("/");
        Assert.Contains("class=\"assistant-enabled\"", home);
        Assert.DoesNotContain("id=\"assistant-root\"", home);
        Assert.Single(Regex.Matches(home, "/app/workspace\\.js\\?v=").Cast<Match>());

        foreach (var path in new[]
        {
            "/about", "/architecture", "/architecture/next", "/decisions", "/verify",
            "/developers", "/built", "/how-it-works", "/coverage", "/benchmarks",
        })
        {
            var html = await _site.Client.GetStringAsync(path);
            Assert.DoesNotContain("class=\"assistant-enabled\"", html);
            Assert.DoesNotContain("assistant-root", html);
            Assert.DoesNotContain("/app/workspace.js", html);
        }
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
            var appDir = Path.Combine(_dir, "wwwroot", "app");
            Directory.CreateDirectory(appDir);
            // The web job owns the real Vite build and DOM smoke test. This isolated asset keeps
            // the server suite self-contained while exercising the production static-file route
            // and immutable cache policy from a clean checkout.
            File.WriteAllText(Path.Combine(appDir, "workspace.js"), "/* golden fixture */\n");
            BuildFixtureIndex(Path.Combine(_dir, "index-t-pub.db"));
            Client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            Client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _dir);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://golden.test");
            builder.UseWebRoot(Path.Combine(_dir, "wwwroot"));
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
    private static readonly string Dir = Path.Combine(RepositoryRoot(), "tests", "Lex.Tests", "golden");

    public static string RepositoryRoot()
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
        var path = Path.Combine(Dir, $"{name}.txt");
        AssertFile(path, actual,
            Environment.GetEnvironmentVariable("LEX_GOLDEN_UPDATE") == "1");
    }

    internal static void AssertFile(string path, string actual, bool updateMode)
    {
        if (updateMode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }
        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Missing golden baseline: {path}. Run with LEX_GOLDEN_UPDATE=1, review the new file, and commit it.");

        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (expected == actual) return;

        // A unified-ish first divergence, because a 60 KB page diff is unreadable in a test runner.
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
        throw new Xunit.Sdk.XunitException(
            $"""
            Golden mismatch in {Path.GetFileNameWithoutExtension(path)} at line {i + 1}.

            expected: {(i < e.Length ? e[i] : "<end of file>")}
              actual: {(i < a.Length ? a[i] : "<end of file>")}

            {expected.Length} vs {actual.Length} chars. If this change is intended, rerun with
            LEX_GOLDEN_UPDATE=1 and read the resulting diff before committing it.
            """);
    }
}
