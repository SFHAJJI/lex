using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>Why accepted Stage 3 producer outputs could not form the shared evidence boundary.</summary>
public enum Stage3EvidenceEnvelopeRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("europe_not_complete")]
    EuropeNotComplete = 1,

    [JsonStringEnumMemberName("luxembourg_not_complete")]
    LuxembourgNotComplete = 2,

    [JsonStringEnumMemberName("europe_formex_run_mismatch")]
    EuropeFormexRunMismatch = 3,

    [JsonStringEnumMemberName("europe_formex_classification_mismatch")]
    EuropeFormexClassificationMismatch = 4,

    [JsonStringEnumMemberName("europe_formex_classification_source_outside_corpus")]
    EuropeFormexClassificationSourceOutsideCorpus = 5,

    [JsonStringEnumMemberName("europe_fidelity_preservation_mismatch")]
    EuropeFidelityPreservationMismatch = 6,

    [JsonStringEnumMemberName("luxembourg_fidelity_preservation_mismatch")]
    LuxembourgFidelityPreservationMismatch = 7,

    [JsonStringEnumMemberName("luxembourg_derivation_population_mismatch")]
    LuxembourgDerivationPopulationMismatch = 8,

    [JsonStringEnumMemberName("luxembourg_akn_article_inventory_mismatch")]
    LuxembourgAknArticleInventoryPopulationMismatch = 9,

    [JsonStringEnumMemberName("luxembourg_akn_legal_content_population_missing")]
    LuxembourgAknLegalContentPopulationMissing = 10,

    [JsonStringEnumMemberName("luxembourg_akn_legal_content_population_mismatch")]
    LuxembourgAknLegalContentPopulationMismatch = 11,
}

/// <summary>
/// The construction-only meeting point for accepted EU, Luxembourg and supplementary Formex
/// outputs. It neither serializes nor upgrades them to a release artifact.
/// </summary>
public sealed class Stage3EvidenceEnvelope
{
    private Stage3EvidenceEnvelope(
        EuQueryExecutionResult europe,
        EuLegalNoticeEvidence? europeLegalNoticeEvidence,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation formexAnnexClassifications,
        Stage3FidelityPreservationReconciliation fidelityPreservation,
        LuxembourgAknArticleInventoryPopulation luxembourgAknArticleInventoryPopulation,
        LuxembourgAknLegalContentPopulation luxembourgAknLegalContentPopulation)
    {
        Europe = europe;
        EuropeLegalNoticeEvidence = europeLegalNoticeEvidence;
        Luxembourg = luxembourg;
        Formex = formex;
        FormexAnnexClassifications = formexAnnexClassifications;
        FidelityPreservation = fidelityPreservation;
        LuxembourgAknArticleInventoryPopulation = luxembourgAknArticleInventoryPopulation;
        LuxembourgAknLegalContentPopulation = luxembourgAknLegalContentPopulation;
    }

    public EuQueryExecutionResult Europe { get; }

    /// <summary>
    /// The strict-reopened retained EUR-Lex legal-notice evidence accepted for terminal rights
    /// projection. Null on lower-layer envelopes that cannot satisfy a corpus/6 build.
    /// </summary>
    public EuLegalNoticeEvidence? EuropeLegalNoticeEvidence { get; }

    public LuxembourgQueryExecutionResult Luxembourg { get; }

    /// <summary>
    /// The total supplementary Formex disposition for the exact EU run in this envelope. Formex
    /// does not enter or alter the primary-body acquisition ladder.
    /// </summary>
    public EuFormexRunOutcomeReconciliation Formex { get; }

    /// <summary>
    /// The proof-complete classifications for every acquired Formex inventory member in the exact
    /// reconciliation above. This supplementary carrier preserves its authoritative member order.
    /// </summary>
    public EuFormexAnnexClassificationReconciliation FormexAnnexClassifications { get; }

    /// <summary>
    /// The exact adapter runs' already-guarded fidelity outputs and explicit unproved obligations.
    /// This carrier does not interpret publisher text or claim those obligations complete.
    /// </summary>
    public Stage3FidelityPreservationReconciliation FidelityPreservation { get; }

    /// <summary>
    /// The complete ordered AKN article dispositions produced from this exact Luxembourg run's
    /// held-body derivation population. This carrier adds no marker or text interpretation.
    /// </summary>
    public LuxembourgAknArticleInventoryPopulation LuxembourgAknArticleInventoryPopulation { get; }

