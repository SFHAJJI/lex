using System.Text.Json.Serialization;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// The three package-acquisition terminals, plus the explicit disposition for a proven ineligible
/// expression that must remain visible in the whole expression population.
/// </summary>
public enum EuFormexPackageOutcomeKind
{
    /// <summary>The expression was proven ineligible by its completed manifestation enumeration.</summary>
    [JsonStringEnumMemberName("not_eligible")]
    NotEligible = 1,

    [JsonStringEnumMemberName("acquired")]
    Acquired = 2,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable = 3,

    [JsonStringEnumMemberName("refused")]
    Refused = 4,
}

/// <summary>
/// One expression's Formex disposition. For an eligible expression its payload shape is closed by
/// the factory used: acquired carries an inventory, unavailable carries the publisher's typed 404,
/// and refused carries the acquisition door's typed refusal. An ineligible expression carries none.
/// </summary>
public sealed class EuFormexPackageOutcome
{
    private EuFormexPackageOutcome(
        LanguageScopedExpression expression,
        EuFormexPackageOutcomeKind kind,
        EuFormexAnnexInventory? acquiredInventory,
        EuDocumentFetchRefusal? unavailableReason,
        int? observedStatus,
        EuDocumentFetchAttemptRefusal? acquisitionRefusal,
        string? detail)
    {
        Expression = expression;
        Kind = kind;
        AcquiredInventory = acquiredInventory;
        UnavailableReason = unavailableReason;
        ObservedStatus = observedStatus;
        AcquisitionRefusal = acquisitionRefusal;
        Detail = detail;
    }

    public LanguageScopedExpression Expression { get; }

    public LanguageScopedExpressionIdentity ExpressionIdentity => Expression.Identity;

    public EuFormexPackageOutcomeKind Kind { get; }

    public EuFormexAnnexInventory? AcquiredInventory { get; }

    public EuDocumentFetchRefusal? UnavailableReason { get; }

    public int? ObservedStatus { get; }

    public EuDocumentFetchAttemptRefusal? AcquisitionRefusal { get; }

    public string? Detail { get; }

    public static EuFormexPackageOutcome NotEligible(LanguageScopedExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return new EuFormexPackageOutcome(
            expression, EuFormexPackageOutcomeKind.NotEligible, null,
            null, null, null, null);
    }

    public static EuFormexPackageOutcome Acquired(
        LanguageScopedExpression expression,
        EuFormexAnnexInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(inventory);
        var acquiredExpression = inventory.TransportBinding.Expression;
        if (!string.Equals(
                acquiredExpression.PublisherUri,
                expression.Identity.PublisherExpressionId,
                StringComparison.Ordinal)
            || !string.Equals(
                acquiredExpression.CanonicalKeySha256,
                expression.SourceObject.CanonicalKeySha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The acquired Formex inventory does not belong to this expression.",
                nameof(inventory));
        }

        return new EuFormexPackageOutcome(
            expression, EuFormexPackageOutcomeKind.Acquired, inventory,
            null, null, null, null);
    }

    public static EuFormexPackageOutcome Unavailable(
        LanguageScopedExpression expression,
        EuDocumentFetchRefusal reason,
        int observedStatus)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (reason != EuDocumentFetchRefusal.RequestedRepresentationNotServed
            || observedStatus != 404)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason), reason,
                "A Formex package is unavailable only when its requested representation returned 404.");
        }

        return new EuFormexPackageOutcome(
            expression, EuFormexPackageOutcomeKind.Unavailable, null,
            reason, observedStatus, null, null);
    }

    public static EuFormexPackageOutcome Refused(
        LanguageScopedExpression expression,
        EuDocumentFetchAttemptRefusal refusal,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (!Enum.IsDefined(refusal) || refusal == EuDocumentFetchAttemptRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new EuFormexPackageOutcome(
            expression, EuFormexPackageOutcomeKind.Refused, null,
            null, null, refusal, detail);
    }
}

/// <summary>Why the proven Formex expression population could not be totally disposed.</summary>
public enum EuFormexPackageOutcomePopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("outcome_outside_expression_population")]
    OutcomeOutsideExpressionPopulation = 1,

    [JsonStringEnumMemberName("expression_content_disagrees")]
    ExpressionContentDisagrees = 2,

    [JsonStringEnumMemberName("expression_disposed_twice")]
    ExpressionDisposedTwice = 3,

    [JsonStringEnumMemberName("outcome_missing")]
    OutcomeMissing = 4,

    [JsonStringEnumMemberName("eligible_expression_marked_ineligible")]
    EligibleExpressionMarkedIneligible = 5,

    [JsonStringEnumMemberName("ineligible_expression_has_package_outcome")]
    IneligibleExpressionHasPackageOutcome = 6,
}

