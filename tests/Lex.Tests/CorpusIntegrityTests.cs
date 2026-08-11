using Lex.Ingest;
using Lex.Law;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        var beforeRepair = await File.ReadAllBytesAsync(body);
        var repair = CheckoutLineEndings.Repair(_dir);
        Assert.False(repair.IsValid);
        Assert.Single(repair.Unresolved);
        Assert.Equal(beforeRepair, await File.ReadAllBytesAsync(body));
    }

    [Fact]
    public async Task Checkout_repair_restores_only_bytes_that_match_the_recorded_lf_hash()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new TextAdapter(multiline: true), default);

        var body = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "en.html");
        var publisherBytes = await File.ReadAllBytesAsync(body);
        var checkoutBytes = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(publisherBytes).Replace("\n", "\r\n", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(body, checkoutBytes);

        var invalid = CorpusIntegrity.Verify(_dir);
        Assert.Contains(invalid.Errors, error => error.EndsWith(
            "en.html sha256 mismatch (LF-normalized bytes match; checkout line endings changed)",
            StringComparison.Ordinal));

        var repair = CheckoutLineEndings.Repair(_dir);
        Assert.True(repair.IsValid, string.Join(Environment.NewLine, repair.Unresolved));
        Assert.Equal(1, repair.Repaired);
        Assert.Equal(publisherBytes, await File.ReadAllBytesAsync(body));
        Assert.True(CorpusIntegrity.Verify(_dir).IsValid);
    }

    [Fact]
    public async Task Ingestion_refreshes_a_stale_record_hash_without_inventing_an_event()
    {
        var adapter = new TextAdapter();
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(adapter, default);

        var metaPath = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        var eventCount = meta.Events.Count;
        meta.RecordSha256 = new string('0', 64);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var writer = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-07T00:00:00Z"));
        await writer.WriteAsync(adapter, default);

        var repaired = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        Assert.Equal(1, writer.Updated);
        Assert.Equal(eventCount, repaired.Events.Count);
        Assert.True(CorpusIntegrity.Verify(_dir).IsValid);
    }

    [Fact]
    public async Task Record_hash_uses_one_cross_platform_canonical_serialization()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new TextAdapter(), default);

        var metaPath = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;

        Assert.Equal(
            "811743eb26717658450f6dcb520c6d2700b50e0bd50b5115ccfb68b2f24e2ca0",
            meta.RecordSha256);
    }

    [Fact]
    public async Task Acquisition_contract_fields_must_be_bounded_and_complete()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"))
            .WriteAsync(new TextAdapter(), default);

        var manifestPath = Path.Combine(_dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;

        manifest.AcquisitionRetryMaximumAttempts = 0;
        manifest.BuildIssues = [new SourceBuildIssue("", "w1", new string('x', 2001))];
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");

        var invalid = CorpusIntegrity.Verify(_dir);
        Assert.Contains(invalid.Errors, error => error.Contains(
            "acquisition_retry_maximum_attempts", StringComparison.Ordinal));
        Assert.Contains(invalid.Errors, error => error.Contains(
            "build issue 1 has an invalid code", StringComparison.Ordinal));
        Assert.Contains(invalid.Errors, error => error.Contains(
            "build issue 1 has an oversized detail", StringComparison.Ordinal));

        manifest.AcquisitionRetryMaximumAttempts = 1;
        var explicitNull = JsonNode.Parse(JsonSerializer.Serialize(manifest, CorpusJson.Options))!;
        explicitNull["build_issues"] = null;
        await File.WriteAllTextAsync(manifestPath, explicitNull.ToJsonString() + "\n");
        Assert.Contains(CorpusIntegrity.Verify(_dir).Errors,
            error => error == "manifest build_issues must be an array");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class TextAdapter(bool multiline = false) : ISourceAdapter
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

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(SourceBodyFetch.Retrieved(multiline
                ? $"<html lang=\"{expression.Language}\">\nofficial\n</html>"
                : $"<html lang=\"{expression.Language}\">official</html>"));
    }
}
