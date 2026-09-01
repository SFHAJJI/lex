using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

public enum LuxembourgWemiCandidateDisposition
{
    StructurallyConsistent = 1,
    TypedQuarantine = 2,
}

public enum LuxembourgWemiBlockerCode
{
    ObservationMismatch = 1,
    RootTypeObjectInvalid = 2,
    RootTypeMissing = 3,
    RootTypeMismatch = 4,
    RootTypeConflict = 5,
    RealizationObjectInvalid = 6,
    RealizationMissing = 7,
    ExpressionTypeObjectInvalid = 8,
    ExpressionTypeMissing = 9,
    ExpressionTypeMismatch = 10,
    ExpressionTypeConflict = 11,
    ExpressionLanguageObjectInvalid = 12,
    ExpressionLanguageMissing = 13,
    EmbodimentObjectInvalid = 14,
    EmbodimentMissing = 15,
    ManifestationTypeObjectInvalid = 16,
    ManifestationTypeMissing = 17,
    ManifestationTypeMismatch = 18,
    ManifestationTypeConflict = 19,
    ManifestationFormatObjectInvalid = 20,
    ManifestationFormatMissing = 21,
    CoordinateConflict = 22,
    ExpressionLanguageConflict = 23,
    ManifestationFormatConflict = 24,

    ManifestationItemObjectInvalid = 25,
    ManifestationItemMissing = 26,
    ManifestationItemConflict = 27,
    ManifestationItemUriFamilyNotAdmitted = 28,
    PreviousItemObjectInvalid = 29,
}

public enum LuxembourgPreviousItemDisposition
{
    PointReplacedFile = 1,
    TypedQuarantineManifestationUnproven = 2,
    TypedQuarantineUnruledUriFamily = 3,
}

public sealed record LuxembourgPreviousItem
{
    internal LuxembourgPreviousItem(
        string manifestationIri,
        string itemIri,
        SourceArtifactRef observationRef,
        LuxembourgPreviousItemDisposition disposition)
    {
        ManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(
            manifestationIri,
            nameof(manifestationIri));
        ItemIri = LuxembourgSourceValidation.RequireExactResourceIri(itemIri, nameof(itemIri));
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
        Disposition = LuxembourgSourceValidation.RequireDefined(disposition, nameof(disposition));
        var isPreviousFamily = LuxembourgItemUriFamily.IsPrevious(ItemIri);
        if ((Disposition == LuxembourgPreviousItemDisposition.PointReplacedFile &&
             !isPreviousFamily) ||
            (Disposition == LuxembourgPreviousItemDisposition.TypedQuarantineUnruledUriFamily &&
             isPreviousFamily))
        {
            throw new ArgumentException(
                "The previous Item disposition must match its normalized publisher URI family.",
                nameof(disposition));
        }
    }

    public string ManifestationIri { get; }

    public string ItemIri { get; }

    public SourceArtifactRef ObservationRef { get; }

    public LuxembourgPreviousItemDisposition Disposition { get; }

    public string ReasonCode => Disposition switch
    {
        LuxembourgPreviousItemDisposition.PointReplacedFile =>
            "point_previous_item_file_family",
        LuxembourgPreviousItemDisposition.TypedQuarantineManifestationUnproven =>
            "typed_quarantine_previous_item_manifestation_unproven",
        LuxembourgPreviousItemDisposition.TypedQuarantineUnruledUriFamily =>
            "typed_quarantine_previous_item_uri_family_unruled",
        _ => throw new InvalidOperationException("Unknown previous Item disposition."),
    };
}

public sealed record LuxembourgWemiBlocker
{
    internal LuxembourgWemiBlocker(
        LuxembourgWemiBlockerCode code,
        string subjectIri,
        string predicateIri,
        string objectIriOrEmpty,
        string languageIriOrEmpty,
        string formatIriOrEmpty)
    {
        Code = LuxembourgSourceValidation.RequireDefined(code, nameof(code));
        SubjectIri = LuxembourgSourceValidation.RequireScalarString(subjectIri, nameof(subjectIri));
        PredicateIri = LuxembourgSourceValidation.RequireScalarString(
            predicateIri,
            nameof(predicateIri));
        ObjectIriOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            objectIriOrEmpty,
            nameof(objectIriOrEmpty));
        LanguageIriOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            languageIriOrEmpty,
            nameof(languageIriOrEmpty));
        FormatIriOrEmpty = LuxembourgSourceValidation.RequireScalarStringAllowEmpty(
            formatIriOrEmpty,
            nameof(formatIriOrEmpty));
    }

    public LuxembourgWemiBlockerCode Code { get; }

    public string SubjectIri { get; }

    public string PredicateIri { get; }

    public string ObjectIriOrEmpty { get; }

    public string LanguageIriOrEmpty { get; }

    public string FormatIriOrEmpty { get; }
}

