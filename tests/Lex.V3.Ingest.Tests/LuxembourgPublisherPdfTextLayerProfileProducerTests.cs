using System.Security.Cryptography;
using System.Reflection;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPublisherPdfTextLayerProfileProducerTests
{
    [TestMethod]
    public async Task ExactConsolidatedPdfProducesCanonicalVisiblePageText()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var (layout, store) = await LayoutAsync(bytes);

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPublisherPdfTextLayerDisposition.Admitted, outcome.Disposition);
        Assert.AreSame(layout.Outcomes.Single(), outcome.SourceLayoutEvidence);
        Assert.AreEqual(1, outcome.PageCount);
        Assert.AreEqual(1_462, outcome.GlyphFragmentCount);
        Assert.AreEqual(0, outcome.ImageCount);
        Assert.IsGreaterThan(1_400, outcome.CharacterCount);
        Assert.IsNotNull(outcome.TextArtifactReceipt);
        Assert.AreEqual(
            "c167272b4d670b2b47b0586e4c688e480fb49ceffcbe0dbcbffc11df71f9bfe1",
            outcome.SemanticIdentitySha256);

        var document = await LuxembourgPublisherPdfTextLayerArtifactReader.ReadAsync(
            outcome, store, CancellationToken.None);
        Assert.HasCount(1, document.Pages);
        var page = document.Pages.Single();
        Assert.AreEqual(1, page.PhysicalPageNumber);
        Assert.AreEqual(0, page.ImageCount);
        Assert.HasCount(1_462, page.OrderedGlyphTextFragments);
        var exactGlyphSequence = string.Concat(page.OrderedGlyphTextFragments);
        StringAssert.Contains(exactGlyphSequence,
            "Art. 1er.Par dérogation à l’article L. 551-2 paragraphe 3 du Code du travail");
        StringAssert.Contains(exactGlyphSequence,
            "Art. 3.Notre ministre ayant le Travail, l’Emploi");
        Assert.AreEqual(outcome.CharacterCount, exactGlyphSequence.Length);
    }

    [TestMethod]
    public async Task InvisiblePublisherTextIsAnExplicitAmbiguityGap()
    {
        var bytes = Fixture("lu-pdf-invisible-1987-12-23-n5.bin", 237_720,
            "9f34cd405f1f0f9b4349fa967b2271b257d4982fbf90351f4b6261f56633c111");
        var (layout, store) = await LayoutAsync(bytes);

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPublisherPdfTextLayerDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerGapReason.InvisibleTextLayer,
            outcome.GapReason);
        Assert.IsNull(outcome.TextArtifactReceipt);
    }

    [TestMethod]
    public async Task ImageCountsRemainExplicitBesideOrderedGlyphFragments()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var (layout, store) = await LayoutAsync(bytes);

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPublisherPdfTextLayerDisposition.Admitted, outcome.Disposition);
        Assert.AreEqual(9, outcome.ImageCount);
        var document = await LuxembourgPublisherPdfTextLayerArtifactReader.ReadAsync(
            outcome, store, CancellationToken.None);
        Assert.AreEqual(9, document.Pages.Sum(static page => page.ImageCount));
        CollectionAssert.AreEqual(
            new[] { 2_889, 2_605, 2_692, 2_776, 3_421, 2_756, 2_549, 2_764 },
            document.Pages.Select(static page => page.OrderedGlyphTextFragments.Count).ToArray(),
            "Every glyph fragment must remain attributed to its exact physical page.");
        Assert.AreEqual(
            outcome.GlyphFragmentCount,
            document.Pages.Sum(static page => page.OrderedGlyphTextFragments.Count));
    }

    [TestMethod]
    public async Task MixedPopulationPreservesOrderAndEachExactSourceMember()
    {
        var clean = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var invisible = Fixture("lu-pdf-invisible-1987-12-23-n5.bin", 237_720,
            "9f34cd405f1f0f9b4349fa967b2271b257d4982fbf90351f4b6261f56633c111");
        var eligibility = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompleteTwoPublisherPdfsForStage3BodyCompositionAsync(clean, invisible));
        var store = await StoreAsync(clean, invisible);
        var layout = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(eligibility, CancellationToken.None);
        Assert.IsTrue(layout.Produced, $"{layout.Refusal}: {layout.Detail}");

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout.Population!, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var layoutPopulation = layout.Population!;
        var textPopulation = result.Population!;
        Assert.HasCount(2, textPopulation.Outcomes);
        CollectionAssert.AreEqual(
            layoutPopulation.Outcomes.Select(static value => value.SemanticIdentitySha256).ToArray(),
            textPopulation.Outcomes.Select(
                static value => value.SourceLayoutEvidence.SemanticIdentitySha256).ToArray());
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerDisposition.Admitted,
            textPopulation.Outcomes.Single(value =>
                value.SourceLayoutEvidence.TransportReceipt.Reference.ContentSha256
                    == CustodyDigest.Of(clean)).Disposition);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerGapReason.InvisibleTextLayer,
            textPopulation.Outcomes.Single(value =>
                value.SourceLayoutEvidence.TransportReceipt.Reference.ContentSha256
                    == CustodyDigest.Of(invisible)).GapReason);
    }

    [TestMethod]
    public async Task EqualPdfBytesForDistinctMembersRemainBoundToEachExactSource()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var eligibility = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompleteTwoPublisherPdfsForStage3BodyCompositionAsync(bytes, bytes));
        var store = await StoreAsync(bytes);
        var layout = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(eligibility, CancellationToken.None);
        Assert.IsTrue(layout.Produced, $"{layout.Refusal}: {layout.Detail}");

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout.Population!, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcomes = result.Population!.Outcomes;
        Assert.HasCount(2, outcomes);
        Assert.IsTrue(outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgPublisherPdfTextLayerDisposition.Admitted));
        Assert.AreNotEqual(
            outcomes[0].SourceLayoutEvidence.SemanticIdentitySha256,
            outcomes[1].SourceLayoutEvidence.SemanticIdentitySha256);
        Assert.AreNotEqual(
            outcomes[0].TextArtifactReceipt!.Reference.ContentSha256,
            outcomes[1].TextArtifactReceipt!.Reference.ContentSha256,
            "The canonical text artifact must bind the exact source member, not only PDF bytes.");
        _ = await LuxembourgPublisherPdfTextLayerArtifactReader.ReadAsync(
            outcomes[0], store, CancellationToken.None);
        _ = await LuxembourgPublisherPdfTextLayerArtifactReader.ReadAsync(
            outcomes[1], store, CancellationToken.None);
    }

    [TestMethod]
    public async Task GazetteAndUpstreamGapRemainExplicitWithoutTextArtifacts()
    {
        var gazetteBytes = LuxembourgGazetteAcquisitionTests.GazetteSelectedPdfBytes();
        var gazetteEligibility = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync());
        Assert.AreEqual(
            gazetteEligibility.Outcomes.Single().TransportReceipt.Reference.ByteLength,
            gazetteBytes.Length,
            "The exposed acquisition fixture must be the exact retained Gazette bytes.");
        var gazetteStore = ReadRoutingStore.Wrap(
            await StoreAsync(gazetteBytes),
            gazetteEligibility.Outcomes.Single().TransportReceipt.Reference,
            gazetteBytes);
        var gazetteLayout = await new LuxembourgPdfLayoutEvidenceProducer(gazetteStore)
            .RunAsync(gazetteEligibility, CancellationToken.None);
        Assert.IsTrue(gazetteLayout.Produced, $"{gazetteLayout.Refusal}: {gazetteLayout.Detail}");

        var gazette = await new LuxembourgPublisherPdfTextLayerProfileProducer(gazetteStore)
            .RunAsync(gazetteLayout.Population!, CancellationToken.None);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerDisposition.NotApplicable,
            gazette.Population!.Outcomes.Single().Disposition);
        Assert.IsNull(gazette.Population.Outcomes.Single().TextArtifactReceipt);

        var gapEligibility = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompleteWithDistinctReceiptsForStage3BodyCompositionAsync());
        var gapLayout = await new LuxembourgPdfLayoutEvidenceProducer(
                new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore())
            .RunAsync(gapEligibility, CancellationToken.None);
        Assert.IsTrue(gapLayout.Produced, $"{gapLayout.Refusal}: {gapLayout.Detail}");
        var gap = await new LuxembourgPublisherPdfTextLayerProfileProducer(
                new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore())
            .RunAsync(gapLayout.Population!, CancellationToken.None);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerGapReason.UpstreamLayoutGap,
            gap.Population!.Outcomes.Single().GapReason);
    }

    [TestMethod]
    public async Task MissingLayoutArtifactRefusesTheWholePopulation()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var (layout, _) = await LayoutAsync(bytes);

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(
                new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore())
            .RunAsync(layout, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerProductionRefusal.LayoutEvidenceUnavailable,
            result.Refusal);
    }

    [TestMethod]
    public async Task EqualEvidenceProducesStableIdentityAndContentAddress()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var first = await LayoutAsync(bytes);
        var second = await LayoutAsync(bytes);

        var firstResult = await new LuxembourgPublisherPdfTextLayerProfileProducer(first.Store)
            .RunAsync(first.Population, CancellationToken.None);
        var secondResult = await new LuxembourgPublisherPdfTextLayerProfileProducer(second.Store)
            .RunAsync(second.Population, CancellationToken.None);

        Assert.AreEqual(firstResult.Population!.IdentitySha256, secondResult.Population!.IdentitySha256);
        Assert.AreEqual(
            firstResult.Population.Outcomes.Single().TextArtifactReceipt!.Reference.ContentSha256,
            secondResult.Population.Outcomes.Single().TextArtifactReceipt!.Reference.ContentSha256);
    }

    [TestMethod]
    public async Task ArtifactCustodyFailureRefusesTheWholePopulation()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var (layout, store) = await LayoutAsync(bytes);
        var refusing = CreateRefusingStore.Wrap(store);

        var result = await new LuxembourgPublisherPdfTextLayerProfileProducer(refusing)
            .RunAsync(layout, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.AreEqual(
            LuxembourgPublisherPdfTextLayerProductionRefusal.TextArtifactCustodyUnavailable,
            result.Refusal);
    }

    [TestMethod]
    public void ArtifactCeilingStopsOnTheCrossingGlyphWithoutGrowingTheBuffer()
    {
        const string ruleProfileSha256 =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string sourceIdentitySha256 =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        int prefixLength;
        using (var probe = new LuxembourgPublisherPdfTextLayerArtifactCodec.Writer(
                   ruleProfileSha256, sourceIdentitySha256, 1, long.MaxValue))
        {
            probe.BeginPage(1, imageCount: 0, glyphFragmentCount: 1);
            prefixLength = probe.ToMemory().Length;
        }

        using var bounded = new LuxembourgPublisherPdfTextLayerArtifactCodec.Writer(
            ruleProfileSha256, sourceIdentitySha256, 1, prefixLength);
        bounded.BeginPage(1, imageCount: 0, glyphFragmentCount: 1);
        Assert.AreEqual(prefixLength, bounded.ToMemory().Length);

        Assert.ThrowsExactly<TextLayerArtifactTooLargeException>(() =>
            bounded.WriteTextFragment("é"));
        Assert.AreEqual(prefixLength, bounded.ToMemory().Length,
            "The rejected glyph fragment must not grow the canonical artifact.");
    }

    [TestMethod]
    public void PublicDoorAcceptsOnlyTheProofCompleteLayoutPopulation()
    {
        var parameters = typeof(LuxembourgPublisherPdfTextLayerProfileProducer)
            .GetMethod(nameof(LuxembourgPublisherPdfTextLayerProfileProducer.RunAsync))!
            .GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgPdfLayoutEvidencePopulation), typeof(CancellationToken) },
            parameters);
    }

    private static async Task<(LuxembourgPdfLayoutEvidencePopulation Population, ICustodyStore Store)>
        LayoutAsync(byte[] bytes)
    {
        var eligibility = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = await StoreAsync(bytes);
        var layout = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(eligibility, CancellationToken.None);
        Assert.IsTrue(layout.Produced, $"{layout.Refusal}: {layout.Detail}");
        return (layout.Population!, store);
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
        var europe = acquired.Run;
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

    private class CreateRefusingStore : DispatchProxy
    {
        private ICustodyStore _inner = null!;

        internal static ICustodyStore Wrap(ICustodyStore inner)
        {
            var proxy = Create<ICustodyStore, CreateRefusingStore>();
            ((CreateRefusingStore)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICustodyStore.CreateAsync))
            {
                throw new CustodyRequiredException("fixture write unavailable");
            }

            return targetMethod!.Invoke(_inner, args);
        }
    }

    private class ReadRoutingStore : DispatchProxy
    {
        private ICustodyStore _inner = null!;
        private DurableBlobRef _reference = null!;
        private byte[] _bytes = null!;

        internal static ICustodyStore Wrap(
            ICustodyStore inner,
            DurableBlobRef reference,
            byte[] bytes)
        {
            var proxy = Create<ICustodyStore, ReadRoutingStore>();
            var state = (ReadRoutingStore)(object)proxy;
            state._inner = inner;
            state._reference = reference;
            state._bytes = bytes;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICustodyStore.ReadAsync)
                && args?[0] is DurableBlobRef reference
                && Equals(reference, _reference))
            {
                return Task.FromResult<ReadOnlyMemory<byte>>(_bytes);
            }

            return targetMethod!.Invoke(_inner, args);
        }
    }

}
