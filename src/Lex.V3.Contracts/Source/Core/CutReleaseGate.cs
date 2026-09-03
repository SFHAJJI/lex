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

/// <summary>
/// R3 lines 400 to 401's closed artifact-kind table, named in the source text's own words. Distinct
/// from any custody-layer artifact kind: this is what a released row IS, not how its bytes are held.
/// </summary>
/// <remarks>
/// Line 400 names one kind for <see cref="ReleaseClass.EnumerationEvidenceOnly"/>: enumeration
/// evidence itself, which "cannot carry a public payload, absence claim, corpus row, index row, or
/// product capability". Line 401 names nine for <see cref="ReleaseClass.AcquisitionOrProduct"/>:
/// "every public corpus, index, body, metadata, relation, gap, absence, withdrawal, or capability
/// release". Ten kinds, closed, and <see cref="CutReleaseGate.DeriveReleaseClass"/> is the one total
/// mapping from a kind to its class.
/// </remarks>
public enum ReleaseArtifactKind
{
    [JsonStringEnumMemberName("enumeration_evidence")]
    EnumerationEvidence = 1,

    [JsonStringEnumMemberName("public_corpus")]
    PublicCorpus = 2,

    [JsonStringEnumMemberName("index")]
    Index = 3,

    [JsonStringEnumMemberName("body")]
    Body = 4,

    [JsonStringEnumMemberName("metadata")]
    Metadata = 5,

    [JsonStringEnumMemberName("relation")]
    Relation = 6,

    [JsonStringEnumMemberName("gap")]
    Gap = 7,

    [JsonStringEnumMemberName("absence")]
    Absence = 8,

    [JsonStringEnumMemberName("withdrawal")]
    Withdrawal = 9,

    [JsonStringEnumMemberName("capability_release")]
    CapabilityRelease = 10,
}

/// <summary>
/// Classifies an open wire key into <see cref="ReleaseArtifactKind"/>. A caller can lie about the
/// key exactly as it could lie about a factory choice; binding a key to what an artifact actually
/// is belongs to whatever later component asserts row identity, not to this classifier. What this
/// closes is different: once a key is given, the release class it produces is never a second choice
/// the caller also makes.
/// </summary>
public static class ReleaseArtifactKindRegistry
{
    private static readonly IReadOnlyDictionary<string, ReleaseArtifactKind> ByWireKey =
        Enum.GetValues<ReleaseArtifactKind>().ToDictionary(
            static kind => WireKeyOf(kind), StringComparer.Ordinal);

    public static ReleaseArtifactKind? Classify(string wireKey) =>
        !string.IsNullOrEmpty(wireKey) && ByWireKey.TryGetValue(wireKey, out var kind) ? kind : null;

