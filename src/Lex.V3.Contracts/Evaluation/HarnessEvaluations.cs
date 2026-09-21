namespace Lex.V3.Contracts.Evaluation;

/// <summary>What one run of the retrieval harness measured, and the gates it read.</summary>
public sealed record RetrievalReport
{
    public RetrievalReport(
        MetricResult anchorNdcgAt10, MetricResult noHitAccuracy, MetricResult resolverExactness, IReadOnlyList<GateResult> gates)
    {
        AnchorNdcgAt10 = anchorNdcgAt10 ?? throw new ArgumentNullException(nameof(anchorNdcgAt10));
        NoHitAccuracy = noHitAccuracy ?? throw new ArgumentNullException(nameof(noHitAccuracy));
        ResolverExactness = resolverExactness ?? throw new ArgumentNullException(nameof(resolverExactness));
        Gates = gates ?? throw new ArgumentNullException(nameof(gates));
    }

    public MetricResult AnchorNdcgAt10 { get; init; }

    public MetricResult NoHitAccuracy { get; init; }

    public MetricResult ResolverExactness { get; init; }

    public IReadOnlyList<GateResult> Gates { get; init; }

    /// <summary>The gates a retrieval release needs. This type's own declaration, so a harness that drops one is not read against what it kept.</summary>
    public IReadOnlyList<string> RequiredGates =>
        [EvaluationGateNames.AnchorNdcgAt10, EvaluationGateNames.NoHitAccuracy, EvaluationGateNames.ResolverExactness];

    /// <summary>Whether the run releases: each required gate is reported once and every gate passes.</summary>
    public bool Releases => EvaluationGates.ReleasePasses(Gates, RequiredGates);
}

/// <summary>What one run of the verdict harness measured, and the gate it read.</summary>
public sealed record VerdictReport
{
    public VerdictReport(MetricResult exactMatch, GateResult gate)
    {
        ExactMatch = exactMatch ?? throw new ArgumentNullException(nameof(exactMatch));
        Gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public MetricResult ExactMatch { get; init; }

    public GateResult Gate { get; init; }

    public IReadOnlyList<string> RequiredGates => [EvaluationGateNames.VerdictExactMatch];

    public bool Releases => EvaluationGates.ReleasePasses([Gate], RequiredGates);
}

/// <summary>What one run of the temporal harness measured, and the gate it read.</summary>
public sealed record TemporalReport
{
    public TemporalReport(MetricResult exactness, GateResult gate)
    {
        Exactness = exactness ?? throw new ArgumentNullException(nameof(exactness));
        Gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public MetricResult Exactness { get; init; }

    public GateResult Gate { get; init; }

    public IReadOnlyList<string> RequiredGates => [EvaluationGateNames.TemporalExactness];

    public bool Releases => EvaluationGates.ReleasePasses([Gate], RequiredGates);
}

/// <summary>
/// The retrieval harness: anchor nDCG, no-hit accuracy and resolver exactness over a set of cases, and the three gates
/// that read them. The two accuracies are invariants and gate at 1.0; nDCG gates at the threshold the caller declares.
/// Each stratum is measured only over the cases that belong to it, so a stratum with no case is not measured and never
/// a perfect score.
/// </summary>
public static class RetrievalEvaluation
{

    public static RetrievalReport Evaluate(
        IReadOnlyList<EvaluationCase> cases, RetrievalArm arm, int floor, double ndcgThreshold)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(arm);
        EnsureUnique(cases.Select(static value => value.CaseId));

        // The cutoff is ten, and an invariant gate reads 100 percent and nothing less.
        const int cutoff = 10;
        const double invariantThreshold = 1.0;
        var rankings = cases.ToDictionary(static value => value.CaseId, value => arm(value.CaseId), StringComparer.Ordinal);
        var ndcg = RetrievalMetrics.Mean(
            cases.Select(value => RetrievalMetrics.NdcgAtK(value.Judgments, rankings[value.CaseId], cutoff)).ToArray(),
            floor);
        var noHit = RetrievalMetrics.Mean(
            cases.Where(static value => !value.Judgments.Anchors.Any(static anchor => anchor.Grade > 0))
                .Select(value => MetricResult.Measured(rankings[value.CaseId].Count == 0 ? 1.0 : 0.0))
                .ToArray(),
            floor);
        var resolver = RetrievalMetrics.Mean(
            cases.Where(static value => value.Kind == EvaluationCaseKind.ExactIdentifier)
                .Select(value => MetricResult.Measured(ResolvesExactly(value, rankings[value.CaseId]) ? 1.0 : 0.0))
                .ToArray(),
            floor);

        return new RetrievalReport(
            ndcg,
            noHit,
            resolver,
            [
                EvaluationGates.AtLeast(EvaluationGateNames.AnchorNdcgAt10, ndcg, ndcgThreshold),
                EvaluationGates.AtLeast(EvaluationGateNames.NoHitAccuracy, noHit, invariantThreshold),
                EvaluationGates.AtLeast(EvaluationGateNames.ResolverExactness, resolver, invariantThreshold),
            ]);
    }

    // An exact-identifier case is resolved when the first result is its one supporting anchor. A case that does not have
    // exactly one supporting anchor is a defect of the dataset and counts as not resolved, never as skipped.
    private static bool ResolvesExactly(EvaluationCase value, IReadOnlyList<RankedAnchor> ranking)
    {
        var supporting = value.Judgments.Anchors.Where(static anchor => anchor.Grade == JudgedAnchor.SupportingGrade).ToArray();
        return supporting.Length == 1 &&
               ranking.Count > 0 &&
               string.Equals(ranking[0].WorkKey, supporting[0].WorkKey, StringComparison.Ordinal) &&
               string.Equals(ranking[0].AnchorId, supporting[0].AnchorId, StringComparison.Ordinal);
    }

    internal static void EnsureUnique(IEnumerable<string> caseIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var caseId in caseIds)
        {
            if (!seen.Add(caseId))
            {
                throw new ArgumentException($"Case '{caseId}' appears twice.", nameof(caseIds));
            }
        }
    }
}

/// <summary>The verdict harness: the share of cases whose emitted verdict is exactly the gold one, gated at 1.0.</summary>
public static class VerdictEvaluation
{
    public static VerdictReport Evaluate(IReadOnlyList<VerdictCase> cases, VerdictArm arm, int floor)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(arm);
        RetrievalEvaluation.EnsureUnique(cases.Select(static value => value.CaseId));

        var exact = RetrievalMetrics.Mean(
            cases.Select(value => MetricResult.Measured(
                string.Equals(arm(value.CaseId), value.GoldVerdict, StringComparison.Ordinal) ? 1.0 : 0.0)).ToArray(),
            floor);
        return new VerdictReport(exact, EvaluationGates.AtLeast(EvaluationGateNames.VerdictExactMatch, exact, 1.0));
    }
}

/// <summary>The temporal harness: the share of cases whose selected state is exactly the expected one, gated at 1.0.</summary>
public static class TemporalEvaluation
{
    public static TemporalReport Evaluate(IReadOnlyList<TemporalCase> cases, TemporalArm arm, int floor)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(arm);
        RetrievalEvaluation.EnsureUnique(cases.Select(static value => value.CaseId));

        var exact = RetrievalMetrics.Mean(
            cases.Select(value => MetricResult.Measured(
                string.Equals(arm(value.WorkKey, value.AsOf), value.ExpectedStateKey, StringComparison.Ordinal) ? 1.0 : 0.0)).ToArray(),
            floor);
        return new TemporalReport(exact, EvaluationGates.AtLeast(EvaluationGateNames.TemporalExactness, exact, 1.0));
    }
}
