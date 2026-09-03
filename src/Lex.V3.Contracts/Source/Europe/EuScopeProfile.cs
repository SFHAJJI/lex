using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// binding slice. The per-object watermark seed map and its closure digests are part of that same
/// D1-05 deliverable and are not bound anywhere in this file either; this file consumes
/// already-produced dispositions and says nothing about how the underlying per-object RDF snapshot,
/// its seed map, or its closure evidence were produced. What this file provides instead is real and
/// provable now: given an object's already-computed, already-reviewed EU dispositions, produce the
/// exact <c>scope/1</c> four-axis reduction input for it, and a profile binding real enough that
/// <see cref="ScopeReducer.Reduce"/> accepts it end to end.
/// </para>
/// </remarks>
public static class EuScopeProfile
{
    /// <summary>The Union's own <see cref="ScopeProfileBinding.SourceProfileRef"/> identity.</summary>
    private const string ProfileResourceId = "urn:uuid:49fe8a39-4d46-4c94-b82c-12e6c8a639ef";

    /// <summary>The Union's own <see cref="ScopeProfileBinding.SelectorTableRef"/> identity.</summary>
    private const string SelectorTableResourceId = "urn:uuid:57e32290-68a8-4a34-b7a8-226886bc11a2";

    /// <summary>
    /// The accepted <c>D1-EU-SCOPE-MANIFEST-INVENTORY-CANDIDATE-4</c> digest
    /// (<c>coordination/measurements/D1-EU-SCOPE-MANIFEST-INVENTORY-CANDIDATE-4-2026-08-31.md</c>,
    /// issue 331, Decision 73), the SHA-256 of that file's exact bytes, folded into
    /// <see cref="ProfileSha256"/> exactly as
    /// <see cref="Luxembourg.VerifiedLuxembourgSourceProfile"/> folds in its own accepted Candidate
    /// 6 digest. A future revision of the accepted candidate is a new digest and a new schema
    /// ruling, not an edit to this constant.
    /// </summary>
    private const string Candidate4Sha256 =
        "052a3223d0f95caa491225c0d5420a164217eb3c370eb3478673fb39413ab4f1";

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
    /// The Union's <c>scope/1</c> profile identity digest.
    /// </summary>
    /// <remarks>
    /// Computed, not a literal placeholder: it hashes the closed EU vocabularies and rule tables
    /// this binding actually encodes (every act form; every channel with its
    /// <see cref="EuChannelDisposition.PolicyFor"/> admission; every official language with whether
    /// <see cref="EuLanguageBodyDisposition.BodyCandidateLanguages"/> admits its body; every
    /// manifestation format with whether <see cref="EuManifestationScope.FormatsThatCanNeverCarryABody"/>
    /// excludes it; every content class with its <see cref="EuRightsDisposition.BasisFor"/> reuse
    /// basis; every relation family), folding in <see cref="Candidate4Sha256"/> the same way
    /// <see cref="Luxembourg.VerifiedLuxembourgSourceProfile"/> folds in its own accepted candidate
    /// digest. This is not an observed publisher census the way Luxembourg's vocabulary snapshot is
    /// -- the bound vocabulary is closed and fixed rather than supplied per call -- so the digest is
    /// computed once from that fixed input rather than once per resolved object. It is still a real
    /// digest of the real encoded policy: <see cref="ComputeProfileSha256"/> is exposed internally
    /// so a test can vary that input and observe the output change.
    /// </remarks>
    private static readonly string ProfileSha256 = ComputeProfileSha256(
        EuScopeVocabulary.ActForms,
        EuScopeVocabulary.Channels,
        EuScopeVocabulary.OfficialLanguages,
        Enum.GetValues<EuManifestationFormat>(),
        Enum.GetValues<EuContentClass>(),
        EuScopeVocabulary.RelationFamilies,
        Candidate4Sha256);

    /// <summary>
    /// The Union's <c>scope/1</c> selector-table identity digest, computed over this file's own
    /// closed selector and projection-rule keys, mirroring
    /// <see cref="Luxembourg.VerifiedLuxembourgSourceProfile"/>'s selector-table digest.
    /// </summary>
    private static readonly string SelectorTableSha256 =
        ComputeSelectorTableSha256(SelectorKeys, ProjectionRules);

