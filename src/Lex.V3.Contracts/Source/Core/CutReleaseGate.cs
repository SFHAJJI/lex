using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>D1-01 Candidate 5 R3 lines 398 to 401: the two release classes <c>cut_release_gate/1</c> evaluates.</summary>
public enum ReleaseClass
{
    [JsonStringEnumMemberName("enumeration_evidence_only")]
    EnumerationEvidenceOnly = 1,

    [JsonStringEnumMemberName("acquisition_or_product")]
    AcquisitionOrProduct = 2,
}

/// <summary>R3 line 398: "It emits exactly cut_release_eligible or cut_release_blocked".</summary>
public enum CutReleaseVerdict
{
    [JsonStringEnumMemberName("cut_release_eligible")]
    CutReleaseEligible = 1,

    [JsonStringEnumMemberName("cut_release_blocked")]
    CutReleaseBlocked = 2,
}

/// <summary>
/// Why a cut was blocked. Closed, and restricted to the reasons this contract can actually reach;
/// see the remarks on <see cref="CutReleaseGate"/> for the "unknown artifact kind or release class"
/// reason R3 line 403 names that this enum deliberately omits, and why.
/// </summary>
public enum CutReleaseBlockReason
{
    /// <summary>No block: the verdict is <see cref="CutReleaseVerdict.CutReleaseEligible"/>.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration completion claim was null or declared itself incomplete.</summary>
    [JsonStringEnumMemberName("enumeration_completion_false_or_missing")]
    EnumerationCompletionFalseOrMissing = 1,

    /// <summary>
    /// For <see cref="ReleaseClass.AcquisitionOrProduct"/> only: the acquisition completion claim
    /// was null or declared itself incomplete.
    /// </summary>
    [JsonStringEnumMemberName("acquisition_completion_false_or_missing")]
    AcquisitionCompletionFalseOrMissing = 2,

    /// <summary>The supplied registry reference does not equal <see cref="GlobalBlockerRegistry.RegistryRef"/>.</summary>
    [JsonStringEnumMemberName("registry_digest_mismatch")]
    RegistryDigestMismatch = 3,

    /// <summary>The supplied count vector omits at least one registered family.</summary>
    [JsonStringEnumMemberName("missing_family")]
    MissingFamily = 4,

    /// <summary>The supplied count vector names the same family more than once.</summary>
    [JsonStringEnumMemberName("duplicate_family")]
    DuplicateFamily = 5,

    /// <summary>A supplied family entry's total does not equal the sum of its own subtype counts.</summary>
    [JsonStringEnumMemberName("evaluation_error")]
    EvaluationError = 6,

    /// <summary>
    /// A supplied family entry disagrees with the independent recomputation from the cut's ledgers,
    /// on either its total or its subtype breakdown.
    /// </summary>
    [JsonStringEnumMemberName("count_ledger_mismatch")]
    CountLedgerMismatch = 7,

    /// <summary>At least one registered family's supplied total is nonzero.</summary>
    [JsonStringEnumMemberName("nonzero_blocker_count")]
    NonzeroBlockerCount = 8,
}

/// <summary>
/// A supplied claim that one of R3's two completion states holds for a cut.
/// </summary>
/// <remarks>
/// This contract does not itself verify the six <c>enumeration_complete</c> conditions or the
/// further <c>acquisition_complete</c> conditions R3 lines 344 to 353 state -- discovery census,
/// per-axis equations, witnesses, paging controls, and manifest and policy digest bindings belong
/// to a separate completion contract this task does not build. <see cref="CutReleaseGate"/>
/// therefore treats completion as evidence handed to it, and can refuse only a claim that is
/// missing or that declares itself incomplete, which is exactly what R3 line 403 itself names: "a
/// false or missing applicable completion state ... yields cut_release_blocked".
/// </remarks>
public sealed record CutCompletionClaim
{
    public CutCompletionClaim(string completionId, bool isComplete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completionId);
        CompletionId = completionId;
        IsComplete = isComplete;
    }

    public string CompletionId { get; }

    public bool IsComplete { get; }
}

