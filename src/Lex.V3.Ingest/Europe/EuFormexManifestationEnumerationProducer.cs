using System.Globalization;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public enum EuFormexManifestationEnumerationRefusal
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
    [JsonStringEnumMemberName("row_names_another_expression")]
    RowNamesAnotherExpression = 5,
    [JsonStringEnumMemberName("manifestation_type_delivered_twice")]
    ManifestationTypeDeliveredTwice = 6,
}

/// <summary>One manifestation type proven by a completed enumeration for one expression.</summary>
public sealed record EuExpressionManifestationType(
    string PublisherType,
    long Multiplicity,
    string SourceObservationId)
{
    public bool IsFormex => string.Equals(
        PublisherType, EuFormexManifestationDiscoveryPlan.FormexTypeToken, StringComparison.Ordinal);
}

/// <summary>The complete manifestation-type answer for one expression, or one refusal.</summary>
public sealed class EuFormexManifestationEnumerationResult
{
    private EuFormexManifestationEnumerationResult(
        LanguageScopedExpression expression,
        IReadOnlyList<EuExpressionManifestationType>? manifestationTypes,
        AbsenceFamilyEnumerationProof? proof,
        EuFormexManifestationEnumerationRefusal refusal,
        string? detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget)
    {
        Expression = expression;
        ManifestationTypes = manifestationTypes is null
            ? null : Array.AsReadOnly(manifestationTypes.ToArray());
        Proof = proof;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
        WireBudget = wireBudget;
    }

    public LanguageScopedExpression Expression { get; }
    public LanguageScopedExpressionIdentity ExpressionIdentity => Expression.Identity;
    public IReadOnlyList<EuExpressionManifestationType>? ManifestationTypes { get; }
    public AbsenceFamilyEnumerationProof? Proof { get; }
    public EuFormexManifestationEnumerationRefusal Refusal { get; }
    public string? Detail { get; }
    public int ProductRequestCount { get; }
    public WireBudgetSnapshot WireBudget { get; }
    public bool Delivered => Refusal == EuFormexManifestationEnumerationRefusal.None;
    public bool IsFormexEligible => Delivered && ManifestationTypes!.Any(static value => value.IsFormex);

    internal static EuFormexManifestationEnumerationResult Success(
        LanguageScopedExpression expression,
        IReadOnlyList<EuExpressionManifestationType> types,
        AbsenceFamilyEnumerationProof proof,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(expression, types, proof, EuFormexManifestationEnumerationRefusal.None, null,
            productRequestCount, wireBudget);

    internal static EuFormexManifestationEnumerationResult Refused(
        LanguageScopedExpression expression,
        EuFormexManifestationEnumerationRefusal refusal,
        string detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(expression, null, null, refusal, detail, productRequestCount, wireBudget);
}

public enum EuFormexEligibilityPopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,
    [JsonStringEnumMemberName("expression_production_refused")]
    ExpressionProductionRefused = 1,
    [JsonStringEnumMemberName("expression_enumeration_refused")]
    ExpressionEnumerationRefused = 2,
    [JsonStringEnumMemberName("expression_enumeration_missing")]
    ExpressionEnumerationMissing = 3,
    [JsonStringEnumMemberName("expression_enumerated_twice")]
    ExpressionEnumeratedTwice = 4,
    [JsonStringEnumMemberName("expression_outside_population")]
    ExpressionOutsidePopulation = 5,
    [JsonStringEnumMemberName("expression_content_disagrees")]
    ExpressionContentDisagrees = 6,
}

/// <summary>
/// A proof-complete eligibility projection over every expression in one proven expression production.
/// </summary>
public sealed class EuFormexEligibilityPopulation
{
    private EuFormexEligibilityPopulation(
        EuLanguageScopedExpressionProductionResult expressionProduction,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        IReadOnlyList<LanguageScopedExpression> eligibleExpressions)
    {
        ExpressionProduction = expressionProduction;
        Enumerations = Array.AsReadOnly(enumerations.ToArray());
        EligibleExpressions = Array.AsReadOnly(eligibleExpressions.ToArray());
    }

    public EuLanguageScopedExpressionProductionResult ExpressionProduction { get; }
    public IReadOnlyList<EuFormexManifestationEnumerationResult> Enumerations { get; }
    public IReadOnlyList<LanguageScopedExpression> EligibleExpressions { get; }

