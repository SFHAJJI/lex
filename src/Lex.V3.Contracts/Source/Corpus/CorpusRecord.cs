using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Corpus;

/// <summary>
/// The corpus/6 record's own schema identity.
/// </summary>
/// <remarks>
/// D1-06's queue split (<c>STAGE1-AUTHORITY-AND-QUEUE-2026-09-03.md</c>, "D1-06 split") and every
/// coordination reference to this deliverable name it "corpus/6" (for example the much larger
/// <c>lex-corpus/6</c> of the dual-channel licence design, the full signed corpus manifest set
/// Rebuild 0 emits for both publishers -- a different artifact this slice does not build). This
/// constant names the narrower thing D1-06a actually builds, one record per object, following this
/// codebase's own <c>lex-v3-source-&lt;domain&gt;/&lt;version&gt;</c> convention
/// (<see cref="ScopeManifestSchemaIds"/>, <see cref="SourceCoreSchemaIds"/>) while preserving the
/// version number 6 that "corpus/6" names everywhere the deliverable is discussed, rather than
/// restarting the version at 1.
/// </remarks>
public static class CorpusRecordSchemaIds
{
    /// <summary>
    /// Not <c>lex-corpus/6</c> (the dual-channel licence design's full signed corpus manifest set,
    /// see the type-level remarks above): a different, larger, separately owned artifact this
    /// constant's own value is never confused with, in code, by sharing a schema id.
    /// </summary>
    public const string Record = "lex-v3-source-corpus-record/6";
}

/// <summary>
/// Which of the three shapes a corpus record's own body field takes: a real held body, a typed
/// reason none is held, or an accepted body not yet acquired. Closed at three; see
/// <see cref="CorpusBodyRecord"/>.
/// </summary>
public enum CorpusBodyRecordKind
{
    [JsonStringEnumMemberName("held")]
    Held = 1,

    [JsonStringEnumMemberName("not_held")]
    NotHeld = 2,

    [JsonStringEnumMemberName("pending_acquisition")]
    PendingAcquisition = 3,
}

/// <summary>
/// Which of the two shapes <see cref="CorpusBodyRecord.PendingAcquisitionReason"/> takes: D1-06b's
/// own writer simply has not attempted a fetch yet for this accepted object (the only case D1-06b
/// itself ever produces, since no fetch capability exists in this codebase yet), or a future fetch
/// (D1-06c) ran and was refused. Closed at two.
/// </summary>
/// <remarks>
/// Named in this record's own reviewer verdict (event
/// <c>lex-event-20260904T071246618Z-2d4ca939f7144ea5ac3fd4c421091154</c>, fix three).
/// <see cref="AcquisitionRefused"/>'s own reason is <see cref="CorpusAcquisitionRefusalReason"/>, a
/// closed enum rather than the free-form string this type originally carried; see that type's own
/// remarks for why and for its exact members.
/// </remarks>
public enum CorpusBodyPendingAcquisitionReasonKind
{
    [JsonStringEnumMemberName("not_yet_acquired")]
    NotYetAcquired = 1,

    [JsonStringEnumMemberName("acquisition_refused")]
    AcquisitionRefused = 2,
}

/// <summary>
/// The closed cause vocabulary for <see cref="CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused"/>.
/// Closed at twenty-two: the original fourteen members, one for every cause named in
/// <see cref="Lex.V3.Contracts.Source.Http.HttpAcquisitionReasonRegistry"/>, plus D1-06c-EU fix one's
/// own widening (SCOPE_RULING lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93 item 1)
/// to eight more: the EU document-fetch route's own three named shapes
/// (<see cref="RequestedRepresentationNotServed"/>, <see cref="WrongAcceptToken"/>,
/// <see cref="RedirectTargetOriginNotAdmitted"/>), plus five more added in the same widening for the
/// LU-2 lane's own document-get route so it needs no second schema/enum touch when it lands. Two of
/// those five are no longer merely reserved: defect nine's own fold-in one gave the EU route a
/// per-object mapping for each, so <c>EuQueryExecutionAdapter</c> produces
/// <see cref="RobotsDisallowed"/> and <see cref="UnexpectedPublisherStatus"/> today and LU-2 will
/// produce them too. The three that remain reserved and produced by no adapter are
/// <see cref="NotFound"/>, <see cref="Gone"/> and <see cref="RetryExhausted"/>.
/// </summary>
/// <remarks>
/// The registry (Source/Http, out of this slice's own path claim -- read, never touched, per
/// D1-06b's scope ruling) is read, not touched: every one of its causes is plausible for a future
/// document-body fetch (D1-06c), since none of them are specific to the two SPARQL query endpoints
/// <c>RoutedHttpAcquisitionSession</c> sends today. This mirrors, rather than duplicates, that
/// registry's own four grouped enums
/// (<see cref="Lex.V3.Contracts.Source.Http.HttpPartialBodyReason"/>,
/// <see cref="Lex.V3.Contracts.Source.Http.HttpCompletionUnprovenReason"/>,
/// <see cref="Lex.V3.Contracts.Source.Http.HttpPreHeaderFailureClass"/>,
/// <see cref="Lex.V3.Contracts.Source.Http.HttpResponseSemanticsReason"/>): each of the first fourteen
/// members below shares its name and wire spelling with exactly the registry member it names, cited
/// by name/value in its own doc comment rather than by widening that file's own visibility. D1-06b's
/// own writer never produces this vocabulary itself (it has no fetch to refuse); it exists so the
/// wire shape is ready for D1-06c's real refusal without another breaking change to this record.
/// <para>
/// The eight members added by fix one mirror the EU and (reserved) LU-2 document routes' own closed
/// refusals the identical way: shared name, shared wire spelling. The three EU members are actually
/// produced today, by <c>EuQueryExecutionAdapter</c>'s own document-fetch classification (see that
/// type's own remarks on <c>TryMapDocumentFetchToCorpusAcquisitionRefusal</c>). The five LU-2 members
/// are not produced by any adapter in this codebase yet -- there is no LU-2 lane here to produce
/// them -- and are named directly from the ruling's own wire spellings rather than copied from a
/// <c>LuxembourgDocumentGetOutcomeKind</c> this worktree does not contain; LU-2 should confirm its own
/// closed vocabulary agrees with these five spellings when it lands, exactly as this remark already
/// says.
/// </para>
/// </remarks>
public enum CorpusAcquisitionRefusalReason
{
    /// <summary>Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPartialBodyReason.BodyDeadline"/>.</summary>
    [JsonStringEnumMemberName("body_deadline")]
    BodyDeadline = 1,

