using System.Globalization;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public enum EuNationalImplementingMeasureProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 1,

    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 2,

    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 3,

    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 4,
}

/// <summary>One Commission NIM assertion and its already guarded bridge-side acquisition.</summary>
public sealed record EuNationalImplementingMeasureRelation(
    string EuWorkUri,
    EuWorkKindAssertion WorkKindAssertion,
    string PublisherWorkTypeIri,
    string NimWorkUri,
    string NimCelex,
    string ImplementsPredicateIri,
    string? LegiluxEli,
    EuTranspositionSourceAcquisition Acquisition);

/// <summary>
/// One verified publisher row whose exact work type is outside E5's two-member work-kind contract.
/// It remains evidence-bound and must not be read as a non-transposable work or a missing row.
/// </summary>
public sealed record EuNationalImplementingMeasureOutOfE5WorkKindExclusion(
    string EuWorkUri,
    string EuWorkEli,
    string PublisherWorkTypeIri,
    string NimWorkUri,
    string NimCelex,
    string ImplementsPredicateIri,
    string? LegiluxEli,
    SourceArtifactRef EvidenceRef)
{
    public const string DispositionToken = "out_of_e5_work_kind";
    public string Disposition => DispositionToken;
}

/// <summary>
/// One verified publisher row whose raw work type and raw ELI name contradictory work families.
/// Neither coordinate is rewritten or preferred, and the row cannot enter the admitted E5 bridge.
/// </summary>
public sealed record EuNationalImplementingMeasurePublisherCoordinatesConflict(
    string EuWorkUri,
    string EuWorkEli,
    string PublisherWorkTypeIri,
    string NimWorkUri,
    string NimCelex,
    string ImplementsPredicateIri,
    string? LegiluxEli,
    SourceArtifactRef EvidenceRef)
{
    public const string DispositionToken = "publisher_coordinates_conflict";
    public string Disposition => DispositionToken;
}

