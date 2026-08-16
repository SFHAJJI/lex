using System.Net;
using Lex.Web;

namespace Lex.Tests;

public sealed class ArchitectureDossierTests : IClassFixture<GoldenTests.Site>
{
    private static readonly (string Path, string Label)[] Tabs =
    [
        ("/built", "Overview"),
        ("/built/model", "Legal model"),
        ("/built/data", "Data authority"),
        ("/built/retrieval", "Retrieval"),
        ("/built/assistant", "Assistant"),
        ("/built/release", "Release"),
        ("/built/decisions", "Trade-offs"),
        ("/built/incidents", "Incidents"),
        ("/built/limits", "Limits and scale"),
    ];

    private readonly HttpClient _client;

    public ArchitectureDossierTests(GoldenTests.Site site) => _client = site.Client;

    [Fact]
    public async Task The_nine_dossier_tabs_are_complete_and_self_navigable()
    {
        foreach (var (path, label) in Tabs)
        {
            using var response = await _client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains($"<link rel=\"canonical\" href=\"https://golden.test{path}\">", html);
            Assert.Contains($"<h1>{label}</h1>", html);
            Assert.Contains("<nav class=\"dossier-tabs\" aria-label=\"Architecture dossier\">", html);
            Assert.Contains($"href=\"{path}\" aria-current=\"page\">{label}</a>", html);
            Assert.Contains("<article class=\"architecture-dossier\">", html);
            Assert.All(Tabs, tab => Assert.Contains($"href=\"{tab.Path}\"", html));
        }
    }

    [Fact]
    public async Task Dossier_diagrams_are_owned_static_accessible_svg()
    {
        using var response = await _client.GetAsync("/built/diagrams/system.svg");
        var svg = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("role=\"img\"", svg);
        Assert.Contains("aria-labelledby=\"title desc\"", svg);
        Assert.Contains("<title id=\"title\">", svg);
        Assert.Contains("<desc id=\"desc\">", svg);
        Assert.Contains("id=\"reader-channels\"", svg);
        Assert.Contains("server-rendered + React", svg);
        Assert.Contains("typed plan + evidence", svg);
        Assert.Contains("HTTP + stdio, read-only", svg);
        Assert.DoesNotContain("d=\"M1015 218v-7\"", svg);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);

        using var missing = await _client.GetAsync("/built/diagrams/not-owned.svg");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Retrieval_page_exposes_keyboard_scroll_and_a_described_diagram()
    {
        var html = await _client.GetStringAsync("/built/retrieval");

        Assert.Contains("<div class=\"dossier-table\" tabindex=\"0\" role=\"region\" aria-label=\"Scrollable architecture table\"><table>", html);
        Assert.DoesNotContain("<table tabindex=", html);
        Assert.Contains("<img src=\"/built/diagrams/retrieval.svg\" alt=\"", html);
        Assert.Contains("Open the retrieval diagram at full size", html);
        Assert.Contains("Offline signed benchmarks authorize vector mounting", html);
        Assert.Contains("never switches an individual query", html);
        Assert.Contains("user or API caller explicitly chooses", html);

        var css = await _client.GetStringAsync("/dossier.css");
        Assert.Contains(".dossier-table {", css);
        Assert.Contains("overflow-x: auto;", css);
        Assert.Contains("overflow-wrap: anywhere;", css);
    }

    [Fact]
    public async Task Assistant_page_exposes_the_current_bounded_sequence_at_readable_size()
    {
        var html = await _client.GetStringAsync("/built/assistant");
        var svg = await _client.GetStringAsync("/built/diagrams/assistant.svg");
        var boundary = await _client.GetStringAsync("/built/diagrams/assistant-boundary.svg");
        var css = await _client.GetStringAsync("/dossier.css");

        Assert.Contains(
            "<div class=\"dossier-sequence\" tabindex=\"0\" role=\"region\" aria-label=\"Scrollable assistant sequence diagram\">",
            html);
        Assert.Contains(
            "<div class=\"dossier-boundary\" tabindex=\"0\" role=\"region\" aria-label=\"Scrollable assistant ownership and trust-boundary diagram\">",
            html);
        Assert.Contains("id=\"assistant-sequence\"", svg);
        Assert.Contains("Every message starts and ends on an owning lifeline", svg);
        Assert.Contains("RESOLVE DETERMINISTICALLY", svg);
        Assert.Contains("PLAN ONCE", svg);
        Assert.Contains("VALIDATE, CORRECT ONCE,", svg);
        Assert.Contains("EXECUTE WITHOUT", svg);
        Assert.Contains("TYPED RESULT", svg);
        Assert.Contains("OPTIONAL PROSE", svg);
        Assert.Contains("Admission + subject", svg);
        Assert.Contains("The planner receives schemas only. It never observes execution and never replans.", svg);
        Assert.DoesNotContain("Public MCP", svg);
        Assert.Contains("id=\"bounded-assistant-agent\"", boundary);
        Assert.Contains("id=\"deterministic-legal-truth\"", boundary);
        Assert.Contains("id=\"public-mcp-projection\"", boundary);
        Assert.Contains("Not the planner transport", boundary);
        Assert.Contains("direct reply default", boundary);
        Assert.Contains("same configured Azure OpenAI deployment", html);
        Assert.Contains("There is no hidden UI toggle", html);
        Assert.Contains("synthesis=true", html);
        Assert.Contains("ReWOO-inspired reasoning without an observation loop", html);
        Assert.Contains("Open-ended ReAct", html);
        Assert.Contains("Naive RAG with LLM-selected identity", html);
        Assert.Contains(".dossier-sequence {", css);
        Assert.Contains("min-width: 1200px;", css);
        Assert.Contains(".dossier-boundary {", css);
        Assert.Contains("min-width: 1000px;", css);
    }

