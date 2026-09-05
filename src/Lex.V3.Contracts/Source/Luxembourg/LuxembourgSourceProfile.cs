using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Luxembourg;

public sealed class VerifiedLuxembourgSourceProfile
{
    private const string PublisherOriginValue = "https://data.legilux.public.lu";
    private const string SparqlEndpointValue =
        "https://data.legilux.public.lu/sparqlendpoint";

    internal const string JoluxPrefix =
        "http://data.legilux.public.lu/resource/ontology/jolux#";
    internal const string TypeDocumentPrefix =
        "http://data.legilux.public.lu/resource/authority/resource-type/";
    internal const string UserFormatPrefix =
        "http://data.legilux.public.lu/resource/authority/user-format/";
    internal const string LegalValuePrefix =
        "http://data.legilux.public.lu/resource/authority/statut-version/";
    internal const string AdmittingLicence =
        "http://creativecommons.org/licenses/by/4.0/";
    internal const string NonAdmittingLicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    private const string LanguageAuthorityPrefix =
        "http://publications.europa.eu/resource/authority/language/";
    internal const string RdfType =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>
    /// R5.1's typeDocument assertion predicate, public (unlike <see cref="JoluxPrefix"/> above) so
    /// a caller outside this assembly can name it directly. D1-04's own adapter
    /// (<c>LuxembourgQueryExecutionAdapter.BuildCoarseDispositionMarkers</c>, in
    /// <c>Lex.V3.Ingest</c>) used to search <see cref="RequiredIriVocabulary"/> for the one
    /// <see cref="LuxembourgVocabularyKind.AssertionPredicate"/> value ending in "typeDocument"
    /// instead of naming this predicate directly; that search is gone.
    /// </summary>
    /// <remarks>
    /// D1-04c verified this claim rather than assuming it, and the earlier form of this remark was
    /// wrong: it said <c>Lex.V3.Ingest</c> "now reads this constant directly," naming
    /// <c>BuildCoarseDispositionMarkers</c> as that reader. D1-04c's own defect fix retired
    /// <c>BuildCoarseDispositionMarkers</c> outright (the coarse gap it filtered for was provably
    /// unreachable once <c>LuxembourgResourceResolution.TypedRole</c> existed), so that method is no
    /// longer merely a non-reader of this constant -- it does not exist at all. Nothing in
    /// <c>Lex.V3.Ingest</c> reads <see cref="TypeDocumentPredicateIri"/> today; this constant has no
    /// reader anywhere outside this file. <see cref="LuxembourgScopeResolver"/> (this same assembly,
    /// <c>LuxembourgScopeResolver.cs</c>, the private <c>TypeDocument</c> field) keeps its own
    /// independent <c>JoluxPrefix + "typeDocument"</c> duplicate rather than reading this constant --
    /// that duplicate remains the already-named gap item 18 (lane-w) tracks; unifying it is out of
    /// D1-04c's own path claim (<c>Ingest/Luxembourg</c> plus this file and
    /// <c>LuxembourgQueryPlan.cs</c>), not fixed here.
    /// </remarks>
    public const string TypeDocumentPredicateIri = JoluxPrefix + "typeDocument";

    /// <summary>
    /// <see cref="LuxembourgScopeResolver"/>'s three "priority candidate" typeDocument suffixes
    /// (its own <c>PriorityCandidateTypes</c> bucket): accepted through bucket membership only,
    /// never through a separately verified typed role (item 15 of the D1-04 design-synthesis
    /// ruling). Public, unlike that internal resolver field, so <c>LuxembourgQueryExecutionAdapter</c>
    /// in <c>Lex.V3.Ingest</c> reads the same three literals instead of declaring its own copy of
    /// "TC", "RECT" and "ACC" in a second switch across the assembly boundary.
    /// </summary>
    public const string PriorityCandidateTypeTc = "TC";

    /// <summary>See <see cref="PriorityCandidateTypeTc"/>.</summary>
    public const string PriorityCandidateTypeRect = "RECT";

    /// <summary>See <see cref="PriorityCandidateTypeTc"/>.</summary>
    public const string PriorityCandidateTypeAcc = "ACC";