public sealed record LuxembourgWemiCandidate
{
    internal LuxembourgWemiCandidate(
        string rootIri,
        string expressionIri,
        string manifestationIri,
        string itemIri,
        string languageIri,
        string formatIri,
        SourceArtifactRef observationRef,
        LuxembourgWemiCandidateDisposition disposition,
        IReadOnlyList<LuxembourgWemiBlockerCode> blockerCodes)
    {
        RootIri = LuxembourgSourceValidation.RequireExactResourceIri(rootIri, nameof(rootIri));
        ExpressionIri = LuxembourgSourceValidation.RequireExactResourceIri(
            expressionIri,
            nameof(expressionIri));
        ManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(
            manifestationIri,
            nameof(manifestationIri));
        ItemIri = LuxembourgSourceValidation.RequireExactResourceIri(itemIri, nameof(itemIri));
        LanguageIri = LuxembourgSourceValidation.RequireExactAbsoluteIri(
            languageIri,
            nameof(languageIri));
        FormatIri = LuxembourgSourceValidation.RequireExactAbsoluteIri(formatIri, nameof(formatIri));
        ObservationRef = observationRef ?? throw new ArgumentNullException(nameof(observationRef));
        Disposition = LuxembourgSourceValidation.RequireDefined(disposition, nameof(disposition));

        var codes = (blockerCodes ?? throw new ArgumentNullException(nameof(blockerCodes)))
            .Select(code => LuxembourgSourceValidation.RequireDefined(code, nameof(blockerCodes)))
            .Distinct()
            .Order()
            .ToArray();
        if ((codes.Length == 0) !=
            (Disposition == LuxembourgWemiCandidateDisposition.StructurallyConsistent))
        {
            throw new ArgumentException(
                "Only a blocker-free WEMI candidate may be structurally consistent.",
                nameof(disposition));
        }

        var isCurrentItem = LuxembourgItemUriFamily.IsCurrent(ItemIri);
        var hasItemFamilyBlocker = codes.Contains(
            LuxembourgWemiBlockerCode.ManifestationItemUriFamilyNotAdmitted);
        if (isCurrentItem == hasItemFamilyBlocker)
        {
            throw new ArgumentException(
                "The WEMI blocker set must match the normalized current Item URI family.",
                nameof(itemIri));
        }

        BlockerCodes = Array.AsReadOnly(codes);
    }

    public string RootIri { get; }

    public string ExpressionIri { get; }

    public string ManifestationIri { get; }

    public string ItemIri { get; }

    public string LanguageIri { get; }

    public string FormatIri { get; }

    public SourceArtifactRef ObservationRef { get; }

    public LuxembourgWemiCandidateDisposition Disposition { get; }

    public IReadOnlyList<LuxembourgWemiBlockerCode> BlockerCodes { get; }
}

internal static class LuxembourgItemUriFamily
{
    private const string Origin = "http://data.legilux.public.lu";
    private const string CurrentPathPrefix = "/filestore/";
    private const string PreviousPathPrefix = "/file/";

    internal static bool IsCurrent(string value) =>
        IsExactFamily(value, Origin + CurrentPathPrefix, CurrentPathPrefix);

    internal static bool IsPrevious(string value) =>
        IsExactFamily(value, Origin + PreviousPathPrefix, PreviousPathPrefix);

    private static bool IsExactFamily(
        string value,
        string rawPrefix,
        string normalizedPathPrefix)
    {
        try
        {
            LuxembourgSourceValidation.RequireExactResourceIri(value, nameof(value));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var parsed = new Uri(value, UriKind.Absolute);
        return value.StartsWith(rawPrefix, StringComparison.Ordinal) &&
               string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
               string.Equals(parsed.Host, "data.legilux.public.lu", StringComparison.Ordinal) &&
               parsed.AbsolutePath.StartsWith(normalizedPathPrefix, StringComparison.Ordinal) &&
               parsed.AbsolutePath.Length > normalizedPathPrefix.Length;
    }
}

public sealed record LuxembourgWemiTopologyResolution
{
    internal LuxembourgWemiTopologyResolution(
        IReadOnlyList<LuxembourgWemiCandidate> candidates,
        IReadOnlyList<LuxembourgWemiBlocker> blockers,
        IReadOnlyList<LuxembourgPreviousItem> previousItems,
        IReadOnlyList<string> reachableExpressionIris,
        IReadOnlyList<string> reachableManifestationIris)
    {
        Candidates = LuxembourgSourceValidation.Copy(candidates, nameof(candidates));
        Blockers = LuxembourgSourceValidation.Copy(blockers, nameof(blockers));
        PreviousItems = LuxembourgSourceValidation.Copy(
            previousItems,
            nameof(previousItems));
        ReachableExpressionIris = CopyReachableIris(
            reachableExpressionIris,
            nameof(reachableExpressionIris));
        ReachableManifestationIris = CopyReachableIris(
            reachableManifestationIris,
            nameof(reachableManifestationIris));
    }

