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
    public void FixedLogicalInputPinsTheExactEuIndexBytes()
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            EuropeIndexBuilder.BuildFixedInputDeterminismEvidence()));
        Assert.AreEqual("00bb3fb7ba6307ec376bac22050a66bcd682b01ae3d25fa61bc626e4cabe1df8", digest);
    }

    [TestMethod]
    public void PlatformOnlySqliteOptionsDoNotChangePortableProvenance()
    {
        var common = new[] { "DEFAULT_PAGE_SIZE=4096", "ENABLE_FTS5", "THREADSAFE=1" };
        var windows = common.Concat([
            "ATOMIC_INTRINSICS=0", "COMPILER=msvc-1951", "MUTEX_W32",
        ]);
        var linux = common.Concat([
            "ATOMIC_INTRINSICS=1", "COMPILER=gcc-13.3.0", "MUTEX_PTHREADS",
        ]);

        Assert.AreEqual(
            SqlitePortableProvenance.CompileOptionsSha256(windows),
            SqlitePortableProvenance.CompileOptionsSha256(linux));
    }

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
        var admitted = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(static outcome =>
                outcome.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted);
        Assert.IsTrue(admitted.Articles.SelectMany(static article => article.Tokens)
            .Any(static token => token.Kind == EuFormexMainBodyTokenKind.Footnote));
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
        foreach (var exactSpan in new[]
                 {
                     "It shall apply from 25 May 2018.",
                     "identifiable natural person (‘data subject’)",
                     "its publication in the Official Journal of the European Union.",
                     "Article 99 Entry into force and application",
                     "(1) ‘personal data’ means any information",
                 })
        {
            var exactHit = reader.Search("eng", cell.PeriodFrom, cell.PeriodTo, exactSpan);
            Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, exactHit.Outcome);
            Assert.HasCount(1, exactHit.ArticleIdentities, exactSpan);
        }
        var footnote = reader.Search("eng", cell.PeriodFrom, cell.PeriodTo,
            "laying down a procedure for the provision of information in the field of technical regulations");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, footnote.Outcome);
        Assert.IsEmpty(footnote.ArticleIdentities, "Footnote text must not be searchable as article wording.");
        var gap = reader.Search("eng", new DateOnly(2016, 4, 28), new DateOnly(2016, 4, 28), "Regulation");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex, gap.Outcome);
        Assert.IsEmpty(gap.ArticleIdentities);
    }

    [TestMethod]
    public async Task AcquiredNonEnglishFormexPackageRemainsTypedWithoutBindingToEnglishWorkBody()
    {
        var envelope = await RetainedGdprEnvelopeAsync(acquireFrenchExpression: true);
        var acquired = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(static outcome => outcome.Source.Kind == EuFormexPackageOutcomeKind.Acquired);
        Assert.AreEqual(
            "http://publications.europa.eu/resource/authority/language/FRA",
            acquired.Source.Expression.OfficialLanguage);

        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        Assert.IsFalse(corpus.VerifiedSet.Set.Members.SelectMany(static member => member.Stage3Outcomes)
            .Any(outcome => outcome.SemanticIdentitySha256 == acquired.SemanticIdentitySha256),
            "An alternate-language Formex package remains typed in the evidence population but is not " +
            "misrepresented as the selected work-level body outcome.");

        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(0, reader.ArticleCount);
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
            "UPDATE stamp SET sqlite_source_id='substituted' WHERE stamp_id=1");
        AssertHostileDatabaseRefused(built, corpus.ArtifactRef,
            "CREATE TABLE injected(value TEXT) STRICT");
        AssertHostileDatabaseRefused(built, corpus.ArtifactRef,
            "PRAGMA application_id=0");
    }

    internal static async Task<Stage3DerivationProfileEnvelope> RetainedGdprEnvelopeAsync(
        bool reopenRetainedBytes = true,
        bool acquireFrenchExpression = false)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin"));
        const string expressionIri =
            "http://publications.europa.eu/resource/cellar/5f2552c2-11bd-11e6-ba9a-01aa75ed71a1.0001";
        const string frenchExpressionIri =
            "http://publications.europa.eu/resource/cellar/5f2552c2-11bd-11e6-ba9a-01aa75ed71a1.0002";
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root),
            seedCelex: "32016R0679",
            expressionIri: expressionIri,
            additionalExpressionIri: acquireFrenchExpression ? frenchExpressionIri : null,
            additionalExpressionLanguageAuthority: acquireFrenchExpression
                ? "http://publications.europa.eu/resource/authority/language/FRA" : null);
        var acquiredExpressionIri = acquireFrenchExpression ? frenchExpressionIri : expressionIri;
        var expression = run.CorrigendumTripwires!.ProductionsByFamilyKey.Values
            .SelectMany(static production => production.Expressions!.Derivation!.Expressions)
            .Single(candidate => string.Equals(
                candidate.Identity.PublisherExpressionId, acquiredExpressionIri, StringComparison.Ordinal));
        var fixture = await EuFormexAnnexInventoryProducerTests.FixtureAsync(bytes, expression);
        var inventoryResult = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference, CancellationToken.None);
        Assert.IsNotNull(inventoryResult.Inventory, inventoryResult.Detail);
        var inventory = inventoryResult.Inventory;
        Assert.AreEqual(0, inventory.Members.Count);
        var acquired = EuFormexPackageOutcome.Acquired(expression, inventory);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(run, acquired);

        return await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: run,
            formexOverride: formex,
            formexStore: reopenRetainedBytes
                ? fixture.Store
                : new EuAcquisitionTestFixture.EuInMemoryCustodyStore());
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
