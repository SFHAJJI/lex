using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Binds the Union publisher's already-reviewed, already-merged scope dispositions onto the one
/// shared <c>scope/1</c> manifest family (<see cref="ScopeManifest"/>,
/// <c>lex-v3-source-scope-manifest/1</c>) that D1-Core Candidate 3 section 4 and D1-01 Candidate 5
/// R1 define.
/// </summary>
/// <remarks>
/// <para>
/// R1 is explicit that "each publisher has one canonical <c>scope/1</c> manifest" classifying every
/// object across the same four closed axes: <c>record</c>, <c>body</c>, <c>relation</c>, and
/// <c>supporting_document</c>. Luxembourg already has this binding
/// (<see cref="Luxembourg.VerifiedLuxembourgSourceProfile"/> and
/// <see cref="Luxembourg.LuxembourgScopeResolver"/>): it builds one <see cref="ScopeProfileBinding"/>
/// whose <see cref="ScopeProfileBinding.SourceProfileRef"/> and member keys carry Luxembourg's own
/// identity, then reduces raw Jolux assertions into that shared shape. Nothing under
/// <c>Source/Europe</c> did any of this before this file: every EU-specific disposition type
/// (<see cref="EuChannelDisposition"/>, <see cref="EuLanguageBodyDisposition"/>,
/// <see cref="EuFormatDisposition"/>, <see cref="EuRightsDisposition"/>,
/// <see cref="EuRelationFamilyDisposition"/>) lived on its own closed island with no path into
/// <see cref="ScopeManifest"/> at all.
/// </para>
/// <para>
/// "Mapped, not renamed" is the whole point of this file's shape. It does not fold the Union's
/// dispositions into a publisher-anonymous manifest, and it does not mint a second top-level schema
/// for the Union the way an earlier design candidate named <c>lex-lu-scope-manifest/3</c> for
/// Luxembourg's own design text. Both publishers write the one shared
/// <c>lex-v3-source-scope-manifest/1</c> schema; what stays publisher-specific is the
/// <see cref="ScopeProfileBinding.SourceProfileRef"/> and <see cref="ScopeProfileBinding.SelectorTableRef"/>
/// identity (this file's own stable <c>urn:uuid</c> constants, distinct from Luxembourg's) and the
/// member keys underneath them (<c>eu_</c>-prefixed, never Luxembourg's). A reader of a produced
/// manifest can always tell which publisher's reviewed policy produced it, the same way Luxembourg's
/// <c>lu_</c>-prefixed reason codes already let a reader tell LU rows from EU rows.
/// </para>
/// <para>
/// Register note (queue item 5, <c>STAGE1-AUTHORITY-AND-QUEUE-2026-09-03.md</c> line 60): the
/// register's one-line description names an EU design-text schema <c>total_scope/1</c> as the thing
/// to map. No candidate text, no measurement file, and no line of <c>src/</c> anywhere in this
/// repository defines a schema by that name; the phrase "total scope manifest" that a grep-for
/// substring match turns up belongs to Luxembourg's own superseded Candidate 2 title
/// (<c>D1-LU-SCOPE-MANIFEST-INVENTORY-CANDIDATE-2</c>, "D1 Luxembourg total scope manifest"), not to
/// any EU text. Per NEVER-STALL's rule for a term with no accepted authority, this file does not
/// wait on that name: it cites the actually-accepted EU authority instead
/// (<c>D1-EU-SCOPE-MANIFEST-INVENTORY-CANDIDATE-4</c>, itself a bounded repair of Candidate 2, both
/// under <c>coordination/measurements/</c>) and the already-merged EU contracts that already
/// implement pieces of that candidate's closed vocabulary
/// (<see cref="EuScopeVocabulary"/> for channels, relation families, official languages, and act
/// forms; <see cref="EuManifestationScope"/> for formats, rights, and rights exceptions). Building
/// the per-object raw-observation classifier Luxembourg has (<c>LuScopeDimensions</c>,
/// <c>LuxembourgScopeResolver.ResolveDimensions</c>) is out of this slice's reach: it would require
/// an EU per-object RDF-assertion snapshot type this repository does not have yet, and producing one
/// is exactly the still-open D1-05 EU query-plan and witness work (queue items 3 and 4), not this
/// binding slice. What this file provides instead is real and provable now: given an object's
/// already-computed, already-reviewed EU dispositions, produce the exact <c>scope/1</c>
/// four-axis reduction input for it, and a profile binding real enough that
/// <see cref="ScopeReducer.Reduce"/> accepts it end to end.
/// </para>
/// </remarks>
public static class EuScopeProfile
{
    /// <summary>The Union's own <see cref="ScopeProfileBinding.SourceProfileRef"/> identity.</summary>
    private const string ProfileResourceId = "urn:uuid:49fe8a39-4d46-4c94-b82c-12e6c8a639ef";