    [Fact]
    public async Task Every_dossier_diagram_is_followed_by_an_explanatory_table()
    {
        foreach (var (path, _) in Tabs)
        {
            var html = await _client.GetStringAsync(path);
            var diagram = html.IndexOf("<img src=\"/built/diagrams/", StringComparison.Ordinal);
            while (diagram >= 0)
            {
                var nextDiagram = html.IndexOf(
                    "<img src=\"/built/diagrams/", diagram + 1, StringComparison.Ordinal);
                var table = html.IndexOf(
                    "<div class=\"dossier-table\" tabindex=\"0\" role=\"region\" aria-label=\"Scrollable architecture table\"><table>",
                    diagram + 1,
                    StringComparison.Ordinal);

                Assert.True(table > diagram && (nextDiagram < 0 || table < nextDiagram),
                    $"Expected an explanatory table after each diagram on '{path}'.");
                diagram = nextDiagram;
            }
        }
    }

    [Fact]
    public async Task Full_dossier_reuses_the_tab_sources_in_interview_order()
    {
        var html = await _client.GetStringAsync("/architecture/dossier");
        var headings = new[]
        {
            "Overview", "Legal model", "Data authority", "Retrieval", "Assistant",
            "Release", "Trade-offs", "Incidents", "Limits and scale",
        };

        Assert.Contains("<article class=\"architecture-dossier architecture-dossier-full\">", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\">", html);
        var prior = -1;
        foreach (var heading in headings)
        {
            var position = html.IndexOf($">{heading}</h2>", StringComparison.Ordinal);
            Assert.True(position > prior, $"Expected '{heading}' after the previous dossier section.");
            prior = position;
        }
    }

    [Fact]
    public void Canonical_dossier_sources_do_not_freeze_one_rollout_state()
    {
        var root = Golden.RepositoryRoot();
        var pages = Path.Combine(root, "docs", "architecture", "pages");
        var forbidden = new[]
        {
            "merged and gated",
            "still gated",
            "fresh v4 candidate must",
            "remain gated until",
            "until the fresh signed v4",
        };

        foreach (var path in Directory.EnumerateFiles(pages, "*.md"))
        {
            var markdown = File.ReadAllText(path);
            foreach (var phrase in forbidden)
                Assert.DoesNotContain(phrase, markdown, StringComparison.OrdinalIgnoreCase);
        }

        var registry = File.ReadAllText(Path.Combine(root, "docs", "architecture-program.json"));
        Assert.DoesNotContain("\"current\":", registry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_dossier_matches_the_single_replica_traffic_invariant()
    {
        var html = await _client.GetStringAsync("/built/release");
        var workflow = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), ".github", "workflows", "revision-traffic.yml"));

        Assert.Contains("min=max=1", html);
        Assert.DoesNotContain("0 to 5 replicas", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target must have exactly one pinned replica", workflow);
        Assert.Contains("rollback must have exactly one pinned replica", workflow);
    }

