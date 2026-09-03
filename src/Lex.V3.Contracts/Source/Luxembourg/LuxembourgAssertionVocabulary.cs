using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The closed Luxembourg (JOLux) non-relation assertion-predicate vocabulary. Twenty-six, per
/// Decision 65: Candidate 6's generic structural and Act-fact categories, made exact. Sixteen of
/// the twenty-six (<see cref="IsPartOf"/>, <see cref="DateApplicability"/>,
/// <see cref="DateEndApplicability"/>, <see cref="PublicationDate"/>, <see cref="InForceStatus"/>,
/// <see cref="DateDocument"/>, <see cref="DateEntryInForce"/>, <see cref="DateNoLongerInForce"/>,
/// <see cref="HistoricalLegalId"/>, <see cref="ResponsibilityOf"/>, <see cref="Title"/>,
/// <see cref="TitleShort"/>, <see cref="License"/>, <see cref="Rights"/>,
/// <see cref="RightsHolder"/>, <see cref="Publisher"/>) are the values the source profile
/// previously omitted; Decision 65 adds them by name. The other ten, including
/// <see cref="RdfType"/>, are the members already present in the already-merged
/// <see cref="VerifiedLuxembourgSourceProfile"/>'s <c>AssertionPredicate</c> vocabulary rows (this
/// file's sibling in the same directory, not edited by this slice).
/// </summary>
/// <remarks>
/// Tokens are the exact JOLux local predicate name, matching
/// <see cref="LuxembourgRelationPredicate"/>'s convention, except <see cref="RdfType"/>: that
/// predicate is <c>http://www.w3.org/1999/02/22-rdf-syntax-ns#type</c>, outside the JOLux
/// namespace, so its token is the standard <c>rdf:type</c> CURIE rather than a JOLux local name.
/// </remarks>
public enum LuxembourgAssertionPredicate
{
    [JsonStringEnumMemberName("rdf:type")]
    RdfType = 1,

    [JsonStringEnumMemberName("dateApplicability")]
    DateApplicability = 2,

    [JsonStringEnumMemberName("dateDocument")]
    DateDocument = 3,

    [JsonStringEnumMemberName("dateEndApplicability")]
    DateEndApplicability = 4,

    [JsonStringEnumMemberName("dateEntryInForce")]
    DateEntryInForce = 5,

    [JsonStringEnumMemberName("dateNoLongerInForce")]
    DateNoLongerInForce = 6,

    [JsonStringEnumMemberName("historicalLegalId")]
    HistoricalLegalId = 7,

    [JsonStringEnumMemberName("inForceStatus")]
    InForceStatus = 8,

    [JsonStringEnumMemberName("isEmbodiedBy")]
    IsEmbodiedBy = 9,

    [JsonStringEnumMemberName("isExemplifiedBy")]
    IsExemplifiedBy = 10,

    [JsonStringEnumMemberName("isMemberOf")]
    IsMemberOf = 11,

    [JsonStringEnumMemberName("isPartOf")]
    IsPartOf = 12,

    [JsonStringEnumMemberName("isRealizedBy")]
    IsRealizedBy = 13,

    [JsonStringEnumMemberName("language")]
    Language = 14,

    [JsonStringEnumMemberName("legalValue")]
    LegalValue = 15,

    [JsonStringEnumMemberName("license")]
    License = 16,

    [JsonStringEnumMemberName("previousIsExemplifiedBy")]
    PreviousIsExemplifiedBy = 17,

    [JsonStringEnumMemberName("publicationDate")]
    PublicationDate = 18,

    [JsonStringEnumMemberName("publisher")]
    Publisher = 19,

    [JsonStringEnumMemberName("responsibilityOf")]
    ResponsibilityOf = 20,

    [JsonStringEnumMemberName("rights")]
    Rights = 21,

    [JsonStringEnumMemberName("rightsHolder")]
    RightsHolder = 22,

    [JsonStringEnumMemberName("title")]
    Title = 23,

    [JsonStringEnumMemberName("titleShort")]
    TitleShort = 24,

