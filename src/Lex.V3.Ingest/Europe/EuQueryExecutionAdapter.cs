using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
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

    public EuEnumerationRefusalDetail? ExecutorRefusal { get; }

    public AbsenceFamilyEnumerationProofRefusal? ProofRefusal { get; }

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
    None = 0,

    /// <summary>A requested census-family (D1-05a's own <c>Family</c> set) partition did not prove.</summary>
    CensusFamilyNotProven = 1,

    /// <summary>A requested object-facts family (P, X or W) batch did not prove.</summary>
    ObjectFactsFamilyNotProven = 2,

    /// <summary>
    /// A proven family's delivered rows did not independently re-verify when reopened from custody
    /// through <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>.
    /// </summary>
    FamilyRowsNotVerified = 3,

    /// <summary>
    /// D1-05c-2 precision two: the observed root set is bound to Appendix A's own 82-seed pack by
    /// identity through <see cref="EuPrimaryEnumerationRootBinding.TryBind"/>, which refused.
    /// </summary>
    RootBindingRefused = 4,

    /// <summary>
    /// This run could not resolve a seed's own <see cref="EuActForm"/> from family P's own
    /// <c>resource_legal_type</c> observations for that seed's root. <see cref="EuCellarObjectDecode.TryDecode"/>
    /// requires this as a caller-supplied input it does not itself derive; see this adapter's own
    /// remarks on <see cref="EuQueryExecutionAdapter.TryResolveRecordForm"/> for exactly how it is read
    /// and why a value this reader cannot map refuses rather than guesses.
    /// </summary>
    RecordFormNotResolved = 5,

    /// <summary>
    /// <see cref="EuCellarObjectDecode.TryDecode"/> itself refused one seed's own closure. See
    /// <see cref="EuQueryExecutionResult.DecodeRefusal"/>, <see cref="EuQueryExecutionResult.DecodeOffendingIri"/>
    /// and <see cref="EuQueryExecutionResult.DecodeSnapshotRefusal"/> for the exact reason.
    /// </summary>
    ObjectDecodeRefused = 6,

    [JsonStringEnumMemberName("scope_manifest_not_held")]
    ScopeManifestNotHeld = 7,

    /// <summary>
    /// The written and reopened manifest did not admit as the Union's own through
    /// <see cref="EuScopeManifestBindingProof.TryOpenAsEuManifest"/>.
    /// </summary>
    ManifestBindingRefused = 8,

    /// <summary>D1-05c-2 precision three: no valid first-cut watermark start position could be computed.</summary>
    WatermarkBootstrapRefused = 9,

    /// <summary>The frozen watermark witness plan itself refused.</summary>
    WatermarkPlanRefused = 10,

    /// <summary>
    /// A family-W row named a root that is not a member of this run's own <see cref="EuPrimaryEnumerationRootBinding"/>
    /// (<c>O</c>'s root subset), or a family-W row's own object term could not be canonicalized at
    /// all. Mirrors precision two's identity binding for P and X: a watermark observation that
    /// cannot be tied to a root this run actually discovered is refused naming the offending value,
    /// never silently excluded from the first-cut bootstrap.
    /// </summary>
    RootWatermarkBindingRefused = 11,

    /// <summary>
    /// Defect 3's own witness binding (<see cref="EuFeedRootIntersection.TryBind"/>) refused before
    /// this run could ever reconcile the frozen watermark witness against its own primary
    /// enumeration.
    /// </summary>
    WitnessBindingRefused = 12,

    /// <summary>
    /// <see cref="EuPrimaryEnumerationWitnessReconciliation.TryReconcile"/> itself refused. A real
    /// nonempty termination list from this run's own observed witness traversal
    /// (<see cref="EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync"/>) is never a cause of this
    /// refusal on its own -- see <see cref="EuPrimaryEnumerationWitnessReconciliation.CheckedTerminationCount"/>'s
    /// own remarks -- but a termination naming an in-pack root this run's primary enumeration never
    /// discovered still refuses here, exactly as it would for any other cut.
    /// </summary>
    WitnessReconciliationRefused = 13,

    /// <summary>
    /// <see cref="ScopeReducer.Reduce"/> itself threw. Defect 4: unlike a single object's own
    /// <see cref="EuObjectReductionExclusion"/>, this call reduces every admitted object's inputs
    /// together, so a failure here cannot be attributed to one offending object and is reported as a
    /// whole-run refusal instead.
    /// </summary>
    ScopeReductionRefused = 14,

    /// <summary>
    /// Defect 3's own real-execution fix: <see cref="EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync"/>
    /// itself refused before this run could ever decode a real delivered witness row into a
    /// termination. Replaces the assumed-empty-result shortcut this refusal code did not previously
    /// need to exist for.
    /// </summary>
    WitnessTraversalRefused = 15,
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
        string scopeManifestCanonicalSha256)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(watermarkWitnessPlan);
        ArgumentNullException.ThrowIfNull(rootBinding);
        ArgumentNullException.ThrowIfNull(witnessReconciliation);
        ArgumentNullException.ThrowIfNull(witnessTerminations);
        ArgumentNullException.ThrowIfNull(scopeManifestReceipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeManifestCanonicalSha256);
        var completion = familyOutcomes.All(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven)
            ? EuQueryExecutionCompletion.AllFamiliesProven
            : EuQueryExecutionCompletion.PartialFamilyRefused;
        return new(
            topology, familyOutcomes, observedObjectCount, observedExpressionCount, reductionExclusions,
            watermarkWitnessPlan, rootBinding, witnessReconciliation, witnessTerminations, scopeManifestReceipt,
            scopeManifestCanonicalSha256, completion, null, null, null, null);
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
            topology, familyOutcomes, 0, 0, [], null, null, null, null, null, null, null, refusal,
            decodeRefusal, decodeOffendingIri, decodeSnapshotRefusal);
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

    public EuQueryExecutionRefusalDetail? Refusal { get; }

    public EuCellarObjectDecodeRefusal? DecodeRefusal { get; }

    public string? DecodeOffendingIri { get; }

    public EuCellarObjectSnapshotRefusal? DecodeSnapshotRefusal { get; }
}

