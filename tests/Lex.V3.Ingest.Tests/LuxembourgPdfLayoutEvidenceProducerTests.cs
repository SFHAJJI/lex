using System.Reflection;
using System.Security.Cryptography;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPdfLayoutEvidenceProducerTests
{
    [TestMethod]
    public async Task RealPublisherPdfsRetainImagesRenderingModesAndCanonicalIdentity()
    {
        var consolidated = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var invisible = Fixture("lu-pdf-invisible-1987-12-23-n5.bin", 237_720,
            "9f34cd405f1f0f9b4349fa967b2271b257d4982fbf90351f4b6261f56633c111");

        var clean = await ProduceOneAsync(consolidated);
        var cleanDocument = await OpenAsync(clean.Store, clean.Outcome);
        Assert.HasCount(1, cleanDocument.Pages);
        Assert.HasCount(1_462, cleanDocument.Glyphs);
        Assert.AreEqual(0, cleanDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(0, InvisibleCount(cleanDocument));
        Assert.AreEqual("a39876abcccef29a0f22fc53468401ec28462f408c70e7792a7fcd06ac1b255f",
            clean.Outcome.SemanticIdentitySha256,
            "A field omission or reordering must move the canonical admitted identity.");

        var hidden = await ProduceOneAsync(invisible);
        var hiddenDocument = await OpenAsync(hidden.Store, hidden.Outcome);
        Assert.AreEqual(58, hiddenDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(27, InvisibleCount(hiddenDocument));

        var imageBearing = await ProduceOneAsync(LuxembourgDocumentFetchFixtures.PdfBody());
        var imageDocument = await OpenAsync(imageBearing.Store, imageBearing.Outcome);
        Assert.AreEqual(9, imageDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(0, InvisibleCount(imageDocument));
    }

    [TestMethod]
    public async Task MixedPopulationPreservesSourceOrderAndReadsEachExactReceipt()
    {
        var firstBytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var secondBytes = Fixture("lu-pdf-invisible-1987-12-23-n5.bin", 237_720,
            "9f34cd405f1f0f9b4349fa967b2271b257d4982fbf90351f4b6261f56633c111");
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompleteTwoPublisherPdfsForStage3BodyCompositionAsync(firstBytes, secondBytes));
        var store = await StoreAsync(firstBytes, secondBytes);

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        Assert.HasCount(2, result.Population!.Outcomes);
        CollectionAssert.AreEqual(
            source.Outcomes.Select(static value => value.PublisherItemIri).ToArray(),
            result.Population.Outcomes.Select(static value => value.SourceEligibility.PublisherItemIri).ToArray());
        CollectionAssert.AreEqual(
            source.Outcomes.Select(static value => value.TransportReceipt.Reference.ContentSha256).ToArray(),
            result.Population.Outcomes.Select(static value => value.TransportReceipt.Reference.ContentSha256).ToArray(),
            "Every output must carry the exact source member receipt in source order.");
        var firstOutcome = result.Population.Outcomes.Single(value =>
            value.TransportReceipt.Reference.ContentSha256 == CustodyDigest.Of(firstBytes));
        var secondOutcome = result.Population.Outcomes.Single(value =>
            value.TransportReceipt.Reference.ContentSha256 == CustodyDigest.Of(secondBytes));
        var firstDocument = await OpenAsync(store, firstOutcome);
        var secondDocument = await OpenAsync(store, secondOutcome);
        Assert.AreEqual(0, firstDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(58, secondDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(27, InvisibleCount(secondDocument));
    }

    [TestMethod]
    public async Task ImageOnlyPdfIsAZeroGlyphGapWhosePagesSurvive()
    {
        var produced = await ProduceOneAsync(
            EuImageOnlyAnnexProducerTests.MinimalPdf(image: true, text: false));

        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, produced.Outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.NoTextGlyphs, produced.Outcome.GapReason);
        Assert.AreEqual(1, produced.Outcome.PageCount);
        Assert.AreEqual(0, produced.Outcome.GlyphCount);
        Assert.IsNotNull(produced.Outcome.LayoutEvidenceReceipt);
        var document = await OpenAsync(produced.Store, produced.Outcome);
        Assert.HasCount(1, document.Pages);
        Assert.AreEqual(1, document.Pages.Single().ImageCount);
        Assert.IsEmpty(document.Glyphs);
    }

    [TestMethod]
    public async Task ArtifactCeilingStopsEncodingAsAnExplicitMemberGap()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var inner = await StoreAsync(bytes);
        var store = CreateGuardStore.Wrap(inner, CreateBehavior.FailTest);

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store, maximumArtifactBytes: 64)
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.EvidenceArtifactTooLarge, outcome.GapReason);
        Assert.IsNull(outcome.LayoutEvidenceReceipt);
    }

    [TestMethod]
    public void ArtifactCeilingStopsOnTheCrossingGlyphWriteWithoutGrowingTheBuffer()
    {
        const string ruleProfileSha256 =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string sourceIdentitySha256 =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var page = new LuxembourgPdfPageEvidence(1, 612d, 792d, 0, 0);
        var glyph = new LuxembourgPdfGlyphEvidence(
            1, 0, "A", 10d, 20d, 30d, 40d, 12d, "FixtureFont", 0, 0);

        int prefixLength;
        using (var probe = new LuxembourgPdfLayoutEvidenceArtifactCodec.Writer(
                   ruleProfileSha256, sourceIdentitySha256, 1, long.MaxValue))
        {
            probe.WritePage(page, glyphCount: 1);
            prefixLength = probe.ToMemory().Length;
        }

        using var bounded = new LuxembourgPdfLayoutEvidenceArtifactCodec.Writer(
            ruleProfileSha256, sourceIdentitySha256, 1, prefixLength);
        bounded.WritePage(page, glyphCount: 1);
        Assert.AreEqual(prefixLength, bounded.ToMemory().Length,
            "The ceiling must admit the complete header and page prefix.");

        Assert.ThrowsExactly<LayoutEvidenceArtifactTooLargeException>(() =>
            bounded.WriteGlyph(glyph));
        Assert.AreEqual(prefixLength, bounded.ToMemory().Length,
            "The rejected crossing write must not grow the retained buffer beyond its ceiling.");
    }

    [TestMethod]
    public async Task UnreadableEligiblePdfIsAnExplicitGapRatherThanInventedText()
    {
        var bytes = "%PDF-1.7 deliberately unreadable\n%%EOF\n"u8.ToArray();
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = await StoreAsync(bytes);

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.PdfUnreadable, outcome.GapReason);
        Assert.IsNull(outcome.LayoutEvidenceReceipt);
        Assert.AreEqual(0, outcome.PageCount);
        Assert.AreEqual(0, outcome.GlyphCount);
    }

    [TestMethod]
    public async Task MissingRetainedBytesRefuseTheWholePopulation()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var result = await new LuxembourgPdfLayoutEvidenceProducer(
                new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore())
            .RunAsync(source, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.IsNull(result.Population);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceProductionRefusal.RetainedBytesUnavailable, result.Refusal);
    }

    [TestMethod]
    public async Task ArtifactCustodyFailureRefusesTheWholePopulation()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var inner = await StoreAsync(bytes);
        var store = CreateGuardStore.Wrap(inner, CreateBehavior.Refuse);

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.IsNull(result.Population);
        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceProductionRefusal.LayoutEvidenceCustodyUnavailable,
            result.Refusal);
    }

    [TestMethod]
    public async Task NonPdfAndUpstreamGapRemainExplicitAndNeedNoCustodyRead()
    {
        var xml = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync());
        var upstreamGap = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompleteWithDistinctReceiptsForStage3BodyCompositionAsync());
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();

        var xmlResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(xml, CancellationToken.None);
        var gapResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(upstreamGap, CancellationToken.None);

        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.NotApplicable,
            xmlResult.Population!.Outcomes.Single().Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.UpstreamEligibilityGap,
            gapResult.Population!.Outcomes.Single().GapReason);
    }

    [TestMethod]
    public async Task EqualPublisherEvidenceHasStableIdentityAcrossRuns()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var firstSource = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var secondSource = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = await StoreAsync(bytes);

        var first = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(firstSource, CancellationToken.None);
        var second = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(secondSource, CancellationToken.None);

        Assert.AreEqual(first.Population!.IdentitySha256, second.Population!.IdentitySha256);
        Assert.AreEqual(first.Population.Outcomes.Single().SemanticIdentitySha256,
            second.Population.Outcomes.Single().SemanticIdentitySha256);
        Assert.AreEqual(first.Population.Outcomes.Single().LayoutEvidenceReceipt!.Reference.ContentSha256,
            second.Population.Outcomes.Single().LayoutEvidenceReceipt!.Reference.ContentSha256);
    }

    [TestMethod]
    public void PublicDoorAcceptsOnlyTheProofCompleteEligibilityPopulation()
    {
        var parameters = typeof(LuxembourgPdfLayoutEvidenceProducer)
            .GetMethod(nameof(LuxembourgPdfLayoutEvidenceProducer.RunAsync))!
            .GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgPdfProfileEligibilityPopulation), typeof(CancellationToken) },
            parameters);
    }

    private static int InvisibleCount(LuxembourgPdfLayoutEvidenceDocument document) =>
        document.Glyphs.Count(static glyph => glyph.RenderingMode == 3);

    private static async Task<(LuxembourgPdfLayoutEvidenceOutcome Outcome, ICustodyStore Store)>
        ProduceOneAsync(byte[] bytes)
    {
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = await StoreAsync(bytes);
        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);
        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        return (result.Population!.Outcomes.Single(), store);
    }

    private static byte[] Fixture(string fileName, int expectedLength, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", fileName));
        Assert.AreEqual(expectedLength, bytes.Length);
        Assert.AreEqual(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static async Task<LuxembourgPdfProfileEligibilityPopulation> EligibilityAsync(
        LuxembourgQueryExecutionResult luxembourg)
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var europe = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }.Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            europe, [acquired.Outcome]);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(
            formex, [acquired.Classification]);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            europe, luxembourg, formex, classifications, fidelity,
            out var envelopeRefusal, out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");
        var composition = Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail);
        Assert.IsNotNull(composition, $"{refusal}: {detail}");
        return LuxembourgPdfProfileEligibilityProducer.Produce(composition);
    }

    private static Task<LuxembourgPdfLayoutEvidenceDocument> OpenAsync(
        ICustodyStore store,
        LuxembourgPdfLayoutEvidenceOutcome outcome) =>
        LuxembourgPdfLayoutEvidenceArtifactReader.ReadAsync(outcome, store, CancellationToken.None);

    private static async Task<ICustodyStore> StoreAsync(params byte[][] retained)
    {
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        foreach (var bytes in retained)
        {
            _ = await store.CreateAsync(
                bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }

        return store;
    }

    private enum CreateBehavior
    {
        FailTest,
        Refuse,
    }

    private class CreateGuardStore : DispatchProxy
    {
        private ICustodyStore _inner = null!;
        private CreateBehavior _behavior;

        internal static ICustodyStore Wrap(ICustodyStore inner, CreateBehavior behavior)
        {
            var proxy = Create<ICustodyStore, CreateGuardStore>();
            var state = (CreateGuardStore)(object)proxy;
            state._inner = inner;
            state._behavior = behavior;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICustodyStore.CreateAsync))
            {
                if (_behavior == CreateBehavior.FailTest)
                    throw new AssertFailedException("An oversized artifact must stop before custody.");
                throw new CustodyRequiredException("fixture write unavailable");
            }

            return targetMethod!.Invoke(_inner, args);
        }
    }
}