    public IReadOnlyList<LuxembourgWemiCandidate> Candidates { get; }

    public IReadOnlyList<LuxembourgWemiBlocker> Blockers { get; }

    public IReadOnlyList<LuxembourgPreviousItem> PreviousItems { get; }

    internal IReadOnlyList<string> ReachableExpressionIris { get; }

    internal IReadOnlyList<string> ReachableManifestationIris { get; }

    private static IReadOnlyList<string> CopyReachableIris(
        IReadOnlyList<string> values,
        string parameterName)
    {
        var copy = LuxembourgSourceValidation.CopyStrings(values, parameterName);
        foreach (var value in copy)
        {
            LuxembourgSourceValidation.RequireExactResourceIri(value, parameterName);
        }

        return copy;
    }
}

public static class LuxembourgWemiTopology
{
    private const string RdfType =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string IsRealizedBy = Jolux + "isRealizedBy";
    private const string IsEmbodiedBy = Jolux + "isEmbodiedBy";
    private const string IsExemplifiedBy = Jolux + "isExemplifiedBy";
    private const string PreviousIsExemplifiedBy = Jolux + "previousIsExemplifiedBy";
    private const string Language = Jolux + "language";
    private const string UserFormat = Jolux + "userFormat";
    private const string Act = Jolux + "Act";
    private const string Consolidation = Jolux + "Consolidation";
    private const string Expression = Jolux + "Expression";
    private const string Manifestation = Jolux + "Manifestation";

    private static readonly HashSet<string> WemiTypes =
        new([Act, Consolidation, Expression, Manifestation], StringComparer.Ordinal);