    /// <summary>The Union's own <see cref="ScopeProfileBinding.SelectorTableRef"/> identity.</summary>
    private const string SelectorTableResourceId = "urn:uuid:57e32290-68a8-4a34-b7a8-226886bc11a2";

    // The EU profile and selector-table artifacts are structural identities for this binding, not
    // an observed publisher census the way Luxembourg's vocabulary snapshot is. Their content
    // digest is therefore a fixed constant over the closed vocabularies this file binds, not a
    // hash of runtime input. A future revision of the bound vocabulary is a new digest and a new
    // schema ruling, exactly as ScopeManifest's own header documents for a changed root definition.
    private const string ProfileSha256 =
        "a814c4b78f42bce29beaf1cead2d188bff86473e6f599140e0c5695b5ed3fb10";
    private const string SelectorTableSha256 =
        "025bddab62e3a25dcf45519577ace992abeb4968d4c1e28516b9063c4310502c";

    private static readonly string[] SelectorKeys =
    {
        "eu_selector.record_form",
        "eu_selector.channel",
        "eu_selector.language_body",
        "eu_selector.format",
        "eu_selector.rights",
        "eu_selector.relation_family",
        "eu_selector.supporting_content_class",
    };

    private static readonly (ScopeAxis Axis, string Key)[] ProjectionRules =
    {
        (ScopeAxis.Record, "eu_projection.record"),
        (ScopeAxis.Body, "eu_projection.body"),
        (ScopeAxis.Relation, "eu_projection.relation"),
        (ScopeAxis.SupportingDocument, "eu_projection.supporting_document"),
    };

    private const string BodyCandidateRoleKey = "eu_role.body_candidate";

    /// <summary>
    /// The Union's <c>scope/1</c> profile binding: one selector table covering the closed EU
    /// vocabulary already merged in <see cref="EuScopeVocabulary"/> and
    /// <see cref="EuManifestationScope"/>, and exactly one projection rule per axis.
    /// </summary>
    public static ScopeProfileBinding BuildBinding()
    {
        var profileRef = new SourceArtifactRef(ProfileResourceId, ProfileSha256);
        var tableRef = new SourceArtifactRef(SelectorTableResourceId, SelectorTableSha256);
        var members = SelectorKeys
            .Concat(ProjectionRules.Select(static rule => rule.Key))
            .Select(key => new SourceRegistryMemberRef(tableRef, key))
            .Append(new SourceRegistryMemberRef(profileRef, BodyCandidateRoleKey))
            .OrderBy(static member => member.RegistryRef.ResourceId, StringComparer.Ordinal)
            .ThenBy(static member => member.RegistryRef.Sha256, StringComparer.Ordinal)
            .ThenBy(static member => member.MemberKey, StringComparer.Ordinal)
            .ToArray();
        var ordinals = members
            .Select((member, ordinal) => (member.MemberKey, ordinal))
            .ToDictionary(static value => value.MemberKey, static value => value.ordinal);
        var rules = ProjectionRules
            .Select((rule, ordinal) => new ScopeRuleBinding(rule.Axis, ordinals[rule.Key], ordinal))
            .ToArray();
        return new ScopeProfileBinding(
            profileRef,
            tableRef,
            members,
            SelectorKeys.Select(key => ordinals[key]).ToArray(),
            rules,
            ordinals[BodyCandidateRoleKey]);
    }

    /// <summary>
    /// Reduce one EU object's already-reviewed dispositions into the exact <c>scope/1</c> shape
    /// <see cref="ScopeReducer"/> requires: one selector per ordered selector member, and one
    /// matched rule evaluation per axis.
    /// </summary>
    public static ScopeObjectReductionInput BuildScopeInput(
        ScopeProfileBinding profile,
        EuScopeObjectDispositions dispositions,
        IReadOnlyDictionary<SourceArtifactRef, int> evidenceOrdinals)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(dispositions);
        ArgumentNullException.ThrowIfNull(evidenceOrdinals);

