using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
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

/// <summary>
/// Item 15 of the D1-04 design-synthesis ruling named the gap this enum marks: "Luxembourg
/// ScopeResolver implements bucket membership only, not R5.1's TC and RECT typed roles nor an ACC
/// constitutional review evidence gate; that is a defect in the merged resolver and gets its own
/// slice after D1-04's first freeze names the gap; D1-04 records the coarser disposition with typed
/// acquisition state so the gap stays visible, never papers over it." Item 15 has since closed that
/// gap: the resolver now carries R5.1's TC, RECT and ACC roles as their own
/// <see cref="LuxembourgTypedRoleResolution"/>, separate from and alongside the coarser
/// <c>PublicationFamily</c> bucket-membership disposition this enum names. What remains true, and
/// what this enum still marks, is that a resource carrying one of these members was accepted through
/// bucket membership at this coarse dimension; it is not a claim that the resource's typed role is
/// unresolved.
/// </summary>
/// <remarks>
/// ACC's own member is named for what item 15 actually resolved, not the lane's initial (and
/// reviewer-corrected) always-refuse reading. The reviewer RULING
/// lex-event-20260904T002301246Z-7699c8fdd1ad4868a7d94dcb152fbf57 held that R5.1 rule 6's own
/// evidence is the publisher's typeDocument assertion carrying the exact ACC IRI -- no further
/// predicate required or substitutable -- so an ACC resource is admitted through
/// <c>PriorityCandidateTypes</c> bucket membership exactly like TC and RECT, and separately carries
/// R5.1's <c>constitutional_review_decision</c> role
/// (<see cref="LuxembourgTypedRoleResolution"/>). The former
/// <c>AccConstitutionalReviewEvidenceGateNotApplied</c> name described a gate that the ruling
/// refused as contradicting the accepted text; this member is the same coarse signal TC and RECT
/// already carry, renamed to match.
/// </remarks>
public enum LuxembourgCoarseDispositionGap
{
    /// <summary>
    /// Accepted through <c>PriorityCandidateTypes</c> bucket membership only. R5.1's own TC role
    /// (its own coordinate, the consolidation-without-legal-effect disclosure, never relabeled as
    /// its base act) is not separately verified.
    /// </summary>
    [JsonStringEnumMemberName("tc_typed_role_not_distinguished")]
    TcTypedRoleNotDistinguished = 1,

    /// <summary>
    /// Accepted through <c>PriorityCandidateTypes</c> bucket membership only. R5.1's own RECT role
    /// (its own coordinate, the corrective-material disclosure, never relabeled as the corrected
    /// act) is not separately verified.
    /// </summary>
    [JsonStringEnumMemberName("rect_typed_role_not_distinguished")]
    RectTypedRoleNotDistinguished = 2,

    /// <summary>
    /// Accepted through <c>PriorityCandidateTypes</c> bucket membership only. R5.1's own ACC role
    /// (its own coordinate, the constitutional-review-decision-never-statutory-text disclosure,
    /// never treated as statutory text and never entering the legislation timeline) is not
    /// separately verified at this coarse level.
    /// </summary>
    [JsonStringEnumMemberName("acc_typed_role_not_distinguished")]
    AccTypedRoleNotDistinguished = 3,
}

/// <summary>
/// One resource whose <c>PublicationFamily</c> disposition is the coarser bucket-membership
/// acceptance item 15 names, rather than R5.1's full role-level rule. Never silently treated as
/// fully resolved.
/// </summary>
public sealed record LuxembourgCoarseDispositionMarker(
    string PublisherUri,
    string ObservedTypeDocumentIri,
    LuxembourgCoarseDispositionGap Gap);

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
}

