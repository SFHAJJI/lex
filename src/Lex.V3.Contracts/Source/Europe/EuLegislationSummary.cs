using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One EU Summary of Legislation record: EUR-Lex's own plain-language explanation of a legal act,
/// never the act itself. Stage 2 item E7, built on the already-merged Facts layer exactly as
/// <see cref="EuCaseLawLinkBinding"/> (E6) and <see cref="EuDateAxiomBinding"/> (E1) already are.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is, corrected before any code was written.</b> A LegisSum record is not explanatory
/// text minted alongside a case-law judgment. It is the CDM class <c>summary_legislation_eu</c>: a
/// plain-language summary of a legal act, one per summarized act, in up to 24 languages, published on
/// EUR-Lex and licensed CC BY 4.0. review/23-research-temporal.md section 7, line 88 gives the one
/// worked instance this lane has: "Summaries of EU legislation (PROVEN): class
/// <c>summary_legislation_eu</c>, <c>work_id_document legissum:310401_2</c>,
/// <c>summary_legislation_eu_summarizes_resource_legal</c> to the act, <c>_version 2.0.0</c>,
/// <c>_obsolete 0</c>, <c>_validated_by_institution</c> (JUST), <c>_drafted_in_language ENG</c>,
/// <c>_is_about_classification_summary</c> (class-sum-leg NAL), <c>work_date_document 2026-03-24</c>
/// (last revision), 24 language expressions, fmx4 and xhtml5," and states directly: "They are
/// explanatory, not law"; the legal notice licenses them under CC BY 4.0 (section 9). Every field
/// below is one of these proven values, typed. <c>work_date_document</c> and
/// <c>_is_about_classification_summary</c> are not carried here: the scope ruling's own field list
/// (SCOPE_RULING precision one) names the legissum identity, the summarized-act edge, language,
/// version, obsolete state, validating institution and the licence, and nothing wider; carrying the
/// two unrequested fields would be ceremony this record has no specified capability for.
/// </para>
/// <para>
/// <b>Why this is not a two-sided Facts relation, unlike <see cref="EuCaseLawLinkBinding"/>.</b> E6's
/// binding wraps a full <see cref="RelationFact"/>/<see cref="PublisherRelation"/> because both the
/// case and the resource_legal it interprets fit <see cref="OfficialIdentitySet"/> under the existing,
/// closed <see cref="FactsIdentifierFamily"/> set (CELEX, ECLI, ...). A legissum work id
/// (<c>legissum:310401_2</c>, also named at review/23 section 2, line 45) does not fit any of the
/// eight pinned families: it is not a CELEX (<see cref="OfficialIdentifier.ProfileOf"/> refuses it),
/// not an ELI, not an ECLI, and not the CELEX persistent-identifier alias
/// <see cref="FactsIdentifierFamily.CellarPsiUri"/> names either -- that family's own schema pattern
/// (<c>FactsSchemaHardener.CellarPsiPattern</c>) is narrowed to <c>.../resource/celex/...</c>
/// specifically, so a legissum PSI would fail it even if the enum name alone looked generic enough.
/// Adding a ninth family would touch <c>Facts/FactsVocabulary.cs</c>, which is out of this lane's path
/// claim (the only Facts-adjacent file this lane touches is <c>EuCaseLawLink.cs</c>, and only for its
/// three named fixes). So <see cref="WorkIdDocument"/> is carried as its own validated field, the same
/// way E1 carries <see cref="EuNalSchemeIdentity"/> beside a reused Facts type rather than forcing a
/// value Facts has no family for into one that does not genuinely fit. Only the summarized-act side of
/// the edge -- which does fit an EU CELEX identity -- is carried as a real
/// <see cref="OfficialIdentitySet"/> (<see cref="SummarizedAct"/>), reused directly.
/// </para>
/// <para>
/// <b>The licence, reused rather than invented.</b> <see cref="Licence"/> is never a constructor
/// parameter: it is computed exactly once, inside <see cref="Create"/>, as
/// <see cref="EuRightsDisposition.BasisFor(EuContentClass)"/> applied to
/// <see cref="EuContentClass.Summary"/> -- the already-reviewed EU rights matrix
/// (<c>EuManifestationScope.cs</c>) that already answers "summaries ... under CC BY 4.0" from the
/// reviewed EUR-Lex legal notice (review/23 section 9), and already carries
/// <see cref="EuContentClass.Summary"/> as one of its five closed content classes, mapped to
/// <see cref="EuReuseBasis.CcBy40"/>. This mirrors <see cref="EuDateAxiomBinding"/>'s own "single role
/// home" precision: a property computed from an already-reviewed source is never also a parameter a
/// caller could set inconsistently with it, and no second, parallel licence vocabulary is invented
/// here.
/// </para>
/// <para>
/// <b>The predicate, pinned the same way <see cref="EuCaseLawPredicateVocabulary"/> pins its own
/// two.</b> <see cref="EuLegislationSummaryPredicateVocabulary"/> holds the one real, worked-instance
/// CDM predicate review/23 evidences for this edge:
/// <c>summary_legislation_eu_summarizes_resource_legal</c>, named in the predicate list at section 3,
/// line 54, and instantiated at section 7, line 88. No second predicate is pinned: review/23 names no
/// other predicate connecting a <c>summary_legislation_eu</c> work to anything.
/// </para>
/// <para>
/// <b><see cref="SummarizedActBodyScope"/>, reused exactly as E6 reuses <see cref="TargetBodyScope"/>.
/// </b> It names the summarized act's own body -- whether Lex holds the GDPR's text, say -- never the
/// summary's own explanatory text. Unlike E6, this lane declares no restriction pairing a particular
/// <see cref="TargetBodyScope"/> value with a refusal: the summarized act is always the ordinary,
/// non-explanatory side of this edge, so every scope value stays legitimate here (there is no "case at
/// the target" style asymmetry to guard against, because the explanatory side of this edge -- the
/// summary itself -- never sits where <see cref="TargetBodyScope"/> looks).
/// </para>
/// <para>
/// <b>Explanatory, not law: proven structurally, not by convention.</b> SCOPE_RULING precision three
/// asks for a real exclusion, not a comment. <c>EuFactsEvidenceBundle</c>, declared in
/// <c>EuLegislationSummaryTests.cs</c> rather than here (see below for why), is that exclusion: a
/// closed bundle admitting only E1's own <see cref="EuDateAxiomBinding"/> and E6's own
/// <see cref="EuCaseLawLinkBinding"/> (reused directly, not re-wrapped), through
/// <c>EuFactsEvidenceBundleItem</c>'s exactly two named factories. No factory taking a
/// <see cref="EuLegislationSummary"/> exists, and none can be added without editing
/// <c>EuFactsEvidenceBundleItem</c> itself, because its own constructor is <c>private</c> and
/// reachable only from its two nested variant classes. This codebase has no pre-existing type that
/// already aggregates a <see cref="PublisherDateFact"/>-based binding and a
/// <see cref="RelationFact"/>-based binding together (confirmed by grepping the full repository for
/// every consumer of both Facts types and every closed-variant registry, including
/// <c>FactsSchemaExporter.SchemaTypes</c> and <c>PreviewObjectSet</c>; none holds both), so this bundle
/// is minted for this one purpose, reusing E1's and E6's finished types as its two admitted members
/// rather than inventing a parallel vocabulary for either. Its own "accepts only closed registered
/// variants" shape mirrors <c>PreviewObjectSet</c> (<c>Contracts/PreviewPayload.cs</c>), the pattern
/// this codebase already uses for exactly this kind of proof.
/// </para>
/// <para>
/// <b>Why the bundle lives in the test project, not here.</b> A first version of
/// <c>EuFactsEvidenceBundleItem</c>/<c>EuFactsEvidenceBundle</c> lived in this file and held
/// <see cref="EuDateAxiomBinding"/>/<see cref="EuCaseLawLinkBinding"/> directly. That made both types
/// new producers of those two guarded types under <c>Lex.V3.Contracts.dll</c>'s own reflection scope,
/// which broke E1's and E6's own already-merged "no other type in the assembly holds or produces a
/// binding" construction-surface tests
/// (<c>EuDateAxiomTests.NoOtherTypeInTheAssemblyHoldsOrProducesABinding</c>,
/// <c>EuCaseLawLinkTests.NoOtherTypeInTheAssemblyHoldsOrProducesABinding</c>) -- exactly the kind of
/// collateral touch the scope ruling forbids, since fixing it would have meant editing
/// <c>EuCaseLawLinkTests.cs</c> beyond its three named items, or <c>EuDateAxiomTests.cs</c> at all.
/// The proof does not need to live in the shipped Contracts surface:
/// <c>ConstructionSurface.ProducersIn</c> scans one assembly at a time, so a bundle declared in the
/// test project can hold both binding types, prove the identical exclusion, and never appear in
/// either assembly-scoped scan.
/// </para>
/// </remarks>
public sealed class EuLegislationSummary
{
    private EuLegislationSummary(
        string workIdDocument,
        OfficialIdentitySet summarizedAct,
        string predicateUri,
        TargetBodyScope summarizedActBodyScope,
        string draftedInLanguage,
        string version,
        bool obsolete,
        string validatedByInstitution,
        EuReuseBasis licence,
        string sourceObservationId)
    {
        WorkIdDocument = workIdDocument;
        SummarizedAct = summarizedAct;
        PredicateUri = predicateUri;
        SummarizedActBodyScope = summarizedActBodyScope;
        DraftedInLanguage = draftedInLanguage;
        Version = version;
        Obsolete = obsolete;
        ValidatedByInstitution = validatedByInstitution;
        Licence = licence;
        SourceObservationId = sourceObservationId;
    }

