using System.Globalization;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Evaluation;

/// <summary>
/// What a shuffled control found. A control shuffles its inputs so that a sound harness must fail, and it is the
/// harness that is being tested: the control that is caught is the good outcome.
/// </summary>
public enum ControlVerdict
{
    /// <summary>The harness noticed the shuffle, as it must: the metrics collapsed and the invariant gates stopped passing.</summary>
    [JsonStringEnumMemberName("caught_the_shuffle")]
    CaughtTheShuffle = 1,

    /// <summary>The harness did not notice: shuffled inputs still passed. The harness is defective and its release is blocked.</summary>
    [JsonStringEnumMemberName("missed_the_shuffle")]
    MissedTheShuffle = 2,

    /// <summary>The control proved nothing: no reference arm that passes the unshuffled inputs, or a shuffle that changed nothing.</summary>
    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable = 3,
}

/// <summary>One control's verdict, why, and the seed it ran under, so that the same run can be repeated.</summary>
public sealed record ControlResult
{
    public ControlResult(string name, ControlVerdict verdict, string reason, ulong seed)
    {
        Name = Identity.Require(name, nameof(name));
        Verdict = Enum.IsDefined(verdict)
            ? verdict
            : throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "The verdict is not in the closed vocabulary.");
        Reason = Identity.Require(reason, nameof(reason));
        Seed = seed;
    }

    public string Name { get; }

    public ControlVerdict Verdict { get; }

    public string Reason { get; }

    public ulong Seed { get; }

    /// <summary>Only a caught shuffle lets the harness stand: a missed one is a defect and a control that proved nothing is no proof.</summary>
    public bool BlocksTheHarness => Verdict != ControlVerdict.CaughtTheShuffle;
}

/// <summary>The three required controls together. The harness is blocked unless each was run once and each was caught.</summary>
public sealed record ControlSuiteResult
{
    public ControlSuiteResult(IReadOnlyList<ControlResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Results = Array.AsReadOnly(results.ToArray());
    }

    public IReadOnlyList<ControlResult> Results { get; }

    public bool HarnessBlocked
    {
        get
        {
            var names = Results.Select(static result => result.Name).ToArray();
            var complete = names.Length == 3 &&
                           names.Contains(ShuffledControlNames.QrelsShuffle) &&
                           names.Contains(ShuffledControlNames.VerdictShuffle) &&
                           names.Contains(ShuffledControlNames.DateShuffle);
            return !complete || Results.Any(static result => result.BlocksTheHarness);
        }
    }
}

/// <summary>The retrieval harness under test, as a control calls it.</summary>
public delegate RetrievalReport RetrievalEvaluator(IReadOnlyList<EvaluationCase> cases, RetrievalArm arm);

/// <summary>The verdict harness under test, as a control calls it.</summary>
public delegate VerdictReport VerdictEvaluator(IReadOnlyList<VerdictCase> cases, VerdictArm arm);

/// <summary>The temporal harness under test, as a control calls it.</summary>
public delegate TemporalReport TemporalEvaluator(IReadOnlyList<TemporalCase> cases, TemporalArm arm);

/// <summary>
/// The three shuffled controls. Each takes the harness's own evaluation as a parameter, because the harness is what it
/// tests: a defective harness handed to a control must be caught by it. Each needs a reference arm that passes every
/// gate on the unshuffled inputs, since a shuffle that a failing arm also fails proves nothing.
/// </summary>
public static class ShuffledControls
{
    /// <summary>
    /// Permutes the judgments among the cases of each collection, so that no case keeps its own where the counts allow.
    /// Anchor nDCG@10 must fall below 0.15 and both invariant gates must stop passing.
    /// </summary>
    public static ControlResult QrelsShuffle(
        IReadOnlyList<EvaluationCase> cases, RetrievalArm arm, RetrievalEvaluator evaluate, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(arm);
        ArgumentNullException.ThrowIfNull(evaluate);

        // Anchor nDCG@10 must fall below 0.15 after the qrels are permuted.
        const double collapseCeiling = 0.15;
        var baseline = evaluate(cases, arm);
        if (!baseline.Releases)
        {
            return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.NotApplicable, seed,
                "the reference arm does not pass the unshuffled cases: " + Describe(baseline.Gates, baseline.RequiredGates));
        }