    // Candidate 6 is the base policy; Decision 65 is bound by the exact canonical predicate rows.
    private const string Candidate6Sha256 =
        "a8e4fc0159127e8a7102f1cc51c76daf617224e1515d1c8d8c92bbb882c9ded9";
    private const string ProfileResourceId =
        "urn:uuid:19191414-0517-46fb-b4e0-bc6231601c88";
    private const string SelectorTableResourceId =
        "urn:uuid:72fdaf8b-e367-43c5-8b34-e22a99bfdbe7";

    internal static readonly string[] SelectorKeys =
    {
        "selector.record",
        "selector.relation",
        "selector.supporting_document",
        "selector.publication_family",
        "selector.language",
        "selector.format",
        "selector.authenticity",
        "selector.body_join",
        "selector.rights_sparql",
        "selector.rights_in_file",
        "selector.transport_uri",
        "selector.transport_robots",
        "selector.transport_http",
    };

    private static readonly (ScopeAxis Axis, string Key)[] ProjectionRules =
    {
        (ScopeAxis.Record, "projection.record"),
        (ScopeAxis.Body, "projection.body"),
        (ScopeAxis.Relation, "projection.relation"),
        (ScopeAxis.SupportingDocument, "projection.supporting_document"),
    };

    private static readonly ReadOnlyCollection<LuxembourgIriVocabularyValue>
        RequiredVocabulary = Array.AsReadOnly(BuildRequiredVocabulary());
    private static readonly ReadOnlyCollection<LuxembourgRelationRule>
        SettledRelationRules = Array.AsReadOnly(BuildRelationRules());

    private readonly HashSet<VocabularyKey> _observedIriKeys;
    private readonly IReadOnlyDictionary<string, int> _memberOrdinals;

    private VerifiedLuxembourgSourceProfile(
        LuxembourgVocabularySnapshot snapshot,
        IReadOnlyList<LuxembourgIriVocabularyValue> observedIriVocabulary,
        IReadOnlyList<LuxembourgLiteralVocabularyValue> observedLiteralVocabulary,
        ScopeProfileBinding scopeBinding,
        IReadOnlyDictionary<string, int> memberOrdinals)
    {
        Snapshot = snapshot;
        ObservedIriVocabulary = observedIriVocabulary;
        ObservedLiteralVocabulary = observedLiteralVocabulary;
        ScopeBinding = scopeBinding;
        _memberOrdinals = memberOrdinals;
        _observedIriKeys = observedIriVocabulary
            .Select(static value => new VocabularyKey(value.Kind, value.FullIri))
            .ToHashSet();
    }

    public static IReadOnlyList<LuxembourgIriVocabularyValue> RequiredIriVocabulary =>
        RequiredVocabulary;

    public string PublisherOrigin => PublisherOriginValue;

    public string SparqlEndpoint => SparqlEndpointValue;

    public LuxembourgVocabularySnapshot Snapshot { get; }

    public ScopeProfileBinding ScopeBinding { get; }

    public IReadOnlyList<LuxembourgIriVocabularyValue> ObservedIriVocabulary { get; }

    public IReadOnlyList<LuxembourgLiteralVocabularyValue> ObservedLiteralVocabulary { get; }

    public IReadOnlyList<LuxembourgRelationRule> RelationRules => SettledRelationRules;