/// <summary>Delivered relations, or one typed refusal. Never both.</summary>
public sealed class EuNationalImplementingMeasureProductionResult
{
    private EuNationalImplementingMeasureProductionResult(
        IReadOnlyList<EuNationalImplementingMeasureRelation>? relations,
        IReadOnlyList<EuNationalImplementingMeasureOutOfE5WorkKindExclusion>? exclusions,
        IReadOnlyList<EuNationalImplementingMeasurePublisherCoordinatesConflict>? conflicts,
        SourceArtifactRef? completionEvidenceRef,
        EuNationalImplementingMeasureProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Relations = relations;
        OutOfE5WorkKindExclusions = exclusions;
        PublisherCoordinateConflicts = conflicts;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    public IReadOnlyList<EuNationalImplementingMeasureRelation>? Relations { get; }
    public IReadOnlyList<EuNationalImplementingMeasureOutOfE5WorkKindExclusion>?
        OutOfE5WorkKindExclusions { get; }
    public IReadOnlyList<EuNationalImplementingMeasurePublisherCoordinatesConflict>?
        PublisherCoordinateConflicts { get; }
    public SourceArtifactRef? CompletionEvidenceRef { get; }
    public EuNationalImplementingMeasureProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public int ProductRequestCount { get; }
    public bool Delivered => Refusal == EuNationalImplementingMeasureProductionRefusal.None;

    internal static EuNationalImplementingMeasureProductionResult Success(
        IReadOnlyList<EuNationalImplementingMeasureRelation> relations,
        IReadOnlyList<EuNationalImplementingMeasureOutOfE5WorkKindExclusion> exclusions,
        IReadOnlyList<EuNationalImplementingMeasurePublisherCoordinatesConflict> conflicts,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount) =>
        new(Array.AsReadOnly(relations.ToArray()), Array.AsReadOnly(exclusions.ToArray()),
            Array.AsReadOnly(conflicts.ToArray()), completionEvidenceRef,
            EuNationalImplementingMeasureProductionRefusal.None, null, productRequestCount);

    internal static EuNationalImplementingMeasureProductionResult Refused(
        EuNationalImplementingMeasureProductionRefusal refusal,
        string detail,
        int productRequestCount)
    {
        if (refusal == EuNationalImplementingMeasureProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }
        return new(null, null, null, null, refusal, detail, productRequestCount);
    }

    /// <summary>
    /// All completed NIM assertions for one EU work. A genuinely empty result is represented by
    /// one completed acquisition with an empty side set and the same enumeration evidence.
    /// </summary>
    public EuTranspositionSourceAcquisition ForEuWork(string euWorkUri)
    {
        if (!Delivered || Relations is null || OutOfE5WorkKindExclusions is null ||
            PublisherCoordinateConflicts is null ||
            CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException("A refused production result has no completed NIM acquisition.");
        }
        RequireCellarWorkUri(euWorkUri, nameof(euWorkUri));
        if (OutOfE5WorkKindExclusions.Any(value =>
                string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "An out-of-E5-work-kind publisher row cannot be read as a completed empty E5 set.");
        }
        if (PublisherCoordinateConflicts.Any(value =>
                string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "A publisher-coordinate conflict cannot be read as a completed empty E5 set.");
        }
        var workRelations = Relations
            .Where(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal))
            .ToArray();
        var workKinds = workRelations.Select(static value => value.WorkKindAssertion.Kind)
            .Distinct()
            .ToArray();
        if (workKinds.Length > 1)
        {
            throw new ArgumentException(
                "One EU work cannot carry more than one admitted work kind.", nameof(euWorkUri));
        }
        if (workKinds is [EuWorkKind.Regulation])
        {
            return new EuTranspositionSourceAcquisition(
                EuTranspositionAssertedBy.Nim,
                EuRelationAcquisitionState.Complete,
                [],
                CompletionEvidenceRef);
        }

        var matches = workRelations
            .SelectMany(static value => value.Acquisition.Sides)
            // Distinct raw rows can name one identical publisher assertion under separate CELEX
            // coordinates. Relations retains every row; the bridge column carries the assertion
            // once. Any difference in evidence or disclaimer survives this equality check and the
            // acquisition constructor continues to refuse the repeated URI.
            .Distinct()
            .ToArray();
        return new EuTranspositionSourceAcquisition(
            EuTranspositionAssertedBy.Nim,
            EuRelationAcquisitionState.Complete,
            matches,
            CompletionEvidenceRef);
    }

    private static void RequireCellarWorkUri(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !value.StartsWith(
                "http://publications.europa.eu/resource/cellar/",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("An EU work must be an exact Cellar work IRI.", parameterName);
        }
    }
}

