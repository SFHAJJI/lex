using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.Index;

public sealed record RetrievalRelevantAnchor(
    string Work,
    string Anchor,
    int Gain)
{
    [JsonIgnore]
    public string Coordinate => $"{Work}#{Anchor}";
}

public sealed record RetrievalBenchmarkCase(
    string Id,
    string Category,
    string Query,
    string Language,
    string TimeScope,
    string? AsOf,
    IReadOnlyList<string> RelevantWorks,
    string Explanation,
    string ReviewStatus,
    string? Hierarchy = null,
    string? Domain = null,
    string Collection = "",
    string Split = "tuning",
    string? ExpectedResolution = null,
    string? ExpectedRole = null,
    bool ExpectNoHits = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RetrievalRelevantAnchor>? RelevantAnchors = null);

public sealed record RetrievalBenchmarkCaseSet(
    IReadOnlyList<RetrievalBenchmarkCase> Cases,
    string Sha256);

public sealed record RetrievalBenchmarkBaseline(
    string Schema,
    string CasesFile,
    string CasesSha256,
    int SampleCount,
    string ReviewStatus,
    string ReviewedBy,
    string ReviewedAt);

public sealed record MeasuredRetrievalMetric
{
    internal MeasuredRetrievalMetric(double value) => Value = value;

    public double Value { get; }
}

public sealed record RetrievalMetricObservation
{
    [JsonInclude]
    [JsonPropertyName("value")]
    [JsonPropertyOrder(0)]
    private double? SerializedValue { get; }

    [JsonPropertyOrder(1)]
    public int Denominator { get; }

    [JsonPropertyOrder(2)]
    public string Status { get; }

    [JsonConstructor]
    private RetrievalMetricObservation(
        double? serializedValue,
        int denominator,
        string status) =>
        (SerializedValue, Denominator, Status) = (serializedValue, denominator, status);

    public static RetrievalMetricObservation Measured(double value, int denominator) =>
        new(value, denominator, "measured");

    public static RetrievalMetricObservation Insufficient() =>
        new(null, 0, "insufficient_denominator");

    internal static RetrievalMetricObservation FromSerialized(
        double? value,
        int denominator,
        string status) =>
        new(value, denominator, status);

    public bool IsStructurallyCoherent() => Status switch
    {
        "measured" => TryGetMeasured(out _),
        "insufficient_denominator" => SerializedValue is null && Denominator == 0,
        _ => false,
    };

    public bool TryGetMeasured(
        [NotNullWhen(true)] out MeasuredRetrievalMetric? measured)
    {
        if (Status == "measured" && SerializedValue is double candidate
            && double.IsFinite(candidate) && Denominator > 0)
        {
            measured = new MeasuredRetrievalMetric(candidate);
            return true;
        }

        measured = null;
        return false;
    }
}

[method: JsonConstructor]
public sealed record RetrievalMetrics(
    RetrievalMetricObservation Mrr,
    RetrievalMetricObservation RecallAt10,
    RetrievalMetricObservation NdcgAt10,
    RetrievalMetricObservation WorkMrr,
    RetrievalMetricObservation WorkRecallAt10,
    RetrievalMetricObservation WorkNdcgAt10,
    RetrievalMetricObservation ExactFirstAccuracy,
    RetrievalMetricObservation TemporalLeakageFailures,
    RetrievalMetricObservation P50Ms,
    RetrievalMetricObservation P95Ms,
    RetrievalMetricObservation P99Ms,
    RetrievalMetricObservation NoHitAccuracy,
    RetrievalMetricObservation ResolutionAccuracy,
    RetrievalMetricObservation RoleIntentAccuracy)
{
    // Compatibility constructor for in-repo synthetic fixtures. Serialized v4 evidence always
    // uses the primary observation contract and is validated with its real denominators.
    internal RetrievalMetrics(
        double mrr, double recallAt10, double ndcgAt10, double exactFirstAccuracy,
        int temporalLeakageFailures, double p50Ms, double p95Ms, double p99Ms,
        double noHitAccuracy, double resolutionAccuracy, double roleIntentAccuracy)
        : this(
            RetrievalMetricObservation.Measured(mrr, 1),
            RetrievalMetricObservation.Measured(recallAt10, 1),
            RetrievalMetricObservation.Measured(ndcgAt10, 1),
            RetrievalMetricObservation.Measured(mrr, 1),
            RetrievalMetricObservation.Measured(recallAt10, 1),
            RetrievalMetricObservation.Measured(ndcgAt10, 1),
            RetrievalMetricObservation.Measured(exactFirstAccuracy, 1),
            RetrievalMetricObservation.Measured(temporalLeakageFailures, 1),
            RetrievalMetricObservation.Measured(p50Ms, 1),
            RetrievalMetricObservation.Measured(p95Ms, 1),
            RetrievalMetricObservation.Measured(p99Ms, 1),
            RetrievalMetricObservation.Measured(noHitAccuracy, 1),
            RetrievalMetricObservation.Measured(resolutionAccuracy, 1),
            RetrievalMetricObservation.Measured(roleIntentAccuracy, 1))
    {
    }
}

public sealed record RetrievalBenchmarkReport(
    string Schema,
    string Timestamp,
    int SampleCount,
    string ReviewStatus,
    string BaselineSchema,
    string ExpectedCasesSha256,
    string ActualCasesSha256,
    string ReviewAttestation,
    string CodeCommit,
    string CorpusCommit,
    string ManifestId,
    string ModelId,
    string ModelRevision,
    string Machine,
    string ResourceConfiguration,
    double ModelLoadMs,
    double ColdQueryMs,
    long ProcessMemoryBytes,
    long MemoryLimitBytes,
    long IndexBytes,
    long VectorBytes,
    RetrievalMetrics KeywordTuning,
    RetrievalMetrics HybridTuning,
    RetrievalMetrics KeywordHoldout,
    RetrievalMetrics HybridHoldout,
    int TuningSampleCount,
    int HoldoutSampleCount,
    bool ActivationGatePassed,
    IReadOnlyList<string> GateFailures,
    IReadOnlyList<RetrievalBenchmarkStratum>? HoldoutStrata = null,
    RetrievalNegativeControlResult? ShuffledTop10Control = null,
    RetrievalNegativeControlResult? QrelsShuffleControl = null,
    string? CaseResultsSchema = null,
    string? CaseResultsFile = null,
    int CaseResultsCount = 0,
    string? CaseResultsSha256 = null);

