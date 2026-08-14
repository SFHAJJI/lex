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
        ("/built/decisions", "Decisions"),
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
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);

        using var missing = await _client.GetAsync("/built/diagrams/not-owned.svg");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Retrieval_page_exposes_keyboard_scroll_and_a_described_diagram()
    {
        var html = await _client.GetStringAsync("/built/retrieval");

        Assert.Contains("<table tabindex=\"0\" aria-label=\"Scrollable architecture table\">", html);
        Assert.Contains("<img src=\"/built/diagrams/retrieval.svg\" alt=\"", html);
        Assert.Contains("Open the retrieval diagram at full size", html);
    }

    [Fact]
    public async Task Full_dossier_reuses_the_tab_sources_in_interview_order()
    {
        var html = await _client.GetStringAsync("/architecture/dossier");
        var headings = new[]
        {
            "Overview", "Legal model", "Data authority", "Retrieval", "Assistant",
            "Release", "Decisions", "Incidents", "Limits and scale",
        };

        Assert.Contains("<article class=\"architecture-dossier architecture-dossier-full\">", html);
        var prior = -1;
        foreach (var heading in headings)
        {
            var position = html.IndexOf($">{heading}</h2>", StringComparison.Ordinal);
            Assert.True(position > prior, $"Expected '{heading}' after the previous dossier section.");
            prior = position;
        }
    }
}
