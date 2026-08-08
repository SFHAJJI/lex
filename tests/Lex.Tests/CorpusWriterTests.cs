using System.Text.Json;
using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed class CorpusWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-writer-{Guid.NewGuid():N}");

    public CorpusWriterTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Existing_record_refreshes_normalized_metadata_with_an_append_only_event()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("publisher_metadata", "old-domain"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var path = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(path), CorpusJson.Options)!;

        Assert.Equal("in_force", meta.Raw["binding_status"]);
        Assert.Equal("financial-services", meta.Raw["domains"]);
        var revision = Assert.Single(meta.Events, e => e.Event == "metadata_revised");
        Assert.Equal("2026-08-06T00:00:00Z", revision.ObservedFrom);
        Assert.Contains("raw.binding_status", revision.Detail);
        Assert.Contains("raw.domains", revision.Detail);
    }

    [Fact]
    public async Task Ingest_reports_a_real_expression_denominator_and_completion()
    {
        using var progress = new StringWriter();
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), progress)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var lines = progress.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, line => line.Contains(
            "[progress] test: ingest expressions=0/1 percent=0.0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(
            "[progress] test: ingest expressions=1/1 percent=100.0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("eta=00:00:00 current=w1", StringComparison.Ordinal));

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.Expressions);
        Assert.Equal(0, manifest.ExpressionsWithText);
        Assert.Equal(1, manifest.ExpressionsWithoutText);
        Assert.Equal(ManifestDoc.CurrentPublisherDiscoverySchema,
            manifest.PublisherDiscoverySchema);
    }

    [Fact]
    public async Task Existing_record_adds_a_newly_available_language_by_identity()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services", ["en"]), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services", ["en", "fr"]), default);

        var path = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(path), CorpusJson.Options)!;

        Assert.Equal(["en", "fr"], meta.Expressions.Select(e => e.Language));
        var added = Assert.Single(meta.Events, e => e.Event == "expression_added");
        Assert.Equal("fr", added.Scope);
        Assert.Equal("language=fr", added.Detail);
    }

    [Fact]
    public async Task Existing_record_refreshes_publisher_discovery_metadata_and_expression_short_title()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services", titleShort: "GDPR - Regulation"), default);
        var publisherMetadata = new[]
        {
            new PublisherMetadataRecord(
                "publisher_short_title",
                "http://publications.europa.eu/ontology/cdm#expression_title_short",
                "en",
                "gdpr, personal data protection",
                "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679"),
        };

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services", titleShort: "Regulation",
                publisherMetadata: publisherMetadata, documentRoles: ["delegated"]), default);

        var path = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(path), CorpusJson.Options)!;

        Assert.Equal("Regulation", Assert.Single(meta.Expressions).TitleShort);
        Assert.Equal(publisherMetadata, meta.PublisherMetadata);
        Assert.Equal(["delegated"], meta.DocumentRoles);
        var revision = meta.Events.Last(e => e.Event == "metadata_revised");
        Assert.Contains("expressions.en.title_short", revision.Detail);
        Assert.Contains("publisher_metadata", revision.Detail);
        Assert.Contains("document_roles", revision.Detail);
    }

    [Fact]
    public async Task Missing_publisher_record_is_tombstoned_and_can_be_resighted()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-03T00:00:00Z"))
            .WriteAsync(new EmptyAdapter(), default);

        var path = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "meta.json");
        var withdrawn = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        Assert.Equal("withdrawn_from_source", withdrawn.Events[^1].Event);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var resighted = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        Assert.Equal("resighted", resighted.Events[^1].Event);
    }

    [Fact]
    public async Task Manifest_records_expected_works_that_produced_no_versions()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance", hasVersions: false), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.ScopeExpectedWorks);
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("no_versions", issue.Code);
        Assert.Equal("w1", issue.Work);
    }

    [Fact]
    public void Legacy_manifest_without_expected_scope_keeps_inventory_unavailable()
    {
        var manifest = JsonSerializer.Deserialize<ManifestDoc>("""
            {
              "publisher":{"id":"test"},
              "tier":"A",
              "attribution":"test",
              "text_included":false,
              "text_public":false,
              "history_begins":"publisher",
              "ingester_version":"old"
            }
            """, CorpusJson.Options)!;

        Assert.Null(manifest.ScopeExpectedWorks);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class OneVersionAdapter(
        string bindingStatus,
        string domain,
        IReadOnlyList<string>? languages = null,
        string titleShort = "Work one",
        IReadOnlyList<PublisherMetadataRecord>? publisherMetadata = null,
        IReadOnlyList<string>? documentRoles = null,
        bool hasVersions = true) : ISourceAdapter
    {
        private readonly WorkRef _work = new(new Identifier("official:w1"), "w1", "REG", "Work one");

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], languages ?? ["en"], TextIncluded: false, TextPublic: false, HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return _work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
        {
            if (!hasVersions) return Task.FromResult<IReadOnlyList<VersionRecord>>([]);
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier("official:v1"), _work.Id, "REG", new DateOnly(2024, 1, 1), null,
                    "publisher", "true", new DateOnly(2024, 1, 1),
                    (languages ?? ["en"]).Select(language => new ExpressionRecord(
                        language, new DateOnly(2024, 1, 1), null, "publisher",
                        "Work one", titleShort, $"https://example.test/v1/{language}")).ToArray(),
                    [], new Dictionary<string, string>
                    {
                        ["binding_status"] = bindingStatus,
                        ["domains"] = domain,
                    }, publisherMetadata, documentRoles)
            ];
            return Task.FromResult(versions);
        }

        public Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    private sealed class EmptyAdapter : ISourceAdapter
    {
        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en"], TextIncluded: false, TextPublic: false, HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>([]);

        public Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }
}
