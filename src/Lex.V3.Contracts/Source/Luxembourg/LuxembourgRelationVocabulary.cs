using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The closed Luxembourg (JOLux) relation-predicate vocabulary. Eighteen, per Decision 65: the LU
/// acquisition scope corrects Candidate 6's seventeen-member enumeration, which omitted
/// <c>jolux:cites</c>, because the V3 work dossier requires Cites and Cited-by and a bounded
/// inventory measured 77,653 asserted citation edges. This ruling changes acquisition scope only;
/// it does not turn an unacquired family into an empty set (Decision 64 remains controlling).
/// </summary>
/// <remarks>
/// <para>
/// The eighteen members and their declaration order are transcribed from the already-merged
/// <see cref="VerifiedLuxembourgSourceProfile"/>'s <c>RelationPredicate</c> vocabulary rows (this
/// file's sibling in the same directory, not edited by this slice), which is itself the exact
/// closed set Decision 65 names: Candidate 6's seventeen plus <c>cites</c>. Six of the eighteen
/// (<see cref="HasIndirectImpact"/> through <see cref="ImpactConsolidatedByExpression"/>) are the
/// <c>jolux:LegalResourceImpact</c> family: Legilux's machine-readable "tableau des modifications"
/// dates, types and links each amendment to the consolidation that absorbed it
/// (review/22-research-relations.md section 4).
/// </para>
/// <para>
/// Tokens are the exact JOLux local predicate name (the full IRI is this local name appended to
/// <c>http://data.legilux.public.lu/resource/ontology/jolux#</c>), matching the convention the
/// merged source profile already uses for its own vocabulary rows.
/// </para>
/// </remarks>
public enum LuxembourgRelationPredicate
{
    [JsonStringEnumMemberName("modifies")]
    Modifies = 1,

    [JsonStringEnumMemberName("repeals")]
    Repeals = 2,

    [JsonStringEnumMemberName("rectifies")]
    Rectifies = 3,

    [JsonStringEnumMemberName("basedOn")]
    BasedOn = 4,

    [JsonStringEnumMemberName("transposes")]
    Transposes = 5,

    [JsonStringEnumMemberName("modifiedTempBy")]
    ModifiedTempBy = 6,

    [JsonStringEnumMemberName("hasIndirectImpact")]
    HasIndirectImpact = 7,

    [JsonStringEnumMemberName("legalAnalysisHasLegalResourceImpact")]
    LegalAnalysisHasLegalResourceImpact = 8,

    [JsonStringEnumMemberName("impactFromLegalResource")]
    ImpactFromLegalResource = 9,

    [JsonStringEnumMemberName("impactToLegalResource")]
    ImpactToLegalResource = 10,

    [JsonStringEnumMemberName("impactToExpression")]
    ImpactToExpression = 11,

    [JsonStringEnumMemberName("legalResourceImpactHasDateEntryInForce")]
    LegalResourceImpactHasDateEntryInForce = 12,

    [JsonStringEnumMemberName("legalResourceImpactHasType")]
    LegalResourceImpactHasType = 13,

    [JsonStringEnumMemberName("impactConsolidatedBy")]
    ImpactConsolidatedBy = 14,

    [JsonStringEnumMemberName("impactConsolidatedByExpression")]
    ImpactConsolidatedByExpression = 15,

    [JsonStringEnumMemberName("basicAct")]
    BasicAct = 16,

    /// <summary>
    /// Decision 58(b): retained with asserted direction and typed semantics. Never amendment
    /// attribution, never part of the boundary-date matching algorithm, and never the source of an
    /// ontology-authorized inverse (<see cref="LuxembourgInvertibleRelationPredicate"/> excludes
    /// it, so no code path can mint a "consolidated_by"-labelled inverse from this predicate).
    /// </summary>
    [JsonStringEnumMemberName("consolidates")]
    Consolidates = 17,

    /// <summary>
    /// Decision 65's correction to Candidate 6: the only member with a pinned ontology-authorized
    /// inverse (<c>cited_by</c>), because the product dossier requires answering citation walks in
    /// both directions.
    /// </summary>
    [JsonStringEnumMemberName("cites")]
    Cites = 18,
}