    /// <summary>
    /// The complete ordered legal-content dispositions produced from the exact AKN inventory
    /// above. This carrier does not reinterpret tokens, markers or publisher evidence.
    /// </summary>
    public LuxembourgAknLegalContentPopulation LuxembourgAknLegalContentPopulation { get; }

    public static Stage3EvidenceEnvelope? TryCreate(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation formexAnnexClassifications,
        Stage3FidelityPreservationReconciliation fidelityPreservation,
        LuxembourgAknArticleInventoryPopulation luxembourgAknArticleInventoryPopulation,
        LuxembourgAknLegalContentPopulation? luxembourgAknLegalContentPopulation,
        out Stage3EvidenceEnvelopeRefusal refusal,
        out string? detail) => TryCreateCore(
            europe,
            europeLegalNoticeEvidence: null,
            luxembourg,
            formex,
            formexAnnexClassifications,
            fidelityPreservation,
            luxembourgAknArticleInventoryPopulation,
            luxembourgAknLegalContentPopulation,
            out refusal,
            out detail);

    /// <summary>
    /// Terminal-builder door that binds strict-reopened retained EU legal-notice evidence into the
    /// exact Stage 3 composition. A detached matrix reference cannot substitute for this value.
    /// </summary>
    public static Stage3EvidenceEnvelope? TryCreateWithEuropeLegalNoticeEvidence(
        EuQueryExecutionResult europe,
        EuLegalNoticeEvidence europeLegalNoticeEvidence,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation formexAnnexClassifications,
        Stage3FidelityPreservationReconciliation fidelityPreservation,
        LuxembourgAknArticleInventoryPopulation luxembourgAknArticleInventoryPopulation,
        LuxembourgAknLegalContentPopulation? luxembourgAknLegalContentPopulation,
        out Stage3EvidenceEnvelopeRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(europeLegalNoticeEvidence);
        return TryCreateCore(
            europe,
            europeLegalNoticeEvidence,
            luxembourg,
            formex,
            formexAnnexClassifications,
            fidelityPreservation,
            luxembourgAknArticleInventoryPopulation,
            luxembourgAknLegalContentPopulation,
            out refusal,
            out detail);
    }

    private static Stage3EvidenceEnvelope? TryCreateCore(
        EuQueryExecutionResult europe,
        EuLegalNoticeEvidence? europeLegalNoticeEvidence,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation formexAnnexClassifications,
        Stage3FidelityPreservationReconciliation fidelityPreservation,
        LuxembourgAknArticleInventoryPopulation luxembourgAknArticleInventoryPopulation,
        LuxembourgAknLegalContentPopulation? luxembourgAknLegalContentPopulation,
        out Stage3EvidenceEnvelopeRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(europe);
        ArgumentNullException.ThrowIfNull(luxembourg);
        ArgumentNullException.ThrowIfNull(formex);
        ArgumentNullException.ThrowIfNull(formexAnnexClassifications);
        ArgumentNullException.ThrowIfNull(fidelityPreservation);
        ArgumentNullException.ThrowIfNull(luxembourgAknArticleInventoryPopulation);

        refusal = Stage3EvidenceEnvelopeRefusal.None;
        detail = null;
        if (luxembourgAknLegalContentPopulation is null)
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgAknLegalContentPopulationMissing;
            detail = "the AKN legal-content population is missing";
            return null;
        }

