using System.Collections.ObjectModel;
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
/// The identifier families a publisher fact may carry, declared here rather than reused from
/// the shared catalog.
/// </summary>
/// <remarks>
/// The shared <c>IdentifierFamily</c> has ELI, CELEX, memorial and historical legal id only. A
/// EUR-Lex case is identified by a Cellar work URI, a CELEX number and an ECLI at the same time,
/// and a single shared family cannot hold that set: choosing one discards the others, which is
/// the loss this package exists to prevent. This family set is Facts-local and additive, and the
/// shared catalog is untouched.
/// </remarks>
public enum FactsIdentifierFamily
{
    [JsonStringEnumMemberName("eli")]
    Eli,

    [JsonStringEnumMemberName("celex")]
    Celex,

    /// <summary>The ECLI of a case. A member of the identity set, never a loose string.</summary>
    [JsonStringEnumMemberName("ecli")]
    Ecli,

    /// <summary>A Cellar work-level URI.</summary>
    [JsonStringEnumMemberName("cellar_work_uri")]
    CellarWorkUri,

    /// <summary>A Cellar resource-level URI.</summary>
    [JsonStringEnumMemberName("cellar_resource_uri")]
    CellarResourceUri,

    [JsonStringEnumMemberName("memorial")]
    Memorial,

    [JsonStringEnumMemberName("historical_legal_id")]
    HistoricalLegalId,
}

/// <summary>
/// How a relation edge came to exist. This is never inferred from context: every edge states
/// which of the three it is, so a locally derived view can never be read as a publisher claim.
/// </summary>
public enum RelationAssertionKind
{
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
/// Whether the target of an edge carries an ECLI, does not, or is not the sort of thing that
/// has one.
/// </summary>
/// <remarks>
/// The three states are decided against the target's own identity set rather than declared
/// freely, and there is deliberately no value meaning "we made one up". Candidate 1 conflated
/// the second and third: a Luxembourg statute has no ECLI and never will, which is not the same
/// condition as a court decision whose publisher record omits one.
/// </remarks>
public enum EcliState
{
    /// <summary>The target identity set carries exactly one ECLI.</summary>
    [JsonStringEnumMemberName("ecli_present")]
    EcliPresent,

    /// <summary>
    /// The target is a case and the publisher's record carries no ECLI for it. The edge is kept
    /// and typed, never dropped and never filled with a constructed identifier.
    /// </summary>
    [JsonStringEnumMemberName("ecli_missing")]
    EcliMissing,

    /// <summary>The target is not a case, so an ECLI does not apply to it.</summary>
    [JsonStringEnumMemberName("ecli_not_applicable")]
    EcliNotApplicable,
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

    /// <summary>Signature of the act, which is distinct from its document date.</summary>
    [JsonStringEnumMemberName("signature_date")]
    SignatureDate,

    /// <summary>
    /// Entry into force. Distinct from <see cref="ApplicationDate"/>: EUR-Lex `fd_335` separates
    /// EV from MA and the Work dossier renders them separately, so collapsing the two loses a
    /// publisher fact the dossier is required to show.
    /// </summary>
    [JsonStringEnumMemberName("entry_into_force")]
    EntryIntoForce,

    /// <summary>Date of application, the MA half of the EV/MA pair.</summary>
    [JsonStringEnumMemberName("application_date")]
    ApplicationDate,

    [JsonStringEnumMemberName("end_of_validity")]
    EndOfValidity,

    /// <summary>
    /// A publisher deadline with no evidence tying it to transposition.
    /// </summary>
    /// <remarks>
    /// The generic `resource_legal_date_deadline` is not necessarily a transposition deadline.
    /// This is where such a date lands unless the promotion evidence below justifies the
    /// stronger reading, so an ordinary deadline is never silently upgraded.
    /// </remarks>
    [JsonStringEnumMemberName("publisher_deadline")]
    PublisherDeadline,

    /// <summary>
    /// A deadline shown to be a transposition deadline by directive-specific qualifier or NIM
    /// evidence. Never assigned without that evidence.
    /// </summary>
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
/// What justifies reading a publisher deadline as a transposition deadline.
/// </summary>
/// <remarks>
/// A deadline carries <see cref="DateSemanticRole.PublisherDeadline"/> unless one of these is
/// present. Candidate 2 had no such rule, so every generic deadline either became a
/// transposition deadline it might not be, or collapsed into "role not stated" and lost the fact.
/// </remarks>
public enum TranspositionEvidence
{
    /// <summary>No evidence. The date stays a publisher deadline.</summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>A directive-specific publisher qualifier naming transposition.</summary>
    [JsonStringEnumMemberName("directive_qualifier")]
    DirectiveQualifier,

