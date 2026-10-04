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
    /// One leaf an adaptive cover delivered, as journaled: the family's set, range, plan and renderer (as a family record
    /// names them), the chain's split history when the leaf was delivered, the leaf's index and range, its enumeration
    /// checkpoint, and the run and interpretation profile it was proven under.
    /// </summary>
    private sealed record CoverLeaf(string Set, LuxembourgQueryPartitionRange Range, SourceArtifactRef Plan, string PlanWireSha256,
        SourceArtifactRef Renderer, IReadOnlyList<LuxembourgPartitionSplitStep> Splits, int Index, LuxembourgQueryPartitionRange Leaf,
        SourceArtifactRef Checkpoint, SourceArtifactRef Run, SourceArtifactRef Profile);

    /// <summary>The journal key of a cover's delivered leaf: its family's partition id and its index in the chain.</summary>
    private static string CoverLeafKey(string family, int index) => family + "#" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The leaves of this family's cover an interrupted run delivered and journaled, restored in index order as far as
    /// each still binds and proves: its record names this family's set, range, plan and renderer and its own index; its
    /// split history, replayed from the root, places it at that index with the leaves accepted before it still first; its
    /// enumeration checkpoint restores under its run and the cover's one profile, its query bounds are its leaf's, and it
    /// proves that leaf. The first that does not ends the prefix, and the cover goes on from there; null when none binds,
    /// and the family is enumerated from its first leaf. The accepted leaves are journaled again in this run's journal,
    /// so a resume of this run finds them too. Nothing here is admitted on the journal's word.
    /// </summary>
    internal async Task<LuxembourgCoverResumePoint?> TryResumeCoverLeavesAsync(LuxembourgPartitionRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Progress is not { } progress || progress.Resume is not { } resume) return null;
        var family = request.Partition.PartitionId;
        var planBytes = LuxembourgQueryPlan.GetWireBytes(request.InvariantPlan);
        var plan = LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan);
        var planWire = CustodyDigest.Of(planBytes);
        var accepted = new List<CoverLeaf>();
        var receipts = new List<RepeatedEnumerationDeliveryReceipt>();
        LuxembourgPartitionChain? chain = null;
        var index = 0;
        for (; resume.TryTake(AcquisitionJournal.LuxembourgCoverLeafPhase, CoverLeafKey(family, index), out var payload); index++)
        {
            try
            {
                var record = ContractJson.Deserialize<CoverLeaf>(payload.GetRawText());
                if (record.Set != request.SetId || record.Range != request.Partition || record.Plan != plan ||
                    record.PlanWireSha256 != planWire || record.Renderer != request.RendererSource.Reference ||
                    record.Index != index || record.Splits is null || record.Leaf is null || record.Checkpoint is null ||
                    record.Run is null || record.Profile is null || (accepted.Count > 0 && record.Profile != accepted[0].Profile))
                    break;
                var candidate = LuxembourgPartitionChain.Root(request.Partition);
                foreach (var step in record.Splits)
                    candidate = candidate.SplitLeaf(step.LeafPartitionId, step.Boundary, step.LeftPartitionId, step.RightPartitionId);
                if (index >= candidate.Leaves.Count || candidate.Leaves[index] != record.Leaf) break;
                var prefixHolds = true;
                for (var earlier = 0; earlier < accepted.Count; earlier++)
                    prefixHolds &= candidate.Leaves[earlier] == accepted[earlier].Leaf;
                if (!prefixHolds) break;
                var receipt = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(_custodyStore, record.Checkpoint, record.Run,
                    record.Profile, cancellationToken).ConfigureAwait(false);
                await LuxembourgPartitionCoverCheckpoint.ValidateRangeAsync(_custodyStore, receipt, record.Leaf, cancellationToken)
                    .ConfigureAwait(false);
                if (receipt.TryProveFamilyEnumeration(record.Leaf.PartitionId, out _) is null) break;
                accepted.Add(record);
                receipts.Add(receipt);
                chain = candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or CustodyRequiredException
                or CustodyIntegrityException)
            {
                break;
            }
        }

        // Any later lines of this family are never used: the cover is acquired again from the first leaf that did not bind.
        while (resume.TryTake(AcquisitionJournal.LuxembourgCoverLeafPhase, CoverLeafKey(family, ++index), out _))
        {
        }

        if (chain is null) return null;
        foreach (var record in accepted)
        {
            progress.Tally(AcquisitionJournal.LuxembourgCoverLeafPhase, replayed: true);
            await AppendCoverLeafAsync(record, planBytes, request.RendererSource, cancellationToken).ConfigureAwait(false);
        }

        return new LuxembourgCoverResumePoint(chain, receipts, accepted.Select(static record => record.Checkpoint).ToArray(),
            accepted[0].Profile);
    }

    /// <summary>Journals a leaf this run's cover delivered, once its enumeration checkpoint is held.</summary>
    internal async Task JournalCoverLeafAsync(LuxembourgPartitionRunRequest request, LuxembourgCoverLeafDelivered leaf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(leaf);
        if (Progress is not { } progress || progress.Journal is null) return;
        progress.Tally(AcquisitionJournal.LuxembourgCoverLeafPhase, replayed: false);
        var planBytes = LuxembourgQueryPlan.GetWireBytes(request.InvariantPlan);
        var record = new CoverLeaf(request.SetId, request.Partition,
            LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan), CustodyDigest.Of(planBytes),
            request.RendererSource.Reference, leaf.Splits.ToArray(), leaf.Index, leaf.Leaf, leaf.Checkpoint, leaf.Run,
            leaf.InterpretationProfileRef);
        await AppendCoverLeafAsync(record, planBytes, request.RendererSource, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends one leaf once its plan and renderer bytes are held, naming them and its enumeration checkpoint. A leaf whose
    /// inputs cannot be held is not journaled, and a resume acquires it again.
    /// </summary>
    private async Task AppendCoverLeafAsync(CoverLeaf record, byte[] planBytes, MachineQueryRendererSource renderer,
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

        await journal.AppendAsync(AcquisitionJournal.LuxembourgCoverLeafPhase, CoverLeafKey(record.Range.PartitionId, record.Index),
            AcquisitionJournal.Payload(record), [record.Checkpoint.Sha256, record.PlanWireSha256, record.Renderer.Sha256])
            .ConfigureAwait(false);
    }

    /// <summary>The leaf callback a family's adaptive cover is given: this adapter's journal, for that family's request.</summary>
    private sealed class CoverLeafJournal(LuxembourgQueryExecutionAdapter adapter, LuxembourgPartitionRunRequest request,
        CancellationToken cancellationToken)
    {
        internal Task AppendAsync(LuxembourgCoverLeafDelivered leaf) => adapter.JournalCoverLeafAsync(request, leaf, cancellationToken);
    }
}
