using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Europe;

/// <summary>One family's execution: the executor call, and, if it delivered, the proof attempt.</summary>
public enum EuFamilyEnumerationOutcomeKind
{
    /// <summary>The executor delivered and the family's whole enumeration was proven.</summary>
    Proven = 1,

    /// <summary>The executor itself refused before delivering.</summary>
    ExecutorRefused = 2,

    /// <summary>
    /// The executor delivered, but the delivery does not prove this family's whole enumeration --
    /// for example the selection reached the row cap (<see cref="AbsenceFamilyEnumerationProofRefusal.SelectionReachedTheRowCap"/>,
    /// D1-05c-2 precision five's <c>PartitionRequired</c> case), or the two passes disagreed.
    /// </summary>
    ProofRefused = 3,
}

public sealed class EuFamilyEnumerationOutcome
{
    private EuFamilyEnumerationOutcome(
        string familyKey,
        EuFamilyEnumerationOutcomeKind kind,
        AbsenceFamilyEnumerationProof? proof,
        EuEnumerationRefusalDetail? executorRefusal,
        AbsenceFamilyEnumerationProofRefusal? proofRefusal)
    {
        FamilyKey = familyKey;
        Kind = kind;
        Proof = proof;
        ExecutorRefusal = executorRefusal;
        ProofRefusal = proofRefusal;
    }

    public string FamilyKey { get; }

    public EuFamilyEnumerationOutcomeKind Kind { get; }

    public AbsenceFamilyEnumerationProof? Proof { get; }

    /// <summary>
    /// The executor's own refusal for a family that never delivered.
    /// </summary>
    /// <remarks>
    /// WRITTEN HERE AND READ NOWHERE IN <c>src</c>, stated rather than left for a reader to discover.
    /// Its only consumers are in tests: the Stage 1 canary prints it per family and its evidence
    /// index records the code, the offending key, the refused body's digest, the terminal status and
    /// the request ordinal. That is deliberate for now, because no production caller of
    /// <see cref="EuQueryExecutionAdapter.RunAsync"/> exists at all (the composition root is a
    /// Stage 6 boundary, RULING lex-event-20260904T231236855Z-8c7a540fc4d2420f859f9d92fdfc733a), so
    /// there is no src consumer for it to have. When that root arrives this is the field it reads.
    /// </remarks>
    public EuEnumerationRefusalDetail? ExecutorRefusal { get; }

    public AbsenceFamilyEnumerationProofRefusal? ProofRefusal { get; }

    /// <summary>
    /// The custody class of the run behind a proven family, read off the proof this outcome carries.
    /// Null when no proof was minted.
    /// </summary>
    /// <remarks>
    /// RULING lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6: the session says WHICH of the
    /// three each member is, so a caller reading this run's outcomes can see that a family proved
    /// over artifacts held without an enforced floor. Derived from
    /// <see cref="AbsenceFamilyEnumerationProof.RetainedFloor"/> rather than stored beside it, so the
    /// two can never disagree. <see cref="AbsenceCut.TryCreateComplete"/> is what refuses such a
    /// proof at release; this is what makes the class visible before then.
    /// </remarks>
    public CustodyMembership? RetainedFloor => Proof?.RetainedFloor;

    /// <summary>
    /// The measured delivered row count against the publisher delivery ceiling
    /// (<see cref="EuConsolidationDiscoveryPlan.PublisherDeliveryCeilingRows"/>, 1,000,000, Decision
    /// 23), when this family proved. D1-05c-2 precision six: a real measured number, never estimated.
    /// </summary>
    public long? DeliveredRowCount => Proof?.DeliveredRowCount;

    public static EuFamilyEnumerationOutcome Proven(string familyKey, AbsenceFamilyEnumerationProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return new(familyKey, EuFamilyEnumerationOutcomeKind.Proven, proof, null, null);
    }

    public static EuFamilyEnumerationOutcome ExecutorRefused(string familyKey, EuEnumerationRefusalDetail refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new(familyKey, EuFamilyEnumerationOutcomeKind.ExecutorRefused, null, refusal, null);
    }

    public static EuFamilyEnumerationOutcome ProofRefused(string familyKey, AbsenceFamilyEnumerationProofRefusal refusal)
    {
        if (refusal == AbsenceFamilyEnumerationProofRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(familyKey, EuFamilyEnumerationOutcomeKind.ProofRefused, null, null, refusal);
    }
}

public enum EuQueryExecutionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A requested census-family (D1-05a's own <c>Family</c> set) partition did not prove.</summary>
    [JsonStringEnumMemberName("census_family_not_proven")]
    CensusFamilyNotProven = 1,

    /// <summary>A requested object-facts family (P, X, W or M) batch did not prove.</summary>
    [JsonStringEnumMemberName("object_facts_family_not_proven")]
    ObjectFactsFamilyNotProven = 2,

    /// <summary>
    /// A proven family's delivered rows did not independently re-verify when reopened from custody
    /// through <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>.
    /// </summary>
    [JsonStringEnumMemberName("family_rows_not_verified")]
    FamilyRowsNotVerified = 3,

    /// <summary>
    /// D1-05c-2 precision two: the observed root set is bound to Appendix A's own 82-seed pack by
    /// identity through <see cref="EuPrimaryEnumerationRootBinding.TryBind"/>, which refused.
    /// </summary>
    [JsonStringEnumMemberName("root_binding_refused")]
    RootBindingRefused = 4,

    /// <summary>
    /// This run could not resolve a seed's own <see cref="EuActForm"/> from family P's own
    /// <c>resource_legal_type</c> observations for that seed's root. <see cref="EuCellarObjectDecode.TryDecode"/>
    /// requires this as a caller-supplied input it does not itself derive; see this adapter's own
    /// remarks on <see cref="EuQueryExecutionAdapter.TryResolveRecordForm"/> for exactly how it is read
    /// and why a value this reader cannot map refuses rather than guesses.
    /// </summary>
    [JsonStringEnumMemberName("record_form_not_resolved")]
    RecordFormNotResolved = 5,

    /// <summary>
    /// <see cref="EuCellarObjectDecode.TryDecode"/> itself refused one seed's own closure. See
    /// <see cref="EuQueryExecutionResult.DecodeRefusal"/>, <see cref="EuQueryExecutionResult.DecodeOffendingIri"/>
    /// and <see cref="EuQueryExecutionResult.DecodeSnapshotRefusal"/> for the exact reason.
    /// </summary>
    [JsonStringEnumMemberName("object_decode_refused")]
    ObjectDecodeRefused = 6,

    /// <summary>
    /// This run's scope manifest could not be retained at all: the custody write failed, or the
    /// digest-checked reopen handed back bytes that are not the ones the write receipt names.
    /// </summary>
    /// <remarks>
    /// Was <c>ScopeManifestNotHeld</c>, and fired for exactly one condition: the store published no
    /// retention enforcement. RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c
    /// removed that condition, since an unenforced manifest is recorded with the class it observed
    /// and the run continues. Re-conditioned rather than removed, per RULING
    /// lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6, because a genuine custody failure
    /// really can happen here and used to escape <c>RunAsync</c> as an exception. A refusal is a
    /// statement, never a crash.
    /// </remarks>
    [JsonStringEnumMemberName("scope_manifest_not_retained")]
    ScopeManifestNotRetained = 7,

    /// <summary>
    /// The written and reopened manifest did not admit as the Union's own through
    /// <see cref="EuScopeManifestBindingProof.TryOpenAsEuManifest"/>.
    /// </summary>
    [JsonStringEnumMemberName("manifest_binding_refused")]
    ManifestBindingRefused = 8,

    /// <summary>D1-05c-2 precision three: no valid first-cut watermark start position could be computed.</summary>
    [JsonStringEnumMemberName("watermark_bootstrap_refused")]
    WatermarkBootstrapRefused = 9,

    /// <summary>The frozen watermark witness plan itself refused.</summary>
    [JsonStringEnumMemberName("watermark_plan_refused")]
    WatermarkPlanRefused = 10,

    /// <summary>
    /// A family-W row named a root that is not a member of this run's own <see cref="EuPrimaryEnumerationRootBinding"/>
    /// (<c>O</c>'s root subset), or a family-W row's own object term could not be canonicalized at
    /// all. Mirrors precision two's identity binding for P and X: a watermark observation that
    /// cannot be tied to a root this run actually discovered is refused naming the offending value,
    /// never silently excluded from the first-cut bootstrap.
    /// </summary>
    [JsonStringEnumMemberName("root_watermark_binding_refused")]
    RootWatermarkBindingRefused = 11,

    /// <summary>
    /// Defect 3's own witness binding (<see cref="EuFeedRootIntersection.TryBind"/>) refused before
    /// this run could ever reconcile the frozen watermark witness against its own primary
    /// enumeration.
    /// </summary>
    [JsonStringEnumMemberName("witness_binding_refused")]
    WitnessBindingRefused = 12,

    /// <summary>
    /// <see cref="EuPrimaryEnumerationWitnessReconciliation.TryReconcile"/> itself refused. A real
    /// nonempty termination list from this run's own observed witness traversal
    /// (<see cref="EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync"/>) is never a cause of this
    /// refusal on its own -- see <see cref="EuPrimaryEnumerationWitnessReconciliation.CheckedTerminationCount"/>'s
    /// own remarks -- but a termination naming an in-pack root this run's primary enumeration never
    /// discovered still refuses here, exactly as it would for any other cut.
    /// </summary>
    [JsonStringEnumMemberName("witness_reconciliation_refused")]
    WitnessReconciliationRefused = 13,

    /// <summary>
    /// <see cref="ScopeReducer.Reduce"/> itself threw. Defect 4: unlike a single object's own
    /// <see cref="EuObjectReductionExclusion"/>, this call reduces every admitted object's inputs
    /// together, so a failure here cannot be attributed to one offending object and is reported as a
    /// whole-run refusal instead.
    /// </summary>
    [JsonStringEnumMemberName("scope_reduction_refused")]
    ScopeReductionRefused = 14,

    /// <summary>
    /// Defect 3's own real-execution fix: <see cref="EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync"/>
    /// itself refused before this run could ever decode a real delivered witness row into a
    /// termination. Replaces the assumed-empty-result shortcut this refusal code did not previously
    /// need to exist for.
    /// </summary>
    [JsonStringEnumMemberName("witness_traversal_refused")]
    WitnessTraversalRefused = 15,

    /// <summary>
    /// D1-06c-EU defect 4 (SCOPE_RULING lex-event-20260904T130546972Z-c72fad2da5b34344af802c068d8fbf08
    /// item 4): the document-fetch profile's own robots bootstrap did not start a session at all, so
    /// no Minted row in this run could even attempt its own GET.
    /// </summary>
    [JsonStringEnumMemberName("document_fetch_session_not_started")]
    DocumentFetchSessionNotStarted = 16,

    /// <summary>
    /// A document body was fetched successfully (a real, classified 200) but could not be retained
    /// at all: the custody write failed, or the digest-checked read of the fetched bytes handed back
    /// something the routed evidence's own terminal hop digest does not name.
    /// </summary>
    /// <remarks>
    /// Was <c>DocumentBodyNotHeld</c>, and fired for exactly one condition: the store published no
    /// retention enforcement, which refused the WHOLE RUN over one row's body. RULING
    /// lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c removed that condition, since
    /// <c>CorpusBodyRecord.Held</c> derives and records the class it observed. Re-conditioned rather
    /// than removed, per RULING lex-event-20260904T215906714Z-6dadaf27829d4a3aa3c355063754ccd6: a body this
    /// run cannot retain at all is a real failure, and it used to escape as an exception.
    /// </remarks>
    [JsonStringEnumMemberName("document_body_not_retained")]
    DocumentBodyNotRetained = 17,

    /// <summary>
    /// A document-fetch GET completed for real, but its classified outcome has no faithful member in
    /// D1-06b's own closed <see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason"/>
    /// vocabulary to carry into <c>CorpusRecordSetWriter</c>'s acquisition-outcomes door.
    /// </summary>
    /// <remarks>
    /// D1-06c-EU fix one (SCOPE_RULING lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93
    /// item 1) widened that vocabulary with this route's own three named shapes --
    /// <see cref="EuDocumentFetchRefusal.WrongAcceptToken"/>,
    /// <see cref="EuDocumentFetchRefusal.RequestedRepresentationNotServed"/> and
    /// <see cref="Lex.V3.Contracts.Source.Http.HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted"/>
    /// -- so each of those three now becomes that one object's own <c>PendingAcquisition</c> cause
    /// (see <see cref="TryMapDocumentFetchToCorpusAcquisitionRefusal"/>) instead of refusing this
    /// whole run: a 1995-act-shaped 404 must never block a 2026 act's own record. This refusal is
    /// reserved for what genuinely remains unrepresentable: every other route-level shape (a
    /// robots-policy-unavailable outcome, a redirect refused, looped or limit-exceeded, a stale
    /// profile, or a publisher server failure) that vocabulary's twenty-two members -- fourteen
    /// mirrored one for one from <see cref="Lex.V3.Contracts.Source.Http.HttpAcquisitionReasonRegistry"/>'s
    /// own entity-transfer, before-response-headers, completion-unproven and response-semantics
    /// reasons, plus this route's own three named above, plus five reserved for the LU-2 lane's own
    /// document-get route -- were never scoped to cover. A route-level cause this narrow still refuses
    /// the whole run, naming the real classified cause, rather than mapping it to an unrelated
    /// existing member or silently treating it as held.
    /// </remarks>
    [JsonStringEnumMemberName("acquisition_outcome_not_representable")]
    AcquisitionOutcomeNotRepresentable = 18,

    /// <summary>
    /// D1-06c-EU fix two (SCOPE_RULING lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93
    /// item 2): this run's own corpus/6 record set (<see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/>,
    /// called as this run's own last step) could not be retained at all. Carries
    /// <see cref="Lex.V3.Ingest.CorpusRecordSetWriteRefusalKind.RecordSetNotRetained"/>'s own detail:
    /// the write failed, or the reopen handed back bytes the digest does not name. Renamed with that
    /// writer under RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c, and no longer
    /// fires because a store published no enforcement.
    /// </summary>
    [JsonStringEnumMemberName("record_set_not_retained")]
    RecordSetNotRetained = 19,
}

public sealed class EuQueryExecutionRefusalDetail
{
    internal EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal code, string? detail)
    {
        if (code == EuQueryExecutionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "A refusal detail requires a real refusal code.");
        }

        Code = code;
        Detail = detail;
    }

    public EuQueryExecutionRefusal Code { get; }

    public string? Detail { get; }
}

/// <summary>
/// D1-05c-2 precision five: whether every family this run enumerated proved its whole enumeration.
/// Computed, never caller-declared, exactly mirroring <c>LuxembourgQueryExecutionCompletion</c>.
/// </summary>
public enum EuQueryExecutionCompletion
{
    [JsonStringEnumMemberName("all_families_proven")]
    AllFamiliesProven = 1,

    [JsonStringEnumMemberName("partial_family_refused")]
    PartialFamilyRefused = 2,
}

/// <summary>
/// One object this run's reduction excluded from the manifest because
/// <see cref="EuScopeSnapshotReduction.Reduce"/> cannot reduce it without throwing (D1-05c-2 precision
/// four: the reduction never throws, so this is the typed record of the object it would otherwise
/// have thrown for, not a silent drop). Currently unreachable against D1-05c-1's own decode, which
/// retired the one relation authority (<see cref="EuRelationAuthority.OntologyAuthorizedInverse"/>)
/// that <see cref="EuScopeSnapshotReduction.Reduce"/> cannot reduce; retained as typed defense in
/// depth in case that ever changes, exactly the discipline this codebase already applies elsewhere
/// (see <see cref="LuxembourgQueryExecutionRefusal.AssertionRowTermUnbound"/>'s own remarks for the
/// precedent).
/// </summary>
public sealed record EuObjectReductionExclusion(SourceObjectRef ObjectRef, EuRelationFamily Family, string Reason);

