using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Equal(0.125m, evidence.TotalCostEur);
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

    private Fixture Package()
    {
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var code = new string('a', 40);
        var artifactSet = new string('b', 64);
        var indexIds = new[] { new string('c', 64), new string('d', 64) };
        var catalog = JsonNode.Parse("""
            {
              "schema":"lex-assistant-eval/3",
              "frozen_at":"2026-08-15T10:00:00Z",
              "budget":{"maximum_cost_eur":10},
              "cases":[
                {"id":"one","question":"What did Article 6 say on 1 January 2021?","repetitions":2},
                {"id":"two","question":"Which laws changed in 2024?","repetitions":1}
              ]
            }
            """)!.AsObject();
        var catalogBytes = Bytes(catalog);
        var catalogSha = Sha(catalogBytes);
        var targetEvidenceSha = new string('e', 64);
        var runtime = new AssistantEvaluationRuntimeIdentity(
            code, "ca-lex-web--candidate", "candidate.example",
            "registry.example/lex@sha256:" + new string('f', 64),
            artifactSet, catalogSha, "candidate-models.example", "gpt-5-mini",
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
                  "revision_name":"{{{runtime.Revision}}}",
                  "revision_fqdn":"{{{runtime.RevisionHostname}}}",
                  "image":"{{{runtime.Image}}}",
                  "code_commit":"{{{code}}}",
                  "artifact_manifest_set":"{{{artifactSet}}}",
                  "candidate_model_host":"candidate-models.example",
                  "candidate_deployment":"gpt-5-mini",
                  "evidence_sha256":"{{{targetEvidenceSha}}}"
                },
                "index_manifest_ids":["{{{indexIds[0]}}}","{{{indexIds[1]}}}"],
                "candidate_model":{
                  "endpoint":"https://candidate-models.example",
                  "deployment":"gpt-5-mini",
                  "model_name":"gpt-5-mini",
                  "model_version":"2025-08-07",
                  "sku":"GlobalStandard"
                },
                "grader_model":{
                  "endpoint":"https://grader.example",
                  "deployment":"lex-assistant-eval-grader",
                  "model_name":"gpt-5-nano",
                  "model_version":"2025-08-07",
                  "sku":"GlobalStandard"
                }
              },
              "actual_candidate_usage":{"input_tokens":1000,"output_tokens":100},
              "actual_grader_usage":{"input_tokens":600,"output_tokens":60},
              "actual_candidate_cost_eur":0.075,
              "actual_grader_cost_eur":0.05,
              "actual_total_cost_eur":0.125,
              "latency":{
                "planner":{"p50_milliseconds":100,"p95_milliseconds":130,"p99_milliseconds":130},
                "mcp":{"p50_milliseconds":40,"p95_milliseconds":50,"p99_milliseconds":50},
                "transport_queue_residual":{"p50_milliseconds":10,"p95_milliseconds":10,"p99_milliseconds":10},
                "submit_to_first_operation_result":{"p50_milliseconds":180,"p95_milliseconds":210,"p99_milliseconds":210},
                "synthesis":{"p50_milliseconds":0,"p95_milliseconds":0,"p99_milliseconds":0},
                "total":{"p50_milliseconds":360,"p95_milliseconds":420,"p99_milliseconds":420}
              },
              "results":[
                {"case_id":"one","repetition":1,"passed":true,"relevance":{"score":5},"failures":[],"candidate_usage":{"input_tokens":300,"output_tokens":30},"grader_usage":{"input_tokens":200,"output_tokens":20},"timings":{"submit_to_first_operation_result_milliseconds":180,"total_milliseconds":360}},
                {"case_id":"one","repetition":2,"passed":true,"relevance":{"score":4},"failures":[],"candidate_usage":{"input_tokens":300,"output_tokens":30},"grader_usage":{"input_tokens":200,"output_tokens":20},"timings":{"submit_to_first_operation_result_milliseconds":210,"total_milliseconds":420}},
                {"case_id":"two","repetition":1,"passed":true,"relevance":{"score":5},"failures":[],"candidate_usage":{"input_tokens":400,"output_tokens":40},"grader_usage":{"input_tokens":200,"output_tokens":20},"timings":{"submit_to_first_operation_result_milliseconds":170,"total_milliseconds":350}}
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
