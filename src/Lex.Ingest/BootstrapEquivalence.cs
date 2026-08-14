using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lex.Index;

namespace Lex.Ingest;

public static class BootstrapEquivalenceVerifier
{
    public const string Schema = "lex-first-release-equivalence/1";
    public const string EvidenceFile = "bootstrap-equivalence.json";
    public const string ManifestFile = "bootstrap-equivalence.manifest.json";
    public const string SignatureFile = "bootstrap-equivalence.manifest.sig";
    private const string Purpose = "assistant-evaluation-bootstrap-equivalence";
    private const string PreparationState = "legacy-a-active-c-active-r-inactive";
    private static readonly Regex Digest = new(
        "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex HexDigest = new(
        "^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex EvaluationRelease = new(
        "^assistant-eval-[0-9a-f]{12}-[0-9a-f]{12}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Revision = new(
        "^ca-lex-web--[a-z0-9-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex CodeCommit = new(
        "^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex ContainerAppResourceId = new(
        "^/subscriptions/[0-9a-f-]{36}/resourceGroups/[^/]{1,90}/providers/"
        + "Microsoft\\.App/containerApps/ca-lex-web$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static BootstrapEquivalenceEvidence Verify(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        string evidencePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        BootstrapRevisionLiveEvidence candidate,
        BootstrapRevisionLiveEvidence rollback,
        BootstrapRevisionLiveEvidence legacyAuthority,
        IReadOnlyList<BootstrapRevisionRouteEvidence> revisionRoutes,
        DateTimeOffset verifiedAt) => VerifyCore(
            rootDirectory, manifestPath, signaturePath, evidencePath, trustRoots,
            expectedContainerAppResourceId, expectedLegacyAuthorityRevision,
            expectedCandidateRevision, expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            candidate, rollback, legacyAuthority, revisionRoutes, verifiedAt,
            establishedFallback: false);

    public static BootstrapEquivalenceEvidence VerifyEstablishedFallback(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        string evidencePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        BootstrapRevisionLiveEvidence candidate,
        BootstrapRevisionLiveEvidence rollback,
        IReadOnlyList<BootstrapRevisionRouteEvidence> revisionRoutes,
        DateTimeOffset verifiedAt) => VerifyCore(
            rootDirectory, manifestPath, signaturePath, evidencePath, trustRoots,
            expectedContainerAppResourceId, expectedLegacyAuthorityRevision,
            expectedCandidateRevision, expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            candidate, rollback, null, revisionRoutes, verifiedAt,
            establishedFallback: true);

    public static BootstrapEquivalenceEvidence VerifyHistoricalPackage(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        string evidencePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        string expectedCodeCommit,
        DateTimeOffset verifiedAt)
    {
        if (!CodeCommit.IsMatch(expectedCodeCommit))
            throw new InvalidDataException("Bootstrap equivalence code commit is invalid.");
        var package = LoadSignedEvidence(
            rootDirectory, manifestPath, signaturePath, evidencePath, trustRoots,
            expectedContainerAppResourceId, expectedLegacyAuthorityRevision,
            expectedCandidateRevision, expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            verifiedAt, establishedFallback: true);
        ValidateSignedPackage(
            package, manifestPath, expectedContainerAppResourceId,
            expectedLegacyAuthorityRevision, expectedCandidateRevision,
            expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            expectedCodeCommit, verifiedAt);
        return package.Evidence;
    }

    public static void ValidateInvocation(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        string evidencePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        DateTimeOffset verifiedAt,
        bool establishedFallback) =>
        _ = LoadSignedEvidence(
            rootDirectory, manifestPath, signaturePath, evidencePath, trustRoots,
            expectedContainerAppResourceId, expectedLegacyAuthorityRevision,
            expectedCandidateRevision, expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            verifiedAt, establishedFallback);