    public static EuFormexEligibilityPopulation? TryCreate(
        EuLanguageScopedExpressionProductionResult expressionProduction,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        out EuFormexEligibilityPopulationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(expressionProduction);
        ArgumentNullException.ThrowIfNull(enumerations);
        refusal = EuFormexEligibilityPopulationRefusal.None;
        detail = null;
        if (!expressionProduction.Delivered || expressionProduction.Derivation is null)
        {
            refusal = EuFormexEligibilityPopulationRefusal.ExpressionProductionRefused;
            detail = expressionProduction.Refusal.ToString();
            return null;
        }

        var expressions = expressionProduction.Derivation.Expressions;
        var expected = expressions.ToDictionary(static value => value.Identity);
        var delivered = new Dictionary<LanguageScopedExpressionIdentity, EuFormexManifestationEnumerationResult>();
        foreach (var enumeration in enumerations)
        {
            if (!enumeration.Delivered)
            {
                refusal = EuFormexEligibilityPopulationRefusal.ExpressionEnumerationRefused;
                detail = enumeration.ExpressionIdentity.PublisherExpressionId + ": " + enumeration.Refusal;
                return null;
            }
            if (!expected.TryGetValue(enumeration.ExpressionIdentity, out var expression))
            {
                refusal = EuFormexEligibilityPopulationRefusal.ExpressionOutsidePopulation;
                detail = enumeration.ExpressionIdentity.PublisherExpressionId;
                return null;
            }
            if (!string.Equals(expression.CanonicalContentSha256,
                    enumeration.Expression.CanonicalContentSha256, StringComparison.Ordinal))
            {
                refusal = EuFormexEligibilityPopulationRefusal.ExpressionContentDisagrees;
                detail = enumeration.ExpressionIdentity.PublisherExpressionId;
                return null;
            }
            if (!delivered.TryAdd(enumeration.ExpressionIdentity, enumeration))
            {
                refusal = EuFormexEligibilityPopulationRefusal.ExpressionEnumeratedTwice;
                detail = enumeration.ExpressionIdentity.PublisherExpressionId;
                return null;
            }
        }

        var missing = expressions.FirstOrDefault(value => !delivered.ContainsKey(value.Identity));
        if (missing is not null)
        {
            refusal = EuFormexEligibilityPopulationRefusal.ExpressionEnumerationMissing;
            detail = missing.Identity.PublisherExpressionId;
            return null;
        }

        var ordered = expressions.Select(value => delivered[value.Identity]).ToArray();
        return new EuFormexEligibilityPopulation(
            expressionProduction,
            ordered,
            expressions.Where(value => delivered[value.Identity].IsFormexEligible).ToArray());
    }
}

/// <summary>Runs and decodes the proof-bearing manifestation family for one expression.</summary>
public sealed class EuFormexManifestationEnumerationProducer
{
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuFormexManifestationEnumerationProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal EuFormexManifestationEnumerationProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    public async Task<EuFormexManifestationEnumerationResult> RunAsync(
        EuFormexManifestationRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);
        var run = await _executor.RunEuFormexManifestationsAsync(request, sourceWitness, cancellationToken)
            .ConfigureAwait(false);
        var budget = WireBudgetSnapshot.Of(request.WireBudget);
        if (run.Receipt is not { } receipt)
        {
            return EuFormexManifestationEnumerationResult.Refused(
                request.Expression,
                EuFormexManifestationEnumerationRefusal.EnumerationRefused,
                run.Refusal is { } refusal ? refusal.Code + ": " + refusal.CoreRefusalDetail
                    : "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount,
                budget);
        }

        var key = EuFormexManifestationDiscoveryPlan.PartitionKeyFor(request.ExpressionIdentity);
        var proof = receipt.TryProveFamilyEnumeration(key, out var proofRefusal);
        if (proof is null)
        {
            return EuFormexManifestationEnumerationResult.Refused(
                request.Expression,
                EuFormexManifestationEnumerationRefusal.EnumerationProofRefused,
                proofRefusal.ToString(), run.ProductRequestCount, budget);
        }

