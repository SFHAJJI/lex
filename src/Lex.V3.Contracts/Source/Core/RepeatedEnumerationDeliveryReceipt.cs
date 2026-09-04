using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// The resource identities of one observation, minted together and once. No public constructor, no
/// setter, so no caller reuses or reorders them. This is what <see cref="EnumerationDeliveryComparison"/>'s
/// own <c>RequireDistinct</c> defends; here it cannot be reached.
/// </summary>
/// <remarks>
/// Queue item 19: moved here, renamed, from <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgObservationIdentity</c>.
/// Nothing about this type ever named Luxembourg or any other publisher: four opaque minted URNs and
/// a factory, structurally identical for every publisher that reads request/response evidence back
/// out of custody by digest.
/// </remarks>
public sealed class RepeatedEnumerationObservationIdentity
{
    private RepeatedEnumerationObservationIdentity(
        string machinePlanResourceId,
        string inputResourceId,
        string logicalRequestResourceId,
        string httpEvidenceResourceId)
    {
        MachinePlanResourceId = machinePlanResourceId;
        InputResourceId = inputResourceId;
        LogicalRequestResourceId = logicalRequestResourceId;
        HttpEvidenceResourceId = httpEvidenceResourceId;
    }

    public static RepeatedEnumerationObservationIdentity NewObservation() => new(
        NewUrn(), NewUrn(), NewUrn(), NewUrn());

    public string MachinePlanResourceId { get; }

    public string InputResourceId { get; }

    public string LogicalRequestResourceId { get; }

    public string HttpEvidenceResourceId { get; }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}

/// <summary>
/// The four transport facts of one observation, each already read back out of custody by the
/// executor. Nothing here is a claim: every member is re-hashed by this namespace's own delivery
/// comparison against the reference minted beside it.
/// </summary>
/// <remarks>
/// Queue item 19: moved here, renamed, from <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgObservedTransport</c>.
/// </remarks>
public sealed record RepeatedEnumerationObservedTransport(
    HttpLogicalRequest LogicalRequest,
    RoutedHttpEvidence HttpEvidence,
    DurableBlobWriteReceipt DurableWriteReceipt,
    ReadOnlyMemory<byte> RetainedPayloadBytes);

/// <summary>
/// What one observation contributes to a run's custody beyond the five references
/// <see cref="RepeatedEnumerationEvidenceRefs"/> names.
/// </summary>
/// <remarks>
/// Queue item 19: moved here, renamed, from <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgObservationCustody</c>.
/// It exists so <see cref="RepeatedEnumerationDeliveryReceipt.TryCreate"/> can require the response
/// body of every observation without knowing anything about SPARQL or about any one publisher: the
/// receipt walks the refs the comparison exposes and looks each one up here, so an observation whose
/// body was left out is a refusal rather than a silently narrower floor.
/// </remarks>
public sealed record RepeatedEnumerationObservationCustody(
    RepeatedEnumerationEvidenceRefs References,
    string ResponseBodySha256,
    CustodyMembership ResponseBodyMembership,
    string DurableWriteReceiptSha256);

/// <summary>
/// Why <see cref="RepeatedEnumerationDeliveryReceipt.TryCreate"/> refused to mint a receipt. Closed.
/// </summary>
/// <remarks>
/// Queue item 19: moved here, renamed, from <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgEnumerationReceiptRefusal</c>.
/// The wire tokens are unchanged: every <see cref="JsonStringEnumMemberNameAttribute"/> value below
/// is byte-identical to the retired type's own.
/// </remarks>
public enum RepeatedEnumerationReceiptRefusal
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
/// One publisher partition's delivery, together with what this run can honestly say about the
/// custody of every artifact the delivery names.
/// </summary>
/// <remarks>
/// Queue item 19: moved here, renamed, from <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgEnumerationDeliveryReceipt</c>.
/// Nothing in this type's fields or logic ever named Luxembourg: every member is
/// <see cref="EnumerationDeliveryComparison"/>, custody membership maps, or plain digests, so the
/// same receipt shape now serves every publisher's executor.
/// </remarks>
public sealed class RepeatedEnumerationDeliveryReceipt
{
    private readonly IReadOnlyDictionary<string, CustodyMembership> _retainedMembership;
    private readonly IReadOnlyList<string> _unenforcedMemberDigests;