    public static string WireKeyOf(ReleaseArtifactKind kind) => kind switch
    {
        ReleaseArtifactKind.EnumerationEvidence => "enumeration_evidence",
        ReleaseArtifactKind.PublicCorpus => "public_corpus",
        ReleaseArtifactKind.Index => "index",
        ReleaseArtifactKind.Body => "body",
        ReleaseArtifactKind.Metadata => "metadata",
        ReleaseArtifactKind.Relation => "relation",
        ReleaseArtifactKind.Gap => "gap",
        ReleaseArtifactKind.Absence => "absence",
        ReleaseArtifactKind.Withdrawal => "withdrawal",
        ReleaseArtifactKind.CapabilityRelease => "capability_release",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>R3 line 398: "It emits exactly cut_release_eligible or cut_release_blocked".</summary>
public enum CutReleaseVerdict
{
    [JsonStringEnumMemberName("cut_release_eligible")]
    CutReleaseEligible = 1,

    [JsonStringEnumMemberName("cut_release_blocked")]
    CutReleaseBlocked = 2,
}

/// <summary>Why a cut was blocked. Closed.</summary>
public enum CutReleaseBlockReason
{
    /// <summary>No block: the verdict is <see cref="CutReleaseVerdict.CutReleaseEligible"/>.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The wire artifact-kind key did not classify under <see cref="ReleaseArtifactKindRegistry"/>,
    /// so no <see cref="ReleaseClass"/> could be derived. R3 line 403's "unknown artifact kind or
    /// release class" named as one reason, because there is no release class to fail against
    /// separately once the kind itself is unrecognized.
    /// </summary>
    [JsonStringEnumMemberName("unknown_artifact_kind_or_release_class")]
    UnknownArtifactKindOrReleaseClass = 9,

    /// <summary>
    /// The enumeration completion claim was null, declared itself incomplete, or named a different
    /// cut than the one being evaluated.
    /// </summary>
    [JsonStringEnumMemberName("enumeration_completion_false_or_missing")]
    EnumerationCompletionFalseOrMissing = 1,

    /// <summary>
    /// For <see cref="ReleaseClass.AcquisitionOrProduct"/> only: the acquisition completion claim
    /// was null, declared itself incomplete, or named a different cut than the one being evaluated.
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
/// A supplied claim that one of R3's two completion states holds for exactly one cut.
/// </summary>
/// <remarks>
/// This contract does not itself verify the six <c>enumeration_complete</c> conditions or the
/// further <c>acquisition_complete</c> conditions R3 lines 344 to 353 state -- discovery census,
/// per-axis equations, witnesses, paging controls, and manifest and policy digest bindings belong
/// to a separate completion contract this task does not build. <see cref="CutReleaseGate"/>
/// therefore treats completion as evidence handed to it, and can refuse only a claim that is
/// missing, that declares itself incomplete, or that names a cut other than the one being
/// evaluated, which is exactly what R3 line 403 and line 401 together name: "a false or missing
/// applicable completion state ... yields cut_release_blocked", for "the same cut" line 401
/// requires. A claim for cut B offered to cut A's evaluation is not evidence for cut A, so it is
/// treated as missing applicable evidence rather than given a reason of its own.
/// </remarks>
public sealed record CutCompletionClaim
{
    public CutCompletionClaim(string cutId, string completionId, bool isComplete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cutId);
        ArgumentException.ThrowIfNullOrWhiteSpace(completionId);
        CutId = cutId;
        CompletionId = completionId;
        IsComplete = isComplete;
    }

    /// <summary>The cut this claim is evidence for. Checked against the cut being evaluated.</summary>
    public string CutId { get; }

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
/// artifact-kind table" and states it "cannot be selected by a caller, producer, or row". Lines 400
/// to 401 state what each class covers, in the source text's own words, and that is the closed
/// derivation by artifact effect: <see cref="ReleaseArtifactKind"/> names the ten kinds and
/// <see cref="DeriveReleaseClass"/> is the one total mapping from a kind to a class. The single
/// entry point, <see cref="TryEvaluate"/>, takes an open wire key, classifies it through
/// <see cref="ReleaseArtifactKindRegistry"/>, and derives the class itself; no caller-visible
/// parameter of type <see cref="Core.ReleaseClass"/> exists anywhere on this type. A caller can lie
/// about which key names its artifact exactly as it could lie about which factory to call; binding
/// a key to what an artifact actually is belongs to whichever component later asserts row identity,
/// not to this classifier.
/// </para>
/// <para>
/// The "unknown artifact kind or release class" reason line 403 names is
/// <see cref="CutReleaseBlockReason.UnknownArtifactKindOrReleaseClass"/>, reached when the wire key
/// does not classify: the gate still returns a verdict, always blocked, with no derivable
/// <see cref="Core.ReleaseClass"/> to report.
/// </para>
/// <para>
/// The completion states are likewise consumed as supplied evidence rather than re-derived: see
/// the remarks on <see cref="CutCompletionClaim"/>. What this gate enforces is R3 line 403's own
/// words about that evidence, not the six or four conditions behind it. Line 401's "for the same
/// cut" is enforced by checking each claim's own <see cref="CutCompletionClaim.CutId"/> against the
/// cut being evaluated; a claim for a different cut is not evidence for this one and is treated as
/// missing under the existing reason, not given a reason of its own.
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
        ReleaseArtifactKind? artifactKind,
        ReleaseClass? releaseClass,
        CutReleaseVerdict verdict,
        CutReleaseBlockReason reason,
        CutCompletionClaim? enumerationCompletion,
        CutCompletionClaim? acquisitionCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector)
    {
        CutId = cutId;
        ArtifactKind = artifactKind;
        ReleaseClass = releaseClass;
        Verdict = verdict;
        Reason = reason;
        EnumerationCompletion = enumerationCompletion;
        AcquisitionCompletion = acquisitionCompletion;
        SuppliedRegistryRef = suppliedRegistryRef;
        SuppliedCountVector = suppliedCountVector;
    }

    public string CutId { get; }

    /// <summary>Null exactly when <see cref="Reason"/> is <see cref="CutReleaseBlockReason.UnknownArtifactKindOrReleaseClass"/>.</summary>
    public ReleaseArtifactKind? ArtifactKind { get; }

    /// <summary>Null exactly when <see cref="Reason"/> is <see cref="CutReleaseBlockReason.UnknownArtifactKindOrReleaseClass"/>.</summary>
    public ReleaseClass? ReleaseClass { get; }

    public CutReleaseVerdict Verdict { get; }

    public CutReleaseBlockReason Reason { get; }

    public CutCompletionClaim? EnumerationCompletion { get; }

    /// <summary>Always null for <see cref="Core.ReleaseClass.EnumerationEvidenceOnly"/>.</summary>
    public CutCompletionClaim? AcquisitionCompletion { get; }

    public SourceArtifactRef SuppliedRegistryRef { get; }

    public IReadOnlyList<GlobalBlockerFamilyCountEntry> SuppliedCountVector { get; }

    /// <summary>
    /// R3 lines 400 to 401's own words as one total mapping. Every <see cref="ReleaseArtifactKind"/>
    /// member is handled by name, not by a default branch, so a new kind added to the enum without a
    /// case here is a compiler error rather than a silent misclassification.
    /// </summary>
    public static ReleaseClass DeriveReleaseClass(ReleaseArtifactKind kind) => kind switch
    {
        ReleaseArtifactKind.EnumerationEvidence => Core.ReleaseClass.EnumerationEvidenceOnly,
        ReleaseArtifactKind.PublicCorpus
            or ReleaseArtifactKind.Index
            or ReleaseArtifactKind.Body
            or ReleaseArtifactKind.Metadata
            or ReleaseArtifactKind.Relation
            or ReleaseArtifactKind.Gap
            or ReleaseArtifactKind.Absence
            or ReleaseArtifactKind.Withdrawal
            or ReleaseArtifactKind.CapabilityRelease => Core.ReleaseClass.AcquisitionOrProduct,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The only entry point. <paramref name="artifactKindWireKey"/> is classified through
    /// <see cref="ReleaseArtifactKindRegistry"/> and its <see cref="Core.ReleaseClass"/> derived from
    /// that classification alone; nothing here takes a release-class parameter a caller could set.
    /// </summary>
    public static CutReleaseGate TryEvaluate(
        string cutId,
        string artifactKindWireKey,
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

        var kind = ReleaseArtifactKindRegistry.Classify(artifactKindWireKey);
        if (kind is null)
        {
            return new CutReleaseGate(
                cutId,
                artifactKind: null,
                releaseClass: null,
                CutReleaseVerdict.CutReleaseBlocked,
                CutReleaseBlockReason.UnknownArtifactKindOrReleaseClass,
                enumerationCompletion,
                acquisitionCompletion,
                suppliedRegistryRef,
                suppliedCountVector);
        }

        var releaseClass = DeriveReleaseClass(kind.Value);
        var reason = DetermineBlockReason(
            cutId,
            releaseClass,
            enumerationCompletion,
            acquisitionCompletion,
            suppliedRegistryRef,
            suppliedCountVector,
            recomputedCountVector);

        return new CutReleaseGate(
            cutId,
            kind,
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
        string cutId,
        ReleaseClass releaseClass,
        CutCompletionClaim? enumerationCompletion,
        CutCompletionClaim? acquisitionCompletion,
        SourceArtifactRef suppliedRegistryRef,
        IReadOnlyList<GlobalBlockerFamilyCountEntry> suppliedCountVector,
        GlobalBlockerCountVector recomputedCountVector)
    {
        // A claim for a different cut is not evidence for this one. Checked alongside null and
        // IsComplete, under the same reason: line 401's "for the same cut" is a condition on
        // whether the claim applies here at all, not a new way completion can be false.
        if (enumerationCompletion is null ||
            !enumerationCompletion.IsComplete ||
            !string.Equals(enumerationCompletion.CutId, cutId, StringComparison.Ordinal))
        {
            return CutReleaseBlockReason.EnumerationCompletionFalseOrMissing;
        }

        if (releaseClass == Core.ReleaseClass.AcquisitionOrProduct &&
            (acquisitionCompletion is null ||
                !acquisitionCompletion.IsComplete ||
                !string.Equals(acquisitionCompletion.CutId, cutId, StringComparison.Ordinal)))
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
