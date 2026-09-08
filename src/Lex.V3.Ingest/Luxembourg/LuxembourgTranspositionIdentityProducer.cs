using System.Globalization;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

public enum LuxembourgTranspositionIdentityProductionRefusal
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

    [JsonStringEnumMemberName("ambiguous_identity")]
    AmbiguousIdentity = 5,
}

/// <summary>
/// One retained Legilux transposition target and the publisher's exact mapping of that local node
/// to an official EU ELI. The original relation coordinate remains unchanged.
/// </summary>
public sealed record LuxembourgTranspositionIdentityRelation(
    string NationalMeasureUri,
    string LocalEuWorkUri,
    string EuEli,
    EuWorkKindAssertion WorkKindAssertion,
    SourceArtifactRef CompletionEvidenceRef);

/// <summary>Delivered identity relations, or one typed refusal. Never both.</summary>
public sealed class LuxembourgTranspositionIdentityProductionResult
{
    private LuxembourgTranspositionIdentityProductionResult(
        IReadOnlyList<LuxembourgTranspositionIdentityRelation>? relations,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgTranspositionIdentityProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Relations = relations;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    public IReadOnlyList<LuxembourgTranspositionIdentityRelation>? Relations { get; }
    public SourceArtifactRef? CompletionEvidenceRef { get; }
    public LuxembourgTranspositionIdentityProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public int ProductRequestCount { get; }
    public bool Delivered => Refusal == LuxembourgTranspositionIdentityProductionRefusal.None;

    internal static LuxembourgTranspositionIdentityProductionResult Success(
        IReadOnlyList<LuxembourgTranspositionIdentityRelation> relations,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount) =>
        new(Array.AsReadOnly(relations.ToArray()), completionEvidenceRef,
            LuxembourgTranspositionIdentityProductionRefusal.None, null, productRequestCount);

    internal static LuxembourgTranspositionIdentityProductionResult Refused(
        LuxembourgTranspositionIdentityProductionRefusal refusal,
        string detail,
        int productRequestCount)
    {
        if (refusal == LuxembourgTranspositionIdentityProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }
        return new(null, null, refusal, detail, productRequestCount);
    }
}

/// <summary>
/// Executes the bounded Legilux identity family, reopens its retained rows through the shared proof
/// door, and admits only complete publisher mappings to the accepted EU work-kind contract.
/// </summary>
public sealed class LuxembourgTranspositionIdentityProducer
{
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgTranspositionIdentityProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgTranspositionIdentityProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    public async Task<LuxembourgTranspositionIdentityProductionResult> RunAsync(
        LuxembourgTranspositionIdentityRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);
        var run = await _executor.RunLuxembourgTranspositionIdentitiesAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            return LuxembourgTranspositionIdentityProductionResult.Refused(
                LuxembourgTranspositionIdentityProductionRefusal.EnumerationRefused,
                run.Refusal?.Code.ToString() ?? "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgTranspositionIdentityProductionResult.Refused(
                LuxembourgTranspositionIdentityProductionRefusal.EnumerationProofRefused,
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
            return LuxembourgTranspositionIdentityProductionResult.Refused(
                LuxembourgTranspositionIdentityProductionRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }
        return DecodeRows(rows, profile, proof.AcquisitionRunRef, run.ProductRequestCount);
    }

    internal static LuxembourgTranspositionIdentityProductionResult DecodeRows(
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
            var relations = rows.Select(row => DecodeRow(row, profile, completionEvidenceRef)).ToArray();
            var ambiguous = relations
                .GroupBy(static relation => relation.LocalEuWorkUri, StringComparer.Ordinal)
                .FirstOrDefault(group => group
                    .Select(static relation => relation.EuEli)
                    .Distinct(StringComparer.Ordinal)
                    .Skip(1)
                    .Any());
            if (ambiguous is not null)
            {
                return LuxembourgTranspositionIdentityProductionResult.Refused(
                    LuxembourgTranspositionIdentityProductionRefusal.AmbiguousIdentity,
                    $"The local EU work {ambiguous.Key} has more than one publisher EU identity.",
                    productRequestCount);
            }
            return LuxembourgTranspositionIdentityProductionResult.Success(
                relations, completionEvidenceRef, productRequestCount);
        }
        catch (ArgumentException exception)
        {
            return LuxembourgTranspositionIdentityProductionResult.Refused(
                LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted,
                exception.Message,
                productRequestCount);
        }
    }