    /// <summary>Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPartialBodyReason.BodyReadFailure"/>.</summary>
    [JsonStringEnumMemberName("body_read_failure")]
    BodyReadFailure = 2,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPartialBodyReason.ByteBoundPreventedCompletion"/>.
    /// </summary>
    [JsonStringEnumMemberName("byte_bound_prevented_completion")]
    ByteBoundPreventedCompletion = 3,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPartialBodyReason.CallerCancelledAfterHeaders"/>.
    /// </summary>
    [JsonStringEnumMemberName("caller_cancelled_after_headers")]
    CallerCancelledAfterHeaders = 4,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPartialBodyReason.DeclaredLengthShortRead"/>.
    /// </summary>
    [JsonStringEnumMemberName("declared_length_short_read")]
    DeclaredLengthShortRead = 5,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpCompletionUnprovenReason.MissingCompletionProof"/>.
    /// </summary>
    [JsonStringEnumMemberName("missing_completion_proof")]
    MissingCompletionProof = 6,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpCompletionUnprovenReason.TransferCodingConflict"/>.
    /// </summary>
    [JsonStringEnumMemberName("transfer_coding_conflict")]
    TransferCodingConflict = 7,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpCompletionUnprovenReason.InvalidContentLength"/>.
    /// </summary>
    [JsonStringEnumMemberName("invalid_content_length")]
    InvalidContentLength = 8,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpCompletionUnprovenReason.UnsupportedTransferCoding"/>.
    /// </summary>
    [JsonStringEnumMemberName("unsupported_transfer_coding")]
    UnsupportedTransferCoding = 9,

    /// <summary>Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPreHeaderFailureClass.HeaderDeadline"/>.</summary>
    [JsonStringEnumMemberName("header_deadline")]
    HeaderDeadline = 10,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpPreHeaderFailureClass.TransportBeforeHeaders"/>.
    /// </summary>
    [JsonStringEnumMemberName("transport_before_headers")]
    TransportBeforeHeaders = 11,

    /// <summary>
    /// Mirrors
    /// <see cref="Lex.V3.Contracts.Source.Http.HttpResponseSemanticsReason.RevalidationRequestNotAdmitted"/>.
    /// </summary>
    [JsonStringEnumMemberName("revalidation_request_not_admitted")]
    RevalidationRequestNotAdmitted = 12,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpResponseSemanticsReason.StatusContentForbidden"/>.
    /// </summary>
    [JsonStringEnumMemberName("status_content_forbidden")]
    StatusContentForbidden = 13,

    /// <summary>
    /// Mirrors <see cref="Lex.V3.Contracts.Source.Http.HttpResponseSemanticsReason.StatusFramingConflict"/>.
    /// </summary>
    [JsonStringEnumMemberName("status_framing_conflict")]
    StatusFramingConflict = 14,

    /// <summary>
    /// D1-06c-EU fix one. Mirrors
    /// <see cref="Lex.V3.Contracts.Source.Europe.EuDocumentFetchRefusal.RequestedRepresentationNotServed"/>,
    /// the EU document-fetch route's own 404 business refusal: this exact representation was not
    /// served for this object. Produced today by <c>EuQueryExecutionAdapter</c>.
    /// </summary>
    [JsonStringEnumMemberName("requested_representation_not_served")]
    RequestedRepresentationNotServed = 15,

    /// <summary>
    /// D1-06c-EU fix one. Mirrors
    /// <see cref="Lex.V3.Contracts.Source.Europe.EuDocumentFetchRefusal.WrongAcceptToken"/>, the EU
    /// document-fetch route's own 400 business refusal. Produced today by
    /// <c>EuQueryExecutionAdapter</c>.
    /// </summary>
    [JsonStringEnumMemberName("wrong_accept_token")]
    WrongAcceptToken = 16,

    /// <summary>
    /// D1-06c-EU fix one. Mirrors
    /// <see cref="Lex.V3.Contracts.Source.Http.HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted"/>:
    /// a well-formed absolute-HTTPS redirect target on a different origin than this route's own first
    /// hop. Produced today by <c>EuQueryExecutionAdapter</c>.
    /// </summary>
    [JsonStringEnumMemberName("redirect_target_origin_not_admitted")]
    RedirectTargetOriginNotAdmitted = 17,

    /// <summary>
    /// Added by D1-06c-EU fix one for the LU-2 lane, and produced today after all: defect nine's own
    /// fold-in one maps a robots bootstrap refusal on one object's document fetch to this cause
    /// (<c>EuQueryExecutionAdapter</c> line 1198) rather than refusing the whole run. LU-2's own
    /// document-get route will produce it too, and should confirm this wire spelling against its own
    /// closed vocabulary when it lands, since the spelling here was named from the ruling's own text
    /// rather than copied from an LU-2 enum this worktree does not contain.
    /// </summary>
    [JsonStringEnumMemberName("robots_disallowed")]
    RobotsDisallowed = 18,

    /// <summary>
    /// D1-06c-EU fix one, reserved for the LU-2 lane's own document-get route landing in this same
    /// widening so it needs no second schema/enum touch when it lands. Not produced by any adapter in
    /// this codebase today; named from the ruling's own wire spelling, not copied from an LU-2 enum
    /// this worktree does not contain -- LU-2 should confirm this spelling against its own closed
    /// vocabulary when it lands.
    /// </summary>
    [JsonStringEnumMemberName("not_found")]
    NotFound = 19,

    /// <summary>Reserved for LU-2, same note as <see cref="NotFound"/>.</summary>
    [JsonStringEnumMemberName("gone")]
    Gone = 20,

    /// <summary>
    /// Reserved for LU-2, same note as <see cref="NotFound"/>. Deliberately still unproduced by the
    /// EU route: defect nine's own fold-in two asked for a retry-exhaustion mapping here, and the
    /// repair refused it because this route has no retry-shaped signal to map (neither
    /// <c>EuDocumentFetchAttemptRefusal</c> nor <c>HttpRouteIncompleteReason</c> carries one), so a
    /// mapping would have named a cause no code path can reach. Ratified by RULING
    /// lex-event-20260904T163100119Z-bfe97e59d2ef46fb974389cdd4e20d0f.
    /// </summary>
    [JsonStringEnumMemberName("retry_exhausted")]
    RetryExhausted = 21,

