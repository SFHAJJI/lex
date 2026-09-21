using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgIndexBuilderTests
{
    [TestMethod]
    public void FixedLogicalInputPinsTheExactLuxembourgIndexBytes()
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            LuxembourgIndexBuilder.BuildFixedInputDeterminismEvidence()));
        Assert.AreEqual("c47a1955716899133f2c418108ab820e023a9287b839644cf2dcd15eebefa48a", digest);
    }

    private const string Retained1991 = "loi-1991-08-10-n3--2024-02-01--fr.bin";

    [TestMethod]
    public void BuilderAndStrictReaderShipAsOneTerminalSlice()
    {
        var schema = (string)typeof(LuxembourgIndexBuilder)
            .GetField(nameof(LuxembourgIndexBuilder.Schema))!
            .GetRawConstantValue()!;
        Assert.AreEqual("lex-v3-luxembourg-index/3", schema);
        Assert.IsNotNull(typeof(LuxembourgIndexBuilder).GetMethod(nameof(LuxembourgIndexBuilder.TryBuild)));
        Assert.IsNotNull(typeof(LuxembourgIndexReader).GetMethod(nameof(LuxembourgIndexReader.OpenAndVerify)));
    }

    [TestMethod]
    [DataRow("https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3")]
    [DataRow("http://example.invalid/eli/etat/leg/loi/1991/08/10/n3")]
    [DataRow("http://data.legilux.public.lu/eli/other/loi/1991/08/10/n3")]
    public void WorkKeyProjectionRejectsWrongSchemeHostOrLegalPath(string workIri)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexBuilder.WorkKeyOf(workIri));
    }

    [TestMethod]
    public async Task CompleteEnvelopeBuildsDeterministicLuOnlyIndexAndMeasuredManifest()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var first = LuxembourgIndexBuilder.TryBuild(envelope, out var firstRefusal, out var firstDetail);
        var second = LuxembourgIndexBuilder.TryBuild(envelope, out var secondRefusal, out var secondDetail);

        Assert.IsNotNull(first, $"{firstRefusal}: {firstDetail}");
        Assert.IsNotNull(second, $"{secondRefusal}: {secondDetail}");
        CollectionAssert.AreEqual(first.IndexBytes.ToArray(), second.IndexBytes.ToArray());
        Assert.AreEqual(first.IndexRef, second.IndexRef);
        Assert.AreEqual(first.CapabilityManifestRef, second.CapabilityManifestRef);
        CollectionAssert.AreEqual(
            first.CapabilityManifestBytes.ToArray(),
            second.CapabilityManifestBytes.ToArray());
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            second.CapabilityManifest.Cells.ToArray());
        Assert.AreEqual(PublisherId.LuLegilux, first.CapabilityManifest.Publisher);
        Assert.AreEqual(first.IndexRef.Sha256, first.CapabilityManifest.IndexSha256);
        var reopenedManifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            first.CapabilityManifestRef,
            first.CapabilityManifestBytes.Span,
            PublisherId.LuLegilux,
            first.IndexRef.Sha256);
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            reopenedManifest.Cells.ToArray());

        using var reader = LuxembourgIndexReader.OpenAndVerify(
            first.IndexRef,
            first.IndexBytes.Span,
            corpus.ArtifactRef,
            first.CapabilityManifest);
        var expectedLuMembers = corpus.VerifiedSet.Set.Members.Count(static member =>
            member.Publisher == PublisherId.LuLegilux);
        Assert.AreEqual(expectedLuMembers, reader.MemberCount);
        Assert.IsTrue(reader.MemberCount < corpus.VerifiedSet.Set.Members.Count,
            "The Luxembourg index must not contain the EU population.");
        Assert.IsTrue(corpus.VerifiedSet.Set.Members.Any(static member =>
            member.Publisher == PublisherId.LuLegilux &&
            member.Outcome == LexCorpus6OutcomeKind.RightsWithheld),
            "The accepted fixture must exercise the retained rights-withheld terminal path.");
        Assert.AreEqual(0, reader.ArticleCount,
            "A rights-withheld member remains population evidence but cannot become legal text.");
        Assert.IsEmpty(first.CapabilityManifest.Cells,
            "No searchable row means no advertised capability.");
        var unsupported = reader.Search(
            "deu", new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1), "law");
        Assert.AreEqual(
            V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex,
            unsupported.Outcome);
        Assert.IsEmpty(unsupported.ArticleIdentities);
    }

    [TestMethod]
    public async Task RetainedPublisherAknProducesDayExactCapabilitiesWithoutAdvertisingTheGap()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        Assert.AreEqual(
            "3a6bb598a9310f8a31240c1f33ae357d6e1f7a46392ca33d223c2718cdded95c",
            Convert.ToHexStringLower(SHA256.HashData(xml)));
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg,
            luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var inventory = envelope.BodyComposition.Envelope.LuxembourgAknArticleInventoryPopulation
            .Outcomes.Single().Inventory;
        Assert.IsNotNull(inventory);
        Assert.HasCount(54, inventory.Articles);
        Assert.IsTrue(inventory.Articles.All(static article => article.PublisherApplicability is not null));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var luxembourgMembers = corpus.VerifiedSet.Set.Members.Where(static value =>
            value.Publisher == PublisherId.LuLegilux).ToArray();
        var member = luxembourgMembers.Single(static value =>
            value.Outcome == LexCorpus6OutcomeKind.Acquired);
        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, member.Outcome);
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(luxembourgMembers.Length, reader.MemberCount);
        Assert.AreEqual(49, reader.ArticleCount);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        Assert.AreEqual(
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3",
            state.PublisherWorkIri);
        Assert.AreEqual(
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo",
            state.PublisherLegalResourceIri);
        Assert.AreEqual(manifestation[..manifestation.LastIndexOf('/')], state.ExpressionIri);
        Assert.HasCount(49, state.ArticleIdentities);
        CollectionAssert.Contains(
            state.RuleProfileSha256s.ToArray(),
            LuxembourgAknArticleInventoryProducer.RuleProfileSha256);
        Assert.HasCount(4, built.CapabilityManifest.Cells);
        var cells = built.CapabilityManifest.Cells.OrderBy(static cell => cell.PeriodFrom).ToArray();
        Assert.AreEqual(new DateOnly(2021, 8, 22), cells[0].PeriodFrom);
        Assert.AreEqual(cells[0].PeriodFrom, cells[0].PeriodTo);
        Assert.AreEqual(36, cells[0].Population);
        Assert.AreEqual(new DateOnly(2023, 9, 16), cells[^1].PeriodFrom);
        Assert.AreEqual(cells[^1].PeriodFrom, cells[^1].PeriodTo);
        Assert.AreEqual(1, cells[^1].Population);
        var expectedArticle = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation
            .Outcomes.Single(static value => value.Article?.Coordinate.PublisherId == "art_1er")
            .Article!;
        var supported = reader.Search(
            "fra", new DateOnly(2021, 8, 22), new DateOnly(2021, 8, 22),
            "La profession d’avocat est une profession libérale et indépendante.");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, supported.Outcome);
        CollectionAssert.AreEqual(
            new[] { expectedArticle.IdentitySha256 }, supported.ArticleIdentities.ToArray());
        var gap = reader.Search(
            "fra", new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), "article");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex, gap.Outcome);
        Assert.IsEmpty(gap.ArticleIdentities);
    }

    [TestMethod]
    public async Task AdmittedAknTextWithoutAdmittingRightsNeverBecomesSearchable()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{manifestation}}"/></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{manifestation}}</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta><body>
                <article id="art_1"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p>Withheld publisher words.</p></content></article>
              </body></act>
            </akomaNtoso>
            """);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(
                xml, store, manifestation, item, includeEndpointLicence: false);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        Assert.IsTrue(envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
            .Any(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.IsTrue(corpus.VerifiedSet.Set.Members.Any(static value =>
            value.Publisher == PublisherId.LuLegilux &&
            value.Outcome == LexCorpus6OutcomeKind.RightsWithheld));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(0, reader.ArticleCount);
        Assert.IsEmpty(built.CapabilityManifest.Cells);

        // The withheld member's own outcome list records the admitted outcome, and the coverage count of the
        // corpus's outcomes leaves it out because the index holds no article for a member that is not acquired.
        // This is the acquired filter on an index the builder made, not on one whose rows a test edited.
        var withheld = corpus.VerifiedSet.Set.Members.Single(static value =>
            value.Publisher == PublisherId.LuLegilux &&
            value.Outcome == LexCorpus6OutcomeKind.RightsWithheld);
        Assert.IsTrue(withheld.Stage3Outcomes.Any(static value =>
            value.Domain == LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent &&
            value.Disposition == LexCorpus6Stage3Disposition.AknAdmitted));
        Assert.IsEmpty(reader.ResolveCoverage().ArticleOutcomes);
    }

    [TestMethod]
    public async Task TheArticlesTheIndexHoldsAreExactlyTheAdmittedAndMarkerOnlyOutcomesOfAnAcquiredMember()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        // Three articles the reviewed profile treats three ways: publisher words (admitted), nothing but
        // modification markers (marker-only evidence, which the index holds) and an element outside its
        // vocabulary (not admitted, so not held).
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{manifestation}}"/></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{manifestation}}</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta><body>
                <article id="art_1"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p>Held publisher words.</p></content></article>
                <article id="art_2"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p><mod class="mod-start" for="#pm1"/><mod class="mod-end" for="#pm1"/></p></content></article>
                <article id="art_3"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p><del/></p></content></article>
              </body></act>
            </akomaNtoso>
            """);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        Assert.IsTrue(
            corpus.VerifiedSet.Set.Members.Any(static value =>
                value.Publisher == PublisherId.LuLegilux && value.Outcome == LexCorpus6OutcomeKind.Acquired),
            "no Luxembourg member is acquired: " + string.Join(
                ", ",
                corpus.VerifiedSet.Set.Members.Where(static value => value.Publisher == PublisherId.LuLegilux)
                    .Select(static value => value.Outcome + "/" + value.LuxembourgRights?.Disposition)));
        // The stage's own dispositions, read from its population and not through the index.
        CollectionAssert.AreEquivalent(
            new[]
            {
                LuxembourgAknLegalContentDisposition.Admitted,
                LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence,
                LuxembourgAknLegalContentDisposition.UnsupportedContentShape,
            },
            envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
                .Select(static value => value.Disposition).ToArray());

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        // Two articles: the admitted one and the marker-only one. Dropping either from the index's insert gate
        // makes the corpus's count of outcomes say something the articles it holds do not.
        Assert.AreEqual(2, reader.ArticleCount);
        var coverage = reader.ResolveCoverage();
        Assert.AreEqual(2, coverage.Articles);
        CollectionAssert.AreEqual(
            new[] { "akn_admitted=1", "akn_marker_only_evidence=1", "akn_unsupported_content_shape=1" },
            coverage.ArticleOutcomes.Select(static row => row.Key + "=" + row.Value).ToArray());
    }

    [TestMethod]
    public async Task CorpusIncompletenessRefusesBeforeAnIndexExists()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(includeLegalNotice: false);

        var result = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNull(result);
        Assert.AreEqual(LuxembourgIndexBuildRefusal.CorpusRefused, refusal, detail);
    }

    [TestMethod]
    public async Task StrictReaderRejectsByteCorpusAndCapabilitySubstitution()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");

        var changedBytes = built.IndexBytes.ToArray();
        changedBytes[^1] ^= 1;
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, changedBytes, corpus.ArtifactRef, built.CapabilityManifest));

        var otherCorpus = new SourceArtifactRef(corpus.ArtifactRef.ResourceId, new string('a', 64));
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, otherCorpus, built.CapabilityManifest));

        var foreignCorpusIdentity = new SourceArtifactRef(
            "urn:uuid:ffffffff-ffff-5fff-8fff-ffffffffffff", corpus.ArtifactRef.Sha256);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, foreignCorpusIdentity, built.CapabilityManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            new string('b', 64),
            [],
            out var wrongManifest,
            out _));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, wrongManifest!));

        var extendedBytes = AddUnexpectedTable(built.IndexBytes.Span);
        var extendedDigest = Convert.ToHexStringLower(SHA256.HashData(extendedBytes));
        var extendedRef = new SourceArtifactRef(
            LexCorpus6Builder.ResourceIdOf(extendedDigest), extendedDigest);
        var extendedManifest = RebindManifest(built.CapabilityManifest, extendedDigest);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            extendedRef, extendedBytes, corpus.ArtifactRef, extendedManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            built.IndexRef.Sha256,
            [new V3IndexCapabilityCell(
                PublisherId.LuLegilux, built.IndexRef.Sha256, "search", "articles",
                "searchable_text", "fra", new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 1), 1)],
            out var inventedManifest,
            out var inventedRefusal), inventedRefusal.ToString());
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, inventedManifest!));

        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE members SET outcome='invented' WHERE rowid=(SELECT min(rowid) FROM members)");
        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE stamp SET sqlite_version='0.0.0' WHERE stamp_id=1");
        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE stamp SET sqlite_source_id='substituted' WHERE stamp_id=1");
    }

    [TestMethod]
    [DataRow("PRAGMA user_version=2", "schema identity")]
    [DataRow("UPDATE states SET state_sha256='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'", "digest is not derived")]
    [DataRow("UPDATE articles SET expression_iri=expression_iri || '/other'", "does not bind its exact article population")]
    [DataRow("UPDATE states SET work_key='wrong-work-key'", "not canonical")]
    [DataRow("UPDATE states SET publisher_legal_resource_iri=expression_iri", "omits or crosses")]
    [DataRow("UPDATE states SET expression_iri=publisher_legal_resource_iri || '/de'", "does not bind its exact article population")]
    public async Task StrictReaderRejectsEachStateInvariantIndependently(string sql, string expected)
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertTamperedDatabaseRejected(built, corpusRef, sql, expected);
    }

    [TestMethod]
    public async Task StrictReaderRejectsAnArticleClaimedByTwoStates()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            const string secondDate = "2024-02-02";
            var profiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)!;
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            var digest = LuxembourgIndexBuilder.StateSha256(
                state.WorkKey, secondDate, state.ExpressionIri, state.PublisherWorkIri,
                state.PublisherLegalResourceIri, state.Language, profiles, identities);
            Execute(connection,
                "INSERT INTO states VALUES($work,$date,$digest,$expression,$workIri,$resource,$language,$profiles,$identities)",
                ("$work", state.WorkKey), ("$date", secondDate), ("$digest", digest),
                ("$expression", state.ExpressionIri), ("$workIri", state.PublisherWorkIri),
                ("$resource", state.PublisherLegalResourceIri), ("$language", state.Language),
                ("$profiles", state.RuleProfilesJson), ("$identities", state.ArticleIdentitiesJson));
        }, "does not bind its exact article population");
    }

    [TestMethod]
    public async Task StrictReaderRejectsAStateWhoseLanguageContradictsItsArticles()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            const string language = "deu";
            var profiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)!;
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            var digest = LuxembourgIndexBuilder.StateSha256(
                state.WorkKey, state.ApplicabilityDate, state.ExpressionIri, state.PublisherWorkIri,
                state.PublisherLegalResourceIri, language, profiles, identities);
            Execute(connection,
                "UPDATE states SET language=$language,state_sha256=$digest",
                ("$language", language), ("$digest", digest));
        }, "language contradicts its articles");
    }

    [TestMethod]
    public async Task StrictReaderRejectsAStateThatOmitsOneOfItsExpressionArticles()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            var profiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)!;
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            Assert.IsGreaterThan(1, identities.Length);
            identities = [identities[0]];
            var identitiesJson = System.Text.Json.JsonSerializer.Serialize(identities);
            var digest = LuxembourgIndexBuilder.StateSha256(
                state.WorkKey, state.ApplicabilityDate, state.ExpressionIri, state.PublisherWorkIri,
                state.PublisherLegalResourceIri, state.Language, profiles, identities);
            Execute(connection,
                "UPDATE states SET article_identities_json=$identities,state_sha256=$digest",
                ("$identities", identitiesJson), ("$digest", digest));
        }, "omits or crosses its expression population");
    }

    [TestMethod]
    public async Task StrictReaderRejectsAStateWhoseIdentityListRepeatsAnArticle()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        // The search join reaches an article's state through json_each over this list and counts one
        // row per element, so a repeated identity would serve an article twice and make its cursor
        // ambiguous. The list is sorted here and its digest recomputed, so nothing but the repeat is wrong.
        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            var profiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)!;
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            Assert.IsGreaterThan(1, identities.Length);
            identities = identities.Append(identities[0]).Order(StringComparer.Ordinal).ToArray();
            var identitiesJson = System.Text.Json.JsonSerializer.Serialize(identities);
            var digest = LuxembourgIndexBuilder.StateSha256(
                state.WorkKey, state.ApplicabilityDate, state.ExpressionIri, state.PublisherWorkIri,
                state.PublisherLegalResourceIri, state.Language, profiles, identities);
            Execute(connection,
                "UPDATE states SET article_identities_json=$identities,state_sha256=$digest",
                ("$identities", identitiesJson), ("$digest", digest));
        }, "not canonical");
    }

    [TestMethod]
    public async Task StrictReaderRejectsALegalResourceOutsideItsWork()
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection =>
        {
            var state = ReadStates(connection).Single();
            const string workIri = "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/n1";
            var workKey = LuxembourgIndexBuilder.WorkKeyOf(workIri);
            var profiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.RuleProfilesJson)!;
            var identities = System.Text.Json.JsonSerializer.Deserialize<string[]>(state.ArticleIdentitiesJson)!;
            var digest = LuxembourgIndexBuilder.StateSha256(
                workKey, state.ApplicabilityDate, state.ExpressionIri, workIri,
                state.PublisherLegalResourceIri, state.Language, profiles, identities);
            Execute(connection,
                "UPDATE states SET work_key=$work, publisher_work_iri=$workIri, state_sha256=$digest",
                ("$work", workKey), ("$workIri", workIri), ("$digest", digest));
        }, "not canonical");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void StateProjectionRejectsAnObjectWhoseArticlesMixExpressionsOrLanguages(
        bool mixExpression)
    {
        const string objectRef = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string expression = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr";
        var articles = new[]
        {
            new LuxembourgIndexBuilder.ArticleRow(
                new string('b', 64), objectRef, expression, "art_1", null, "2024-02-01",
                "fra", new string('c', 64), "one", "[]"),
            new LuxembourgIndexBuilder.ArticleRow(
                new string('d', 64), objectRef,
                mixExpression ? expression + "/other" : expression,
                "art_2", null, "2024-02-01", mixExpression ? "fra" : "deu",
                new string('c', 64), "two", "[]"),
        };
        var builderType = typeof(LuxembourgIndexBuilder);
        var sourceType = builderType.GetNestedType(
            "StateSource", System.Reflection.BindingFlags.NonPublic)!;
        var source = Activator.CreateInstance(sourceType,
            expression,
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3",
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo",
            "2024-02-01",
            new string('c', 64))!;
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), sourceType);
        var sources = (System.Collections.IDictionary)Activator.CreateInstance(dictionaryType)!;
        sources.Add(objectRef, source);
        var method = builderType.GetMethod(
            "ProjectStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var exception = Assert.ThrowsExactly<System.Reflection.TargetInvocationException>(() =>
            method.Invoke(null, new object[] { articles, sources }));
        Assert.IsInstanceOfType<InvalidDataException>(exception.InnerException);
        StringAssert.Contains(exception.InnerException.Message, "does not match its admitted articles");
    }

    private static async Task<(LuxembourgIndexBuildResult Built, SourceArtifactRef CorpusRef)>
        BuildStateIndexAsync()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        return (built, corpus.ArtifactRef);
    }

    private static V3IndexCapabilityManifest RebindManifest(
        V3IndexCapabilityManifest source,
        string indexSha256)
    {
        var cells = source.Cells.Select(cell => new V3IndexCapabilityCell(
            cell.Publisher, indexSha256, cell.Operation, cell.Column, cell.Field,
            cell.Language, cell.PeriodFrom, cell.PeriodTo, cell.Population));
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            source.Publisher, indexSha256, cells, out var rebound, out var refusal),
            refusal.ToString());
        return rebound!;
    }

    private static byte[] AddUnexpectedTable(ReadOnlySpan<byte> source)
        => MutateDatabase(source, "CREATE TABLE invented(value TEXT) STRICT");

    private static void AssertTamperedDatabaseRejected(
        LuxembourgIndexBuildResult built,
        SourceArtifactRef corpusRef,
        string sql,
        string? expectedMessage = null)
    {
        var bytes = MutateDatabase(built.IndexBytes.Span, sql);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var manifest = RebindManifest(built.CapabilityManifest, digest);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            reference, bytes, corpusRef, manifest));
        if (expectedMessage is not null) StringAssert.Contains(exception.Message, expectedMessage);
    }

    private static void AssertRecomputedStateTamperRejected(
        LuxembourgIndexBuildResult built,
        SourceArtifactRef corpusRef,
        Action<SqliteConnection> tamper,
        string expectedMessage)
    {
        var bytes = MutateDatabase(built.IndexBytes.Span, connection =>
        {
            tamper(connection);
            var logicalRows = LuxembourgIndexBuilder.HashLogicalRows(
                ReadMembers(connection), ReadArticles(connection), ReadStates(connection),
                ReadWorkTitles(connection));
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1",
                ("$digest", logicalRows));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var manifest = RebindManifest(built.CapabilityManifest, digest);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            reference, bytes, corpusRef, manifest));
        StringAssert.Contains(exception.Message, expectedMessage);
    }

    private static byte[] MutateDatabase(ReadOnlySpan<byte> source, string sql)
        => MutateDatabase(source, connection => Execute(connection, sql));

    private static byte[] MutateDatabase(
        ReadOnlySpan<byte> source,
        Action<SqliteConnection> mutate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-hostile-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, source.ToArray());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
            mutate(connection);
            connection.Close();
            return File.ReadAllBytes(path);
        }
        finally
        {
            LuxembourgIndexBuilder.DeleteDatabase(path);
        }
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private static LuxembourgIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,rights_disposition,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.MemberRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return rows.ToArray();
    }

    private static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,rule_profile_sha256,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.ArticleRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9)));
        return rows.ToArray();
    }

    private static LuxembourgIndexBuilder.StateRow[] ReadStates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json FROM states ORDER BY work_key,applicability_date,expression_iri,language";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.StateRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8)));
        return rows.ToArray();
    }

    private static LuxembourgIndexBuilder.WorkTitleRow[] ReadWorkTitles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_identifier,expression_iri,language,title,normalized_title,document_date,title_kind,evidence_sha256 FROM work_titles ORDER BY work_identifier,expression_iri,language,title,title_kind,evidence_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.WorkTitleRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
            reader.GetString(7)));
        return rows.ToArray();
    }
}
