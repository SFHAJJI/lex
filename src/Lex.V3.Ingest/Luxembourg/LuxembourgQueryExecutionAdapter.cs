using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
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
/// Item 15 of the D1-04 design-synthesis ruling: "LuxembourgScopeResolver implements bucket
/// membership only, not R5.1's TC and RECT typed roles nor an ACC constitutional review evidence
/// gate; that is a defect in the merged resolver and gets its own slice after D1-04's first freeze
/// names the gap; D1-04 records the coarser disposition with typed acquisition state so the gap
/// stays visible, never papers over it."
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
    /// One or more <see cref="LuxembourgResourceObservation"/> values were supplied to
    /// <see cref="LuxembourgQueryExecutionAdapter.RunAsync"/>, but no family this run enumerated
    /// can attest them: either no resource-observation family was designated at all, or the
    /// designated family key does not name a family this run proved. The review objection this
    /// refusal closes: nothing previously tied a caller's <c>observations</c> to what the executor
    /// actually delivered, so a manifest could be built from a hand-supplied set the run never
    /// enumerated.
    /// </summary>
    ObservationsWithoutProvenCensus = 3,

    /// <summary>
    /// The designated resource-observation family was proven, but the number of observations
    /// supplied does not equal the number of rows that family's proof reports as delivered. This
    /// is a narrower binding than full identity-set equality between the supplied observations and
    /// the family's delivered canonical keys: see the remark on
    /// <see cref="LuxembourgQueryExecutionAdapter.RunAsync"/> for why that stronger comparison is
    /// not available from this assembly today.
    /// </summary>
    ObservationCountDoesNotMatchDelivery = 4,
}

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
        DurableBlobWriteReceipt? scopeManifestReceipt,
        string? scopeManifestCanonicalSha256,
        LuxembourgQueryExecutionCompletion? completion,
        LuxembourgQueryExecutionRefusalDetail? refusal)
    {
        Topology = topology;
        FamilyOutcomes = familyOutcomes;
        RelationFamilyAcquisitions = relationFamilyAcquisitions;
        CoarseDispositionMarkers = coarseDispositionMarkers;
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
        DurableBlobWriteReceipt scopeManifestReceipt,
        string scopeManifestCanonicalSha256)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(scopeManifestReceipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeManifestCanonicalSha256);
        var completion = familyOutcomes.All(
            static outcome => outcome.Kind == LuxembourgFamilyEnumerationOutcomeKind.Proven)
            ? LuxembourgQueryExecutionCompletion.AllFamiliesProven
            : LuxembourgQueryExecutionCompletion.PartialFamilyRefused;
        return new(
            topology, familyOutcomes, relationFamilyAcquisitions, coarseDispositionMarkers, scopeManifestReceipt,
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
        return new(topology, familyOutcomes, relationFamilyAcquisitions, [], null, null, null, refusal);
    }

    /// <summary>Always present: minting it cannot fail, and it is useful context on a refusal too.</summary>
    public SourceProfileTopology Topology { get; }

    public IReadOnlyList<LuxembourgFamilyEnumerationOutcome> FamilyOutcomes { get; }

    public IReadOnlyList<LuxembourgRelationFamilyAcquisition> RelationFamilyAcquisitions { get; }

    public IReadOnlyList<LuxembourgCoarseDispositionMarker> CoarseDispositionMarkers { get; }

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
/// RESIDUE, named rather than papered over. This adapter does not decode a family's delivered
/// SPARQL rows into <see cref="LuxembourgResourceObservation"/> objects: no production path in this
/// codebase does that today (only test fixtures construct
/// <see cref="LuxembourgResourceObservation"/>), and the repeated-enumeration executor's own row
/// parser is a private implementation detail of proving enumeration completeness, not a public
/// reader. <c>RunAsync</c>'s own <c>observations</c> parameter is therefore supplied by the caller.
/// This mirrors <see cref="VerifiedLuxembourgSourceProfile.Resolve"/>'s own existing
/// input boundary rather than inventing new plumbing the ruled design does not ask for; closing it
/// is its own future slice. Likewise, this adapter drives one partition per family
/// (<see cref="LuxembourgRepeatedEnumerationExecutor.RunPartitionAsync"/>), not a
/// <see cref="LuxembourgPartitionCover"/> chain: a family whose selection requires repartitioning is
/// reported as an ordinary refused family outcome rather than silently retried across a chain.
/// </para>
/// </remarks>
public sealed class LuxembourgQueryExecutionAdapter
{
    private readonly ICustodyStore _custodyStore;
    private readonly LuxembourgRepeatedEnumerationExecutor _executor;
    private readonly VerifiedLuxembourgSourceProfile _sourceProfile;