/// <summary>
/// One family's entry in a supplied count vector, exactly as a cut's receipt declares it.
/// </summary>
/// <remarks>
/// Unlike <see cref="GlobalBlockerFamilyTally"/>, which <see cref="GlobalBlockerCountVector"/>
/// derives itself and which can never disagree with its own subtype counts, this type is untrusted
/// wire shape: a producer can declare a <see cref="Total"/> that does not equal the sum of its own
/// <see cref="SubtypeCounts"/>. Catching that incoherent declaration is exactly R3 line 403's
/// "evaluation error", so this constructor deliberately does not enforce the sum -- enforcing it
/// here would make that reason unreachable from <see cref="CutReleaseGate"/>.
/// </remarks>
public sealed record GlobalBlockerFamilyCountEntry
{
    public GlobalBlockerFamilyCountEntry(
        GlobalBlockerFamily family, int total, IReadOnlyDictionary<string, int> subtypeCounts)
    {
        Family = SourceCoreValidation.RequireDefined(family, nameof(family));
        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        Total = total;
        ArgumentNullException.ThrowIfNull(subtypeCounts);
        var copy = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, count) in subtypeCounts)
        {
            SourceCoreValidation.RequireMemberKey(key, nameof(subtypeCounts));
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(subtypeCounts));
            }

            copy.Add(key, count);
        }

        SubtypeCounts = new ReadOnlyDictionary<string, int>(copy);
    }

    public GlobalBlockerFamily Family { get; }

    public int Total { get; }

    public IReadOnlyDictionary<string, int> SubtypeCounts { get; }
}

/// <summary>
/// D1-01 Candidate 5 R3 lines 398 to 403: <c>cut_release_gate/1</c>, the final total policy over
/// <c>(cut_id, release_class, enumeration_completion_id, acquisition_completion_id_or_null,
/// cut_global_blocker_registry_digest, complete_global_blocker_count_vector)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>release_class is not a parameter.</b> Line 398 requires it to be "derived from a closed
/// artifact-kind table" and states it "cannot be selected by a caller, producer, or row". The
/// source text names the two release classes themselves in full (lines 400 to 401) but nowhere
/// gives the artifact-kind table that maps a concrete artifact to one of them; no accepted text in
/// this repository defines it, and inventing that table was rejected as a term the accepted text
/// does not itself define. Instead this type structurally removes the selection:
/// <see cref="EvaluateEnumerationEvidenceOnly"/> and <see cref="EvaluateAcquisitionOrProduct"/> are
/// the only two entry points, each hard-codes its own <see cref="ReleaseClass"/>, and neither takes
/// a release-class parameter a caller could set. This is the same technique
/// <c>AbsenceCut.TryCreateComplete</c> and <c>TryCreatePartial</c> already use in this codebase for
/// <c>Completion</c>. Closing the real gap -- deriving which entry point a concrete artifact should
/// reach -- needs the accepted artifact-kind table; until it exists, a caller must already know
/// which release class its artifact is, exactly as it must already know whether its run enumerated
/// completely before calling <c>TryCreateComplete</c>.
/// </para>
/// <para>
/// Consequently the "unknown artifact kind or release class" reason line 403 names cannot be
/// reached and is not a member of <see cref="CutReleaseBlockReason"/>: there is no artifact-kind
/// input to be unknown, and release_class is always exactly one of the two values a method
/// signature already fixed at compile time. A reason with no reachable branch is a defect this
/// codebase treats as worse than an admitted narrowing (a refusal with no driving test is a known
/// failure mode here), so it is left out rather than added unreachable.
/// </para>
/// <para>
/// The completion states are likewise consumed as supplied evidence rather than re-derived: see
/// the remarks on <see cref="CutCompletionClaim"/>. What this gate enforces is R3 line 403's own
/// words about that evidence, not the six or four conditions behind it.
/// </para>
/// <para>
/// The supplied count vector is checked against an independent recomputation
/// (<see cref="GlobalBlockerCountVector"/>) built from the caller's own ledger occurrences, never
/// against a copy of the supplied vector itself: the two values are produced by genuinely different
/// code from genuinely different inputs, so the comparison can fail, and the hostile fixtures make
/// it fail on purpose.
/// </para>
/// <para>
/// <b>What this type does not implement.</b> R3 line 403's last two sentences describe a further,
/// larger total -- "Final release_eligible exists only when the complete declared release row set
/// reconciles to the complete cut row-result set, every included row is payload_release_admissible,
/// no required row was omitted, and this exact cut is cut_release_eligible" -- that consumes this
/// gate's verdict alongside row-local <c>release_policy/1</c> facts (R3 line 394) which no contract
/// in this repository yet computes. That row-set reconciliation is out of scope for this type; it
/// implements exactly the cut-level <c>cut_release_eligible</c> / <c>cut_release_blocked</c> policy.
/// </para>
/// </remarks>
public sealed class CutReleaseGate
{
    private CutReleaseGate(
        string cutId,
        ReleaseClass releaseClass,
        CutReleaseVerdict verdict,
        CutReleaseBlockReason reason,
        CutCompletionClaim? enumerationCompletion,
        CutCompletionClaim? acquisitionCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector)
    {
        CutId = cutId;
        ReleaseClass = releaseClass;
        Verdict = verdict;
        Reason = reason;
        EnumerationCompletion = enumerationCompletion;
        AcquisitionCompletion = acquisitionCompletion;
        SuppliedRegistryRef = suppliedRegistryRef;
        SuppliedCountVector = suppliedCountVector;
    }