    /// <summary>
    /// The Union's <c>scope/1</c> profile binding: one selector table covering the closed EU
    /// vocabulary already merged in <see cref="EuScopeVocabulary"/> and
    /// <see cref="EuManifestationScope"/>, and exactly one projection rule per axis.
    /// </summary>
    /// <remarks>
    /// The member sort below (registry-ref resource id, then sha256, then member key) is not a free
    /// choice: <see cref="ScopeProfileBinding"/>'s own constructor requires exactly this canonical
    /// order and throws on anything else. <see cref="ProfileResourceId"/> sorting ahead of
    /// <see cref="SelectorTableResourceId"/> under that order -- which is why the Union's one
    /// profile-owned member lands before every table-owned member below -- follows from which UUID
    /// happens to sort first; that relationship is pinned by
    /// <c>EuScopeProfileTests.ProfileResourceSortsBeforeSelectorTableResourceUnderTheSharedCanonicalOrder</c>
    /// so a future change to either UUID cannot silently reorder the table without a failing test.
    /// </remarks>
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

        // Every selector below cites the evidence reference of the exact disposition it reads,
        // never a shared stand-in: the record selector cites RecordEvidenceRef, the relation
        // selector cites RelationEvidenceRef, the supporting selector cites SupportingEvidenceRef,
        // and the channel/language/format/rights selectors each cite their own disposition's own
        // EvidenceRef rather than a batched "body" evidence that does not correspond to what any one
        // of them actually observed. The one exception is a missing language expression: there is no
        // per-language observation to cite when none exists, so that case cites RecordEvidenceRef,
        // the one evidence this type always carries for the object itself, rather than inventing a
        // second per-object reference for exactly one selector.
        var selectors = new[]
        {
            Present(
                [RecordFormToken(dispositions.RecordForm)],
                dispositions.RecordEvidenceRef,
                evidenceOrdinals),
            Present(
                [ChannelToken(dispositions.ChannelDisposition.Channel)],
                dispositions.ChannelDisposition.EvidenceRef,
                evidenceOrdinals),
            dispositions.LanguageDisposition is { } language
                ? Present(
                    [LanguageToken(language.Language)],
                    language.EvidenceRef,
                    evidenceOrdinals)
                // R1's closed selector vocabulary is ScopeSelectorState; "no Expression was observed
                // at all" is PublisherValueAbsent, not SelectorNotApplicable -- the state a missing
                // format or missing rights basis uses below. Collapsing all three into one state
                // would leave the R1 distinction living only on which axis happened to be named,
                // rather than on the wire.
                : PublisherAbsent(dispositions.RecordEvidenceRef, evidenceOrdinals),
            dispositions.FormatDisposition is { } format
                ? Present(
                    [FormatToken(format.Format)],
                    format.EvidenceRef,
                    evidenceOrdinals)
                : NotApplicable(profile, ScopeAxis.Body),
            dispositions.RightsDisposition is { } rights
                ? Present(
                    [ContentClassToken(rights.ContentClass)],
                    rights.EvidenceRef,
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
            Projection(
                profile,
                ScopeAxis.Record,
                ReduceRecord(dispositions.RecordForm),
                isBodyCandidate: false),
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
    /// The record axis for every discovered object: accepted, derived as a rule over the closed act
    /// form vocabulary rather than a constant.
    /// </summary>
    /// <remarks>
    /// <see cref="EuActForm"/>'s own doc comment is explicit that the twelve members are "closed at
    /// twelve, and the twelve sum exactly to the version headline, so a thirteenth is a scope change
    /// rather than an omission": the reviewed scope's act-form census already equals the full
    /// accepted set, and no EU disposition type (<see cref="EuChannelDisposition"/>,
    /// <see cref="EuLanguageBodyDisposition"/>, <see cref="EuFormatDisposition"/>,
    /// <see cref="EuRightsDisposition"/>, <see cref="EuRelationFamilyDisposition"/>) carries an
    /// act-form-level exclusion concept the way <see cref="EuChannelDisposition.PolicyFor"/> excludes
    /// a channel or <see cref="EuManifestationScope.FormatsThatCanNeverCarryABody"/> excludes a
    /// format. So the record axis is not a filtered subset the way body is: it is every member of the
    /// closed set, written out exactly once each so the rule is provable and a thirteenth form fails
    /// closed instead of silently inheriting an answer decided for a different one, the same
    /// discipline <see cref="RecordFormToken"/> already applies to the same vocabulary.
    /// </remarks>
    private static ScopeDisposition ReduceRecord(EuActForm form) => form switch
    {
        EuActForm.Directive => ScopeDisposition.AcceptedSelected,
        EuActForm.Regulation => ScopeDisposition.AcceptedSelected,
        EuActForm.DelegatedRegulation => ScopeDisposition.AcceptedSelected,
        EuActForm.ImplementingRegulation => ScopeDisposition.AcceptedSelected,
        EuActForm.Treaty => ScopeDisposition.AcceptedSelected,
        EuActForm.Corrigendum => ScopeDisposition.AcceptedSelected,
        EuActForm.DelegatedDirective => ScopeDisposition.AcceptedSelected,
        EuActForm.ImplementingDecision => ScopeDisposition.AcceptedSelected,
        EuActForm.Decision => ScopeDisposition.AcceptedSelected,
        EuActForm.DecisionEntscheid => ScopeDisposition.AcceptedSelected,
        EuActForm.ImplementingDirective => ScopeDisposition.AcceptedSelected,
        EuActForm.DelegatedDecision => ScopeDisposition.AcceptedSelected,
        _ => throw new ArgumentOutOfRangeException(
            nameof(form),
            form,
            "This act form carries no reviewed record disposition. The twelve accepted forms sum " +
            "exactly to the reviewed scope's version headline, so every member of the closed set " +
            "is accepted by construction and a thirteenth is a scope change to be re-reviewed, " +
            "never an inherited default."),
    };

    /// <summary>
    /// The body axis is the worst-wins join over four independently evaluated contributions --
    /// acquisition channel, held language, held format, and established rights basis -- under the
    /// total order D1-01 Candidate 5 R1 fixes for composing a concrete multi-axis request:
    /// <c>never_ingest &gt; point &gt; typed_quarantine &gt; accepted_selected</c>. Each contribution
    /// is computed independently of the other three, and the axis result is the worst of the four --
    /// never the first one an ordered check happens to reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was an ordered early-return chain before this file's refreeze, and the order
    /// contradicted the precedence stated above: an excluded channel returned <c>point</c> before an
    /// unreached format check could ever return <c>never_ingest</c> for a <c>print</c> manifestation,
    /// silently hiding the stronger exclusion behind the weaker one whenever both applied to the same
    /// object, confirmed at runtime. <see cref="ScopeDisposition"/>'s own declared member order
    /// (<c>accepted_selected &lt; typed_quarantine &lt; point &lt; never_ingest</c>) already matches
    /// the stated precedence, so the join below is a plain pairwise "worse wins" reduction over the
    /// four contributions, and every pair of contributions that can disagree is driven by a test.
    /// </para>
    /// <para>
    /// A format in <see cref="EuManifestationScope.FormatsThatCanNeverCarryABody"/> is the one input
    /// that reaches <see cref="ScopeDisposition.NeverIngest"/>, because that set is itself closed to
    /// a physical manifestation (print) that can never be read as digital text; every other
    /// body-not-admitted format, and a format not yet observed at all, is a typed gap pending a
    /// reviewed profile (<c>typed_quarantine</c>), not a permanent exclusion. An excluded acquisition
    /// channel, or an observed language expression whose body this scope does not hold, each cap
    /// their own contribution at <c>point</c>; a content class with no established reuse basis caps
    /// its contribution at <c>typed_quarantine</c>. Candidate 2's "Languages" and "Formats" sections
    /// and <see cref="EuRightsDisposition"/>'s own doc comment spell out each of these individually;
    /// this method's only job is to join the four rather than pick whichever the caller wrote first.
    /// </para>
    /// <para>
    /// A missing <see cref="EuScopeObjectDispositions.LanguageDisposition"/> is not the same fact as
    /// an observed one that is not a body candidate, and this method does not collapse them the way
    /// an earlier revision did. R1's closed selector vocabulary is
    /// <see cref="ScopeSelectorState"/>, and the language selector now publishes
    /// <see cref="ScopeSelectorState.PublisherValueAbsent"/> for "no Expression was observed at
    /// all" (see <see cref="BuildScopeInput"/>); the body join below reads that same fact as
    /// <c>typed_quarantine</c> -- a gap pending observation, the same answer a missing format
    /// gets from <see cref="FormatBodyContribution"/> -- and keeps distinct from <c>point</c>, which
    /// means an Expression was observed and this scope deliberately does not hold its body
    /// (<see cref="EuLanguageBodyState.BodyNotHeldPoint"/>). Folding the two into one <c>point</c>
    /// answer would silently claim every unobserved language expression as a reviewed exclusion
    /// rather than an open question.
    /// </para>
    /// </remarks>
    private static ScopeDisposition ReduceBody(EuScopeObjectDispositions dispositions) =>
        Worst(
            Worst(
                ChannelBodyContribution(dispositions.ChannelDisposition),
                LanguageBodyContribution(dispositions.LanguageDisposition)),
            Worst(
                FormatBodyContribution(dispositions.FormatDisposition),
                RightsBodyContribution(dispositions.RightsDisposition)));

    /// <summary>The channel's own contribution to the body join: point when excluded.</summary>
    private static ScopeDisposition ChannelBodyContribution(EuChannelDisposition channel) =>
        channel.MayGraduate() ? ScopeDisposition.AcceptedSelected : ScopeDisposition.Point;

    /// <summary>
    /// The language's own contribution to the body join. A missing disposition is published on the
    /// wire as <see cref="ScopeSelectorState.PublisherValueAbsent"/> (see
    /// <see cref="BuildScopeInput"/>) -- no Expression was observed at all -- and this join reads
    /// that as a typed gap (<c>typed_quarantine</c>), the same answer
    /// <see cref="FormatBodyContribution"/> gives a missing format; an observed expression whose
    /// body this scope does not hold is <c>point</c>, unchanged. The two are deliberately not the
    /// same outcome: one is an open question pending observation, the other is a reviewed exclusion.
    /// </summary>
    private static ScopeDisposition LanguageBodyContribution(EuLanguageBodyDisposition? language)
    {
        if (language is not { } value)
        {
            return ScopeDisposition.TypedQuarantine;
        }

        return value.CarriesBody() ? ScopeDisposition.AcceptedSelected : ScopeDisposition.Point;
    }

    /// <summary>
    /// The format's own contribution to the body join: never-ingest for a physical manifestation,
    /// typed-quarantine for a format not yet observed or observed but not admitted as a body source.
    /// </summary>
    private static ScopeDisposition FormatBodyContribution(EuFormatDisposition? format)
    {
        if (format is not { } value)
        {
            return ScopeDisposition.TypedQuarantine;
        }

        if (EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(value.Format))
        {
            return ScopeDisposition.NeverIngest;
        }

        return value.Admission == EuFormatBodyAdmission.BodyAdmitted
            ? ScopeDisposition.AcceptedSelected
            : ScopeDisposition.TypedQuarantine;
    }

    /// <summary>The rights basis's own contribution to the body join: typed-quarantine when absent.</summary>
    private static ScopeDisposition RightsBodyContribution(EuRightsDisposition? rights) =>
        rights is null ? ScopeDisposition.TypedQuarantine : ScopeDisposition.AcceptedSelected;

    /// <summary>
    /// The worse (higher-precedence) of two body contributions, under
    /// <c>ScopeDisposition</c>'s own declared order.
    /// </summary>
    private static ScopeDisposition Worst(ScopeDisposition left, ScopeDisposition right) =>
        left > right ? left : right;

    /// <summary>
    /// The relation axis resolves to <see cref="ScopeDisposition.Point"/> when an object carries no
    /// relation edges at all (a dossier or NIM record, for instance) -- <c>ScopeDisposition</c> has
    /// no member named "not applicable"; that word names a different, selector-level fact
    /// (<see cref="ScopeSelectorState.SelectorNotApplicable"/>) -- and otherwise collapses to
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
    /// The supporting-document axis resolves to <see cref="ScopeDisposition.Point"/> when an object
    /// carries no supporting content class: it is itself the legal text or its own metadata record,
    /// not a supporting document of anything, mirroring Luxembourg's own rule for the same case.
    /// Candidate 2's ACCEPTED explanatory row is exactly <see cref="EuContentClass.Summary"/>
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

    /// <summary>
    /// A selector whose value was sought and found absent by a complete observation --
    /// <see cref="ScopeSelectorState.PublisherValueAbsent"/>, R1's distinct state for "we looked and
    /// there was nothing", never to be confused with <see cref="NotApplicable"/>'s "this selector
    /// does not apply to this object at all".
    /// </summary>
    private static ScopeSelectorEvidence PublisherAbsent(
        SourceArtifactRef evidenceRef,
        IReadOnlyDictionary<SourceArtifactRef, int> evidenceOrdinals) => new(
        ScopeSelectorState.PublisherValueAbsent,
        [],
        ScopeSelectorEvidenceKind.CompleteObservationAbsence,
        evidenceOrdinals[evidenceRef],
        null,
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

    /// <summary>
    /// The wire token <see cref="EuChannelDisposition.PolicyFor"/>'s admission answer publishes,
    /// per <see cref="EuChannelAdmission"/>'s own <c>JsonStringEnumMemberName</c>. Hashed instead
    /// of the C# identifier so a rename of <see cref="EuChannelAdmission.Admitted"/> or
    /// <see cref="EuChannelAdmission.Excluded"/> that leaves the wire token untouched cannot change
    /// the published profile digest, and a wire-token change cannot hide behind an unchanged digest.
    /// </summary>
    private static string ChannelAdmissionToken(EuChannelAdmission admission) => admission switch
    {
        EuChannelAdmission.Admitted => "admitted",
        EuChannelAdmission.Excluded => "excluded",
        _ => throw new ArgumentOutOfRangeException(
            nameof(admission), admission, "Unknown EU channel admission."),
    };

    /// <summary>
    /// The wire token <see cref="EuRightsDisposition.BasisFor"/>'s reuse-basis answer publishes,
    /// per <see cref="EuReuseBasis"/>'s own <c>JsonStringEnumMemberName</c>, for the same
    /// rename-safety reason as <see cref="ChannelAdmissionToken"/>.
    /// </summary>
    private static string ReuseBasisToken(EuReuseBasis basis) => basis switch
    {
        EuReuseBasis.Cc0 => "cc0",
        EuReuseBasis.CcBy40 => "cc_by_4_0",
        EuReuseBasis.EurLexLegalNoticePermission => "eur_lex_legal_notice_permission",
        _ => throw new ArgumentOutOfRangeException(
            nameof(basis), basis, "Unknown EU reuse basis."),
    };

    /// <summary>
    /// The wire token <see cref="ScopeAxis"/> publishes, per its own <c>JsonStringEnumMemberName</c>.
    /// Hashed into <see cref="SelectorTableSha256"/> instead of the numeric ordinal so a future
    /// renumbering of the shared <c>scope/1</c> axis enum -- which changes nothing on the wire --
    /// cannot silently change the published selector-table identity.
    /// </summary>
    private static string ScopeAxisToken(ScopeAxis axis) => axis switch
    {
        ScopeAxis.Record => "record",
        ScopeAxis.Body => "body",
        ScopeAxis.Relation => "relation",
        ScopeAxis.SupportingDocument => "supporting_document",
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown scope axis."),
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

    /// <summary>
    /// Computes <see cref="ProfileSha256"/> from the closed EU vocabularies this binding encodes,
    /// folding in the accepted scope-manifest candidate digest.
    /// </summary>
    /// <remarks>
    /// Internal, not private, purely so a test can call it with a deliberately different candidate
    /// digest or a deliberately mutated vocabulary list and observe that the output changes: proof
    /// this is a real digest of its stated inputs rather than a placeholder value that happens to
    /// look like one. Production always calls it with the full closed vocabularies, so a test
    /// varying the input is exercising a capability the production call site does not use, not a
    /// caller-choice a real object could exploit.
    /// </remarks>
    internal static string ComputeProfileSha256(
        IReadOnlyList<EuActForm> actForms,
        IReadOnlyList<EuChannel> channels,
        IReadOnlyList<EuOfficialLanguage> languages,
        IReadOnlyList<EuManifestationFormat> formats,
        IReadOnlyList<EuContentClass> contentClasses,
        IReadOnlyList<EuRelationFamily> relationFamilies,
        string candidate4Sha256)
    {
        ArgumentNullException.ThrowIfNull(actForms);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(contentClasses);
        ArgumentNullException.ThrowIfNull(relationFamilies);
        SourceCoreValidation.RequireSha256(candidate4Sha256, nameof(candidate4Sha256));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-source-profile/1");
        Append(hash, candidate4Sha256);

        Append(hash, "act_forms");
        Append(hash, actForms.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var form in actForms)
        {
            Append(hash, "act_form");
            Append(hash, RecordFormToken(form));
        }

        Append(hash, "channels");
        Append(hash, channels.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var channel in channels)
        {
            Append(hash, "channel");
            Append(hash, ChannelToken(channel));
            Append(hash, ChannelAdmissionToken(EuChannelDisposition.PolicyFor(channel)));
        }

        Append(hash, "languages");
        Append(hash, languages.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var language in languages)
        {
            Append(hash, "language");
            Append(hash, LanguageToken(language));
            Append(
                hash,
                EuLanguageBodyDisposition.BodyCandidateLanguages.Contains(language)
                    ? "body_candidate"
                    : "body_not_held_point");
        }

        Append(hash, "formats");
        Append(hash, formats.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var format in formats)
        {
            Append(hash, "format");
            Append(hash, FormatToken(format));
            Append(
                hash,
                EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(format)
                    ? "never_carries_body"
                    : "may_carry_body");
        }

        Append(hash, "content_classes");
        Append(hash, contentClasses.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var contentClass in contentClasses)
        {
            Append(hash, "content_class");
            Append(hash, ContentClassToken(contentClass));
            Append(hash, ReuseBasisToken(EuRightsDisposition.BasisFor(contentClass)));
        }

        Append(hash, "relation_families");
        Append(hash, relationFamilies.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var family in relationFamilies)
        {
            Append(hash, "relation_family");
            Append(hash, RelationFamilyToken(family));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Computes <see cref="SelectorTableSha256"/> from this file's own closed selector and
    /// projection-rule keys. Internal for the same sensitivity-testing reason as
    /// <see cref="ComputeProfileSha256"/>.
    /// </summary>
    /// <remarks>
    /// The two sections are domain separated: each is preceded by its own literal section label
    /// and entry count, and each entry inside a section carries its own leaf label too. Without
    /// this, the two sections shared one undifferentiated byte stream through
    /// <see cref="Append"/>'s length-prefixed framing: a selector key that happened to equal a
    /// projection rule's axis token, followed by the rest of that rule's key, could hash identically
    /// to a shorter selector-key list plus a longer projection-rule list, so two different closed
    /// tables could publish one digest. The section label and count close that: a selector key can
    /// never be mistaken for the start of the projection-rules section, because the projection-rules
    /// section header only ever appears once, at the true boundary between the two.
    /// </remarks>
    internal static string ComputeSelectorTableSha256(
        IReadOnlyList<string> selectorKeys,
        IReadOnlyList<(ScopeAxis Axis, string Key)> projectionRules)
    {
        ArgumentNullException.ThrowIfNull(selectorKeys);
        ArgumentNullException.ThrowIfNull(projectionRules);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-scope-projection/1");

        Append(hash, "selector_keys");
        Append(hash, selectorKeys.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var selector in selectorKeys)
        {
            Append(hash, "selector_key");
            Append(hash, selector);
        }

        Append(hash, "projection_rules");
        Append(hash, projectionRules.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var rule in projectionRules)
        {
            Append(hash, "projection_rule");
            Append(hash, ScopeAxisToken(rule.Axis));
            Append(hash, rule.Key);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
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

    public IReadOnlyList<EuRelationFamilyDisposition> RelationDispositions { get; }

    public SourceArtifactRef RelationEvidenceRef { get; }

    public EuContentClass? SupportingContentClass { get; }

    public SourceArtifactRef SupportingEvidenceRef { get; }
}