    /// <summary>
    /// Added by D1-06c-EU fix one for the LU-2 lane, and produced today after all: defect nine's own
    /// fold-in one maps a document fetch that completed for real at a terminal status this route has
    /// no reviewed reading for (anything but 200, 400 or 404) to this cause
    /// (<c>EuQueryExecutionAdapter</c> line 1305) rather than refusing the whole run. LU-2 will
    /// produce it too, with the same spelling caveat as <see cref="RobotsDisallowed"/>.
    /// </summary>
    [JsonStringEnumMemberName("unexpected_publisher_status")]
    UnexpectedPublisherStatus = 22,
}

/// <summary>
/// The typed reason a <see cref="CorpusBodyRecordKind.PendingAcquisition"/> body carries no body
/// yet: an exact variant, <see cref="CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired"/> with no
/// further detail, or <see cref="CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused"/> naming
/// the actual refusal from the closed <see cref="CorpusAcquisitionRefusalReason"/> vocabulary.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusBodyPendingAcquisitionReason
{
    [JsonConstructor]
    public CorpusBodyPendingAcquisitionReason(
        CorpusBodyPendingAcquisitionReasonKind kind,
        CorpusAcquisitionRefusalReason? refusal)
    {
        Kind = ContractValidation.RequireDefined(kind, nameof(kind));
        switch (Kind)
        {
            case CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired:
                if (refusal is not null)
                {
                    throw new ArgumentException(
                        "A not-yet-acquired reason carries no refusal.", nameof(refusal));
                }

                break;

            case CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused:
                if (refusal is null)
                {
                    throw new ArgumentException(
                        "An acquisition-refused reason must name the actual refusal.",
                        nameof(refusal));
                }

                ContractValidation.RequireDefined(refusal.Value, nameof(refusal));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Refusal = refusal;
    }

    public CorpusBodyPendingAcquisitionReasonKind Kind { get; }

    /// <summary>
    /// The actual refusal named, for
    /// <see cref="CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused"/> only;
    /// <see langword="null"/> for <see cref="CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired"/>.
    /// </summary>
    public CorpusAcquisitionRefusalReason? Refusal { get; }

    public static CorpusBodyPendingAcquisitionReason NotYetAcquired() =>
        new(CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired, null);

    public static CorpusBodyPendingAcquisitionReason AcquisitionRefused(
        CorpusAcquisitionRefusalReason refusal) =>
        new(CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused, refusal);
}

/// <summary>
/// One corpus/6 record's own body: a real held body, receipted and floored; the typed reason none
/// is held; or an accepted body pending acquisition. An exact variant, validated in the constructor
/// exactly as <see cref="ScopeSelectorEvidence"/> validates its own four states, never a free-form
/// set of nullable fields a caller could set inconsistently.
/// </summary>
/// <remarks>
/// <para>
/// The "no body held" reason is not a new vocabulary. It IS <see cref="ScopeDisposition"/>,
/// restricted here to its three non-<see cref="ScopeDisposition.AcceptedSelected"/> members --
/// exactly the closed set the two merged adapters actually classify a body's exclusion as today.
/// <c>EuScopeProfile.ReduceBody</c>'s four independent contributions (channel, language, format,
/// rights) each resolve to one of <see cref="ScopeDisposition.TypedQuarantine"/>,
/// <see cref="ScopeDisposition.Point"/> or <see cref="ScopeDisposition.NeverIngest"/> whenever they
/// do not admit a body, and the axis result is the worst of the four under
/// <see cref="ScopeDisposition"/>'s own declared order. Luxembourg's own body join (behind
/// <c>LuxembourgScopeResolver</c>, out of this slice's path claim) publishes into the same shared
/// <see cref="ScopeManifest"/> body axis, so it is bound by the same closed three-member set by
/// construction of the manifest schema itself: <see cref="ScopeManifestCanonicalWriter"/>'s own
/// body-candidate projection recognises exactly these four disposition values for every axis, and
/// there is no fifth. Building a second, corpus-specific reason enum alongside it would either
/// duplicate this vocabulary or silently narrow it; this type reuses it directly instead, per the
/// SCOPE_RULING's first precision. <see cref="CorpusRecord"/>'s own constructor (fix two of the
/// corpus/6 verdict, event <c>lex-event-20260904T071246618Z-2d4ca939f7144ea5ac3fd4c421091154</c>)
/// additionally refuses to hold a not-held body together with a
/// <see cref="CorpusRecord.BodyDisposition"/> the reason disagrees with, since only that type
/// carries both sides of the comparison.
/// </para>
/// <para>
/// <see cref="CorpusBodyRecordKind.PendingAcquisition"/>: an object whose body axis is
/// <see cref="ScopeDisposition.AcceptedSelected"/> but whose body has not (yet) been acquired or
/// receipted. The peer reviewer verdict on this slice (fix three, same event as above) named this
/// the state every accepted object passes through in D1-06b whenever the fetch has not happened or
/// was refused, and required a typed reason distinguishing the two:
/// <see cref="CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired"/> and
/// <see cref="CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused"/>. Still no receipt and no
/// floor -- both remain exactly what a real held body proves, never asserted independently -- and no
/// not-held reason, because the body axis here is accepted, not one of the three exclusion
/// dispositions.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusBodyRecord
{
    [JsonConstructor]
    public CorpusBodyRecord(
        CorpusBodyRecordKind kind,
        DurableBlobWriteReceipt? receipt,
        CustodyMembership? floor,
        ScopeDisposition? notHeldReason,
        CorpusBodyPendingAcquisitionReason? pendingAcquisitionReason)
    {
        Kind = ContractValidation.RequireDefined(kind, nameof(kind));

        switch (Kind)
        {
            case CorpusBodyRecordKind.Held:
                if (receipt is null)
                {
                    throw new ArgumentException(
                        "A held body record requires its own receipt.", nameof(receipt));
                }

                if (notHeldReason is not null)
                {
                    throw new ArgumentException(
                        "A held body record carries no not-held reason.", nameof(notHeldReason));
                }

                if (pendingAcquisitionReason is not null)
                {
                    throw new ArgumentException(
                        "A held body record carries no pending-acquisition reason.",
                        nameof(pendingAcquisitionReason));
                }

                // The floor is never an independently supplied fact: it is exactly what
                // CustodyMembershipClassifier derives from the receipt's own policy evidence, the
                // one rule Decision 71 fixes in one place. A caller-supplied value that disagreed
                // with the receipt it names would let a corpus record claim a floor its own
                // evidence does not prove.
                var actualFloor = CustodyMembershipClassifier.Classify(receipt);
                if (floor != actualFloor)
                {
                    throw new ArgumentException(
                        $"A held body record's floor must be exactly {actualFloor} for this " +
                        "receipt, never an independently supplied value.",
                        nameof(floor));
                }

                break;

            case CorpusBodyRecordKind.NotHeld:
                if (receipt is not null)
                {
                    throw new ArgumentException(
                        "A not-held body record carries no receipt.", nameof(receipt));
                }

                if (floor is not null)
                {
                    throw new ArgumentException(
                        "A not-held body record carries no floor.", nameof(floor));
                }

                if (pendingAcquisitionReason is not null)
                {
                    throw new ArgumentException(
                        "A not-held body record carries no pending-acquisition reason.",
                        nameof(pendingAcquisitionReason));
                }

                if (notHeldReason is not (ScopeDisposition.TypedQuarantine or ScopeDisposition.Point
                        or ScopeDisposition.NeverIngest))
                {
                    throw new ArgumentException(
                        "A not-held reason must be one of the manifest's three non-accepted body " +
                        "dispositions: typed_quarantine, point or never_ingest.",
                        nameof(notHeldReason));
                }

                break;

            case CorpusBodyRecordKind.PendingAcquisition:
                if (receipt is not null)
                {
                    throw new ArgumentException(
                        "A pending-acquisition body record carries no receipt.", nameof(receipt));
                }

                if (floor is not null)
                {
                    throw new ArgumentException(
                        "A pending-acquisition body record carries no floor.", nameof(floor));
                }

                if (notHeldReason is not null)
                {
                    throw new ArgumentException(
                        "A pending-acquisition body record carries no not-held reason.",
                        nameof(notHeldReason));
                }

                if (pendingAcquisitionReason is null)
                {
                    throw new ArgumentException(
                        "A pending-acquisition body record requires its own typed reason.",
                        nameof(pendingAcquisitionReason));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Receipt = receipt;
        Floor = floor;
        NotHeldReason = notHeldReason;
        PendingAcquisitionReason = pendingAcquisitionReason;
    }

    public CorpusBodyRecordKind Kind { get; }

    public DurableBlobWriteReceipt? Receipt { get; }

    public CustodyMembership? Floor { get; }

    public ScopeDisposition? NotHeldReason { get; }

    /// <summary>
    /// The typed acquisition state, for <see cref="CorpusBodyRecordKind.PendingAcquisition"/> only.
    /// </summary>
    public CorpusBodyPendingAcquisitionReason? PendingAcquisitionReason { get; }

    /// <summary>A held body, receipted through <paramref name="receipt"/>. The floor is derived, never asserted.</summary>
    public static CorpusBodyRecord Held(DurableBlobWriteReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new CorpusBodyRecord(
            CorpusBodyRecordKind.Held,
            receipt,
            CustodyMembershipClassifier.Classify(receipt),
            null,
            null);
    }

    /// <summary>No body held, for one of the manifest's three non-accepted body dispositions.</summary>
    public static CorpusBodyRecord NotHeld(ScopeDisposition reason) =>
        new(CorpusBodyRecordKind.NotHeld, null, null, reason, null);

    /// <summary>
    /// An accepted body not yet acquired: the body axis is
    /// <see cref="ScopeDisposition.AcceptedSelected"/> but D1-06b has not (yet) produced a receipt
    /// for it, or its own fetch was refused.
    /// </summary>
    public static CorpusBodyRecord PendingAcquisition(CorpusBodyPendingAcquisitionReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new CorpusBodyRecord(
            CorpusBodyRecordKind.PendingAcquisition, null, null, null, reason);
    }
}

/// <summary>
/// The corpus/6 record: the final, point-in-time record for one object in scope, built from either
/// publisher's own scope manifest (D1-06 split, <c>STAGE1-AUTHORITY-AND-QUEUE-2026-09-03.md</c>,
/// SCOPE_RULING <c>lex-event-20260904T062043029Z-b0bb5529327b49a197ddad0dce54bbc8</c>). One record
/// type for both publishers: <see cref="ObjectRef"/> IS <see cref="SourceObjectRef"/> and every
/// disposition below IS <see cref="ScopeDisposition"/>, the manifest's own types, reused directly
/// rather than wrapped in a corpus-specific identity or disposition vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// This type is fixtures-only, contracts-only work (D1-06a). Nothing here calls a custody store,
/// an adapter, or a live manifest; D1-06b is the writer that produces a real instance from a real
/// run, floored and reopened through a checked read.
/// </para>
/// <para>
/// Fix one of the peer reviewer verdict on this slice (event
/// <c>lex-event-20260904T071246618Z-2d4ca939f7144ea5ac3fd4c421091154</c>): the first committed shape
/// of this type kept only <see cref="RecordDisposition"/>, one <see cref="ScopeDisposition"/> for
/// the manifest's <c>ScopeAxis.Record</c> alone, and dropped the other three axes
/// <c>ScopeManifestRow</c> carries, with no ordinal or row reference back into the manifest -- a
/// reader could not check the record against the manifest it claimed to come from. This type now
/// carries <see cref="ObjectOrdinal"/> (the exact row) plus all four axis outcomes
/// (<see cref="RecordDisposition"/>, <see cref="BodyDisposition"/>, <see cref="RelationDisposition"/>,
/// <see cref="SupportingDocumentDisposition"/>), each the same <see cref="ScopeDisposition"/> the
/// manifest itself declares, so the record is the manifest's own statement rather than a claim
/// about it. <c>ScopeManifest.cs</c> itself has no single named type that bundles exactly these
/// four per-row disposition values alone: its <c>ScopeManifestRow</c> instead carries
/// <c>AxisWinningRuleOrdinals</c> plus the matched-rule table (its compact persisted form), and the
/// richer <see cref="ScopeAxisResult"/> used for live/expanded reduction bundles the winning rule
/// ordinal and its role/capability members alongside the disposition. That is named here as a small
/// manifest-side gap rather than invented casually, since this type's own path claim is
/// Source/Corpus, not Source/Scope.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusRecord
{
    [JsonConstructor]
    public CorpusRecord(
        string schema,
        SourceObjectRef objectRef,
        int objectOrdinal,
        ScopeDisposition recordDisposition,
        ScopeDisposition bodyDisposition,
        ScopeDisposition relationDisposition,
        ScopeDisposition supportingDocumentDisposition,
        CorpusBodyRecord body,
        SourceArtifactRef manifestRef,
        SourceArtifactRef runIdentity)
    {
        if (!string.Equals(schema, CorpusRecordSchemaIds.Record, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A corpus record must declare {CorpusRecordSchemaIds.Record}.", nameof(schema));
        }

        Schema = schema;
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        ObjectOrdinal = ScopeValidation.RequireOrdinal(objectOrdinal, nameof(objectOrdinal));
        RecordDisposition = ContractValidation.RequireDefined(
            recordDisposition, nameof(recordDisposition));
        BodyDisposition = ContractValidation.RequireDefined(
            bodyDisposition, nameof(bodyDisposition));
        RelationDisposition = ContractValidation.RequireDefined(
            relationDisposition, nameof(relationDisposition));
        SupportingDocumentDisposition = ContractValidation.RequireDefined(
            supportingDocumentDisposition, nameof(supportingDocumentDisposition));
        Body = body ?? throw new ArgumentNullException(nameof(body));

        // Fix two of the peer reviewer verdict (same event as above): the body can never disagree
        // with the manifest's own body axis this record now carries. A caller could still build a
        // CorpusBodyRecord.NotHeld with any of the three reasons independently -- that type alone
        // cannot see the manifest -- so this is the one place both sides of the comparison are ever
        // held together, and it refuses to hold them apart.
        if (BodyDisposition == ScopeDisposition.AcceptedSelected)
        {
            if (Body.Kind == CorpusBodyRecordKind.NotHeld)
            {
                throw new ArgumentException(
                    "A record whose body axis is accepted_selected cannot carry a not-held body.",
                    nameof(body));
            }
        }
        else if (Body.Kind != CorpusBodyRecordKind.NotHeld || Body.NotHeldReason != BodyDisposition)
        {
            throw new ArgumentException(
                $"A record whose body axis is {BodyDisposition} must carry a not-held body whose " +
                "reason is exactly that disposition.",
                nameof(body));
        }

        ManifestRef = manifestRef ?? throw new ArgumentNullException(nameof(manifestRef));
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
    }

    public string Schema { get; }

    /// <summary>The object's own identity: the manifest's <see cref="SourceObjectRef"/>, reused directly.</summary>
    public SourceObjectRef ObjectRef { get; }

    /// <summary>
    /// This object's ordinal in the manifest named by <see cref="ManifestRef"/>: exactly
    /// <c>ScopeManifestRow.ObjectOrdinal</c> for the row this record was built from, so a reader
    /// holding that manifest can locate the exact row and check every disposition below against it.
    /// </summary>
    public int ObjectOrdinal { get; }

    /// <summary>
    /// The object's own disposition from the manifest: the record axis's outcome, the same closed
    /// <see cref="ScopeDisposition"/> the manifest itself carries for <c>ScopeAxis.Record</c>.
    /// Independent of <see cref="Body"/> and the other two axes below: the manifest's own four axes
    /// are evaluated independently of one another (an object can be an accepted record whose body
    /// axis is excluded, or vice versa).
    /// </summary>
    public ScopeDisposition RecordDisposition { get; }

    /// <summary>
    /// The manifest row's own <c>ScopeAxis.Body</c> outcome. <see cref="Body"/> is constructed to
    /// agree with this value exactly: <see cref="ScopeDisposition.AcceptedSelected"/> requires
    /// <see cref="CorpusBodyRecord.Held"/> or <see cref="CorpusBodyRecord.PendingAcquisition"/>; any
    /// other value requires <see cref="CorpusBodyRecord.NotHeld"/> with that same value as the
    /// reason. This is fix two of the corpus/6 verdict: a caller cannot make this record say
    /// <see cref="ScopeDisposition.Point"/> for a body the manifest actually excluded as
    /// <see cref="ScopeDisposition.TypedQuarantine"/>, because the constructor above refuses to
    /// hold the two together.
    /// </summary>
    public ScopeDisposition BodyDisposition { get; }

    /// <summary>The manifest row's own <c>ScopeAxis.Relation</c> outcome, carried exactly as stated.</summary>
    public ScopeDisposition RelationDisposition { get; }

    /// <summary>
    /// The manifest row's own <c>ScopeAxis.SupportingDocument</c> outcome, carried exactly as
    /// stated.
    /// </summary>
    public ScopeDisposition SupportingDocumentDisposition { get; }

    /// <summary>The receipted document, the typed reason none is held, or the pending-acquisition state.</summary>
    public CorpusBodyRecord Body { get; }

    /// <summary>Which manifest artifact this record was built from.</summary>
    public SourceArtifactRef ManifestRef { get; }

    /// <summary>
    /// Which run produced this record, named and typed the same way every other acquisition-run
    /// identity in this codebase is (for example <c>RepeatedEnumerationDeliveryProof.RunIdentity</c>,
    /// <c>AbsenceFamilyEnumerationProof</c>'s <c>AcquisitionRunRef</c>): a <see cref="SourceArtifactRef"/>,
    /// never a corpus-specific run-identity type.
    /// </summary>
    public SourceArtifactRef RunIdentity { get; }
}

/// <summary>
/// The reader door for a corpus record previously durably written by
/// <see cref="CorpusRecordCanonicalWriter.Write"/>, shaped like
/// <see cref="VerifiedScopeManifest.ParseAndVerify"/>: verifies the SHA-256 against
/// <paramref name="artifactRef"/> before any content is exposed, then requires the parsed record's
/// own canonical re-serialization to reproduce the input bytes exactly. No InternalsVisibleTo is
/// granted for this door; D1-06b will reopen corpus records from custody the same way the adapters
/// already reopen manifests and rows.
/// </summary>
/// <remarks>
/// Unlike <see cref="VerifiedScopeManifest.ParseAndVerify"/> this door takes no evidence resolver:
/// a <see cref="CorpusRecord"/> carries no row that must be checked against externally supplied
/// observation evidence the way a scope manifest's rows must. Its own constructor already proves
/// every invariant this type has (the exact body variant, the floor derived from its own receipt),
/// so byte integrity against <paramref name="artifactRef"/> plus exact canonical round-tripping is
/// the whole of what this door needs to re-verify.
/// </remarks>
public sealed class VerifiedCorpusRecord
{
    internal VerifiedCorpusRecord(CorpusRecord record)
    {
        Record = record;
    }

    /// <summary>
    /// The verified record's own content. Reading it needs no InternalsVisibleTo; holding an
    /// instance is itself the evidence that <see cref="ParseAndVerify"/> ran to completion, because
    /// the constructor above is the only door onto this type and it stays internal.
    /// </summary>
    public CorpusRecord Record { get; }

    public static VerifiedCorpusRecord ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);

        if (!string.Equals(
                CorpusRecordCanonicalWriter.ComputeRecordSha256(canonicalBytes),
                artifactRef.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The corpus record bytes do not match their artifact reference.",
                nameof(canonicalBytes));
        }

        var json = ScopeValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
        CorpusRecord record;
        try
        {
            record = ContractJson.Deserialize<CorpusRecord>(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The corpus record bytes are not one valid typed canonical document.",
                nameof(canonicalBytes),
                exception);
        }

        using var rebuilt = new MemoryStream();
        CorpusRecordCanonicalWriter.Write(rebuilt, record);
        if (!canonicalBytes.SequenceEqual(rebuilt.ToArray()))
        {
            throw new ArgumentException(
                "The corpus record is not its exact canonical typed representation.",
                nameof(canonicalBytes));
        }

        return new VerifiedCorpusRecord(record);
    }
}

/// <summary>
/// Canonicalizes a <see cref="CorpusRecord"/> into deterministic UTF-8 bytes and their SHA-256,
/// domain-separated exactly the way <see cref="ScopeManifestCanonicalWriter"/> domain-separates its
/// own digest: a fixed ASCII domain string is hashed ahead of the document bytes, so a corpus
/// record and a scope manifest that happened to serialize to the same JSON bytes could never
/// collide on one digest.
/// </summary>
public static class CorpusRecordCanonicalWriter
{
    private const string RecordDomain = "lex-v3-source-corpus-record/6\n";

    public static string Write(Stream destination, CorpusRecord record)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(record);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The canonical destination must be writable.", nameof(destination));
        }

        using var buffer = new MemoryStream();
        using (var writer = NewWriter(buffer))
        {
            WriteRecord(writer, record);
            writer.Flush();
        }

        buffer.WriteByte((byte)'\n');
        var bytes = buffer.ToArray();
        destination.Write(bytes, 0, bytes.Length);
        return ComputeRecordSha256(bytes);
    }

    /// <summary>
    /// The exact digest <see cref="Write"/> returns for its own output, recomputed directly from
    /// durable bytes so a reader can check them against a pinned artifact reference before parsing.
    /// <paramref name="canonicalBytes"/> must be exactly what <see cref="Write"/> wrote, trailing
    /// newline included.
    /// </summary>
    internal static string ComputeRecordSha256(ReadOnlySpan<byte> canonicalBytes)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        incremental.AppendData(Encoding.ASCII.GetBytes(RecordDomain));
        incremental.AppendData(canonicalBytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        incremental.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static Utf8JsonWriter NewWriter(Stream output) => new(
        output,
        new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        });

    // Internal, not private: CorpusRecordSetCanonicalWriter (same file, same assembly) reuses this
    // exact per-record encoding to embed each record inline in a record set's own canonical bytes,
    // so a set's own digest can never drift from what CorpusRecordCanonicalWriter.Write itself
    // produces for the identical record standing alone.
    internal static void WriteRecord(Utf8JsonWriter writer, CorpusRecord record)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", CorpusRecordSchemaIds.Record);
        writer.WritePropertyName("object_ref");
        ScopeManifestCanonicalWriter.WriteObjectRef(writer, record.ObjectRef);
        writer.WriteNumber("object_ordinal", record.ObjectOrdinal);
        writer.WriteString("record_disposition", DispositionName(record.RecordDisposition));
        writer.WriteString("body_disposition", DispositionName(record.BodyDisposition));
        writer.WriteString("relation_disposition", DispositionName(record.RelationDisposition));
        writer.WriteString(
            "supporting_document_disposition",
            DispositionName(record.SupportingDocumentDisposition));
        writer.WritePropertyName("body");
        WriteBody(writer, record.Body);
        writer.WritePropertyName("manifest_ref");
        ScopeManifestCanonicalWriter.WriteArtifact(writer, record.ManifestRef);
        writer.WritePropertyName("run_identity");
        ScopeManifestCanonicalWriter.WriteArtifact(writer, record.RunIdentity);
        writer.WriteEndObject();
    }

    private static void WriteBody(Utf8JsonWriter writer, CorpusBodyRecord body)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", BodyKindName(body.Kind));
        writer.WritePropertyName("receipt");
        if (body.Receipt is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            // DurableBlobWriteReceiptDigest already treats ContractJson.Serialize's output as this
            // receipt's own canonical form (it is exactly what that digest hashes); embedding it
            // here rather than hand-writing the receipt's fields a second time keeps the two
            // canonicalizations from being able to drift apart.
            writer.WriteRawValue(ContractJson.Serialize(body.Receipt), skipInputValidation: true);
        }

        writer.WritePropertyName("floor");
        if (body.Floor is { } floor)
        {
            writer.WriteStringValue(FloorName(floor));
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WritePropertyName("not_held_reason");
        if (body.NotHeldReason is { } reason)
        {
            writer.WriteStringValue(DispositionName(reason));
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WritePropertyName("pending_acquisition_reason");
        if (body.PendingAcquisitionReason is { } pending)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", PendingAcquisitionReasonKindName(pending.Kind));
            writer.WritePropertyName("refusal");
            if (pending.Refusal is { } refusal)
            {
                writer.WriteStringValue(RefusalReasonName(refusal));
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteEndObject();
    }

    private static string BodyKindName(CorpusBodyRecordKind kind) => kind switch
    {
        CorpusBodyRecordKind.Held => "held",
        CorpusBodyRecordKind.NotHeld => "not_held",
        CorpusBodyRecordKind.PendingAcquisition => "pending_acquisition",
        _ => throw new InvalidOperationException("Unknown corpus body record kind."),
    };

    private static string PendingAcquisitionReasonKindName(
        CorpusBodyPendingAcquisitionReasonKind kind) => kind switch
    {
        CorpusBodyPendingAcquisitionReasonKind.NotYetAcquired => "not_yet_acquired",
        CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused => "acquisition_refused",
        _ => throw new InvalidOperationException("Unknown pending-acquisition reason kind."),
    };

    // Every wire spelling here is exactly the member_key HttpAcquisitionReasonRegistry.cs's own
    // CanonicalArtifact literal already uses for the mirrored cause (Source/Http, read, not
    // touched); see CorpusAcquisitionRefusalReason's own remarks for the full cross-reference.
    private static string RefusalReasonName(CorpusAcquisitionRefusalReason refusal) => refusal switch
    {
        CorpusAcquisitionRefusalReason.BodyDeadline => "body_deadline",
        CorpusAcquisitionRefusalReason.BodyReadFailure => "body_read_failure",
        CorpusAcquisitionRefusalReason.ByteBoundPreventedCompletion => "byte_bound_prevented_completion",
        CorpusAcquisitionRefusalReason.CallerCancelledAfterHeaders => "caller_cancelled_after_headers",
        CorpusAcquisitionRefusalReason.DeclaredLengthShortRead => "declared_length_short_read",
        CorpusAcquisitionRefusalReason.MissingCompletionProof => "missing_completion_proof",
        CorpusAcquisitionRefusalReason.TransferCodingConflict => "transfer_coding_conflict",
        CorpusAcquisitionRefusalReason.InvalidContentLength => "invalid_content_length",
        CorpusAcquisitionRefusalReason.UnsupportedTransferCoding => "unsupported_transfer_coding",
        CorpusAcquisitionRefusalReason.HeaderDeadline => "header_deadline",
        CorpusAcquisitionRefusalReason.TransportBeforeHeaders => "transport_before_headers",
        CorpusAcquisitionRefusalReason.RevalidationRequestNotAdmitted =>
            "revalidation_request_not_admitted",
        CorpusAcquisitionRefusalReason.StatusContentForbidden => "status_content_forbidden",
        CorpusAcquisitionRefusalReason.StatusFramingConflict => "status_framing_conflict",
        CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed => "requested_representation_not_served",
        CorpusAcquisitionRefusalReason.WrongAcceptToken => "wrong_accept_token",
        CorpusAcquisitionRefusalReason.RedirectTargetOriginNotAdmitted => "redirect_target_origin_not_admitted",
        CorpusAcquisitionRefusalReason.RobotsDisallowed => "robots_disallowed",
        CorpusAcquisitionRefusalReason.NotFound => "not_found",
        CorpusAcquisitionRefusalReason.Gone => "gone",
        CorpusAcquisitionRefusalReason.RetryExhausted => "retry_exhausted",
        CorpusAcquisitionRefusalReason.UnexpectedPublisherStatus => "unexpected_publisher_status",
        _ => throw new InvalidOperationException("Unknown corpus acquisition refusal reason."),
    };

    private static string FloorName(CustodyMembership floor) => floor switch
    {
        CustodyMembership.ReadOnce => "read_once",
        CustodyMembership.RetainedUnenforced => "retained_unenforced",
        CustodyMembership.Floored => "floored",
        _ => throw new InvalidOperationException("Unknown custody membership."),
    };

    // Not reused from ScopeManifestCanonicalWriter's own DispositionName: that method is private to
    // its class, and ScopeManifest.cs is out of this slice's path claim except for a genuine
    // missing piece, which widening a four-line switch from private to internal is not.
    private static string DispositionName(ScopeDisposition disposition) => disposition switch
    {
        ScopeDisposition.AcceptedSelected => "accepted_selected",
        ScopeDisposition.TypedQuarantine => "typed_quarantine",
        ScopeDisposition.Point => "point",
        ScopeDisposition.NeverIngest => "never_ingest",
        _ => throw new InvalidOperationException("Unknown scope disposition."),
    };
}

/// <summary>
/// The corpus/6 record set's own schema identity: D1-06b's own new wire artifact, not itself named
/// in any pre-existing "corpus/6" coordination reference the way <see cref="CorpusRecordSchemaIds.Record"/>
/// is (see that type's own remarks), so this starts at version 1 rather than borrowing the record's
/// own "6".
/// </summary>
public static class CorpusRecordSetSchemaIds
{
    public const string Set = "lex-v3-source-corpus-record-set/1";
}

/// <summary>
/// One durable, canonically written run's worth of <see cref="CorpusRecord"/>s: the whole set
/// D1-06b's own writer produces from one <see cref="Lex.V3.Contracts.Source.Scope.ScopeManifest"/>,
/// held together so a caller custody-writes and reopens the run's complete output in one artifact
/// rather than one artifact per object. Every member below is validated for internal consistency
/// (fix two's own discipline, applied at set scope): every record must declare the set's own
/// <see cref="ManifestRef"/> and <see cref="RunIdentity"/>, records must be strictly ordered by
/// <see cref="CorpusRecord.ObjectOrdinal"/> with no duplicate ordinal, and no two records may name
/// the same <see cref="CorpusRecord.ObjectRef"/>, so a reader can never observe a set that mixes
/// records from two different runs or manifests, silently reorders or duplicates one object's row,
/// or lists the same object twice under two different ordinals.
/// Emptiness is not refused: a manifest that observed zero objects is a legitimate, if unusual, run.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusRecordSet
{
    [JsonConstructor]
    public CorpusRecordSet(
        string schema,
        SourceArtifactRef manifestRef,
        SourceArtifactRef runIdentity,
        IReadOnlyList<CorpusRecord> records)
    {
        if (!string.Equals(schema, CorpusRecordSetSchemaIds.Set, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A corpus record set must declare {CorpusRecordSetSchemaIds.Set}.", nameof(schema));
        }

        Schema = schema;
        ManifestRef = manifestRef ?? throw new ArgumentNullException(nameof(manifestRef));
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        Records = ScopeValidation.Copy(records, nameof(records));

        var seenObjectRefs = new HashSet<SourceObjectRef>();
        for (var index = 0; index < Records.Count; index++)
        {
            var record = Records[index];
            if (record.ManifestRef != ManifestRef)
            {
                throw new ArgumentException(
                    "Every record in a set must declare the set's own manifest reference.",
                    nameof(records));
            }

            if (record.RunIdentity != RunIdentity)
            {
                throw new ArgumentException(
                    "Every record in a set must declare the set's own run identity.",
                    nameof(records));
            }

            if (index > 0 && Records[index - 1].ObjectOrdinal >= record.ObjectOrdinal)
            {
                throw new ArgumentException(
                    "A record set must be strictly ordered by object ordinal, with no duplicate.",
                    nameof(records));
            }

            // Ordinal uniqueness above refuses two rows at the same ordinal; it says nothing about
            // one object appearing twice under two DIFFERENT ordinals. A set is one record per
            // manifest object, so the same ObjectRef naming two rows is refused here regardless of
            // ordinal.
            if (!seenObjectRefs.Add(record.ObjectRef))
            {
                throw new ArgumentException(
                    "A record set cannot name the same object twice: every record's own object " +
                    "reference must be distinct.",
                    nameof(records));
            }
        }
    }

    public string Schema { get; }

    /// <summary>Which manifest artifact every record in this set was built from.</summary>
    public SourceArtifactRef ManifestRef { get; }

    /// <summary>Which run produced every record in this set.</summary>
    public SourceArtifactRef RunIdentity { get; }

    public IReadOnlyList<CorpusRecord> Records { get; }
}

/// <summary>
/// The reader door for a corpus record set previously durably written by
/// <see cref="CorpusRecordSetCanonicalWriter.Write"/>, shaped exactly like
/// <see cref="VerifiedCorpusRecord.ParseAndVerify"/>: verifies the SHA-256 against
/// <paramref name="artifactRef"/> before any content is exposed, then requires the parsed set's own
/// canonical re-serialization to reproduce the input bytes exactly. No InternalsVisibleTo is granted
/// for this door.
/// </summary>
public sealed class VerifiedCorpusRecordSet
{
    internal VerifiedCorpusRecordSet(CorpusRecordSet set)
    {
        Set = set;
    }

    /// <summary>
    /// The verified set's own content. Reading it needs no InternalsVisibleTo; holding an instance
    /// is itself the evidence that <see cref="ParseAndVerify"/> ran to completion, because the
    /// constructor above is the only door onto this type and it stays internal.
    /// </summary>
    public CorpusRecordSet Set { get; }

    public static VerifiedCorpusRecordSet ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);

        if (!string.Equals(
                CorpusRecordSetCanonicalWriter.ComputeSetSha256(canonicalBytes),
                artifactRef.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The corpus record set bytes do not match their artifact reference.",
                nameof(canonicalBytes));
        }

        var json = ScopeValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
        CorpusRecordSet set;
        try
        {
            set = ContractJson.Deserialize<CorpusRecordSet>(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The corpus record set bytes are not one valid typed canonical document.",
                nameof(canonicalBytes),
                exception);
        }

        using var rebuilt = new MemoryStream();
        CorpusRecordSetCanonicalWriter.Write(rebuilt, set);
        if (!canonicalBytes.SequenceEqual(rebuilt.ToArray()))
        {
            throw new ArgumentException(
                "The corpus record set is not its exact canonical typed representation.",
                nameof(canonicalBytes));
        }

        return new VerifiedCorpusRecordSet(set);
    }
}

