using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgVocabularyKind
{
    ResourceClass = 1,
    TypeDocument = 2,
    UserFormat = 3,
    RelationPredicate = 4,
    Language = 5,
    PublicationFamily = 6,
    LegalValue = 7,
    Licence = 8,
    Rights = 9,
    RightsHolder = 10,
    Publisher = 11,
    ForceStatus = 12,
    AssertionPredicate = 13,
}

public enum LuxembourgAssertionObjectKind
{
    Iri = 1,
    Literal = 2,
}

public enum LuxembourgRelationSemantic
{
    AssertedRelation = 1,
    AssertedCitation = 2,
    ConsolidatesShapeRequired = 3,
}

public enum LuxembourgRelationDisposition
{
    Accepted = 1,
    TypedQuarantine = 2,
}

public enum LuxembourgSelectorCardinality
{
    Missing = 1,
    Single = 2,
    Multiple = 3,
}

public enum LuxembourgConsolidatesDirection
{
    AssertedSubjectToObject = 1,
}

public enum LuxembourgConsolidatesShapeState
{
    AcceptedTcToCompatibleAct = 1,
    TypedQuarantineSubjectClassMissing = 2,
    TypedQuarantineSubjectClassIncompatible = 3,
    TypedQuarantineSubjectTypeMissing = 4,
    TypedQuarantineSubjectTypeMultiple = 5,
    TypedQuarantineSubjectTypeNotTc = 6,
    TypedQuarantineTargetResourceMissing = 7,
    TypedQuarantineTargetClassMissing = 8,
    TypedQuarantineTargetClassIncompatible = 9,
    TypedQuarantineTargetTypeMissing = 10,
    TypedQuarantineTargetTypeMultiple = 11,
    TypedQuarantineTargetRoleIncompatible = 12,
    TypedQuarantineTargetTypeUnruled = 13,
}

public enum LuxembourgAssertionDisposition
{
    Accepted = 1,
    TypedQuarantine = 2,
}

/// <summary>
/// Why a whole Luxembourg run refused. Every member names the condition that produces it, so a
/// reader can check the claim against the code rather than infer it from the name.
/// </summary>
/// <remarks>
/// Residue R1, RULING lex-event-20260905T044206627Z-43bd39db4edb474c834cb2acd1e1e1ff. Two of these
/// members were constructed by no production path while the conditions they name escaped as an
/// untyped <see cref="ArgumentException"/>, so this vocabulary advertised coverage it did not have.
/// Naming each producer in its own summary is what makes that failure visible next time: a code
/// whose summary names its real producer can be checked by a reader, and a code whose summary does
/// not cannot.
/// </remarks>
public enum LuxembourgProfileResolutionFailureCode
{
    /// <summary>
    /// An observation whose object is not a Luxembourg resource IRI under the Jolux authority.
    /// Produced by <c>LuxembourgScopeResolver.ValidateObservation</c>.
    /// </summary>
    [JsonStringEnumMemberName("profile_resolution_failed_invalid_publisher_iri")]
    InvalidPublisherIri = 1,

    /// <summary>
    /// The snapshot omits a settled exact rule value the profile requires, so the vocabulary is not
    /// complete enough to resolve anything. Produced by
    /// <c>VerifiedLuxembourgSourceProfile.TryOpen</c>.
    /// </summary>
    [JsonStringEnumMemberName("profile_resolution_failed_incomplete_vocabulary")]
    IncompleteVocabulary = 2,

    /// <summary>
    /// An observed predicate or object IRI that is not the settled value, which is drift in the
    /// publisher vocabulary rather than a duplicate of a settled one. Produced by
    /// <c>LuxembourgScopeResolver.ValidateObservation</c>.
    /// </summary>
    [JsonStringEnumMemberName("profile_resolution_failed_unknown_vocabulary_drift")]
    UnknownVocabularyDrift = 3,