    [JsonStringEnumMemberName("typeDocument")]
    TypeDocument = 25,

    [JsonStringEnumMemberName("userFormat")]
    UserFormat = 26,
}

/// <summary>
/// The structural fact-kind of an assertion predicate. This is the vocabulary-separation axis
/// Candidate 5 R4 requires: "LU consolidation applicability is a separate fact with its exact
/// source predicate; it cannot share an act-force slot or be inferred from an act date." Every
/// predicate has exactly one kind, fixed by <see cref="LuxembourgAssertionVocabulary.FactKindOf"/>,
/// never chosen by whoever writes a row.
/// </summary>
/// <remarks>
/// Every member carries a <c>[JsonStringEnumMemberName]</c> wire token: before this fix, this was
/// the one new enum in this slice with no wire form of its own, so its wire form was its C# member
/// name (<c>"ActForce"</c>, not <c>"act_force"</c>), and a wire test named for the disposition
/// guard passed only because the untokenised member name was an unknown value that failed
/// deserialisation before the guard was ever reached
/// (<c>LuxembourgAssertionVocabularyTests.ADeserialisedWrongFactKindIsRefusedOnTheDispositionWireToo</c>).
/// </remarks>
public enum LuxembourgAssertionFactKind
{
    /// <summary>
    /// An Act's own force state: <see cref="LuxembourgAssertionPredicate.DateEntryInForce"/>,
    /// <see cref="LuxembourgAssertionPredicate.DateNoLongerInForce"/>, and the Act-scoped reading
    /// of <see cref="LuxembourgAssertionPredicate.InForceStatus"/>. Never populated from a
    /// Consolidation's applicability interval.
    /// </summary>
    [JsonStringEnumMemberName("act_force")]
    ActForce = 1,

    /// <summary>
    /// A Consolidation's applicability interval:
    /// <see cref="LuxembourgAssertionPredicate.DateApplicability"/>,
    /// <see cref="LuxembourgAssertionPredicate.DateEndApplicability"/>, and the
    /// Consolidation-scoped reading of <see cref="LuxembourgAssertionPredicate.InForceStatus"/>.
    /// Documentation without legal effect (Decision 58 preamble); never an act-force claim.
    /// </summary>
    [JsonStringEnumMemberName("consolidation_applicability")]
    ConsolidationApplicability = 2,

    /// <summary>
    /// A descriptive date that is neither a force date nor an applicability date:
    /// <see cref="LuxembourgAssertionPredicate.DateDocument"/>,
    /// <see cref="LuxembourgAssertionPredicate.PublicationDate"/>.
    /// </summary>
    [JsonStringEnumMemberName("descriptive_date")]
    DescriptiveDate = 3,

    /// <summary>
    /// An Act's own non-temporal identity: <see cref="LuxembourgAssertionPredicate.HistoricalLegalId"/>,
    /// <see cref="LuxembourgAssertionPredicate.ResponsibilityOf"/>.
    /// </summary>
    [JsonStringEnumMemberName("act_identity")]
    ActIdentity = 4,

    /// <summary>
    /// Resource classification: <see cref="LuxembourgAssertionPredicate.RdfType"/>,
    /// <see cref="LuxembourgAssertionPredicate.TypeDocument"/>.
    /// </summary>
    [JsonStringEnumMemberName("resource_type")]
    ResourceType = 5,

    /// <summary>
    /// WEMI structural links: <see cref="LuxembourgAssertionPredicate.IsPartOf"/>,
    /// <see cref="LuxembourgAssertionPredicate.IsMemberOf"/>,
    /// <see cref="LuxembourgAssertionPredicate.IsRealizedBy"/>,
    /// <see cref="LuxembourgAssertionPredicate.IsEmbodiedBy"/>,
    /// <see cref="LuxembourgAssertionPredicate.IsExemplifiedBy"/>,
    /// <see cref="LuxembourgAssertionPredicate.PreviousIsExemplifiedBy"/>.
    /// </summary>
    [JsonStringEnumMemberName("wemi_structural")]
    WemiStructural = 6,

