using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>The fidelity obligations the accepted producer set does not yet prove. Closed.</summary>
public enum Stage3FidelityPreservationObligation
{
    /// <summary>The retained evidence does not support a reviewed marker-only interpretation rule.</summary>
    [JsonStringEnumMemberName("marker_only_rule_unsupported")]
    MarkerOnlyRuleUnsupported = 1,

    /// <summary>No accepted producer yet proves that publisher footnotes survive derivation.</summary>
    [JsonStringEnumMemberName("footnote_preservation_unproven")]
    FootnotePreservationUnproven = 2,

    /// <summary>No accepted producer yet proves that publisher citations survive derivation.</summary>
    [JsonStringEnumMemberName("citation_preservation_unproven")]
    CitationPreservationUnproven = 3,
}

/// <summary>Why the accepted adapter results could not form the fidelity-preservation carrier.</summary>
public enum Stage3FidelityPreservationReconciliationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("europe_not_complete")]
    EuropeNotComplete = 1,

    [JsonStringEnumMemberName("luxembourg_not_complete")]
    LuxembourgNotComplete = 2,
}

/// <summary>
/// Retains the exact guarded fidelity outputs of the accepted EU and Luxembourg runs and names what
/// those runs still do not prove. It adds no interpretation rule and makes no completion claim.
/// </summary>
public sealed class Stage3FidelityPreservationReconciliation
{
    private static readonly ReadOnlyCollection<Stage3FidelityPreservationObligation> OpenObligations =
        Array.AsReadOnly(new[]
        {
            Stage3FidelityPreservationObligation.MarkerOnlyRuleUnsupported,
            Stage3FidelityPreservationObligation.FootnotePreservationUnproven,
            Stage3FidelityPreservationObligation.CitationPreservationUnproven,
        });

    private Stage3FidelityPreservationReconciliation(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg)
    {
        Europe = europe;
        Luxembourg = luxembourg;
        LocatedAmendments = europe.LocatedAmendmentProduction!;
        CorrigendumTripwires = europe.CorrigendumTripwires!;
        LuxembourgGazetteBodies = luxembourg.GazetteBodySetsByOrdinal!;
        LuxembourgPopulation = luxembourg.PopulationLedger!;
    }

    public EuQueryExecutionResult Europe { get; }

    public LuxembourgQueryExecutionResult Luxembourg { get; }

    public EuLocatedAmendmentProduction LocatedAmendments { get; }

    public EuCorrigendumTripwireCompletion CorrigendumTripwires { get; }

    public IReadOnlyDictionary<int, LuxembourgGazetteBodySet> LuxembourgGazetteBodies { get; }

    public LuxembourgNeverConsolidatedBodyLedger LuxembourgPopulation { get; }

    /// <summary>
    /// The accepted producer set's remaining preservation obligations. Their presence prevents a
    /// downstream builder from reading an absent producer as an observed zero or completed proof.
    /// </summary>
    public IReadOnlyList<Stage3FidelityPreservationObligation> UnresolvedObligations => OpenObligations;

    public static Stage3FidelityPreservationReconciliation? TryCreate(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        out Stage3FidelityPreservationReconciliationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(europe);
        ArgumentNullException.ThrowIfNull(luxembourg);

        if (europe.Refusal is not null
            || europe.Completion != EuQueryExecutionCompletion.AllFamiliesProven
            || europe.LocatedAmendmentProduction is null
            || europe.CorrigendumTripwires is null)
        {
            refusal = Stage3FidelityPreservationReconciliationRefusal.EuropeNotComplete;
            detail = europe.Refusal?.Code.ToString()
                ?? (europe.Completion != EuQueryExecutionCompletion.AllFamiliesProven
                    ? europe.Completion?.ToString()
                    : null)
                ?? (europe.LocatedAmendmentProduction is null
                    ? "located amendment production absent"
                    : "corrigendum tripwire production absent");
            return null;
        }

        if (luxembourg.Refusal is not null
            || luxembourg.Completion != LuxembourgQueryExecutionCompletion.AllFamiliesProven
            || luxembourg.GazetteBodySetsByOrdinal is null
            || luxembourg.PopulationLedger is null)
        {
            refusal = Stage3FidelityPreservationReconciliationRefusal.LuxembourgNotComplete;
            detail = luxembourg.Refusal?.Code.ToString()
                ?? (luxembourg.Completion != LuxembourgQueryExecutionCompletion.AllFamiliesProven
                    ? luxembourg.Completion?.ToString()
                    : null)
                ?? (luxembourg.GazetteBodySetsByOrdinal is null
                    ? "Gazette body production absent"
                    : "population ledger absent");
            return null;
        }

        refusal = Stage3FidelityPreservationReconciliationRefusal.None;
        detail = null;
        return new Stage3FidelityPreservationReconciliation(europe, luxembourg);
    }
}