    [Fact]
    public async Task Release_dossier_explains_the_owned_assistant_evaluation_gate()
    {
        var html = await _client.GetStringAsync("/built/release");
        var productReview = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "docs", "product-architecture-review.md"));

        Assert.Contains("Evaluation reviewer", html);
        Assert.Contains("Soufien Hajji", html);
        Assert.Contains("Catalog author", html);
        Assert.Contains("Lex release engineering", html);
        Assert.Contains("25 frozen scenarios", html);
        Assert.Contains("not third-party review", html);
        Assert.Contains("Plan once", html);
        Assert.Contains("Runtime grounding judge", html);
        Assert.Contains("Release grader", html);
        Assert.Contains("Candidate token budget", html);
        Assert.Contains("Grader token budget", html);
        Assert.Contains("EUR 10", html);
        Assert.Contains("not a live Azure billing cutoff", html);
        Assert.Contains("setup and final turns", html);
        Assert.Contains("expected_synthesis", html);
        Assert.Contains("argument_alternatives", html);
        Assert.Contains("typed SSE", html);
        Assert.Contains("Any failed repetition", html);
        Assert.Contains("Publisher acquisition", html);
        Assert.Contains("Corpus release", html);
        Assert.Contains("Derived articles", html);
        Assert.Contains("SQLite and vectors", html);
        Assert.Contains("200-case deterministic retrieval benchmark", html);
        Assert.Contains("no large language model (LLM) grader", html);
        Assert.Contains("25-case live assistant evaluation", html);
        Assert.Contains("exact-byte publication transaction", html);
        Assert.Contains("Verified limits", html);
        Assert.Contains("Luxembourg hybrid", html);
        Assert.Contains("EU hybrid", html);
        Assert.Contains("image-only EU annex", html);
        Assert.Contains("25 frozen cases", productReview);
        Assert.Contains("620,000 candidate input tokens", productReview);
        Assert.Contains("980,000 grader input tokens", productReview);
        Assert.Contains("392,000 grader output tokens", productReview);
        Assert.DoesNotContain("20 frozen cases", productReview);
    }

    [Fact]
    public async Task Release_page_and_status_fail_closed_before_a_matching_signed_run_exists()
    {
        using var page = await _client.GetAsync("/built/release");
        var html = await page.Content.ReadAsStringAsync();
        using var status = await _client.GetAsync("/built/release/evaluation.json");
        var json = await status.Content.ReadAsStringAsync();

        Assert.Contains("No verified evaluation published.", html);
        Assert.Contains("href=\"/attestation.json\"", html);
        Assert.Contains("href=\"/built/release/evaluation.json\"", html);
        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", status.Headers.CacheControl?.ToString());
        Assert.Equal("application/json", status.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"schema\": \"lex-public-assistant-evaluation-status/1\"", json);
        Assert.Contains("\"status\": \"unavailable\"", json);
        Assert.DoesNotContain("report_sha256", json);
    }

    [Fact]
    public async Task Release_page_discloses_only_runtime_bound_signed_report_claims()
    {
        var evidence = new VerifiedAssistantEvaluationEvidence(
            Repository: "SFHAJJI/lex-ops",
            ReleaseTag: "assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb",
            ReleaseUrl: "https://github.com/SFHAJJI/lex-ops/releases/tag/assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb",
            ReportUrl: "https://github.com/SFHAJJI/lex-ops/releases/download/assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb/assistant-eval-report.json",
            ManifestUrl: "https://github.com/SFHAJJI/lex-ops/releases/download/assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb/assistant-eval.manifest.json",
            SignatureUrl: "https://github.com/SFHAJJI/lex-ops/releases/download/assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb/assistant-eval.manifest.sig",
            RunAt: "2026-08-15T12:00:00Z",
            CodeCommit: new string('a', 40),
            Revision: "ca-lex-web--candidate",
            RevisionHostname: "candidate.example",
            Image: "registry.example/lex@sha256:" + new string('c', 64),
            ArtifactManifestSet: new string('d', 64),
            CatalogSha256: new string('e', 64),
            ReportSha256: new string('b', 64),
            CandidateEvidenceSha256: new string('f', 64),
            CandidateModelHost: "candidate-models.example",
            CandidateDeployment: "candidate-release",
            CandidateModelName: "gpt-5-mini",
            CandidateModelVersion: "2025-08-07",
            GraderDeployment: "lex-assistant-eval-grader",
            GraderModelName: "gpt-5-nano",
            GraderModelVersion: "2025-08-07",
            IndexManifestIds: [new string('1', 64), new string('2', 64)],
            CaseCount: 25,
            RepetitionCount: 49,
            CandidateInputTokens: 1000,
            CandidateOutputTokens: 100,
            GraderInputTokens: 500,
            GraderOutputTokens: 50,
            TotalCostEur: 0.38m,
            MaximumCostEur: 10m,
            FirstOperationP95Milliseconds: 850,
            TotalP99Milliseconds: 2_500,
            BrowserP95Milliseconds: 42);
        using var site = new GoldenTests.Site(new AssistantEvaluationEvidenceSnapshot(evidence));
        using var page = await site.Client.GetAsync("/built/release");
        var html = await page.Content.ReadAsStringAsync();
        var json = await site.Client.GetStringAsync("/built/release/evaluation.json");

        Assert.Contains("Exact runtime match.", html);
        Assert.Contains("Signed report verdict: <b>passed</b>", html);
        Assert.Contains("Signed catalog: 25 cases /", html);
        Assert.Contains("49 repetitions", html);
        Assert.Contains("canonical release verifier checked", html);
        Assert.Contains("does not rerun or independently rescore", html);
        Assert.DoesNotContain("All 25 reviewed cases", html);
        Assert.Contains("EUR 0.3800 / EUR 10 maximum", html);
        Assert.Contains("first result p95 850 ms", html);
        Assert.Contains("running and evaluated code commit", html);
        Assert.Contains("signed report SHA-256", html);
        Assert.Contains("These hashes intentionally differ", html);
        Assert.Contains("\"status\": \"verified_signed_release\"", json);
        Assert.Contains("\"signed_report_verdict\": \"passed\"", json);
        Assert.Contains("\"cases\": 25", json);
        Assert.Contains("\"repetitions\": 49", json);
        Assert.Contains("\"code_commit\": \"" + new string('a', 40) + "\"", json);
        Assert.Contains("\"candidate_model\"", json);
        Assert.Contains("\"gpt-5-mini\"", json);
        Assert.Contains("\"grader_model\"", json);
        Assert.Contains("\"usage\"", json);
        Assert.Contains("\"latency_milliseconds\"", json);
        Assert.DoesNotContain("resource_id", json);
        Assert.DoesNotContain("reviewer_id", json);
        Assert.DoesNotContain("prompt", json);
        Assert.DoesNotContain("reply", json);
    }

    [Fact]
    public async Task Release_dossier_links_each_release_boundary_to_its_implementation()
    {
        var html = await _client.GetStringAsync("/built/release");
        var overview = await _client.GetStringAsync("/built");
        var retention = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "docs", "operations-retention.md"));
        var infrastructure = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "infra", "README.md"));
        var decisions = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "docs", "architecture-program.json"));
        var hybridRoadmap = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "docs", "hybrid-eu-roadmap.md"));
        var specification = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "docs", "lex-spec-v4.md"));

        Assert.Contains("Inspect the implementation", html);
        Assert.Contains("Links follow protected <code>main</code>", html);
        Assert.Contains("Signed evidence binds exact", html);
        Assert.Contains("commits and digests", html);
        Assert.Contains("Suggested reading order", html);
        Assert.Contains("GitHub Immutable Release", html);
        Assert.Contains("coordination evidence, not immutable storage", html);
        Assert.DoesNotContain("immutable Blob release", html);
        Assert.DoesNotContain("Release paths are never cleanup candidates", html);
        Assert.DoesNotContain("signed immutable Blob release", retention);
        Assert.Contains("There is no canonical Blob release", retention);
        Assert.Contains("GitHub Immutable", infrastructure);
        Assert.Contains("is the canonical durable artifact boundary", infrastructure);
        Assert.Contains("GitHub Immutable Release is the current canonical durable authority", decisions);
        Assert.DoesNotContain("Blob is the durable distribution layer", decisions);
        Assert.Contains("GitHub Immutable Release", hybridRoadmap);
        Assert.Contains("publication through Blob is never automatic", hybridRoadmap);
        Assert.DoesNotContain("published through Blob instead of GitHub Releases", hybridRoadmap);
        Assert.DoesNotContain("downloads and verifies a release from Blob", hybridRoadmap);
        Assert.Contains("blocks the current publication path", specification);
        Assert.Contains("separately reviewed and authorized write-once-read-many publication design", specification);
        Assert.DoesNotContain("moves publication to Blob", specification);
        Assert.Contains("Status legend", html);
        Assert.Contains("Built", html);
        Assert.Contains("Measured", html);
        Assert.Contains("Quarantined", html);
        Assert.Contains("Next", html);
        Assert.Contains("Worked evidence trace: General Data Protection Regulation (GDPR) Article 6", html);
        Assert.Contains("eu-eurlex:32016r0679:2016-05-04--af3e8edc", html);
        Assert.Contains("record SHA-256", html);
        Assert.Contains("dffea205327743e03f21c6910a899b7bfc081e40905defd085ab9d52dbb3fc87", html);
        Assert.Contains("as_of(work=eu-eurlex:32016r0679", html);
        Assert.Contains("#art_6", html);
        Assert.Equal(4, Count(html, "Why this design?"));
        Assert.Equal(1, Count(html, "<img src=\"/built/diagrams/"));
        Assert.Equal(1, Count(overview, "<img src=\"/built/diagrams/"));

        var expectedLinks = new[]
        {
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Sources.EurLex/EurLexAdapter.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Derive/DeriveWriter.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ingest/IndexFromCorpus.cs",
            "https://github.com/SFHAJJI/lex/blob/main/evals/retrieval-cases.json",
            "https://github.com/SFHAJJI/lex/blob/main/evals/retrieval-baseline-v2.json",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Index/RetrievalBenchmark.cs",
            "https://github.com/SFHAJJI/lex-ops/blob/main/.github/workflows/publish-prebuilt-index.yml",
            "https://github.com/SFHAJJI/lex-ops/blob/main/publish-prebuilt-index.sh",
            "https://github.com/SFHAJJI/lex/blob/main/deploy/fetch-indexes.sh",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Web/IndexRegistry.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ask/AskService.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Mcp/McpCore.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ask/UiMapper.cs",
            "https://github.com/SFHAJJI/lex/blob/main/evals/assistant-cases-v3.json",
            "https://github.com/SFHAJJI/lex/blob/main/evals/assistant-cases-v3.review.json",
            "https://github.com/SFHAJJI/lex/blob/main/evals/assistant-cases-v3.review.sig",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ingest/AssistantEvaluationRunner.cs",
            "https://github.com/SFHAJJI/lex-ops/blob/main/.github/workflows/publish-assistant-evaluation.yml",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Web/AssistantEvaluationEvidence.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Web/AssistantEvaluationEvidenceProvider.cs",
            "https://github.com/SFHAJJI/lex/blob/main/src/Lex.Web/BuiltEndpoints.cs",
            "https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/AssistantEvaluationEvidenceTests.cs",
            "https://github.com/SFHAJJI/lex/blob/main/.github/workflows/revision-traffic.yml",
            "https://github.com/SFHAJJI/lex/blob/main/docs/architecture/pages/limits.md",
            "https://github.com/SFHAJJI/lex/blob/main/docs/architecture/pages/incidents.md",
        };
        Assert.All(expectedLinks, link => Assert.Contains($"href=\"{link}\"", html));
        Assert.Contains("href=\"/benchmarks/latest.json\"", html);
        Assert.Contains("href=\"/built/release/evaluation.json\"", html);
        Assert.DoesNotContain("#L", html);

        using var benchmark = await _client.GetAsync("/benchmarks/latest.json");
        Assert.Equal(HttpStatusCode.NotFound, benchmark.StatusCode);
        Assert.Equal("application/json", benchmark.Content.Headers.ContentType?.MediaType);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var position = source.IndexOf(value, StringComparison.Ordinal);
             position >= 0;
             position = source.IndexOf(value, position + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void Evaluation_matrix_distinguishes_owner_signed_catalog_from_unexecuted_candidate()
    {
        var matrix = File.ReadAllText(Path.Combine(
            Golden.RepositoryRoot(), "docs", "assistant-evaluation-scenario-matrix.md"));

        Assert.Contains("Owner-reviewed and signed", matrix);
        Assert.Contains("e21876f96bd0dc6dd720b3c8878daac00baed2b96560b04d99470fabdb015376", matrix);
        Assert.Contains("candidate has already passed the live run", matrix);
        Assert.Contains("Live LLM release evaluation", matrix);
        Assert.Contains("Deterministic integration", matrix);
        Assert.Contains("Browser and UI", matrix);
        Assert.Contains("Controlled transport and chaos", matrix);
        Assert.Contains("succeeded_empty", matrix);
        Assert.Contains("no post-freeze replanning", matrix);
        Assert.Contains("clarification round-trip", matrix);
        Assert.Contains("Luxembourg", matrix);
        Assert.Contains("25 live cases", matrix);
        Assert.Contains("lu-constitution-article", matrix);
        Assert.Contains("lu-profile-not-comparable", matrix);
        Assert.Contains("gdpr-article-and-timeline", matrix);
        Assert.Contains("clarification-continues-with-identity", matrix);
    }

    [Fact]
    public async Task Dated_benchmark_baseline_is_never_presented_as_current_or_live()
    {
        var html = await _client.GetStringAsync("/benchmarks");

        Assert.Contains("Historical measured service baseline", html);
        Assert.Contains("corpus commits at measurement", html);
        Assert.Contains("historical context", html);
        Assert.DoesNotContain("Current service baseline", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live corpus commits", html, StringComparison.OrdinalIgnoreCase);
    }
}
