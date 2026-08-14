using System.Net;

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