/// <summary>
/// Whose claim a Luxembourg relation edge is. Mirrors <c>EuRelationAuthority</c>
/// (<c>src/Lex.V3.Contracts/EuScopeDimensions.cs</c>) so the two publishers share one vocabulary
/// shape for the same underlying distinction: a derived inverse and a publisher assertion are
/// different facts about the world, and only one of them can be checked against the publisher.
/// </summary>
public enum LuxembourgRelationAuthority
{
    /// <summary>The publisher asserts this edge in this direction.</summary>
    [JsonStringEnumMemberName("publisher_asserted")]
    PublisherAsserted = 1,

    /// <summary>
    /// The pinned JOLux ontology authorizes this exact inverse. Structurally limited to
    /// <see cref="LuxembourgInvertibleRelationPredicate"/> (Candidate 5 R4 lines 537-554): "An
    /// inverse is derived only when an exact inverse mapping is frozen from the pinned ontology."
    /// </summary>
    [JsonStringEnumMemberName("ontology_authorized_inverse")]
    OntologyAuthorizedInverse = 2,

    /// <summary>
    /// Computed locally from held edges. Permanently unlabelled with any publisher predicate and
    /// excluded from evidence export. R4: "Otherwise V3 may expose a generic locally derived
    /// inbound view that is never labeled with a publisher predicate."
    /// </summary>
    [JsonStringEnumMemberName("local_inbound_view")]
    LocalInboundView = 3,
}

/// <summary>
/// How far acquisition of one Luxembourg relation family has actually got. Decision 64, applied to
/// LU exactly as it already governs EU's <c>EuRelationAcquisitionState</c>: an empty edge list and
/// "we never asked" are indistinguishable to a consumer, so absence is a claim that belongs to one
/// exact family and only a completed bounded observation of that family can support it.
/// </summary>
public enum LuxembourgRelationAcquisitionState
{
    /// <summary>Never asked for. Cannot support any absence claim.</summary>
    [JsonStringEnumMemberName("unacquired")]
    Unacquired = 1,

    /// <summary>Asked for, and the bounded observation did not complete.</summary>
    [JsonStringEnumMemberName("incomplete")]
    Incomplete = 2,

    /// <summary>Observed, and the completion proof did not hold.</summary>
    [JsonStringEnumMemberName("uncertain")]
    Uncertain = 3,

    /// <summary>
    /// A complete bounded observation of this exact family. The only state that can support an
    /// absence claim.
    /// </summary>
    [JsonStringEnumMemberName("complete")]
    Complete = 4,
}

/// <summary>
/// Whether a relation edge's target resource is held in this corpus. REL-001: "held/unheld target
/// state"; REL-004 names the identified-but-unheld shape for a target this corpus cannot resolve to
/// a held resource. Naming this here (rather than leaving an edge's target implicitly "whatever we
/// have") is what the Stage 2 scope ruling means by "unheld targets typed."
/// </summary>
public enum LuxembourgRelationTargetState
{
    /// <summary>The target resource is held in this corpus.</summary>
    [JsonStringEnumMemberName("held")]
    Held = 1,

    /// <summary>
    /// The target is identified by the publisher but not held. The edge is retained; nothing about
    /// it is dropped or guessed.
    /// </summary>
    [JsonStringEnumMemberName("identified_but_unheld")]
    IdentifiedButUnheld = 2,
}

/// <summary>
/// The subset of <see cref="LuxembourgRelationPredicate"/> for which the pinned JOLux ontology
/// authorizes an inverse. Exactly one: <see cref="Cites"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a strict, disjoint subset type, not a filter over <see cref="LuxembourgRelationPredicate"/>.
/// The other seventeen predicates have no member here at all: there is no way to spell "modifies is
/// ontology-invertible" as a value of this type, so no code path that accepts
/// <see cref="LuxembourgInvertibleRelationPredicate"/> can be handed one of the other seventeen. That
/// is the structural fence Candidate 5 R4 asks for ("an unknown predicate must be structurally
/// incapable of producing an inverse, not merely refused at runtime"): the refusal for the other
/// seventeen does not live in an <c>if</c> statement, it lives in this enum simply having no such
/// member to construct.
/// </para>
/// <para>
/// Only <c>cites</c> is pinned today because Decision 65 is the only authority that names a
/// required bidirectional walk ("the V3 work dossier requires Cites and Cited-by"). R4 also says
/// unknown or unpinned predicates "get no invented inverse," so this type does not guess an inverse
/// for <c>modifies</c>, <c>repeals</c>, <c>basedOn</c>, or any other predicate absent a future
/// ruling that pins one; until then those seventeen may only ever carry
/// <see cref="LuxembourgRelationAuthority.PublisherAsserted"/> or
/// <see cref="LuxembourgRelationAuthority.LocalInboundView"/> authority.
/// </para>
/// </remarks>
public enum LuxembourgInvertibleRelationPredicate
{
    [JsonStringEnumMemberName("cites")]
    Cites = 1,
}