    /// <summary>A national implementing measure record tying the deadline to transposition.</summary>
    [JsonStringEnumMemberName("nim_record")]
    NimRecord,
}

/// <summary>
/// The precision actually present in the publisher's lexical value.
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
/// Whether a date is the publishers' open-end sentinel.
/// </summary>
/// <remarks>
/// Candidate 1 also declared <c>not_yet_determined</c>. It is removed rather than kept, because
/// no lexical form could be bound to it: "the publisher has not decided" is not a date, and a
/// record that requires a lexical date value is the wrong carrier for it. Keeping an unbindable
/// member would have meant any date at all could be labelled with it.
/// </remarks>
public enum DateOpenSentinel
{
    [JsonStringEnumMemberName("not_open")]
    NotOpen,

    /// <summary>
    /// The EUR-Lex open end, which is exactly <c>9999-12-31</c> at <c>xsd:date</c>. Any other
    /// lexical value carrying this state is refused.
    /// </summary>
    [JsonStringEnumMemberName("open_ended")]
    OpenEnded,
}

/// <summary>
/// Which closed vocabulary a drift was detected against.
/// </summary>
public enum VocabularyKind
{
    [JsonStringEnumMemberName("relation_assertion_kind")]
    RelationAssertionKind,

    [JsonStringEnumMemberName("identifier_family")]
    IdentifierFamily,

    [JsonStringEnumMemberName("ecli_state")]
    EcliState,

    [JsonStringEnumMemberName("target_body_scope")]
    TargetBodyScope,

    [JsonStringEnumMemberName("date_semantic_role")]
    DateSemanticRole,

    [JsonStringEnumMemberName("transposition_evidence")]
    TranspositionEvidence,

    [JsonStringEnumMemberName("date_precision")]
    DatePrecision,

    [JsonStringEnumMemberName("date_open_sentinel")]
    DateOpenSentinel,
}

/// <summary>
/// The exact one-to-one binding between a closed enum and the vocabulary kind that names it.
/// </summary>
/// <remarks>
/// Without this, a drift report could read a term through one vocabulary and label it with
/// another: Codex read <c>DateSemanticRole.DocumentDate</c> while the report claimed the
/// vocabulary was <c>relation_predicate</c>, and nothing objected. A drift report whose label
/// does not match the set it was measured against is worse than no report, because it sends the
/// reader to the wrong contract.
/// </remarks>
public static class FactsVocabularies
{
    private static readonly ReadOnlyDictionary<Type, VocabularyKind> KindsByType =
        new(new Dictionary<Type, VocabularyKind>
        {
            [typeof(RelationAssertionKind)] = VocabularyKind.RelationAssertionKind,
            [typeof(FactsIdentifierFamily)] = VocabularyKind.IdentifierFamily,
            [typeof(EcliState)] = VocabularyKind.EcliState,
            [typeof(TargetBodyScope)] = VocabularyKind.TargetBodyScope,
            [typeof(DateSemanticRole)] = VocabularyKind.DateSemanticRole,
            [typeof(TranspositionEvidence)] = VocabularyKind.TranspositionEvidence,
            [typeof(DatePrecision)] = VocabularyKind.DatePrecision,
            [typeof(DateOpenSentinel)] = VocabularyKind.DateOpenSentinel,
        });

    /// <summary>The kind naming this enum, or a throw if the enum is not a Facts vocabulary.</summary>
    public static VocabularyKind KindFor<TEnum>()
        where TEnum : struct, Enum =>
        KindsByType.TryGetValue(typeof(TEnum), out var kind)
            ? kind
            : throw new ArgumentException(
                $"{typeof(TEnum).Name} is not a closed Facts vocabulary.",
                nameof(TEnum));

    public static bool IsFactsVocabulary(Type type) => KindsByType.ContainsKey(type);

    /// <summary>Every kind, so a test can prove the registry covers the whole enum.</summary>
    public static IReadOnlyCollection<VocabularyKind> AllKinds { get; } =
        Array.AsReadOnly(KindsByType.Values.ToArray());
}
