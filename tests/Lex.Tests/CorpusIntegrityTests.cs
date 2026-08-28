using Lex.Ingest;
using Lex.Law;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Tests;

public sealed class CorpusIntegrityTests : IDisposable
{
    private const string CodeCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BuilderCommit = "dddddddddddddddddddddddddddddddddddddddd";
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"lex-corpus-integrity-{Guid.NewGuid():N}");

    public CorpusIntegrityTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Verification_binds_metadata_and_every_observation_to_its_bytes()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);

        var valid = CorpusIntegrity.Verify(_dir);
        Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Errors));
        Assert.Equal(1, valid.ActualWorks);
        Assert.Equal(1, valid.ActualVersions);
        Assert.Equal(2, valid.Expressions);
        Assert.Equal(2, valid.Observations);

        var body = Path.Combine(VersionDirectory, "en.html");
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
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(multiline: true), default);

        var body = Path.Combine(VersionDirectory, "en.html");
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
    public async Task V3_ingestion_rejects_a_stale_record_hash_without_rewriting_it()
    {
        var adapter = new TextAdapter();
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(adapter, default);

        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.RecordSha256 = new string('0', 64);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        var before = await File.ReadAllBytesAsync(metaPath);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-07T00:00:00Z"), CodeCommit)
                .WriteAsync(adapter, default));

        Assert.Contains("record_sha256 mismatch", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(metaPath));
        Assert.False(CorpusIntegrity.Verify(_dir).IsValid);
    }

    [Fact]
    public async Task Record_hash_uses_one_cross_platform_canonical_serialization()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);

        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;

        Assert.Equal(
            "f6fc09887eb7783f3ff1d2fa902e650a9d92988dc3e26597646037d693693d30",
            meta.RecordSha256);
    }

    [Fact]
    public async Task Acquisition_contract_fields_must_be_bounded_and_complete()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
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

    [Fact]
    public async Task Version_four_manifest_rejects_a_tampered_ingester_identity()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        manifest.IngesterCodeCommit = "tampered";
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains(
            "ingester_code_commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Version_four_rejects_tampered_source_configuration_and_unknown_metadata_kinds()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var manifestPath = Path.Combine(_dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
        manifest.SourceConfigurationSha256 = new string('1', 64);
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");

        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.PublisherMetadata =
        [
            new PublisherMetadataRecord(
                "invented_legal_classification", "https://example.test/concept", "en",
                "Invented classification", "https://example.test/concept"),
        ];
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);

        Assert.Contains(report.Errors, error => error.Contains(
            "source_configuration_sha256 must be null", StringComparison.Ordinal));
        Assert.Contains(report.Errors, error => error.Contains(
            "publisher_metadata is invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Version_three_remains_read_only_integrity_input_for_fresh_migration()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        manifest.Schema = "lex-corpus/3";
        manifest.IngesterCodeCommit = null;
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal("lex-corpus/3", report.Schema);
        Assert.Null(report.IngesterCodeCommit);
    }

    [Fact]
    public async Task Renamed_version_directory_is_rejected_before_derive_or_index()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var renamed = Path.Combine(Path.GetDirectoryName(VersionDirectory)!,
            "2024-01-01--" + new string('0', 64));
        Directory.Move(VersionDirectory, renamed);

        var report = CorpusIntegrity.Verify(_dir);
        Assert.Contains(report.Errors, error => error.Contains(
            "identity mismatch", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => Lex.Derive.DeriveWriter.Derive(
            _dir, Path.Combine(_dir, "derived"), "test", CodeCommit,
            "dddddddddddddddddddddddddddddddddddddddd",
            "cccccccccccccccccccccccccccccccccccccccc"));
        Assert.Throws<InvalidDataException>(() => IndexFromCorpus.Build(
            _dir, null, Path.Combine(_dir, "index.db"), null,
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
            codeCommit: BuilderCommit));
    }

    [Fact]
    public async Task Publisher_identity_and_lex_id_are_bound_even_when_record_hash_is_refreshed()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.PublisherVersionIdentifier = "official:substituted";
        meta.LexId = meta.LexId + "-substituted";
        meta.RecordSha256 = null;
        meta.RecordSha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(meta, CorpusJson.Options))));
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);
        Assert.Contains(report.Errors, error => error.Contains(
            "identity mismatch", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => IndexFromCorpus.Build(
            _dir, null, Path.Combine(_dir, "index.db"), null,
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
            codeCommit: BuilderCommit));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("2026-08-07T00:00:00Z", null)]
    [InlineData("2026-08-07T00:00:00Z", 3)]
    public async Task Pending_absence_requires_a_complete_bounded_sequence_state(
        string? firstMissedAt,
        int? runsMissed)
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.Events.Add(new EventEntry
        {
            Event = "absent_unconfirmed",
            ObservedFrom = "2026-08-07T00:00:00Z",
            Scope = "version",
            FirstMissedAt = firstMissedAt,
            RunsMissed = runsMissed,
        });
        meta.RecordSha256 = null;
        meta.RecordSha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(meta, CorpusJson.Options))));
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);

        Assert.Contains(report.Errors, error => error.Contains(
            "absence lifecycle", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_v4_withdrawal_without_structured_three_run_evidence_is_invalid(
        bool claimsExplicitMigration)
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.Events.Add(new EventEntry
        {
            Event = "withdrawn_from_source",
            ObservedFrom = "2026-08-07T00:00:00Z",
            Scope = "version",
        });
        meta.RecordSha256 = null;
        meta.RecordSha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(meta, CorpusJson.Options))));
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        if (claimsExplicitMigration)
        {
            var manifestPath = Path.Combine(_dir, "manifest.json");
            var manifest = JsonSerializer.Deserialize<ManifestDoc>(
                await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
            manifest.MigrationBaselineWorks = 1;
            await File.WriteAllTextAsync(manifestPath,
                JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");
        }

        var report = CorpusIntegrity.Verify(_dir);

        Assert.Contains(report.Errors, error => error.Contains(
            "absence lifecycle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migration_marker_cannot_relabel_fresh_run_evidence_as_a_historical_audit()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new TextAdapter(), default);
        for (var run = 1; run <= 3; run++)
            await new CorpusWriter(_dir,
                    DateTimeOffset.Parse($"2026-08-0{run + 1}T00:00:00Z"), CodeCommit,
                    runIdentity: $"fresh-run-{run}")
                .WriteAsync(new EmptyTextAdapter(), default);

        var metaPath = Path.Combine(VersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        var firstAbsence = meta.Events.FindIndex(entry =>
            entry.Event == "absent_unconfirmed");
        meta.Events.Insert(firstAbsence, new EventEntry
        {
            Event = "withdrawn_from_source",
            ObservedFrom = "2026-08-01T12:00:00Z",
            Scope = "version",
        });
        meta.RecordSha256 = null;
        meta.RecordSha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(meta, CorpusJson.Options))));
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        var manifestPath = Path.Combine(_dir, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
        manifest.MigrationBaselineWorks = 1;
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);

        Assert.Contains(report.Errors, error => error.Contains(
            "absence lifecycle", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string VersionDirectory => Path.Combine(
        _dir, "works", "w1", "versions", "2024-01-01--" +
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("official:v1"))));

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

    private sealed class EmptyTextAdapter : ISourceAdapter
    {
        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test",
                Tier.A, "test", null),
            [], ["en", "fr"], TextIncluded: true, TextPublic: true,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>([]);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            throw new InvalidOperationException();
    }
}
