using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why the declared EU scope could not become one complete transposition population.</summary>
public enum EuTranspositionBridgePopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("work_scope_empty")]
    WorkScopeEmpty = 1,

    [JsonStringEnumMemberName("work_scope_not_unique")]
    WorkScopeNotUnique = 2,

    [JsonStringEnumMemberName("work_scope_not_admitted")]
    WorkScopeNotAdmitted = 3,

    [JsonStringEnumMemberName("source_not_delivered")]
    SourceNotDelivered = 4,

    [JsonStringEnumMemberName("source_work_outside_scope")]
    SourceWorkOutsideScope = 5,

    [JsonStringEnumMemberName("bridge_refused")]
    BridgeRefused = 6,

    [JsonStringEnumMemberName("join_evidence_not_held")]
    JoinEvidenceNotHeld = 7,

}

/// <summary>One scoped bridge and the custody receipt for its derived evidence, when it has any.</summary>
public sealed class EuTranspositionBridgePopulationRow
{
    internal EuTranspositionBridgePopulationRow(
        EuTranspositionBridge bridge,
        DurableBlobWriteReceipt? normalisedEliJoinEvidenceReceipt)
    {
        Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        if ((bridge.NormalisedEliJoin is null) != (normalisedEliJoinEvidenceReceipt is null))
        {
            throw new ArgumentException(
                "A derived normalised-ELI join and its custody receipt are present together.",
                nameof(normalisedEliJoinEvidenceReceipt));
        }
        if (bridge.NormalisedEliJoin is not null &&
            !string.Equals(
                bridge.NormalisedEliJoin.EvidenceRef.Sha256,
                normalisedEliJoinEvidenceReceipt!.Reference.ContentSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The join evidence receipt holds different bytes than the derived join names.",
                nameof(normalisedEliJoinEvidenceReceipt));
        }

        NormalisedEliJoinEvidenceReceipt = normalisedEliJoinEvidenceReceipt;
    }

    public EuTranspositionBridge Bridge { get; }
    public DurableBlobWriteReceipt? NormalisedEliJoinEvidenceReceipt { get; }
}

/// <summary>A complete scoped population, or one typed refusal. Never both.</summary>
public sealed class EuTranspositionBridgePopulationResult
{
    private EuTranspositionBridgePopulationResult(
        IReadOnlyList<EuTranspositionBridgePopulationRow>? rows,
        EuTranspositionBridgePopulationRefusal refusal,
        string? detail)
    {
        Rows = rows;
        Refusal = refusal;
        Detail = detail;
    }

    public IReadOnlyList<EuTranspositionBridgePopulationRow>? Rows { get; }
    public EuTranspositionBridgePopulationRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuTranspositionBridgePopulationRefusal.None;

    internal static EuTranspositionBridgePopulationResult Success(
        IReadOnlyList<EuTranspositionBridgePopulationRow> rows) =>
        new(Array.AsReadOnly(rows.ToArray()), EuTranspositionBridgePopulationRefusal.None, null);

    internal static EuTranspositionBridgePopulationResult Refused(
        EuTranspositionBridgePopulationRefusal refusal,
        string detail)
    {
        if (refusal == EuTranspositionBridgePopulationRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }
        return new(null, refusal, detail);
    }
}

/// <summary>
/// Builds every bridge in one declared EU work scope from the two already-completed publisher
/// acquisitions. Source rows outside that scope are refused rather than silently discarded, and
/// derived join evidence is held by content address before the population is returned.
/// </summary>
public sealed class EuTranspositionBridgePopulationProducer
{
    private readonly ICustodyStore _custodyStore;

