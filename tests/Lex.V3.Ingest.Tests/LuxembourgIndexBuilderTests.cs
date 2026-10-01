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
        Assert.AreEqual("41032c02cf05cd744e72109dc18e543cf87c4b82420ebcaa9cb415971c42aad7", digest);
    }

    internal const string Retained1991 = "loi-1991-08-10-n3--2024-02-01--fr.bin";

    [TestMethod]
    public void BuilderAndStrictReaderShipAsOneTerminalSlice()
    {
        var schema = (string)typeof(LuxembourgIndexBuilder)
            .GetField(nameof(LuxembourgIndexBuilder.Schema))!
            .GetRawConstantValue()!;
        Assert.AreEqual("lex-v3-luxembourg-index/7", schema);
        Assert.IsTrue(typeof(LuxembourgIndexBuilder).GetMethods().Any(static method => method.Name == nameof(LuxembourgIndexBuilder.TryBuild)));
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
    public async Task AStatesArticlesPlusTheArticlesItsDocumentHadAndItDoesNotHoldAreTheDocumentsArticles()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var real = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        // The real act has 49 admitted articles and five the reviewed profile cannot represent. One admitted article
        // is made, in memory, an article of nothing but a modification start and end, so that the document has all
        // three kinds of article and the state holds one that is marker-only. Bytes are kept as they are (Latin-1
        // reads and writes any byte), and the replacement must happen exactly once.
        var text = Encoding.Latin1.GetString(real);
        var pattern = new System.Text.RegularExpressions.Regex(
            "<article id=\"art_2\">(?<head>.*?</scl:JOLUXWork>).*?</article>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.AreEqual(1, pattern.Matches(text).Count);
        var changed = pattern.Replace(
            text,
            "<article id=\"art_2\">${head}<content><p><mod class=\"mod-start\" for=\"#pm9\"/><mod class=\"mod-end\" for=\"#pm9\"/></p></content></article>");
        Assert.AreNotEqual(text, changed);
        var xml = Encoding.Latin1.GetBytes(changed);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        // The stage's own dispositions, read from its population and not through the index.
        var outcomes = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes;
        Assert.AreEqual(54, outcomes.Count);
        Assert.AreEqual(48, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.AreEqual(1, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence));
        Assert.AreEqual(5, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        // 48 admitted and the marker-only one are held, and the five the profile could not represent are not.
        Assert.HasCount(49, state.ArticleIdentities);
        var notAdmitted = reader.ResolveArticlesNotAdmitted([state.StateSha256])[state.StateSha256];
        Assert.AreEqual(5, notAdmitted);
        // The sentence the answers carry: the state's articles plus articles_not_admitted are the document's.
        Assert.AreEqual(outcomes.Count, state.ArticleIdentities.Count + notAdmitted);
    }

    [TestMethod]
    public async Task AnArticleNestedInsideAnotherIsNotRecordedApartFromItsParentSoTheSumIsTheOutcomesAndNotTheElements()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var real = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        // A further article is nested inside art_3, in memory and byte-preserving. The profile's vocabulary does not
        // contain `article`, so it is an element outside it and art_3 is not admitted whole; and the inventory and the
        // profile both count top-level articles only, so the document has 55 article elements and 54 outcomes.
        var text = Encoding.Latin1.GetString(real);
        var pattern = new System.Text.RegularExpressions.Regex(
            "(?<parent><article id=\"art_3\">.*?)</article>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.AreEqual(1, pattern.Matches(text).Count);
        var changed = pattern.Replace(
            text,
            "${parent}<article id=\"art_3_nested\"><content><p>A nested article.</p></content></article></article>");
        var elements = new System.Text.RegularExpressions.Regex("<article[ >]").Matches(changed).Count;
        Assert.AreEqual(55, elements);
        var xml = Encoding.Latin1.GetBytes(changed);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var outcomes = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes;
        Assert.AreEqual(54, outcomes.Count);
        Assert.AreEqual(48, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.AreEqual(6, outcomes.Count(static value => value.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));
        Assert.IsTrue(outcomes.Any(static value =>
            value.Coordinate?.PublisherId == "art_3" &&
            value.Disposition == LuxembourgAknLegalContentDisposition.UnsupportedContentShape));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        var state = reader.ResolveState("loi-1991-08-10-n3", "2024-02-01").Single();
        var notAdmitted = reader.ResolveArticlesNotAdmitted([state.StateSha256])[state.StateSha256];
        Assert.HasCount(48, state.ArticleIdentities);
        Assert.AreEqual(6, notAdmitted);
        // 48 + 6 is the 54 articles the corpus recorded (what `provenance` counts by token), and not the 55 article
        // elements a reader who counts the document finds: which is why the sentence says "recorded" and not "in the
        // document".
        Assert.AreEqual(54, state.ArticleIdentities.Count + notAdmitted);
        Assert.AreNotEqual(elements, state.ArticleIdentities.Count + notAdmitted);
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
                  <scl:jolux scl:name="license">{{VerifiedLuxembourgSourceProfile.AdmittingLicence}}</scl:jolux>
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

    /// <summary>
    /// The event log is a function of the states, and the reader recomputes it: a row whose digest,
    /// number or key differs, an extra row, or a log emptied of its rows is refused even with the stamp
    /// recomputed over the tampered table, so only the recompute can refuse it. (A non-null observation
    /// time and any name but first_sighting are refused by the table's own checks.)
    /// </summary>
    [TestMethod]
    [DataRow("UPDATE events SET detail_json='{\"state_sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}'", "not the log of its states", DisplayName = "a wrong state digest")]
    [DataRow("UPDATE events SET seq=2", "do not number its events", DisplayName = "a renumbered event")]
    [DataRow("UPDATE events SET key='[\"other-work\",\"2024-01-01\",\"https://example.invalid/e\",\"fra\"]'", "not the log of its states", DisplayName = "a rewritten key")]
    [DataRow("INSERT INTO events VALUES(2,'state','[\"extra\"]','first_sighting',NULL,'{}')", "do not number its events", DisplayName = "an extra event")]
    [DataRow("DELETE FROM events", "do not number its events", DisplayName = "an emptied log")]
    public async Task StrictReaderRejectsAnEventLogThatIsNotTheGenesisLogOfItsStates(string sql, string expected)
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection => Execute(connection, sql), expected);
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

    // ---- Index schema /7's event log (STATUS item 4, predecessor chaining, the first slice). ----

    /// <summary>
    /// A genesis log as schema /7 holds it: one observation, of this build's corpus, with no predecessor, numbering
    /// every event; the log's own stamp (its schema, and the digest of exactly its observations and events); and each
    /// event naming the bodies its state's articles were read from, as the corpus holds them, so a later build that
    /// carries the log forward can tell a replaced publisher file by the log alone.
    /// </summary>
    [TestMethod]
    public async Task TheGenesisLogHasOneObservationItsOwnStampAndEachStatesSourceBodies()
    {
        var (built, corpus) = await BuildStateIndexWithCorpusAsync();
        LuxembourgIndexBuilder.EventRow[] events = [];
        LuxembourgIndexBuilder.ObservationRow[] observations = [];
        (string Schema, string Digest) logStamp = default;
        _ = MutateDatabase(built.IndexBytes.Span, connection =>
        {
            events = ReadEvents(connection);
            observations = ReadObservations(connection);
            logStamp = ReadLogStamp(connection);
        });

        Assert.IsNotEmpty(events, "the fixture's state has a first_sighting");
        CollectionAssert.AreEqual(
            new[] { new LuxembourgIndexBuilder.ObservationRow(1, corpus.ArtifactRef.Sha256, null, 1, events.Length, null) },
            observations,
            "one observation of this corpus, with no predecessor, numbering every event, at no observation time");
        Assert.AreEqual((LuxembourgIndexBuilder.EventLogSchema, LuxembourgIndexBuilder.HashEventLog(observations, events)), logStamp);

        var held = corpus.Set.Members
            .Where(static member => member.Publisher == Lex.V3.Contracts.PublisherId.LuLegilux && member.BodySha256 is not null)
            .Select(static member => member.BodySha256!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in events)
        {
            var bodies = System.Text.Json.JsonDocument.Parse(value.DetailJson).RootElement.GetProperty("source_body_sha256")
                .EnumerateArray().Select(static body => body.GetString()!).ToArray();
            Assert.IsNotEmpty(bodies, $"event {value.Seq} names its state's source bodies");
            CollectionAssert.AreEqual(bodies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), bodies, "sorted, each once");
            Assert.IsTrue(bodies.All(held.Contains), $"event {value.Seq} names a body the corpus holds");
        }

        using var reader = LuxembourgIndexReader.OpenAndVerify(built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        reader.VerifyEventLogSources(corpus);
    }

    /// <summary>
    /// The observations and the log stamp are the reader's own recomputation, with the logical-rows stamp recomputed
    /// over the tampered tables so only these checks can refuse: an observation numbering an event the log lacks, of
    /// another corpus, a second or no observation; a log stamp of other rows, of another log schema, or none.
    /// </summary>
    [TestMethod]
    [DataRow("UPDATE observations SET last_seq=last_seq+1", "do not number its events", DisplayName = "an observation numbering an event the log lacks")]
    [DataRow("UPDATE observations SET corpus_sha256='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'", "is not of this index's corpus", DisplayName = "an observation of another corpus")]
    [DataRow("INSERT INTO observations VALUES(2,'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb','cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',2,1,NULL)", "is not of this index's corpus", DisplayName = "a second observation of another corpus")]
    [DataRow("DELETE FROM observations", "do not number its events", DisplayName = "no observation")]
    [DataRow("UPDATE log_stamp SET log_rows_sha256='0000000000000000000000000000000000000000000000000000000000000000'", "does not match its log stamp", DisplayName = "a log stamp of other rows")]
    [DataRow("UPDATE log_stamp SET log_schema='lex-v3-event-log/0'", "does not match its log stamp", DisplayName = "a log stamp of another log schema")]
    [DataRow("DELETE FROM log_stamp", "does not match its log stamp", DisplayName = "no log stamp")]
    public async Task StrictReaderRejectsATamperedObservationOrLogStamp(string sql, string expected)
    {
        var (built, corpusRef) = await BuildStateIndexAsync();

        AssertRecomputedStateTamperRejected(built, corpusRef, connection => Execute(connection, sql), expected);
    }

    /// <summary>
    /// Source bodies the index alone cannot confirm: an event naming a body its state was not read from passes the
    /// reader (the index holds no body digest) and is refused by the corpus; one naming its body twice is not the
    /// canonical genesis log, and the reader refuses it. Both stamps are recomputed, so only these checks can refuse.
    /// </summary>
    [TestMethod]
    public async Task AnEventsSourceBodiesAreTheCorpusOwnAndCanonical()
    {
        var (built, corpus) = await BuildStateIndexWithCorpusAsync();
        var (foreign, foreignManifest, foreignRef) = Resealed(built, detail => detail["source_body_sha256"] = new System.Text.Json.Nodes.JsonArray(new string('e', 64)));
        using (var reader = LuxembourgIndexReader.OpenAndVerify(foreignRef, foreign, corpus.ArtifactRef, foreignManifest))
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() => reader.VerifyEventLogSources(corpus));
            StringAssert.Contains(exception.Message, "names source bodies the corpus does not hold");
        }

        var (twice, twiceManifest, twiceRef) = Resealed(built, detail =>
        {
            var body = detail["source_body_sha256"]![0]!.GetValue<string>();
            detail["source_body_sha256"] = new System.Text.Json.Nodes.JsonArray(body, body);
        });
        var refused = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(twiceRef, twice, corpus.ArtifactRef, twiceManifest));
        StringAssert.Contains(refused.Message, "not the log of its states");
    }

    /// <summary>
    /// The mount checks the event log's source bodies against the corpus it mounts, so an index whose log names a body
    /// the corpus does not hold for the state is not mounted, though the index alone verifies.
    /// </summary>
    [TestMethod]
    public async Task TheMountRefusesAnEventLogNamingABodyItsCorpusDoesNotHoldForTheState()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var indexPath = Path.Combine(fixture.Directory, Lex.V3.Api.V3CorpusMount.IndexFileName);
        var bytes = ResealedBytes(await File.ReadAllBytesAsync(indexPath),
            detail => detail["source_body_sha256"] = new System.Text.Json.Nodes.JsonArray(new string('e', 64)), out var articles, out var titles);
        await File.WriteAllBytesAsync(indexPath, bytes);
        var manifest = LuxembourgIndexBuilder.MeasureCapabilities(Convert.ToHexStringLower(SHA256.HashData(bytes)), articles, titles);
        using (var stream = new MemoryStream())
        {
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, Lex.V3.Api.V3CorpusMount.CapabilityManifestFileName), stream.ToArray());
        }

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Lex.V3.Api.V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));
        StringAssert.Contains(exception.Message, "names source bodies the corpus does not hold");
    }

    // ---- Predecessor chaining, the second slice: the predecessor as a build input. ----

    /// <summary>
    /// G3a: a build chained to a predecessor carries the predecessor's observations and events forward unchanged, an
    /// exact prefix, and appends one observation of its own naming the predecessor's digest; the same states append no
    /// event (silence). Two builds with the same predecessor are the same bytes, and a third chains onto the second.
    /// </summary>
    [TestMethod]
    public async Task AChainedBuildCarriesThePredecessorsLogAsAnExactPrefix()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        var predecessor = LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out var readRefusal, out var readDetail);
        Assert.IsNotNull(predecessor, $"{readRefusal}: {readDetail}");
        Assert.AreEqual(first.IndexRef.Sha256, predecessor.IndexSha256);

        var second = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, out var refusal, out var detail);
        Assert.IsNotNull(second, $"{refusal}: {detail}");
        var again = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, out refusal, out detail);
        Assert.IsNotNull(again, $"{refusal}: {detail}");
        CollectionAssert.AreEqual(second.IndexBytes.ToArray(), again.IndexBytes.ToArray(), "two builds with one predecessor are one index");

        var (firstObservations, firstEvents) = ReadLog(first.IndexBytes.ToArray());
        var (secondObservations, secondEvents) = ReadLog(second.IndexBytes.ToArray());
        CollectionAssert.AreEqual(firstEvents, secondEvents, "the predecessor's events, carried forward; the same states append none");
        CollectionAssert.AreEqual(firstObservations, secondObservations.Take(firstObservations.Length).ToArray(), "the predecessor's observations, an exact prefix");
        Assert.AreEqual(
            new LuxembourgIndexBuilder.ObservationRow(2, corpus.ArtifactRef.Sha256, first.IndexRef.Sha256, firstEvents.Length + 1, firstEvents.Length, null),
            secondObservations[^1],
            "one observation of this build, naming the predecessor, appending no event");
        using (var reader = LuxembourgIndexReader.OpenAndVerify(second.IndexRef, second.IndexBytes.Span, corpus.ArtifactRef, second.CapabilityManifest))
        {
            reader.VerifyEventLogSources(corpus);
        }

        var third = LuxembourgIndexBuilder.TryBuild(
            envelope, LuxembourgIndexPredecessor.TryRead(second.IndexRef, second.IndexBytes.Span, out _, out _), out refusal, out detail);
        Assert.IsNotNull(third, $"{refusal}: {detail}");
        var (thirdObservations, thirdEvents) = ReadLog(third.IndexBytes.ToArray());
        CollectionAssert.AreEqual(secondObservations, thirdObservations.Take(secondObservations.Length).ToArray(), "the chain grows by one observation a build");
        Assert.AreEqual(second.IndexRef.Sha256, thirdObservations[^1].PredecessorIndexSha256);
        CollectionAssert.AreEqual(firstEvents, thirdEvents);
    }

    /// <summary>
    /// A state the predecessor's log does not hold is appended: a first_sighting, or expression_added when the log holds
    /// the same work and date in another language; one the log holds unchanged appends nothing; one it holds with
    /// another digest or other bodies is refused until comparison events are served.
    /// </summary>
    [TestMethod]
    public void NewStatesAppendFirstSightingsAndANewLanguageOfAHeldWorkAndDateIsExpressionAdded()
    {
        static LuxembourgIndexBuilder.StateRow State(string work, string date, string expression, string language, string digest) =>
            new(work, date, digest, expression, "w", "r", language, "[]", "[]");
        var held = State("loi-a", "2024-01-01", "https://example.invalid/a/fr", "fra", new string('1', 64));
        var fold = new Dictionary<string, LuxembourgIndexBuilder.LoggedState>(StringComparer.Ordinal)
        {
            [LuxembourgIndexBuilder.StateKey(held)] = new("loi-a", "2024-01-01", "https://example.invalid/a/fr", "fra", held.StateSha256, [new string('b', 64)]),
        };
        var german = State("loi-a", "2024-01-01", "https://example.invalid/a/de", "deu", new string('2', 64));
        var other = State("loi-b", "2024-01-01", "https://example.invalid/b/fr", "fra", new string('3', 64));
        var bodies = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [held.StateSha256] = [new string('b', 64)],
            [german.StateSha256] = [new string('c', 64)],
            [other.StateSha256] = [new string('d', 64)],
        };

        var appended = LuxembourgIndexBuilder.ProjectChainedEvents(fold, [other, held, german], bodies, 7, out var changed);
        Assert.IsNull(changed);
        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgIndexBuilder.EventRow(7, "state", LuxembourgIndexBuilder.StateKey(german), "expression_added", null,
                    $"{{\"state_sha256\":\"{german.StateSha256}\",\"source_body_sha256\":[\"{new string('c', 64)}\"]}}"),
                new LuxembourgIndexBuilder.EventRow(8, "state", LuxembourgIndexBuilder.StateKey(other), "first_sighting", null,
                    $"{{\"state_sha256\":\"{other.StateSha256}\",\"source_body_sha256\":[\"{new string('d', 64)}\"]}}"),
            },
            appended,
            "in the states' key order, numbered on from the log, the held state silent");

        var redigested = held with { StateSha256 = new string('9', 64) };
        bodies[redigested.StateSha256] = [new string('b', 64)];
        Assert.IsNull(LuxembourgIndexBuilder.ProjectChainedEvents(fold, [redigested], bodies, 7, out changed), "a held key with another digest");
        StringAssert.Contains(changed, "/lu-legilux/loi-a/2024-01-01");
        bodies[held.StateSha256] = [new string('e', 64)];
        Assert.IsNull(LuxembourgIndexBuilder.ProjectChainedEvents(fold, [held], bodies, 7, out changed), "a held key with other source bodies");
    }

    /// <summary>
    /// A predecessor is read by its own digest and log: other bytes than its build names, no log stamp (index schema 6
    /// and before), another log schema, a log that does not match its stamp, or a log its observations do not number,
    /// are each refused with their reason.
    /// </summary>
    [TestMethod]
    public async Task APredecessorIsReadByItsOwnDigestAndItsOwnLog()
    {
        var (_, first, _) = await BuildStateEnvelopeAsync();
        var bytes = first.IndexBytes.ToArray();
        Assert.IsNull(LuxembourgIndexPredecessor.TryRead(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(new string('0', 64)), new string('0', 64)), bytes, out var refusal, out _));
        Assert.AreEqual(LuxembourgIndexBuildRefusal.PredecessorMismatch, refusal, "other bytes than the build names");

        foreach (var (tamper, restamp, expected, reason) in new (string Sql, bool Restamp, LuxembourgIndexBuildRefusal Refusal, string Reason)[]
                 {
                     ("DROP TABLE log_stamp", false, LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers, "no event log stamp"),
                     ("UPDATE log_stamp SET log_schema='lex-v3-event-log/0'", false, LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers, "lex-v3-event-log/0"),
                     ("UPDATE log_stamp SET log_rows_sha256='" + new string('0', 64) + "'", false, LuxembourgIndexBuildRefusal.PredecessorMismatch, "does not match its log stamp"),
                     ("UPDATE observations SET last_seq=last_seq+1", true, LuxembourgIndexBuildRefusal.PredecessorMismatch, "do not number its events"),
                     // The review of #866's reproduction: a genesis observation's event renamed, the log stamp recomputed.
                     ("UPDATE events SET event='expression_added' WHERE seq=1", true, LuxembourgIndexBuildRefusal.PredecessorMismatch, "holds events that are not the ones it appends"),
                 })
        {
            var (tampered, reference) = Tampered(bytes, tamper, restamp);
            Assert.IsNull(LuxembourgIndexPredecessor.TryRead(reference, tampered, out refusal, out var detail), tamper);
            Assert.AreEqual(expected, refusal, tamper);
            StringAssert.Contains(detail, reason, tamper);
        }
    }

    /// <summary>
    /// The review of #866: every observation of a carried log is replayed, not only the last, so a chained index whose
    /// predecessor part holds an event no build could have written (a genesis event renamed expression_added) is refused
    /// by the reader, with both stamps recomputed over the change.
    /// </summary>
    [TestMethod]
    public async Task AChainedIndexWhoseCarriedLogHoldsAFalseEventIsRefused()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        var second = LuxembourgIndexBuilder.TryBuild(
            envelope, LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out _, out _), out var refusal, out var detail);
        Assert.IsNotNull(second, $"{refusal}: {detail}");
        var renamed = MutateDatabase(second.IndexBytes.Span, connection =>
        {
            Execute(connection, "UPDATE events SET event='expression_added' WHERE seq=1");
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LuxembourgIndexBuilder.HashLogicalRows(
                ReadMembers(connection), ReadArticles(connection), ReadStates(connection), ReadWorkTitles(connection), ReadRelations(connection), ReadWorkFacts(connection), ReadEvents(connection))));
            Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                ("$digest", LuxembourgIndexBuilder.HashEventLog(ReadObservations(connection), ReadEvents(connection))));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(renamed));
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest), renamed, corpus.ArtifactRef, RebindManifest(second.CapabilityManifest, digest)));
        StringAssert.Contains(exception.Message, "not the log of its states");
        StringAssert.Contains(exception.InnerException!.Message, "observation 1 holds events that are not the ones it appends");
    }

    /// <summary>
    /// A predecessor whose log holds this build's state with another digest is refused (comparison events are not
    /// served yet); one whose log does not hold the state yet gets a first_sighting appended, which the reader then
    /// holds to exactly what the observation must append.
    /// </summary>
    [TestMethod]
    public async Task AChainedBuildAppendsANewStateAndRefusesAChangedOne()
    {
        var (envelope, first, corpus) = await BuildStateEnvelopeAsync();
        var bytes = first.IndexBytes.ToArray();
        var (changedLog, changedRef) = Tampered(bytes, connection =>
        {
            var held = ReadEvents(connection)[0];
            var detail = System.Text.Json.Nodes.JsonNode.Parse(held.DetailJson)!;
            detail["state_sha256"] = new string('9', 64);
            Execute(connection, "UPDATE events SET detail_json=$detail WHERE seq=1", ("$detail", detail.ToJsonString()));
        });
        var changed = LuxembourgIndexPredecessor.TryRead(changedRef, changedLog, out var refusal, out var readDetail);
        Assert.IsNotNull(changed, $"{refusal}: {readDetail}");
        Assert.IsNull(LuxembourgIndexBuilder.TryBuild(envelope, changed, out refusal, out var detail));
        Assert.AreEqual(LuxembourgIndexBuildRefusal.PredecessorStateChanged, refusal);
        StringAssert.Contains(detail, "comparison events are not served yet");

        var (emptyLog, emptyRef) = Tampered(bytes, connection =>
        {
            Execute(connection, "DELETE FROM events");
            Execute(connection, "UPDATE observations SET last_seq=0");
        });
        var empty = LuxembourgIndexPredecessor.TryRead(emptyRef, emptyLog, out refusal, out readDetail);
        Assert.IsNotNull(empty, $"{refusal}: {readDetail}");
        var chained = LuxembourgIndexBuilder.TryBuild(envelope, empty, out refusal, out detail);
        Assert.IsNotNull(chained, $"{refusal}: {detail}");
        var (observations, events) = ReadLog(chained.IndexBytes.ToArray());
        Assert.AreEqual(2, observations.Length);
        Assert.AreEqual(new LuxembourgIndexBuilder.ObservationRow(2, corpus.ArtifactRef.Sha256, emptyRef.Sha256, 1, events.Length, null), observations[1]);
        Assert.IsTrue(events.All(static value => value.Event == "first_sighting"), "each state, new to the log, first sighted");

        // The appended events are the reader's own recomputation: one renamed, both stamps recomputed, is refused.
        var renamed = MutateDatabase(chained.IndexBytes.Span, connection =>
        {
            Execute(connection, "UPDATE events SET event='expression_added' WHERE seq=1");
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LuxembourgIndexBuilder.HashLogicalRows(
                ReadMembers(connection), ReadArticles(connection), ReadStates(connection), ReadWorkTitles(connection), ReadRelations(connection), ReadWorkFacts(connection), ReadEvents(connection))));
            Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                ("$digest", LuxembourgIndexBuilder.HashEventLog(ReadObservations(connection), ReadEvents(connection))));
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(renamed));
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest), renamed, corpus.ArtifactRef, RebindManifest(chained.CapabilityManifest, digest)));
        StringAssert.Contains(exception.Message, "not the log of its states");
    }

    /// <summary>The state index's envelope and its first build, with the corpus, for the chained builds.</summary>
    private static async Task<(Stage3DerivationProfileEnvelope Envelope, LuxembourgIndexBuildResult Built, VerifiedLexCorpus6ManifestSet Corpus)> BuildStateEnvelopeAsync()
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
        return (envelope, built, corpus.VerifiedSet);
    }

    private static (LuxembourgIndexBuilder.ObservationRow[] Observations, LuxembourgIndexBuilder.EventRow[] Events) ReadLog(byte[] bytes)
    {
        LuxembourgIndexBuilder.ObservationRow[] observations = [];
        LuxembourgIndexBuilder.EventRow[] events = [];
        _ = MutateDatabase(bytes, connection =>
        {
            observations = ReadObservations(connection);
            events = ReadEvents(connection);
        });
        return (observations, events);
    }

    /// <summary>An index changed by <paramref name="tamper"/>, its log stamp recomputed over the change when asked, with its new digest.</summary>
    private static (byte[] Bytes, SourceArtifactRef Reference) Tampered(byte[] source, string tamper, bool restampLog) =>
        Tampered(source, connection => Execute(connection, tamper), restampLog);

    private static (byte[] Bytes, SourceArtifactRef Reference) Tampered(byte[] source, Action<SqliteConnection> tamper, bool restampLog = true)
    {
        var bytes = MutateDatabase(source, connection =>
        {
            tamper(connection);
            if (restampLog)
            {
                Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                    ("$digest", LuxembourgIndexBuilder.HashEventLog(ReadObservations(connection), ReadEvents(connection))));
            }
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest));
    }

    /// <summary>The state index built with its corpus, for the checks that need the corpus's own body digests.</summary>
    internal static async Task<(LuxembourgIndexBuildResult Built, VerifiedLexCorpus6ManifestSet Corpus)> BuildStateIndexWithCorpusAsync()
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
        return (built, corpus.VerifiedSet);
    }

    /// <summary>The index with its first event's detail changed, and both stamps (logical rows, log) recomputed over it.</summary>
    private static (byte[] Bytes, V3IndexCapabilityManifest Manifest, SourceArtifactRef Reference) Resealed(
        LuxembourgIndexBuildResult built,
        Action<System.Text.Json.Nodes.JsonNode> change)
    {
        var bytes = ResealedBytes(built.IndexBytes.ToArray(), change, out _, out _);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, RebindManifest(built.CapabilityManifest, digest), new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest));
    }

    internal static byte[] ResealedBytes(
        byte[] source,
        Action<System.Text.Json.Nodes.JsonNode> change,
        out LuxembourgIndexBuilder.ArticleRow[] articles,
        out LuxembourgIndexBuilder.WorkTitleRow[] titles)
    {
        LuxembourgIndexBuilder.ArticleRow[] readArticles = [];
        LuxembourgIndexBuilder.WorkTitleRow[] readTitles = [];
        var bytes = MutateDatabase(source, connection =>
        {
            var first = ReadEvents(connection)[0];
            var detail = System.Text.Json.Nodes.JsonNode.Parse(first.DetailJson)!;
            change(detail);
            Execute(connection, "UPDATE events SET detail_json=$detail WHERE seq=$seq", ("$detail", detail.ToJsonString()), ("$seq", first.Seq));
            readArticles = ReadArticles(connection);
            readTitles = ReadWorkTitles(connection);
            Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LuxembourgIndexBuilder.HashLogicalRows(
                ReadMembers(connection), readArticles, ReadStates(connection), readTitles, ReadRelations(connection), ReadWorkFacts(connection), ReadEvents(connection))));
            Execute(connection, "UPDATE log_stamp SET log_rows_sha256=$digest WHERE log_stamp_id=1",
                ("$digest", LuxembourgIndexBuilder.HashEventLog(ReadObservations(connection), ReadEvents(connection))));
        });
        articles = readArticles;
        titles = readTitles;
        return bytes;
    }

    internal static LuxembourgIndexBuilder.ObservationRow[] ReadObservations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT observation,corpus_sha256,predecessor_index_sha256,first_seq,last_seq,observed_from FROM observations ORDER BY observation";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.ObservationRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows.ToArray();
    }

    private static (string Schema, string Digest) ReadLogStamp(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT log_schema,log_rows_sha256 FROM log_stamp WHERE log_stamp_id=1";
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read(), "the log stamp row");
        return (reader.GetString(0), reader.GetString(1));
    }

    internal static async Task<(LuxembourgIndexBuildResult Built, SourceArtifactRef CorpusRef)>
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

    internal static V3IndexCapabilityManifest RebindManifest(
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
                ReadWorkTitles(connection), ReadRelations(connection), ReadWorkFacts(connection), ReadEvents(connection));
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

    internal static byte[] MutateDatabase(
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

    internal static void Execute(
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

    internal static LuxembourgIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
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

    internal static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
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

    internal static LuxembourgIndexBuilder.StateRow[] ReadStates(SqliteConnection connection)
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

    internal static LuxembourgIndexBuilder.WorkTitleRow[] ReadWorkTitles(SqliteConnection connection)
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
    internal static LuxembourgIndexBuilder.EventRow[] ReadEvents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT seq,scope,key,event,observed_from,detail_json FROM events ORDER BY seq";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.EventRow>();
        while (reader.Read()) values.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        return values.ToArray();
    }

    /// <summary>
    /// Rewrites the event log as the genesis log of <paramref name="states"/>, for a fixture that added or
    /// changed states legitimately (the log is a function of the states, as the reader checks).
    /// </summary>
    internal static LuxembourgIndexBuilder.EventRow[] RefreshGenesisEvents(
        SqliteConnection connection,
        IReadOnlyList<LuxembourgIndexBuilder.ArticleRow> articles,
        IReadOnlyList<LuxembourgIndexBuilder.StateRow> states,
        IReadOnlyDictionary<string, string> bodyByObjectRef)
    {
        // The genesis log of the edited states, re-sealed whole: its events (each naming its state's source bodies),
        // its one observation and its own stamp, as the builder writes them.
        foreach (var table in new[] { "events", "observations", "log_stamp" })
        {
            using var clear = connection.CreateCommand();
            clear.CommandText = $"DELETE FROM {table}";
            clear.ExecuteNonQuery();
        }

        var events = LuxembourgIndexBuilder.ProjectGenesisEvents(
            states, LuxembourgIndexBuilder.SourceBodiesOfStates(articles, states, bodyByObjectRef));
        foreach (var value in events)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO events VALUES($seq,$scope,$key,$event,$observed,$detail)";
            insert.Parameters.AddWithValue("$seq", value.Seq);
            insert.Parameters.AddWithValue("$scope", value.Scope);
            insert.Parameters.AddWithValue("$key", value.Key);
            insert.Parameters.AddWithValue("$event", value.Event);
            insert.Parameters.AddWithValue("$observed", (object?)value.ObservedFrom ?? DBNull.Value);
            insert.Parameters.AddWithValue("$detail", value.DetailJson);
            Assert.AreEqual(1, insert.ExecuteNonQuery());
        }

        string corpusSha256;
        using (var stamp = connection.CreateCommand())
        {
            stamp.CommandText = "SELECT corpus_sha256 FROM stamp WHERE stamp_id=1";
            corpusSha256 = (string)stamp.ExecuteScalar()!;
        }

        var observations = LuxembourgIndexBuilder.ProjectGenesisObservations(corpusSha256, events.Length);
        foreach (var observation in observations)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO observations VALUES($observation,$corpus,NULL,$first,$last,NULL)";
            insert.Parameters.AddWithValue("$observation", observation.Observation);
            insert.Parameters.AddWithValue("$corpus", observation.CorpusSha256);
            insert.Parameters.AddWithValue("$first", observation.FirstSeq);
            insert.Parameters.AddWithValue("$last", observation.LastSeq);
            Assert.AreEqual(1, insert.ExecuteNonQuery());
        }

        using (var logStamp = connection.CreateCommand())
        {
            logStamp.CommandText = "INSERT INTO log_stamp VALUES(1,$schema,$digest)";
            logStamp.Parameters.AddWithValue("$schema", LuxembourgIndexBuilder.EventLogSchema);
            logStamp.Parameters.AddWithValue("$digest", LuxembourgIndexBuilder.HashEventLog(observations, events));
            Assert.AreEqual(1, logStamp.ExecuteNonQuery());
        }

        return events;
    }

    /// <summary>The body digest of every Luxembourg member of a mount's corpus file, by object reference.</summary>
    internal static IReadOnlyDictionary<string, string> CorpusBodies(string directory) =>
        LuxembourgIndexBuilder.BodiesByObjectRef(VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(
            File.ReadAllBytes(Path.Combine(directory, Lex.V3.Api.V3CorpusMount.CorpusFileName))));

    internal static LuxembourgIndexBuilder.WorkFactRow[] ReadWorkFacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts ORDER BY subject_iri,predicate,object_kind,object_value,datatype_iri,language_tag,evidence_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.WorkFactRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return rows.ToArray();
    }

    internal static LuxembourgIndexBuilder.RelationRow[] ReadRelations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT from_ref,ordinal,edge_type,asserted_by,source_predicate,in_note,label,href,to_kind,to_ref FROM relations ORDER BY from_ref,ordinal";
        using var reader = command.ExecuteReader();
        var rows = new List<LuxembourgIndexBuilder.RelationRow>();
        while (reader.Read()) rows.Add(new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt32(5) == 1,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return rows.ToArray();
    }
}