public sealed record RetrievalBenchmarkRun(
    RetrievalBenchmarkReport Report,
    byte[] CaseResultsJsonl);

public static class RetrievalBenchmarkGate
{
    internal const int MaxCaseResultsBytes = 64 * 1024 * 1024;
    internal const int MaxCaseResultLineBytes = 64 * 1024;

    private static readonly HashSet<string> RatioStratumMetrics = new(StringComparer.Ordinal)
    {
        "anchor_mrr", "anchor_recall_at10", "anchor_ndcg_at10",
        "legacy_work_mrr", "legacy_work_recall_at10", "legacy_work_ndcg_at10",
    };

    private static readonly HashSet<string> BlockingStratumMetrics = new(StringComparer.Ordinal)
    {
        "anchor_exact_first_accuracy", "temporal_leakage_failures", "no_hit_accuracy",
        "resolution_accuracy", "role_intent_accuracy",
    };

    private static readonly string[] ReportedStratumMetrics =
    [
        "anchor_mrr", "anchor_recall_at10", "anchor_ndcg_at10",
        "legacy_work_mrr", "legacy_work_recall_at10", "legacy_work_ndcg_at10",
        "latency_p95_ms",
    ];

    private static readonly HashSet<string> CaseResultFields = new(StringComparer.Ordinal)
    {
        "schema", "stage", "case_id", "collection", "category", "split",
        "qrel_set_sha256", "ranked_coordinates_at10", "anchor_gains_at10",
        "mrr", "recall_at10", "ndcg_at10", "work_mrr", "work_recall_at10",
        "work_ndcg_at10", "exact_first_accuracy", "temporal_leakage_failures",
        "latency_ms", "no_hit_accuracy", "resolution_accuracy", "role_intent_accuracy",
    };

    private static readonly string[] CaseResultMetricFields =
    [
        "mrr", "recall_at10", "ndcg_at10", "work_mrr", "work_recall_at10",
        "work_ndcg_at10", "exact_first_accuracy", "temporal_leakage_failures",
        "latency_ms", "no_hit_accuracy", "resolution_accuracy", "role_intent_accuracy",
    ];

    private static readonly HashSet<string> MetricObservationFields = new(StringComparer.Ordinal)
    {
        "value", "denominator", "status",
    };

    private static readonly JsonSerializerOptions CaseResultJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static bool HasReleaseIdentity(
        string codeCommit, string corpusCommit, string manifestId,
        string modelId, string modelRevision, string machine,
        string resourceConfiguration, long memoryLimitBytes)
    {
        return codeCommit is not null && codeCommit.Length == 40 && codeCommit.All(Uri.IsHexDigit)
            && Known(corpusCommit, "unknown")
            && Known(manifestId, "unverified", "legacy")
            && Known(modelId, "none", "unknown")
            && Known(modelRevision, "none", "unknown")
            && Known(machine, "unknown", "not supplied")
            && Known(resourceConfiguration, "unknown", "not supplied")
            && memoryLimitBytes > 0;
    }

    public static bool HasReleaseIdentity(RetrievalBenchmarkReport report) => HasReleaseIdentity(
        report.CodeCommit, report.CorpusCommit, report.ManifestId,
        report.ModelId, report.ModelRevision, report.Machine,
        report.ResourceConfiguration, report.MemoryLimitBytes);

    public static bool ReportsAreCompatible(
        IReadOnlyCollection<RetrievalBenchmarkReport> reports, int expectedCount)
    {
        return reports.Count == expectedCount
            && reports.All(report => IsStructurallyValid(report))
            && reports.All(HasReleaseIdentity)
            && reports.Select(item => item.CodeCommit).Distinct(StringComparer.Ordinal).Count() == 1
            && reports.Select(item => $"{item.ModelId}@{item.ModelRevision}")
                .Distinct(StringComparer.Ordinal).Count() == 1
            && reports.Select(item => item.ResourceConfiguration)
                .Distinct(StringComparer.Ordinal).Count() == 1
            && reports.Select(item => item.MemoryLimitBytes).Distinct().Count() == 1;
    }

    public static bool IsStructurallyValid(
        RetrievalBenchmarkReport report, string? expectedCollection = null)
    {
        if (report.Schema != "lex-retrieval-benchmark/4"
            || report.SampleCount is <= 0 or > 10_000
            || report.TuningSampleCount <= 0
            || report.HoldoutSampleCount <= 0
            || report.TuningSampleCount + report.HoldoutSampleCount != report.SampleCount
            || !double.IsFinite(report.ModelLoadMs) || report.ModelLoadMs < 0
            || !double.IsFinite(report.ColdQueryMs) || report.ColdQueryMs < 0
            || report.ProcessMemoryBytes < 0 || report.MemoryLimitBytes <= 0
            || report.IndexBytes <= 0 || report.VectorBytes < 0
            || report.GateFailures is null
            || report.GateFailures.Any(string.IsNullOrWhiteSpace)
            || report.GateFailures.Distinct(StringComparer.Ordinal).Count()
               != report.GateFailures.Count
            || report.ActivationGatePassed != (report.GateFailures.Count == 0)
            || !MetricsAreValid(report.KeywordTuning, report.TuningSampleCount)
            || !MetricsAreValid(report.HybridTuning, report.TuningSampleCount)
            || !MetricsAreValid(report.KeywordHoldout, report.HoldoutSampleCount)
            || !MetricsAreValid(report.HybridHoldout, report.HoldoutSampleCount)
            || !ControlIsValid(report.ShuffledTop10Control, "shuffled-top10/2",
                report.HoldoutSampleCount)
            || !ControlIsValid(report.QrelsShuffleControl, "qrels-shuffle/2",
                report.HoldoutSampleCount)
            || !StrataAreValid(report.HoldoutStrata, report.HoldoutSampleCount,
                expectedCollection)
            || !BlockingStrataCoverAggregate(report.HoldoutStrata!, report.HybridHoldout)
            || report.CaseResultsSchema != "lex-retrieval-case-results/1"
            || !IsSafeCaseResultsFileName(report.CaseResultsFile)
            || report.CaseResultsCount is <= 0 or > 40_000
            || report.CaseResultsCount != 2L * report.TuningSampleCount
                                         + 4L * report.HoldoutSampleCount
            || !IsSha256(report.CaseResultsSha256))
            return false;

        if (report.ActivationGatePassed
            && (report.ShuffledTop10Control!.Outcome != "detected"
                || report.QrelsShuffleControl!.Outcome != "detected"
                || report.HoldoutStrata!.Any(row =>
                    row.Disposition == "blocking" && row.GatePassed is not true)))
            return false;

        return true;
    }

