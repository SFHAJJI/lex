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
    public async Task Incomplete_enumeration_preserves_the_prior_clean_corpus_without_tombstones()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance"), default);
        var before = Snapshot();

        var failure = await Assert.ThrowsAsync<SourceEnumerationIncompleteException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"))
                .WriteAsync(new IncompleteAdapter(), default));

        Assert.Equal("incomplete_enumeration", failure.Issue.Code);
        Assert.Equal(before, Snapshot());
        var meta = await ReadVersionMeta();
        Assert.DoesNotContain(meta.Events, item => item.Event == "withdrawn_from_source");
    }

    [Fact]
    public async Task Failed_body_acquisition_rolls_back_every_candidate_file()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);
        var before = Snapshot();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"))
                .WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
                    titleHint: "Candidate title", bodyException: new HttpRequestException("network")), default));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Typed_metadata_only_outcome_is_signed_into_the_build_inventory()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: new SourceBodyFetch(SourceBodyStatus.PermanentNotFound,
                    Detail: "publisher returned 404", Attempts: 4)), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("body_not_found", issue.Code);
        Assert.Contains("attempts=4", issue.Detail);
        Assert.Equal(4, manifest.AcquisitionRetryMaximumAttempts);
        Assert.Equal("body_not_found", Assert.Single((await ReadVersionMeta()).Expressions).Text.Reason);
    }

    [Fact]
    public async Task Production_candidate_with_a_typed_issue_keeps_the_prior_corpus_selected()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);
        var before = Snapshot();

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
            titleHint: "Candidate title",
            bodyFetch: new SourceBodyFetch(SourceBodyStatus.PermanentNotFound,
                Detail: "publisher returned 404", Attempts: 4)), default,
            requireComplete: true);

        Assert.False(candidate.Committed);
        Assert.Equal("body_not_found", Assert.Single(candidate.BuildIssues).Code);
        Assert.Equal(before, Snapshot());
    }

    // The distinction that stopped the nightly. A publisher offering no XML for an expression is
    // not a failed acquisition, it is the publisher saying there is nothing to acquire in that
    // format, and the corpus already records that as Text.Reason with Available=false. Legilux
    // announces future-dated consolidations before their XML exists, so counting these as
    // acquisition failures discarded the whole candidate every night, for both publishers, with a
    // count that could only grow.
    [Fact]
    public async Task A_metadata_only_expression_does_not_discard_the_candidate()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
            titleHint: "Candidate title",
            bodyFetch: new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an XML manifestation for this expression.")),
            default, requireComplete: true);

        Assert.True(candidate.Committed);
        // Still recorded, so coverage stays honest: reported, not silently accepted.
        Assert.NotEmpty(candidate.BuildIssues);
        Assert.All(candidate.BuildIssues,
            issue => Assert.Equal("publisher_metadata_only", issue.Code));
        // The language the publisher offered nothing for carries the reason, and carries no
        // text: committing the candidate must not invent coverage it does not have.
        var expressions = (await ReadVersionMeta()).Expressions;
        var metadataOnly = Assert.Single(expressions,
            expression => expression.Text.Reason == "publisher_metadata_only");
        Assert.False(metadataOnly.Text.Available);
    }

    // The other half, and the case that decides whether the narrowed gate is still a gate: one
    // expression the publisher offers nothing for, and one that genuinely failed to fetch. The
    // metadata-only issue must not rescue the candidate.
    [Fact]
    public async Task A_real_failure_beside_a_metadata_only_one_still_discards_the_candidate()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);
        var before = Snapshot();

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
        // fr and de, not en: the first write already observed en, and an expression with an
        // observation is skipped rather than refetched, so scripting en would never be reached.
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr", "de"],
            titleHint: "Candidate title",
            bodyFetchByLanguage: new Dictionary<string, SourceBodyFetch>(StringComparer.Ordinal)
            {
                ["fr"] = new(SourceBodyStatus.PublisherMetadataOnly,
                    Detail: "The publisher did not enumerate an XML manifestation for this expression."),
                ["de"] = new(SourceBodyStatus.RetryExhausted,
                    Detail: "publisher timed out", Attempts: 4),
            }), default, requireComplete: true);

        Assert.False(candidate.Committed);
        Assert.Equal(before, Snapshot());
        // Both were recorded; only the real failure decided the outcome.
        Assert.Contains(candidate.BuildIssues, issue => issue.Code == "publisher_metadata_only");
        Assert.Contains(candidate.BuildIssues, issue => issue.Code == "body_retry_exhausted");
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

    [Fact]
    public async Task Manifest_counts_a_fetched_body_as_an_expression_with_text()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.Expressions);
        Assert.Equal(1, manifest.ExpressionsWithText);
        Assert.Equal(0, manifest.ExpressionsWithoutText);
        Assert.True(File.Exists(Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "en.html")));
    }

    [Fact]
    public async Task Empty_retrieved_body_is_a_typed_issue_not_stored_text()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"))
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("   ")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(0, manifest.ExpressionsWithText);
        Assert.Equal(1, manifest.ExpressionsWithoutText);
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("body_empty", issue.Code);

        var expr = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.False(expr.Text.Available);
        Assert.Equal("body_empty", expr.Text.Reason);
        Assert.Empty(expr.Observations);
        Assert.False(File.Exists(Path.Combine(_dir, "works", "w1", "versions", "2024-01-01", "en.html")));
    }

    [Fact]
    public async Task Alt_manifestation_does_not_block_primary_body_backfill()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"))
            .WriteAsync(new AltThenPrimaryAdapter(new SourceBodyFetch(
                SourceBodyStatus.RetryExhausted, Detail: "network", Attempts: 2)), default);

        var versionDir = Path.Combine(_dir, "works", "w1", "versions", "2024-01-01");
        Assert.False(File.Exists(Path.Combine(versionDir, "en.html")));
        var first = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.True(first.Text.Available);                       // an alt manifestation IS observed text
        Assert.All(first.Observations, o => Assert.NotNull(o.Format));

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"))
            .WriteAsync(new AltThenPrimaryAdapter(
                SourceBodyFetch.Retrieved("<html>primary text</html>")), default);

        Assert.True(File.Exists(Path.Combine(versionDir, "en.html")));
        var second = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.Contains(second.Observations, o => o.Format is null);
    }

    private async Task<VersionMeta> ReadVersionMeta() => JsonSerializer.Deserialize<VersionMeta>(
        await File.ReadAllTextAsync(Path.Combine(
            _dir, "works", "w1", "versions", "2024-01-01", "meta.json")), CorpusJson.Options)!;

    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_dir, path).Replace('\\', '/'),
            path => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.Ordinal);

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
        bool hasVersions = true,
        SourceBodyFetch? bodyFetch = null,
        string titleHint = "Work one",
        Exception? bodyException = null,
        IReadOnlyDictionary<string, SourceBodyFetch>? bodyFetchByLanguage = null) : ISourceAdapter
    {
        private readonly WorkRef _work = new(new Identifier("official:w1"), "w1", "REG", titleHint);

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            // A per-language script declares text inclusion just as a single fetch does. Without
            // this the whole version takes the metadata-only branch and a scripted real failure
            // is never reached, which is what made the mixed test pass for the wrong reason.
            [], languages ?? ["en"],
            TextIncluded: bodyFetch is not null || bodyException is not null
                          || bodyFetchByLanguage is not null,
            TextPublic: bodyFetch is not null || bodyException is not null
                        || bodyFetchByLanguage is not null,
            HistoryBegins: "publisher");

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

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            if (bodyException is not null) throw bodyException;
            // Per-language override so one candidate can carry a real failure and a metadata-only
            // expression at once, which is the case that decides whether the narrowed gate is
            // still a gate.
            if (bodyFetchByLanguage?.TryGetValue(expression.Language, out var perLanguage) == true)
                return Task.FromResult(perLanguage);
            return Task.FromResult(bodyFetch
                ?? new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
        }
    }

    private sealed class IncompleteAdapter : ISourceAdapter, ISourceBuildInventory
    {
        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en"], TextIncluded: true, TextPublic: true, HistoryBegins: "publisher");

        public SourceBuildInventory GetBuildInventory() => new(1, [], EnumerationComplete: false, RetryMaximumAttempts: 4);

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>([]);

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            throw new InvalidOperationException("No work may be fetched after an incomplete enumeration.");
    }

    private sealed class AltThenPrimaryAdapter(SourceBodyFetch bodyFetch) : ISourceAdapter
    {
        private readonly WorkRef _work = new(new Identifier("official:w1"), "w1", "REG", "Work one");

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en"], TextIncluded: true, TextPublic: true, HistoryBegins: "publisher");

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
                    [new ExpressionRecord("en", new DateOnly(2024, 1, 1), null, "publisher",
                        "Work one", "Work one", "https://example.test/v1/en")],
                    [], new Dictionary<string, string>(), null, null)
            ];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(bodyFetch);

        public Task<SourceManifestationFetch> FetchAltManifestation(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(SourceManifestationFetch.Retrieved(new ManifestationFetch(
                "fmx4", [new ManifestationMember("main.xml", "<xml/>"u8.ToArray())],
                "https://example.test/v1/en/fmx4")));
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

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
    }
}