    /// <summary>
    /// The legissum work's own identity exactly as the publisher states it (e.g.
    /// <c>legissum:310401_2</c>, review/23 section 7, line 88, also named at section 2, line 45).
    /// Never an <see cref="OfficialIdentitySet"/>: see the type remarks for why the closed
    /// <see cref="FactsIdentifierFamily"/> set does not fit this identity.
    /// </summary>
    public string WorkIdDocument { get; }

    /// <summary>
    /// The summarized act, the edge's real object, reused directly as a Facts
    /// <see cref="OfficialIdentitySet"/> (typically a CELEX identity, e.g. the GDPR's).
    /// </summary>
    public OfficialIdentitySet SummarizedAct { get; }

    /// <summary>
    /// The one pinned, review/23-evidenced predicate connecting this record to
    /// <see cref="SummarizedAct"/>. Always
    /// <see cref="EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri"/>: see
    /// <see cref="Create"/>.
    /// </summary>
    public string PredicateUri { get; }

    /// <summary>
    /// Whether <see cref="SummarizedAct"/>'s own body is held. Reused directly from Facts, and
    /// always about the summarized act, never about this record's own explanatory text. See the
    /// type remarks.
    /// </summary>
    public TargetBodyScope SummarizedActBodyScope { get; }

    /// <summary>
    /// <c>_drafted_in_language</c>, an ISO 639-3-shaped three-letter code exactly as the publisher
    /// states it (e.g. <c>ENG</c>, review/23 section 7, line 88).
    /// </summary>
    public string DraftedInLanguage { get; }

