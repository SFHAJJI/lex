using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Why <see cref="EuFormexPackagePopulationProducer.RunAsync"/> delivered no reconciliation. Closed.
/// Every member names the gate that refused, and the detail carries that gate's own reason.
/// </summary>
public enum EuFormexPackagePopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The run refused, is not proven for every family, or carries no expression productions.</summary>
    [JsonStringEnumMemberName("run_not_complete")]
    RunNotComplete = 1,

    /// <summary>
    /// An expression of the run is not the exact numeric child Cellar IRI of its work, so no
    /// manifestation enumeration can be bound for it (<see cref="EuFormexManifestationRunRequest"/>).
    /// </summary>
    [JsonStringEnumMemberName("expression_selection_invalid")]
    ExpressionSelectionInvalid = 2,

    /// <summary><see cref="EuFormexEligibilityPopulation.TryCreate"/> refused a family's enumerations.</summary>
    [JsonStringEnumMemberName("eligibility_refused")]
    EligibilityRefused = 3,

    /// <summary><see cref="EuFormexPackageOutcomePopulation.TryClose"/> refused a family's outcomes.</summary>
    [JsonStringEnumMemberName("outcome_population_refused")]
    OutcomePopulationRefused = 4,

    /// <summary><see cref="EuFormexRunOutcomeReconciliation.TryClose"/> refused the run's populations.</summary>
    [JsonStringEnumMemberName("reconciliation_refused")]
    ReconciliationRefused = 5,
}

/// <summary>The run's Formex reconciliation, or one typed refusal; every enumeration attempted travels with either.</summary>
public sealed class EuFormexPackagePopulationResult
{
    private EuFormexPackagePopulationResult(
        EuFormexRunOutcomeReconciliation? reconciliation,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        int eligibleExpressionCount,
        int productRequestCount,
        EuFormexPackagePopulationRefusal? refusal,
        string? detail)
    {
        Reconciliation = reconciliation;
        Enumerations = enumerations;
        EligibleExpressionCount = eligibleExpressionCount;
        ProductRequestCount = productRequestCount;
        Refusal = refusal;
        Detail = detail;
    }

    /// <summary>
    /// The value <c>Stage3EvidenceEnvelope</c> and <see cref="EuFormexMainBodyLegalContentProducer"/>
    /// take, bound by reference to the run it was produced for. Present iff delivered.
    /// </summary>
    public EuFormexRunOutcomeReconciliation? Reconciliation { get; }

    /// <summary>
    /// Every manifestation enumeration this run attempted, in production order: the publisher
    /// facts behind each outcome and the wire accounting, kept whether or not the run closed.
    /// </summary>
    public IReadOnlyList<EuFormexManifestationEnumerationResult> Enumerations { get; }

    /// <summary>How many enumerated expressions list a Formex manifestation.</summary>
    public int EligibleExpressionCount { get; }

    /// <summary>Publisher requests the enumerations spent, robots excluded.</summary>
    public int ProductRequestCount { get; }

    public EuFormexPackagePopulationRefusal? Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Reconciliation is not null;

    public static EuFormexPackagePopulationResult Success(
        EuFormexRunOutcomeReconciliation reconciliation,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        int eligibleExpressionCount,
        int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        ArgumentNullException.ThrowIfNull(enumerations);
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleExpressionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(productRequestCount);
        return new(
            reconciliation, Array.AsReadOnly(enumerations.ToArray()), eligibleExpressionCount,
            productRequestCount, null, null);
    }

