using Lex.V3.Contracts.Custody;
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
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);
        var akn = await CompleteAknEvidenceAsync(luxembourg);

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, fidelity, akn.Inventory, akn.LegalContent,
            out var refusal, out var detail);

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.AreSame(eu, envelope.Europe);
        Assert.AreSame(luxembourg, envelope.Luxembourg);
        Assert.AreSame(formex, envelope.Formex);
        Assert.AreSame(classifications, envelope.FormexAnnexClassifications);
        Assert.AreSame(fidelity, envelope.FidelityPreservation);
        Assert.AreSame(akn.Inventory, envelope.LuxembourgAknArticleInventoryPopulation);
        Assert.AreSame(akn.LegalContent, envelope.LuxembourgAknLegalContentPopulation);
    }

    [TestMethod]
    public async Task LuxembourgAknInventoryMustComeFromTheExactRunPopulation()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var foreignLuxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);
        var akn = await CompleteAknEvidenceAsync(luxembourg);
        var foreignAkn = await CompleteAknEvidenceAsync(foreignLuxembourg);

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, fidelity, akn.Inventory, akn.LegalContent,
            out var refusal, out var detail);

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.AreSame(akn.Inventory, envelope.LuxembourgAknArticleInventoryPopulation);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, fidelity, foreignAkn.Inventory,
            foreignAkn.LegalContent,
            out refusal, out detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.LuxembourgAknArticleInventoryPopulationMismatch, refusal);
        Assert.AreEqual(
            "the AKN article inventory belongs to a different held-body derivation population",
            detail);

        Assert.ThrowsExactly<ArgumentNullException>(() => Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, fidelity, null!, akn.LegalContent,
            out _, out _));
    }

    [TestMethod]
    public async Task LuxembourgAknLegalContentMustComeFromTheExactInventoryPopulation()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);
        var akn = await CompleteAknEvidenceAsync(luxembourg);
        var foreignAkn = await CompleteAknEvidenceAsync(luxembourg);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, fidelity, akn.Inventory,
            foreignAkn.LegalContent, out var refusal, out var detail));
        Assert.AreEqual(
            Stage3EvidenceEnvelopeRefusal.LuxembourgAknLegalContentPopulationMismatch,
            refusal,
            detail);
        Assert.AreEqual(
            "the AKN legal content belongs to a different article inventory population",
            detail);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, classifications, fidelity, akn.Inventory, null!,
            out refusal, out detail));
        Assert.AreEqual("LuxembourgAknLegalContentPopulationMissing", refusal.ToString(), detail);
        Assert.AreEqual("the AKN legal-content population is missing", detail);
    }

    [TestMethod]
    public async Task EitherPartialAdapterResultRefusesTheEnvelope()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);
        var refusedEu = EuQueryExecutionResult.Refused(
            eu.Topology, [],
            new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.CensusFamilyNotProven, "test"));
        var refusedLuxembourg = LuxembourgQueryExecutionResult.Refused(
            luxembourg.Topology, [], [],
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained, null, "test"));
        var akn = await CompleteAknEvidenceAsync(luxembourg);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            refusedEu, luxembourg, formex, classifications, fidelity, akn.Inventory,
            akn.LegalContent, out var euRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, euRefusal);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, refusedLuxembourg, formex, classifications, fidelity, akn.Inventory,
            akn.LegalContent, out var luRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.LuxembourgNotComplete, luRefusal);
    }

    [TestMethod]
    public async Task ADeliveredEuropeResultMissingItsJoinedProductionStillRefuses()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);
        var missingJoinedProduction = EuQueryExecutionResult.Delivered(
            eu.Topology, eu.FamilyOutcomes, eu.ObservedObjectCount, eu.ObservedExpressionCount,
            eu.ReductionExclusions, eu.WatermarkWitnessPlan!, eu.RootBinding!,
            eu.WitnessReconciliation!, eu.WitnessTerminations!, eu.ScopeManifestReceipt!,
            eu.ScopeManifestCanonicalSha256!, eu.DocumentAcquisitionOutcomesByOrdinal!,
            eu.DocumentLadderResultsByOrdinal!, eu.ObservedManifestationTypesByCelex!,
            eu.ObservedExpressionsByCelex!, eu.MintedRowsByOrdinal!, eu.DateAxioms,
            eu.CorpusRecordSetRef!, eu.CorpusRecordSetReceipt!, eu.CorpusRecordSet!,
            eu.CorrigendumTripwires!);

        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, missingJoinedProduction.Completion);
        Assert.IsNull(TryCreate(
            missingJoinedProduction, luxembourg, formex, classifications, fidelity, out var refusal, out _));
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
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, foreignFormex, foreignClassifications, fidelity, out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeFormexRunMismatch, refusal);
        Assert.AreEqual("the Formex reconciliation belongs to a different EU result", detail);
    }

    [TestMethod]
    public async Task FormexReconciliationCannotBeOmitted()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();

        Assert.ThrowsExactly<ArgumentNullException>(() => TryCreate(
            eu, luxembourg, null!, null!, null!, out _, out _));
    }

    [TestMethod]
    public async Task FormexClassificationReconciliationCannotBeOmitted()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        Assert.ThrowsExactly<ArgumentNullException>(() => TryCreate(
            eu, luxembourg, formex, null!, fidelity, out _, out _));
    }

    [TestMethod]
    public async Task FormexClassificationsMustBelongToTheExactFormexReconciliation()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var foreignFormex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var foreignClassifications = CompleteClassifications(foreignFormex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, formex, foreignClassifications, fidelity, out var refusal, out var detail));
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
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, formex, classifications, fidelity, out var refusal, out var detail));
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
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        var envelope = TryCreate(
            eu, luxembourg, formex, classifications, fidelity, out var refusal, out var detail);

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
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, formex, classifications, fidelity, out var refusal, out var detail));
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
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(eu, luxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, formex, classifications, fidelity, out var refusal, out var detail));
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
        Assert.Contains(typeof(Stage3FidelityPreservationReconciliation), parameters);
        Assert.Contains(typeof(LuxembourgAknArticleInventoryPopulation), parameters);
        Assert.Contains(typeof(LuxembourgAknLegalContentPopulation), parameters);
        Assert.IsFalse(parameters.Contains(typeof(IEnumerable<EuImageOnlyAnnexProductionResult>)));
    }

    [TestMethod]
    public async Task FidelityPreservationMustBelongToTheExactEuropeResult()
    {
        var eu = await CompleteEuropeAsync();
        var foreignEurope = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var foreignFidelity = Stage3FidelityPreservationReconciliationTests.Complete(
            foreignEurope, luxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, formex, classifications, foreignFidelity, out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeFidelityPreservationMismatch, refusal);
        Assert.AreEqual("the fidelity preservation belongs to a different EU result", detail);
    }

    [TestMethod]
    public async Task FidelityPreservationCannotBeOmitted()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);

        Assert.ThrowsExactly<ArgumentNullException>(() => TryCreate(
            eu, luxembourg, formex, classifications, null!, out _, out _));
    }

    [TestMethod]
    public async Task FidelityPreservationMustBelongToTheExactLuxembourgResult()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var foreignLuxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var classifications = CompleteClassifications(formex);
        var foreignFidelity = Stage3FidelityPreservationReconciliationTests.Complete(
            eu, foreignLuxembourg);

        Assert.IsNull(TryCreate(
            eu, luxembourg, formex, classifications, foreignFidelity, out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.LuxembourgFidelityPreservationMismatch, refusal);
        Assert.AreEqual("the fidelity preservation belongs to a different Luxembourg result", detail);
    }

    internal static EuFormexAnnexClassificationReconciliation CompleteClassifications(
        EuFormexRunOutcomeReconciliation formex,
        IReadOnlyList<EuBoundAnnexBodyClassification>? classifications = null) =>
        EuFormexAnnexClassificationReconciliation.TryClose(
            formex, classifications ?? [], out var refusal, out var detail)
        ?? throw new AssertFailedException($"Formex annex classification refused: {refusal}: {detail}");

    internal static Stage3EvidenceEnvelope? TryCreate(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation classifications,
        Stage3FidelityPreservationReconciliation fidelity,
        out Stage3EvidenceEnvelopeRefusal refusal,
        out string? detail)
    {
        var akn = CompleteAknEvidenceAsync(luxembourg).GetAwaiter().GetResult();
        return Stage3EvidenceEnvelope.TryCreate(
            europe,
            luxembourg,
            formex,
            classifications,
            fidelity,
            akn.Inventory,
            akn.LegalContent,
            out refusal,
            out detail);
    }

    internal static async Task<(
        LuxembourgAknArticleInventoryPopulation Inventory,
        LuxembourgAknLegalContentPopulation LegalContent)> CompleteAknEvidenceAsync(
            LuxembourgQueryExecutionResult luxembourg,
            ICustodyStore? custody = null)
    {
        custody ??= new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var inventory = await new LuxembourgAknArticleInventoryProducer(custody)
            .RunAsync(luxembourg.HeldBodyDerivationPopulation!, CancellationToken.None);
        var legalContent = await new LuxembourgAknLegalContentProfileProducer(custody)
            .RunAsync(inventory, CancellationToken.None);
        return (inventory, legalContent);
    }

    internal static LuxembourgAknArticleInventoryPopulation CompleteAknInventory(
        LuxembourgQueryExecutionResult luxembourg) =>
        CompleteAknInventoryAsync(luxembourg).GetAwaiter().GetResult();

    internal static Task<LuxembourgAknArticleInventoryPopulation> CompleteAknInventoryAsync(
        LuxembourgQueryExecutionResult luxembourg) =>
        new LuxembourgAknArticleInventoryProducer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(luxembourg.HeldBodyDerivationPopulation!, CancellationToken.None);

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
}