        var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static value => value.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }
        var profile = request.Plan.CreateDeliveryProfile();
        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof, receipt.Delivery, profile, receipt.Delivery.InterpretationProfileRef,
            receipt.Delivery.CountA.HttpEvidenceRef, pages, out var rowRefusal);
        return rows is null
            ? EuFormexManifestationEnumerationResult.Refused(
                request.Expression, EuFormexManifestationEnumerationRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(), run.ProductRequestCount, budget)
            : DecodeRows(rows, profile, proof, request.Expression, budget, run.ProductRequestCount);
    }

    internal static EuFormexManifestationEnumerationResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        AbsenceFamilyEnumerationProof proof,
        LanguageScopedExpression expression,
        WireBudgetSnapshot wireBudget,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(expression);
        var expectedKey = EuFormexManifestationDiscoveryPlan.PartitionKeyFor(expression.Identity);
        if (!string.Equals(proof.FamilyKey, expectedKey, StringComparison.Ordinal) ||
            proof.DeliveredRowCount != rows.Count)
        {
            return EuFormexManifestationEnumerationResult.Refused(
                expression, EuFormexManifestationEnumerationRefusal.EnumerationProofRefused,
                "The proof must name this expression family and exactly this delivered row count.",
                productRequestCount, wireBudget);
        }
        var types = new List<EuExpressionManifestationType>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            EuExpressionManifestationType decoded;
            try
            {
                decoded = DecodeRow(row, profile, expression.Identity, proof.AcquisitionRunRef.ResourceId);
            }
            catch (WrongExpressionException exception)
            {
                return EuFormexManifestationEnumerationResult.Refused(
                    expression, EuFormexManifestationEnumerationRefusal.RowNamesAnotherExpression,
                    exception.Message, productRequestCount, wireBudget);
            }
            catch (ArgumentException exception)
            {
                return EuFormexManifestationEnumerationResult.Refused(
                    expression, EuFormexManifestationEnumerationRefusal.RowNotAdmitted,
                    exception.Message, productRequestCount, wireBudget);
            }
            if (!seen.Add(decoded.PublisherType))
            {
                return EuFormexManifestationEnumerationResult.Refused(
                    expression, EuFormexManifestationEnumerationRefusal.ManifestationTypeDeliveredTwice,
                    decoded.PublisherType, productRequestCount, wireBudget);
            }
            types.Add(decoded);
        }
        return EuFormexManifestationEnumerationResult.Success(
            expression, types, proof, productRequestCount, wireBudget);
    }

    private static EuExpressionManifestationType DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        LanguageScopedExpressionIdentity identity,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count)
        {
            throw new ArgumentException("A manifestation row has exactly the profile's terms.", nameof(row));
        }
        var work = Term(row, profile, "work");
        var expression = Term(row, profile, "expression");
        if (work.Kind != RepeatedEnumerationRdfTermKind.Iri ||
            expression.Kind != RepeatedEnumerationRdfTermKind.Iri ||
            !string.Equals(work.Value, identity.PublisherWorkId, StringComparison.Ordinal) ||
            !string.Equals(expression.Value, identity.PublisherExpressionId, StringComparison.Ordinal))
        {
            throw new WrongExpressionException(
                $"The row names work '{work.Value}' and expression '{expression.Value}', not the selected expression.");
        }

        var type = Term(row, profile, "manifestation_type");
        if (type.Kind != RepeatedEnumerationRdfTermKind.Literal || string.IsNullOrEmpty(type.Value) ||
            type.Language is not null || type.Datatype is not (null or XsdString))
        {
            throw new ArgumentException("A manifestation type is a non-empty plain or xsd:string literal.", nameof(row));
        }
        RequirePlainLiteral(row, profile, "manifestation_type_kind", "literal");
        RequirePlainLiteral(row, profile, "datatype_iri", type.Datatype ?? string.Empty);
        RequirePlainLiteral(row, profile, "language_tag", string.Empty);
        var multiplicity = PositiveInteger(Term(row, profile, "multiplicity"));
        RequirePlainLiteral(row, profile, "key_1", "literal");
        RequirePlainLiteral(row, profile, "key_2", type.Value);
        RequirePlainLiteral(row, profile, "key_3", type.Datatype ?? string.Empty);
        RequirePlainLiteral(row, profile, "key_4", string.Empty);
        return new EuExpressionManifestationType(type.Value, multiplicity, observationId);
    }

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row, RepeatedEnumerationInterpretationProfile profile, string name)
    {
        var index = profile.ProjectionVariables.ToList().IndexOf(name);
        return index < 0 ? throw new ArgumentException($"The profile does not project {name}.", nameof(profile))
            : row.Terms[index];
    }

    private static void RequirePlainLiteral(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name,
        string expected)
    {
        var term = Term(row, profile, name);
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Datatype is not null ||
            term.Language is not null || !string.Equals(term.Value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} does not describe the delivered manifestation type.", nameof(row));
        }
    }

    private static long PositiveInteger(RepeatedEnumerationRdfTerm term)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Datatype != XsdInteger ||
            term.Language is not null || !long.TryParse(term.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new ArgumentException("multiplicity is a positive xsd:integer.", nameof(term));
        }
        return value;
    }

    private sealed class WrongExpressionException(string message) : ArgumentException(message);
}