    public static EuFormexPackagePopulationResult Refused(
        EuFormexPackagePopulationRefusal refusal,
        string detail,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        int productRequestCount)
    {
        if (refusal == EuFormexPackagePopulationRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ArgumentNullException.ThrowIfNull(enumerations);
        ArgumentOutOfRangeException.ThrowIfNegative(productRequestCount);
        return new(null, Array.AsReadOnly(enumerations.ToArray()), 0, productRequestCount, refusal, detail);
    }
}

/// <summary>
/// Produces the Formex package population of one complete EU run: one typed outcome per expression
/// the run observed, closed per family and reconciled against the run, so the Stage 3 envelope can
/// be composed from a real run rather than from the offline fixture composition tests hold.
/// </summary>
/// <remarks>
/// <para>
/// What it does for real: for every expression of every production the run holds, it runs the
/// manifestation enumeration (<see cref="EuFormexManifestationEnumerationProducer"/>, its own robots
/// session, two count and page passes, retained evidence), so "this expression has a Formex
/// manifestation" and "this expression has none" are both publisher facts with proofs, never
/// assumptions. Every language the publisher lists is enumerated: the eligibility population
/// requires exactly one delivered enumeration per expression of the production, and the run's
/// own expression count is what the reconciliation checks against.
/// </para>
/// <para>
/// What it does not do yet, stated as the outcome itself states it: it sends no package request.
/// An <c>Acquired</c> outcome needs an <see cref="EuFormexPackage"/>, and a package needs the
/// manifestation's Cellar items observed with their stream names, through an item enumeration this
/// build does not have; the transport binding also pins a request shape (one Item URI, one hop)
/// that the live fmx4 route (manifestation URI, 303 to <c>{manifestation}/zip</c>) does not take.
/// Until both exist, an eligible expression is <c>Refused(observation_not_executed)</c> with
/// <see cref="DeferredAcquisitionDetail"/> as its detail, which is exactly the idiom the reference
/// composition (<c>EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope</c>) uses and which
/// the corpus builder already accepts. The corpus built from it carries EU records without Formex
/// articles, as the served product does today.
/// </para>
/// <para>
/// What the corpus says about it, stated exactly: the main-body producer maps this outcome to
/// <c>PackageRefused</c>, and <c>LexCorpus6Builder</c> writes the held EU member's stage 3 outcome
/// as <c>europe_formex_main_body</c> / <c>formex_main_body_package_refused</c>, with no detail text
/// (the record has none; the detail enters only the outcome's semantic identity hash). In the
/// corpus file a deferred acquisition is therefore indistinguishable from a package the transport
/// really refused. That is acceptable for the first mount, which serves no stage 3 outcome, and it
/// is why a typed deferred outcome member is the first Formex slice after the mount, before any
/// public claim rests on this field (STATUS.md). The builder test pins today's shape so the change
/// is a visible test edit.
/// </para>
/// </remarks>
public sealed class EuFormexPackagePopulationProducer
{
    /// <summary>
    /// The detail carried by every eligible expression's in-process outcome while package
    /// acquisition is not built. One fixed string, so the outcome objects a run produces can be
    /// told apart from real transport refusals and a later slice can find every such outcome. It
    /// does not reach the corpus file; see the type remarks.
    /// </summary>
    public const string DeferredAcquisitionDetail =
        "formex package acquisition deferred: no package request was sent; the package needs the "
        + "manifestation's Cellar items observed with their stream names (item enumeration not built) "
        + "and a transport binding that admits the manifestation-level fmx4 route";

    private readonly EuFormexManifestationEnumerationProducer _enumerations;

    public EuFormexPackagePopulationProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>Test-only seam, the same one every Europe producer declares.</summary>
    internal EuFormexPackagePopulationProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _enumerations = new EuFormexManifestationEnumerationProducer(custodyStore, timeProvider, testHandlerOverride);
    }

