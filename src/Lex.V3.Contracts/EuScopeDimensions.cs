using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts;

/// <summary>
/// Where a Union work sits in the hierarchy of Union law. Closed at two by construction: the
/// pipeline derives it from the first character of the CELEX, so there is no third branch to
/// reach and a third value would be a code change rather than a publisher surprise.
/// </summary>
public enum EuHierarchy
{
    [JsonStringEnumMemberName("primary_eu_law")]
    PrimaryEuLaw = 1,

    [JsonStringEnumMemberName("secondary_eu_law")]
    SecondaryEuLaw = 2,
}

/// <summary>
/// The publisher's in-force signal. Closed at three, and a value outside them is scope drift.
/// </summary>
/// <remarks>
/// <para>
/// An earlier version of this comment said the normaliser is a total function whose default arm is
/// <c>unknown</c>, so every value the publisher can send lands in one of these. That was written as
/// a reassurance and it is the opposite of the rule: a catch-all default is exactly how an
/// unrecognised publisher literal stops being scope drift and becomes a quiet <c>unknown</c>. The
/// V2 normaliser does behave that way; this contract does not inherit it.
/// </para>
/// <para>
/// <see cref="Unknown"/> is therefore reserved for the recognised condition where the publisher
/// supplied no in-force value at all. It is not a bucket for values we failed to recognise. A
/// future publisher literal must fail closed through the deserialiser, and it does: an unmapped
/// token throws rather than mapping here.
/// </para>
/// <para>
/// This is also the only in-force signal read. The architect research records annulment being
/// encoded by collapsing the validity interval to zero length rather than by any typed field, so an
/// act annulled ex tunc is <c>in_force = 0</c> with equal entry-into-force and end-of-validity
/// dates and looks ordinary to anything reading this enum alone.
/// </para>
/// </remarks>
public enum EuBindingStatus
{
    [JsonStringEnumMemberName("in_force")]
    InForce = 1,

    [JsonStringEnumMemberName("not_in_force")]
    NotInForce = 2,

    /// <summary>
    /// The publisher supplied no in-force value. A recognised absence, never a landing place for
    /// an unrecognised one.
    /// </summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown = 3,
}

/// <summary>
/// Whether a record is a dated consolidation or the original official expression. Closed at two:
/// these are the only literals the pipeline's two call sites pass.
/// </summary>
public enum EuConsolidationStatus
{
    [JsonStringEnumMemberName("published")]
    Published = 1,

    [JsonStringEnumMemberName("original_official_expression")]
    OriginalOfficialExpression = 2,
}

/// <summary>
/// How the text under a Union record was obtained. Closed at four.
/// </summary>
/// <remarks>
/// Four rather than three. A census of 154 sampled rows saw only the first three; the fourth
/// exists in the extraction layer and that sample never reached it, which is why this set is
/// stated from the code rather than from observation. Four profiles mean six pairwise
/// comparison boundaries, and a diff refuses across every one of them.
/// </remarks>
public enum EuExtractionProfile
{
    [JsonStringEnumMemberName("fmx4-eu/1")]
    Formex4 = 1,

    [JsonStringEnumMemberName("xhtml-eu/1")]
    Xhtml = 2,

    [JsonStringEnumMemberName("xhtml-eu-xlink-context/1")]
    XhtmlXlinkContext = 3,

    [JsonStringEnumMemberName("html-eu-tolerant/1")]
    HtmlTolerant = 4,
}

/// <summary>
/// The act forms present in the reviewed Union scope. Closed at twelve, and the twelve sum
/// exactly to the version headline, so a thirteenth is a scope change rather than an omission.
/// </summary>
public enum EuActForm
{
    [JsonStringEnumMemberName("DIR")]
    Directive = 1,

    [JsonStringEnumMemberName("REG")]
    Regulation = 2,

    [JsonStringEnumMemberName("REG_DEL")]
    DelegatedRegulation = 3,

    [JsonStringEnumMemberName("REG_IMPL")]
    ImplementingRegulation = 4,

    [JsonStringEnumMemberName("TREATY")]
    Treaty = 5,

    [JsonStringEnumMemberName("CORRIGENDUM")]
    Corrigendum = 6,

    [JsonStringEnumMemberName("DIR_DEL")]
    DelegatedDirective = 7,

    [JsonStringEnumMemberName("DEC_IMPL")]
    ImplementingDecision = 8,

    [JsonStringEnumMemberName("DEC")]
    Decision = 9,

