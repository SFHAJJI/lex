using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
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

    /// <summary>
    /// A supplied membership value is one no write receipt can produce. <see
    /// cref="CustodyMembershipClassifier"/> answers only <see
    /// cref="CustodyMembership.RetainedUnenforced"/> or <see cref="CustodyMembership.Floored"/>,
    /// so <see cref="CustodyMembership.ReadOnce"/> arriving here means the caller's map was not
    /// built from receipts. Refused rather than folded, because the floor rule below has no
    /// defensible answer for a membership that establishes no custody at all, and answering
    /// <see cref="CustodyMembership.Floored"/> for it would be the strongest possible wrong answer.
    /// </summary>
    [JsonStringEnumMemberName("membership_is_not_receipt_derived")]
    MembershipIsNotReceiptDerived = 4,
}

/// <summary>
/// One Luxembourg partition's delivery, together with what this run can honestly say about the
/// custody of every artifact the delivery names.
/// </summary>
public sealed class LuxembourgEnumerationDeliveryReceipt
{
    private readonly IReadOnlyDictionary<string, CustodyMembership> _retainedMembership;
    private readonly IReadOnlyList<string> _unenforcedMemberDigests;

    private LuxembourgEnumerationDeliveryReceipt(
        EnumerationDeliveryComparison delivery,
        IReadOnlyDictionary<string, CustodyMembership> retainedMembership,
        CustodyMembership retainedFloor,
        IReadOnlyList<string> unenforcedMemberDigests)
    {
        Delivery = delivery;
        _retainedMembership = retainedMembership;
        RetainedFloor = retainedFloor;
        _unenforcedMemberDigests = unenforcedMemberDigests;
    }

    /// <summary>
    /// The Core comparison, by identity. This receipt restates nothing it establishes: counts,
    /// digests, outcome, threshold, partition key and run identity are read from here.
    /// </summary>
    public EnumerationDeliveryComparison Delivery { get; }

    /// <summary>
    /// Per-digest membership for every artifact this run wrote and receipted, keyed by lowercase
    /// SHA-256. Per observation that is: the session's four retained binder and logical-request
    /// artifacts, the response body, the body's durable write receipt, and the HTTP evidence
    /// document the executor retained. Frozen.
    /// </summary>
    /// <remarks>
    /// The response body and its write receipt are in here rather than beside it. An earlier shape
    /// listed them as verified-without-a-custody-claim, on the reasoning that this run reads them
    /// rather than writing them. That reasoning was wrong about who wrote them: the acquisition
    /// session writes both, holds a receipt for both, and the digests are reachable from those
    /// receipts, so classifying them costs nothing. Leaving them outside the floor let
    /// <see cref="RequireFlooredRun"/> pass while the publisher's own response bytes, the only
    /// artifact whose loss cannot be repaired by re-deriving anything, sat unfloored.
    /// </remarks>
    public IReadOnlyDictionary<string, CustodyMembership> RetainedMembership => _retainedMembership;

    /// <summary>The weakest membership over every member. Never stronger than the worst of them.</summary>
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
    /// Minted only from a comparison and the exact observations that produced it. There is no
    /// other input from which a receipt can be built: <see cref="EnumerationDeliveryComparison"/>
    /// has a private constructor whose only door is <c>Create</c>, and
    /// <see cref="LuxembourgDeliveryObservation"/> has a private constructor whose only doors
    /// require a transport already bound to a real terminal hop. Holding one is the evidence.
    /// </summary>
    /// <param name="observationCustody">
    /// One entry per observation behind <paramref name="delivery"/>, in any order. Every reference
    /// set the delivery names must appear here with an identical <c>References</c> tuple, so a
    /// caller cannot pair one run's comparison with another run's bodies and cannot leave a body
    /// out to keep it off the floor; either refuses
    /// <see cref="LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld"/>.
    /// </param>
    public static LuxembourgEnumerationDeliveryReceipt? TryCreate(
        EnumerationDeliveryComparison delivery,
        IReadOnlyDictionary<string, CustodyMembership> sessionArtifactMembership,
        IReadOnlyDictionary<string, CustodyMembership> executorWrittenMembership,
        IReadOnlyList<LuxembourgObservationCustody> observationCustody,
        out LuxembourgEnumerationReceiptRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(sessionArtifactMembership);
        ArgumentNullException.ThrowIfNull(executorWrittenMembership);
        ArgumentNullException.ThrowIfNull(observationCustody);

        var byEvidenceDigest = new Dictionary<string, LuxembourgObservationCustody>(StringComparer.Ordinal);
        foreach (var custody in observationCustody)
        {
            ArgumentNullException.ThrowIfNull(custody);
            byEvidenceDigest[custody.References.HttpEvidenceRef.Sha256] = custody;
        }

        var retained = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        foreach (var references in AllObservations(delivery))
        {
            if (!byEvidenceDigest.TryGetValue(references.HttpEvidenceRef.Sha256, out var observation) ||
                observation.References != references)
            {
                refusal = LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                return null;
            }

            // Everything the acquisition session retained for this observation: the four send
            // dependencies it binds, plus the durable write receipt it wrote for the response body
            // (RoutedHttpAcquisitionSession.HoldAsync retains that receipt through the same
            // RetainArtifactAsync path as the other four, so its membership is in the same map).
            foreach (var digest in new[]
                     {
                         references.QueryPlanRef.Sha256,
                         references.QueryInputRef.Sha256,
                         references.RenderReceiptRef.Sha256,
                         references.LogicalRequestRef.Sha256,
                         observation.DurableWriteReceiptSha256,
                     })
            {
                if (!sessionArtifactMembership.TryGetValue(digest, out var membership))
                {
                    refusal = LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                    return null;
                }

                if (executorWrittenMembership.TryGetValue(digest, out var conflicting) &&
                    conflicting != membership)
                {
                    refusal = LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
                    return null;
                }

                if (!Record(retained, digest, membership, out refusal))
                {
                    return null;
                }
            }

            // The response body, classified from the write receipt the store issued for those exact
            // bytes. LuxembourgDeliveryObservation.Create has already refused unless that receipt's
            // Reference.ContentSha256 is this body's digest, so this is the body's own membership
            // and not some other object's.
            //
            // Two observations CAN name the same body digest: a publisher that answers two passes
            // identically sends byte-identical bodies, and an empty terminal page is the common
            // case. If they disagree about its custody, that is the same disagreement this method
            // already refuses between the two input maps, and it refuses here for the same reason.
            if (Disagrees(sessionArtifactMembership, observation.ResponseBodySha256, observation.ResponseBodyMembership) ||
                Disagrees(executorWrittenMembership, observation.ResponseBodySha256, observation.ResponseBodyMembership))
            {
                refusal = LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
                return null;
            }

            if (!Record(retained, observation.ResponseBodySha256, observation.ResponseBodyMembership, out refusal))
            {
                return null;
            }

            if (!executorWrittenMembership.TryGetValue(
                    references.HttpEvidenceRef.Sha256,
                    out var evidenceMembership))
            {
                refusal = LuxembourgEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                return null;
            }

            if (sessionArtifactMembership.TryGetValue(
                    references.HttpEvidenceRef.Sha256,
                    out var conflictingEvidence) &&
                conflictingEvidence != evidenceMembership)
            {
                refusal = LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
                return null;
            }

            if (!Record(retained, references.HttpEvidenceRef.Sha256, evidenceMembership, out refusal))
            {
                return null;
            }
        }

        var floor = CustodyMembership.Floored;
        var unenforced = new List<string>();
        foreach (var (digest, membership) in retained)
        {
            if (membership != CustodyMembership.Floored)
            {
                unenforced.Add(digest);
            }

            floor = Weakest(floor, membership);
        }

        refusal = LuxembourgEnumerationReceiptRefusal.None;
        return new LuxembourgEnumerationDeliveryReceipt(
            delivery,
            retained,
            floor,
            Array.AsReadOnly(unenforced.ToArray()));
    }