    public static LuxembourgWemiTopologyResolution Resolve(
        string rootIri,
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        SourceArtifactRef observationRef)
    {
        LuxembourgSourceValidation.RequireExactResourceIri(rootIri, nameof(rootIri));
        ArgumentNullException.ThrowIfNull(observationRef);
        var input = LuxembourgSourceValidation.Copy(assertions, nameof(assertions));
        var blockers = new List<LuxembourgWemiBlocker>();
        var bound = new List<LuxembourgObservedAssertion>();

        foreach (var assertion in input)
        {
            if (assertion.ObservationRef != observationRef)
            {
                blockers.Add(Blocker(
                    LuxembourgWemiBlockerCode.ObservationMismatch,
                    assertion.SubjectIri,
                    assertion.PredicateIri,
                    assertion.ObjectIriOrLexical));
                continue;
            }

            bound.Add(assertion);
        }

        var facts = bound
            .Distinct()
            .OrderBy(static assertion => assertion.SubjectIri, ScalarComparer)
            .ThenBy(static assertion => assertion.PredicateIri, ScalarComparer)
            .ThenBy(static assertion => assertion.ObjectKind)
            .ThenBy(static assertion => assertion.ObjectIriOrLexical, ScalarComparer)
            .ThenBy(static assertion => assertion.DatatypeIriOrEmpty, ScalarComparer)
            .ThenBy(static assertion => assertion.LanguageTagOrEmpty, ScalarComparer)
            .ToArray();

        var rootType = EvaluateType(
            facts,
            rootIri,
            [Act, Consolidation],
            LuxembourgWemiBlockerCode.RootTypeObjectInvalid,
            LuxembourgWemiBlockerCode.RootTypeMissing,
            LuxembourgWemiBlockerCode.RootTypeMismatch,
            LuxembourgWemiBlockerCode.RootTypeConflict);
        blockers.AddRange(rootType.Blockers);

        var realizations = IriObjects(
            facts,
            rootIri,
            IsRealizedBy,
            LuxembourgWemiBlockerCode.RealizationObjectInvalid,
            LuxembourgWemiBlockerCode.RealizationMissing,
            requireResourceIri: true);
        blockers.AddRange(realizations.Blockers);
        var candidates = new List<CandidateDraft>();
        var reachableExpressions = new HashSet<string>(StringComparer.Ordinal);
        var reachableManifestations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expressionIri in realizations.Values)
        {
            var expressionType = EvaluateType(
                facts,
                expressionIri,
                [Expression],
                LuxembourgWemiBlockerCode.ExpressionTypeObjectInvalid,
                LuxembourgWemiBlockerCode.ExpressionTypeMissing,
                LuxembourgWemiBlockerCode.ExpressionTypeMismatch,
                LuxembourgWemiBlockerCode.ExpressionTypeConflict);
            blockers.AddRange(expressionType.Blockers);
            if (rootType.Blockers.Count == 0 && expressionType.Blockers.Count == 0)
            {
                reachableExpressions.Add(expressionIri);
            }
            var languages = IriObjects(
                facts,
                expressionIri,
                Language,
                LuxembourgWemiBlockerCode.ExpressionLanguageObjectInvalid,
                LuxembourgWemiBlockerCode.ExpressionLanguageMissing,
                requireResourceIri: false,
                conflictCode: LuxembourgWemiBlockerCode.ExpressionLanguageConflict);
            blockers.AddRange(languages.Blockers);
            var manifestations = IriObjects(
                facts,
                expressionIri,
                IsEmbodiedBy,
                LuxembourgWemiBlockerCode.EmbodimentObjectInvalid,
                LuxembourgWemiBlockerCode.EmbodimentMissing,
                requireResourceIri: true);
            blockers.AddRange(manifestations.Blockers);

            foreach (var manifestationIri in manifestations.Values)
            {
                var manifestationType = EvaluateType(
                    facts,
                    manifestationIri,
                    [Manifestation],
                    LuxembourgWemiBlockerCode.ManifestationTypeObjectInvalid,
                    LuxembourgWemiBlockerCode.ManifestationTypeMissing,
                    LuxembourgWemiBlockerCode.ManifestationTypeMismatch,
                    LuxembourgWemiBlockerCode.ManifestationTypeConflict);
                blockers.AddRange(manifestationType.Blockers);
                if (reachableExpressions.Contains(expressionIri) &&
                    manifestationType.Blockers.Count == 0)
                {
                    reachableManifestations.Add(manifestationIri);
                }
                var formats = IriObjects(
                    facts,
                    manifestationIri,
                    UserFormat,
                    LuxembourgWemiBlockerCode.ManifestationFormatObjectInvalid,
                    LuxembourgWemiBlockerCode.ManifestationFormatMissing,
                    requireResourceIri: false,
                    conflictCode: LuxembourgWemiBlockerCode.ManifestationFormatConflict);
                blockers.AddRange(formats.Blockers);
                var items = IriObjects(
                    facts,
                    manifestationIri,
                    IsExemplifiedBy,
                    LuxembourgWemiBlockerCode.ManifestationItemObjectInvalid,
                    LuxembourgWemiBlockerCode.ManifestationItemMissing,
                    requireResourceIri: true,
                    conflictCode: LuxembourgWemiBlockerCode.ManifestationItemConflict);
                foreach (var itemIri in items.Values.Where(item =>
                             !LuxembourgItemUriFamily.IsCurrent(item)))
                {
                    items.Blockers.Add(Blocker(
                        LuxembourgWemiBlockerCode.ManifestationItemUriFamilyNotAdmitted,
                        manifestationIri,
                        IsExemplifiedBy,
                        itemIri));
                }
                blockers.AddRange(items.Blockers);
                var pathCodes = rootType.Blockers
                    .Concat(realizations.Blockers)
                    .Concat(expressionType.Blockers)
                    .Concat(languages.Blockers)
                    .Concat(manifestations.Blockers)
                    .Concat(manifestationType.Blockers)
                    .Concat(formats.Blockers)
                    .Concat(items.Blockers)
                    .Select(static blocker => blocker.Code)
                    .Concat(blockers
                        .Where(static blocker =>
                            blocker.Code == LuxembourgWemiBlockerCode.ObservationMismatch)
                        .Select(static blocker => blocker.Code))
                    .Distinct()
                    .Order()
                    .ToArray();

                foreach (var languageIri in languages.Values)
                {
                    foreach (var formatIri in formats.Values)
                    {
                        foreach (var itemIri in items.Values)
                        {
                            candidates.Add(new CandidateDraft(
                                rootIri,
                                expressionIri,
                                manifestationIri,
                                itemIri,
                                languageIri,
                                formatIri,
                                pathCodes));
                        }
                    }
                }
            }
        }

