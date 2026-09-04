using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Luxembourg;

internal static class LuxembourgScopeResolver
{
    private const string TypeDocument = VerifiedLuxembourgSourceProfile.JoluxPrefix + "typeDocument";
    private const string UserFormat = VerifiedLuxembourgSourceProfile.JoluxPrefix + "userFormat";
    private const string Language = VerifiedLuxembourgSourceProfile.JoluxPrefix + "language";
    private const string LegalValue = VerifiedLuxembourgSourceProfile.JoluxPrefix + "legalValue";
    private const string IsMemberOf = VerifiedLuxembourgSourceProfile.JoluxPrefix + "isMemberOf";
    private const string IsRealizedBy =
        VerifiedLuxembourgSourceProfile.JoluxPrefix + "isRealizedBy";
    private const string IsEmbodiedBy =
        VerifiedLuxembourgSourceProfile.JoluxPrefix + "isEmbodiedBy";
    private const string IsExemplifiedBy =
        VerifiedLuxembourgSourceProfile.JoluxPrefix + "isExemplifiedBy";
    private const string PreviousIsExemplifiedBy =
        VerifiedLuxembourgSourceProfile.JoluxPrefix + "previousIsExemplifiedBy";

    private static readonly HashSet<string> NeverTypes = Values("ACCA", "RC");
    private static readonly HashSet<string> QuarantinedTypes = Values("DIV", "PA");
    private static readonly HashSet<string> PointTypes = Values("RECUEIL", "CODE_RECUEIL");
    private static readonly HashSet<string> RegulatorTypes = Values("RCSF", "RBCL", "RILR");
    private static readonly HashSet<string> TcRectTypes = Values(
        VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc,
        VerifiedLuxembourgSourceProfile.PriorityCandidateTypeRect);
    private static readonly HashSet<string> AccTypes = Values(
        VerifiedLuxembourgSourceProfile.PriorityCandidateTypeAcc);
    private static readonly HashSet<string> PriorityCandidateTypes =
        TcRectTypes.Concat(AccTypes).ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> OrdinaryCandidateTypes = Values(
        "A", "AGC", "AGD", "AMIN",
        "ARGD", "CODE", "Constitution", "CONV", "LOI", "ORD", "PROT", "REG", "RGC",
        "RGD", "RI", "RMIN", "ST");
    private static readonly HashSet<string> AdmittedNonShelfTypes = PriorityCandidateTypes
        .Concat(OrdinaryCandidateTypes)
        .Concat(RegulatorTypes)
        .ToHashSet(StringComparer.Ordinal);
    // D1-06c-LU-2 repair, RULING lex-event-20260904T194556163Z-dd9191017eaf4c3b83ea04862933006f
    // item three: these three sets ARE this codebase's userFormat vocabulary. They were private,
    // so the document-fetch route grew a second, hand-written token list beside them, and the two
    // disagreed without anything noticing: "doc" was in this file all along and absent from that
    // list, because the census the list was written from joined on jolux:legalValue and every doc
    // manifestation lacks one. They are internal now so the selection side is cross-checked
    // against them by test rather than transcribed from a probe. Adding a token here without
    // teaching the route about it fails that test.
    internal static readonly HashSet<string> StructuredFormats = Formats("xml", "xml-akomantoso");
    internal static readonly HashSet<string> PointFormats = Formats("pdfa", "pdf", "html");
    internal static readonly HashSet<string> NeverFormats = Formats("doc", "docx", "svg");

    /// <summary>
    /// Every userFormat IRI any rule in this file classifies, in one sorted list. The single
    /// closed list the route's own admitted-token table is checked against.
    /// </summary>
    internal static IReadOnlyList<string> KnownUserFormatIris { get; } = StructuredFormats
        .Concat(PointFormats)
        .Concat(NeverFormats)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToArray();
    private static readonly HashSet<string> PointSupportClasses = Classes(
        "InitialDraft", "OpinionConseilEtat", "DraftDocument", "Amendment",
        "OpinionProfessionalOrganisation", "DraftRelatedDocument", "TreatyDocument",
        "TreatyProcess", "TreatySignature", "TaskForTreaty", "PartyConditionToTreaty",
        "TransmissionOfSignedInstrument", "RatificationRestriction", "EULegalResource",
        "EUDirective", "EUReglementation");
    private static readonly HashSet<string> MetadataSupportClasses = Classes(
        "Work", "LegalResource", "ComplexWork", "Manifestation", "Expression", "Article",
        "Collection", "Code", "Memorial", "LegalResourceImpact");

    /// <summary>
    /// The ordinal <see cref="BuildScopeInput"/>'s own <c>selectors</c> array assigns the
    /// publication-family selector, matching <see cref="VerifiedLuxembourgSourceProfile"/>'s
    /// "selector.publication_family" member-key position in the same fixed order. Item 18's
    /// SCOPE_RULING requires a test that needs this exact selector to locate it by this ordinal,
    /// never by recognising a value shape it expects to see, because
    /// <see cref="ScopeSelectorEvidence"/> carries no axis or dimension field a test could search
    /// by, and a value-shape search the resolver itself produced can never fail to be satisfied by
    /// that same resolver's output.
    /// </summary>
    internal const int PublicationFamilySelectorIndex = 3;

    internal static LuxembourgProfileResolution Resolve(
        VerifiedLuxembourgSourceProfile profile,
        IReadOnlyList<LuxembourgResourceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var copy = LuxembourgSourceValidation.Copy(observations, nameof(observations));
        var ordered = copy
            .OrderBy(static observation =>
                ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef),
                StringComparer.Ordinal)
            .ThenBy(
                static observation => observation.ObjectRef.PublisherUri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        var publisherUris = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            var observation = ordered[index];
            if (!publisherUris.Add(observation.ObjectRef.PublisherUri))
            {
                return Failure(
                    LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
                    observation.ObjectRef.PublisherUri);
            }

            var structuralFailure = ValidateObservation(profile, observation);
            if (structuralFailure is not null)
            {
                return new LuxembourgProfileResolution.Failed(structuralFailure);
            }
        }

        var observationsByPublisherUri = ordered.ToDictionary(
            static observation => observation.ObjectRef.PublisherUri,
            StringComparer.Ordinal);

        var classified = ordered
            .Select(observation =>
            {
                var relations = ResolveRelations(
                    profile,
                    observation,
                    observationsByPublisherUri);
                var wemiTopology = LuxembourgWemiTopology.Resolve(
                    observation.ObjectRef.PublisherUri,
                    observation.Assertions,
                    observation.ObservationRef);
                var bodyJoin = LuxembourgBodyJoin.Resolve(
                    observation.ObjectRef.PublisherUri,
                    observation.ObservationRef,
                    wemiTopology,
                    observation.SparqlRightsObservations,
                    observation.InFileRightsObservations);
                return new
                {
                    Observation = observation,
                    Dimensions = ResolveDimensions(
                        profile,
                        observation,
                        relations,
                        bodyJoin),
                    Assertions = ResolveAssertions(profile, observation, wemiTopology),
                    Relations = relations,
                    WemiTopology = wemiTopology,
                    BodyJoin = bodyJoin,
                    TypedRole = ResolveTypedRole(observation),
                };
            })
            .ToArray();
        var evidenceArtifacts = classified
            .SelectMany(static value => UsedEvidenceArtifacts(
                value.Observation,
                value.Dimensions))
            .Distinct()
            .OrderBy(
                static artifact => artifact.ResourceId,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static artifact => artifact.Sha256,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        var evidenceOrdinals = evidenceArtifacts
            .Select((artifact, ordinal) => (artifact, ordinal))
            .ToDictionary(static value => value.artifact, static value => value.ordinal);

        var resources = new LuxembourgResourceResolution[classified.Length];
        var scopeInputs = new ScopeObjectReductionInput[classified.Length];
        for (var ordinal = 0; ordinal < classified.Length; ordinal++)
        {
            var observation = classified[ordinal].Observation;
            var dimensions = classified[ordinal].Dimensions;
            var relations = classified[ordinal].Relations;
            resources[ordinal] = new LuxembourgResourceResolution(
                observation.ObjectRef,
                dimensions,
                classified[ordinal].Assertions,
                relations,
                classified[ordinal].WemiTopology,
                classified[ordinal].BodyJoin,
                classified[ordinal].TypedRole);
            scopeInputs[ordinal] = BuildScopeInput(
                profile,
                observation,
                dimensions,
                relations,
                classified[ordinal].WemiTopology,
                classified[ordinal].BodyJoin,
                evidenceOrdinals);
        }

        return new LuxembourgProfileResolution.Resolved(
            profile.ScopeBinding.SourceProfileRef,
            profile.Snapshot.CompleteEnumerationRef,
            evidenceArtifacts,
            scopeInputs,
            resources,
            BuildAccounting(resources));
    }