/// <summary>
/// The total package-acquisition disposition over one proof-complete Formex eligibility population.
/// </summary>
public sealed class EuFormexPackageOutcomePopulation
{
    private EuFormexPackageOutcomePopulation(
        EuFormexEligibilityPopulation eligibility,
        IReadOnlyList<EuFormexPackageOutcome> outcomes)
    {
        Eligibility = eligibility;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
    }

    public EuFormexEligibilityPopulation Eligibility { get; }

    /// <summary>
    /// Exactly one disposition per enumerated expression, in proven expression-population order.
    /// </summary>
    public IReadOnlyList<EuFormexPackageOutcome> Outcomes { get; }

    public int NotEligibleCount => Outcomes.Count(static outcome =>
        outcome.Kind == EuFormexPackageOutcomeKind.NotEligible);

    public int AcquiredCount => Outcomes.Count(static outcome =>
        outcome.Kind == EuFormexPackageOutcomeKind.Acquired);

    public int UnavailableCount => Outcomes.Count(static outcome =>
        outcome.Kind == EuFormexPackageOutcomeKind.Unavailable);

    public int RefusedCount => Outcomes.Count(static outcome =>
        outcome.Kind == EuFormexPackageOutcomeKind.Refused);

    public static EuFormexPackageOutcomePopulation? TryClose(
        EuFormexEligibilityPopulation eligibility,
        IReadOnlyList<EuFormexPackageOutcome> outcomes,
        out EuFormexPackageOutcomePopulationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(outcomes);
        refusal = EuFormexPackageOutcomePopulationRefusal.None;
        detail = null;

        var expected = eligibility.Enumerations.ToDictionary(
            static enumeration => enumeration.ExpressionIdentity);
        var delivered = new Dictionary<LanguageScopedExpressionIdentity, EuFormexPackageOutcome>();
        foreach (var outcome in outcomes)
        {
            ArgumentNullException.ThrowIfNull(outcome, nameof(outcomes));
            if (!expected.TryGetValue(outcome.ExpressionIdentity, out var enumeration))
            {
                refusal = EuFormexPackageOutcomePopulationRefusal.OutcomeOutsideExpressionPopulation;
                detail = outcome.ExpressionIdentity.PublisherExpressionId;
                return null;
            }

            if (!string.Equals(
                    enumeration.Expression.CanonicalContentSha256,
                    outcome.Expression.CanonicalContentSha256,
                    StringComparison.Ordinal))
            {
                refusal = EuFormexPackageOutcomePopulationRefusal.ExpressionContentDisagrees;
                detail = outcome.ExpressionIdentity.PublisherExpressionId;
                return null;
            }

            if (enumeration.IsFormexEligible
                && outcome.Kind == EuFormexPackageOutcomeKind.NotEligible)
            {
                refusal = EuFormexPackageOutcomePopulationRefusal.EligibleExpressionMarkedIneligible;
                detail = outcome.ExpressionIdentity.PublisherExpressionId;
                return null;
            }

            if (!enumeration.IsFormexEligible
                && outcome.Kind != EuFormexPackageOutcomeKind.NotEligible)
            {
                refusal = EuFormexPackageOutcomePopulationRefusal.IneligibleExpressionHasPackageOutcome;
                detail = outcome.ExpressionIdentity.PublisherExpressionId;
                return null;
            }

            if (!delivered.TryAdd(outcome.ExpressionIdentity, outcome))
            {
                refusal = EuFormexPackageOutcomePopulationRefusal.ExpressionDisposedTwice;
                detail = outcome.ExpressionIdentity.PublisherExpressionId;
                return null;
            }
        }

        var missing = eligibility.Enumerations.FirstOrDefault(enumeration =>
            !delivered.ContainsKey(enumeration.ExpressionIdentity));
        if (missing is not null)
        {
            refusal = EuFormexPackageOutcomePopulationRefusal.OutcomeMissing;
            detail = missing.ExpressionIdentity.PublisherExpressionId;
            return null;
        }

        return new EuFormexPackageOutcomePopulation(
            eligibility,
            eligibility.Enumerations.Select(enumeration => delivered[enumeration.ExpressionIdentity]).ToArray());
    }
}
