using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.Index;

public sealed record RetrievalBenchmarkCaseResult(
    string Schema,
    string Stage,
    string CaseId,
    string Collection,
    string Category,
    string Split,
    string QrelSetSha256,
    IReadOnlyList<string> RankedCoordinatesAt10,
    IReadOnlyList<int> AnchorGainsAt10,
    RetrievalMetricObservation Mrr,
    RetrievalMetricObservation RecallAt10,
    RetrievalMetricObservation NdcgAt10,
    RetrievalMetricObservation WorkMrr,
    RetrievalMetricObservation WorkRecallAt10,
    RetrievalMetricObservation WorkNdcgAt10,
    RetrievalMetricObservation ExactFirstAccuracy,
    RetrievalMetricObservation TemporalLeakageFailures,
    RetrievalMetricObservation LatencyMs,
    RetrievalMetricObservation NoHitAccuracy,
    RetrievalMetricObservation ResolutionAccuracy,
    RetrievalMetricObservation RoleIntentAccuracy);

public sealed record RetrievalNegativeControlResult(
    string Schema,
    string Outcome,
    string Severity,
    int EligibleDenominator,
    IReadOnlyList<string> FailedGateNames,
    bool MembershipIdentical,
    bool NonRankingIdentical,
    bool UnrelatedDenominatorsAndGatesIdentical,
    int OwnQrelSetRetainedCount,
    RetrievalMetricObservation AnchorNdcgAt10);

public sealed record RetrievalBenchmarkStratum(
    string Metric,
    string Collection,
    string Category,
    string Split,
    string Disposition,
    int InvariantFloor,
    int StatisticalFloor,
    string SupportStatus,
    RetrievalMetricObservation Observation,
    bool? GatePassed);

internal sealed record RetrievalCaseObservation(
    RetrievalBenchmarkCase Case,
    IReadOnlyList<string> RankedCoordinates,
    IReadOnlyList<string> RankedWorks,
    double LatencyMs,
    int? TemporalLeakageFailures,
    bool? NoHitCorrect,
    bool? ResolutionCorrect,
    bool? RoleCorrect);

internal sealed record RetrievalBenchmarkEvaluationResult(
    string Stage,
    RetrievalMetrics Metrics,
    IReadOnlyList<RetrievalCaseObservation> Observations,
    IReadOnlyList<RetrievalBenchmarkCaseResult> CaseResults);

internal sealed record RetrievalControlEvaluation(
    RetrievalNegativeControlResult Result,
    RetrievalBenchmarkEvaluationResult Evaluation);

internal static class RetrievalBenchmarkEvaluation
{
    private static readonly JsonSerializerOptions CaseResultJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static RetrievalBenchmarkEvaluationResult Evaluate(
        string stage,
        IReadOnlyList<RetrievalBenchmarkCase> cases,
        Func<RetrievalBenchmarkCase, SearchExecution> search,
        Action<RetrievalBenchmarkRunner.Progress>? progress)
    {
        var observations = new List<RetrievalCaseObservation>(cases.Count);
        var phase = Stopwatch.StartNew();
        progress?.Invoke(new(stage, 0, cases.Count, phase.Elapsed, null));
        for (var index = 0; index < cases.Count; index++)
        {
            var benchmarkCase = cases[index];
            var timer = Stopwatch.StartNew();
            var execution = search(benchmarkCase);
            timer.Stop();
            observations.Add(Capture(benchmarkCase, execution, timer.Elapsed.TotalMilliseconds));
            var completed = index + 1;
            if (completed == cases.Count || completed % 10 == 0)
                progress?.Invoke(new(stage, completed, cases.Count, phase.Elapsed,
                    completed == 0 ? null : phase.Elapsed * (cases.Count - completed) / completed));
        }
        return Score(stage, observations);
    }