    public string CutId { get; }

    public ReleaseClass ReleaseClass { get; }

    public CutReleaseVerdict Verdict { get; }

    public CutReleaseBlockReason Reason { get; }

    public CutCompletionClaim? EnumerationCompletion { get; }

    /// <summary>Always null for <see cref="Core.ReleaseClass.EnumerationEvidenceOnly"/>.</summary>
    public CutCompletionClaim? AcquisitionCompletion { get; }

    public SourceArtifactRef SuppliedRegistryRef { get; }

    public IReadOnlyList<GlobalBlockerFamilyCountEntry> SuppliedCountVector { get; }

    /// <summary>
    /// The only path to an <see cref="Core.ReleaseClass.EnumerationEvidenceOnly"/> verdict. R3 line
    /// 400: this class "requires enumeration_complete and cannot carry a public payload, absence
    /// claim, corpus row, index row, or product capability" -- reflected here by there being no
    /// acquisition-completion parameter for one to ride in on.
    /// </summary>
    public static CutReleaseGate EvaluateEnumerationEvidenceOnly(
        string cutId,
        CutCompletionClaim? enumerationCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector,
        GlobalBlockerCountVector recomputedCountVector) =>
        Evaluate(
            cutId,
            ReleaseClass.EnumerationEvidenceOnly,
            enumerationCompletion,
            acquisitionCompletion: null,
            suppliedRegistryRef,
            suppliedCountVector,
            recomputedCountVector);

    /// <summary>
    /// The only path to an <see cref="Core.ReleaseClass.AcquisitionOrProduct"/> verdict. R3 line
    /// 401: this class "requires both enumeration_complete and acquisition_complete for the same
    /// cut".
    /// </summary>
    public static CutReleaseGate EvaluateAcquisitionOrProduct(
        string cutId,
        CutCompletionClaim? enumerationCompletion,
        CutCompletionClaim? acquisitionCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector,
        GlobalBlockerCountVector recomputedCountVector) =>
        Evaluate(
            cutId,
            ReleaseClass.AcquisitionOrProduct,
            enumerationCompletion,
            acquisitionCompletion,
            suppliedRegistryRef,
            suppliedCountVector,
            recomputedCountVector);

