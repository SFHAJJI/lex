using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Ingest;

namespace Lex.Tests;

public sealed class BootstrapEquivalenceTests : IDisposable
{
    private const string AppId =
        "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg-lex-prod/"
        + "providers/Microsoft.App/containerApps/ca-lex-web";
    private const string Candidate = "ca-lex-web--candidate-abc";
    private const string Rollback = "ca-lex-web--fallback-abc";
    private const string Legacy = "ca-lex-web--legacy-abc";
    private const string Release = "assistant-eval-aaaaaaaaaaaa-bbbbbbbbbbbb";
    private const string ImageDigest =
        "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string TemplateDigest =
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string CasesSha =
        "abababababababababababababababababababababababababababababababab";
    private const string CodeCommit = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string GeneratedAt = "2026-08-14T10:02:00Z";
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"lex-bootstrap-equivalence-{Guid.NewGuid():N}");

    public BootstrapEquivalenceTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Signed_equivalence_binds_the_exact_live_candidate_and_fallback()
    {
        var bundle = CreateBundle(Evidence());

        var verified = Verify(bundle);

        Assert.Equal(Candidate, verified.Candidate.RevisionName);
        Assert.Equal(Rollback, verified.Rollback.RevisionName);
        Assert.Equal(["revisionSuffix"], verified.ExcludedTemplateFields);
    }

    [Fact]
    public void Tampering_with_equivalence_evidence_after_signing_fails_closed()
    {
        var bundle = CreateBundle(Evidence());
        var bytes = File.ReadAllBytes(bundle.EvidencePath);
        bytes[10] ^= 1;
        File.WriteAllBytes(bundle.EvidencePath, bytes);

        Assert.ThrowsAny<CryptographicException>(() => Verify(bundle));
    }

    [Fact]
    public void A_different_live_revision_identity_is_rejected()
    {
        var bundle = CreateBundle(Evidence()) with
        {
            CandidateLive = CandidateLive() with
            {
                RevisionResourceId = AppId + "/revisions/ca-lex-web--other",
            },
        };

        Assert.Throws<InvalidDataException>(() => Verify(bundle));
    }

    [Fact]
    public void Signing_state_requires_exactly_a_active_100_r_inactive_0_and_c_active_0()
    {
        var activeRollback = CreateBundle(Evidence()) with
        {
            RollbackLive = RollbackLive() with { Active = true },
        };
        Assert.Throws<InvalidDataException>(() => Verify(activeRollback));

        var extraActive = CreateBundle(Evidence()) with
        {
            Routes = Routes().Append(new BootstrapRevisionRouteEvidence(
                "ca-lex-web--unexpected", AppId + "/revisions/ca-lex-web--unexpected",
                "2026-08-14T09:30:00Z", true, 0)).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => Verify(extraActive));
    }

    [Fact]
    public void Template_or_full_ACR_image_drift_is_rejected()
    {
        var templateBundle = CreateBundle(Evidence()) with
        {
            CandidateLive = CandidateLive() with
            {
                CanonicalTemplateDigest = "sha256:" + new string('1', 64),
            },
        };
        Assert.Throws<InvalidDataException>(() => Verify(templateBundle));

        var imageBundle = CreateBundle(Evidence()) with
        {
            RollbackLive = RollbackLive() with
            {
                Image = "other.example/lex-web@" + ImageDigest,
            },
        };
        Assert.Throws<InvalidDataException>(() => Verify(imageBundle));
    }

    [Fact]
    public void A_signed_artifact_for_a_different_evaluation_release_is_rejected()
    {
        var other = Evidence() with
        {
            EvaluationRelease = "assistant-eval-111111111111-222222222222",
        };
        var bundle = CreateBundle(other);

        Assert.Throws<InvalidDataException>(() => Verify(bundle));
    }

    [Fact]
    public void Cases_digest_and_exact_a_r_c_timestamps_are_cross_checked()
    {
        var wrongCases = CreateBundle(Evidence() with
        {
            CasesSha256 = new string('1', 64),
        });
        Assert.Throws<InvalidDataException>(() => Verify(wrongCases));

        var staleRouteSnapshot = CreateBundle(Evidence()) with
        {
            Routes = Routes().Select(item => item.RevisionName == Candidate
                ? item with { CreatedTime = "2026-08-14T10:01:30Z" }
                : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => Verify(staleRouteSnapshot));

        var chronologyEvidence = Evidence() with
        {
            LegacyAuthority = Evidence().LegacyAuthority with
            {
                CreatedTime = "2026-08-14T10:00:30Z",
            },
        };
        var badChronology = CreateBundle(chronologyEvidence) with
        {
            LegacyLive = LegacyLive() with { CreatedTime = "2026-08-14T10:00:30Z" },
        };
        Assert.Throws<InvalidDataException>(() => Verify(badChronology));
    }

    [Fact]
    public void Signed_bootstrap_promotion_window_expires_after_two_hours()
    {
        var bundle = CreateBundle(Evidence());

        Assert.Throws<InvalidDataException>(() => BootstrapEquivalenceVerifier.Verify(
            _dir, bundle.ManifestPath, bundle.SignaturePath, bundle.EvidencePath,
            [bundle.Root], AppId, Legacy, Candidate, Rollback, Release, TemplateDigest,
            ImageDigest, CasesSha, bundle.CandidateLive, bundle.RollbackLive, bundle.LegacyLive,
            bundle.Routes, DateTimeOffset.Parse("2026-08-14T12:02:01Z")));
    }

    [Fact]
    public void Established_fallback_uses_the_signed_history_but_requires_exact_live_c_and_r()
    {
        var bundle = CreateBundle(Evidence()) with
        {
            CandidateLive = CandidateLive() with { TrafficWeight = 100 },
            RollbackLive = RollbackLive() with { Active = true },
            Routes =
            [
                Routes().Single(item => item.RevisionName == Candidate) with
                {
                    TrafficWeight = 100,
                },
                Routes().Single(item => item.RevisionName == Rollback) with
                {
                    Active = true,
                },
            ],
        };

        var verified = BootstrapEquivalenceVerifier.VerifyEstablishedFallback(
            _dir, bundle.ManifestPath, bundle.SignaturePath, bundle.EvidencePath,
            [bundle.Root], AppId, Legacy, Candidate, Rollback, Release, TemplateDigest,
            ImageDigest, CasesSha, bundle.CandidateLive, bundle.RollbackLive, bundle.Routes,
            DateTimeOffset.Parse("2026-09-14T11:00:00Z"));

        Assert.Equal(Rollback, verified.Rollback.RevisionName);
        var unexpected = bundle with
        {
            Routes = bundle.Routes.Append(new BootstrapRevisionRouteEvidence(
                Legacy, AppId + "/revisions/" + Legacy,
                "2026-08-14T09:00:00Z", false, 0)).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() =>
            BootstrapEquivalenceVerifier.VerifyEstablishedFallback(
                _dir, unexpected.ManifestPath, unexpected.SignaturePath,
                unexpected.EvidencePath, [unexpected.Root], AppId, Legacy, Candidate,
                Rollback, Release, TemplateDigest, ImageDigest, CasesSha,
                unexpected.CandidateLive, unexpected.RollbackLive, unexpected.Routes,
                DateTimeOffset.Parse("2026-09-14T11:00:00Z")));
    }

    [Fact]
    public void Historical_source_package_keeps_exact_signed_identity_without_live_legacy_state()
    {
        var bundle = CreateBundle(Evidence());

        var verified = BootstrapEquivalenceVerifier.VerifyHistoricalPackage(
            _dir, bundle.ManifestPath, bundle.SignaturePath, bundle.EvidencePath,
            [bundle.Root], AppId, Legacy, Candidate, Rollback, Release, TemplateDigest,
            ImageDigest, CasesSha, CodeCommit,
            DateTimeOffset.Parse("2026-09-14T11:00:00Z"));

        Assert.Equal(Candidate, verified.Candidate.RevisionName);
        Assert.Equal(Rollback, verified.Rollback.RevisionName);
    }

    [Fact]
    public void Historical_source_package_rejects_wrong_commit_or_claimed_role_identity()
    {
        var bundle = CreateBundle(Evidence());
        Assert.Throws<InvalidDataException>(() =>
            BootstrapEquivalenceVerifier.VerifyHistoricalPackage(
                _dir, bundle.ManifestPath, bundle.SignaturePath, bundle.EvidencePath,
                [bundle.Root], AppId, Legacy, Candidate, Rollback, Release, TemplateDigest,
                ImageDigest, CasesSha, new string('1', 40),
                DateTimeOffset.Parse("2026-09-14T11:00:00Z")));

        var wrongRole = CreateBundle(Evidence() with
        {
            Rollback = Evidence().Rollback with { Active = true },
        });
        Assert.Throws<InvalidDataException>(() =>
            BootstrapEquivalenceVerifier.VerifyHistoricalPackage(
                _dir, wrongRole.ManifestPath, wrongRole.SignaturePath, wrongRole.EvidencePath,
                [wrongRole.Root], AppId, Legacy, Candidate, Rollback, Release, TemplateDigest,
                ImageDigest, CasesSha, CodeCommit,
                DateTimeOffset.Parse("2026-09-14T11:00:00Z")));
    }

    [Fact]
    public void A_signature_from_an_untrusted_replacement_key_is_rejected()
    {
        var bundle = CreateBundle(Evidence());
        var attacker = StampSigner.CreateKeyPem();
        File.WriteAllText(bundle.SignaturePath,
            ArtifactManifests.SignBase64(File.ReadAllBytes(bundle.ManifestPath), attacker));

        Assert.ThrowsAny<CryptographicException>(() => Verify(bundle));
    }

    [Fact]
    public void Canonical_template_digest_matches_the_shared_Python_contract()
    {
        var template = JsonNode.Parse("""
            {
              "revisionSuffix":"fallback",
              "containers":[{
                "name":"lex-web",
                "image":"crsoufien3orem.azurecr.io/lex-web@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "resources":{"cpu":1.0,"memory":"2Gi"},
                "env":[{"name":"LEX_CODE_COMMIT","value":"abc"}],
                "metadata":{"revisionSuffix":"nested"}
              }],
              "scale":{"minReplicas":1,"maxReplicas":1}
            }
            """)!;

        var digest = CanonicalDigest(template);
        ((JsonObject)template)["revisionSuffix"] = "candidate";

        Assert.Equal(
            "sha256:103d74222e33da6daadeac6d421b127ff8e0a1bb1f7ff86f27ba426a3a55fbbc",
            digest);
        Assert.Equal(digest, CanonicalDigest(template));
        var container = (JsonObject)((JsonArray)template["containers"]!)[0]!;
        ((JsonObject)container["metadata"]!)["revisionSuffix"] = "changed";
        Assert.NotEqual(digest, CanonicalDigest(template));
    }

    [Fact]
    public void Azure_parser_accepts_inactive_fallback_but_requires_strict_live_types()
    {
        var fallback = RevisionBody(Rollback, active: false, traffic: JsonValue.Create(0));

        var parsed = ParseAzureRevision(Rollback, fallback);

        Assert.False(parsed.Active);
        Assert.Equal(0, parsed.TrafficWeight);
        Assert.Equal(CodeCommit, parsed.CodeCommit);
        Assert.Equal(CanonicalDigest(fallback), parsed.CanonicalTemplateDigest);

        fallback["properties"]!["active"] = "false";
        Assert.Throws<TargetInvocationException>(() => ParseAzureRevision(Rollback, fallback));
    }

    [Fact]
    public void Azure_complete_route_parser_rejects_loose_types_and_duplicate_inventory()
    {
        var response = new JsonObject
        {
            ["value"] = new JsonArray
            {
                RouteBody(Legacy, "2026-08-14T09:00:00Z", true, JsonValue.Create(100)),
                RouteBody(Rollback, "2026-08-14T10:00:00Z", false, JsonValue.Create(0)),
                RouteBody(Candidate, "2026-08-14T10:01:00Z", true, JsonValue.Create(0)),
            },
        };

        Assert.Equal(3, ParseAzureRoutes(response).Count);

        response["nextLink"] = "https://management.azure.com/next-page";
        Assert.Throws<TargetInvocationException>(() => ParseAzureRoutes(response));
        response.Remove("nextLink");

        response["value"]![0]!["properties"]!["trafficWeight"] = "100";
        Assert.Throws<TargetInvocationException>(() => ParseAzureRoutes(response));
    }

    private Bundle CreateBundle(BootstrapEquivalenceEvidence evidence)
    {
        var evidencePath = Path.Combine(_dir, BootstrapEquivalenceVerifier.EvidenceFile);
        var manifestPath = Path.Combine(_dir, BootstrapEquivalenceVerifier.ManifestFile);
        var signaturePath = Path.Combine(_dir, BootstrapEquivalenceVerifier.SignatureFile);
        File.WriteAllBytes(evidencePath, JsonSerializer.SerializeToUtf8Bytes(evidence,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            }));
        var key = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot(
            AssistantEvaluationReleaseVerifier.ArtifactKeyId, key);
        var evidenceSha = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(evidencePath)));
        var manifest = ArtifactManifests.Create(
            _dir,
            [BootstrapEquivalenceVerifier.EvidenceFile],
            AssistantEvaluationReleaseVerifier.ArtifactKeyId,
            "2026-08-14T10:03:00Z",
            CodeCommit,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidate_revision"] = Candidate,
                ["canonical_template_digest"] = TemplateDigest,
                ["cases_sha256"] = evidence.CasesSha256,
                ["equivalence_sha256"] = evidenceSha,
                ["evaluation_release"] = evidence.EvaluationRelease,
                ["image_digest"] = ImageDigest,
                ["legacy_authority_revision"] = Legacy,
                ["preparation_state"] = "legacy-a-active-c-active-r-inactive",
                ["purpose"] = "assistant-evaluation-bootstrap-equivalence",
                ["rollback_revision"] = Rollback,
                ["schema"] = BootstrapEquivalenceVerifier.Schema,
            });
        var manifestBytes = ArtifactManifests.Serialize(manifest);
        File.WriteAllBytes(manifestPath, manifestBytes);
        File.WriteAllText(signaturePath, ArtifactManifests.SignBase64(manifestBytes, key));
        return new Bundle(evidencePath, manifestPath, signaturePath, root,
            CandidateLive(), RollbackLive(), LegacyLive(), Routes());
    }

    private BootstrapEquivalenceEvidence Verify(Bundle bundle) =>
        BootstrapEquivalenceVerifier.Verify(
            _dir,
            bundle.ManifestPath,
            bundle.SignaturePath,
            bundle.EvidencePath,
            [bundle.Root],
            AppId,
            Legacy,
            Candidate,
            Rollback,
            Release,
            TemplateDigest,
            ImageDigest,
            CasesSha,
            bundle.CandidateLive,
            bundle.RollbackLive,
            bundle.LegacyLive,
            bundle.Routes,
            DateTimeOffset.Parse("2026-08-14T11:00:00Z"));

    private static BootstrapEquivalenceEvidence Evidence() => new(
        BootstrapEquivalenceVerifier.Schema,
        GeneratedAt,
        Release,
        AppId,
        ImageDigest,
        TemplateDigest,
        CasesSha,
        ["revisionSuffix"],
        "legacy-a-active-c-active-r-inactive",
        new BootstrapEquivalenceRevision(
            Legacy, AppId + "/revisions/" + Legacy, "2026-08-14T09:00:00Z", true, 100),
        new BootstrapEquivalenceRevision(
            Rollback, AppId + "/revisions/" + Rollback, "2026-08-14T10:00:00Z", false, 0),
        new BootstrapEquivalenceRevision(
            Candidate, AppId + "/revisions/" + Candidate, "2026-08-14T10:01:00Z", true, 0));

    private static BootstrapRevisionLiveEvidence CandidateLive() => new(
        AppId,
        Candidate,
        AppId + "/revisions/" + Candidate,
        "2026-08-14T10:01:00Z",
        "crsoufien3orem.azurecr.io/lex-web@" + ImageDigest,
        TemplateDigest,
        CodeCommit,
        true,
        0);

    private static BootstrapRevisionLiveEvidence RollbackLive() => new(
        AppId,
        Rollback,
        AppId + "/revisions/" + Rollback,
        "2026-08-14T10:00:00Z",
        "crsoufien3orem.azurecr.io/lex-web@" + ImageDigest,
        TemplateDigest,
        CodeCommit,
        false,
        0);

    private static BootstrapRevisionLiveEvidence LegacyLive() => new(
        AppId,
        Legacy,
        AppId + "/revisions/" + Legacy,
        "2026-08-14T09:00:00Z",
        "crsoufien3orem.azurecr.io/lex-web@" + "sha256:" + new string('f', 64),
        "sha256:" + new string('1', 64),
        CodeCommit,
        true,
        100);

    private static IReadOnlyList<BootstrapRevisionRouteEvidence> Routes() =>
    [
        new(Legacy, AppId + "/revisions/" + Legacy,
            "2026-08-14T09:00:00Z", true, 100),
        new(Rollback, AppId + "/revisions/" + Rollback,
            "2026-08-14T10:00:00Z", false, 0),
        new(Candidate, AppId + "/revisions/" + Candidate,
            "2026-08-14T10:01:00Z", true, 0),
    ];

    private static JsonObject RevisionBody(
        string revision,
        bool active,
        JsonNode? traffic) => new()
        {
            ["id"] = AppId + "/revisions/" + revision,
            ["name"] = revision,
            ["properties"] = new JsonObject
            {
                ["active"] = active,
                ["trafficWeight"] = traffic,
                ["runningState"] = active ? "Running" : "Deactivated",
                ["createdTime"] = revision == Rollback
                ? "2026-08-14T10:00:00Z" : "2026-08-14T10:01:00Z",
                ["template"] = new JsonObject
                {
                    ["revisionSuffix"] = revision.Split("--", 2)[1],
                    ["containers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "lex-web",
                        ["image"] = "crsoufien3orem.azurecr.io/lex-web@" + ImageDigest,
                        ["resources"] = new JsonObject
                        {
                            ["cpu"] = 1.0m,
                            ["memory"] = "2Gi",
                        },
                        ["env"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["name"] = "LEX_CODE_COMMIT",
                                ["value"] = CodeCommit,
                            },
                        },
                    },
                },
                    ["scale"] = new JsonObject
                    {
                        ["minReplicas"] = 1,
                        ["maxReplicas"] = 1,
                    },
                },
            },
        };

    private static JsonObject RouteBody(
        string revision,
        string createdTime,
        bool active,
        JsonNode? traffic) => new()
        {
            ["id"] = AppId + "/revisions/" + revision,
            ["name"] = revision,
            ["properties"] = new JsonObject
            {
                ["active"] = active,
                ["trafficWeight"] = traffic,
                ["createdTime"] = createdTime,
            },
        };

    private static string CanonicalDigest(JsonNode value) =>
        (string)(typeof(AzureModelDeploymentResolver)
            .GetMethod("CanonicalContainerAppTemplateDigest",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Canonical template digest is absent."))
        .Invoke(null, [value])!;

    private static BootstrapRevisionLiveEvidence ParseAzureRevision(
        string revision,
        JsonObject value) =>
        (BootstrapRevisionLiveEvidence)(typeof(AzureModelDeploymentResolver)
            .GetMethod("ParseContainerAppBootstrapRevision",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Bootstrap revision parser is absent."))
        .Invoke(null, [AppId, revision, value])!;

    private static IReadOnlyList<BootstrapRevisionRouteEvidence> ParseAzureRoutes(
        JsonObject value) =>
        (IReadOnlyList<BootstrapRevisionRouteEvidence>)(typeof(AzureModelDeploymentResolver)
            .GetMethod("ParseContainerAppBootstrapRoutes",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Bootstrap route parser is absent."))
        .Invoke(null, [AppId, value])!;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed record Bundle(
        string EvidencePath,
        string ManifestPath,
        string SignaturePath,
        ArtifactTrustRoot Root,
        BootstrapRevisionLiveEvidence CandidateLive,
        BootstrapRevisionLiveEvidence RollbackLive,
        BootstrapRevisionLiveEvidence LegacyLive,
        IReadOnlyList<BootstrapRevisionRouteEvidence> Routes);
}