    public static RetrievalBenchmarkEvaluationResult Score(
        string stage, IReadOnlyList<RetrievalCaseObservation> observations)
    {
        var metrics = ScoreMetrics(observations);
        var rows = observations.Select(observation =>
        {
            var one = ScoreMetrics([observation]);
            var gains = GainMap(observation.Case);
            return new RetrievalBenchmarkCaseResult(
                "lex-retrieval-case-result/1", stage, observation.Case.Id,
                observation.Case.Collection, observation.Case.Category, observation.Case.Split,
                QrelSetSha256(observation.Case.RelevantAnchors),
                observation.RankedCoordinates.Take(10).ToArray(),
                observation.RankedCoordinates.Take(10)
                    .Select(coordinate => gains.GetValueOrDefault(coordinate)).ToArray(),
                one.Mrr, one.RecallAt10, one.NdcgAt10,
                one.WorkMrr, one.WorkRecallAt10, one.WorkNdcgAt10,
                one.ExactFirstAccuracy, one.TemporalLeakageFailures,
                RetrievalMetricObservation.Measured(observation.LatencyMs, 1),
                one.NoHitAccuracy, one.ResolutionAccuracy, one.RoleIntentAccuracy);
        }).ToArray();
        return new(stage, metrics, observations, rows);
    }