    private static BootstrapEquivalenceEvidence VerifyCore(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        string evidencePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        BootstrapRevisionLiveEvidence candidate,
        BootstrapRevisionLiveEvidence rollback,
        BootstrapRevisionLiveEvidence? legacyAuthority,
        IReadOnlyList<BootstrapRevisionRouteEvidence> revisionRoutes,
        DateTimeOffset verifiedAt,
        bool establishedFallback)
    {
        var package = LoadSignedEvidence(
            rootDirectory, manifestPath, signaturePath, evidencePath, trustRoots,
            expectedContainerAppResourceId, expectedLegacyAuthorityRevision,
            expectedCandidateRevision, expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            verifiedAt, establishedFallback);
        var evidence = package.Evidence;
        if (rollback.CodeCommit != candidate.CodeCommit)
            throw new InvalidDataException(
                "Bootstrap candidate and rollback code commits differ.");
        ValidateSignedPackage(
            package, manifestPath, expectedContainerAppResourceId,
            expectedLegacyAuthorityRevision, expectedCandidateRevision,
            expectedRollbackRevision, expectedEvaluationRelease,
            expectedCanonicalTemplateDigest, expectedImageDigest, expectedCasesSha256,
            candidate.CodeCommit, verifiedAt);

        if (establishedFallback)
        {
            ValidateEstablishedLive("candidate", evidence.Candidate, candidate,
                expectedContainerAppResourceId, expectedCandidateRevision,
                expectedImageDigest, expectedCanonicalTemplateDigest,
                claimedActive: true, liveTraffic: 100);
            ValidateEstablishedLive("rollback", evidence.Rollback, rollback,
                expectedContainerAppResourceId, expectedRollbackRevision,
                expectedImageDigest, expectedCanonicalTemplateDigest,
                claimedActive: false, liveTraffic: 0);
            ValidateHistoricalLegacyClaim(evidence.LegacyAuthority,
                expectedContainerAppResourceId, expectedLegacyAuthorityRevision);
            ValidateEstablishedRoutes(revisionRoutes, expectedContainerAppResourceId,
                expectedCandidateRevision, expectedRollbackRevision);
        }
        else
        {
            ValidateLive("candidate", evidence.Candidate, candidate,
                expectedContainerAppResourceId, expectedCandidateRevision,
                expectedImageDigest, expectedCanonicalTemplateDigest,
                expectedActive: true);
            ValidateLive("rollback", evidence.Rollback, rollback,
                expectedContainerAppResourceId, expectedRollbackRevision,
                expectedImageDigest, expectedCanonicalTemplateDigest,
                expectedActive: false);
            ValidateLegacyAuthority(evidence.LegacyAuthority,
                legacyAuthority ?? throw new InvalidDataException(
                    "Bootstrap legacy authority live evidence is absent."),
                expectedContainerAppResourceId, expectedLegacyAuthorityRevision);
            ValidatePreparatoryRoutes(revisionRoutes, expectedContainerAppResourceId,
                expectedLegacyAuthorityRevision, expectedRollbackRevision,
                expectedCandidateRevision);
        }
        var routesByRevision = revisionRoutes.ToDictionary(
            item => item.RevisionName, StringComparer.Ordinal);
        RequireMatchingRouteSnapshot(candidate, routesByRevision[expectedCandidateRevision]);
        RequireMatchingRouteSnapshot(rollback, routesByRevision[expectedRollbackRevision]);
        if (!establishedFallback)
            RequireMatchingRouteSnapshot(legacyAuthority!,
                routesByRevision[expectedLegacyAuthorityRevision]);
        return evidence;
    }