    /// <summary>
    /// An Expression's language and titles: <see cref="LuxembourgAssertionPredicate.Language"/>,
    /// <see cref="LuxembourgAssertionPredicate.Title"/>,
    /// <see cref="LuxembourgAssertionPredicate.TitleShort"/>.
    /// </summary>
    [JsonStringEnumMemberName("expression_language_or_title")]
    ExpressionLanguageOrTitle = 7,

    /// <summary>A Manifestation's format: <see cref="LuxembourgAssertionPredicate.UserFormat"/>.</summary>
    [JsonStringEnumMemberName("manifestation_format")]
    ManifestationFormat = 8,

    /// <summary>
    /// A Manifestation's or Expression's own legal-value assertion:
    /// <see cref="LuxembourgAssertionPredicate.LegalValue"/>.
    /// </summary>
    [JsonStringEnumMemberName("legal_value_assertion")]
    LegalValueAssertion = 9,

    /// <summary>
    /// Rights and provenance, exact-subject only, no lift:
    /// <see cref="LuxembourgAssertionPredicate.License"/>,
    /// <see cref="LuxembourgAssertionPredicate.Rights"/>,
    /// <see cref="LuxembourgAssertionPredicate.RightsHolder"/>,
    /// <see cref="LuxembourgAssertionPredicate.Publisher"/>.
    /// </summary>
    [JsonStringEnumMemberName("rights_and_provenance")]
    RightsAndProvenance = 10,
}

/// <summary>
/// The two Act-force date predicates. A strict, disjoint type from
/// <see cref="LuxembourgConsolidationApplicabilityDatePredicate"/>: no value of one type can be
/// passed where the other is expected, because they are different enums, not a shared enum split
/// by a runtime tag. This is the structural half of the E3 guard; <see cref="LuxembourgActForceDateFact"/>
/// can only be constructed from this type.
/// </summary>
public enum LuxembourgActForceDatePredicate
{
    [JsonStringEnumMemberName("dateEntryInForce")]
    DateEntryInForce = 1,

    [JsonStringEnumMemberName("dateNoLongerInForce")]
    DateNoLongerInForce = 2,
}

/// <summary>
/// The two Consolidation-applicability date predicates. Disjoint from
/// <see cref="LuxembourgActForceDatePredicate"/> by construction: see that type's remarks.
/// </summary>
public enum LuxembourgConsolidationApplicabilityDatePredicate
{
    [JsonStringEnumMemberName("dateApplicability")]
    DateApplicability = 1,

    [JsonStringEnumMemberName("dateEndApplicability")]
    DateEndApplicability = 2,
}