        var drafts = candidates
            .Distinct()
            .OrderBy(static candidate => candidate.LanguageIri, ScalarComparer)
            .ThenBy(static candidate => candidate.FormatIri, ScalarComparer)
            .ThenBy(static candidate => candidate.ExpressionIri, ScalarComparer)
            .ThenBy(static candidate => candidate.ManifestationIri, ScalarComparer)
            .ThenBy(static candidate => candidate.ItemIri, ScalarComparer)
            .ToArray();
        var conflicts = drafts
            .Where(static candidate => candidate.BlockerCodes.Count == 0)
            .GroupBy(
                static candidate => (candidate.LanguageIri, candidate.FormatIri))
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet();

        foreach (var conflict in conflicts)
        {
            blockers.Add(new LuxembourgWemiBlocker(
                LuxembourgWemiBlockerCode.CoordinateConflict,
                rootIri,
                IsRealizedBy,
                string.Empty,
                conflict.LanguageIri,
                conflict.FormatIri));
        }

        var resolvedCandidates = drafts
            .Select(candidate =>
            {
                var codes = candidate.BlockerCodes;
                if (conflicts.Contains((candidate.LanguageIri, candidate.FormatIri)) &&
                    codes.Count == 0)
                {
                    codes = [LuxembourgWemiBlockerCode.CoordinateConflict];
                }

                return new LuxembourgWemiCandidate(
                    candidate.RootIri,
                    candidate.ExpressionIri,
                    candidate.ManifestationIri,
                    candidate.ItemIri,
                    candidate.LanguageIri,
                    candidate.FormatIri,
                    observationRef,
                    codes.Count == 0
                        ? LuxembourgWemiCandidateDisposition.StructurallyConsistent
                        : LuxembourgWemiCandidateDisposition.TypedQuarantine,
                    codes);
            })
            .ToArray();
        var previousItems = ResolvePreviousItems(
            facts,
            reachableManifestations,
            observationRef,
            blockers);
        var orderedBlockers = blockers
            .Distinct()
            .OrderBy(static blocker => blocker.Code)
            .ThenBy(static blocker => blocker.SubjectIri, ScalarComparer)
            .ThenBy(static blocker => blocker.PredicateIri, ScalarComparer)
            .ThenBy(static blocker => blocker.ObjectIriOrEmpty, ScalarComparer)
            .ThenBy(static blocker => blocker.LanguageIriOrEmpty, ScalarComparer)
            .ThenBy(static blocker => blocker.FormatIriOrEmpty, ScalarComparer)
            .ToArray();

        return new LuxembourgWemiTopologyResolution(
            resolvedCandidates,
            orderedBlockers,
            previousItems,
            reachableExpressions
                .OrderBy(static value => value, ScalarComparer)
                .ToArray(),
            reachableManifestations
                .OrderBy(static value => value, ScalarComparer)
                .ToArray());
    }

    private static IReadOnlyList<LuxembourgPreviousItem> ResolvePreviousItems(
        IReadOnlyList<LuxembourgObservedAssertion> facts,
        IReadOnlySet<string> reachableManifestations,
        SourceArtifactRef observationRef,
        ICollection<LuxembourgWemiBlocker> blockers)
    {
        var result = new List<LuxembourgPreviousItem>();
        foreach (var assertion in facts.Where(assertion =>
                     assertion.PredicateIri == PreviousIsExemplifiedBy))
        {
            if (!LuxembourgSourceValidation.IsExactResourceIri(assertion.SubjectIri))
            {
                continue;
            }

            if (!LuxembourgSourceValidation.IsExactIriTerm(
                    assertion,
                    requireResourceIri: true))
            {
                blockers.Add(Blocker(
                    LuxembourgWemiBlockerCode.PreviousItemObjectInvalid,
                    assertion.SubjectIri,
                    assertion.PredicateIri,
                    assertion.ObjectIriOrLexical));
                continue;
            }

            var disposition = !reachableManifestations.Contains(assertion.SubjectIri)
                ? LuxembourgPreviousItemDisposition.TypedQuarantineManifestationUnproven
                : LuxembourgItemUriFamily.IsPrevious(assertion.ObjectIriOrLexical)
                    ? LuxembourgPreviousItemDisposition.PointReplacedFile
                    : LuxembourgPreviousItemDisposition.TypedQuarantineUnruledUriFamily;
            result.Add(new LuxembourgPreviousItem(
                assertion.SubjectIri,
                assertion.ObjectIriOrLexical,
                observationRef,
                disposition));
        }

        return result
            .Distinct()
            .OrderBy(static row => row.ManifestationIri, ScalarComparer)
            .ThenBy(static row => row.ItemIri, ScalarComparer)
            .ToArray();
    }