    private static IEnumerable<SourceArtifactRef> UsedEvidenceArtifacts(
        LuxembourgResourceObservation observation,
        LuScopeDimensions dimensions)
    {
        yield return observation.ObservationRef;
        if (dimensions.Rights.State != LuScopeTerminalState.NotApplicable)
        {
            yield return observation.SparqlRightsObservations.EnumerationRef;
            yield return observation.InFileRightsObservations.EnumerationRef;
        }
    }

    private static LuxembourgProfileResolutionFailure? ValidateObservation(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation)
    {
        if (observation.ObservationRef != profile.Snapshot.ObservationRef)
        {
            return NewFailure(
                LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
                observation.ObjectRef.PublisherUri);
        }

        if (observation.SparqlRightsObservations.RunIdentity != observation.ObservationRef ||
            observation.InFileRightsObservations.RunIdentity != observation.ObservationRef)
        {
            return NewFailure(
                LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
                observation.ObjectRef.PublisherUri);
        }

        if (observation.ObjectRef.Authority != SourceAuthority.Jolux ||
            !IsLuxembourgResourceIri(observation.ObjectRef.PublisherUri))
        {
            return NewFailure(
                LuxembourgProfileResolutionFailureCode.InvalidPublisherIri,
                observation.ObjectRef.PublisherUri);
        }

        if (observation.Assertions.Distinct().Count() != observation.Assertions.Count ||
            observation.Relations.Distinct().Count() != observation.Relations.Count)
        {
            return NewFailure(
                LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
                observation.ObjectRef.PublisherUri);
        }

        // An unruled licence IRI used to refuse the WHOLE RUN here as UnknownVocabularyDrift. That
        // is the D1-05d blast-radius shape: one odd licence on one manifestation anywhere in the
        // store could stop every Luxembourg run, and the pressure it created was to drop such rows
        // before they arrived, which loses the IRI silently. RULING
        // lex-event-20260904T204900861Z-6b737927d58a409dab05149aa28052e5: an unruled licence is
        // THAT OBJECT'S typed rights state instead. The channel carries the IRI, the resolution
        // reports TypedQuarantineUnruledLicence for it, that object's body is not admitted, and
        // every other object in the run is unaffected. Whole-run refusal stays for rows outside the
        // closure, which is a fact about the enumeration rather than about one publisher value.

        foreach (var assertion in observation.Assertions)
        {
            if (assertion.ObservationRef != observation.ObservationRef ||
                !TryExactIri(assertion.SubjectIri) ||
                !TryExactIri(assertion.PredicateIri))
            {
                return NewFailure(
                    LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
                    observation.ObjectRef.PublisherUri);
            }

            if (!profile.ContainsVocabulary(
                    LuxembourgVocabularyKind.AssertionPredicate,
                    assertion.PredicateIri))
            {
                return NewFailure(
                    LuxembourgProfileResolutionFailureCode.UnknownVocabularyDrift,
                    assertion.PredicateIri);
            }

            LuxembourgVocabularyKind? vocabularyKind = assertion.PredicateIri switch
            {
                VerifiedLuxembourgSourceProfile.RdfType =>
                    LuxembourgVocabularyKind.ResourceClass,
                TypeDocument => LuxembourgVocabularyKind.TypeDocument,
                UserFormat => LuxembourgVocabularyKind.UserFormat,
                Language => LuxembourgVocabularyKind.Language,
                LegalValue => LuxembourgVocabularyKind.LegalValue,
                _ => null,
            };
            if (assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri)
            {
                if (!LuxembourgSourceValidation.IsExactIriTerm(assertion))
                {
                    continue;
                }

                if (vocabularyKind is { } kind &&
                    !profile.ContainsVocabulary(kind, assertion.ObjectIriOrLexical))
                {
                    return NewFailure(
                        LuxembourgProfileResolutionFailureCode.UnknownVocabularyDrift,
                        assertion.ObjectIriOrLexical);
                }
            }
            else
            {
                _ = LuxembourgLiteralCanonicalizer.Canonicalize(
                    assertion.ObjectIriOrLexical,
                    assertion.DatatypeIriOrEmpty,
                    assertion.LanguageTagOrEmpty);
            }
        }

        foreach (var relation in observation.Relations)
        {
            if (relation.ObservationRef != observation.ObservationRef ||
                !string.Equals(
                    relation.SubjectIri,
                    observation.ObjectRef.PublisherUri,
                    StringComparison.Ordinal) ||
                !LuxembourgSourceValidation.IsExactResourceIri(relation.SubjectIri) ||
                !TryExactIri(relation.PredicateIri) ||
                !TryExactIri(relation.ObjectIri))
            {
                return NewFailure(
                    LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
                    observation.ObjectRef.PublisherUri);
            }

            if (!profile.ContainsVocabulary(
                    LuxembourgVocabularyKind.RelationPredicate,
                    relation.PredicateIri))
            {
                return NewFailure(
                    LuxembourgProfileResolutionFailureCode.UnknownVocabularyDrift,
                    relation.PredicateIri);
            }
        }

        return null;
    }

    private static LuScopeDimensions ResolveDimensions(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation,
        IReadOnlyList<LuxembourgResolvedRelation> resolvedRelations,
        LuxembourgBodyJoinResolution bodyJoin)
    {
        var resourceIri = observation.ObjectRef.PublisherUri;
        var classes = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            resourceIri);
        var types = IriValues(observation.Assertions, TypeDocument, resourceIri);
        var hasExpression = IsExactWemiDomain(classes, "Expression");
        var hasManifestation = IsExactWemiDomain(classes, "Manifestation");
        var formats = IriValues(observation.Assertions, UserFormat, resourceIri);
        var languages = IriValues(observation.Assertions, Language, resourceIri);
        var legalValues = IriValues(observation.Assertions, LegalValue, resourceIri);

        var record = classes.Length == 0
            ? Disposition(
                LuScopeTerminalState.MissingPublisherValue,
                "missing_resource_class",
                "lu_record_required_selector")
            : classes.Any(value => !profile.IsSettledVocabulary(
                LuxembourgVocabularyKind.ResourceClass,
                value))
                ? Disposition(
                    LuScopeTerminalState.TypedQuarantine,
                    "typed_quarantine_unruled_resource_class",
                    "lu_record_unruled_class",
                    observation.ObservationRef)
                : Disposition(
                    LuScopeTerminalState.AcceptedMetadata,
                    "accepted_bounded_metadata",
                    "lu_record_accepted_metadata",
                    observation.ObservationRef);

        var family = ResolvePublicationFamily(profile, observation, classes, types);
        var format = ResolveFormat(hasManifestation, formats, observation.ObservationRef);
        var language = ResolveLanguage(
            profile,
            hasExpression,
            languages,
            observation.ObservationRef);
        var authenticity = ResolveAuthenticity(
            profile,
            hasExpression || hasManifestation,
            legalValues,
            observation.ObservationRef);
        var rights = ResolveRights(hasManifestation, resourceIri, observation);
        var transport = ResolveTransport(
            hasManifestation || bodyJoin.Candidates.Count > 0,
            observation.ObservationRef);
        var body = ResolveBody(family, bodyJoin, observation);
        var relation = ResolveRelationDimension(resolvedRelations, observation.ObservationRef);
        var supportingDocument = ResolveSupportingDocument(classes, observation.ObservationRef);