    /// <summary>
    /// The snapshot presents an EXACT DUPLICATE ROW: the same kind and IRI twice in the IRI
    /// vocabulary, or a byte-identical row twice in the literal vocabulary. Produced by
    /// <c>VerifiedLuxembourgSourceProfile.TryOpen</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This summary said "one selector has two answers", which claimed more than the check makes
    /// true, so it is narrowed to what is detected. Literal equality is WHOLE RECORD, so two
    /// literal rows sharing a kind and a canonical selector value but differing in their raw
    /// lexical bytes are NOT detected. That is correct rather than a gap, and the evidence is that
    /// <c>CanonicalSelectorLexicalValue</c> is consumed by nothing but the profile digest: no
    /// selector reads it, so two such rows are two distinct publisher observations that both reach
    /// the digest, not two answers competing for one position. If a selector ever reads the
    /// canonical value they become two answers and this check has to widen with it.
    /// </para>
    /// That mapping is a READING of the code, recorded as one so the next reader can check it.
    /// Duplicate rows were the only input-driven condition left without a home once the neighbours
    /// were excluded: the static selector table cannot collide, since its keys and projection rules
    /// are compile-time arrays; a per-object selector conflict already resolves as
    /// <c>typed_quarantine_selector_conflict</c> at object level rather than refusing a run; and the
    /// identity mismatch in <c>ReduceScope</c> is a different condition. The reviewer confirmed it
    /// from the other side, by elimination over this enum rather than from the throw sites:
    /// <see cref="UnknownVocabularyDrift"/> means a value that is not the settled one, not a
    /// duplicate of it, so the duplicate condition has no other home here. Two independent readings
    /// agreeing is strong evidence and not proof.
    /// </remarks>
    [JsonStringEnumMemberName("profile_resolution_failed_selector_conflict")]
    SelectorConflict = 4,

    /// <summary>
    /// Evidence that does not bind: an observation naming a different run, a duplicate publisher
    /// URI, duplicate assertions or relations, or a term that is not an exact IRI. Produced by
    /// <c>LuxembourgScopeResolver.Resolve</c> and its <c>ValidateObservation</c>.
    /// </summary>
    [JsonStringEnumMemberName("profile_resolution_failed_evidence_binding_rejected")]
    EvidenceBindingRejected = 5,
}

public enum LuxembourgDimension
{
    Record = 1,
    Body = 2,
    Relation = 3,
    SupportingDocument = 4,
    PublicationFamily = 5,
    Language = 6,
    Format = 7,
    Authenticity = 8,
    Rights = 9,
    Transport = 10,
}

/// <summary>
/// R5.1's own role for a TC, RECT or ACC object, distinguished from bare
/// <c>PriorityCandidateTypes</c> bucket membership (item 15 of the D1-04 design-synthesis ruling;
/// reviewer SCOPE_RULING lex-event-20260903T234803274Z-54f15ecf651941ebb58c91e269959aed). ACC's own
/// member was corrected by the reviewer RULING
/// lex-event-20260904T002301246Z-7699c8fdd1ad4868a7d94dcb152fbf57 after this lane's first freeze
/// read the 23:48Z SCOPE_RULING too strictly and gated every ACC resource to an unconditional
/// refusal; see <see cref="ConstitutionalReviewDecision"/>.
/// </summary>
public enum LuxembourgTypedRoleKind
{
    /// <summary>The object is not a TC, RECT or ACC Act, or does not qualify as an Act at all.</summary>
    NotApplicable = 1,

    /// <summary>
    /// R5.1 rule 4: accepted only as <c>coordinated_text_act</c>, carrying its own coordinate and
    /// the consolidation-without-legal-effect disclosure, never relabeled as its base act.
    /// </summary>
    CoordinatedText = 2,

    /// <summary>
    /// R5.1 rule 5: accepted only as <c>corrigendum_act</c>, carrying its own coordinate and the
    /// corrective-material disclosure, never relabeled as the corrected act or statutory
    /// replacement text.
    /// </summary>
    Corrigendum = 3,

    /// <summary>
    /// R5.1 rule 6, as corrected by the reviewer RULING
    /// lex-event-20260904T002301246Z-7699c8fdd1ad4868a7d94dcb152fbf57: accepted only as
    /// <c>constitutional_review_decision</c> when the publisher's own typeDocument assertion
    /// carries the exact ACC resource type IRI. That assertion is the one evidence R5.1 designates
    /// (Candidate 5 R5.1, line 599 and line 608); no further predicate is required and none may
    /// substitute -- a title, a relation or an alternate format never widens this exact semantic
    /// carve-out (line 617, Decision 58). Carries its own coordinate and the
    /// interpretation-source-never-statutory-text disclosure, exactly like
    /// <see cref="CoordinatedText"/> and <see cref="Corrigendum"/>: never treated as statutory
    /// text, and its judgment date never enters the legislation timeline.
    /// </summary>
    ConstitutionalReviewDecision = 4,
}

/// <summary>Closed disclosure codes for <see cref="LuxembourgTypedRoleResolution"/>.</summary>
public static class LuxembourgTypedRoleDisclosures
{
    /// <summary>R5.1 rule 4: a TC body is consolidation-without-legal-effect, never its base act.</summary>
    public const string ConsolidationWithoutLegalEffect =
        "disclosure_consolidation_without_legal_effect";

