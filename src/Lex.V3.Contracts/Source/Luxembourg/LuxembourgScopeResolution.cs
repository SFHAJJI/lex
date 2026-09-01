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

public enum LuxembourgProfileResolutionFailureCode
{
    InvalidPublisherIri = 1,
    IncompleteVocabulary = 2,
    UnknownVocabularyDrift = 3,
    SelectorConflict = 4,
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
                 [VerifiedLuxembourgSourceProfile.TypeDocumentPrefix + "TC"]) ||
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
        LuxembourgBodyJoinResolution bodyJoin)
    {
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        Dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        Assertions = LuxembourgSourceValidation.Copy(assertions, nameof(assertions));
        Relations = LuxembourgSourceValidation.Copy(relations, nameof(relations));
        WemiTopology = wemiTopology ?? throw new ArgumentNullException(nameof(wemiTopology));
        BodyJoin = bodyJoin ?? throw new ArgumentNullException(nameof(bodyJoin));
    }

    public SourceObjectRef ObjectRef { get; }

    public LuScopeDimensions Dimensions { get; }

    public IReadOnlyList<LuxembourgResolvedAssertion> Assertions { get; }

    public IReadOnlyList<LuxembourgResolvedRelation> Relations { get; }

    public LuxembourgWemiTopologyResolution WemiTopology { get; }

    public LuxembourgBodyJoinResolution BodyJoin { get; }
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

    public string ReasonCode => Code switch
    {
        LuxembourgProfileResolutionFailureCode.InvalidPublisherIri =>
            "profile_resolution_failed_invalid_publisher_iri",
        LuxembourgProfileResolutionFailureCode.IncompleteVocabulary =>
            "profile_resolution_failed_incomplete_vocabulary",
        LuxembourgProfileResolutionFailureCode.UnknownVocabularyDrift =>
            "profile_resolution_failed_unknown_vocabulary_drift",
        LuxembourgProfileResolutionFailureCode.SelectorConflict =>
            "profile_resolution_failed_selector_conflict",
        LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected =>
            "profile_resolution_failed_evidence_binding_rejected",
        _ => throw new InvalidOperationException("Unknown Luxembourg profile failure code."),
    };
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