    private static NodeTypeEvaluation EvaluateType(
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        string subjectIri,
        IReadOnlyList<string> expectedTypes,
        LuxembourgWemiBlockerCode invalidCode,
        LuxembourgWemiBlockerCode missingCode,
        LuxembourgWemiBlockerCode mismatchCode,
        LuxembourgWemiBlockerCode conflictCode)
    {
        var typeAssertions = assertions.Where(assertion =>
            assertion.SubjectIri == subjectIri && assertion.PredicateIri == RdfType).ToArray();
        var blockers = typeAssertions
            .Where(static assertion =>
                !LuxembourgSourceValidation.IsExactIriTerm(assertion))
            .Select(assertion => Blocker(
                invalidCode,
                subjectIri,
                RdfType,
                assertion.ObjectIriOrLexical))
            .ToList();
        var roles = typeAssertions
            .Where(static assertion => LuxembourgSourceValidation.IsExactIriTerm(assertion))
            .Select(static assertion => assertion.ObjectIriOrLexical)
            .Where(WemiTypes.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, ScalarComparer)
            .ToArray();

        if (roles.Length == 0)
        {
            blockers.Add(Blocker(missingCode, subjectIri, RdfType));
        }
        else if (roles.Length > 1)
        {
            blockers.AddRange(roles.Select(role => Blocker(
                conflictCode,
                subjectIri,
                RdfType,
                role)));
        }
        else if (!expectedTypes.Contains(roles[0], StringComparer.Ordinal))
        {
            blockers.Add(Blocker(mismatchCode, subjectIri, RdfType, roles[0]));
        }

        return new NodeTypeEvaluation(blockers);
    }

    private static IriObjectEvaluation IriObjects(
        IReadOnlyList<LuxembourgObservedAssertion> assertions,
        string subjectIri,
        string predicateIri,
        LuxembourgWemiBlockerCode invalidCode,
        LuxembourgWemiBlockerCode missingCode,
        bool requireResourceIri,
        LuxembourgWemiBlockerCode? conflictCode = null)
    {
        var matching = assertions.Where(assertion =>
            assertion.SubjectIri == subjectIri && assertion.PredicateIri == predicateIri).ToArray();
        var values = new List<string>();
        var blockers = new List<LuxembourgWemiBlocker>();
        foreach (var assertion in matching)
        {
            if (!LuxembourgSourceValidation.IsExactIriTerm(assertion, requireResourceIri))
            {
                blockers.Add(Blocker(
                    invalidCode,
                    subjectIri,
                    predicateIri,
                    assertion.ObjectIriOrLexical));
                continue;
            }

            values.Add(assertion.ObjectIriOrLexical);
        }

        var ordered = values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, ScalarComparer)
            .ToArray();
        if (ordered.Length == 0)
        {
            blockers.Add(Blocker(missingCode, subjectIri, predicateIri));
        }
        else if (ordered.Length > 1 && conflictCode is not null)
        {
            blockers.AddRange(ordered.Select(value => Blocker(
                conflictCode.Value,
                subjectIri,
                predicateIri,
                value)));
        }

        return new IriObjectEvaluation(ordered, blockers);
    }

    private static LuxembourgWemiBlocker Blocker(
        LuxembourgWemiBlockerCode code,
        string subjectIri,
        string predicateIri,
        string objectIriOrEmpty = "") => new(
        code,
        subjectIri,
        predicateIri,
        objectIriOrEmpty,
        string.Empty,
        string.Empty);

    private static IComparer<string> ScalarComparer =>
        LuxembourgSourceValidation.UnicodeScalarComparer;

    private sealed record NodeTypeEvaluation(IReadOnlyList<LuxembourgWemiBlocker> Blockers);

    private sealed record IriObjectEvaluation(
        IReadOnlyList<string> Values,
        List<LuxembourgWemiBlocker> Blockers);

    private sealed record CandidateDraft(
        string RootIri,
        string ExpressionIri,
        string ManifestationIri,
        string ItemIri,
        string LanguageIri,
        string FormatIri,
        IReadOnlyList<LuxembourgWemiBlockerCode> BlockerCodes);

}
