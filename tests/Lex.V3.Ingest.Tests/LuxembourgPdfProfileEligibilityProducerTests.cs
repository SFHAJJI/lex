using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPdfProfileEligibilityProducerTests
{
    [TestMethod]
    public async Task ExactGazetteEvidenceClassifiesTheSelectedPdfWithoutInterpretingIt()
    {
        var composition = await CompleteCompositionAsync();

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
        var first = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync());
        var second = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync());

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

    private static async Task<Stage3BodyComposition> CompleteCompositionAsync()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var europe = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }
            .Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var luxembourg = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
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