    /// <param name="run">The complete EU run whose expressions are populated; the reconciliation binds to it by reference.</param>
    /// <param name="manifestationRendererSource">The renderer-source artifact for the manifestation enumeration.</param>
    /// <param name="sourceWitness">The bound SPARQL witness each enumeration session starts from.</param>
    /// <param name="wireBudget">One ceiling for every enumeration of this run, robots included.</param>
    public async Task<EuFormexPackagePopulationResult> RunAsync(
        EuQueryExecutionResult run,
        MachineQueryRendererSource manifestationRendererSource,
        BoundMachineRequest sourceWitness,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(manifestationRendererSource);
        ArgumentNullException.ThrowIfNull(sourceWitness);
        ArgumentNullException.ThrowIfNull(wireBudget);

        var enumerations = new List<EuFormexManifestationEnumerationResult>();
        var productRequests = 0;

        // The same completeness the reconciliation demands, checked before any traffic: an
        // incomplete run cannot be reconciled, so enumerating its expressions would spend the
        // publisher's requests on a result that is refused regardless.
        if (run.Refusal is not null
            || run.Completion != EuQueryExecutionCompletion.AllFamiliesProven
            || run.CorrigendumTripwires is null)
        {
            return EuFormexPackagePopulationResult.Refused(
                EuFormexPackagePopulationRefusal.RunNotComplete,
                run.Refusal is { } refused
                    ? $"the run refused: {refused.Code}"
                    : "the run does not carry complete family and tripwire accounting",
                enumerations,
                productRequests);
        }

        // Every request is bound before the first one is sent. Binding is pure (it canonicalizes
        // the expression's selection and nothing else), so an expression that cannot be enumerated
        // refuses the run before any family's traffic is spent on a result that is refused anyway.
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        var families = new List<(string FamilyKey, EuLanguageScopedExpressionProductionResult Production,
            IReadOnlyList<EuFormexManifestationRunRequest> Requests)>();
        foreach (var (familyKey, production) in run.CorrigendumTripwires.ProductionsByFamilyKey
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var expressions = production.Expressions;
            if (expressions?.Derivation is null)
            {
                return EuFormexPackagePopulationResult.Refused(
                    EuFormexPackagePopulationRefusal.RunNotComplete,
                    $"family {familyKey} carries no delivered expression production",
                    enumerations,
                    productRequests);
            }

            var requests = new List<EuFormexManifestationRunRequest>(expressions.Derivation.Expressions.Count);
            foreach (var expression in expressions.Derivation.Expressions)
            {
                try
                {
                    requests.Add(new EuFormexManifestationRunRequest(
                        plan, expression, NewUrn(), manifestationRendererSource, wireBudget));
                }
                catch (ArgumentException exception)
                {
                    return EuFormexPackagePopulationResult.Refused(
                        EuFormexPackagePopulationRefusal.ExpressionSelectionInvalid,
                        $"family {familyKey}: {expression.Identity.PublisherExpressionId}: {exception.Message}",
                        enumerations,
                        productRequests);
                }
            }

            families.Add((familyKey, expressions, requests));
        }

        var populations = new List<EuFormexPackageOutcomePopulation>(families.Count);
        var eligible = 0;
        foreach (var (familyKey, expressions, requests) in families)
        {
            var batch = new List<EuFormexManifestationEnumerationResult>(requests.Count);
            foreach (var request in requests)
            {
                var enumeration = await _enumerations.RunAsync(request, sourceWitness, cancellationToken)
                    .ConfigureAwait(false);
                productRequests += enumeration.ProductRequestCount;
                batch.Add(enumeration);
                enumerations.Add(enumeration);
            }

            var eligibility = EuFormexEligibilityPopulation.TryCreate(
                expressions, batch, out var eligibilityRefusal, out var eligibilityDetail);
            if (eligibility is null)
            {
                return EuFormexPackagePopulationResult.Refused(
                    EuFormexPackagePopulationRefusal.EligibilityRefused,
                    $"family {familyKey}: {eligibilityRefusal}: {eligibilityDetail}",
                    enumerations,
                    productRequests);
            }

            var outcomes = new List<EuFormexPackageOutcome>(eligibility.Enumerations.Count);
            foreach (var enumeration in eligibility.Enumerations)
            {
                if (!enumeration.IsFormexEligible)
                {
                    outcomes.Add(EuFormexPackageOutcome.NotEligible(enumeration.Expression));
                    continue;
                }

                eligible++;
                outcomes.Add(EuFormexPackageOutcome.Refused(
                    enumeration.Expression,
                    EuDocumentFetchAttemptRefusal.ObservationNotExecuted,
                    DeferredAcquisitionDetail));
            }

            var population = EuFormexPackageOutcomePopulation.TryClose(
                eligibility, outcomes, out var outcomeRefusal, out var outcomeDetail);
            if (population is null)
            {
                return EuFormexPackagePopulationResult.Refused(
                    EuFormexPackagePopulationRefusal.OutcomePopulationRefused,
                    $"family {familyKey}: {outcomeRefusal}: {outcomeDetail}",
                    enumerations,
                    productRequests);
            }

            populations.Add(population);
        }

        var reconciliation = EuFormexRunOutcomeReconciliation.TryClose(
            run, populations, out var reconciliationRefusal, out var reconciliationDetail);
        if (reconciliation is null)
        {
            return EuFormexPackagePopulationResult.Refused(
                EuFormexPackagePopulationRefusal.ReconciliationRefused,
                $"{reconciliationRefusal}: {reconciliationDetail}",
                enumerations,
                productRequests);
        }

        return EuFormexPackagePopulationResult.Success(reconciliation, enumerations, eligible, productRequests);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