    [JsonStringEnumMemberName("DEC_ENTSCHEID")]
    DecisionEntscheid = 10,

    [JsonStringEnumMemberName("DIR_IMPL")]
    ImplementingDirective = 11,

    [JsonStringEnumMemberName("DEC_DEL")]
    DelegatedDecision = 12,
}

/// <summary>
/// The CDM relation families in view for the Union scope, named by their exact predicate.
/// </summary>
/// <remarks>
/// Thirteen, assembled from two independently built sets that overlap by two rather than nesting:
/// eleven the architect research observed on live records, and four this pipeline reads, of which
/// two were never in the research list. Stating the union is the only honest way to carry both.
/// Nine of the thirteen are read by nothing today, including both repeal families and all three
/// case-law families.
/// </remarks>
public enum EuRelationFamily
{
    [JsonStringEnumMemberName("resource_legal_amends_resource_legal")]
    Amends = 1,

    [JsonStringEnumMemberName("resource_legal_amended_by_resource_legal")]
    AmendedBy = 2,

    [JsonStringEnumMemberName("resource_legal_corrects_resource_legal")]
    Corrects = 3,

    [JsonStringEnumMemberName("resource_legal_based_on_resource_legal")]
    BasedOn = 4,

    [JsonStringEnumMemberName("resource_legal_repeals_resource_legal")]
    Repeals = 5,

    [JsonStringEnumMemberName("resource_legal_implicitly_repeals_resource_legal")]
    ImplicitlyRepeals = 6,

    [JsonStringEnumMemberName("resource_legal_proposes_to_amend_resource_legal")]
    ProposesToAmend = 7,

    [JsonStringEnumMemberName("act_consolidated_based_on_resource_legal")]
    ConsolidatedBasedOn = 8,

    [JsonStringEnumMemberName("act_consolidated_consolidates_resource_legal")]
    ConsolidatedConsolidates = 9,

    [JsonStringEnumMemberName("case-law_interpretes_resource_legal")]
    CaseLawInterpretes = 10,

    [JsonStringEnumMemberName("case-law_declares_void_by_preliminary_ruling_resource_legal")]
    CaseLawDeclaresVoid = 11,

    [JsonStringEnumMemberName("communication_case_new_submits_preliminary_question_resource_legal")]
    SubmitsPreliminaryQuestion = 12,

    [JsonStringEnumMemberName("communication_case_new_requests_annulment_of_resource_legal")]
    RequestsAnnulment = 13,
}

/// <summary>
/// The Union's official language authorities. Twenty-four.
/// </summary>
/// <remarks>
/// <para>
/// Metadata is accepted for all twenty-four. Only <em>bodies</em> are restricted, and the
/// restriction is not an exclusion: the other twenty-two are POINT with
/// <c>language_body_not_held</c>, which is a statement about text we do not carry, not about
/// records we refuse.
/// </para>
/// <para>
/// An earlier version of this file modelled the language axis as two admitted and twenty-two
/// excluded, reusing channel admission. That collapsed a body policy into a metadata exclusion and
/// would have erased records this corpus does hold: corrigendum language metadata is retained in
/// every observed language, including 385 corrigenda with no ENG or FRA counterpart at all. An
/// exclusion at the metadata level would have made those invisible while claiming to be complete,
/// which is the false-absence shape Decision 64 exists to prevent.
/// </para>
/// <para>
/// Cellar itself carries 94 distinct language values across all expressions, measured on the live
/// endpoint, because it holds material beyond the official twenty-four. That larger set is not the
/// scope vocabulary and is recorded here only so the next reader does not rediscover the
/// discrepancy and assume one of the three numbers is wrong.
/// </para>
/// <para>
/// One measured consequence of the bilingual scope, worth carrying rather than discovering: 385
/// related corrigenda exist only in official languages other than EN and FR, so language-specific
/// corrigendum coverage is structurally incomplete by construction rather than by accident.
/// </para>
/// </remarks>
public enum EuOfficialLanguage
{
    [JsonStringEnumMemberName("BUL")] Bulgarian = 1,
    [JsonStringEnumMemberName("CES")] Czech = 2,
    [JsonStringEnumMemberName("DAN")] Danish = 3,
    [JsonStringEnumMemberName("DEU")] German = 4,
    [JsonStringEnumMemberName("ELL")] Greek = 5,
    [JsonStringEnumMemberName("ENG")] English = 6,
    [JsonStringEnumMemberName("EST")] Estonian = 7,
    [JsonStringEnumMemberName("FIN")] Finnish = 8,
    [JsonStringEnumMemberName("FRA")] French = 9,
    [JsonStringEnumMemberName("GLE")] Irish = 10,
    [JsonStringEnumMemberName("HRV")] Croatian = 11,
    [JsonStringEnumMemberName("HUN")] Hungarian = 12,
    [JsonStringEnumMemberName("ITA")] Italian = 13,
    [JsonStringEnumMemberName("LAV")] Latvian = 14,
    [JsonStringEnumMemberName("LIT")] Lithuanian = 15,
    [JsonStringEnumMemberName("MLT")] Maltese = 16,
    [JsonStringEnumMemberName("NLD")] Dutch = 17,
    [JsonStringEnumMemberName("POL")] Polish = 18,
    [JsonStringEnumMemberName("POR")] Portuguese = 19,
    [JsonStringEnumMemberName("RON")] Romanian = 20,
    [JsonStringEnumMemberName("SLK")] Slovak = 21,
    [JsonStringEnumMemberName("SLV")] Slovenian = 22,
    [JsonStringEnumMemberName("SPA")] Spanish = 23,
    [JsonStringEnumMemberName("SWE")] Swedish = 24,
}

