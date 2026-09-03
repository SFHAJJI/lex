using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgEnumerationReceiptRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// A digest the comparison depends on is absent from the membership this run supplied. Either
    /// the map belongs to another run, or a send dependency was never retained. Both make the
    /// membership claim unusable, so no receipt is issued.
    /// </summary>
    [JsonStringEnumMemberName("send_closure_member_not_held")]
    SendClosureMemberNotHeld = 1,

    /// <summary>
    /// Two sources of membership disagree about one digest. A run cannot hold one object under two
    /// different floors.
    /// </summary>
    [JsonStringEnumMemberName("membership_disagrees_on_a_digest")]
    MembershipDisagreesOnADigest = 2,

    /// <summary>The Core comparison threw. Its message is carried on the executor result.</summary>
    [JsonStringEnumMemberName("delivery_comparison_refused")]
    DeliveryComparisonRefused = 3,
}

/// <summary>
/// One Luxembourg partition's delivery, together with what this run can honestly say about the
/// custody of every artifact the delivery names.
/// </summary>
public sealed class LuxembourgEnumerationDeliveryReceipt
{
    private readonly IReadOnlyDictionary<string, CustodyMembership> _retainedMembership;
    private readonly IReadOnlyList<string> _verifiedWithoutCustodyClaim;
    private readonly IReadOnlyList<string> _unenforcedMemberDigests;

    private LuxembourgEnumerationDeliveryReceipt(
        EnumerationDeliveryComparison delivery,
        IReadOnlyDictionary<string, CustodyMembership> retainedMembership,
        IReadOnlyList<string> verifiedWithoutCustodyClaim,
        CustodyMembership retainedFloor,
        IReadOnlyList<string> unenforcedMemberDigests)
    {
        Delivery = delivery;
        _retainedMembership = retainedMembership;
        _verifiedWithoutCustodyClaim = verifiedWithoutCustodyClaim;
        RetainedFloor = retainedFloor;
        _unenforcedMemberDigests = unenforcedMemberDigests;
    }

    /// <summary>
    /// The Core comparison, by identity. This receipt restates nothing it establishes: counts,
    /// digests, outcome, threshold, partition key and run identity are read from here.
    /// </summary>
    public EnumerationDeliveryComparison Delivery { get; }

    /// <summary>
    /// Per-digest membership for every artifact this run WROTE and receipted, keyed by lowercase
    /// SHA-256: the session's four retained binder and logical-request artifacts per observation,
    /// plus the evidence documents the executor retained. Frozen.
    /// </summary>
    public IReadOnlyDictionary<string, CustodyMembership> RetainedMembership => _retainedMembership;

    /// <summary>
    /// Digests this run opened and verified but did not write: the response payloads and their
    /// durable write receipts. A read is not a custody weakness, so these are reported beside the
    /// floor rather than folded into it.
    /// </summary>
    public IReadOnlyList<string> VerifiedWithoutCustodyClaim => _verifiedWithoutCustodyClaim;

    /// <summary>The weakest membership over the WRITTEN members. Never stronger than the worst of them.</summary>
    public CustodyMembership RetainedFloor { get; }

    /// <summary>Every written digest whose store published no enforcement. Empty iff Floored.</summary>
    public IReadOnlyList<string> UnenforcedMemberDigests => _unenforcedMemberDigests;

    /// <summary>
    /// The comparison, for a consumer that requires a durable run. Throws naming every unenforced
    /// digest. The only accessor that asserts durability; <see cref="Delivery"/> asserts none.
    /// </summary>
    public EnumerationDeliveryComparison RequireFlooredRun()
    {
        if (_unenforcedMemberDigests.Count > 0)
        {
            throw new InvalidOperationException(
                "The following digests are held without an enforced retention floor: " +
                string.Join(", ", _unenforcedMemberDigests));
        }

        return Delivery;
    }

    /// <summary>
    /// Minted only from a comparison. There is no other input from which a receipt can be built,
    /// and <see cref="EnumerationDeliveryComparison"/> has a private constructor whose only door is
    /// <c>Create</c>. Holding one is the evidence.
    /// </summary>
    public static LuxembourgEnumerationDeliveryReceipt? TryCreate(
        EnumerationDeliveryComparison delivery,
        IReadOnlyDictionary<string, CustodyMembership> sessionArtifactMembership,
        IReadOnlyDictionary<string, CustodyMembership> executorWrittenMembership,
        IReadOnlyList<string> readOnlyDigests,
        out LuxembourgEnumerationReceiptRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(sessionArtifactMembership);
        ArgumentNullException.ThrowIfNull(executorWrittenMembership);
        ArgumentNullException.ThrowIfNull(readOnlyDigests);

        var retained = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        foreach (var observation in AllObservations(delivery))
        {
            foreach (var reference in new[]
                     {
                         observation.QueryPlanRef,
                         observation.QueryInputRef,
                         observation.RenderReceiptRef,
                         observation.LogicalRequestRef,
                     })
            {
                if (!sessionArtifactMembership.TryGetValue(reference.Sha256, out var membership))
                {
                    refusal = LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                    return null;
                }

                if (executorWrittenMembership.TryGetValue(reference.Sha256, out var conflicting) &&
                    conflicting != membership)
                {
                    refusal = LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
                    return null;
                }

                retained[reference.Sha256] = membership;
            }

            if (!executorWrittenMembership.TryGetValue(
                    observation.HttpEvidenceRef.Sha256,
                    out var evidenceMembership))
            {
                refusal = LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                return null;
            }

            if (sessionArtifactMembership.TryGetValue(
                    observation.HttpEvidenceRef.Sha256,
                    out var conflictingEvidence) &&
                conflictingEvidence != evidenceMembership)
            {
                refusal = LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
                return null;
            }

            retained[observation.HttpEvidenceRef.Sha256] = evidenceMembership;
        }

        var floor = CustodyMembership.Floored;
        var unenforced = new List<string>();
        foreach (var (digest, membership) in retained)
        {
            if (membership != CustodyMembership.Floored)
            {
                unenforced.Add(digest);
            }

            floor = Weaker(floor, membership);
        }

        refusal = LuxembourgEnumerationReceiptRefusal.None;
        return new LuxembourgEnumerationDeliveryReceipt(
            delivery,
            retained,
            Array.AsReadOnly(readOnlyDigests.ToArray()),
            floor,
            Array.AsReadOnly(unenforced.ToArray()));
    }

    /// <summary>
    /// The weakest of two memberships, by an explicit switch rather than <c>Enum.Min</c>, so a
    /// renumbering of <see cref="CustodyMembership"/> cannot silently invert which value reads as
    /// weaker.
    /// </summary>
    private static CustodyMembership Weaker(CustodyMembership left, CustodyMembership right)
    {
        if (left == CustodyMembership.ReadOnce || right == CustodyMembership.ReadOnce)
        {
            return CustodyMembership.ReadOnce;
        }

        if (left == CustodyMembership.RetainedUnenforced || right == CustodyMembership.RetainedUnenforced)
        {
            return CustodyMembership.RetainedUnenforced;
        }

        return CustodyMembership.Floored;
    }

    private static IEnumerable<RepeatedEnumerationEvidenceRefs> AllObservations(
        EnumerationDeliveryComparison delivery) =>
        new[] { delivery.CountA }
            .Concat(delivery.PagesA.Pages.Select(static page => page.Evidence))
            .Append(delivery.CountB)
            .Concat(delivery.PagesB.Pages.Select(static page => page.Evidence));
}
