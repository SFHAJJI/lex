using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// A cover of one root range by adjacent half-open children. Built only by splitting a leaf at one
/// interior cursor, so a gap or an overlap is not representable. There is deliberately no
/// constructor taking a list of ranges: a list is where a gap check would be needed, and a check is
/// what this type exists to avoid. Adjacency is object identity, not equality: <see cref="SplitLeaf"/>
/// builds the left child's <see cref="LuxembourgQueryPartitionRange.EndExclusive"/> and the right
/// child's <see cref="LuxembourgQueryPartitionRange.StartInclusive"/> from the exact same boundary
/// cursor instance.
/// </summary>
public sealed class LuxembourgPartitionChain
{
    private readonly IReadOnlyList<LuxembourgQueryPartitionRange> _leaves;

    private LuxembourgPartitionChain(
        LuxembourgQueryPartitionRange rootRange,
        IReadOnlyList<LuxembourgQueryPartitionRange> leaves)
    {
        RootRange = rootRange;
        _leaves = leaves;
    }

    public LuxembourgQueryPartitionRange RootRange { get; }

    /// <summary>Leaves in ascending StartInclusive order, contiguous and flush with the root.</summary>
    public IReadOnlyList<LuxembourgQueryPartitionRange> Leaves => _leaves;

    public static LuxembourgPartitionChain Root(LuxembourgQueryPartitionRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return new LuxembourgPartitionChain(range, [range]);
    }

    /// <summary>
    /// Replaces leaf [a, c) with [a, b) and [b, c) built from the same three cursor objects.
    /// Refuses a boundary outside (a, c), and refuses a partition id that is not both printable
    /// ASCII (<see cref="LuxembourgQueryPartitionRange"/>) and a machine member key (<see
    /// cref="MachineQueryValidation.RequireMachineMemberKey"/>), which is the narrower of the two,
    /// so an id that would fail deep inside <c>MachineQueryInputArtifact.Create</c> fails here
    /// instead. Returns a new chain; this instance is unchanged.
    /// </summary>
    public LuxembourgPartitionChain SplitLeaf(
        string leafPartitionId,
        LuxembourgQueryCursor boundary,
        string leftPartitionId,
        string rightPartitionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(leafPartitionId);
        ArgumentNullException.ThrowIfNull(boundary);
        MachineQueryValidation.RequireMachineMemberKey(leftPartitionId, nameof(leftPartitionId));
        MachineQueryValidation.RequireMachineMemberKey(rightPartitionId, nameof(rightPartitionId));
        if (string.Equals(leftPartitionId, rightPartitionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A split must give its two children distinct partition ids.",
                nameof(rightPartitionId));
        }

        if (_leaves.Any(existing => existing.PartitionId == leftPartitionId ||
                existing.PartitionId == rightPartitionId))
        {
            throw new ArgumentException(
                "A split child's partition id must be unused anywhere in this chain.",
                nameof(leftPartitionId));
        }

        var index = IndexOfLeaf(leafPartitionId);
        var leaf = _leaves[index];
        if (boundary.CompareTo(leaf.StartInclusive) <= 0 ||
            boundary.CompareTo(leaf.EndExclusive) >= 0)
        {
            throw new ArgumentException(
                "A split boundary must fall strictly inside the leaf it splits.",
                nameof(boundary));
        }

        var left = new LuxembourgQueryPartitionRange(leftPartitionId, leaf.StartInclusive, boundary);
        var right = new LuxembourgQueryPartitionRange(rightPartitionId, boundary, leaf.EndExclusive);
        var next = new List<LuxembourgQueryPartitionRange>(_leaves.Count + 1);
        next.AddRange(_leaves.Take(index));
        next.Add(left);
        next.Add(right);
        next.AddRange(_leaves.Skip(index + 1));
        return new LuxembourgPartitionChain(RootRange, next);
    }