    /// <summary>
    /// Opens a snapshot as a verified profile, or refuses it with a typed whole-run failure that
    /// names which condition refused it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Residue R1. RULING lex-event-20260905T044206627Z-43bd39db4edb474c834cb2acd1e1e1ff, finding
    /// lex-event-20260904T215524557Z-7cb36f1f533c4318b978a4ff97c929d7. Both conditions below used
    /// to leave here as an untyped <see cref="ArgumentException"/> while
    /// <see cref="LuxembourgProfileResolutionFailureCode"/> carried a named member for each, so the
    /// vocabulary advertised coverage the code did not have: a reader saw two named codes for two
    /// real conditions and reasonably concluded those conditions were reported as refusals, when
    /// nothing constructed either member and no caller could classify what actually escaped.
    /// </para>
    /// <para>
    /// The untyped throws are gone rather than kept beside this door, because a typed refusal that
    /// shares a path with an untyped throw is the same defect one level up.
    /// </para>
    /// </remarks>
    /// <returns>The verified profile, or null with <paramref name="failure"/> set.</returns>
    public static VerifiedLuxembourgSourceProfile? TryOpen(
        LuxembourgVocabularySnapshot snapshot,
        out LuxembourgProfileResolutionFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var iriValues = TryCanonicalizeIriVocabulary(snapshot.IriValues, out var iriConflict);
        if (iriValues is null)
        {
            failure = new LuxembourgProfileResolutionFailure(
                LuxembourgProfileResolutionFailureCode.SelectorConflict, iriConflict!);
            return null;
        }

        var literalValues = TryCanonicalizeLiteralVocabulary(
            snapshot.LiteralValues, out var literalConflict);
        if (literalValues is null)
        {
            failure = new LuxembourgProfileResolutionFailure(
                LuxembourgProfileResolutionFailureCode.SelectorConflict, literalConflict!);
            return null;
        }

        var actual = iriValues
            .Select(static value => new VocabularyKey(value.Kind, value.FullIri))
            .ToHashSet();
        var missing = RequiredVocabulary
            .Select(static value => new VocabularyKey(value.Kind, value.FullIri))
            .Where(value => !actual.Contains(value))
            .OrderBy(static value => value.Kind)
            .ThenBy(
                static value => value.FullIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        if (missing.Length != 0)
        {
            failure = new LuxembourgProfileResolutionFailure(
                LuxembourgProfileResolutionFailureCode.IncompleteVocabulary,
                Subject(missing[0].Kind, missing[0].FullIri));
            return null;
        }

        var sourceProfileRef = new SourceArtifactRef(
            ProfileResourceId,
            ComputeProfileSha256(snapshot, iriValues, literalValues));
        var selectorTableRef = new SourceArtifactRef(
            SelectorTableResourceId,
            ComputeSelectorTableSha256());
        var (binding, ordinals) = BuildScopeBinding(sourceProfileRef, selectorTableRef);
        var canonicalSnapshot = new LuxembourgVocabularySnapshot(
            snapshot.ObservationRef,
            snapshot.CompleteEnumerationRef,
            iriValues,
            literalValues);
        failure = null;
        return new VerifiedLuxembourgSourceProfile(
            canonicalSnapshot,
            Array.AsReadOnly(iriValues),
            Array.AsReadOnly(literalValues),
            binding,
            ordinals);
    }

    /// <summary>
    /// The subject a whole-run vocabulary failure names: the kind and the value, so a reader can
    /// find the offending row rather than being told only that one exists. Never empty, because a
    /// failure subject must be a non-empty scalar string and a literal lexical value may be empty.
    /// </summary>
    private static string Subject(LuxembourgVocabularyKind kind, string value) =>
        value.Length == 0 ? kind.ToString() : kind + " " + value;

    /// <summary>
    /// Resolves scope over observations that can only be constructed from this run's own proven
    /// assertion-family enumeration. The parameter type IS the guarantee: see
    /// <see cref="LuxembourgProvenResourceObservations"/> for why this is a door rather than a
    /// condition the body join checks and names.
    /// </summary>
    public LuxembourgProfileResolution Resolve(
        LuxembourgProvenResourceObservations observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return LuxembourgScopeResolver.Resolve(this, observations.Observations);
    }

    public VerifiedScopeManifest ReduceScope(
        LuxembourgProfileResolution.Resolved resolution,
        IScopeReductionEvidenceResolver evidenceResolver) =>
        ReduceScope(resolution, evidenceResolver, new Dictionary<SourceObjectRef, ScopeManifestFetchAddress>());

    /// <summary>
    /// D1-06c-LU-2 item 1: the same reduction, with this run's own minted per-object fetch
    /// addresses projected onto the rows. An object with no entry keeps the typed absence
    /// ScopeObjectReductionInput already defaults to, so a run that mints nothing produces exactly
    /// the manifest the two-parameter overload produces.
    /// </summary>
    public VerifiedScopeManifest ReduceScope(
        LuxembourgProfileResolution.Resolved resolution,
        IScopeReductionEvidenceResolver evidenceResolver,
        IReadOnlyDictionary<SourceObjectRef, ScopeManifestFetchAddress> mintedFetchAddresses)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(evidenceResolver);
        ArgumentNullException.ThrowIfNull(mintedFetchAddresses);
        if (resolution.SourceProfileRef != ScopeBinding.SourceProfileRef ||
            resolution.CompleteEnumerationRef != Snapshot.CompleteEnumerationRef ||
            evidenceResolver.CompleteEnumerationRef != Snapshot.CompleteEnumerationRef)
        {
            throw new ArgumentException(
                "The Luxembourg resolution, profile, and complete-enumeration evidence must share one identity.",
                nameof(resolution));
        }

        // A plain loop rather than a Select: a lambda here would be a compiler-generated method
        // returning ScopeObjectReductionInput, which shows up as a new producer of that type in
        // EuScopeProfileTests' own exact ProducersIn pin under a name that moves whenever anything
        // else in this file does. The loop keeps the producer set unchanged.
        var inputs = new ScopeObjectReductionInput[resolution.ScopeInputs.Count];
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = resolution.ScopeInputs[index];
            inputs[index] = mintedFetchAddresses.TryGetValue(input.ObjectRef, out var address)
                ? new ScopeObjectReductionInput(
                    input.ObjectRef, input.Selectors, input.RuleEvaluations, address)
                : input;
        }

