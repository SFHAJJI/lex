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
    public async Task Fresh_v3_migration_allows_an_expression_set_to_shrink_for_the_same_publisher_state()
    {
        var corpusRoot = Path.Combine(_dir, "publisher-state-expression-reduction");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en", "fr"]));
        var current = new LegacyPublisherIdentityAdapter("official:v1", ["fr"]);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(1, report.ActualVersions);
        Assert.Equal(1, report.Expressions);
        Assert.Equal(1, current.BodyFetchCount);
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
        string? sourceVersionIdentifier = null) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        private static readonly WorkRef Work = new(
            new Identifier("official:w1"), "w1", "REG", "Work one");

        public int BodyFetchCount { get; private set; }

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test",
                Tier.A, "test", null),
            [], ["en", "fr"], TextIncluded: true, TextPublic: true,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return Work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct)
        {
            var sourceIdentity = sourceVersionIdentifier ?? versionIdentifier;
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier(versionIdentifier), Work.Id, "REG",
                    new DateOnly(2024, 1, 1), null, "publisher", "true",
                    new DateOnly(2024, 1, 1),
                    languages.Select(language => new ExpressionRecord(
                        language, new DateOnly(2024, 1, 1), null, "publisher",
                        "Work one", "Work one",
                        $"https://example.test/state/{Uri.EscapeDataString(sourceIdentity)}/{language}"))
                        .ToArray(),
                    [], new Dictionary<string, string>())
            ];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
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
}