/// <summary>
/// D1-05d: what one accepted manifest row's own fetch ladder actually did. Which listed
/// representations this run asked the office for, in the order it asked, and which one it served.
/// </summary>
/// <remarks>
/// <para>
/// The manifest row itself carries exactly ONE fetch address, the first candidate, and no schema
/// bump adds a second (RULING lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262). That
/// address is this run's FIRST ATTEMPT, not necessarily the representation it holds: when the
/// office lists a type and then answers "does not hold a content datastream of the requested type",
/// the run falls through to the next listed candidate. So the row's address alone cannot name the
/// format actually held, and this value is where the run says it.
/// </para>
/// <para>
/// <see cref="Served"/> is null when every attempt failed. <see cref="Attempted"/> is then the
/// tried-types list the RULING requires alongside
/// <see cref="CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed"/>. Each attempt's own
/// routed evidence is separately retained by the executor under Decision 78; this value names them
/// rather than replacing them.
/// </para>
/// </remarks>
public sealed record EuDocumentLadderResult(
    IReadOnlyList<EuManifestationMediaType> Attempted,
    EuManifestationMediaType? Served);

/// <summary>
/// One seed's distinct expression counts, separated by closure position.
/// </summary>
/// <param name="OfRootWork">Expressions whose parent is the seed's own root Work.</param>
/// <param name="OfConsolidatedStates">
/// Expressions whose parent is one of the states the census discovered. Reported rather than
/// folded in, because a census of root Works is not a census of the closure.
/// </param>
public sealed record EuObservedExpressionSplit(int OfRootWork, int OfConsolidatedStates);

/// <summary>
/// One minted manifest row's accounting: which object it names, and whether this run's own body
/// axis selected it for acquisition.
/// </summary>
/// <remarks>
/// <para>
/// THIS EXISTS SO THE EVIDENCE INDEX CAN CARRY A ROW FOR EVERY MINTED ROW. It used to emit one
/// only for ordinals a fetch was ATTEMPTED for, so a row the body axis excluded was ABSENT
/// entirely and a reader could not tell NOT SELECTED from FAILED from NEVER ATTEMPTED. A missing
/// row is the worst form of the unobserved-versus-zero defect, because there is not even a field
/// to be wrong in.
/// </para>
/// <para>
/// It is deliberately NOT carried as a <c>CorpusAcquisitionOutcome</c>. That type is the record
/// builder's input and the builder REFUSES an outcome for a row its own body axis did not accept,
/// which is a correct invariant: an outcome means a fetch happened. The question the index answers
/// is wider than the question the builder asks, so it gets its own carrier rather than widening
/// one whose narrowness is load bearing.
/// </para>
/// </remarks>
/// <param name="CanonicalKey">The object this row names, which is stable across runs.</param>
/// <param name="SelectedByBodyAxis">Whether this run's manifest selected the row for a body.</param>
public sealed record EuMintedRowAccounting(string CanonicalKey, bool SelectedByBodyAxis);

/// <summary>Delivered or refused, never both and never neither.</summary>
public sealed class EuQueryExecutionResult
{
    private EuQueryExecutionResult(
        SourceProfileTopology topology,
        IReadOnlyList<EuFamilyEnumerationOutcome> familyOutcomes,
        int observedObjectCount,
        int observedExpressionCount,
        IReadOnlyList<EuObjectReductionExclusion> reductionExclusions,
        EuWatermarkWitnessPlan? watermarkWitnessPlan,
        EuPrimaryEnumerationRootBinding? rootBinding,
        EuPrimaryEnumerationWitnessReconciliation? witnessReconciliation,
        IReadOnlyList<EuFeedEntryTermination>? witnessTerminations,
        DurableBlobWriteReceipt? scopeManifestReceipt,
        string? scopeManifestCanonicalSha256,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? documentAcquisitionOutcomesByOrdinal,
        IReadOnlyDictionary<int, EuDocumentLadderResult>? documentLadderResultsByOrdinal,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? observedManifestationTypesByCelex,
        IReadOnlyDictionary<string, EuObservedExpressionSplit>? observedExpressionsByCelex,
        IReadOnlyDictionary<int, EuMintedRowAccounting>? mintedRowsByOrdinal,
        SourceArtifactRef? corpusRecordSetRef,
        VerifiedCorpusRecordSet? corpusRecordSet,
        EuQueryExecutionCompletion? completion,
        EuQueryExecutionRefusalDetail? refusal,
        EuCellarObjectDecodeRefusal? decodeRefusal,
        string? decodeOffendingIri,
        EuCellarObjectSnapshotRefusal? decodeSnapshotRefusal)
    {
        Topology = topology;
        FamilyOutcomes = familyOutcomes;
        ObservedObjectCount = observedObjectCount;
        ObservedExpressionCount = observedExpressionCount;
        ReductionExclusions = reductionExclusions;
        WatermarkWitnessPlan = watermarkWitnessPlan;
        RootBinding = rootBinding;
        WitnessReconciliation = witnessReconciliation;
        WitnessTerminations = witnessTerminations;
        ScopeManifestReceipt = scopeManifestReceipt;
        ScopeManifestCanonicalSha256 = scopeManifestCanonicalSha256;
        DocumentAcquisitionOutcomesByOrdinal = documentAcquisitionOutcomesByOrdinal;
        DocumentLadderResultsByOrdinal = documentLadderResultsByOrdinal;
        ObservedManifestationTypesByCelex = observedManifestationTypesByCelex;
        ObservedExpressionsByCelex = observedExpressionsByCelex;
        MintedRowsByOrdinal = mintedRowsByOrdinal;
        CorpusRecordSetRef = corpusRecordSetRef;
        CorpusRecordSet = corpusRecordSet;
        Completion = completion;
        Refusal = refusal;
        DecodeRefusal = decodeRefusal;
        DecodeOffendingIri = decodeOffendingIri;
        DecodeSnapshotRefusal = decodeSnapshotRefusal;
    }

    public static EuQueryExecutionResult Delivered(
        SourceProfileTopology topology,
        IReadOnlyList<EuFamilyEnumerationOutcome> familyOutcomes,
        int observedObjectCount,
        int observedExpressionCount,
        IReadOnlyList<EuObjectReductionExclusion> reductionExclusions,
        EuWatermarkWitnessPlan watermarkWitnessPlan,
        EuPrimaryEnumerationRootBinding rootBinding,
        EuPrimaryEnumerationWitnessReconciliation witnessReconciliation,
        IReadOnlyList<EuFeedEntryTermination> witnessTerminations,
        DurableBlobWriteReceipt scopeManifestReceipt,
        string scopeManifestCanonicalSha256,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome> documentAcquisitionOutcomesByOrdinal,
        IReadOnlyDictionary<int, EuDocumentLadderResult> documentLadderResultsByOrdinal,
        IReadOnlyDictionary<string, IReadOnlyList<string>> observedManifestationTypesByCelex,
        IReadOnlyDictionary<string, EuObservedExpressionSplit> observedExpressionsByCelex,
        IReadOnlyDictionary<int, EuMintedRowAccounting> mintedRowsByOrdinal,
        SourceArtifactRef corpusRecordSetRef,
        VerifiedCorpusRecordSet corpusRecordSet)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(watermarkWitnessPlan);
        ArgumentNullException.ThrowIfNull(rootBinding);
        ArgumentNullException.ThrowIfNull(witnessReconciliation);
        ArgumentNullException.ThrowIfNull(witnessTerminations);
        ArgumentNullException.ThrowIfNull(scopeManifestReceipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeManifestCanonicalSha256);
        ArgumentNullException.ThrowIfNull(documentAcquisitionOutcomesByOrdinal);
        ArgumentNullException.ThrowIfNull(documentLadderResultsByOrdinal);
        ArgumentNullException.ThrowIfNull(observedManifestationTypesByCelex);
        ArgumentNullException.ThrowIfNull(observedExpressionsByCelex);
        ArgumentNullException.ThrowIfNull(mintedRowsByOrdinal);
        ArgumentNullException.ThrowIfNull(corpusRecordSetRef);
        ArgumentNullException.ThrowIfNull(corpusRecordSet);
        var completion = familyOutcomes.All(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven)
            ? EuQueryExecutionCompletion.AllFamiliesProven
            : EuQueryExecutionCompletion.PartialFamilyRefused;
        return new(
            topology, familyOutcomes, observedObjectCount, observedExpressionCount, reductionExclusions,
            watermarkWitnessPlan, rootBinding, witnessReconciliation, witnessTerminations, scopeManifestReceipt,
            scopeManifestCanonicalSha256, documentAcquisitionOutcomesByOrdinal, documentLadderResultsByOrdinal,
            observedManifestationTypesByCelex, observedExpressionsByCelex, mintedRowsByOrdinal,
            corpusRecordSetRef, corpusRecordSet, completion, null, null, null, null);
    }

    public static EuQueryExecutionResult Refused(
        SourceProfileTopology topology,
        IReadOnlyList<EuFamilyEnumerationOutcome> familyOutcomes,
        EuQueryExecutionRefusalDetail refusal,
        EuCellarObjectDecodeRefusal? decodeRefusal = null,
        string? decodeOffendingIri = null,
        EuCellarObjectSnapshotRefusal? decodeSnapshotRefusal = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(refusal);
        return new(
            topology, familyOutcomes, 0, 0, [], null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, refusal, decodeRefusal, decodeOffendingIri, decodeSnapshotRefusal);
    }

    /// <summary>Always present: minting it cannot fail, and it is useful context on a refusal too.</summary>
    public SourceProfileTopology Topology { get; }

    public IReadOnlyList<EuFamilyEnumerationOutcome> FamilyOutcomes { get; }

    /// <summary>
    /// D1-05c-2 precision six: the measured size of the observed object set <c>O</c> (every root this
    /// run enumerated plus every discovered consolidated state), real and counted, never estimated.
    /// </summary>
    public int ObservedObjectCount { get; }

    /// <summary>The measured size of the Expression set family X discovered, real and counted.</summary>
    public int ObservedExpressionCount { get; }

    public IReadOnlyList<EuObjectReductionExclusion> ReductionExclusions { get; }

    /// <summary>
    /// D1-05c-2 precision three: the frozen witness, ready to run from the first cut's own census
    /// bound. Present if and only if this result is delivered. Defect 3's own fix: this adapter now
    /// actually executes this plan's own boundary-reread traversal through
    /// <see cref="EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync"/> before reconciling it
    /// against the primary enumeration (see <see cref="WitnessReconciliation"/>) -- Decision 81 fixes
    /// where the first cut's own witness starts (the census bound), it does not say the first cut
    /// skips the witness. An empty first-cut traversal (nothing changed between the census bound and
    /// this run's own send) is a real, observed outcome; it is never assumed without having sent the
    /// query.
    /// </summary>
    public EuWatermarkWitnessPlan? WatermarkWitnessPlan { get; }

    public EuPrimaryEnumerationRootBinding? RootBinding { get; }

    /// <summary>
    /// Defect 3's own fix: the frozen watermark witness (<see cref="WatermarkWitnessPlan"/>), actually
    /// executed and reconciled against this run's own primary enumeration
    /// (<see cref="RootBinding"/>). Present if and only if this result is delivered. This
    /// reconciliation's own <see cref="EuPrimaryEnumerationWitnessReconciliation.CheckedTerminationCount"/>
    /// is the real count of terminations this run's own traversal classified -- zero when the real,
    /// sent query genuinely returned no rows, otherwise one per delivered row (every one of them
    /// honestly <see cref="EuFeedTerminal.UnresolvedOrAmbiguous"/>, since no identity resolver exists
    /// yet -- see <see cref="EuFeedEntryObservation"/>'s own remarks).
    /// </summary>
    public EuPrimaryEnumerationWitnessReconciliation? WitnessReconciliation { get; }

    /// <summary>
    /// Every termination this run's own witness traversal classified, in traversal order. Present iff
    /// this result is delivered. Exposed so a caller (and this ticket's own driving tests) can inspect
    /// the exact real terminal each delivered row reached -- for the first cut, every one of them
    /// <see cref="EuFeedTerminal.UnresolvedOrAmbiguous"/> with
    /// <see cref="EuFeedUnresolvedCause.IdentityResolutionDidNotClose"/>, since no identity resolver
    /// exists yet -- rather than only the aggregate <see cref="EuPrimaryEnumerationWitnessReconciliation.CheckedTerminationCount"/>.
    /// </summary>
    public IReadOnlyList<EuFeedEntryTermination>? WitnessTerminations { get; }

    public EuQueryExecutionCompletion? Completion { get; }

    public DurableBlobWriteReceipt? ScopeManifestReceipt { get; }

    public string? ScopeManifestCanonicalSha256 { get; }

    /// <summary>
    /// D1-06c-EU defect 4: one <see cref="CorpusAcquisitionOutcome"/> per reopened manifest row ordinal
    /// whose own <see cref="Lex.V3.Contracts.Source.Scope.ScopeManifestFetchAddress.Status"/> is
    /// <see cref="Lex.V3.Contracts.Source.Scope.ScopeManifestFetchAddressStatus.Minted"/> and whose
    /// real, classified fetch this run could faithfully represent: <see cref="CorpusAcquisitionOutcome.Held"/>
    /// with a real receipt for a real 200 whose custody write met this run's own floor, or
    /// <see cref="CorpusAcquisitionOutcome.Refused"/> for a real transport-incomplete outcome this
    /// door's own closed vocabulary can name. Present iff this result is delivered; a row present in
    /// the reopened manifest but absent from this dictionary was never Minted at all -- every Minted
    /// row's own outcome either lands here or refuses the whole run (see
    /// <see cref="EuQueryExecutionRefusal.DocumentBodyNotRetained"/> and
    /// <see cref="EuQueryExecutionRefusal.AcquisitionOutcomeNotRepresentable"/>'s own remarks for
    /// exactly which outcomes cannot be represented and so refuse the run instead of appearing here).
    /// D1-06c-EU fix two: this is exactly the <c>acquisitionOutcomesByOrdinal</c> this run itself
    /// hands to <c>CorpusRecordSetWriter.WriteAsync</c> as its own last step (see this file's own
    /// remarks on <see cref="RunAsync"/>); it remains exposed here too because it is useful context on
    /// its own, not because a caller still needs to relay it anywhere.
    /// </summary>
    public IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? DocumentAcquisitionOutcomesByOrdinal { get; }

    /// <summary>
    /// D1-05d: one <see cref="EuDocumentLadderResult"/> per row this run actually attempted a fetch
    /// for, naming which listed representations it asked for and which one the office served.
    /// Present iff this result is delivered, and keyed identically to
    /// <see cref="DocumentAcquisitionOutcomesByOrdinal"/>.
    /// </summary>
    /// <remarks>
    /// This is where a run says which format it HOLDS. The manifest row's own single fetch address
    /// is the first attempt and no more: a listed manifestation type is not necessarily a servable
    /// one, and when the first candidate answers the datastream-absent 404 the run falls through to
    /// the next listed candidate, so reading the row's address back as the held format would
    /// misreport every fall-through object.
    /// </remarks>
    public IReadOnlyDictionary<int, EuDocumentLadderResult>? DocumentLadderResultsByOrdinal { get; }

    /// <summary>
    /// Per requested CELEX, the DISTINCT manifestation type tokens FAMILY M ACTUALLY LISTED for
    /// that seed's OWN ROOT WORK, sorted ordinally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FILTERED TO THE ROOT'S OWN PARENT, and D1-05g's acceptance run is why. Family M is asked
    /// about the whole closure, so its delivered rows cover the root AND every consolidated state
    /// the census discovered. Unioning them and comparing the result against a census OF ROOT
    /// WORKS reported the closure's types as the root's: measured on the retained family M page,
    /// both roots list exactly what the census records and pdfa2a appears ONLY on states, four of
    /// the six. Reading that as publisher drift and widening the census would have recorded a
    /// false fact about the office and made a failing run pass.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// THE PUBLISHER'S OWN LISTING, AND NEVER WHAT THE LADDER ADMITTED. The distinction is the
    /// whole reason this property exists rather than the canary reading
    /// <see cref="DocumentLadderResultsByOrdinal"/>: that is the LADDER, the formats this run
    /// attempted and was served, which is a fact about our fetching rather than about the office's
    /// inventory. Comparing a census of what the office lists against a record of what we managed
    /// to fetch would report our own coverage as the publisher's holdings.
    /// </para>
    /// <para>
    /// A SET RATHER THAN COUNTS, and that is settled rather than convenient. Family M lists
    /// manifestation TYPES per Work and emits no per-format row, so there is no row to count: a
    /// count comparison would have to invent one. The census per-type counts stay recorded as the
    /// publisher's inventory and are compared as type SETS. Counting the inventory, if it is ever
    /// wanted, needs its own acquisition and is residue R8.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ObservedManifestationTypesByCelex { get; }

    /// <summary>
    /// Per requested CELEX, the count of DISTINCT expressions family X delivered FOR THAT SEED'S
    /// OWN ROOT WORK, and separately for the consolidated states its census discovered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SPLIT BECAUSE THE CENSUS IS PER ROOT WORK. <see cref="ObservedExpressionCount"/> is the
    /// whole closure's distinct expressions, roots and states together, which is the right number
    /// for the manifest and the WRONG number to compare against a census of root Works. Before
    /// D1-05g the two happened to agree, because family X was only ever asked about roots; asking
    /// about the states its own census discovered is what separated them, and the acceptance run
    /// measured 116 across the closure against a census total of 47.
    /// </para>
    /// <para>
    /// The states' expressions are REPORTED, not discarded: they are a real observation of real
    /// Works this run acquired, and dropping them to make a comparison line up would be the
    /// fabrication this split exists to avoid.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, EuObservedExpressionSplit>? ObservedExpressionsByCelex { get; }

    /// <summary>
    /// Every MINTED manifest row, whether or not a body was fetched for it.
    /// </summary>
    /// <remarks>
    /// The evidence index emits one row per entry here, so a row the body axis excluded is stated
    /// rather than absent. It is keyed by ordinal and CARRIES THE OBJECT KEY, because the ordinal a
    /// body lands on was measured to differ between two runs of the same head while the four bodies
    /// held were the same four; an explanation keyed to position would be true of one run and false
    /// of the next.
    /// </remarks>
    public IReadOnlyDictionary<int, EuMintedRowAccounting>? MintedRowsByOrdinal { get; }

    /// <summary>
    /// D1-06c-EU fix two: this run's own written corpus/6 record set artifact reference. Present iff
    /// this result is delivered.
    /// </summary>
    public SourceArtifactRef? CorpusRecordSetRef { get; }

    /// <summary>
    /// D1-06c-EU fix two: the corpus/6 record set this run wrote, reopened and verified through its
    /// own checked door (<see cref="Lex.V3.Contracts.Source.Corpus.VerifiedCorpusRecordSet.ParseAndVerify"/>)
    /// by <see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/> itself, never the in-memory set
    /// this run built. Present iff this result is delivered.
    /// </summary>
    public VerifiedCorpusRecordSet? CorpusRecordSet { get; }

    public EuQueryExecutionRefusalDetail? Refusal { get; }

    public EuCellarObjectDecodeRefusal? DecodeRefusal { get; }

    public string? DecodeOffendingIri { get; }

    public EuCellarObjectSnapshotRefusal? DecodeSnapshotRefusal { get; }
}

