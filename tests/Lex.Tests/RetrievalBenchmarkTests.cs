using Lex.Index;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Lex.Tests;

public sealed class RetrievalBenchmarkTests
{
    [Fact]
    public void Public_suite_has_the_frozen_cross_publisher_shape()
    {
        var cases = Cases();

        Assert.Equal(200, cases.Count);
        Assert.Equal(200, cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(200, cases.Select(c => string.Join('|',
            c.Category, c.Collection, c.Split, c.Query, c.Language, c.TimeScope, c.AsOf,
            c.Hierarchy, c.Domain, c.ExpectedResolution, c.ExpectedRole, c.ExpectNoHits,
            string.Join(',', c.RelevantWorks))).Distinct(StringComparer.Ordinal).Count());
        var expectedCounts = new Dictionary<string, int>
        {
            ["exact"] = 30, ["temporal"] = 25, ["conceptual"] = 40,
            ["bilingual"] = 25, ["fuzzy"] = 15, ["hierarchy"] = 15,
            ["role"] = 10, ["comparison"] = 10, ["negative"] = 10,
            ["ambiguity"] = 10, ["gap"] = 10,
        };
        Assert.Equal(expectedCounts, cases.GroupBy(c => c.Category)
            .ToDictionary(group => group.Key, group => group.Count()));
        Assert.Contains(cases, c => c.Category == "exact" && c.Query == "GDPR"
            && c.RelevantWorks.SequenceEqual(["eu-eurlex:32016r0679"]));
        Assert.Contains(cases, c => c.Category == "exact" && c.Query == "Code du travail"
            && c.RelevantWorks.SequenceEqual(["lu-legilux:loi-2006-07-31-n2"]));
        Assert.All(cases, c =>
        {
            Assert.NotEmpty(c.Explanation);
            Assert.Equal("engineer-reviewed", c.ReviewStatus);
            Assert.Contains(c.Collection, new[] { "eu-eurlex", "lu-legilux" });
            Assert.Contains(c.Split, new[] { "tuning", "holdout" });
            Assert.All(c.RelevantWorks, work => Assert.StartsWith(c.Collection + ":", work));
        });
        Assert.Contains(cases, c => c.Split == "tuning" && c.Collection == "eu-eurlex");
        Assert.Contains(cases, c => c.Split == "holdout" && c.Collection == "eu-eurlex");
        Assert.Contains(cases, c => c.Split == "tuning" && c.Collection == "lu-legilux");
        Assert.Contains(cases, c => c.Split == "holdout" && c.Collection == "lu-legilux");
        Assert.All(cases.Where(c => c.Category is "negative" or "gap"), c =>
        {
            Assert.Empty(c.RelevantWorks);
            Assert.True(c.ExpectNoHits);
        });
        Assert.All(cases.Where(c => c.Category == "comparison"), c =>
            Assert.Equal(2, c.RelevantWorks.Count));
        Assert.All(cases.Where(c => c.Category == "role"), c =>
            Assert.Contains(c.ExpectedRole, new[] { "delegated", "implementing" }));
        var filterDomains = cases.Where(c => c.Category == "hierarchy")
            .Select(c => c.Domain).Where(d => d is not null).Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var expectedDomains = new HashSet<string>(
        [
            "financial-services", "aml-corporate", "competition", "tax", "employment",
            "consumer-environment", "procurement-and-ip", "judicial-cooperation", "primary-eu-law",
        ], StringComparer.Ordinal);
        Assert.True(expectedDomains.IsSubsetOf(filterDomains),
            $"Missing filter domains: {string.Join(", ", expectedDomains.Except(filterDomains))}");
        var temporalDates = cases.Where(c => c.Category == "temporal" && c.AsOf is not null)
            .GroupBy(c => c.RelevantWorks.Single())
            .FirstOrDefault(group => group.Select(c => c.AsOf).Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.NotNull(temporalDates);
        Assert.Contains(temporalDates!, c => c.Split == "tuning");
        Assert.Contains(temporalDates!, c => c.Split == "holdout");
    }

    [Fact]
    public void Metrics_compare_canonical_collection_and_work_identity()
    {
        var wrongPublisher = Doc("lu-legilux", "shared");
        var hit = new RetrievalHit(wrongPublisher,
            new ProvisionRow("rid", 0, "art_1", "p", "article", "1", null, null, null,
                "title", "text", new string('a', 64)), "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            "identity-001", "exact", "shared", "en", "all_versions", null,
            ["eu-eurlex:shared"], "Canonical identity includes the collection.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout");

        var metrics = RetrievalBenchmarkRunner.Evaluate("identity", [benchmarkCase],
            _ => new SearchExecution("keyword", [hit], [], new SearchQueryPlan(
                "shared", "shared", [], null, null, false)), null);

        Assert.Equal(0, metrics.Mrr);
        Assert.Equal(0, metrics.RecallAt10);
        Assert.Equal(0, metrics.NdcgAt10);
    }

    [Fact]
    public void Negative_metrics_have_explicit_denominators_and_no_division_by_zero()
    {
        var benchmarkCase = new RetrievalBenchmarkCase(
            "negative-001", "negative", "zxqv", "en", "all_versions", null, [],
            "No held work is relevant.", "engineer-reviewed", Collection: "eu-eurlex",
            Split: "holdout", ExpectedResolution: "not_requested", ExpectNoHits: true);

        var metrics = RetrievalBenchmarkRunner.Evaluate("negative", [benchmarkCase],
            _ => new SearchExecution("keyword", [], [], new SearchQueryPlan(
                "zxqv", "zxqv", [], null, null, false)), null);

        Assert.Equal(1, metrics.NoHitAccuracy);
        Assert.Equal(1, metrics.ResolutionAccuracy);
        Assert.False(double.IsNaN(metrics.Mrr));
    }

    [Fact]
    public void Comparison_ndcg_scores_every_relevant_work_not_only_the_first()
    {
        RetrievalHit Hit(string work) => new(Doc("eu-eurlex", work),
            new ProvisionRow("rid-" + work, 0, "art_1", "p-" + work, "article", "1",
                null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            "comparison-001", "comparison", "compare a and b", "en", "all_versions", null,
            ["eu-eurlex:a", "eu-eurlex:b"], "Both works are relevant.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout");

        var metrics = RetrievalBenchmarkRunner.Evaluate("comparison", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("noise"), Hit("noise"), Hit("a"), Hit("b")], [],
                new SearchQueryPlan("compare a and b", "", ["a", "b"], null, null, true)), null);

        Assert.Equal(1, metrics.RecallAt10);
        Assert.InRange(metrics.NdcgAt10, 0.69, 0.70);
        Assert.Equal(0.5, metrics.Mrr);

        var incomplete = RetrievalBenchmarkRunner.Evaluate("comparison", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("a")], [],
                new SearchQueryPlan("compare a and b", "", ["a", "b"], null, null, true)), null);
        Assert.Equal(0.5, incomplete.RecallAt10);
        Assert.InRange(incomplete.NdcgAt10, 0.61, 0.62);
    }

    [Fact]
    public void Frozen_pre_tuning_baseline_binds_the_exact_case_artifact_and_splits()
    {
        var root = RepoRoot();
        var casePath = Path.Combine(root, "evals", "retrieval-cases.json");
        var baselinePath = Path.Combine(root, "evals", "retrieval-baseline-v2.json");
        var baseline = JsonNode.Parse(File.ReadAllText(baselinePath))!.AsObject();
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(casePath)));
        var caseSet = RetrievalBenchmarkCatalog.LoadSet(casePath);
        var cases = caseSet.Cases;
        var typedBaseline = RetrievalBenchmarkCatalog.LoadBaseline(baselinePath);

        Assert.Equal(digest, baseline["cases_sha256"]!.GetValue<string>());
        Assert.Equal(cases.Count, baseline["sample_count"]!.GetValue<int>());
        Assert.Equal("pending_signed_production_artifacts",
            baseline["measurement_status"]!.GetValue<string>());
        Assert.Empty(RetrievalBenchmarkRunner.BaselineFailures(caseSet, typedBaseline));
        Assert.Contains("digest", Assert.Single(RetrievalBenchmarkRunner.BaselineFailures(
            caseSet with { Sha256 = new string('0', 64) }, typedBaseline)));
        foreach (var collection in cases.GroupBy(item => item.Collection))
        {
            Assert.Equal(collection.Count(item => item.Split == "tuning"),
                baseline["collections"]![collection.Key]!["tuning"]!.GetValue<int>());
            Assert.Equal(collection.Count(item => item.Split == "holdout"),
                baseline["collections"]![collection.Key]!["holdout"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Malformed_gate_inputs_fail_closed_during_catalog_load()
    {
        static JsonObject Valid() => new()
        {
            ["id"] = "case-1", ["category"] = "exact", ["query"] = "identifier",
            ["language"] = "en", ["time_scope"] = "all_versions", ["as_of"] = null,
            ["relevant_works"] = new JsonArray("collection:work"),
            ["explanation"] = "reviewed identity lookup", ["review_status"] = "engineer-reviewed",
            ["collection"] = "collection", ["split"] = "holdout",
            ["expected_resolution"] = "resolved", ["expect_no_hits"] = false,
        };
        var malformed = new List<JsonObject>();
        var blankId = Valid(); blankId["id"] = " "; malformed.Add(blankId);
        var category = Valid(); category["category"] = "invented"; malformed.Add(category);
        var language = Valid(); language["language"] = "de"; malformed.Add(language);
        var time = Valid(); time["time_scope"] = "as_of"; time["as_of"] = "not-a-date"; malformed.Add(time);
        var missingWorks = Valid(); missingWorks["relevant_works"] = null; malformed.Add(missingWorks);
        var contradiction = Valid(); contradiction["expect_no_hits"] = true; malformed.Add(contradiction);
        var negative = Valid(); negative["category"] = "negative";
        negative["relevant_works"] = new JsonArray(); malformed.Add(negative);

        foreach (var item in malformed)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                new JsonArray(item).ToJsonString()));
            Assert.Throws<InvalidDataException>(() => RetrievalBenchmarkCatalog.LoadSet(stream));
        }
    }

    [Fact]
    public void Normalized_queries_cannot_cross_the_tuning_holdout_boundary()
    {
        JsonObject Case(string id, string split, string query) => new()
        {
            ["id"] = id, ["category"] = "conceptual", ["query"] = query,
            ["language"] = "en", ["time_scope"] = "all_versions", ["as_of"] = null,
            ["relevant_works"] = new JsonArray("collection:work"),
            ["explanation"] = "same retrieval judgment", ["review_status"] = "engineer-reviewed",
            ["collection"] = "collection", ["split"] = split, ["expect_no_hits"] = false,
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            new JsonArray(Case("one", "tuning", "Data-access duties"),
                Case("two", "holdout", "data access duties")).ToJsonString()));

        Assert.Throws<InvalidDataException>(() => RetrievalBenchmarkCatalog.LoadSet(stream));
    }

    [Fact]
    public void Release_identity_and_cross_collection_resources_fail_closed()
    {
        var eu = Report();
        var lu = Report() with { ManifestId = "other-publisher-manifest" };

        Assert.True(RetrievalBenchmarkGate.HasReleaseIdentity(eu));
        Assert.False(RetrievalBenchmarkGate.HasReleaseIdentity(eu with { CodeCommit = "uncommitted" }));
        Assert.False(RetrievalBenchmarkGate.HasReleaseIdentity(eu with { ManifestId = "unverified" }));
        Assert.False(RetrievalBenchmarkGate.HasReleaseIdentity(
            eu with { ResourceConfiguration = "not supplied" }));
        Assert.True(RetrievalBenchmarkGate.ReportsAreCompatible([eu, lu], 2));
        Assert.False(RetrievalBenchmarkGate.ReportsAreCompatible(
            [eu, lu with { ResourceConfiguration = "different" }], 2));
        Assert.False(RetrievalBenchmarkGate.ReportsAreCompatible(
            [eu, lu with { MemoryLimitBytes = eu.MemoryLimitBytes * 2 }], 2));
        Assert.False(RetrievalBenchmarkGate.ReportsAreCompatible([eu], 2));
    }

    [Fact]
    public void Verified_benchmark_claims_must_match_its_index_and_benchmark_manifests()
    {
        var report = Report() with
        {
            ManifestId = new string('1', 64),
            CodeCommit = new string('b', 40),
        };
        var manifests = new[]
        {
            new Lex.Web.VerifiedArtifactManifest("index.manifest.json", new string('1', 64),
                "key", report.CodeCommit, report.Timestamp, ["index-eu-eurlex.db"],
                new Dictionary<string, string>
                {
                    ["collection"] = "eu-eurlex",
                    ["corpus_commit"] = report.CorpusCommit,
                }),
            new Lex.Web.VerifiedArtifactManifest("benchmark.manifest.json", new string('2', 64),
                "key", report.CodeCommit, report.Timestamp,
                ["retrieval-benchmark-eu-eurlex.json"],
                new Dictionary<string, string>
                {
                    ["collection"] = "eu-eurlex",
                    ["corpus_commit"] = report.CorpusCommit,
                    ["index_manifest_sha256"] = new string('1', 64),
                }),
        };

        Assert.True(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report, "eu-eurlex", manifests));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report with { ManifestId = new string('3', 64) }, "eu-eurlex", manifests));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report with { CodeCommit = new string('c', 40) }, "eu-eurlex", manifests));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report, "eu-eurlex",
            [manifests[0] with { CodeCommit = new string('c', 40) }, manifests[1]]));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report, "eu-eurlex",
            [manifests[0], manifests[1] with
            {
                Sources = new Dictionary<string, string>
                {
                    ["collection"] = "eu-eurlex",
                    ["corpus_commit"] = report.CorpusCommit,
                    ["index_manifest_sha256"] = new string('4', 64),
                },
            }]));
    }

    [Fact]
    public void Hybrid_activation_requires_a_passing_report_bound_to_the_exact_runtime()
    {
        const string collection = "lu-legilux";
        var cases = Cases().Where(item => item.Collection == collection).ToArray();
        var baseline = RetrievalBenchmarkCatalog.LoadBaseline(
            Path.Combine(RepoRoot(), "evals", "retrieval-baseline-v2.json"));
        var report = Report() with
        {
            SampleCount = cases.Length,
            TuningSampleCount = cases.Count(item => item.Split == "tuning"),
            HoldoutSampleCount = cases.Count(item => item.Split == "holdout"),
            BaselineSchema = baseline.Schema,
            ExpectedCasesSha256 = baseline.CasesSha256,
            ActualCasesSha256 = baseline.CasesSha256,
            ReviewStatus = baseline.ReviewStatus,
            ReviewAttestation = $"{baseline.ReviewedBy}@{baseline.ReviewedAt}",
            CorpusCommit = new string('c', 40),
            ManifestId = new string('1', 64),
            ModelId = "test/e5",
            ModelRevision = "test-revision",
        };
        var indexManifest = new Lex.Web.VerifiedArtifactManifest(
            "index-lu-legilux.manifest.json", report.ManifestId, "key", report.CodeCommit,
            report.Timestamp,
            ["index-lu-legilux.db", "index-lu-legilux.vectors", "model-manifest.json",
                "model.onnx", "sentencepiece.bpe.model"],
            new Dictionary<string, string>
            {
                ["collection"] = collection,
                ["corpus_commit"] = report.CorpusCommit,
            });
        var benchmarkManifest = new Lex.Web.VerifiedArtifactManifest(
            "retrieval-benchmark-lu-legilux.manifest.json", new string('2', 64), "key",
            report.CodeCommit, report.Timestamp, ["retrieval-benchmark-lu-legilux.json"],
            new Dictionary<string, string>
            {
                ["collection"] = collection,
                ["corpus_commit"] = report.CorpusCommit,
                ["index_manifest_sha256"] = indexManifest.Sha256,
            });
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = collection,
            ["code_commit"] = report.CodeCommit,
            ["corpus_commit"] = report.CorpusCommit,
            ["embedding_model"] = report.ModelId,
            ["embedding_revision"] = report.ModelRevision,
        };

        var accepted = Lex.Web.HybridActivationGate.Evaluate(
            report, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var failedGate = Lex.Web.HybridActivationGate.Evaluate(
            report with
            {
                ActivationGatePassed = false,
                GateFailures = ["holdout warm p95 exceeds 250 ms"],
            }, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var wrongRuntime = Lex.Web.HybridActivationGate.Evaluate(
            report, collection, new string('f', 40), stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);

        Assert.True(accepted.Activated);
        Assert.Equal("activated", accepted.Reason);
        Assert.False(failedGate.Activated);
        Assert.Equal("benchmark_gate_failed", failedGate.Reason);
        Assert.False(wrongRuntime.Activated);
        Assert.Equal("benchmark_identity_mismatch", wrongRuntime.Reason);
    }

    [Fact]
    public void Runner_reports_tuning_and_holdout_without_cross_split_aggregation()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-benchmark-{Guid.NewGuid():N}.db");
        try
        {
            var doc = Doc("eu-eurlex", "known");
            var provision = new ProvisionRow($"{doc.Key}|en|2024-01-01", 0, "art_1",
                $"{doc.Key}#art_1", "article", "1", null, null, null, doc.Title,
                "knownterm", Convert.ToHexStringLower(
                    SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("knownterm"))));
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "eu-eurlex",
                ["built_at"] = "2026-08-09T00:00:00Z",
                ["corpus_commit"] = "corpus-commit",
            }, [doc], [provision], [], [], null);
            using var reader = LexIndexReader.Open(db);
            var cases = new[]
            {
                new RetrievalBenchmarkCase("tuning", "conceptual", "knownterm", "en",
                    "all_versions", null, ["eu-eurlex:known"], "known hit",
                    "engineer-reviewed", Collection: "eu-eurlex", Split: "tuning"),
                new RetrievalBenchmarkCase("holdout", "exact", "missingterm", "en",
                    "all_versions", null, ["eu-eurlex:known"], "known miss",
                    "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout"),
            };
            var caseSet = new RetrievalBenchmarkCaseSet(cases, new string('d', 64));
            var baseline = new RetrievalBenchmarkBaseline("lex-retrieval-baseline/2",
                "evals/retrieval-cases.json", caseSet.Sha256, 2, "engineer-reviewed",
                "reviewer", "2026-08-09");

            var report = RetrievalBenchmarkRunner.Run(reader, caseSet, baseline, db, null,
                new string('b', 40), new string('1', 64), "runner", "2 GiB", 2_147_483_648,
                1, 1, DateTimeOffset.Parse("2026-08-09T00:00:00Z"));

            Assert.Equal("lex-retrieval-benchmark/3", report.Schema);
            Assert.Equal(1, report.KeywordTuning.Mrr);
            Assert.Equal(1, report.HybridTuning.Mrr);
            Assert.Equal(0, report.KeywordHoldout.Mrr);
            Assert.Equal(0, report.HybridHoldout.Mrr);
            Assert.False(report.ActivationGatePassed);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Activation_latency_is_authorized_only_by_holdout_measurements()
    {
        var fastTuning = new RetrievalMetrics(1, 1, 1, 1, 0, 1, 10, 20, 1, 1, 1);
        var slowHoldout = fastTuning with { P95Ms = 251 };

        Assert.Empty(RetrievalBenchmarkRunner.HoldoutLatencyFailures(fastTuning));
        Assert.Equal("holdout warm p95 exceeds 250 ms",
            Assert.Single(RetrievalBenchmarkRunner.HoldoutLatencyFailures(slowHoldout)));
    }

    [Fact]
    public void Deployment_fetches_and_tracks_both_publisher_benchmark_manifests()
    {
        var root = RepoRoot();
        var fetch = File.ReadAllText(Path.Combine(root, "deploy", "fetch-indexes.sh"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));

        Assert.Contains("benchmark=\"retrieval-benchmark-$collection.json\"", fetch);
        Assert.DoesNotContain("if [ \"$repo\" = \"lex-corpus-eu-eurlex\" ]", fetch);
        Assert.Contains("retrieval-benchmark-eu-eurlex.manifest.json", workflow);
        Assert.Contains("retrieval-benchmark-lu-legilux.manifest.json", workflow);
    }

    private static DocRow Doc(string collection, string work) => new(
        $"{collection}:{work}:2024-01-01", collection, work, $"urn:{work}", "REG", "en",
        "2024-01-01", null, "publisher", "2026-08-09T00:00:00Z", false,
        true, true, "record", "body", "https://example.test", "title", "title",
        null, "2024-01-01", null);

    private static RetrievalBenchmarkReport Report()
    {
        var metrics = new RetrievalMetrics(1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1);
        return new RetrievalBenchmarkReport(
            "lex-retrieval-benchmark/3", "2026-08-09T00:00:00Z", 1, "reviewed",
            "lex-retrieval-baseline/2", new string('a', 64), new string('a', 64),
            "reviewer@2026-08-09", new string('b', 40), "corpus-commit", "manifest-digest",
            "intfloat/multilingual-e5-small", "model-revision", "runner-1", "1 cpu, 2 GiB",
            1, 1, 100, 2L * 1024 * 1024 * 1024, 100, 100,
            metrics, metrics, metrics, metrics, 1, 1, true, []);
    }

    private static IReadOnlyList<RetrievalBenchmarkCase> Cases()
    {
        return RetrievalBenchmarkCatalog.Load(
            Path.Combine(RepoRoot(), "evals", "retrieval-cases.json"));
    }


    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lex.slnx")))
            directory = Directory.GetParent(directory)?.FullName
                        ?? throw new InvalidOperationException("Repository root not found.");
        return directory;
    }
}
