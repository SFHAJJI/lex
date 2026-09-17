using System.Net;
using System.Security.Cryptography;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuropeIndexBuilderTests
{
    [TestMethod]
    public async Task CompleteEnvelopeBuildsDeterministicStrictlyReopenableEuOnlyIndex()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();

        var first = EuropeIndexBuilder.TryBuild(envelope, out var firstRefusal, out var firstDetail);
        var second = EuropeIndexBuilder.TryBuild(envelope, out var secondRefusal, out var secondDetail);

        Assert.IsNotNull(first, $"{firstRefusal}: {firstDetail}");
        Assert.IsNotNull(second, $"{secondRefusal}: {secondDetail}");
        CollectionAssert.AreEqual(first.IndexBytes.ToArray(), second.IndexBytes.ToArray());
        CollectionAssert.AreEqual(first.CapabilityManifestBytes.ToArray(), second.CapabilityManifestBytes.ToArray());
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(
            first.IndexRef, first.IndexBytes.Span, corpus.ArtifactRef, first.CapabilityManifest);
        Assert.AreEqual(
            corpus.VerifiedSet.Set.Members.Count(static member => member.Publisher == PublisherId.EuEurLex),
            reader.MemberCount);
        Assert.AreEqual(0, reader.ArticleCount);
        Assert.AreEqual(0, first.CapabilityManifest.Cells.Count);
    }

    [TestMethod]
    public async Task RetainedGdprPackageBuildsMeasuredArticlesAndRealSupportedSearch()
    {
        var envelope = await RetainedGdprEnvelopeAsync();
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);

        Assert.AreEqual(99, reader.ArticleCount);
        Assert.HasCount(1, built.CapabilityManifest.Cells);
        var cell = built.CapabilityManifest.Cells.Single();
        Assert.AreEqual("eng", cell.Language);
        Assert.AreEqual(new DateOnly(2016, 4, 27), cell.PeriodFrom);
        Assert.AreEqual(new DateOnly(2016, 4, 27), cell.PeriodTo);
        Assert.AreEqual(99, cell.Population);

        var hit = reader.Search("eng", cell.PeriodFrom, cell.PeriodTo,
            "This Regulation lays down rules relating to the protection of natural persons");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, hit.Outcome);
        Assert.HasCount(1, hit.ArticleIdentities);
        var gap = reader.Search("eng", new DateOnly(2016, 4, 28), new DateOnly(2016, 4, 28), "Regulation");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex, gap.Outcome);
        Assert.IsEmpty(gap.ArticleIdentities);
    }

    [TestMethod]
    public async Task AcceptedDatedCorrigendumProjectionIsCarriedExactly()
    {
        var europe = await EuCorrigendumTripwireWiringTests.CompleteDatedResultAsync();
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: europe);
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        var expectedLines = corpus.VerifiedSet.Set.CorrigendumProductions
            .SelectMany(static production => production.Tripwires)
            .Sum(static tripwire => tripwire.Lines.Count);
        var expectedGaps = corpus.VerifiedSet.Set.CorrigendumProductions
            .Sum(static production => production.UnresolvedGaps.Count);
        Assert.IsGreaterThan(0, expectedLines + expectedGaps);

        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(expectedLines, reader.CorrigendumLineCount);
        Assert.AreEqual(expectedGaps, reader.CorrigendumGapCount);
    }

    [TestMethod]
    public async Task MissingCompleteInputRefusesBeforeIndexBytes()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            includeFormexMainBody: false);

        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out _);

        Assert.IsNull(built);
        Assert.AreEqual(EuropeIndexBuildRefusal.CorpusRefused, refusal);
    }

    [TestMethod]
    public async Task ReaderRejectsIdentitySchemaCorpusManifestLogicalRowsAndProvenanceSubstitution()
    {
        var envelope = await RetainedGdprEnvelopeAsync();
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail)!;
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;

        var wrongIndex = new SourceArtifactRef(built.IndexRef.ResourceId, new string('a', 64));
        Assert.ThrowsExactly<ArgumentException>(() => EuropeIndexReader.OpenAndVerify(
            wrongIndex, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest));
        var wrongCorpus = new SourceArtifactRef(
            LexCorpus6Builder.ResourceIdOf(new string('b', 64)), new string('b', 64));
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, wrongCorpus, built.CapabilityManifest));

        var emptyManifest = V3IndexCapabilityManifest.TryCreate(
            PublisherId.EuEurLex, built.IndexRef.Sha256, [], out var manifest, out var manifestRefusal);
        Assert.IsTrue(emptyManifest, manifestRefusal.ToString());
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, manifest!));

        AssertHostileDatabaseRefused(built, corpus.ArtifactRef,
            "UPDATE articles SET searchable_text='substituted' WHERE rowid=(SELECT min(rowid) FROM articles)");
        AssertHostileDatabaseRefused(built, corpus.ArtifactRef,
            "UPDATE stamp SET sqlite_version='substituted' WHERE stamp_id=1");
        AssertHostileDatabaseRefused(built, corpus.ArtifactRef,
            "CREATE TABLE injected(value TEXT) STRICT");
        AssertHostileDatabaseRefused(built, corpus.ArtifactRef,
            "PRAGMA application_id=0");
    }

    private static async Task<Stage3DerivationProfileEnvelope> RetainedGdprEnvelopeAsync()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin"));
        var fixture = await EuFormexAnnexInventoryProducerTests.FixtureAsync(bytes);
        var inventoryResult = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference, CancellationToken.None);
        Assert.IsNotNull(inventoryResult.Inventory, inventoryResult.Detail);
        var inventory = inventoryResult.Inventory;
        Assert.AreEqual(0, inventory.Members.Count);
        var expression = LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(
                fixture.Binding.Expression.ParentKeyRef!.PublisherUri,
                fixture.Binding.Expression.PublisherUri),
            "EN", null, fixture.Binding.Expression,
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, fixture.Receipt)]));
        var acquired = EuFormexPackageOutcome.Acquired(expression, inventory);
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root),
            custodyStore: fixture.Store,
            documentFetchResponse: request => EuAcquisitionTestFixture.BinaryResponse(
                request, HttpStatusCode.OK, bytes, "application/zip;charset=UTF-8"));
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(run, [acquired]);

        return await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: run,
            formexOverride: formex,
            formexStore: fixture.Store);
    }

    private static void AssertHostileDatabaseRefused(
        EuropeIndexBuildResult built,
        SourceArtifactRef corpusRef,
        string sql)
    {
        var bytes = MutateDatabase(built.IndexBytes.Span, sql);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var cells = built.CapabilityManifest.Cells.Select(cell => new V3IndexCapabilityCell(
            cell.Publisher, digest, cell.Operation, cell.Column, cell.Field, cell.Language,
            cell.PeriodFrom, cell.PeriodTo, cell.Population)).ToArray();
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.EuEurLex, digest, cells, out var manifest, out var refusal), refusal.ToString());
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexReader.OpenAndVerify(
            reference, bytes, corpusRef, manifest!));
    }

    private static byte[] MutateDatabase(ReadOnlySpan<byte> source, string sql)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-index-hostile-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, source.ToArray());
            using var connection = EuropeIndexBuilder.Open(path, Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite);
            EuropeIndexBuilder.Execute(connection, sql);
            connection.Close();
            return File.ReadAllBytes(path);
        }
        finally
        {
            EuropeIndexBuilder.DeleteDatabase(path);
        }
    }
}