    public static string QrelSetSha256(IReadOnlyList<RetrievalRelevantAnchor>? anchors)
    {
        var canonical = string.Concat((anchors ?? [])
            .OrderBy(anchor => anchor.Coordinate, StringComparer.Ordinal)
            .ThenBy(anchor => anchor.Gain)
            .Select(anchor => $"{anchor.Coordinate}\0{anchor.Gain.ToString(CultureInfo.InvariantCulture)}\n"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static byte[] SerializeJsonl(
        IReadOnlyList<RetrievalBenchmarkCaseResult> rows)
    {
        using var output = new MemoryStream();
        foreach (var row in rows)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(row, CaseResultJson);
            output.Write(payload);
            output.WriteByte((byte)'\n');
        }
        return output.ToArray();
    }

    private static RetrievalCaseObservation Capture(
        RetrievalBenchmarkCase benchmarkCase, SearchExecution execution, double latencyMs)
    {
        var rankedCoordinates = execution.Hits.Select(RetrievalBenchmarkRunner.CanonicalCoordinate)
            .Distinct(StringComparer.Ordinal).ToArray();
        var rankedWorks = execution.Hits.Select(hit =>
                $"{hit.Doc.Collection}:{hit.Doc.GroupKey}".ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).ToArray();
        int? leakage = benchmarkCase.AsOf is null ? null : execution.Hits.Count(hit =>
            string.CompareOrdinal(hit.Doc.ValidFrom, benchmarkCase.AsOf) > 0
            || hit.Doc.ValidTo is not null
               && string.CompareOrdinal(hit.Doc.ValidTo, benchmarkCase.AsOf) < 0);
        bool? noHit = benchmarkCase.ExpectNoHits ? execution.Hits.Count == 0 : null;
        bool? resolution = benchmarkCase.ExpectedResolution is null ? null
            : execution.QueryPlan?.WorkResolutionStatus == benchmarkCase.ExpectedResolution;
        bool? role = benchmarkCase.ExpectedRole is null ? null
            : execution.QueryPlan?.RoleIntent == benchmarkCase.ExpectedRole;
        return new(benchmarkCase, rankedCoordinates, rankedWorks, latencyMs,
            leakage, noHit, resolution, role);
    }

    private static RetrievalMetrics ScoreMetrics(IReadOnlyList<RetrievalCaseObservation> observations)
    {
        var reciprocal = 0d;
        var recall = 0d;
        var ndcg = 0d;
        var workReciprocal = 0d;
        var workRecall = 0d;
        var workNdcg = 0d;
        var reciprocalCount = 0;
        var recallCount = 0;
        var ndcgCount = 0;
        var workCount = 0;
        var exactCorrect = 0;
        var exactCount = 0;
        var leakage = 0;
        var leakageCount = 0;
        var noHitCorrect = 0;
        var noHitCount = 0;
        var resolutionCorrect = 0;
        var resolutionCount = 0;
        var roleCorrect = 0;
        var roleCount = 0;
        var latencies = new List<double>(observations.Count);

        foreach (var observation in observations)
        {
            var benchmarkCase = observation.Case;
            latencies.Add(observation.LatencyMs);
            if (benchmarkCase.RelevantWorks.Count > 0)
            {
                workCount++;
                var first = observation.RankedWorks.ToList().FindIndex(work =>
                    benchmarkCase.RelevantWorks.Contains(work, StringComparer.Ordinal));
                if (first >= 0) workReciprocal += 1d / (first + 1);
                var worksAt10 = observation.RankedWorks.Take(10).ToArray();
                workRecall += (double)worksAt10.Count(work =>
                                  benchmarkCase.RelevantWorks.Contains(work, StringComparer.Ordinal))
                              / benchmarkCase.RelevantWorks.Count;
                var dcg = worksAt10.Select((work, rank) =>
                        benchmarkCase.RelevantWorks.Contains(work, StringComparer.Ordinal)
                            ? 1d / Math.Log2(rank + 2) : 0d).Sum();
                var ideal = Enumerable.Range(0, Math.Min(10, benchmarkCase.RelevantWorks.Count))
                    .Sum(rank => 1d / Math.Log2(rank + 2));
                workNdcg += dcg / ideal;
            }

            var anchors = benchmarkCase.RelevantAnchors ?? [];
            if (anchors.Count > 0)
            {
                var gains = GainMap(benchmarkCase);
                var first = observation.RankedCoordinates.ToList()
                    .FindIndex(coordinate => gains.ContainsKey(coordinate));
                reciprocalCount++;
                if (first >= 0) reciprocal += 1d / (first + 1);

                var relevant = anchors
                    .Select(anchor => anchor.Coordinate).ToHashSet(StringComparer.Ordinal);
                recallCount++;
                recall += (double)observation.RankedCoordinates.Take(10)
                              .Count(relevant.Contains) / relevant.Count;

                ndcgCount++;
                var dcg = observation.RankedCoordinates.Take(10)
                    .Select((coordinate, rank) =>
                        gains.GetValueOrDefault(coordinate) / Math.Log2(rank + 2)).Sum();
                var ideal = anchors.Select(anchor => anchor.Gain).OrderDescending().Take(10)
                    .Select((gain, rank) => gain / Math.Log2(rank + 2)).Sum();
                ndcg += dcg / ideal;

                if (benchmarkCase.Category == "exact")
                {
                    exactCount++;
                    if (observation.RankedCoordinates.Count > 0
                        && gains.GetValueOrDefault(observation.RankedCoordinates[0]) > 0)
                        exactCorrect++;
                }
            }

            if (observation.TemporalLeakageFailures is int caseLeakage)
            {
                leakageCount++;
                leakage += caseLeakage;
            }
            if (observation.NoHitCorrect is bool noHit)
            {
                noHitCount++;
                if (noHit) noHitCorrect++;
            }
            if (observation.ResolutionCorrect is bool resolution)
            {
                resolutionCount++;
                if (resolution) resolutionCorrect++;
            }
            if (observation.RoleCorrect is bool role)
            {
                roleCount++;
                if (role) roleCorrect++;
            }
        }

        latencies.Sort();
        return new(
            Average(reciprocal, reciprocalCount), Average(recall, recallCount),
            Average(ndcg, ndcgCount), Average(workReciprocal, workCount),
            Average(workRecall, workCount), Average(workNdcg, workCount),
            Average(exactCorrect, exactCount), Total(leakage, leakageCount),
            Percentile(latencies, .50), Percentile(latencies, .95), Percentile(latencies, .99),
            Average(noHitCorrect, noHitCount), Average(resolutionCorrect, resolutionCount),
            Average(roleCorrect, roleCount));
    }

    private static Dictionary<string, int> GainMap(RetrievalBenchmarkCase benchmarkCase) =>
        (benchmarkCase.RelevantAnchors ?? []).ToDictionary(
            anchor => anchor.Coordinate, anchor => anchor.Gain, StringComparer.Ordinal);

    private static RetrievalMetricObservation Average(double total, int denominator) =>
        denominator == 0 ? RetrievalMetricObservation.Insufficient()
            : RetrievalMetricObservation.Measured(total / denominator, denominator);

    private static RetrievalMetricObservation Total(double total, int denominator) =>
        denominator == 0 ? RetrievalMetricObservation.Insufficient()
            : RetrievalMetricObservation.Measured(total, denominator);

    private static RetrievalMetricObservation Percentile(IReadOnlyList<double> sorted, double p) =>
        sorted.Count == 0 ? RetrievalMetricObservation.Insufficient()
            : RetrievalMetricObservation.Measured(
                sorted[(int)Math.Ceiling(p * sorted.Count) - 1], sorted.Count);
}

internal static class RetrievalBenchmarkControls
{
    public static string ShuffledTop10SortKey(
        string caseSetSha256, string collection, string caseId, int originalRank,
        string canonicalCoordinate)
    {
        var framed = string.Join('\0', caseSetSha256, collection, caseId,
            originalRank.ToString(CultureInfo.InvariantCulture), canonicalCoordinate);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)));
    }

    public static RetrievalControlEvaluation ShuffledTop10(
        RetrievalBenchmarkCaseSet caseSet, RetrievalBenchmarkEvaluationResult baseline)
    {
        var eligible = new HashSet<string>(StringComparer.Ordinal);
        var transformed = baseline.Observations.Select(observation =>
        {
            var top = observation.RankedCoordinates.Take(10).ToArray();
            var gains = (observation.Case.RelevantAnchors ?? []).ToDictionary(
                anchor => anchor.Coordinate, anchor => anchor.Gain, StringComparer.Ordinal);
            if (top.Length < 2
                || top.Select(coordinate => gains.GetValueOrDefault(coordinate)).Distinct().Count() < 2)
                return observation;

            var shuffled = top.Select((coordinate, rank) => new
                {
                    Coordinate = coordinate,
                    Rank = rank,
                    Key = ShuffledTop10SortKey(caseSet.Sha256, observation.Case.Collection,
                        observation.Case.Id, rank + 1, coordinate),
                })
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ThenBy(item => item.Rank)
                .Select(item => item.Coordinate).ToArray();
            if (shuffled.SequenceEqual(top, StringComparer.Ordinal))
                shuffled = [.. shuffled.Skip(1), shuffled[0]];
            eligible.Add(observation.Case.Id);
            var coordinates = shuffled.Concat(observation.RankedCoordinates.Skip(top.Length)).ToArray();
            return observation with { RankedCoordinates = coordinates };
        }).ToArray();
        var evaluation = RetrievalBenchmarkEvaluation.Score("shuffled-top10/2", transformed);
        var baselineEligible = baseline.Observations.Where(item => eligible.Contains(item.Case.Id)).ToArray();
        var shuffledEligible = transformed.Where(item => eligible.Contains(item.Case.Id)).ToArray();
        var baselineMetrics = RetrievalBenchmarkEvaluation.Score("baseline-eligible", baselineEligible).Metrics;
        var controlMetrics = RetrievalBenchmarkEvaluation.Score("control-eligible", shuffledEligible).Metrics;
        var failed = LowerRankingGates(baselineMetrics, controlMetrics, orderSensitiveOnly: true);
        var membershipIdentical = baselineEligible.Zip(shuffledEligible).All(pair =>
            pair.First.RankedCoordinates.Take(10).Order(StringComparer.Ordinal)
                .SequenceEqual(pair.Second.RankedCoordinates.Take(10)
                    .Order(StringComparer.Ordinal), StringComparer.Ordinal));
        var nonRankingIdentical = baselineEligible.Zip(shuffledEligible)
            .All(pair => NonRankingEqual(pair.First, pair.Second));
        var unrelatedIdentical = ShuffledUnrelatedMetricsEqual(
            baselineMetrics, controlMetrics) && nonRankingIdentical;
        var outcome = eligible.Count == 0 ? "insufficient_denominator"
            : failed.Count > 0 && membershipIdentical && nonRankingIdentical
              && unrelatedIdentical
                ? "detected" : "escaped";
        return new(new(
                "shuffled-top10/2", outcome, "product_gate", eligible.Count,
                failed, membershipIdentical, nonRankingIdentical,
                unrelatedIdentical, 0, controlMetrics.NdcgAt10),
            evaluation);
    }

    public static RetrievalControlEvaluation QrelsShuffle(
        RetrievalBenchmarkCaseSet caseSet, RetrievalBenchmarkEvaluationResult baseline)
    {
        var replacements = new Dictionary<string, IReadOnlyList<RetrievalRelevantAnchor>>(
            StringComparer.Ordinal);
        var eligibleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collection in baseline.Observations.GroupBy(
                     item => item.Case.Collection, StringComparer.Ordinal))
        {
            var candidates = collection.Where(item => item.Case.RelevantAnchors is { Count: > 0 })
                .OrderBy(item => CaseOrderKey(caseSet.Sha256, item.Case), StringComparer.Ordinal)
                .ToArray();
            var sets = candidates.GroupBy(item =>
                    RetrievalBenchmarkEvaluation.QrelSetSha256(item.Case.RelevantAnchors),
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => (IReadOnlyList<RetrievalRelevantAnchor>)group.First().Case.RelevantAnchors!
                        .Select(anchor => anchor with { }).ToArray(), StringComparer.Ordinal);
            var digests = sets.Keys.OrderBy(digest =>
                    DigestOrderKey(caseSet.Sha256, collection.Key, digest), StringComparer.Ordinal)
                .ToArray();
            if (digests.Length < 2) continue;
            foreach (var candidate in candidates)
            {
                var own = RetrievalBenchmarkEvaluation.QrelSetSha256(candidate.Case.RelevantAnchors);
                var ownIndex = Array.IndexOf(digests, own);
                replacements[candidate.Case.Id] = sets[digests[(ownIndex + 1) % digests.Length]];
                eligibleIds.Add(candidate.Case.Id);
            }
        }

        var transformed = baseline.Observations.Select(observation =>
            replacements.TryGetValue(observation.Case.Id, out var anchors)
                ? observation with { Case = observation.Case with { RelevantAnchors = anchors } }
                : observation).ToArray();
        var evaluation = RetrievalBenchmarkEvaluation.Score("qrels-shuffle/2", transformed);
        var baselineEligible = baseline.Observations.Where(item => eligibleIds.Contains(item.Case.Id)).ToArray();
        var shuffledEligible = transformed.Where(item => eligibleIds.Contains(item.Case.Id)).ToArray();
        var retained = baselineEligible.Zip(shuffledEligible).Count(pair =>
            RetrievalBenchmarkEvaluation.QrelSetSha256(pair.First.Case.RelevantAnchors)
            == RetrievalBenchmarkEvaluation.QrelSetSha256(pair.Second.Case.RelevantAnchors));
        var baselineMetrics = RetrievalBenchmarkEvaluation.Score("baseline-eligible", baselineEligible).Metrics;
        var controlMetrics = RetrievalBenchmarkEvaluation.Score("control-eligible", shuffledEligible).Metrics;
        var failed = LowerRankingGates(baselineMetrics, controlMetrics, orderSensitiveOnly: false);
        var membershipIdentical = baselineEligible.Zip(shuffledEligible).All(pair =>
            pair.First.RankedCoordinates.SequenceEqual(
                pair.Second.RankedCoordinates, StringComparer.Ordinal));
        var nonRankingIdentical = baselineEligible.Zip(shuffledEligible)
            .All(pair => NonRankingEqual(pair.First, pair.Second));
        var unrelatedIdentical = UnrelatedMetricsEqual(baselineMetrics, controlMetrics)
                                 && nonRankingIdentical;
        var detected = eligibleIds.Count > 0 && retained == 0
                       && controlMetrics.NdcgAt10.HasMeasuredValue
                       && controlMetrics.NdcgAt10.RequireMeasuredValue() < 0.15
                       && failed.Count > 0 && membershipIdentical
                       && nonRankingIdentical && unrelatedIdentical;
        var outcome = eligibleIds.Count == 0 ? "insufficient_denominator"
            : detected ? "detected" : "escaped";
        return new(new(
                "qrels-shuffle/2", outcome, "product_gate", eligibleIds.Count,
                failed, membershipIdentical, nonRankingIdentical, unrelatedIdentical,
                retained, controlMetrics.NdcgAt10),
            evaluation);
    }

    private static IReadOnlyList<string> LowerRankingGates(
        RetrievalMetrics baseline, RetrievalMetrics control, bool orderSensitiveOnly)
    {
        var failed = new List<string>();
        if (Lower(control.Mrr, baseline.Mrr))
            failed.Add("anchor_mrr_not_below_unshuffled");
        if (Lower(control.NdcgAt10, baseline.NdcgAt10))
            failed.Add("anchor_ndcg_at10_not_below_unshuffled");
        if (!orderSensitiveOnly && Lower(control.RecallAt10, baseline.RecallAt10))
            failed.Add("anchor_recall_at10_not_below_unshuffled");
        return failed;
    }

    private static bool Lower(
        RetrievalMetricObservation candidate, RetrievalMetricObservation baseline) =>
        candidate.HasMeasuredValue
        && baseline.HasMeasuredValue
        && candidate.RequireMeasuredValue() + 0.000000000001
           < baseline.RequireMeasuredValue();

    private static bool NonRankingEqual(
        RetrievalCaseObservation left, RetrievalCaseObservation right) =>
        left.Case.Id == right.Case.Id
        && left.RankedWorks.SequenceEqual(right.RankedWorks, StringComparer.Ordinal)
        && left.LatencyMs == right.LatencyMs
        && left.TemporalLeakageFailures == right.TemporalLeakageFailures
        && left.NoHitCorrect == right.NoHitCorrect
        && left.ResolutionCorrect == right.ResolutionCorrect
        && left.RoleCorrect == right.RoleCorrect;

    private static bool UnrelatedMetricsEqual(RetrievalMetrics left, RetrievalMetrics right) =>
        left.WorkMrr == right.WorkMrr
        && left.WorkRecallAt10 == right.WorkRecallAt10
        && left.WorkNdcgAt10 == right.WorkNdcgAt10
        && left.TemporalLeakageFailures == right.TemporalLeakageFailures
        && left.P50Ms == right.P50Ms && left.P95Ms == right.P95Ms && left.P99Ms == right.P99Ms
        && left.NoHitAccuracy == right.NoHitAccuracy
        && left.ResolutionAccuracy == right.ResolutionAccuracy
        && left.RoleIntentAccuracy == right.RoleIntentAccuracy
        && left.Mrr.Denominator == right.Mrr.Denominator
        && left.RecallAt10.Denominator == right.RecallAt10.Denominator
        && left.NdcgAt10.Denominator == right.NdcgAt10.Denominator
        && left.ExactFirstAccuracy.Denominator == right.ExactFirstAccuracy.Denominator;

    private static bool ShuffledUnrelatedMetricsEqual(
        RetrievalMetrics left, RetrievalMetrics right) =>
        left.RecallAt10 == right.RecallAt10
        && left.WorkMrr == right.WorkMrr
        && left.WorkRecallAt10 == right.WorkRecallAt10
        && left.WorkNdcgAt10 == right.WorkNdcgAt10
        && left.TemporalLeakageFailures == right.TemporalLeakageFailures
        && left.P50Ms == right.P50Ms && left.P95Ms == right.P95Ms && left.P99Ms == right.P99Ms
        && left.NoHitAccuracy == right.NoHitAccuracy
        && left.ResolutionAccuracy == right.ResolutionAccuracy
        && left.RoleIntentAccuracy == right.RoleIntentAccuracy
        && left.Mrr.Denominator == right.Mrr.Denominator
        && left.NdcgAt10.Denominator == right.NdcgAt10.Denominator
        && left.ExactFirstAccuracy.Denominator == right.ExactFirstAccuracy.Denominator;

    private static string CaseOrderKey(string caseSetSha256, RetrievalBenchmarkCase benchmarkCase) =>
        Hash(caseSetSha256, benchmarkCase.Collection, benchmarkCase.Id);

    private static string DigestOrderKey(string caseSetSha256, string collection, string digest) =>
        Hash(caseSetSha256, collection, digest);

    private static string Hash(params string[] fields) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', fields))));
}

