using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts;
using System.Text.Json;
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

    /// <summary>The successful population's original associations could not be retained.</summary>
    [JsonStringEnumMemberName("checkpoint_not_retained")]
    CheckpointNotRetained = 6,
}

/// <summary>The run's Formex reconciliation, or one typed refusal; every enumeration attempted travels with either.</summary>
public sealed class EuFormexPackagePopulationResult
{
    private EuFormexPackagePopulationResult(
        EuFormexRunOutcomeReconciliation? reconciliation,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        IReadOnlyList<EuBoundAnnexBodyClassification> annexClassifications,
        int eligibleExpressionCount,
        int productRequestCount,
        EuFormexPackagePopulationRefusal? refusal,
        string? detail)
    {
        Reconciliation = reconciliation;
        Enumerations = enumerations;
        AnnexClassifications = annexClassifications;
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

    public SourceArtifactRef? CheckpointRef { get; private init; }

    internal EuFormexPackagePopulationResult WithCheckpoint(SourceArtifactRef reference) =>
        new(Reconciliation, Enumerations, AnnexClassifications, EligibleExpressionCount, ProductRequestCount, Refusal, Detail)
            { Acquisitions = this.Acquisitions, CheckpointRef = reference };


    /// <summary>Successful population's original package results, retaining their checkpoint associations.</summary>
    public IReadOnlyList<EuFormexPackageAcquisitionResult> Acquisitions { get; private init; } = [];

    internal EuFormexPackagePopulationResult WithAcquisitions(IReadOnlyList<EuFormexPackageAcquisitionResult> acquisitions)
    {
        ArgumentNullException.ThrowIfNull(acquisitions);
        if (Reconciliation is null || acquisitions.Any(acquisition => acquisition is null ||
            !Reconciliation.Outcomes.Any(outcome => ReferenceEquals(outcome, acquisition.Outcome))) ||
            acquisitions.Select(static acquisition => acquisition.Outcome.ExpressionIdentity).Distinct().Count() != acquisitions.Count)
            throw new ArgumentException("Package results must belong to this exact successful population.", nameof(acquisitions));
        return new(Reconciliation, Enumerations, AnnexClassifications, EligibleExpressionCount, ProductRequestCount, Refusal, Detail)
            { Acquisitions = Array.AsReadOnly(acquisitions.ToArray()), CheckpointRef = this.CheckpointRef };
    }


    /// <summary>
    /// Every manifestation enumeration this run attempted, in production order: the publisher
    /// facts behind each outcome and the wire accounting, kept whether or not the run closed.
    /// </summary>
    public IReadOnlyList<EuFormexManifestationEnumerationResult> Enumerations { get; }

    /// <summary>
    /// One classification per acquired package that names annexes, in outcome order: what the
    /// classification reconciliation closes over (<see cref="EuFormexAnnexClassificationReconciliation.TryClose"/>).
    /// </summary>
    public IReadOnlyList<EuBoundAnnexBodyClassification> AnnexClassifications { get; }

    /// <summary>How many enumerated expressions list a Formex manifestation.</summary>
    public int EligibleExpressionCount { get; }

    /// <summary>Publisher requests the enumerations and the package fetches spent, robots excluded.</summary>
    public int ProductRequestCount { get; }

    /// <summary>Expressions whose package was acquired, bound and inventoried.</summary>
    public int AcquiredExpressionCount => Reconciliation?.Outcomes.Count(static outcome =>
        outcome.Kind == EuFormexPackageOutcomeKind.Acquired) ?? 0;

    /// <summary>Expressions explicitly left unenumerated because their language is outside EN/FRA.</summary>
    public int NotEnumeratedExpressionCount => Reconciliation?.Outcomes.Count(static outcome =>
        outcome.Kind == EuFormexPackageOutcomeKind.NotEnumeratedLanguageOutOfScope) ?? 0;

    public EuFormexPackagePopulationRefusal? Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Reconciliation is not null;

    /// <summary>
    /// Diagnostic projection of the existing reconciliation for retained CLI logs. It creates no
    /// proof or acquisition outcome. On refusal, the expression total is unknown and outcomes null.
    /// Enum values use their declared wire codes; publisher identities and details are JSON-escaped.
    /// </summary>
    public string CreateOutcomeDiagnosticsJson() => JsonSerializer.Serialize(new
    {
        schema = "lex-v3-eu-formex-outcome-diagnostic/2",
        delivered = Delivered,
        expression_count = Reconciliation?.ExpressionCount,
        enumerated_count = Enumerations.Count,
        successful_enumeration_count = Enumerations.Count(static enumeration => enumeration.Delivered),
        failed_enumerations = Enumerations.Where(static enumeration => !enumeration.Delivered).Select(static enumeration => new
        {
            work = enumeration.ExpressionIdentity.PublisherWorkId,
            expression = enumeration.ExpressionIdentity.PublisherExpressionId,
            language = enumeration.Expression.OfficialLanguage,
            refusal = ContractWire.NameOf(enumeration.Refusal),
            detail = enumeration.Detail,
            product_request_count = enumeration.ProductRequestCount,
        }),
        eligible_count = Delivered ? (int?)EligibleExpressionCount : null,
        acquired_count = Delivered ? (int?)AcquiredExpressionCount : null,
        not_enumerated_language_out_of_scope_count = Delivered ? (int?)NotEnumeratedExpressionCount : null,
        refusal = Refusal is { } refusal ? ContractWire.NameOf(refusal) : null,
        detail = Detail,
        outcomes = Reconciliation?.Outcomes.Select(static outcome => new
        {
            work = outcome.ExpressionIdentity.PublisherWorkId,
            expression = outcome.ExpressionIdentity.PublisherExpressionId,
            language = outcome.Expression.OfficialLanguage,
            kind = ContractWire.NameOf(outcome.Kind),
            not_acquired_reason = outcome.NotAcquiredReason == EuFormexPackageNotAcquiredReason.None
                ? null : ContractWire.NameOf(outcome.NotAcquiredReason),
            package_refusal = outcome.PackageRefusal == EuFormexAnnexInventoryRefusal.None
                ? null : ContractWire.NameOf(outcome.PackageRefusal),
            acquisition_refusal = outcome.AcquisitionRefusal is { } acquisitionRefusal ? ContractWire.NameOf(acquisitionRefusal) : null,
            unavailable_reason = outcome.UnavailableReason is { } unavailableReason ? ContractWire.NameOf(unavailableReason) : null,
            observed_status = outcome.ObservedStatus,
            retained_package_sha256 = outcome.AcquiredInventory?.SourceReceipt.Reference.ContentSha256,
            detail = outcome.Detail,
        }),
    });


    public static EuFormexPackagePopulationResult Success(
        EuFormexRunOutcomeReconciliation reconciliation,
        IReadOnlyList<EuFormexManifestationEnumerationResult> enumerations,
        IReadOnlyList<EuBoundAnnexBodyClassification> annexClassifications,
        int eligibleExpressionCount,
        int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        ArgumentNullException.ThrowIfNull(enumerations);
        ArgumentNullException.ThrowIfNull(annexClassifications);
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleExpressionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(productRequestCount);
        return new(
            reconciliation, Array.AsReadOnly(enumerations.ToArray()), Array.AsReadOnly(annexClassifications.ToArray()),
            eligibleExpressionCount, productRequestCount, null, null);
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
        return new(null, Array.AsReadOnly(enumerations.ToArray()), [], 0, productRequestCount, refusal, detail);
    }
}

/// <summary>
/// Produces the Formex package population of one complete EU run: one typed outcome per expression
/// the run observed, closed per family and reconciled against the run, so the Stage 3 envelope can
/// be composed from a real run rather than from the offline fixture composition tests hold.
/// </summary>
/// <remarks>
/// <para>
/// What it does for real: for every English or French expression of every production the run holds, it runs the
/// manifestation enumeration (<see cref="EuFormexManifestationEnumerationProducer"/>, its own robots
/// session, two count and page passes, retained evidence), so "this expression has a Formex
/// manifestation" and "this expression has none" are both publisher facts with proofs, never
/// assumptions for English and French. Other languages receive an explicit unenumerated,
/// language-out-of-scope outcome with no eligibility claim. The reconciliation still checks
/// the complete run expression count, including these outcomes.
/// </para>
/// <para>
/// Then, for every eligible expression, it acquires the package
/// (<see cref="EuFormexPackageAcquisitionProducer"/>): the one GET of the exact <c>fmx4</c>
/// manifestation the enumeration delivered, through the acquisition session, bound as a package
/// transport and read into an annex inventory. What is not acquired is stated as its own outcome
/// kind with its reason (<c>not_acquired</c>: the run holds no body for an enumerated expression;
/// an identity or manifestation the grammar refuses;
/// an annex-bearing package whose annex chain does not close), and a publisher answer that is not
/// a package is <c>route_refused</c> or <c>package_rejected</c>, never a transport refusal it was
/// not. A package that names annexes is acquired together with the classification of those annexes
/// against the held XHTML body and the work's PDF (one more GET), which travels on the result to
/// the classification reconciliation. The corpus builder binds every outcome to exactly one held
/// EU body and writes the main-body disposition per member, so an acquired English package reaches
/// the corpus as <c>formex_main_body_admitted</c> with its articles in the Europe index.
/// </para>
/// </remarks>
public sealed partial class EuFormexPackagePopulationProducer
{
    private readonly ICustodyStore _custodyStore;
    private readonly EuFormexManifestationEnumerationProducer _enumerations;
    private readonly EuFormexPackageAcquisitionProducer _acquisitions;

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
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        ArgumentNullException.ThrowIfNull(timeProvider);
        _enumerations = new EuFormexManifestationEnumerationProducer(custodyStore, timeProvider, testHandlerOverride);
        _acquisitions = new EuFormexPackageAcquisitionProducer(custodyStore, timeProvider, testHandlerOverride);
    }