    public static bool CaseResultsMatch(
        RetrievalBenchmarkReport report, string caseResultsPath)
    {
        try
        {
            var declaredLength = new FileInfo(caseResultsPath).Length;
            if (!CaseResultsSizeIsValid(report, declaredLength)) return false;
            using var stream = new FileStream(caseResultsPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            return stream.Length == declaredLength && CaseResultsStreamMatches(report, stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException
                                          or JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    public static bool StrataMatchCases(
        RetrievalBenchmarkReport report,
        IReadOnlyCollection<RetrievalBenchmarkCase> cases,
        string collection)
    {
        var holdout = cases.Where(item => item.Collection == collection
                                          && item.Split == "holdout").ToArray();
        if (holdout.Length == 0 || holdout.Length != report.HoldoutSampleCount
            || report.HoldoutStrata is null)
            return false;

        var expected = new Dictionary<(string Category, string Metric, string Disposition), int>();
        foreach (var category in holdout.GroupBy(item => item.Category, StringComparer.Ordinal))
        {
            var rows = category.ToArray();
            var anchorCount = rows.Count(item => item.RelevantAnchors is { Count: > 0 });
            var legacyWorkCount = rows.Count(item => item.RelevantWorks.Count > 0);
            foreach (var metric in ReportedStratumMetrics)
            {
                var denominator = metric switch
                {
                    "anchor_mrr" or "anchor_recall_at10" or "anchor_ndcg_at10" => anchorCount,
                    "legacy_work_mrr" or "legacy_work_recall_at10"
                        or "legacy_work_ndcg_at10" => legacyWorkCount,
                    _ => rows.Length,
                };
                expected[(category.Key, metric, "reported")] = denominator;
            }

            void AddBlocking(string metric, int denominator) =>
                expected[(category.Key, metric, "blocking")] = denominator;

            if (category.Key == "exact")
                AddBlocking("anchor_exact_first_accuracy", anchorCount);
            if (rows.Any(item => item.AsOf is not null))
                AddBlocking("temporal_leakage_failures", rows.Count(item => item.AsOf is not null));
            if (rows.Any(item => item.ExpectNoHits))
                AddBlocking("no_hit_accuracy", rows.Count(item => item.ExpectNoHits));
            if (rows.Any(item => item.ExpectedResolution is not null))
                AddBlocking("resolution_accuracy",
                    rows.Count(item => item.ExpectedResolution is not null));
            if (rows.Any(item => item.ExpectedRole is not null))
                AddBlocking("role_intent_accuracy", rows.Count(item => item.ExpectedRole is not null));
        }

        var actual = new Dictionary<(string Category, string Metric, string Disposition), int>();
        foreach (var row in report.HoldoutStrata)
        {
            if (row is null || !actual.TryAdd(
                    (row.Category, row.Metric, row.Disposition), row.Observation.Denominator))
                return false;
        }
        if (actual.Count != expected.Count) return false;
        foreach (var (key, expectedDenominator) in expected)
        {
            if (!actual.TryGetValue(key, out var denominator)
                || denominator != expectedDenominator)
                return false;
        }
        return true;
    }

    public static bool CaseResultsBytesMatch(
        RetrievalBenchmarkReport report, byte[] bytes)
    {
        if (!CaseResultsSizeIsValid(report, bytes.LongLength)) return false;
        using var stream = new MemoryStream(bytes, writable: false);
        return CaseResultsStreamMatches(report, stream);
    }

    internal static bool CaseResultsSizeIsValid(RetrievalBenchmarkReport report, long length) =>
        report.CaseResultsCount is > 0 and <= 40_000
        && IsSha256(report.CaseResultsSha256)
        && length > 0
        && length <= MaxCaseResultsBytes
        && length <= (long)report.CaseResultsCount * MaxCaseResultLineBytes;

    private static bool CaseResultsStreamMatches(
        RetrievalBenchmarkReport report, Stream stream)
    {
        if (!CaseResultsSizeIsValid(report, stream.Length)) return false;

        try
        {
            var stagesByCase = new Dictionary<string, (string Split, HashSet<string> Stages)>(
                StringComparer.Ordinal);
            var collections = report.HoldoutStrata?.Select(row => row.Collection)
                .Distinct(StringComparer.Ordinal).ToArray() ?? [];
            if (collections.Length != 1) return false;

            var input = new byte[64 * 1024];
            var line = new byte[MaxCaseResultLineBytes - 1];
            var lineLength = 0;
            var rowCount = 0;
            long totalBytes = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = stream.Read(input, 0, input.Length)) != 0)
            {
                totalBytes += read;
                if (totalBytes > MaxCaseResultsBytes
                    || totalBytes > (long)report.CaseResultsCount * MaxCaseResultLineBytes)
                    return false;
                hash.AppendData(input, 0, read);
                for (var index = 0; index < read; index++)
                {
                    var value = input[index];
                    if (value == (byte)'\r') return false;
                    if (value != (byte)'\n')
                    {
                        if (lineLength >= line.Length) return false;
                        line[lineLength++] = value;
                        continue;
                    }

                    if (lineLength == 0 || line[0] != (byte)'{'
                        || !CaseResultLineMatches(line.AsMemory(0, lineLength), collections[0],
                            stagesByCase))
                        return false;
                    rowCount++;
                    if (rowCount > report.CaseResultsCount) return false;
                    lineLength = 0;
                }
            }

            if (lineLength != 0 || rowCount != report.CaseResultsCount
                || !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()),
                    report.CaseResultsSha256, StringComparison.OrdinalIgnoreCase)
                || stagesByCase.Count != report.SampleCount
                || stagesByCase.Values.Count(item => item.Split == "tuning")
                   != report.TuningSampleCount
                || stagesByCase.Values.Count(item => item.Split == "holdout")
                   != report.HoldoutSampleCount)
                return false;
            var tuningStages = new HashSet<string>(
                ["keyword-tuning", "hybrid-tuning"], StringComparer.Ordinal);
            var holdoutStages = new HashSet<string>(
                ["keyword-holdout", "hybrid-holdout", "shuffled-top10/2", "qrels-shuffle/2"],
                StringComparer.Ordinal);
            return stagesByCase.Values.All(item => item.Stages.SetEquals(
                item.Split == "tuning" ? tuningStages : holdoutStages));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException
                                          or DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool CaseResultLineMatches(
        ReadOnlyMemory<byte> line, string expectedCollection,
        IDictionary<string, (string Split, HashSet<string> Stages)> stagesByCase)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        var fields = document.RootElement.EnumerateObject()
            .Select(property => property.Name).ToArray();
        if (fields.Length != CaseResultFields.Count
            || !fields.ToHashSet(StringComparer.Ordinal).SetEquals(CaseResultFields))
            return false;
        foreach (var metricName in CaseResultMetricFields)
        {
            if (!document.RootElement.TryGetProperty(metricName, out var metric)
                || metric.ValueKind != JsonValueKind.Object)
                return false;
            var metricFields = metric.EnumerateObject()
                .Select(property => property.Name).ToArray();
            if (metricFields.Length != MetricObservationFields.Count
                || !metricFields.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(MetricObservationFields))
                return false;
        }
        var row = document.RootElement.Deserialize<RetrievalBenchmarkCaseResult>(CaseResultJson);
        if (!CaseResultIsValid(row, expectedCollection)) return false;
        if (!stagesByCase.TryGetValue(row!.CaseId, out var state))
            state = (row.Split, new HashSet<string>(StringComparer.Ordinal));
        if (state.Split != row.Split || !state.Stages.Add(row.Stage)) return false;
        stagesByCase[row.CaseId] = state;
        return true;
    }

    private static bool CaseResultIsValid(
        RetrievalBenchmarkCaseResult? row, string? expectedCollection)
    {
        if (row is null || row.Schema != "lex-retrieval-case-result/1"
            || string.IsNullOrWhiteSpace(row.CaseId)
            || string.IsNullOrWhiteSpace(row.Collection)
            || expectedCollection is not null
               && !string.Equals(row.Collection, expectedCollection, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(row.Category)
            || row.Split is not ("tuning" or "holdout")
            || row.Stage is not ("keyword-tuning" or "hybrid-tuning" or "keyword-holdout"
                or "hybrid-holdout" or "shuffled-top10/2" or "qrels-shuffle/2")
            || row.Split == "tuning" != row.Stage.EndsWith("-tuning", StringComparison.Ordinal)
            || !IsSha256(row.QrelSetSha256)
            || row.RankedCoordinatesAt10 is null || row.RankedCoordinatesAt10.Count > 10
            || row.RankedCoordinatesAt10.Any(string.IsNullOrWhiteSpace)
            || row.RankedCoordinatesAt10.Distinct(StringComparer.Ordinal).Count()
               != row.RankedCoordinatesAt10.Count
            || row.AnchorGainsAt10 is null
            || row.AnchorGainsAt10.Count != row.RankedCoordinatesAt10.Count
            || row.AnchorGainsAt10.Any(gain => gain is not (0 or 1 or 3)))
            return false;
        var ratios = new[]
        {
            row.Mrr, row.RecallAt10, row.NdcgAt10, row.WorkMrr, row.WorkRecallAt10,
            row.WorkNdcgAt10, row.ExactFirstAccuracy, row.NoHitAccuracy,
            row.ResolutionAccuracy, row.RoleIntentAccuracy,
        };
        var metrics = ratios.Concat([
            row.TemporalLeakageFailures, row.LatencyMs,
        ]).ToArray();
        return metrics.All(metric => metric is not null && metric.IsStructurallyCoherent()
                                             && metric.Denominator <= 1)
               && ratios.All(IsUnitIntervalOrInsufficient)
               && IsNonNegativeIntegerOrInsufficient(row.TemporalLeakageFailures)
               && row.LatencyMs.Denominator == 1
               && row.LatencyMs.TryGetMeasured(out var latency) && latency.Value >= 0;
    }

    private static bool MetricsAreValid(RetrievalMetrics? metrics, int sampleCount)
    {
        if (metrics is null) return false;
        var ratios = new[]
        {
            metrics.Mrr, metrics.RecallAt10, metrics.NdcgAt10,
            metrics.WorkMrr, metrics.WorkRecallAt10, metrics.WorkNdcgAt10,
            metrics.ExactFirstAccuracy, metrics.NoHitAccuracy,
            metrics.ResolutionAccuracy, metrics.RoleIntentAccuracy,
        };
        var observations = ratios.Concat([
            metrics.TemporalLeakageFailures, metrics.P50Ms, metrics.P95Ms, metrics.P99Ms,
        ]).ToArray();
        if (observations.Any(item => item is null || !item.IsStructurallyCoherent()
            || item.Denominator > sampleCount)
            || ratios.Any(item => !IsUnitIntervalOrInsufficient(item))
            || !IsNonNegativeIntegerOrInsufficient(metrics.TemporalLeakageFailures)
            || new[] { metrics.P50Ms, metrics.P95Ms, metrics.P99Ms }
                .Any(item => item.TryGetMeasured(out var value) && value.Value < 0))
            return false;

        var latencies = new[] { metrics.P50Ms, metrics.P95Ms, metrics.P99Ms };
        if (latencies.Any(item => item.Status != "measured"
                                  || item.Denominator != sampleCount))
            return false;
        return metrics.P50Ms.TryGetMeasured(out var p50)
               && metrics.P95Ms.TryGetMeasured(out var p95)
               && metrics.P99Ms.TryGetMeasured(out var p99)
               && p50.Value <= p95.Value && p95.Value <= p99.Value;
    }

    private static bool ControlIsValid(
        RetrievalNegativeControlResult? control, string expectedSchema, int holdoutCount)
    {
        if (control is null
            || control.Schema != expectedSchema
            || control.Severity != "product_gate"
            || control.EligibleDenominator < 0
            || control.EligibleDenominator > holdoutCount
            || control.FailedGateNames is null
            || control.FailedGateNames.Any(name => name is not
                ("anchor_mrr_not_below_unshuffled"
                 or "anchor_ndcg_at10_not_below_unshuffled"
                 or "anchor_recall_at10_not_below_unshuffled"))
            || control.FailedGateNames.Distinct(StringComparer.Ordinal).Count()
               != control.FailedGateNames.Count
            || control.OwnQrelSetRetainedCount < 0
            || control.OwnQrelSetRetainedCount > control.EligibleDenominator
            || control.AnchorNdcgAt10 is null
            || !control.AnchorNdcgAt10.IsStructurallyCoherent()
            || !IsUnitIntervalOrInsufficient(control.AnchorNdcgAt10))
            return false;

        if (control.EligibleDenominator == 0)
            return control.Outcome == "insufficient_denominator"
                   && control.FailedGateNames.Count == 0
                   && control.OwnQrelSetRetainedCount == 0
                   && control.AnchorNdcgAt10.Status == "insufficient_denominator"
                   && control.MembershipIdentical && control.NonRankingIdentical
                   && control.UnrelatedDenominatorsAndGatesIdentical;

        if (control.Outcome is not ("detected" or "escaped")
            || control.AnchorNdcgAt10.Denominator != control.EligibleDenominator)
            return false;
        if (control.Outcome == "escaped") return true;

        var detected = control.FailedGateNames.Count > 0
                       && control.MembershipIdentical
                       && control.NonRankingIdentical
                       && control.UnrelatedDenominatorsAndGatesIdentical;
        return expectedSchema == "shuffled-top10/2"
            ? detected && control.OwnQrelSetRetainedCount == 0
                      && !control.FailedGateNames.Contains(
                          "anchor_recall_at10_not_below_unshuffled", StringComparer.Ordinal)
            : detected && control.OwnQrelSetRetainedCount == 0
                       && control.AnchorNdcgAt10.TryGetMeasured(out var ndcg) && ndcg.Value < 0.15;
    }

    private static bool StrataAreValid(
        IReadOnlyList<RetrievalBenchmarkStratum>? strata, int holdoutCount,
        string? expectedCollection)
    {
        if (strata is null || strata.Count == 0
            || strata.Any(row => row is null)
            || strata.Select(row => row.Collection).Distinct(StringComparer.Ordinal).Count() != 1
            || strata.Select(row => $"{row.Collection}\0{row.Category}\0{row.Metric}")
                .Distinct(StringComparer.Ordinal).Count() != strata.Count)
            return false;

        foreach (var row in strata)
        {
            if (string.IsNullOrWhiteSpace(row.Collection)
                || expectedCollection is not null
                   && !string.Equals(row.Collection, expectedCollection, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(row.Category)
                || row.Split != "holdout"
                || row.InvariantFloor != 8
                || row.StatisticalFloor < row.InvariantFloor
                || row.Observation is null || !row.Observation.IsStructurallyCoherent()
                || row.Observation.Denominator > holdoutCount)
                return false;

            var expectedSupport = row.Observation.Denominator < row.InvariantFloor
                ? "insufficient_denominator"
                : row.Observation.Denominator < row.StatisticalFloor
                    ? "invariant_only_n8" : "statistically_supported";
            if (row.SupportStatus != expectedSupport) return false;

            if (row.Disposition == "reported")
            {
                if (row.GatePassed is not null
                    || row.Metric != "latency_p95_ms" && !RatioStratumMetrics.Contains(row.Metric)
                    || row.Metric == "latency_p95_ms"
                       && row.Observation.TryGetMeasured(out var latency) && latency.Value < 0
                    || RatioStratumMetrics.Contains(row.Metric)
                       && !IsUnitIntervalOrInsufficient(row.Observation))
                    return false;
                continue;
            }

            if (row.Disposition != "blocking" || !BlockingStratumMetrics.Contains(row.Metric)
                || row.GatePassed is null)
                return false;
            var expected = row.Metric == "temporal_leakage_failures" ? 0d : 1d;
            var passed = row.Observation.Denominator >= row.InvariantFloor
                         && row.Observation.TryGetMeasured(out var measured)
                         && measured.Value == expected;
            if (row.GatePassed != passed) return false;
        }
        return true;
    }

    private static bool IsUnitIntervalOrInsufficient(RetrievalMetricObservation observation) =>
        !observation.TryGetMeasured(out var value) || value.Value is >= 0 and <= 1;

    private static bool IsNonNegativeIntegerOrInsufficient(
        RetrievalMetricObservation observation) =>
        !observation.TryGetMeasured(out var value)
        || value.Value >= 0 && value.Value == Math.Truncate(value.Value);

    private static bool BlockingStrataCoverAggregate(
        IReadOnlyList<RetrievalBenchmarkStratum> strata, RetrievalMetrics holdout)
    {
        var aggregate = new Dictionary<string, RetrievalMetricObservation>(StringComparer.Ordinal)
        {
            ["anchor_exact_first_accuracy"] = holdout.ExactFirstAccuracy,
            ["temporal_leakage_failures"] = holdout.TemporalLeakageFailures,
            ["no_hit_accuracy"] = holdout.NoHitAccuracy,
            ["resolution_accuracy"] = holdout.ResolutionAccuracy,
            ["role_intent_accuracy"] = holdout.RoleIntentAccuracy,
        };
        foreach (var (metric, observation) in aggregate)
        {
            var rows = strata.Where(row => row.Disposition == "blocking"
                                           && row.Metric == metric).ToArray();
            if (rows.Sum(row => (long)row.Observation.Denominator) != observation.Denominator
                || observation.Denominator > 0 && rows.Length == 0)
                return false;
        }
        return true;
    }

    private static bool IsSha256(string? value) => value is not null
        && value.Length == 64 && value.All(Uri.IsHexDigit);

    internal static bool IsSafeCaseResultsFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.EndsWith(".jsonl", StringComparison.Ordinal)
        && value.All(character => character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');

    private static bool Known(string? value, params string[] placeholders) =>
        !string.IsNullOrWhiteSpace(value)
        && !placeholders.Contains(value, StringComparer.OrdinalIgnoreCase);
}

public static class RetrievalBenchmarkCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "exact", "temporal", "conceptual", "bilingual", "fuzzy", "hierarchy",
        "role", "comparison", "negative", "ambiguity", "gap",
    };

    public static RetrievalBenchmarkCaseSet LoadSet(Stream stream)
    {
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var payload = bytes.ToArray();
        List<RetrievalBenchmarkCase> cases;
        try
        {
            cases = JsonSerializer.Deserialize<List<RetrievalBenchmarkCase>>(payload, JsonOptions)
                    ?? throw new InvalidDataException("Retrieval benchmark cases are missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Retrieval benchmark cases are malformed JSON.", exception);
        }
        if (cases.Count is 0 or > 10_000 || cases.Any(item => item is null)
            || cases.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 128)
            || cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cases.Count)
            throw new InvalidDataException("Retrieval benchmark case identifiers are empty, duplicated, or unbounded.");
        foreach (var item in cases)
        {
            if (string.IsNullOrWhiteSpace(item.Collection)
                || item.Collection.Length > 128
                || item.Collection.Contains('\0')
                || item.Id.Contains('\0')
                || string.IsNullOrWhiteSpace(item.Query) || item.Query.Length > 1000
                || string.IsNullOrWhiteSpace(item.Explanation) || item.Explanation.Length > 2000
                || !Categories.Contains(item.Category)
                || item.Language is not ("en" or "fr")
                || item.TimeScope is not ("all_versions" or "as_of")
                || item.TimeScope == "as_of" != (item.AsOf is not null)
                || item.AsOf is not null && !DateOnly.TryParseExact(
                    item.AsOf, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _)
                || item.Split is not ("tuning" or "holdout")
                || item.ReviewStatus is not ("generated-unreviewed" or "engineer-reviewed" or "lawyer-reviewed")
                || item.RelevantWorks is null || item.RelevantWorks.Count > 10
                || item.RelevantWorks.Count != item.RelevantWorks.Distinct(StringComparer.Ordinal).Count()
                || item.RelevantWorks.Any(work => work.Contains('\0')
                    || !work.StartsWith(item.Collection + ":", StringComparison.Ordinal))
                || item.RelevantAnchors is { Count: > 100 }
                || item.RelevantAnchors is not null && item.RelevantAnchors.Any(anchor =>
                    string.IsNullOrWhiteSpace(anchor.Work) || anchor.Work.Length > 256
                    || string.IsNullOrWhiteSpace(anchor.Anchor) || anchor.Anchor.Length > 256
                    || anchor.Work.Contains('\0') || anchor.Anchor.Contains('\0')
                    || anchor.Work.Contains('#', StringComparison.Ordinal)
                    || anchor.Anchor.Contains('#', StringComparison.Ordinal)
                    || anchor.Gain is not (1 or 3)
                    || !item.RelevantWorks.Contains(anchor.Work, StringComparer.Ordinal))
                || item.RelevantAnchors is not null
                   && item.RelevantAnchors.Select(anchor => anchor.Coordinate)
                       .Distinct(StringComparer.Ordinal).Count() != item.RelevantAnchors.Count)
                throw new InvalidDataException($"Retrieval benchmark case '{item.Id}' is malformed.");
            if (item.ExpectNoHits && item.RelevantWorks.Count > 0
                || item.Category is ("negative" or "gap") && !item.ExpectNoHits
                || item.ExpectedResolution is not null
                   && item.ExpectedResolution is not ("not_requested" or "resolved" or "ambiguous" or "unresolved" or "unavailable")
                || item.ExpectedRole is not null
                   && item.ExpectedRole is not ("delegated" or "implementing" or "amending" or "corrigendum" or "consolidated"))
                throw new InvalidDataException($"Retrieval benchmark case '{item.Id}' has contradictory expectations.");
        }
        var splitLeak = cases.GroupBy(item =>
                (item.Collection, Query: NormalizeQuery(item.Query)))
            .FirstOrDefault(group => group.Select(item => item.Split)
                .Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (splitLeak is not null)
            throw new InvalidDataException(
                $"Query '{splitLeak.Key.Query}' occurs in both tuning and holdout for one collection.");
        return new RetrievalBenchmarkCaseSet(cases,
            Convert.ToHexStringLower(SHA256.HashData(payload)));
    }

    public static RetrievalBenchmarkCaseSet LoadSet(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadSet(stream);
    }

    public static IReadOnlyList<RetrievalBenchmarkCase> Load(Stream stream) => LoadSet(stream).Cases;
    public static IReadOnlyList<RetrievalBenchmarkCase> Load(string path) => LoadSet(path).Cases;

    public static RetrievalBenchmarkBaseline LoadBaseline(Stream stream)
    {
        RetrievalBenchmarkBaseline baseline;
        try
        {
            baseline = JsonSerializer.Deserialize<RetrievalBenchmarkBaseline>(stream, JsonOptions)
                       ?? throw new InvalidDataException("Retrieval benchmark baseline is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Retrieval benchmark baseline is malformed JSON.", exception);
        }
        if (baseline.Schema != "lex-retrieval-baseline/2"
            || string.IsNullOrWhiteSpace(baseline.CasesFile)
            || baseline.CasesSha256 is null || baseline.CasesSha256.Length != 64
            || baseline.CasesSha256.Any(character => !Uri.IsHexDigit(character))
            || baseline.SampleCount is <= 0 or > 10_000
            || baseline.ReviewStatus is not ("engineer-reviewed" or "lawyer-reviewed")
            || string.IsNullOrWhiteSpace(baseline.ReviewedBy)
            || !DateOnly.TryParseExact(baseline.ReviewedAt, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            throw new InvalidDataException("Retrieval benchmark baseline is incomplete or unsupported.");
        return baseline;
    }

    public static RetrievalBenchmarkBaseline LoadBaseline(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadBaseline(stream);
    }

    private static string NormalizeQuery(string value)
    {
        var characters = value.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public static class RetrievalBenchmarkRunner
{
    public sealed record Progress(string Stage, int Completed, int Total,
        TimeSpan Elapsed, TimeSpan? EstimatedRemaining);

    public static RetrievalBenchmarkRun RunWithCaseResults(
        LexIndexReader reader, RetrievalBenchmarkCaseSet caseSet,
        RetrievalBenchmarkBaseline baseline,
        string indexPath, string? vectorPath,
        string codeCommit, string manifestId, string machine, string resourceConfiguration,
        long memoryLimitBytes, double modelLoadMs, double coldQueryMs, DateTimeOffset timestamp,
        string caseResultsFile, Action<Progress>? progress = null)
    {
        if (!RetrievalBenchmarkGate.IsSafeCaseResultsFileName(caseResultsFile))
            throw new InvalidDataException("Per-case benchmark output must be a JSONL filename.");
        var cases = caseSet.Cases.Where(item => item.Collection == reader.Collection).ToArray();
        if (cases.Length == 0)
            throw new InvalidDataException(
                $"The benchmark contains no cases for mounted collection '{reader.Collection}'.");
        var tuning = cases.Where(item => item.Split == "tuning").ToArray();
        var holdout = cases.Where(item => item.Split == "holdout").ToArray();
        if (tuning.Length == 0 || holdout.Length == 0)
            throw new InvalidDataException("Every mounted collection requires tuning and holdout cases.");
        _ = reader.SearchHybrid("benchmark warmup", FilterSet.All, 1);
        var keywordEvaluation = RetrievalBenchmarkEvaluation.Evaluate("keyword-tuning", tuning,
            c => reader.SearchKeyword(c.Query, Filters(c), 10, c.Category == "fuzzy"), progress);
        var hybridEvaluation = RetrievalBenchmarkEvaluation.Evaluate("hybrid-tuning", tuning,
            c => reader.SearchHybrid(c.Query, Filters(c), 10), progress);
        var keywordHoldoutEvaluation = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", holdout,
            c => reader.SearchKeyword(c.Query, Filters(c), 10, c.Category == "fuzzy"), progress);
        var hybridHoldoutEvaluation = RetrievalBenchmarkEvaluation.Evaluate("hybrid-holdout", holdout,
            c => reader.SearchHybrid(c.Query, Filters(c), 10), progress);
        var keyword = keywordEvaluation.Metrics;
        var hybrid = hybridEvaluation.Metrics;
        var keywordHoldout = keywordHoldoutEvaluation.Metrics;
        var hybridHoldout = hybridHoldoutEvaluation.Metrics;
        var shuffledTop10 = RetrievalBenchmarkControls.ShuffledTop10(caseSet, hybridHoldoutEvaluation);
        var qrelsShuffle = RetrievalBenchmarkControls.QrelsShuffle(caseSet, hybridHoldoutEvaluation);
        var strata = RetrievalBenchmarkStrata.Build(reader.Collection, hybridHoldoutEvaluation);
        var caseRows = keywordEvaluation.CaseResults
            .Concat(hybridEvaluation.CaseResults)
            .Concat(keywordHoldoutEvaluation.CaseResults)
            .Concat(hybridHoldoutEvaluation.CaseResults)
            .Concat(shuffledTop10.Evaluation.CaseResults)
            .Concat(qrelsShuffle.Evaluation.CaseResults)
            .ToArray();
        var caseResultsJsonl = RetrievalBenchmarkEvaluation.SerializeJsonl(caseRows);
        var failures = BaselineFailures(caseSet, baseline).ToList();
        var corpusCommit = reader.Stamp.GetValueOrDefault("corpus_commit", "unknown");
        var modelId = reader.Stamp.GetValueOrDefault("embedding_model", "none");
        var modelRevision = reader.Stamp.GetValueOrDefault("embedding_revision", "none");
        if (!RetrievalBenchmarkGate.HasReleaseIdentity(
                codeCommit, corpusCommit, manifestId, modelId, modelRevision,
                machine, resourceConfiguration, memoryLimitBytes))
            failures.Add("release identity or resource configuration is missing or unverified");
        if (cases.Any(c => c.ReviewStatus is not ("engineer-reviewed" or "lawyer-reviewed")))
            failures.Add("relevance judgments have not been reviewed");
        failures.AddRange(RetrievalBenchmarkStrata.BlockingFailures(strata));
        if (shuffledTop10.Result.Outcome != "detected")
            failures.Add($"product gate: shuffled-top10/2 {shuffledTop10.Result.Outcome}");
        if (qrelsShuffle.Result.Outcome != "detected")
            failures.Add($"product gate: qrels-shuffle/2 {qrelsShuffle.Result.Outcome}");
        if (!AtLeast(hybridHoldout.ExactFirstAccuracy, 1))
            failures.Add("holdout exact legal identifier accuracy is below 100 percent");
        if (!EqualsValue(hybridHoldout.TemporalLeakageFailures, 0))
            failures.Add("holdout temporal leakage is not zero");
        if (!AtLeast(hybridHoldout.NoHitAccuracy, 1))
            failures.Add("holdout negative or gap accuracy is below 100 percent");
        if (!AtLeast(hybridHoldout.ResolutionAccuracy, 1))
            failures.Add("holdout work resolution accuracy is below 100 percent");
        if (!AtLeast(hybridHoldout.RoleIntentAccuracy, 1))
            failures.Add("holdout role intent accuracy is below 100 percent");
        var comparisonHybrid = RetrievalBenchmarkEvaluation.Score("comparison-hybrid",
            hybridHoldoutEvaluation.Observations.Where(
                item => item.Case.Category == "comparison").ToArray()).Metrics;
        if (!AtLeast(comparisonHybrid.RecallAt10, 1))
            failures.Add("holdout comparison anchor recall is below 100 percent");
        var conceptualKeyword = RetrievalBenchmarkEvaluation.Score("conceptual-keyword",
            keywordHoldoutEvaluation.Observations.Where(
                item => item.Case.Category == "conceptual").ToArray()).Metrics;
        var conceptualHybrid = RetrievalBenchmarkEvaluation.Score("conceptual-hybrid",
            hybridHoldoutEvaluation.Observations.Where(
                item => item.Case.Category == "conceptual").ToArray()).Metrics;
        if (!conceptualKeyword.NdcgAt10.TryGetMeasured(out var conceptualKeywordNdcg)
            || !conceptualHybrid.NdcgAt10.TryGetMeasured(out var conceptualHybridNdcg)
            || conceptualKeywordNdcg.Value == 0
            || conceptualHybridNdcg.Value < conceptualKeywordNdcg.Value * 1.10)
            failures.Add("conceptual nDCG@10 did not improve by at least 10 percent");
        if (!hybridHoldout.NdcgAt10.TryGetMeasured(out var hybridNdcg)
            || !keywordHoldout.NdcgAt10.TryGetMeasured(out var keywordNdcg)
            || hybridNdcg.Value + 0.000001 < keywordNdcg.Value * 0.98)
            failures.Add("holdout nDCG@10 regressed by more than 2 percent");
        failures.AddRange(HoldoutLatencyFailures(hybridHoldout));
        var workingSet = Process.GetCurrentProcess().WorkingSet64;
        if (memoryLimitBytes <= 0) failures.Add("configured memory limit was not supplied");
        else if (workingSet >= memoryLimitBytes * 0.75)
            failures.Add("process memory is not below 75 percent of the configured limit");

        var report = new RetrievalBenchmarkReport(
            "lex-retrieval-benchmark/4", timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            cases.Length, AggregateReviewStatus(cases), baseline.Schema,
            baseline.CasesSha256, caseSet.Sha256, $"{baseline.ReviewedBy}@{baseline.ReviewedAt}", codeCommit,
            corpusCommit, manifestId, modelId, modelRevision, machine, resourceConfiguration,
            modelLoadMs, coldQueryMs, workingSet, memoryLimitBytes, new FileInfo(indexPath).Length,
            vectorPath is not null && File.Exists(vectorPath) ? new FileInfo(vectorPath).Length : 0,
            keyword, hybrid, keywordHoldout, hybridHoldout, tuning.Length, holdout.Length,
            failures.Count == 0, failures, strata, shuffledTop10.Result, qrelsShuffle.Result,
            "lex-retrieval-case-results/1", caseResultsFile, caseRows.Length,
            Convert.ToHexStringLower(SHA256.HashData(caseResultsJsonl)));
        return new(report, caseResultsJsonl);
    }

    internal static IReadOnlyList<string> BaselineFailures(
        RetrievalBenchmarkCaseSet caseSet, RetrievalBenchmarkBaseline baseline)
    {
        var failures = new List<string>();
        if (!string.Equals(caseSet.Sha256, baseline.CasesSha256, StringComparison.OrdinalIgnoreCase))
            failures.Add("benchmark cases do not match the frozen baseline digest");
        if (caseSet.Cases.Count != baseline.SampleCount)
            failures.Add("benchmark case count does not match the frozen baseline");
        if (caseSet.Cases.Any(item => item.ReviewStatus != baseline.ReviewStatus))
            failures.Add("benchmark review status does not match the frozen attestation");
        return failures;
    }

    internal static IReadOnlyList<string> HoldoutLatencyFailures(RetrievalMetrics hybridHoldout) =>
        !hybridHoldout.P95Ms.TryGetMeasured(out var p95)
            ? ["holdout warm p95 has insufficient denominator"]
            : p95.Value > 250 ? ["holdout warm p95 exceeds 250 ms"] : [];

    private static bool AtLeast(RetrievalMetricObservation observation, double minimum) =>
        observation.TryGetMeasured(out var measured) && measured.Value >= minimum;

    private static bool EqualsValue(RetrievalMetricObservation observation, double expected) =>
        observation.TryGetMeasured(out var measured) && measured.Value == expected;

    private static string AggregateReviewStatus(IReadOnlyList<RetrievalBenchmarkCase> cases)
    {
        if (cases.All(c => c.ReviewStatus == "lawyer-reviewed")) return "lawyer-reviewed";
        if (cases.All(c => c.ReviewStatus is "engineer-reviewed" or "lawyer-reviewed")) return "reviewed";
        return "generated-unreviewed";
    }

    private static FilterSet Filters(RetrievalBenchmarkCase c) => new(
        c.AsOf is null ? null : DateOnly.ParseExact(c.AsOf, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture), null, null, c.Language,
        null, c.Hierarchy, null, null, c.Domain);

    internal static RetrievalMetrics Evaluate(
        string stage, IReadOnlyList<RetrievalBenchmarkCase> cases,
        Func<RetrievalBenchmarkCase, SearchExecution> search, Action<Progress>? progress)
        => RetrievalBenchmarkEvaluation.Evaluate(stage, cases, search, progress).Metrics;

    internal static string CanonicalCoordinate(RetrievalHit hit) =>
        $"{hit.Doc.Collection}:{hit.Doc.GroupKey}#{hit.Provision.Anchor}";
}

public static class RetrievalBenchmarkArtifactWriter
{
    private static readonly JsonSerializerOptions ReportJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static void Write(
        string reportPath, string caseResultsPath, RetrievalBenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var reportFullPath = Path.GetFullPath(reportPath);
        var caseResultsFullPath = Path.GetFullPath(caseResultsPath);
        var reportDirectory = Path.GetDirectoryName(reportFullPath)
                              ?? throw new InvalidDataException("Benchmark report has no directory.");
        var caseResultsDirectory = Path.GetDirectoryName(caseResultsFullPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(reportDirectory, caseResultsDirectory, pathComparison)
            || string.Equals(reportFullPath, caseResultsFullPath, pathComparison)
            || Path.GetFileName(caseResultsFullPath) != run.Report.CaseResultsFile)
            throw new InvalidDataException(
                "Per-case benchmark evidence must use the bound filename beside the report.");
        if (!RetrievalBenchmarkGate.IsStructurallyValid(run.Report)
            || !RetrievalBenchmarkGate.CaseResultsBytesMatch(
                run.Report, run.CaseResultsJsonl))
            throw new InvalidDataException(
                "Per-case benchmark evidence does not match the aggregate report binding.");

        Directory.CreateDirectory(reportDirectory);
        var reportBytes = JsonSerializer.SerializeToUtf8Bytes(run.Report, ReportJson);
        var token = Guid.NewGuid().ToString("N");
        var caseTemp = caseResultsFullPath + $".{token}.tmp";
        var reportTemp = reportFullPath + $".{token}.tmp";
        try
        {
            WriteDurably(caseTemp, run.CaseResultsJsonl);
            WriteDurably(reportTemp, reportBytes);
            File.Move(caseTemp, caseResultsFullPath, overwrite: true);
            File.Move(reportTemp, reportFullPath, overwrite: true);
        }
        finally
        {
            File.Delete(caseTemp);
            File.Delete(reportTemp);
        }
    }

    private static void WriteDurably(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