public sealed class LuxembourgFamilyEnumerationOutcome
{
    private LuxembourgFamilyEnumerationOutcome(
        string familyKey,
        LuxembourgFamilyEnumerationOutcomeKind kind,
        AbsenceFamilyEnumerationProof? proof,
        LuxembourgEnumerationRefusalDetail? executorRefusal,
        AbsenceFamilyEnumerationProofRefusal? proofRefusal)
    {
        FamilyKey = familyKey;
        Kind = kind;
        Proof = proof;
        ExecutorRefusal = executorRefusal;
        ProofRefusal = proofRefusal;
    }

    public string FamilyKey { get; }

    public LuxembourgFamilyEnumerationOutcomeKind Kind { get; }

    public AbsenceFamilyEnumerationProof? Proof { get; }

    public LuxembourgEnumerationRefusalDetail? ExecutorRefusal { get; }

    public AbsenceFamilyEnumerationProofRefusal? ProofRefusal { get; }

    public static LuxembourgFamilyEnumerationOutcome Proven(string familyKey, AbsenceFamilyEnumerationProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.Proven, proof, null, null);
    }

    public static LuxembourgFamilyEnumerationOutcome ExecutorRefused(
        string familyKey, LuxembourgEnumerationRefusalDetail refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.ExecutorRefused, null, refusal, null);
    }

    public static LuxembourgFamilyEnumerationOutcome ProofRefused(
        string familyKey, AbsenceFamilyEnumerationProofRefusal refusal)
    {
        if (refusal == AbsenceFamilyEnumerationProofRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(familyKey, LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, null, null, refusal);
    }
}

public enum LuxembourgQueryExecutionRefusal
{
    None = 0,

    /// <summary>The merged R5.1 pipeline's <c>Resolve</c> step refused (see <see cref="LuxembourgQueryExecutionRefusalDetail.ResolutionFailure"/>).</summary>
    ScopeResolutionFailed = 1,

    /// <summary>
    /// The scope manifest was written, but the store enforced no retention floor on it, so this run
    /// cannot claim it as held evidence (never bypass the Core floor).
    /// </summary>
    ScopeManifestNotHeld = 2,

    /// <summary>
    /// <see cref="LuxembourgQueryExecutionAdapter.RunAsync"/> was given a non-null
    /// <c>resourceObservationFamilyKey</c>, but no family this run enumerated can attest one: either
    /// the key does not name any entry of this run's family results, or the entry it names is not
    /// <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/>. <see
    /// cref="Lex.V3.Contracts.Source.Core.VerifiedRepeatedEnumerationRows.TryOpen"/> can only reopen
    /// rows behind a proof that exists, so an unproven or unmatched designation refuses here rather
    /// than silently deriving zero observations from a family this run never actually censused.
    /// </summary>
    ResourceObservationFamilyNotProven = 3,

    /// <summary>
    /// The designated resource-observation family was proven, but its delivered rows did not
    /// independently re-verify through <see
    /// cref="Lex.V3.Contracts.Source.Core.VerifiedRepeatedEnumerationRows.TryOpen"/> when reopened
    /// from custody: see <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/> for the exact
    /// <see cref="Lex.V3.Contracts.Source.Core.RepeatedEnumerationRowsOpenRefusal"/> reason.
    /// </summary>
    ResourceObservationRowsNotVerified = 4,

    /// <summary>
    /// D1-04b's ruling on the two families: the "assertion-rows" family (set "A") is bound to the
    /// "subjects" census family (set "S") by IDENTITY-SET EQUALITY, never a count. This refuses when
    /// a subject appearing in A's own decoded rows is not a member of S's own delivered key set --
    /// two independent enumerations over the same triple store disagreeing about which subjects
    /// exist is a genuine data-integrity problem this adapter reports rather than silently drops.
    /// See <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/> for the exact subject.
    /// </summary>
    ObservationSubjectNotInDeliveredCensus = 5,