    /// <summary>
    /// The only Luxembourg path from a delivery receipt to an absence enumeration proof, and so the
    /// only LU path to <see cref="AbsenceCut.TryCreateComplete"/>, which admits no family without
    /// one. It reads <see cref="RequireFlooredRun"/>, never <see cref="Delivery"/>: a run holding
    /// any member without an enforced floor cannot mint a proof at all, because a proof of complete
    /// enumeration whose evidence may not survive ninety days is a claim nobody can go back and
    /// check. That is a throw rather than a typed refusal because the caller asked for durability
    /// by calling this at all, and the digests that lack it are named in the message.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Any member of this run is held without an enforced retention floor.
    /// </exception>
    public AbsenceFamilyEnumerationProof? TryProveFamilyEnumeration(
        string familyKey,
        out AbsenceFamilyEnumerationProofRefusal refusal) =>
        AbsenceFamilyEnumerationProof.TryCreate(familyKey, RequireFlooredRun(), out refusal);

    /// <summary>
    /// The single place a digest becomes a member. It refuses a membership no write receipt can
    /// produce (so no such value ever reaches <see cref="Weakest"/>), and it refuses a second,
    /// different membership for a digest already recorded, which is the same one-object-one-floor
    /// rule this method applies between its two input maps. Every member goes through here, so
    /// there is no kind of member for which the rule quietly does not hold.
    /// </summary>
    private static bool Record(
        Dictionary<string, CustodyMembership> retained,
        string digest,
        CustodyMembership membership,
        out LuxembourgEnumerationReceiptRefusal refusal)
    {
        if (membership is not (CustodyMembership.RetainedUnenforced or CustodyMembership.Floored))
        {
            refusal = LuxembourgEnumerationReceiptRefusal.MembershipIsNotReceiptDerived;
            return false;
        }

        if (Disagrees(retained, digest, membership))
        {
            refusal = LuxembourgEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
            return false;
        }

        retained[digest] = membership;
        refusal = LuxembourgEnumerationReceiptRefusal.None;
        return true;
    }

    private static bool Disagrees(
        IReadOnlyDictionary<string, CustodyMembership> map, string digest, CustodyMembership membership) =>
        map.TryGetValue(digest, out var recorded) && recorded != membership;

    /// <summary>
    /// The weakest of two memberships, by an explicit comparison rather than <c>Enum.Min</c>, so a
    /// renumbering of <see cref="CustodyMembership"/> cannot silently invert which value reads as
    /// weaker. Total over the two values <see cref="Admit"/> lets through, and over nothing else:
    /// there is deliberately no <see cref="CustodyMembership.ReadOnce"/> case here, because a case
    /// no caller can reach is a case no test can kill.
    /// </summary>
    internal static CustodyMembership Weakest(CustodyMembership left, CustodyMembership right) =>
        left == CustodyMembership.Floored && right == CustodyMembership.Floored
            ? CustodyMembership.Floored
            : CustodyMembership.RetainedUnenforced;

    private static IEnumerable<RepeatedEnumerationEvidenceRefs> AllObservations(
        EnumerationDeliveryComparison delivery) =>
        new[] { delivery.CountA }
            .Concat(delivery.PagesA.Pages.Select(static page => page.Evidence))
            .Append(delivery.CountB)
            .Concat(delivery.PagesB.Pages.Select(static page => page.Evidence));
}
