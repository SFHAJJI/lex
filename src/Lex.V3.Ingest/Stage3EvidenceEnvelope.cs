using System.Text.Json.Serialization;
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
}

/// <summary>
/// The construction-only meeting point for accepted EU, Luxembourg and supplementary Formex
/// outputs. It neither serializes nor upgrades them to a release artifact.
/// </summary>
public sealed class Stage3EvidenceEnvelope
{
    private Stage3EvidenceEnvelope(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation formexAnnexClassifications)
    {
        Europe = europe;
        Luxembourg = luxembourg;
        Formex = formex;
        FormexAnnexClassifications = formexAnnexClassifications;
    }

    public EuQueryExecutionResult Europe { get; }

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

    public static Stage3EvidenceEnvelope? TryCreate(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        EuFormexRunOutcomeReconciliation formex,
        EuFormexAnnexClassificationReconciliation formexAnnexClassifications,
        out Stage3EvidenceEnvelopeRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(europe);
        ArgumentNullException.ThrowIfNull(luxembourg);
        ArgumentNullException.ThrowIfNull(formex);
        ArgumentNullException.ThrowIfNull(formexAnnexClassifications);

        refusal = Stage3EvidenceEnvelopeRefusal.None;
        detail = null;
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

        return new Stage3EvidenceEnvelope(europe, luxembourg, formex, formexAnnexClassifications);
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
        result.GazetteBodySetsByOrdinal is not null &&
        result.GazetteListingFetchRefusalsByOrdinal is not null &&
        result.GazetteListingsWithContradictoryLegalValueByOrdinal is not null;

}
