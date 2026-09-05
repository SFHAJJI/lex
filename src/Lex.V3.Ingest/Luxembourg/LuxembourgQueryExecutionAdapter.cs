using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// Decision 64: "Each relation family carries its own closed acquisition state and evidence
/// identity. A genuinely empty edge set is admissible only when that family's bounded enumeration
/// completed and its completion evidence is retained. An unacquired, incomplete or uncertain family
/// remains explicitly typed as such and cannot support an absence claim."
/// </summary>
public enum LuxembourgRelationFamilyAcquisitionState
{
    /// <summary>The family's bounded enumeration completed this run; completion evidence is held.</summary>
    [JsonStringEnumMemberName("acquired_complete")]
    AcquiredComplete = 1,

    /// <summary>No enumeration of this family was attempted this run.</summary>
    [JsonStringEnumMemberName("unacquired")]
    Unacquired = 2,

    /// <summary>An enumeration of this family was attempted and did not complete.</summary>
    [JsonStringEnumMemberName("incomplete")]
    Incomplete = 3,

    /// <summary>
    /// The family's completion cannot presently be assessed (for example, the executor itself was
    /// refused before it could report a specific incompleteness cause).
    /// </summary>
    [JsonStringEnumMemberName("uncertain")]
    Uncertain = 4,
}

/// <summary>
/// One relation predicate's Decision 64 acquisition state. D1-04 mints one of these for every
/// predicate <see cref="VerifiedLuxembourgSourceProfile.RelationRules"/> names (the 18 predicates
/// Decision 65 closes LU relation acquisition over), never a bare empty array standing in for "not
/// acquired".
/// </summary>
public sealed class LuxembourgRelationFamilyAcquisition
{
    private LuxembourgRelationFamilyAcquisition(
        string predicateIri,
        LuxembourgRelationFamilyAcquisitionState state,
        AbsenceFamilyEnumerationProof? completionEvidence,
        string? reason)
    {
        PredicateIri = predicateIri;
        State = state;
        CompletionEvidence = completionEvidence;
        Reason = reason;
    }

    public string PredicateIri { get; }

    public LuxembourgRelationFamilyAcquisitionState State { get; }

    /// <summary>Present if and only if <see cref="State"/> is <see cref="LuxembourgRelationFamilyAcquisitionState.AcquiredComplete"/>.</summary>
    public AbsenceFamilyEnumerationProof? CompletionEvidence { get; }

    /// <summary>Present if and only if <see cref="State"/> is not <see cref="LuxembourgRelationFamilyAcquisitionState.AcquiredComplete"/>.</summary>
    public string? Reason { get; }

    /// <summary>
    /// The only path to <see cref="LuxembourgRelationFamilyAcquisitionState.AcquiredComplete"/>.
    /// Requires the completion evidence itself, so a caller cannot claim completeness without
    /// holding the proof that earns it.
    /// </summary>
    public static LuxembourgRelationFamilyAcquisition Complete(
        string predicateIri, AbsenceFamilyEnumerationProof completionEvidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateIri);
        ArgumentNullException.ThrowIfNull(completionEvidence);
        return new(
            predicateIri, LuxembourgRelationFamilyAcquisitionState.AcquiredComplete, completionEvidence, null);
    }

    /// <summary>
    /// One of the other three Decision 64 states. Refuses
    /// <see cref="LuxembourgRelationFamilyAcquisitionState.AcquiredComplete"/> so that state can
    /// never be reached without completion evidence through this door either.
    /// </summary>
    public static LuxembourgRelationFamilyAcquisition NotComplete(
        string predicateIri, LuxembourgRelationFamilyAcquisitionState state, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (state == LuxembourgRelationFamilyAcquisitionState.AcquiredComplete)
        {
            throw new ArgumentException(
                "AcquiredComplete requires completion evidence; call Complete instead.",
                nameof(state));
        }

        return new(predicateIri, state, null, reason);
    }
}

/// <summary>One family's execution: the executor call, and, if it delivered, the proof attempt.</summary>
public enum LuxembourgFamilyEnumerationOutcomeKind
{
    /// <summary>The executor delivered and the family's whole enumeration was proven.</summary>
    Proven = 1,

    /// <summary>The executor itself refused before delivering (see <see cref="LuxembourgFamilyEnumerationOutcome.ExecutorRefusal"/>).</summary>
    ExecutorRefused = 2,

    /// <summary>
    /// The executor delivered, but the delivery does not prove this family's whole enumeration (see
    /// <see cref="LuxembourgFamilyEnumerationOutcome.ProofRefusal"/>) -- for example the selection
    /// reached the row cap, or the two passes disagreed.
    /// </summary>
    ProofRefused = 3,

    /// <summary>
    /// D1-04c: the family's own single-partition pass refused <see cref="LuxembourgEnumerationRefusal.PartitionRequired"/>,
    /// and the caller-supplied cover chain for it reconciled (<see cref="LuxembourgPartitionCover.TryCreate"/>)
    /// into every leaf's own proven enumeration (see <see cref="LuxembourgFamilyEnumerationOutcome.CoverLeafProofs"/>).
    /// </summary>
    CoverProven = 4,

    /// <summary>
    /// D1-04c: the family's own single-partition pass refused <see cref="LuxembourgEnumerationRefusal.PartitionRequired"/>,
    /// a cover chain was supplied for it, but the chain could not reconcile into a proven whole
    /// enumeration (see <see cref="LuxembourgFamilyEnumerationOutcome.CoverRefusal"/>).
    /// </summary>
    CoverRefused = 5,
}

/// <summary>Which stage of a census or assertion family's cover-chain reconciliation refused. Closed.</summary>
public enum LuxembourgPartitionCoverReconciliationRefusal
{
    /// <summary>
    /// One of the chain's own leaves refused before delivering (see
    /// <see cref="LuxembourgPartitionCoverReconciliationDetail.LeafExecutorRefusal"/>).
    /// </summary>
    LeafExecutorRefused = 1,

    /// <summary>
    /// A leaf delivered, but its own family-enumeration proof failed (see
    /// <see cref="LuxembourgPartitionCoverReconciliationDetail.LeafProofRefusal"/>) -- a leaf's own
    /// delivery proof disagreeing, per the ruling's own wording.
    /// </summary>
    LeafProofRefused = 2,

    /// <summary>
    /// <see cref="LuxembourgPartitionCover.TryCreate"/> itself refused (see
    /// <see cref="LuxembourgPartitionCoverReconciliationDetail.CoverRefusal"/>).
    /// </summary>
    CoverReconciliationRefused = 3,
}

/// <summary>
/// D1-04c item 1: why a census or assertion family's cover-chain fallback -- driven when its
/// single-partition pass refuses <see cref="LuxembourgEnumerationRefusal.PartitionRequired"/> and a
/// caller-supplied <see cref="LuxembourgPartitionChain"/> exists for it -- could not reconcile into a
/// proven whole enumeration. Exactly one of <see cref="LeafExecutorRefusal"/>,
/// <see cref="LeafProofRefusal"/> or <see cref="CoverRefusal"/> is present, matching <see cref="Code"/>.
/// A chain that cannot reconcile is a typed family refusal, never a raw exception.
/// </summary>
public sealed class LuxembourgPartitionCoverReconciliationDetail
{
    private LuxembourgPartitionCoverReconciliationDetail(
        LuxembourgPartitionCoverReconciliationRefusal code,
        string leafPartitionId,
        LuxembourgEnumerationRefusalDetail? leafExecutorRefusal,
        AbsenceFamilyEnumerationProofRefusal? leafProofRefusal,
        LuxembourgPartitionCoverRefusal? coverRefusal)
    {
        Code = code;
        LeafPartitionId = leafPartitionId;
        LeafExecutorRefusal = leafExecutorRefusal;
        LeafProofRefusal = leafProofRefusal;
        CoverRefusal = coverRefusal;
    }

    public LuxembourgPartitionCoverReconciliationRefusal Code { get; }

    /// <summary>
    /// The chain leaf this refusal names. Empty for <see cref="LuxembourgPartitionCoverReconciliationRefusal.CoverReconciliationRefused"/>,
    /// which is <see cref="LuxembourgPartitionCover.TryCreate"/>'s own refusal: that door reports one
    /// closed reason for the whole chain, never a specific leaf ordinal.
    /// </summary>
    public string LeafPartitionId { get; }

    /// <summary>Present if and only if <see cref="Code"/> is <see cref="LuxembourgPartitionCoverReconciliationRefusal.LeafExecutorRefused"/>.</summary>
    public LuxembourgEnumerationRefusalDetail? LeafExecutorRefusal { get; }

    /// <summary>Present if and only if <see cref="Code"/> is <see cref="LuxembourgPartitionCoverReconciliationRefusal.LeafProofRefused"/>.</summary>
    public AbsenceFamilyEnumerationProofRefusal? LeafProofRefusal { get; }

    /// <summary>Present if and only if <see cref="Code"/> is <see cref="LuxembourgPartitionCoverReconciliationRefusal.CoverReconciliationRefused"/>.</summary>
    public LuxembourgPartitionCoverRefusal? CoverRefusal { get; }

    public static LuxembourgPartitionCoverReconciliationDetail LeafExecutorRefused(
        string leafPartitionId, LuxembourgEnumerationRefusalDetail refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafPartitionId);
        ArgumentNullException.ThrowIfNull(refusal);
        return new(
            LuxembourgPartitionCoverReconciliationRefusal.LeafExecutorRefused,
            leafPartitionId, refusal, null, null);
    }

    public static LuxembourgPartitionCoverReconciliationDetail LeafProofRefused(
        string leafPartitionId, AbsenceFamilyEnumerationProofRefusal refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leafPartitionId);
        if (refusal == AbsenceFamilyEnumerationProofRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(
            LuxembourgPartitionCoverReconciliationRefusal.LeafProofRefused, leafPartitionId, null, refusal, null);
    }

    public static LuxembourgPartitionCoverReconciliationDetail ReconciliationRefused(
        LuxembourgPartitionCoverRefusal refusal)
    {
        if (refusal == LuxembourgPartitionCoverRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(
            LuxembourgPartitionCoverReconciliationRefusal.CoverReconciliationRefused,
            string.Empty, null, null, refusal);
    }
}

public sealed class LuxembourgFamilyEnumerationOutcome
{
    private LuxembourgFamilyEnumerationOutcome(
        string familyKey,
        LuxembourgFamilyEnumerationOutcomeKind kind,
        AbsenceFamilyEnumerationProof? proof,
        LuxembourgEnumerationRefusalDetail? executorRefusal,
        AbsenceFamilyEnumerationProofRefusal? proofRefusal,
        IReadOnlyList<AbsenceFamilyEnumerationProof>? coverLeafProofs,
        LuxembourgPartitionCoverReconciliationDetail? coverRefusal)
    {
        FamilyKey = familyKey;
        Kind = kind;
        Proof = proof;
        ExecutorRefusal = executorRefusal;
        ProofRefusal = proofRefusal;
        CoverLeafProofs = coverLeafProofs;
        CoverRefusal = coverRefusal;
    }

    public string FamilyKey { get; }

    public LuxembourgFamilyEnumerationOutcomeKind Kind { get; }

    public AbsenceFamilyEnumerationProof? Proof { get; }

    public LuxembourgEnumerationRefusalDetail? ExecutorRefusal { get; }

    public AbsenceFamilyEnumerationProofRefusal? ProofRefusal { get; }

    /// <summary>
    /// Present if and only if <see cref="Kind"/> is <see cref="LuxembourgFamilyEnumerationOutcomeKind.CoverProven"/>:
    /// one proof per leaf of the reconciled <see cref="LuxembourgPartitionCover"/>, in chain order.
    /// There is no single family-wide <see cref="AbsenceFamilyEnumerationProof"/> for a cover, because
    /// <see cref="AbsenceFamilyEnumerationProof.TryCreate"/> requires its family key to equal the
    /// delivery's own partition key exactly, and each leaf's delivery names its own leaf partition,
    /// never the root family's.
    /// </summary>
    public IReadOnlyList<AbsenceFamilyEnumerationProof>? CoverLeafProofs { get; }

    /// <summary>Present if and only if <see cref="Kind"/> is <see cref="LuxembourgFamilyEnumerationOutcomeKind.CoverRefused"/>.</summary>
    public LuxembourgPartitionCoverReconciliationDetail? CoverRefusal { get; }

    public static LuxembourgFamilyEnumerationOutcome Proven(string familyKey, AbsenceFamilyEnumerationProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.Proven, proof, null, null, null, null);
    }

    public static LuxembourgFamilyEnumerationOutcome ExecutorRefused(
        string familyKey, LuxembourgEnumerationRefusalDetail refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.ExecutorRefused, null, refusal, null, null, null);
    }

    public static LuxembourgFamilyEnumerationOutcome ProofRefused(
        string familyKey, AbsenceFamilyEnumerationProofRefusal refusal)
    {
        if (refusal == AbsenceFamilyEnumerationProofRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, null, null, refusal, null, null);
    }

    public static LuxembourgFamilyEnumerationOutcome CoverProven(
        string familyKey, IReadOnlyList<AbsenceFamilyEnumerationProof> leafProofs)
    {
        ArgumentNullException.ThrowIfNull(leafProofs);
        if (leafProofs.Count == 0)
        {
            throw new ArgumentException("A cover-proven family requires at least one leaf proof.", nameof(leafProofs));
        }

        return new(
            familyKey, LuxembourgFamilyEnumerationOutcomeKind.CoverProven, null, null, null,
            Array.AsReadOnly(leafProofs.ToArray()), null);
    }

    public static LuxembourgFamilyEnumerationOutcome CoverRefused(
        string familyKey, LuxembourgPartitionCoverReconciliationDetail refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.CoverRefused, null, null, null, null, refusal);
    }
}

public enum LuxembourgQueryExecutionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The merged R5.1 pipeline's <c>Resolve</c> step refused (see <see cref="LuxembourgQueryExecutionRefusalDetail.ResolutionFailure"/>).</summary>
    [JsonStringEnumMemberName("scope_resolution_failed")]
    ScopeResolutionFailed = 1,

    /// <summary>
    /// A GENUINE CUSTODY FAILURE on the scope manifest: either the write itself errored, or the
    /// bytes could not be reproduced at their own digest when this run reopened them. Those are the
    /// only two producers, CustodyHold.TryHoldAsync returning no receipt and the checked reopen
    /// raising CustodyIntegrityException, and both mean the manifest is NOT RETAINED.
    /// </summary>
    /// <remarks>
    /// WAS <c>ScopeManifestNotHeld</c>, and its summary said it fired when the store enforced no
    /// retention floor. It never did: the floor gate was removed by the Decision 71 interpretation,
    /// which records the observed class and continues, so the name and the summary both described a
    /// condition that could not produce this member. Renamed to match
    /// <see cref="Lex.V3.Ingest.Europe.EuQueryExecutionRefusal.ScopeManifestNotRetained"/> under the misattribution class
    /// ruled at b0edd672: ONE CONDITION CARRIES ONE NAME ACROSS BOTH PUBLISHERS, so a reader who
    /// learns what this refusal means from the EU adapter is not misled by the LU one.
    /// </remarks>
    [JsonStringEnumMemberName("scope_manifest_not_retained")]
    ScopeManifestNotRetained = 2,

    /// <summary>
    /// <see cref="LuxembourgQueryExecutionAdapter.RunAsync"/> was given a non-null
    /// <c>resourceObservationFamilyKey</c>, but no family this run enumerated can attest one: either
    /// the key does not name any entry of this run's family results, or the entry it names is not
    /// <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/>. <see
    /// cref="Lex.V3.Contracts.Source.Core.VerifiedRepeatedEnumerationRows.TryOpen"/> can only reopen
    /// rows behind a proof that exists, so an unproven or unmatched designation refuses here rather
    /// than silently deriving zero observations from a family this run never actually censused.
    /// </summary>
    [JsonStringEnumMemberName("resource_observation_family_not_proven")]
    ResourceObservationFamilyNotProven = 3,

    /// <summary>
    /// The designated resource-observation family was proven, but its delivered rows did not
    /// independently re-verify through <see
    /// cref="Lex.V3.Contracts.Source.Core.VerifiedRepeatedEnumerationRows.TryOpen"/> when reopened
    /// from custody: see <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/> for the exact
    /// <see cref="Lex.V3.Contracts.Source.Core.RepeatedEnumerationRowsOpenRefusal"/> reason.
    /// </summary>
    [JsonStringEnumMemberName("resource_observation_rows_not_verified")]
    ResourceObservationRowsNotVerified = 4,

    /// <summary>
    /// D1-04b's ruling on the two families: the "assertion-rows" family (set "A") is bound to the
    /// "subjects" census family (set "S") by IDENTITY-SET EQUALITY, never a count. This refuses when
    /// a subject appearing in A's own decoded rows is not a member of S's own delivered key set --
    /// two independent enumerations over the same triple store disagreeing about which subjects
    /// exist is a genuine data-integrity problem this adapter reports rather than silently drops.
    /// See <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/> for the exact subject.
    /// </summary>
    [JsonStringEnumMemberName("observation_subject_not_in_delivered_census")]
    ObservationSubjectNotInDeliveredCensus = 5,

    /// <summary>
    /// D1-04b's reviewer fold-in: an "assertion-rows" family row's own <c>object_kind</c> projection
    /// carried a value outside the three <c>LuxembourgQueryPlan.BuildTemplates</c>' own BIND can
    /// produce (<c>"iri"</c>, <c>"literal"</c>, <c>"unsupported_blank_node"</c>). This is publisher
    /// data disagreeing with the query plan's own closed shape, not a caller-contract violation, so
    /// it refuses here rather than throwing. See <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/>
    /// for the exact subject and value.
    /// </summary>
    [JsonStringEnumMemberName("assertion_row_object_kind_not_recognised")]
    AssertionRowObjectKindNotRecognised = 6,

    /// <summary>
    /// D1-04b's reviewer fold-in: a term this run needed at a specific, named projection position
    /// (the census's own resource-identity term, or one of the assertion-rows family's own subject,
    /// predicate, object, object_kind, datatype_iri or language_tag terms) was unbound in a
    /// delivered, independently re-verified row. This is publisher data disagreeing with the query
    /// plan's own closed projection shape, not a caller-contract violation, so it refuses here rather
    /// than throwing. See <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/> for exactly
    /// which term.
    /// <para>
    /// Investigated and currently unreachable through a family that reached
    /// <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/>:
    /// <c>LuxembourgQueryPlan.CreateDeliveryProfile</c> binds every LU publisher-query template's
    /// <c>CanonicalKeyVariables</c> to its own full <c>ProjectionVariables</c> list (both the
    /// "subjects" census's <c>key_1..key_6</c> and the "assertion-rows" family's
    /// subject/predicate/object/object_kind/datatype_iri/language_tag/<c>key_1..key_6</c> alike), and
    /// <c>RepeatedEnumerationDeliveryProof</c>'s own page verification already refuses a delivery
    /// whose canonical-key components are not bound, before either this run's own family-outcome loop
    /// or <see cref="ReopenAndVerifyFamilyRowsAsync"/>'s own reverification ever sees it as proven.
    /// Retained as typed, non-throwing defense in depth rather than removed, in case that
    /// canonical-key coverage ever narrows to something less than the full projection.
    /// </para>
    /// </summary>
    [JsonStringEnumMemberName("assertion_row_term_unbound")]
    AssertionRowTermUnbound = 7,

    /// <summary>
    /// D1-06c-LU-2: this run's own document-fetch session never started for one row, and the cause
    /// is a fact about the run rather than about that document: robots was unreachable,
    /// uninterpretable or expired, the store could not hold the robots policy, or the transport
    /// failed before headers on every attempt this profile allows. A publisher DENIAL is
    /// deliberately not here; that is one object's own
    /// <see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason.RobotsDisallowed"/>
    /// record, because it is the publisher speaking about that document.
    /// </summary>
    [JsonStringEnumMemberName("document_fetch_session_not_started")]
    DocumentFetchSessionNotStarted = 8,

    /// <summary>
    /// A document body was fetched for real, but the store enforced no retention floor on it, so
    /// this run cannot claim it as held (never bypass the Decision 71 floor). Refuses the whole run
    /// rather than recording an object as held on bytes nothing protects.
    /// </summary>
    [JsonStringEnumMemberName("document_body_not_held")]
    DocumentBodyNotHeld = 9,

    /// <summary>
    /// A document GET completed or failed in a shape this route has no reviewed reading for and
    /// D1-06b's closed <see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason"/>
    /// vocabulary cannot name faithfully. The whole run refuses, naming the real classified cause,
    /// rather than mapping it onto an unrelated existing member or accepting it as held.
    /// </summary>
    [JsonStringEnumMemberName("document_get_outcome_not_representable")]
    DocumentGetOutcomeNotRepresentable = 10,

    /// <summary>
    /// A GENUINE CUSTODY FAILURE on this run's own corpus/6 record set, forwarded verbatim from
    /// <see cref="Lex.V3.Ingest.CorpusRecordSetWriteRefusalKind.RecordSetNotRetained"/>: the writer
    /// routes through CustodyHold like every other artifact, so this fires when that hold errored or
    /// the set could not be reproduced at its own digest. Mirrors
    /// <see cref="ScopeManifestNotRetained"/> for the run's last step.
    /// </summary>
    /// <remarks>
    /// WAS <c>RecordSetNotHeld</c>, with the same untrue summary about an enforced floor, and its
    /// only producer already forwarded a refusal the shared writer had renamed. Renamed with it.
    /// </remarks>
    [JsonStringEnumMemberName("record_set_not_retained")]
    RecordSetNotRetained = 11,
}

/// <summary>
/// D1-04b's reviewer fold-in: why <see cref="LuxembourgQueryExecutionAdapter.BuildResourceObservations"/>
/// excluded an "assertion-rows" family row from its subject's own derived
/// <see cref="LuxembourgResourceObservation.Assertions"/> list. Both causes are the query plan's own
/// documented boundary, not a delivery-integrity problem -- but without recording which rows were
/// excluded and why, a subject whose every row was excluded this way is indistinguishable in the
/// output from a subject with genuinely zero rows in the assertion family at all.
/// </summary>
public enum LuxembourgResourceObservationExclusionCause
{
    /// <summary>
    /// The row's predicate is real content, but it is relation content (a RelationPredicate-only
    /// IRI, not an AssertionPredicate-vocabulary one): it belongs to
    /// <see cref="LuxembourgObservedRelation"/>, sourced from the unrelated "relation-assertions"
    /// family, not to <see cref="LuxembourgResourceObservation.Assertions"/>.
    /// </summary>
    [JsonStringEnumMemberName("predicate_not_admitted")]
    PredicateNotAdmitted = 1,

    /// <summary>
    /// The row's object is a blank node. <see cref="LuxembourgAssertionObjectKind"/> admits only
    /// <c>Iri</c> or <c>Literal</c>, so a blank-node object cannot be represented by
    /// <see cref="LuxembourgObservedAssertion"/> at all.
    /// </summary>
    [JsonStringEnumMemberName("blank_node_object")]
    BlankNodeObject = 2,
}

/// <summary>
/// One subject's own count of "assertion-rows" family rows excluded from its derived
/// <see cref="LuxembourgResourceObservation.Assertions"/> list for one <see cref="Cause"/>. Never
/// minted for zero rows: an entry's presence already means at least one row was excluded, so
/// <see cref="RowCount"/> is always at least one.
/// </summary>
public sealed record LuxembourgResourceObservationExclusionAccounting(
    string Subject,
    LuxembourgResourceObservationExclusionCause Cause,
    int RowCount);

public sealed class LuxembourgQueryExecutionRefusalDetail
{
    /// <summary>
    /// Internal: only <see cref="LuxembourgQueryExecutionAdapter"/> and this assembly's own tests
    /// construct one, matching <c>LuxembourgEnumerationRefusalDetail</c>'s door, so a refusal detail
    /// is never built from outside naming a refusal that did not happen.
    /// </summary>
    internal LuxembourgQueryExecutionRefusalDetail(
        LuxembourgQueryExecutionRefusal code,
        LuxembourgProfileResolutionFailure? resolutionFailure,
        string? detail)
    {
        if (code == LuxembourgQueryExecutionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "A refusal detail requires a real refusal code.");
        }

        if ((code == LuxembourgQueryExecutionRefusal.ScopeResolutionFailed) != (resolutionFailure is not null))
        {
            throw new ArgumentException(
                "ResolutionFailure is present if and only if the code is ScopeResolutionFailed.",
                nameof(resolutionFailure));
        }

        Code = code;
        ResolutionFailure = resolutionFailure;
        Detail = detail;
    }

    public LuxembourgQueryExecutionRefusal Code { get; }

    public LuxembourgProfileResolutionFailure? ResolutionFailure { get; }

    public string? Detail { get; }
}

/// <summary>
/// Fold-in one of the D1-04 refreeze: whether a <see cref="LuxembourgQueryExecutionResult.Delivered"/>
/// result proves every family it enumerated, or is only partial. Nothing about writing and holding
/// the scope manifest depends on every <see cref="LuxembourgFamilyEnumerationOutcome"/> being
/// <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/> -- a delivered result with one or more
/// refused families is legal today and was previously undocumented, which is exactly the shape a
/// consumer could silently treat as a complete run. There is no setter and no caller-supplied value:
/// <see cref="LuxembourgQueryExecutionResult.Delivered"/> computes this from the same
/// <c>familyOutcomes</c> it is given, so a caller cannot declare completeness it did not earn.
/// </summary>
public enum LuxembourgQueryExecutionCompletion
{
    /// <summary>Every family this run enumerated proved its whole enumeration.</summary>
    [JsonStringEnumMemberName("all_families_proven")]
    AllFamiliesProven = 1,

    /// <summary>
    /// At least one family this run enumerated was refused, whether by the executor itself
    /// (<see cref="LuxembourgFamilyEnumerationOutcomeKind.ExecutorRefused"/>) or by the completeness
    /// proof (<see cref="LuxembourgFamilyEnumerationOutcomeKind.ProofRefused"/>). The scope manifest
    /// and every acquisition state this result carries are still exactly what they say; this value
    /// only makes explicit that the run's family enumeration, taken as a whole, is not complete.
    /// </summary>
    [JsonStringEnumMemberName("partial_family_refused")]
    PartialFamilyRefused = 2,
}

/// <summary>Delivered or refused, never both and never neither -- the same shape the executor uses.</summary>
public sealed class LuxembourgQueryExecutionResult
{
    private LuxembourgQueryExecutionResult(
        SourceProfileTopology topology,
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> familyOutcomes,
        IReadOnlyList<LuxembourgRelationFamilyAcquisition> relationFamilyAcquisitions,
        IReadOnlyList<string> resourceObservationSubjects,
        IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> resourceObservationExclusions,
        DurableBlobWriteReceipt? scopeManifestReceipt,
        string? scopeManifestCanonicalSha256,
        LuxembourgQueryExecutionCompletion? completion,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? documentAcquisitionOutcomesByOrdinal,
        SourceArtifactRef? corpusRecordSetRef,
        VerifiedCorpusRecordSet? corpusRecordSet,
        LuxembourgQueryExecutionRefusalDetail? refusal)
    {
        Topology = topology;
        FamilyOutcomes = familyOutcomes;
        RelationFamilyAcquisitions = relationFamilyAcquisitions;
        ResourceObservationSubjects = resourceObservationSubjects;
        ResourceObservationExclusions = resourceObservationExclusions;
        ScopeManifestReceipt = scopeManifestReceipt;
        ScopeManifestCanonicalSha256 = scopeManifestCanonicalSha256;
        Completion = completion;
        DocumentAcquisitionOutcomesByOrdinal = documentAcquisitionOutcomesByOrdinal;
        CorpusRecordSetRef = corpusRecordSetRef;
        CorpusRecordSet = corpusRecordSet;
        Refusal = refusal;
    }