    /// <summary>R5.1 rule 5: a RECT body is corrective material, never the corrected act.</summary>
    public const string CorrectiveMaterialNeverCorrectedAct =
        "disclosure_corrective_material_never_corrected_act";

    /// <summary>
    /// R5.1 rule 6: an ACC body is <c>constitutional_review_decision</c>, a separately typed
    /// interpretation source that never becomes statutory text; its judgment date never enters the
    /// legislation timeline.
    /// </summary>
    public const string ConstitutionalReviewDecisionNeverStatutoryText =
        "disclosure_constitutional_review_decision_never_statutory_text";
}

/// <summary>
/// One resource's R5.1 typed role, carried on the resolver's own disposition
/// (<see cref="LuxembourgResourceResolution"/>) rather than folded into the coarser
/// <see cref="LuxembourgDimension.PublicationFamily"/> bucket, and never reaching the scope
/// manifest wire schema directly (a later, separately ruled schema change would be required for
/// that).
/// </summary>
public sealed record LuxembourgTypedRoleResolution
{
    private LuxembourgTypedRoleResolution(
        LuxembourgTypedRoleKind kind,
        string? ownCoordinate,
        string? disclosureCode)
    {
        Kind = LuxembourgSourceValidation.RequireDefined(kind, nameof(kind));
        switch (kind)
        {
            case LuxembourgTypedRoleKind.NotApplicable:
                if (ownCoordinate is not null || disclosureCode is not null)
                {
                    throw new ArgumentException(
                        "A not-applicable role carries no coordinate or disclosure.");
                }

                break;
            case LuxembourgTypedRoleKind.CoordinatedText:
            case LuxembourgTypedRoleKind.Corrigendum:
            case LuxembourgTypedRoleKind.ConstitutionalReviewDecision:
                if (string.IsNullOrEmpty(ownCoordinate) || string.IsNullOrEmpty(disclosureCode))
                {
                    throw new ArgumentException(
                        "An accepted TC, RECT or ACC role must carry its own coordinate and disclosure.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        OwnCoordinate = ownCoordinate;
        DisclosureCode = disclosureCode;
    }

    /// <summary>The closed role this resource resolved to.</summary>
    public LuxembourgTypedRoleKind Kind { get; }

    /// <summary>
    /// The resource's own publisher IRI, present only when <see cref="Kind"/> is not
    /// <see cref="LuxembourgTypedRoleKind.NotApplicable"/>, so a TC, RECT or ACC role can never be
    /// read off some other resource's coordinate instead of its own.
    /// </summary>
    public string? OwnCoordinate { get; }

    /// <summary>
    /// The R5.1 disclosure required alongside an accepted
    /// <see cref="LuxembourgTypedRoleKind.CoordinatedText"/>,
    /// <see cref="LuxembourgTypedRoleKind.Corrigendum"/> or
    /// <see cref="LuxembourgTypedRoleKind.ConstitutionalReviewDecision"/> role.
    /// </summary>
    public string? DisclosureCode { get; }

    internal static readonly LuxembourgTypedRoleResolution NotApplicableInstance =
        new(LuxembourgTypedRoleKind.NotApplicable, null, null);

    internal static LuxembourgTypedRoleResolution AcceptedCoordinatedText(string ownCoordinate) =>
        new(
            LuxembourgTypedRoleKind.CoordinatedText,
            ownCoordinate,
            LuxembourgTypedRoleDisclosures.ConsolidationWithoutLegalEffect);

    internal static LuxembourgTypedRoleResolution AcceptedCorrigendum(string ownCoordinate) =>
        new(
            LuxembourgTypedRoleKind.Corrigendum,
            ownCoordinate,
            LuxembourgTypedRoleDisclosures.CorrectiveMaterialNeverCorrectedAct);

    internal static LuxembourgTypedRoleResolution AcceptedConstitutionalReviewDecision(
        string ownCoordinate) =>
        new(
            LuxembourgTypedRoleKind.ConstitutionalReviewDecision,
            ownCoordinate,
            LuxembourgTypedRoleDisclosures.ConstitutionalReviewDecisionNeverStatutoryText);
}

public sealed record LuxembourgIriVocabularyValue
{
    public LuxembourgIriVocabularyValue(LuxembourgVocabularyKind kind, string fullIri)
    {
        Kind = LuxembourgSourceValidation.RequireDefined(kind, nameof(kind));
        FullIri = LuxembourgSourceValidation.RequireExactAbsoluteIri(fullIri, nameof(fullIri));
    }

    public LuxembourgVocabularyKind Kind { get; }

    public string FullIri { get; }
}

public sealed record LuxembourgLiteralVocabularyValue
{
    public LuxembourgLiteralVocabularyValue(
        LuxembourgVocabularyKind kind,
        string rawDatatypeIriOrEmpty,
        string rawLanguageTagOrEmpty,
        string rawLexicalValue)
    {
        Kind = LuxembourgSourceValidation.RequireDefined(kind, nameof(kind));
        RawDatatypeIriOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            rawDatatypeIriOrEmpty,
            nameof(rawDatatypeIriOrEmpty));
        RawLanguageTagOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            rawLanguageTagOrEmpty,
            nameof(rawLanguageTagOrEmpty));
        RawLexicalValue = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            rawLexicalValue,
            nameof(rawLexicalValue));

        var canonicalization = LuxembourgLiteralCanonicalizer.Canonicalize(
            RawLexicalValue,
            RawDatatypeIriOrEmpty,
            RawLanguageTagOrEmpty);
        DatatypeIri = canonicalization.DatatypeIri;
        LanguageTag = canonicalization.LanguageTag;
        CanonicalSelectorLexicalValue = canonicalization.CanonicalSelectorLexicalValue;
        Disposition = canonicalization.Disposition;
        Reason = canonicalization.Reason;
        ReasonCode = canonicalization.ReasonCode;
    }

