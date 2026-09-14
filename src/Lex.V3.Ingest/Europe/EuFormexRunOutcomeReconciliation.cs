using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why one EU run could not be reconciled with its Formex outcome populations.</summary>
public enum EuFormexRunOutcomeReconciliationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("run_not_complete")]
    RunNotComplete = 1,

    [JsonStringEnumMemberName("run_expression_production_invalid")]
    RunExpressionProductionInvalid = 2,

    [JsonStringEnumMemberName("population_outside_run")]
    PopulationOutsideRun = 3,

    [JsonStringEnumMemberName("population_production_disagrees")]
    PopulationProductionDisagrees = 4,

    [JsonStringEnumMemberName("population_supplied_twice")]
    PopulationSuppliedTwice = 5,

    [JsonStringEnumMemberName("population_missing")]
    PopulationMissing = 6,

    [JsonStringEnumMemberName("expression_claimed_twice")]
    ExpressionClaimedTwice = 7,

    [JsonStringEnumMemberName("run_expression_count_disagrees")]
    RunExpressionCountDisagrees = 8,
}

/// <summary>
/// Binds every Formex package outcome population to the expression productions embedded in one
/// delivered EU run, then proves that their distinct expressions are the population that run says
/// it observed.
/// </summary>
/// <remarks>
/// The run does not expose its raw per-expression identity set. It does retain the same execution's
/// complete expression productions inside <see cref="EuQueryExecutionResult.CorrigendumTripwires"/>,
/// one per proven Expression-facts batch. Those productions are therefore the expectation. A caller
/// supplies only the guarded outcome populations to reconcile against it; it cannot supply or
/// shorten the expected batch or expression list.
/// </remarks>
public sealed class EuFormexRunOutcomeReconciliation
{
    private EuFormexRunOutcomeReconciliation(
        EuQueryExecutionResult run,
        IReadOnlyDictionary<string, EuFormexPackageOutcomePopulation> populationsByFamilyKey,
        IReadOnlyList<EuFormexPackageOutcome> outcomes)
    {
        Run = run;
        PopulationsByFamilyKey = populationsByFamilyKey;
        Outcomes = outcomes;
    }

    public EuQueryExecutionResult Run { get; }

    /// <summary>Exactly one guarded outcome population per run-proven Expression-facts batch.</summary>
    public IReadOnlyDictionary<string, EuFormexPackageOutcomePopulation> PopulationsByFamilyKey { get; }

    /// <summary>Exactly one outcome per distinct run-observed expression, ordered by batch then production.</summary>
    public IReadOnlyList<EuFormexPackageOutcome> Outcomes { get; }

    public int ExpressionCount => Outcomes.Count;

