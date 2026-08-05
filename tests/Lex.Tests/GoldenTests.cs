using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lex.Index;
using Microsoft.AspNetCore.Mvc.Testing;

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
    /// A fixture corpus and the real app on top of it. Deterministic by construction: fixed dates,
    /// fixed hashes, a signing key generated once per run and normalised out of the snapshots.
    /// </summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
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
        var today = DateTime.UtcNow.Date;
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