/// <summary>
/// D1-05c-2: the EU query-execution adapter. Mints the Union <see cref="SourceProfileTopology"/>,
/// runs and proves every family (D1-05a's own census family <c>S</c>, reused unchanged, plus D1-05c-1's
/// three object-facts families P, X, W), binds the observed object set to the closure proof by
/// identity, decodes through <see cref="EuCellarObjectDecode"/>, reduces through
/// <see cref="EuScopeSnapshotReduction"/> and <see cref="EuScopeProfile.BuildScopeInput"/> into
/// <see cref="ScopeReducer.Reduce"/>, writes and holds the manifest, freezes the first-cut watermark
/// witness, and reconciles it against the primary enumeration through
/// <see cref="EuPrimaryEnumerationWitnessReconciliation"/> (never itself executing a live traversal of
/// the frozen plan; see <see cref="EuQueryExecutionResult.WatermarkWitnessPlan"/>'s own remarks).
/// Follows proposal B's step list, authority the D1-05c synthesis ruling.
/// </summary>
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
    /// <param name="objectFactsFamilies">One D1-05c-1 object-facts family batch (P, X or W) and its bound source witness, per batch this run enumerates. Every seed named in <paramref name="censusFamilies"/> must be covered by at least one P batch, one X batch and one W batch (W covering the roots only) for this run to decode anything.</param>
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
    /// <param name="evidenceResolver">The evidence resolver the scope reduction requires.</param>
    public async Task<EuQueryExecutionResult> RunAsync(
        IReadOnlyList<(EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness)> censusFamilies,
        IReadOnlyList<(EuObjectFactsPartitionRunRequest Request, BoundMachineRequest SourceWitness)> objectFactsFamilies,
        MachineQueryRendererSource witnessRendererSource,
        BoundMachineRequest witnessSourceWitness,
        IScopeReductionEvidenceResolver evidenceResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(censusFamilies);
        ArgumentNullException.ThrowIfNull(objectFactsFamilies);
        ArgumentNullException.ThrowIfNull(witnessRendererSource);
        ArgumentNullException.ThrowIfNull(witnessSourceWitness);
        ArgumentNullException.ThrowIfNull(evidenceResolver);

        var topology = MintTopology();
        var outcomes = new List<EuFamilyEnumerationOutcome>(censusFamilies.Count + objectFactsFamilies.Count);

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

        // ---- Run and prove every object-facts batch (P, X, W). ----
        // Keyed by (Set, familyKey) rather than familyKey alone: EuObjectFactsDiscoveryPlan.PartitionKeyFor
        // is a pure function of the batch's own object set, never of which query set (P, X or W) asked
        // it, so two families sharing one batch of objects (the common case: P, X and W all cover the
        // same discovered closure) mint the IDENTICAL partition key. A dictionary keyed on that key
        // alone would silently collapse three proven families into one.
        var objectFactsByKey = new Dictionary<
            (EuObjectFactsQuerySet Set, string FamilyKey),
            (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationDeliveryReceipt Receipt)>();
        foreach (var (request, sourceWitness) in objectFactsFamilies)
        {
            var runResult = await _executor.RunObjectFactsPartitionAsync(request, sourceWitness, cancellationToken)
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

        if (censusByFamilyKey.Count != censusFamilies.Count)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.CensusFamilyNotProven,
                    "one or more requested census-family seeds did not prove this run's enumeration."));
        }

        if (objectFactsByKey.Count != objectFactsFamilies.Count)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ObjectFactsFamilyNotProven,
                    "one or more requested object-facts family batches did not prove this run's enumeration."));
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
            !objectFactsRows.TryGetValue(EuObjectFactsQuerySet.RootWatermark, out var wFamilies) || wFamilies.Count == 0)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ObjectFactsFamilyNotProven,
                    "this run must enumerate at least one proven batch of each of family P, X and W."));
        }

        var pProfile = pFamilies[0].Profile;
        var xProfile = xFamilies[0].Profile;
        var allPRows = pFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var allXRows = xFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var allWRows = wFamilies.SelectMany(static entry => entry.Rows).ToArray();
        var wProfile = wFamilies[0].Profile;

        // D1-05c-2 precision two: the evidence every observation in every decoded snapshot rests on
        // is family P's own interpretation-profile identity -- a real artifact this run actually
        // acquired, never a fabricated stand-in.
        var evidenceRef = pFamilies[0].Proof.InterpretationProfileRef;

        // ---- Per seed: derive the closure from the census family's own rows, filter P/X to it, decode. ----
        var allSnapshots = new List<EuCellarObjectSnapshot>();
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
        var closuresByCelex = new Dictionary<string, (HashSet<string> Closure, string RootIri)>(StringComparer.Ordinal);
        var allRequestedSeedsClosure = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (requestedCelex, (familyRows, familyProfile)) in censusRowsBySeed)
        {
            var seedClosure = ExtractClosure(familyRows, familyProfile, requestedCelex, out var seedRootIri);
            closuresByCelex[requestedCelex] = (seedClosure, seedRootIri);
            allRequestedSeedsClosure.UnionWith(seedClosure);
        }

        foreach (var (requestedCelex, (familyRows, familyProfile)) in censusRowsBySeed)
        {
            var (closure, rootIri) = closuresByCelex[requestedCelex];
            discoveredRoots.Add(rootIri);

            var seedPRows = FilterByClosureColumn(allPRows, pProfile, "object", closure, allRequestedSeedsClosure);
            var seedXRows = FilterByClosureColumn(allXRows, xProfile, "parent", closure, allRequestedSeedsClosure);

            if (!TryResolveRecordForm(seedPRows, pProfile, rootIri, out var recordForm))
            {
                return EuQueryExecutionResult.Refused(
                    topology, outcomes,
                    new EuQueryExecutionRefusalDetail(
                        EuQueryExecutionRefusal.RecordFormNotResolved,
                        $"seed '{requestedCelex}' (root '{rootIri}') carries no admitted " +
                        "resource_legal_type value this adapter can map to a closed EuActForm."));
            }

            var snapshots = EuCellarObjectDecode.TryDecode(
                requestedCelex,
                familyRows,
                familyProfile,
                seedPRows,
                pProfile,
                seedXRows,
                xProfile,
                recordForm,
                evidenceRef,
                out var decodeRefusal,
                out var offendingIri,
                out var snapshotRefusal);
            if (snapshots is null)
            {
                return EuQueryExecutionResult.Refused(
                    topology, outcomes,
                    new EuQueryExecutionRefusalDetail(
                        EuQueryExecutionRefusal.ObjectDecodeRefused,
                        $"seed '{requestedCelex}' decode refused: {decodeRefusal}."),
                    decodeRefusal,
                    offendingIri,
                    snapshotRefusal);
            }

            allSnapshots.AddRange(snapshots);
            CollectExpressionIris(seedXRows, xProfile, expressionIris);
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
        var orderedEvidenceArtifacts = new[] { evidenceRef };
        var evidenceOrdinals = new Dictionary<SourceArtifactRef, int> { [evidenceRef] = 0 };
        var observedObjects = new List<SourceObjectRef>();
        var reductionInputs = new List<ScopeObjectReductionInput>();
        var exclusions = new List<EuObjectReductionExclusion>();

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
                var fetchAddress = MintFetchAddress(dispositions.ObjectRef);
                var input = EuScopeProfile.BuildScopeInput(
                    scopeProfile, dispositions, evidenceOrdinals, fetchAddress);
                observedObjects.Add(snapshot.ObjectRef);
                reductionInputs.Add(input);
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

        var writeReceipt = await _custodyStore.CreateAsync(manifestBytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);
        if (CustodyMembershipClassifier.Classify(writeReceipt) != CustodyMembership.Floored)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ScopeManifestNotHeld,
                    "The scope manifest was written but the store enforced no retention floor on it."));
        }

        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, writeReceipt.Reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);
        var manifestArtifactRef = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", manifestCanonicalSha256);
        var reopenedManifest = EuScopeManifestBindingProof.TryOpenAsEuManifest(
            manifestArtifactRef, reopened.Span, evidenceResolver, out var bindingRefusal);
        if (reopenedManifest is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ManifestBindingRefused, bindingRefusal.ToString()));
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

        var witnessPlan = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            startPosition,
            out var watermarkPlanRefusal);
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
                witnessPlan, witnessRendererSource, witnessSourceWitness, cancellationToken)
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

        var feedWitness = EuFeedRootIntersection.TryBind(
            EuConsolidationDiscoveryPlan.Create().ArtifactRef,
            witnessClosureMatrixRef,
            witnessIdentityPredicateBindingRef,
            rootBinding.DiscoveredRoots,
            Array.Empty<EuFeedFamilyProjection>(),
            out var feedWitnessRefusal);
        if (feedWitness is null)
        {
            return EuQueryExecutionResult.Refused(
                topology, outcomes,
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WitnessBindingRefused, feedWitnessRefusal.ToString()));
        }

        // Every delivered entry, decoded honestly: no identity resolver exists in this codebase yet
        // (EuFeedEntryObservation's own remarks -- "writing a resolver here would be inventing that
        // answer"), so every real row this run observed is constructed with
        // identityResolutionClosed: false and empty resolved roots/projections, which Classify then
        // correctly and honestly terminates as EuFeedTerminal.UnresolvedOrAmbiguous /
        // IdentityResolutionDidNotClose. This is a real, already-modeled outcome, not a shortcut.
        var terminations = new List<EuFeedEntryTermination>(traversal.Entries.Count);
        foreach (var entry in traversal.Entries.CanonicalEntries)
        {
            var observation = EuFeedEntryObservation.TryObserve(
                entry,
                identityResolutionClosed: false,
                Array.Empty<string>(),
                Array.Empty<EuFeedFamilyProjection>(),
                out var observationRefusal)
                ?? throw new InvalidOperationException(
                    $"unreachable: an honestly-unresolved witness observation cannot itself be refused ({observationRefusal}).");
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

        return EuQueryExecutionResult.Delivered(
            topology,
            outcomes,
            observedObjectCount: allSnapshots.Count,
            observedExpressionCount: expressionIris.Count,
            reductionExclusions: exclusions,
            watermarkWitnessPlan: witnessPlan,
            rootBinding: rootBinding,
            witnessReconciliation: witnessReconciliation,
            witnessTerminations: terminations,
            scopeManifestReceipt: writeReceipt,
            scopeManifestCanonicalSha256: manifestCanonicalSha256);
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
    /// D1-05c-1's own decode contract requires a caller-resolved <see cref="EuActForm"/> as an input
    /// it does not itself derive ("Not recoverable from these closures' rows; the caller supplies it
    /// from wherever it independently resolves resource_legal_type" -- <see cref="EuCellarObjectDecode.TryDecode"/>'s
    /// own doc comment). This reads it from the SAME family P rows this run already acquired for the
    /// seed's own root: every <c>resource_legal_type</c> value P observed for the root, read by its
    /// last IRI path segment against the EU Publications Office resource-type authority table's own
    /// short-code convention (the same convention <see cref="EuCellarObjectDecode"/>'s own
    /// <c>CONSOLID_ACT</c> marker and <see cref="EuScopeProfile.RecordFormToken"/>'s wire tokens both
    /// already use). This is not a new Contracts-layer mapping invented for this slice: it is reading
    /// a value already present in already-acquired publisher data, never a new query or a guess. A
    /// root whose every observed <c>resource_legal_type</c> value fails to map refuses this seed's
    /// decode rather than defaulting to any one closed member.
    /// </summary>
    private static bool TryResolveRecordForm(
        IReadOnlyList<RepeatedEnumerationRow> pRows,
        RepeatedEnumerationInterpretationProfile pProfile,
        string rootIri,
        out EuActForm recordForm)
    {
        var objectIndex = IndexOf(pProfile, "object");
        var predicateIndex = IndexOf(pProfile, "predicate");
        var valueIndex = IndexOf(pProfile, "value");
        var valueKindIndex = IndexOf(pProfile, "value_kind");
        // EuObjectFactsDiscoveryPlan.CdmIri is internal to Lex.V3.Contracts (this path claim does not
        // extend there); this is the exact IRI its own switch produces for ResourceLegalType
        // ("cdm:resource_legal_type", the fixed CDM namespace every EU predicate constant in this
        // repository already uses), stated here as a plain literal rather than reached reflectively.
        const string resourceLegalTypeIri = "http://publications.europa.eu/ontology/cdm#resource_legal_type";

        foreach (var row in pRows)
        {
            var objectValue = row.Terms[objectIndex].Value;
            if (objectValue is null || EuPackRootCanonicalForm.TryCanonicalize(objectValue, out _) != rootIri)
            {
                continue;
            }

            if (row.Terms[predicateIndex].Value != resourceLegalTypeIri ||
                row.Terms[valueKindIndex].Value != "iri")
            {
                continue;
            }

            var value = row.Terms[valueIndex].Value;
            if (value is not null && TryMapResourceTypeCode(value, out recordForm))
            {
                return true;
            }
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
    /// <c>{origin}/resource/cellar/{CanonicalKey}</c>, so the object's own already-validated
    /// <see cref="SourceObjectRef.CanonicalKey"/> IS the <c>ps-id</c> this route needs for
    /// <c>ps-name=cellar</c> -- no further parsing of the identity IRI is needed or attempted.
    /// </para>
    /// <para>
    /// The manifestation media type and language are fixed to the one combination
    /// <c>review/23-research-temporal.md</c> section 1.2 PROVES reaches a real 200 with actual
    /// content (<c>Accept: application/xhtml+xml</c>, <c>Accept-Language: en</c>). Choosing a
    /// different manifestation per object is a later slice's policy decision, not this one's.
    /// </para>
    /// <para>
    /// Never throws: a row this route cannot yet address (wrong authority, or a shape
    /// <see cref="EuDocumentFetchAddress.TryCreate"/> refuses) becomes <c>NotMinted</c> rather than
    /// failing the whole object's reduction, matching this loop's own "reduction never throws"
    /// discipline for everything else it calls.
    /// </para>
    /// </remarks>
    private static ScopeManifestFetchAddress MintFetchAddress(SourceObjectRef objectRef)
    {
        if (objectRef.Authority != SourceAuthority.Cellar)
        {
            return ScopeManifestFetchAddress.NotMinted(
                ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet);
        }

        var address = EuDocumentFetchAddress.TryCreate(
            "cellar",
            objectRef.CanonicalKey,
            EuManifestationMediaType.XhtmlXml,
            EuDocumentLanguage.Eng,
            out _);
        return address is null
            ? ScopeManifestFetchAddress.NotMinted(ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet)
            : address.ToManifestFetchAddress();
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
