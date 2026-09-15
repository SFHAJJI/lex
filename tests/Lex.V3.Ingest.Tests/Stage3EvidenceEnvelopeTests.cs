using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class Stage3EvidenceEnvelopeTests
{
    [TestMethod]
    public async Task CompleteAdapterResultsAndFormexClassificationsMintTheEnvelope()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, out var refusal, out var detail);

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.AreSame(eu, envelope.Europe);
        Assert.AreSame(luxembourg, envelope.Luxembourg);
        Assert.AreSame(formex, envelope.Formex);
        Assert.AreSame(classifications, envelope.FormexAnnexClassifications);
    }

    [TestMethod]
    public async Task EitherPartialAdapterResultRefusesTheEnvelope()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var refusedEu = EuQueryExecutionResult.Refused(
            eu.Topology, [],
            new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.CensusFamilyNotProven, "test"));
        var refusedLuxembourg = LuxembourgQueryExecutionResult.Refused(
            luxembourg.Topology, [], [],
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained, null, "test"));

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            refusedEu, luxembourg, formex, classifications, out var euRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, euRefusal);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, refusedLuxembourg, formex, classifications, out var luRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.LuxembourgNotComplete, luRefusal);
    }

    [TestMethod]
    public async Task ADeliveredEuropeResultMissingItsJoinedProductionStillRefuses()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var missingJoinedProduction = EuQueryExecutionResult.Delivered(
            eu.Topology, eu.FamilyOutcomes, eu.ObservedObjectCount, eu.ObservedExpressionCount,
            eu.ReductionExclusions, eu.WatermarkWitnessPlan!, eu.RootBinding!,
            eu.WitnessReconciliation!, eu.WitnessTerminations!, eu.ScopeManifestReceipt!,
            eu.ScopeManifestCanonicalSha256!, eu.DocumentAcquisitionOutcomesByOrdinal!,
            eu.DocumentLadderResultsByOrdinal!, eu.ObservedManifestationTypesByCelex!,
            eu.ObservedExpressionsByCelex!, eu.MintedRowsByOrdinal!, eu.DateAxioms,
            eu.CorpusRecordSetRef!, eu.CorpusRecordSet!, eu.CorrigendumTripwires!);

        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, missingJoinedProduction.Completion);
        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            missingJoinedProduction, luxembourg, formex, classifications, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, refusal);
    }

    [TestMethod]
    public async Task FormexReconciliationMustBelongToTheExactEuropeResult()
    {
        var eu = await CompleteEuropeAsync();
        var foreignEurope = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var foreignFormex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(foreignEurope);
        var foreignClassifications = CompleteClassifications(foreignFormex);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, foreignFormex, foreignClassifications, out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeFormexRunMismatch, refusal);
        Assert.AreEqual("the Formex reconciliation belongs to a different EU result", detail);
    }

    [TestMethod]
    public async Task FormexReconciliationCannotBeOmitted()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();

        Assert.ThrowsExactly<ArgumentNullException>(() => Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, null!, null!, out _, out _));
    }

    [TestMethod]
    public async Task FormexClassificationReconciliationCannotBeOmitted()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);

        Assert.ThrowsExactly<ArgumentNullException>(() => Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, null!, out _, out _));
    }

    [TestMethod]
    public async Task FormexClassificationsMustBelongToTheExactFormexReconciliation()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var foreignFormex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var foreignClassifications = CompleteClassifications(foreignFormex);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, foreignClassifications, out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeFormexClassificationMismatch, refusal);
        Assert.AreEqual("the Formex annex classifications belong to a different Formex reconciliation", detail);
    }

    [TestMethod]
    public async Task EveryClassificationSourceMustBelongToTheEuropeCorpus()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var eu = acquired.Run;
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            eu, [acquired.Outcome]);
        var classifications = CompleteClassifications(formex, [acquired.Classification]);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, out var refusal, out var detail));
        Assert.AreEqual(
            Stage3EvidenceEnvelopeRefusal.EuropeFormexClassificationSourceOutsideCorpus,
            refusal);
        Assert.AreEqual(
            ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                acquired.Classification.Binding.FormexSource.ObjectRef),
            detail);
    }

    [TestMethod]
    public async Task AcquiredClassificationsWhoseSourcesBelongToEuropeMintTheEnvelope()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var eu = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }
            .Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            eu, [acquired.Outcome]);
        var classifications = CompleteClassifications(formex, [acquired.Classification]);

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, out var refusal, out var detail);

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.AreSame(classifications, envelope.FormexAnnexClassifications);
    }

    [TestMethod]
    public async Task XhtmlClassificationSourceMustBelongToTheEuropeCorpus()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var eu = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }
            .Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            eu, [acquired.Outcome]);
        var classifications = CompleteClassifications(formex, [acquired.Classification]);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, out var refusal, out var detail));
        Assert.AreEqual(
            Stage3EvidenceEnvelopeRefusal.EuropeFormexClassificationSourceOutsideCorpus,
            refusal);
        Assert.AreEqual(
            ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                acquired.Classification.Binding.XhtmlSource.ObjectRef),
            detail);
    }

    [TestMethod]
    public async Task PdfClassificationSourceMustBelongToTheEuropeCorpus()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var eu = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
            }
            .Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            eu, [acquired.Outcome]);
        var classifications = CompleteClassifications(formex, [acquired.Classification]);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, out var refusal, out var detail));
        Assert.AreEqual(
            Stage3EvidenceEnvelopeRefusal.EuropeFormexClassificationSourceOutsideCorpus,
            refusal);
        Assert.AreEqual(
            ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                acquired.Classification.Binding.PdfSource.ObjectRef),
            detail);
    }

    [TestMethod]
    public void PublicDoorAcceptsNoCallerSelectedAnnexProductionList()
    {
        var parameters = typeof(Stage3EvidenceEnvelope).GetMethod(nameof(Stage3EvidenceEnvelope.TryCreate))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(EuFormexAnnexClassificationReconciliation), parameters);
        Assert.IsFalse(parameters.Contains(typeof(IEnumerable<EuImageOnlyAnnexProductionResult>)));
    }

    internal static EuFormexAnnexClassificationReconciliation CompleteClassifications(
        EuFormexRunOutcomeReconciliation formex,
        IReadOnlyList<EuBoundAnnexBodyClassification>? classifications = null) =>
        EuFormexAnnexClassificationReconciliation.TryClose(
            formex, classifications ?? [], out var refusal, out var detail)
        ?? throw new AssertFailedException($"Formex annex classification refused: {refusal}: {detail}");

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
}