    private static CutReleaseGate Evaluate(
        string cutId,
        ReleaseClass releaseClass,
        CutCompletionClaim? enumerationCompletion,
        CutCompletionClaim? acquisitionCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector,
        GlobalBlockerCountVector recomputedCountVector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cutId);
        ArgumentNullException.ThrowIfNull(suppliedRegistryRef);
        ArgumentNullException.ThrowIfNull(suppliedCountVector);
        ArgumentNullException.ThrowIfNull(recomputedCountVector);
        foreach (var entry in suppliedCountVector)
        {
            ArgumentNullException.ThrowIfNull(entry);
        }

        var reason = DetermineBlockReason(
            releaseClass,
            enumerationCompletion,
            acquisitionCompletion,
            suppliedRegistryRef,
            suppliedCountVector,
            recomputedCountVector);

        return new CutReleaseGate(
            cutId,
            releaseClass,
            reason is null ? CutReleaseVerdict.CutReleaseEligible : CutReleaseVerdict.CutReleaseBlocked,
            reason ?? CutReleaseBlockReason.None,
            enumerationCompletion,
            acquisitionCompletion,
            suppliedRegistryRef,
            suppliedCountVector);
    }

    /// <summary>
    /// Checked in one fixed order, documented here because R3 line 403 lists its reasons in prose
    /// without a precedence table (unlike <c>publication_truth_table/1</c> at line 384, which does).
    /// Shape is checked before value: completion, then the registry identity, then whether the
    /// supplied vector even has the right family shape, then whether each entry is internally
    /// coherent, only then whether it agrees with the independent recomputation, and only last
    /// whether anything is actually nonzero. A vector that fails an earlier check is not meaningful
    /// to compare numerically, so later checks are never reached for it.
    /// </summary>
    private static CutReleaseBlockReason? DetermineBlockReason(
        ReleaseClass releaseClass,
        CutCompletionClaim? enumerationCompletion,
        CutCompletionClaim? acquisitionCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector,
        GlobalBlockerCountVector recomputedCountVector)
    {
        if (enumerationCompletion is null || !enumerationCompletion.IsComplete)
        {
            return CutReleaseBlockReason.EnumerationCompletionFalseOrMissing;
        }

        if (releaseClass == ReleaseClass.AcquisitionOrProduct &&
            (acquisitionCompletion is null || !acquisitionCompletion.IsComplete))
        {
            return CutReleaseBlockReason.AcquisitionCompletionFalseOrMissing;
        }

        if (suppliedRegistryRef != GlobalBlockerRegistry.RegistryRef)
        {
            return CutReleaseBlockReason.RegistryDigestMismatch;
        }

        var seenFamilies = new HashSet<GlobalBlockerFamily>();
        foreach (var entry in suppliedCountVector)
        {
            if (!seenFamilies.Add(entry.Family))
            {
                return CutReleaseBlockReason.DuplicateFamily;
            }
        }

        if (seenFamilies.Count != GlobalBlockerRegistry.Families.Count)
        {
            return CutReleaseBlockReason.MissingFamily;
        }

        foreach (var entry in suppliedCountVector)
        {
            if (entry.Total != entry.SubtypeCounts.Values.Sum())
            {
                return CutReleaseBlockReason.EvaluationError;
            }
        }

        foreach (var entry in suppliedCountVector)
        {
            if (entry.Total != recomputedCountVector.Total(entry.Family) ||
                !SubtypeCountsEqual(entry.SubtypeCounts, recomputedCountVector.SubtypeCounts(entry.Family)))
            {
                return CutReleaseBlockReason.CountLedgerMismatch;
            }
        }

        return suppliedCountVector.Any(static entry => entry.Total != 0)
            ? CutReleaseBlockReason.NonzeroBlockerCount
            : null;
    }

    private static bool SubtypeCountsEqual(
        IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right) =>
        left.Count == right.Count &&
        left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
