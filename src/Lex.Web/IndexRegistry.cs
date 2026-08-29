using Lex.Index;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.Web;

/// <summary>
/// The mounted indexes, one per publisher (D27), as a service rather than as a local variable
/// captured by twenty-five route lambdas.
///
/// Publishers are independent by design, so one unreadable index must not take the others down
/// with it: a refusal is logged loudly and the rest still mount. <c>/coverage</c> then reports the
/// survivors, which is the honest state of the service rather than a blank page.
/// </summary>
public sealed class IndexRegistry : IDisposable
{
    private readonly Dictionary<string, LexIndexReader> _readers = new(StringComparer.Ordinal);
    private readonly List<VerifiedArtifactManifest> _artifactManifests = [];
    private readonly HashSet<string> _verifiedFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HybridActivationStatus> _hybridActivations = new(StringComparer.Ordinal);
    private readonly ITextEncoder? _encoder;

    public IndexRegistry(IOptions<LexOptions> options, ILogger<IndexRegistry> log)
        : this(options, log, ArtifactTrustStore.Roots, path => MultilingualE5Encoder.Open(path))
    {
    }

    internal IndexRegistry(
        IOptions<LexOptions> options,
        ILogger<IndexRegistry> log,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        Func<string, ITextEncoder> openEncoder)
    {
        var dir = options.Value.IndexDir;
        if (!Directory.Exists(dir))
        {
            log.LogWarning("No index directory at {Dir}; the service will report empty coverage.", dir);
            return;
        }

        var manifests = Directory.EnumerateFiles(dir, "*.manifest.json").Order(StringComparer.Ordinal).ToList();
        if (manifests.Count == 0 && options.Value.RequireArtifactManifest)
        {
            log.LogCritical("Artifact manifests are required but none exist in {Dir}; no index will be mounted.", dir);
            return;
        }
        foreach (var manifest in manifests)
        {
            try
            {
                var signature = manifest[..^".json".Length] + ".sig";
                var manifestBytes = File.ReadAllBytes(manifest);
                foreach (var path in ArtifactManifests.VerifyDirectory(
                             dir, manifest, signature, trustRoots))
                    _verifiedFiles.Add(path);
                var parsed = ArtifactManifests.Parse(manifestBytes);
                _artifactManifests.Add(new VerifiedArtifactManifest(
                    Path.GetFileName(manifest),
                    Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
                    parsed.KeyId,
                    parsed.CodeCommit,
                    parsed.CreatedAt,
                    parsed.Files.Select(f => f.Path).ToArray(),
                    parsed.Sources));
                log.LogInformation("Verified artifact manifest {Manifest}", Path.GetFileName(manifest));
            }
            catch (Exception ex)
            {
                // Benchmark evidence is an independent signed artifact. A missing, corrupt or
                // untrusted report quarantines semantic retrieval without hiding a separately
                // verified legal database from deterministic keyword search.
                log.LogError("Artifact manifest {Manifest} was rejected: {Reason}",
                    Path.GetFileName(manifest), ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Value.EmbeddingModelDir))
        {
            try
            {
                var modelDir = Path.GetFullPath(options.Value.EmbeddingModelDir);
                if (manifests.Count > 0)
                    foreach (var file in new[] { "model-manifest.json", "model.onnx", "sentencepiece.bpe.model" })
                    {
                        var relative = Path.GetRelativePath(dir, Path.Combine(modelDir, file)).Replace('\\', '/');
                        if (!_verifiedFiles.Contains(relative))
                            throw new InvalidDataException($"embedding artifact '{relative}' is not in a verified manifest");
                    }
                _encoder = openEncoder(modelDir);
                log.LogInformation("Loaded local embedding model {Model} at {Revision}",
                    _encoder.ModelId, _encoder.ModelRevision);
            }
            catch (Exception ex)
            {
                log.LogError("Hybrid retrieval remains disabled: {Reason}", ex.Message);
            }
        }

        foreach (var db in Directory.EnumerateFiles(dir, "index-*.db"))
        {
            try
            {
                if (manifests.Count > 0 && !_verifiedFiles.Contains(Path.GetFileName(db)))
                    throw new InvalidDataException("the database is not listed by a verified artifact manifest");
                var vectorPath = Path.ChangeExtension(db, ".vectors");
                var vectorRelative = Path.GetRelativePath(dir, vectorPath).Replace('\\', '/');
                var reader = LexIndexReader.Open(db);
                var activation = HybridActivation(reader, Path.GetFileName(db), vectorRelative,
                    vectorPath, options.Value.CodeCommit);
                if (activation.Activated)
                {
                    try
                    {
                        reader.Dispose();
                        reader = LexIndexReader.Open(db, _encoder, vectorPath);
                    }
                    catch (Exception ex)
                    {
                        // Semantic vectors are a derived acceleration artifact. A corrupt or stale
                        // sidecar must disable hybrid retrieval for this publisher, not hide the
                        // independently verified legal index and its deterministic keyword search.
                        log.LogError(
                            "Hybrid retrieval disabled for {Db}; mounting verified lexical index: {Reason}",
                            Path.GetFileName(db), ex.Message);
                        reader = LexIndexReader.Open(db);
                        activation = new(false, "vector_open_failed");
                    }
                }
                _readers[reader.Collection] = reader;
                _hybridActivations[reader.Collection] = activation;
                log.LogInformation("Mounted {Db} ({Collection}, signature_valid={Valid})",
                    db, reader.Collection, reader.SignatureValid);
            }
            catch (Exception ex)
            {
                // Deliberately swallowed: see the class remarks. The message names the file and
                // the reason, which is what a stale schema needs in order to be diagnosed.
                log.LogError("Refused {Db}: {Reason}", db, ex.Message);
            }
        }

        if (_readers.Count == 0)
            log.LogWarning("No indexes mounted from {Dir}.", dir);
    }

    public IReadOnlyDictionary<string, LexIndexReader> All => _readers;

    public IReadOnlyDictionary<string, HybridActivationStatus> HybridActivations => _hybridActivations;

    public IReadOnlyList<VerifiedArtifactManifest> VerifiedArtifactManifests => _artifactManifests;

    public string? VerifiedManifestSetId => _artifactManifests.Count == 0 ? null
        : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(
            _artifactManifests.OrderBy(item => item.File, StringComparer.Ordinal)
                .Select(item => $"{item.Sha256}  {item.File}\n")))));

    public ReadinessReport Readiness(LexOptions options) =>
        ReadinessPolicy.Evaluate(_readers, options.RequiredPublisherSet,
            options.RequireArtifactManifest, options.ArtifactManifestId, VerifiedManifestSetId,
            _hybridActivations);

    public bool IsArtifactVerified(string relativePath) =>
        _verifiedFiles.Contains(relativePath.Replace('\\', '/'));

    public IEnumerable<LexIndexReader> Values => _readers.Values;

    public int Count => _readers.Count;

    public bool TryGet(string collection, out LexIndexReader reader) =>
        _readers.TryGetValue(collection, out reader!);

    /// <summary>The readers a request should ask, narrowed to one publisher when named.</summary>
    public IEnumerable<LexIndexReader> For(string? publisher) =>
        publisher is null ? _readers.Values : _readers.Values.Where(r => r.Collection == publisher);

    private HybridActivationStatus HybridActivation(
        LexIndexReader reader,
        string indexFile,
        string vectorRelative,
        string vectorPath,
        string? runtimeCodeCommit)
    {
        if (_encoder is null) return new(false, "encoder_unavailable");
        if (!File.Exists(vectorPath)) return new(false, "vector_missing");
        if (!_verifiedFiles.Contains(vectorRelative)) return new(false, "vector_unverified");

        var indexManifests = _artifactManifests.Where(item =>
            item.Artifacts.Contains(indexFile, StringComparer.Ordinal)
            && item.Artifacts.Contains(vectorRelative, StringComparer.Ordinal)).ToArray();
        if (indexManifests.Length != 1) return new(false, "index_manifest_missing");

        var benchmarkFile = $"retrieval-benchmark-{reader.Collection}.json";
        if (!_verifiedFiles.Contains(benchmarkFile)) return new(false, "benchmark_missing");
        var benchmarkManifests = _artifactManifests.Where(item =>
            item.Artifacts.Contains(benchmarkFile, StringComparer.Ordinal)).ToArray();
        if (benchmarkManifests.Length != 1) return new(false, "benchmark_missing");

        try
        {
            var report = JsonSerializer.Deserialize<RetrievalBenchmarkReport>(
                File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(vectorPath)!, benchmarkFile)),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            if (report is null
                || !RetrievalBenchmarkGate.IsStructurallyValid(report, reader.Collection))
                return new(false, "benchmark_invalid");
            if (!_verifiedFiles.Contains(report.CaseResultsFile!))
                return new(false, "benchmark_missing");
            if (!RetrievalBenchmarkGate.CaseResultsMatch(report, Path.Combine(
                    Path.GetDirectoryName(vectorPath)!, report.CaseResultsFile!)))
                return new(false, "benchmark_invalid");
            if (report.IndexBytes != new FileInfo(Path.Combine(
                    Path.GetDirectoryName(vectorPath)!, indexFile)).Length
                || report.VectorBytes != new FileInfo(vectorPath).Length)
                return new(false, "benchmark_identity_mismatch");
            return HybridActivationGate.Evaluate(
                report, reader.Collection, runtimeCodeCommit, reader.Stamp,
                _encoder.ModelId, _encoder.ModelRevision,
                indexManifests[0], benchmarkManifests[0]);
        }
        catch (Exception)
        {
            return new(false, "benchmark_invalid");
        }
    }

    public void Dispose()
    {
        foreach (var r in _readers.Values) r.Dispose();
        _readers.Clear();
        _encoder?.Dispose();
    }
}