        return new LuScopeDimensions(
            record,
            body,
            relation,
            supportingDocument,
            family,
            language,
            format,
            authenticity,
            rights,
            transport);
    }

    private static LuScopeDimensionDisposition ResolvePublicationFamily(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation,
        IReadOnlyList<string> classes,
        IReadOnlyList<string> types)
    {
        var evidence = observation.ObservationRef;
        if (types.Count == 0 && classes.Count == 0)
        {
            return Disposition(
                LuScopeTerminalState.MissingPublisherValue,
                "missing_publication_family",
                "lu_family_required_selector");
        }

        if (types.Count > 1)
        {
            return Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_selector_conflict",
                "lu_family_selector_conflict",
                evidence);
        }

        if (types.Count == 1)
        {
            var type = types[0];
            var state = NeverTypes.Contains(type)
                ? LuScopeTerminalState.NeverIngest
                : QuarantinedTypes.Contains(type) ||
                  !profile.IsSettledVocabulary(LuxembourgVocabularyKind.TypeDocument, type)
                    ? LuScopeTerminalState.TypedQuarantine
                    : PointTypes.Contains(type)
                        ? LuScopeTerminalState.Point
                        : PriorityCandidateTypes.Contains(type) &&
                          IsActClass(classes)
                            ? LuScopeTerminalState.AcceptedCandidate
                            : RegulatorTypes.Contains(type)
                                ? IsRegulatorQualified(observation, type)
                                    ? LuScopeTerminalState.AcceptedCandidate
                                    : LuScopeTerminalState.TypedQuarantine
                                : OrdinaryCandidateTypes.Contains(type) &&
                                  (IsConsolidationQualified(observation, type) ||
                                   IsAsPublishedOriginalQualified(observation, type))
                                    ? LuScopeTerminalState.AcceptedCandidate
                                    : LuScopeTerminalState.TypedQuarantine;
            return Disposition(
                state,
                state switch
                {
                    LuScopeTerminalState.AcceptedCandidate => "accepted_exact_family",
                    LuScopeTerminalState.Point => "point_exact_family",
                    LuScopeTerminalState.NeverIngest => "never_ingest_exact_family",
                    _ => "typed_quarantine_role_not_admitted",
                },
                "lu_family_exact_type",
                evidence);
        }

        // types is empty here (both branches above already ruled out zero and multiple), so an
        // Act class with no jolux:typeDocument assertion at all is a genuinely absent required
        // value, not an unrecognised one: R5.1 rule 11 (Candidate 5 line 613) reads this
        // publisher_value_absent with body typed_quarantine, naming the absent type. The old
        // catchall below said "unknown_publication_family", which claims a value was observed and
        // not admitted -- false when no typeDocument assertion exists at all. The resource's class
        // presence stays visible through its own evidence, the supporting-document selector.
        //
        // This branch is checked first and so displaces the two fallbacks immediately below it,
        // PointSupportClasses and MetadataSupportClasses (or prov#Entity): an Act class with no
        // typeDocument that also happens to carry a point-support or metadata-support class (for
        // example jolux:Work) still lands here as TypedQuarantine, never on their Point or
        // AcceptedMetadata outcomes. That displacement is deliberate -- the conservative reading
        // of rule 11 for an Act, where an absent typeDocument governs regardless of any other
        // WEMI-support class also present -- not an oversight of those two fallbacks.
        if (IsActClass(classes))
        {
            return Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_publication_type_absent",
                "lu_family_type_absent",
                evidence);
        }

        if (classes.Any(PointSupportClasses.Contains))
        {
            return Disposition(
                LuScopeTerminalState.Point,
                "point_support_family",
                "lu_family_support_point",
                evidence);
        }

        if (classes.Any(value =>
                MetadataSupportClasses.Contains(value) ||
                value == "http://www.w3.org/ns/prov#Entity"))
        {
            return Disposition(
                LuScopeTerminalState.AcceptedMetadata,
                "accepted_reachable_support_metadata",
                "lu_family_support_metadata",
                evidence);
        }

        return Disposition(
            LuScopeTerminalState.TypedQuarantine,
            "typed_quarantine_unknown_publication_family",
            "lu_family_catchall",
            evidence);
    }

    /// <summary>
    /// R5.1's own TC, RECT and ACC role, distinguished from bare <c>PriorityCandidateTypes</c>
    /// bucket membership. All three roles come directly, and only, from the publisher's own
    /// typeDocument assertion the resolver already reads (Candidate 6 section 4's own type-keyed
    /// family map): TC and RECT resolve here exactly as this lane's first freeze had them, and ACC
    /// resolves here exactly the same way per the reviewer RULING
    /// lex-event-20260904T002301246Z-7699c8fdd1ad4868a7d94dcb152fbf57. That ruling held that R5.1
    /// rule 6's own evidence for "this is a constitutional-review judgment" is the exact ACC
    /// typeDocument assertion itself -- already required to reach this branch via
    /// <see cref="AccTypes"/> below -- and that no further predicate is required or may substitute
    /// (a title, a relation, an alternate format), correcting this lane's own earlier reading of
    /// the 23:48Z SCOPE_RULING as requiring some other, undefined evidence predicate that
    /// unconditionally refused every ACC resource.
    /// </summary>
    private static LuxembourgTypedRoleResolution ResolveTypedRole(
        LuxembourgResourceObservation observation)
    {
        var resourceIri = observation.ObjectRef.PublisherUri;
        var classes = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            resourceIri);
        var types = IriValues(observation.Assertions, TypeDocument, resourceIri);
        if (types.Length != 1 || !IsActClass(classes))
        {
            return LuxembourgTypedRoleResolution.NotApplicableInstance;
        }

        var type = types[0];
        if (TcRectTypes.Contains(type))
        {
            return string.Equals(
                type,
                VerifiedLuxembourgSourceProfile.TypeDocumentPrefix +
                VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc,
                StringComparison.Ordinal)
                ? LuxembourgTypedRoleResolution.AcceptedCoordinatedText(resourceIri)
                : LuxembourgTypedRoleResolution.AcceptedCorrigendum(resourceIri);
        }

        if (AccTypes.Contains(type))
        {
            return LuxembourgTypedRoleResolution.AcceptedConstitutionalReviewDecision(resourceIri);
        }

        return LuxembourgTypedRoleResolution.NotApplicableInstance;
    }

    private static LuScopeDimensionDisposition ResolveFormat(
        bool hasManifestation,
        IReadOnlyList<string> formats,
        SourceArtifactRef evidence)
    {
        if (!hasManifestation)
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_manifestation",
                "lu_format_not_applicable");
        }

        if (formats.Count == 0)
        {
            return Disposition(
                LuScopeTerminalState.MissingPublisherValue,
                "missing_user_format",
                "lu_format_required_selector");
        }

        if (formats.Count > 1)
        {
            return Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_selector_conflict",
                "lu_format_selector_conflict",
                evidence);
        }

        var state = NeverFormats.Contains(formats[0])
            ? LuScopeTerminalState.NeverIngest
            : StructuredFormats.Contains(formats[0])
                ? LuScopeTerminalState.AcceptedCandidate
                : PointFormats.Contains(formats[0])
                    ? LuScopeTerminalState.Point
                    : LuScopeTerminalState.TypedQuarantine;

        return Disposition(
            state,
            state switch
            {
                LuScopeTerminalState.AcceptedCandidate => "accepted_structured_format",
                LuScopeTerminalState.Point => "point_link_or_locator_format",
                LuScopeTerminalState.NeverIngest => "never_ingest_format",
                _ => "typed_quarantine_unknown_format",
            },
            "lu_format_exact_value",
            evidence);
    }

    private static LuScopeDimensionDisposition ResolveLanguage(
        VerifiedLuxembourgSourceProfile profile,
        bool hasExpression,
        IReadOnlyList<string> languages,
        SourceArtifactRef evidence)
    {
        if (!hasExpression)
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_expression",
                "lu_language_not_applicable");
        }

        return languages.Count switch
        {
            0 => Disposition(
                LuScopeTerminalState.MissingPublisherValue,
                "missing_language",
                "lu_language_required_selector"),
            1 when profile.IsSettledVocabulary(
                LuxembourgVocabularyKind.Language,
                languages[0]) => Disposition(
                    LuScopeTerminalState.AcceptedCandidate,
                    "accepted_exact_language",
                    "lu_language_exact_value",
                    evidence),
            1 => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_unknown_language",
                "lu_language_unruled_value",
                evidence),
            _ => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_selector_conflict",
                "lu_language_selector_conflict",
                evidence),
        };
    }

    private static LuScopeDimensionDisposition ResolveAuthenticity(
        VerifiedLuxembourgSourceProfile profile,
        bool applicable,
        IReadOnlyList<string> legalValues,
        SourceArtifactRef evidence)
    {
        if (!applicable)
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_expression_or_manifestation",
                "lu_authenticity_not_applicable");
        }

        return legalValues.Count switch
        {
            0 => Disposition(
                LuScopeTerminalState.MissingPublisherValue,
                "missing_legal_value",
                "lu_authenticity_required_selector"),
            1 when profile.IsSettledVocabulary(
                LuxembourgVocabularyKind.LegalValue,
                legalValues[0]) => Disposition(
                    LuScopeTerminalState.AcceptedMetadata,
                    "accepted_exact_subject_legal_value",
                    "lu_authenticity_exact_subject",
                    evidence),
            1 => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_unknown_legal_value",
                "lu_authenticity_unruled_value",
                evidence),
            _ => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_selector_conflict",
                "lu_authenticity_selector_conflict",
                evidence),
        };
    }

    private static LuScopeDimensionDisposition ResolveRights(
        bool hasManifestation,
        string resourceIri,
        LuxembourgResourceObservation observation)
    {
        if (!hasManifestation)
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_manifestation",
                "lu_rights_not_applicable");
        }

        var rights = LuxembourgRightsChannels.Resolve(
            resourceIri,
            observation.ObservationRef,
            observation.SparqlRightsObservations,
            observation.InFileRightsObservations);
        var evidence = new[]
            {
                observation.SparqlRightsObservations.EnumerationRef,
                observation.InFileRightsObservations.EnumerationRef,
                rights.SparqlObservation?.EvidenceRef,
                rights.InFileObservation?.EvidenceRef,
            }
            .OfType<SourceArtifactRef>()
            .ToArray();
        return rights.Disposition switch
        {
            LuxembourgRightsChannelDisposition.AgreedSameRunCcBy => Disposition(
                LuScopeTerminalState.AcceptedMetadata,
                "accepted_observed_dual_channel_licence_agreement",
                "lu_rights_observed_channel_agreement",
                evidence),
            LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_rights_channel_enumeration_unproven",
                "lu_rights_enumeration_unproven",
                evidence),
            LuxembourgRightsChannelDisposition.MissingValue => Disposition(
                LuScopeTerminalState.MissingPublisherValue,
                "missing_rights_value",
                "lu_rights_observed_empty_channel",
                evidence),
            LuxembourgRightsChannelDisposition.Stale => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_stale_rights",
                "lu_rights_stale",
                evidence),
            LuxembourgRightsChannelDisposition.EvidenceNotIndependent => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_rights_evidence_not_independent",
                "lu_rights_evidence_independence",
                evidence),
            LuxembourgRightsChannelDisposition.Multiple => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_multiple_rights",
                "lu_rights_multiple",
                evidence),
            LuxembourgRightsChannelDisposition.Conflict => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_rights_conflict",
                "lu_rights_conflict",
                evidence),
            LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_non_admitting_rights",
                "lu_rights_non_admitting",
                evidence),
            LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence => Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_unruled_licence",
                "lu_rights_unruled_licence",
                evidence),
            _ => throw new InvalidOperationException("Unknown LU rights-channel disposition."),
        };
    }

    private static LuScopeDimensionDisposition ResolveTransport(
        bool hasManifestation,
        SourceArtifactRef observationRef)
    {
        if (!hasManifestation)
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_body_source",
                "lu_transport_not_applicable");
        }

        return Disposition(
            LuScopeTerminalState.TypedQuarantine,
            "typed_quarantine_manifestation_transport_unbound",
            "lu_transport_verified_manifestation_join_required",
            observationRef);
    }

    private static LuScopeDimensionDisposition ResolveBody(
        LuScopeDimensionDisposition family,
        LuxembourgBodyJoinResolution bodyJoin,
        LuxembourgResourceObservation observation)
    {
        if (family.State == LuScopeTerminalState.NeverIngest)
        {
            return CompositeDisposition(
                LuScopeTerminalState.NeverIngest,
                "never_ingest_body_gate",
                "lu_body_exact_denial",
                family);
        }

        if (family.State is LuScopeTerminalState.TypedQuarantine or
            LuScopeTerminalState.Point)
        {
            return CompositeDisposition(
                family.State,
                family.ReasonCode,
                "lu_body_publication_family",
                family);
        }

        if (family.State != LuScopeTerminalState.AcceptedCandidate)
        {
            return CompositeDisposition(
                LuScopeTerminalState.Point,
                "point_missing_body_family",
                "lu_body_missing_family",
                family);
        }

        if (bodyJoin.Candidates.Count == 0)
        {
            // The publisher's own listing reaches no manifestation at all for this root. That is
            // the unservable-listing reason, not an unknown.
            return Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_publisher_realization_path_unproven",
                "lu_body_exact_wemi_join",
                BodyJoinEvidenceArtifacts(observation));
        }

        // THE ACCEPTING ARM. Before this, every one of ResolveBody's five paths returned a
        // withholding state, so the Body axis was the only axis in this file with no accepting
        // path at all, and the accepted fraction of every real Luxembourg manifest was zero of N.
        // Owner principle, RULING lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0:
        // a law that can legitimately be ingested is ingested. A candidate reaches
        // AcceptedCandidate exactly when it carries no blocker, and the four blockers that remain
        // are the publisher's own facts (see LuxembourgBodyBlockerCode): an unservable listing, a
        // format we cannot compare text from, the publisher marking the object not reusable, plus
        // one structural guard against selecting another root's file. Everything the publisher
        // simply did not say is an unknown, recorded on the candidate's own rights resolution and
        // carried forward rather than used as a reason to hold nothing.
        if (bodyJoin.Candidates.Any(static candidate =>
                candidate.Disposition == LuxembourgBodyCandidateDisposition.AcceptedCandidate))
        {
            return Disposition(
                LuScopeTerminalState.AcceptedCandidate,
                "accepted_publisher_listed_wording_manifestation",
                "lu_body_accepted_wording_manifestation",
                BodyJoinEvidenceArtifacts(observation));
        }

        // Every candidate this root offers carries at least one of those four publisher facts.
        return Disposition(
            LuScopeTerminalState.TypedQuarantine,
            "typed_quarantine_no_admitted_body_candidate",
            "lu_body_no_admitted_candidate",
            BodyJoinEvidenceArtifacts(observation));
    }

    private static SourceArtifactRef[] BodyJoinEvidenceArtifacts(
        LuxembourgResourceObservation observation) =>
        new[]
        {
            observation.ObservationRef,
            observation.SparqlRightsObservations.EnumerationRef,
            observation.InFileRightsObservations.EnumerationRef,
        }
        .Concat(observation.SparqlRightsObservations.Observations
            .Select(static row => row.EvidenceRef))
        .Concat(observation.InFileRightsObservations.Observations
            .Select(static row => row.EvidenceRef))
        .Distinct()
        .OrderBy(
            static value => value.ResourceId,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
        .ToArray();

    private static LuScopeDimensionDisposition ResolveRelationDimension(
        IReadOnlyList<LuxembourgResolvedRelation> relations,
        SourceArtifactRef evidence)
    {
        if (relations.Count == 0)
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_assertion",
                "lu_relation_not_applicable");
        }

        return relations.Any(static relation =>
                relation.Disposition == LuxembourgRelationDisposition.TypedQuarantine)
            ? Disposition(
                LuScopeTerminalState.TypedQuarantine,
                "typed_quarantine_relation_shape_or_predicate",
                "lu_relation_closed_disposition",
                evidence)
            : Disposition(
                LuScopeTerminalState.AcceptedMetadata,
                "accepted_asserted_relation",
                "lu_relation_closed_disposition",
                evidence);
    }

    private static LuScopeDimensionDisposition ResolveSupportingDocument(
        IReadOnlyList<string> classes,
        SourceArtifactRef evidence)
    {
        if (classes.Count == 0 ||
            classes.Contains(VerifiedLuxembourgSourceProfile.JoluxPrefix + "Act") ||
            classes.Contains(VerifiedLuxembourgSourceProfile.JoluxPrefix + "Consolidation"))
        {
            return Disposition(
                LuScopeTerminalState.NotApplicable,
                "not_applicable_no_support",
                "lu_support_not_applicable");
        }

        var point = classes.Any(PointSupportClasses.Contains);
        var metadata = classes.Any(value =>
            MetadataSupportClasses.Contains(value) ||
            value == "http://www.w3.org/ns/prov#Entity");
        var unclassified = classes.Any(value =>
            !PointSupportClasses.Contains(value) &&
            !MetadataSupportClasses.Contains(value) &&
            value != "http://www.w3.org/ns/prov#Entity");
        if ((point && metadata) || unclassified)
        {
            return Disposition(
                LuScopeTerminalState.TypedQuarantine,
                unclassified
                    ? "typed_quarantine_unclassified_support"
                    : "typed_quarantine_support_selector_conflict",
                unclassified
                    ? "lu_support_unclassified"
                    : "lu_support_selector_conflict",
                evidence);
        }

        if (point)
        {
            return Disposition(
                LuScopeTerminalState.Point,
                "point_supporting_document",
                "lu_support_point",
                evidence);
        }

        if (metadata)
        {
            return Disposition(
                LuScopeTerminalState.AcceptedMetadata,
                "accepted_supporting_metadata",
                "lu_support_metadata",
                evidence);
        }

        return Disposition(
            LuScopeTerminalState.NotApplicable,
            "not_applicable_no_support",
            "lu_support_not_applicable");
    }

    private static IReadOnlyList<LuxembourgResolvedRelation> ResolveRelations(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation,
        IReadOnlyDictionary<string, LuxembourgResourceObservation> observationsByPublisherUri)
    {
        var rules = profile.RelationRules.ToDictionary(
            static rule => rule.PredicateIri,
            StringComparer.Ordinal);
        return observation.Relations
            .Select(relation =>
            {
                var settled = rules.TryGetValue(relation.PredicateIri, out var rule);
                var semantic = settled
                    ? rule!.Semantic
                    : LuxembourgRelationSemantic.AssertedRelation;
                var shape = semantic == LuxembourgRelationSemantic.ConsolidatesShapeRequired
                    ? ResolveConsolidatesShape(
                        profile,
                        observation,
                        relation,
                        observationsByPublisherUri)
                    : null;
                var accepted = settled &&
                               (shape is null ||
                                shape.State ==
                                LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct);
                return new LuxembourgResolvedRelation(
                    relation.SubjectIri,
                    relation.PredicateIri,
                    relation.ObjectIri,
                    relation.ObservationRef,
                    semantic,
                    accepted
                        ? LuxembourgRelationDisposition.Accepted
                        : LuxembourgRelationDisposition.TypedQuarantine,
                    shape);
            })
            .OrderBy(
                static relation => relation.SubjectIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static relation => relation.PredicateIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static relation => relation.ObjectIri,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static relation => relation.ObservationRef.ResourceId,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ThenBy(
                static relation => relation.ObservationRef.Sha256,
                LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
    }

    private static LuxembourgConsolidatesShape ResolveConsolidatesShape(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation source,
        LuxembourgObservedRelation relation,
        IReadOnlyDictionary<string, LuxembourgResourceObservation> observationsByPublisherUri)
    {
        var sourceClasses = IriValues(
            source.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            relation.SubjectIri);
        var sourceTypes = IriValues(source.Assertions, TypeDocument, relation.SubjectIri);
        var targetFound = observationsByPublisherUri.TryGetValue(
            relation.ObjectIri,
            out var target);
        var targetClasses = targetFound
            ? IriValues(
                target!.Assertions,
                VerifiedLuxembourgSourceProfile.RdfType,
                relation.ObjectIri)
            : [];
        var targetTypes = targetFound
            ? IriValues(target!.Assertions, TypeDocument, relation.ObjectIri)
            : [];
        var sourceCardinality = SelectorCardinality(sourceTypes.Length);
        var targetCardinality = SelectorCardinality(targetTypes.Length);

        var state = sourceClasses.Length == 0
            ? LuxembourgConsolidatesShapeState.TypedQuarantineSubjectClassMissing
            : !IsActClass(sourceClasses)
                ? LuxembourgConsolidatesShapeState.TypedQuarantineSubjectClassIncompatible
                : sourceCardinality == LuxembourgSelectorCardinality.Missing
                    ? LuxembourgConsolidatesShapeState.TypedQuarantineSubjectTypeMissing
                    : sourceCardinality == LuxembourgSelectorCardinality.Multiple
                        ? LuxembourgConsolidatesShapeState.TypedQuarantineSubjectTypeMultiple
                        : !string.Equals(
                            sourceTypes[0],
                            VerifiedLuxembourgSourceProfile.TypeDocumentPrefix +
                            VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc,
                            StringComparison.Ordinal)
                            ? LuxembourgConsolidatesShapeState.TypedQuarantineSubjectTypeNotTc
                            : !targetFound
                                ? LuxembourgConsolidatesShapeState
                                    .TypedQuarantineTargetResourceMissing
                                : targetClasses.Length == 0
                                    ? LuxembourgConsolidatesShapeState
                                        .TypedQuarantineTargetClassMissing
                                    : !IsActClass(targetClasses)
                                        ? LuxembourgConsolidatesShapeState
                                            .TypedQuarantineTargetClassIncompatible
                                        : targetCardinality == LuxembourgSelectorCardinality.Missing
                                            ? LuxembourgConsolidatesShapeState
                                                .TypedQuarantineTargetTypeMissing
                                            : targetCardinality ==
                                              LuxembourgSelectorCardinality.Multiple
                                                ? LuxembourgConsolidatesShapeState
                                                    .TypedQuarantineTargetTypeMultiple
                                                : !profile.IsSettledVocabulary(
                                                    LuxembourgVocabularyKind.TypeDocument,
                                                    targetTypes[0])
                                                    ? LuxembourgConsolidatesShapeState
                                                        .TypedQuarantineTargetTypeUnruled
                                                : IsCompatibleConsolidatesTarget(
                                                    target!,
                                                    targetTypes[0])
                                                    ? LuxembourgConsolidatesShapeState
                                                        .AcceptedTcToCompatibleAct
                                                    : LuxembourgConsolidatesShapeState
                                                        .TypedQuarantineTargetRoleIncompatible;

        return new LuxembourgConsolidatesShape(
            sourceClasses,
            sourceTypes,
            sourceCardinality,
            targetClasses,
            targetTypes,
            targetCardinality,
            LuxembourgConsolidatesDirection.AssertedSubjectToObject,
            state);
    }

    private static bool IsCompatibleConsolidatesTarget(
        LuxembourgResourceObservation target,
        string type) =>
        string.Equals(
            type,
            VerifiedLuxembourgSourceProfile.TypeDocumentPrefix +
            VerifiedLuxembourgSourceProfile.PriorityCandidateTypeTc,
            StringComparison.Ordinal) ||
        (OrdinaryCandidateTypes.Contains(type) && IsAsPublishedOriginalQualified(target, type)) ||
        (RegulatorTypes.Contains(type) && IsRegulatorQualified(target, type));

    private static LuxembourgSelectorCardinality SelectorCardinality(int count) => count switch
    {
        0 => LuxembourgSelectorCardinality.Missing,
        1 => LuxembourgSelectorCardinality.Single,
        _ => LuxembourgSelectorCardinality.Multiple,
    };

    private static IReadOnlyList<LuxembourgResolvedAssertion> ResolveAssertions(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation,
        LuxembourgWemiTopologyResolution wemiTopology)
    {
        var assertions = observation.Assertions;
        var classesBySubject = assertions
            .Where(static assertion =>
                LuxembourgSourceValidation.IsExactIriTerm(assertion) &&
                string.Equals(
                    assertion.PredicateIri,
                    VerifiedLuxembourgSourceProfile.RdfType,
                    StringComparison.Ordinal))
            .GroupBy(static assertion => assertion.SubjectIri, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static assertion => assertion.ObjectIriOrLexical)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(
                        static value => value,
                        LuxembourgSourceValidation.UnicodeScalarComparer)
                    .ToArray(),
                StringComparer.Ordinal);
        var admittedAuxiliaryOriginals = AdmittedAuxiliaryOriginals(
            observation,
            classesBySubject);

        return assertions
        .Select(assertion => new LuxembourgResolvedAssertion(
            assertion,
            IsAcceptedAssertion(
                profile,
                observation.ObjectRef.PublisherUri,
                assertion,
                classesBySubject,
                wemiTopology,
                admittedAuxiliaryOriginals)
                ? LuxembourgAssertionDisposition.Accepted
                : LuxembourgAssertionDisposition.TypedQuarantine))
        .OrderBy(
            static value => value.Assertion.SubjectIri,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(
            static value => value.Assertion.PredicateIri,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(static value => value.Assertion.ObjectKind)
        .ThenBy(
            static value => value.Assertion.ObjectIriOrLexical,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(
            static value => value.Assertion.DatatypeIriOrEmpty,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(
            static value => value.Assertion.LanguageTagOrEmpty,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(
            static value => value.Assertion.ObservationRef.ResourceId,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ThenBy(
            static value => value.Assertion.ObservationRef.Sha256,
            LuxembourgSourceValidation.UnicodeScalarComparer)
        .ToArray();
    }

    private static bool IsAcceptedAssertion(
        VerifiedLuxembourgSourceProfile profile,
        string rootIri,
        LuxembourgObservedAssertion assertion,
        IReadOnlyDictionary<string, string[]> classesBySubject,
        LuxembourgWemiTopologyResolution wemiTopology,
        IReadOnlySet<string> admittedAuxiliaryOriginals)
    {
        if (!profile.IsSettledVocabulary(
                LuxembourgVocabularyKind.AssertionPredicate,
                assertion.PredicateIri) ||
            !LuxembourgSourceValidation.IsExactIriTerm(assertion))
        {
            return false;
        }

        classesBySubject.TryGetValue(assertion.SubjectIri, out var subjectClasses);
        subjectClasses ??= [];
        var isRoot = string.Equals(assertion.SubjectIri, rootIri, StringComparison.Ordinal);
        var isAuxiliaryOriginal = admittedAuxiliaryOriginals.Contains(assertion.SubjectIri);
        var isReachableExpression = wemiTopology.ReachableExpressionIris.Contains(
            assertion.SubjectIri,
            StringComparer.Ordinal);
        var isReachableManifestation = wemiTopology.ReachableManifestationIris.Contains(
            assertion.SubjectIri,
            StringComparer.Ordinal);
        return assertion.PredicateIri switch
        {
            VerifiedLuxembourgSourceProfile.RdfType =>
                (isRoot || isAuxiliaryOriginal ||
                 isReachableExpression || isReachableManifestation) &&
                profile.IsSettledVocabulary(
                    LuxembourgVocabularyKind.ResourceClass,
                    assertion.ObjectIriOrLexical),
            TypeDocument => (isRoot || isAuxiliaryOriginal) &&
                IsExactWemiDomain(subjectClasses, "Act", "Consolidation") &&
                profile.IsSettledVocabulary(
                    LuxembourgVocabularyKind.TypeDocument,
                    assertion.ObjectIriOrLexical),
            IsMemberOf => (isRoot || isAuxiliaryOriginal) &&
                IsExactWemiDomain(subjectClasses, "Act", "Consolidation"),
            Language => isReachableExpression &&
                IsExactWemiDomain(subjectClasses, "Expression") &&
                profile.IsSettledVocabulary(
                    LuxembourgVocabularyKind.Language,
                    assertion.ObjectIriOrLexical),
            UserFormat => isReachableManifestation &&
                IsExactWemiDomain(subjectClasses, "Manifestation") &&
                profile.IsSettledVocabulary(
                    LuxembourgVocabularyKind.UserFormat,
                    assertion.ObjectIriOrLexical),
            LegalValue =>
                ((isReachableExpression && IsExactWemiDomain(subjectClasses, "Expression")) ||
                 (isReachableManifestation &&
                  IsExactWemiDomain(subjectClasses, "Manifestation"))) &&
                profile.IsSettledVocabulary(
                    LuxembourgVocabularyKind.LegalValue,
                    assertion.ObjectIriOrLexical),
            IsRealizedBy => isRoot &&
                IsExactWemiDomain(subjectClasses, "Act", "Consolidation") &&
                wemiTopology.ReachableExpressionIris.Contains(
                    assertion.ObjectIriOrLexical,
                    StringComparer.Ordinal),
            IsEmbodiedBy => isReachableExpression &&
                IsExactWemiDomain(subjectClasses, "Expression") &&
                wemiTopology.ReachableManifestationIris.Contains(
                    assertion.ObjectIriOrLexical,
                    StringComparer.Ordinal),
            IsExemplifiedBy => isReachableManifestation &&
                IsExactWemiDomain(subjectClasses, "Manifestation") &&
                LuxembourgItemUriFamily.IsCurrent(assertion.ObjectIriOrLexical),
            PreviousIsExemplifiedBy => isReachableManifestation &&
                IsExactWemiDomain(subjectClasses, "Manifestation") &&
                LuxembourgItemUriFamily.IsPrevious(assertion.ObjectIriOrLexical),
            _ => false,
        };
    }

    private static IReadOnlySet<string> AdmittedAuxiliaryOriginals(
        LuxembourgResourceObservation observation,
        IReadOnlyDictionary<string, string[]> classesBySubject)
    {
        var rootIri = observation.ObjectRef.PublisherUri;
        if (!classesBySubject.TryGetValue(rootIri, out var rootClasses) ||
            !IsExactWemiDomain(rootClasses, "Consolidation") ||
            !TryGetProvenOriginalActIri(observation, rootIri, out var originalIri))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>([originalIri], StringComparer.Ordinal);
    }

    private static bool TryGetProvenOriginalActIri(
        LuxembourgResourceObservation observation,
        string resourceIri,
        out string originalIri)
    {
        originalIri = string.Empty;
        var parents = IriValues(observation.Assertions, IsMemberOf, resourceIri);
        if (parents.Length != 1 ||
            !LuxembourgSourceValidation.IsExactResourceIri(parents[0]) ||
            parents[0].EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = parents[0] + "/jo";
        var originalClasses = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            candidate);
        var originalParents = IriValues(observation.Assertions, IsMemberOf, candidate);
        var originalTypes = IriValues(observation.Assertions, TypeDocument, candidate);
        if (!IsExactWemiDomain(originalClasses, "Act") ||
            !originalParents.SequenceEqual(parents) ||
            originalTypes.Length != 1 ||
            !AdmittedNonShelfTypes.Contains(originalTypes[0]))
        {
            return false;
        }

        originalIri = candidate;
        return true;
    }

    private static bool IsExactWemiDomain(
        IReadOnlyList<string> classes,
        params string[] allowedRoles)
    {
        var expected = allowedRoles
            .Select(static role => VerifiedLuxembourgSourceProfile.JoluxPrefix + role)
            .ToHashSet(StringComparer.Ordinal);
        var actualRoles = classes
            .Where(static value => value is
                VerifiedLuxembourgSourceProfile.JoluxPrefix + "Act" or
                VerifiedLuxembourgSourceProfile.JoluxPrefix + "Consolidation" or
                VerifiedLuxembourgSourceProfile.JoluxPrefix + "Expression" or
                VerifiedLuxembourgSourceProfile.JoluxPrefix + "Manifestation")
            .ToArray();
        return actualRoles.Length == 1 && expected.Contains(actualRoles[0]);
    }

    private static ScopeObjectReductionInput BuildScopeInput(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation,
        LuScopeDimensions dimensions,
        IReadOnlyList<LuxembourgResolvedRelation> relations,
        LuxembourgWemiTopologyResolution wemiTopology,
        LuxembourgBodyJoinResolution bodyJoin,
        IReadOnlyDictionary<SourceArtifactRef, int> evidenceOrdinals)
    {
        var classes = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            observation.ObjectRef.PublisherUri);
        var types = IriValues(
            observation.Assertions,
            TypeDocument,
            observation.ObjectRef.PublisherUri);
        var selectors = new[]
        {
            Selector(
                profile,
                ScopeAxis.Record,
                dimensions.Record,
                [observation.ObjectRef.PublisherUri, .. classes, .. types],
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Relation,
                dimensions.Relation,
                relations.Select(RelationDigest).ToArray(),
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.SupportingDocument,
                dimensions.SupportingDocument,
                classes,
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.PublicationFamily,
                types,
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Language,
                IriValues(
                    observation.Assertions,
                    Language,
                    observation.ObjectRef.PublisherUri),
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Format,
                IriValues(
                    observation.Assertions,
                    UserFormat,
                    observation.ObjectRef.PublisherUri),
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Authenticity,
                IriValues(
                    observation.Assertions,
                    LegalValue,
                    observation.ObjectRef.PublisherUri),
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Body,
                ["body_join_sha256:" + BodyJoinDigest(wemiTopology, bodyJoin)],
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Rights,
                [
                    $"enumeration:{observation.SparqlRightsObservations.EnumerationRef.Sha256}",
                    .. observation.SparqlRightsObservations.Observations
                        .SelectMany(static row => row.LicenceIris),
                ],
                observation.SparqlRightsObservations.EnumerationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Rights,
                [
                    $"enumeration:{observation.InFileRightsObservations.EnumerationRef.Sha256}",
                    .. observation.InFileRightsObservations.Observations
                        .SelectMany(static row => row.LicenceIris),
                ],
                observation.InFileRightsObservations.EnumerationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Transport,
                dimensions.Transport.State == LuScopeTerminalState.NotApplicable
                    ? []
                    : ["manifestation_transport_uri_unbound"],
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Transport,
                dimensions.Transport.State == LuScopeTerminalState.NotApplicable
                    ? []
                    : ["manifestation_robots_evidence_unbound"],
                observation.ObservationRef,
                evidenceOrdinals),
            Selector(
                profile,
                ScopeAxis.Body,
                dimensions.Transport,
                dimensions.Transport.State == LuScopeTerminalState.NotApplicable
                    ? []
                    : ["manifestation_http_observation_unbound"],
                observation.ObservationRef,
                evidenceOrdinals),
        };
        var evaluations = new[]
        {
            Projection(profile, ScopeAxis.Record, dimensions.Record),
            Projection(profile, ScopeAxis.Body, dimensions.Body),
            Projection(profile, ScopeAxis.Relation, dimensions.Relation),
            Projection(profile, ScopeAxis.SupportingDocument, dimensions.SupportingDocument),
        };
        return new ScopeObjectReductionInput(observation.ObjectRef, selectors, evaluations);
    }

    private static ScopeSelectorEvidence Selector(
        VerifiedLuxembourgSourceProfile profile,
        ScopeAxis projectionAxis,
        LuScopeDimensionDisposition dimension,
        IReadOnlyList<string> values,
        SourceArtifactRef evidenceRef,
        IReadOnlyDictionary<SourceArtifactRef, int> evidenceOrdinals)
    {
        var canonicalValues = values
            .Where(static value => value.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray();
        if (dimension.State == LuScopeTerminalState.NotApplicable)
        {
            return new ScopeSelectorEvidence(
                ScopeSelectorState.SelectorNotApplicable,
                [],
                null,
                null,
                profile.RuleOrdinal(projectionAxis),
                null);
        }

        if (dimension.State == LuScopeTerminalState.TypedQuarantine &&
            dimension.RuleId.EndsWith("_selector_conflict", StringComparison.Ordinal))
        {
            if (canonicalValues.Length < 2)
            {
                throw new InvalidOperationException(
                    $"Selector conflict rule '{dimension.RuleId}' retained {canonicalValues.Length} " +
                    "distinct values instead of at least two.");
            }

            return new ScopeSelectorEvidence(
                ScopeSelectorState.PublisherValueConflict,
                canonicalValues,
                ScopeSelectorEvidenceKind.ObservedConflictingValueSet,
                evidenceOrdinals[evidenceRef],
                null,
                profile.MemberOrdinal("cause.selector_conflict"));
        }

        if (canonicalValues.Length == 0)
        {
            return new ScopeSelectorEvidence(
                ScopeSelectorState.PublisherValueAbsent,
                [],
                ScopeSelectorEvidenceKind.CompleteObservationAbsence,
                evidenceOrdinals[evidenceRef],
                null,
                null);
        }

        return new ScopeSelectorEvidence(
            ScopeSelectorState.PublisherValuePresent,
            canonicalValues,
            ScopeSelectorEvidenceKind.ObservedValueSet,
            evidenceOrdinals[evidenceRef],
            null,
            null);
    }

    private static ScopeRuleEvaluation Projection(
        VerifiedLuxembourgSourceProfile profile,
        ScopeAxis axis,
        LuScopeDimensionDisposition dimension)
    {
        var disposition = Project(axis, dimension.State);
        var roles = axis == ScopeAxis.Body &&
                    dimension.State == LuScopeTerminalState.AcceptedCandidate
            ? new[] { profile.ScopeBinding.BodyCandidateRoleMemberOrdinal }
            : Array.Empty<int>();
        return new ScopeRuleEvaluation(
            profile.RuleOrdinal(axis),
            ScopeRuleEvaluationState.Matched,
            dimension.State == LuScopeTerminalState.NeverIngest
                ? ScopeRuleEffect.ExactDenial
                : ScopeRuleEffect.Positive,
            disposition,
            roles,
            []);
    }

    private static ScopeDisposition Project(ScopeAxis axis, LuScopeTerminalState state) => state switch
    {
        LuScopeTerminalState.NeverIngest => ScopeDisposition.NeverIngest,
        LuScopeTerminalState.TypedQuarantine or LuScopeTerminalState.MissingPublisherValue =>
            ScopeDisposition.TypedQuarantine,
        LuScopeTerminalState.Point or LuScopeTerminalState.NotApplicable => ScopeDisposition.Point,
        LuScopeTerminalState.AcceptedCandidate => ScopeDisposition.AcceptedSelected,
        LuScopeTerminalState.AcceptedMetadata when axis == ScopeAxis.Body => ScopeDisposition.Point,
        LuScopeTerminalState.AcceptedMetadata => ScopeDisposition.AcceptedSelected,
        _ => throw new InvalidOperationException("Unknown Luxembourg terminal-state projection."),
    };

    private static IReadOnlyList<LuxembourgDimensionAccounting> BuildAccounting(
        IReadOnlyList<LuxembourgResourceResolution> resources)
    {
        var result = new List<LuxembourgDimensionAccounting>(70);
        foreach (var dimension in Enum.GetValues<LuxembourgDimension>())
        {
            foreach (var state in Enum.GetValues<LuScopeTerminalState>())
            {
                result.Add(new LuxembourgDimensionAccounting(
                    dimension,
                    state,
                    resources.Select((resource, ordinal) => (resource, ordinal))
                        .Where(value => State(value.resource.Dimensions, dimension) == state)
                        .Select(static value => value.ordinal)
                        .ToArray()));
            }
        }

        return result;
    }

    private static LuScopeTerminalState State(
        LuScopeDimensions dimensions,
        LuxembourgDimension dimension) => dimension switch
        {
            LuxembourgDimension.Record => dimensions.Record.State,
            LuxembourgDimension.Body => dimensions.Body.State,
            LuxembourgDimension.Relation => dimensions.Relation.State,
            LuxembourgDimension.SupportingDocument => dimensions.SupportingDocument.State,
            LuxembourgDimension.PublicationFamily => dimensions.PublicationFamily.State,
            LuxembourgDimension.Language => dimensions.Language.State,
            LuxembourgDimension.Format => dimensions.Format.State,
            LuxembourgDimension.Authenticity => dimensions.Authenticity.State,
            LuxembourgDimension.Rights => dimensions.Rights.State,
            LuxembourgDimension.Transport => dimensions.Transport.State,
            _ => throw new InvalidOperationException("Unknown Luxembourg dimension."),
        };

    private static string[] IriValues(
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        string predicate,
        string? exactSubject = null) => assertions
        .Where(assertion =>
            LuxembourgSourceValidation.IsExactIriTerm(assertion) &&
            string.Equals(assertion.PredicateIri, predicate, StringComparison.Ordinal) &&
            (exactSubject is null ||
             string.Equals(assertion.SubjectIri, exactSubject, StringComparison.Ordinal)))
        .Select(static assertion => assertion.ObjectIriOrLexical)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static value => value, LuxembourgSourceValidation.UnicodeScalarComparer)
        .ToArray();

    private static LuScopeDimensionDisposition Disposition(
        LuScopeTerminalState state,
        string reason,
        string rule,
        params SourceArtifactRef[] evidence) => new(
        state,
        reason,
        rule,
        evidence.Select(static value => $"{value.ResourceId}#{value.Sha256}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray());

    private static LuScopeDimensionDisposition CompositeDisposition(
        LuScopeTerminalState state,
        string reason,
        string rule,
        params LuScopeDimensionDisposition[] dependencies) => new(
        state,
        reason,
        rule,
        dependencies
            .SelectMany(static value => value.EvidenceIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, LuxembourgSourceValidation.UnicodeScalarComparer)
            .ToArray());

    private static string RelationDigest(LuxembourgResolvedRelation relation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-relation-selector/2");
        AppendField(hash, "subject_iri", relation.SubjectIri);
        AppendField(hash, "predicate_iri", relation.PredicateIri);
        AppendField(hash, "object_iri", relation.ObjectIri);
        AppendArtifact(hash, "observation", relation.ObservationRef);
        AppendField(hash, "semantic", ((int)relation.Semantic).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        AppendField(hash, "disposition", ((int)relation.Disposition).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        AppendField(
            hash,
            "consolidates_shape_presence",
            relation.ConsolidatesShape is null ? "absent" : "present");
        if (relation.ConsolidatesShape is { } shape)
        {
            AppendList(hash, "shape_subject_classes", shape.SubjectClasses);
            AppendList(hash, "shape_subject_type_documents", shape.SubjectTypeDocuments);
            AppendField(hash, "shape_subject_type_cardinality", ((int)shape.SubjectTypeCardinality).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendList(hash, "shape_target_classes", shape.TargetClasses);
            AppendList(hash, "shape_target_type_documents", shape.TargetTypeDocuments);
            AppendField(hash, "shape_target_type_cardinality", ((int)shape.TargetTypeCardinality).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendField(hash, "shape_direction", ((int)shape.Direction).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendField(hash, "shape_state", ((int)shape.State).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string BodyJoinDigest(
        LuxembourgWemiTopologyResolution wemiTopology,
        LuxembourgBodyJoinResolution bodyJoin)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-body-join-selector/2");
        AppendField(hash, "root_iri", bodyJoin.RootIri);
        AppendArtifact(hash, "observation_run", bodyJoin.ObservationRunRef);
        AppendCount(hash, "root_blockers", bodyJoin.RootBlockerCodes.Count);
        foreach (var blocker in bodyJoin.RootBlockerCodes)
        {
            AppendField(hash, "root_blocker", ((int)blocker).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        AppendCount(hash, "wemi_blockers", bodyJoin.WemiBlockers.Count);
        foreach (var blocker in bodyJoin.WemiBlockers)
        {
            Append(hash, "wemi_blocker");
            AppendField(hash, "code", ((int)blocker.Code).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendField(hash, "subject_iri", blocker.SubjectIri);
            AppendField(hash, "predicate_iri", blocker.PredicateIri);
            AppendField(hash, "object_iri_or_empty", blocker.ObjectIriOrEmpty);
            AppendField(hash, "language_iri_or_empty", blocker.LanguageIriOrEmpty);
            AppendField(hash, "format_iri_or_empty", blocker.FormatIriOrEmpty);
        }

        AppendCount(hash, "previous_items", wemiTopology.PreviousItems.Count);
        foreach (var previous in wemiTopology.PreviousItems)
        {
            Append(hash, "previous_item");
            AppendField(hash, "manifestation_iri", previous.ManifestationIri);
            AppendField(hash, "item_iri", previous.ItemIri);
            AppendArtifact(hash, "observation", previous.ObservationRef);
            AppendField(hash, "disposition", ((int)previous.Disposition).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        AppendCount(hash, "body_candidates", bodyJoin.Candidates.Count);
        foreach (var candidate in bodyJoin.Candidates)
        {
            var wemi = candidate.WemiCandidate;
            Append(hash, "body_candidate");
            AppendField(hash, "root_iri", wemi.RootIri);
            AppendField(hash, "expression_iri", wemi.ExpressionIri);
            AppendField(hash, "manifestation_iri", wemi.ManifestationIri);
            AppendField(hash, "item_iri", wemi.ItemIri);
            AppendField(hash, "language_iri", wemi.LanguageIri);
            AppendField(hash, "format_iri", wemi.FormatIri);
            AppendArtifact(hash, "wemi_observation", wemi.ObservationRef);
            AppendField(hash, "wemi_disposition", ((int)wemi.Disposition).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendCount(hash, "wemi_candidate_blockers", wemi.BlockerCodes.Count);
            foreach (var blocker in wemi.BlockerCodes)
            {
                AppendField(hash, "wemi_candidate_blocker", ((int)blocker).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            AppendField(hash, "body_disposition", ((int)candidate.Disposition).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendCount(hash, "body_candidate_blockers", candidate.BlockerCodes.Count);
            foreach (var blocker in candidate.BlockerCodes)
            {
                AppendField(hash, "body_candidate_blocker", ((int)blocker).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            var rights = candidate.RightsResolution;
            AppendField(hash, "rights_disposition", ((int)rights.Disposition).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendArtifact(
                hash,
                "rights_sparql_enumeration",
                rights.SparqlObservations.EnumerationRef);
            AppendArtifact(
                hash,
                "rights_in_file_enumeration",
                rights.InFileObservations.EnumerationRef);
            AppendOptionalArtifact(
                hash,
                "rights_sparql_observation",
                rights.SparqlObservation?.EvidenceRef);
            AppendOptionalArtifact(
                hash,
                "rights_in_file_observation",
                rights.InFileObservation?.EvidenceRef);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendList(
        IncrementalHash hash,
        string field,
        IReadOnlyList<string> values)
    {
        AppendCount(hash, field, values.Count);
        foreach (var value in values)
        {
            AppendField(hash, field + "_item", value);
        }
    }

    private static void AppendCount(IncrementalHash hash, string field, int count) =>
        AppendField(
            hash,
            field + "_count",
            count.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void AppendOptionalArtifact(
        IncrementalHash hash,
        string field,
        SourceArtifactRef? value)
    {
        AppendField(hash, field + "_presence", value is null ? "absent" : "present");
        if (value is not null)
        {
            AppendArtifact(hash, field, value);
        }
    }

    private static void AppendArtifact(
        IncrementalHash hash,
        string field,
        SourceArtifactRef value)
    {
        AppendField(hash, field + "_resource_id", value.ResourceId);
        AppendField(hash, field + "_sha256", value.Sha256);
    }

    private static void AppendField(IncrementalHash hash, string field, string value)
    {
        Append(hash, field);
        Append(hash, value);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsLuxembourgResourceIri(string value) =>
        value.StartsWith("http://data.legilux.public.lu/", StringComparison.Ordinal) &&
        LuxembourgSourceValidation.IsExactResourceIri(value);

    private static bool TryExactIri(string value)
    {
        try
        {
            LuxembourgSourceValidation.RequireExactAbsoluteIri(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static LuxembourgProfileResolution.Failed Failure(
        LuxembourgProfileResolutionFailureCode code,
        string subject) => new(NewFailure(code, subject));

    private static LuxembourgProfileResolutionFailure NewFailure(
        LuxembourgProfileResolutionFailureCode code,
        string subject) => new(code, subject);

    private static HashSet<string> Values(params string[] suffixes) => suffixes
        .Select(static suffix => VerifiedLuxembourgSourceProfile.TypeDocumentPrefix + suffix)
        .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Formats(params string[] suffixes) => suffixes
        .Select(static suffix => VerifiedLuxembourgSourceProfile.UserFormatPrefix + suffix)
        .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Classes(params string[] suffixes) => suffixes
        .Select(static suffix => VerifiedLuxembourgSourceProfile.JoluxPrefix + suffix)
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsActClass(IReadOnlyList<string> classes) =>
        IsExactWemiDomain(classes, "Act");

    private static bool IsConsolidationQualified(
        LuxembourgResourceObservation observation,
        string type)
    {
        if (!OrdinaryCandidateTypes.Contains(type))
        {
            return false;
        }

        var exactClasses = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            observation.ObjectRef.PublisherUri);
        var exactTypes = IriValues(
            observation.Assertions,
            TypeDocument,
            observation.ObjectRef.PublisherUri);
        return IsExactWemiDomain(exactClasses, "Consolidation") &&
               exactTypes.SequenceEqual(new[] { type }) &&
               TryGetProvenOriginalActIri(
                   observation,
                   observation.ObjectRef.PublisherUri,
                   out _);
    }

    private static bool IsAsPublishedOriginalQualified(
        LuxembourgResourceObservation observation,
        string type)
    {
        if (!OrdinaryCandidateTypes.Contains(type))
        {
            return false;
        }

        var resourceIri = observation.ObjectRef.PublisherUri;
        var exactClasses = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            resourceIri);
        var exactTypes = IriValues(observation.Assertions, TypeDocument, resourceIri);
        return IsExactWemiDomain(exactClasses, "Act") &&
               exactTypes.SequenceEqual(new[] { type }) &&
               TryGetProvenOriginalActIri(observation, resourceIri, out var originalIri) &&
               string.Equals(resourceIri, originalIri, StringComparison.Ordinal);
    }

    private static bool IsRegulatorQualified(
        LuxembourgResourceObservation observation,
        string regulatorType)
    {
        if (!RegulatorTypes.Contains(regulatorType))
        {
            return false;
        }

        var resourceIri = observation.ObjectRef.PublisherUri;
        var exactClasses = IriValues(
            observation.Assertions,
            VerifiedLuxembourgSourceProfile.RdfType,
            resourceIri);
        var exactTypes = IriValues(observation.Assertions, TypeDocument, resourceIri);
        if (!IsExactWemiDomain(exactClasses, "Act"))
        {
            return false;
        }

        if (!exactTypes.SequenceEqual(new[] { regulatorType }))
        {
            return false;
        }

        return TryGetProvenOriginalActIri(observation, resourceIri, out var originalIri) &&
               string.Equals(resourceIri, originalIri, StringComparison.Ordinal);
    }
}