        var body = ReduceBody(dispositions);
        var relation = ReduceRelation(dispositions);
        var supporting = ReduceSupportingDocument(dispositions);

        var selectors = new[]
        {
            Present(
                [RecordFormToken(dispositions.RecordForm)],
                dispositions.RecordEvidenceRef,
                evidenceOrdinals),
            Present(
                [ChannelToken(dispositions.ChannelDisposition.Channel)],
                dispositions.BodyEvidenceRef,
                evidenceOrdinals),
            dispositions.LanguageDisposition is { } language
                ? Present(
                    [LanguageToken(language.Language)],
                    dispositions.BodyEvidenceRef,
                    evidenceOrdinals)
                : NotApplicable(profile, ScopeAxis.Body),
            dispositions.FormatDisposition is { } format
                ? Present(
                    [FormatToken(format.Format)],
                    dispositions.BodyEvidenceRef,
                    evidenceOrdinals)
                : NotApplicable(profile, ScopeAxis.Body),
            dispositions.RightsDisposition is { } rights
                ? Present(
                    [ContentClassToken(rights.ContentClass)],
                    dispositions.BodyEvidenceRef,
                    evidenceOrdinals)
                : NotApplicable(profile, ScopeAxis.Body),
            dispositions.RelationDispositions.Count > 0
                ? Present(
                    dispositions.RelationDispositions
                        .Select(static value => RelationFamilyToken(value.Family))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static value => value, StringComparer.Ordinal)
                        .ToArray(),
                    dispositions.RelationEvidenceRef,
                    evidenceOrdinals)
                : NotApplicable(profile, ScopeAxis.Relation),
            dispositions.SupportingContentClass is { } supportingClass
                ? Present(
                    [ContentClassToken(supportingClass)],
                    dispositions.SupportingEvidenceRef,
                    evidenceOrdinals)
                : NotApplicable(profile, ScopeAxis.SupportingDocument),
        };

        var evaluations = new[]
        {
            Projection(profile, ScopeAxis.Record, ScopeDisposition.AcceptedSelected, isBodyCandidate: false),
            Projection(profile, ScopeAxis.Body, body, isBodyCandidate: body == ScopeDisposition.AcceptedSelected),
            Projection(profile, ScopeAxis.Relation, relation, isBodyCandidate: false),
            Projection(
                profile,
                ScopeAxis.SupportingDocument,
                supporting,
                isBodyCandidate: false),
        };

        return new ScopeObjectReductionInput(dispositions.ObjectRef, selectors, evaluations);
    }

    /// <summary>
    /// The body axis is "worst wins" over acquisition channel, held language, held format, and
    /// established rights basis, exactly the precedence D1-01 Candidate 5 R1 fixes for composing a
    /// concrete multi-axis request: <c>never_ingest &gt; point &gt; typed_quarantine &gt;
    /// accepted_selected</c>.
    /// </summary>
    /// <remarks>
    /// A format in <see cref="EuManifestationScope.FormatsThatCanNeverCarryABody"/> is the one input
    /// that reaches <see cref="ScopeDisposition.NeverIngest"/> here, because that set is itself
    /// closed to a physical manifestation (print) that can never be read as digital text; every
    /// other body-not-admitted format is a typed gap pending a reviewed profile
    /// (<c>typed_quarantine</c>), not a permanent exclusion. Everything else in this composition is
    /// spelled out in Candidate 2's "Languages" and "Formats" sections and in
    /// <see cref="EuRightsDisposition"/>'s own doc comment: an excluded acquisition channel, a
    /// language whose body this scope does not hold, or a content class with no established reuse
    /// basis all stop a datum graduating past its respective disposition.
    /// </remarks>
    private static ScopeDisposition ReduceBody(EuScopeObjectDispositions dispositions)
    {
        if (!dispositions.ChannelDisposition.MayGraduate())
        {
            return ScopeDisposition.Point;
        }

        if (dispositions.LanguageDisposition is not { } language ||
            !language.CarriesBody())
        {
            return ScopeDisposition.Point;
        }

        if (dispositions.FormatDisposition is not { } format)
        {
            return ScopeDisposition.TypedQuarantine;
        }

        if (EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(format.Format))
        {
            return ScopeDisposition.NeverIngest;
        }

        if (format.Admission != EuFormatBodyAdmission.BodyAdmitted)
        {
            return ScopeDisposition.TypedQuarantine;
        }

        return dispositions.RightsDisposition is null
            ? ScopeDisposition.TypedQuarantine
            : ScopeDisposition.AcceptedSelected;
    }

    /// <summary>
    /// The relation axis is not applicable when an object carries no relation edges at all (a
    /// dossier or NIM record, for instance), and otherwise collapses to
    /// <see cref="ScopeDisposition.TypedQuarantine"/> whenever any bound family's acquisition has
    /// not completed. Decision 64 is explicit that only a completed bounded observation of one exact
    /// family can support an answer for it; an incomplete family therefore cannot be silently
    /// outvoted by a complete one on the same object.
    /// </summary>
    private static ScopeDisposition ReduceRelation(EuScopeObjectDispositions dispositions)
    {
        if (dispositions.RelationDispositions.Count == 0)
        {
            return ScopeDisposition.Point;
        }

        return dispositions.RelationDispositions.Any(static value =>
            value.Acquisition != EuRelationAcquisitionState.Complete)
            ? ScopeDisposition.TypedQuarantine
            : ScopeDisposition.AcceptedSelected;
    }

    /// <summary>
    /// The supporting-document axis. An object with no supporting content class is itself the legal
    /// text or its own metadata record, not a supporting document of anything, mirroring
    /// Luxembourg's own "not applicable to Act or Consolidation" rule. Candidate 2's ACCEPTED
    /// explanatory row is exactly <see cref="EuContentClass.Summary"/>
    /// (<c>cdm:summary_legislation_eu</c>, "never legal evidence"); editorial content shares that
    /// answer. The remaining content classes are body-content classes, never a supporting-document
    /// shape, so a caller naming one here is a construction error this axis quarantines rather than
    /// silently accepts.
    /// </summary>
    private static ScopeDisposition ReduceSupportingDocument(EuScopeObjectDispositions dispositions)
    {
        if (dispositions.SupportingContentClass is not { } contentClass)
        {
            return ScopeDisposition.Point;
        }

        return contentClass is EuContentClass.Summary or EuContentClass.EditorialContent
            ? ScopeDisposition.AcceptedSelected
            : ScopeDisposition.TypedQuarantine;
    }

    private static ScopeSelectorEvidence Present(
        IReadOnlyList<string> canonicalValues,
        SourceArtifactRef evidenceRef,
        IReadOnlyDictionary<SourceArtifactRef, int> evidenceOrdinals) => new(
        ScopeSelectorState.PublisherValuePresent,
        canonicalValues,
        ScopeSelectorEvidenceKind.ObservedValueSet,
        evidenceOrdinals[evidenceRef],
        null,
        null);

    private static ScopeSelectorEvidence NotApplicable(ScopeProfileBinding profile, ScopeAxis axis) =>
        new(
            ScopeSelectorState.SelectorNotApplicable,
            [],
            null,
            null,
            RuleOrdinal(profile, axis),
            null);

    private static ScopeRuleEvaluation Projection(
        ScopeProfileBinding profile,
        ScopeAxis axis,
        ScopeDisposition disposition,
        bool isBodyCandidate) => new(
        RuleOrdinal(profile, axis),
        ScopeRuleEvaluationState.Matched,
        disposition == ScopeDisposition.NeverIngest
            ? ScopeRuleEffect.ExactDenial
            : ScopeRuleEffect.Positive,
        disposition,
        isBodyCandidate ? [profile.BodyCandidateRoleMemberOrdinal] : Array.Empty<int>(),
        Array.Empty<int>());

    private static int RuleOrdinal(ScopeProfileBinding profile, ScopeAxis axis) =>
        profile.OrderedRules.Single(rule => rule.Axis == axis).Ordinal;

    private static string RecordFormToken(EuActForm form) => form switch
    {
        EuActForm.Directive => "DIR",
        EuActForm.Regulation => "REG",
        EuActForm.DelegatedRegulation => "REG_DEL",
        EuActForm.ImplementingRegulation => "REG_IMPL",
        EuActForm.Treaty => "TREATY",
        EuActForm.Corrigendum => "CORRIGENDUM",
        EuActForm.DelegatedDirective => "DIR_DEL",
        EuActForm.ImplementingDecision => "DEC_IMPL",
        EuActForm.Decision => "DEC",
        EuActForm.DecisionEntscheid => "DEC_ENTSCHEID",
        EuActForm.ImplementingDirective => "DIR_IMPL",
        EuActForm.DelegatedDecision => "DEC_DEL",
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown EU act form."),
    };

    private static string ChannelToken(EuChannel channel) => channel switch
    {
        EuChannel.CellarSparqlEndpoint => "cellar_sparql_endpoint",
        EuChannel.PublicationsRestResource => "publications_rest_resource",
        EuChannel.EurLexPortal => "eurlex_portal",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown EU channel."),
    };

    private static string LanguageToken(EuOfficialLanguage language) => language switch
    {
        EuOfficialLanguage.Bulgarian => "BUL",
        EuOfficialLanguage.Czech => "CES",
        EuOfficialLanguage.Danish => "DAN",
        EuOfficialLanguage.German => "DEU",
        EuOfficialLanguage.Greek => "ELL",
        EuOfficialLanguage.English => "ENG",
        EuOfficialLanguage.Estonian => "EST",
        EuOfficialLanguage.Finnish => "FIN",
        EuOfficialLanguage.French => "FRA",
        EuOfficialLanguage.Irish => "GLE",
        EuOfficialLanguage.Croatian => "HRV",
        EuOfficialLanguage.Hungarian => "HUN",
        EuOfficialLanguage.Italian => "ITA",
        EuOfficialLanguage.Latvian => "LAV",
        EuOfficialLanguage.Lithuanian => "LIT",
        EuOfficialLanguage.Maltese => "MLT",
        EuOfficialLanguage.Dutch => "NLD",
        EuOfficialLanguage.Polish => "POL",
        EuOfficialLanguage.Portuguese => "POR",
        EuOfficialLanguage.Romanian => "RON",
        EuOfficialLanguage.Slovak => "SLK",
        EuOfficialLanguage.Slovenian => "SLV",
        EuOfficialLanguage.Spanish => "SPA",
        EuOfficialLanguage.Swedish => "SWE",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown EU language."),
    };

    private static string FormatToken(EuManifestationFormat format) => format switch
    {
        EuManifestationFormat.Formex4 => "fmx4",
        EuManifestationFormat.Xhtml => "xhtml",
        EuManifestationFormat.Xhtml5 => "xhtml5",
        EuManifestationFormat.Html => "html",
        EuManifestationFormat.Pdf => "pdf",
        EuManifestationFormat.PdfA1a => "pdfa1a",
        EuManifestationFormat.PdfA1b => "pdfa1b",
        EuManifestationFormat.PdfA2a => "pdfa2a",
        EuManifestationFormat.Print => "print",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown EU format."),
    };

    private static string ContentClassToken(EuContentClass contentClass) => contentClass switch
    {
        EuContentClass.Metadata => "metadata",
        EuContentClass.Consolidation => "consolidation",
        EuContentClass.Summary => "summary",
        EuContentClass.OriginalLegalText => "original_legal_text",
        EuContentClass.EditorialContent => "editorial_content",
        _ => throw new ArgumentOutOfRangeException(
            nameof(contentClass), contentClass, "Unknown EU content class."),
    };

    private static string RelationFamilyToken(EuRelationFamily family) => family switch
    {
        EuRelationFamily.Amends => "resource_legal_amends_resource_legal",
        EuRelationFamily.AmendedBy => "resource_legal_amended_by_resource_legal",
        EuRelationFamily.Corrects => "resource_legal_corrects_resource_legal",
        EuRelationFamily.BasedOn => "resource_legal_based_on_resource_legal",
        EuRelationFamily.Repeals => "resource_legal_repeals_resource_legal",
        EuRelationFamily.ImplicitlyRepeals => "resource_legal_implicitly_repeals_resource_legal",
        EuRelationFamily.ProposesToAmend => "resource_legal_proposes_to_amend_resource_legal",
        EuRelationFamily.ConsolidatedBasedOn => "act_consolidated_based_on_resource_legal",
        EuRelationFamily.ConsolidatedConsolidates => "act_consolidated_consolidates_resource_legal",
        EuRelationFamily.CaseLawInterpretes => "case-law_interpretes_resource_legal",
        EuRelationFamily.CaseLawDeclaresVoid =>
            "case-law_declares_void_by_preliminary_ruling_resource_legal",
        EuRelationFamily.SubmitsPreliminaryQuestion =>
            "communication_case_new_submits_preliminary_question_resource_legal",
        EuRelationFamily.RequestsAnnulment =>
            "communication_case_new_requests_annulment_of_resource_legal",
        _ => throw new ArgumentOutOfRangeException(
            nameof(family), family, "Unknown EU relation family."),
    };
}