    private int IndexOfLeaf(string leafPartitionId)
    {
        for (var index = 0; index < _leaves.Count; index++)
        {
            if (string.Equals(_leaves[index].PartitionId, leafPartitionId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ArgumentException(
            "The chain has no leaf with that partition id.",
            nameof(leafPartitionId));
    }
}

/// <summary>What the reconciliation of a cover rests on.</summary>
public enum LuxembourgPartitionCoverBasis
{
    /// <summary>
    /// The root range's own proven enumeration was supplied and its delivered row count equals the
    /// sum of the leaves'. The tiling is checked against an independent observation.
    /// </summary>
    [JsonStringEnumMemberName("root_count_verified")]
    RootCountVerified = 1,

    /// <summary>
    /// No proven root enumeration exists, because the root's selection is at or above the publisher
    /// delivery ceiling. Coverage rests on range structure alone: the leaves tile the root and each
    /// leaf is proven whole, but no observation cross-checks the sum.
    /// </summary>
    [JsonStringEnumMemberName("leaf_tiling_only")]
    LeafTilingOnly = 2,
}

public enum LuxembourgPartitionCoverRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("leaf_receipt_missing")]
    LeafReceiptMissing = 1,

    [JsonStringEnumMemberName("leaf_partition_key_mismatch")]
    LeafPartitionKeyMismatch = 2,

    [JsonStringEnumMemberName("leaf_selections_differ")]
    LeafSelectionsDiffer = 3,

    [JsonStringEnumMemberName("leaf_partition_required")]
    LeafPartitionRequired = 4,

    [JsonStringEnumMemberName("leaf_run_identity_differs")]
    LeafRunIdentityDiffers = 5,

    [JsonStringEnumMemberName("leaf_profile_differs")]
    LeafProfileDiffers = 6,

    [JsonStringEnumMemberName("root_count_does_not_equal_the_leaf_sum")]
    RootCountDoesNotEqualTheLeafSum = 7,
}

/// <summary>
/// The reconciliation of a chain's leaves into one coverage claim. Every refusal below is checked
/// in the order listed, per leaf in chain order, before the cross-leaf checks run.
/// </summary>
public sealed class LuxembourgPartitionCover
{
    private readonly IReadOnlyList<LuxembourgEnumerationDeliveryReceipt> _leafReceipts;

    private LuxembourgPartitionCover(
        LuxembourgPartitionChain chain,
        IReadOnlyList<LuxembourgEnumerationDeliveryReceipt> leafReceipts,
        LuxembourgPartitionCoverBasis basis,
        SourceArtifactRef runIdentity,
        SourceArtifactRef interpretationProfileRef,
        long leafDeliveredRowCountSum,
        CustodyMembership retainedFloor)
    {
        Chain = chain;
        _leafReceipts = leafReceipts;
        Basis = basis;
        RunIdentity = runIdentity;
        InterpretationProfileRef = interpretationProfileRef;
        LeafDeliveredRowCountSum = leafDeliveredRowCountSum;
        RetainedFloor = retainedFloor;
    }

    public LuxembourgPartitionChain Chain { get; }

    public IReadOnlyList<LuxembourgEnumerationDeliveryReceipt> LeafReceipts => _leafReceipts;

    public LuxembourgPartitionCoverBasis Basis { get; }

    public SourceArtifactRef RunIdentity { get; }

    public SourceArtifactRef InterpretationProfileRef { get; }

    public long LeafDeliveredRowCountSum { get; }

    public CustodyMembership RetainedFloor { get; }

