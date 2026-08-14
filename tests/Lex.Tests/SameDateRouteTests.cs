using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lex.Tests;

public sealed class SameDateRouteTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-same-date-route-{Guid.NewGuid():N}");
    private readonly SameDateSite _site;

    public SameDateRouteTests()
    {
        Directory.CreateDirectory(_root);
        _site = new SameDateSite(_root);
    }

    [Fact]
    public async Task Exact_same_date_version_keys_are_addressable_and_bare_date_refuses_ambiguity()
    {
        var firstKey = "2025-07-28--" + new string('a', 64);
        var secondKey = "2025-07-28--" + new string('b', 64);

        var ambiguous = await _site.Client.GetAsync("/same/work/2025-07-28");
        var ambiguousBody = await ambiguous.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);
        Assert.Contains("ambiguous_version", ambiguousBody, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{firstKey}", ambiguousBody, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{secondKey}", ambiguousBody, StringComparison.Ordinal);

        var later = await _site.Client.GetAsync("/same/work/2025-08-01");
        var laterBody = await later.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
        Assert.Contains("ambiguous_version", laterBody, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{firstKey}", laterBody, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{secondKey}", laterBody, StringComparison.Ordinal);

        var first = await _site.Client.GetStringAsync($"/same/work/{firstKey}");
        var second = await _site.Client.GetStringAsync($"/same/work/{secondKey}");
        Assert.Contains($"same:work:{firstKey}", first, StringComparison.Ordinal);
        Assert.Contains($"same:work:{secondKey}", second, StringComparison.Ordinal);
        Assert.DoesNotContain($"same:work:{secondKey}", first, StringComparison.Ordinal);
        Assert.DoesNotContain($"same:work:{firstKey}", second, StringComparison.Ordinal);

        var byPublisherIdentifier = await _site.Client.GetStringAsync(
            $"/same/{Uri.EscapeDataString("official:work")}/{firstKey}");
        Assert.Contains($"same:work:{firstKey}", byPublisherIdentifier,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_refuses_ambiguous_dates_and_compares_exact_same_date_version_keys()
    {
        var firstKey = "2025-07-28--" + new string('a', 64);
        var secondKey = "2025-07-28--" + new string('b', 64);

        var ambiguous = await _site.Client.GetAsync(
            "/same/work/diff/2025-07-28/2025-07-28");
        Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);
        var ambiguousBody = await ambiguous.Content.ReadAsStringAsync();
        Assert.Contains("ambiguous_version", ambiguousBody, StringComparison.Ordinal);
        Assert.Contains(firstKey, ambiguousBody, StringComparison.Ordinal);
        Assert.Contains(secondKey, ambiguousBody, StringComparison.Ordinal);

        var later = await _site.Client.GetAsync(
            "/same/work/diff/2025-08-01/2025-08-02");
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
        Assert.Contains("ambiguous_version",
            await later.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var exact = await _site.Client.GetAsync($"/same/work/diff/{firstKey}/{secondKey}");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        var exactBody = await exact.Content.ReadAsStringAsync();
        Assert.Contains($"same:work:{firstKey}", exactBody, StringComparison.Ordinal);
        Assert.Contains($"same:work:{secondKey}", exactBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_version_page_offers_only_openable_exact_collision_links()
    {
        var firstKey = "2025-07-28--" + new string('a', 64);
        var secondKey = "2025-07-28--" + new string('b', 64);

        var response = await _site.Client.GetAsync("/same/work/2020-01-01");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no_version_for_date", body, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{firstKey}", body, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{secondKey}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/same/work/2025-07-28\"", body,
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK,
            (await _site.Client.GetAsync($"/same/work/{firstKey}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _site.Client.GetAsync($"/same/work/{secondKey}")).StatusCode);
    }

    [Fact]
    public async Task Sitemap_lists_each_exact_publisher_state_instead_of_an_ambiguous_date()
    {
        var firstKey = "2025-07-28--" + new string('a', 64);
        var secondKey = "2025-07-28--" + new string('b', 64);

        var sitemap = await _site.Client.GetStringAsync("/sitemap.xml");

        Assert.Contains($"/same/work/{firstKey}", sitemap, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{secondKey}", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("<loc>https://example.test/same/work/2025-07-28</loc>",
            sitemap, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_force_page_surfaces_exact_choices_for_an_ambiguous_work()
    {
        var firstKey = "2025-07-28--" + new string('a', 64);
        var secondKey = "2025-07-28--" + new string('b', 64);

        var response = await _site.Client.GetAsync("/in-force-on?date=2025-08-01&publisher=same");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ambiguous_version", body, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{firstKey}", body, StringComparison.Ordinal);
        Assert.Contains($"/same/work/{secondKey}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/same/work/2025-08-01\"", body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multilingual_expressions_are_one_version_unit_on_work_and_version_pages()
    {
        var firstKey = "2020-01-01--" + new string('c', 64);
        var secondKey = "2021-01-01--" + new string('d', 64);

        var workPage = await _site.Client.GetStringAsync("/same/constitution");
        Assert.Contains("2 version(s)", workPage, StringComparison.Ordinal);
        Assert.Contains("Constitution francaise", workPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Deutsche Verfassung", workPage, StringComparison.Ordinal);

        var versionPage = await _site.Client.GetStringAsync($"/same/constitution/{firstKey}");
        Assert.Contains("Constitution francaise", versionPage, StringComparison.Ordinal);
        Assert.Contains($"/same/constitution/{secondKey}", versionPage, StringComparison.Ordinal);
        Assert.DoesNotContain($"/same/constitution/{firstKey}\">next version", versionPage,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _site.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class SameDateSite : WebApplicationFactory<Program>
    {
        private readonly string _root;
        public HttpClient Client { get; }

        public SameDateSite(string root)
        {
            _root = root;
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "app"));
            File.WriteAllText(Path.Combine(root, "wwwroot", "app", "workspace.js"), "/* test */\n");
            BuildIndex(Path.Combine(root, "index-same.db"));
            Client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _root);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://example.test");
            builder.UseWebRoot(Path.Combine(_root, "wwwroot"));
        }

        protected override void Dispose(bool disposing)
        {
            Client.Dispose();
            base.Dispose(disposing);
        }

        private static void BuildIndex(string path)
        {
            var firstKey = "2025-07-28--" + new string('a', 64);
            var secondKey = "2025-07-28--" + new string('b', 64);
            DocRow Row(string key, string marker) => new(
                $"same:work:{key}", "same", "work", "official:work", "REG", "en",
                "2025-07-28", null, "publisher", "2026-08-14T00:00:00Z", false,
                false, false, Sha(marker), null, $"https://example.test/{marker}",
                $"Version {marker}", $"Version {marker}", null, "2025-07-28", null);
            var constitutionFirst = "2020-01-01--" + new string('c', 64);
            var constitutionSecond = "2021-01-01--" + new string('d', 64);
            DocRow Constitution(string key, string language, string title, string? validTo) => new(
                $"same:constitution:{key}", "same", "constitution", "official:constitution",
                "Constitution", language, key[..10], validTo, "publisher",
                "2026-08-14T00:00:00Z", false, false, false, Sha(title), null,
                $"https://example.test/constitution/{language}/{key}", title, title, null,
                key[..10], null);
            IndexBuilder.Build(path, new Dictionary<string, string>
            {
                ["collection"] = "same", ["tier"] = "A", ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-14T00:00:00Z", ["corpus_commit"] = "test",
            }, [
                Row(firstKey, "first"), Row(secondKey, "second"),
                Constitution(constitutionFirst, "de", "Deutsche Verfassung", "2020-12-31"),
                Constitution(constitutionFirst, "fr", "Constitution francaise", "2020-12-31"),
                Constitution(constitutionFirst, "lb", "Letzebuerger Verfassung", "2020-12-31"),
                Constitution(constitutionSecond, "fr", "Constitution francaise", null),
            ], [], [], [],
                StampSigner.CreateKeyPem());
        }

        private static string Sha(string value) => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