/// <summary>
/// One EU object's already-reviewed dispositions, bundled for reduction into <c>scope/1</c>.
/// </summary>
/// <remarks>
/// This type does not observe or classify anything itself. It carries the outputs of EU contracts
/// this repository already merged (<see cref="EuActForm"/> census, <see cref="EuChannelDisposition"/>,
/// <see cref="EuLanguageBodyDisposition"/>, <see cref="EuFormatDisposition"/>,
/// <see cref="EuRightsDisposition"/>, <see cref="EuRelationFamilyDisposition"/>) so
/// <see cref="EuScopeProfile.BuildScopeInput"/> has one place to read them from. Producing these
/// values for a real Cellar object is the EU query-plan and witness work the register lists as still
/// open (queue items 3 and 4); this type is deliberately silent about how they were produced.
/// </remarks>
public sealed class EuScopeObjectDispositions
{
    public EuScopeObjectDispositions(
        SourceObjectRef objectRef,
        EuActForm recordForm,
        SourceArtifactRef recordEvidenceRef,
        EuChannelDisposition channelDisposition,
        EuLanguageBodyDisposition? languageDisposition,
        EuFormatDisposition? formatDisposition,
        EuRightsDisposition? rightsDisposition,
        SourceArtifactRef bodyEvidenceRef,
        IReadOnlyList<EuRelationFamilyDisposition> relationDispositions,
        SourceArtifactRef relationEvidenceRef,
        EuContentClass? supportingContentClass,
        SourceArtifactRef supportingEvidenceRef)
    {
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        RecordForm = ContractValidation.RequireDefined(recordForm, nameof(recordForm));
        RecordEvidenceRef = recordEvidenceRef
            ?? throw new ArgumentNullException(nameof(recordEvidenceRef));
        ChannelDisposition = channelDisposition
            ?? throw new ArgumentNullException(nameof(channelDisposition));
        LanguageDisposition = languageDisposition;
        FormatDisposition = formatDisposition;
        RightsDisposition = rightsDisposition;
        BodyEvidenceRef = bodyEvidenceRef ?? throw new ArgumentNullException(nameof(bodyEvidenceRef));
        RelationDispositions = (relationDispositions
            ?? throw new ArgumentNullException(nameof(relationDispositions))).ToArray();
        if (RelationDispositions.Select(static value => value.Family).Distinct().Count() !=
            RelationDispositions.Count)
        {
            throw new ArgumentException(
                "Relation dispositions must name each family at most once.",
                nameof(relationDispositions));
        }

        RelationEvidenceRef = relationEvidenceRef
            ?? throw new ArgumentNullException(nameof(relationEvidenceRef));
        SupportingContentClass = supportingContentClass is { } value
            ? ContractValidation.RequireDefined(value, nameof(supportingContentClass))
            : null;
        SupportingEvidenceRef = supportingEvidenceRef
            ?? throw new ArgumentNullException(nameof(supportingEvidenceRef));
    }

    public SourceObjectRef ObjectRef { get; }

    public EuActForm RecordForm { get; }

    public SourceArtifactRef RecordEvidenceRef { get; }

    public EuChannelDisposition ChannelDisposition { get; }

    public EuLanguageBodyDisposition? LanguageDisposition { get; }

    public EuFormatDisposition? FormatDisposition { get; }

    public EuRightsDisposition? RightsDisposition { get; }

    public SourceArtifactRef BodyEvidenceRef { get; }

    public IReadOnlyList<EuRelationFamilyDisposition> RelationDispositions { get; }

    public SourceArtifactRef RelationEvidenceRef { get; }

    public EuContentClass? SupportingContentClass { get; }

    public SourceArtifactRef SupportingEvidenceRef { get; }
}