/// <summary>
/// D1-05c-2: the EU query-execution adapter. Mints the Union <see cref="SourceProfileTopology"/>,
/// runs and proves every family (D1-05a's own census family <c>S</c>, reused unchanged, plus D1-05c-1's
/// four object-facts families P, X, W and M), binds the observed object set to the closure proof by
/// identity, decodes through <see cref="EuCellarObjectDecode"/>, reduces through
/// <see cref="EuScopeSnapshotReduction"/> and <see cref="EuScopeProfile.BuildScopeInput"/> into
/// <see cref="ScopeReducer.Reduce"/>, writes and holds the manifest, freezes the first-cut watermark
/// witness, and reconciles it against the primary enumeration through
/// <see cref="EuPrimaryEnumerationWitnessReconciliation"/> (never itself executing a live traversal of
/// the frozen plan; see <see cref="EuQueryExecutionResult.WatermarkWitnessPlan"/>'s own remarks).
/// Follows proposal B's step list, authority the D1-05c synthesis ruling.
/// </summary>
/// <remarks>
/// D1-06c-EU defect 4 (SCOPE_RULING lex-event-20260904T130546972Z-c72fad2da5b34344af802c068d8fbf08
/// item 4): once the manifest is written, floored and reopened, this run also actually drives the
/// document-fetch GET for every reopened row whose own <c>FetchAddress</c> is Minted, through
/// <see cref="EuRepeatedEnumerationExecutor.RunDocumentFetchAsync"/> and
/// <see cref="EuDocumentFetchOutcome.Classify"/>, and prepares real
/// <see cref="Lex.V3.Ingest.CorpusAcquisitionOutcome"/> values for the ordinals that outcome can
/// faithfully represent (see <see cref="EuQueryExecutionResult.DocumentAcquisitionOutcomesByOrdinal"/>).
/// A route-level or classified refusal on ONE row's own fetch never refuses the whole run by itself
/// any more (D1-06c-EU fix one, SCOPE_RULING
/// lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93 item 1): the three named shapes
/// that vocabulary now covers become that one row's own typed cause, and every other row still gets
/// its own record.
/// <para>
/// D1-06c-EU fix two (same SCOPE_RULING, item 2): this run now itself calls
/// <see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/> as its own last step, after the
/// manifest is written and every document fetch above has been attempted and classified. D1-06b's own
/// writer builds one <c>CorpusRecordSet</c> from the reopened manifest plus this run's own document
/// acquisition outcomes; before this fix nothing in either the EU or the Luxembourg adapter ever
/// called it, so no corpus/6 record set was ever durably written by a real run. This run mints its
/// own <c>RunIdentity</c> paired with real evidence (the manifest's own custody-write digest) and
/// reuses the identical custody floor (<see cref="CustodyClass.NightlyFloor90d"/>) its own manifest
/// and document-body writes already require. A record set the store cannot RETAIN at all refuses the
/// whole run (<see cref="EuQueryExecutionRefusal.RecordSetNotRetained"/>), exactly as an unretainable
/// manifest or document body does. An unenforced FLOOR is not that failure and no longer refuses
/// anything here: the class is recorded and the run continues, per RULING
/// lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c.
/// </para>
/// </remarks>
public sealed class EuQueryExecutionAdapter
{
    private readonly ICustodyStore _custodyStore;
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuQueryExecutionAdapter(ICustodyStore custodyStore, EuRepeatedEnumerationExecutor executor)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(_custodyStore);
    }

    /// <summary>
    /// Runs one D1-05c-2 slice: enumerates every census-family seed and every object-facts batch (one
    /// partition request each; a batch or seed reaching <c>PartitionRequired</c> is reported as an
    /// ordinary refused family, per precision five -- no cover/chain, deferred to D1-04c's pattern),
    /// decodes and reduces the closure, and writes the resulting scope manifest as held evidence.
    /// </summary>
    /// <param name="censusFamilies">One D1-05a census-family partition (one admitted seed CELEX) and its bound source witness, per seed this run enumerates.</param>
    /// <param name="objectFactsFamilies">One object-facts family batch (P, X, W or M) and its bound source witness, per batch this run enumerates. Every seed named in <paramref name="censusFamilies"/> must be covered by at least one P batch, one X batch, one W batch (W covering the roots only) and one M batch for this run to decode anything; the run refuses otherwise.</param>
    /// <param name="witnessRendererSource">
    /// The renderer-source artifact naming <c>EuWatermarkWitnessSparqlRenderer</c>'s own code
    /// (SCOPE_RULING lex-event-20260904T092316893Z-6d969a2ba7934aa995907a55914bf3b6), held with its
    /// bytes exactly as every other Europe family's own renderer source already is. Kept distinct
    /// from every census/object-facts request's own <c>RendererSource</c>: those name different
    /// renderer code (<c>EuConsolidationSparqlRenderer</c>, <c>EuObjectFactsSparqlRenderer</c>), and
    /// reusing one of them here would misattribute the witness's own real HTTP send to code that
    /// never rendered it.
    /// </param>
    /// <param name="witnessSourceWitness">
    /// The bound robots-negotiation witness the witness traversal's own session starts from. Any
    /// bound request targeting the EU SPARQL endpoint resolves to the identical official EU profile
    /// (robots negotiation depends only on that profile, never on which family is about to run), so
    /// this may be the same kind of witness already supplied for census/object-facts families.
    /// </param>
    /// <param name="documentFetchRendererSource">
    /// D1-06c-EU defect 4: the renderer-source artifact naming <c>EuDocumentFetchRenderer</c>'s own
    /// code, held with its bytes exactly as every other Europe bind already requires. Kept distinct
    /// from every other renderer source this run binds, for the identical reason
    /// <paramref name="witnessRendererSource"/> is: reusing one would misattribute a real HTTP send to
    /// code that never rendered it.
    /// </param>
    /// <param name="documentFetchSourceWitness">
    /// The bound robots-negotiation witness each document-fetch GET's own session starts from. Unlike
    /// <paramref name="witnessSourceWitness"/>, this targets a genuinely different official profile
    /// (<c>OfficialMachineQuerySourceProfileId.EuropeanUnionDocumentFetch</c>, GET against
    /// <c>publications.europa.eu/resource/...</c> rather than POST against the SPARQL endpoint), so it
    /// cannot be the same kind of witness the other parameters here use.
    /// </param>
    /// <param name="evidenceResolver">The evidence resolver the scope reduction requires.</param>
    public async Task<EuQueryExecutionResult> RunAsync(
        IReadOnlyList<(EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness)> censusFamilies,
        EuObjectFactsBatchPolicy objectFactsPolicy,
        MachineQueryRendererSource witnessRendererSource,
        BoundMachineRequest witnessSourceWitness,
        MachineQueryRendererSource documentFetchRendererSource,
        BoundMachineRequest documentFetchSourceWitness,
        IScopeReductionEvidenceResolver evidenceResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(censusFamilies);
        ArgumentNullException.ThrowIfNull(objectFactsPolicy);
        ArgumentNullException.ThrowIfNull(witnessRendererSource);
        ArgumentNullException.ThrowIfNull(witnessSourceWitness);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        ArgumentNullException.ThrowIfNull(documentFetchSourceWitness);
        ArgumentNullException.ThrowIfNull(evidenceResolver);

        var topology = MintTopology();
        var outcomes = new List<EuFamilyEnumerationOutcome>(censusFamilies.Count * 5);

        // ---- Run and prove every census-family seed. ----
        var censusByFamilyKey = new Dictionary<
            string, (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationDeliveryReceipt Receipt, string RequestedCelex)>(
            StringComparer.Ordinal);
        foreach (var (request, sourceWitness) in censusFamilies)
        {
            var runResult = await _executor.RunCensusPartitionAsync(request, sourceWitness, cancellationToken)
                .ConfigureAwait(false);
            if (!TryRecordOutcome(runResult, out var familyKey, out var proof, out var receipt, outcomes))
            {
                continue;
            }

            if (proof is not null && receipt is not null)
            {
                censusByFamilyKey[familyKey] = (proof, receipt, request.RequestedCelex);
            }
        }

        if (censusByFamilyKey.Count != censusFamilies.Count)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.CensusFamilyNotProven,
                    "one or more requested census-family seeds did not prove this run's enumeration."));
        }

        // ---- Reopen and independently re-verify every proven family's own delivered rows. ----
        var censusRowsBySeed = new Dictionary<string, (IReadOnlyList<RepeatedEnumerationRow> Rows, RepeatedEnumerationInterpretationProfile Profile)>(
            StringComparer.Ordinal);
        foreach (var (familyKey, (proof, receipt, requestedCelex)) in censusByFamilyKey)
        {
            var profile = EuConsolidationDiscoveryPlan.Create().CreateDeliveryProfile(EuConsolidationQuerySet.Family);
            var (rows, reopenedProfile, refusal) = await ReopenAndVerifyAsync(proof, receipt, profile, cancellationToken)
                .ConfigureAwait(false);
            if (rows is null)
            {
                return EuQueryExecutionResult.Refused(
                    topology, outcomes,
                    new EuQueryExecutionRefusalDetail(
                        EuQueryExecutionRefusal.FamilyRowsNotVerified,
                        $"the census family for seed '{requestedCelex}' (family key '{familyKey}') did not " +
                        $"reverify: {refusal}."));
            }

            censusRowsBySeed[requestedCelex] = (rows, reopenedProfile);
        }

        // ---- D1-05g: derive O from THIS RUN'S OWN PROVEN CENSUS, then ask P, X, W and M. ----
        // The order is the fix. Before D1-05g the caller handed in the object lists and every
        // caller passed the seed ROOTS, so family P was asked about two objects while the decoder
        // walked root plus every consolidated state the census had just discovered. Lane A proved
        // it against the retained bytes: 41 rows over exactly two distinct objects, and
        // TryBuildPredicateObservation treats zero matches for a subject as malformed BY EXPLICIT
        // DESIGN, so the state arrived undescribed and the decode refused. A tolerance was tried
        // and cannot work: the content class is derived from these same family P rows, so decoding
        // an undescribed state as NotObserved produced ContentClassClosurePositionMismatch the
        // moment it ran. There is no reading of an absent row that yields a content class.
        var closuresByCelex = new Dictionary<string, (HashSet<string> Closure, string RootIri)>(StringComparer.Ordinal);
        var allRequestedSeedsClosure = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (requestedCelex, (familyRows, familyProfile)) in censusRowsBySeed)
        {
            var seedClosure = ExtractClosure(familyRows, familyProfile, requestedCelex, out var seedRootIri);
            closuresByCelex[requestedCelex] = (seedClosure, seedRootIri);
            allRequestedSeedsClosure.UnionWith(seedClosure);
        }


        var objectFactsRequests = EuObjectFactsBatchFactory.Build(
            objectFactsPolicy,
            allRequestedSeedsClosure,
            closuresByCelex.Values.Select(static entry => entry.RootIri).ToArray());

        // ---- Run and prove every object-facts batch (P, X, W, M). ----
        // Keyed by (Set, familyKey) rather than familyKey alone: EuObjectFactsDiscoveryPlan.PartitionKeyFor
        // is a pure function of the batch's own object set, never of which query set (P, X, W or M)
        // asked it, so two families sharing one batch of objects (the common case: P, X, W and M all
        // cover the same discovered closure) mint the IDENTICAL partition key. A dictionary keyed on
        // that key alone would silently collapse four proven families into one.
        var objectFactsByKey = new Dictionary<
            (EuObjectFactsQuerySet Set, string FamilyKey),
            (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationDeliveryReceipt Receipt)>();
        foreach (var request in objectFactsRequests)
        {
            var runResult = await _executor.RunObjectFactsPartitionAsync(
                    request, objectFactsPolicy.SourceWitness, cancellationToken)
                .ConfigureAwait(false);
            if (!TryRecordOutcome(runResult, out var familyKey, out var proof, out var receipt, outcomes))
            {
                continue;
            }

            if (proof is not null && receipt is not null)
            {
                objectFactsByKey[(request.Set, familyKey)] = (proof, receipt);
            }
        }

        if (objectFactsByKey.Count != objectFactsRequests.Count)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ObjectFactsFamilyNotProven,
                    "one or more requested object-facts family batches did not prove this run's enumeration."));
        }

        var objectFactsRows = new Dictionary<EuObjectFactsQuerySet, List<(IReadOnlyList<RepeatedEnumerationRow> Rows, RepeatedEnumerationInterpretationProfile Profile, AbsenceFamilyEnumerationProof Proof)>>();
        foreach (var ((set, familyKey), (proof, receipt)) in objectFactsByKey)
        {
            var profile = EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(set);
            var (rows, reopenedProfile, refusal) = await ReopenAndVerifyAsync(proof, receipt, profile, cancellationToken)
                .ConfigureAwait(false);
            if (rows is null)
            {
                return EuQueryExecutionResult.Refused(
                    topology, outcomes,
                    new EuQueryExecutionRefusalDetail(
                        EuQueryExecutionRefusal.FamilyRowsNotVerified,
                        $"the object-facts family '{set}' batch (family key '{familyKey}') did not " +
                        $"reverify: {refusal}."));
            }

            if (!objectFactsRows.TryGetValue(set, out var list))
            {
                list = [];
                objectFactsRows[set] = list;
            }

            list.Add((rows, reopenedProfile, proof));
        }

        if (!objectFactsRows.TryGetValue(EuObjectFactsQuerySet.ObjectFacts, out var pFamilies) || pFamilies.Count == 0 ||
            !objectFactsRows.TryGetValue(EuObjectFactsQuerySet.ExpressionFacts, out var xFamilies) || xFamilies.Count == 0 ||
            !objectFactsRows.TryGetValue(EuObjectFactsQuerySet.RootWatermark, out var wFamilies) || wFamilies.Count == 0 ||
            !objectFactsRows.TryGetValue(EuObjectFactsQuerySet.ManifestationFacts, out var mFamilies) || mFamilies.Count == 0)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ObjectFactsFamilyNotProven,
                    "this run must enumerate at least one proven batch of each of family P, X, W and M."));
        }

        var pProfile = pFamilies[0].Profile;
        var xProfile = xFamilies[0].Profile;
        var allPRows = pFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var allXRows = xFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var allWRows = wFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var wProfile = wFamilies[0].Profile;
        var allMRows = mFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var mProfile = mFamilies[0].Profile;

        // D1-05c-2 precision two: the evidence every observation in every decoded snapshot rests on
        // is family P's own interpretation-profile identity -- a real artifact this run actually
        // acquired, never a fabricated stand-in.
        var evidenceRef = pFamilies[0].Proof.InterpretationProfileRef;

        // D1-05d, REVIEW_RESULT lex-event-20260904T192428840Z-a6a8ebd26c58436aafd109a55303c12e
        // defect one: family M's format observations rest on family M's OWN proof, not P's. Before
        // this fix every format disposition was stamped with the ref above while this one went
        // unused, so a disposition named a listing it had not been read from and
        // EuManifestationListingDecode's own parameter doc was contradicted by its only real caller.
        var manifestationEvidenceRef = mFamilies[0].Proof.InterpretationProfileRef;

        // ---- Per seed: derive the closure from the census family's own rows, filter P/X to it, decode. ----
        var allSnapshots = new List<EuCellarObjectSnapshot>();
        var observedManifestationTypesByCelex =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var observedExpressionsByCelex =
            new Dictionary<string, EuObservedExpressionSplit>(StringComparer.Ordinal);
        var discoveredRoots = new List<string>();
        var expressionIris = new HashSet<string>(StringComparer.Ordinal);
        var rootWatermarkObservations = new List<(string WatermarkLexical, string CanonicalEntryKey)>();

        // Every requested seed's own closure, computed once up front, plus their union. Defect 1's
        // fix needs both: FilterByClosureColumn below must keep narrowing allPRows/allXRows down to
        // one seed's own subset (EuCellarObjectDecode.TryDecode refuses ANY row outside the ONE
        // closure it is handed, so a batch spanning several seeds' closures still has to be split by
        // seed before decode ever sees it) -- but a row belonging to NO requested seed at all must no
        // longer be silently dropped alongside a row that legitimately belongs to a sibling seed's
        // own closure.
        // ---- D1-05g: resolve EVERY seed's act form before decoding ANY of them. ----
        // The old shape returned on the first seed that failed, inside the decode loop. Both roots
        // of the canary fail identically when the mapping is wrong, and the message named only the
        // one reached first, which is how the defect read as a directive-specific problem for as
        // long as it did. A refusal that names one of two failures actively misdirects.
        var recordFormByCelex = new Dictionary<string, EuActForm>(StringComparer.Ordinal);
        var recordFormFailures = new List<string>();
        foreach (var (requestedCelex, _) in censusRowsBySeed)
        {
            var (closure, rootIri) = closuresByCelex[requestedCelex];
            var seedPRows = FilterByClosureColumn(
                allPRows, pProfile, "object", closure, allRequestedSeedsClosure);
            if (TryResolveRecordForm(
                    seedPRows, pProfile, rootIri, out var resolvedForm, out var conflict))
            {
                recordFormByCelex[requestedCelex] = resolvedForm;
                continue;
            }

            recordFormFailures.Add(
                $"seed '{requestedCelex}' (root '{rootIri}') "
                + (conflict is null
                    ? "observed "
                    : $"is CO-TYPED, carrying {conflict}, which cannot be resolved by row order. It observed ")
                + DescribeRowsForRoot(seedPRows, pProfile, rootIri));
        }

        if (recordFormFailures.Count != 0)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.RecordFormNotResolved,
                    $"{recordFormFailures.Count} of {censusRowsBySeed.Count} root(s) carry no "
                    + "act-form value this adapter can map to a closed EuActForm. "
                    + string.Join(" ", recordFormFailures)));
        }

        foreach (var (requestedCelex, (familyRows, familyProfile)) in censusRowsBySeed)
        {
            var (closure, rootIri) = closuresByCelex[requestedCelex];
            discoveredRoots.Add(rootIri);

            var seedPRows = FilterByClosureColumn(allPRows, pProfile, "object", closure, allRequestedSeedsClosure);
            var seedXRows = FilterByClosureColumn(allXRows, xProfile, "parent", closure, allRequestedSeedsClosure);
            // Family M is narrowed by its own ?parent column for the identical reason family X is:
            // EuCellarObjectDecode.TryDecode refuses any row outside the ONE closure it is handed.
            var seedMRows = FilterByClosureColumn(allMRows, mProfile, "parent", closure, allRequestedSeedsClosure);

            var recordForm = recordFormByCelex[requestedCelex];

            var snapshots = EuCellarObjectDecode.TryDecode(
                requestedCelex,
                familyRows,
                familyProfile,
                seedPRows,
                pProfile,
                seedXRows,
                xProfile,
                seedMRows,
                mProfile,
                manifestationEvidenceRef,
                recordForm,
                evidenceRef,
                out var decodeRefusal,
                out var offendingIri,
                out var snapshotRefusal,
                out var listingRefusal);
            if (snapshots is null)
            {
                return EuQueryExecutionResult.Refused(
                    topology, outcomes,
                    new EuQueryExecutionRefusalDetail(
                        EuQueryExecutionRefusal.ObjectDecodeRefused,
                        $"seed '{requestedCelex}' decode refused: {decodeRefusal}" +
                        (listingRefusal == EuManifestationListingRefusal.None
                            ? "."
                            : $" (manifestation listing: {listingRefusal}).")),
                    decodeRefusal,
                    offendingIri,
                    snapshotRefusal);
            }

            allSnapshots.AddRange(snapshots);
            CollectExpressionIris(seedXRows, xProfile, expressionIris);

            // D1-05g: the type set the canary compares against the census, taken from FAMILY M'S
            // OWN ROWS. Not from the ladder: the ladder is what this run attempted and was served,
            // which is a fact about our fetching rather than about the office's inventory.
            observedManifestationTypesByCelex[requestedCelex] =
                CollectListedManifestationTypes(seedMRows, mProfile, rootIri);
            observedExpressionsByCelex[requestedCelex] =
                SplitExpressionsByParent(seedXRows, xProfile, rootIri);
        }

        // ---- D1-05c-2 precision two: bind the discovered roots to Appendix A's own 82-seed pack. ----
        var rootBinding = EuPrimaryEnumerationRootBinding.TryBind(
            EuConsolidationDiscoveryPlan.Create().ArtifactRef, discoveredRoots, out var rootBindingRefusal);
        if (rootBinding is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.RootBindingRefused, rootBindingRefusal.ToString()));
        }

        // ---- Reduce every non-excluded snapshot. Precision four: the reduction never throws. ----
        var scopeProfile = EuScopeProfile.BuildBinding();
        // Family M's own proof joins the manifest's ordered evidence artifacts, because the format
        // selector cites it and ScopeReducer resolves every selector's evidence through this list.
        // Guarded against the two refs coinciding rather than assumed distinct: they are distinct
        // for every real run (P and M are different query sets with different interpretation
        // profiles), but a duplicate key here would refuse the whole run, which is a far worse
        // failure than sharing one ordinal.
        // ScopeManifest requires this list canonically sorted and unique (ScopeValidation's own
        // CompareArtifact: resource id, then digest), so the two refs are sorted here and their
        // ordinals read off the sorted order rather than assumed to be declaration order.
        // Only artifacts something actually cites may appear: ScopeReducer requires the table to
        // contain EXACTLY the referenced set, so family M's proof joins it if and only if at least
        // one snapshot carries a format observation to cite it. A run where the office listed
        // nothing for every object cites P's proof alone, and that is correct rather than a gap.
        var anyFormatObserved = allSnapshots.Exists(static snapshot => snapshot.Format is not null);
        var distinctEvidence = new List<SourceArtifactRef> { evidenceRef };
        if (anyFormatObserved && CompareEvidenceArtifact(evidenceRef, manifestationEvidenceRef) != 0)
        {
            distinctEvidence.Add(manifestationEvidenceRef);
        }

        distinctEvidence.Sort(CompareEvidenceArtifact);
        var orderedEvidenceArtifacts = distinctEvidence.ToArray();
        var evidenceOrdinals = new Dictionary<SourceArtifactRef, int>();
        for (var index = 0; index < orderedEvidenceArtifacts.Length; index++)
        {
            evidenceOrdinals[orderedEvidenceArtifacts[index]] = index;
        }
        var observedObjects = new List<SourceObjectRef>();
        var reductionInputs = new List<ScopeObjectReductionInput>();
        var exclusions = new List<EuObjectReductionExclusion>();
        // Defect 4's own fix: the real EuDocumentFetchAddress a Minted row's own manifest projection
        // (ScopeManifestFetchAddress) cannot carry back (it is deliberately thinner -- plain bounded
        // strings, not this route's own closed enums; see that type's own remarks), captured here so
        // the acquisition step below can actually send the GET for a Minted row without re-deriving
        // it from the publisher-neutral projection's own string fields.
        // D1-05d: one ORDERED ladder per object, not one address. Its first entry is the address the
        // manifest row carries; the rest are the fall-through attempts.
        var mintedAddressesByObjectRef =
            new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>();

        foreach (var snapshot in allSnapshots)
        {
            if (!TryGuardReducible(snapshot, out var offendingFamily, out var offendingReason))
            {
                exclusions.Add(new EuObjectReductionExclusion(snapshot.ObjectRef, offendingFamily, offendingReason));
                continue;
            }

            try
            {
                var dispositions = EuScopeSnapshotReduction.Reduce(snapshot);
                var (fetchAddress, mintedLadder) = MintFetchAddress(
                    dispositions.ObjectRef, dispositions.FormatDisposition);
                var input = EuScopeProfile.BuildScopeInput(
                    scopeProfile, dispositions, evidenceOrdinals, fetchAddress);
                observedObjects.Add(snapshot.ObjectRef);
                reductionInputs.Add(input);
                if (mintedLadder.Count > 0)
                {
                    mintedAddressesByObjectRef[dispositions.ObjectRef] = mintedLadder;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                or NotSupportedException or KeyNotFoundException)
            {
                // Defect 4's own fix. TryGuardReducible above predicts the exact two conditions
                // EuScopeSnapshotReduction.Reduce is known to throw for today (an
                // InvalidOperationException for mixed relation authorities, a NotSupportedException
                // for OntologyAuthorizedInverse) and excludes them before this call is ever reached;
                // that pre-check is kept as the documented, named reason for those two cases. This
                // catch is the actual safety net the "reduction never throws" claim requires: every
                // exception type Reduce or BuildScopeInput is documented (or, for BuildScopeInput's
                // own evidenceOrdinals[...] lookup, observed) to raise -- including one neither call
                // is known to throw today -- becomes this one object's own typed exclusion here,
                // exactly as the pre-check's own failure path already does, rather than escaping
                // RunAsync entirely and silently breaking every other object's own delivery too.
                exclusions.Add(new EuObjectReductionExclusion(
                    snapshot.ObjectRef,
                    default,
                    $"reduction threw {exception.GetType().Name}: {exception.Message}"));
            }
        }

        VerifiedScopeManifest manifest;
        try
        {
            manifest = ScopeReducer.Reduce(
                scopeProfile, orderedEvidenceArtifacts, observedObjects, reductionInputs, evidenceResolver);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // ScopeReducer.Reduce reduces every admitted object together into one manifest, so a
            // failure here cannot be pinned on one offending object the way the per-object catch
            // above can; it is reported as a whole-run refusal instead, never left to escape
            // uncaught.
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ScopeReductionRefused,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }

        using var manifestStream = new MemoryStream();
        var manifestCanonicalSha256 = ScopeManifestCanonicalWriter.Write(manifestStream, manifest);
        var manifestBytes = manifestStream.ToArray();

        // RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c: the manifest's observed
        // membership is recorded and this run continues. It used to refuse whenever the store
        // published no enforcement, throwing away a manifest that had been written correctly. The
        // membership is not asserted anywhere: this receipt travels out on
        // EuQueryExecutionResult.ScopeManifestReceipt, and CustodyMembershipClassifier.Classify is
        // the one rule that reads a class off it, exactly as CorpusBodyRecord.Held derives rather
        // than accepts one.
        //
        // What still refuses is a GENUINE custody failure, and CustodyHold is the one place that
        // decides it: it proves the hold by reopening the receipt's own digest through the checked
        // reader rather than trusting the write, and returns no receipt at all when it cannot, so
        // "stored under a weaker guarantee" and "failed to store" stay different facts. RULING
        // lex-event-20260904T222140534Z-4141e26bfe9d4ce18649118d06c4dbd7 routes both publishers through
        // that single definition rather than each lane writing its own.
        var (writeReceipt, holdFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, manifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (writeReceipt is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ScopeManifestNotRetained, holdFailure));
        }

        // Read for the BYTES, which the hold above deliberately discards. Not a second proof: the
        // hold already established the store reproduces exactly these bytes at this digest, and the
        // manifest binding below needs the reopened bytes themselves rather than that fact.
        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, writeReceipt.Reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);
        var manifestArtifactRef = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", manifestCanonicalSha256);
        // D1-06c-EU fix two: this run's own identity for the corpus/6 record set it writes as its
        // last step (see this method's own remarks below and this class's own remarks on RunAsync).
        // Paired with real evidence -- this exact run's own manifest custody-write digest, distinct
        // from manifestArtifactRef's own canonical digest above -- rather than an inert placeholder,
        // mirroring how every other minted SourceArtifactRef in this method pairs a fresh urn:uuid
        // with a real digest this run already computed.
        var runIdentityRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}", writeReceipt.Reference.ContentSha256);
        var reopenedManifest = EuScopeManifestBindingProof.TryOpenAsEuManifest(
            manifestArtifactRef, reopened.Span, evidenceResolver, out var bindingRefusal);
        if (reopenedManifest is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ManifestBindingRefused, bindingRefusal.ToString()));
        }

        // ---- Defect 4's own fix, and D1-06c-EU defect nine's own fix (REVIEW_RESULT
        // lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922): actually drive the fetch
        // for every reopened row whose own body axis is accepted, through the routed session,
        // classifying the real response and preparing this run's own CorpusAcquisitionOutcome per
        // ordinal. Extracted into RunDocumentAcquisitionAsync -- see that method's own remarks for
        // exactly what lands here versus what refuses the whole run instead, and for why the gate
        // moved there. ----
        var (documentAcquisitionOutcomesByOrdinal, documentLadderResultsByOrdinal,
                mintedRowAccounting, acquisitionRefusal) =
            await RunDocumentAcquisitionAsync(
                reopenedManifest, mintedAddressesByObjectRef, documentFetchRendererSource,
                documentFetchSourceWitness, cancellationToken)
            .ConfigureAwait(false);
        if (acquisitionRefusal is not null)
        {
            return EuQueryExecutionResult.Refused(topology, outcomes, acquisitionRefusal);
        }

        // ---- D1-05c-2 precision three: freeze the first-cut watermark witness. ----
        if (!TryCollectRootWatermarkObservations(
                allWRows, wProfile, rootBinding, rootWatermarkObservations, out var offendingWatermarkValue))
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.RootWatermarkBindingRefused,
                    $"a family-W row named '{offendingWatermarkValue}', which this run's own primary " +
                    "enumeration did not discover as a root, could not be canonicalized at all, or did " +
                    "not carry a literal watermark value."));
        }

        var startPosition = EuFirstCutWatermarkBootstrap.TryComputeStartPosition(
            rootWatermarkObservations, out var watermarkBootstrapRefusal);
        if (startPosition is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WatermarkBootstrapRefused, watermarkBootstrapRefusal.ToString()));
        }

        // DESIGN A: the witness is restricted to THIS PACK'S OWN OBJECTS, in batches, rather than
        // scanning the whole lastModificationDate graph. Measured rather than assumed: the
        // unrestricted query needs 66.9 seconds to first byte against a 60 second RequestTimeout,
        // because it sorts the whole feed; restricted to the pack it answers in 168 milliseconds
        // with ORDER BY intact. The alternative of keeping the whole feed and dropping the deep sort
        // was refuted by counting it: 17,230,321 rows after the bound, four orders of magnitude past
        // any page a run could hold.
        //
        // The capacity is family P's SYMBOL and never a literal, so a change there moves this too.
        var packObjects = allSnapshots
            .Select(static snapshot => snapshot.ObjectRef.PublisherUri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static iri => iri, StringComparer.Ordinal)
            .ToArray();
        var witnessBatches = new List<EuWatermarkWitnessPlan>();
        EuWatermarkPlanRefusal watermarkPlanRefusal = EuWatermarkPlanRefusal.None;
        for (var offset = 0; offset < packObjects.Length; offset += EuWatermarkWitnessPlan.BatchCapacity)
        {
            var batch = packObjects
                .Skip(offset)
                .Take(EuWatermarkWitnessPlan.BatchCapacity)
                .ToArray();
            var batchPlan = EuWatermarkWitnessPlan.TryFreeze(
                EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
                EuWatermarkWitnessPlan.WatermarkPredicateIri,
                EuWatermarkWitnessPlan.SortedResultWindowRows,
                startPosition,
                batch,
                out watermarkPlanRefusal);
            if (batchPlan is null)
            {
                witnessBatches.Clear();
                break;
            }

            witnessBatches.Add(batchPlan);
        }

        var witnessPlan = witnessBatches.Count == 0 ? null : witnessBatches[0];
        if (witnessPlan is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WatermarkPlanRefused, watermarkPlanRefusal.ToString()));
        }

        // ---- Defect 3's own fix: actually SEND the frozen witness plan and reconcile the REAL
        // delivered rows against this run's own primary enumeration, rather than reconciling against
        // an assumed-empty result the query was never sent for. The peer reviewer's own ruling is
        // exact: "Decision 81 fixes where the first cut's witness starts (the census bound); it does
        // not say the first cut skips the witness." So this run -- necessarily the first cut, since
        // TryComputeStartPosition above has nothing but THIS run's own census to have computed
        // startPosition from -- still has to run the witness's own traversal from that bound; it is
        // simply likely, not guaranteed, to observe few or zero rows beyond it.
        var traversal = await _executor.RunWitnessTraversalAsync(
                witnessBatches, witnessRendererSource, witnessSourceWitness, cancellationToken)
            .ConfigureAwait(false);
        if (traversal.Entries is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WitnessTraversalRefused,
                    $"code={traversal.Refusal!.Code} detail={traversal.Refusal.Detail}"));
        }

        var witnessClosureMatrixRef = new SourceArtifactRef(
            EuWitnessClosureMatrixResourceId, witnessPlan.QueryPlanIdentityDigest);

        // Coordinator fold-in: this ref used to be minted from a literal domain-hashed placeholder
        // string ("no_feed_acquisition_in_this_slice") admitting that no feed acquisition had
        // happened. Real feed acquisition now happens above, so the placeholder must not stand: the
        // digest is now traversal.DeliveryEvidenceSha256, real evidence this run actually observed
        // sending and receiving the witness query over HTTP. This still does not claim to be resolved
        // "identity predicates and canonical projections" content -- no identity resolver exists yet
        // (EuFeedEntryObservation's own remarks) -- only that the reference now names real, retained
        // acquisition evidence rather than an inert string.
        var witnessIdentityPredicateBindingRef = new SourceArtifactRef(
            EuWitnessIdentityPredicateBindingResourceId, traversal.DeliveryEvidenceSha256!);

        // D1-05g: THE DISCOVERED FAMILY IS THIS RUN'S OWN CLOSURE, not an empty list. Every state
        // here was delivered by the census this run proved and reverified, so a projection from a
        // state to its root is evidence this run acquired rather than an answer invented for the
        // witness. The family was empty before, which is why DiscoveredFamilyContains could never
        // be true and every delivered entry fell to the unresolved terminal.
        var witnessProjections = closuresByCelex
            .SelectMany(entry => entry.Value.Closure.Select(member => new EuFeedFamilyProjection(
                entry.Value.RootIri,
                entry.Key,
                member)))
            .DistinctBy(static projection => (projection.SourceWorkRoot, projection.ProjectedKey))
            .OrderBy(static projection => projection.SourceWorkRoot, StringComparer.Ordinal)
            .ThenBy(static projection => projection.ProjectedKey, StringComparer.Ordinal)
            .ToArray();

        var feedWitness = EuFeedRootIntersection.TryBind(
            EuConsolidationDiscoveryPlan.Create().ArtifactRef,
            witnessClosureMatrixRef,
            witnessIdentityPredicateBindingRef,
            rootBinding.DiscoveredRoots,
            witnessProjections,
            out var feedWitnessRefusal);
        if (feedWitness is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WitnessBindingRefused, feedWitnessRefusal.ToString()));
        }

        // D1-05g: AN ENTRY THIS RUN DEMONSTRABLY HOLDS IS RESOLVED FROM WHAT IT HOLDS.
        //
        // The previous shape observed EVERY entry with identityResolutionClosed: false, on the
        // reasoning that no identity resolver exists and writing one would be inventing the answer.
        // That reasoning was right about inventing and wrong about this case. The acceptance run
        // delivered exactly one entry, cellar/5f2552c2, and this same run had already proved,
        // reopened and reverified a census saying that IRI is a consolidated state of root
        // cellar/3e485e15, and written it a record. Terminating something we hold as
        // UnresolvedOrAmbiguous is not honesty, it is discarding evidence the run acquired.
        //
        // So the lookup is THIS RUN'S OWN CLOSURE and nothing else. An entry outside it stays
        // honestly unresolved, because for that one there really is no resolver.
        var projectionByEntry = witnessProjections.ToDictionary(
            static projection => projection.ProjectedKey, StringComparer.Ordinal);
        var rootByEntry = witnessProjections.ToDictionary(
            static projection => projection.ProjectedKey,
            static projection => projection.SourceWorkRoot,
            StringComparer.Ordinal);

        var terminations = new List<EuFeedEntryTermination>(traversal.Entries.Count);
        foreach (var entry in traversal.Entries.CanonicalEntries)
        {
            var canonicalEntry = EuPackRootCanonicalForm.TryCanonicalize(entry.CanonicalEntryKey, out _)
                ?? entry.CanonicalEntryKey;
            var resolved = projectionByEntry.TryGetValue(canonicalEntry, out var projection);
            var observation = EuFeedEntryObservation.TryObserve(
                entry,
                identityResolutionClosed: resolved,
                resolved ? [rootByEntry[canonicalEntry]] : Array.Empty<string>(),
                resolved ? [projection!] : Array.Empty<EuFeedFamilyProjection>(),
                out var observationRefusal)
                ?? throw new InvalidOperationException(
                    $"unreachable: a witness observation cannot itself be refused ({observationRefusal}).");
            terminations.Add(feedWitness.Classify(observation, traversal.Entries));
        }

        var witnessReconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            rootBinding, feedWitness, terminations, out var reconciliationRefusal);
        if (witnessReconciliation is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WitnessReconciliationRefused, reconciliationRefusal.ToString()));
        }

        // ---- D1-06c-EU fix two: write this run's whole corpus/6 record set as the last step, after
        // the manifest (above) and every document fetch this run attempted
        // (RunDocumentAcquisitionAsync above). Item 2 of SCOPE_RULING
        // lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93: before this fix nothing in
        // this codebase called CorpusRecordSetWriter.WriteAsync, so no corpus/6 record set was ever
        // durably written by a real run. Reuses this run's own scope-manifest custody floor
        // (CustodyClass.NightlyFloor90d), the exact constant CorpusRecordSetWriter itself already
        // requires.
        //
        // Defect nine's own fix (same event as above): documentAcquisitionOutcomesByOrdinal is handed
        // to the writer unfiltered here, and needs no second filter, because
        // RunDocumentAcquisitionAsync's own gate already means every key in it names an accepted-
        // selected body ordinal -- a Minted row this manifest's own body-axis policy excludes gets no
        // fetch attempt at all now, so it was never a candidate to appear in this dictionary in the
        // first place. An object minted but excluded from the body axis still gets a real corpus
        // record: CorpusRecordBuilder's own default path makes it NotHeld, naming the manifest's own
        // disposition as the reason. ----
        var recordSetWriter = new CorpusRecordSetWriter(_custodyStore);
        var recordSetResult = await recordSetWriter.WriteAsync(
                reopenedManifest, manifestArtifactRef, runIdentityRef, documentAcquisitionOutcomesByOrdinal,
                cancellationToken)
            .ConfigureAwait(false);
        if (recordSetResult.Refusal is not null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.RecordSetNotRetained, recordSetResult.Refusal.Detail));
        }

        return EuQueryExecutionResult.Delivered(
            topology,
            outcomes,
            observedObjectCount: allSnapshots.Count,
            observedExpressionCount: expressionIris.Count,
            reductionExclusions: exclusions,
            observedManifestationTypesByCelex: observedManifestationTypesByCelex,
            observedExpressionsByCelex: observedExpressionsByCelex,
            mintedRowsByOrdinal: mintedRowAccounting ?? new Dictionary<int, EuMintedRowAccounting>(),
            watermarkWitnessPlan: witnessPlan,
            rootBinding: rootBinding,
            witnessReconciliation: witnessReconciliation,
            witnessTerminations: terminations,
            scopeManifestReceipt: writeReceipt,
            scopeManifestCanonicalSha256: manifestCanonicalSha256,
            documentAcquisitionOutcomesByOrdinal: documentAcquisitionOutcomesByOrdinal!,
            documentLadderResultsByOrdinal: documentLadderResultsByOrdinal!,
            corpusRecordSetRef: recordSetResult.SetRef!,
            corpusRecordSet: recordSetResult.VerifiedSet!);
    }

    /// <summary>
    /// D1-06c-EU defect nine (REVIEW_RESULT
    /// lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922): drives the document-fetch GET
    /// for every reopened manifest row whose own body axis is
    /// <see cref="ScopeDisposition.AcceptedSelected"/> -- never for a Minted row this manifest's own
    /// body-axis policy has already excluded (for example <see cref="ScopeDisposition.TypedQuarantine"/>).
    /// Before this fix the loop attempted a fetch for every Minted row regardless of body axis and
    /// only filtered the outcomes afterward (right before handing them to
    /// <see cref="CorpusRecordSetWriter.WriteAsync"/>), so a real run issued one GET per quarantined
    /// object rather than one GET per accepted object, and every corpus record it wrote was NotHeld
    /// even though bytes had been fetched into custody. Extracted out of <see cref="RunAsync"/> so a
    /// test can drive this phase directly against a hand-built manifest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before D1-05d nothing in this codebase derived a real <see cref="EuFormatDisposition"/> for a
    /// decoded snapshot, so <see cref="EuScopeSnapshotReduction.Reduce"/>'s own body-axis join could
    /// never reach <see cref="ScopeDisposition.AcceptedSelected"/> through this codebase's real
    /// decode seam, and every real EU run gated every Minted row shut here and recorded NotHeld for
    /// every object. D1-05d closes that: family M's listing now mints a real disposition, so this
    /// gate lets a real object through for the first time.
    /// </para>
    /// <para>
    /// D1-05d's own fall-through (RULING
    /// lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262). A listed manifestation type
    /// is not a servable one, so each accepted row carries an ORDERED ladder of addresses rather
    /// than one address. This loop attempts them in order within this run; every attempt goes
    /// through the same routed session and is retained exactly as a single attempt already was
    /// (Decision 78). A 404 of the datastream shape -- the classified
    /// <see cref="EuDocumentFetchRefusal.RequestedRepresentationNotServed"/>, which is decisively
    /// not a bad token, since an invalid Accept answers 400 -- falls through to the next listed
    /// candidate. Anything else, including a 400, stops that object's ladder at once and is recorded
    /// as its own cause: falling through a bad token would hide a request defect behind a publisher
    /// fact. When every candidate answers that 404, the object records PendingAcquisition with
    /// <see cref="CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed"/>, and the tried
    /// types are named through this method's own returned ladder results.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The real per-ordinal outcomes this run's fetches produced together with each accepted row's
    /// own attempted-and-served formats, or a whole-run refusal for a route-level or classified
    /// shape this door's own closed <see cref="CorpusAcquisitionRefusalReason"/> vocabulary cannot
    /// represent. Never both, never neither.
    /// </returns>
    internal async Task<(
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? Outcomes,
        IReadOnlyDictionary<int, EuDocumentLadderResult>? LadderResults,
        IReadOnlyDictionary<int, EuMintedRowAccounting>? MintedRows,
        EuQueryExecutionRefusalDetail? Refusal)> RunDocumentAcquisitionAsync(
        ScopeManifest reopenedManifest,
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> mintedAddressesByObjectRef,
        MachineQueryRendererSource documentFetchRendererSource,
        BoundMachineRequest documentFetchSourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reopenedManifest);
        ArgumentNullException.ThrowIfNull(mintedAddressesByObjectRef);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        ArgumentNullException.ThrowIfNull(documentFetchSourceWitness);

        // Defect nine's own fix: the accepted-ordinal set is computed once, here, before the loop,
        // and gates iteration directly -- it no longer only narrows what gets handed to
        // CorpusRecordSetWriter.WriteAsync after every row has already been fetched.
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

        var documentAcquisitionOutcomesByOrdinal = new Dictionary<int, CorpusAcquisitionOutcome>();
        var ladderResultsByOrdinal = new Dictionary<int, EuDocumentLadderResult>();
        var mintedRows = new Dictionary<int, string>();
        var bodyAxisExcluded = new HashSet<int>();
        for (var rowOrdinal = 0; rowOrdinal < reopenedManifest.Rows.Count; rowOrdinal++)
        {
            var row = reopenedManifest.Rows[rowOrdinal];
            if (row.FetchAddress.Status != ScopeManifestFetchAddressStatus.Minted)
            {
                continue;
            }

            // D1-05g: EVERY MINTED ROW IS RECORDED, selected or not. The evidence index emitted a
            // row only for ordinals a fetch was attempted for, so a row the body axis excluded was
            // simply ABSENT and a reader could not tell "not selected" from "failed" from "never
            // attempted". A missing row is the worst form of the unobserved-versus-zero defect,
            // because there is not even a field to be wrong in.
            mintedRows[rowOrdinal] = reopenedManifest.ObservedObjects[rowOrdinal].ObjectRef.CanonicalKey;

            // Defect nine's own gate: no fetch attempt at all for a Minted row this manifest's own
            // body axis already excludes. The row above records that it existed and was not
            // selected; this skips the FETCH and not the accounting.
            if (!bodyAcceptedOrdinals.Contains(rowOrdinal))
            {
                bodyAxisExcluded.Add(rowOrdinal);
                continue;
            }

            var mintedObjectRef = reopenedManifest.ObservedObjects[rowOrdinal].ObjectRef;
            if (!mintedAddressesByObjectRef.TryGetValue(mintedObjectRef, out var ladder) || ladder.Count == 0)
            {
                // Unreachable in practice: every Minted row's own object came from this exact run's
                // own per-snapshot loop, the only path that ever mints one. Refusing the whole run
                // here, rather than throwing, keeps this method's own "never throws past a typed
                // refusal" discipline even for a defect this loop cannot itself introduce.
                return (null, null, null, new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.AcquisitionOutcomeNotRepresentable,
                    $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}') carries a " +
                    "Minted fetch address this run never itself minted."));
            }

            var attemptedMediaTypes = new List<EuManifestationMediaType>(ladder.Count);
            CorpusAcquisitionOutcome? rowOutcome = null;
            EuManifestationMediaType? servedMediaType = null;

            foreach (var candidateAddress in ladder)
            {
                attemptedMediaTypes.Add(candidateAddress.MediaType);
                var plan = new EuDocumentFetchPlan(candidateAddress);
                var bound = plan.Bind(
                    $"urn:uuid:{Guid.NewGuid():D}",
                    $"urn:uuid:{Guid.NewGuid():D}",
                    documentFetchRendererSource);
                var attempt = await _executor.RunDocumentFetchAsync(
                        bound.Request, documentFetchSourceWitness, cancellationToken)
                    .ConfigureAwait(false);
                if (attempt.Evidence is null)
                {
                    if (attempt.Refusal == EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused)
                    {
                        // Fold-in three (same REVIEW_RESULT as this method's own summary): a robots-
                        // bootstrap refusal is this one object's own PendingAcquisition cause, not a
                        // whole-run refusal. It ends this object's ladder rather than falling
                        // through: robots is a fact about the route, not about this format, so the
                        // next candidate on the same host would be refused identically.
                        rowOutcome = CorpusAcquisitionOutcome.Refused(
                            CorpusAcquisitionRefusalReason.RobotsDisallowed);
                        break;
                    }

                    // Every other attempt-level refusal (today, only ObservationNotExecuted) stays a
                    // whole-run refusal: this run's own document-fetch session never started at all,
                    // which is not a fact about any one object's own document.
                    return (null, null, null, new EuQueryExecutionRefusalDetail(
                        EuQueryExecutionRefusal.DocumentFetchSessionNotStarted,
                        $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): code=" +
                        $"{attempt.Refusal} detail={attempt.Detail}."));
                }

                var evidence = attempt.Evidence;
                var classified = EuDocumentFetchOutcome.Classify(evidence);
                if (classified.Refusal is null && classified.ObservedStatus == 200)
                {
                    // RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c: the body's observed
                    // membership is recorded on the record this gate produces, and the run
                    // continues. CorpusBodyRecord.Held(receipt) derives its own Floor through
                    // CustodyMembershipClassifier, so a body held without an enforced floor is
                    // recorded as RetainedUnenforced rather than costing the whole run a body the
                    // office had already served and this run had already written.
                    // Reading the FETCHED bytes back out of the acquisition session's own
                    // custody, by the terminal hop's digest. This read is not the hold; it is how
                    // this loop gets the bytes the route already retained.
                    ReadOnlyMemory<byte> bodyBytes;
                    try
                    {
                        bodyBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                                _custodyStore, evidence.Hops[^1].Sha256, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                        when (exception is CustodyIntegrityException or CustodyRequiredException)
                    {
                        return (null, null, null, new EuQueryExecutionRefusalDetail(
                            EuQueryExecutionRefusal.DocumentBodyNotRetained,
                            $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): the " +
                            $"fetched body could not be reread: {exception.GetType().Name}: " +
                            $"{exception.Message}"));
                    }

                    var (bodyReceipt, bodyHoldFailure) = await CustodyHold
                        .TryHoldAsync(_custodyStore, bodyBytes, cancellationToken)
                        .ConfigureAwait(false);
                    if (bodyReceipt is null)
                    {
                        return (null, null, null, new EuQueryExecutionRefusalDetail(
                            EuQueryExecutionRefusal.DocumentBodyNotRetained,
                            $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): " +
                            bodyHoldFailure));
                    }

                    rowOutcome = CorpusAcquisitionOutcome.Held(bodyReceipt);
                    servedMediaType = candidateAddress.MediaType;
                    break;
                }

                // D1-05d's fall-through, and the ONE condition that takes it. The office listed this
                // type and answered "does not hold a content datastream of the requested type", so
                // the next LISTED candidate is worth attempting. Every other outcome, a 400 "Illegal
                // accept header" included, ends this object's ladder at its own recorded cause.
                if (classified.Refusal == EuDocumentFetchRefusal.RequestedRepresentationNotServed)
                {
                    continue;
                }

                if (TryMapDocumentFetchToCorpusAcquisitionRefusal(classified, evidence, out var representableRefusal))
                {
                    // D1-06c-EU fix one: this object's own document-fetch refusal becomes its own
                    // PendingAcquisition cause. The outer loop continues to the next row: one
                    // object's document being unavailable must never prevent every OTHER object in
                    // the same run from getting its own record.
                    rowOutcome = CorpusAcquisitionOutcome.Refused(representableRefusal);
                    break;
                }

                // Neither a real 200, nor this route's own named shapes (fix one), nor a robots-
                // bootstrap refusal (fold-in three), nor a terminal status this door can name (fold-in
                // one), nor a transport-incomplete shape D1-06b's own CorpusAcquisitionRefusalReason
                // vocabulary can name (see EuQueryExecutionRefusal.AcquisitionOutcomeNotRepresentable's
                // own remarks): a route-level refusal (robots-policy-unavailable, redirect refused, looped
                // or limit-exceeded, or a stale profile) that vocabulary was never scoped to cover. The
                // whole run refuses, naming the real classified cause, rather than mapping it to an
                // unrelated existing member or silently accepting it as held.
                var routeOutcomeDetail = evidence.Outcome is IncompleteHttpRouteOutcome incompleteOutcome
                    ? $"{evidence.Outcome.GetType().Name}({incompleteOutcome.Reason})"
                    : evidence.Outcome.GetType().Name;
                return (null, null, null, new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.AcquisitionOutcomeNotRepresentable,
                    $"manifest row {rowOrdinal} ('{mintedObjectRef.CanonicalKey}'): classified " +
                    $"refusal={classified.Refusal} observedStatus={classified.ObservedStatus} " +
                    $"routeOutcome={routeOutcomeDetail}."));
            }

            // Every listed candidate answered the datastream-absent 404. RULING
            // lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262 point three: the object
            // records PendingAcquisition with RequestedRepresentationNotServed, and the tried types
            // travel back in this row's own ladder result. No new vocabulary member.
            rowOutcome ??= CorpusAcquisitionOutcome.Refused(
                CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed);

            documentAcquisitionOutcomesByOrdinal[rowOrdinal] = rowOutcome;
            ladderResultsByOrdinal[rowOrdinal] = new EuDocumentLadderResult(
                Array.AsReadOnly(attemptedMediaTypes.ToArray()), servedMediaType);
        }

        return (documentAcquisitionOutcomesByOrdinal, ladderResultsByOrdinal,
            mintedRows.ToDictionary(
                static pair => pair.Key,
                pair => new EuMintedRowAccounting(pair.Value, !bodyAxisExcluded.Contains(pair.Key))),
            null);
    }

    /// <summary>
    /// D1-06c-EU fix one: whether a document-fetch's own classified outcome is one of this route's
    /// three named shapes -- <see cref="EuDocumentFetchRefusal.WrongAcceptToken"/>,
    /// <see cref="EuDocumentFetchRefusal.RequestedRepresentationNotServed"/> or
    /// <see cref="HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted"/> -- or, D1-06c-EU
    /// defect nine's own fold-in one, a terminal status this route completed at for real but
    /// <see cref="EuDocumentFetchOutcome.Classify"/> does not name (neither 200 nor its own closed
    /// 400/404 business vocabulary), that <see cref="CorpusAcquisitionRefusalReason"/> now carries as
    /// one object's own typed cause, falling back to <see cref="TryMapToCorpusAcquisitionRefusal"/>'s
    /// own hop-level mirror for everything else.
    /// </summary>
    private static bool TryMapDocumentFetchToCorpusAcquisitionRefusal(
        EuDocumentFetchOutcome classified, RoutedHttpEvidence evidence, out CorpusAcquisitionRefusalReason mapped)
    {
        switch (classified.Refusal)
        {
            case EuDocumentFetchRefusal.WrongAcceptToken:
                mapped = CorpusAcquisitionRefusalReason.WrongAcceptToken;
                return true;
            case EuDocumentFetchRefusal.RequestedRepresentationNotServed:
                mapped = CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed;
                return true;
        }

        if (evidence.Outcome is IncompleteHttpRouteOutcome
            { Reason: HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted })
        {
            mapped = CorpusAcquisitionRefusalReason.RedirectTargetOriginNotAdmitted;
            return true;
        }

        // Fold-in one (REVIEW_RESULT lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922):
        // a route that completed for real, at a terminal status this route has no reviewed reading
        // for (a 500, a 503, a 429 -- anything but 200, 400 or 404), is this one object's own typed
        // cause: the object's document is unavailable in this publisher response, not a fact about
        // the run's own document-fetch capability.
        if (classified.Refusal is null && evidence.Outcome is CompleteHttpRouteOutcome &&
            classified.ObservedStatus is { } observedStatus && observedStatus != 200)
        {
            mapped = CorpusAcquisitionRefusalReason.UnexpectedPublisherStatus;
            return true;
        }

        return TryMapToCorpusAcquisitionRefusal(evidence, out mapped);
    }

    /// <summary>
    /// Defect 4's own honest boundary: whether a hop-level transport-incomplete outcome has a
    /// faithful member in D1-06b's own closed <see cref="CorpusAcquisitionRefusalReason"/>
    /// vocabulary. That vocabulary mirrors <see cref="HttpAcquisitionReasonRegistry"/>'s own fourteen
    /// entity-transfer, before-response-headers, completion-unproven and response-semantics reasons
    /// one for one under the identical wire names (both are literally authored against
    /// <c>schemas/v3-source/http/http-acquisition-reason-registry.json</c>), so a hop's own
    /// already-checked registry member maps by its wire key alone -- never by re-deriving or guessing
    /// one. Every other route-level or classified-business shape (see this method's own caller) has no
    /// member here and returns false.
    /// </summary>
    private static bool TryMapToCorpusAcquisitionRefusal(
        RoutedHttpEvidence evidence, out CorpusAcquisitionRefusalReason mapped)
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
    /// Fixed resource-id halves of defect 3's own witness binding's two artifact references (see the
    /// reasoning in <see cref="RunAsync"/> just above where these are used), minted the same way
    /// <see cref="MintTopology"/>'s own <c>registryRef</c> resource id already is: a fixed literal
    /// naming a structural domain, never a claim about an observation nobody has taken.
    /// <see cref="EuWitnessClosureMatrixResourceId"/> pairs with a real digest --
    /// <see cref="EuWatermarkWitnessPlan.QueryPlanIdentityDigest"/>, the one
    /// <see cref="EuPrimaryEnumerationWitnessReconciliation.TryReconcile"/>'s own independence check
    /// actually compares against <see cref="EuPrimaryEnumerationRootBinding.ClosureQueryPlanRef"/> --
    /// so only its resource id, not its content digest, is a placeholder.
    /// <see cref="EuWitnessIdentityPredicateBindingResourceId"/> pairs with
    /// <c>traversal.DeliveryEvidenceSha256</c>, real evidence this run actually observed from sending
    /// the witness query over HTTP and receiving its real response (see <see cref="RunAsync"/>'s own
    /// remarks at that call site). It stood as a fully inert domain-hashed placeholder before defect
    /// 3's real-execution fix, admitting in its own digest input that no feed acquisition had
    /// happened; now that real acquisition happens on every run, leaving that placeholder standing
    /// would misreport a real run as one that performed none. This still is not a claim that
    /// resolved "identity predicates and canonical projections" content exists -- no identity
    /// resolver has been built (<see cref="EuFeedEntryObservation"/>'s own remarks) -- only that the
    /// reference now names real, retained acquisition evidence.
    /// </summary>
    private const string EuWitnessClosureMatrixResourceId = "urn:uuid:7d3a1f2e-4b6c-4a9d-8e5f-1a2b3c4d5e6f";

    private const string EuWitnessIdentityPredicateBindingResourceId = "urn:uuid:9f4b2c1d-6e3a-4f8b-9c2d-3e4f5a6b7c8d";

    /// <summary>
    /// D1-05c-2's own <see cref="SourceProfileTopology"/> minting, mirroring
    /// <c>LuxembourgSourceProfileTopology.Mint</c> exactly, adapted to this path claim: the two-pass
    /// reconciliation this executor performs is not a second independent witness (both passes read
    /// the one Cellar Virtuoso store), so R3.2 requires this to say so explicitly. Kept here rather
    /// than as a Contracts-layer type for the same path-claim reason as <see cref="EuDeliveryEvidence"/>'s
    /// own types: <see cref="SourceProfileTopology"/> is a plain public Core record with no
    /// construction guard restricting who may mint one.
    /// </summary>
    private static SourceProfileTopology MintTopology()
    {
        var registryRef = new SourceArtifactRef(
            "urn:uuid:2c2f9c7e-9f0b-4b8e-9a4a-4a6a3b6f4c9e",
            SingleMemberRegistrySha256("lex-v3-eu-source-profile-topology-registry/1", "single_publisher_store"));
        var member = new SourceRegistryMemberRef(registryRef, "single_publisher_store");
        return new SourceProfileTopology(
            SourceCoreSchemaIds.SourceProfileTopology, EuScopeProfile.BuildBinding().SourceProfileRef, member);
    }

    private static string SingleMemberRegistrySha256(string domain, string memberKey)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        void Append(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        Append(domain);
        Append(memberKey);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool TryRecordOutcome(
        EuEnumerationRunResult runResult,
        out string familyKey,
        out AbsenceFamilyEnumerationProof? proof,
        out RepeatedEnumerationDeliveryReceipt? receipt,
        List<EuFamilyEnumerationOutcome> outcomes)
    {
        proof = null;
        receipt = null;
        if (runResult.Receipt is { } deliveredReceipt)
        {
            familyKey = deliveredReceipt.Delivery.PartitionKey;
            var provenProof = deliveredReceipt.TryProveFamilyEnumeration(familyKey, out var proofRefusal);
            if (provenProof is not null)
            {
                outcomes.Add(EuFamilyEnumerationOutcome.Proven(familyKey, provenProof));
                proof = provenProof;
                receipt = deliveredReceipt;
                return true;
            }

            outcomes.Add(EuFamilyEnumerationOutcome.ProofRefused(familyKey, proofRefusal));
            return false;
        }

        // No family key is available at all: the executor refused before ever binding a request this
        // run can name. A synthetic, unique key still lets the caller's own family-count checks tell
        // "every requested family proved" apart from "one refused before delivering".
        familyKey = $"executor-refused-{Guid.NewGuid():N}";
        outcomes.Add(EuFamilyEnumerationOutcome.ExecutorRefused(familyKey, runResult.Refusal!));
        return false;
    }

    private async Task<(
        IReadOnlyList<RepeatedEnumerationRow>? Rows,
        RepeatedEnumerationInterpretationProfile Profile,
        RepeatedEnumerationRowsOpenRefusal Refusal)> ReopenAndVerifyAsync(
        AbsenceFamilyEnumerationProof proof,
        RepeatedEnumerationDeliveryReceipt receipt,
        RepeatedEnumerationInterpretationProfile profile,
        CancellationToken cancellationToken)
    {
        var delivery = receipt.Delivery;
        var pages = new List<RepeatedEnumerationResolvedEvidence>(delivery.PagesA.Pages.Count);
        foreach (var pageRef in delivery.PagesA.Pages.OrderBy(static page => page.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(pageRef.Evidence, cancellationToken).ConfigureAwait(false));
        }

        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof, delivery, profile, delivery.InterpretationProfileRef, delivery.CountA.HttpEvidenceRef, pages,
            out var refusal);
        return (rows, profile, refusal);
    }

    /// <summary>
    /// The DISTINCT manifestation type tokens family M listed for one Work, sorted ordinally.
    /// </summary>
    /// <remarks>
    /// Rows whose value is unbound are the office's own typed ABSENCE and contribute no token, so a
    /// Work the office lists nothing for yields an EMPTY set rather than a missing entry. That
    /// distinction is the point: an empty set is an observation, and a missing key would be a
    /// question never asked.
    /// </remarks>
    private static IReadOnlyList<string> CollectListedManifestationTypes(
        IReadOnlyList<RepeatedEnumerationRow> mRows,
        RepeatedEnumerationInterpretationProfile mProfile,
        string rootIri)
    {
        var parentIndex = IndexOf(mProfile, "parent");
        var valueIndex = IndexOf(mProfile, "value");
        var valueKindIndex = IndexOf(mProfile, "value_kind");
        return mRows
            .Where(row =>
            {
                var parent = row.Terms[parentIndex].Value;
                return parent is not null
                    && EuPackRootCanonicalForm.TryCanonicalize(parent, out _) == rootIri;
            })
            .Where(row => row.Terms[valueKindIndex].Value == "literal")
            .Select(row => row.Terms[valueIndex].Value)
            .Where(static value => !string.IsNullOrEmpty(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Extracts D1-05c-1's own closure (the root plus every discovered consolidated state) from
    /// D1-05a's family rows, exactly as <see cref="EuCellarObjectDecode.TryDecode"/> does internally --
    /// duplicated here only because this adapter needs the closure BEFORE calling that door, to filter
    /// family P and X's own batch-wide rows down to one seed's own subset (proposal B section c: "the
    /// rule is D1-04b's, over IRIs instead of subjects, run on the raw subject term before any
    /// predicate admission"). <see cref="EuCellarObjectDecode.TryDecode"/> re-derives and re-checks the
    /// identical closure from the same rows independently, so a disagreement here can only ever narrow
    /// what this adapter filters in, never something decode would silently trust instead.
    /// </summary>
    private static HashSet<string> ExtractClosure(
        IReadOnlyList<RepeatedEnumerationRow> familyRows,
        RepeatedEnumerationInterpretationProfile familyProfile,
        string requestedCelex,
        out string rootIri)
    {
        var baseIndex = IndexOf(familyProfile, "base");
        var stateIndex = IndexOf(familyProfile, "state");
        string? root = null;
        var closure = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in familyRows)
        {
            var baseValue = row.Terms[baseIndex].Value;
            var stateValue = row.Terms[stateIndex].Value;
            if (baseValue is null || stateValue is null)
            {
                continue;
            }

            var canonicalBase = EuPackRootCanonicalForm.TryCanonicalize(baseValue, out _);
            var canonicalState = EuPackRootCanonicalForm.TryCanonicalize(stateValue, out _);
            if (canonicalBase is null || canonicalState is null)
            {
                continue;
            }

            root ??= canonicalBase;
            closure.Add(canonicalState);
        }

        root ??= EuAppendixASeedMap.SeedsInCelexOrder
            .Where(seed => string.Equals(seed.Celex, requestedCelex, StringComparison.Ordinal))
            .Select(static seed => EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _))
            .FirstOrDefault(static value => value is not null)
            ?? throw new InvalidOperationException(
                "requestedCelex must be one of Appendix A's 82 admitted seeds.");
        closure.Add(root);
        rootIri = root;
        return closure;
    }

    /// <summary>
    /// Every row of <paramref name="rows"/> whose <paramref name="columnVariableName"/> term
    /// canonicalizes to a member of <paramref name="closure"/> (this seed's own <c>O</c>), plus every
    /// row that canonicalizes to something outside <paramref name="closure"/> but IS a member of
    /// <paramref name="allRequestedSeedsClosure"/> -- this run's own union closure across every
    /// requested seed: that row legitimately belongs to a sibling seed's own decode call, and is
    /// dropped here only because that other seed's own pass, not this one, is the one that narrows and
    /// decodes it.
    /// </summary>
    /// <remarks>
    /// Defect 1's fix. A row that does not canonicalize at all, or that canonicalizes to an object no
    /// requested seed's census discovered, is left in unconditionally:
    /// <see cref="EuCellarObjectDecode.TryDecode"/> is the one door that turns that shape into a typed
    /// refusal naming the offending IRI (D1-05c-2 precision two), so this filter must never silently
    /// drop a row decode would otherwise refuse. Before this fix every out-of-closure row was dropped
    /// here regardless of which case it was, so a row belonging to no seed at all was silently lost
    /// rather than ever reaching decode's own refusal.
    /// </remarks>
    private static IReadOnlyList<RepeatedEnumerationRow> FilterByClosureColumn(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        string columnVariableName,
        HashSet<string> closure,
        HashSet<string> allRequestedSeedsClosure)
    {
        var index = IndexOf(profile, columnVariableName);
        var result = new List<RepeatedEnumerationRow>();
        foreach (var row in rows)
        {
            var value = row.Terms[index].Value;
            var canonical = value is null ? null : EuPackRootCanonicalForm.TryCanonicalize(value, out _);
            if (canonical is not null && !closure.Contains(canonical) && allRequestedSeedsClosure.Contains(canonical))
            {
                continue;
            }

            result.Add(row);
        }

        return result;
    }

    /// <summary>
    /// Every family P row this run observed for one root, as predicate, value_kind and value
    /// VERBATIM, for a refusal that has to be actionable without a second run.
    /// </summary>
    /// <remarks>
    /// A refusal saying a value could not be mapped WITHOUT saying what the value was names
    /// nothing: the reader cannot tell an unadmitted form from an absent predicate from a
    /// projection that never selected it, and those are three different defects with three
    /// different fixes. Only the bytes distinguish them, so the bytes travel with the refusal.
    /// The absent case is stated in words rather than as an empty list, because an empty list
    /// reads as a rendering bug.
    /// </remarks>
    private static string DescribeRowsForRoot(
        IReadOnlyList<RepeatedEnumerationRow> pRows,
        RepeatedEnumerationInterpretationProfile pProfile,
        string rootIri)
    {
        var objectIndex = IndexOf(pProfile, "object");
        var predicateIndex = IndexOf(pProfile, "predicate");
        var valueIndex = IndexOf(pProfile, "value");
        var valueKindIndex = IndexOf(pProfile, "value_kind");

        var described = pRows
            .Where(row =>
            {
                var objectValue = row.Terms[objectIndex].Value;
                return objectValue is not null
                    && EuPackRootCanonicalForm.TryCanonicalize(objectValue, out _) == rootIri;
            })
            .Select(row =>
            {
                var predicate = row.Terms[predicateIndex].Value ?? "(unbound)";
                var valueKind = row.Terms[valueKindIndex].Value ?? "(unbound)";
                var value = row.Terms[valueIndex].Value ?? "(unbound)";
                return $"[predicate={predicate} value_kind={valueKind} value={value}]";
            })
            .OrderBy(static entry => entry, StringComparer.Ordinal)
            .ToArray();

        return described.Length == 0
            ? "NO family P row at all, so the predicate was not delivered for this root."
            : $"{described.Length} family P row(s): {string.Join(" ", described)}";
    }

    /// <summary>
    /// D1-05c-1's own decode contract requires a caller-resolved <see cref="EuActForm"/> as an input
    /// it does not itself derive ("Not recoverable from these closures' rows; the caller supplies it
    /// from wherever it independently resolves resource_legal_type" -- <see cref="EuCellarObjectDecode.TryDecode"/>'s
    /// own doc comment). This reads it from the SAME family P rows this run already acquired for the
    /// seed's own root, read by its last IRI path segment against the EU Publications Office
    /// resource-type authority table's own short-code convention (the same convention
    /// <see cref="EuCellarObjectDecode"/>'s own <c>CONSOLID_ACT</c> marker and
    /// <see cref="EuScopeProfile.RecordFormToken"/>'s wire tokens both already use). This is not a
    /// new Contracts-layer mapping invented for this slice: it is reading a value already present in
    /// already-acquired publisher data, never a new query or a guess. A root whose every observed
    /// value fails to map refuses this seed's decode rather than defaulting to any closed member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// D1-05g: THE PREDICATE IS <c>work_has_resource-type</c> AND IT USED TO BE THE WRONG ONE. This
    /// guard asked for <c>resource_legal_type</c> AND required <c>value_kind</c> of <c>"iri"</c>.
    /// Those two conditions are mutually exclusive against the publisher's own data:
    /// <c>resource_legal_type</c> carries a one-letter STRING LITERAL, measured as <c>"L"</c> for
    /// the directive root and <c>"R"</c> for the regulation root, so the loop skipped every row and
    /// the switch below was never reached for any root. The switch speaks
    /// <c>work_has_resource-type</c>'s vocabulary, whose values ARE authority IRIs ending DIR, REG
    /// and TREATY, and both predicates were already being projected and retained side by side.
    /// </para>
    /// <para>
    /// THE AUTHORITY FOR CHOOSING IT, cited by address rather than summarised:
    /// <c>coordination/measurements/D1-EU-DIRECT-SEED-RESOURCE-TYPES-2026-09-01.md</c> line 32
    /// joins through the <c>resource_legal_id_celex</c> and <c>work_has_resource-type</c> graph
    /// pattern, over the 82-seed inventory at seed SHA-256
    /// <c>ea1b4f276406a8bede5223459b92d7a94321de5b9a38de63397f2e22688d50c0</c>, and records the
    /// complete observed direct-seed partition as TREATY 6, DIR 40, REG 36.
    /// </para>
    /// <para>
    /// TWO CLAIMS THAT MUST NOT BE BLURRED. THE MEASUREMENT PROVES THE 82: every seed in the pack
    /// carries one of three values and the switch below already maps all three, so no seed needs a
    /// new <see cref="EuActForm"/> member and D1-05g is a wiring fix rather than a vocabulary
    /// admission. THE RUN PROVES TWO: the canary reaches two roots and demonstrates DIR and REG on
    /// those. Closure over today's pack is not closure over tomorrow's publisher, so any value
    /// outside the switch stays a typed refusal naming the value VERBATIM rather than being mapped
    /// to a nearest member or dropped.
    /// </para>
    /// </remarks>
    private static bool TryResolveRecordForm(
        IReadOnlyList<RepeatedEnumerationRow> pRows,
        RepeatedEnumerationInterpretationProfile pProfile,
        string rootIri,
        out EuActForm recordForm,
        out string? observedConflict)
    {
        var objectIndex = IndexOf(pProfile, "object");
        var predicateIndex = IndexOf(pProfile, "predicate");
        var valueIndex = IndexOf(pProfile, "value");
        var valueKindIndex = IndexOf(pProfile, "value_kind");
        // D1-05g. Reached through the typed accessor like every other guard in this reduction,
        // now that Decision 80 has made it a pinned public door. The literal that used to sit here
        // named a DIFFERENT predicate from the one the switch below speaks, and a string literal is
        // checked against nothing, which is how the two drifted apart unnoticed.
        var actFormPredicateIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkHasResourceType);

        observedConflict = null;
        (EuActForm Form, string Value)? observed = null;
        foreach (var row in pRows)
        {
            var objectValue = row.Terms[objectIndex].Value;
            if (objectValue is null || EuPackRootCanonicalForm.TryCanonicalize(objectValue, out _) != rootIri)
            {
                continue;
            }

            if (row.Terms[predicateIndex].Value != actFormPredicateIri ||
                row.Terms[valueKindIndex].Value != "iri")
            {
                continue;
            }

            var value = row.Terms[valueIndex].Value;
            if (value is null)
            {
                continue;
            }

            // AN UNRECOGNISED VALUE IS A REFUSAL, NOT A ROW TO SKIP. Continuing past it meant a
            // root carrying one mappable value and one unknown classified on the mappable one and
            // DROPPED THE UNKNOWN SILENTLY, which is narrower than the measurement this resolver
            // cites: that cut fails EXTRA and UNKNOWN values as well as co-typed ones, so a root
            // the office has since given a second, unrecognised type would have been recorded as
            // though the office still typed it once.
            if (!TryMapResourceTypeCode(value, out var mapped))
            {
                observedConflict = observed is null
                    ? value + " (unrecognised)"
                    : observed.Value.Value + " and " + value + " (the second unrecognised)";
                recordForm = default;
                return false;
            }

            // A SECOND MAPPABLE VALUE IS A REFUSAL, NOT A TIE BROKEN BY ROW ORDER. This used to
            // return on the first value it met, so a co-typed root would have been classified by
            // whichever row the publisher happened to deliver first, and the answer would change
            // between runs without anything saying so. The canary cannot see this: both its seeds
            // are singletons. The accepted 82-seed measurement is what makes it reachable, since it
            // admits exactly six singleton TREATY, forty singleton DIR and thirty six singleton REG
            // sets and records the predicate itself as MULTIVALUED, and says missing, extra,
            // co-typed, unknown, case-altered or unicode-aliased values fail the direct-seed cut.
            if (observed is not null && mapped != observed.Value.Form)
            {
                observedConflict = observed.Value.Value + " and " + value;
                recordForm = default;
                return false;
            }

            observed ??= (mapped, value);
        }

        if (observed is not null)
        {
            recordForm = observed.Value.Form;
            return true;
        }

        recordForm = default;
        return false;
    }

    private static bool TryMapResourceTypeCode(string resourceTypeIri, out EuActForm form)
    {
        var code = resourceTypeIri[(resourceTypeIri.LastIndexOf('/') + 1)..];
        switch (code)
        {
            case "DIR": form = EuActForm.Directive; return true;
            case "REG": form = EuActForm.Regulation; return true;
            case "REG_DEL": form = EuActForm.DelegatedRegulation; return true;
            case "REG_IMPL": form = EuActForm.ImplementingRegulation; return true;
            case "TREATY": form = EuActForm.Treaty; return true;
            case "CORRIGENDUM": form = EuActForm.Corrigendum; return true;
            case "DIR_DEL": form = EuActForm.DelegatedDirective; return true;
            case "DEC_IMPL": form = EuActForm.ImplementingDecision; return true;
            case "DEC": form = EuActForm.Decision; return true;
            case "DEC_ENTSCHEID": form = EuActForm.DecisionEntscheid; return true;
            case "DIR_IMPL": form = EuActForm.ImplementingDirective; return true;
            case "DEC_DEL": form = EuActForm.DelegatedDecision; return true;
            default: form = default; return false;
        }
    }

    /// <summary>
    /// One seed's distinct expressions, split by whether their parent is the ROOT Work or one of
    /// the consolidated states this run's census discovered.
    /// </summary>
    private static EuObservedExpressionSplit SplitExpressionsByParent(
        IReadOnlyList<RepeatedEnumerationRow> xRows,
        RepeatedEnumerationInterpretationProfile xProfile,
        string rootIri)
    {
        var parentIndex = IndexOf(xProfile, "parent");
        var objectIndex = IndexOf(xProfile, "object");
        var ofRoot = new HashSet<string>(StringComparer.Ordinal);
        var ofStates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in xRows)
        {
            var parent = row.Terms[parentIndex].Value;
            var expression = row.Terms[objectIndex].Value;
            if (parent is null || expression is null)
            {
                continue;
            }

            var canonicalParent = EuPackRootCanonicalForm.TryCanonicalize(parent, out _);
            if (string.Equals(canonicalParent, rootIri, StringComparison.Ordinal))
            {
                ofRoot.Add(expression);
            }
            else
            {
                ofStates.Add(expression);
            }
        }

        return new EuObservedExpressionSplit(ofRoot.Count, ofStates.Count);
    }

    private static void CollectExpressionIris(
        IReadOnlyList<RepeatedEnumerationRow> xRows, RepeatedEnumerationInterpretationProfile xProfile, HashSet<string> into)
    {
        var objectIndex = IndexOf(xProfile, "object");
        foreach (var row in xRows)
        {
            var value = row.Terms[objectIndex].Value;
            if (value is not null)
            {
                into.Add(value);
            }
        }
    }

    /// <summary>
    /// Defect 2's own fix. A family-W row's own object term must bind by identity to this run's own
    /// primary enumeration (<paramref name="rootBinding"/>) exactly as P and X already do (precision
    /// two): a root W names that <paramref name="rootBinding"/> never discovered is refused naming
    /// that IRI, never silently added as though it were one of O's own roots. A row whose object term
    /// fails to canonicalize at all is likewise refused, never silently skipped -- before this fix
    /// both conditions were silently dropped here, the exact "skip instead of refuse" shape this
    /// session's review keeps catching on both country adapters.
    /// </summary>
    /// <remarks>
    /// Defect 6's own fix. A row whose <c>value_kind</c> is not <c>"literal"</c> (the watermark
    /// predicate's own explicit-absence shape: <c>EuObjectFactsDiscoveryPlan</c>'s own
    /// <c>FILTER NOT EXISTS</c> branch emits <c>"unbound"</c> for a root that carries no
    /// <c>cmr:lastModificationDate</c> at all) used to be silently skipped with a bare
    /// <c>continue</c>, which contradicted this method's own already-shipped claim (defect 2) that
    /// every W row either binds by identity or is refused by name: a row could still fall through
    /// unaccounted for as long as its own value shape, rather than its object identity, was the
    /// problem. It is now refused here too, naming both the offending root and the actual value kind
    /// it carried, matching the exact same discipline as every other condition this method checks.
    /// </remarks>
    /// <param name="offendingValue">
    /// The exact value this method refused on (the raw, non-canonicalizing object term; the canonical
    /// root <paramref name="rootBinding"/> does not contain; or the canonical root paired with the
    /// actual non-literal <c>value_kind</c> it carried), when this method returns
    /// <see langword="false"/>; otherwise <see langword="null"/>.
    /// </param>
    private static bool TryCollectRootWatermarkObservations(
        IReadOnlyList<RepeatedEnumerationRow> wRows,
        RepeatedEnumerationInterpretationProfile wProfile,
        EuPrimaryEnumerationRootBinding rootBinding,
        List<(string WatermarkLexical, string CanonicalEntryKey)> into,
        out string? offendingValue)
    {
        var objectIndex = IndexOf(wProfile, "object");
        var valueIndex = IndexOf(wProfile, "value");
        var valueKindIndex = IndexOf(wProfile, "value_kind");
        foreach (var row in wRows)
        {
            var objectValue = row.Terms[objectIndex].Value;
            var canonicalRoot = objectValue is null ? null : EuPackRootCanonicalForm.TryCanonicalize(objectValue, out _);
            if (canonicalRoot is null)
            {
                offendingValue = objectValue;
                return false;
            }

            if (!rootBinding.Contains(canonicalRoot))
            {
                offendingValue = canonicalRoot;
                return false;
            }

            var valueKind = row.Terms[valueKindIndex].Value;
            if (valueKind != "literal")
            {
                offendingValue = $"{canonicalRoot} (value_kind={valueKind ?? "(unbound)"})";
                return false;
            }

            var watermarkValue = row.Terms[valueIndex].Value;
            if (watermarkValue is null)
            {
                offendingValue = canonicalRoot;
                return false;
            }

            // Newly discovered while wiring up defect 3's own real execution (not one of the six
            // named defects; flagged separately in the fix's own commit and report). This used to be
            // "eu-consolidation-root:" + canonicalRoot, borrowing EuCellarObjectDecode's own
            // SourceObjectRef.CanonicalKey prefix convention for a completely different identity: a
            // witness entry key. EuBoundaryCrossing.TryCross reconciles the first real page's own
            // delivered entry_key values (STR(?entry) over the live Cellar endpoint -- the plain
            // canonical root IRI, never "eu-consolidation-root:"-prefixed) against
            // StartPosition.CanonicalEntryKey as the retained tie set for the very first request. A
            // prefixed key could never appear in a real STR(?entry) result, so the very first real
            // boundary crossing would always refuse with BoundaryEntrySkipped once the witness plan
            // was actually sent over HTTP -- unreachable before defect 3's fix, since nothing executed
            // this plan for real until now. The plain canonical root IRI is what the witness's own
            // entry_key column actually carries for this entity, so that is what the bootstrap must
            // use too.
            into.Add((watermarkValue, canonicalRoot));
        }

        offendingValue = null;
        return true;
    }

    /// <summary>
    /// D1-06c-EU, item 3: "The EU adapter mints a real fetch address for every EU row it produces."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="EuWemiIdentityBoundary"/>'s own <c>CellarOrigins</c> constant already proves every
    /// Cellar WEMI object's (work, expression, manifestation, item) <c>PublisherUri</c> is exactly
    /// <c>{origin}/resource/cellar/{ps-id}</c>. Defect 4's own fix: the ps-id this route needs for
    /// <c>ps-name=cellar</c> is stripped straight out of that origin-prefixed <c>PublisherUri</c>
    /// (see <see cref="TryExtractCellarKey"/>), never read off <see cref="SourceObjectRef.CanonicalKey"/>.
    /// That field is a decode-internal identity for this reduction pipeline's own bookkeeping --
    /// <see cref="EuCellarObjectDecode"/>'s own <c>BuildObjectRef</c> mints it as
    /// <c>"eu-consolidation-root:" + rootIri</c> or <c>"eu-consolidation-state:" + stateIri</c>, the
    /// full IRI with a disambiguating prefix, not the bare Cellar key <see cref="EuWemiIdentityBoundary"/>'s
    /// own convention assumes. Reading it as a ps-id (the original, unfixed shape of this method)
    /// always failed <see cref="EuDocumentFetchAddress.TryCreate"/>'s own ps-id shape check -- the
    /// prefix's embedded <c>:</c> is admitted, but the IRI's own embedded <c>/</c> characters are
    /// not -- so this method minted <c>NotMinted</c> for every real decoded object, silently, with no
    /// failing test to say so until defect 4's own end-to-end acquisition test actually looked at
    /// <see cref="EuQueryExecutionResult.DocumentAcquisitionOutcomesByOrdinal"/> and found it empty.
    /// </para>
    /// <para>
    /// D1-05d replaces this method's own former fixed <c>Accept: application/xhtml+xml</c> with the
    /// object's own ladder: the ordered candidates family M's listing minted
    /// (<see cref="EuFormatDisposition.OrderedCandidates"/>), each turned into its exact Accept
    /// token by <see cref="EuDocumentFetchAddress.TryMediaTypeFor"/>. The first candidate is the one
    /// address the manifest row carries, per RULING
    /// lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262 ("the manifest row keeps one
    /// fetch address, the first candidate, no schema bump"); the rest are the fall-through the
    /// acquisition step attempts within the same run when a listed candidate answers 404. An object
    /// whose disposition carries no candidates at all mints no address: its body axis is not
    /// accepted, so nothing would fetch it anyway.
    /// </para>
    /// <para>
    /// Never throws: a row this route cannot yet address (wrong authority, a <c>PublisherUri</c> not
    /// on either admitted Cellar origin, or a shape <see cref="EuDocumentFetchAddress.TryCreate"/>
    /// refuses) becomes <c>NotMinted</c> rather than failing the whole object's reduction, matching
    /// this loop's own "reduction never throws" discipline for everything else it calls.
    /// </para>
    /// <para>
    /// Defect 4's own fix also returns the real <see cref="EuDocumentFetchAddress"/> alongside its
    /// publisher-neutral manifest projection (null exactly when the projection is <c>NotMinted</c>),
    /// so the caller can actually drive this route's own GET for a Minted row without re-deriving a
    /// typed address from the projection's own plain bounded-string fields, which
    /// <see cref="ScopeManifestFetchAddress"/>'s own remarks say it is deliberately too thin to
    /// support.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The manifest's own canonical order for evidence artifacts: resource id, then digest, both
    /// ordinal. Reproduced here rather than reused because <c>ScopeValidation.CompareArtifact</c> is
    /// internal to Lex.V3.Contracts and this path claim does not extend there; the rule it encodes is
    /// stated in that method and enforced by <c>ScopeManifest</c>, which refuses an unsorted list.
    /// </summary>
    private static int CompareEvidenceArtifact(SourceArtifactRef left, SourceArtifactRef right)
    {
        var comparison = string.CompareOrdinal(left.ResourceId, right.ResourceId);
        return comparison != 0 ? comparison : string.CompareOrdinal(left.Sha256, right.Sha256);
    }

    private static (ScopeManifestFetchAddress Manifest, IReadOnlyList<EuDocumentFetchAddress> Ladder)
        MintFetchAddress(SourceObjectRef objectRef, EuFormatDisposition? formatDisposition)
    {
        var notMinted = (
            ScopeManifestFetchAddress.NotMinted(ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet),
            (IReadOnlyList<EuDocumentFetchAddress>)Array.Empty<EuDocumentFetchAddress>());

        if (objectRef.Authority != SourceAuthority.Cellar ||
            !TryExtractCellarKey(objectRef.PublisherUri, out var cellarKey) ||
            formatDisposition is null || formatDisposition.OrderedCandidates.Count == 0)
        {
            return notMinted;
        }

        var ladder = new List<EuDocumentFetchAddress>(formatDisposition.OrderedCandidates.Count);
        foreach (var candidate in formatDisposition.OrderedCandidates)
        {
            // Unreachable while EuFormatDisposition's own guard keeps every candidate on
            // EuManifestationListingDecode.FormatLadder, which is itself built only from formats
            // TryMediaTypeFor answers for. Kept as a hard stop rather than a silent skip: a rung
            // with no Accept token must never become a fetch this route cannot name.
            if (!EuDocumentFetchAddress.TryMediaTypeFor(candidate, out var mediaType))
            {
                return notMinted;
            }

            var address = EuDocumentFetchAddress.TryCreate(
                "cellar", cellarKey, mediaType, EuDocumentLanguage.Eng, out _);
            if (address is null)
            {
                return notMinted;
            }

            ladder.Add(address);
        }

        return (ladder[0].ToManifestFetchAddress(), ladder);
    }

    /// <summary>
    /// The only two origins a Cellar object may be named by, both schemes the publisher answers on --
    /// the identical pair <see cref="EuWemiIdentityBoundary"/>'s own private <c>CellarOrigins</c>
    /// constant already checks a <c>PublisherUri</c> against, reproduced here (that constant is
    /// private to a different type) so this method can recover the suffix, not merely confirm one
    /// exists.
    /// </summary>
    private static readonly string[] CellarOrigins =
    [
        "http://publications.europa.eu/resource/cellar/",
        "https://publications.europa.eu/resource/cellar/",
    ];

    private static bool TryExtractCellarKey(string publisherUri, out string cellarKey)
    {
        foreach (var origin in CellarOrigins)
        {
            if (publisherUri.StartsWith(origin, StringComparison.Ordinal))
            {
                cellarKey = publisherUri[origin.Length..];
                return cellarKey.Length > 0;
            }
        }

        cellarKey = string.Empty;
        return false;
    }

    /// <summary>
    /// D1-05c-2's own reduction guard (precision four): checked BEFORE calling
    /// <see cref="EuScopeSnapshotReduction.Reduce"/> rather than caught after, per this codebase's own
    /// "guard the shape, don't widen the catch filter" discipline. Mirrors exactly the two conditions
    /// <see cref="EuScopeSnapshotReduction.ReduceRelation"/> throws for: a relation family whose edges
    /// carry more than one authority, and any edge under
    /// <see cref="EuRelationAuthority.OntologyAuthorizedInverse"/> (which that reduction has no
    /// ontology-registry reference to supply). Both are unreachable against D1-05c-1's own decode
    /// today (it retired the inverse authority entirely), so this guard is defense in depth, not a
    /// path this adapter's own tests can drive to true against real decode output.
    /// </summary>
    private static bool TryGuardReducible(
        EuCellarObjectSnapshot snapshot, out EuRelationFamily offendingFamily, out string reason)
    {
        foreach (var observation in snapshot.RelationObservations)
        {
            if (observation.Edges.Count == 0)
            {
                continue;
            }

            var authority = observation.Edges[0].Authority;
            if (observation.Edges.Any(edge => edge.Authority != authority))
            {
                offendingFamily = observation.Family;
                reason = "mixed relation authorities within one family";
                return false;
            }

            if (authority == EuRelationAuthority.OntologyAuthorizedInverse)
            {
                offendingFamily = observation.Family;
                reason = "ontology-authorized-inverse edge with no ontology authority reference to supply";
                return false;
            }
        }

        offendingFamily = default;
        reason = "";
        return true;
    }

    private static int IndexOf(RepeatedEnumerationInterpretationProfile profile, string variableName)
    {
        for (var i = 0; i < profile.ProjectionVariables.Count; i++)
        {
            if (string.Equals(profile.ProjectionVariables[i], variableName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException($"'{variableName}' is not part of this profile's projection.", nameof(variableName));
    }
}
