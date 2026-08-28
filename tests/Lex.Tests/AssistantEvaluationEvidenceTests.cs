using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Evaluation;
using Lex.Index;
using Lex.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lex.Tests;

public sealed class AssistantEvaluationEvidenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"lex-eval-evidence-{Guid.NewGuid():N}");

    public AssistantEvaluationEvidenceTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Production_container_keeps_one_shared_evidence_cache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(
            new LexOptions { IndexDir = Path.Combine(_dir, "absent-indexes") }));
        services.AddSingleton<IndexRegistry>();
        services.AddSingleton(TimeProvider.System);
        services.AddAssistantEvaluationEvidence();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAssistantEvaluationEvidenceProvider>();
        var second = provider.GetRequiredService<IAssistantEvaluationEvidenceProvider>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Signed_package_binds_the_report_to_the_exact_runtime()
    {
        var fixture = Package();

        var evidence = AssistantEvaluationEvidenceVerifier.Verify(
            fixture.Release, fixture.Files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority);

        Assert.True(evidence.Matches(fixture.Runtime));
        Assert.Equal(2, evidence.CaseCount);
        Assert.Equal(3, evidence.RepetitionCount);
        Assert.Equal(0.1248m, evidence.TotalCostEur);
        Assert.Equal(210, evidence.FirstOperationP95Milliseconds);
        Assert.Equal(420, evidence.TotalP99Milliseconds);
        Assert.Equal(25, evidence.BrowserP95Milliseconds);
        Assert.Equal(fixture.ReportSha256, evidence.ReportSha256);
        Assert.Equal(fixture.Runtime.CatalogSha256, evidence.CatalogSha256);
        Assert.Equal("gpt-5-mini", evidence.CandidateModelName);
        Assert.Equal("gpt-5-nano", evidence.GraderModelName);
    }

    [Fact]
    public void Signed_package_preserves_an_unavailable_relevance_score_as_null()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            root["results"]![0]!["relevance"]!["score"] = null;
            root["results"]![0]!["relevance"]!["unavailable_cause"] =
                "grader_finish_reason_length";
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        var evidence = AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority);

        var firstCase = evidence.CaseOutcomes.Single(item => item.CaseId == "one");
        Assert.Null(firstCase.RelevanceScores[0]);
        Assert.Equal<int?>(4, firstCase.RelevanceScores[1]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("string")]
    [InlineData("zero")]
    [InlineData("six")]
    public void Signed_package_rejects_an_invalid_nullable_relevance_score(string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var relevance = root["results"]![0]!["relevance"]!.AsObject();
            switch (mutation)
            {
                case "missing":
                    relevance.Remove("score");
                    break;
                case "string":
                    relevance["score"] = "5";
                    break;
                case "zero":
                    relevance["score"] = 0;
                    break;
                case "six":
                    relevance["score"] = 6;
                    break;
            }
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("missing_cause")]
    [InlineData("free_text_cause")]
    [InlineData("unknown_cause")]
    [InlineData("score_plus_cause")]
    [InlineData("null_plus_null")]
    [InlineData("billed_cause_without_usage")]
    [InlineData("unbilled_cause_with_usage")]
    public void Signed_package_rejects_incoherent_relevance_and_grader_usage(string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var result = root["results"]![0]!.AsObject();
            var relevance = result["relevance"]!.AsObject();
            relevance["score"] = null;
            relevance["unavailable_cause"] = "grader_finish_reason_length";
            switch (mutation)
            {
                case "missing_cause":
                    relevance.Remove("unavailable_cause");
                    break;
                case "free_text_cause":
                    relevance["unavailable_cause"] = "the grader leaked a raw failure message";
                    break;
                case "unknown_cause":
                    relevance["unavailable_cause"] = "grader_new_unreviewed_token";
                    break;
                case "score_plus_cause":
                    relevance["score"] = 5;
                    break;
                case "null_plus_null":
                    relevance["unavailable_cause"] = null;
                    break;
                case "billed_cause_without_usage":
                    result["grader_usage"]!["input_tokens"] = 0;
                    result["grader_usage"]!["output_tokens"] = 0;
                    break;
                case "unbilled_cause_with_usage":
                    relevance["unavailable_cause"] = "grader_not_configured";
                    break;
            }
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_aggregate_grader_usage_drift()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var result = root["results"]![0]!;
            result["relevance"]!["score"] = null;
            result["relevance"]!["unavailable_cause"] = "grader_not_configured";
            result["grader_usage"]!["input_tokens"] = 0;
            result["grader_usage"]!["output_tokens"] = 0;
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_grader_cost_drift()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            root["actual_grader_cost_eur"] = 0.04m;
            root["actual_total_cost_eur"] = 0.115m;
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_aggregate_grader_usage_only_drift()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
            root["actual_grader_usage"]!["input_tokens"] = 601);
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_candidate_usage_and_cost_drift()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            root["results"]![0]!["candidate_usage"]!["input_tokens"] = 301;
            root["actual_candidate_usage"]!["input_tokens"] = 1_001;
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_zero_candidate_run()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            root["actual_candidate_usage"]!["input_tokens"] = 0;
            root["actual_candidate_usage"]!["output_tokens"] = 0;
            root["actual_candidate_cost_eur"] = 0;
            root["actual_total_cost_eur"] = root["actual_grader_cost_eur"]!.DeepClone();
            foreach (var result in root["results"]!.AsArray())
            {
                result!["candidate_usage"]!["input_tokens"] = 0;
                result["candidate_usage"]!["output_tokens"] = 0;
            }
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_report_pricing_drift()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            root["pricing"]!["grader"]!["input"]!["euros_per_million"] = 70;
            root["pricing"]!["grader_input_euros_per_million"] = 70;
            root["actual_grader_cost_eur"] = 0.0438m;
            root["actual_total_cost_eur"] = 0.1188m;
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signed_package_rejects_unknown_report_properties(bool nestedInResult)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var target = nestedInResult ? root["results"]![0]!.AsObject() : root;
            target["raw_answer"] = canary;
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        var exception = Assert.Throws<InvalidDataException>(() =>
            AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
        Assert.False(exception.ToString().Contains(canary, StringComparison.Ordinal),
            "verification diagnostic contained private report content");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signed_package_rejects_duplicate_known_report_properties(bool nestedInResult)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        var report = Encoding.UTF8.GetString(
            files[AssistantEvaluationEvidenceVerifier.ReportFile]);
        var marker = nestedInResult ? "\"case_id\":" : "\"schema\":";
        var duplicate = nestedInResult
            ? "\"case_id\":\"duplicate\","
            : "\"schema\":\"duplicate\",";
        var index = report.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        files[AssistantEvaluationEvidenceVerifier.ReportFile] = Encoding.UTF8.GetBytes(
            report.Insert(index, duplicate));
        ResignManifestPayloadOnly(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("candidate_input_meter_id")]
    [InlineData("grader_output_meter_name")]
    [InlineData("candidate_input_effective_start_date")]
    [InlineData("candidate_model_name")]
    [InlineData("grader_model_version")]
    public void Signed_package_rejects_each_re_signed_pricing_snapshot_identity_drift(
        string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var pricing = root["pricing"]!;
            switch (mutation)
            {
                case "candidate_input_meter_id":
                    pricing["candidate"]!["input"]!["meter_id"] = "candidate-input-drift";
                    break;
                case "grader_output_meter_name":
                    pricing["grader"]!["output"]!["meter_name"] = "Grader output drift";
                    break;
                case "candidate_input_effective_start_date":
                    pricing["candidate"]!["input"]!["effective_start_date"] =
                        "2026-07-31T00:00:00Z";
                    break;
                case "candidate_model_name":
                    pricing["candidate"]!["model_name"] = "gpt-5-mini-drift";
                    break;
                case "grader_model_version":
                    pricing["grader"]!["model_version"] = "2025-08-08";
                    break;
            }
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_unknown_browser_evidence_properties_without_echoing_them()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
        MutateJson(files, AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile,
            root => root["raw_answer"] = canary);
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files, fixture.ReportSha256, fixture.Runtime.CodeCommit);

        var exception = Assert.Throws<InvalidDataException>(() =>
            AssistantEvaluationEvidenceVerifier.Verify(
                release, files, [fixture.ArtifactRoot], fixture.Now,
                fixture.AdmissionAuthority));
        Assert.DoesNotContain(canary, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Signed_package_rejects_duplicate_known_browser_evidence_properties()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        var browser = Encoding.UTF8.GetString(
            files[AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile]);
        var marker = "\"passed\":";
        var index = browser.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        files[AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile] = Encoding.UTF8.GetBytes(
            browser.Insert(index, "\"passed\":true,"));
        ResignManifestPayloadOnly(files, fixture.ArtifactKey,
            AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile);
        var release = ReleaseFor(files, fixture.ReportSha256, fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("failed_result")]
    [InlineData("missing_preflight")]
    [InlineData("missing_timings")]
    [InlineData("wrong_repetition")]
    [InlineData("wrong_grading_mode")]
    [InlineData("missing_prompt_sha256")]
    public void Signed_package_rejects_re_signed_deterministic_report_drift(string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var result = root["results"]![0]!.AsObject();
            switch (mutation)
            {
                case "failed_result":
                    result["passed"] = false;
                    break;
                case "missing_preflight":
                    root.Remove("preflight");
                    break;
                case "missing_timings":
                    result.Remove("timings");
                    break;
                case "wrong_repetition":
                    result["repetition"] = 99;
                    break;
                case "wrong_grading_mode":
                    result["grading_mode"] = "deterministic";
                    break;
                case "missing_prompt_sha256":
                    result.Remove("prompt_sha256");
                    break;
            }
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_rejects_re_signed_pricing_catalog_identity_drift()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            root["pricing"]!["currency"] = "USD";
            root["pricing"]!["candidate"]!["model_name"] = "drifted-model";
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("candidate_resource")]
    [InlineData("candidate_evidence")]
    [InlineData("grader_resource")]
    [InlineData("grader_endpoint")]
    [InlineData("grader_endpoint_path_with_same_digest")]
    [InlineData("grader_deployment")]
    [InlineData("grader_evidence")]
    public void Signed_package_rejects_re_signed_model_route_or_evidence_drift(string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var identity = root["identity"]!;
            switch (mutation)
            {
                case "candidate_resource":
                    identity["candidate_model"]!["resource_id"] = "/subscriptions/attacker";
                    break;
                case "candidate_evidence":
                    identity["candidate_model"]!["evidence_sha256"] = new string('8', 64);
                    break;
                case "grader_resource":
                    identity["grader_model"]!["resource_id"] = "/subscriptions/attacker";
                    break;
                case "grader_endpoint":
                    identity["grader_model"]!["endpoint"] = "https://attacker.example";
                    break;
                case "grader_endpoint_path_with_same_digest":
                    identity["grader_model"]!["endpoint"] = "https://grader.example/attacker";
                    break;
                case "grader_deployment":
                    identity["grader_model"]!["deployment"] = "attacker";
                    break;
                case "grader_evidence":
                    identity["grader_model"]!["evidence_sha256"] = new string('8', 64);
                    break;
            }
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("candidate_model", "candidate-models.example")]
    [InlineData("grader_model", "grader.example")]
    public void Signed_package_rejects_re_signed_empty_userinfo_model_authority(
        string route, string host)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile,
            root => root["identity"]![route]!["endpoint"] = $"https://@{host}");
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("candidate_model", "https://candidate-models\u3002example")]
    [InlineData("grader_model", "https://grader\uFF0Eexample")]
    [InlineData("candidate_model", "https://127.0.0.1.")]
    [InlineData("grader_model", "https://169.254.169.254.")]
    [InlineData("candidate_model", "https://candidate_models.example")]
    [InlineData("grader_model", "https://bad-.example")]
    public void Signed_package_rejects_fully_signed_noncanonical_model_authority(
        string route, string endpoint)
    {
        var fixture = route == "candidate_model"
            ? Package(candidateEndpoint: endpoint, useUncheckedEndpointIdentity: true)
            : Package(graderEndpoint: endpoint, useUncheckedEndpointIdentity: true);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            fixture.Release, fixture.Files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("candidate_resource")]
    [InlineData("grader_route")]
    [InlineData("target_resource")]
    [InlineData("malformed_candidate_resource")]
    public void Signed_package_rejects_coherently_resigned_identity_substitution(
        string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var identity = root["identity"]!;
            switch (mutation)
            {
                case "candidate_resource":
                    identity["candidate_model"]!["resource_id"] =
                        "/subscriptions/00000000-0000-0000-0000-000000000003/"
                        + "resourceGroups/hostile-rg/providers/"
                        + "Microsoft.CognitiveServices/accounts/hostile-candidate";
                    RecomputeModelEvidence(identity["candidate_model"]!);
                    break;
                case "grader_route":
                    identity["grader_model"]!["resource_id"] =
                        "/subscriptions/00000000-0000-0000-0000-000000000004/"
                        + "resourceGroups/hostile-rg/providers/"
                        + "Microsoft.CognitiveServices/accounts/hostile-grader";
                    identity["grader_model"]!["endpoint"] =
                        "https://hostile-grader.example";
                    identity["grader_model"]!["deployment"] =
                        "hostile-grader-deployment";
                    RecomputeModelEvidence(identity["grader_model"]!);
                    break;
                case "target_resource":
                    identity["target"]!["resource_id"] =
                        "/subscriptions/00000000-0000-0000-0000-000000000005/"
                        + "resourceGroups/hostile-rg/providers/"
                        + "Microsoft.App/containerApps/hostile-app";
                    RecomputeTargetEvidence(identity["target"]!);
                    break;
                case "malformed_candidate_resource":
                    identity["candidate_model"]!["resource_id"] =
                        "/subscriptions/------------------------------------/resourceGroups/ /"
                        + "providers/Microsoft.CognitiveServices/accounts/??";
                    RecomputeModelEvidence(identity["candidate_model"]!);
                    break;
            }
        });
        if (mutation == "target_resource")
        {
            var report = JsonNode.Parse(
                files[AssistantEvaluationEvidenceVerifier.ReportFile])!.AsObject();
            var digest = report["identity"]!["target"]!["evidence_sha256"]!
                .GetValue<string>();
            MutateJson(files, AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile,
                browser => browser["candidate_evidence_sha256"] = digest);
        }
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("candidate_input")]
    [InlineData("candidate_output")]
    [InlineData("grader_input")]
    [InlineData("grader_output")]
    public void Signed_package_rejects_re_signed_per_case_token_ceiling_overrun(string meter)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var result = root["results"]![0]!;
            var usage = meter.StartsWith("candidate", StringComparison.Ordinal)
                ? result["candidate_usage"]! : result["grader_usage"]!;
            var field = meter.EndsWith("input", StringComparison.Ordinal)
                ? "input_tokens" : "output_tokens";
            usage[field] = meter switch
            {
                "candidate_input" => 1_101,
                "candidate_output" => 111,
                "grader_input" => 4_097,
                "grader_output" => 51,
                _ => throw new InvalidOperationException("Unknown test meter."),
            };
            RecomputeReportUsageAndCost(root);
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Signed_package_accepts_candidate_usage_within_combined_history_ceiling()
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile, root =>
        {
            var usage = root["results"]![0]!["candidate_usage"]!;
            usage["input_tokens"] = 1_050;
            usage["output_tokens"] = 105;
            RecomputeReportUsageAndCost(root);
        });
        ResignManifest(files, fixture.ArtifactKey);
        var release = ReleaseFor(files,
            Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
            fixture.Runtime.CodeCommit);

        var evidence = AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority);

        Assert.Equal(1_750, evidence.CandidateInputTokens);
        Assert.Equal(175, evidence.CandidateOutputTokens);
    }

    [Theory]
    [InlineData("grading_rubric")]
    [InlineData("expected_gap_status")]
    [InlineData("expected_clarification")]
    [InlineData("expected_population_minimum")]
    [InlineData("expected_population_path")]
    [InlineData("history_expected_synthesis")]
    public void Signed_package_rejects_fully_signed_nested_catalog_type_drift(string mutation)
    {
        var fixture = Package(catalog =>
        {
            var evaluationCase = catalog["cases"]![0]!;
            var hostile = new JsonObject { ["unknown"] = true };
            switch (mutation)
            {
                case "grading_rubric":
                    evaluationCase["grading"]!["rubric"] = hostile;
                    break;
                case "expected_gap_status":
                    evaluationCase["expected"]!["gap_status"] = hostile;
                    break;
                case "expected_clarification":
                    evaluationCase["expected"]!["clarification"] = hostile;
                    break;
                case "expected_population_minimum":
                    evaluationCase["expected"]!["population_minimum"] = hostile;
                    break;
                case "expected_population_path":
                    evaluationCase["expected"]!["population_path"] = hostile;
                    break;
                case "history_expected_synthesis":
                    evaluationCase["history"]![0]!["expected_synthesis"] = hostile;
                    break;
            }
        });

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            fixture.Release, fixture.Files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Theory]
    [InlineData("empty_expected")]
    [InlineData("missing_llm_rubric")]
    [InlineData("unknown_expected_tool")]
    public void Signed_package_rejects_fully_signed_catalog_without_canonical_case_semantics(
        string mutation)
    {
        var fixture = Package(catalog =>
        {
            var evaluationCase = catalog["cases"]![0]!;
            switch (mutation)
            {
                case "empty_expected":
                    evaluationCase["expected"] = new JsonObject();
                    break;
                case "missing_llm_rubric":
                    evaluationCase["grading"]!.AsObject().Remove("rubric");
                    break;
                case "unknown_expected_tool":
                    evaluationCase["expected"]!["tool"] = "attacker_tool";
                    break;
            }
        });

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            fixture.Release, fixture.Files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Runtime_match_rejects_each_different_identity_domain()
    {
        var fixture = Package();
        var evidence = AssistantEvaluationEvidenceVerifier.Verify(
            fixture.Release, fixture.Files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority);

        Assert.False(evidence.Matches(fixture.Runtime with { CodeCommit = new string('9', 40) }));
        Assert.False(evidence.Matches(fixture.Runtime with { Revision = "ca-lex-web--different" }));
        Assert.False(evidence.Matches(fixture.Runtime with { RevisionHostname = "other.example" }));
        Assert.False(evidence.Matches(fixture.Runtime with { Image = "registry.example/lex@sha256:" + new string('9', 64) }));
        Assert.False(evidence.Matches(fixture.Runtime with { ArtifactManifestSet = new string('9', 64) }));
        Assert.False(evidence.Matches(fixture.Runtime with { CatalogSha256 = new string('9', 64) }));
        Assert.False(evidence.Matches(fixture.Runtime with { IndexManifestIds = [new string('9', 64)] }));
        Assert.False(evidence.Matches(fixture.Runtime with { CandidateModelHost = "other.example" }));
        Assert.False(evidence.Matches(fixture.Runtime with { CandidateDeployment = "other" }));
    }

    [Theory]
    [InlineData("report-bytes")]
    [InlineData("release-digest")]
    [InlineData("manifest-signature")]
    [InlineData("admission-signature")]
    [InlineData("signed-verdict-failed")]
    [InlineData("browser-failed")]
    [InlineData("browser-contract")]
    [InlineData("unbounded-model")]
    [InlineData("mutable-release")]
    public void Tampering_or_nonpassing_evidence_fails_closed(string mutation)
    {
        var fixture = Package();
        var files = fixture.Files.ToDictionary(item => item.Key, item => item.Value.ToArray(),
            StringComparer.Ordinal);
        var release = fixture.Release;

        switch (mutation)
        {
            case "report-bytes":
                files[AssistantEvaluationEvidenceVerifier.ReportFile][10] ^= 1;
                break;
            case "release-digest":
                release = release with
                {
                    Assets = release.Assets.ToDictionary(item => item.Key, item =>
                        item.Key == AssistantEvaluationEvidenceVerifier.ReportFile
                            ? item.Value with { Digest = "sha256:" + new string('0', 64) }
                            : item.Value, StringComparer.Ordinal),
                };
                break;
            case "manifest-signature":
                files[AssistantEvaluationEvidenceVerifier.ManifestSignatureFile][0] ^= 1;
                break;
            case "admission-signature":
                files[AssistantEvaluationEvidenceVerifier.AdmissionSignatureFile][0] ^= 1;
                ResignManifest(files, fixture.ArtifactKey);
                release = ReleaseFor(files, fixture.ReportSha256,
                    fixture.Runtime.CodeCommit);
                break;
            case "signed-verdict-failed":
                MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile,
                    root => root["activation_gate_passed"] = false);
                ResignManifest(files, fixture.ArtifactKey);
                release = ReleaseFor(files, Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
                    fixture.Runtime.CodeCommit);
                break;
            case "browser-failed":
                MutateJson(files, AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile,
                    root => root["passed"] = false);
                ResignManifest(files, fixture.ArtifactKey);
                release = ReleaseFor(files, fixture.ReportSha256, fixture.Runtime.CodeCommit);
                break;
            case "browser-contract":
                MutateJson(files, AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile,
                    root => root["viewport_width"] = 1);
                ResignManifest(files, fixture.ArtifactKey);
                release = ReleaseFor(files, fixture.ReportSha256, fixture.Runtime.CodeCommit);
                break;
            case "unbounded-model":
                MutateJson(files, AssistantEvaluationEvidenceVerifier.ReportFile,
                    root => root["identity"]!["grader_model"]!["model_name"] = new string('x', 201));
                ResignManifest(files, fixture.ArtifactKey);
                release = ReleaseFor(files,
                    Sha(files[AssistantEvaluationEvidenceVerifier.ReportFile]),
                    fixture.Runtime.CodeCommit);
                break;
            case "mutable-release":
                release = release with { Immutable = false };
                break;
        }

        Assert.ThrowsAny<Exception>(() => AssistantEvaluationEvidenceVerifier.Verify(
            release, files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public void Release_asset_set_is_closed_and_bounded()
    {
        var fixture = Package();
        var extra = fixture.Release with
        {
            Assets = fixture.Release.Assets.Append(new KeyValuePair<string, AssistantEvaluationReleaseAsset>(
                "surprise.txt", new(99, "surprise.txt", 1, "sha256:" + new string('0', 64),
                    "uploaded", "https://github.example/surprise")))
                .ToDictionary(StringComparer.Ordinal),
        };

        Assert.Throws<InvalidDataException>(() => AssistantEvaluationEvidenceVerifier.Verify(
            extra, fixture.Files, [fixture.ArtifactRoot], fixture.Now,
            fixture.AdmissionAuthority));
    }

    [Fact]
    public async Task GitHub_discovery_verifies_one_exact_release_and_coalesces_refreshes()
    {
        var fixture = Package();
        var handler = GitHub(fixture);
        using var provider = Provider(fixture, handler, () => fixture.Runtime);

        var snapshots = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => provider.GetAsync(CancellationToken.None)));

        Assert.All(snapshots, snapshot => Assert.True(snapshot.Verified));
        Assert.All(snapshots, snapshot => Assert.Equal(
            fixture.ReportSha256, snapshot.Evidence!.ReportSha256));
        Assert.Equal(11, handler.Requests);
    }

    [Fact]
    public async Task GitHub_discovery_never_calls_the_network_without_complete_runtime_identity()
    {
        var fixture = Package();
        var handler = GitHub(fixture);
        using var provider = Provider(fixture, handler, () => null);

        var snapshot = await provider.GetAsync(CancellationToken.None);

        Assert.False(snapshot.Verified);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task GitHub_discovery_fails_closed_for_mismatch_or_unbounded_refs()
    {
        var fixture = Package();
        var mismatchHandler = GitHub(fixture);
        using var mismatch = Provider(fixture, mismatchHandler,
            () => fixture.Runtime with { Revision = "ca-lex-web--rollback" });
        Assert.False((await mismatch.GetAsync(CancellationToken.None)).Verified);

        var tags = Enumerable.Range(0, 5)
            .Select(index => $"assistant-eval-{fixture.Runtime.CodeCommit[..12]}-{index:x12}")
            .ToArray();
        var unboundedHandler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("matching-refs", StringComparison.Ordinal)
                ? JsonResponse(new JsonArray(tags.Select(tag => new JsonObject
                {
                    ["ref"] = "refs/tags/" + tag,
                    ["object"] = new JsonObject { ["type"] = "commit", ["sha"] = new string('1', 40) },
                }).ToArray()))
                : throw new InvalidOperationException("Unbounded discovery must not fetch a release."));
        using var unbounded = Provider(fixture, unboundedHandler, () => fixture.Runtime);
        Assert.False((await unbounded.GetAsync(CancellationToken.None)).Verified);
        Assert.Equal(1, unboundedHandler.Requests);
    }

    private Fixture Package(
        Action<JsonObject>? mutateCatalog = null,
        string candidateEndpoint = "https://candidate-models.example",
        string graderEndpoint = "https://grader.example",
        bool useUncheckedEndpointIdentity = false)
    {
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var code = new string('a', 40);
        var artifactSet = new string('b', 64);
        var indexIds = new[] { new string('c', 64), new string('d', 64) };
        const string FirstQuestion = "What did Article 6 say on 1 January 2021?";
        const string SecondQuestion = "Which laws changed in 2024?";
        var catalog = JsonNode.Parse("""
            {
              "schema":"lex-assistant-eval/3",
              "frozen_at":"2026-08-15T10:00:00Z",
              "authored_by":"Test catalog author",
              "author_id":"entra:test-author",
              "pricing":{
                "schema":"lex-assistant-eval-pricing/1",
                "currency":"EUR",
                "source_uri":"https://prices.azure.com/api/retail/prices",
                "retrieved_at":"2026-08-15T09:00:00Z",
                "valid_until":"2026-08-16T09:00:00Z",
                "candidate":{
                  "model_name":"gpt-5-mini",
                  "model_version":"2025-08-07",
                  "sku":"GlobalStandard",
                  "input":{"meter_id":"candidate-input","meter_name":"Candidate input","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":70},
                  "output":{"meter_id":"candidate-output","meter_name":"Candidate output","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":50}
                },
                "grader":{
                  "model_name":"gpt-5-nano",
                  "model_version":"2025-08-07",
                  "sku":"GlobalStandard",
                  "input":{"meter_id":"grader-input","meter_name":"Grader input","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":80},
                  "output":{"meter_id":"grader-output","meter_name":"Grader output","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":30}
                }
              },
              "budget":{
                "maximum_candidate_input_tokens":3200,
                "maximum_candidate_output_tokens":320,
                "maximum_grader_input_tokens":12288,
                "maximum_grader_output_tokens":150,
                "maximum_cost_eur":10,
                "maximum_first_operation_p95_latency_ms":1000,
                "maximum_first_operation_hard_latency_ms":2000,
                "maximum_synthesis_p95_latency_ms":1000,
                "maximum_transport_queue_residual_p95_latency_ms":100,
                "maximum_total_p99_latency_ms":3000
              },
              "cases":[
                {
                  "id":"one",
                  "question":"What did Article 6 say on 1 January 2021?",
                  "repetitions":2,
                  "maximum_input_tokens":1000,
                  "maximum_output_tokens":100,
                  "maximum_latency_ms":1000,
                  "expected_synthesis":true,
                  "history":[
                    {
                      "role":"user",
                      "content":"First setup turn.",
                      "maximum_input_tokens":100,
                      "maximum_output_tokens":10,
                      "expected_synthesis":false,
                      "expected":{"tool":"search","legal_outcome":"succeeded","transport_outcome":"completed","effect":"provision","arguments":{"query":"setup"}}
                    }
                  ],
                  "expected":{"tool":"search","legal_outcome":"succeeded","transport_outcome":"completed","effect":"provision","arguments":{"query":"article 6"}},
                  "grading":{"mode":"llm","maximum_input_tokens":4096,"maximum_output_tokens":50,"rubric":"Judge whether the answer addresses the requested provision."}
                },
                {
                  "id":"two",
                  "question":"Which laws changed in 2024?",
                  "repetitions":1,
                  "maximum_input_tokens":1000,
                  "maximum_output_tokens":100,
                  "maximum_latency_ms":1000,
                  "expected_synthesis":false,
                  "expected":{"tool":"changes_in_period","legal_outcome":"succeeded","transport_outcome":"completed","effect":"history","arguments":{"from_date":"2024-01-01","to_date":"2024-12-31"}},
                  "grading":{"mode":"llm","maximum_input_tokens":4096,"maximum_output_tokens":50,"rubric":"Judge whether the answer covers the requested period."}
                }
              ]
            }
            """)!.AsObject();
        mutateCatalog?.Invoke(catalog);
        var catalogBytes = Bytes(catalog);
        var catalogSha = Sha(catalogBytes);
        var candidateHost = new Uri(candidateEndpoint).IdnHost;
        var targetEvidenceSha = TargetEvidenceSha(
            "/subscriptions/00000000-0000-0000-0000-000000000006/resourceGroups/rg/providers/Microsoft.App/containerApps/ca-lex-web",
            "ca-lex-web--candidate", "candidate.example",
            "registry.example/lex@sha256:" + new string('f', 64),
            1m, 1_073_741_824, 1, 1, 0, code, artifactSet,
            candidateHost, "gpt-5-mini");
        const string CandidateResource =
            "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/candidate";
        const string GraderResource =
            "/subscriptions/00000000-0000-0000-0000-000000000002/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/grader";
        var candidateModelEvidenceSha = useUncheckedEndpointIdentity
            ? UncheckedModelEvidenceSha(CandidateResource, candidateEndpoint,
                "gpt-5-mini", "GlobalStandard", "OpenAI", "gpt-5-mini", "2025-08-07")
            : ModelEvidenceSha(CandidateResource, candidateEndpoint,
                "gpt-5-mini", "GlobalStandard", "OpenAI", "gpt-5-mini", "2025-08-07");
        var graderModelEvidenceSha = useUncheckedEndpointIdentity
            ? UncheckedModelEvidenceSha(GraderResource, graderEndpoint,
                "lex-assistant-eval-grader", "GlobalStandard", "OpenAI", "gpt-5-nano",
                "2025-08-07")
            : ModelEvidenceSha(GraderResource, graderEndpoint,
                "lex-assistant-eval-grader", "GlobalStandard", "OpenAI", "gpt-5-nano",
                "2025-08-07");
        var runtime = new AssistantEvaluationRuntimeIdentity(
            code, "ca-lex-web--candidate", "candidate.example",
            "registry.example/lex@sha256:" + new string('f', 64),
            artifactSet, catalogSha, candidateHost, "gpt-5-mini",
            indexIds);
        var reviewKey = ECDsa.Create();
        var reviewPem = reviewKey.ExportECPrivateKeyPem();
        var admissionRoot = ArtifactManifests.TrustRoot("review-key", reviewPem);
        var admissionAuthority = new Lex.Evaluation.EvaluationAdmissionAuthority(
            "entra:test-owner", admissionRoot.KeyId,
            admissionRoot.FingerprintSha256, admissionRoot.PublicKeyPem);
        var admission = new Lex.Evaluation.EvaluationAdmissionCapability(
            Lex.Evaluation.EvaluationAdmissionContract.Schema,
            admissionAuthority.KeyId,
            admissionAuthority.ReviewerId,
            runtime.Revision,
            runtime.Image,
            runtime.CodeCommit,
            runtime.ArtifactManifestSet,
            catalogSha,
            targetEvidenceSha,
            candidateModelEvidenceSha,
            graderModelEvidenceSha,
            DateTimeOffset.Parse("2026-08-15T11:25:00Z"),
            DateTimeOffset.Parse("2026-08-15T11:45:00Z"),
            Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray())
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            1,
            1_000,
            200,
            0.10m,
            [new Lex.Evaluation.EvaluationAdmissionRequest(
                "eval-admission-case-1", new string('1', 64),
                "eval-admission-case-1", 1, 1_000, 200, 0.10m)]);
        var admissionBytes = Lex.Evaluation.EvaluationAdmissionContract.Serialize(admission);
        var admissionSha = Sha(admissionBytes);
        var admissionRunIdentity =
            Lex.Evaluation.EvaluationAdmissionContract.RunIdentity(admission);
        var firstPromptSha = Sha(Encoding.UTF8.GetBytes(
            "user\nFirst setup turn.\n---\nuser\n" + FirstQuestion));
        var secondPromptSha = Sha(Encoding.UTF8.GetBytes("user\n" + SecondQuestion));
        var report = JsonNode.Parse($$$"""
            {
              "schema":"lex-assistant-eval-report/3",
              "cases_sha256":"{{{catalogSha}}}",
              "frozen_at":"2026-08-15T10:00:00Z",
              "run_at":"2026-08-15T11:30:00Z",
              "admission_run_identity":"{{{admissionRunIdentity}}}",
              "admission_sha256":"{{{admissionSha}}}",
              "identity":{
                "target":{
                  "resource_id":"/subscriptions/00000000-0000-0000-0000-000000000006/resourceGroups/rg/providers/Microsoft.App/containerApps/ca-lex-web",
                  "revision_name":"{{{runtime.Revision}}}",
                  "revision_fqdn":"{{{runtime.RevisionHostname}}}",
                  "image":"{{{runtime.Image}}}",
                  "cpu_cores":1,
                  "memory_limit_bytes":1073741824,
                  "minimum_replicas":1,
                  "maximum_replicas":1,
                  "traffic_weight":0,
                  "code_commit":"{{{code}}}",
                  "artifact_manifest_set":"{{{artifactSet}}}",
                  "candidate_model_host":"{{{candidateHost}}}",
                  "candidate_deployment":"gpt-5-mini",
                  "evidence_sha256":"{{{targetEvidenceSha}}}"
                },
                "index_manifest_ids":["{{{indexIds[0]}}}","{{{indexIds[1]}}}"],
                "candidate_model":{
                  "resource_id":"/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/candidate",
                  "endpoint":"{{{candidateEndpoint}}}",
                  "deployment":"gpt-5-mini",
                  "sku":"GlobalStandard",
                  "model_format":"OpenAI",
                  "model_name":"gpt-5-mini",
                  "model_version":"2025-08-07",
                  "evidence_sha256":"{{{candidateModelEvidenceSha}}}"
                },
                "grader_model":{
                  "resource_id":"/subscriptions/00000000-0000-0000-0000-000000000002/resourceGroups/rg/providers/Microsoft.CognitiveServices/accounts/grader",
                  "endpoint":"{{{graderEndpoint}}}",
                  "deployment":"lex-assistant-eval-grader",
                  "sku":"GlobalStandard",
                  "model_format":"OpenAI",
                  "model_name":"gpt-5-nano",
                  "model_version":"2025-08-07",
                  "evidence_sha256":"{{{graderModelEvidenceSha}}}"
                }
              },
              "preflight":{
                "reserved_candidate_input_tokens":3200,
                "reserved_candidate_output_tokens":320,
                "reserved_grader_input_tokens":12288,
                "reserved_grader_output_tokens":150,
                "estimated_candidate_cost_eur":0.24,
                "estimated_grader_cost_eur":0.98754,
                "estimated_total_cost_eur":1.22754
              },
              "pricing":{
                "schema":"lex-assistant-eval-pricing/1",
                "currency":"EUR",
                "source_uri":"https://prices.azure.com/api/retail/prices",
                "retrieved_at":"2026-08-15T09:00:00Z",
                "valid_until":"2026-08-16T09:00:00Z",
                "candidate":{
                  "model_name":"gpt-5-mini",
                  "model_version":"2025-08-07",
                  "sku":"GlobalStandard",
                  "input":{"meter_id":"candidate-input","meter_name":"Candidate input","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":70},
                  "output":{"meter_id":"candidate-output","meter_name":"Candidate output","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":50}
                },
                "grader":{
                  "model_name":"gpt-5-nano",
                  "model_version":"2025-08-07",
                  "sku":"GlobalStandard",
                  "input":{"meter_id":"grader-input","meter_name":"Grader input","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":80},
                  "output":{"meter_id":"grader-output","meter_name":"Grader output","effective_start_date":"2026-08-01T00:00:00Z","euros_per_million":30}
                },
                "candidate_input_euros_per_million":70,
                "candidate_output_euros_per_million":50,
                "grader_input_euros_per_million":80,
                "grader_output_euros_per_million":30
              },
              "actual_candidate_usage":{"input_tokens":1000,"output_tokens":100,"total_tokens":1100},
              "actual_grader_usage":{"input_tokens":600,"output_tokens":60,"total_tokens":660},
              "actual_candidate_cost_eur":0.075,
              "actual_grader_cost_eur":0.0498,
              "actual_total_cost_eur":0.1248,
              "latency":{
                "planner":{"p50_milliseconds":100,"p95_milliseconds":130,"p99_milliseconds":130},
                "mcp":{"p50_milliseconds":40,"p95_milliseconds":50,"p99_milliseconds":50},
                "transport_queue_residual":{"p50_milliseconds":10,"p95_milliseconds":10,"p99_milliseconds":10},
                "submit_to_first_operation_result":{"p50_milliseconds":180,"p95_milliseconds":210,"p99_milliseconds":210},
                "synthesis":{"p50_milliseconds":60,"p95_milliseconds":70,"p99_milliseconds":70},
                "total":{"p50_milliseconds":360,"p95_milliseconds":420,"p99_milliseconds":420}
              },
              "results":[
                {"case_id":"one","repetition":1,"prompt_sha256":"{{{firstPromptSha}}}","grading_mode":"llm","passed":true,"failures":[],"relevance":{"score":5,"unavailable_cause":null},"candidate_usage":{"input_tokens":300,"output_tokens":30,"total_tokens":330},"grader_usage":{"input_tokens":200,"output_tokens":20,"total_tokens":220},"timings":{"planner_milliseconds":100,"mcp_milliseconds":40,"transport_queue_residual_milliseconds":10,"submit_to_first_operation_result_milliseconds":180,"synthesis_milliseconds":60,"total_milliseconds":360}},
                {"case_id":"one","repetition":2,"prompt_sha256":"{{{firstPromptSha}}}","grading_mode":"llm","passed":true,"failures":[],"relevance":{"score":4,"unavailable_cause":null},"candidate_usage":{"input_tokens":300,"output_tokens":30,"total_tokens":330},"grader_usage":{"input_tokens":200,"output_tokens":20,"total_tokens":220},"timings":{"planner_milliseconds":130,"mcp_milliseconds":50,"transport_queue_residual_milliseconds":10,"submit_to_first_operation_result_milliseconds":210,"synthesis_milliseconds":70,"total_milliseconds":420}},
                {"case_id":"two","repetition":1,"prompt_sha256":"{{{secondPromptSha}}}","grading_mode":"llm","passed":true,"failures":[],"relevance":{"score":5,"unavailable_cause":null},"candidate_usage":{"input_tokens":400,"output_tokens":40,"total_tokens":440},"grader_usage":{"input_tokens":200,"output_tokens":20,"total_tokens":220},"timings":{"planner_milliseconds":90,"mcp_milliseconds":35,"transport_queue_residual_milliseconds":5,"submit_to_first_operation_result_milliseconds":170,"synthesis_milliseconds":null,"total_milliseconds":350}}
              ],
              "gate_failures":[],
              "activation_gate_passed":true
            }
            """)!.AsObject();
        var reportBytes = Bytes(report);
        var reportSha = Sha(reportBytes);
        var browser = JsonNode.Parse($$$"""
            {
              "schema":"lex-assistant-browser-evidence/1",
              "run_at":"2026-08-15T11:35:00Z",
              "base_url":"https://{{{runtime.RevisionHostname}}}",
              "revision_name":"{{{runtime.Revision}}}",
              "code_commit":"{{{code}}}",
              "artifact_manifest_set":"{{{artifactSet}}}",
              "candidate_evidence_sha256":"{{{targetEvidenceSha}}}",
              "browser_name":"chromium",
              "browser_version":"140.0",
              "viewport_width":1440,
              "viewport_height":900,
              "metric":"operation_result_received_to_presented_ms",
              "samples_milliseconds":[20,21,22,23,25],
              "latency":{"p50_milliseconds":22,"p95_milliseconds":25,"p99_milliseconds":25},
              "maximum_p95_milliseconds":500,
              "passed":true
            }
            """)!.AsObject();
        var review = JsonNode.Parse($$$"""
            {
              "schema":"lex-assistant-eval-review/1",
              "key_id":"review-key",
              "cases_sha256":"{{{catalogSha}}}",
              "reviewer":"Soufien Hajji",
              "reviewer_id":"entra:test-owner",
              "reviewed_at":"2026-08-15T10:30:00Z",
              "decision":"approved",
              "attestation":"I reviewed this catalog."
            }
            """)!.AsObject();
        var reviewBytes = Bytes(review);
        var artifactKey = ECDsa.Create();
        var artifactPem = artifactKey.ExportECPrivateKeyPem();
        var artifactRoot = ArtifactManifests.TrustRoot("keyvault-lex-v2", artifactPem);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [AssistantEvaluationEvidenceVerifier.ReportFile] = reportBytes,
            [AssistantEvaluationEvidenceVerifier.CasesFile] = catalogBytes,
            [AssistantEvaluationEvidenceVerifier.ReviewFile] = reviewBytes,
            [AssistantEvaluationEvidenceVerifier.ReviewSignatureFile] = Encoding.UTF8.GetBytes(
                ArtifactManifests.SignBase64(reviewBytes, reviewPem)),
            [AssistantEvaluationEvidenceVerifier.AdmissionFile] = admissionBytes,
            [AssistantEvaluationEvidenceVerifier.AdmissionSignatureFile] = Encoding.UTF8.GetBytes(
                ArtifactManifests.SignBase64(admissionBytes, reviewPem)),
            [AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile] = Bytes(browser),
        };
        WriteManifest(files, artifactPem, code, artifactSet, runtime.Revision,
            catalogSha, targetEvidenceSha);
        var release = ReleaseFor(files, reportSha, code);
        return new(now, runtime, release, files, artifactRoot, artifactPem, reportSha,
            admissionAuthority);
    }

    private void WriteManifest(
        Dictionary<string, byte[]> files,
        string key,
        string code,
        string artifactSet,
        string revision,
        string catalogSha,
        string targetEvidenceSha)
    {
        foreach (var item in files)
            File.WriteAllBytes(Path.Combine(_dir, item.Key), item.Value);
        var report = JsonNode.Parse(
            files[AssistantEvaluationEvidenceVerifier.ReportFile])!.AsObject();
        var manifest = ArtifactManifests.Create(_dir,
            AssistantEvaluationEvidenceVerifier.SignedPayloadFiles,
            "keyvault-lex-v2", "2026-08-15T11:40:00Z", code,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["artifact_manifest_set"] = artifactSet,
                ["admission_run_identity"] =
                    report["admission_run_identity"]!.GetValue<string>(),
                ["admission_sha256"] = Sha(
                    files[AssistantEvaluationEvidenceVerifier.AdmissionFile]),
                ["browser_evidence_sha256"] = Sha(files[AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile]),
                ["candidate_evidence_sha256"] = targetEvidenceSha,
                ["candidate_revision"] = revision,
                ["cases_sha256"] = catalogSha,
                ["purpose"] = "assistant-evaluation",
                ["report_schema"] = "lex-assistant-eval-report/3",
            });
        var manifestBytes = ArtifactManifests.Serialize(manifest);
        files[AssistantEvaluationEvidenceVerifier.ManifestFile] = manifestBytes;
        files[AssistantEvaluationEvidenceVerifier.ManifestSignatureFile] = Encoding.UTF8.GetBytes(
            ArtifactManifests.SignBase64(manifestBytes, key));
    }

    private void ResignManifest(Dictionary<string, byte[]> files, string key)
    {
        var report = JsonNode.Parse(files[AssistantEvaluationEvidenceVerifier.ReportFile])!.AsObject();
        var target = report["identity"]!["target"]!;
        WriteManifest(files, key, target["code_commit"]!.GetValue<string>(),
            target["artifact_manifest_set"]!.GetValue<string>(),
            target["revision_name"]!.GetValue<string>(),
            report["cases_sha256"]!.GetValue<string>(),
            target["evidence_sha256"]!.GetValue<string>());
    }

    private static void ResignManifestPayloadOnly(
        Dictionary<string, byte[]> files,
        string key,
        string payload = AssistantEvaluationEvidenceVerifier.ReportFile)
    {
        var payloadBytes = files[payload];
        var manifest = ArtifactManifests.Parse(
            files[AssistantEvaluationEvidenceVerifier.ManifestFile]);
        var sources = manifest.Sources.ToDictionary(item => item.Key, item => item.Value,
            StringComparer.Ordinal);
        if (payload == AssistantEvaluationEvidenceVerifier.BrowserEvidenceFile)
            sources["browser_evidence_sha256"] = Sha(payloadBytes);
        manifest = manifest with
        {
            Files = manifest.Files.Select(item =>
                item.Path == payload
                    ? item with { Size = payloadBytes.LongLength, Sha256 = Sha(payloadBytes) }
                    : item).ToArray(),
            Sources = sources,
        };
        var manifestBytes = ArtifactManifests.Serialize(manifest);
        files[AssistantEvaluationEvidenceVerifier.ManifestFile] = manifestBytes;
        files[AssistantEvaluationEvidenceVerifier.ManifestSignatureFile] = Encoding.UTF8.GetBytes(
            ArtifactManifests.SignBase64(manifestBytes, key));
    }

    private static AssistantEvaluationRelease ReleaseFor(
        IReadOnlyDictionary<string, byte[]> files, string reportSha, string code)
    {
        var tag = $"assistant-eval-{code[..12]}-{reportSha[..12]}";
        var assets = files.ToDictionary(item => item.Key, item =>
            new AssistantEvaluationReleaseAsset(
                Math.Abs(item.Key.GetHashCode(StringComparison.Ordinal)) + 1L,
                item.Key, item.Value.LongLength, "sha256:" + Sha(item.Value), "uploaded",
                $"https://github.com/SFHAJJI/lex-ops/releases/download/{tag}/{item.Key}"),
            StringComparer.Ordinal);
        return new("SFHAJJI/lex-ops", tag,
            $"https://github.com/SFHAJJI/lex-ops/releases/tag/{tag}",
            Immutable: true, Draft: false, Prerelease: false, assets);
    }

    private static void MutateJson(
        Dictionary<string, byte[]> files, string name, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(files[name])!.AsObject();
        mutate(node);
        files[name] = Bytes(node);
    }

    private static void RecomputeReportUsageAndCost(JsonObject report)
    {
        long candidateInput = 0;
        long candidateOutput = 0;
        long graderInput = 0;
        long graderOutput = 0;
        foreach (var result in report["results"]!.AsArray())
        {
            var candidate = result!["candidate_usage"]!;
            var grader = result["grader_usage"]!;
            var rowCandidateInput = (long)candidate["input_tokens"]!.GetValue<int>();
            var rowCandidateOutput = (long)candidate["output_tokens"]!.GetValue<int>();
            var rowGraderInput = (long)grader["input_tokens"]!.GetValue<int>();
            var rowGraderOutput = (long)grader["output_tokens"]!.GetValue<int>();
            candidate["total_tokens"] = rowCandidateInput + rowCandidateOutput;
            grader["total_tokens"] = rowGraderInput + rowGraderOutput;
            candidateInput += rowCandidateInput;
            candidateOutput += rowCandidateOutput;
            graderInput += rowGraderInput;
            graderOutput += rowGraderOutput;
        }
        report["actual_candidate_usage"]!["input_tokens"] = candidateInput;
        report["actual_candidate_usage"]!["output_tokens"] = candidateOutput;
        report["actual_candidate_usage"]!["total_tokens"] = candidateInput + candidateOutput;
        report["actual_grader_usage"]!["input_tokens"] = graderInput;
        report["actual_grader_usage"]!["output_tokens"] = graderOutput;
        report["actual_grader_usage"]!["total_tokens"] = graderInput + graderOutput;
        var pricing = report["pricing"]!;
        var candidateCost = candidateInput
                * pricing["candidate"]!["input"]!["euros_per_million"]!.GetValue<decimal>()
                / 1_000_000m
            + candidateOutput
                * pricing["candidate"]!["output"]!["euros_per_million"]!.GetValue<decimal>()
                / 1_000_000m;
        var graderCost = graderInput
                * pricing["grader"]!["input"]!["euros_per_million"]!.GetValue<decimal>()
                / 1_000_000m
            + graderOutput
                * pricing["grader"]!["output"]!["euros_per_million"]!.GetValue<decimal>()
                / 1_000_000m;
        report["actual_candidate_cost_eur"] = candidateCost;
        report["actual_grader_cost_eur"] = graderCost;
        report["actual_total_cost_eur"] = candidateCost + graderCost;
    }

    private static string TargetEvidenceSha(
        string resourceId,
        string revision,
        string revisionFqdn,
        string image,
        decimal cpuCores,
        long memoryLimitBytes,
        int minimumReplicas,
        int maximumReplicas,
        int trafficWeight,
        string codeCommit,
        string artifactManifestSet,
        string candidateModelHost,
        string candidateDeployment)
        => AssistantEvaluationIdentityDigest.TargetSha256(
            resourceId, revision, revisionFqdn, image, cpuCores, memoryLimitBytes,
            minimumReplicas, maximumReplicas, trafficWeight, codeCommit,
            artifactManifestSet, candidateModelHost, candidateDeployment);

    private static string ModelEvidenceSha(
        string resourceId,
        string endpoint,
        string deployment,
        string sku,
        string modelFormat,
        string modelName,
        string modelVersion)
        => AssistantEvaluationIdentityDigest.ModelSha256(
            resourceId, endpoint, deployment, sku, modelFormat, modelName, modelVersion);

    private static string UncheckedModelEvidenceSha(
        string resourceId,
        string endpoint,
        string deployment,
        string sku,
        string modelFormat,
        string modelName,
        string modelVersion)
    {
        var canonical = string.Join('\n', resourceId.TrimEnd('/').ToLowerInvariant(),
            new Uri(endpoint).IdnHost.ToLowerInvariant(), deployment, sku,
            modelFormat, modelName, modelVersion);
        return Sha(Encoding.UTF8.GetBytes(canonical));
    }

    private static void RecomputeModelEvidence(JsonNode model)
    {
        model["evidence_sha256"] = ModelEvidenceSha(
            model["resource_id"]!.GetValue<string>(),
            model["endpoint"]!.GetValue<string>(),
            model["deployment"]!.GetValue<string>(),
            model["sku"]!.GetValue<string>(),
            model["model_format"]!.GetValue<string>(),
            model["model_name"]!.GetValue<string>(),
            model["model_version"]!.GetValue<string>());
    }

    private static void RecomputeTargetEvidence(JsonNode target)
    {
        target["evidence_sha256"] = TargetEvidenceSha(
            target["resource_id"]!.GetValue<string>(),
            target["revision_name"]!.GetValue<string>(),
            target["revision_fqdn"]!.GetValue<string>(),
            target["image"]!.GetValue<string>(),
            target["cpu_cores"]!.GetValue<decimal>(),
            target["memory_limit_bytes"]!.GetValue<long>(),
            target["minimum_replicas"]!.GetValue<int>(),
            target["maximum_replicas"]!.GetValue<int>(),
            target["traffic_weight"]!.GetValue<int>(),
            target["code_commit"]!.GetValue<string>(),
            target["artifact_manifest_set"]!.GetValue<string>(),
            target["candidate_model_host"]!.GetValue<string>(),
            target["candidate_deployment"]!.GetValue<string>());
    }

    private static byte[] Bytes(JsonNode value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = false });

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static GitHubAssistantEvaluationEvidenceProvider Provider(
        Fixture fixture,
        StubHandler handler,
        Func<AssistantEvaluationRuntimeIdentity?> runtime)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        return new(http, runtime, new FixedTimeProvider(fixture.Now),
            NullLogger<GitHubAssistantEvaluationEvidenceProvider>.Instance,
            [fixture.ArtifactRoot], fixture.AdmissionAuthority);
    }

    private static StubHandler GitHub(Fixture fixture) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("matching-refs", StringComparison.Ordinal))
            return JsonResponse(new JsonArray(new JsonObject
            {
                ["ref"] = "refs/tags/" + fixture.Release.Tag,
                ["object"] = new JsonObject
                {
                    ["type"] = "commit",
                    ["sha"] = new string('1', 40),
                },
            }));
        if (path.Contains("/releases/tags/", StringComparison.Ordinal))
            return JsonResponse(new JsonObject
            {
                ["tag_name"] = fixture.Release.Tag,
                ["html_url"] = fixture.Release.HtmlUrl,
                ["immutable"] = fixture.Release.Immutable,
                ["draft"] = fixture.Release.Draft,
                ["prerelease"] = fixture.Release.Prerelease,
                ["assets"] = new JsonArray(fixture.Release.Assets.Values.Select(asset =>
                    new JsonObject
                    {
                        ["id"] = asset.Id,
                        ["name"] = asset.Name,
                        ["size"] = asset.Size,
                        ["digest"] = asset.Digest,
                        ["state"] = asset.State,
                        ["browser_download_url"] = asset.BrowserDownloadUrl,
                    }).ToArray()),
            });
        var assetName = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        if (!fixture.Files.TryGetValue(assetName, out var bytes))
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
            RequestMessage = request,
        };
    });

    private static HttpResponseMessage JsonResponse(JsonNode node) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        AssistantEvaluationRuntimeIdentity Runtime,
        AssistantEvaluationRelease Release,
        Dictionary<string, byte[]> Files,
        ArtifactTrustRoot ArtifactRoot,
        string ArtifactKey,
        string ReportSha256,
        Lex.Evaluation.EvaluationAdmissionAuthority AdmissionAuthority);

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