    /// <summary>
    /// <c>_version</c> exactly as the publisher states it (e.g. <c>2.0.0</c>, review/23 section 7,
    /// line 88).
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// <c>_obsolete</c>, the publisher's own flag (review/23 section 7, line 88: "<c>_obsolete 0</c>"),
    /// read as a boolean: <c>0</c> is <see langword="false"/>, <c>1</c> is <see langword="true"/>.
    /// </summary>
    public bool Obsolete { get; }

    /// <summary>
    /// <c>_validated_by_institution</c> exactly as the publisher states it (e.g. <c>JUST</c>,
    /// review/23 section 7, line 88). Kept as free text, the same discipline
    /// <c>CorpusBodyPendingAcquisitionReason.Refusal</c> already uses: review/23 proves exactly one
    /// value, not the closed vocabulary of every institution code the publisher might use, so no
    /// closed enum is invented for a set this lane has not observed.
    /// </summary>
    public string ValidatedByInstitution { get; }

    /// <summary>
    /// The licence this record's own explanatory text carries. Always
    /// <see cref="EuReuseBasis.CcBy40"/>: see the type remarks. Never a constructor parameter.
    /// </summary>
    public EuReuseBasis Licence { get; }

    /// <summary>The custody coordinate for the observation this record came from.</summary>
    public string SourceObservationId { get; }

