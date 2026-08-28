using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.Index;
using Lex.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lex.Tests;

/// <summary>
/// B2 metadata_only (Decision 41). The lane classifier is bound to the one normative case
/// table its TypeScript twin loads too, the served search page suppresses metadata-only hits
/// behind the typed notice, and a drift canary derives the complete match-reason vocabulary
/// from the producer sources so a new upstream reason cannot silently change rendering.
/// </summary>
public sealed class MatchLaneTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-match-lane-{Guid.NewGuid():N}");

    public MatchLaneTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static JsonObject CaseTable()
    {
        var path = Path.Combine(RepoRoot(), "tests", "Lex.Tests", "match-lane-cases.json");
        return (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
    }

    [Fact]
    public void Every_normative_case_classifies_identically_in_csharp()
    {
        var cases = (JsonArray)CaseTable()["cases"]!;
        Assert.True(cases.Count >= 28, "the table must keep its full case set");
        foreach (var item in cases.OfType<JsonObject>())
        {
            var reasons = ((JsonArray)item["reasons"]!)
                .Select(reason => reason?.GetValue<string>()).ToArray();
            Assert.Equal(
                item["lane"]!.GetValue<string>(),
                MatchLanes.Classify(reasons));
        }
    }

    [Fact]
    public void Every_producer_reason_is_deliberately_classified()
    {
        var table = CaseTable();
        foreach (var reason in ((JsonArray)table["producer_vocabulary"]!)
                     .Select(item => item!.GetValue<string>()))
            Assert.NotEqual(MatchLanes.UnclassifiedRender, MatchLanes.Classify([reason]));
        var prefix = table["ambiguous_prefix"]!.GetValue<string>();
        Assert.Equal(MatchLanes.Identity, MatchLanes.Classify([prefix + "exact_title"]));
    }

    [Fact]
    public void Metadata_only_requires_at_least_one_hit_and_all_positive_metadata()
    {
        Assert.False(MatchLanes.MetadataOnly([]));
        Assert.True(MatchLanes.MetadataOnly([new[] { "work_metadata" }]));
        Assert.True(MatchLanes.MetadataOnly(
            [new[] { "work_metadata" }, new[] { "work_metadata" }]));
        Assert.False(MatchLanes.MetadataOnly(
            [new[] { "work_metadata" }, new[] { "keyword" }]));
        Assert.False(MatchLanes.MetadataOnly(
            [new[] { "work_metadata" }, new[] { "exact_title" }]));
        Assert.False(MatchLanes.MetadataOnly(
            [new[] { "work_metadata" }, new[] { "never_seen_reason" }]));
        Assert.False(MatchLanes.MetadataOnly([Array.Empty<string>()]));
    }

    /// <summary>
    /// The Codex Q1 amendment: the canary must cover the COMPLETE reason vocabulary the
    /// producer code emits, not merely reasons occurring in a fixture corpus. It derives that
    /// vocabulary from the ranking sources read-only and fails when the table and the code
    /// disagree in either direction, so a new upstream reason forces a deliberate lane ruling.
    /// </summary>
    [Fact]
    public void Drift_canary_derives_the_complete_producer_vocabulary()
    {
        var root = RepoRoot();
        var workSearch = File.ReadAllText(
            Path.Combine(root, "src", "Lex.Index", "WorkSearch.cs"));
        var indexReader = File.ReadAllText(
            Path.Combine(root, "src", "Lex.Index", "IndexReader.cs"));
        var derived = new SortedSet<string>(StringComparer.Ordinal);

        // Identity lanes: the AddExact prefixes crossed with the suffix-switch arms.
        var prefixes = Regex.Matches(workSearch, "AddExact\\([^;]*?\"(exact|contained)\"")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.NotEmpty(prefixes);
        var suffixSwitch = Regex.Match(
            workSearch, "var suffix = kind switch\\s*\\{(?<arms>[^}]*)\\}",
            RegexOptions.Singleline);
        Assert.True(suffixSwitch.Success, "the identity suffix switch moved; retune the canary");
        var suffixes = Regex.Matches(suffixSwitch.Groups["arms"].Value, "=> \"([a-z_]+)\"")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.NotEmpty(suffixes);
        foreach (var prefix in prefixes)
            foreach (var suffix in suffixes)
                derived.Add($"{prefix}_{suffix}");

        // Flat WorkMatch reasons: direct literals and the AddFtsMatches reason argument.
        foreach (Match match in Regex.Matches(
                     workSearch, "new WorkMatch\\(\\s*[^,]+,\\s*\"([a-z_]+)\""))
            derived.Add(match.Groups[1].Value);
        foreach (Match match in Regex.Matches(
                     workSearch, "AddFtsMatches\\([^;]*?, \"([a-z_]+)\"\\)"))
            derived.Add(match.Groups[1].Value);

        // The semantic kind ternary mints two reasons.
        var ternary = Regex.Match(
            workSearch, "== \"work\" \\? \"([a-z_]+)\" : \"([a-z_]+)\"");
        Assert.True(ternary.Success, "the semantic reason ternary moved; retune the canary");
        derived.Add(ternary.Groups[1].Value);
        derived.Add(ternary.Groups[2].Value);

        // Retrieval-hit reason arrays in IndexReader: literal arrays assigned to
        // MatchReasons, the legacyReasons arms, and arrays inside RetrievalHit arguments.
        // The lookbehind excludes indexer accesses: a literal array is never preceded by an
        // identifier character, a quote, ')' or ']'.
        var literalArray = new Regex(
            "(?<![\\w\\)\\]\"])\\[(?<items>\"[a-z_]+\"(?:,\\s*\"[a-z_]+\")*)\\]");
        void Harvest(string window)
        {
            foreach (Match match in literalArray.Matches(window))
                foreach (Match item in Regex.Matches(match.Groups["items"].Value, "\"([a-z_]+)\""))
                    derived.Add(item.Groups[1].Value);
        }
        foreach (Match match in Regex.Matches(
                     indexReader, "MatchReasons = (\\[[^\\]]*\\])"))
            Harvest(match.Groups[1].Value);
        var legacyReasons = Regex.Match(
            indexReader, "legacyReasons =[^;]*;", RegexOptions.Singleline);
        Assert.True(legacyReasons.Success, "the legacy reason arms moved; retune the canary");
        foreach (Match match in Regex.Matches(
                     legacyReasons.Value, "new\\[\\] \\{ (?<items>\"[a-z_]+\"(?:, \"[a-z_]+\")*) \\}"))
            foreach (Match item in Regex.Matches(match.Groups["items"].Value, "\"([a-z_]+)\""))
                derived.Add(item.Groups[1].Value);
        foreach (Match match in Regex.Matches(indexReader, "new RetrievalHit\\("))
            Harvest(indexReader.Substring(
                match.Index, Math.Min(500, indexReader.Length - match.Index)));

        // The ambiguity wrapper composes over resolution reasons and stays a prefix.
        var table = CaseTable();
        Assert.Contains("\"ambiguous_\" +", indexReader, StringComparison.Ordinal);
        Assert.Equal("ambiguous_", table["ambiguous_prefix"]!.GetValue<string>());

        var pinned = ((JsonArray)table["producer_vocabulary"]!)
            .Select(item => item!.GetValue<string>())
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        Assert.Equal(pinned, derived.ToArray());
    }

    [Fact]
    public void Notice_html_is_byte_bounded_with_frozen_copy_and_exact_hosts()
    {
        var withMatches = MatchLanes.NoticeHtml("lu-legilux",
            [("/lu-legilux/w1/2024-01-01", "Loi sur les chiens <script>", "w1 · matched in metadata")]);
        Assert.StartsWith("<div class=\"notice\" role=\"note\"", withMatches, StringComparison.Ordinal);
        Assert.EndsWith("</div>", withMatches, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"metadata-only-notice\"", withMatches, StringComparison.Ordinal);
        Assert.Contains(MatchLanes.Heading, withMatches, StringComparison.Ordinal);
        Assert.Contains(MatchLanes.Body, withMatches, StringComparison.Ordinal);
        Assert.Contains($"<summary>{MatchLanes.DisclosureLabel}</summary>", withMatches, StringComparison.Ordinal);
        Assert.Contains("https://legilux.public.lu", withMatches, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", withMatches, StringComparison.Ordinal);

        var euNotice = MatchLanes.NoticeHtml("eu-eurlex", []);
        Assert.Contains("https://eur-lex.europa.eu", euNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("<details>", euNotice, StringComparison.Ordinal);

        // An unknown collection falls back to the internal search page, never a guessed URL.
        var unknownCollection = MatchLanes.NoticeHtml("x-unknown", []);
        Assert.DoesNotContain("https://", unknownCollection, StringComparison.Ordinal);
        Assert.Contains("href=\"/search\"", unknownCollection, StringComparison.Ordinal);
    }

    [Fact]
    public void Served_reasons_read_fail_open_to_unclassified()
    {
        Assert.Empty(MatchLanes.ReasonsOf(new JsonObject()));
        Assert.Empty(MatchLanes.ReasonsOf(new JsonObject { ["match_reasons"] = "keyword" }));
        Assert.Equal(MatchLanes.UnclassifiedRender,
            MatchLanes.Classify(MatchLanes.ReasonsOf(new JsonObject())));
        var typed = MatchLanes.ReasonsOf(new JsonObject
        {
            ["match_reasons"] = new JsonArray("work_metadata"),
        });
        Assert.Equal(MatchLanes.Metadata, MatchLanes.Classify(typed));
    }

    [Fact]
    public async Task Search_page_suppresses_metadata_only_hits_behind_the_notice()
    {
        using var site = new MetadataSite(Path.Combine(_root, "metadata-only"));
        var page = await site.Client.GetStringAsync("/search?q=finances");

        Assert.Contains("data-testid=\"metadata-only-notice\"", page, StringComparison.Ordinal);
        Assert.Contains(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Contains(MatchLanes.DisclosureLabel, page, StringComparison.Ordinal);
        // The match is disclosed as a plain link, never rendered as a result card.
        Assert.Contains("Loi sur le budget annuel</a>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Loi sur le budget annuel</b>", page, StringComparison.Ordinal);
        Assert.Contains("matched in metadata", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_page_with_text_hits_renders_cards_and_no_notice()
    {
        using var site = new MetadataSite(Path.Combine(_root, "text-hit"));
        var page = await site.Client.GetStringAsync("/search?q=dispositions");

        Assert.DoesNotContain("metadata-only-notice", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"card\">", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_page_with_identity_hit_renders_cards_and_no_notice()
    {
        using var site = new MetadataSite(Path.Combine(_root, "identity-hit"));
        var page = await site.Client.GetStringAsync("/search?q=loi-fin-0001");

        Assert.DoesNotContain("metadata-only-notice", page, StringComparison.Ordinal);
        Assert.Contains("<b>Loi sur le budget annuel</b>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_page_with_no_hits_keeps_the_existing_empty_state()
    {
        using var site = new MetadataSite(Path.Combine(_root, "no-hit"));
        var page = await site.Client.GetStringAsync("/search?q=zzzznotalaw");

        Assert.DoesNotContain("metadata-only-notice", page, StringComparison.Ordinal);
        Assert.DoesNotContain(MatchLanes.Heading, page, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lex.slnx")))
            directory = Directory.GetParent(directory)?.FullName
                        ?? throw new InvalidOperationException("Repository root not found.");
        return directory;
    }

    private static string Sha(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// A real served site whose one work matches the query token only through work metadata
    /// (its title carries "finances", no provision text does), so a real keyword search
    /// produces a work_metadata-only response through the production retrieval path.
    /// </summary>
    private sealed class MetadataSite : WebApplicationFactory<Program>
    {
        private readonly string _root;
        public HttpClient Client { get; }

        public MetadataSite(string root)
        {
            _root = root;
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "app"));
            File.WriteAllText(
                Path.Combine(root, "wwwroot", "app", "workspace.js"), "/* test */\n");
            BuildIndex(Path.Combine(root, "index-lu-legilux.db"));
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _root);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://example.test");
            builder.UseWebRoot(Path.Combine(_root, "wwwroot"));
        }

        protected override void Dispose(bool disposing)
        {
            Client?.Dispose();
            base.Dispose(disposing);
        }

        private static void BuildIndex(string path)
        {
            var key = "lu-legilux:loi-fin-0001:2024-01-01";
            var doc = new DocRow(
                key, "lu-legilux", "loi-fin-0001", "official:loi-fin-0001", "LOI", "fr",
                "2024-01-01", null, "publisher", "2026-08-14T00:00:00Z", false, true, true,
                Sha("fin"), null, "https://example.test/loi-fin-0001",
                "Loi sur le budget annuel", "Loi sur le budget annuel", null,
                "2024-01-01", null) with { Domains = "|finances|" };
            var rid = $"{key}|fr|2024-01-01";
            var provisions = new List<ProvisionRow>
            {
                new(rid, 1, "art_1", $"{key}#art_1", "article", "Art. 1er", null, null,
                    null, "Loi sur le budget annuel",
                    "Les dispositions generales s'appliquent a tout organisme.",
                    Sha("Les dispositions generales s'appliquent a tout organisme.")),
            };
            IndexBuilder.Build(path, new Dictionary<string, string>
            {
                ["collection"] = "lu-legilux", ["tier"] = "A",
                ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-14T00:00:00Z", ["corpus_commit"] = "test",
            }, [doc], provisions, [], [], StampSigner.CreateKeyPem());
        }
    }
}