    /// <summary>
    /// Reconciles a chain's leaves. Checked per leaf, in chain order: the receipt is present, its
    /// partition key matches the leaf it is claimed for, its two passes agreed
    /// (<see cref="EnumerationDeliveryOutcome.EqualSelections"/>), and the leaf was not itself
    /// saturated (<see cref="RepeatedEnumerationThresholdAssessment.BelowMaximum"/> - a saturated
    /// leaf is not a leaf, it is a node that still needs splitting). Then, across every leaf: one
    /// run identity, one interpretation profile. Finally, when a root receipt is supplied, its
    /// partition key must name the chain's root and its delivered row count must equal the sum of
    /// the leaves'.
    /// </summary>
    public static LuxembourgPartitionCover? TryCreate(
        LuxembourgPartitionChain chain,
        IReadOnlyList<LuxembourgEnumerationDeliveryReceipt> leafReceiptsInLeafOrder,
        LuxembourgEnumerationDeliveryReceipt? rootReceipt,
        out LuxembourgPartitionCoverRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(leafReceiptsInLeafOrder);

        if (leafReceiptsInLeafOrder.Count != chain.Leaves.Count ||
            leafReceiptsInLeafOrder.Any(static receipt => receipt is null))
        {
            refusal = LuxembourgPartitionCoverRefusal.LeafReceiptMissing;
            return null;
        }

        SourceArtifactRef? runIdentity = null;
        SourceArtifactRef? profileRef = null;
        CustodyMembership? floor = null;
        long sum = 0;

        for (var index = 0; index < chain.Leaves.Count; index++)
        {
            var leaf = chain.Leaves[index];
            var receipt = leafReceiptsInLeafOrder[index];
            var delivery = receipt.Delivery;

            if (delivery.PartitionKey != leaf.PartitionId)
            {
                refusal = LuxembourgPartitionCoverRefusal.LeafPartitionKeyMismatch;
                return null;
            }

            if (delivery.Outcome != EnumerationDeliveryOutcome.EqualSelections)
            {
                refusal = LuxembourgPartitionCoverRefusal.LeafSelectionsDiffer;
                return null;
            }

            if (delivery.ThresholdAssessment != RepeatedEnumerationThresholdAssessment.BelowMaximum)
            {
                refusal = LuxembourgPartitionCoverRefusal.LeafPartitionRequired;
                return null;
            }

            if (runIdentity is null)
            {
                runIdentity = delivery.RunIdentity;
            }
            else if (runIdentity != delivery.RunIdentity)
            {
                refusal = LuxembourgPartitionCoverRefusal.LeafRunIdentityDiffers;
                return null;
            }

            if (profileRef is null)
            {
                profileRef = delivery.InterpretationProfileRef;
            }
            else if (profileRef != delivery.InterpretationProfileRef)
            {
                refusal = LuxembourgPartitionCoverRefusal.LeafProfileDiffers;
                return null;
            }

            sum = checked(sum + delivery.DeliveredRowCountA);
            // One rule, one place. This used to be a second copy of the receipt's own switch, which
            // is two places for one invariant and was the reason both copies carried the same dead
            // ReadOnce case. Every leaf floor here was produced by that same rule, so it is already
            // one of the two values Weakest is total over.
            floor = floor is null
                ? receipt.RetainedFloor
                : LuxembourgEnumerationDeliveryReceipt.Weakest(floor.Value, receipt.RetainedFloor);
        }

        if (rootReceipt is not null)
        {
            if (rootReceipt.Delivery.PartitionKey != chain.RootRange.PartitionId)
            {
                refusal = LuxembourgPartitionCoverRefusal.LeafPartitionKeyMismatch;
                return null;
            }

            if (rootReceipt.Delivery.DeliveredRowCountA != sum)
            {
                refusal = LuxembourgPartitionCoverRefusal.RootCountDoesNotEqualTheLeafSum;
                return null;
            }
        }

        refusal = LuxembourgPartitionCoverRefusal.None;
        return new LuxembourgPartitionCover(
            chain,
            Array.AsReadOnly(leafReceiptsInLeafOrder.ToArray()),
            rootReceipt is not null
                ? LuxembourgPartitionCoverBasis.RootCountVerified
                : LuxembourgPartitionCoverBasis.LeafTilingOnly,
            runIdentity!,
            profileRef!,
            sum,
            floor!.Value);
    }

}