    /// <summary>
    /// The only path that mints a record. <see cref="Licence"/> is computed here, never accepted as
    /// a parameter: see the type remarks.
    /// </summary>
    /// <param name="workIdDocument">
    /// The legissum work id, e.g. <c>legissum:310401_2</c>. Must start with the literal prefix
    /// <c>legissum:</c> followed by at least one further character, and be 1 to 200 printable ASCII
    /// characters with no leading or trailing space (the same opaque-identity discipline Facts
    /// applies to every publisher identifier).
    /// </param>
    /// <param name="summarizedAct">
    /// The act this record summarizes, exactly as the publisher asserted it. Must be an EU EUR-Lex
    /// identity: <c>summary_legislation_eu</c> is an EU-only CDM class, so a Luxembourg identity here
    /// would misstate what publisher this edge belongs to.
    /// </param>
    /// <param name="predicateUri">
    /// Must be <see cref="EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri"/>.
    /// Any other value, including a syntactically valid but unpinned absolute URI, is refused.
    /// </param>
    /// <param name="summarizedActBodyScope">Whether <paramref name="summarizedAct"/>'s own body is held.</param>
    /// <param name="draftedInLanguage">Three uppercase ASCII letters, e.g. <c>ENG</c>.</param>
    /// <param name="version">Three dot-separated non-negative integers, e.g. <c>2.0.0</c>.</param>
    /// <param name="obsolete">The publisher's own <c>_obsolete</c> flag, read as a boolean.</param>
    /// <param name="validatedByInstitution">
    /// The publisher's own <c>_validated_by_institution</c> code, e.g. <c>JUST</c>. 1 to 200
    /// printable ASCII characters with no leading or trailing space.
    /// </param>
    /// <param name="sourceObservationId">The custody coordinate for the observation this record came from.</param>
    public static EuLegislationSummary Create(
        string workIdDocument,
        OfficialIdentitySet summarizedAct,
        string predicateUri,
        TargetBodyScope summarizedActBodyScope,
        string draftedInLanguage,
        string version,
        bool obsolete,
        string validatedByInstitution,
        string sourceObservationId)
    {
        ArgumentNullException.ThrowIfNull(summarizedAct);

        if (!IsWorkIdDocument(workIdDocument))
        {
            throw new ArgumentException(
                "A legislation summary's work id must be \"legissum:\" followed by at least one " +
                "further printable ASCII character, with no leading or trailing space.",
                nameof(workIdDocument));
        }

        if (summarizedAct.Publisher != PublisherId.EuEurLex)
        {
            throw new ArgumentException(
                "summary_legislation_eu is an EU-only CDM class, so the summarized act must be an " +
                "EU EUR-Lex identity.",
                nameof(summarizedAct));
        }

        if (predicateUri is null ||
            !EuLegislationSummaryPredicateVocabulary.Pinned.Contains(predicateUri))
        {
            throw new ArgumentException(
                $"\"{predicateUri}\" is not one of the pinned, review/23-evidenced EU legislation " +
                "summary predicates.",
                nameof(predicateUri));
        }

        if (!Enum.IsDefined(summarizedActBodyScope))
        {
            throw new ArgumentException(
                $"{summarizedActBodyScope} is not a declared {nameof(TargetBodyScope)} member.",
                nameof(summarizedActBodyScope));
        }

        if (!IsThreeUppercaseLetters(draftedInLanguage))
        {
            throw new ArgumentException(
                "The drafted-in language must be exactly three uppercase ASCII letters, e.g. \"ENG\".",
                nameof(draftedInLanguage));
        }

        if (!IsThreePartVersion(version))
        {
            throw new ArgumentException(
                "The version must be three dot-separated non-negative integers, e.g. \"2.0.0\".",
                nameof(version));
        }

        if (!IsOpaqueIdentity(validatedByInstitution))
        {
            throw new ArgumentException(
                "The validating institution must be 1 to 200 printable ASCII characters, with no " +
                "leading or trailing space.",
                nameof(validatedByInstitution));
        }

        return new EuLegislationSummary(
            workIdDocument,
            summarizedAct,
            predicateUri,
            summarizedActBodyScope,
            draftedInLanguage,
            version,
            obsolete,
            validatedByInstitution,
            EuRightsDisposition.BasisFor(EuContentClass.Summary),
            SourceObservation.Require(sourceObservationId, nameof(sourceObservationId)));
    }

