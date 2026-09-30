using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Evaluation;

/// <summary>
/// One gate as the evaluation card prints it: its verdict in the closed vocabulary, its value or the typed reason it has
/// none, the threshold it was read against and the number of cases in its stratum. A rate (every gate but anchor nDCG,
/// which is a graded mean) also carries its Wilson 95 percent interval, and a rate of exactly 1.0 the rule-of-three
/// bound on the failure rate, so that a pass on a small set is never oversold (<c>36-ideal-evaluation.md</c> section 5).
/// </summary>
public sealed record EvaluationCardGateRow(
    string Gate, GateVerdict Verdict, double? Value, NotMeasuredReason? Reason, double Threshold, int N)
{
    /// <summary>Anchor nDCG is a mean of graded gains, not a share of cases, so it has no binomial interval.</summary>
    public bool IsRate => !string.Equals(Gate, EvaluationGateNames.AnchorNdcgAt10, StringComparison.Ordinal);

    /// <summary>The Wilson 95 percent interval of a measured rate, or null.</summary>
    public (double Low, double High)? Wilson95 =>
        IsRate && Value is { } value && N > 0
            ? EvaluationCard.Wilson95((int)Math.Round(value * N), N)
            : null;

    /// <summary>The rule-of-three 95 percent upper bound on the failure rate of a rate measured at exactly 1.0, or null.</summary>
    public double? RuleOfThreeFailureUpper95 =>
        IsRate && Value == 1.0 && N > 0 ? EvaluationCard.RuleOfThreeUpper95(N) : null;
}

/// <summary>
/// One case set run through one arm: the gates it produced, the shuffled control run over it, and the digest of the
/// cases, so a card names exactly what it measured.
/// </summary>
public sealed record EvaluationCardSet(
    string Set, string Arm, string CasesSha256, int CaseCount, IReadOnlyList<EvaluationCardGateRow> Gates, ControlResult Control);

/// <summary>
/// The evaluation card of <c>36-ideal-evaluation.md</c> section 6, as far as the launch contract asks for it: every
/// machine gate with its verdict and bounds, every shuffled control's result, and the statistical rows marked "not yet
/// labelled", because the human-labelled datasets gate after launch (Decision 92). Purpose-built cases are evidence for
/// the harness and never a labelled dataset, so no row here supports a statistical claim.
/// </summary>
public static class EvaluationCard
{
    public const string Schema = "lex-v3-evaluation-card/1";

    /// <summary>The status of every statistical row until its dataset is labelled, frozen and reviewed.</summary>
    public const string NotYetLabelled = "not_yet_labelled";

    private const double Z95 = 1.959963984540054;