    /// <param name="run">The complete EU run whose expressions are populated; the reconciliation binds to it by reference.</param>
    /// <param name="manifestationRendererSource">The renderer-source artifact for the manifestation enumeration.</param>
    /// <param name="documentFetchRendererSource">The renderer-source artifact for the package and PDF GETs (the document-fetch plan).</param>
    /// <param name="workCelex">The CELEX of the work the run acquired, carried on every annex binding.</param>
    /// <param name="sourceWitness">The bound SPARQL witness each enumeration session starts from.</param>
    /// <param name="wireBudget">One ceiling for every enumeration, package and PDF request of this run, robots included.</param>
    public Task<EuFormexPackagePopulationResult> RunAsync(
        EuQueryExecutionResult run,
        MachineQueryRendererSource manifestationRendererSource,
        MachineQueryRendererSource documentFetchRendererSource,
        string workCelex,
        BoundMachineRequest sourceWitness,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workCelex);
        return RunCoreAsync(run, manifestationRendererSource, documentFetchRendererSource,
            workCelex, sourceWitness, wireBudget, cancellationToken);
    }

    /// <summary>
    /// Acquires a combined run using original roots and census-proven consolidated Cellar work identities.
    /// Observed work CELEX values remain optional; another work's identifier never fills an absence.
    /// </summary>
    public Task<EuFormexPackagePopulationResult> RunAsync(
        EuQueryExecutionResult run,
        MachineQueryRendererSource manifestationRendererSource,
        MachineQueryRendererSource documentFetchRendererSource,
        BoundMachineRequest sourceWitness,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken) =>
        RunCoreAsync(run, manifestationRendererSource, documentFetchRendererSource,
            null, sourceWitness, wireBudget, cancellationToken);

    private async Task<EuFormexPackagePopulationResult> RunCoreAsync(
        EuQueryExecutionResult run,
        MachineQueryRendererSource manifestationRendererSource,
        MachineQueryRendererSource documentFetchRendererSource,
        string? workCelex,
        BoundMachineRequest? sourceWitness,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken,
        PopulationReplay? replay = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(manifestationRendererSource);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        if (replay is null) ArgumentNullException.ThrowIfNull(sourceWitness);
        var context = replay ?? new PopulationReplay(null);
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
            IReadOnlyList<(LanguageScopedExpression Expression, EuFormexManifestationRunRequest? Request)> Requests)>();
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

            var requests = new List<(LanguageScopedExpression Expression, EuFormexManifestationRunRequest? Request)>(expressions.Derivation.Expressions.Count);
            foreach (var expression in expressions.Derivation.Expressions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!EuFormexEligibilityPopulation.IsServedLanguage(expression)) continue;
                try
                {
                    requests.Add((expression, context.IsReplay ? null : new EuFormexManifestationRunRequest(
                        plan, expression, NewUrn(), manifestationRendererSource, wireBudget)));
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
        var classifications = new List<EuBoundAnnexBodyClassification>();
        var acquisitions = new List<EuFormexPackageAcquisitionResult>();
        var eligible = 0;
        foreach (var (familyKey, expressions, requests) in families)
        {
            var batch = new List<EuFormexManifestationEnumerationResult>(requests.Count);
            foreach (var request in requests)
            {
                var enumeration = context.IsReplay
                    ? await context.EnumerateAsync(_custodyStore, familyKey, request.Expression, cancellationToken).ConfigureAwait(false)
                    : await _enumerations.RunAsync(request.Request!, sourceWitness!, cancellationToken).ConfigureAwait(false);
                if (!context.IsReplay && enumeration.Delivered) context.CaptureEnumeration(familyKey, enumeration);
                productRequests += enumeration.ProductRequestCount;
                batch.Add(enumeration);
                enumerations.Add(enumeration);
            }

            var eligibility = EuFormexEligibilityPopulation.TryCreateForServedLanguages(
                expressions, batch, out var eligibilityRefusal, out var eligibilityDetail);
            if (eligibility is null)
            {
                return EuFormexPackagePopulationResult.Refused(
                    EuFormexPackagePopulationRefusal.EligibilityRefused,
                    $"family {familyKey}: {eligibilityRefusal}: {eligibilityDetail}",
                    enumerations,
                    productRequests);
            }

            var outcomes = eligibility.NotEnumeratedExpressions
                .Select(EuFormexPackageOutcome.NotEnumeratedLanguageOutOfScope).ToList();
            foreach (var enumeration in eligibility.Enumerations)
            {
                if (!enumeration.IsFormexEligible)
                {
                    outcomes.Add(EuFormexPackageOutcome.NotEligible(enumeration.Expression));
                    continue;
                }

                eligible++;
                var expressionCelex = workCelex;
                if (expressionCelex is null)
                {
                    expressionCelex = EuObservedWorkIdentity.Resolve(run,
                        enumeration.Expression.Identity.PublisherWorkId, context.OriginalWorksOnly);
                    if (expressionCelex is null && (context.OriginalWorksOnly ||
                        !EuObservedWorkIdentity.IsProven(run, enumeration.Expression.Identity.PublisherWorkId)))
                    {
                        outcomes.Add(EuFormexPackageOutcome.NotAcquired(
                            enumeration.Expression, EuFormexPackageNotAcquiredReason.IdentityNotAdmitted,
                            context.OriginalWorksOnly
                                ? "the expression's work does not bind to exactly one reviewed Appendix A CELEX"
                                : "the expression's work has no admitted original root or proven census relation"));
                        continue;
                    }

                }

                var acquisition = context.IsReplay
                    ? await context.AcquireAsync(_custodyStore, enumeration, run, expressionCelex, documentFetchRendererSource, cancellationToken).ConfigureAwait(false)
                    : await _acquisitions.RunAsync(enumeration, run.CorpusRecordSet, expressionCelex,
                        documentFetchRendererSource, wireBudget, cancellationToken).ConfigureAwait(false);
                if (!context.IsReplay) context.CapturePackage(acquisition);
                acquisitions.Add(acquisition);
                productRequests += acquisition.ProductRequestCount;
                outcomes.Add(acquisition.Outcome);
                if (acquisition.AnnexClassification is { } classification)
                {
                    classifications.Add(classification);
                }
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

        var result = EuFormexPackagePopulationResult.Success(reconciliation, enumerations, classifications, eligible, productRequests)
            .WithAcquisitions(acquisitions);
        if (context.IsReplay)
        {
            context.RequireEnd();
            return result;
        }
        try
        {
            var checkpoint = await RetainPopulationAsync(run, result, manifestationRendererSource,
                documentFetchRendererSource, workCelex, context, cancellationToken).ConfigureAwait(false);
            return result.WithCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return EuFormexPackagePopulationResult.Refused(EuFormexPackagePopulationRefusal.CheckpointNotRetained,
                exception.Message, enumerations, productRequests);
        }
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