    public LuxembourgVocabularyKind Kind { get; }

    public string RawDatatypeIriOrEmpty { get; }

    public string RawLanguageTagOrEmpty { get; }

    public string RawLexicalValue { get; }

    public string DatatypeIri { get; }

    public string LanguageTag { get; }

    public string? CanonicalSelectorLexicalValue { get; }

    public LuxembourgLiteralDisposition Disposition { get; }

    public LuxembourgLiteralReason Reason { get; }

    public string ReasonCode { get; }
}

public sealed record LuxembourgVocabularySnapshot
{
    public LuxembourgVocabularySnapshot(
        SourceArtifactRef observationRef,
        SourceArtifactRef completeEnumerationRef,
        IReadOnlyList<LuxembourgIriVocabularyValue> iriValues,
        IReadOnlyList<LuxembourgLiteralVocabularyValue> literalValues)
    {
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
        CompleteEnumerationRef = completeEnumerationRef
            ?? throw new ArgumentNullException(nameof(completeEnumerationRef));
        IriValues = LuxembourgSourceValidation.Copy(iriValues, nameof(iriValues));
        LiteralValues = LuxembourgSourceValidation.Copy(literalValues, nameof(literalValues));
    }

    public SourceArtifactRef ObservationRef { get; }

    public SourceArtifactRef CompleteEnumerationRef { get; }

    public IReadOnlyList<LuxembourgIriVocabularyValue> IriValues { get; }

    public IReadOnlyList<LuxembourgLiteralVocabularyValue> LiteralValues { get; }
}

public sealed record LuxembourgObservedAssertion
{
    public LuxembourgObservedAssertion(
        string subjectIri,
        string predicateIri,
        LuxembourgAssertionObjectKind objectKind,
        string objectIriOrLexical,
        string datatypeIriOrEmpty,
        string languageTagOrEmpty,
        SourceArtifactRef observationRef)
    {
        SubjectIri = LuxembourgSourceValidation.RequireScalarString(subjectIri, nameof(subjectIri));
        PredicateIri = LuxembourgSourceValidation.RequireScalarString(
            predicateIri,
            nameof(predicateIri));
        ObjectKind = LuxembourgSourceValidation.RequireDefined(objectKind, nameof(objectKind));
        ObjectIriOrLexical = LuxembourgSourceValidation.RequireScalarString(
            objectIriOrLexical,
            nameof(objectIriOrLexical));
        DatatypeIriOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            datatypeIriOrEmpty,
            nameof(datatypeIriOrEmpty));
        LanguageTagOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            languageTagOrEmpty,
            nameof(languageTagOrEmpty));
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
    }

    public string SubjectIri { get; }

    public string PredicateIri { get; }

    public LuxembourgAssertionObjectKind ObjectKind { get; }

    public string ObjectIriOrLexical { get; }

    public string DatatypeIriOrEmpty { get; }

    public string LanguageTagOrEmpty { get; }

    public SourceArtifactRef ObservationRef { get; }
}

