using Lex.V3.Contracts.Derivation;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class Stage3EvidenceEnvelopeTests
{
    [TestMethod]
    public async Task CompleteAdapterResultsAndImageOnlyGapMintTheEnvelope()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var annexProduction = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAsync();

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu,
            luxembourg,
            [annexProduction],
            out var refusal,
            out var detail);

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.AreSame(eu, envelope.Europe);
        Assert.AreSame(luxembourg, envelope.Luxembourg);
        Assert.HasCount(1, envelope.ImageOnlyEuAnnexes);
        Assert.AreSame(annexProduction.Disposition, envelope.ImageOnlyEuAnnexes[0]);
    }

    [TestMethod]
    public async Task EitherPartialAdapterResultRefusesTheEnvelope()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var refusedEu = EuQueryExecutionResult.Refused(
            eu.Topology,
            [],
            new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.CensusFamilyNotProven, "test"));
        var refusedLuxembourg = LuxembourgQueryExecutionResult.Refused(
            luxembourg.Topology,
            [],
            [],
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained,
                null,
                "test"));

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            refusedEu, luxembourg, [], out var euRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, euRefusal);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, refusedLuxembourg, [], out var luRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.LuxembourgNotComplete, luRefusal);
    }

    [TestMethod]
    public async Task ADeliveredEuropeResultMissingItsJoinedProductionStillRefuses()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var missingJoinedProduction = EuQueryExecutionResult.Delivered(
            eu.Topology,
            eu.FamilyOutcomes,
            eu.ObservedObjectCount,
            eu.ObservedExpressionCount,
            eu.ReductionExclusions,
            eu.WatermarkWitnessPlan!,
            eu.RootBinding!,
            eu.WitnessReconciliation!,
            eu.WitnessTerminations!,
            eu.ScopeManifestReceipt!,
            eu.ScopeManifestCanonicalSha256!,
            eu.DocumentAcquisitionOutcomesByOrdinal!,
            eu.DocumentLadderResultsByOrdinal!,
            eu.ObservedManifestationTypesByCelex!,
            eu.ObservedExpressionsByCelex!,
            eu.MintedRowsByOrdinal!,
            eu.DateAxioms,
            eu.CorpusRecordSetRef!,
            eu.CorpusRecordSet!,
            eu.CorrigendumTripwires!);

        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, missingJoinedProduction.Completion);
        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            missingJoinedProduction, luxembourg, [], out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, refusal);
    }

    [TestMethod]
    public async Task AnnexEntriesMustBeImageOnlyGapsAndUniqueBySourceLocation()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var gap = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAsync();
        var sameSourceLocationDifferentEvidence =
            await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeFromTwoPagePdfAsync();
        var admitted = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAsync(
            EuAnnexBodyDispositionOutcome.Admitted);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, [admitted], out var outcomeRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.AnnexIsNotTextUnavailable, outcomeRefusal);

        var refused = EuImageOnlyAnnexProductionResult.Refused(
            EuImageOnlyAnnexProductionRefusal.ProfileInvalid,
            "test");
        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, [refused], out var productionRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.AnnexProductionRefused, productionRefusal);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, [gap, sameSourceLocationDifferentEvidence], out var duplicateRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.DuplicateAnnex, duplicateRefusal);
    }

    [TestMethod]
    public async Task AnnexesAreCopiedAndSortedByEvidenceIdentity()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var first = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAtAsync("III");
        var second = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAtAsync("IV");
        var input = new[] { first, second }
            .OrderByDescending(static production => production.Disposition!.IdentitySha256, StringComparer.Ordinal)
            .ToList();

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, input, out var refusal, out var detail);
        input.Clear();

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.HasCount(2, envelope.ImageOnlyEuAnnexes);
        Assert.IsFalse(
            envelope.ImageOnlyEuAnnexes is ICollection<EuAnnexBodyDisposition> { IsReadOnly: false });
        Assert.IsTrue(StringComparer.Ordinal.Compare(
            envelope.ImageOnlyEuAnnexes[0].IdentitySha256,
            envelope.ImageOnlyEuAnnexes[1].IdentitySha256) < 0);
    }

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
}