    /// <summary>
    /// The Wilson score interval at 95 percent for <paramref name="successes"/> out of <paramref name="n"/>, rounded to
    /// four places: the interval <c>36-ideal-evaluation.md</c> section 5 asks beside every published rate.
    /// </summary>
    public static (double Low, double High) Wilson95(int successes, int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successes, n);
        var p = (double)successes / n;
        var z2 = Z95 * Z95;
        var denominator = 1 + z2 / n;
        var centre = (p + z2 / (2 * n)) / denominator;
        var half = Z95 * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denominator;
        return (Round(Math.Max(0.0, centre - half)), Round(Math.Min(1.0, centre + half)));
    }

    /// <summary>
    /// With no failure in <paramref name="n"/> cases, the true failure rate is below 3/n with about 95 percent confidence;
    /// printed beside every 100 percent so that a clean small sample is never read as proof.
    /// </summary>
    public static double RuleOfThreeUpper95(int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        return Round(Math.Min(1.0, 3.0 / n));
    }

    /// <summary>A temporal case set through one arm: <c>temporal_exactness</c> over every case.</summary>
    public static EvaluationCardSet Temporal(string arm, IReadOnlyList<TemporalCase> cases, TemporalReport report, ControlResult control)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(report);
        var digest = Digest(new JsonArray(cases.Select(static value => (JsonNode)new JsonObject
        {
            ["case_id"] = value.CaseId,
            ["work_key"] = value.WorkKey,
            ["as_of"] = value.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["expected_state_key"] = value.ExpectedStateKey,
        }).ToArray()));
        return new EvaluationCardSet("temporal", arm, digest, cases.Count, [Row(report.Gate, report.Exactness, 1.0, cases.Count)], control);
    }

    /// <summary>A verdict (refusal) case set through one arm: <c>verdict_exact_match</c> over every case.</summary>
    public static EvaluationCardSet Verdict(string set, string arm, IReadOnlyList<VerdictCase> cases, VerdictReport report, ControlResult control)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(report);
        var digest = Digest(new JsonArray(cases.Select(static value => (JsonNode)new JsonObject
        {
            ["case_id"] = value.CaseId,
            ["gold_verdict"] = value.GoldVerdict,
        }).ToArray()));
        return new EvaluationCardSet(set, arm, digest, cases.Count, [Row(report.Gate, report.ExactMatch, 1.0, cases.Count)], control);
    }

    /// <summary>
    /// A retrieval case set through one arm: its three gates, each over its own stratum as
    /// <see cref="RetrievalEvaluation"/> measures it.
    /// </summary>
    public static EvaluationCardSet Retrieval(
        string arm, IReadOnlyList<EvaluationCase> cases, RetrievalReport report, double ndcgThreshold, ControlResult control)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(report);
        var digest = Digest(new JsonArray(cases.Select(static value => (JsonNode)new JsonObject
        {
            ["case_id"] = value.CaseId,
            ["collection"] = value.Collection,
            ["kind"] = value.Kind == EvaluationCaseKind.ExactIdentifier ? "exact_identifier" : "retrieval",
            ["judgments"] = new JsonArray(value.Judgments.Anchors.Select(static anchor => (JsonNode)new JsonObject
            {
                ["work_key"] = anchor.WorkKey,
                ["anchor_id"] = anchor.AnchorId,
                ["grade"] = anchor.Grade,
            }).ToArray()),
        }).ToArray()));
        EvaluationCardGateRow RowOf(string name, MetricResult metric, double threshold, int n) =>
            Row(report.Gates.Single(gate => string.Equals(gate.Name, name, StringComparison.Ordinal)), metric, threshold, n);
        return new EvaluationCardSet(
            "retrieval",
            arm,
            digest,
            cases.Count,
            [
                RowOf(EvaluationGateNames.AnchorNdcgAt10, report.AnchorNdcgAt10, ndcgThreshold, cases.Count(RetrievalEvaluation.JudgesAnyRelevant)),
                RowOf(EvaluationGateNames.NoHitAccuracy, report.NoHitAccuracy, 1.0, cases.Count(RetrievalEvaluation.IsNoHit)),
                RowOf(EvaluationGateNames.ResolverExactness, report.ResolverExactness, 1.0, cases.Count(RetrievalEvaluation.IsExactIdentifier)),
            ],
            control);
    }

    /// <summary>
    /// The datasets of <c>36-ideal-evaluation.md</c> section 2 whose statistical gates the card will carry, each "not yet
    /// labelled" until it is labelled, frozen and reviewed after launch (Decision 92).
    /// </summary>
    public static IReadOnlyList<(string Dataset, string Name, string Gates)> StatisticalRows { get; } =
    [
        ("D1", "retrieval qrels v3", "retrieval (section 1.4): anchor nDCG@10 against the baseline minus its MDE, by paired permutation test"),
        ("D2", "derivation audit set", "derivation fidelity (section 1.2)"),
        ("D3", "hard-negative and refusal set", "typed absence and refusal correctness (section 4)"),
        ("D4", "assistant verdict set", "assistant contract (section 1.6): verdict exact match, with self-agreement"),
        ("D5", "span-support set", "span-level support scoring (section 3)"),
        ("D6", "crosswalk gold", "the gated hybrid and crosswalk lane's activation against keyword"),
        ("D7", "temporal correctness set", "as_of, timeline, diff and knowable_on correctness beyond the golden cases"),
        ("D8", "grader audit set", "grader-versus-human agreement; the grader never gates"),
    ];

    /// <summary>
    /// The card as a JSON document: target, machine gates, shuffled controls, statistical rows and the negative-results
    /// register. Deterministic for the same input, so it can be held as a census and its digest compared.
    /// </summary>
    public static JsonObject Render(string target, IReadOnlyList<EvaluationCardSet> sets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(sets);
        return new JsonObject
        {
            ["schema"] = Schema,
            ["target"] = target,
            ["machine_gates"] = new JsonArray(sets.Select(static set => (JsonNode)new JsonObject
            {
                ["set"] = set.Set,
                ["arm"] = set.Arm,
                ["cases"] = set.CaseCount,
                ["cases_sha256"] = set.CasesSha256,
                ["gates"] = new JsonArray(set.Gates.Select(static row => (JsonNode)GateNode(row)).ToArray()),
            }).ToArray()),
            ["shuffled_controls"] = new JsonArray(sets.Select(static set => (JsonNode)new JsonObject
            {
                ["control"] = set.Control.Name,
                ["set"] = set.Set,
                ["arm"] = set.Arm,
                ["verdict"] = ControlVerdictName(set.Control.Verdict),
                ["reason"] = set.Control.Reason,
                ["seed"] = set.Control.Seed,
            }).ToArray()),
            ["statistical_rows"] = new JsonArray(StatisticalRows.Select(static row => (JsonNode)new JsonObject
            {
                ["dataset"] = row.Dataset,
                ["name"] = row.Name,
                ["gates"] = row.Gates,
                ["status"] = NotYetLabelled,
                ["governed_by"] = "Decision 92: the human-labelled datasets gate Stage 8 (S8-A03, S8-A11), after launch",
            }).ToArray()),
            ["negative_results"] = new JsonArray(
                new JsonObject
                {
                    ["hypothesis"] = "a ranked lane (BM25 or hybrid) serves better provisions than the strict and relaxed keyword lanes",
                    ["dataset"] = "D1, not yet labelled",
                    ["result"] = "not measured",
                    ["decision"] = "not shipped: search serves only the keyword lanes and refuses a ranked mode with retrieval_mode_unavailable",
                    ["what_would_reverse_it"] = "D1 labelled and frozen, and a ranked lane beating the keyword lanes by more than D1's MDE under the paired permutation test",
                }),
        };
    }

    /// <summary>The card's canonical text: indented JSON with LF line ends and a final LF.</summary>
    public static string ToText(JsonObject card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static EvaluationCardGateRow Row(GateResult gate, MetricResult metric, double threshold, int n) =>
        new(gate.Name, gate.Verdict, metric.Value, metric.Reason, threshold, n);

    private static JsonObject GateNode(EvaluationCardGateRow row)
    {
        var node = new JsonObject
        {
            ["gate"] = row.Gate,
            ["verdict"] = GateVerdictName(row.Verdict),
            ["value"] = row.Value is { } value ? Round(value) : null,
            ["threshold"] = row.Threshold,
            ["n"] = row.N,
        };
        if (row.Reason is { } reason)
        {
            node["not_measured_reason"] = NotMeasuredReasonName(reason);
        }

        if (row.Wilson95 is { } interval)
        {
            node["wilson_95"] = new JsonArray(interval.Low, interval.High);
        }

        if (row.RuleOfThreeFailureUpper95 is { } bound)
        {
            node["rule_of_three_failure_upper_95"] = bound;
        }

        return node;
    }

    private static string GateVerdictName(GateVerdict verdict) => verdict switch
    {
        GateVerdict.Pass => "pass",
        GateVerdict.Fail => "fail",
        GateVerdict.NotMeasured => "not_measured",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "The verdict is not in the closed vocabulary."),
    };

    private static string ControlVerdictName(ControlVerdict verdict) => verdict switch
    {
        ControlVerdict.CaughtTheShuffle => "caught_the_shuffle",
        ControlVerdict.MissedTheShuffle => "missed_the_shuffle",
        ControlVerdict.NotApplicable => "not_applicable",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "The verdict is not in the closed vocabulary."),
    };

    private static string NotMeasuredReasonName(NotMeasuredReason reason) => reason switch
    {
        NotMeasuredReason.NoRelevantJudgment => "no_relevant_judgment",
        NotMeasuredReason.NoMeasurableQuery => "no_measurable_query",
        NotMeasuredReason.StratumBelowFloor => "stratum_below_floor",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The reason is not in the closed vocabulary."),
    };

    private static string Digest(JsonArray cases) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cases.ToJsonString())));

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