public sealed record LuxembourgObservedRelation
{
    public LuxembourgObservedRelation(
        string subjectIri,
        string predicateIri,
        string objectIri,
        SourceArtifactRef observationRef)
    {
        SubjectIri = LuxembourgSourceValidation.RequireScalarString(subjectIri, nameof(subjectIri));
        PredicateIri = LuxembourgSourceValidation.RequireScalarString(
            predicateIri,
            nameof(predicateIri));
        ObjectIri = LuxembourgSourceValidation.RequireScalarString(objectIri, nameof(objectIri));
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
    }

    public string SubjectIri { get; }

    public string PredicateIri { get; }

    public string ObjectIri { get; }

    public SourceArtifactRef ObservationRef { get; }
}

public sealed record LuxembourgResourceObservation
{
    public LuxembourgResourceObservation(
        SourceObjectRef objectRef,
        SourceArtifactRef observationRef,
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        IReadOnlyList<LuxembourgObservedRelation> relations,
        LuxembourgSparqlRightsChannelObservations sparqlRightsObservations,
        LuxembourgInFileRightsChannelObservations inFileRightsObservations)
    {
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
        Assertions = LuxembourgSourceValidation.Copy(assertions, nameof(assertions));
        Relations = LuxembourgSourceValidation.Copy(relations, nameof(relations));
        SparqlRightsObservations = sparqlRightsObservations
            ?? throw new ArgumentNullException(nameof(sparqlRightsObservations));
        InFileRightsObservations = inFileRightsObservations
            ?? throw new ArgumentNullException(nameof(inFileRightsObservations));
    }

    public SourceObjectRef ObjectRef { get; }

    public SourceArtifactRef ObservationRef { get; }

    public IReadOnlyList<LuxembourgObservedAssertion> Assertions { get; }

    public IReadOnlyList<LuxembourgObservedRelation> Relations { get; }

    public LuxembourgSparqlRightsChannelObservations SparqlRightsObservations { get; }

    public LuxembourgInFileRightsChannelObservations InFileRightsObservations { get; }
}

public sealed record LuxembourgRelationRule
{
    internal LuxembourgRelationRule(
        string predicateIri,
        LuxembourgRelationSemantic semantic)
    {
        PredicateIri = LuxembourgSourceValidation.RequireExactAbsoluteIri(
            predicateIri,
            nameof(predicateIri));
        Semantic = LuxembourgSourceValidation.RequireDefined(semantic, nameof(semantic));
    }

    public string PredicateIri { get; }

    public LuxembourgRelationSemantic Semantic { get; }
}

public sealed record LuxembourgConsolidatesShape
{
    internal LuxembourgConsolidatesShape(
        IReadOnlyList<string> subjectClasses,
        IReadOnlyList<string> subjectTypeDocuments,
        LuxembourgSelectorCardinality subjectTypeCardinality,
        IReadOnlyList<string> targetClasses,
        IReadOnlyList<string> targetTypeDocuments,
        LuxembourgSelectorCardinality targetTypeCardinality,
        LuxembourgConsolidatesDirection direction,
        LuxembourgConsolidatesShapeState state)
    {
        SubjectClasses = LuxembourgSourceValidation.CopyStrings(
            subjectClasses,
            nameof(subjectClasses));
        SubjectTypeDocuments = LuxembourgSourceValidation.CopyStrings(
            subjectTypeDocuments,
            nameof(subjectTypeDocuments));
        SubjectTypeCardinality = LuxembourgSourceValidation.RequireDefined(
            subjectTypeCardinality,
            nameof(subjectTypeCardinality));
        TargetClasses = LuxembourgSourceValidation.CopyStrings(
            targetClasses,
            nameof(targetClasses));
        TargetTypeDocuments = LuxembourgSourceValidation.CopyStrings(
            targetTypeDocuments,
            nameof(targetTypeDocuments));
        TargetTypeCardinality = LuxembourgSourceValidation.RequireDefined(
            targetTypeCardinality,
            nameof(targetTypeCardinality));
        Direction = LuxembourgSourceValidation.RequireDefined(direction, nameof(direction));
        State = LuxembourgSourceValidation.RequireDefined(state, nameof(state));

        if (SubjectTypeCardinality != Cardinality(SubjectTypeDocuments.Count) ||
            TargetTypeCardinality != Cardinality(TargetTypeDocuments.Count))
        {
            throw new ArgumentException(
                "Selector cardinality must describe the retained exact value set.");
        }

        if (State == LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct &&
            (!SubjectClasses.SequenceEqual(
                 [VerifiedLuxembourgSourceProfile.JoluxPrefix + "Act"]) ||
             !TargetClasses.SequenceEqual(
                 [VerifiedLuxembourgSourceProfile.JoluxPrefix + "Act"]) ||
             !SubjectTypeDocuments.SequenceEqual(
                 [VerifiedLuxembourgSourceProfile.TypeDocumentPrefix +
                  VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc]) ||
             TargetTypeDocuments.Count != 1 ||
             SubjectTypeCardinality != LuxembourgSelectorCardinality.Single ||
             TargetTypeCardinality != LuxembourgSelectorCardinality.Single ||
             Direction != LuxembourgConsolidatesDirection.AssertedSubjectToObject))
        {
            throw new ArgumentException(
                "An accepted consolidates shape requires exact Act roles, one TC source type, " +
                "one target type, and asserted subject-to-object direction.",
                nameof(state));
        }
    }

