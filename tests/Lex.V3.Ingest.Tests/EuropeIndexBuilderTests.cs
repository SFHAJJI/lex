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
        Assert.AreEqual("7d6aa9be7334bcdb2fd224a6f0857fddbadca6b92d49a0532337a528f38cd711", digest);
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceCoordinatesPreserveAdmittedPackageAndExactArticleBinding(bool french)
    {
        var envelope = await RetainedGdprEnvelopeAsync(acquireFrenchExpression: french);
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.IsTrue(reader.HasArticleSourceEvidence);
        var admitted = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(static value => value.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted);
        var inventory = admitted.Source.AcquiredInventory!;
        foreach (var article in admitted.Articles)
        {
            var evidence = reader.ReadArticleSourceEvidence(article.IdentitySha256);
            Assert.IsNotNull(evidence);
            Assert.AreEqual(inventory.SourceReceipt.Reference.ContentSha256, evidence.PackageSha256);
            Assert.AreEqual(inventory.TransportBinding.RequestEvidence.Uri, evidence.OfficialSourceUri);
            Assert.AreEqual(admitted.Source.ExpressionIdentity.PublisherWorkId, evidence.PublisherWorkId);
            Assert.AreEqual(article.PublisherExpressionId, evidence.PublisherExpressionId);
            Assert.AreEqual(article.PublisherIdentifier, evidence.PublisherIdentifier);
            Assert.AreEqual(article.PackageEntry, evidence.PackageEntry);
            Assert.AreEqual(french ? "fra" : "eng", evidence.Language);
            Assert.AreEqual("2016-04-27", evidence.WordingDate);
            Assert.AreEqual(article.IdentitySha256, evidence.ArticleIdentitySha256);
        }
        Assert.IsNull(reader.ReadArticleSourceEvidence(new string('0', 64)));
    }

    [TestMethod]
    public async Task ActualFrozenSchema2MountRemainsReadableWithoutInventingSourceCoordinates()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch");
        using var compressed = File.OpenRead(Path.Combine(fixture, "legacy-europe-index-v2-gzip.bin"));
        using var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        await gzip.CopyToAsync(output);
        var bytes = output.ToArray();
        const string indexSha = "77f38ce099bf4adb1c1d42d4af2a6682b6d0bfd24a5b70111cf949afcd98ecc0";
        Assert.AreEqual(indexSha, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var capabilityBytes = await File.ReadAllBytesAsync(Path.Combine(fixture, "legacy-europe-index-v2-capability.bin"));
        Assert.AreEqual("97fa74ef5d47ce7c07320cbfede2463d654e713e0d3a873dae1441bf8b259c22",
            Convert.ToHexStringLower(SHA256.HashData(capabilityBytes)));
        var capabilitySha = V3IndexCapabilityManifestArtifact.ComputeSha256(capabilityBytes);
        var capability = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(capabilitySha), capabilitySha),
            capabilityBytes, PublisherId.EuEurLex, indexSha);
        const string corpusSha = "cd36530f70421ab2efe543a31871266ac64823d75b419155c95806c7ec04dae5";
        using var reader = EuropeIndexReader.OpenAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(indexSha), indexSha), bytes,
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(corpusSha), corpusSha), capability);
        Assert.AreEqual(198, reader.ArticleCount);
        Assert.IsFalse(reader.HasArticleSourceEvidence);
        CollectionAssert.AreEqual(new[] { "eng", "fra" }, reader.SearchableLanguages().ToArray());
        var article = reader.ResolveExact("32016R0679").First().ArticleIdentities.First();
        Assert.IsNull(reader.ReadArticleSourceEvidence(article));
    }

    [TestMethod]
    [DataRow("UPDATE article_sources SET package_sha256=printf('%064d',0)")]
    [DataRow("UPDATE article_sources SET official_source_uri='https://example.invalid/substitution'")]
    [DataRow("DELETE FROM article_sources WHERE rowid=(SELECT min(rowid) FROM article_sources)")]
    [DataRow("PRAGMA user_version=2")]
    [DataRow("UPDATE stamp SET schema_identity='lex-v3-europe-index/2'")]
    public async Task ReaderRejectsSourceSubstitutionMissingCoordinatesAndVersionMixing(string sql)
    {
        var envelope = await RetainedGdprEnvelopeAsync();
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        AssertHostileDatabaseRefused(built, corpus.ArtifactRef, sql);
    }

    [TestMethod]
    public async Task AWorkScopedSearchMatchesEveryNeedleInOneExpressionAndSaysWhenALanguageIsNotMeasured()
    {
        var envelope = await RetainedGdprEnvelopeAsync();
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        var expression = reader.ResolveExact("32016R0679").Single().PublisherExpressionId;

        CollectionAssert.AreEqual(new[] { "eng" }, reader.SearchableLanguages().ToArray());
        Assert.IsNull(
            reader.SearchExpressionArticles("fra", ["Regulation"], expression),
            "no searchable text is measured in French, so the index cannot answer; that is not an empty list.");

        var phrase = reader.SearchExpressionArticles("eng", ["It shall apply from 25 May 2018."], expression)!;
        Assert.HasCount(1, phrase);
        Assert.AreEqual("2016-04-27", phrase[0].WordingDate);
        Assert.AreEqual("32016R0679", phrase[0].PublisherWorkCelex);
        Assert.HasCount(1, reader.ResolveExact(phrase[0].ProvisionCoordinate), "the hit's coordinate resolves to its provision.");

        var both = reader.SearchExpressionArticles("eng", ["shall apply", "25 May 2018"], expression)!;
        var reversed = reader.SearchExpressionArticles("eng", ["25 May 2018", "shall apply"], expression)!;
        CollectionAssert.AreEqual(
            both.Select(static hit => hit.ArticleIdentitySha256).ToArray(),
            reversed.Select(static hit => hit.ArticleIdentitySha256).ToArray(),
            "the needles are an AND, in any order.");
        CollectionAssert.IsSubsetOf(
            both.Select(static hit => hit.ArticleIdentitySha256).ToArray(),
            reader.SearchExpressionArticles("eng", ["shall apply"], expression)!.Select(static hit => hit.ArticleIdentitySha256).ToArray(),
            "adding a needle narrows.");
        Assert.IsEmpty(
            reader.SearchExpressionArticles("eng", ["It shall apply from 25 May 2018."], "http://publications.europa.eu/resource/cellar/not-held")!,
            "the search is scoped to the expression named.");
    }

    [TestMethod]
    public async Task ExactPublisherWorkExpressionAndProvisionCoordinatesResolveFromVerifiedRows()
    {
        var envelope = await RetainedGdprEnvelopeAsync();
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        var admitted = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(static outcome =>
                outcome.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted);
        var article = admitted.Articles[0];
        var work = admitted.Source.ExpressionIdentity.PublisherWorkId;
        var expression = admitted.Source.ExpressionIdentity.PublisherExpressionId;

        var byWork = reader.ResolveExact(work);
        var byExpression = reader.ResolveExact(expression);
        var byCelex = reader.ResolveExact("32016R0679");
        var qualifiedProvision = EuropeIndexReader.QualifiedProvisionIdentifierOf(
            expression, article.PublisherIdentifier);
        var byProvision = reader.ResolveExact(qualifiedProvision);

        Assert.HasCount(1, byWork);
        Assert.HasCount(1, byExpression);
        Assert.HasCount(1, byCelex,
            "The publisher CELEX carried by the admitted Formex package is an R0 exact coordinate.");
        Assert.HasCount(1, byProvision);
        Assert.AreEqual(work, byWork[0].PublisherWorkId);
        Assert.AreEqual(expression, byWork[0].PublisherExpressionId);
        Assert.AreEqual(work, byCelex[0].PublisherWorkId);
        Assert.AreEqual(expression, byCelex[0].PublisherExpressionId);
        Assert.AreEqual(99, byWork[0].ArticleIdentities.Count);
        Assert.AreEqual(article.IdentitySha256, byProvision[0].ArticleIdentities.Single());
        Assert.AreEqual(article.PublisherIdentifier,
            byProvision[0].PublisherProvisionIdentifiers.Single());
        Assert.IsEmpty(reader.ResolveExact(article.PublisherIdentifier),
            "A Formex ARTICLE identifier is document-local and is not an exact coordinate alone.");
        Assert.IsEmpty(reader.ResolveExact("https://example.invalid/not-in-the-index"));
    }

    [TestMethod]
    public async Task FrenchFormexPackageBindsOnlyItsExpressionAndBuildsDeterministically()
    {
        var envelope = await RetainedGdprEnvelopeAsync(acquireFrenchExpression: true);
        var acquired = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(static outcome => outcome.Source.Kind == EuFormexPackageOutcomeKind.Acquired);
        Assert.AreEqual(
            "http://publications.europa.eu/resource/authority/language/FRA",
            acquired.Source.Expression.OfficialLanguage);

        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var frenchMembers = corpus.VerifiedSet.Set.Members.Where(member => member.Stage3Outcomes
            .Any(outcome => outcome.SemanticIdentitySha256 == acquired.SemanticIdentitySha256)).ToArray();
        Assert.HasCount(1, frenchMembers,
            "The French outcome belongs to one expression member and cannot attach to the English Work.");
        var againCorpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        Assert.AreEqual(corpus.ArtifactRef, againCorpus.ArtifactRef);

        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(1, reader.ArticleCount);
        var cell = built.CapabilityManifest.Cells.Single();
        Assert.AreEqual("fra", cell.Language);
        var hit = reader.Search("fra", cell.PeriodFrom, cell.PeriodTo, "Texte français de test");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, hit.Outcome);
        Assert.HasCount(1, hit.ArticleIdentities);
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex,
            reader.Search("eng", cell.PeriodFrom, cell.PeriodTo, "data").Outcome);
        var resolved = reader.ResolveExact(acquired.Source.ExpressionIdentity.PublisherExpressionId);
        Assert.HasCount(1, resolved);
        Assert.AreEqual(acquired.Source.ExpressionIdentity.PublisherWorkId, resolved[0].PublisherWorkId);
        var again = EuropeIndexBuilder.TryBuild(envelope, out _, out _)!;
        CollectionAssert.AreEqual(built.IndexBytes.ToArray(), again.IndexBytes.ToArray());
        CollectionAssert.AreEqual(built.CapabilityManifestBytes.ToArray(), again.CapabilityManifestBytes.ToArray());
    }

    [TestMethod]
    public async Task EnglishPackageOnFrenchExpressionCannotEnterTheIndex()
    {
        var envelope = await RetainedGdprEnvelopeAsync(acquireFrenchExpression: true, mismatchedFrenchPackage: true);
        var outcome = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes.Single(
            value => value.Source.Kind == EuFormexPackageOutcomeKind.Acquired);
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape, outcome.Disposition);
        StringAssert.Contains(outcome.Detail, "language does not match");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = EuropeIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(0, reader.ArticleCount);
        Assert.IsEmpty(built.CapabilityManifest.Cells);
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
        bool acquireFrenchExpression = false,
        bool mismatchedFrenchPackage = false)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin"));
        if (acquireFrenchExpression && !mismatchedFrenchPackage)
            bytes = EuAcquisitionTestFixture.SyntheticFrenchMainBodyPackage();
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
