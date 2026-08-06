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

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class OneVersionAdapter(
        string bindingStatus, string domain, IReadOnlyList<string>? languages = null) : ISourceAdapter
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
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier("official:v1"), _work.Id, "REG", new DateOnly(2024, 1, 1), null,
                    "publisher", "true", new DateOnly(2024, 1, 1),
                    (languages ?? ["en"]).Select(language => new ExpressionRecord(
                        language, new DateOnly(2024, 1, 1), null, "publisher",
                        "Work one", "Work one", $"https://example.test/v1/{language}")).ToArray(),
                    [], new Dictionary<string, string>
                    {
                        ["binding_status"] = bindingStatus,
                        ["domains"] = domain,
                    })
            ];
            return Task.FromResult(versions);
        }

        public Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }
}