    private static LuxembourgTranspositionIdentityRelation DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef evidenceRef)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Terms.Count != profile.ProjectionVariables.Count || row.Terms.Count != 9)
        {
            throw new ArgumentException("A Legilux transposition identity row has nine exact terms.", nameof(row));
        }

        var measure = RequireLegiluxEli(Term(row, profile, "measure"), "measure", "/eli/etat/leg/");
        var localEuWork = RequireLegiluxEli(
            Term(row, profile, "local_eu_work"), "local_eu_work", "/eli/dir_ue/");
        var euEli = RequireEuEli(Term(row, profile, "eu_eli"));
        RequireExactIri(
            Term(row, profile, "eu_work_kind"),
            LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri,
            "eu_work_kind must be the publisher's EUDirective class.");
        _ = RequirePositiveInteger(Term(row, profile, "multiplicity"), "multiplicity");

        RequirePlainLiteral(Term(row, profile, "key_1"), "key_1", measure);
        RequirePlainLiteral(Term(row, profile, "key_2"), "key_2", localEuWork);
        RequirePlainLiteral(Term(row, profile, "key_3"), "key_3", euEli);
        RequirePlainLiteral(
            Term(row, profile, "key_4"),
            "key_4",
            LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri);

        var work = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.Eli, euEli)]);
        return new LuxembourgTranspositionIdentityRelation(
            measure,
            localEuWork,
            euEli,
            new EuWorkKindAssertion(work, EuWorkKind.Directive),
            evidenceRef);
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
            throw new ArgumentException($"The Legilux identity profile is missing {name}.", nameof(profile));
        }
        return row.Terms[index];
    }

    private static string RequireLegiluxEli(
        RepeatedEnumerationRdfTerm term,
        string name,
        string requiredPathPrefix)
    {
        var value = RequireBareIri(term, name);
        if (OfficialIdentifier.EliMintedBy(value) != PublisherId.LuLegilux ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.AbsolutePath.StartsWith(requiredPathPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} must be an exact Legilux ELI in the admitted family.", name);
        }
        return value;
    }

    private static string RequireEuEli(RepeatedEnumerationRdfTerm term)
    {
        var value = RequireBareIri(term, "eu_eli");
        if (OfficialIdentifier.EliMintedBy(value) != PublisherId.EuEurLex ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.AbsolutePath.StartsWith("/eli/dir/", StringComparison.Ordinal))
        {
            throw new ArgumentException("eu_eli must be an exact EU publisher ELI for a directive.", nameof(term));
        }
        return value;
    }

    private static string RequireBareIri(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || term.Value is null ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException($"{name} must be a publisher IRI.", name);
        }
        return term.Value;
    }

    private static void RequireExactIri(
        RepeatedEnumerationRdfTerm term,
        string expected,
        string message)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri ||
            !string.Equals(term.Value, expected, StringComparison.Ordinal) ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException(message, nameof(term));
        }
    }

    private static long RequirePositiveInteger(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Datatype != XsdInteger ||
            term.Language is not null ||
            !long.TryParse(term.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException($"{name} must be one positive xsd:integer.", name);
        }
        return value;
    }

    private static void RequirePlainLiteral(
        RepeatedEnumerationRdfTerm term,
        string name,
        string expected)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal ||
            term.Datatype is not null || term.Language is not null ||
            !string.Equals(term.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} must repeat the publisher value exactly.", name);
        }
    }
}