    public static EuFormexRunOutcomeReconciliation? TryClose(
        EuQueryExecutionResult run,
        IReadOnlyList<EuFormexPackageOutcomePopulation> populations,
        out EuFormexRunOutcomeReconciliationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(populations);
        refusal = EuFormexRunOutcomeReconciliationRefusal.None;
        detail = null;

        if (run.Refusal is not null
            || run.Completion != EuQueryExecutionCompletion.AllFamiliesProven
            || run.CorrigendumTripwires is null)
        {
            refusal = EuFormexRunOutcomeReconciliationRefusal.RunNotComplete;
            detail = run.Refusal is { } refused
                ? $"the run refused: {refused.Code}"
                : "the run does not carry complete family and tripwire accounting";
            return null;
        }

        var expected = new Dictionary<string, EuLanguageScopedExpressionProductionResult>(StringComparer.Ordinal);
        var expressionIdentities = new HashSet<LanguageScopedExpressionIdentity>();
        foreach (var (familyKey, production) in run.CorrigendumTripwires.ProductionsByFamilyKey)
        {
            var expressions = production.Expressions;
            if (!production.Delivered || expressions is null || !expressions.Delivered
                || expressions.Derivation is null
                || expressions.RetainedDerivation is null
                || expressions.RetainedEpisode is null
                || !string.Equals(
                    expressions.RetainedDerivation.Reference.ContentSha256,
                    expressions.Derivation.DerivationSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expressions.RetainedEpisode.Reference.ContentSha256,
                    expressions.Derivation.EpisodeSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expressions.Derivation.ExpressionFactsProof.FamilyKey,
                    familyKey,
                    StringComparison.Ordinal))
            {
                refusal = EuFormexRunOutcomeReconciliationRefusal.RunExpressionProductionInvalid;
                detail = familyKey;
                return null;
            }

            expected.Add(familyKey, expressions);
            foreach (var expression in expressions.Derivation.Expressions)
            {
                if (!expressionIdentities.Add(expression.Identity))
                {
                    refusal = EuFormexRunOutcomeReconciliationRefusal.ExpressionClaimedTwice;
                    detail = expression.Identity.PublisherExpressionId;
                    return null;
                }
            }
        }

        var delivered = new Dictionary<string, EuFormexPackageOutcomePopulation>(StringComparer.Ordinal);
        foreach (var population in populations)
        {
            ArgumentNullException.ThrowIfNull(population, nameof(populations));
            var supplied = population.Eligibility.ExpressionProduction;
            var derivation = supplied.Derivation;
            if (!supplied.Delivered || derivation is null)
            {
                refusal = EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees;
                detail = "a supplied population has no delivered expression production";
                return null;
            }

            var familyKey = derivation.ExpressionFactsProof.FamilyKey;
            if (!expected.TryGetValue(familyKey, out var runProduction))
            {
                refusal = EuFormexRunOutcomeReconciliationRefusal.PopulationOutsideRun;
                detail = familyKey;
                return null;
            }

            if (!SameProduction(runProduction, supplied))
            {
                refusal = EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees;
                detail = familyKey;
                return null;
            }

            if (!delivered.TryAdd(familyKey, population))
            {
                refusal = EuFormexRunOutcomeReconciliationRefusal.PopulationSuppliedTwice;
                detail = familyKey;
                return null;
            }
        }

        var missing = expected.Keys.OrderBy(static key => key, StringComparer.Ordinal)
            .FirstOrDefault(key => !delivered.ContainsKey(key));
        if (missing is not null)
        {
            refusal = EuFormexRunOutcomeReconciliationRefusal.PopulationMissing;
            detail = missing;
            return null;
        }

        if (expressionIdentities.Count != run.ObservedExpressionCount)
        {
            refusal = EuFormexRunOutcomeReconciliationRefusal.RunExpressionCountDisagrees;
            detail = $"the embedded productions name {expressionIdentities.Count} distinct expressions but the run reports "
                + run.ObservedExpressionCount;
            return null;
        }

        var ordered = delivered.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
        return new EuFormexRunOutcomeReconciliation(
            run,
            new ReadOnlyDictionary<string, EuFormexPackageOutcomePopulation>(
                ordered.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
            Array.AsReadOnly(ordered.SelectMany(static pair => pair.Value.Outcomes).ToArray()));
    }

    private static bool SameProduction(
        EuLanguageScopedExpressionProductionResult expected,
        EuLanguageScopedExpressionProductionResult supplied) =>
        expected.Derivation is { } expectedDerivation
        && supplied.Derivation is { } suppliedDerivation
        && expected.RetainedDerivation is { } expectedRetainedDerivation
        && supplied.RetainedDerivation is { } suppliedRetainedDerivation
        && expected.RetainedEpisode is { } expectedRetainedEpisode
        && supplied.RetainedEpisode is { } suppliedRetainedEpisode
        && string.Equals(expectedDerivation.DerivationSha256, suppliedDerivation.DerivationSha256,
            StringComparison.Ordinal)
        && string.Equals(expectedDerivation.EpisodeSha256, suppliedDerivation.EpisodeSha256,
            StringComparison.Ordinal)
        && string.Equals(
            DurableBlobWriteReceiptDigest.Of(expectedRetainedDerivation),
            DurableBlobWriteReceiptDigest.Of(suppliedRetainedDerivation),
            StringComparison.Ordinal)
        && string.Equals(
            DurableBlobWriteReceiptDigest.Of(expectedRetainedEpisode),
            DurableBlobWriteReceiptDigest.Of(suppliedRetainedEpisode),
            StringComparison.Ordinal)
        && expected.ObjectsAskedAbout is { } expectedObjects
        && supplied.ObjectsAskedAbout is { } suppliedObjects
        && expectedObjects.SetEquals(suppliedObjects);
}
