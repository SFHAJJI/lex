using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class Stage3FidelityPreservationReconciliationTests
{
    [TestMethod]
    public async Task AnAcceptedEuropeRunRetainsItsExactGuardedFidelityCarriersAndNamesEveryOpenObligation()
    {
        var europe = await CompleteEuropeAsync();
        var luxembourg = await CompleteLuxembourgAsync();

        var reconciliation = Stage3FidelityPreservationReconciliation.TryCreate(
            europe, luxembourg, out var refusal, out var detail);

        Assert.AreEqual(Stage3FidelityPreservationReconciliationRefusal.None, refusal, detail);
        Assert.IsNotNull(reconciliation);
        Assert.AreSame(europe, reconciliation.Europe);
        Assert.AreSame(luxembourg, reconciliation.Luxembourg);
        Assert.AreSame(europe.LocatedAmendmentProduction, reconciliation.LocatedAmendments);
        Assert.AreSame(europe.CorrigendumTripwires, reconciliation.CorrigendumTripwires);
        Assert.AreSame(luxembourg.GazetteBodySetsByOrdinal, reconciliation.LuxembourgGazetteBodies);
        Assert.AreSame(luxembourg.PopulationLedger, reconciliation.LuxembourgPopulation);
        CollectionAssert.AreEqual(
            new[]
            {
                Stage3FidelityPreservationObligation.MarkerOnlyRuleUnsupported,
                Stage3FidelityPreservationObligation.FootnotePreservationUnproven,
                Stage3FidelityPreservationObligation.CitationPreservationUnproven,
            },
            reconciliation.UnresolvedObligations.ToArray());
    }

    [TestMethod]
    public async Task ARefusedEuropeRunCannotMintFidelityPreservation()
    {
        var complete = await CompleteEuropeAsync();
        var luxembourg = await CompleteLuxembourgAsync();
        var refused = EuQueryExecutionResult.Refused(
            complete.Topology,
            [],
            new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.CensusFamilyNotProven, "test"));

        Assert.IsNull(Stage3FidelityPreservationReconciliation.TryCreate(
            refused, luxembourg, out var refusal, out var detail));
        Assert.AreEqual(Stage3FidelityPreservationReconciliationRefusal.EuropeNotComplete, refusal);
        Assert.AreEqual("CensusFamilyNotProven", detail);
    }

    [TestMethod]
    public async Task ADeliveredRunMissingLocatedAmendmentsNamesTheUnprovedCarrier()
    {
        var complete = await CompleteEuropeAsync();
        var luxembourg = await CompleteLuxembourgAsync();
        var missing = EuQueryExecutionResult.Delivered(
            complete.Topology, complete.FamilyOutcomes, complete.ObservedObjectCount,
            complete.ObservedExpressionCount, complete.ReductionExclusions,
            complete.WatermarkWitnessPlan!, complete.RootBinding!, complete.WitnessReconciliation!,
            complete.WitnessTerminations!, complete.ScopeManifestReceipt!,
            complete.ScopeManifestCanonicalSha256!, complete.DocumentAcquisitionOutcomesByOrdinal!,
            complete.DocumentLadderResultsByOrdinal!, complete.ObservedManifestationTypesByCelex!,
            complete.ObservedExpressionsByCelex!, complete.MintedRowsByOrdinal!, complete.DateAxioms,
            complete.CorpusRecordSetRef!, complete.CorpusRecordSet!, complete.CorrigendumTripwires!);

        Assert.IsNull(Stage3FidelityPreservationReconciliation.TryCreate(
            missing, luxembourg, out var refusal, out var detail));
        Assert.AreEqual(Stage3FidelityPreservationReconciliationRefusal.EuropeNotComplete, refusal);
        Assert.AreEqual("located amendment production absent", detail);
    }

    [TestMethod]
    public void AEuropeRunCannotBeOmitted()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            Stage3FidelityPreservationReconciliation.TryCreate(null!, null!, out _, out _));
    }

    [TestMethod]
    public async Task ARefusedLuxembourgRunCannotMintFidelityPreservation()
    {
        var europe = await CompleteEuropeAsync();
        var complete = await CompleteLuxembourgAsync();
        var refused = LuxembourgQueryExecutionResult.Refused(
            complete.Topology,
            [],
            [],
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained, null, "test"));

        Assert.IsNull(Stage3FidelityPreservationReconciliation.TryCreate(
            europe, refused, out var refusal, out var detail));
        Assert.AreEqual(Stage3FidelityPreservationReconciliationRefusal.LuxembourgNotComplete, refusal);
        Assert.AreEqual("ScopeManifestNotRetained", detail);
    }

    [TestMethod]
    public async Task ALuxembourgRunCannotBeOmitted()
    {
        var europe = await CompleteEuropeAsync();

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            Stage3FidelityPreservationReconciliation.TryCreate(europe, null!, out _, out _));
    }

    internal static Stage3FidelityPreservationReconciliation Complete(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg) =>
        Stage3FidelityPreservationReconciliation.TryCreate(
            europe, luxembourg, out var refusal, out var detail)
        ?? throw new AssertFailedException($"Fidelity preservation refused: {refusal}: {detail}");

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));

    private static Task<LuxembourgQueryExecutionResult> CompleteLuxembourgAsync() =>
        LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
}