/// <summary>
/// Runs the fixed Luxembourg sector-7 family, mints its enumeration proof, reopens its retained
/// page bytes through the shared proof door, and only then builds the accepted NIM bridge side.
/// </summary>
public sealed class EuNationalImplementingMeasureProducer
{
    private const string CellarWorkPrefix = "http://publications.europa.eu/resource/cellar/";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdAnyUri = "http://www.w3.org/2001/XMLSchema#anyURI";
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuNationalImplementingMeasureProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal EuNationalImplementingMeasureProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    public async Task<EuNationalImplementingMeasureProductionResult> RunAsync(
        EuNationalImplementingMeasureRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);
        var run = await _executor.RunNationalImplementingMeasuresAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            var refusal = run.Refusal;
            return EuNationalImplementingMeasureProductionResult.Refused(
                EuNationalImplementingMeasureProductionRefusal.EnumerationRefused,
                refusal is null
                    ? "enumeration returned neither a receipt nor a refusal"
                    : refusal.Code + (string.IsNullOrEmpty(refusal.CoreRefusalDetail)
                        ? string.Empty
                        : $": {refusal.CoreRefusalDetail}"),
                run.ProductRequestCount);
        }

        var familyKey = receipt.Delivery.PartitionKey;
        var proof = receipt.TryProveFamilyEnumeration(familyKey, out var proofRefusal);
        if (proof is null)
        {
            return EuNationalImplementingMeasureProductionResult.Refused(
                EuNationalImplementingMeasureProductionRefusal.EnumerationProofRefused,
                proofRefusal.ToString(),
                run.ProductRequestCount);
        }

        var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static value => value.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }
        var profile = request.Plan.CreateDeliveryProfile();
        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof,
            receipt.Delivery,
            profile,
            receipt.Delivery.InterpretationProfileRef,
            receipt.Delivery.CountA.HttpEvidenceRef,
            pages,
            out var rowRefusal);
        if (rows is null)
        {
            return EuNationalImplementingMeasureProductionResult.Refused(
                EuNationalImplementingMeasureProductionRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }
        return DecodeRows(rows, profile, proof.AcquisitionRunRef, run.ProductRequestCount);
    }

    internal static EuNationalImplementingMeasureProductionResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);
        try
        {
            var decoded = rows.Select(row => DecodeRow(row, profile, completionEvidenceRef)).ToArray();
            return EuNationalImplementingMeasureProductionResult.Success(
                decoded.Where(static value => value.Relation is not null)
                    .Select(static value => value.Relation!).ToArray(),
                decoded.Where(static value => value.Exclusion is not null)
                    .Select(static value => value.Exclusion!).ToArray(),
                decoded.Where(static value => value.Conflict is not null)
                    .Select(static value => value.Conflict!).ToArray(),
                completionEvidenceRef, productRequestCount);
        }
        catch (ArgumentException exception)
        {
            return EuNationalImplementingMeasureProductionResult.Refused(
                EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted,
                exception.Message,
                productRequestCount);
        }
    }

    private static DecodedRow DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef evidenceRef)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Terms.Count != profile.ProjectionVariables.Count || row.Terms.Count != 18)
        {
            throw new ArgumentException("A Luxembourg sector-7 NIM row has eighteen exact terms.", nameof(row));
        }
        var nim = RequireCellarWork(Term(row, profile, "nim"), "nim");
        RequireIri(Term(row, profile, "country"),
            EuNationalImplementingMeasureDiscoveryPlan.LuxembourgCountryIri, "country");
        var nimCelex = RequireSectorSevenCelex(Term(row, profile, "nim_celex"));
        var predicate = RequireImplementsPredicate(Term(row, profile, "implements_predicate"));
        var euWork = RequireCellarWork(Term(row, profile, "eu_work"), "eu_work");
        var euWorkEli = RequireEuWorkEli(Term(row, profile, "eu_work_eli"));
        var publisherWorkTypeIri = RequirePublisherWorkTypeIri(Term(row, profile, "eu_work_kind"));
        var eliTerm = Term(row, profile, "eli");
        var eliKind = RequirePlainLiteral(Term(row, profile, "eli_kind"), "eli_kind");
        var eli = RequireEli(eliTerm, eliKind);
        var workKind = ClassifyWorkType(publisherWorkTypeIri);
        _ = RequirePositiveInteger(Term(row, profile, "multiplicity"), "multiplicity");

        RequirePlainLiteral(Term(row, profile, "key_1"), "key_1", nim);
        RequirePlainLiteral(Term(row, profile, "key_2"), "key_2", nimCelex);
        RequirePlainLiteral(Term(row, profile, "key_3"), "key_3", predicate);
        RequirePlainLiteral(Term(row, profile, "key_4"), "key_4", euWork);
        RequirePlainLiteral(Term(row, profile, "key_5"), "key_5", euWorkEli);
        RequirePlainLiteral(Term(row, profile, "key_6"), "key_6", publisherWorkTypeIri);
        RequirePlainLiteral(Term(row, profile, "key_7"), "key_7", eli ?? string.Empty);
        RequirePlainLiteral(Term(row, profile, "page_key"), "page_key", PageKey(
            nim, nimCelex, predicate, euWork, euWorkEli, publisherWorkTypeIri, eli ?? string.Empty));

        if (!WorkTypeMatchesKindAndEli(workKind, publisherWorkTypeIri, euWorkEli))
        {
            return new DecodedRow(null, null,
                new EuNationalImplementingMeasurePublisherCoordinatesConflict(
                    euWork, euWorkEli, publisherWorkTypeIri,
                    nim, nimCelex, predicate, eli, evidenceRef));
        }

        if (workKind is null)
        {
            return new DecodedRow(null, new EuNationalImplementingMeasureOutOfE5WorkKindExclusion(
                euWork, euWorkEli, publisherWorkTypeIri, nim, nimCelex, predicate, eli, evidenceRef), null);
        }

        var workKindAssertion = new EuWorkKindAssertion(
            new OfficialIdentitySet(PublisherId.EuEurLex,
            [
                new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, euWork),
                new OfficialIdentifier(FactsIdentifierFamily.Eli, euWorkEli),
            ]),
            workKind.Value);

        var side = new EuTranspositionSide(
            EuTranspositionAssertedBy.Nim,
            eli ?? nim,
            evidenceRef,
            EuMemberStateDisclaimer.Text,
            EuMemberStateDisclaimer.SourceUri);
        var acquisition = new EuTranspositionSourceAcquisition(
            EuTranspositionAssertedBy.Nim,
            EuRelationAcquisitionState.Complete,
            [side],
            evidenceRef);
        return new DecodedRow(
            new EuNationalImplementingMeasureRelation(
                euWork, workKindAssertion, publisherWorkTypeIri,
                nim, nimCelex, predicate, eli, acquisition),
            null,
            null);
    }

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name)
    {
        var index = -1;
        for (var ordinal = 0; ordinal < profile.ProjectionVariables.Count; ordinal++)
        {
            if (string.Equals(profile.ProjectionVariables[ordinal], name, StringComparison.Ordinal))
            {
                index = ordinal;
                break;
            }
        }
        if (index < 0)
        {
            throw new ArgumentException($"The NIM profile is missing {name}.", nameof(profile));
        }
        return row.Terms[index];
    }

    private static string RequireCellarWork(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || term.Value is null ||
            !term.Value.StartsWith(CellarWorkPrefix, StringComparison.Ordinal) ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException($"{name} must be a Cellar work IRI.", name);
        }
        return term.Value;
    }

    private static void RequireIri(RepeatedEnumerationRdfTerm term, string expected, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri ||
            !string.Equals(term.Value, expected, StringComparison.Ordinal) ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException($"{name} is outside the admitted Luxembourg family.", name);
        }
    }

    private static string RequireSectorSevenCelex(RepeatedEnumerationRdfTerm term)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Value is null ||
            term.Value.Length is < 2 or > 64 || term.Value[0] != '7' ||
            term.Language is not null || term.Datatype is not (null or XsdString))
        {
            throw new ArgumentException("nim_celex must be a sector-7 CELEX literal.", nameof(term));
        }
        return term.Value;
    }

    private static string RequireImplementsPredicate(RepeatedEnumerationRdfTerm term)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || term.Datatype is not null || term.Language is not null ||
            term.Value is not (EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri or
                EuNationalImplementingMeasureDiscoveryPlan.LegacyImplementsDirectivePredicateIri))
        {
            throw new ArgumentException("implements_predicate is not one of the two admitted publisher predicates.", nameof(term));
        }
        return term.Value;
    }

    private static string RequireEuWorkEli(RepeatedEnumerationRdfTerm term)
    {
        var admittedTerm = term.Kind is RepeatedEnumerationRdfTermKind.Iri or RepeatedEnumerationRdfTermKind.Literal
            && term.Language is null
            && term.Datatype is null or XsdString or XsdAnyUri;
        if (!admittedTerm || term.Value is null ||
            OfficialIdentifier.EliMintedBy(term.Value) != PublisherId.EuEurLex)
        {
            throw new ArgumentException(
                "eu_work_eli must be an exact ELI minted by the EU publisher.", nameof(term));
        }
        return term.Value;
    }

    private static string RequirePublisherWorkTypeIri(RepeatedEnumerationRdfTerm term)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException("eu_work_kind must be an admitted publisher class IRI.", nameof(term));
        }
        return term.Value ?? throw new ArgumentException(
            "eu_work_kind must be an admitted publisher class IRI.", nameof(term));
    }

    private static EuWorkKind? ClassifyWorkType(string publisherWorkTypeIri) =>
        WorkTypeRule(publisherWorkTypeIri).Kind;

    internal static bool WorkTypeMatchesKindAndEli(
        EuWorkKind? kind,
        string publisherWorkTypeIri,
        string euWorkEli)
    {
        ArgumentNullException.ThrowIfNull(publisherWorkTypeIri);
        ArgumentNullException.ThrowIfNull(euWorkEli);
        try
        {
            var rule = WorkTypeRule(publisherWorkTypeIri);
            return rule.Kind == kind &&
                Uri.TryCreate(euWorkEli, UriKind.Absolute, out var uri) &&
                uri.AbsolutePath.StartsWith(rule.EliPathPrefix, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static (EuWorkKind? Kind, string EliPathPrefix) WorkTypeRule(
        string publisherWorkTypeIri) => publisherWorkTypeIri switch
    {
        EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri =>
            (EuWorkKind.Directive, "/eli/dir/"),
        EuNationalImplementingMeasureDiscoveryPlan.RegulationResourceTypeIri =>
            (EuWorkKind.Regulation, "/eli/reg/"),
        EuNationalImplementingMeasureDiscoveryPlan.DelegatedDirectiveResourceTypeIri =>
            (EuWorkKind.Directive, "/eli/dir_del/"),
        EuNationalImplementingMeasureDiscoveryPlan.ImplementingDirectiveResourceTypeIri =>
            (EuWorkKind.Directive, "/eli/dir_impl/"),
        EuNationalImplementingMeasureDiscoveryPlan.DecisionResourceTypeIri =>
            (null, "/eli/dec/"),
        EuNationalImplementingMeasureDiscoveryPlan.FrameworkDecisionResourceTypeIri =>
            (null, "/eli/dec_framw/"),
        _ => throw new ArgumentException(
            $"eu_work_kind '{publisherWorkTypeIri}' is outside the ruled E5 work-type partition.",
            nameof(publisherWorkTypeIri)),
    };

    private sealed record DecodedRow(
        EuNationalImplementingMeasureRelation? Relation,
        EuNationalImplementingMeasureOutOfE5WorkKindExclusion? Exclusion,
        EuNationalImplementingMeasurePublisherCoordinatesConflict? Conflict);

    private static string PageKey(params string[] parts) =>
        string.Join('|', parts.Select(Uri.EscapeDataString));

    private static string? RequireEli(RepeatedEnumerationRdfTerm term, string kind)
    {
        if (kind == "unbound" && term.Kind == RepeatedEnumerationRdfTermKind.Unbound)
        {
            return null;
        }

        var literal = kind == "literal"
            && term.Kind == RepeatedEnumerationRdfTermKind.Literal
            && term.Language is null
            && term.Datatype is null or XsdString or XsdAnyUri;
        var iri = kind == "iri"
            && term.Kind == RepeatedEnumerationRdfTermKind.Iri
            && term.Datatype is null
            && term.Language is null;
        if ((!literal && !iri) || term.Value is null ||
            !Uri.TryCreate(term.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "eli must be an absolute publisher URI term or explicitly unbound.", nameof(term));
        }
        return term.Value;
    }

    private static long RequirePositiveInteger(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Datatype != XsdInteger ||
            term.Language is not null ||
            !long.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException($"{name} must be one positive xsd:integer literal.", name);
        }
        return value;
    }

    private static string RequirePlainLiteral(
        RepeatedEnumerationRdfTerm term,
        string name,
        string? expected = null)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Value is null ||
            term.Datatype is not null || term.Language is not null ||
            expected is not null && !string.Equals(term.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} must be the exact plain literal selected by the query.", name);
        }
        return term.Value;
    }
}
