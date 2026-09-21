using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Evaluation;

/// <summary>Why a metric has no value. Closed, and a metric without a value always carries one of these.</summary>
public enum NotMeasuredReason
{
    /// <summary>The query judges nothing of the grade the metric counts, so there is nothing to compare a ranking with.</summary>
    [JsonStringEnumMemberName("no_relevant_judgment")]
    NoRelevantJudgment = 1,

    /// <summary>No query in the stratum was measurable, so there is nothing to average.</summary>
    [JsonStringEnumMemberName("no_measurable_query")]
    NoMeasurableQuery = 2,

    /// <summary>Fewer measured queries than the stratum's declared floor.</summary>
    [JsonStringEnumMemberName("stratum_below_floor")]
    StratumBelowFloor = 3,
}

/// <summary>
/// A metric's value, or the typed reason it has none. There is no third state and no default: a metric with nothing to
/// measure is not 1.0, which is the defect the V2 harness had (<c>rankingCount == 0 ? 1 : ...</c>).
/// </summary>
public sealed record MetricResult
{
    private MetricResult(double? value, NotMeasuredReason? reason)
    {
        Value = value;
        Reason = reason;
    }

    public double? Value { get; }

    public NotMeasuredReason? Reason { get; }

    public bool IsMeasured => Value is not null;

    public static MetricResult Measured(double value) =>
        double.IsFinite(value) && value is >= 0.0 and <= 1.0
            ? new MetricResult(value, null)
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A measured metric is a number from 0 to 1.");

    public static MetricResult NotMeasured(NotMeasuredReason reason) =>
        Enum.IsDefined(reason)
            ? new MetricResult(null, reason)
            : throw new ArgumentOutOfRangeException(nameof(reason), reason, "The reason is not in the closed vocabulary.");
}

/// <summary>
/// Retrieval metrics judged at the provision. A returned anchor earns the grade its query gives that work and anchor,
/// and nothing where the query does not judge it, so a wrong article inside the right work earns nothing. A repeated
/// anchor earns its gain once.
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>
    /// Normalised discounted cumulative gain at <paramref name="k"/>: the ranking's gain over log2 of rank plus one, over
    /// the same sum for the best possible order of what the query judges. Not measured where nothing is judged relevant.
    /// </summary>
    public static MetricResult NdcgAtK(QueryJudgments judgments, IReadOnlyList<RankedAnchor> ranking, int k)
    {
        ArgumentNullException.ThrowIfNull(judgments);
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);

        var ideal = judgments.Anchors
            .Select(static anchor => anchor.Grade)
            .Where(static grade => grade > 0)
            .OrderByDescending(static grade => grade)
            .Take(k)
            .ToArray();
        if (ideal.Length == 0)
        {
            return MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment);
        }

        var seen = new HashSet<(string, string)>();
        var gains = new List<int>();
        foreach (var item in ranking.Take(k))
        {
            gains.Add(seen.Add((item.WorkKey, item.AnchorId)) ? judgments.GradeOf(item) : 0);
        }

        return MetricResult.Measured(DiscountedGain(gains) / DiscountedGain(ideal));
    }

    /// <summary>
    /// The share of the anchors that alone support the answer (grade 3) that appear in the first <paramref name="k"/>.
    /// Context is not counted: finding a definition without the operative text is not a win.
    /// </summary>
    public static MetricResult RecallAtK(QueryJudgments judgments, IReadOnlyList<RankedAnchor> ranking, int k)
    {
        ArgumentNullException.ThrowIfNull(judgments);
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);

        var supporting = judgments.Anchors
            .Where(static anchor => anchor.Grade == JudgedAnchor.SupportingGrade)
            .Select(static anchor => (anchor.WorkKey, anchor.AnchorId))
            .ToHashSet();
        if (supporting.Count == 0)
        {
            return MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment);
        }

        var found = ranking.Take(k).Select(static item => (item.WorkKey, item.AnchorId)).Where(supporting.Contains).Distinct().Count();
        return MetricResult.Measured((double)found / supporting.Count);
    }

    /// <summary>
    /// One over the rank of the first returned anchor judged at <paramref name="minimumGrade"/> or above (the supporting
    /// grade unless said otherwise, the stricter reading that recall also uses); 0 where none is returned.
    /// </summary>
    public static MetricResult ReciprocalRank(
        QueryJudgments judgments, IReadOnlyList<RankedAnchor> ranking, int minimumGrade = JudgedAnchor.SupportingGrade)
    {
        ArgumentNullException.ThrowIfNull(judgments);
        ArgumentNullException.ThrowIfNull(ranking);
        if (minimumGrade is not (JudgedAnchor.SupportingGrade or JudgedAnchor.ContextGrade))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumGrade), minimumGrade, "The minimum grade is 3 or 1.");
        }

        if (!judgments.Anchors.Any(anchor => anchor.Grade >= minimumGrade))
        {
            return MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment);
        }

        for (var index = 0; index < ranking.Count; index++)
        {
            if (judgments.GradeOf(ranking[index]) >= minimumGrade)
            {
                return MetricResult.Measured(1.0 / (index + 1));
            }
        }

        return MetricResult.Measured(0.0);
    }

    /// <summary>
    /// The mean of a stratum's per-query values over the queries that were measured. A query that was not measured is
    /// neither a zero nor a one; a stratum with none measured, or fewer than <paramref name="floor"/>, is not measured.
    /// </summary>
    public static MetricResult Mean(IReadOnlyCollection<MetricResult> perQuery, int floor)
    {
        ArgumentNullException.ThrowIfNull(perQuery);
        ArgumentOutOfRangeException.ThrowIfLessThan(floor, 1);

        var measured = perQuery.Where(static result => result.IsMeasured).Select(static result => result.Value!.Value).ToArray();
        if (measured.Length == 0)
        {
            return MetricResult.NotMeasured(NotMeasuredReason.NoMeasurableQuery);
        }

        return measured.Length < floor
            ? MetricResult.NotMeasured(NotMeasuredReason.StratumBelowFloor)
            : MetricResult.Measured(measured.Average());
    }

    private static double DiscountedGain(IReadOnlyList<int> gains)
    {
        var total = 0.0;
        for (var index = 0; index < gains.Count; index++)
        {
            total += gains[index] / Math.Log2(index + 2);
        }

        return total;
    }
}
