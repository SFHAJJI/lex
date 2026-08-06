using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed class CorpusIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"lex-corpus-integrity-{Guid.NewGuid():N}");

    public CorpusIntegrityTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Verification_binds_metadata_and_every_observation_to_its_bytes()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new TextAdapter(), default);

        var valid = CorpusIntegrity.Verify(_dir);
        Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Errors));
        Assert.Equal(1, valid.ActualWorks);
        Assert.Equal(1, valid.ActualVersions);
        Assert.Equal(2, valid.Expressions);
        Assert.Equal(2, valid.Observations);

        var body = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "en.html");
        await File.AppendAllTextAsync(body, "tampered");

        var invalid = CorpusIntegrity.Verify(_dir);
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.EndsWith("en.html sha256 mismatch", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class TextAdapter : ISourceAdapter
    {
        private static readonly WorkRef Work = new(
            new Identifier("official:w1"), "w1", "REG", "Work one");

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en", "fr"], TextIncluded: true, TextPublic: true, HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return Work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
        {
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier("official:v1"), Work.Id, "REG", new DateOnly(2024, 1, 1), null,
                    "publisher", "true", new DateOnly(2024, 1, 1),
                    [
                        new("en", new DateOnly(2024, 1, 1), null, "publisher", "Work one", "Work one", "https://example.test/en"),
                        new("fr", new DateOnly(2024, 1, 1), null, "publisher", "Texte un", "Texte un", "https://example.test/fr"),
                    ],
                    [], new Dictionary<string, string> { ["consolidation_status"] = "published" })
            ];
            return Task.FromResult(versions);
        }

        public Task<string?> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult<string?>($"<html lang=\"{expression.Language}\">official</html>");
    }
}
