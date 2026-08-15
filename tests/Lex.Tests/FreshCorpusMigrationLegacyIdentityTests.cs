using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed partial class CorpusWriterTests
{
    private static Identifier ResolveTestLegacyIdentity(
        LegacyVersionIdentity legacy,
        Func<LegacyExpressionIdentity, string> resolveExpression)
    {
        var identities = legacy.Expressions.Select(resolveExpression)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (identities.Length != 1)
            throw new InvalidDataException(
                "The test legacy expressions do not identify one publisher state.");
        return new Identifier(identities[0]);
    }

    [Fact]
    public async Task Fresh_v3_migration_reuses_verified_text_for_the_same_publisher_state_and_language()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-expression-reduction");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en", "fr"]));
        var retained = ReadOnlyVersion(corpusRoot).Expressions.Single(
            expression => expression.Language == "fr").Observations.Single();
        var current = new LegacyPublisherIdentityAdapter("official:v1", ["fr"]);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(1, report.ActualVersions);
        Assert.Equal(1, report.Expressions);
        Assert.Equal(0, current.BodyFetchCount);
        var expression = Assert.Single(ReadOnlyVersion(corpusRoot).Expressions);
        Assert.Equal("fr", expression.Language);
        var observation = Assert.Single(expression.Observations);
        Assert.Equal(retained.Sha256, observation.Sha256);
        Assert.Equal(retained.ObservedFrom, observation.ObservedFrom);
    }

    [Fact]
    public async Task Fresh_v3_migration_fetches_a_new_language_beside_reused_text()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-new-language");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var current = new LegacyPublisherIdentityAdapter("official:v1", ["en", "fr"]);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(1, current.BodyFetchCount);
        Assert.Equal(["en", "fr"], ReadOnlyVersion(corpusRoot).Expressions
            .Select(expression => expression.Language).Order().ToArray());
    }

    [Fact]
    public async Task Fresh_v3_migration_updates_expression_source_without_rewriting_observation_provenance()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-source-revision");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var retained = Assert.Single(ReadOnlyVersion(corpusRoot).Expressions).Observations.Single();
        var current = new LegacyPublisherIdentityAdapter(
            "official:v1", ["en"], currentSourceSuffix: "?view=current");

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(0, current.BodyFetchCount);
        var expression = Assert.Single(ReadOnlyVersion(corpusRoot).Expressions);
        Assert.True(expression.SourceUri?.EndsWith(
            "?view=current", StringComparison.Ordinal) == true);
        var observation = Assert.Single(expression.Observations);
        Assert.Equal(retained.SourceUri, observation.SourceUri);
        Assert.Equal(retained.Sha256, observation.Sha256);
        Assert.Equal(retained.ObservedFrom, observation.ObservedFrom);
    }

    [Fact]
    public async Task Fresh_v3_migration_reconciles_current_fields_while_reusing_observations()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-current-metadata");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter(
                "official:v1", ["en"],
                typeCode: "OLD",
                validTo: new DateOnly(2024, 12, 31),
                inForceStatus: "false",
                publicationDate: new DateOnly(2023, 12, 31),
                validTimeSource: "legacy-source",
                relationTarget: "official:old",
                title: "Old title",
                titleShort: "Old",
                raw: new Dictionary<string, string>
                {
                    ["legacy_only"] = "must disappear",
                    ["status"] = "old",
                },
                publisherMetadata:
                [
                    new PublisherMetadataRecord(
                        "eurovoc", "https://example.test/old-concept", "en", "Old concept",
                        "https://example.test/old-concept"),
                ],
                documentRoles: ["legacy-role"]));
        var current = new LegacyPublisherIdentityAdapter(
            "official:v1", ["en"],
            typeCode: "REG",
            validTo: new DateOnly(2025, 12, 31),
            expressionValidFrom: new DateOnly(2024, 2, 1),
            inForceStatus: "true",
            publicationDate: new DateOnly(2024, 1, 1),
            validTimeSource: "publisher",
            relationTarget: "official:new",
            title: "Current title",
            titleShort: "Current",
            currentSourceSuffix: "?view=current",
            raw: new Dictionary<string, string> { ["status"] = "current" });

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(0, current.BodyFetchCount);
        var version = ReadOnlyVersion(corpusRoot);
        Assert.Equal("REG", version.DocumentType);
        Assert.Equal("2025-12-31", version.ValidTo);
        Assert.Equal("true", version.InForceStatus);
        Assert.Equal("2024-01-01", version.PublicationDate);
        Assert.Equal("publisher", version.ValidTimeSource);
        Assert.Null(version.PublisherMetadata);
        Assert.Null(version.DocumentRoles);
        var relation = Assert.Single(version.Relations);
        Assert.Equal("official:new", relation["target"]);
        var expression = Assert.Single(version.Expressions);
        Assert.Equal("2024-02-01", expression.ValidFrom);
        Assert.Equal("2025-12-31", expression.ValidTo);
        Assert.Equal("publisher", expression.ValidTimeSource);
        Assert.Equal("Current title", expression.Title);
        Assert.Equal("Current", expression.TitleShort);
        Assert.EndsWith("?view=current", expression.SourceUri,
            StringComparison.Ordinal);
        Assert.Equal(expression.SourceUri, expression.Text.Url);
        Assert.Single(version.Raw);
        Assert.Equal("current", version.Raw["status"]);
        Assert.DoesNotContain("legacy_only", version.Raw.Keys);
        Assert.DoesNotContain(version.Events,
            item => item.Event == "interval_closed");
        var validityRevisions = version.Events
            .Where(item => item.Event == "validity_revised").ToArray();
        Assert.Equal(4, validityRevisions.Length);
        Assert.Contains(validityRevisions, item => item.Scope == "version"
            && item.Detail == "field=valid_to;old=2024-12-31;new=2025-12-31");
        Assert.Contains(validityRevisions, item => item.Scope == "version"
            && item.Detail == "field=publication_date;old=2023-12-31;new=2024-01-01");
        Assert.Contains(validityRevisions,
            item => item.Scope == "expression:en:2024-02-01"
                    && item.Detail == "field=valid_from;old=2024-01-01;new=2024-02-01");
        Assert.Contains(validityRevisions,
            item => item.Scope == "expression:en:2024-02-01"
                    && item.Detail == "field=valid_to;old=2024-12-31;new=2025-12-31");
        var revisions = version.Events
            .Where(item => item.Event == "metadata_revised").ToArray();
        Assert.Equal(2, revisions.Length);
        var revisedFields = revisions.SelectMany(item =>
                item.Detail!["fields=".Length..].Split(','))
            .ToArray();
        Assert.Equal(revisedFields.Length,
            revisedFields.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("document_type", revisedFields);
        Assert.Contains("in_force_status", revisedFields);
        Assert.Contains("publisher_metadata", revisedFields);
        Assert.Contains("document_roles", revisedFields);
        Assert.Contains("expressions.en.title", revisedFields);
        Assert.Contains("expressions.en.title_short", revisedFields);
        Assert.Contains("expressions.en.source_uri", revisedFields);
        Assert.DoesNotContain("valid_to", revisedFields);
        Assert.DoesNotContain("publication_date", revisedFields);
    }

    [Fact]
    public async Task Fresh_v3_migration_records_version_and_expression_interval_closures_once()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-interval-closure");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var current = new LegacyPublisherIdentityAdapter(
            "official:v1", ["en"], validTo: new DateOnly(2025, 12, 31));

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(0, current.BodyFetchCount);
        var version = ReadOnlyVersion(corpusRoot);
        Assert.Equal("2025-12-31", version.ValidTo);
        Assert.Equal("2025-12-31", Assert.Single(version.Expressions).ValidTo);
        var closures = version.Events
            .Where(item => item.Event == "interval_closed").ToArray();
        Assert.Equal(2, closures.Length);
        Assert.Contains(closures, item => item.Scope == "version"
            && item.Detail == "field=valid_to;new=2025-12-31");
        Assert.Contains(closures,
            item => item.Scope == "expression:en:2024-01-01"
                    && item.Detail == "field=valid_to;new=2025-12-31");
        Assert.DoesNotContain(version.Events,
            item => item.Event == "validity_revised");
    }

    [Fact]
    public async Task Fresh_v3_migration_preserves_the_retained_expression_order()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-expression-order");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["fr", "en"]));
        var current = new LegacyPublisherIdentityAdapter(
            "official:v1", ["en", "fr"]);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(["fr", "en"], ReadOnlyVersion(corpusRoot).Expressions
            .Select(expression => expression.Language).ToArray());
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested/slug")]
    [InlineData("nested\\slug")]
    public async Task Fresh_v3_migration_rejects_unsafe_destination_work_slugs(string workSlug)
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-unsafe-slug-"
            + Convert.ToHexString(Guid.NewGuid().ToByteArray()));
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var before = Inventory(corpusRoot);
        var current = new LegacyPublisherIdentityAdapter(
            "official:v1", ["en"], currentWorkSlug: workSlug);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("unsafe destination work slug", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_v3_migration_rejects_tampered_active_observation_before_enumeration()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-tampered-observation");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var observation = Assert.Single(ReadOnlyVersion(corpusRoot).Expressions).Observations.Single();
        await File.AppendAllTextAsync(Path.Combine(
            Path.GetDirectoryName(VersionMetaPath(corpusRoot))!, observation.File!), "tampered");
        var current = new LegacyPublisherIdentityAdapter("official:v1", ["en"]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("Protected corpus baseline is not integrity-compatible",
            error.Message, StringComparison.Ordinal);
        Assert.Equal(0, current.EnumerateCount);
        Assert.Equal(0, current.BodyFetchCount);
    }

    [Fact]
    public async Task Fresh_v3_migration_rejects_a_different_publisher_state_before_body_fetch()
    {
        var corpusRoot = Path.Combine(_dir, "different-publisher-state");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en", "fr"]));
        var before = Inventory(corpusRoot);
        var current = new LegacyPublisherIdentityAdapter(
            "official:v2", ["en", "fr"], sourceVersionIdentifier: "official:v1");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("publisher version 'official:v1'", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    private sealed class LegacyPublisherIdentityAdapter(
        string versionIdentifier,
        IReadOnlyList<string> languages,
        string? sourceVersionIdentifier = null,
        string currentSourceSuffix = "",
        DateOnly? publicationDate = null,
        string validTimeSource = "publisher",
        string? relationTarget = null,
        string currentWorkSlug = "w1",
        IReadOnlyDictionary<string, string>? raw = null,
        string? typeCode = "REG",
        DateOnly? validTo = null,
        DateOnly? expressionValidFrom = null,
        string? inForceStatus = "true",
        string? title = "Work one",
        string? titleShort = "Work one",
        IReadOnlyList<PublisherMetadataRecord>? publisherMetadata = null,
        IReadOnlyList<string>? documentRoles = null,
        Action? beforeFirstBodyFetch = null) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        private readonly WorkRef _work = new(
            new Identifier("official:w1"), currentWorkSlug, typeCode, "Work one");

        public int BodyFetchCount { get; private set; }
        public int EnumerateCount { get; private set; }

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test",
                Tier.A, "test", null),
            [], ["en", "fr"], TextIncluded: true, TextPublic: true,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            EnumerateCount++;
            yield return _work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct)
        {
            var sourceIdentity = sourceVersionIdentifier ?? versionIdentifier;
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier(versionIdentifier), _work.Id, typeCode,
                    new DateOnly(2024, 1, 1), validTo, validTimeSource, inForceStatus,
                    publicationDate ?? new DateOnly(2024, 1, 1),
                    languages.Select(language => new ExpressionRecord(
                        language, expressionValidFrom ?? new DateOnly(2024, 1, 1),
                        validTo, validTimeSource,
                        title, titleShort,
                        $"https://example.test/state/{Uri.EscapeDataString(sourceIdentity)}/{language}"
                        + currentSourceSuffix))
                        .ToArray(),
                    relationTarget is null
                        ? []
                        : [new RelationRecord("related", new Identifier(relationTarget))],
                    raw is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string>(raw, StringComparer.Ordinal),
                    publisherMetadata,
                    documentRoles)
            ];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            if (BodyFetchCount == 0) beforeFirstBodyFetch?.Invoke();
            BodyFetchCount++;
            return Task.FromResult(SourceBodyFetch.Retrieved(
                $"<html>{version.Id.Value}:{expression.Language}</html>"));
        }

        public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy)
            => ResolveTestLegacyIdentity(legacy, expression =>
            {
                var prefix = "https://example.test/state/";
                if (!expression.SourceUri.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidDataException("The test source URI is invalid.");
                var suffix = "/" + expression.Language;
                if (!expression.SourceUri.EndsWith(suffix, StringComparison.Ordinal))
                    throw new InvalidDataException("The test source language is invalid.");
                return Uri.UnescapeDataString(
                    expression.SourceUri[prefix.Length..^suffix.Length]);
            });
    }

    private static string VersionMetaPath(string corpusRoot) =>
        Directory.EnumerateFiles(
                Path.Combine(corpusRoot, "works"), "meta.json", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) != "w1");

    private static VersionMeta ReadOnlyVersion(string corpusRoot) =>
        System.Text.Json.JsonSerializer.Deserialize<VersionMeta>(
            File.ReadAllText(VersionMetaPath(corpusRoot)), CorpusJson.Options)
        ?? throw new InvalidDataException("Test corpus version metadata is empty.");
}
