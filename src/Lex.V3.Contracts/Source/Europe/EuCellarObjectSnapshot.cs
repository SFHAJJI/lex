using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Whether a closed CDM predicate was asked for on one object, and if so, what the publisher said.
/// </summary>
/// <remarks>
/// The distinction this ruling names explicitly: "not observed" and "observed absent" are different
/// facts. <see cref="NotObserved"/> means this cut's closure query never asked this predicate about
/// this object (a coverage gap, not a publisher fact). <see cref="ObservedAbsent"/> means it asked
/// and the publisher supplied no value (a real, complete, negative observation). Collapsing the two
/// is exactly the false-absence shape Decision 64 exists to prevent, generalized here from relation
/// families to every closed predicate this pipeline reads.
/// </remarks>
public enum EuPredicateObservationState
{
    /// <summary>This cut never asked this predicate about this object.</summary>
    NotObserved = 1,

    /// <summary>Asked, and the publisher supplied one or more values.</summary>
    ObservedPresent = 2,

    /// <summary>Asked, the observation completed, and the publisher supplied nothing.</summary>
    ObservedAbsent = 3,
}

/// <summary>
/// One of the thirteen non-relation <see cref="EuCdmPredicate"/> observations for one object,
/// restricted to that closed vocabulary. Relation predicates live in
/// <see cref="EuRelationFamilyObservation"/> instead, mirroring <see cref="EuScopeDimensions"/>'s own
/// separation of the two closed sets.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuPredicateObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuPredicateObservation(
        EuCdmPredicate predicate,
        EuPredicateObservationState state,
        IReadOnlyList<string> values,
        SourceArtifactRef evidenceRef)
    {
        Predicate = ContractValidation.RequireDefined(predicate, nameof(predicate));
        State = ContractValidation.RequireDefined(state, nameof(state));
        var snapshot = (values ?? throw new ArgumentNullException(nameof(values))).ToArray();

        if (state == EuPredicateObservationState.ObservedPresent)
        {
            if (snapshot.Length == 0)
            {
                throw new ArgumentException(
                    "An observed-present predicate must carry at least one value.", nameof(values));
            }

            if (Array.Exists(snapshot, string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("A predicate value cannot be blank.", nameof(values));
            }
        }
        else if (snapshot.Length != 0)
        {
            throw new ArgumentException(
                $"{state} carries no values; only ObservedPresent does.", nameof(values));
        }

        Values = Array.AsReadOnly(snapshot);
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuCdmPredicate Predicate { get; }

    public EuPredicateObservationState State { get; }

    public IReadOnlyList<string> Values { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// One observed relation edge: which family, whose claim it is, and the target -- reduced to
/// Appendix A's exact lexical form even when the target is outside the 82-root pack.
/// </summary>
/// <remarks>
/// A relation target is deliberately never checked against <see cref="EuAppendixASeedMap.PackRoots"/>.
/// R7 is explicit that "accepted edges at the final frontier remain attributed edges to
/// identified-but-unheld targets": a citation to a Work outside the pack is a real, in-scope fact
/// about the pack member that carries it, and refusing it here would erase relation shape the ruling
/// requires to survive. What is still required, everywhere a root-shaped string enters this
/// contract, is Appendix A's exact lexical form (the ruling's "at every point D1-05 itself resolves
/// or normalizes a root" -- the target is resolved here even though it is never bound here).
/// </remarks>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuRelationEdgeObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuRelationEdgeObservation(
        EuRelationFamily family,
        EuRelationAuthority authority,
        string targetWorkRoot,
        SourceArtifactRef evidenceRef)
    {
        Family = ContractValidation.RequireDefined(family, nameof(family));
        Authority = ContractValidation.RequireDefined(authority, nameof(authority));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWorkRoot);
        var canonical = EuPackRootCanonicalForm.TryCanonicalize(targetWorkRoot, out var canonicalRefusal);
        TargetWorkRoot = canonical ?? throw new ArgumentException(
            $"The relation target could not be reduced to Appendix A's exact lexical form " +
            $"({canonicalRefusal}). A frontier target is still required to be a well formed root, " +
            "even when it is outside the pack (R7).",
            nameof(targetWorkRoot));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuRelationFamily Family { get; }

    public EuRelationAuthority Authority { get; }

    public string TargetWorkRoot { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// One relation family's raw acquisition observation: how far this cut's bounded observation of
/// this exact family got, and the edges it found. Decision 64's typed acquisition state, at the
/// observation layer rather than the disposition layer -- <see cref="EuRelationFamilyDisposition"/>
/// is what a reduction builds from this, never what this itself is.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuRelationFamilyObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuRelationFamilyObservation(
        EuRelationFamily family,
        EuRelationAcquisitionState acquisition,
        IReadOnlyList<EuRelationEdgeObservation> edges,
        SourceArtifactRef? completionEvidenceRef)
    {
        Family = ContractValidation.RequireDefined(family, nameof(family));
        Acquisition = ContractValidation.RequireDefined(acquisition, nameof(acquisition));
        var snapshot = (edges ?? throw new ArgumentNullException(nameof(edges))).ToArray();
        if (Array.Exists(snapshot, static edge => edge is null))
        {
            throw new ArgumentException("An edge cannot be null.", nameof(edges));
        }

        if (Array.Exists(snapshot, edge => edge.Family != Family))
        {
            throw new ArgumentException(
                "Every edge must name this observation's own family.", nameof(edges));
        }

        // Never asked means nothing can have been found. A bare empty array standing in for
        // "not acquired" is exactly what Decision 64 forbids; requiring Unacquired to carry zero
        // edges (rather than merely permitting it) keeps the two states from becoming
        // indistinguishable at the edges list alone.
        if (acquisition == EuRelationAcquisitionState.Unacquired && snapshot.Length > 0)
        {
            throw new ArgumentException(
                "An unacquired family cannot carry edges; nothing was asked for, so nothing can " +
                "have been found.",
                nameof(edges));
        }

        if (acquisition == EuRelationAcquisitionState.Complete)
        {
            CompletionEvidenceRef = completionEvidenceRef ?? throw new ArgumentNullException(
                nameof(completionEvidenceRef),
                "A complete acquisition must name the observation that completed it.");
        }
        else if (completionEvidenceRef is not null)
        {
            throw new ArgumentException(
                "Completion evidence belongs only to a complete acquisition.",
                nameof(completionEvidenceRef));
        }

        Edges = Array.AsReadOnly(snapshot);
    }

    public EuRelationFamily Family { get; }

    public EuRelationAcquisitionState Acquisition { get; }

    public IReadOnlyList<EuRelationEdgeObservation> Edges { get; }

    /// <summary>The observation that completed this family's acquisition, when one did.</summary>
    public SourceArtifactRef? CompletionEvidenceRef { get; }
}

/// <summary>
/// Which of R1's language-expression facts this object's one language of interest carries.
/// </summary>
/// <remarks>
/// Folds R1's "publisher expression absent" state and <see cref="EuLanguageBodyState"/> into one
/// three-way fact, because the queue's own addition names exactly this: "D1-05's snapshot carries
/// ExpressionObserved" as the fix for treating a missing Expression the same as one whose body this
/// scope does not hold. <see cref="NotObserved"/> is R1's <c>publisher_expression_absent</c>
/// (nothing to report a body policy about at all); the other two are an observed Expression whose
/// body either is, or is not, held.
/// </remarks>
public enum EuExpressionObservationState
{
    /// <summary>No Expression assertion was found for this language at all.</summary>
    NotObserved = 1,

    /// <summary>An Expression was observed and this scope holds its body.</summary>
    ExpressionObservedBodyHeld = 2,

    /// <summary>An Expression was observed and this scope does not hold its body.</summary>
    ExpressionObservedBodyNotHeld = 3,
}

/// <summary>One object's channel-acquisition observation: which route actually served it.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuChannelObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuChannelObservation(
        EuChannel channel, string reasonCode, string ruleId, SourceArtifactRef evidenceRef)
    {
        Channel = ContractValidation.RequireDefined(channel, nameof(channel));
        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        RuleId = ContractValidation.RequireIdentifier(ruleId, nameof(ruleId));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuChannel Channel { get; }

    public string ReasonCode { get; }

    public string RuleId { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// One object's language-expression observation: which language, and which of R1's three facts
/// (<see cref="EuExpressionObservationState"/>) it carries.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuLanguageExpressionObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuLanguageExpressionObservation(
        EuOfficialLanguage language,
        EuExpressionObservationState state,
        string reasonCode,
        string ruleId,
        SourceArtifactRef evidenceRef)
    {
        Language = ContractValidation.RequireDefined(language, nameof(language));
        State = ContractValidation.RequireDefined(state, nameof(state));
        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        RuleId = ContractValidation.RequireIdentifier(ruleId, nameof(ruleId));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuOfficialLanguage Language { get; }

    public EuExpressionObservationState State { get; }

    public string ReasonCode { get; }

    public string RuleId { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>
/// One object's observed manifestation format, and whether this scope actually fetches that
/// format's body.
/// </summary>
/// <remarks>
/// Unlike channel (<see cref="EuChannelDisposition.PolicyFor"/>) and rights
/// (<see cref="EuRightsDisposition.BasisFor"/>), format admission has no fixed function from format
/// identity alone: <see cref="EuFormatDisposition"/>'s own constructor takes
/// <see cref="EuFormatBodyAdmission"/> as given rather than deriving it, because which formats this
/// scope actually fetches as body text is the reviewed <see cref="EuManifestationScope"/> profile's
/// own answer, not a fact this contract could compute from the format enum by itself. So this
/// observation carries the admission the reviewed profile assigned, exactly as observed, rather than
/// inventing a second derivation that could silently disagree with the profile's real one.
/// </remarks>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuFormatObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuFormatObservation(
        EuManifestationFormat format,
        EuFormatBodyAdmission admission,
        string reasonCode,
        SourceArtifactRef evidenceRef)
    {
        Format = ContractValidation.RequireDefined(format, nameof(format));
        Admission = ContractValidation.RequireDefined(admission, nameof(admission));
        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuManifestationFormat Format { get; }

    public EuFormatBodyAdmission Admission { get; }

    public string ReasonCode { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>One object's observed content class, for the rights axis.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record EuContentClassObservation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public EuContentClassObservation(EuContentClass contentClass, SourceArtifactRef evidenceRef)
    {
        ContentClass = ContractValidation.RequireDefined(contentClass, nameof(contentClass));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuContentClass ContentClass { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>Why a Cellar object snapshot was refused. Closed.</summary>
public enum EuCellarObjectSnapshotRefusal
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>
    /// The object's Work root could not be reduced to Appendix A's exact lexical form. Every root
    /// D1-05 itself resolves is canonicalized before anything downstream compares against it.
    /// </summary>
    WorkRootNotCanonical = 1,

    /// <summary>
    /// The canonical Work root is not a member of <see cref="EuAppendixASeedMap.PackRoots"/>. Point
    /// 9 of the ruling: a query result naming a Work outside Appendix A's 82-root pack is refused,
    /// never silently accepted as an ordinary object.
    /// </summary>
    WorkRootOutsideAppendixAPack = 2,

    /// <summary>A predicate from the closed 13-member vocabulary carries no observation.</summary>
    PredicateObservationMissing = 3,

    /// <summary>A predicate from the closed vocabulary carries more than one observation.</summary>
    PredicateObservationRepeated = 4,

    /// <summary>
    /// A relation family from <see cref="EuScopeVocabulary.ReadRelationFamilies"/> carries no
    /// observation.
    /// </summary>
    RelationFamilyObservationMissing = 5,

    /// <summary>A relation family carries more than one observation.</summary>
    RelationFamilyObservationRepeated = 6,
}

/// <summary>
/// One admitted Cellar object's raw observed assertions and relations, restricted to the closed EU
/// predicate and relation-family vocabulary, for exactly one cut.
/// </summary>
/// <remarks>
/// <para>
/// The ruling's first deliverable: "the per object RDF snapshot type is D1-05's own first
/// deliverable (not an external blocker) -- build a type capturing one admitted Cellar object's raw
/// observed assertions and relations, restricted to the closed EU predicate/relation-family
/// vocabulary, with an explicit distinction between 'not observed' and 'observed absent'." This type
/// is that snapshot. It carries facts, never policy: <see cref="EuScopeSnapshotReduction"/> is the
/// pure function that turns a snapshot into <see cref="EuScopeObjectDispositions"/>, and this type
/// never constructs a disposition itself.
/// </para>
/// <para>
/// Channel, format and rights are not among the thirteen <see cref="EuCdmPredicate"/> members --
/// channel is which route this run used to acquire the object, not a publisher assertion; format is
/// a Manifestation-authority fact outside the closed predicate list; rights is read from the EUR-Lex
/// legal notice, an external policy document, not a per-object Cellar triple. Folding them into
/// <see cref="PredicateObservations"/> would assert an RDF observation this repository has not taken,
/// so each is its own explicit, separately evidenced field instead.
/// </para>
/// </remarks>
public sealed class EuCellarObjectSnapshot
{
    private readonly IReadOnlyDictionary<EuCdmPredicate, EuPredicateObservation> _predicateIndex;
    private readonly IReadOnlyDictionary<EuRelationFamily, EuRelationFamilyObservation> _relationIndex;

    private EuCellarObjectSnapshot(
        SourceObjectRef objectRef,
        string canonicalWorkRoot,
        EuActForm recordForm,
        SourceArtifactRef recordEvidenceRef,
        IReadOnlyList<EuPredicateObservation> predicateObservations,
        IReadOnlyDictionary<EuCdmPredicate, EuPredicateObservation> predicateIndex,
        EuChannelObservation channel,
        EuLanguageExpressionObservation? language,
        EuFormatObservation? format,
        EuContentClassObservation? rights,
        IReadOnlyList<EuRelationFamilyObservation> relationObservations,
        IReadOnlyDictionary<EuRelationFamily, EuRelationFamilyObservation> relationIndex,
        SourceArtifactRef relationAxisEvidenceRef,
        EuContentClassObservation? supporting,
        SourceArtifactRef supportingEvidenceRef)
    {
        ObjectRef = objectRef;
        CanonicalWorkRoot = canonicalWorkRoot;
        RecordForm = recordForm;
        RecordEvidenceRef = recordEvidenceRef;
        PredicateObservations = predicateObservations;
        _predicateIndex = predicateIndex;
        Channel = channel;
        Language = language;
        Format = format;
        Rights = rights;
        RelationObservations = relationObservations;
        _relationIndex = relationIndex;
        RelationAxisEvidenceRef = relationAxisEvidenceRef;
        Supporting = supporting;
        SupportingEvidenceRef = supportingEvidenceRef;
    }

    /// <summary>The Cellar object this snapshot observed.</summary>
    public SourceObjectRef ObjectRef { get; }

    /// <summary>
    /// This object's own Work root, already reduced to Appendix A's exact lexical form and already
    /// checked against <see cref="EuAppendixASeedMap.PackRoots"/> membership.
    /// </summary>
    public string CanonicalWorkRoot { get; }

    /// <summary>The act form this object's Work carries.</summary>
    public EuActForm RecordForm { get; }

    /// <summary>The observation the record form was read from.</summary>
    public SourceArtifactRef RecordEvidenceRef { get; }

    /// <summary>Every one of the thirteen closed predicate observations, exactly once each.</summary>
    public IReadOnlyList<EuPredicateObservation> PredicateObservations { get; }

    /// <summary>This object's channel-acquisition observation.</summary>
    public EuChannelObservation Channel { get; }

    /// <summary>
    /// This object's one language of interest, or null when no language-body evaluation applies to
    /// this object (for example, a Work-level object with no single Expression under evaluation).
    /// </summary>
    public EuLanguageExpressionObservation? Language { get; }

    /// <summary>This object's observed manifestation format, or null when none has been observed yet.</summary>
    public EuFormatObservation? Format { get; }

    /// <summary>This object's observed content class for the rights axis, or null when none applies.</summary>
    public EuContentClassObservation? Rights { get; }

    /// <summary>
    /// Every relation family this pipeline reads (<see cref="EuScopeVocabulary.ReadRelationFamilies"/>),
    /// exactly once each.
    /// </summary>
    public IReadOnlyList<EuRelationFamilyObservation> RelationObservations { get; }

    /// <summary>
    /// The evidence that this object's relation axis was evaluated at all, distinct from any single
    /// family's own completion evidence.
    /// </summary>
    public SourceArtifactRef RelationAxisEvidenceRef { get; }

    /// <summary>This object's supporting-document content class, or null when it carries none.</summary>
    public EuContentClassObservation? Supporting { get; }

    /// <summary>The evidence for the supporting-document axis, required unconditionally.</summary>
    public SourceArtifactRef SupportingEvidenceRef { get; }

    /// <summary>The observation for one of the thirteen closed predicates.</summary>
    public EuPredicateObservation Predicate(EuCdmPredicate predicate) => _predicateIndex[predicate];

    /// <summary>The observation for one of the read relation families.</summary>
    public EuRelationFamilyObservation Relation(EuRelationFamily family) => _relationIndex[family];

    /// <summary>The only path that mints a snapshot.</summary>
    public static EuCellarObjectSnapshot? TryObserve(
        SourceObjectRef objectRef,
        string resolvedWorkRoot,
        EuActForm recordForm,
        SourceArtifactRef recordEvidenceRef,
        IReadOnlyList<EuPredicateObservation> predicateObservations,
        EuChannelObservation channel,
        EuLanguageExpressionObservation? language,
        EuFormatObservation? format,
        EuContentClassObservation? rights,
        IReadOnlyList<EuRelationFamilyObservation> relationObservations,
        SourceArtifactRef relationAxisEvidenceRef,
        EuContentClassObservation? supporting,
        SourceArtifactRef supportingEvidenceRef,
        out EuCellarObjectSnapshotRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(objectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedWorkRoot);
        ContractValidation.RequireDefined(recordForm, nameof(recordForm));
        ArgumentNullException.ThrowIfNull(recordEvidenceRef);
        ArgumentNullException.ThrowIfNull(predicateObservations);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(relationObservations);
        ArgumentNullException.ThrowIfNull(relationAxisEvidenceRef);
        ArgumentNullException.ThrowIfNull(supportingEvidenceRef);

        var canonicalRoot = EuPackRootCanonicalForm.TryCanonicalize(resolvedWorkRoot, out _);
        if (canonicalRoot is null)
        {
            refusal = EuCellarObjectSnapshotRefusal.WorkRootNotCanonical;
            return null;
        }

        if (!EuAppendixASeedMap.PackRoots.Contains(canonicalRoot))
        {
            refusal = EuCellarObjectSnapshotRefusal.WorkRootOutsideAppendixAPack;
            return null;
        }

        var predicateIndex = new Dictionary<EuCdmPredicate, EuPredicateObservation>();
        foreach (var observation in predicateObservations)
        {
            if (observation is null)
            {
                throw new ArgumentException(
                    "A predicate observation cannot be null.", nameof(predicateObservations));
            }

            if (!predicateIndex.TryAdd(observation.Predicate, observation))
            {
                refusal = EuCellarObjectSnapshotRefusal.PredicateObservationRepeated;
                return null;
            }
        }

        foreach (var predicate in EuScopeVocabulary.CdmPredicates)
        {
            if (!predicateIndex.ContainsKey(predicate))
            {
                refusal = EuCellarObjectSnapshotRefusal.PredicateObservationMissing;
                return null;
            }
        }

        var relationIndex = new Dictionary<EuRelationFamily, EuRelationFamilyObservation>();
        foreach (var observation in relationObservations)
        {
            if (observation is null)
            {
                throw new ArgumentException(
                    "A relation family observation cannot be null.", nameof(relationObservations));
            }

            if (!relationIndex.TryAdd(observation.Family, observation))
            {
                refusal = EuCellarObjectSnapshotRefusal.RelationFamilyObservationRepeated;
                return null;
            }
        }

        foreach (var family in EuScopeVocabulary.ReadRelationFamilies)
        {
            if (!relationIndex.ContainsKey(family))
            {
                refusal = EuCellarObjectSnapshotRefusal.RelationFamilyObservationMissing;
                return null;
            }
        }

        refusal = EuCellarObjectSnapshotRefusal.None;
        return new EuCellarObjectSnapshot(
            objectRef,
            canonicalRoot,
            recordForm,
            recordEvidenceRef,
            Array.AsReadOnly(predicateObservations.ToArray()),
            predicateIndex,
            channel,
            language,
            format,
            rights,
            Array.AsReadOnly(relationObservations.ToArray()),
            relationIndex,
            relationAxisEvidenceRef,
            supporting,
            supportingEvidenceRef);
    }
}
