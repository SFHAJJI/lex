using System.Globalization;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgQueryExecutionAdapter
{
    /// <summary>
    /// The journaled acquisition this adapter takes part in: where each proven family and each document and Gazette GET
    /// is journaled once custody holds it, and, when the acquisition resumes an interrupted run, that run's verified
    /// journal. Null for an acquisition that keeps no journal; a full replay never has one.
    /// </summary>
    internal LuxembourgAcquisitionProgress? Progress { get; init; }

    /// <summary>
    /// A family the interrupted run proved, reopened from its journaled record through the restore a full replay uses,
    /// when that record names this run's set, range, plan and renderer. Null when the journal names no such family or
    /// the family it names does not reopen: that family is enumerated again, since the journal only says where to look.
    /// </summary>
    private async Task<(QueryFamily Family, List<FamilyRowsLeg> Legs, byte[] PlanBytes)?> TryResumeFamilyAsync(
        LuxembourgPartitionRunRequest request, CancellationToken cancellationToken)
    {
        if (Progress is not { } progress || progress.Resume is not { } resume ||
            !resume.TryTake(AcquisitionJournal.LuxembourgQueryFamilyPhase, request.Partition.PartitionId, out var payload))
            return null;
        try
        {
            var family = ContractJson.Deserialize<QueryFamily>(payload.GetRawText());
            var planBytes = LuxembourgQueryPlan.GetWireBytes(request.InvariantPlan);
            if (family.Set != request.SetId || family.Range != request.Partition ||
                family.Renderer != request.RendererSource.Reference ||
                family.Plan != LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan) ||
                family.PlanWireSha256 != CustodyDigest.Of(planBytes))
                return null;
            var (legs, _) = await RestoreFamilyLegsAsync(_custodyStore, family, request, cancellationToken).ConfigureAwait(false);
            progress.Tally(AcquisitionJournal.LuxembourgQueryFamilyPhase, replayed: true);
            return (family, legs, planBytes);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or CustodyRequiredException
            or CustodyIntegrityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The legs of one retained proven family: its single partition, or each leaf of its reconciled cover, each proven
    /// again from its restored receipt and each count held to the plan's closed template, with the first count's bound
    /// request. The one restore a full replay and a resumed family share.
    /// </summary>
    private static async Task<(List<FamilyRowsLeg> Legs, BoundMachineRequest First)> RestoreFamilyLegsAsync(ICustodyStore store,
        QueryFamily family, LuxembourgPartitionRunRequest request, CancellationToken cancellationToken)
    {
        var legs = new List<FamilyRowsLeg>();
        if (family.Kind == LuxembourgFamilyEnumerationOutcomeKind.Proven)
        {
            var receipt = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(store, family.Checkpoint,
                family.Run, family.Profile, cancellationToken).ConfigureAwait(false);
            var proof = receipt.TryProveFamilyEnumeration(family.Range.PartitionId, out var refusal)
                ?? throw new CustodyIntegrityException("Retained LU family no longer proves: " + refusal);
            legs.Add(new(proof, receipt, request));
        }
        else if (family.Kind == LuxembourgFamilyEnumerationOutcomeKind.CoverProven)
        {
            var cover = await LuxembourgPartitionCoverCheckpoint.RestoreAsync(store, family.Checkpoint,
                family.Range, family.Run, family.Profile, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < cover.Chain.Leaves.Count; index++)
            {
                var leaf = cover.Chain.Leaves[index];
                var receipt = cover.LeafReceipts[index];
                var proof = receipt.TryProveFamilyEnumeration(leaf.PartitionId, out var refusal)
                    ?? throw new CustodyIntegrityException("Retained LU leaf no longer proves: " + refusal);
                legs.Add(new(proof, receipt, request with { Partition = leaf }));
            }
        }
        else throw new CustodyIntegrityException("The LU catalog requires proven original families.");
        if (legs.Count == 0) throw new CustodyIntegrityException("A retained LU family has no proven legs.");
        BoundMachineRequest? first = null;
        foreach (var leg in legs)
        {
            var original = await VerifyQueryTemplateAsync(store, leg, cancellationToken).ConfigureAwait(false);
            first ??= original;
        }

        return (legs, first!);
    }

    /// <summary>
    /// Journals a family this run proved live, as the query catalog will record it at the end of the run: its set,
    /// range, plan, renderer, outcome kind, retained enumeration or cover, and the run and profile of its first proof.
    /// </summary>
    private async Task JournalLiveFamilyAsync(LuxembourgPartitionRunRequest request, LuxembourgFamilyEnumerationOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (Progress is not { } progress) return;
        progress.Tally(AcquisitionJournal.LuxembourgQueryFamilyPhase, replayed: false);
        var proof = outcome.Proof ?? (outcome.CoverLeafProofs is { Count: > 0 } leaves ? leaves[0] : null);
        if (progress.Journal is null || outcome.CheckpointRef is not { } checkpoint || proof is null) return;
        var planBytes = LuxembourgQueryPlan.GetWireBytes(request.InvariantPlan);
        var family = new QueryFamily(request.SetId, request.Partition,
            LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan), CustodyDigest.Of(planBytes),
            request.RendererSource.Reference, outcome.Kind, checkpoint, proof.AcquisitionRunRef, proof.InterpretationProfileRef);
        await JournalFamilyAsync(family, planBytes, request.RendererSource, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends one proven family once its plan and renderer bytes are held, naming them and its retained enumeration or
    /// cover. A family whose inputs cannot be held is not journaled and a resume enumerates it again; the acquisition
    /// itself goes on as it would without a journal, and its catalog still refuses what it cannot retain.
    /// </summary>
    private async Task JournalFamilyAsync(QueryFamily family, byte[] planBytes, MachineQueryRendererSource renderer,
        CancellationToken cancellationToken)
    {
        if (Progress?.Journal is not { } journal) return;
        try
        {
            await HoldQueryInputAsync(planBytes, cancellationToken).ConfigureAwait(false);
            await HoldQueryInputAsync(renderer.CopyBytes(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return;
        }

        await journal.AppendAsync(AcquisitionJournal.LuxembourgQueryFamilyPhase, family.Range.PartitionId,
            AcquisitionJournal.Payload(family), [family.Checkpoint.Sha256, family.PlanWireSha256, family.Renderer.Sha256])
            .ConfigureAwait(false);
    }

    /// <summary>The journal key of one document or Gazette GET: the manifest row it serves and its address's identity.</summary>
    private static string FetchKey(int ordinal, SourceArtifactRef address) =>
        ordinal.ToString(CultureInfo.InvariantCulture) + ":" + address.Sha256;

    /// <summary>
    /// A journaled GET: the phase checkpoint's own record of it, with the input digest of the selection it was fetched
    /// for, so a resume replays it only for the same manifest, addresses and (for the Gazette) held routes.
    /// </summary>
    private sealed record JournaledFetch(string InputSha256, DocumentFetch Fetch);
}

/// <summary>
/// A Luxembourg acquisition's part in a journaled run: the journal its units are written to and, when it resumes an
/// interrupted run, that run's verified journal, from which each unit is taken once. It counts, per phase, the units it
/// replayed and the units it acquired live, for the catalog a resumed run retains.
/// </summary>
internal sealed class LuxembourgAcquisitionProgress(IAcquisitionJournal? journal, AcquisitionResume? resume)
{
    private readonly Dictionary<string, (int Replayed, int Live)> _tallies = new(StringComparer.Ordinal);

    internal IAcquisitionJournal? Journal { get; } = journal;

    internal AcquisitionResume? Resume { get; } = resume;

    internal void Tally(string phase, bool replayed)
    {
        var (replayedCount, liveCount) = _tallies.GetValueOrDefault(phase);
        _tallies[phase] = replayed ? (replayedCount + 1, liveCount) : (replayedCount, liveCount + 1);
    }

    /// <summary>The counts of <paramref name="phases"/>, in that order.</summary>
    internal AcquisitionResumedPhase[] Phases(params string[] phases) => phases.Select(phase =>
    {
        var (replayedCount, liveCount) = _tallies.GetValueOrDefault(phase);
        return new AcquisitionResumedPhase(phase, replayedCount, liveCount);
    }).ToArray();
}