    private static bool IsWorkIdDocument(string? value) =>
        IsOpaqueIdentity(value) &&
        value!.StartsWith("legissum:", StringComparison.Ordinal) &&
        value.Length > "legissum:".Length;

    private static bool IsThreeUppercaseLetters(string? value) =>
        value is { Length: 3 } && value.All(static c => c is >= 'A' and <= 'Z');

    private static bool IsThreePartVersion(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var parts = value.Split('.');
        return parts.Length == 3 && parts.All(static part =>
            part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    /// <summary>
    /// 1 to 200 printable ASCII characters, with no leading or trailing space: the same opaque
    /// identity grammar <c>Facts.FactsValidation.IsOpaqueIdentity</c> enforces, restated here rather
    /// than reused across the internal boundary between this assembly's own Facts-internal helper
    /// and this file (both live in <c>Lex.V3.Contracts</c>, so the reuse would be legal, but the
    /// Facts-owned helper is intentionally left untouched and unreferenced by name here to keep this
    /// file's own dependency on Facts limited to the public types the type remarks already name).
    /// </summary>
    private static bool IsOpaqueIdentity(string? value)
    {
        if (value is null || value.Length is 0 or > 200)
        {
            return false;
        }

        if (value.Trim().Length != value.Length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The exact, closed set of real EU legislation-summary CDM predicates
/// review/23-research-temporal.md evidences with a worked instance, and the only predicate
/// <see cref="EuLegislationSummary.Create"/> accepts.
/// </summary>
public static class EuLegislationSummaryPredicateVocabulary
{
    /// <summary>
    /// Named in the CDM predicate list at review/23 section 3, line 54, and instantiated at section
    /// 7, line 88: "Summaries of EU legislation (PROVEN): class <c>summary_legislation_eu</c>, ...
    /// <c>summary_legislation_eu_summarizes_resource_legal</c> to the act, ...".
    /// </summary>
    public const string SummarizesResourceLegalPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "summary_legislation_eu_summarizes_resource_legal";

    internal static readonly IReadOnlyCollection<string> Pinned = new HashSet<string>(
        [SummarizesResourceLegalPredicateUri],
        StringComparer.Ordinal);
}

// The SCOPE_RULING precision three exclusion proof (EuFactsEvidenceBundleItem/EuFactsEvidenceBundle)
// lives in tests/Lex.V3.Tests/Contracts/Source/Europe/EuLegislationSummaryTests.cs, not here: see
// the type remarks above, "Why the bundle lives in the test project, not here."