        var shuffled = ShuffleJudgments(cases, seed, out var changed);
        if (!changed)
        {
            return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.NotApplicable, seed,
                "the shuffle changes no case's judgments: no collection holds two cases judged differently");
        }

        var report = evaluate(shuffled, arm);
        if (report.AnchorNdcgAt10.Value is not { } ndcg)
        {
            return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.NotApplicable, seed,
                $"anchor nDCG@10 is not measured after the shuffle ({report.AnchorNdcgAt10.Reason})");
        }

        if (ndcg >= collapseCeiling)
        {
            return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.MissedTheShuffle, seed,
                $"anchor nDCG@10 is {Format(ndcg)} after the shuffle and must fall below {Format(collapseCeiling)}");
        }

        foreach (var invariant in new[] { EvaluationGateNames.NoHitAccuracy, EvaluationGateNames.ResolverExactness })
        {
            var gate = report.Gates.FirstOrDefault(value => string.Equals(value.Name, invariant, StringComparison.Ordinal));
            if (gate is null)
            {
                return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.MissedTheShuffle, seed,
                    $"the harness reports no gate '{invariant}', so nothing could fire");
            }

            if (gate.Verdict == GateVerdict.Pass)
            {
                return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.MissedTheShuffle, seed,
                    $"invariant gate '{invariant}' still passes after the shuffle");
            }
        }

        return Result(ShuffledControlNames.QrelsShuffle, ControlVerdict.CaughtTheShuffle, seed,
            $"anchor nDCG@10 fell to {Format(ndcg)} and both invariant gates stopped passing");
    }

    /// <summary>
    /// Permutes the gold verdicts among the cases, so that no case keeps its own where the counts allow. Exact match must
    /// fall to the base-rate ceiling, the share of the largest verdict class: what a constant guess would score.
    /// </summary>
    public static ControlResult VerdictShuffle(
        IReadOnlyList<VerdictCase> cases, VerdictArm arm, VerdictEvaluator evaluate, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(arm);
        ArgumentNullException.ThrowIfNull(evaluate);

        var baseline = evaluate(cases, arm);
        if (!baseline.Releases)
        {
            return Result(ShuffledControlNames.VerdictShuffle, ControlVerdict.NotApplicable, seed,
                "the reference arm does not pass the unshuffled cases: " + Describe([baseline.Gate], baseline.RequiredGates));
        }

        var ordered = cases.OrderBy(static value => value.CaseId, StringComparer.Ordinal).ToArray();
        var permutation = SeededDerangement.ByKey(ordered.Select(static value => value.GoldVerdict).ToArray(), seed);
        var shuffled = ordered.Select((value, index) => new VerdictCase(value.CaseId, ordered[permutation[index]].GoldVerdict)).ToArray();
        if (shuffled.Zip(ordered).All(static pair => pair.First.GoldVerdict == pair.Second.GoldVerdict))
        {
            return Result(ShuffledControlNames.VerdictShuffle, ControlVerdict.NotApplicable, seed,
                "the shuffle changes no gold verdict: the cases carry one verdict");
        }

        var report = evaluate(shuffled, arm);
        if (report.ExactMatch.Value is not { } exact)
        {
            return Result(ShuffledControlNames.VerdictShuffle, ControlVerdict.NotApplicable, seed,
                $"verdict exact match is not measured after the shuffle ({report.ExactMatch.Reason})");
        }

        var ceiling = (double)ordered.GroupBy(static value => value.GoldVerdict, StringComparer.Ordinal).Max(static group => group.Count())
                      / ordered.Length;
        return exact <= ceiling
            ? Result(ShuffledControlNames.VerdictShuffle, ControlVerdict.CaughtTheShuffle, seed,
                $"exact match fell to {Format(exact)}, within the base-rate ceiling {Format(ceiling)}")
            : Result(ShuffledControlNames.VerdictShuffle, ControlVerdict.MissedTheShuffle, seed,
                $"exact match is {Format(exact)} after the shuffle and must not exceed the base-rate ceiling {Format(ceiling)}");
    }

    /// <summary>
    /// Shifts every as-of date by an interval the corpus holds, chosen by the seed, so that the state a date selects
    /// changes. Every expectation must break: exactness must be zero.
    /// </summary>
    public static ControlResult DateShuffle(
        IReadOnlyList<TemporalCase> cases,
        TemporalArm arm,
        TemporalEvaluator evaluate,
        IReadOnlyList<int> heldIntervalDays,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(arm);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(heldIntervalDays);
        if (heldIntervalDays.Count == 0 || heldIntervalDays.Any(static days => days < 1))
        {
            throw new ArgumentException("The held intervals are a non-empty list of whole days, each at least one.", nameof(heldIntervalDays));
        }

        var baseline = evaluate(cases, arm);
        if (!baseline.Releases)
        {
            return Result(ShuffledControlNames.DateShuffle, ControlVerdict.NotApplicable, seed,
                "the reference arm does not pass the unshifted cases: " + Describe([baseline.Gate], baseline.RequiredGates));
        }

        var random = new SplitMix64(seed);
        var shifted = cases.OrderBy(static value => value.CaseId, StringComparer.Ordinal)
            .Select(value => new TemporalCase(
                value.CaseId, value.WorkKey, value.AsOf.AddDays(heldIntervalDays[random.NextBelow(heldIntervalDays.Count)]), value.ExpectedStateKey))
            .ToArray();
        var report = evaluate(shifted, arm);
        if (report.Exactness.Value is not { } exact)
        {
            return Result(ShuffledControlNames.DateShuffle, ControlVerdict.NotApplicable, seed,
                $"temporal exactness is not measured after the shift ({report.Exactness.Reason})");
        }

        return exact == 0.0
            ? Result(ShuffledControlNames.DateShuffle, ControlVerdict.CaughtTheShuffle, seed, "every expectation broke after the shift")
            : Result(ShuffledControlNames.DateShuffle, ControlVerdict.MissedTheShuffle, seed,
                $"{Format(exact)} of the expectations still hold after the shift and every one must break");
    }

    private static EvaluationCase[] ShuffleJudgments(IReadOnlyList<EvaluationCase> cases, ulong seed, out bool changed)
    {
        var replaced = new Dictionary<string, EvaluationCase>(StringComparer.Ordinal);
        changed = false;
        foreach (var collection in cases.GroupBy(static value => value.Collection, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var ordered = collection.OrderBy(static value => value.CaseId, StringComparer.Ordinal).ToArray();
            var signatures = ordered.Select(static value => Signature(value.Judgments)).ToArray();
            var permutation = SeededDerangement.ByKey(signatures, seed);
            for (var index = 0; index < ordered.Length; index++)
            {
                var source = ordered[permutation[index]];
                replaced[ordered[index].CaseId] = new EvaluationCase(
                    ordered[index].CaseId, ordered[index].Collection, ordered[index].Kind,
                    new QueryJudgments(ordered[index].CaseId, source.Judgments.Anchors));
                changed |= signatures[index] != signatures[permutation[index]];
            }
        }

        return cases.Select(value => replaced[value.CaseId]).ToArray();
    }

    private static string Signature(QueryJudgments judgments) =>
        string.Join(
            "\u001e",
            judgments.Anchors
                .OrderBy(static anchor => anchor.WorkKey, StringComparer.Ordinal)
                .ThenBy(static anchor => anchor.AnchorId, StringComparer.Ordinal)
                .Select(static anchor => $"{anchor.WorkKey}\u001f{anchor.AnchorId}\u001f{anchor.Grade}"));

    private static ControlResult Result(string name, ControlVerdict verdict, ulong seed, string reason) =>
        new(name, verdict, reason, seed);

    private static string Describe(IReadOnlyList<GateResult> gates, IReadOnlyList<string> required) =>
        string.Join(
            "; ",
            gates.Where(static gate => gate.Verdict != GateVerdict.Pass).Select(static gate => $"{gate.Name} {gate.Verdict}")
                .Concat(EvaluationGates.Unreported(gates, required).Select(static name => $"{name} not reported exactly once")));

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