    public IReadOnlyList<string> SubjectClasses { get; }

    public IReadOnlyList<string> SubjectTypeDocuments { get; }

    public LuxembourgSelectorCardinality SubjectTypeCardinality { get; }

    public IReadOnlyList<string> TargetClasses { get; }

    public IReadOnlyList<string> TargetTypeDocuments { get; }

    public LuxembourgSelectorCardinality TargetTypeCardinality { get; }

    public LuxembourgConsolidatesDirection Direction { get; }

    public LuxembourgConsolidatesShapeState State { get; }

    private static LuxembourgSelectorCardinality Cardinality(int count) => count switch
    {
        0 => LuxembourgSelectorCardinality.Missing,
        1 => LuxembourgSelectorCardinality.Single,
        _ => LuxembourgSelectorCardinality.Multiple,
    };
}

public sealed record LuxembourgResolvedRelation
{
    internal LuxembourgResolvedRelation(
        string subjectIri,
        string predicateIri,
        string objectIri,
        SourceArtifactRef observationRef,
        LuxembourgRelationSemantic semantic,
        LuxembourgRelationDisposition disposition,
        LuxembourgConsolidatesShape? consolidatesShape)
    {
        SubjectIri = LuxembourgSourceValidation.RequireExactResourceIri(
            subjectIri,
            nameof(subjectIri));
        PredicateIri = LuxembourgSourceValidation.RequireExactAbsoluteIri(
            predicateIri,
            nameof(predicateIri));
        ObjectIri = LuxembourgSourceValidation.RequireExactAbsoluteIri(objectIri, nameof(objectIri));
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
        Semantic = LuxembourgSourceValidation.RequireDefined(semantic, nameof(semantic));
        Disposition = LuxembourgSourceValidation.RequireDefined(disposition, nameof(disposition));
        ConsolidatesShape = consolidatesShape;

        var isConsolidates = Semantic == LuxembourgRelationSemantic.ConsolidatesShapeRequired;
        if (isConsolidates != (ConsolidatesShape is not null) ||
            (ConsolidatesShape is not null &&
             (Disposition == LuxembourgRelationDisposition.Accepted) !=
             (ConsolidatesShape.State ==
              LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct)))
        {
            throw new ArgumentException(
                "A consolidates relation must carry one matching closed shape disposition.");
        }
    }

    public string SubjectIri { get; }

    public string PredicateIri { get; }

    public string ObjectIri { get; }

    public SourceArtifactRef ObservationRef { get; }

    public LuxembourgRelationSemantic Semantic { get; }

    public LuxembourgRelationDisposition Disposition { get; }

    public LuxembourgConsolidatesShape? ConsolidatesShape { get; }
}

public sealed record LuxembourgResolvedAssertion
{
    internal LuxembourgResolvedAssertion(
        LuxembourgObservedAssertion assertion,
        LuxembourgAssertionDisposition disposition)
    {
        Assertion = assertion ?? throw new ArgumentNullException(nameof(assertion));
        Disposition = LuxembourgSourceValidation.RequireDefined(disposition, nameof(disposition));
    }

    public LuxembourgObservedAssertion Assertion { get; }

    public LuxembourgAssertionDisposition Disposition { get; }
}

public sealed record LuxembourgDimensionAccounting
{
    internal LuxembourgDimensionAccounting(
        LuxembourgDimension dimension,
        LuScopeTerminalState state,
        IReadOnlyList<int> resourceOrdinals)
    {
        Dimension = LuxembourgSourceValidation.RequireDefined(dimension, nameof(dimension));
        State = LuxembourgSourceValidation.RequireDefined(state, nameof(state));
        ResourceOrdinals = Array.AsReadOnly(
            (resourceOrdinals ?? throw new ArgumentNullException(nameof(resourceOrdinals))).ToArray());
        for (var index = 0; index < ResourceOrdinals.Count; index++)
        {
            if (ResourceOrdinals[index] < 0 ||
                (index > 0 && ResourceOrdinals[index - 1] >= ResourceOrdinals[index]))
            {
                throw new ArgumentException(
                    "Resource ordinals must be nonnegative, sorted, and unique.",
                    nameof(resourceOrdinals));
            }
        }
    }

