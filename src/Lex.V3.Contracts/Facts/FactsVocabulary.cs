using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// Wire identities for the D1 publisher-fact contracts.
/// </summary>
/// <remarks>
/// These live beside the Facts contracts rather than in <c>V3SchemaIds</c> on purpose. The D1
/// path claim makes the shared contract catalog exclusively Codex's, so this package declares
/// its own identities inside its own exclusive path and never edits the shared catalog.
/// </remarks>
public static class FactsSchemaIds
{
    public const string FactsCommon = "lex-v3-facts-common/1";
    public const string PublisherRelation = "lex-v3-publisher-relation/1";
    public const string DerivedInverseRelation = "lex-v3-derived-inverse-relation/1";
    public const string LocalInboundView = "lex-v3-local-inbound-view/1";
    public const string RelationFact = "lex-v3-relation-fact/1";
    public const string PublisherDate = "lex-v3-publisher-date/1";
    public const string PublisherDateFact = "lex-v3-publisher-date-fact/1";
    public const string VocabularyDrift = "lex-v3-vocabulary-drift/1";
}

public static class FactsSchemaResourceIds
{
    public const string FactsCommon = "urn:uuid:0a5d1c2e-4f3b-4d18-9c67-1a8f2b6d5e40";
    public const string PublisherRelation = "urn:uuid:1b6e2d3f-5a4c-4e29-8d70-2b9f3c7e6f51";
    public const string DerivedInverseRelation = "urn:uuid:2c7f3e40-6b5d-4f3a-9e81-3ca04d8f7062";
    public const string LocalInboundView = "urn:uuid:3d804f51-7c6e-4a4b-af92-4db15e907173";
    public const string RelationFact = "urn:uuid:4e915062-8d7f-4b5c-b0a3-5ec26fa18284";
    public const string PublisherDate = "urn:uuid:5fa26173-9e80-4c6d-c1b4-6fd370b29395";
    public const string PublisherDateFact = "urn:uuid:60b37284-af91-4d7e-d2c5-70e481c3a4a6";
    public const string VocabularyDrift = "urn:uuid:71c48395-b0a2-4e8f-e3d6-81f592d4b5b7";

    public static string ForWireSchema(string schema) => schema switch
    {
        FactsSchemaIds.FactsCommon => FactsCommon,
        FactsSchemaIds.PublisherRelation => PublisherRelation,
        FactsSchemaIds.DerivedInverseRelation => DerivedInverseRelation,
        FactsSchemaIds.LocalInboundView => LocalInboundView,
        FactsSchemaIds.RelationFact => RelationFact,
        FactsSchemaIds.PublisherDate => PublisherDate,
        FactsSchemaIds.PublisherDateFact => PublisherDateFact,
        FactsSchemaIds.VocabularyDrift => VocabularyDrift,
        _ => throw new ArgumentException("Unknown facts schema identity.", nameof(schema)),
    };
}

/// <summary>
/// How a relation edge came to exist. This is never inferred from context: every edge states
/// which of the three it is, so a locally derived view can never be read as a publisher claim.
/// </summary>
public enum RelationAssertionKind
{
    /// <summary>The publisher asserted this edge directly, in the direction it was asserted.</summary>
    [JsonStringEnumMemberName("publisher_asserted")]
    PublisherAsserted,

    /// <summary>
    /// The inverse of a publisher assertion, derived only where the publisher's own ontology
    /// authorizes that inverse. The authorizing statement travels with the edge.
    /// </summary>
    [JsonStringEnumMemberName("ontology_authorized_inverse")]
    OntologyAuthorizedInverse,

    /// <summary>
    /// A locally computed inbound view. Carries no publisher authority whatsoever and must
    /// never be presented as one.
    /// </summary>
    [JsonStringEnumMemberName("local_inbound_view")]
    LocalInboundView,
}

/// <summary>
/// Whether the ECLI of a relation target is held, absent at the publisher, or absent locally.
/// There is deliberately no value meaning "we made one up".
/// </summary>
public enum EcliState
{
    [JsonStringEnumMemberName("ecli_present")]
    EcliPresent,

    /// <summary>
    /// The publisher's own record carries no ECLI for this case. The edge is kept and typed,
    /// never dropped and never filled with a constructed identifier.
    /// </summary>
    [JsonStringEnumMemberName("ecli_missing")]
    EcliMissing,
}

/// <summary>
/// Whether the body behind an officially identified target is held. A target with official
/// identity and no held body is a first-class state, not an error and not an omission.
/// </summary>
public enum TargetBodyScope
{
    [JsonStringEnumMemberName("body_in_scope_held")]
    BodyInScopeHeld,

    [JsonStringEnumMemberName("body_in_scope_not_held")]
    BodyInScopeNotHeld,

    [JsonStringEnumMemberName("body_outside_scope")]
    BodyOutsideScope,
}

/// <summary>
/// The semantic role a publisher date plays. Never inferred from the order dates appear in.
/// </summary>
public enum DateSemanticRole
{
    [JsonStringEnumMemberName("document_date")]
    DocumentDate,

    [JsonStringEnumMemberName("publication_date")]
    PublicationDate,

    [JsonStringEnumMemberName("entry_into_force")]
    EntryIntoForce,

    [JsonStringEnumMemberName("end_of_validity")]
    EndOfValidity,

    [JsonStringEnumMemberName("transposition_deadline")]
    TranspositionDeadline,

    [JsonStringEnumMemberName("notification_date")]
    NotificationDate,

    /// <summary>
    /// The publisher supplied a date whose role its own vocabulary does not pin down. This is a
    /// recorded state, not a default: it is never silently mapped onto one of the roles above.
    /// </summary>
    [JsonStringEnumMemberName("role_not_stated_by_publisher")]
    RoleNotStatedByPublisher,
}

/// <summary>
/// The precision actually present in the publisher's lexical value. A day-precision reading of a
/// year-precision literal is a fabrication, so precision is carried rather than assumed.
/// </summary>
public enum DatePrecision
{
    [JsonStringEnumMemberName("year")]
    Year,

    [JsonStringEnumMemberName("year_month")]
    YearMonth,

    [JsonStringEnumMemberName("year_month_day")]
    YearMonthDay,
}

/// <summary>
/// Open-ended date sentinels as the publishers actually express them.
/// </summary>
public enum DateOpenSentinel
{
    [JsonStringEnumMemberName("not_open")]
    NotOpen,

    /// <summary>An explicitly open end, such as EUR-Lex 9999-12-31.</summary>
    [JsonStringEnumMemberName("open_ended")]
    OpenEnded,

    /// <summary>The publisher states the value is not yet determined.</summary>
    [JsonStringEnumMemberName("not_yet_determined")]
    NotYetDetermined,
}

/// <summary>
/// Which closed vocabulary a drift was detected against.
/// </summary>
public enum VocabularyKind
{
    [JsonStringEnumMemberName("relation_predicate")]
    RelationPredicate,

    [JsonStringEnumMemberName("date_semantic_role")]
    DateSemanticRole,

    [JsonStringEnumMemberName("date_open_sentinel")]
    DateOpenSentinel,

    [JsonStringEnumMemberName("date_datatype")]
    DateDatatype,

    [JsonStringEnumMemberName("axiom_qualifier")]
    AxiomQualifier,
}