/// <summary>
/// One Act-force date fact. Can only be built from <see cref="LuxembourgActForceDatePredicate"/>:
/// there is no constructor overload, cast, or implicit conversion from
/// <see cref="LuxembourgConsolidationApplicabilityDatePredicate"/> or from the flat
/// <see cref="LuxembourgAssertionPredicate"/>, so a consolidation-interval value cannot be handed to
/// this type at all, let alone stored in it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgActForceDateFact
{
    [JsonConstructor]
    public LuxembourgActForceDateFact(
        LuxembourgActForceDatePredicate predicate,
        string rawLexicalValue,
        string datatypeIri,
        SourceArtifactRef evidenceRef,
        LuxembourgAssertionPredicate? underlyingPredicate = null)
    {
        Predicate = ContractValidation.RequireDefined(predicate, nameof(predicate));
        RawLexicalValue = ContractValidation.RequireIdentifier(rawLexicalValue, nameof(rawLexicalValue));
        DatatypeIri = ContractValidation.RequireIdentifier(datatypeIri, nameof(datatypeIri));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));

        var derived = LuxembourgAssertionVocabulary.UnderlyingPredicate(Predicate);
        if (underlyingPredicate is not null && underlyingPredicate.Value != derived)
        {
            throw new ArgumentException(
                $"{underlyingPredicate} does not match {derived}, the underlying predicate " +
                $"{Predicate} derives to; underlying_predicate is always re-derived and can " +
                "never disagree with it.",
                nameof(underlyingPredicate));
        }

        UnderlyingPredicate = derived;
    }

    public LuxembourgActForceDatePredicate Predicate { get; }

    /// <summary>The publisher's raw lexical value, kept verbatim rather than reparsed.</summary>
    public string RawLexicalValue { get; }

    public string DatatypeIri { get; }

    public SourceArtifactRef EvidenceRef { get; }

    /// <summary>
    /// Always exactly <see cref="LuxembourgAssertionVocabulary.UnderlyingPredicate(LuxembourgActForceDatePredicate)"/>
    /// of <see cref="Predicate"/>, never an independently trusted wire value: after construction
    /// this is never actually <see langword="null"/>, only nullable-typed because System.Text.Json
    /// requires a constructor parameter's type to match the property it binds. The constructor's
    /// <c>underlyingPredicate</c> parameter is optional (a normal document need not carry a
    /// redundant, always-derivable field at all), but when a document does supply one, it must
    /// agree with the derivation or the document is refused. Before this fix the property had no
    /// constructor parameter at all: it was serialised on write and silently dropped on read, so a
    /// document whose <c>underlying_predicate</c> contradicted its own <c>predicate</c> was
    /// accepted with no complaint. See
    /// <c>LuxembourgAssertionVocabularyTests.AContradictingUnderlyingPredicateIsRefusedOnTheActForceDateFactWire</c>.
    /// </summary>
    public LuxembourgAssertionPredicate? UnderlyingPredicate { get; }
}

/// <summary>
/// One Consolidation-applicability date fact. The mirror of
/// <see cref="LuxembourgActForceDateFact"/>: can only be built from
/// <see cref="LuxembourgConsolidationApplicabilityDatePredicate"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgConsolidationApplicabilityDateFact
{
    [JsonConstructor]
    public LuxembourgConsolidationApplicabilityDateFact(
        LuxembourgConsolidationApplicabilityDatePredicate predicate,
        string rawLexicalValue,
        string datatypeIri,
        SourceArtifactRef evidenceRef,
        LuxembourgAssertionPredicate? underlyingPredicate = null)
    {
        Predicate = ContractValidation.RequireDefined(predicate, nameof(predicate));
        RawLexicalValue = ContractValidation.RequireIdentifier(rawLexicalValue, nameof(rawLexicalValue));
        DatatypeIri = ContractValidation.RequireIdentifier(datatypeIri, nameof(datatypeIri));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));

        var derived = LuxembourgAssertionVocabulary.UnderlyingPredicate(Predicate);
        if (underlyingPredicate is not null && underlyingPredicate.Value != derived)
        {
            throw new ArgumentException(
                $"{underlyingPredicate} does not match {derived}, the underlying predicate " +
                $"{Predicate} derives to; underlying_predicate is always re-derived and can " +
                "never disagree with it.",
                nameof(underlyingPredicate));
        }

        UnderlyingPredicate = derived;
    }

    public LuxembourgConsolidationApplicabilityDatePredicate Predicate { get; }

    public string RawLexicalValue { get; }

    public string DatatypeIri { get; }

    public SourceArtifactRef EvidenceRef { get; }

    /// <summary>
    /// Always exactly the derived predicate for <see cref="Predicate"/>, never an independently
    /// trusted wire value; the same rule, and the same reason for the nullable type, as
    /// <see cref="LuxembourgActForceDateFact.UnderlyingPredicate"/>. See
    /// <c>LuxembourgAssertionVocabularyTests.AContradictingUnderlyingPredicateIsRefusedOnTheConsolidationApplicabilityDateFactWire</c>.
    /// </summary>
    public LuxembourgAssertionPredicate? UnderlyingPredicate { get; }
}