        return ScopeReducer.Reduce(
            ScopeBinding,
            resolution.OrderedEvidenceArtifacts,
            resolution.Resources.Select(static resource => resource.ObjectRef).ToArray(),
            inputs,
            evidenceResolver);
    }

    internal bool ContainsVocabulary(LuxembourgVocabularyKind kind, string fullIri) =>
        _observedIriKeys.Contains(new VocabularyKey(kind, fullIri));

    internal bool IsSettledVocabulary(LuxembourgVocabularyKind kind, string fullIri) =>
        RequiredVocabulary.Any(value =>
            value.Kind == kind && string.Equals(value.FullIri, fullIri, StringComparison.Ordinal));

    internal static bool IsBodyAdmittingLicence(string fullIri) =>
        string.Equals(fullIri, AdmittingLicence, StringComparison.Ordinal);

    internal int RuleOrdinal(ScopeAxis axis) => Array.FindIndex(
        ProjectionRules,
        candidate => candidate.Axis == axis);

    internal int MemberOrdinal(string memberKey) => _memberOrdinals[memberKey];

    /// <summary>
    /// Canonicalises the IRI vocabulary, or reports the first row that competes with an earlier one
    /// for the same selector position. Two rows sharing a kind and an IRI are two answers to one
    /// selector, which is the selector conflict.
    /// </summary>
    private static LuxembourgIriVocabularyValue[]? TryCanonicalizeIriVocabulary(
        IReadOnlyList<LuxembourgIriVocabularyValue> values,
        out string? conflictSubject)
    {
        var copy = LuxembourgSourceValidation.Copy(values, nameof(values)).ToArray();
        var seen = new HashSet<VocabularyKey>();
        foreach (var value in copy)
        {
            if (!seen.Add(new VocabularyKey(value.Kind, value.FullIri)))
            {
                conflictSubject = Subject(value.Kind, value.FullIri);
                return null;
            }
        }

        conflictSubject = null;
        return copy
            .OrderBy(static value => value.Kind)
            .ThenBy(
                static value => value.FullIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
    }

    /// <summary>
    /// Canonicalises the literal vocabulary, or reports the first row that competes with an earlier
    /// one for the same selector position, exactly as the IRI form does.
    /// </summary>
    private static LuxembourgLiteralVocabularyValue[]? TryCanonicalizeLiteralVocabulary(
        IReadOnlyList<LuxembourgLiteralVocabularyValue> values,
        out string? conflictSubject)
    {
        var copy = LuxembourgSourceValidation.Copy(values, nameof(values)).ToArray();
        var ordered = copy
            .OrderBy(static value => value.Kind)
            .ThenBy(
                static value => value.RawDatatypeIriOrEmpty,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static value => value.RawLanguageTagOrEmpty,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static value => value.RawLexicalValue,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1] == ordered[index])
            {
                conflictSubject = Subject(ordered[index].Kind, ordered[index].RawLexicalValue);
                return null;
            }
        }

        conflictSubject = null;
        return ordered;
    }

    private static (ScopeProfileBinding Binding, IReadOnlyDictionary<string, int> Ordinals)
        BuildScopeBinding(SourceArtifactRef profileRef, SourceArtifactRef selectorTableRef)
    {
        var members = SelectorKeys
            .Concat(ProjectionRules.Select(static rule => rule.Key))
            .Select(key => new SourceRegistryMemberRef(selectorTableRef, key))
            .Append(new SourceRegistryMemberRef(profileRef, "role.body_candidate"))
            .Append(new SourceRegistryMemberRef(profileRef, "cause.selector_conflict"))
            .OrderBy(
                static member => member.RegistryRef.ResourceId,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static member => member.RegistryRef.Sha256,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static member => member.MemberKey,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        var ordinals = members
            .Select((member, ordinal) => (member.MemberKey, ordinal))
            .ToDictionary(static value => value.MemberKey, static value => value.ordinal);
        var rules = ProjectionRules
            .Select((rule, ordinal) => new ScopeRuleBinding(
                rule.Axis,
                ordinals[rule.Key],
                ordinal))
            .ToArray();
        var binding = new ScopeProfileBinding(
            profileRef,
            selectorTableRef,
            members,
            SelectorKeys.Select(key => ordinals[key]).ToArray(),
            rules,
            ordinals["role.body_candidate"]);
        return (binding, new ReadOnlyDictionary<string, int>(ordinals));
    }

    private static string ComputeProfileSha256(
        LuxembourgVocabularySnapshot snapshot,
        IReadOnlyList<LuxembourgIriVocabularyValue> iriValues,
        IReadOnlyList<LuxembourgLiteralVocabularyValue> literalValues)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-source-profile/4");
        Append(hash, Candidate6Sha256);
        AppendArtifact(hash, snapshot.ObservationRef);
        AppendArtifact(hash, snapshot.CompleteEnumerationRef);
        Append(hash, "iri_vocabulary_rows");
        Append(hash, iriValues.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var value in iriValues)
        {
            Append(hash, "iri_vocabulary_row");
            Append(hash, ((int)value.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, value.FullIri);
        }

        Append(hash, "literal_vocabulary_rows");
        Append(
            hash,
            literalValues.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var value in literalValues)
        {
            Append(hash, "literal_vocabulary_row");
            Append(hash, ((int)value.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, value.RawDatatypeIriOrEmpty);
            Append(hash, value.RawLanguageTagOrEmpty);
            Append(hash, value.RawLexicalValue);
            Append(hash, value.DatatypeIri);
            Append(hash, value.LanguageTag);
            Append(hash, ((int)value.Disposition).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, value.ReasonCode);
            Append(hash, value.CanonicalSelectorLexicalValue is null ? "absent" : "present");
            if (value.CanonicalSelectorLexicalValue is not null)
            {
                Append(hash, value.CanonicalSelectorLexicalValue);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string ComputeSelectorTableSha256()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-scope-projection/1");
        foreach (var selector in SelectorKeys)
        {
            Append(hash, selector);
        }

        foreach (var rule in ProjectionRules)
        {
            Append(hash, ((int)rule.Axis).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, rule.Key);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendArtifact(IncrementalHash hash, SourceArtifactRef value)
    {
        Append(hash, value.ResourceId);
        Append(hash, value.Sha256);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static LuxembourgIriVocabularyValue[] BuildRequiredVocabulary()
    {
        var values = new List<LuxembourgIriVocabularyValue>();
        Add(values, LuxembourgVocabularyKind.ResourceClass, JoluxPrefix,
            "Act", "Consolidation", "InitialDraft", "OpinionConseilEtat", "TreatyDocument",
            "EULegalResource", "EUDirective", "EUReglementation", "Work", "LegalResource",
            "ComplexWork", "Manifestation", "Expression", "Article", "Collection", "Code",
            "Memorial", "LegalResourceImpact", "DraftDocument", "Amendment",
            "OpinionProfessionalOrganisation", "DraftRelatedDocument", "TreatyProcess",
            "TreatySignature", "TaskForTreaty", "PartyConditionToTreaty",
            "TransmissionOfSignedInstrument", "RatificationRestriction");
        values.Add(new LuxembourgIriVocabularyValue(
            LuxembourgVocabularyKind.ResourceClass,
            "http://www.w3.org/ns/prov#Entity"));

        values.Add(new LuxembourgIriVocabularyValue(
            LuxembourgVocabularyKind.AssertionPredicate,
            RdfType));
        Add(values, LuxembourgVocabularyKind.AssertionPredicate, JoluxPrefix,
            "dateApplicability", "dateDocument", "dateEndApplicability", "dateEntryInForce",
            "dateNoLongerInForce", "historicalLegalId", "inForceStatus", "isEmbodiedBy",
            "isExemplifiedBy", "isMemberOf", "isPartOf", "isRealizedBy", "language",
            "legalValue", "license", "previousIsExemplifiedBy", "publicationDate", "publisher",
            "responsibilityOf", "rights", "rightsHolder", "title", "titleShort", "typeDocument",
            "userFormat");

        Add(values, LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix,
            "TC", "RECT", "ACC", "ACCA", "RC", "DIV", "PA", "RECUEIL", "CODE_RECUEIL",
            "RCSF", "RBCL", "RILR", "A", "AGC", "AGD", "AMIN", "ARGD", "CODE",
            "Constitution", "CONV", "LOI", "ORD", "PROT", "REG", "RGC", "RGD", "RI",
            "RMIN", "ST");

        Add(values, LuxembourgVocabularyKind.UserFormat, UserFormatPrefix,
            "xml", "xml-akomantoso", "pdfa", "pdf", "html", "docx", "doc", "jpeg", "jpg",
            "xls", "xlsx", "xml-lux", "zip");

        Add(values, LuxembourgVocabularyKind.Language, LanguageAuthorityPrefix,
            "DEU", "ENG", "FRA", "LTZ");

        Add(values, LuxembourgVocabularyKind.LegalValue, LegalValuePrefix,
            "definitif", "non-officiel", "officiel");

        values.Add(new LuxembourgIriVocabularyValue(
            LuxembourgVocabularyKind.Licence,
            AdmittingLicence));
        values.Add(new LuxembourgIriVocabularyValue(
            LuxembourgVocabularyKind.Licence,
            NonAdmittingLicenceScl));

        Add(values, LuxembourgVocabularyKind.RelationPredicate, JoluxPrefix,
            "modifies", "repeals", "rectifies", "basedOn", "transposes", "modifiedTempBy",
            "hasIndirectImpact", "legalAnalysisHasLegalResourceImpact",
            "impactFromLegalResource", "impactToLegalResource", "impactToExpression",
            "legalResourceImpactHasDateEntryInForce", "legalResourceImpactHasType",
            "impactConsolidatedBy", "impactConsolidatedByExpression", "basicAct", "consolidates",
            "cites");

        return values
            .OrderBy(static value => value.Kind)
            .ThenBy(
                static value => value.FullIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
    }

    private static LuxembourgRelationRule[] BuildRelationRules() => RequiredVocabulary
        .Where(static value => value.Kind == LuxembourgVocabularyKind.RelationPredicate)
        .Select(static value => new LuxembourgRelationRule(
            value.FullIri,
            value.FullIri == JoluxPrefix + "cites"
                ? LuxembourgRelationSemantic.AssertedCitation
                : value.FullIri == JoluxPrefix + "consolidates"
                    ? LuxembourgRelationSemantic.ConsolidatesShapeRequired
                    : LuxembourgRelationSemantic.AssertedRelation))
        .ToArray();

    private static void Add(
        ICollection<LuxembourgIriVocabularyValue> values,
        LuxembourgVocabularyKind kind,
        string prefix,
        params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            values.Add(new LuxembourgIriVocabularyValue(kind, prefix + suffix));
        }
    }

    private readonly record struct VocabularyKey(LuxembourgVocabularyKind Kind, string FullIri);
}