internal static class RetrievalBenchmarkStrata
{
    private const int InvariantFloor = 8;
    private const int DefaultStatisticalFloor = 20;

    public static IReadOnlyList<RetrievalBenchmarkStratum> Build(
        string collection, RetrievalBenchmarkEvaluationResult holdout)
    {
        var rows = new List<RetrievalBenchmarkStratum>();
        foreach (var category in holdout.Observations.GroupBy(
                     item => item.Case.Category, StringComparer.Ordinal)
                 .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var metrics = RetrievalBenchmarkEvaluation.Score(
                $"{holdout.Stage}-{category.Key}", category.ToArray()).Metrics;
            AddReported(rows, collection, category.Key, "anchor_mrr", metrics.Mrr);
            AddReported(rows, collection, category.Key, "anchor_recall_at10", metrics.RecallAt10);
            AddReported(rows, collection, category.Key, "anchor_ndcg_at10", metrics.NdcgAt10);
            AddReported(rows, collection, category.Key, "legacy_work_mrr", metrics.WorkMrr);
            AddReported(rows, collection, category.Key,
                "legacy_work_recall_at10", metrics.WorkRecallAt10);
            AddReported(rows, collection, category.Key,
                "legacy_work_ndcg_at10", metrics.WorkNdcgAt10);
            AddReported(rows, collection, category.Key, "latency_p95_ms", metrics.P95Ms,
                statisticalFloor: 200);

            if (category.Any(item => item.Case.Category == "exact"))
                AddBlocking(rows, collection, category.Key, "anchor_exact_first_accuracy",
                    metrics.ExactFirstAccuracy, expected: 1);
            if (category.Any(item => item.Case.AsOf is not null))
                AddBlocking(rows, collection, category.Key, "temporal_leakage_failures",
                    metrics.TemporalLeakageFailures, expected: 0);
            if (category.Any(item => item.Case.ExpectNoHits))
                AddBlocking(rows, collection, category.Key, "no_hit_accuracy",
                    metrics.NoHitAccuracy, expected: 1);
            if (category.Any(item => item.Case.ExpectedResolution is not null))
                AddBlocking(rows, collection, category.Key, "resolution_accuracy",
                    metrics.ResolutionAccuracy, expected: 1);
            if (category.Any(item => item.Case.ExpectedRole is not null))
                AddBlocking(rows, collection, category.Key, "role_intent_accuracy",
                    metrics.RoleIntentAccuracy, expected: 1);
        }
        return rows;
    }