/// <summary>
/// One assertion predicate's fact-kind classification, checked against the pinned category rather
/// than accepted from a caller. This is the runtime half of the E3 guard, for callers that hold a
/// flat <see cref="LuxembourgAssertionPredicate"/> rather than one of the already-fenced date-fact
/// types: a disposition that claimed <see cref="LuxembourgAssertionFactKind.ActForce"/> for
/// <see cref="LuxembourgAssertionPredicate.DateApplicability"/> (or the reverse) would be exactly
/// the confusion Candidate 5 R4 forbids, so the pairing is refused here rather than merely
/// documented as wrong elsewhere. This is the exact mutation the Stage 2 register names for E3: "no
/// act force slot is ever populated from a consolidation interval."
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgAssertionFactDisposition
{
    [JsonConstructor]
    public LuxembourgAssertionFactDisposition(
        LuxembourgAssertionPredicate predicate,
        LuxembourgAssertionFactKind factKind,
        SourceArtifactRef evidenceRef)
    {
        Predicate = ContractValidation.RequireDefined(predicate, nameof(predicate));
        FactKind = ContractValidation.RequireDefined(factKind, nameof(factKind));
        var pinned = LuxembourgAssertionVocabulary.FactKindOf(Predicate);
        if (pinned != FactKind)
        {
            throw new ArgumentException(
                $"{Predicate} is pinned to {pinned}, not {FactKind}; a fact's kind is read from " +
                "the closed vocabulary rather than chosen by whoever writes a row.",
                nameof(factKind));
        }

        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public LuxembourgAssertionPredicate Predicate { get; }

    public LuxembourgAssertionFactKind FactKind { get; }

    public SourceArtifactRef EvidenceRef { get; }
}

/// <summary>The closed Luxembourg assertion vocabularies, and the fail-closed lookups over them.</summary>
public static class LuxembourgAssertionVocabulary
{
    /// <summary>Every assertion predicate. Twenty-six.</summary>
    public static IReadOnlyList<LuxembourgAssertionPredicate> Predicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgAssertionPredicate>());

    /// <summary>Every Act-force date predicate. Two.</summary>
    public static IReadOnlyList<LuxembourgActForceDatePredicate> ActForceDatePredicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgActForceDatePredicate>());

    /// <summary>Every Consolidation-applicability date predicate. Two.</summary>
    public static IReadOnlyList<LuxembourgConsolidationApplicabilityDatePredicate>
        ConsolidationApplicabilityDatePredicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgConsolidationApplicabilityDatePredicate>());

    /// <summary>
    /// The pinned fact-kind for every assertion predicate, naming all twenty-six explicitly and
    /// failing closed for anything else. C# cannot make an enum switch compiler-exhaustive over
    /// named members alone, so <c>LuxembourgAssertionVocabularyTests</c>'
    /// <c>FactKindCategoriesPartitionAllTwentySixPredicatesExactly</c> is what actually proves every
    /// member is covered and pinned to its expected category.
    /// </summary>
    public static LuxembourgAssertionFactKind FactKindOf(LuxembourgAssertionPredicate predicate) =>
        ContractValidation.RequireDefined(predicate, nameof(predicate)) switch
        {
            LuxembourgAssertionPredicate.DateEntryInForce => LuxembourgAssertionFactKind.ActForce,
            LuxembourgAssertionPredicate.DateNoLongerInForce => LuxembourgAssertionFactKind.ActForce,
            LuxembourgAssertionPredicate.InForceStatus => LuxembourgAssertionFactKind.ActForce,

            LuxembourgAssertionPredicate.DateApplicability =>
                LuxembourgAssertionFactKind.ConsolidationApplicability,
            LuxembourgAssertionPredicate.DateEndApplicability =>
                LuxembourgAssertionFactKind.ConsolidationApplicability,

            LuxembourgAssertionPredicate.DateDocument => LuxembourgAssertionFactKind.DescriptiveDate,
            LuxembourgAssertionPredicate.PublicationDate => LuxembourgAssertionFactKind.DescriptiveDate,

            LuxembourgAssertionPredicate.HistoricalLegalId => LuxembourgAssertionFactKind.ActIdentity,
            LuxembourgAssertionPredicate.ResponsibilityOf => LuxembourgAssertionFactKind.ActIdentity,

            LuxembourgAssertionPredicate.RdfType => LuxembourgAssertionFactKind.ResourceType,
            LuxembourgAssertionPredicate.TypeDocument => LuxembourgAssertionFactKind.ResourceType,

            LuxembourgAssertionPredicate.IsPartOf => LuxembourgAssertionFactKind.WemiStructural,
            LuxembourgAssertionPredicate.IsMemberOf => LuxembourgAssertionFactKind.WemiStructural,
            LuxembourgAssertionPredicate.IsRealizedBy => LuxembourgAssertionFactKind.WemiStructural,
            LuxembourgAssertionPredicate.IsEmbodiedBy => LuxembourgAssertionFactKind.WemiStructural,
            LuxembourgAssertionPredicate.IsExemplifiedBy => LuxembourgAssertionFactKind.WemiStructural,
            LuxembourgAssertionPredicate.PreviousIsExemplifiedBy => LuxembourgAssertionFactKind.WemiStructural,

            LuxembourgAssertionPredicate.Language => LuxembourgAssertionFactKind.ExpressionLanguageOrTitle,
            LuxembourgAssertionPredicate.Title => LuxembourgAssertionFactKind.ExpressionLanguageOrTitle,
            LuxembourgAssertionPredicate.TitleShort => LuxembourgAssertionFactKind.ExpressionLanguageOrTitle,

            LuxembourgAssertionPredicate.UserFormat => LuxembourgAssertionFactKind.ManifestationFormat,

            LuxembourgAssertionPredicate.LegalValue => LuxembourgAssertionFactKind.LegalValueAssertion,

            LuxembourgAssertionPredicate.License => LuxembourgAssertionFactKind.RightsAndProvenance,
            LuxembourgAssertionPredicate.Rights => LuxembourgAssertionFactKind.RightsAndProvenance,
            LuxembourgAssertionPredicate.RightsHolder => LuxembourgAssertionFactKind.RightsAndProvenance,
            LuxembourgAssertionPredicate.Publisher => LuxembourgAssertionFactKind.RightsAndProvenance,
            _ => throw new ArgumentOutOfRangeException(
                nameof(predicate),
                predicate,
                "This assertion predicate has no pinned fact kind."),
        };

    /// <summary>The full-vocabulary predicate an Act-force date predicate stands for.</summary>
    public static LuxembourgAssertionPredicate UnderlyingPredicate(
        LuxembourgActForceDatePredicate predicate) =>
        ContractValidation.RequireDefined(predicate, nameof(predicate)) switch
        {
            LuxembourgActForceDatePredicate.DateEntryInForce =>
                LuxembourgAssertionPredicate.DateEntryInForce,
            LuxembourgActForceDatePredicate.DateNoLongerInForce =>
                LuxembourgAssertionPredicate.DateNoLongerInForce,
            _ => throw new ArgumentOutOfRangeException(
                nameof(predicate),
                predicate,
                "This Act-force date predicate has no pinned underlying assertion predicate."),
        };

    /// <summary>The full-vocabulary predicate a Consolidation-applicability date predicate stands for.</summary>
    public static LuxembourgAssertionPredicate UnderlyingPredicate(
        LuxembourgConsolidationApplicabilityDatePredicate predicate) =>
        ContractValidation.RequireDefined(predicate, nameof(predicate)) switch
        {
            LuxembourgConsolidationApplicabilityDatePredicate.DateApplicability =>
                LuxembourgAssertionPredicate.DateApplicability,
            LuxembourgConsolidationApplicabilityDatePredicate.DateEndApplicability =>
                LuxembourgAssertionPredicate.DateEndApplicability,
            _ => throw new ArgumentOutOfRangeException(
                nameof(predicate),
                predicate,
                "This Consolidation-applicability date predicate has no pinned underlying " +
                "assertion predicate."),
        };
}
