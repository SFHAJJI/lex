using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPdfProfileEligibilityProducerTests
{
    [TestMethod]
    public async Task ExactGazetteEvidenceClassifiesTheSelectedPdfWithoutInterpretingIt()
    {
        var composition = await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync());

        var population = LuxembourgPdfProfileEligibilityProducer.Produce(composition);

        Assert.AreSame(composition, population.SourceComposition);
        Assert.HasCount(composition.LuxembourgDerivationPopulation.Inputs.Count, population.Outcomes);
        var outcome = population.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfProfileEligibilityDisposition.GazettePdfEligible, outcome.Disposition);
        Assert.IsNull(outcome.GapReason);
        Assert.IsNotNull(outcome.GazetteEvidence);
        Assert.AreEqual(outcome.Input.SelectedWemiCandidate.ExpressionIri, outcome.PublisherExpressionIri);
        Assert.AreEqual(outcome.Input.SelectedWemiCandidate.ManifestationIri, outcome.PublisherManifestationIri);
        Assert.AreEqual(outcome.Input.SelectedWemiCandidate.ItemIri, outcome.PublisherItemIri);
        Assert.AreSame(outcome.Input.Receipt, outcome.TransportReceipt);
        Assert.AreEqual(
            outcome.Input.Receipt,
            outcome.GazetteEvidence.RetainedTransportBytes,
            "eligibility is bound to the exact retained receipt already admitted by the Gazette producer");
    }

    [TestMethod]
    public async Task EqualPublisherFactsHaveStableEligibilityIdentityAcrossExecutions()
    {
        var first = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync()));
        var second = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync()));

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(
            first.Outcomes.Single().SemanticIdentitySha256,
            second.Outcomes.Single().SemanticIdentitySha256);
    }

    [TestMethod]
    public void ThePublicDoorAcceptsOnlyTheProofBoundComposition()
    {
        var parameters = typeof(LuxembourgPdfProfileEligibilityProducer)
            .GetMethod(nameof(LuxembourgPdfProfileEligibilityProducer.Produce))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(new[] { typeof(Stage3BodyComposition) }, parameters);
    }

    [TestMethod]
    public async Task AcceptedPublicCompositionsReachEveryRetainedDispositionAndGap()
    {
        var xml = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync()));
        var publisherPdf = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests.CompletePublisherPdfForStage3BodyCompositionAsync()));
        var receiptMismatch = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteWithDistinctReceiptsForStage3BodyCompositionAsync()));

        AssertOutcome(xml.Outcomes.Single(), LuxembourgPdfProfileEligibilityDisposition.NotPdf);
        AssertOutcome(
            publisherPdf.Outcomes.Single(),
            LuxembourgPdfProfileEligibilityDisposition.PublisherPdfEligible);
        AssertOutcome(
            receiptMismatch.Outcomes.Single(),
            LuxembourgPdfProfileEligibilityDisposition.TypedGap,
            LuxembourgPdfProfileEligibilityGapReason.GazetteReceiptMismatch);
    }

    [TestMethod]
    public async Task ASelectedPdfWithoutRetainedGazetteBytesUsesThePublisherPdfFamily()
    {
        var composition = await CompleteCompositionAsync(
            await LuxembourgGazetteAcquisitionTests
                .CompleteWithUnretainedSelectedGazetteForStage3BodyCompositionAsync());
        var matchingGazette = composition.Luxembourg
            .SelectMany(static value => value.GazetteBodies.Bodies)
            .Single(body => body.Candidate.WemiCandidate.ItemIri ==
                composition.LuxembourgDerivationPopulation.Inputs.Single().SelectedWemiCandidate.ItemIri);
        Assert.AreEqual(LuxembourgGazetteBodyOutcome.TypedGap, matchingGazette.Outcome);
        Assert.IsNull(matchingGazette.RetainedTransportBytes);

        var outcome = LuxembourgPdfProfileEligibilityProducer.Produce(composition).Outcomes.Single();

        AssertOutcome(outcome, LuxembourgPdfProfileEligibilityDisposition.PublisherPdfEligible);
    }

    [TestMethod]
    public async Task EveryAcceptedHeldInputHasExactlyOneOutcomeInProofBoundSourceOrder()
    {
        var compositions = new[]
        {
            await CompleteCompositionAsync(
                await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync()),
            await CompleteCompositionAsync(
                await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync()),
            await CompleteCompositionAsync(
                await LuxembourgGazetteAcquisitionTests.CompletePublisherPdfForStage3BodyCompositionAsync()),
            await CompleteCompositionAsync(
                await LuxembourgGazetteAcquisitionTests.CompleteWithDistinctReceiptsForStage3BodyCompositionAsync()),
            await CompleteCompositionAsync(
                await LuxembourgGazetteAcquisitionTests
                    .CompleteWithUnretainedSelectedGazetteForStage3BodyCompositionAsync()),
        };

        foreach (var composition in compositions)
        {
            var population = LuxembourgPdfProfileEligibilityProducer.Produce(composition);
            Assert.HasCount(composition.LuxembourgDerivationPopulation.Inputs.Count, population.Outcomes);
            for (var index = 0; index < population.Outcomes.Count; index++)
            {
                Assert.AreSame(
                    composition.LuxembourgDerivationPopulation.Inputs[index],
                    population.Outcomes[index].Input);
            }
        }
    }

    private static void AssertOutcome(
        LuxembourgPdfProfileEligibilityOutcome outcome,
        LuxembourgPdfProfileEligibilityDisposition disposition,
        LuxembourgPdfProfileEligibilityGapReason? gapReason = null)
    {
        Assert.AreEqual(disposition, outcome.Disposition);
        Assert.AreEqual(gapReason, outcome.GapReason);
        Assert.AreEqual(
            disposition == LuxembourgPdfProfileEligibilityDisposition.GazettePdfEligible,
            outcome.GazetteEvidence is not null);
    }

    private static async Task<Stage3BodyComposition> CompleteCompositionAsync(
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
        var envelope = Stage3EvidenceEnvelope.TryCreate(
            europe,
            luxembourg,
            formex,
            classifications,
            fidelity,
            Stage3EvidenceEnvelopeTests.CompleteAknInventory(luxembourg),
            out var envelopeRefusal,
            out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");
        var composition = Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail);
        Assert.IsNotNull(composition, $"{refusal}: {detail}");
        return composition;
    }
}