    public LuxembourgQueryExecutionAdapter(
        ICustodyStore custodyStore,
        LuxembourgRepeatedEnumerationExecutor executor,
        VerifiedLuxembourgSourceProfile sourceProfile)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _sourceProfile = sourceProfile ?? throw new ArgumentNullException(nameof(sourceProfile));
    }

    /// <summary>
    /// Runs one D1-04 slice: enumerates <paramref name="families"/> (one partition request each,
    /// no cover/chain yet -- see the type remarks above), then reuses the merged R5.1 pipeline
    /// exactly once over <paramref name="observations"/>.
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
    /// Which entry of <paramref name="families"/>, if any, is the LU resource-discovery family
    /// (query-plan set "S", template "subjects") <paramref name="observations"/> is asserted to be
    /// the delivered census of. This is the refreeze's answer to the one objection that mattered
    /// most in review: previously nothing tied a caller's <paramref name="observations"/> to what
    /// the executor actually enumerated this run, so the scope manifest could classify objects the
    /// run never saw and omit ones it delivered. When <paramref name="observations"/> is non-empty
    /// (or this key is non-null), the named family must be among <paramref name="families"/> and
    /// its enumeration must be <see cref="LuxembourgFamilyEnumerationOutcomeKind.Proven"/>, and the
    /// number of <paramref name="observations"/> must equal that family's proven
    /// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/>; otherwise <c>RunAsync</c>
    /// refuses with <see cref="LuxembourgQueryExecutionRefusal.ObservationsWithoutProvenCensus"/> or
    /// <see cref="LuxembourgQueryExecutionRefusal.ObservationCountDoesNotMatchDelivery"/> rather than
    /// resolving scope over an unattested set.
    /// <para>
    /// RESIDUE, named rather than papered over, exactly as the type remarks above name the
    /// observation-decoding residue. This is a count binding, not the stronger identity-set
    /// equality the review objection also offered as acceptable ("the supplied observations'
    /// identity set equals the delivered canonical key set of the resource family"). That stronger
    /// check is not reachable from this assembly today: <see cref="EnumerationDeliveryComparison"/>
    /// retains a family's delivered canonical keys only as <c>CanonicalKeyDigestA</c>/<c>B</c>, a
    /// hash over RDF term tuples canonicalized by <c>ContractCanonicalizer</c>, which is
    /// <c>internal</c> to <c>Lex.V3.Contracts</c> and granted <c>InternalsVisibleTo</c> only to that
    /// assembly's own tests, never to <c>Lex.V3.Ingest</c>; and
    /// <see cref="AbsenceFamilyEnumerationProof"/>'s own remarks say plainly that this digest "does
    /// not bind the delivered rows to a cut's observed keys... which no list of canonical publisher
    /// URIs can be checked against from here". Reproducing that digest from this assembly would mean
    /// duplicating a private canonicalization and the private SPARQL-row parsing that feeds it
    /// (<c>EnumerationDeliveryComparison.Parse</c>/<c>ParseRows</c>), which is exactly the
    /// re-implementation this design deliberately avoids elsewhere (see the observation-decoding
    /// residue above). A durable fix for the stronger comparison is a new public reader door on
    /// <c>Source.Core</c> analogous to <see cref="Scope.VerifiedScopeManifest.ParseAndVerify"/>
    /// (Decision 80's precedent for exactly this "production Ingest previously had no way to turn
    /// durable bytes back into a verified value" shape); that is a cross-cutting change to shared,
    /// already-merged EU/LU infrastructure and belongs to its own reviewed slice, not bundled into
    /// this one. The count binding here is the strongest check achievable without it, and it already
    /// closes the concrete defect the objection's own tests demonstrated: a manifest built from zero
    /// enumerated families, or from an enumerated family with no matching observations.
    /// </para>
    /// </param>
    /// <param name="observations">The resource observations to resolve. See the residue note above.</param>
    /// <param name="evidenceResolver">The evidence resolver the merged R5.1 scope reduction requires.</param>
    public async Task<LuxembourgQueryExecutionResult> RunAsync(
        IReadOnlyList<(LuxembourgPartitionRunRequest PartitionRequest, BoundMachineRequest SourceWitness)> families,
        string? relationAssertionsFamilyKey,
        string? resourceObservationFamilyKey,
        IReadOnlyList<LuxembourgResourceObservation> observations,
        IScopeReductionEvidenceResolver evidenceResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(evidenceResolver);

        var topology = LuxembourgSourceProfileTopology.Mint(_sourceProfile);

        var outcomes = new List<LuxembourgFamilyEnumerationOutcome>(families.Count);
        AbsenceFamilyEnumerationProof? relationProof = null;
        string? relationIncompleteReason = null;
        var sawRelationFamily = false;

        foreach (var (partitionRequest, sourceWitness) in families)
        {
            ArgumentNullException.ThrowIfNull(partitionRequest);
            var familyKey = partitionRequest.Partition.PartitionId;
            var isRelationFamily = string.Equals(
                familyKey, relationAssertionsFamilyKey, StringComparison.Ordinal);

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

        // THE OBJECTION this refreeze closes: bind observations to what this run's own family
        // enumeration actually delivered, rather than trusting a caller-supplied list wholesale.
        // Refuses before Resolve/ReduceScope ever sees the observations, so an unattested set never
        // reaches the scope manifest at all.
        var censusRefusal = CheckObservationCensus(resourceObservationFamilyKey, observations, outcomes);
        if (censusRefusal is not null)
        {
            return LuxembourgQueryExecutionResult.Refused(topology, outcomes, relationAcquisitions, censusRefusal);
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
            topology, outcomes, relationAcquisitions, coarseMarkers, writeReceipt,
            manifestCanonicalSha256);
    }

    /// <summary>
    /// The review objection's mismatch guard, and a mismatch test drives every branch here.
    /// Returns null (no refusal) only when either nothing was supplied that needs attesting, or
    /// what was supplied is attested by a family this run actually proved. See the
    /// <paramref name="resourceObservationFamilyKey"/> remark on <see cref="RunAsync"/> for why this
    /// is a count binding rather than full identity-set equality.
    /// </summary>
    private static LuxembourgQueryExecutionRefusalDetail? CheckObservationCensus(
        string? resourceObservationFamilyKey,
        IReadOnlyList<LuxembourgResourceObservation> observations,
        IReadOnlyList<LuxembourgFamilyEnumerationOutcome> outcomes)
    {
        if (resourceObservationFamilyKey is null)
        {
            // Symmetric with BuildRelationFamilyAcquisitions' own "did not try this run" case: no
            // observations and no designation is the ordinary empty run, not a mismatch.
            return observations.Count == 0
                ? null
                : new LuxembourgQueryExecutionRefusalDetail(
                    LuxembourgQueryExecutionRefusal.ObservationsWithoutProvenCensus,
                    null,
                    $"{observations.Count} observation(s) were supplied but no resource-observation " +
                    "family was designated to attest them.");
        }

        var outcome = outcomes.FirstOrDefault(
            candidate => string.Equals(
                candidate.FamilyKey, resourceObservationFamilyKey, StringComparison.Ordinal));
        if (outcome is null || outcome.Kind != LuxembourgFamilyEnumerationOutcomeKind.Proven)
        {
            return new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ObservationsWithoutProvenCensus,
                null,
                $"the designated resource-observation family '{resourceObservationFamilyKey}' was " +
                "not proven by this run's enumeration.");
        }

        if (observations.Count != outcome.Proof!.DeliveredRowCount)
        {
            return new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ObservationCountDoesNotMatchDelivery,
                null,
                $"{observations.Count} observation(s) were supplied but the proven " +
                $"resource-observation family '{resourceObservationFamilyKey}' delivered " +
                $"{outcome.Proof.DeliveredRowCount}.");
        }

        return null;
    }

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
        // ending in "typeDocument". VerifiedLuxembourgSourceProfile.TypeDocumentPredicateIri is the
        // one place both this assembly and LuxembourgScopeResolver (Lex.V3.Contracts) read it from.
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