/// <summary>
/// The pinned inverse mapping for <see cref="LuxembourgInvertibleRelationPredicate"/>. Both
/// directions of the mapping name every current member explicitly and fail closed (via
/// <see cref="ArgumentOutOfRangeException"/>) for anything else; C# enum switches cannot be made
/// compiler-exhaustive over named members alone, so <c>LuxembourgRelationVocabularyTests</c>'
/// census test is what actually proves every member of both enums is covered, run every time this
/// file changes.
/// </summary>
public static class LuxembourgRelationOntology
{
    /// <summary>
    /// The exact one-transpose inverse label for a pinned predicate (R4: "each derived inverse is
    /// exactly one transpose with a derived_from edge"). Never a publisher predicate name: see
    /// <c>LuxembourgRelationVocabularyTests.InverseLabelsNeverShareAPublisherPredicate</c>.
    /// </summary>
    public static string InverseLabel(LuxembourgInvertibleRelationPredicate predicate) =>
        ContractValidation.RequireDefined(predicate, nameof(predicate)) switch
        {
            LuxembourgInvertibleRelationPredicate.Cites => "cited_by",
            _ => throw new ArgumentOutOfRangeException(
                nameof(predicate),
                predicate,
                "This invertible predicate has no pinned inverse label."),
        };

    /// <summary>The full-vocabulary predicate this pinned inverse is authorized for.</summary>
    public static LuxembourgRelationPredicate UnderlyingPredicate(
        LuxembourgInvertibleRelationPredicate predicate) =>
        ContractValidation.RequireDefined(predicate, nameof(predicate)) switch
        {
            LuxembourgInvertibleRelationPredicate.Cites => LuxembourgRelationPredicate.Cites,
            _ => throw new ArgumentOutOfRangeException(
                nameof(predicate),
                predicate,
                "This invertible predicate has no pinned underlying relation predicate."),
        };
}

/// <summary>
/// One ontology-authorized inverse: the pinned predicate it inverts, its fixed label, and the
/// registry member that authorizes it. Can only be constructed from
/// <see cref="LuxembourgInvertibleRelationPredicate"/>, so the type itself is the fence described
/// on that enum, not merely this record's constructor.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgOntologyAuthorizedInverse
{
    [JsonConstructor]
    public LuxembourgOntologyAuthorizedInverse(
        LuxembourgInvertibleRelationPredicate predicate,
        SourceRegistryMemberRef ontologyAuthorityRef)
    {
        Predicate = ContractValidation.RequireDefined(predicate, nameof(predicate));
        InverseLabel = LuxembourgRelationOntology.InverseLabel(Predicate);
        OntologyAuthorityRef = ontologyAuthorityRef
            ?? throw new ArgumentNullException(nameof(ontologyAuthorityRef));
    }

    public LuxembourgInvertibleRelationPredicate Predicate { get; }

    /// <summary>Computed from <see cref="Predicate"/>, never a caller-supplied string.</summary>
    public string InverseLabel { get; }

    public SourceRegistryMemberRef OntologyAuthorityRef { get; }

    /// <summary>The full-vocabulary predicate this inverse belongs to.</summary>
    public LuxembourgRelationPredicate UnderlyingPredicate =>
        LuxembourgRelationOntology.UnderlyingPredicate(Predicate);
}

