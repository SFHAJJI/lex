using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why two already-completed source columns could not form one transposition bridge.</summary>
public enum EuTranspositionBridgeProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("work_kind_not_for_eu_work")]
    WorkKindNotForEuWork = 1,

    [JsonStringEnumMemberName("legilux_not_delivered")]
    LegiluxNotDelivered = 2,

    [JsonStringEnumMemberName("nim_not_delivered")]
    NimNotDelivered = 3,

    [JsonStringEnumMemberName("legilux_not_singular")]
    LegiluxNotSingular = 4,

    [JsonStringEnumMemberName("nim_not_singular")]
    NimNotSingular = 5,

    [JsonStringEnumMemberName("source_columns_contradict_work_kind")]
    SourceColumnsContradictWorkKind = 6,
}

/// <summary>One two-source bridge, or a typed reason no bridge was produced.</summary>
public sealed class EuTranspositionBridgeProductionResult
{
    private EuTranspositionBridgeProductionResult(
        EuTranspositionBridge? bridge,
        EuTranspositionBridgeProductionRefusal refusal,
        string? detail)
    {
        Bridge = bridge;
        Refusal = refusal;
        Detail = detail;
    }

    public EuTranspositionBridge? Bridge { get; }
    public EuTranspositionBridgeProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuTranspositionBridgeProductionRefusal.None;

    internal static EuTranspositionBridgeProductionResult Success(EuTranspositionBridge bridge) =>
        new(bridge ?? throw new ArgumentNullException(nameof(bridge)), EuTranspositionBridgeProductionRefusal.None, null);

    internal static EuTranspositionBridgeProductionResult Refused(
        EuTranspositionBridgeProductionRefusal refusal,
        string detail)
    {
        if (refusal == EuTranspositionBridgeProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, refusal, detail);
    }
}

/// <summary>
/// Assembles exactly one already-produced Legilux column and one already-produced NIM column for
/// one EU work. It never joins national-measure spellings: that derived ELI operation needs its own
/// evidence and remains absent until such evidence exists.
/// </summary>
public static class EuTranspositionBridgeProducer
{
    public static EuTranspositionBridgeProductionResult Produce(
        string euWorkUri,
        EuWorkKindAssertion workKindAssertion,
        LuxembourgTranspositionProductionResult legilux,
        EuNationalImplementingMeasureProductionResult nim)
    {
        ArgumentNullException.ThrowIfNull(workKindAssertion);
        ArgumentNullException.ThrowIfNull(legilux);
        ArgumentNullException.ThrowIfNull(nim);
        try
        {
            _ = new OfficialIdentitySet(
                PublisherId.EuEurLex,
                [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, euWorkUri)]);
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.WorkKindNotForEuWork, exception.Message);
        }

        if (workKindAssertion.Work.Publisher != PublisherId.EuEurLex ||
            !string.Equals(
                workKindAssertion.Work.Value(FactsIdentifierFamily.CellarWorkUri),
                euWorkUri,
                StringComparison.Ordinal))
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.WorkKindNotForEuWork,
                "The work-kind assertion does not name this exact EU Cellar work.");
        }

        IReadOnlyList<EuTranspositionSourceAcquisition> legiluxColumns;
        try
        {
            legiluxColumns = legilux.ForAssertedEuWork(euWorkUri);
        }
        catch (InvalidOperationException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.LegiluxNotDelivered, exception.Message);
        }

        if (legiluxColumns.Count != 1)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.LegiluxNotSingular,
                "The accepted bridge has one Legilux column and cannot choose or merge multiple assertions.");
        }

        IReadOnlyList<EuTranspositionSourceAcquisition> nimColumns;
        try
        {
            nimColumns = nim.ForEuWork(euWorkUri);
        }
        catch (InvalidOperationException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.NimNotDelivered, exception.Message);
        }

        if (nimColumns.Count != 1)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.NimNotSingular,
                "The accepted bridge has one NIM column and cannot choose or merge multiple assertions.");
        }

        try
        {
            return EuTranspositionBridgeProductionResult.Success(new EuTranspositionBridge(
                euWorkUri,
                workKindAssertion.Kind,
                EuTranspositionBridge.TransposabilityFor(workKindAssertion.Kind),
                legiluxColumns[0],
                nimColumns[0],
                normalisedEliJoin: null));
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, exception.Message);
        }
    }
}