/// <summary>
/// Whether this corpus carries the body text of an expression in a given official language.
/// </summary>
/// <remarks>
/// A body axis, deliberately not reusing channel admission. A channel that is excluded may carry
/// nothing at all; a language whose body is not held still contributes its metadata, and the two
/// are different facts that happen to share a shape.
/// </remarks>
public enum EuLanguageBodyState
{
    /// <summary>Body text is fetched for this language.</summary>
    [JsonStringEnumMemberName("body_candidate")]
    BodyCandidate = 1,

    /// <summary>
    /// Metadata is retained and body text is not held. POINT evidence, never an absence claim
    /// about the expression and never an exclusion of its record.
    /// </summary>
    [JsonStringEnumMemberName("body_not_held_point")]
    BodyNotHeldPoint = 2,
}

/// <summary>
/// One official language's body disposition, with the rule and the content-bound evidence for it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuLanguageBodyDisposition
{
    [JsonConstructor]
    public EuLanguageBodyDisposition(
        EuOfficialLanguage language,
        EuLanguageBodyState bodyState,
        string reasonCode,
        string ruleId,
        SourceArtifactRef evidenceRef)
    {
        Language = ContractValidation.RequireDefined(language, nameof(language));
        BodyState = ContractValidation.RequireDefined(bodyState, nameof(bodyState));

        // Body candidacy is fixed by the accepted inventory, not chosen by a caller. Without this
        // a German body candidate was constructible and deserializable, which is the same
        // caller-minted-policy shape this slice exists to remove: the previous version let a
        // caller decide the language policy through an admission flag, and this version let one
        // decide it through a state. Checked before the reason and evidence rules because a
        // language that may not be a candidate at all is a design error, while a missing reason is
        // an omission, and the more fundamental refusal should be the one reported.
        if (bodyState == EuLanguageBodyState.BodyCandidate &&
            !BodyCandidateLanguages.Contains(language))
        {
            throw new ArgumentException(
                $"{language} is not a body candidate; the reviewed scope fetches bodies in " +
                "English and French only, and every other official language is POINT with " +
                "language_body_not_held.",
                nameof(bodyState));
        }

        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        RuleId = ContractValidation.RequireIdentifier(ruleId, nameof(ruleId));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    /// <summary>
    /// The languages whose bodies the reviewed scope fetches. Exactly two, fixed by the accepted
    /// inventory: every other official language is POINT with <c>language_body_not_held</c>.
    /// </summary>
    public static IReadOnlyList<EuOfficialLanguage> BodyCandidateLanguages { get; } =
        Array.AsReadOnly(new[] { EuOfficialLanguage.English, EuOfficialLanguage.French });

    public EuOfficialLanguage Language { get; }

    public EuLanguageBodyState BodyState { get; }

    public string ReasonCode { get; }

    public string RuleId { get; }

    /// <summary>Content-bound rather than a caller string, so the reason can be checked.</summary>
    public SourceArtifactRef EvidenceRef { get; }

    /// <summary>
    /// Whether body text is carried. Deliberately not named for holding the language: metadata is
    /// accepted in all twenty-four regardless of this value.
    /// </summary>
    [JsonIgnore]
    public bool CarriesBody => BodyState == EuLanguageBodyState.BodyCandidate;
}

/// <summary>
/// The CDM predicates this pipeline reads that are not relation edges. Thirteen.
/// </summary>
/// <remarks>
/// Seventeen predicates are read in total. The other four are relation edges and live in
/// <see cref="EuRelationFamily"/> rather than being repeated here, because one predicate listed in
/// two closed sets is two things to keep in step and this file already lost one argument to that.
/// Read the two together for the full seventeen.
/// </remarks>
public enum EuCdmPredicate
{
    [JsonStringEnumMemberName("resource_legal_id_celex")]
    ResourceLegalIdCelex = 1,

    [JsonStringEnumMemberName("expression_belongs_to_work")]
    ExpressionBelongsToWork = 2,

    [JsonStringEnumMemberName("resource_legal_type")]
    ResourceLegalType = 3,

    [JsonStringEnumMemberName("work_has_resource-type")]
    WorkHasResourceType = 4,

    [JsonStringEnumMemberName("work_date_document")]
    WorkDateDocument = 5,

    [JsonStringEnumMemberName("act_consolidated_date")]
    ActConsolidatedDate = 6,

    [JsonStringEnumMemberName("date_creation_legacy")]
    DateCreationLegacy = 7,

    [JsonStringEnumMemberName("resource_legal_in-force")]
    ResourceLegalInForce = 8,

    [JsonStringEnumMemberName("expression_uses_language")]
    ExpressionUsesLanguage = 9,

    [JsonStringEnumMemberName("expression_title")]
    ExpressionTitle = 10,

    [JsonStringEnumMemberName("expression_title_short")]
    ExpressionTitleShort = 11,

    [JsonStringEnumMemberName("work_is_about_concept_eurovoc")]
    WorkIsAboutConceptEurovoc = 12,

    [JsonStringEnumMemberName("resource_legal_is_about_concept_directory-code")]
    ResourceLegalIsAboutConceptDirectoryCode = 13,
}

/// <summary>
/// Whose claim a relation edge is. Never presentation metadata: a derived inverse and a publisher
/// assertion are different facts about the world, and only one of them can be checked against the
/// publisher.
/// </summary>
public enum EuRelationAuthority
{
    /// <summary>The publisher asserts this edge in this direction.</summary>
    [JsonStringEnumMemberName("publisher_asserted")]
    PublisherAsserted = 1,

    /// <summary>The ontology authorises this inverse of a publisher assertion.</summary>
    [JsonStringEnumMemberName("ontology_authorized_inverse")]
    OntologyAuthorizedInverse = 2,

    /// <summary>
    /// Computed by this service from edges it holds. Permanently labelled derived and excluded
    /// from evidence export.
    /// </summary>
    [JsonStringEnumMemberName("local_inbound_view")]
    LocalInboundView = 3,
}

/// <summary>
/// How far acquisition of one relation family has actually got. Decision 64.
/// </summary>
/// <remarks>
/// The reason this exists: an empty edge list and "we never asked" are indistinguishable to a
/// consumer, and the corpus published today carries six relation families hardcoded to the empty
/// array for every work. Someone reading <c>repeals: []</c> concludes the act repeals nothing. So
/// absence is a claim, it belongs to one exact family, and only a completed bounded observation of
/// that family can support it.
/// </remarks>
public enum EuRelationAcquisitionState
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

    /// <summary>A complete bounded observation of this exact family. The only state that can support an absence claim.</summary>
    [JsonStringEnumMemberName("complete")]
    Complete = 4,
}