    public EuTranspositionBridgePopulationProducer(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<EuTranspositionBridgePopulationResult> ProduceAsync(
        IReadOnlyList<EuWorkKindAssertion> workScope,
        LuxembourgTranspositionProductionResult legilux,
        EuNationalImplementingMeasureProductionResult nim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workScope);
        ArgumentNullException.ThrowIfNull(legilux);
        ArgumentNullException.ThrowIfNull(nim);
        if (workScope.Count == 0)
        {
            return EuTranspositionBridgePopulationResult.Refused(
                EuTranspositionBridgePopulationRefusal.WorkScopeEmpty,
                "A complete bridge population requires a nonempty declared EU work scope.");
        }
        if (!legilux.Delivered || !nim.Delivered || legilux.Relations is null || nim.Relations is null)
        {
            return EuTranspositionBridgePopulationResult.Refused(
                EuTranspositionBridgePopulationRefusal.SourceNotDelivered,
                "Both publisher acquisitions must be delivered before a bridge population can be complete.");
        }

        var scoped = new List<(string WorkUri, EuWorkKindAssertion Kind)>(workScope.Count);
        foreach (var assertion in workScope)
        {
            if (assertion is null || assertion.Work.Publisher != PublisherId.EuEurLex ||
                assertion.Work.Value(FactsIdentifierFamily.CellarWorkUri) is not { } workUri)
            {
                return EuTranspositionBridgePopulationResult.Refused(
                    EuTranspositionBridgePopulationRefusal.WorkScopeNotAdmitted,
                    "Every scoped work-kind assertion must carry its exact EU Cellar work identity.");
            }
            scoped.Add((workUri, assertion));
        }

        var scopeSet = scoped.Select(static value => value.WorkUri).ToHashSet(StringComparer.Ordinal);
        if (scopeSet.Count != scoped.Count)
        {
            return EuTranspositionBridgePopulationResult.Refused(
                EuTranspositionBridgePopulationRefusal.WorkScopeNotUnique,
                "The declared EU work scope contains the same Cellar work more than once.");
        }

        var outsideScope = legilux.Relations.Select(static value => value.EuWorkUri)
            .Concat(nim.Relations.Select(static value => value.EuWorkUri))
            .FirstOrDefault(value => !scopeSet.Contains(value));
        if (outsideScope is not null)
        {
            return EuTranspositionBridgePopulationResult.Refused(
                EuTranspositionBridgePopulationRefusal.SourceWorkOutsideScope,
                $"Publisher evidence names {outsideScope}, which is outside the declared EU work scope.");
        }

        var built = new List<(EuTranspositionBridge Bridge, byte[]? EvidenceBytes)>(scoped.Count);
        foreach (var item in scoped.OrderBy(static value => value.WorkUri, StringComparer.Ordinal))
        {
            var result = EuTranspositionBridgeProducer.Produce(item.WorkUri, item.Kind, legilux, nim);
            if (!result.Delivered || result.Bridge is null)
            {
                return EuTranspositionBridgePopulationResult.Refused(
                    EuTranspositionBridgePopulationRefusal.BridgeRefused,
                    $"Bridge production for {item.WorkUri} refused: {result.Refusal}: {result.Detail}");
            }
            built.Add((result.Bridge, result.CopyNormalisedEliJoinEvidenceBytes()));
        }

        var rows = new List<EuTranspositionBridgePopulationRow>(built.Count);
        foreach (var item in built)
        {
            DurableBlobWriteReceipt? receipt = null;
            if (item.EvidenceBytes is not null)
            {
                var held = await CustodyHold.TryHoldAsync(
                    _custodyStore, item.EvidenceBytes, cancellationToken).ConfigureAwait(false);
                if (held.Receipt is null)
                {
                    return EuTranspositionBridgePopulationResult.Refused(
                        EuTranspositionBridgePopulationRefusal.JoinEvidenceNotHeld,
                        $"Derived join evidence for {item.Bridge.EuWorkUri} was not held: {held.Failure}");
                }
                receipt = held.Receipt;
            }
            rows.Add(new EuTranspositionBridgePopulationRow(item.Bridge, receipt));
        }

        return EuTranspositionBridgePopulationResult.Success(rows);
    }
}