/// <summary>
/// Canonicalizes a <see cref="CorpusRecordSet"/> into deterministic UTF-8 bytes and their SHA-256,
/// domain-separated the same way <see cref="CorpusRecordCanonicalWriter"/> and
/// <see cref="ScopeManifestCanonicalWriter"/> domain-separate their own digests. Each record inside
/// the set is written by <see cref="CorpusRecordCanonicalWriter.WriteRecord"/> itself, the exact same
/// per-record encoding <see cref="CorpusRecordCanonicalWriter.Write"/> uses for a standalone record,
/// so a set's own bytes can never disagree with what the identical record would canonicalize to on
/// its own.
/// </summary>
public static class CorpusRecordSetCanonicalWriter
{
    private const string SetDomain = "lex-v3-source-corpus-record-set/1\n";

    public static string Write(Stream destination, CorpusRecordSet set)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(set);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The canonical destination must be writable.", nameof(destination));
        }

        using var buffer = new MemoryStream();
        using (var writer = NewWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", CorpusRecordSetSchemaIds.Set);
            writer.WritePropertyName("manifest_ref");
            ScopeManifestCanonicalWriter.WriteArtifact(writer, set.ManifestRef);
            writer.WritePropertyName("run_identity");
            ScopeManifestCanonicalWriter.WriteArtifact(writer, set.RunIdentity);
            writer.WritePropertyName("records");
            writer.WriteStartArray();
            foreach (var record in set.Records)
            {
                CorpusRecordCanonicalWriter.WriteRecord(writer, record);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        buffer.WriteByte((byte)'\n');
        var bytes = buffer.ToArray();
        destination.Write(bytes, 0, bytes.Length);
        return ComputeSetSha256(bytes);
    }

    /// <summary>
    /// The exact digest <see cref="Write"/> returns for its own output, recomputed directly from
    /// durable bytes so a reader can check them against a pinned artifact reference before parsing.
    /// <paramref name="canonicalBytes"/> must be exactly what <see cref="Write"/> wrote, trailing
    /// newline included.
    /// </summary>
    internal static string ComputeSetSha256(ReadOnlySpan<byte> canonicalBytes)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        incremental.AppendData(Encoding.ASCII.GetBytes(SetDomain));
        incremental.AppendData(canonicalBytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        incremental.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static Utf8JsonWriter NewWriter(Stream output) => new(
        output,
        new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        });
}