/// <summary>
/// A route by which Union data may be admitted. Channel admission is a scope disposition rather
/// than a transport detail, because it decides whether a datum can graduate past POINT.
/// </summary>
/// <remarks>
/// The portal member is deliberate and is not a gap: <c>eur-lex.europa.eu</c> answers every
/// non-browser client with an AWS WAF challenge, HTTP 202 with an empty body, so a route through
/// it cannot yield evidence at all. Naming it at all is what stops it being reached for later as
/// though nobody had checked; whether it may be used is its disposition, not its name.
/// </remarks>
public enum EuChannel
{
    [JsonStringEnumMemberName("cellar_sparql_endpoint")]
    CellarSparqlEndpoint = 1,

    [JsonStringEnumMemberName("publications_rest_resource")]
    PublicationsRestResource = 2,

    [JsonStringEnumMemberName("eurlex_portal")]
    EurLexPortal = 3,
}

/// <summary>
/// Whether a channel may carry data into scope. Separate from the channel's identity on purpose.
/// </summary>
/// <remarks>
/// An earlier version encoded exclusion inside the identity, as a member literally named
/// <c>excluded_eurlex_portal</c>, while the vocabulary exposed all three channels in one
/// undifferentiated list. A consumer iterating that list saw three known channels and had nothing
/// to read admission from except the spelling of a name. Admission is a disposition about a
/// channel, not part of what the channel is.
/// </remarks>
public enum EuChannelAdmission
{
    /// <summary>May carry data into scope.</summary>
    [JsonStringEnumMemberName("admitted")]
    Admitted = 1,