    /// <summary>
    /// D1-04b's reviewer fold-in: an "assertion-rows" family row's own <c>object_kind</c> projection
    /// carried a value outside the three <c>LuxembourgQueryPlan.BuildTemplates</c>' own BIND can
    /// produce (<c>"iri"</c>, <c>"literal"</c>, <c>"unsupported_blank_node"</c>). This is publisher
    /// data disagreeing with the query plan's own closed shape, not a caller-contract violation, so
    /// it refuses here rather than throwing. See <see cref="LuxembourgQueryExecutionRefusalDetail.Detail"/>
    /// for the exact subject and value.
    /// </summary>
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
    AssertionRowTermUnbound = 7,
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
        IReadOnlyList<LuxembourgCoarseDispositionMarker> coarseDispositionMarkers,
        IReadOnlyList<string> resourceObservationSubjects,
        IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> resourceObservationExclusions,
        DurableBlobWriteReceipt? scopeManifestReceipt,
        string? scopeManifestCanonicalSha256,
        LuxembourgQueryExecutionCompletion? completion,
        LuxembourgQueryExecutionRefusalDetail? refusal)
    {
        Topology = topology;
        FamilyOutcomes = familyOutcomes;
        RelationFamilyAcquisitions = relationFamilyAcquisitions;
        CoarseDispositionMarkers = coarseDispositionMarkers;
        ResourceObservationSubjects = resourceObservationSubjects;
        ResourceObservationExclusions = resourceObservationExclusions;
        ScopeManifestReceipt = scopeManifestReceipt;
        ScopeManifestCanonicalSha256 = scopeManifestCanonicalSha256;
        Completion = completion;
        Refusal = refusal;
    }

    public static LuxembourgQueryExecutionResult Delivered(
        SourceProfileTopology topology,
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> familyOutcomes,
        IReadOnlyList<LuxembourgRelationFamilyAcquisition> relationFamilyAcquisitions,
        IReadOnlyList<LuxembourgCoarseDispositionMarker> coarseDispositionMarkers,
        IReadOnlyList<string> resourceObservationSubjects,
        IReadOnlyList<LuxembourgResourceObservationExclusionAccounting> resourceObservationExclusions,
        DurableBlobWriteReceipt scopeManifestReceipt,
        string scopeManifestCanonicalSha256)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(resourceObservationSubjects);
        ArgumentNullException.ThrowIfNull(resourceObservationExclusions);
        ArgumentNullException.ThrowIfNull(scopeManifestReceipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeManifestCanonicalSha256);
        var completion = familyOutcomes.All(
            static outcome => outcome.Kind == LuxembourgFamilyEnumerationOutcomeKind.Proven)
            ? LuxembourgQueryExecutionCompletion.AllFamiliesProven
            : LuxembourgQueryExecutionCompletion.PartialFamilyRefused;
        return new(
            topology, familyOutcomes, relationFamilyAcquisitions, coarseDispositionMarkers,
            resourceObservationSubjects, resourceObservationExclusions, scopeManifestReceipt,
            scopeManifestCanonicalSha256, completion, null);
    }

    public static LuxembourgQueryExecutionResult Refused(
        SourceProfileTopology topology,
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> familyOutcomes,
        IReadOnlyList<LuxembourgRelationFamilyAcquisition> relationFamilyAcquisitions,
        LuxembourgQueryExecutionRefusalDetail refusal)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(refusal);
        return new(topology, familyOutcomes, relationFamilyAcquisitions, [], [], [], null, null, null, refusal);
    }

    /// <summary>Always present: minting it cannot fail, and it is useful context on a refusal too.</summary>
    public SourceProfileTopology Topology { get; }

    public IReadOnlyList<LuxembourgFamilyEnumerationOutcome> FamilyOutcomes { get; }

    public IReadOnlyList<LuxembourgRelationFamilyAcquisition> RelationFamilyAcquisitions { get; }

    public IReadOnlyList<LuxembourgCoarseDispositionMarker> CoarseDispositionMarkers { get; }

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
/// This adapter still drives one partition per family
/// (<see cref="LuxembourgRepeatedEnumerationExecutor.RunPartitionAsync"/>), not a
/// <see cref="LuxembourgPartitionCover"/> chain: a family whose selection requires repartitioning is
/// reported as an ordinary refused family outcome rather than silently retried across a chain.
/// D1-04b measured no live count for either the "subjects" census family or the "assertion-rows"
/// family against the publisher's 1,000,000-row selection ceiling -- no production crawl has run
/// under V3 yet, and every count in this file's own tests is a small synthetic fixture value -- and
/// no such measurement exists anywhere in this repository's coordination record either, so this
/// slice proceeds on the single-partition assumption for both families, named explicitly here rather
/// than assumed silently. Driving <see cref="LuxembourgRepeatedEnumerationExecutor.RunCoverAsync"/>
/// for either family once a real <c>PartitionRequired</c> count is observed remains future work, not
/// this one's, exactly as the prior ruling already established for the census family alone.
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
    /// Runs one D1-04 slice: enumerates <paramref name="families"/> (one partition request each,
    /// no cover/chain yet -- see the type remarks above), derives this run's own
    /// <see cref="LuxembourgResourceObservation"/> values (see <see cref="BuildResourceObservations"/>),
    /// then reuses the merged R5.1 pipeline exactly once over them.
    /// </summary>
    /// <param name="families">
    /// One partition request and its already-bound source witness per family to enumerate. Passed
    /// as a direct parameter rather than bundled into a request record's property: a plain by-value
    /// parameter of <see cref="LuxembourgRepeatedEnumerationExecutor.RunPartitionAsync"/>'s own
    /// input type is not a new way to construct or hold one, exactly as that method's own
    /// <c>request</c> parameter already is not; a record wrapping the same values in a property
    /// would be (<c>LuxembourgExecutorConstructionSurfaceTests.ARunRequestIsAnOpenInputRecord</c>
    /// pins that nothing besides construction and that one consuming parameter holds a
    /// <see cref="LuxembourgPartitionRunRequest"/>). One partition's own <c>Partition.PartitionId</c>
    /// is its family key -- the identifier <see cref="AbsenceFamilyEnumerationProof"/> matches
    /// against -- so there is no separate family-key field to drift from it.
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
    /// <param name="evidenceResolver">The evidence resolver the merged R5.1 scope reduction requires.</param>
    public async Task<LuxembourgQueryExecutionResult> RunAsync(
        IReadOnlyList<(LuxembourgPartitionRunRequest PartitionRequest, BoundMachineRequest SourceWitness)> families,
        string? relationAssertionsFamilyKey,
        string? resourceObservationFamilyKey,
        string? resourceAssertionsFamilyKey,
        IScopeReductionEvidenceResolver evidenceResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(evidenceResolver);
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
        RepeatedEnumerationDeliveryReceipt? censusReceipt = null;
        LuxembourgPartitionRunRequest? censusPartitionRequest = null;
        RepeatedEnumerationDeliveryReceipt? assertionReceipt = null;
        LuxembourgPartitionRunRequest? assertionPartitionRequest = null;

        foreach (var (partitionRequest, sourceWitness) in families)
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
                        censusReceipt = receipt;
                        censusPartitionRequest = partitionRequest;
                    }

                    if (isAssertionFamily)
                    {
                        assertionReceipt = receipt;
                        assertionPartitionRequest = partitionRequest;
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
            if (censusOutcome is null || censusReceipt is null || censusPartitionRequest is null)
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
            if (assertionOutcome is null || assertionReceipt is null || assertionPartitionRequest is null)
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

            var (censusRows, censusProfile, censusRefusal) = await ReopenAndVerifyFamilyRowsAsync(
                    censusOutcome.Proof!, censusReceipt, censusPartitionRequest, cancellationToken)
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

            var (assertionRows, assertionProfile, assertionRefusal) = await ReopenAndVerifyFamilyRowsAsync(
                    assertionOutcome.Proof!, assertionReceipt, assertionPartitionRequest, cancellationToken)
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
                censusRows, censusProfile, assertionRows, assertionProfile,
                assertionPartitionRequest.InvariantPlan.SelectorPredicates);
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
            resourceObservationSubjects = observations.Select(static o => o.ObjectRef.PublisherUri).ToArray();
            resourceObservationExclusions = buildResult.Exclusions!;
        }

        var resolution = _sourceProfile.Resolve(observations);
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
        var coarseMarkers = BuildCoarseDispositionMarkers(resolved);
        var manifest = _sourceProfile.ReduceScope(resolved, evidenceResolver);

        // ScopeManifestCanonicalWriter.Write returns the manifest's OWN canonical identity: a
        // domain-separated hash (SHA256("lex-v3-source-scope-manifest/1\n" + bytes)), never written
        // to the stream itself. It is a different, independent identifier from the custody store's
        // own content address (plain SHA256(bytes)) and the two are never expected to be equal;
        // both are retained below rather than one silently standing in for the other.
        using var manifestStream = new MemoryStream();
        var manifestCanonicalSha256 = ScopeManifestCanonicalWriter.Write(manifestStream, manifest);
        var manifestBytes = manifestStream.ToArray();

        var writeReceipt = await _custodyStore.CreateAsync(
                manifestBytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);

        if (CustodyMembershipClassifier.Classify(writeReceipt) != CustodyMembership.Floored)
        {
            return LuxembourgQueryExecutionResult.Refused(
                topology,
                outcomes,
                relationAcquisitions,
                new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.ScopeManifestNotHeld,
                    null,
                    "The scope manifest was written but the store enforced no retention floor on it."));
        }

        // Re-verified by reopening the exact digest from the store, not trusted from the write call
        // alone: a receipt names bytes, a reopen proves the store actually holds them.
        // ReadByDigestCheckedAsync itself already throws CustodyIntegrityException unless the
        // returned bytes hash to writeReceipt.Reference.ContentSha256, which the store computed
        // from manifestBytes at CreateAsync above; a follow-on SequenceEqual against manifestBytes
        // here would only be re-deriving what that digest check already establishes (fold-in seven
        // of the D1-04 refreeze -- the executor's own delivery proof removed the same redundant
        // check after a checked read for the same reason).
        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, writeReceipt.Reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);

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
        _ = VerifiedScopeManifest.ParseAndVerify(manifestArtifactRef, reopened.Span, evidenceResolver);

        return LuxembourgQueryExecutionResult.Delivered(
            topology, outcomes, relationAcquisitions, coarseMarkers, resourceObservationSubjects,
            resourceObservationExclusions, writeReceipt, manifestCanonicalSha256);
    }

    /// <summary>
    /// Finds <paramref name="familyKey"/> among <paramref name="outcomes"/>, returning it only when
    /// it was actually <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/> this run -- a
    /// missing key and a found-but-not-proven key are both "no usable outcome" to every caller of
    /// this method, which report the difference themselves (or don't need to).
    /// </summary>
    private static LuxembourgFamilyEnumerationOutcome? FindProvenOutcome(
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> outcomes, string familyKey)
    {
        var outcome = outcomes.FirstOrDefault(
            candidate => string.Equals(candidate.FamilyKey, familyKey, StringComparison.Ordinal));
        return outcome is { Kind: LuxembourgFamilyEnumerationOutcomeKind.Proven } ? outcome : null;
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

    /// <summary>The three literal values <c>LuxembourgQueryPlan.BuildTemplates</c>' own <c>object_kind</c> BIND can produce.</summary>
    private const string AssertionObjectKindIri = "iri";

    private const string AssertionObjectKindLiteral = "literal";
    private const string AssertionObjectKindUnsupportedBlankNode = "unsupported_blank_node";

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
            var objectRef = new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Jolux,
                new SourceRegistryMemberRef(_sourceProfile.ScopeBinding.SourceProfileRef, "legal_resource"),
                subject,
                subject,
                Sha256Hex(subject),
                _sourceProfile.ScopeBinding.SourceProfileRef,
                null);
            observations.Add(new LuxembourgResourceObservation(
                objectRef,
                observationRef,
                assertions,
                [],
                new LuxembourgSparqlRightsChannelObservations(observationRef, observationRef, []),
                new LuxembourgInFileRightsChannelObservations(observationRef, observationRef, [])));
        }

        var exclusions = exclusionCounts
            .Select(static pair => new LuxembourgResourceObservationExclusionAccounting(
                pair.Key.Subject, pair.Key.Cause, pair.Value))
            .OrderBy(static exclusion => exclusion.Subject, StringComparer.Ordinal)
            .ThenBy(static exclusion => exclusion.Cause)
            .ToArray();

        return ResourceObservationBuildResult.Built(observations, exclusions);
    }

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

    /// <summary>Item 15: names every resource whose acceptance rests on bucket membership only.</summary>
    private static IReadOnlyList<LuxembourgCoarseDispositionMarker> BuildCoarseDispositionMarkers(
        LuxembourgProfileResolution.Resolved resolved)
    {
        // Fold-in six of the D1-04 refreeze: read the predicate directly off the shared public
        // constant instead of searching RequiredIriVocabulary for the one AssertionPredicate value
        // ending in "typeDocument". VerifiedLuxembourgSourceProfile.TypeDocumentPredicateIri is where
        // this assembly reads it from; LuxembourgScopeResolver (Lex.V3.Contracts) does not read this
        // constant at all -- it keeps its own private "TypeDocument" string duplicate
        // (LuxembourgScopeResolver.cs) rather than sharing this one. That duplicate is a separate,
        // already-named gap (item 18, lane-w), not fixed here.
        var typeDocumentPredicateIri = VerifiedLuxembourgSourceProfile.TypeDocumentPredicateIri;

        var markers = new List<LuxembourgCoarseDispositionMarker>();
        foreach (var resource in resolved.Resources)
        {
            if (resource.Dimensions.PublicationFamily.State != LuScopeTerminalState.AcceptedCandidate)
            {
                continue;
            }

            var typeDocumentIri = resource.Assertions
                .Where(assertion =>
                    assertion.Disposition == LuxembourgAssertionDisposition.Accepted &&
                    string.Equals(
                        assertion.Assertion.PredicateIri, typeDocumentPredicateIri, StringComparison.Ordinal) &&
                    string.Equals(
                        assertion.Assertion.SubjectIri,
                        resource.ObjectRef.PublisherUri,
                        StringComparison.Ordinal))
                .Select(static assertion => assertion.Assertion.ObjectIriOrLexical)
                .FirstOrDefault();
            if (typeDocumentIri is null)
            {
                continue;
            }

            var gap = LastPathSegment(typeDocumentIri) switch
            {
                VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc =>
                    LuxembourgCoarseDispositionGap.TcTypedRoleNotDistinguished,
                VerifiedLuxembourgSourceProfile.PriorityCandidateTypeRect =>
                    LuxembourgCoarseDispositionGap.RectTypedRoleNotDistinguished,
                VerifiedLuxembourgSourceProfile.PriorityCandidateTypeAcc =>
                    LuxembourgCoarseDispositionGap.AccTypedRoleNotDistinguished,
                _ => (LuxembourgCoarseDispositionGap?)null,
            };
            if (gap is { } value)
            {
                markers.Add(new LuxembourgCoarseDispositionMarker(
                    resource.ObjectRef.PublisherUri, typeDocumentIri, value));
            }
        }

        return markers;
    }

    private static string LastPathSegment(string iri) => iri[(iri.LastIndexOf('/') + 1)..];
}