/// <summary>
/// One relation family's disposition: whose claim it is, how far acquisition got, and (only for an
/// ontology-authorized inverse) the pinned inverse it names. Mirrors
/// <c>EuRelationFamilyDisposition</c> field for field, including the same deliberate omission: this
/// type records acquisition state and does not decide whether an empty edge list may be read as an
/// absence claim. That decision needs the shared delivery proof plus an independently different
/// witness (Decision 64 and the amendment on issue 343), so only the later LU source-completion
/// validator may mint it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgRelationFamilyDisposition
{
    [JsonConstructor]
    public LuxembourgRelationFamilyDisposition(
        LuxembourgRelationPredicate family,
        LuxembourgRelationAuthority authority,
        LuxembourgRelationAcquisitionState acquisition,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgOntologyAuthorizedInverse? ontologyInverse)
    {
        Family = ContractValidation.RequireDefined(family, nameof(family));
        Authority = ContractValidation.RequireDefined(authority, nameof(authority));
        Acquisition = ContractValidation.RequireDefined(acquisition, nameof(acquisition));

        // Authority first, same ordering EU's disposition uses and for the same reason: with the
        // evidence rules first, an inverse claiming completion with no evidence would be refused
        // for the wrong reason and this guard would never be reached on that input.
        if (authority == LuxembourgRelationAuthority.OntologyAuthorizedInverse)
        {
            if (ontologyInverse is null)
            {
                throw new ArgumentNullException(
                    nameof(ontologyInverse),
                    "An ontology-authorized inverse must name the pinned predicate that authorizes it.");
            }

            if (ontologyInverse.UnderlyingPredicate != Family)
            {
                throw new ArgumentException(
                    $"{ontologyInverse.Predicate} does not authorize an inverse for {Family}; " +
                    "the ontology-authorized inverse must name this exact family's own pinned " +
                    "predicate, not a different one.",
                    nameof(ontologyInverse));
            }

            OntologyInverse = ontologyInverse;
        }
        else if (ontologyInverse is not null)
        {
            throw new ArgumentException(
                "Only an ontology-authorized inverse carries a pinned inverse reference.",
                nameof(ontologyInverse));
        }

        if (authority == LuxembourgRelationAuthority.LocalInboundView &&
            acquisition == LuxembourgRelationAcquisitionState.Complete)
        {
            throw new ArgumentException(
                "A locally computed inbound view cannot be a completed publisher observation.",
                nameof(authority));
        }

        if (acquisition == LuxembourgRelationAcquisitionState.Complete)
        {
            CompletionEvidenceRef = completionEvidenceRef
                ?? throw new ArgumentNullException(
                    nameof(completionEvidenceRef),
                    "A complete acquisition must name the observation that completed it.");
        }
        else if (completionEvidenceRef is not null)
        {
            throw new ArgumentException(
                "Completion evidence belongs only to a complete acquisition.",
                nameof(completionEvidenceRef));
        }
    }

    public LuxembourgRelationPredicate Family { get; }

    public LuxembourgRelationAuthority Authority { get; }

    public LuxembourgRelationAcquisitionState Acquisition { get; }

    /// <summary>The observation that completed this family's acquisition, when one did.</summary>
    public SourceArtifactRef? CompletionEvidenceRef { get; }

    /// <summary>The pinned inverse this disposition claims, when its authority is one.</summary>
    public LuxembourgOntologyAuthorizedInverse? OntologyInverse { get; }
}

/// <summary>The closed Luxembourg relation vocabularies, enumerable rather than hand-counted.</summary>
public static class LuxembourgRelationVocabulary
{
    /// <summary>Every relation predicate. Eighteen.</summary>
    public static IReadOnlyList<LuxembourgRelationPredicate> Predicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgRelationPredicate>());

    /// <summary>Every predicate with a pinned ontology-authorized inverse. One.</summary>
    public static IReadOnlyList<LuxembourgInvertibleRelationPredicate> InvertiblePredicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgInvertibleRelationPredicate>());

    /// <summary>Every relation authority. Three.</summary>
    public static IReadOnlyList<LuxembourgRelationAuthority> Authorities { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgRelationAuthority>());

    /// <summary>Every acquisition state. Four.</summary>
    public static IReadOnlyList<LuxembourgRelationAcquisitionState> AcquisitionStates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgRelationAcquisitionState>());

    /// <summary>Every target-held state. Two.</summary>
    public static IReadOnlyList<LuxembourgRelationTargetState> TargetStates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgRelationTargetState>());
}