    /// <summary>Known, and refused. A datum arriving by this route cannot graduate past POINT.</summary>
    [JsonStringEnumMemberName("excluded")]
    Excluded = 2,
}

/// <summary>
/// One channel's admission, with the reason and the evidence behind it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuChannelDisposition
{
    [JsonConstructor]
    public EuChannelDisposition(
        EuChannel channel,
        EuChannelAdmission admission,
        string reasonCode,
        string ruleId,
        SourceArtifactRef evidenceRef)
    {
        Channel = ContractValidation.RequireDefined(channel, nameof(channel));
        Admission = ContractValidation.RequireDefined(admission, nameof(admission));
        // Reason, rule and evidence are required for both outcomes. An exclusion without a reason
        // is an assertion, and an admission without one is worse: it is the state a consumer will
        // rely on to fetch.
        ReasonCode = ContractValidation.RequireIdentifier(reasonCode, nameof(reasonCode));
        RuleId = ContractValidation.RequireIdentifier(ruleId, nameof(ruleId));
        EvidenceRef = evidenceRef ?? throw new ArgumentNullException(nameof(evidenceRef));
    }

    public EuChannel Channel { get; }

    public EuChannelAdmission Admission { get; }

    public string ReasonCode { get; }

    public string RuleId { get; }

    public SourceArtifactRef EvidenceRef { get; }

    /// <summary>Whether a datum arriving by this channel may graduate past POINT.</summary>
    [JsonIgnore]
    public bool MayGraduate => Admission == EuChannelAdmission.Admitted;
}