    public static IReadOnlyList<string> BlockingFailures(
        IReadOnlyList<RetrievalBenchmarkStratum> strata) => strata
        .Where(row => row.Disposition == "blocking" && row.GatePassed is not true)
        .Select(row => $"blocking stratum {row.Collection}/{row.Category}/{row.Metric} "
                       + $"failed with {row.SupportStatus} n={row.Observation.Denominator}")
        .ToArray();

    private static void AddReported(
        ICollection<RetrievalBenchmarkStratum> rows, string collection, string category,
        string metric, RetrievalMetricObservation observation,
        int statisticalFloor = DefaultStatisticalFloor) => rows.Add(new(
        metric, collection, category, "holdout", "reported", InvariantFloor,
        statisticalFloor, Support(observation, statisticalFloor), observation, null));

    private static void AddBlocking(
        ICollection<RetrievalBenchmarkStratum> rows, string collection, string category,
        string metric, RetrievalMetricObservation observation, double expected)
    {
        var support = Support(observation, DefaultStatisticalFloor);
        var gatePassed = observation.Denominator >= InvariantFloor
                         && observation.HasMeasuredValue
                         && observation.RequireMeasuredValue() == expected;
        rows.Add(new(metric, collection, category, "holdout", "blocking", InvariantFloor,
            DefaultStatisticalFloor, support, observation, gatePassed));
    }

    private static string Support(
        RetrievalMetricObservation observation, int statisticalFloor)
    {
        if (!observation.IsStructurallyCoherent()) return "invalid_metric";
        if (observation.Denominator < InvariantFloor) return "insufficient_denominator";
        if (observation.Denominator < statisticalFloor) return "invariant_only_n8";
        return "statistically_supported";
    }
}