    public LuxembourgDimension Dimension { get; }

    public LuScopeTerminalState State { get; }

    public IReadOnlyList<int> ResourceOrdinals { get; }
}

public sealed record LuxembourgResourceResolution
{
    internal LuxembourgResourceResolution(
        SourceObjectRef objectRef,
        LuScopeDimensions dimensions,
        IReadOnlyList<LuxembourgResolvedAssertion> assertions,
        IReadOnlyList<LuxembourgResolvedRelation> relations,
        LuxembourgWemiTopologyResolution wemiTopology,
        LuxembourgBodyJoinResolution bodyJoin,
        LuxembourgTypedRoleResolution typedRole)
    {
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        Dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        Assertions = LuxembourgSourceValidation.Copy(assertions, nameof(assertions));
        Relations = LuxembourgSourceValidation.Copy(relations, nameof(relations));
        WemiTopology = wemiTopology ?? throw new ArgumentNullException(nameof(wemiTopology));
        BodyJoin = bodyJoin ?? throw new ArgumentNullException(nameof(bodyJoin));
        TypedRole = typedRole ?? throw new ArgumentNullException(nameof(typedRole));
    }

    public SourceObjectRef ObjectRef { get; }

    public LuScopeDimensions Dimensions { get; }

    public IReadOnlyList<LuxembourgResolvedAssertion> Assertions { get; }

    public IReadOnlyList<LuxembourgResolvedRelation> Relations { get; }

    public LuxembourgWemiTopologyResolution WemiTopology { get; }

    public LuxembourgBodyJoinResolution BodyJoin { get; }

    /// <summary>R5.1's own TC, RECT or ACC role for this resource. See <see cref="LuxembourgTypedRoleResolution"/>.</summary>
    public LuxembourgTypedRoleResolution TypedRole { get; }
}

public sealed record LuxembourgProfileResolutionFailure
{
    internal LuxembourgProfileResolutionFailure(
        LuxembourgProfileResolutionFailureCode code,
        string subject)
    {
        Code = LuxembourgSourceValidation.RequireDefined(code, nameof(code));
        Subject = LuxembourgSourceValidation.RequireScalarString(subject, nameof(subject));
    }

    public LuxembourgProfileResolutionFailureCode Code { get; }

    public string Subject { get; }

    /// <summary>The exact wire token this code carries, read from the member itself.</summary>
    /// <remarks>
    /// This was a hand-written switch over five members while the vocabulary carried no wire
    /// attribute at all: two sources of truth that can drift, and they had. The pin covered three
    /// of the five, and renaming one of the two unpinned tokens left the whole suite green.
    /// Reading the attribute makes the member the only source, and a member added without a token
    /// now fails loudly at its first use instead of projecting its CLR spelling.
    /// <see cref="ContractWire"/> is the existing mechanism and its remarks already give this exact
    /// argument; its name says Absence while its function is general, which is worth correcting
    /// separately rather than duplicating a second reader here.
    /// </remarks>
    public string ReasonCode => ContractWire.NameOf(Code);
}

public abstract record LuxembourgProfileResolution
{
    private LuxembourgProfileResolution()
    {
    }

    public sealed record Resolved : LuxembourgProfileResolution
    {
        internal Resolved(
            SourceArtifactRef sourceProfileRef,
            SourceArtifactRef completeEnumerationRef,
            IReadOnlyList<SourceArtifactRef> orderedEvidenceArtifacts,
            IReadOnlyList<ScopeObjectReductionInput> scopeInputs,
            IReadOnlyList<LuxembourgResourceResolution> resources,
            IReadOnlyList<LuxembourgDimensionAccounting> accounting)
        {
            SourceProfileRef = sourceProfileRef
                ?? throw new ArgumentNullException(nameof(sourceProfileRef));
            CompleteEnumerationRef = completeEnumerationRef
                ?? throw new ArgumentNullException(nameof(completeEnumerationRef));
            OrderedEvidenceArtifacts = LuxembourgSourceValidation.Copy(
                orderedEvidenceArtifacts,
                nameof(orderedEvidenceArtifacts));
            ScopeInputs = LuxembourgSourceValidation.Copy(scopeInputs, nameof(scopeInputs));
            Resources = LuxembourgSourceValidation.Copy(resources, nameof(resources));
            Accounting = LuxembourgSourceValidation.Copy(accounting, nameof(accounting));
        }

