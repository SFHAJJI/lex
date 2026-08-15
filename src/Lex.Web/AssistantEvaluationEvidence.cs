using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lex.Index;

namespace Lex.Web;

internal sealed record AssistantEvaluationRuntimeIdentity(
    string CodeCommit,
    string Revision,
    string RevisionHostname,
    string Image,
    string ArtifactManifestSet,
    string CatalogSha256,
    string CandidateModelHost,
    string CandidateDeployment,
    IReadOnlyList<string> IndexManifestIds);

internal sealed record AssistantEvaluationReleaseAsset(
    long Id,
    string Name,
    long Size,
    string Digest,
    string State,
    string BrowserDownloadUrl);

internal sealed record AssistantEvaluationRelease(
    string Repository,
    string Tag,
    string HtmlUrl,
    bool Immutable,
    bool Draft,
    bool Prerelease,
    IReadOnlyDictionary<string, AssistantEvaluationReleaseAsset> Assets);

internal sealed record VerifiedAssistantEvaluationEvidence(
    string Repository,
    string ReleaseTag,
    string ReleaseUrl,
    string ReportUrl,
    string ManifestUrl,
    string SignatureUrl,
    string RunAt,
    string CodeCommit,
    string Revision,
    string RevisionHostname,
    string Image,
    string ArtifactManifestSet,
    string CatalogSha256,
    string ReportSha256,
    string CandidateEvidenceSha256,
    string CandidateModelHost,
    string CandidateDeployment,
    string CandidateModelName,
    string CandidateModelVersion,
    string GraderDeployment,
    string GraderModelName,
    string GraderModelVersion,
    IReadOnlyList<string> IndexManifestIds,
    int CaseCount,
    int RepetitionCount,
    long CandidateInputTokens,
    long CandidateOutputTokens,
    long GraderInputTokens,
    long GraderOutputTokens,
    decimal TotalCostEur,
    decimal MaximumCostEur,
    double FirstOperationP95Milliseconds,
    double TotalP99Milliseconds,
    double BrowserP95Milliseconds)
{
    internal bool Matches(AssistantEvaluationRuntimeIdentity runtime) =>
        Fixed(CodeCommit, runtime.CodeCommit)
        && string.Equals(Revision, runtime.Revision, StringComparison.Ordinal)
        && string.Equals(RevisionHostname, runtime.RevisionHostname,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(Image, runtime.Image, StringComparison.Ordinal)
        && Fixed(ArtifactManifestSet, runtime.ArtifactManifestSet)
        && Fixed(CatalogSha256, runtime.CatalogSha256)
        && string.Equals(CandidateModelHost, runtime.CandidateModelHost,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(CandidateDeployment, runtime.CandidateDeployment,
            StringComparison.Ordinal)
        && SameSet(IndexManifestIds, runtime.IndexManifestIds);

    private static bool Fixed(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(right.ToLowerInvariant()));

    private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.Order(StringComparer.Ordinal).SequenceEqual(
            right.Order(StringComparer.Ordinal), StringComparer.Ordinal);
}

/// <summary>
/// Authenticates the immutable release, every signed byte and its exact runtime bindings. The
/// canonical pre-signing verifier in Lex.Ingest owns case, grading, budget and timing semantics;
/// this public projection only exposes bounded claims from the report it authorized and signed.
/// </summary>
internal static class AssistantEvaluationEvidenceVerifier
{
    internal const string ReportFile = "assistant-eval-report.json";
    internal const string CasesFile = "assistant-cases-v3.json";
    internal const string ReviewFile = "assistant-cases-v3.review.json";
    internal const string ReviewSignatureFile = "assistant-cases-v3.review.sig";
    internal const string BrowserEvidenceFile = "assistant-browser-evidence.json";
    internal const string ManifestFile = "assistant-eval.manifest.json";
    internal const string ManifestSignatureFile = "assistant-eval.manifest.sig";
    private const string ArtifactKeyId = "keyvault-lex-v2";

    internal static readonly IReadOnlyList<string> SignedPayloadFiles =
        [ReportFile, CasesFile, ReviewFile, ReviewSignatureFile, BrowserEvidenceFile];

    private static readonly HashSet<string> StandardAssets =
        [.. SignedPayloadFiles, ManifestFile, ManifestSignatureFile];
    private static readonly HashSet<string> BootstrapAssets =
        [.. StandardAssets, "bootstrap-equivalence.json",
            "bootstrap-equivalence.manifest.json", "bootstrap-equivalence.manifest.sig"];
    private static readonly Regex Digest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Commit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex Revision = new(
        "^ca-lex-web--[a-z0-9-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new(
        "^assistant-eval-([0-9a-f]{12})-([0-9a-f]{12})$", RegexOptions.CultureInvariant);

    internal static VerifiedAssistantEvaluationEvidence Verify(
        AssistantEvaluationRelease release,
        IReadOnlyDictionary<string, byte[]> files,
        IReadOnlyList<ArtifactTrustRoot> artifactRoots,
        DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(files);
        if (verifiedAt.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Evaluation verification time must be UTC.");

        var tag = Tag.Match(release.Tag);
        if (!tag.Success || release.Repository != "SFHAJJI/lex-ops"
            || release.Draft || release.Prerelease || !release.Immutable
            || !ExactGitHubUrl(release.HtmlUrl,
                $"/SFHAJJI/lex-ops/releases/tag/{release.Tag}"))
            throw new InvalidDataException(
                "Assistant evaluation release identity is not immutable and exact.");

        VerifyAssets(release, files);

        var manifestBytes = files[ManifestFile];
        var manifest = ArtifactManifests.Parse(manifestBytes);
        ArtifactManifests.VerifySignature(manifestBytes,
            Encoding.UTF8.GetString(files[ManifestSignatureFile]), manifest.KeyId, artifactRoots);
        if (manifest.KeyId != ArtifactKeyId
            || manifest.Files.Count != SignedPayloadFiles.Count
            || manifest.Files.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count()
                != SignedPayloadFiles.Count
            || !manifest.Files.Select(item => item.Path).ToHashSet(StringComparer.Ordinal)
                .SetEquals(SignedPayloadFiles))
            throw new InvalidDataException("Signed assistant evaluation manifest is not closed.");
        foreach (var file in manifest.Files)
            if (file.Size != files[file.Path].LongLength || file.Sha256 != Sha(files[file.Path]))
                throw new CryptographicException(
                    $"Signed assistant evaluation file '{file.Path}' failed its digest check.");

        var reportSha = Sha(files[ReportFile]);
        using var reportDocument = Parse(files[ReportFile], ReportFile);
        var report = reportDocument.RootElement;
        var target = RequiredObject(RequiredObject(report, "identity"), "target");
        var codeCommit = RequiredString(target, "code_commit");
        if (!Commit.IsMatch(codeCommit) || manifest.CodeCommit != codeCommit
            || tag.Groups[1].Value != codeCommit[..12]
            || tag.Groups[2].Value != reportSha[..12])
            throw new InvalidDataException(
                "Assistant evaluation tag does not bind the full report and code.");

        using var catalogDocument = Parse(files[CasesFile], CasesFile);
        var catalog = catalogDocument.RootElement;
        var catalogSha = Sha(files[CasesFile]);
        if (RequiredString(catalog, "schema") != "lex-assistant-eval/3")
            throw new InvalidDataException("Assistant evaluation catalog schema is invalid.");
        var frozenAtText = RequiredString(catalog, "frozen_at");
        var frozenAt = Utc(frozenAtText, "catalog frozen_at");
        var maximumCost = RequiredDecimal(catalog, "budget", "maximum_cost_eur");
        if (maximumCost is <= 0 or > 10)
            throw new InvalidDataException("Assistant evaluation maximum cost is outside display bounds.");
        var cases = RequiredArray(catalog, "cases").EnumerateArray().Select(item =>
        {
            var id = BoundedString(item, 100, "id");
            var repetitions = RequiredInt(item, "repetitions");
            if (repetitions is < 1 or > 10)
                throw new InvalidDataException(
                    "Assistant evaluation repetition count is outside display bounds.");
            return (Id: id, Repetitions: repetitions);
        }).ToArray();
        var repetitionCount = cases.Sum(item => item.Repetitions);
        if (cases.Length is < 1 or > 100 || repetitionCount > 500
            || cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidDataException(
                "Assistant evaluation catalog summary is outside display bounds.");

        var runAtText = RequiredString(report, "run_at");
        var runAt = Utc(runAtText, "run_at");
        if (RequiredString(report, "schema") != "lex-assistant-eval-report/3"
            || !Fixed(RequiredString(report, "cases_sha256"), catalogSha)
            || RequiredString(report, "frozen_at") != frozenAtText
            || runAt < frozenAt || runAt > verifiedAt.AddMinutes(5)
            || !RequiredBoolean(report, "activation_gate_passed")
            || RequiredArray(report, "gate_failures").GetArrayLength() != 0)
            throw new InvalidDataException(
                "Signed assistant evaluation report does not claim a passing verdict.");

        var candidateUsage = RequiredObject(report, "actual_candidate_usage");
        var graderUsage = RequiredObject(report, "actual_grader_usage");
        var candidateInput = NonnegativeLong(candidateUsage, "input_tokens");
        var candidateOutput = NonnegativeLong(candidateUsage, "output_tokens");
        var graderInput = NonnegativeLong(graderUsage, "input_tokens");
        var graderOutput = NonnegativeLong(graderUsage, "output_tokens");
        var candidateCost = NonnegativeDecimal(report, "actual_candidate_cost_eur");
        var graderCost = NonnegativeDecimal(report, "actual_grader_cost_eur");
        var totalCost = NonnegativeDecimal(report, "actual_total_cost_eur");
        if (candidateCost + graderCost != totalCost || totalCost > maximumCost)
            throw new InvalidDataException("Signed assistant evaluation cost claim is inconsistent.");
        var latency = RequiredObject(report, "latency");
        var firstP95 = NonnegativeDouble(
            RequiredObject(latency, "submit_to_first_operation_result"), "p95_milliseconds");
        var totalP99 = NonnegativeDouble(RequiredObject(latency, "total"), "p99_milliseconds");

        var revision = RequiredString(target, "revision_name");
        var revisionHostname = RequiredString(target, "revision_fqdn");
        var image = BoundedString(target, 500, "image");
        var artifactSet = RequiredString(target, "artifact_manifest_set");
        var candidateHost = RequiredString(target, "candidate_model_host");
        var candidateDeployment = BoundedString(target, 200, "candidate_deployment");
        var candidateEvidenceSha = RequiredString(target, "evidence_sha256");
        var identity = RequiredObject(report, "identity");
        var indexIds = RequiredArray(identity, "index_manifest_ids").EnumerateArray()
            .Select(StringValue).ToArray();
        if (!Revision.IsMatch(revision)
            || Uri.CheckHostName(revisionHostname) != UriHostNameType.Dns
            || !Digest.IsMatch(artifactSet) || !Digest.IsMatch(candidateEvidenceSha)
            || indexIds.Length == 0 || indexIds.Any(item => !Digest.IsMatch(item))
            || indexIds.Distinct(StringComparer.Ordinal).Count() != indexIds.Length
            || Uri.CheckHostName(candidateHost) != UriHostNameType.Dns)
            throw new InvalidDataException("Assistant evaluation target identity is malformed.");

        var candidateModel = RequiredObject(identity, "candidate_model");
        var graderModel = RequiredObject(identity, "grader_model");
        var candidateEndpoint = HttpsHost(RequiredString(candidateModel, "endpoint"));
        var candidateModelName = BoundedString(candidateModel, 200, "model_name");
        var candidateModelVersion = BoundedString(candidateModel, 200, "model_version");
        _ = HttpsHost(RequiredString(graderModel, "endpoint"));
        var graderDeployment = BoundedString(graderModel, 200, "deployment");
        var graderModelName = BoundedString(graderModel, 200, "model_name");
        var graderModelVersion = BoundedString(graderModel, 200, "model_version");
        if (!string.Equals(candidateEndpoint, candidateHost, StringComparison.OrdinalIgnoreCase)
            || RequiredString(candidateModel, "deployment") != candidateDeployment)
            throw new InvalidDataException(
                "Assistant evaluation candidate model route is inconsistent.");

        using var browserDocument = Parse(files[BrowserEvidenceFile], BrowserEvidenceFile);
        var browser = browserDocument.RootElement;
        var browserP95 = NonnegativeDouble(
            RequiredObject(browser, "latency"), "p95_milliseconds");
        if (RequiredString(browser, "schema") != "lex-assistant-browser-evidence/1"
            || !RequiredBoolean(browser, "passed")
            || RequiredString(browser, "revision_name") != revision
            || RequiredString(browser, "base_url") != $"https://{revisionHostname}"
            || RequiredString(browser, "code_commit") != codeCommit
            || !Fixed(RequiredString(browser, "artifact_manifest_set"), artifactSet)
            || !Fixed(RequiredString(browser, "candidate_evidence_sha256"), candidateEvidenceSha)
            || RequiredString(browser, "browser_name") != "chromium"
            || RequiredInt(browser, "viewport_width") != 1440
            || RequiredInt(browser, "viewport_height") != 900
            || RequiredString(browser, "metric")
                != "operation_result_received_to_presented_ms"
            || NonnegativeDouble(browser, "maximum_p95_milliseconds") != 500
            || browserP95 > 500)
            throw new InvalidDataException(
                "Signed assistant browser evidence claim is invalid or mismatched.");

        var expectedSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["artifact_manifest_set"] = artifactSet,
            ["browser_evidence_sha256"] = Sha(files[BrowserEvidenceFile]),
            ["candidate_evidence_sha256"] = candidateEvidenceSha,
            ["candidate_revision"] = revision,
            ["cases_sha256"] = catalogSha,
            ["purpose"] = "assistant-evaluation",
            ["report_schema"] = "lex-assistant-eval-report/3",
        };
        if (manifest.Sources.Count != expectedSources.Count
            || expectedSources.Any(item => !manifest.Sources.TryGetValue(item.Key, out var value)
                || value != item.Value))
            throw new InvalidDataException(
                "Signed assistant evaluation manifest identity is invalid.");

        return new(release.Repository, release.Tag, release.HtmlUrl,
            release.Assets[ReportFile].BrowserDownloadUrl,
            release.Assets[ManifestFile].BrowserDownloadUrl,
            release.Assets[ManifestSignatureFile].BrowserDownloadUrl,
            runAtText, codeCommit, revision, revisionHostname, image, artifactSet, catalogSha,
            reportSha, candidateEvidenceSha, candidateHost, candidateDeployment,
            candidateModelName, candidateModelVersion, graderDeployment, graderModelName,
            graderModelVersion, indexIds, cases.Length, repetitionCount, candidateInput,
            candidateOutput, graderInput, graderOutput, totalCost, maximumCost, firstP95,
            totalP99, browserP95);
    }

    private static void VerifyAssets(
        AssistantEvaluationRelease release,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var assetNames = release.Assets.Keys.ToHashSet(StringComparer.Ordinal);
        if (!assetNames.SetEquals(StandardAssets) && !assetNames.SetEquals(BootstrapAssets))
            throw new InvalidDataException("Assistant evaluation release asset set is not closed.");
        if (release.Assets.Values.Select(asset => asset.Id).Distinct().Count()
            != release.Assets.Count)
            throw new InvalidDataException("Assistant evaluation release asset ids are ambiguous.");
        if (files.Count != StandardAssets.Count || !files.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(StandardAssets))
            throw new InvalidDataException("Downloaded assistant evaluation package is incomplete.");

        long totalBytes = 0;
        foreach (var name in StandardAssets)
        {
            var bytes = files[name];
            var asset = release.Assets[name];
            var limit = name switch
            {
                ReportFile or CasesFile => 4L * 1024 * 1024,
                BrowserEvidenceFile or ReviewFile or ManifestFile => 256L * 1024,
                ReviewSignatureFile or ManifestSignatureFile => 16L * 1024,
                _ => 0,
            };
            totalBytes = checked(totalBytes + bytes.LongLength);
            if (asset.Id <= 0 || asset.Name != name || asset.State != "uploaded"
                || bytes.LongLength is 0 || bytes.LongLength > limit
                || asset.Size != bytes.LongLength
                || asset.Digest != "sha256:" + Sha(bytes)
                || !ExactGitHubUrl(asset.BrowserDownloadUrl,
                    $"/SFHAJJI/lex-ops/releases/download/{release.Tag}/{name}"))
                throw new InvalidDataException(
                    $"Assistant evaluation asset '{name}' is invalid.");
        }
        if (totalBytes > 10L * 1024 * 1024)
            throw new InvalidDataException(
                "Assistant evaluation package exceeds its total byte limit.");
    }

    private static bool ExactGitHubUrl(string value, string path) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IdnHost == "github.com"
        && uri.AbsolutePath == path && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    private static JsonDocument Parse(byte[] bytes, string name)
    {
        try { return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 }); }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Assistant evaluation file '{name}' is malformed.", exception);
        }
    }

    private static JsonElement RequiredObject(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"Assistant evaluation field '{string.Join('.', path)}' is not an object.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                $"Assistant evaluation field '{string.Join('.', path)}' is not an array.");
        return value;
    }

    private static string RequiredString(JsonElement root, params string[] path) =>
        StringValue(Path(root, path));

    private static string BoundedString(JsonElement root, int maximum, params string[] path)
    {
        var value = RequiredString(root, path);
        if (value.Length > maximum)
            throw new InvalidDataException("Assistant evaluation display field exceeds its bound.");
        return value;
    }

    private static string StringValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text : throw new InvalidDataException("Assistant evaluation string field is missing.");

    private static bool RequiredBoolean(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException("Assistant evaluation boolean field is missing."),
        };
    }

    private static int RequiredInt(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : throw new InvalidDataException(
                "Assistant evaluation integer field is missing.");
    }

    private static decimal RequiredDecimal(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number : throw new InvalidDataException(
                "Assistant evaluation decimal field is missing.");
    }

    private static long NonnegativeLong(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number)
            || number < 0)
            throw new InvalidDataException("Assistant evaluation token claim is invalid.");
        return number;
    }

    private static decimal NonnegativeDecimal(JsonElement root, params string[] path)
    {
        var value = RequiredDecimal(root, path);
        if (value < 0)
            throw new InvalidDataException("Assistant evaluation cost claim is invalid.");
        return value;
    }

    private static double NonnegativeDouble(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || number < 0 || double.IsNaN(number) || double.IsInfinity(number))
            throw new InvalidDataException("Assistant evaluation measurement claim is invalid.");
        return number;
    }

    private static JsonElement Path(JsonElement root, params string[] path)
    {
        var value = root;
        foreach (var part in path)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
                throw new InvalidDataException(
                    $"Assistant evaluation field '{string.Join('.', path)}' is missing.");
        return value;
    }

    private static DateTimeOffset Utc(string value, string name)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero)
            throw new InvalidDataException($"Assistant evaluation {name} is not UTC.");
        return parsed;
    }

    private static string HttpsHost(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Assistant evaluation model endpoint is invalid.");
        return uri.IdnHost;
    }

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool Fixed(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}