/// <summary>
/// One relation family's disposition: whose claim it is, how far acquisition got, and the
/// content-bound evidence for each.
/// </summary>
/// <remarks>
/// <para>
/// This records acquisition state. It deliberately does <em>not</em> decide whether an empty edge
/// list may be read as an absence claim. An earlier version exposed a
/// <c>SupportsAbsenceClaim</c> property that went true on a complete acquisition with an evidence
/// identifier, which let this slice mint absence eligibility from a caller string. Absence
/// eligibility requires the shared delivery proof plus an independently different witness, and two
/// passes of one query are not independent, so only the later EU source-completion validator may
/// mint it. Decision 64, and the amendment on this issue.
/// </para>
/// <para>
/// Both references are content-bound rather than free strings. An identifier a caller invents can
/// name anything, so <c>"x"</c> could authorize an ontology inverse; a
/// <see cref="SourceArtifactRef"/> is a resource identity plus a digest, and a
/// <see cref="SourceRegistryMemberRef"/> names one member of one such artifact.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuRelationFamilyDisposition
{
    [JsonConstructor]
    public EuRelationFamilyDisposition(
        EuRelationFamily family,
        EuRelationAuthority authority,
        EuRelationAcquisitionState acquisition,
        SourceArtifactRef? completionEvidenceRef,
        SourceRegistryMemberRef? ontologyAuthorityRef)
    {
        Family = ContractValidation.RequireDefined(family, nameof(family));
        Authority = ContractValidation.RequireDefined(authority, nameof(authority));
        Acquisition = ContractValidation.RequireDefined(acquisition, nameof(acquisition));

        // Authority first. Ordering is not cosmetic: with the evidence rules first, an inverse
        // claiming completion with no evidence would be refused for the wrong reason and this
        // guard would never be reached on that input.
        if (authority == EuRelationAuthority.OntologyAuthorizedInverse)
        {
            OntologyAuthorityRef = ontologyAuthorityRef
                ?? throw new ArgumentNullException(
                    nameof(ontologyAuthorityRef),
                    "An ontology-authorized inverse must name the ontology member that authorizes it.");
        }
        else if (ontologyAuthorityRef is not null)
        {
            throw new ArgumentException(
                "Only an ontology-authorized inverse carries an ontology authority reference.",
                nameof(ontologyAuthorityRef));
        }

        if (authority == EuRelationAuthority.LocalInboundView &&
            acquisition == EuRelationAcquisitionState.Complete)
        {
            throw new ArgumentException(
                "A locally computed inbound view cannot be a completed publisher observation.",
                nameof(authority));
        }

        // A completed acquisition names the observation that completed it. This is evidence that
        // an observation happened, and nothing more: it does not make the family absence-eligible.
        if (acquisition == EuRelationAcquisitionState.Complete)
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

    public EuRelationFamily Family { get; }

    public EuRelationAuthority Authority { get; }

    public EuRelationAcquisitionState Acquisition { get; }

    /// <summary>The observation that completed this family's acquisition, when one did.</summary>
    public SourceArtifactRef? CompletionEvidenceRef { get; }

    /// <summary>The ontology member authorizing this inverse, when the authority is one.</summary>
    public SourceRegistryMemberRef? OntologyAuthorityRef { get; }
}

/// <summary>
/// The closed Union vocabularies, and the fail-closed lookups over them.
/// </summary>
/// <remarks>
/// This type deliberately carries no token lookup of its own. `ExactStringEnumConverter` in
/// `ContractJson.cs` already resolves a member to its wire token and back, ordinal and
/// case-sensitive, fails closed with a `JsonException` on an unknown token, and refuses two
/// members that share one token at static initialisation. I wrote a second copy of that before
/// finding it. Two vocabularies that must agree and are enforced in two places eventually
/// disagree, so the closed sets live here and the resolution stays where it already worked.
/// </remarks>
public static class EuScopeVocabulary
{
    /// <summary>Every hierarchy value. Two.</summary>
    public static IReadOnlyList<EuHierarchy> Hierarchies { get; } =
        Array.AsReadOnly(Enum.GetValues<EuHierarchy>());

    /// <summary>Every binding status. Three.</summary>
    public static IReadOnlyList<EuBindingStatus> BindingStatuses { get; } =
        Array.AsReadOnly(Enum.GetValues<EuBindingStatus>());

    /// <summary>Every consolidation status. Two.</summary>
    public static IReadOnlyList<EuConsolidationStatus> ConsolidationStatuses { get; } =
        Array.AsReadOnly(Enum.GetValues<EuConsolidationStatus>());

    /// <summary>Every extraction profile. Four.</summary>
    public static IReadOnlyList<EuExtractionProfile> ExtractionProfiles { get; } =
        Array.AsReadOnly(Enum.GetValues<EuExtractionProfile>());

    /// <summary>Every act form. Twelve.</summary>
    public static IReadOnlyList<EuActForm> ActForms { get; } =
        Array.AsReadOnly(Enum.GetValues<EuActForm>());

    /// <summary>Every official Union language the publisher offers. Twenty-four.</summary>
    public static IReadOnlyList<EuOfficialLanguage> OfficialLanguages { get; } =
        Array.AsReadOnly(Enum.GetValues<EuOfficialLanguage>());

    /// <summary>Every non-relation CDM predicate read. Thirteen.</summary>
    public static IReadOnlyList<EuCdmPredicate> CdmPredicates { get; } =
        Array.AsReadOnly(Enum.GetValues<EuCdmPredicate>());

    /// <summary>
    /// The four relation families this pipeline reads today. Named rather than derived, because
    /// "which of the thirteen do we read" is not recoverable from the vocabulary itself, and the
    /// read set is thirteen non-relation predicates plus exactly these four.
    /// </summary>
    public static IReadOnlyList<EuRelationFamily> ReadRelationFamilies { get; } =
        Array.AsReadOnly(new[]
        {
            EuRelationFamily.Amends,
            EuRelationFamily.Corrects,
            EuRelationFamily.BasedOn,
            EuRelationFamily.ConsolidatedBasedOn,
        });

    /// <summary>Every relation family. Thirteen.</summary>
    public static IReadOnlyList<EuRelationFamily> RelationFamilies { get; } =
        Array.AsReadOnly(Enum.GetValues<EuRelationFamily>());

    /// <summary>
    /// Every channel identity. Three, and this list says nothing about which may be used: read
    /// <see cref="EuChannelDisposition"/> for that. Listing identities and admission together was
    /// the defect this separation fixes.
    /// </summary>
    public static IReadOnlyList<EuChannel> Channels { get; } =
        Array.AsReadOnly(Enum.GetValues<EuChannel>());
}