    public static LuxembourgQueryExecutionResult Delivered(
        SourceProfileTopology topology,
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> familyOutcomes,
        IReadOnlyList<LuxembourgRelationFamilyAcquisition> relationFamilyAcquisitions,
        IReadOnlyList<string> resourceObservationSubjects,
        IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> resourceObservationExclusions,
        DurableBlobWriteReceipt scopeManifestReceipt,
        string scopeManifestCanonicalSha256,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> documentAcquisitionOutcomesByOrdinal,
        SourceArtifactRef corpusRecordSetRef,
        VerifiedCorpusRecordSet corpusRecordSet)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(resourceObservationSubjects);
        ArgumentNullException.ThrowIfNull(resourceObservationExclusions);
        ArgumentNullException.ThrowIfNull(scopeManifestReceipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeManifestCanonicalSha256);
        ArgumentNullException.ThrowIfNull(documentAcquisitionOutcomesByOrdinal);
        ArgumentNullException.ThrowIfNull(corpusRecordSetRef);
        ArgumentNullException.ThrowIfNull(corpusRecordSet);
        var completion = familyOutcomes.All(
            static outcome => outcome.Kind is
                LuxembourgFamilyEnumerationOutcomeKind.Proven or
                LuxembourgFamilyEnumerationOutcomeKind.CoverProven)
            ? LuxembourgQueryExecutionCompletion.AllFamiliesProven
            : LuxembourgQueryExecutionCompletion.PartialFamilyRefused;
        return new(
            topology, familyOutcomes, relationFamilyAcquisitions,
            resourceObservationSubjects, resourceObservationExclusions, scopeManifestReceipt,
            scopeManifestCanonicalSha256, completion, documentAcquisitionOutcomesByOrdinal,
            corpusRecordSetRef, corpusRecordSet, null);
    }

    public static LuxembourgQueryExecutionResult Refused(
        SourceProfileTopology topology,
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> familyOutcomes,
        IReadOnlyList<LuxembourgRelationFamilyAcquisition> relationFamilyAcquisitions,
        LuxembourgQueryExecutionRefusalDetail refusal)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(refusal);
        return new(
            topology, familyOutcomes, relationFamilyAcquisitions, [], [], null, null, null,
            null, null, null, refusal);
    }

    /// <summary>Always present: minting it cannot fail, and it is useful context on a refusal too.</summary>
    public SourceProfileTopology Topology { get; }

    public IReadOnlyList<LuxembourgFamilyEnumerationOutcome> FamilyOutcomes { get; }

    public IReadOnlyList<LuxembourgRelationFamilyAcquisition> RelationFamilyAcquisitions { get; }

    /// <summary>
    /// The exact set of publisher URIs <see cref="LuxembourgQueryExecutionAdapter.BuildResourceObservations"/>
    /// derived one <see cref="LuxembourgResourceObservation"/> for this run, in the census family's
    /// own delivery order. Empty when this run did not derive resource observations at all (no
    /// census family designated) or was refused before derivation completed.
    /// </summary>
    public IReadOnlyList<string> ResourceObservationSubjects { get; }

    /// <summary>
    /// D1-04b's reviewer fold-in: per subject and per <see cref="LuxembourgResourceObservationExclusionCause"/>,
    /// how many "assertion-rows" family rows this run excluded from that subject's own derived
    /// <see cref="LuxembourgResourceObservation.Assertions"/> list. Without this, a subject whose
    /// every row was excluded this way is indistinguishable in <see cref="ResourceObservationSubjects"/>
    /// from a subject with genuinely zero rows in the assertion family at all. Empty whenever
    /// <see cref="ResourceObservationSubjects"/> is (no derivation ran, or this result is refused),
    /// and also whenever a derivation ran but excluded nothing.
    /// </summary>
    public IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> ResourceObservationExclusions { get; }

    /// <summary>
    /// Present if and only if this result is delivered. A consumer that reads
    /// <see cref="ScopeManifestReceipt"/> without also reading this field cannot tell a run that
    /// proved every family from one that did not; see the type remarks on
    /// <see cref="LuxembourgQueryExecutionCompletion"/>.
    /// </summary>
    public LuxembourgQueryExecutionCompletion? Completion { get; }

    /// <summary>
    /// The custody store's own write receipt for the scope manifest bytes, never a bare
    /// <see cref="DurableBlobRef"/>: this type carries the receipt exactly as
    /// <c>RoutedHttpAcquisitionSession</c> carries the ones it holds, so nothing in this assembly
    /// separately re-holds an unreceipted content address (the Decision 80 fence
    /// <c>DurableBlobReceiptFamilyIngestSurfaceTests.NoProducerOfRefOrPolicyEvidenceExistsInIngest</c>
    /// pins). Present if and only if this result is delivered.
    /// </summary>
    public DurableBlobWriteReceipt? ScopeManifestReceipt { get; }

    /// <summary>
    /// The manifest's own canonical identity from <see cref="ScopeManifestCanonicalWriter.Write"/>
    /// -- a domain-separated hash, deliberately distinct from <see cref="ScopeManifestReceipt"/>'s
    /// custody content address. Present if and only if <see cref="ScopeManifestReceipt"/> is.
    /// </summary>
    public string? ScopeManifestCanonicalSha256 { get; }

    /// <summary>
    /// D1-06c-LU-2: this run's own real per-ordinal document-acquisition outcomes, exactly what was
    /// handed to <see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/>. Present iff this run
    /// delivered. An empty dictionary is the honest and, today, the universal answer for a real LU
    /// run: see <see cref="LuxembourgQueryExecutionAdapter.RunDocumentAcquisitionAsync"/>'s own
    /// remarks on the body axis.
    /// </summary>
    public IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? DocumentAcquisitionOutcomesByOrdinal { get; }

    /// <summary>This run's own durably written corpus/6 record set reference. Present iff delivered.</summary>
    public SourceArtifactRef? CorpusRecordSetRef { get; }

    /// <summary>
    /// The record set as reopened and verified by
    /// <see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/> itself, never the in-memory set
    /// this run computed. Present iff delivered.
    /// </summary>
    public VerifiedCorpusRecordSet? CorpusRecordSet { get; }

    public LuxembourgQueryExecutionRefusalDetail? Refusal { get; }
}

/// <summary>
/// D1-04: the Luxembourg query-execution adapter. Mints the LU <see cref="SourceProfileTopology"/>,
/// runs the already-merged repeated-enumeration executor over the requested families to prove their
/// enumeration completeness, reuses the already-merged R5.1 pipeline
/// (<see cref="VerifiedLuxembourgSourceProfile.Resolve"/> then
/// <see cref="VerifiedLuxembourgSourceProfile.ReduceScope"/>) exactly once per run, and carries the
/// resulting scope manifest forward as held evidence: written through
/// <see cref="ScopeManifestCanonicalWriter"/>, retained in <see cref="ICustodyStore"/>, reopened by
/// its own digest and floor-checked before this run claims to hold it (Decision 71's floor
/// discipline, applied here exactly as the executor applies it to its own evidence -- never
/// bypassed, never re-derived as a separate rule).
/// </summary>
/// <remarks>
/// <para>
/// Runs in process against the object graph the executor and the R5.1 pipeline return. This is an
/// accepted, permanent constraint per the D1-04 design-synthesis ruling
/// (lex-event-20260903T192615392Z-b13dee192bd84cea970b71cd8ffd4b89): the receipt and the scope
/// comparison have no wire form and need none, because re-verification re-derives the comparison
/// from custody (Decision 75's rule that a run holds what it depends on, extended here to this
/// adapter's own manifest write).
/// </para>
/// <para>
/// D1-04b closed the observation-decoding residue D1-04a named: <c>RunAsync</c> derives its own
/// <see cref="LuxembourgResourceObservation"/> values from real delivered rows, rather than trusting
/// a caller-supplied list. It does this through
/// <see cref="Lex.V3.Contracts.Source.Core.VerifiedRepeatedEnumerationRows.TryOpen"/> (queue item
/// 17): this adapter reopens a family's pages from custody by the exact digests its own
/// <see cref="RepeatedEnumerationDeliveryReceipt.Delivery"/> names, in page order, assembling each
/// <see cref="Lex.V3.Contracts.Source.Core.RepeatedEnumerationResolvedEvidence"/> from that page's
/// own plan, input, render receipt, logical request, HTTP evidence and write receipt -- never minted
/// anew or faked -- then lets item 17 independently re-parse and re-verify every row before this
/// adapter ever reads one. A verified <c>RepeatedEnumerationRow</c>'s <c>Terms</c> are mapped to an
/// observation by looking up the interpretation profile's own named projection variables, never by
/// positional index, so a template whose column order differs cannot silently mismap. This reopen
/// (<see cref="ReopenAndVerifyFamilyRowsAsync"/>) does not care which family it reopens: it is the
/// same private method for both families D1-04b drives below, generalized from the single-family
/// form D1-04b's own first pass built, rather than a second copy.
/// </para>
/// <para>
/// The reviewer's ruling on D1-04b's first-pass fork
/// (lex-event-20260904T023842960Z-3b559fba1e3c46dba3ef496e401d96f3, over the NOTE at
/// lex-event-20260904T023643784Z-f87a1781e3ae45b88c0a263d9d7a1249) settled which family carries what:
/// the "subjects" family (set "S") projects only <c>STR(?subject)</c> and is the census -- D1-04a's
/// own binding to it was correct, it bound the census, never the content. The "assertion-rows"
/// family (set "A") projects <c>subject, predicate, object, object_kind, datatype_iri,
/// language_tag</c> and is the actual content. <c>RunAsync</c> now designates and proves both
/// families in the same run (<paramref name="resourceObservationFamilyKey"/> for S,
/// <paramref name="resourceAssertionsFamilyKey"/> for A) and binds them by IDENTITY-SET EQUALITY,
/// never a count: every subject A's own decoded rows name must be a member of S's own delivered key
/// set, refused as <see cref="LuxembourgQueryExecutionRefusal.ObservationSubjectNotInDeliveredCensus"/>
/// otherwise, and every key S actually delivered yields exactly one derived observation -- carrying
/// A's real assertions when A has rows for that subject, or honestly empty assertions when it does
/// not (a real "this resource has no assertions this run observed", which the merged
/// <c>LuxembourgScopeResolver</c> is left free to keep typing however it already does; that is a
/// resolver-layer question this slice does not touch). See <see cref="BuildResourceObservations"/>.
/// </para>
/// <para>
/// D1-04c closed the cover-chain gap D1-04b named: when the census or assertion family's own
/// single-partition pass (<see cref="LuxembourgRepeatedEnumerationExecutor.RunPartitionAsync"/>)
/// refuses <see cref="LuxembourgEnumerationRefusal.PartitionRequired"/> and a caller-supplied
/// <see cref="LuxembourgPartitionCover"/> chain exists for that family, <c>RunAsync</c> drives
/// <see cref="LuxembourgRepeatedEnumerationExecutor.RunCoverAsync"/> over it and reconciles the
/// leaves (see <see cref="LuxembourgFamilyEnumerationOutcomeKind.CoverProven"/> and the
/// <c>families</c> parameter's own remarks above). No measured live count exists for either family
/// against the publisher's 1,000,000-row selection ceiling -- no production crawl has run under V3
/// yet -- and this adapter does not assert one: the cover chain makes the eventual partition depth a
/// runtime outcome the publisher's own data determines, never a design-time constant this code
/// assumes. A family with no supplied cover, or whose pass refuses for any other reason, is reported
/// as an ordinary refused family outcome exactly as before.
/// </para>
/// <para>
/// Said plainly, because it is easy to read the cover-chain machinery above as more than it is:
/// this adapter has no split strategy of its own. A caller-supplied <see cref="LuxembourgPartitionChain"/>
/// is exactly as much of a caller-supplied input as <paramref name="families"/> itself; nothing here
/// computes where to split a partition that saturates. So a census or assertion family that refuses
/// <see cref="LuxembourgEnumerationRefusal.PartitionRequired"/> with no cover supplied, or whose
/// supplied cover does not reconcile, is refused with a typed outcome
/// (<see cref="LuxembourgFamilyEnumerationOutcomeKind.ExecutorRefused"/> or
/// <see cref="LuxembourgFamilyEnumerationOutcomeKind.CoverRefused"/> respectively) -- production
/// cannot and does not paper over a family this large today. That remains true until D1-04d (queued
/// separately, not this slice) builds a real production split strategy: the simplest one on the
/// table bisects the refused partition at the last cursor of its first delivered page and recurses
/// on the right leaf, documented as correct and slow.
/// </para>
/// </remarks>
public sealed class LuxembourgQueryExecutionAdapter
{
    private readonly ICustodyStore _custodyStore;
    private readonly LuxembourgRepeatedEnumerationExecutor _executor;
    private readonly VerifiedLuxembourgSourceProfile _sourceProfile;