    private static void ValidateSignedPackage(
        SignedEvidence package,
        string manifestPath,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        string expectedCodeCommit,
        DateTimeOffset verifiedAt)
    {
        if (!CodeCommit.IsMatch(expectedCodeCommit))
            throw new InvalidDataException("Bootstrap equivalence code commit is invalid.");
        var evidence = package.Evidence;
        ValidateHistoricalRevisionClaim(
            "legacy authority", evidence.LegacyAuthority, expectedContainerAppResourceId,
            expectedLegacyAuthorityRevision, expectedActive: true, expectedTraffic: 100);
        ValidateHistoricalRevisionClaim(
            "rollback", evidence.Rollback, expectedContainerAppResourceId,
            expectedRollbackRevision, expectedActive: false, expectedTraffic: 0);
        ValidateHistoricalRevisionClaim(
            "candidate", evidence.Candidate, expectedContainerAppResourceId,
            expectedCandidateRevision, expectedActive: true, expectedTraffic: 0);
        var legacyCreated = Utc(evidence.LegacyAuthority.CreatedTime,
            "legacy_authority.created_time");
        var rollbackCreated = Utc(evidence.Rollback.CreatedTime, "rollback.created_time");
        var candidateCreated = Utc(evidence.Candidate.CreatedTime, "candidate.created_time");
        if (legacyCreated >= rollbackCreated || rollbackCreated >= candidateCreated)
            throw new InvalidDataException(
                "Bootstrap chronology must be strictly legacy A before fallback R before candidate C.");

        var manifest = ArtifactManifests.Parse(File.ReadAllBytes(manifestPath));
        var evidenceSha = Convert.ToHexStringLower(SHA256.HashData(package.Bytes));
        var expectedSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidate_revision"] = expectedCandidateRevision,
            ["canonical_template_digest"] = expectedCanonicalTemplateDigest,
            ["cases_sha256"] = expectedCasesSha256,
            ["equivalence_sha256"] = evidenceSha,
            ["evaluation_release"] = expectedEvaluationRelease,
            ["image_digest"] = expectedImageDigest,
            ["legacy_authority_revision"] = expectedLegacyAuthorityRevision,
            ["preparation_state"] = PreparationState,
            ["purpose"] = Purpose,
            ["rollback_revision"] = expectedRollbackRevision,
            ["schema"] = Schema,
        };
        if (manifest.KeyId != AssistantEvaluationReleaseVerifier.ArtifactKeyId
            || manifest.CodeCommit != expectedCodeCommit
            || manifest.Sources.Count != expectedSources.Count
            || expectedSources.Any(item => !manifest.Sources.TryGetValue(item.Key, out var value)
                || value != item.Value))
            throw new InvalidDataException(
                "Signed bootstrap equivalence manifest does not bind its release and live template.");
        var manifestCreated = Utc(manifest.CreatedAt, "manifest.created_at");
        if (manifestCreated < package.GeneratedAt.AddMinutes(-5)
            || manifestCreated > verifiedAt.AddMinutes(5))
            throw new InvalidDataException("Bootstrap equivalence manifest time is invalid.");
    }

    private static SignedEvidence LoadSignedEvidence(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        string evidencePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedCandidateRevision,
        string expectedRollbackRevision,
        string expectedEvaluationRelease,
        string expectedCanonicalTemplateDigest,
        string expectedImageDigest,
        string expectedCasesSha256,
        DateTimeOffset verifiedAt,
        bool establishedFallback)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)
            || !ContainerAppResourceId.IsMatch(expectedContainerAppResourceId)
            || !Revision.IsMatch(expectedLegacyAuthorityRevision)
            || !Revision.IsMatch(expectedCandidateRevision)
            || !Revision.IsMatch(expectedRollbackRevision)
            || expectedCandidateRevision == expectedRollbackRevision
            || expectedLegacyAuthorityRevision == expectedCandidateRevision
            || expectedLegacyAuthorityRevision == expectedRollbackRevision
            || !EvaluationRelease.IsMatch(expectedEvaluationRelease)
            || !Digest.IsMatch(expectedCanonicalTemplateDigest)
            || !Digest.IsMatch(expectedImageDigest)
            || !HexDigest.IsMatch(expectedCasesSha256))
            throw new InvalidDataException("Bootstrap equivalence expectations are invalid.");

        var root = Path.GetFullPath(rootDirectory);
        RequireExactArtifactPath(root, evidencePath, EvidenceFile);
        RequireExactArtifactPath(root, manifestPath, ManifestFile);
        RequireExactArtifactPath(root, signaturePath, SignatureFile);

        var verified = ArtifactManifests.VerifyDirectory(
            root, manifestPath, signaturePath, trustRoots);
        if (verified.Count != 1 || !verified.Contains(EvidenceFile))
            throw new InvalidDataException(
                "Signed bootstrap equivalence artifact set must contain exactly its evidence file.");
        var bytes = File.ReadAllBytes(evidencePath);
        if (bytes.Length is 0 or > 64 * 1024)
            throw new InvalidDataException("Bootstrap equivalence evidence exceeds its byte limit.");
        BootstrapEquivalenceEvidence evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<BootstrapEquivalenceEvidence>(bytes, Json)
                ?? throw new InvalidDataException("Bootstrap equivalence evidence is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Bootstrap equivalence evidence is malformed.", exception);
        }

        var generatedAt = Utc(evidence.GeneratedAt, "generated_at");
        if (evidence.Schema != Schema
            || generatedAt > verifiedAt.AddMinutes(5)
            || (!establishedFallback && verifiedAt - generatedAt > TimeSpan.FromHours(2))
            || !string.Equals(evidence.ContainerAppResourceId?.TrimEnd('/'),
                expectedContainerAppResourceId.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase)
            || evidence.EvaluationRelease != expectedEvaluationRelease
            || evidence.ImageDigest != expectedImageDigest
            || evidence.CanonicalTemplateDigest != expectedCanonicalTemplateDigest
            || evidence.CasesSha256 != expectedCasesSha256
            || evidence.ExcludedTemplateFields is null
            || !evidence.ExcludedTemplateFields.SequenceEqual(["revisionSuffix"],
                StringComparer.Ordinal)
            || evidence.PreparationState != PreparationState
            || evidence.Candidate is null || evidence.Rollback is null
            || evidence.LegacyAuthority is null)
            throw new InvalidDataException(
                "Bootstrap equivalence evidence does not bind the expected release identity.");
        return new SignedEvidence(evidence, bytes, generatedAt);
    }

    private sealed record SignedEvidence(
        BootstrapEquivalenceEvidence Evidence,
        byte[] Bytes,
        DateTimeOffset GeneratedAt);

    private static void RequireMatchingRouteSnapshot(
        BootstrapRevisionLiveEvidence revision,
        BootstrapRevisionRouteEvidence route)
    {
        if (revision.RevisionName != route.RevisionName
            || !string.Equals(revision.RevisionResourceId?.TrimEnd('/'),
                route.RevisionResourceId?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || Utc(revision.CreatedTime, $"live {revision.RevisionName}.created_time")
                != Utc(route.CreatedTime, $"route {route.RevisionName}.created_time")
            || revision.Active != route.Active
            || revision.TrafficWeight != route.TrafficWeight)
            throw new InvalidDataException(
                "Bootstrap per-revision and complete-route Azure snapshots disagree.");
    }

    private static void ValidateLive(
        string role,
        BootstrapEquivalenceRevision claimed,
        BootstrapRevisionLiveEvidence live,
        string expectedContainerAppResourceId,
        string expectedRevision,
        string expectedImageDigest,
        string expectedCanonicalTemplateDigest,
        bool expectedActive)
    {
        var expectedRevisionId = expectedContainerAppResourceId.TrimEnd('/')
            + "/revisions/" + expectedRevision;
        var expectedImage = "crsoufien3orem.azurecr.io/lex-web@" + expectedImageDigest;
        if (claimed is null || live is null
            || claimed.RevisionName != expectedRevision
            || !string.Equals(claimed.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(live.ContainerAppResourceId?.TrimEnd('/'),
                expectedContainerAppResourceId.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || live.RevisionName != expectedRevision
            || !string.Equals(live.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || Utc(claimed.CreatedTime, $"{role}.created_time")
                != Utc(live.CreatedTime, $"live {role}.created_time")
            || live.Image != expectedImage
            || live.CanonicalTemplateDigest != expectedCanonicalTemplateDigest
            || claimed.Active != expectedActive
            || claimed.TrafficWeight != 0
            || live.Active != expectedActive
            || live.TrafficWeight != 0
            || string.IsNullOrWhiteSpace(live.CodeCommit))
            throw new InvalidDataException(
                $"Bootstrap {role} evidence differs from its exact live Azure revision.");
    }

    private static void ValidateEstablishedLive(
        string role,
        BootstrapEquivalenceRevision claimed,
        BootstrapRevisionLiveEvidence live,
        string expectedContainerAppResourceId,
        string expectedRevision,
        string expectedImageDigest,
        string expectedCanonicalTemplateDigest,
        bool claimedActive,
        int liveTraffic)
    {
        var expectedRevisionId = expectedContainerAppResourceId.TrimEnd('/')
            + "/revisions/" + expectedRevision;
        var expectedImage = "crsoufien3orem.azurecr.io/lex-web@" + expectedImageDigest;
        if (claimed is null || live is null
            || claimed.RevisionName != expectedRevision
            || !string.Equals(claimed.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(live.ContainerAppResourceId?.TrimEnd('/'),
                expectedContainerAppResourceId.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || live.RevisionName != expectedRevision
            || !string.Equals(live.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || Utc(claimed.CreatedTime, $"{role}.created_time")
                != Utc(live.CreatedTime, $"live {role}.created_time")
            || live.Image != expectedImage
            || live.CanonicalTemplateDigest != expectedCanonicalTemplateDigest
            || claimed.Active != claimedActive
            || claimed.TrafficWeight != 0
            || live.Active != true
            || live.TrafficWeight != liveTraffic
            || string.IsNullOrWhiteSpace(live.CodeCommit))
            throw new InvalidDataException(
                $"Established bootstrap {role} differs from its signed immutable revision.");
    }

    private static void ValidateHistoricalLegacyClaim(
        BootstrapEquivalenceRevision claimed,
        string expectedContainerAppResourceId,
        string expectedRevision)
    {
        var expectedRevisionId = expectedContainerAppResourceId.TrimEnd('/')
            + "/revisions/" + expectedRevision;
        if (claimed is null
            || claimed.RevisionName != expectedRevision
            || !string.Equals(claimed.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || claimed.Active != true
            || claimed.TrafficWeight != 100)
            throw new InvalidDataException(
                "Established bootstrap evidence has an invalid historical legacy authority.");
        _ = Utc(claimed.CreatedTime, "legacy_authority.created_time");
    }

    private static void ValidateHistoricalRevisionClaim(
        string role,
        BootstrapEquivalenceRevision claimed,
        string expectedContainerAppResourceId,
        string expectedRevision,
        bool expectedActive,
        int expectedTraffic)
    {
        var expectedRevisionId = expectedContainerAppResourceId.TrimEnd('/')
            + "/revisions/" + expectedRevision;
        if (claimed is null
            || claimed.RevisionName != expectedRevision
            || !string.Equals(claimed.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || claimed.Active != expectedActive
            || claimed.TrafficWeight != expectedTraffic)
            throw new InvalidDataException(
                $"Signed bootstrap {role} identity or preparation state is invalid.");
        _ = Utc(claimed.CreatedTime, $"{role}.created_time");
    }

    private static void ValidateEstablishedRoutes(
        IReadOnlyList<BootstrapRevisionRouteEvidence> routes,
        string expectedContainerAppResourceId,
        string expectedCandidateRevision,
        string expectedRollbackRevision)
    {
        var expectedNames = new[] { expectedCandidateRevision, expectedRollbackRevision }
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (routes is null || routes.Count != 2
            || !routes.Select(item => item.RevisionName)
                .OrderBy(item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedNames, StringComparer.Ordinal))
            throw new InvalidDataException(
                "Established bootstrap fallback requires exactly C and R live revisions.");
        foreach (var route in routes)
        {
            var expectedId = expectedContainerAppResourceId.TrimEnd('/')
                + "/revisions/" + route.RevisionName;
            if (!string.Equals(route.RevisionResourceId?.TrimEnd('/'), expectedId,
                    StringComparison.OrdinalIgnoreCase)
                || route.Active != true
                || route.TrafficWeight is < 0 or > 100)
                throw new InvalidDataException(
                    "Established bootstrap fallback route inventory is malformed.");
            _ = Utc(route.CreatedTime, $"live route {route.RevisionName}.created_time");
        }
        if (routes.Single(item => item.RevisionName == expectedCandidateRevision)
                .TrafficWeight != 100
            || routes.Single(item => item.RevisionName == expectedRollbackRevision)
                .TrafficWeight != 0)
            throw new InvalidDataException(
                "Established bootstrap rollback requires C=active/100 and R=active/0.");
    }

    private static void ValidateLegacyAuthority(
        BootstrapEquivalenceRevision claimed,
        BootstrapRevisionLiveEvidence live,
        string expectedContainerAppResourceId,
        string expectedRevision)
    {
        var expectedRevisionId = expectedContainerAppResourceId.TrimEnd('/')
            + "/revisions/" + expectedRevision;
        if (claimed.RevisionName != expectedRevision
            || !string.Equals(claimed.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(live.ContainerAppResourceId?.TrimEnd('/'),
                expectedContainerAppResourceId.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || live.RevisionName != expectedRevision
            || !string.Equals(live.RevisionResourceId?.TrimEnd('/'), expectedRevisionId,
                StringComparison.OrdinalIgnoreCase)
            || Utc(claimed.CreatedTime, "legacy_authority.created_time")
                != Utc(live.CreatedTime, "live legacy_authority.created_time")
            || claimed.Active != true || claimed.TrafficWeight != 100
            || live.Active != true || live.TrafficWeight != 100)
            throw new InvalidDataException(
                "Bootstrap legacy authority evidence is not the exact active traffic bearer.");
    }

    private static void ValidatePreparatoryRoutes(
        IReadOnlyList<BootstrapRevisionRouteEvidence> routes,
        string expectedContainerAppResourceId,
        string expectedLegacyAuthorityRevision,
        string expectedRollbackRevision,
        string expectedCandidateRevision)
    {
        if (routes is null || routes.Count != 3
            || routes.Select(item => item.RevisionName).Distinct(StringComparer.Ordinal).Count()
                != routes.Count)
            throw new InvalidDataException("Complete bootstrap revision routes are absent or duplicated.");
        var expectedNames = new[]
        {
            expectedLegacyAuthorityRevision,
            expectedRollbackRevision,
            expectedCandidateRevision,
        }.OrderBy(item => item, StringComparer.Ordinal);
        if (!routes.Select(item => item.RevisionName)
                .OrderBy(item => item, StringComparer.Ordinal)
                .SequenceEqual(expectedNames, StringComparer.Ordinal))
            throw new InvalidDataException(
                "Complete bootstrap revision routes contain an unexpected identity.");
        foreach (var route in routes)
        {
            var expectedId = expectedContainerAppResourceId.TrimEnd('/')
                + "/revisions/" + route.RevisionName;
            if (!Revision.IsMatch(route.RevisionName)
                || !string.Equals(route.RevisionResourceId?.TrimEnd('/'), expectedId,
                    StringComparison.OrdinalIgnoreCase)
                || route.TrafficWeight is < 0 or > 100
                || (!route.Active && route.TrafficWeight != 0))
                throw new InvalidDataException("Complete bootstrap revision routes are malformed.");
            _ = Utc(route.CreatedTime, $"live route {route.RevisionName}.created_time");
        }

        var active = routes.Where(item => item.Active)
            .OrderBy(item => item.RevisionName, StringComparer.Ordinal)
            .ToArray();
        var expectedActive = new[]
        {
            expectedLegacyAuthorityRevision,
            expectedCandidateRevision,
        }.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!active.Select(item => item.RevisionName).SequenceEqual(expectedActive,
                StringComparer.Ordinal)
            || active.Single(item => item.RevisionName == expectedLegacyAuthorityRevision)
                .TrafficWeight != 100
            || active.Where(item => item.RevisionName != expectedLegacyAuthorityRevision)
                .Any(item => item.TrafficWeight != 0)
            || routes.Single(item => item.RevisionName == expectedRollbackRevision).Active
            || routes.Single(item => item.RevisionName == expectedRollbackRevision)
                .TrafficWeight != 0
            || routes.Where(item => !item.Active).Any(item => item.TrafficWeight != 0))
            throw new InvalidDataException(
                "Bootstrap signing requires exactly legacy A=active/100, R=inactive/0, and C=active/0.");

        var rollbackCreated = routes.Single(item => item.RevisionName == expectedRollbackRevision)
            .CreatedTime;
        var fallbackInstant = Utc(rollbackCreated, "live rollback route created_time");
        if (routes.Where(item => item.RevisionName != expectedRollbackRevision
                && item.RevisionName != expectedCandidateRevision)
            .Any(item => Utc(item.CreatedTime, $"live legacy {item.RevisionName}.created_time")
                >= fallbackInstant))
            throw new InvalidDataException(
                "Every legacy revision must predate the prepared bootstrap fallback.");
    }

    private static DateTimeOffset Utc(string value, string field)
    {
        if (value is null || !value.EndsWith('Z')
            || !DateTimeOffset.TryParse(value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero)
            throw new InvalidDataException($"Bootstrap equivalence {field} must be an explicit UTC instant.");
        return parsed;
    }

    private static void RequireExactArtifactPath(
        string rootDirectory,
        string suppliedPath,
        string expectedFile)
    {
        var expected = Path.GetFullPath(Path.Combine(rootDirectory, expectedFile));
        var actual = Path.GetFullPath(suppliedPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(expected, actual, comparison))
            throw new InvalidDataException(
                $"Bootstrap equivalence artifact must be named '{expectedFile}' inside its root.");
    }
}

public sealed record BootstrapEquivalenceEvidence(
    string Schema,
    string GeneratedAt,
    string EvaluationRelease,
    string ContainerAppResourceId,
    string ImageDigest,
    string CanonicalTemplateDigest,
    string CasesSha256,
    IReadOnlyList<string> ExcludedTemplateFields,
    string PreparationState,
    BootstrapEquivalenceRevision LegacyAuthority,
    BootstrapEquivalenceRevision Rollback,
    BootstrapEquivalenceRevision Candidate);

public sealed record BootstrapEquivalenceRevision(
    string RevisionName,
    string RevisionResourceId,
    string CreatedTime,
    bool Active,
    int TrafficWeight);

public sealed record BootstrapRevisionLiveEvidence(
    string ContainerAppResourceId,
    string RevisionName,
    string RevisionResourceId,
    string CreatedTime,
    string Image,
    string CanonicalTemplateDigest,
    string CodeCommit,
    bool Active,
    int TrafficWeight);

public sealed record BootstrapRevisionRouteEvidence(
    string RevisionName,
    string RevisionResourceId,
    string CreatedTime,
    bool Active,
    int TrafficWeight);