public sealed record VerifiedArtifactManifest(
    string File,
    string Sha256,
    string KeyId,
    string CodeCommit,
    string CreatedAt,
    IReadOnlyList<string> Artifacts,
    IReadOnlyDictionary<string, string>? Sources = null);

public sealed record HybridActivationStatus(bool Activated, string Reason);

internal static class HybridActivationGate
{
    private static readonly RetrievalBenchmarkCaseSet Cases = LoadCases();
    private static readonly RetrievalBenchmarkBaseline Baseline = LoadBaseline();

    public static HybridActivationStatus Evaluate(
        RetrievalBenchmarkReport report,
        string collection,
        string? runtimeCodeCommit,
        IReadOnlyDictionary<string, string> indexStamp,
        string modelId,
        string modelRevision,
        VerifiedArtifactManifest indexManifest,
        VerifiedArtifactManifest benchmarkManifest)
    {
        var expected = Cases.Cases.Where(item => item.Collection == collection).ToArray();
        var failures = report.GateFailures;
        if (!RetrievalBenchmarkGate.IsStructurallyValid(report, collection)
            || report.BaselineSchema != Baseline.Schema
            || !string.Equals(Cases.Sha256, Baseline.CasesSha256,
                StringComparison.OrdinalIgnoreCase)
            || expected.Length == 0
            || report.SampleCount != expected.Length
            || report.TuningSampleCount != expected.Count(item => item.Split == "tuning")
            || report.HoldoutSampleCount != expected.Count(item => item.Split == "holdout")
            || failures is null
            || report.ReviewStatus is not ("reviewed" or "lawyer-reviewed")
            || !string.Equals(report.ExpectedCasesSha256, Baseline.CasesSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(report.ActualCasesSha256, Baseline.CasesSha256,
                StringComparison.OrdinalIgnoreCase)
            || report.ReviewAttestation != $"{Baseline.ReviewedBy}@{Baseline.ReviewedAt}")
            return new(false, "benchmark_invalid");

        var vectorFile = $"index-{collection}.vectors";
        var identityMatches = RetrievalBenchmarkGate.HasReleaseIdentity(report)
            && string.Equals(runtimeCodeCommit, report.CodeCommit, StringComparison.OrdinalIgnoreCase)
            && string.Equals(indexStamp.GetValueOrDefault("collection"), collection,
                StringComparison.Ordinal)
            && string.Equals(indexStamp.GetValueOrDefault("code_commit"), report.CodeCommit,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(indexStamp.GetValueOrDefault("corpus_commit"), report.CorpusCommit,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(indexStamp.GetValueOrDefault("embedding_model"), report.ModelId,
                StringComparison.Ordinal)
            && string.Equals(indexStamp.GetValueOrDefault("embedding_revision"), report.ModelRevision,
                StringComparison.Ordinal)
            && string.Equals(modelId, report.ModelId, StringComparison.Ordinal)
            && string.Equals(modelRevision, report.ModelRevision, StringComparison.Ordinal)
            && ClaimsMatchVerifiedManifests(report, collection, indexManifest, benchmarkManifest)
            && indexManifest.Artifacts.Contains(vectorFile, StringComparer.Ordinal)
            && indexManifest.Artifacts.Contains("model-manifest.json", StringComparer.Ordinal)
            && indexManifest.Artifacts.Contains("model.onnx", StringComparer.Ordinal)
            && indexManifest.Artifacts.Contains("sentencepiece.bpe.model", StringComparer.Ordinal);
        if (!identityMatches)
            return new(false, "benchmark_identity_mismatch");

        if (!RetrievalBenchmarkGate.StrataMatchCases(report, expected, collection))
            return new(false, "benchmark_invalid");

        if (!report.ActivationGatePassed)
            return new(false, "benchmark_gate_failed");

        var hybrid = report.HybridHoldout;
        var keyword = report.KeywordHoldout;
        if (hybrid.ExactFirstAccuracy.Value is not double exact || exact < 1
            || hybrid.TemporalLeakageFailures.Value is not double leakage || leakage != 0
            || hybrid.NoHitAccuracy.Value is not double noHit || noHit < 1
            || hybrid.ResolutionAccuracy.Value is not double resolution || resolution < 1
            || hybrid.RoleIntentAccuracy.Value is not double role || role < 1
            || hybrid.P95Ms.Value is not double p95 || p95 > 250
            || hybrid.NdcgAt10.Value is not double hybridNdcg
            || keyword.NdcgAt10.Value is not double keywordNdcg
            || hybridNdcg + 0.000001 < keywordNdcg * 0.98
            || report.MemoryLimitBytes <= 0
            || report.ProcessMemoryBytes >= report.MemoryLimitBytes * 0.75)
            return new(false, "benchmark_invalid");

        return new(true, "activated");
    }

    public static bool ClaimsMatchVerifiedManifests(
        RetrievalBenchmarkReport report,
        string collection,
        VerifiedArtifactManifest indexManifest,
        VerifiedArtifactManifest benchmarkManifest)
    {
        var benchmarkFile = $"retrieval-benchmark-{collection}.json";
        var indexFile = $"index-{collection}.db";
        return string.Equals(indexManifest.Sha256, report.ManifestId,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(indexManifest.CodeCommit, report.CodeCommit,
                   StringComparison.OrdinalIgnoreCase)
               && indexManifest.Artifacts.Contains(indexFile, StringComparer.Ordinal)
               && string.Equals(indexManifest.Sources?.GetValueOrDefault("collection"), collection,
                   StringComparison.Ordinal)
               && string.Equals(indexManifest.Sources?.GetValueOrDefault("corpus_commit"),
                   report.CorpusCommit, StringComparison.OrdinalIgnoreCase)
               && benchmarkManifest.Artifacts.Count == 2
               && benchmarkManifest.Artifacts.Contains(benchmarkFile, StringComparer.Ordinal)
               && benchmarkManifest.Artifacts.Contains(report.CaseResultsFile!, StringComparer.Ordinal)
               && string.Equals(benchmarkManifest.CodeCommit, report.CodeCommit,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(benchmarkManifest.Sources?.GetValueOrDefault("collection"), collection,
                   StringComparison.Ordinal)
               && string.Equals(benchmarkManifest.Sources?.GetValueOrDefault("corpus_commit"),
                   report.CorpusCommit, StringComparison.OrdinalIgnoreCase)
               && string.Equals(benchmarkManifest.Sources?.GetValueOrDefault("index_manifest_sha256"),
                   indexManifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static RetrievalBenchmarkCaseSet LoadCases()
    {
        using var stream = typeof(HybridActivationGate).Assembly
            .GetManifestResourceStream("Lex.Web.retrieval-cases.json")
            ?? throw new InvalidOperationException("Embedded retrieval benchmark cases are missing.");
        return RetrievalBenchmarkCatalog.LoadSet(stream);
    }

    private static RetrievalBenchmarkBaseline LoadBaseline()
    {
        using var stream = typeof(HybridActivationGate).Assembly
            .GetManifestResourceStream("Lex.Web.retrieval-baseline-v2.json")
            ?? throw new InvalidOperationException("Embedded retrieval benchmark baseline is missing.");
        return RetrievalBenchmarkCatalog.LoadBaseline(stream);
    }
}

public sealed record PublisherReadiness(
    string Publisher,
    bool Mounted,
    bool SignatureValid,
    int? ExpectedWorks,
    int ActualWorks,
    int BuildIssueCount,
    bool Complete,
    bool HybridReady,
    string HybridStatus);

public sealed record ReadinessReport(
    bool Ready,
    IReadOnlyList<string> RequiredPublishers,
    IReadOnlyList<string> MountedPublishers,
    string? ExpectedManifestSet,
    string? VerifiedManifestSet,
    IReadOnlyList<PublisherReadiness> Publishers,
    IReadOnlyList<string> Issues);

public static class ReadinessPolicy
{
    public static ReadinessReport Evaluate(
        IReadOnlyDictionary<string, LexIndexReader> readers,
        IReadOnlyList<string> requiredPublishers,
        bool requireArtifactManifest,
        string? expectedManifestSet,
        string? verifiedManifestSet,
        IReadOnlyDictionary<string, HybridActivationStatus>? hybridActivations = null)
    {
        var required = requiredPublishers.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var mounted = readers.Keys.Order(StringComparer.Ordinal).ToArray();
        var issues = new List<string>();
        if (required.Length == 0)
            issues.Add("required_publishers_not_configured");
        if (!required.SequenceEqual(mounted, StringComparer.Ordinal))
            issues.Add("mounted_publisher_set_mismatch");
        if (requireArtifactManifest)
        {
            if (string.IsNullOrWhiteSpace(expectedManifestSet)
                || string.Equals(expectedManifestSet, "legacy", StringComparison.Ordinal))
                issues.Add("expected_manifest_set_missing");
            if (string.IsNullOrWhiteSpace(verifiedManifestSet)
                || !string.Equals(expectedManifestSet, verifiedManifestSet,
                    StringComparison.OrdinalIgnoreCase))
                issues.Add("verified_manifest_set_mismatch");
        }

        var publishers = required.Select(publisher =>
        {
            if (!readers.TryGetValue(publisher, out var reader))
                return new PublisherReadiness(
                    publisher, false, false, null, 0, 0, false, false, "not_mounted");
            var coverage = reader.Coverage();
            var issueCount = coverage.BuildIssues?.Count ?? 0;
            var complete = reader.SignatureValid
                && coverage.ExpectedWorks is not null
                && coverage.Groups == coverage.ExpectedWorks
                && issueCount == 0;
            if (!reader.SignatureValid) issues.Add($"{publisher}:signature_invalid");
            if (coverage.ExpectedWorks is null) issues.Add($"{publisher}:expected_inventory_missing");
            else if (coverage.Groups != coverage.ExpectedWorks)
                issues.Add($"{publisher}:inventory_mismatch");
            if (issueCount > 0) issues.Add($"{publisher}:build_issues_present");
            var hybrid = hybridActivations?.GetValueOrDefault(publisher)
                         ?? new HybridActivationStatus(reader.HybridReady,
                             reader.HybridReady ? "activated" : "not_evaluated");
            return new PublisherReadiness(
                publisher, true, reader.SignatureValid, coverage.ExpectedWorks,
                coverage.Groups, issueCount, complete, hybrid.Activated, hybrid.Reason);
        }).ToArray();
        return new ReadinessReport(
            issues.Count == 0 && publishers.All(item => item.Complete),
            required, mounted, expectedManifestSet, verifiedManifestSet, publishers, issues);
    }
}