    /// <summary>
    /// Queue item 19: the publisher-neutral reopen half of D1-04b's own reopen glue, constructed
    /// from this adapter's own <see cref="_custodyStore"/> so nothing here holds a second,
    /// independent custody dependency (Decision 78).
    /// </summary>
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgQueryExecutionAdapter(
        ICustodyStore custodyStore,
        LuxembourgRepeatedEnumerationExecutor executor,
        VerifiedLuxembourgSourceProfile sourceProfile)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _sourceProfile = sourceProfile ?? throw new ArgumentNullException(nameof(sourceProfile));
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(_custodyStore);
    }

    /// <summary>
    /// Runs one D1-04 slice: enumerates <paramref name="families"/>, derives this run's own
    /// <see cref="LuxembourgResourceObservation"/> values (see <see cref="BuildResourceObservations"/>),
    /// then reuses the merged R5.1 pipeline exactly once over them.
    /// </summary>
    /// <param name="families">
    /// One partition request, its already-bound source witness, and an optional cover chain, per
    /// family to enumerate. Passed as a direct parameter rather than bundled into a request record's
    /// property: a plain by-value parameter of
    /// <see cref="LuxembourgRepeatedEnumerationExecutor.RunPartitionAsync"/>'s own input type is not
    /// a new way to construct or hold one, exactly as that method's own <c>request</c> parameter
    /// already is not; a record wrapping the same values in a property would be
    /// (<c>LuxembourgExecutorConstructionSurfaceTests.ARunRequestIsAnOpenInputRecord</c> pins that
    /// nothing besides construction and that one consuming parameter holds a
    /// <see cref="LuxembourgPartitionRunRequest"/>). One partition's own <c>Partition.PartitionId</c>
    /// is its family key -- the identifier <see cref="AbsenceFamilyEnumerationProof"/> matches
    /// against -- so there is no separate family-key field to drift from it.
    /// <para>
    /// D1-04c: a family's own <c>Cover</c> element is null when this run has no fallback for that
    /// family's partition saturating (the ordinary case, and the only one before this slice). When
    /// non-null and that family is the designated census or assertion family
    /// (<paramref name="resourceObservationFamilyKey"/> or <paramref name="resourceAssertionsFamilyKey"/>)
    /// and its single-partition pass refuses <see cref="LuxembourgEnumerationRefusal.PartitionRequired"/>,
    /// <c>RunAsync</c> drives <see cref="LuxembourgRepeatedEnumerationExecutor.RunCoverAsync"/> over the
    /// supplied chain and reconciles the leaves through <see cref="LuxembourgPartitionCover.TryCreate"/>;
    /// the family's own rows are then the union of every leaf's own independently reopened and
    /// re-verified rows (see <see cref="LuxembourgFamilyEnumerationOutcomeKind.CoverProven"/>). This
    /// adapter never computes a split boundary itself -- no such computation exists anywhere in this
    /// codebase today (<c>LuxembourgPartitionChain.SplitLeaf</c> takes an explicit caller-supplied
    /// boundary cursor, and every existing chain in this codebase's own tests is built the same way);
    /// a chain is exactly as much of a caller-supplied input as <paramref name="families"/> itself,
    /// never invented here. A cover supplied for a family whose pass does NOT refuse
    /// <c>PartitionRequired</c>, or for the relation-assertions family, is simply unused: only a
    /// census or assertion family's own <c>PartitionRequired</c> refusal drives it, per the scope
    /// ruling this slice implements.
    /// </para>
    /// </param>
    /// <param name="relationAssertionsFamilyKey">
    /// Which entry of <paramref name="families"/>, if any, is the LU relation-assertions family
    /// (query-plan set "G") Decision 64 accounting is projected from. Null when this run does not
    /// enumerate relations at all.
    /// </param>
    /// <param name="resourceObservationFamilyKey">
    /// Which entry of <paramref name="families"/>, if any, is the LU resource-discovery census
    /// family (query-plan set "S", template "subjects") this run derives one
    /// <see cref="LuxembourgResourceObservation"/> per delivered key from. Null means this run does
    /// not census resources at all, exactly as a null <paramref name="relationAssertionsFamilyKey"/>
    /// means it does not census relations; when null, <paramref name="resourceAssertionsFamilyKey"/>
    /// must also be null (<c>RunAsync</c> throws <see cref="ArgumentException"/> otherwise -- a
    /// caller-contract violation, not a domain refusal). When non-null, the named family must be
    /// among <paramref name="families"/> and its enumeration must be
    /// <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/>, or <c>RunAsync</c> refuses with
    /// <see cref="LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven"/>; its proven
    /// delivered rows must then independently re-verify when reopened from custody through
    /// <see cref="Lex.V3.Contracts.Source.Core.VerifiedRepeatedEnumerationRows.TryOpen"/>, or
    /// <c>RunAsync</c> refuses with
    /// <see cref="LuxembourgQueryExecutionRefusal.ResourceObservationRowsNotVerified"/>. There is no
    /// caller-supplied <c>observations</c> parameter any more (D1-04a's own residue, closed here):
    /// every observation this run resolves scope over is this run's own, independently re-derived
    /// data, never a hand-supplied set the run never actually enumerated.
    /// </param>
    /// <param name="resourceAssertionsFamilyKey">
    /// Which entry of <paramref name="families"/>, if any, is the LU assertion-content family
    /// (query-plan set "A", template "assertion-rows") this run derives real
    /// <see cref="LuxembourgObservedAssertion"/> values from, joined by subject to the census
    /// <paramref name="resourceObservationFamilyKey"/> names. Must be null exactly when
    /// <paramref name="resourceObservationFamilyKey"/> is null. Proven and reopened the same way as
    /// the census family (same two refusal codes on the same two failure shapes); once both families'
    /// rows are in hand, every subject A's own rows name must be a member of S's own delivered key
    /// set or <c>RunAsync</c> refuses with
    /// <see cref="LuxembourgQueryExecutionRefusal.ObservationSubjectNotInDeliveredCensus"/> -- an
    /// identity-set membership test over both families' own decoded rows, never a count.
    /// </param>
    /// <remarks>
    /// SECOND SUMMARY ELEMENT IN ONE DOC COMMENT, now a remark. Nothing warned, because
    /// GenerateDocumentationFile is unset, so the duplicate sat here unseen.
    /// D1-04c item 2: the caller-facing door. Never accepts an evidence resolver from outside --
    /// production code cannot hand this run an arbitrary admission answer. This run's own
    /// <see cref="LuxembourgProductionScopeReductionEvidenceResolver"/> is constructed internally,
    /// from this exact run's own custody store, its own independently re-derived observations, and
    /// its own resolved evidence-artifact set (see the six-parameter internal overload below for the
    /// shared implementation).
    /// </remarks>
    public Task<LuxembourgQueryExecutionResult> RunAsync(
        IReadOnlyList<(
            LuxembourgPartitionRunRequest PartitionRequest,
            BoundMachineRequest SourceWitness,
            LuxembourgPartitionChain? Cover)> families,
        string? relationAssertionsFamilyKey,
        string? resourceObservationFamilyKey,
        string? resourceAssertionsFamilyKey,
        MachineQueryRendererSource documentFetchRendererSource,
        CancellationToken cancellationToken) =>
        RunAsync(
            families, relationAssertionsFamilyKey, resourceObservationFamilyKey, resourceAssertionsFamilyKey,
            evidenceResolver: null, documentFetchRendererSource, cancellationToken);

    /// <summary>
    /// D1-04c item 2: the test-only seam. <paramref name="evidenceResolver"/>, when supplied,
    /// substitutes for this run's own <see cref="LuxembourgProductionScopeReductionEvidenceResolver"/>
    /// entirely -- reachable only from this assembly and <c>Lex.V3.Ingest.Tests</c>
    /// (<c>InternalsVisibleTo</c> already grants that; no widening), never from the public
    /// five-parameter overload production code calls. Null (the five-parameter overload's own only
    /// caller shape) means "construct the real thing": <see cref="LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync"/>
    /// against this run's own <see cref="_custodyStore"/>, its own <c>observations</c> derived below,
    /// and its own <c>resolved.OrderedEvidenceArtifacts</c> -- never a caller-supplied set.
    /// </summary>
    /// <param name="evidenceResolver">
    /// Test-only. Null in every production call (the five-parameter overload always passes null).
    /// </param>
    internal async Task<LuxembourgQueryExecutionResult> RunAsync(
        IReadOnlyList<(
            LuxembourgPartitionRunRequest PartitionRequest,
            BoundMachineRequest SourceWitness,
            LuxembourgPartitionChain? Cover)> families,
        string? relationAssertionsFamilyKey,
        string? resourceObservationFamilyKey,
        string? resourceAssertionsFamilyKey,
        IScopeReductionEvidenceResolver? evidenceResolver,
        MachineQueryRendererSource documentFetchRendererSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        if ((resourceObservationFamilyKey is null) != (resourceAssertionsFamilyKey is null))
        {
            throw new ArgumentException(
                "A resource-observation census family key and its assertion-rows family key must " +
                "both be null (this run does not census resources at all) or both be provided (the " +
                "census supplies the resource identities, the assertion family supplies their real " +
                "content, joined by subject identity-set membership).",
                nameof(resourceAssertionsFamilyKey));
        }

        var topology = LuxembourgSourceProfileTopology.Mint(_sourceProfile);

        var outcomes = new List<LuxembourgFamilyEnumerationOutcome>(families.Count);
        AbsenceFamilyEnumerationProof? relationProof = null;
        string? relationIncompleteReason = null;
        var sawRelationFamily = false;
        var censusLegs = new List<FamilyRowsLeg>();
        var assertionLegs = new List<FamilyRowsLeg>();

        foreach (var (partitionRequest, sourceWitness, cover) in families)
        {
            ArgumentNullException.ThrowIfNull(partitionRequest);
            var familyKey = partitionRequest.Partition.PartitionId;
            var isRelationFamily = string.Equals(
                familyKey, relationAssertionsFamilyKey, StringComparison.Ordinal);
            var isCensusFamily = string.Equals(
                familyKey, resourceObservationFamilyKey, StringComparison.Ordinal);
            var isAssertionFamily = string.Equals(
                familyKey, resourceAssertionsFamilyKey, StringComparison.Ordinal);

            var runResult = await _executor.RunPartitionAsync(
                    partitionRequest, sourceWitness, cancellationToken)
                .ConfigureAwait(false);

            if (runResult.Receipt is { } receipt)
            {
                var proof = receipt.TryProveFamilyEnumeration(familyKey, out var proofRefusal);
                if (proof is not null)
                {
                    outcomes.Add(LuxembourgFamilyEnumerationOutcome.Proven(familyKey, proof));
                    if (isRelationFamily)
                    {
                        sawRelationFamily = true;
                        relationProof = proof;
                    }

                    if (isCensusFamily)
                    {
                        censusLegs.Add(new FamilyRowsLeg(proof, receipt, partitionRequest));
                    }

                    if (isAssertionFamily)
                    {
                        assertionLegs.Add(new FamilyRowsLeg(proof, receipt, partitionRequest));
                    }
                }
                else
                {
                    outcomes.Add(LuxembourgFamilyEnumerationOutcome.ProofRefused(familyKey, proofRefusal));
                    if (isRelationFamily)
                    {
                        sawRelationFamily = true;
                        relationIncompleteReason = $"proof_refused:{proofRefusal}";
                    }
                }
            }
            else if ((isCensusFamily || isAssertionFamily) && cover is not null &&
                runResult.Refusal!.Code == LuxembourgEnumerationRefusal.PartitionRequired)
            {
                // D1-04c item 1: the census or assertion family's own single-partition pass
                // saturated. A cover chain was supplied for exactly this family, so drive it rather
                // than accepting the ordinary refusal below.
                var coverOutcome = await DriveCoverReconciliationAsync(
                        partitionRequest, cover, sourceWitness, cancellationToken)
                    .ConfigureAwait(false);
                if (coverOutcome.Legs is { } legs)
                {
                    outcomes.Add(LuxembourgFamilyEnumerationOutcome.CoverProven(familyKey, coverOutcome.LeafProofs!));
                    if (isCensusFamily)
                    {
                        censusLegs.AddRange(legs);
                    }

                    if (isAssertionFamily)
                    {
                        assertionLegs.AddRange(legs);
                    }
                }
                else
                {
                    outcomes.Add(LuxembourgFamilyEnumerationOutcome.CoverRefused(familyKey, coverOutcome.Refusal!));
                }
            }
            else
            {
                outcomes.Add(LuxembourgFamilyEnumerationOutcome.ExecutorRefused(familyKey, runResult.Refusal!));
                if (isRelationFamily)
                {
                    sawRelationFamily = true;
                    relationIncompleteReason = $"executor_refused:{runResult.Refusal!.Code}";
                }
            }
        }

        var relationAcquisitions = BuildRelationFamilyAcquisitions(
            relationAssertionsFamilyKey, sawRelationFamily, relationProof, relationIncompleteReason);

        // D1-04b: derive this run's own observations from the two designated families' own proven,
        // independently re-verified rows, rather than trusting a caller-supplied list. Refuses
        // before Resolve/ReduceScope ever sees anything, so an unproven or unverified family never
        // reaches the scope manifest at all.
        IReadOnlyList<LuxembourgResourceObservation> observations;
        AbsenceFamilyEnumerationProof? assertionFamilyProof = null;
        IReadOnlyList<string> resourceObservationSubjects = [];
        IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> resourceObservationExclusions = [];
        if (resourceObservationFamilyKey is null)
        {
            // Symmetric with BuildRelationFamilyAcquisitions' own "did not try this run" case: no
            // designation is the ordinary empty run, not a refusal. The constructor-level guard above
            // already requires resourceAssertionsFamilyKey to also be null here.
            observations = [];
        }
        else
        {
            var censusOutcome = FindProvenOutcome(outcomes, resourceObservationFamilyKey);
            if (censusOutcome is null || censusLegs.Count == 0)
            {
                return LuxembourgQueryExecutionResult.Refused(
                    topology,
                    outcomes,
                    relationAcquisitions,
                    new LuxembourgQueryExecutionRefusalDetail(
                        LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven,
                        null,
                        $"the designated resource-observation census family '{resourceObservationFamilyKey}' " +
                        "was not proven by this run's enumeration."));
            }

            var assertionOutcome = FindProvenOutcome(outcomes, resourceAssertionsFamilyKey!);
            if (assertionOutcome is null || assertionLegs.Count == 0)
            {
                return LuxembourgQueryExecutionResult.Refused(
                    topology,
                    outcomes,
                    relationAcquisitions,
                    new LuxembourgQueryExecutionRefusalDetail(
                        LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven,
                        null,
                        $"the designated resource-assertion family '{resourceAssertionsFamilyKey}' was not " +
                        "proven by this run's enumeration."));
            }

            // D1-04c: unions across every leg -- one leg for the ordinary single-partition path
            // (unchanged behavior), or one leg per cover leaf when the family's pass saturated and
            // reconciled through DriveCoverReconciliationAsync above. Each leg is still reopened and
            // independently re-verified through the exact same door (ReopenAndVerifyFamilyRowsAsync,
            // item 17's TryOpen); only the union is new.
            var (censusRows, censusProfile, censusRefusal) = await ReopenAndVerifyFamilyRowsUnionAsync(
                    censusLegs, cancellationToken)
                .ConfigureAwait(false);
            if (censusRows is null)
            {
                return LuxembourgQueryExecutionResult.Refused(
                    topology,
                    outcomes,
                    relationAcquisitions,
                    new LuxembourgQueryExecutionRefusalDetail(
                        LuxembourgQueryExecutionRefusal.ResourceObservationRowsNotVerified,
                        null,
                        $"the designated resource-observation census family '{resourceObservationFamilyKey}' " +
                        $"rows did not reverify: {censusRefusal}."));
            }

            var (assertionRows, assertionProfile, assertionRefusal) = await ReopenAndVerifyFamilyRowsUnionAsync(
                    assertionLegs, cancellationToken)
                .ConfigureAwait(false);
            if (assertionRows is null)
            {
                return LuxembourgQueryExecutionResult.Refused(
                    topology,
                    outcomes,
                    relationAcquisitions,
                    new LuxembourgQueryExecutionRefusalDetail(
                        LuxembourgQueryExecutionRefusal.ResourceObservationRowsNotVerified,
                        null,
                        $"the designated resource-assertion family '{resourceAssertionsFamilyKey}' rows did " +
                        $"not reverify: {assertionRefusal}."));
            }

            var buildResult = BuildResourceObservations(
                censusRows, censusProfile!, assertionRows, assertionProfile!,
                assertionLegs[0].PartitionRequest.InvariantPlan.SelectorPredicates);
            if (buildResult.Kind != ResourceObservationBuildOutcomeKind.Built)
            {
                var refusalCode = buildResult.Kind switch
                {
                    ResourceObservationBuildOutcomeKind.SubjectNotInCensus =>
                        LuxembourgQueryExecutionRefusal.ObservationSubjectNotInDeliveredCensus,
                    ResourceObservationBuildOutcomeKind.ObjectKindNotRecognised =>
                        LuxembourgQueryExecutionRefusal.AssertionRowObjectKindNotRecognised,
                    ResourceObservationBuildOutcomeKind.TermUnbound =>
                        LuxembourgQueryExecutionRefusal.AssertionRowTermUnbound,
                    _ => throw new InvalidOperationException(
                        $"Unreachable: BuildResourceObservations returned an unhandled outcome kind " +
                        $"'{buildResult.Kind}'."),
                };
                var detail = buildResult.Kind == ResourceObservationBuildOutcomeKind.SubjectNotInCensus
                    ? $"the subject '{buildResult.Detail}' has a row in the assertion family " +
                      $"'{resourceAssertionsFamilyKey}' but is not a member of the census family " +
                      $"'{resourceObservationFamilyKey}'s own delivered key set."
                    : buildResult.Detail;
                return LuxembourgQueryExecutionResult.Refused(
                    topology,
                    outcomes,
                    relationAcquisitions,
                    new LuxembourgQueryExecutionRefusalDetail(refusalCode, null, detail));
            }

            observations = buildResult.Observations!;
            // The proof this run actually holds for the assertion family these observations were
            // derived from: FindProvenOutcome above refused the run without it, and
            // ReopenAndVerifyFamilyRowsUnionAsync refused it again unless the delivered rows
            // re-verified from custody. A cover chain proves the same family through its leaves, so
            // either shape supplies it.
            assertionFamilyProof = assertionOutcome.Proof ?? assertionOutcome.CoverLeafProofs?[0];
            resourceObservationSubjects = observations.Select(static o => o.ObjectRef.PublisherUri).ToArray();
            resourceObservationExclusions = buildResult.Exclusions!;
        }

        // The door, not a guard: scope resolution and the body join can only read observations
        // carried by a proof object, so nothing downstream has to check (or be named after) the
        // fact that this family was proven. An empty run designates no family and so has no proof
        // and no observations; RequireProven is reached only on the designated path.
        if (observations.Count != 0 && assertionFamilyProof is null)
        {
            // Unreachable: observations exist only on the designated branch, which refuses the run
            // above unless the family is proven. Typed rather than thrown, for the same reason the
            // rest of this method never throws past a refusal.
            return LuxembourgQueryExecutionResult.Refused(
                topology,
                outcomes,
                relationAcquisitions,
                new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven,
                    null,
                    "this run derived resource observations without holding the assertion family's " +
                    "own enumeration proof."));
        }

        var resolution = _sourceProfile.Resolve(assertionFamilyProof is null
            ? LuxembourgProvenResourceObservations.NoFamilyDesignated()
            : LuxembourgProvenResourceObservations.RequireProven(assertionFamilyProof, observations));
        if (resolution is LuxembourgProfileResolution.Failed failed)
        {
            return LuxembourgQueryExecutionResult.Refused(
                topology,
                outcomes,
                relationAcquisitions,
                new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.ScopeResolutionFailed, failed.Failure, null));
        }

        var resolved = (LuxembourgProfileResolution.Resolved)resolution;

        // D1-04c item 2: this run's own production evidence resolver, constructed here rather than
        // accepted from a caller -- from this exact run's own custody store, its own independently
        // re-derived observations, and its own resolved evidence-artifact set
        // (resolved.OrderedEvidenceArtifacts, minted by LuxembourgScopeResolver.Resolve above from
        // these same observations, never a caller-hand-transcribed set). The test-only
        // evidenceResolver parameter substitutes entirely when supplied; production callers (the
        // public five-parameter RunAsync overload) always pass null here.
        var resolver = evidenceResolver ?? await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(
                _custodyStore, _sourceProfile.Snapshot.CompleteEnumerationRef, observations,
                resolved.OrderedEvidenceArtifacts, cancellationToken)
            .ConfigureAwait(false);
        // D1-06c-LU-2 item 1: mint every addressable object's own document-fetch address from the
        // store's own isExemplifiedBy file URI, and carry it onto the durable manifest row. Before
        // this, every Luxembourg row was NotMinted with reason NoPublisherRouteYet, which was true
        // when there was no LU route and is a lie now that there is one.
        var mintedAddressesByObjectRef = MintDocumentFetchAddresses(resolved);
        var manifest = _sourceProfile.ReduceScope(
            resolved,
            resolver,
            mintedAddressesByObjectRef.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.ToScopeManifestFetchAddress()));

        // ScopeManifestCanonicalWriter.Write returns the manifest's OWN canonical identity: a
        // domain-separated hash (SHA256("lex-v3-source-scope-manifest/1\n" + bytes)), never written
        // to the stream itself. It is a different, independent identifier from the custody store's
        // own content address (plain SHA256(bytes)) and the two are never expected to be equal;
        // both are retained below rather than one silently standing in for the other.
        using var manifestStream = new MemoryStream();
        var manifestCanonicalSha256 = ScopeManifestCanonicalWriter.Write(manifestStream, manifest);
        var manifestBytes = manifestStream.ToArray();

        // RULING lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 interpreting Decision 71: held under an enforced floor and held
        // under a weaker one are both HELD; only a write that errored or bytes that cannot be
        // reproduced at their own digest are a custody failure.
        var (manifestReceipt, manifestHoldFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, manifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (manifestReceipt is null)
        {
            return LuxembourgQueryExecutionResult.Refused(
                topology,
                outcomes,
                relationAcquisitions,
                new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained,
                    null,
                    $"The scope manifest could not be held: {manifestHoldFailure}"));
        }

        var writeReceipt = manifestReceipt;

        // Re-verified by reopening the exact digest from the store, not trusted from the write call
        // alone: a receipt names bytes, a reopen proves the store actually holds them.
        // ReadByDigestCheckedAsync itself already throws CustodyIntegrityException unless the
        // returned bytes hash to writeReceipt.Reference.ContentSha256, which the store computed
        // from manifestBytes at CreateAsync above; a follow-on SequenceEqual against manifestBytes
        // here would only be re-deriving what that digest check already establishes (fold-in seven
        // of the D1-04 refreeze -- the executor's own delivery proof removed the same redundant
        // check after a checked read for the same reason).
        // THIS REOPEN HAD NO CATCH. ReadByDigestCheckedAsync throws CustodyIntegrityException
        // when the store cannot reproduce the bytes at their own digest, and that exception
        // escaped RunAsync untyped, past every typed refusal this method exists to produce and
        // past the principle its own neighbouring tests assert by name. A store can accept the
        // write, satisfy the hold's verification read, and still fail a later read; that is a
        // custody failure on our side, one of the four legitimate reasons a law goes unheld, and
        // it is reported as one rather than thrown at the caller.
        ReadOnlyMemory<byte> reopened;
        try
        {
            reopened = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore, writeReceipt.Reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CustodyIntegrityException exception)
        {
            return LuxembourgQueryExecutionResult.Refused(
                topology,
                outcomes,
                relationAcquisitions,
                new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained,
                    null,
                    "The scope manifest could not be reopened at its own digest: "
                    + exception.Message));
        }

        // Decision 75's rule, applied to this adapter's own artifact: re-verification re-derives
        // the comparison from custody rather than trusting the in-memory object this run already
        // held. VerifiedScopeManifest.ParseAndVerify (the item 14 reader door) independently
        // deserializes the reopened bytes, re-runs every one of the fourteen
        // ScopeManifestReaderOnlyInvariant checks against the same evidence resolver, and requires
        // canonical re-serialization to reproduce the exact bytes -- proving the durably held copy
        // is a genuinely self-consistent scope manifest, not merely byte-identical to what this run
        // computed. The artifact-ref resourceId here is a fresh, unretained local identifier: it
        // never enters any retained canonical form or leaves this method, so it carries none of
        // Decision 77's per-bind-random-identifier concern (that ruling is about an identifier
        // baked into a *retained* policy's own canonical bytes).
        var manifestArtifactRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}", manifestCanonicalSha256);
        var reopenedManifest = VerifiedScopeManifest
            .ParseAndVerify(manifestArtifactRef, reopened.Span, resolver)
            .Manifest;

        // This run's own identity for the corpus/6 record set it writes as its last step, paired
        // with real evidence -- this exact run's own manifest custody-write digest, distinct from
        // manifestArtifactRef's own canonical digest above -- rather than an inert placeholder,
        // mirroring EuQueryExecutionAdapter's own runIdentityRef exactly.
        var runIdentityRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}", writeReceipt.Reference.ContentSha256);

        var (documentAcquisitionOutcomesByOrdinal, acquisitionRefusal) =
            await RunDocumentAcquisitionAsync(
                    reopenedManifest, mintedAddressesByObjectRef, documentFetchRendererSource,
                    cancellationToken)
                .ConfigureAwait(false);
        if (acquisitionRefusal is not null)
        {
            return LuxembourgQueryExecutionResult.Refused(
                topology, outcomes, relationAcquisitions, acquisitionRefusal);
        }

        // D1-06c-LU-2 item 5: this run's whole corpus/6 record set, written as the LITERAL last
        // step, after the manifest above and after every document GET this run attempted. Reuses
        // this run's own scope-manifest custody floor (CustodyClass.NightlyFloor90d), the exact
        // constant CorpusRecordSetWriter itself already requires. The outcomes are handed over
        // unfiltered and need no second filter: RunDocumentAcquisitionAsync's own gate already means
        // every key in the dictionary names an accepted-body ordinal. An object with no outcome
        // still gets a real record -- CorpusRecordBuilder's default path makes it NotHeld, naming
        // the manifest's own disposition as the reason.
        var recordSetWriter = new CorpusRecordSetWriter(_custodyStore);
        var recordSetResult = await recordSetWriter.WriteAsync(
                reopenedManifest, manifestArtifactRef, runIdentityRef,
                documentAcquisitionOutcomesByOrdinal, cancellationToken)
            .ConfigureAwait(false);
        if (recordSetResult.Refusal is not null)
        {
            return LuxembourgQueryExecutionResult.Refused(
                topology, outcomes, relationAcquisitions,
                new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.RecordSetNotRetained,
                    null,
                    recordSetResult.Refusal.Detail));
        }

        return LuxembourgQueryExecutionResult.Delivered(
            topology, outcomes, relationAcquisitions, resourceObservationSubjects,
            resourceObservationExclusions, writeReceipt, manifestCanonicalSha256,
            documentAcquisitionOutcomesByOrdinal!, recordSetResult.SetRef!,
            recordSetResult.VerifiedSet!);
    }

    /// <summary>
    /// D1-06c-LU-2 items 1 and 2: every object this run can address, and the ONE manifestation the
    /// selection ladder picks for it. Pure over this run's own already-resolved data: no second
    /// query, no network.
    /// </summary>
    /// <remarks>
    /// Each candidate comes from the object's own resolved WEMI topology, which walked
    /// isRealizedBy/isEmbodiedBy/isExemplifiedBy across that observation's own assertions. Only
    /// structurally consistent candidates are offered, which is what makes the file URI safe: that
    /// disposition already requires <c>LuxembourgItemUriFamily.IsCurrent</c> (http, host
    /// data.legilux.public.lu, path strictly under /filestore/) and every candidate IRI already
    /// passed <c>RequireExactResourceIri</c> (no userinfo, query or fragment, default port), which
    /// together are exactly <see cref="LuxembourgFileUri.RequireValid"/>'s own conditions. So the
    /// validator cannot refuse a candidate that reaches it; it is still called, because validating
    /// once at the door is what stops an unvalidated string reaching selection at all.
    /// <para>
    /// The act's own ELI page path is the object IRI's own path. The WEMI walk starts at that IRI
    /// and reaches the manifestation through the work-to-expression-to-manifestation chain, so
    /// "the act's page path obtained from the store via the manifestation to expression to work
    /// relation" (RULING lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143) is that
    /// walk read backwards, and needs no extra query.
    /// </para>
    /// <para>
    /// LIMIT, stated rather than hidden: a manifest row carries exactly one fetch address, so this
    /// mints one document per object even when the object offers manifestations in several
    /// languages. The tie-break is total (legal value, token, then the store URI's ordinal order),
    /// so the choice is deterministic rather than arbitrary, but multi-language acquisition is not
    /// in this slice and is not pretended to be.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress>
        MintDocumentFetchAddresses(LuxembourgProfileResolution.Resolved resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var minted = new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress>();
        foreach (var resource in resolution.Resources)
        {
            var address = MintDocumentFetchAddress(
                resource.ObjectRef,
                resource.WemiTopology,
                resource.Assertions.Select(static resolved => resolved.Assertion).ToArray());
            if (address is not null)
            {
                minted[resource.ObjectRef] = address;
            }
        }

        return minted;
    }

    /// <summary>
    /// One object's own address, or null when the store offers it no selectable manifestation. Split
    /// out of <see cref="MintDocumentFetchAddresses"/> so a test can drive the real WEMI join and the
    /// real selection ladder against real assertions, without first assembling a whole resolved
    /// profile (which would need rights-channel observations this decision reads nothing from).
    /// </summary>
    internal static LuxembourgDocumentFetchAddress? MintDocumentFetchAddress(
        SourceObjectRef objectRef,
        LuxembourgWemiTopologyResolution wemiTopology,
        IReadOnlyList<LuxembourgObservedAssertion> assertions)
    {
        ArgumentNullException.ThrowIfNull(objectRef);
        ArgumentNullException.ThrowIfNull(wemiTopology);
        ArgumentNullException.ThrowIfNull(assertions);

        var actEliPagePath = new Uri(objectRef.PublisherUri, UriKind.Absolute).AbsolutePath;
        var candidates = new List<LuxembourgManifestationCandidate>();
        foreach (var wemi in wemiTopology.Candidates)
        {
            if (wemi.Disposition != LuxembourgWemiCandidateDisposition.StructurallyConsistent)
            {
                continue;
            }

            if (LuxembourgAuthorityIri.TryParseUserFormat(wemi.FormatIri) is not { } token)
            {
                // A real store token this route does not select: html, doc, docx or svg. Not an
                // error and not a refusal, simply not a wording candidate for this route.
                continue;
            }

            if (FindLegalValue(assertions, wemi.ManifestationIri) is not { } legalValue)
            {
                // Reached only when the publisher states TWO DIFFERENT legal values for one
                // manifestation, which is the store disagreeing with itself. Neither is chosen and
                // the manifestation stops being a candidate.
                //
                // It used to be reached for a second reason as well, and that was the defect. An
                // ABSENT marker dropped the manifestation too, on the reasoning that the ladder
                // ranked on the marker so an unmarked file must not be silently promoted over a
                // marked one. That reasoning was sound while legal value outranked format, and the
                // amendment (RULING lex-event-20260904T194018108Z-62079c93ce9d405ca1fb326cfea41bd9)
                // inverts it: format is primary, so an unmarked file is not promoted by anything,
                // it simply keeps its own format's place. Dropping it removed the very
                // manifestations D49 prefers, since 99.5 percent of plain xml and 42 percent of
                // xml-akomantoso carry no marker, and it made an expression whose files were all
                // unmarked report an absence that was not one. Absence is now the typed
                // LuxembourgLegalValue.Unstated state, carried into selection, never read as "not
                // official".
                continue;
            }

            candidates.Add(new LuxembourgManifestationCandidate(
                token, legalValue, LuxembourgFileUri.RequireValid(wemi.ItemIri)));
        }

        var selection = LuxembourgManifestationSelection.Select(candidates);
        return selection.Selected is { } selected
            ? LuxembourgDocumentFetchAddress.Create(
                selected.FileUri, selected.Token, selected.LegalValue, actEliPagePath)
            : null;
    }

    /// <summary>
    /// The publisher's own jolux:legalValue marker for one manifestation, read from this object's
    /// own resolved assertions by exact subject and predicate.
    /// <see cref="LuxembourgLegalValue.Unstated"/> when the store states none, which is the common
    /// case; null ONLY when the store states two different values for the same manifestation, which
    /// is publisher data disagreeing with itself.
    /// </summary>
    /// <remarks>
    /// Absence and conflict were the same answer here before the amendment, and that conflation is
    /// what let "no marker" be handled as "unusable". They are separate now: one is a fact about
    /// the publisher having said nothing, the other is a fact about the publisher contradicting
    /// itself, and only the second is a reason to refuse a manifestation.
    /// </remarks>
    private static LuxembourgLegalValue? FindLegalValue(
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        string manifestationIri)
    {
        LuxembourgLegalValue? found = null;
        foreach (var assertion in assertions)
        {
            if (!string.Equals(assertion.SubjectIri, manifestationIri, StringComparison.Ordinal) ||
                !string.Equals(assertion.PredicateIri, JoluxLegalValue, StringComparison.Ordinal) ||
                assertion.ObjectKind != LuxembourgAssertionObjectKind.Iri)
            {
                continue;
            }

            if (LuxembourgAuthorityIri.TryParseLegalValue(assertion.ObjectIriOrLexical) is not { } value)
            {
                continue;
            }

            if (found is not null && found != value)
            {
                // Two different markers on one manifestation is publisher data disagreeing with
                // itself. Neither is chosen: the manifestation stops being a candidate.
                return null;
            }

            found = value;
        }

        return found ?? LuxembourgLegalValue.Unstated;
    }

    /// <summary>
    /// The publisher's own jolux:legalValue predicate IRI. Spelled out here rather than read from
    /// <c>VerifiedLuxembourgSourceProfile.JoluxPrefix</c>, which is internal to Contracts, and
    /// pinned against that prefix by
    /// <c>TheLegalValuePredicateIriIsTheStoresOwnJoluxLegalValuePredicate</c> so the literal cannot
    /// drift from the ontology the resolver itself reads.
    /// </summary>
    internal const string JoluxLegalValue =
        "http://data.legilux.public.lu/resource/ontology/jolux#legalValue";

    /// <summary>
    /// D1-06c-LU-2 items 3, 4 and 5: drives the document GET for every reopened manifest row whose
    /// own body axis is <see cref="ScopeDisposition.AcceptedSelected"/> and whose fetch address this
    /// run actually minted, classifies the real response, and prepares this run's own
    /// <see cref="CorpusAcquisitionOutcome"/> per ordinal. The gate is computed before the loop and
    /// gates iteration directly, exactly as the merged EU adapter's own gate does: no fetch attempt
    /// at all for a row the body axis excludes.
    /// </summary>
    /// <remarks>
    /// WHAT FRACTION OF A REAL LU MANIFEST IS ACCEPTED, answered plainly because the scope ruling
    /// asked for it: ZERO of N, on every LU manifest this codebase can produce, and it is
    /// structural rather than incidental. <c>LuxembourgBodyJoin.ResolveCandidate</c> attaches eight
    /// unconditional milestone blockers to every candidate and returns
    /// <c>LuxembourgBodyCandidateDisposition.Withheld</c> on every path, and
    /// <c>LuxembourgScopeResolver.ResolveBody</c> has no <c>AcceptedCandidate</c> branch at all: its
    /// four arms are NeverIngest, the family's own quarantine or point state, a point for a missing
    /// family, and a typed quarantine. So the Body/AcceptedSelected accounting set is empty for
    /// every real run and this loop attempts nothing. That is the same honest position the EU route
    /// is in for its own reason, and it is why the tests here drive this method directly against a
    /// manifest built with a genuine accepted body axis.
    /// <para>
    /// This corrects a premise in the scope ruling itself, which said the LU body axis is derived
    /// from the store's userFormat listing so that "xml or pdf listed means a body admitted". The
    /// FORMAT axis is derived that way (<c>ResolveFormat</c>: xml and xml-akomantoso accepted, pdf,
    /// pdfa and html a point, doc, docx and svg never); the BODY axis is not, and never accepts.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The real per-ordinal outcomes this run's GETs produced, or a whole-run refusal for a cause
    /// this door's own closed <see cref="CorpusAcquisitionRefusalReason"/> vocabulary cannot
    /// represent. Never both, never neither.
    /// </returns>
    internal async Task<(
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? Outcomes,
        LuxembourgQueryExecutionRefusalDetail? Refusal)> RunDocumentAcquisitionAsync(
        ScopeManifest reopenedManifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> mintedAddressesByObjectRef,
        MachineQueryRendererSource documentFetchRendererSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reopenedManifest);
        ArgumentNullException.ThrowIfNull(mintedAddressesByObjectRef);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);

        var bodyAcceptedOrdinals = new HashSet<int>();
        foreach (var accountingSet in reopenedManifest.Accounting)
        {
            if (accountingSet.Axis == ScopeAxis.Body &&
                accountingSet.Disposition == ScopeDisposition.AcceptedSelected)
            {
                foreach (var ordinal in accountingSet.ObjectOrdinals)
                {
                    bodyAcceptedOrdinals.Add(ordinal);
                }
            }
        }

        var outcomesByOrdinal = new Dictionary<int, CorpusAcquisitionOutcome>();
        for (var rowOrdinal = 0; rowOrdinal < reopenedManifest.Rows.Count; rowOrdinal++)
        {
            var row = reopenedManifest.Rows[rowOrdinal];
            if (row.FetchAddress.Status != ScopeManifestFetchAddressStatus.Minted ||
                !bodyAcceptedOrdinals.Contains(rowOrdinal))
            {
                continue;
            }

            var mintedObjectRef = reopenedManifest.ObservedObjects[rowOrdinal].ObjectRef;
            if (!mintedAddressesByObjectRef.TryGetValue(mintedObjectRef, out var address))
            {
                // Unreachable in practice: every Minted row's address came from this exact run's own
                // MintDocumentFetchAddresses, the only path that mints one. Refusing the whole run
                // here rather than throwing keeps this method's "never throws past a typed refusal"
                // discipline even for a defect this loop cannot itself introduce.
                return (null, new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.DocumentGetOutcomeNotRepresentable,
                    null,
                    $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}') carries a Minted " +
                    "fetch address this run never itself minted."));
            }

            var bound = new LuxembourgDocumentFetchPlan(address).Bind(
                $"urn:uuid:{Guid.NewGuid():D}",
                $"urn:uuid:{Guid.NewGuid():D}",
                documentFetchRendererSource);
            var attempt = await _executor.RunDocumentGetAsync(
                    bound.Request, [address.ActEliPagePath], cancellationToken)
                .ConfigureAwait(false);
            if (attempt.Evidence is null)
            {
                if (attempt.Refusal == LuxembourgDocumentGetAttemptRefusal.RobotsDisallowed)
                {
                    // The publisher's own robots.txt refused THIS document, on one of the three
                    // paths the ruling evaluates. That is this one object's own cause, never a
                    // whole-run refusal: one withheld act must not block every other act's record.
                    outcomesByOrdinal[rowOrdinal] = CorpusAcquisitionOutcome.Refused(
                        CorpusAcquisitionRefusalReason.RobotsDisallowed);
                    continue;
                }

                return (null, new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.DocumentFetchSessionNotStarted,
                    null,
                    $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): code=" +
                    $"{attempt.Refusal} detail={attempt.Detail}."));
            }

            var evidence = attempt.Evidence;
            if (evidence.Outcome is CompleteHttpRouteOutcome && evidence.Hops.Count > 0)
            {
                var classified = LuxembourgDocumentGetOutcome.FromObservedStatus(
                    evidence.Hops[^1].Status, attempt.RetryAllowanceSpent);
                if (classified.Kind == LuxembourgDocumentGetOutcomeKind.Retrieved)
                {
                    // THE SECOND MEMBER OF THE SAME CLASS as the manifest reopen above. This
                    // checked read also throws CustodyIntegrityException when the store cannot
                    // reproduce the body at its own digest, and it too escaped RunAsync untyped.
                    // Found by grepping ReadByDigestCheckedAsync against catch over this file,
                    // which is the sweep that should have run when the first one was typed: two
                    // checked reads, one catch clause in the whole file.
                    ReadOnlyMemory<byte> bodyBytes;
                    try
                    {
                        bodyBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                                _custodyStore, evidence.Hops[^1].Sha256, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (CustodyIntegrityException exception)
                    {
                        return (null, new LuxembourgQueryExecutionRefusalDetail(
                            LuxembourgQueryExecutionRefusal.DocumentBodyNotHeld,
                            null,
                            $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): the "
                            + $"body could not be reopened at its own digest: {exception.Message}"));
                    }

                    // This refused unless the receipt classified as Floored, which meant no body
                    // could be held outside Azure and stopped the acceptance canary at a wall that
                    // had nothing to do with the publisher or this route. RULING lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 interpreting Decision 71:
                    // a store that wrote the bytes and can reproduce them at their own digest and
                    // honestly declares NotEnforced did not fail. The membership class is recorded
                    // rather than gated on: CorpusBodyRecord.Held derives its own Floor from this
                    // receipt and serialises it, so the record says under which guarantee it holds.
                    // A GENUINE failure still refuses and never softens into a weaker class, and
                    // BOTH halves of that sentence are now true of the code they sit beside: a
                    // write error refuses at the hold below, and bytes that do not reopen at
                    // their own digest refuse at the checked read above. Until this cycle the
                    // second half was true of the hold and FALSE of the read, which threw.
                    var (bodyReceipt, holdFailure) = await CustodyHold
                        .TryHoldAsync(_custodyStore, bodyBytes, cancellationToken)
                        .ConfigureAwait(false);
                    if (bodyReceipt is null)
                    {
                        return (null, new LuxembourgQueryExecutionRefusalDetail(
                            LuxembourgQueryExecutionRefusal.DocumentBodyNotHeld,
                            null,
                            $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): {holdFailure}"));
                    }

                    outcomesByOrdinal[rowOrdinal] = CorpusAcquisitionOutcome.Held(bodyReceipt);
                    continue;
                }

                outcomesByOrdinal[rowOrdinal] = CorpusAcquisitionOutcome.Refused(
                    MapDocumentGetKind(classified.Kind));
                continue;
            }

            if (TryMapHopIncompleteToCorpusAcquisitionRefusal(evidence, out var hopRefusal))
            {
                outcomesByOrdinal[rowOrdinal] = CorpusAcquisitionOutcome.Refused(hopRefusal);
                continue;
            }

            var routeOutcomeDetail = evidence.Outcome is IncompleteHttpRouteOutcome incompleteOutcome
                ? $"{evidence.Outcome.GetType().Name}({incompleteOutcome.Reason})"
                : evidence.Outcome.GetType().Name;
            return (null, new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.DocumentGetOutcomeNotRepresentable,
                null,
                $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): " +
                $"routeOutcome={routeOutcomeDetail}."));
        }

        return (outcomesByOrdinal, null);
    }

    /// <summary>
    /// This route's own closed GET vocabulary onto D1-06b's own closed corpus vocabulary, one
    /// member to one member under the identical wire spelling. Every member here is genuinely
    /// reachable on THIS route and nothing is mapped that is not: <c>Retrieved</c> never reaches
    /// this method (it is the held path), and <c>RobotsDisallowed</c> never does either, because a
    /// robots refusal never produces a status to classify and is mapped at its own branch above.
    /// </summary>
    private static CorpusAcquisitionRefusalReason MapDocumentGetKind(
        LuxembourgDocumentGetOutcomeKind kind) => kind switch
    {
        LuxembourgDocumentGetOutcomeKind.NotFound => CorpusAcquisitionRefusalReason.NotFound,
        LuxembourgDocumentGetOutcomeKind.Gone => CorpusAcquisitionRefusalReason.Gone,
        LuxembourgDocumentGetOutcomeKind.RetryExhausted => CorpusAcquisitionRefusalReason.RetryExhausted,
        LuxembourgDocumentGetOutcomeKind.UnexpectedPublisherStatus =>
            CorpusAcquisitionRefusalReason.UnexpectedPublisherStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Whether a hop-level transport-incomplete outcome has a faithful member in D1-06b's own closed
    /// <see cref="CorpusAcquisitionRefusalReason"/> vocabulary, which mirrors
    /// <see cref="HttpAcquisitionReasonRegistry"/>'s own fourteen reasons one for one under the
    /// identical wire names, so a hop's already-checked registry member maps by its wire key alone,
    /// never by re-deriving or guessing one. Everything else returns false and refuses the run.
    /// </summary>
    private static bool TryMapHopIncompleteToCorpusAcquisitionRefusal(
        RoutedHttpEvidence evidence,
        out CorpusAcquisitionRefusalReason mapped)
    {
        if (evidence.Outcome is IncompleteHttpRouteOutcome { Reason: HttpRouteIncompleteReason.HopIncomplete } &&
            evidence.Hops.Count > 0 &&
            evidence.Hops[^1].Completion is IncompleteHttpCompletion incomplete &&
            incomplete.Reason.RegistryRef == HttpAcquisitionReasonRegistry.RegistryRef)
        {
            var candidate = incomplete.Reason.MemberKey switch
            {
                "body_deadline" => CorpusAcquisitionRefusalReason.BodyDeadline,
                "body_read_failure" => CorpusAcquisitionRefusalReason.BodyReadFailure,
                "byte_bound_prevented_completion" => CorpusAcquisitionRefusalReason.ByteBoundPreventedCompletion,
                "caller_cancelled_after_headers" => CorpusAcquisitionRefusalReason.CallerCancelledAfterHeaders,
                "declared_length_short_read" => CorpusAcquisitionRefusalReason.DeclaredLengthShortRead,
                "missing_completion_proof" => CorpusAcquisitionRefusalReason.MissingCompletionProof,
                "transfer_coding_conflict" => CorpusAcquisitionRefusalReason.TransferCodingConflict,
                "invalid_content_length" => CorpusAcquisitionRefusalReason.InvalidContentLength,
                "unsupported_transfer_coding" => CorpusAcquisitionRefusalReason.UnsupportedTransferCoding,
                "header_deadline" => CorpusAcquisitionRefusalReason.HeaderDeadline,
                "transport_before_headers" => CorpusAcquisitionRefusalReason.TransportBeforeHeaders,
                "revalidation_request_not_admitted" => CorpusAcquisitionRefusalReason.RevalidationRequestNotAdmitted,
                "status_content_forbidden" => CorpusAcquisitionRefusalReason.StatusContentForbidden,
                "status_framing_conflict" => CorpusAcquisitionRefusalReason.StatusFramingConflict,
                _ => (CorpusAcquisitionRefusalReason?)null,
            };
            if (candidate is { } value)
            {
                mapped = value;
                return true;
            }
        }

        mapped = default;
        return false;
    }

    /// <summary>
    /// Finds <paramref name="familyKey"/> among <paramref name="outcomes"/>, returning it only when
    /// it was actually proven this run -- a missing key and a found-but-not-proven key are both "no
    /// usable outcome" to every caller of this method, which report the difference themselves (or
    /// don't need to). D1-04c: proven now means <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/>
    /// (one partition) or <see cref="LuxembourgFamilyEnumerationOutcomeKind.CoverProven"/> (a
    /// reconciled cover chain) -- both are a whole enumeration this run holds proof for, differing
    /// only in how many partitions that proof spans.
    /// </summary>
    private static LuxembourgFamilyEnumerationOutcome? FindProvenOutcome(
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> outcomes, string familyKey)
    {
        var outcome = outcomes.FirstOrDefault(
            candidate => string.Equals(candidate.FamilyKey, familyKey, StringComparison.Ordinal));
        return outcome is {
            Kind: LuxembourgFamilyEnumerationOutcomeKind.Proven or LuxembourgFamilyEnumerationOutcomeKind.CoverProven,
        }
            ? outcome
            : null;
    }

    /// <summary>
    /// D1-04c: one already-proven leg of a census or assertion family's own rows -- either the
    /// family's single partition (the ordinary path, exactly one leg), or one cover leaf (one leg per
    /// leaf of a reconciled <see cref="LuxembourgPartitionCover"/>). <see cref="ReopenAndVerifyFamilyRowsUnionAsync"/>
    /// reopens every leg through the same door and unions the rows; nothing about a leg's own
    /// reopening differs by how it was produced.
    /// </summary>
    private sealed record FamilyRowsLeg(
        AbsenceFamilyEnumerationProof Proof,
        RepeatedEnumerationDeliveryReceipt Receipt,
        LuxembourgPartitionRunRequest PartitionRequest);

    /// <summary>The result of driving a census or assertion family's cover-chain fallback.</summary>
    private sealed record CoverReconciliationOutcome(
        IReadOnlyList<AbsenceFamilyEnumerationProof>? LeafProofs,
        IReadOnlyList<FamilyRowsLeg>? Legs,
        LuxembourgPartitionCoverReconciliationDetail? Refusal);

    /// <summary>
    /// D1-04c item 1: drives <see cref="LuxembourgRepeatedEnumerationExecutor.RunCoverAsync"/> over
    /// <paramref name="chain"/> (one session for every leaf, per that method's own contract), then
    /// reconciles the leaves through <see cref="LuxembourgPartitionCover.TryCreate"/> and mints each
    /// leaf's own <see cref="AbsenceFamilyEnumerationProof"/> by its own leaf partition id -- never the
    /// root family key, which <see cref="AbsenceFamilyEnumerationProof.TryCreate"/> would refuse
    /// <c>PartitionIsNotThisFamily</c> against a leaf's own delivery. Every refusal here is checked in
    /// the order the chain reconciles: a leaf that itself refused, then the cover reconciliation
    /// itself, then a leaf's own proof. A chain that cannot reconcile at any of these steps is a typed
    /// family refusal (<see cref="LuxembourgPartitionCoverReconciliationDetail"/>), never a raw
    /// exception.
    /// </summary>
    private async Task<CoverReconciliationOutcome> DriveCoverReconciliationAsync(
        LuxembourgPartitionRunRequest rootRequest,
        LuxembourgPartitionChain chain,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        var leafResults = await _executor.RunCoverAsync(rootRequest, chain, sourceWitness, cancellationToken)
            .ConfigureAwait(false);

        // RunCoverAsync's own contract: exactly one result per chain leaf, in leaf order, whether or
        // not it delivered.
        for (var index = 0; index < leafResults.Count; index++)
        {
            if (leafResults[index].Receipt is null)
            {
                return new CoverReconciliationOutcome(
                    null, null,
                    LuxembourgPartitionCoverReconciliationDetail.LeafExecutorRefused(
                        chain.Leaves[index].PartitionId, leafResults[index].Refusal!));
            }
        }

        // This adapter never holds a root receipt to hand TryCreate here: the root pass refused
        // PartitionRequired before delivering (that refusal is exactly what routed this method's own
        // caller here), so rootReceipt is always null and cover.Basis is therefore always
        // LuxembourgPartitionCoverBasis.LeafTilingOnly, never the root-reconciled alternative -- an
        // inherent constraint of this call path, not a choice made here. cover itself is discarded
        // immediately below once non-null: only its refusal (when null) and, through leafReceipts
        // directly, each leaf's own proof are read; nothing here consumes cover.Basis or any other
        // field of the minted cover object.
        var leafReceipts = leafResults.Select(static result => result.Receipt!).ToArray();
        var cover = LuxembourgPartitionCover.TryCreate(chain, leafReceipts, rootReceipt: null, out var coverRefusal);
        if (cover is null)
        {
            return new CoverReconciliationOutcome(
                null, null, LuxembourgPartitionCoverReconciliationDetail.ReconciliationRefused(coverRefusal));
        }

        var leafProofs = new List<AbsenceFamilyEnumerationProof>(chain.Leaves.Count);
        var legs = new List<FamilyRowsLeg>(chain.Leaves.Count);
        for (var index = 0; index < chain.Leaves.Count; index++)
        {
            var leaf = chain.Leaves[index];
            var receipt = leafReceipts[index];
            var proof = receipt.TryProveFamilyEnumeration(leaf.PartitionId, out var leafProofRefusal);
            if (proof is null)
            {
                return new CoverReconciliationOutcome(
                    null, null,
                    LuxembourgPartitionCoverReconciliationDetail.LeafProofRefused(
                        leaf.PartitionId, leafProofRefusal));
            }

            leafProofs.Add(proof);
            legs.Add(new FamilyRowsLeg(proof, receipt, rootRequest with { Partition = leaf }));
        }

        return new CoverReconciliationOutcome(leafProofs, legs, null);
    }

    /// <summary>
    /// D1-04c: reopens and independently re-verifies every <paramref name="legs"/> entry through
    /// <see cref="ReopenAndVerifyFamilyRowsAsync"/> (unchanged: item 19's shared reopen glue, item 17's
    /// <c>TryOpen</c> door), then returns the UNION of every leg's own verified rows, in leg order --
    /// the census or assertion family's own rows, whether they came from one ordinary partition (one
    /// leg) or a reconciled cover chain (one leg per leaf). The identity binding in
    /// <see cref="BuildResourceObservations"/> below is unchanged either way: it reads a plain row
    /// list and does not know or care how many partitions it was assembled from.
    /// </summary>
    private async Task<(
        IReadOnlyList<RepeatedEnumerationRow>? Rows,
        RepeatedEnumerationInterpretationProfile? Profile,
        string? Refusal)> ReopenAndVerifyFamilyRowsUnionAsync(
        IReadOnlyList<FamilyRowsLeg> legs,
        CancellationToken cancellationToken)
    {
        var allRows = new List<RepeatedEnumerationRow>();
        RepeatedEnumerationInterpretationProfile? profile = null;
        foreach (var leg in legs)
        {
            var (rows, legProfile, refusal) = await ReopenAndVerifyFamilyRowsAsync(
                    leg.Proof, leg.Receipt, leg.PartitionRequest, cancellationToken)
                .ConfigureAwait(false);
            if (rows is null)
            {
                return (
                    null, null,
                    $"leaf '{leg.PartitionRequest.Partition.PartitionId}' did not reverify: {refusal}");
            }

            profile ??= legProfile;
            allRows.AddRange(rows);
        }

        return (allRows, profile, null);
    }

    /// <summary>
    /// Reopens one already-proven family's own delivered pages from custody by the exact digests its
    /// own receipt names, in page order, and hands them to item 17's door
    /// (<see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>) together with the family's proof and
    /// the comparison that minted it. Returns the verified rows and the interpretation profile they
    /// were read under (needed by the caller to map <c>Terms</c> by projection name), or a null row
    /// list plus the specific <see cref="RepeatedEnumerationRowsOpenRefusal"/> reason.
    /// <para>
    /// Generalized by D1-04b from the single-family form its own first pass built: this method reads
    /// nothing "resource"-specific off <paramref name="receipt"/> or <paramref name="partitionRequest"/>
    /// -- it reopens whichever family <paramref name="partitionRequest"/>'s own <c>SetId</c> and
    /// <paramref name="receipt"/>'s own delivery name, so the same code now serves both the "subjects"
    /// census family and the "assertion-rows" content family from <c>RunAsync</c> above, rather than a
    /// second copy differing only in field names.
    /// </para>
    /// </summary>
    private async Task<(
        IReadOnlyList<RepeatedEnumerationRow>? Rows,
        RepeatedEnumerationInterpretationProfile Profile,
        RepeatedEnumerationRowsOpenRefusal Refusal)> ReopenAndVerifyFamilyRowsAsync(
        AbsenceFamilyEnumerationProof proof,
        RepeatedEnumerationDeliveryReceipt receipt,
        LuxembourgPartitionRunRequest partitionRequest,
        CancellationToken cancellationToken)
    {
        var delivery = receipt.Delivery;

        // Deterministic from the same invariant plan, resource id and set id the executor itself
        // derived its profile from (LuxembourgRepeatedEnumerationExecutor.RunPartitionOnSessionAsync):
        // reconstructing it here mints no new artifact and cannot legitimately disagree with
        // delivery.InterpretationProfileRef, which TryOpen itself checks the reconstruction against
        // before trusting anything.
        var profile = partitionRequest.InvariantPlan.CreateDeliveryProfile(
            partitionRequest.InvariantPlanResourceId, partitionRequest.SetId);

        // "PagesA" here is the delivery comparison's own first independent pass (as opposed to its
        // second, "PagesB"), not query-plan set "A" ("assertion-rows") -- the two are an unrelated
        // naming coincidence. Either family this method is called for (set "S" or set "A") reopens
        // its own first pass the identical way.
        var pages = new List<RepeatedEnumerationResolvedEvidence>(delivery.PagesA.Pages.Count);
        foreach (var pageRef in delivery.PagesA.Pages.OrderBy(static page => page.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(pageRef.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof, delivery, profile, delivery.InterpretationProfileRef, delivery.CountA.HttpEvidenceRef,
            pages, out var refusal);
        return (rows, profile, refusal);
    }

    /// <summary>
    /// The projection variable that names the resource's own publisher IRI in the "subjects"
    /// census family's delivery profile (<c>LuxembourgQueryPlan.CreateDeliveryProfile</c>'s
    /// <c>DeliveryProjectionVariables("subjects")</c>: an empty template-specific prefix followed by
    /// the generic <c>key_1..key_6</c> cursor columns, with the subject IRI bound to <c>key_1</c> and
    /// <c>key_2..key_6</c> always the empty string). Looked up by name against
    /// <see cref="RepeatedEnumerationInterpretationProfile.ProjectionVariables"/>, never assumed to
    /// be positional index 0, so a family whose template prefixes extra columns before it still maps
    /// correctly.
    /// </summary>
    private const string ResourceIdentityProjectionVariable = "key_1";

    /// <summary>The "assertion-rows" family's own named projection variables (<c>LuxembourgQueryPlan.DeliveryProjectionVariables("assertion-rows")</c>), looked up by name for the same reason as <see cref="ResourceIdentityProjectionVariable"/>.</summary>
    private const string AssertionSubjectProjectionVariable = "subject";

    private const string AssertionPredicateProjectionVariable = "predicate";
    private const string AssertionObjectProjectionVariable = "object";
    private const string AssertionObjectKindProjectionVariable = "object_kind";
    private const string AssertionDatatypeProjectionVariable = "datatype_iri";
    private const string AssertionLanguageProjectionVariable = "language_tag";

    // D1-04c: the three object_kind tokens used to be duplicated here as private consts,
    // independently restating "iri", "literal" and "unsupported_blank_node" from
    // LuxembourgQueryPlan.BuildTemplates' own SPARQL BIND. Both call sites now read the one
    // public constant LuxembourgQueryPlan defines, so a future change to a token cannot drift
    // between the query text that produces it and the adapter code that classifies it.
    private const string AssertionObjectKindIri = LuxembourgQueryPlan.AssertionObjectKindIri;

    private const string AssertionObjectKindLiteral = LuxembourgQueryPlan.AssertionObjectKindLiteral;
    private const string AssertionObjectKindUnsupportedBlankNode =
        LuxembourgQueryPlan.AssertionObjectKindUnsupportedBlankNode;

    /// <summary>
    /// D1-04b's real derivation, per the reviewer's ruling
    /// (lex-event-20260904T023842960Z-3b559fba1e3c46dba3ef496e401d96f3): one
    /// <see cref="LuxembourgResourceObservation"/> per key <paramref name="censusRows"/> (the
    /// "subjects" family, set "S") actually delivered, carrying whichever real
    /// <see cref="LuxembourgObservedAssertion"/> values <paramref name="assertionRows"/> (the
    /// "assertion-rows" family, set "A") delivered for that same subject -- or honestly empty
    /// assertions when A has none for it.
    /// <para>
    /// The binding between the two families is IDENTITY-SET membership, never a count: every subject
    /// named by any row in <paramref name="assertionRows"/> (checked here on the RAW, unfiltered
    /// subject -- before the predicate/object-kind admission below ever runs, so a subject whose only
    /// A rows get filtered out below still had to pass this membership check) must be a member of the
    /// key set <paramref name="censusRows"/> actually delivered. The first subject that fails this
    /// check is returned as <see cref="ResourceObservationBuildResult.SubjectNotInCensus"/> and no
    /// observations are built at all; the caller turns that into
    /// <see cref="LuxembourgQueryExecutionRefusal.ObservationSubjectNotInDeliveredCensus"/>.
    /// This is a genuine set comparison over both families' own decoded rows, not a row-count
    /// comparison in disguise: two families that deliver the same COUNT of distinct subjects but
    /// disagree on which subjects they are still refuses here.
    /// </para>
    /// <para>
    /// Set A's own predicate filter (<c>LuxembourgQueryPlan.BuildTemplates</c>' <c>predicateValues</c>
    /// for template "assertion-rows") is the union of every AssertionPredicate AND RelationPredicate
    /// vocabulary IRI, because this one family harvests every predicate value the LU dataset can
    /// produce for either purpose. Only rows whose predicate is in
    /// <paramref name="assertionPredicateVocabulary"/> (the AssertionPredicate-kind IRIs; the merged
    /// <c>LuxembourgQueryPlan.SelectorPredicates</c> this run's own invariant plan already carries)
    /// become a <see cref="LuxembourgObservedAssertion"/>: the merged
    /// <c>LuxembourgScopeResolver.ValidateObservation</c> (out of this slice's path claim, and not
    /// touched) hard-requires every assertion's predicate to be an AssertionPredicate-vocabulary IRI,
    /// so admitting a RelationPredicate-only row here (say, "cites" or "modifies", both common) would
    /// fail scope resolution for essentially every real LU resource. A relation-predicate row is real
    /// content, but it is relation content: it belongs to <see cref="LuxembourgObservedRelation"/>,
    /// sourced from the unrelated "relation-assertions" family (set "G") through this adapter's
    /// existing, unchanged relation machinery, not to <see cref="LuxembourgResourceObservation.Assertions"/>.
    /// A row this admission skips is not lost data: it is data this method was never asked to carry.
    /// </para>
    /// <para>
    /// A row whose <c>object_kind</c> is <see cref="AssertionObjectKindUnsupportedBlankNode"/> is
    /// excluded the same way: <see cref="LuxembourgObservedAssertion.ObjectKind"/>
    /// (<see cref="LuxembourgAssertionObjectKind"/>) admits only <c>Iri</c> or <c>Literal</c>, so a
    /// blank-node object cannot be represented by this type at all -- the query plan's own template
    /// names this shape "unsupported_blank_node" for exactly this reason. This is the query plan's
    /// own documented boundary, not a delivery-integrity problem this method refuses over.
    /// </para>
    /// <para>
    /// D1-04b's reviewer fold-in: both exclusions above used to be a bare <c>continue</c> -- no
    /// count, no typed state, nothing recorded anywhere -- so a subject whose every row was excluded
    /// this way was indistinguishable in the output from a subject with genuinely zero rows in A.
    /// This method now returns a real, per-subject, per-cause count of every excluded row
    /// (<see cref="ResourceObservationBuildResult.Exclusions"/>) alongside the derived observations.
    /// Separately, an unrecognised <c>object_kind</c> value or an unbound term at an expected
    /// projection position both used to throw <see cref="InvalidOperationException"/>, even though
    /// both are publisher data disagreeing with the query plan's own closed shape, not a
    /// caller-contract violation -- this method now returns a typed outcome for each instead (see
    /// <see cref="ResourceObservationBuildOutcomeKind.ObjectKindNotRecognised"/> and
    /// <see cref="ResourceObservationBuildOutcomeKind.TermUnbound"/>), which the caller turns into
    /// <see cref="LuxembourgQueryExecutionRefusal.AssertionRowObjectKindNotRecognised"/> and
    /// <see cref="LuxembourgQueryExecutionRefusal.AssertionRowTermUnbound"/> respectively.
    /// </para>
    /// </summary>
    private ResourceObservationBuildResult BuildResourceObservations(
        IReadOnlyList<RepeatedEnumerationRow> censusRows,
        RepeatedEnumerationInterpretationProfile censusProfile,
        IReadOnlyList<RepeatedEnumerationRow> assertionRows,
        RepeatedEnumerationInterpretationProfile assertionProfile,
        IReadOnlyCollection<string> assertionPredicateVocabulary)
    {
        var censusKeyIndex = RequireProjectionIndex(censusProfile, ResourceIdentityProjectionVariable);
        var subjectIndex = RequireProjectionIndex(assertionProfile, AssertionSubjectProjectionVariable);
        var predicateIndex = RequireProjectionIndex(assertionProfile, AssertionPredicateProjectionVariable);
        var objectIndex = RequireProjectionIndex(assertionProfile, AssertionObjectProjectionVariable);
        var objectKindIndex = RequireProjectionIndex(assertionProfile, AssertionObjectKindProjectionVariable);
        var datatypeIndex = RequireProjectionIndex(assertionProfile, AssertionDatatypeProjectionVariable);
        var languageIndex = RequireProjectionIndex(assertionProfile, AssertionLanguageProjectionVariable);
        var assertionPredicates = new HashSet<string>(assertionPredicateVocabulary, StringComparer.Ordinal);

        // The census: every resource identity the "subjects" family actually delivered this run,
        // preserving delivery order for the observations this method emits below.
        var censusKeys = new HashSet<string>(StringComparer.Ordinal);
        var censusOrder = new List<string>(censusRows.Count);
        foreach (var row in censusRows)
        {
            var key = row.Terms[censusKeyIndex].Value;
            if (key is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the census family's resource-identity term");
            }

            if (censusKeys.Add(key))
            {
                censusOrder.Add(key);
            }
        }

        var observationRef = _sourceProfile.Snapshot.ObservationRef;
        var assertionsBySubject = new Dictionary<string, List<LuxembourgObservedAssertion>>(StringComparer.Ordinal);
        var exclusionCounts = new Dictionary<(string Subject, LuxembourgResourceObservationExclusionCause Cause), int>();
        foreach (var row in assertionRows)
        {
            var subject = row.Terms[subjectIndex].Value;
            if (subject is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the assertion-rows family's subject term");
            }

            if (!censusKeys.Contains(subject))
            {
                return ResourceObservationBuildResult.SubjectNotInCensus(subject);
            }

            if (!assertionsBySubject.TryGetValue(subject, out var list))
            {
                list = [];
                assertionsBySubject.Add(subject, list);
            }

            var predicate = row.Terms[predicateIndex].Value;
            if (predicate is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the assertion-rows family's predicate term");
            }

            if (!assertionPredicates.Contains(predicate))
            {
                RecordExclusion(
                    exclusionCounts, subject, LuxembourgResourceObservationExclusionCause.PredicateNotAdmitted);
                continue;
            }

            var objectKind = row.Terms[objectKindIndex].Value;
            if (objectKind is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the assertion-rows family's object_kind term");
            }

            LuxembourgAssertionObjectKind definiteObjectKind;
            if (objectKind == AssertionObjectKindIri)
            {
                definiteObjectKind = LuxembourgAssertionObjectKind.Iri;
            }
            else if (objectKind == AssertionObjectKindLiteral)
            {
                definiteObjectKind = LuxembourgAssertionObjectKind.Literal;
            }
            else if (objectKind == AssertionObjectKindUnsupportedBlankNode)
            {
                RecordExclusion(
                    exclusionCounts, subject, LuxembourgResourceObservationExclusionCause.BlankNodeObject);
                continue;
            }
            else
            {
                return ResourceObservationBuildResult.ObjectKindNotRecognised(subject, objectKind);
            }

            var objectValue = row.Terms[objectIndex].Value;
            if (objectValue is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the assertion-rows family's object term");
            }

            var datatypeValue = row.Terms[datatypeIndex].Value;
            if (datatypeValue is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the assertion-rows family's datatype_iri term");
            }

            var languageValue = row.Terms[languageIndex].Value;
            if (languageValue is null)
            {
                return ResourceObservationBuildResult.TermUnbound(
                    "the assertion-rows family's language_tag term");
            }

            list.Add(new LuxembourgObservedAssertion(
                subject, predicate, definiteObjectKind, objectValue, datatypeValue, languageValue,
                observationRef));
        }

        // ObservationRef is not this method's to vary per row or per page: VerifiedLuxembourgSourceProfile's own
        // ValidateObservation (LuxembourgScopeResolver.cs) requires every observation's ObservationRef, and both
        // rights-channel wrappers' RunIdentity, to equal this exact profile-wide value -- the reviewer's ruling
        // withdrew this method's own earlier concern about a page-scoped identity (precision three, withdrawn).
        var observations = new List<LuxembourgResourceObservation>(censusOrder.Count);
        foreach (var subject in censusOrder)
        {
            IReadOnlyList<LuxembourgObservedAssertion> assertions = assertionsBySubject.TryGetValue(subject, out var list)
                ? list
                : [];
            observations.Add(BuildResourceObservation(
                subject, assertions, observationRef, _sourceProfile.ScopeBinding.SourceProfileRef));
        }

        var exclusions = exclusionCounts
            .Select(static pair => new LuxembourgResourceObservationExclusionAccounting(
                pair.Key.Subject, pair.Key.Cause, pair.Value))
            .OrderBy(static exclusion => exclusion.Subject, StringComparer.Ordinal)
            .ThenBy(static exclusion => exclusion.Cause)
            .ToArray();

        return ResourceObservationBuildResult.Built(observations, exclusions);
    }

    /// <summary>
    /// One observed resource, with BOTH rights channels attached. Extracted from
    /// <see cref="BuildResourceObservations"/> so the wiring itself is testable: driving
    /// <see cref="BuildSparqlRightsRows"/> alone proves the builder and says nothing about whether
    /// the observation actually carries what it built, which is precisely the gap that let the
    /// empty channel stand.
    /// </summary>
    internal static LuxembourgResourceObservation BuildResourceObservation(
        string subject,
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        SourceArtifactRef observationRef,
        SourceArtifactRef scopeProfileRef)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentNullException.ThrowIfNull(observationRef);
        ArgumentNullException.ThrowIfNull(scopeProfileRef);

        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(scopeProfileRef, "legal_resource"),
            subject,
            subject,
            Sha256Hex(subject),
            scopeProfileRef,
            null);
        return new LuxembourgResourceObservation(
            objectRef,
            observationRef,
            assertions,
            [],
            // Channel one, populated from this run's own proven family. jolux:license is an
            // admitted assertion predicate of the very family already reopened and re-verified
            // before this method is reached, so the licence declaration is held evidence, not a new
            // query. It used to be an empty list, which made every body candidate fail the rights
            // blocker for want of a channel nobody had asked for.
            new LuxembourgSparqlRightsChannelObservations(
                observationRef, observationRef, BuildSparqlRightsRows(assertions, observationRef)),
            // Channel two stays genuinely empty. Decision 21's in-file declaration is read out of
            // the document and cannot precede acquisition, so it resolves to the typed
            // SecondChannelPending state (D1-04f owns it). Nothing here fabricates a second
            // evidence ref to manufacture agreement: that is exactly what the channels'
            // disjointness rule exists to catch.
            new LuxembourgInFileRightsChannelObservations(observationRef, observationRef, []));
    }

    /// <summary>
    /// This object's own <c>jolux:license</c> declarations, one row per manifestation that carries
    /// one, read from the assertions the proven assertion family delivered.
    /// </summary>
    /// <remarks>
    /// Only licence IRIs the profile's own vocabulary rules are carried, and the reason is a real
    /// hazard rather than tidiness: <c>LuxembourgScopeResolver.ValidateObservation</c> refuses the
    /// WHOLE RUN with UnknownVocabularyDrift for any licence IRI on a rights channel that the
    /// profile does not know, so carrying an unruled one would let a single odd licence anywhere in
    /// the store kill every run. A manifestation whose licence is unruled therefore gets no channel
    /// row, which leaves its rights ChannelEnumerationUnproven and its body unselected, the
    /// conservative answer. Recording an unruled licence as its own typed quarantine with its own
    /// accounting is D1-04f's, beside the in-file channel; it is named residue, not a silent drop.
    /// </remarks>
    internal static IReadOnlyList<LuxembourgRightsChannelObservation> BuildSparqlRightsRows(
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        SourceArtifactRef observationRef)
    {
        var licencesByManifestation = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var assertion in assertions)
        {
            if (!string.Equals(assertion.PredicateIri, JoluxLicense, StringComparison.Ordinal) ||
                assertion.ObjectKind != LuxembourgAssertionObjectKind.Iri ||
                !Uri.TryCreate(assertion.SubjectIri, UriKind.Absolute, out _))
            {
                continue;
            }

            // EVERY licence is carried, ruled or not. The first version of this filtered to the two
            // ruled IRIs, because an unruled one on a rights channel refused the whole run; that
            // avoided the blast radius with the wrong lever, since a dropped row means the IRI
            // vanishes from the record entirely. The run-level refusal is gone (see
            // LuxembourgScopeResolver.ValidateObservation), so an unruled licence now reaches the
            // resolution as that object's own TypedQuarantineUnruledLicence state, with the IRI
            // recorded on the channel and its body not admitted, while every other object in the
            // run proceeds.
            var licence = assertion.ObjectIriOrLexical;
            if (!licencesByManifestation.TryGetValue(assertion.SubjectIri, out var set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                licencesByManifestation[assertion.SubjectIri] = set;
            }

            set.Add(licence);
        }

        return licencesByManifestation
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new LuxembourgRightsChannelObservation(
                pair.Key, observationRef, observationRef, pair.Value.ToArray()))
            .ToArray();
    }

    /// <summary>
    /// The publisher's own jolux:license predicate, and the two licence IRIs its profile rules.
    /// Spelled here because the profile's own constants are internal to Contracts, and pinned
    /// against them by <c>TheRightsChannelPredicateAndLicencesMatchTheProfilesOwnVocabulary</c>.
    /// </summary>
    internal const string JoluxLicense =
        "http://data.legilux.public.lu/resource/ontology/jolux#license";

    internal const string RuledAdmittingLicence = "http://creativecommons.org/licenses/by/4.0/";

    internal const string RuledNonAdmittingLicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    private static void RecordExclusion(
        Dictionary<(string Subject, LuxembourgResourceObservationExclusionCause Cause), int> exclusionCounts,
        string subject,
        LuxembourgResourceObservationExclusionCause cause)
    {
        var key = (subject, cause);
        exclusionCounts[key] = exclusionCounts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    /// <summary>The outcome shape <see cref="BuildResourceObservations"/> returns: a real derivation, or exactly one of the ways real publisher data can fail it, never both.</summary>
    private enum ResourceObservationBuildOutcomeKind
    {
        Built = 1,
        SubjectNotInCensus = 2,
        ObjectKindNotRecognised = 3,
        TermUnbound = 4,
    }

    /// <summary>
    /// <see cref="BuildResourceObservations"/>'s own private result type, replacing the two-element
    /// tuple its first pass returned: that tuple could only distinguish "built" from "a subject was
    /// not in the census", which stopped being enough once an unrecognised <c>object_kind</c> and an
    /// unbound term also needed to refuse rather than throw. Exactly one door mints each outcome, and
    /// only the matching payload is ever non-null for it.
    /// </summary>
    private sealed class ResourceObservationBuildResult
    {
        private ResourceObservationBuildResult(
            ResourceObservationBuildOutcomeKind kind,
            IReadOnlyList<LuxembourgResourceObservation>? observations,
            IReadOnlyList<LuxembourgResourceObservationExclusionAccounting>? exclusions,
            string? detail)
        {
            Kind = kind;
            Observations = observations;
            Exclusions = exclusions;
            Detail = detail;
        }

        public ResourceObservationBuildOutcomeKind Kind { get; }

        /// <summary>Present if and only if <see cref="Kind"/> is <see cref="ResourceObservationBuildOutcomeKind.Built"/>.</summary>
        public IReadOnlyList<LuxembourgResourceObservation>? Observations { get; }

        /// <summary>Present if and only if <see cref="Kind"/> is <see cref="ResourceObservationBuildOutcomeKind.Built"/>.</summary>
        public IReadOnlyList<LuxembourgResourceObservationExclusionAccounting>? Exclusions { get; }

        /// <summary>
        /// Present if and only if <see cref="Kind"/> is not <see cref="ResourceObservationBuildOutcomeKind.Built"/>:
        /// the failing subject alone for <see cref="ResourceObservationBuildOutcomeKind.SubjectNotInCensus"/>
        /// (the caller wraps it with the family keys it alone knows), or a complete, ready-to-surface
        /// message for the other two kinds.
        /// </summary>
        public string? Detail { get; }

        public static ResourceObservationBuildResult Built(
            IReadOnlyList<LuxembourgResourceObservation> observations,
            IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> exclusions) =>
            new(ResourceObservationBuildOutcomeKind.Built, observations, exclusions, null);

        public static ResourceObservationBuildResult SubjectNotInCensus(string subject) =>
            new(ResourceObservationBuildOutcomeKind.SubjectNotInCensus, null, null, subject);

        public static ResourceObservationBuildResult ObjectKindNotRecognised(string subject, string objectKind) =>
            new(
                ResourceObservationBuildOutcomeKind.ObjectKindNotRecognised, null, null,
                $"the assertion-rows family's object_kind term carries an unrecognised value " +
                $"'{objectKind}' for subject '{subject}'.");

        public static ResourceObservationBuildResult TermUnbound(string what) =>
            new(ResourceObservationBuildOutcomeKind.TermUnbound, null, null, $"{what} is unbound.");
    }

    private static int RequireProjectionIndex(RepeatedEnumerationInterpretationProfile profile, string variable)
    {
        var index = profile.ProjectionVariables.ToList().IndexOf(variable);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The family's interpretation profile has no '{variable}' projection variable.");
        }

        return index;
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Decision 64 projected over the LU relation predicates. All 18 predicates
    /// (<see cref="VerifiedLuxembourgSourceProfile.RelationRules"/>, Decision 65) share one
    /// acquisition state and one evidence identity, because the LU query plan enumerates every
    /// relation predicate together in one family ("relation-assertions"/set G): there is no
    /// per-predicate enumeration to prove or fail independently. Sharing the family's one proof
    /// honestly reflects that; it does not fabricate per-predicate independence the query plan does
    /// not have.
    /// </summary>
    /// <param name="designatedFamilyKey">
    /// <c>RunAsync</c>'s own <c>relationAssertionsFamilyKey</c> parameter, distinguished from "not
    /// attempted" so a caller's own inconsistency (naming a family it never actually requested) is
    /// reported as <see cref="LuxembourgRelationFamilyAcquisitionState.Uncertain"/> rather than
    /// folded into the ordinary "did not try this run" case.
    /// </param>
    private IReadOnlyList<LuxembourgRelationFamilyAcquisition> BuildRelationFamilyAcquisitions(
        string? designatedFamilyKey,
        bool attempted,
        AbsenceFamilyEnumerationProof? proof,
        string? incompleteReason)
    {
        var (state, reason) = proof is not null
            ? (LuxembourgRelationFamilyAcquisitionState.AcquiredComplete, (string?)null)
            : attempted
                ? (LuxembourgRelationFamilyAcquisitionState.Incomplete, incompleteReason)
                : designatedFamilyKey is not null
                    // The caller named a relation-assertions family key but this run's family list
                    // never actually contained it: a config/caller mismatch, not a confident "we
                    // did not try", so the honest state is uncertain rather than unacquired.
                    ? (LuxembourgRelationFamilyAcquisitionState.Uncertain,
                        $"the designated relation-assertions family key '{designatedFamilyKey}' was " +
                        "not found among this run's family results")
                    : (LuxembourgRelationFamilyAcquisitionState.Unacquired,
                        "no relation-assertions family was designated for this run");

        var predicates = _sourceProfile.RelationRules;
        var acquisitions = new List<LuxembourgRelationFamilyAcquisition>(predicates.Count);
        foreach (var rule in predicates)
        {
            acquisitions.Add(state == LuxembourgRelationFamilyAcquisitionState.AcquiredComplete
                ? LuxembourgRelationFamilyAcquisition.Complete(rule.PredicateIri, proof!)
                : LuxembourgRelationFamilyAcquisition.NotComplete(rule.PredicateIri, state, reason!));
        }

        return acquisitions;
    }

}