    private RepeatedEnumerationDeliveryReceipt(
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
    /// <see cref="RetainedFloor"/> read as Floored while the publisher's own response bytes, the
    /// only artifact whose loss cannot be repaired by re-deriving anything, sat unfloored.
    /// </remarks>
    public IReadOnlyDictionary<string, CustodyMembership> RetainedMembership => _retainedMembership;

    /// <summary>The weakest membership over every member. Never stronger than the worst of them.</summary>
    public CustodyMembership RetainedFloor { get; }

    /// <summary>Every written digest whose store published no enforcement. Empty iff Floored.</summary>
    public IReadOnlyList<string> UnenforcedMemberDigests => _unenforcedMemberDigests;

    /// <summary>
    /// Minted only from a comparison and the exact observations that produced it. There is no
    /// other input from which a receipt can be built: <see cref="EnumerationDeliveryComparison"/>
    /// has a private constructor whose only door is <c>Create</c>, and the caller's own delivery
    /// observation type has a private constructor whose only doors require a transport already
    /// bound to a real terminal hop. Holding one is the evidence.
    /// </summary>
    /// <param name="observationCustody">
    /// One entry per observation behind <paramref name="delivery"/>, in any order. Every reference
    /// set the delivery names must appear here with an identical <c>References</c> tuple, so a
    /// caller cannot pair one run's comparison with another run's bodies and cannot leave a body
    /// out to keep it off the floor; either refuses
    /// <see cref="RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld"/>.
    /// </param>
    public static RepeatedEnumerationDeliveryReceipt? TryCreate(
        EnumerationDeliveryComparison delivery,
        IReadOnlyDictionary<string, CustodyMembership> sessionArtifactMembership,
        IReadOnlyDictionary<string, CustodyMembership> executorWrittenMembership,
        IReadOnlyList<RepeatedEnumerationObservationCustody> observationCustody,
        out RepeatedEnumerationReceiptRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(sessionArtifactMembership);
        ArgumentNullException.ThrowIfNull(executorWrittenMembership);
        ArgumentNullException.ThrowIfNull(observationCustody);

        var byEvidenceDigest = new Dictionary<string, RepeatedEnumerationObservationCustody>(StringComparer.Ordinal);
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
                refusal = RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld;
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
                    refusal = RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                    return null;
                }

                if (executorWrittenMembership.TryGetValue(digest, out var conflicting) &&
                    conflicting != membership)
                {
                    refusal = RepeatedEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
                    return null;
                }

                if (!Record(retained, digest, membership, out refusal))
                {
                    return null;
                }
            }

            // The response body, classified from the write receipt the store issued for those exact
            // bytes. The caller's own delivery observation has already refused unless that receipt's
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
                refusal = RepeatedEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
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
                refusal = RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld;
                return null;
            }

            if (sessionArtifactMembership.TryGetValue(
                    references.HttpEvidenceRef.Sha256,
                    out var conflictingEvidence) &&
                conflictingEvidence != evidenceMembership)
            {
                refusal = RepeatedEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
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

        refusal = RepeatedEnumerationReceiptRefusal.None;
        return new RepeatedEnumerationDeliveryReceipt(
            delivery,
            retained,
            floor,
            Array.AsReadOnly(unenforced.ToArray()));
    }

    /// <summary>
    /// The only path from a delivery receipt to an absence enumeration proof, and so the only path
    /// to <see cref="AbsenceCut.TryCreateComplete"/>, which admits no family without one. It reads
    /// <see cref="Delivery"/> and STAMPS the proof with this run's own
    /// <see cref="RetainedFloor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RULING lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6. This used to read a
    /// <c>RequireFlooredRun</c> accessor that THREW naming every unenforced digest, so a run holding
    /// any member without an enforced floor could mint no proof at all. That reasoning was right
    /// about where durability is required and wrong about where it is checked. Refusing the proof
    /// refuses the family, and an adapter refuses a run with an unproven family, so an unfloored run
    /// ended before it ever reduced a manifest: a store that publishes no enforcement could acquire
    /// nothing, which is exactly what the Decision 71 interpretation removes. Proceeding without a
    /// proof was no better, since the manifest would then rest on nothing.
    /// </para>
    /// <para>
    /// So the proof is minted and CARRIES ITS CLASS. The run says which of the three each member is,
    /// and a run whose members are retained unenforced says so rather than saying durable. Durability
    /// is required at the one place that genuinely depends on it:
    /// <see cref="AbsenceCut.TryCreateComplete"/>, THE RELEASE, admits only a proof whose
    /// <see cref="AbsenceFamilyEnumerationProof.RetainedFloor"/> is
    /// <see cref="CustodyMembership.Floored"/>, and refuses with a typed member rather than throwing.
    /// The accessor that threw was removed with this change rather than left unreferenced: nothing
    /// asserts durability by calling it any more.
    /// </para>
    /// </remarks>
    public AbsenceFamilyEnumerationProof? TryProveFamilyEnumeration(
        string familyKey,
        out AbsenceFamilyEnumerationProofRefusal refusal) =>
        AbsenceFamilyEnumerationProof.TryCreate(familyKey, Delivery, RetainedFloor, out refusal);

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
        out RepeatedEnumerationReceiptRefusal refusal)
    {
        if (membership is not (CustodyMembership.RetainedUnenforced or CustodyMembership.Floored))
        {
            refusal = RepeatedEnumerationReceiptRefusal.MembershipIsNotReceiptDerived;
            return false;
        }

        if (Disagrees(retained, digest, membership))
        {
            refusal = RepeatedEnumerationReceiptRefusal.MembershipDisagreesOnADigest;
            return false;
        }

        retained[digest] = membership;
        refusal = RepeatedEnumerationReceiptRefusal.None;
        return true;
    }

    private static bool Disagrees(
        IReadOnlyDictionary<string, CustodyMembership> map, string digest, CustodyMembership membership) =>
        map.TryGetValue(digest, out var recorded) && recorded != membership;

    /// <summary>
    /// The weakest of two memberships, by an explicit comparison rather than <c>Enum.Min</c>, so a
    /// renumbering of <see cref="CustodyMembership"/> cannot silently invert which value reads as
    /// weaker. Total over the two values <c>Record</c> lets through, and over nothing else: there is
    /// deliberately no <see cref="CustodyMembership.ReadOnce"/> case here, because a case no caller
    /// can reach is a case no test can kill.
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
