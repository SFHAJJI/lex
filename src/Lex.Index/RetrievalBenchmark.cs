using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lex.Index;

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
    bool ExpectNoHits = false);

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

public sealed record RetrievalMetrics(
    double Mrr,
    double RecallAt10,
    double NdcgAt10,
    double ExactFirstAccuracy,
    int TemporalLeakageFailures,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double NoHitAccuracy,
    double ResolutionAccuracy,
    double RoleIntentAccuracy);

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
    IReadOnlyList<string> GateFailures);

public static class RetrievalBenchmarkGate
{
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
            && reports.All(HasReleaseIdentity)
            && reports.Select(item => item.CodeCommit).Distinct(StringComparer.Ordinal).Count() == 1
            && reports.Select(item => $"{item.ModelId}@{item.ModelRevision}")
                .Distinct(StringComparer.Ordinal).Count() == 1
            && reports.Select(item => item.ResourceConfiguration)
                .Distinct(StringComparer.Ordinal).Count() == 1
            && reports.Select(item => item.MemoryLimitBytes).Distinct().Count() == 1;
    }

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
                || item.RelevantWorks.Any(work => !work.StartsWith(item.Collection + ":", StringComparison.Ordinal)))
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

    public static RetrievalBenchmarkReport Run(
        LexIndexReader reader, RetrievalBenchmarkCaseSet caseSet,
        RetrievalBenchmarkBaseline baseline,
        string indexPath, string? vectorPath,
        string codeCommit, string manifestId, string machine, string resourceConfiguration,
        long memoryLimitBytes, double modelLoadMs, double coldQueryMs, DateTimeOffset timestamp,
        Action<Progress>? progress = null)
    {
        var cases = caseSet.Cases.Where(item => item.Collection == reader.Collection).ToArray();
        if (cases.Length == 0)
            throw new InvalidDataException(
                $"The benchmark contains no cases for mounted collection '{reader.Collection}'.");
        var tuning = cases.Where(item => item.Split == "tuning").ToArray();
        var holdout = cases.Where(item => item.Split == "holdout").ToArray();
        if (tuning.Length == 0 || holdout.Length == 0)
            throw new InvalidDataException("Every mounted collection requires tuning and holdout cases.");
        _ = reader.SearchHybrid("benchmark warmup", FilterSet.All, 1);
        var keyword = Evaluate("keyword-tuning", tuning,
            c => reader.SearchKeyword(c.Query, Filters(c), 10, c.Category == "fuzzy"), progress);
        var hybrid = Evaluate("hybrid-tuning", tuning,
            c => reader.SearchHybrid(c.Query, Filters(c), 10), progress);
        var keywordHoldout = Evaluate("keyword-holdout", holdout,
            c => reader.SearchKeyword(c.Query, Filters(c), 10, c.Category == "fuzzy"), progress);
        var hybridHoldout = Evaluate("hybrid-holdout", holdout,
            c => reader.SearchHybrid(c.Query, Filters(c), 10), progress);
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
        if (hybridHoldout.ExactFirstAccuracy < 1)
            failures.Add("holdout exact legal identifier accuracy is below 100 percent");
        if (hybridHoldout.TemporalLeakageFailures != 0) failures.Add("holdout temporal leakage is not zero");
        if (hybridHoldout.NoHitAccuracy < 1) failures.Add("holdout negative or gap accuracy is below 100 percent");
        if (hybridHoldout.ResolutionAccuracy < 1) failures.Add("holdout work resolution accuracy is below 100 percent");
        if (hybridHoldout.RoleIntentAccuracy < 1) failures.Add("holdout role intent accuracy is below 100 percent");
        var comparisonCases = holdout.Where(c => c.Category == "comparison").ToArray();
        var comparisonHybrid = Evaluate("comparison-hybrid", comparisonCases,
            c => reader.SearchHybrid(c.Query, Filters(c), 10), progress);
        if (comparisonHybrid.RecallAt10 < 1)
            failures.Add("holdout comparison work recall is below 100 percent");
        var conceptualCases = holdout.Where(c => c.Category == "conceptual").ToArray();
        var conceptualKeyword = Evaluate("conceptual-keyword", conceptualCases,
            c => reader.SearchKeyword(c.Query, Filters(c), 10, false), progress);
        var conceptualHybrid = Evaluate("conceptual-hybrid", conceptualCases,
            c => reader.SearchHybrid(c.Query, Filters(c), 10), progress);
        if (conceptualKeyword.NdcgAt10 == 0
            || conceptualHybrid.NdcgAt10 < conceptualKeyword.NdcgAt10 * 1.10)
            failures.Add("conceptual nDCG@10 did not improve by at least 10 percent");
        if (hybridHoldout.NdcgAt10 + 0.000001 < keywordHoldout.NdcgAt10 * 0.98)
            failures.Add("holdout nDCG@10 regressed by more than 2 percent");
        failures.AddRange(HoldoutLatencyFailures(hybridHoldout));
        var workingSet = Process.GetCurrentProcess().WorkingSet64;
        if (memoryLimitBytes <= 0) failures.Add("configured memory limit was not supplied");
        else if (workingSet >= memoryLimitBytes * 0.75)
            failures.Add("process memory is not below 75 percent of the configured limit");

        return new RetrievalBenchmarkReport(
            "lex-retrieval-benchmark/3", timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            cases.Length, AggregateReviewStatus(cases), baseline.Schema,
            baseline.CasesSha256, caseSet.Sha256, $"{baseline.ReviewedBy}@{baseline.ReviewedAt}", codeCommit,
            corpusCommit, manifestId, modelId, modelRevision, machine, resourceConfiguration,
            modelLoadMs, coldQueryMs, workingSet, memoryLimitBytes, new FileInfo(indexPath).Length,
            vectorPath is not null && File.Exists(vectorPath) ? new FileInfo(vectorPath).Length : 0,
            keyword, hybrid, keywordHoldout, hybridHoldout, tuning.Length, holdout.Length,
            failures.Count == 0, failures);
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
        hybridHoldout.P95Ms > 250 ? ["holdout warm p95 exceeds 250 ms"] : [];

    private static string AggregateReviewStatus(IReadOnlyList<RetrievalBenchmarkCase> cases)
    {
        if (cases.All(c => c.ReviewStatus == "lawyer-reviewed")) return "lawyer-reviewed";
        if (cases.All(c => c.ReviewStatus is "engineer-reviewed" or "lawyer-reviewed")) return "reviewed";
        return "generated-unreviewed";
    }

    private static FilterSet Filters(RetrievalBenchmarkCase c) => new(
        c.AsOf is null ? null : DateOnly.Parse(c.AsOf), null, null, c.Language,
        null, c.Hierarchy, null, null, c.Domain);

    internal static RetrievalMetrics Evaluate(
        string stage, IReadOnlyList<RetrievalBenchmarkCase> cases,
        Func<RetrievalBenchmarkCase, SearchExecution> search, Action<Progress>? progress)
    {
        var reciprocal = 0d;
        var recall = 0d;
        var ndcg = 0d;
        var exactCorrect = 0;
        var exactCount = 0;
        var noHitCorrect = 0;
        var noHitCount = 0;
        var resolutionCorrect = 0;
        var resolutionCount = 0;
        var roleCorrect = 0;
        var roleCount = 0;
        var rankingCount = 0;
        var leakage = 0;
        var latency = new List<double>(cases.Count);
        var phase = Stopwatch.StartNew();
        progress?.Invoke(new Progress(stage, 0, cases.Count, phase.Elapsed, null));
        for (var index = 0; index < cases.Count; index++)
        {
            var c = cases[index];
            var sw = Stopwatch.StartNew();
            var result = search(c);
            sw.Stop();
            latency.Add(sw.Elapsed.TotalMilliseconds);
            var completed = index + 1;
            if (completed == cases.Count || completed % 10 == 0)
                progress?.Invoke(new Progress(stage, completed, cases.Count, phase.Elapsed,
                    phase.Elapsed * (cases.Count - completed) / completed));
            var rankedWorks = result.Hits.Select(h =>
                    $"{h.Doc.Collection}:{h.Doc.GroupKey}".ToLowerInvariant())
                .Distinct(StringComparer.Ordinal).ToList();
            var first = rankedWorks.FindIndex(w => c.RelevantWorks.Contains(w, StringComparer.Ordinal));
            if (c.RelevantWorks.Count > 0)
            {
                rankingCount++;
                if (first >= 0) reciprocal += 1d / (first + 1);
                var worksAt10 = rankedWorks.Take(10).ToArray();
                var relevantAt10 = worksAt10
                    .Count(w => c.RelevantWorks.Contains(w, StringComparer.Ordinal));
                recall += (double)relevantAt10 / c.RelevantWorks.Count;
                var dcg = worksAt10.Select((work, rank) =>
                        c.RelevantWorks.Contains(work, StringComparer.Ordinal)
                            ? 1d / Math.Log2(rank + 2) : 0)
                    .Sum();
                var ideal = Enumerable.Range(0, Math.Min(10, c.RelevantWorks.Count))
                    .Sum(rank => 1d / Math.Log2(rank + 2));
                ndcg += dcg / ideal;
            }
            if (c.Category == "exact")
            {
                exactCount++;
                if (first == 0) exactCorrect++;
            }
            if (c.AsOf is not null)
                leakage += result.Hits.Count(h => string.CompareOrdinal(h.Doc.ValidFrom, c.AsOf) > 0
                    || h.Doc.ValidTo is not null && string.CompareOrdinal(h.Doc.ValidTo, c.AsOf) < 0);
            if (c.ExpectNoHits)
            {
                noHitCount++;
                if (result.Hits.Count == 0) noHitCorrect++;
            }
            if (c.ExpectedResolution is not null)
            {
                resolutionCount++;
                if (result.QueryPlan?.WorkResolutionStatus == c.ExpectedResolution) resolutionCorrect++;
            }
            if (c.ExpectedRole is not null)
            {
                roleCount++;
                if (result.QueryPlan?.RoleIntent == c.ExpectedRole) roleCorrect++;
            }
        }
        latency.Sort();
        double Percentile(double p) => latency.Count == 0 ? 0 : latency[(int)Math.Ceiling(p * latency.Count) - 1];
        return new RetrievalMetrics(
            rankingCount == 0 ? 1 : reciprocal / rankingCount,
            rankingCount == 0 ? 1 : recall / rankingCount,
            rankingCount == 0 ? 1 : ndcg / rankingCount,
            exactCount == 0 ? 1 : (double)exactCorrect / exactCount, leakage,
            Percentile(.50), Percentile(.95), Percentile(.99),
            noHitCount == 0 ? 1 : (double)noHitCorrect / noHitCount,
            resolutionCount == 0 ? 1 : (double)resolutionCorrect / resolutionCount,
            roleCount == 0 ? 1 : (double)roleCorrect / roleCount);
    }
}