        public SourceArtifactRef SourceProfileRef { get; }

        public SourceArtifactRef CompleteEnumerationRef { get; }

        public IReadOnlyList<SourceArtifactRef> OrderedEvidenceArtifacts { get; }

        public IReadOnlyList<ScopeObjectReductionInput> ScopeInputs { get; }

        public IReadOnlyList<LuxembourgResourceResolution> Resources { get; }

        public IReadOnlyList<LuxembourgDimensionAccounting> Accounting { get; }
    }

    public sealed record Failed : LuxembourgProfileResolution
    {
        internal Failed(LuxembourgProfileResolutionFailure failure)
        {
            Failure = failure ?? throw new ArgumentNullException(nameof(failure));
        }

        public LuxembourgProfileResolutionFailure Failure { get; }
    }
}

internal static class LuxembourgSourceValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static IComparer<string> UnicodeScalarComparer { get; } =
        Comparer<string>.Create(CompareUnicodeScalars);

    internal static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("Collections cannot contain null values.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }

    internal static IReadOnlyList<string> CopyStrings(
        IReadOnlyList<string> values,
        string parameterName)
    {
        var copy = Copy(values, parameterName).ToArray();
        for (var index = 0; index < copy.Length; index++)
        {
            RequireScalarString(copy[index], parameterName);
            if (index > 0 && UnicodeScalarComparer.Compare(copy[index - 1], copy[index]) >= 0)
            {
                throw new ArgumentException(
                    "String collections must be ordinal-sorted and unique.",
                    parameterName);
            }
        }

        return Array.AsReadOnly(copy);
    }

    internal static bool IsExactIriTerm(
        LuxembourgObservedAssertion assertion,
        bool requireResourceIri = false)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        if (assertion.ObjectKind != LuxembourgAssertionObjectKind.Iri ||
            assertion.DatatypeIriOrEmpty.Length != 0 ||
            assertion.LanguageTagOrEmpty.Length != 0)
        {
            return false;
        }

        try
        {
            if (requireResourceIri)
            {
                RequireExactResourceIri(assertion.ObjectIriOrLexical, nameof(assertion));
            }
            else
            {
                RequireExactAbsoluteIri(assertion.ObjectIriOrLexical, nameof(assertion));
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static string RequireExactAbsoluteIri(string value, string parameterName)
    {
        RequireScalarString(value, parameterName);
        if (value.Any(char.IsWhiteSpace) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            HasInvalidPercentEscape(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "The value must be an exact absolute IRI without malformed escapes or whitespace.",
                parameterName);
        }

        return value;
    }

    internal static string RequireExactResourceIri(string value, string parameterName)
    {
        RequireExactAbsoluteIri(value, parameterName);
        var parsed = new Uri(value, UriKind.Absolute);
        if (!(value.StartsWith("http://", StringComparison.Ordinal) ||
              value.StartsWith("https://", StringComparison.Ordinal)) ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            !parsed.IsDefaultPort)
        {
            throw new ArgumentException(
                "Publisher resource IRIs must be exact HTTP(S) IRIs without unsafe URI components.",
                parameterName);
        }

        return value;
    }

    internal static bool IsExactResourceIri(string value)
    {
        try
        {
            RequireExactResourceIri(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static string RequireScalarString(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0)
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "The value must contain only valid Unicode scalar values.",
                parameterName,
                exception);
        }

        return value;
    }

    internal static string RequireScalarStringAllowEmpty(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length == 0)
        {
            return value;
        }

        return RequireScalarString(value, parameterName);
    }

    internal static string RequireLanguageTag(string value, string parameterName)
    {
        RequireScalarStringAllowEmpty(value, parameterName);
        if (value.Any(static character =>
                character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')))
        {
            throw new ArgumentException(
                "Language tags must be empty or lowercase ASCII language tags.",
                parameterName);
        }

        return value;
    }

    private static bool HasInvalidPercentEscape(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            if (index + 2 >= value.Length ||
                !Uri.IsHexDigit(value[index + 1]) ||
                !Uri.IsHexDigit(value[index + 2]))
            {
                return true;
            }

            index += 2;
        }

        return false;
    }

    private static int CompareUnicodeScalars(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftRunes = left.EnumerateRunes().GetEnumerator();
        var rightRunes = right.EnumerateRunes().GetEnumerator();
        while (leftRunes.MoveNext())
        {
            if (!rightRunes.MoveNext())
            {
                return 1;
            }

            var comparison = leftRunes.Current.Value.CompareTo(rightRunes.Current.Value);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return rightRunes.MoveNext() ? -1 : 0;
    }
}
