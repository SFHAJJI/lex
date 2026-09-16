using System.Reflection;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPdfLayoutEvidenceProducerTests
{
    [TestMethod]
    public async Task ARealRetainedPublisherPdfYieldsOrderedPhysicalGlyphEvidence()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var source = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));

        var result = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create(bytes))
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        Assert.AreSame(source, result.Population!.SourceEligibilityPopulation);
        Assert.HasCount(source.Outcomes.Count, result.Population.Outcomes);
        var outcome = result.Population.Outcomes.Single();
        Assert.AreSame(source.Outcomes.Single(), outcome.SourceEligibility);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.Admitted, outcome.Disposition);
        Assert.IsNull(outcome.GapReason);
        Assert.IsNotEmpty(outcome.Pages);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, outcome.Pages.Count).ToArray(),
            outcome.Pages.Select(static page => page.PhysicalPageNumber).ToArray());
        Assert.IsNotEmpty(outcome.Glyphs);
        Assert.IsTrue(outcome.Glyphs.All(static glyph => glyph.PhysicalPageNumber > 0));
        CollectionAssert.AreEqual(
            Enumerable.Range(0, outcome.Glyphs.Count).ToArray(),
            outcome.Glyphs.Select(static glyph => glyph.GlyphOrdinal).ToArray());
    }

    [TestMethod]
    public async Task UnreadableEligiblePdfIsAnExplicitGapRatherThanInventedText()
    {
        var bytes = "%PDF-1.7 deliberately unreadable\n%%EOF\n"u8.ToArray();
        var source = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));

        var result = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create(bytes))
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.PdfUnreadable, outcome.GapReason);
        Assert.IsEmpty(outcome.Pages);
        Assert.IsEmpty(outcome.Glyphs);
    }

    [TestMethod]
    public async Task MissingRetainedBytesRefuseTheWholePopulation()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var source = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));

        var result = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.CreateUnavailable())
            .RunAsync(source, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.IsNull(result.Population);
        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceProductionRefusal.RetainedBytesUnavailable,
            result.Refusal);
    }

    [TestMethod]
    public async Task NonPdfAndUpstreamGapRemainExplicitAndNeedNoCustodyRead()
    {
        var xml = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync());
        var upstreamGap = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompleteWithDistinctReceiptsForStage3BodyCompositionAsync());
        var store = ReadStore.Create([], failOnRead: true);

        var xmlResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(xml, CancellationToken.None);
        var gapResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(upstreamGap, CancellationToken.None);

        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceDisposition.NotApplicable,
            xmlResult.Population!.Outcomes.Single().Disposition);
        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceGapReason.UpstreamEligibilityGap,
            gapResult.Population!.Outcomes.Single().GapReason);
    }

    [TestMethod]
    public async Task EqualPublisherEvidenceHasStableIdentityAcrossRuns()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var firstSource = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var secondSource = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));

        var first = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create(bytes))
            .RunAsync(firstSource, CancellationToken.None);
        var second = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create(bytes))
            .RunAsync(secondSource, CancellationToken.None);

        Assert.AreEqual(first.Population!.IdentitySha256, second.Population!.IdentitySha256);
        Assert.AreEqual(
            first.Population.Outcomes.Single().SemanticIdentitySha256,
            second.Population.Outcomes.Single().SemanticIdentitySha256);
    }

    [TestMethod]
    public async Task DifferentCanonicalLayoutsHaveDifferentSemanticIdentity()
    {
        var firstBytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var secondBytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "new-pdfa2a-200-body.bin"));
        var firstSource = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(firstBytes));
        var secondSource = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompletePublisherPdfForStage3BodyCompositionAsync(secondBytes));

        Assert.AreEqual(
            firstSource.Outcomes.Single().SemanticIdentitySha256,
            secondSource.Outcomes.Single().SemanticIdentitySha256,
            "transport bytes are provenance, so the upstream semantic member is the same.");

        var first = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create(firstBytes))
            .RunAsync(firstSource, CancellationToken.None);
        var second = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create(secondBytes))
            .RunAsync(secondSource, CancellationToken.None);

        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceDisposition.Admitted,
            first.Population!.Outcomes.Single().Disposition);
        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceDisposition.Admitted,
            second.Population!.Outcomes.Single().Disposition);
        Assert.AreNotEqual(
            first.Population.Outcomes.Single().SemanticIdentitySha256,
            second.Population.Outcomes.Single().SemanticIdentitySha256,
            "canonical physical-page and glyph evidence, not the container digest, distinguishes layouts.");
    }

    [TestMethod]
    public void PublicDoorAcceptsOnlyTheProofCompleteEligibilityPopulation()
    {
        var parameters = typeof(LuxembourgPdfLayoutEvidenceProducer)
            .GetMethod(nameof(LuxembourgPdfLayoutEvidenceProducer.RunAsync))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgPdfProfileEligibilityPopulation), typeof(CancellationToken) },
            parameters);
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
            }
            .Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
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

    private class ReadStore : DispatchProxy
    {
        private byte[] _bytes = [];
        private bool _failOnRead;

        internal static ICustodyStore Create(byte[] bytes, bool failOnRead = false)
        {
            var proxy = Create<ICustodyStore, ReadStore>();
            var store = (ReadStore)(object)proxy;
            store._bytes = bytes;
            store._failOnRead = failOnRead;
            return proxy;
        }

        internal static ICustodyStore CreateUnavailable()
        {
            var proxy = Create<ICustodyStore, ReadStore>();
            ((ReadStore)(object)proxy)._unavailable = true;
            return proxy;
        }

        private bool _unavailable;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICustodyStore.ReadAsync))
            {
                if (_unavailable)
                {
                    throw new CustodyRequiredException("fixture bytes unavailable");
                }
                if (_failOnRead)
                {
                    throw new AssertFailedException("A non-eligible body must not touch custody.");
                }

                return Task.FromResult<ReadOnlyMemory<byte>>(_bytes);
            }

            throw new InvalidOperationException($"Unexpected custody call: {targetMethod?.Name}");
        }
    }
}
