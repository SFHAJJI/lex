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
    public void Every_producer_reason_is_deliberately_ruled_in_the_table()
    {
        // Deliberateness means the table carries a single-reason case for every emitted
        // reason, whatever lane it rules; semantic_work and semantic_concept are DELIBERATELY
        // unclassified_render (Codex B2 review O3), so a not-unclassified assertion would be
        // wrong, not strict.
        var table = CaseTable();
        var singleReasonCases = ((JsonArray)table["cases"]!).OfType<JsonObject>()
            .Where(item => ((JsonArray)item["reasons"]!).Count == 1)
            .Select(item => ((JsonArray)item["reasons"]!)[0]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var reason in ((JsonArray)table["producer_vocabulary"]!)
                     .Select(item => item!.GetValue<string>()))
            Assert.Contains(reason, singleReasonCases);
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

        // Provenance pins (Codex B2 review O3): the work vector must remain built from
        // subject metadata plus names, and the semantic arm must keep selecting only the
        // work evidence kind. If either changes, the semantic_work and semantic_concept
        // lane rulings must be re-made deliberately, so this canary fails first.
        Assert.Contains("\"subjects: \"", workSearch, StringComparison.Ordinal);
        Assert.Contains("evidence_kind='work'", workSearch, StringComparison.Ordinal);

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
    public void Notice_html_is_byte_bounded_validated_and_exact_hosted()
    {
        var withMatches = MatchLanes.NoticeHtml(["lu-legilux", "eu-eurlex"],
            [new MatchLanes.DisclosureRow(
                 "lu-legilux", "w1", "2024-01-01", "Loi sur les chiens <script>"),
             new MatchLanes.DisclosureRow(
                 "eu-eurlex", "reg-2", "2023-05-01", "Regulation two"),
             // Hostile and malformed rows are omitted, never rendered or linked.
             new MatchLanes.DisclosureRow("evil host", "w2", "2024-01-01", "bad publisher"),
             new MatchLanes.DisclosureRow("lu-legilux", "../../etc", "2024-01-01", "bad work"),
             new MatchLanes.DisclosureRow("lu-legilux", "w3", "not-a-date", "bad date"),
             // A duplicate logical work is disclosed once.
             new MatchLanes.DisclosureRow("lu-legilux", "w1", "2024-01-01", "Duplicate")]);
        Assert.StartsWith("<div class=\"notice\" role=\"note\"", withMatches, StringComparison.Ordinal);
        Assert.EndsWith("</div>", withMatches, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"metadata-only-notice\"", withMatches, StringComparison.Ordinal);
        Assert.Contains(MatchLanes.Heading, withMatches, StringComparison.Ordinal);
        Assert.Contains(MatchLanes.Body, withMatches, StringComparison.Ordinal);
        Assert.Contains($"<summary>{MatchLanes.DisclosureLabel}</summary>", withMatches, StringComparison.Ordinal);
        Assert.Equal(2, withMatches.Split("<li>").Length - 1);
        Assert.Contains("href=\"/lu-legilux/w1/2024-01-01\"", withMatches, StringComparison.Ordinal);
        Assert.Contains("https://legilux.public.lu", withMatches, StringComparison.Ordinal);
        Assert.Contains("https://eur-lex.europa.eu", withMatches, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", withMatches, StringComparison.Ordinal);
        Assert.DoesNotContain("evil host", withMatches, StringComparison.Ordinal);
        Assert.DoesNotContain("../../etc", withMatches, StringComparison.Ordinal);

        // All rows invalid: the primary notice survives with no disclosure shell, and an
        // unknown collection falls back internally, never to a guessed URL.
        var bare = MatchLanes.NoticeHtml(["x-unknown"],
            [new MatchLanes.DisclosureRow("evil host", "w", "2024-01-01", "t")]);
        Assert.Contains(MatchLanes.Heading, bare, StringComparison.Ordinal);
        Assert.DoesNotContain("<details>", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", bare, StringComparison.Ordinal);
        Assert.Contains("href=\"/search\"", bare, StringComparison.Ordinal);
    }

    [Fact]
    public void Disclosure_overflow_counts_only_valid_deduplicated_returned_matches()
    {
        var twelve = Enumerable.Range(0, 12).Select(index => new MatchLanes.DisclosureRow(
            "lu-legilux", $"w-{index}", "2024-01-01", $"Work {index}")).ToArray();
        var overflowing = MatchLanes.NoticeHtml(["lu-legilux"], twelve);
        Assert.Equal(10, overflowing.Split("<li>").Length - 1);
        Assert.Contains("and 2 more returned matches", overflowing, StringComparison.Ordinal);

        // Invalid and duplicate rows never inflate the count.
        var padded = twelve
            .Concat([new MatchLanes.DisclosureRow("evil host", "w-x", "2024-01-01", "bad"),
                     new MatchLanes.DisclosureRow("lu-legilux", "w-0", "2024-01-01", "dup")])
            .ToArray();
        Assert.Contains("and 2 more returned matches",
            MatchLanes.NoticeHtml(["lu-legilux"], padded), StringComparison.Ordinal);

        var exactlyTen = twelve.Take(10).ToArray();
        Assert.DoesNotContain("more returned matches",
            MatchLanes.NoticeHtml(["lu-legilux"], exactlyTen), StringComparison.Ordinal);
    }

    [Fact]
    public void Served_reasons_never_throw_on_a_hostile_member()
    {
        // B1+B2 review, O5: GetValue<string> throws on a number or object, which would take
        // the whole page down. A non-string member is an unknown reason, so the hit renders.
        var hostile = new JsonObject
        {
            ["match_reasons"] = new JsonArray(42, "work_metadata", new JsonObject()),
        };
        var reasons = MatchLanes.ReasonsOf(hostile);
        Assert.Equal(3, reasons.Count);
        Assert.Null(reasons[0]);
        Assert.Equal("work_metadata", reasons[1]);
        Assert.Null(reasons[2]);
        Assert.Equal(MatchLanes.UnclassifiedRender, MatchLanes.Classify(reasons));
        Assert.False(MatchLanes.MetadataOnly([reasons]),
            "an unclassifiable hit must never authorize suppression");
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
    public async Task Search_page_renders_one_notice_for_a_response_wide_metadata_state()
    {
        using var site = new MetadataSite(Path.Combine(_root, "metadata-only"));
        var page = await site.Client.GetStringAsync("/search?q=finances");

        // Both publishers matched only in facets: exactly ONE primary notice with one
        // disclosure over the complete population, official actions for both collections,
        // and zero result cards anywhere.
        Assert.Equal(1, page.Split("data-testid=\"metadata-only-notice\"").Length - 1);
        Assert.Contains(MatchLanes.Heading, page, StringComparison.Ordinal);
        Assert.Equal(1, page.Split($"<summary>{MatchLanes.DisclosureLabel}</summary>").Length - 1);
        Assert.Contains("Loi sur le budget annuel</a>", page, StringComparison.Ordinal);
        Assert.Contains("Regulation on annual estimates</a>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Loi sur le budget annuel</b>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"card\">", page, StringComparison.Ordinal);
        Assert.Contains("https://legilux.public.lu", page, StringComparison.Ordinal);
        Assert.Contains("https://eur-lex.europa.eu", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_page_mixed_across_publishers_keeps_the_old_path_everywhere()
    {
        using var site = new MetadataSite(Path.Combine(_root, "mixed"));
        var page = await site.Client.GetStringAsync("/search?q=sharedfin");

        // LU matched only in a facet while EU holds the token in provision text: the
        // response is mixed, so NO publisher is suppressed and no notice renders. This is
        // the exact regression a per-publisher classification would reintroduce (Codex O1).
        Assert.DoesNotContain("metadata-only-notice", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"card\">", page, StringComparison.Ordinal);
        Assert.Contains("<b>Loi sur le budget annuel</b>", page, StringComparison.Ordinal);
        Assert.Contains("<b>Regulation on annual estimates</b>", page, StringComparison.Ordinal);
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
    /// A real served two-publisher site. The token "finances" lives ONLY in both works'
    /// facets, so it is a response-wide metadata-only query; "sharedfin" is a facet on the
    /// LU work but provision TEXT on the EU work, so it is the cross-publisher mixed case
    /// the response-level rule must leave byte-for-byte on the old path (Codex O1).
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
            BuildLuIndex(Path.Combine(root, "index-lu-legilux.db"));
            BuildEuIndex(Path.Combine(root, "index-eu-eurlex.db"));
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

        private static void BuildLuIndex(string path)
        {
            var key = "lu-legilux:loi-fin-0001:2024-01-01--4b3f9be80e4e10f895cd5f2698dda45424b3966c2aa6aedf57e9383ee807f19f";
            // A GENUINE canonical version key. VersionIdentity mints
            // yyyy-MM-dd--sha256(publisher version identifier), and DateOf accepts nothing
            // else, so a bare-date fixture blessed a coordinate the producer cannot emit.
            var doc = new DocRow(
                key, "lu-legilux", "loi-fin-0001", "official:loi-fin-0001", "LOI", "fr",
                "2024-01-01", null, "publisher", "2026-08-14T00:00:00Z", false, true, true,
                Sha("fin"), null, "https://example.test/loi-fin-0001",
                "Loi sur le budget annuel", "Loi sur le budget annuel", null,
                "2024-01-01", null) with { Domains = "|finances|sharedfin|" };
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

        private static void BuildEuIndex(string path)
        {
            var key = "eu-eurlex:reg-fin-0002:2023-05-01--6df900a28da8636a2a1002a2f6ac7ec87842b1da6a86f27b825b625b25b25925";
            // A GENUINE canonical version key. VersionIdentity mints
            // yyyy-MM-dd--sha256(publisher version identifier), and DateOf accepts nothing
            // else, so a bare-date fixture blessed a coordinate the producer cannot emit.
            var doc = new DocRow(
                key, "eu-eurlex", "reg-fin-0002", "official:reg-fin-0002", "REG", "en",
                "2023-05-01", null, "publisher", "2026-08-14T00:00:00Z", false, true, true,
                Sha("eu-fin"), null, "https://example.test/reg-fin-0002",
                "Regulation on annual estimates", "Regulation on annual estimates", null,
                "2023-05-01", null) with { Domains = "|finances|" };
            var rid = $"{key}|en|2023-05-01";
            var provisions = new List<ProvisionRow>
            {
                new(rid, 1, "art_1", $"{key}#art_1", "article", "Article 1", null, null,
                    null, "Regulation on annual estimates",
                    "The sharedfin rules shall apply to all bodies.",
                    Sha("The sharedfin rules shall apply to all bodies.")),
            };
            IndexBuilder.Build(path, new Dictionary<string, string>
            {
                ["collection"] = "eu-eurlex", ["tier"] = "A",
                ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-14T00:00:00Z", ["corpus_commit"] = "test",
            }, [doc], provisions, [], [], StampSigner.CreateKeyPem());
        }
    }
}
