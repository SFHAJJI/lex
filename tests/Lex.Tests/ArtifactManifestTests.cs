using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Index;
using Lex.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lex.Tests;

public sealed class ArtifactManifestTests : IDisposable
{
    private const string Collection = "lu-legilux";
    private const string CodeCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string CorpusCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-artifacts-{Guid.NewGuid():N}");

    public ArtifactManifestTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Signed_manifest_verifies_every_file_against_a_pinned_root()
    {
        File.WriteAllText(Path.Combine(_dir, "index-a.db"), "SQLite format 3 signed test index");
        File.WriteAllText(Path.Combine(_dir, "vectors.bin"), "compact vectors");
        var privateKey = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("release-2026", privateKey);
        var manifest = ArtifactManifests.Create(
            _dir,
            ["vectors.bin", "index-a.db"],
            root.KeyId,
            "2026-08-06T00:00:00Z",
            "abc123",
            new Dictionary<string, string> { ["corpus"] = "def456" });
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-a.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-a.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, privateKey));

        var verified = ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]);

        Assert.Equal(["index-a.db", "vectors.bin"], verified.Order(StringComparer.Ordinal));
        Assert.Equal("abc123", ArtifactManifests.Parse(bytes).CodeCommit);
    }

    [Fact]
    public void Editing_an_artifact_after_signing_fails_closed()
    {
        var file = Path.Combine(_dir, "index-a.db");
        File.WriteAllText(file, "original");
        var privateKey = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("release-2026", privateKey);
        var manifest = ArtifactManifests.Create(
            _dir, ["index-a.db"], root.KeyId, "2026-08-06T00:00:00Z", "abc123");
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-a.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-a.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, privateKey));
        File.WriteAllText(file, "tampered");

        Assert.ThrowsAny<CryptographicException>(() =>
            ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]));
    }

    [Fact]
    public void A_valid_signature_from_an_unpinned_replacement_key_is_rejected()
    {
        File.WriteAllText(Path.Combine(_dir, "index-a.db"), "original");
        var trustedPrivateKey = StampSigner.CreateKeyPem();
        var attackerPrivateKey = StampSigner.CreateKeyPem();
        var trustedRoot = ArtifactManifests.TrustRoot("trusted", trustedPrivateKey);
        var attackerRoot = ArtifactManifests.TrustRoot("attacker", attackerPrivateKey);
        var manifest = ArtifactManifests.Create(
            _dir, ["index-a.db"], attackerRoot.KeyId, "2026-08-06T00:00:00Z", "abc123");
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-a.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-a.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, attackerPrivateKey));

        Assert.ThrowsAny<CryptographicException>(() =>
            ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [trustedRoot]));
    }

    [Fact]
    public void Artifact_paths_cannot_escape_the_release_directory()
    {
        var key = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("release-2026", key);
        var manifest = new ArtifactManifest(
            ArtifactManifests.Schema,
            ArtifactManifests.Algorithm,
            root.KeyId,
            "2026-08-06T00:00:00Z",
            "abc123",
            new Dictionary<string, string>(),
            [new ArtifactFile("../outside.db", "sqlite-index", 1, new string('0', 64))]);
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "bad.manifest.json");
        var signaturePath = Path.Combine(_dir, "bad.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(bytes, key));

        Assert.Throws<InvalidDataException>(() =>
            ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]));
    }

    [Fact]
    public void Key_vault_base64url_signatures_are_accepted()
    {
        File.WriteAllText(Path.Combine(_dir, "index-eu.db"), "authoritative bytes");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportECPrivateKeyPem();
        var root = ArtifactManifests.TrustRoot("key-vault-v2", privateKey);
        var manifest = ArtifactManifests.Create(
            _dir, ["index-eu.db"], root.KeyId, "2026-08-06T00:00:00Z", "abc123");
        var bytes = ArtifactManifests.Serialize(manifest);
        var manifestPath = Path.Combine(_dir, "index-eu.manifest.json");
        var signaturePath = Path.Combine(_dir, "index-eu.manifest.sig");
        File.WriteAllBytes(manifestPath, bytes);
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        File.WriteAllText(signaturePath,
            Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        Assert.Single(ArtifactManifests.VerifyDirectory(_dir, manifestPath, signaturePath, [root]));
    }

    [Fact]
    public void Runtime_mounts_nothing_when_manifests_are_required_but_absent()
    {
        using var registry = new IndexRegistry(
            Options.Create(new LexOptions { IndexDir = _dir, RequireArtifactManifest = true }),
            NullLogger<IndexRegistry>.Instance);

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Runtime_mounts_vectors_only_when_the_signed_benchmark_passes()
    {
        var release = BuildHybridRelease(activationPassed: true);
        var options = new LexOptions
        {
            IndexDir = _dir,
            EmbeddingModelDir = _dir,
            CodeCommit = CodeCommit,
            RequiredPublishers = Collection,
        };

        using var registry = new IndexRegistry(
            Options.Create(options), NullLogger<IndexRegistry>.Instance,
            [release.Root], _ => new FakeEncoder());

        var reader = Assert.Single(registry.All).Value;
        Assert.True(reader.HybridReady);
        Assert.Equal(new HybridActivationStatus(true, "activated"),
            registry.HybridActivations[Collection]);
        var readiness = registry.Readiness(options);
        Assert.True(readiness.Ready);
        var publisher = Assert.Single(readiness.Publishers);
        Assert.True(publisher.HybridReady);
        Assert.Equal("activated", publisher.HybridStatus);
    }

    [Theory]
    [InlineData(false, true, "benchmark_gate_failed")]
    [InlineData(false, false, "benchmark_missing")]
    public void Failed_or_missing_benchmark_quarantines_vectors_without_hiding_keyword_index(
        bool activationPassed, bool includeBenchmark, string expectedReason)
    {
        var release = BuildHybridRelease(activationPassed, includeBenchmark);
        var options = new LexOptions
        {
            IndexDir = _dir,
            EmbeddingModelDir = _dir,
            CodeCommit = CodeCommit,
            RequiredPublishers = Collection,
        };

        using var registry = new IndexRegistry(
            Options.Create(options), NullLogger<IndexRegistry>.Instance,
            [release.Root], _ => new FakeEncoder());

        var reader = Assert.Single(registry.All).Value;
        Assert.False(reader.HybridReady);
        Assert.Equal("keyword", reader.SearchHybrid("known", FilterSet.All, 1).RetrievalMode);
        Assert.Equal(new HybridActivationStatus(false, expectedReason),
            registry.HybridActivations[Collection]);
        var readiness = registry.Readiness(options);
        Assert.True(readiness.Ready);
        var publisher = Assert.Single(readiness.Publishers);
        Assert.False(publisher.HybridReady);
        Assert.Equal(expectedReason, publisher.HybridStatus);
    }

    [Fact]
    public void Hybrid_activation_is_independent_per_publisher()
    {
        var lu = BuildHybridRelease(activationPassed: true);
        var eu = BuildHybridRelease(activationPassed: false, collection: "eu-eurlex");
        var options = new LexOptions
        {
            IndexDir = _dir,
            EmbeddingModelDir = _dir,
            CodeCommit = CodeCommit,
            RequiredPublishers = "eu-eurlex,lu-legilux",
        };

        using var registry = new IndexRegistry(
            Options.Create(options), NullLogger<IndexRegistry>.Instance,
            [lu.Root, eu.Root], _ => new FakeEncoder());

        Assert.True(registry.All[Collection].HybridReady);
        Assert.False(registry.All["eu-eurlex"].HybridReady);
        Assert.Equal("activated", registry.HybridActivations[Collection].Reason);
        Assert.Equal("benchmark_gate_failed", registry.HybridActivations["eu-eurlex"].Reason);
        Assert.True(registry.Readiness(options).Ready);
    }

    [Fact]
    public void Invalid_benchmark_signature_does_not_hide_the_verified_keyword_index()
    {
        var release = BuildHybridRelease(activationPassed: true);
        File.WriteAllText(
            Path.Combine(_dir, $"retrieval-benchmark-{Collection}.manifest.sig"), "invalid");
        var options = new LexOptions
        {
            IndexDir = _dir,
            EmbeddingModelDir = _dir,
            CodeCommit = CodeCommit,
            RequiredPublishers = Collection,
        };

        using var registry = new IndexRegistry(
            Options.Create(options), NullLogger<IndexRegistry>.Instance,
            [release.Root], _ => new FakeEncoder());

        Assert.Single(registry.All);
        Assert.False(registry.All[Collection].HybridReady);
        Assert.Equal("benchmark_missing", registry.HybridActivations[Collection].Reason);
    }

    [Fact]
    public void Invalid_database_digest_still_refuses_the_publisher()
    {
        var release = BuildHybridRelease(activationPassed: true);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.AppendAllText(Path.Combine(_dir, $"index-{Collection}.db"), "tampered");

        using var registry = new IndexRegistry(
            Options.Create(new LexOptions
            {
                IndexDir = _dir,
                EmbeddingModelDir = _dir,
                CodeCommit = CodeCommit,
                RequiredPublishers = Collection,
            }), NullLogger<IndexRegistry>.Instance,
            [release.Root], _ => new FakeEncoder());

        Assert.Equal(0, registry.Count);
    }

    private (ArtifactTrustRoot Root, string PrivateKey) BuildHybridRelease(
        bool activationPassed, bool includeBenchmark = true, string collection = Collection)
    {
        var privateKey = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot($"test-release-{collection}", privateKey);
        var database = Path.Combine(_dir, $"index-{collection}.db");
        var vectors = Path.ChangeExtension(database, ".vectors");
        var issues = "[]";
        var document = new DocRow(
            $"{collection}:known:2024-01-01", collection, "known", "urn:known", "LOI", "fr",
            "2024-01-01", null, "publisher_applicability", "2024-01-01", false, false,
            false, "record", "body", "https://example.test/known", "Known law", "Known law",
            null, "2024-01-01", null);
        var text = "known legal text";
        var provision = new ProvisionRow(
            $"{document.Key}|fr|2024-01-01", 0, "art_1", $"{document.Key}#art_1",
            "article", "1", null, null, null, document.Title, text,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        using (var encoder = new FakeEncoder())
            IndexBuilder.Build(database, new Dictionary<string, string>
                {
                    ["collection"] = collection,
                    ["code_commit"] = CodeCommit,
                    ["corpus_commit"] = CorpusCommit,
                    ["scope_expected_works"] = "1",
                    ["build_issues_json"] = issues,
                    ["build_issues_digest"] = Convert.ToHexStringLower(
                        SHA256.HashData(Encoding.UTF8.GetBytes(issues))),
                }, [document], [provision], [], [], privateKey,
                semantic: new SemanticBuildOptions(
                    encoder, vectors, "model-sha", "tokenizer-sha"));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        File.WriteAllText(Path.Combine(_dir, "model-manifest.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "model.onnx"), "test model");
        File.WriteAllText(Path.Combine(_dir, "sentencepiece.bpe.model"), "test tokenizer");
        var indexManifest = ArtifactManifests.Create(
            _dir,
            [$"index-{collection}.db", $"index-{collection}.vectors", "model-manifest.json",
                "model.onnx", "sentencepiece.bpe.model"],
            root.KeyId, "2026-08-15T00:00:00Z", CodeCommit,
            new Dictionary<string, string>
            {
                ["collection"] = collection,
                ["corpus_commit"] = CorpusCommit,
            });
        var indexManifestBytes = WriteManifest(
            $"index-{collection}", indexManifest, privateKey);
        if (!includeBenchmark) return (root, privateKey);

        var baseline = RetrievalBenchmarkCatalog.LoadBaseline(
            Path.Combine(RepoRoot(), "evals", "retrieval-baseline-v2.json"));
        var cases = RetrievalBenchmarkCatalog.Load(
            Path.Combine(RepoRoot(), "evals", "retrieval-cases.json"))
            .Where(item => item.Collection == collection).ToArray();
        var tuningCount = cases.Count(item => item.Split == "tuning");
        var holdoutCount = cases.Count(item => item.Split == "holdout");
        RetrievalMetrics Metrics(int denominator)
        {
            var measured = RetrievalMetricObservation.Measured(1, denominator);
            return new RetrievalMetrics(
                measured, measured, measured, measured, measured, measured,
                measured, RetrievalMetricObservation.Measured(0, denominator),
                measured, measured, measured, measured, measured, measured);
        }
        var tuningMetrics = Metrics(tuningCount);
        var holdoutMetrics = Metrics(holdoutCount);
        var controlNdcg = RetrievalMetricObservation.Measured(0, 8);
        RetrievalNegativeControlResult Control(string schema) => new(
            schema, "detected", "product_gate", 8,
            ["anchor_ndcg_at10_not_below_unshuffled"], true, true, true, 0, controlNdcg);
        IReadOnlyList<RetrievalBenchmarkStratum> strata =
        [
            new("anchor_exact_first_accuracy", collection, "exact", "holdout", "blocking",
                8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), true),
            new("anchor_ndcg_at10", collection, "conceptual", "holdout", "reported",
                8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), null),
        ];
        var insufficient = RetrievalMetricObservation.Insufficient();
        RetrievalBenchmarkCaseResult Row(
            RetrievalBenchmarkCase benchmarkCase, string stage) => new(
            "lex-retrieval-case-result/1", stage, benchmarkCase.Id, collection,
            benchmarkCase.Category, benchmarkCase.Split, new string('a', 64), [], [],
            insufficient, insufficient, insufficient, insufficient, insufficient,
            insufficient, insufficient, insufficient,
            RetrievalMetricObservation.Measured(1, 1), insufficient, insufficient, insufficient);
        var caseRows = cases.SelectMany(benchmarkCase => benchmarkCase.Split == "tuning"
            ? new[]
            {
                Row(benchmarkCase, "keyword-tuning"), Row(benchmarkCase, "hybrid-tuning"),
            }
            : new[]
            {
                Row(benchmarkCase, "keyword-holdout"), Row(benchmarkCase, "hybrid-holdout"),
                Row(benchmarkCase, "shuffled-top10/2"), Row(benchmarkCase, "qrels-shuffle/2"),
            }).ToArray();
        var caseJson = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var caseBytes = Encoding.UTF8.GetBytes(string.Concat(caseRows.Select(row =>
            JsonSerializer.Serialize(row, caseJson) + "\n")));
        var caseFile = $"retrieval-benchmark-{collection}.cases.jsonl";
        File.WriteAllBytes(Path.Combine(_dir, caseFile), caseBytes);
        var report = new RetrievalBenchmarkReport(
            "lex-retrieval-benchmark/4", "2026-08-15T00:00:00Z", cases.Length, "reviewed",
            baseline.Schema, baseline.CasesSha256, baseline.CasesSha256,
            $"{baseline.ReviewedBy}@{baseline.ReviewedAt}", CodeCommit, CorpusCommit,
            Convert.ToHexStringLower(SHA256.HashData(indexManifestBytes)),
            "test/e5", "test-revision", "test-runner", "1 cpu, 2 GiB", 1, 1, 100,
            2L * 1024 * 1024 * 1024, new FileInfo(database).Length,
            new FileInfo(vectors).Length, tuningMetrics, tuningMetrics,
            holdoutMetrics, holdoutMetrics, tuningCount, holdoutCount, activationPassed,
            activationPassed ? [] : ["holdout warm p95 exceeds 250 ms"],
            strata, Control("shuffled-top10/2"), Control("qrels-shuffle/2"),
            "lex-retrieval-case-results/1", caseFile, caseRows.Length,
            Convert.ToHexStringLower(SHA256.HashData(caseBytes)));
        var reportFile = $"retrieval-benchmark-{collection}.json";
        File.WriteAllBytes(Path.Combine(_dir, reportFile), JsonSerializer.SerializeToUtf8Bytes(
            report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        var benchmarkManifest = ArtifactManifests.Create(
            _dir, [reportFile, caseFile], root.KeyId, report.Timestamp, CodeCommit,
            new Dictionary<string, string>
            {
                ["collection"] = collection,
                ["corpus_commit"] = CorpusCommit,
                ["index_manifest_sha256"] = report.ManifestId,
            });
        WriteManifest($"retrieval-benchmark-{collection}", benchmarkManifest, privateKey);
        return (root, privateKey);
    }

    private byte[] WriteManifest(string stem, ArtifactManifest manifest, string privateKey)
    {
        var bytes = ArtifactManifests.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(_dir, $"{stem}.manifest.json"), bytes);
        File.WriteAllText(Path.Combine(_dir, $"{stem}.manifest.sig"),
            ArtifactManifests.SignBase64(bytes, privateKey));
        return bytes;
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lex.slnx")))
            directory = Directory.GetParent(directory)?.FullName
                        ?? throw new InvalidOperationException("Repository root not found.");
        return directory;
    }

    private sealed class FakeEncoder : ITextEncoder
    {
        public string ModelId => "test/e5";
        public string ModelRevision => "test-revision";
        public int Dimensions => 8;
        public int CountTokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        public int PrefixLengthForTokens(string text, int maxTokens) => text.Length;
        public int SuffixStartForTokens(string text, int maxTokens) => 0;
        public float[] Encode(string text, EmbeddingInputKind kind) => [1, 0, 0, 0, 0, 0, 0, 0];
        public void Dispose() { }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
