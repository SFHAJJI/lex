using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;
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
        eu = Stage3EvidenceLineageTests.AddEuropeCorpusRecord(
            eu,
            annexProduction.Disposition!.SourceObject);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu,
            luxembourg,
            formex,
            [annexProduction],
            out var refusal,
            out var detail);

        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.None, refusal, detail);
        Assert.IsNotNull(envelope);
        Assert.AreSame(eu, envelope.Europe);
        Assert.AreSame(luxembourg, envelope.Luxembourg);
        Assert.AreSame(formex, envelope.Formex);
        Assert.HasCount(1, envelope.ImageOnlyEuAnnexes);
        Assert.AreSame(annexProduction.Disposition, envelope.ImageOnlyEuAnnexes[0]);
    }

    [TestMethod]
    public async Task EitherPartialAdapterResultRefusesTheEnvelope()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
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
            refusedEu, luxembourg, formex, [], out var euRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, euRefusal);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, refusedLuxembourg, formex, [], out var luRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.LuxembourgNotComplete, luRefusal);
    }

    [TestMethod]
    public async Task ADeliveredEuropeResultMissingItsJoinedProductionStillRefuses()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
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
            missingJoinedProduction, luxembourg, formex, [], out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeNotComplete, refusal);
    }

    [TestMethod]
    public async Task FormexReconciliationMustBelongToTheExactEuropeResult()
    {
        var eu = await CompleteEuropeAsync();
        var foreignEurope = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var foreignFormex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(foreignEurope);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, foreignFormex, [], out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeFormexRunMismatch, refusal);
        Assert.AreEqual("the Formex reconciliation belongs to a different EU result", detail);
    }

    [TestMethod]
    public async Task FormexReconciliationCannotBeOmitted()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();

        Assert.ThrowsExactly<ArgumentNullException>(() => Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, null!, [], out _, out _));
    }

    [TestMethod]
    public async Task ImageOnlyAnnexSourceMustBelongToTheEuropeCorpus()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var annex = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAsync();

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, [annex], out var refusal, out var detail));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeAnnexOutsideCorpus, refusal);
        Assert.AreEqual(
            ScopeManifestCanonicalWriter.ComputeObjectRefSha256(annex.Disposition!.SourceObject),
            detail);
    }

    [TestMethod]
    public async Task ImageOnlyAnnexSourceCannotMatchTheEuropeCorpusByCanonicalKeyAlone()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var annex = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAsync();
        var source = annex.Disposition!.SourceObject;
        var sameKeyForeignSource = new SourceObjectRef(
            source.Schema,
            source.Authority,
            source.EntityKind,
            "https://example.invalid/resource/cellar/" + source.CanonicalKey,
            source.CanonicalKey,
            source.CanonicalKeySha256,
            source.IdentityProfileRef,
            source.ParentKeyRef);
        eu = Stage3EvidenceLineageTests.AddEuropeCorpusRecord(eu, sameKeyForeignSource);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, [annex], out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeAnnexOutsideCorpus, refusal);
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
        eu = Stage3EvidenceLineageTests.AddEuropeCorpusRecord(eu, gap.Disposition!.SourceObject);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, [admitted], out var outcomeRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.AnnexIsNotTextUnavailable, outcomeRefusal);

        var refused = EuImageOnlyAnnexProductionResult.Refused(
            EuImageOnlyAnnexProductionRefusal.ProfileInvalid,
            "test");
        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, [refused], out var productionRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.AnnexProductionRefused, productionRefusal);

        Assert.IsNull(Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, [gap, sameSourceLocationDifferentEvidence], out var duplicateRefusal, out _));
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.DuplicateAnnex, duplicateRefusal);
    }

    [TestMethod]
    public async Task AnnexesAreCopiedAndSortedByEvidenceIdentity()
    {
        var eu = await CompleteEuropeAsync();
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var first = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAtAsync("III");
        var second = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAtAsync("IV");
        eu = Stage3EvidenceLineageTests.AddEuropeCorpusRecord(eu, first.Disposition!.SourceObject);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(eu);
        var input = new[] { first, second }
            .OrderByDescending(static production => production.Disposition!.IdentitySha256, StringComparer.Ordinal)
            .ToList();

        var envelope = Stage3EvidenceEnvelope.TryCreate(
            eu, luxembourg, formex, input, out var refusal, out var detail);
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