        if (!EuropeIsComplete(europe))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.EuropeNotComplete;
            detail = europe.Refusal?.Code.ToString() ?? europe.Completion?.ToString() ?? "completion absent";
            return null;
        }

        if (!LuxembourgIsComplete(luxembourg))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgNotComplete;
            detail = luxembourg.Refusal?.Code.ToString()
                ?? luxembourg.Completion?.ToString()
                ?? "completion absent";
            return null;
        }

        if (!ReferenceEquals(formex.Run, europe))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.EuropeFormexRunMismatch;
            detail = "the Formex reconciliation belongs to a different EU result";
            return null;
        }

        if (!ReferenceEquals(formexAnnexClassifications.Formex, formex))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.EuropeFormexClassificationMismatch;
            detail = "the Formex annex classifications belong to a different Formex reconciliation";
            return null;
        }

        if (!ReferenceEquals(fidelityPreservation.Europe, europe))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.EuropeFidelityPreservationMismatch;
            detail = "the fidelity preservation belongs to a different EU result";
            return null;
        }

        if (!ReferenceEquals(fidelityPreservation.Luxembourg, luxembourg))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgFidelityPreservationMismatch;
            detail = "the fidelity preservation belongs to a different Luxembourg result";
            return null;
        }

        if (!ReferenceEquals(
                luxembourg.HeldBodyDerivationPopulation!.CorpusRecordSet,
                luxembourg.CorpusRecordSet))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgDerivationPopulationMismatch;
            detail = "the held-body derivation population belongs to a different corpus record set";
            return null;
        }

        if (!ReferenceEquals(
                luxembourgAknArticleInventoryPopulation.SourcePopulation,
                luxembourg.HeldBodyDerivationPopulation))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgAknArticleInventoryPopulationMismatch;
            detail = "the AKN article inventory belongs to a different held-body derivation population";
            return null;
        }

        if (!ReferenceEquals(
                luxembourgAknLegalContentPopulation.SourceInventoryPopulation,
                luxembourgAknArticleInventoryPopulation))
        {
            refusal = Stage3EvidenceEnvelopeRefusal.LuxembourgAknLegalContentPopulationMismatch;
            detail = "the AKN legal content belongs to a different article inventory population";
            return null;
        }

        var europeObjectRefs = europe.CorpusRecordSet!.Set.Records
            .Select(static record => record.ObjectRef)
            .ToHashSet();
        var sourceOutsideCorpus = formexAnnexClassifications.Classifications
            .SelectMany(static classification => new[]
            {
                classification.Binding.FormexSource.ObjectRef,
                classification.Binding.XhtmlSource.ObjectRef,
                classification.Binding.PdfSource.ObjectRef,
            })
            .FirstOrDefault(source => !europeObjectRefs.Contains(source));
        if (sourceOutsideCorpus is not null)
        {
            refusal = Stage3EvidenceEnvelopeRefusal.EuropeFormexClassificationSourceOutsideCorpus;
            detail = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(sourceOutsideCorpus);
            return null;
        }

        return new Stage3EvidenceEnvelope(
            europe,
            europeLegalNoticeEvidence,
            luxembourg,
            formex,
            formexAnnexClassifications,
            fidelityPreservation,
            luxembourgAknArticleInventoryPopulation,
            luxembourgAknLegalContentPopulation);
    }

    private static bool EuropeIsComplete(EuQueryExecutionResult result) =>
        result.Refusal is null &&
        result.Completion == EuQueryExecutionCompletion.AllFamiliesProven &&
        result.FamilyOutcomes.All(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven) &&
        result.WatermarkWitnessPlan is not null &&
        result.RootBinding is not null &&
        result.WitnessReconciliation is not null &&
        result.WitnessTerminations is not null &&
        result.ScopeManifestReceipt is not null &&
        result.ScopeManifestCanonicalSha256 is not null &&
        result.DocumentAcquisitionOutcomesByOrdinal is not null &&
        result.DocumentLadderResultsByOrdinal is not null &&
        result.ObservedManifestationTypesByCelex is not null &&
        result.ObservedExpressionsByCelex is not null &&
        result.MintedRowsByOrdinal is not null &&
        result.LocatedAmendmentProduction is not null &&
        result.CorrigendumTripwires is not null &&
        result.CorpusRecordSetRef is not null &&
        result.CorpusRecordSet is not null;

    private static bool LuxembourgIsComplete(LuxembourgQueryExecutionResult result) =>
        result.Refusal is null &&
        result.Completion == LuxembourgQueryExecutionCompletion.AllFamiliesProven &&
        result.FamilyOutcomes.All(static outcome => outcome.Kind is
            LuxembourgFamilyEnumerationOutcomeKind.Proven or
            LuxembourgFamilyEnumerationOutcomeKind.CoverProven) &&
        result.ScopeManifestReceipt is not null &&
        result.ScopeManifestCanonicalSha256 is not null &&
        result.DocumentAcquisitionOutcomesByOrdinal is not null &&
        result.CorpusRecordSetRef is not null &&
        result.CorpusRecordSet is not null &&
        result.HeldBodyDerivationPopulation is not null &&
        result.GazetteBodySetsByOrdinal is not null &&
        result.GazetteListingFetchRefusalsByOrdinal is not null &&
        result.GazetteListingsWithContradictoryLegalValueByOrdinal is not null;

}
