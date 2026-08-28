using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lex.Evaluation;
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

/// <summary>One frozen case as the signed report measured it: how many repetitions ran, how many
/// passed the deterministic contract, and each nullable relevance score. Relevance is reported
/// and gates nothing.</summary>
internal sealed record AssistantEvaluationCaseOutcome(
    string CaseId,
    string Question,
    int Repetitions,
    int Passed,
    IReadOnlyList<int?> RelevanceScores);

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
    double BrowserP95Milliseconds,
    IReadOnlyList<AssistantEvaluationCaseOutcome> CaseOutcomes)
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
/// canonical promotion verifier in Lex.Ingest owns the live Azure gate; this public projection
/// independently recomputes the closed deterministic claims before it exposes signed evidence.
/// </summary>
internal static class AssistantEvaluationEvidenceVerifier
{
    internal const string ReportFile = "assistant-eval-report.json";
    internal const string CasesFile = "assistant-cases-v3.json";
    internal const string ReviewFile = "assistant-cases-v3.review.json";
    internal const string ReviewSignatureFile = "assistant-cases-v3.review.sig";
    internal const string AdmissionFile = "assistant-eval-admission.json";
    internal const string AdmissionSignatureFile = "assistant-eval-admission.sig";
    internal const string BrowserEvidenceFile = "assistant-browser-evidence.json";
    internal const string ManifestFile = "assistant-eval.manifest.json";
    internal const string ManifestSignatureFile = "assistant-eval.manifest.sig";
    private const string ArtifactKeyId = "keyvault-lex-v2";

    internal static readonly IReadOnlyList<string> SignedPayloadFiles =
        [ReportFile, CasesFile, ReviewFile, ReviewSignatureFile,
            AdmissionFile, AdmissionSignatureFile, BrowserEvidenceFile];

    private static readonly HashSet<string> StandardAssets =
        [.. SignedPayloadFiles, ManifestFile, ManifestSignatureFile];
    private static readonly HashSet<string> BootstrapAssets =
        [.. StandardAssets, "bootstrap-equivalence.json",
            "bootstrap-equivalence.manifest.json", "bootstrap-equivalence.manifest.sig"];
    private static readonly Regex Digest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Commit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex RunIdentity = new(
        "^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
    private static readonly Regex Revision = new(
        "^ca-lex-web--[a-z0-9-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new(
        "^assistant-eval-([0-9a-f]{12})-([0-9a-f]{12})$", RegexOptions.CultureInvariant);

    private sealed record EvaluationMeterPrice(
        string MeterId,
        string MeterName,
        string EffectiveStartDate,
        decimal EurosPerMillion);

    private sealed record EvaluationModelPricing(
        string ModelName,
        string ModelVersion,
        string Sku,
        EvaluationMeterPrice Input,
        EvaluationMeterPrice Output);

    private sealed record EvaluationPricing(
        string Schema,
        string Currency,
        string SourceUri,
        string RetrievedAt,
        string ValidUntil,
        EvaluationModelPricing Candidate,
        EvaluationModelPricing Grader);

    private sealed record EvaluationBudget(
        long MaximumCandidateInputTokens,
        long MaximumCandidateOutputTokens,
        long MaximumGraderInputTokens,
        long MaximumGraderOutputTokens,
        decimal MaximumCostEur,
        double MaximumFirstOperationP95LatencyMilliseconds,
        double MaximumFirstOperationHardLatencyMilliseconds,
        double MaximumSynthesisP95LatencyMilliseconds,
        double MaximumTransportQueueResidualP95LatencyMilliseconds,
        double MaximumTotalP99LatencyMilliseconds);

    private sealed record EvaluationMessage(
        string Role,
        string Content,
        long MaximumInputTokens,
        long MaximumOutputTokens);

    private sealed record EvaluationCase(
        string Id,
        string Question,
        int Repetitions,
        long MaximumInputTokens,
        long MaximumOutputTokens,
        double MaximumLatencyMilliseconds,
        bool ExpectedSynthesis,
        string GradingMode,
        long MaximumGraderInputTokens,
        long MaximumGraderOutputTokens,
        IReadOnlyList<EvaluationMessage> History);

    private readonly record struct EvaluationUsage(long InputTokens, long OutputTokens);

    private sealed record EvaluationTimings(
        double PlannerMilliseconds,
        double McpMilliseconds,
        double TransportQueueResidualMilliseconds,
        double SubmitToFirstOperationResultMilliseconds,
        double? SynthesisMilliseconds,
        double TotalMilliseconds);

    private sealed record EvaluationLatency(
        double P50Milliseconds,
        double P95Milliseconds,
        double P99Milliseconds);

    private sealed record EvaluationLatencySegments(
        EvaluationLatency Planner,
        EvaluationLatency Mcp,
        EvaluationLatency TransportQueueResidual,
        EvaluationLatency SubmitToFirstOperationResult,
        EvaluationLatency Synthesis,
        EvaluationLatency Total);

    private sealed record EvaluationResult(
        string CaseId,
        int Repetition,
        string PromptSha256,
        string GradingMode,
        bool Passed,
        int? Relevance,
        EvaluationUsage CandidateUsage,
        EvaluationUsage GraderUsage,
        EvaluationTimings Timings);

    internal static VerifiedAssistantEvaluationEvidence Verify(
        AssistantEvaluationRelease release,
        IReadOnlyDictionary<string, byte[]> files,
        IReadOnlyList<ArtifactTrustRoot> artifactRoots,
        DateTimeOffset verifiedAt) => Verify(
            release, files, artifactRoots, verifiedAt,
            EvaluationAdmissionTrustStore.Load());

    internal static VerifiedAssistantEvaluationEvidence Verify(
        AssistantEvaluationRelease release,
        IReadOnlyDictionary<string, byte[]> files,
        IReadOnlyList<ArtifactTrustRoot> artifactRoots,
        DateTimeOffset verifiedAt,
        EvaluationAdmissionAuthority admissionAuthority)
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
        VerifyReportProperties(report);
        var target = RequiredObject(RequiredObject(report, "identity"), "target");
        var codeCommit = RequiredString(target, "code_commit");
        if (!Commit.IsMatch(codeCommit) || manifest.CodeCommit != codeCommit
            || tag.Groups[1].Value != codeCommit[..12]
            || tag.Groups[2].Value != reportSha[..12])
            throw new InvalidDataException(
                "Assistant evaluation tag does not bind the full report and code.");

        AssistantEvaluationCatalogContract.Validate(files[CasesFile]);
        using var catalogDocument = Parse(files[CasesFile], CasesFile);
        var catalog = catalogDocument.RootElement;
        VerifyCatalogProperties(catalog);
        var catalogSha = Sha(files[CasesFile]);
        if (RequiredString(catalog, "schema") != "lex-assistant-eval/3")
            throw new InvalidDataException("Assistant evaluation catalog schema is invalid.");
        var frozenAtText = RequiredString(catalog, "frozen_at");
        var frozenAt = Utc(frozenAtText, "catalog frozen_at");
        _ = BoundedString(catalog, 200, "authored_by");
        _ = BoundedString(catalog, 200, "author_id");
        var budget = ReadBudget(catalog);
        var catalogPricing = ReadPricing(catalog, "pricing");
        var cases = ReadCases(catalog);
        var repetitionCount = cases.Sum(item => item.Repetitions);
        if (repetitionCount > 75)
            throw new InvalidDataException(
                "Assistant evaluation catalog summary is outside display bounds.");

        var runAtText = RequiredString(report, "run_at");
        var runAt = Utc(runAtText, "run_at");
        var admissionRunIdentity = RequiredString(report, "admission_run_identity");
        var admissionSha256 = RequiredString(report, "admission_sha256");
        if (RequiredString(report, "schema") != "lex-assistant-eval-report/3"
            || !Fixed(RequiredString(report, "cases_sha256"), catalogSha)
            || RequiredString(report, "frozen_at") != frozenAtText
            || !RunIdentity.IsMatch(admissionRunIdentity)
            || !Digest.IsMatch(admissionSha256)
            || runAt < frozenAt || runAt > verifiedAt.AddMinutes(5)
            || !RequiredBoolean(report, "activation_gate_passed")
            || RequiredArray(report, "gate_failures").GetArrayLength() != 0)
            throw new InvalidDataException(
                "Signed assistant evaluation report does not claim a passing verdict.");

        var (candidateInput, candidateOutput) = RequiredUsage(
            report, "actual_candidate_usage");
        var (graderInput, graderOutput) = RequiredUsage(
            report, "actual_grader_usage");
        var candidateCost = NonnegativeDecimal(report, "actual_candidate_cost_eur");
        var graderCost = NonnegativeDecimal(report, "actual_grader_cost_eur");
        var totalCost = NonnegativeDecimal(report, "actual_total_cost_eur");
        var reportedPricing = ReadPricing(report, "pricing");
        var reportedPricingJson = RequiredObject(report, "pricing");
        if (reportedPricing != catalogPricing
            || runAt < Utc(catalogPricing.RetrievedAt, "pricing retrieved_at")
            || runAt > Utc(catalogPricing.ValidUntil, "pricing valid_until")
            || NonnegativeDecimal(reportedPricingJson,
                    "candidate_input_euros_per_million")
                != catalogPricing.Candidate.Input.EurosPerMillion
            || NonnegativeDecimal(reportedPricingJson,
                    "candidate_output_euros_per_million")
                != catalogPricing.Candidate.Output.EurosPerMillion
            || NonnegativeDecimal(reportedPricingJson,
                    "grader_input_euros_per_million")
                != catalogPricing.Grader.Input.EurosPerMillion
            || NonnegativeDecimal(reportedPricingJson,
                    "grader_output_euros_per_million")
                != catalogPricing.Grader.Output.EurosPerMillion
            || candidateCost + graderCost != totalCost
            || totalCost > budget.MaximumCostEur)
            throw new InvalidDataException("Signed assistant evaluation cost claim is inconsistent.");
        VerifyPreflight(report, cases, catalogPricing, budget);
        var reportedLatency = ReadLatencySegments(report);

        var revision = RequiredString(target, "revision_name");
        var revisionHostname = BoundedString(target, 253, "revision_fqdn");
        var targetResource = BoundedString(target, 1_000, "resource_id");
        var image = BoundedString(target, 500, "image");
        var cpuCores = RequiredDecimal(target, "cpu_cores");
        var memoryLimitBytes = NonnegativeLong(target, "memory_limit_bytes");
        var minimumReplicas = RequiredInt(target, "minimum_replicas");
        var maximumReplicas = RequiredInt(target, "maximum_replicas");
        var trafficWeight = RequiredInt(target, "traffic_weight");
        var artifactSet = RequiredString(target, "artifact_manifest_set");
        var candidateHost = BoundedString(target, 253, "candidate_model_host");
        var candidateDeployment = BoundedString(target, 200, "candidate_deployment");
        var candidateEvidenceSha = RequiredString(target, "evidence_sha256");
        var identity = RequiredObject(report, "identity");
        var indexIds = RequiredArray(identity, "index_manifest_ids").EnumerateArray()
            .Select(StringValue).ToArray();
        if (!Revision.IsMatch(revision)
            || !targetResource.Contains("/providers/Microsoft.App/containerApps/",
                StringComparison.OrdinalIgnoreCase)
            || Uri.CheckHostName(revisionHostname) != UriHostNameType.Dns
            || cpuCores is <= 0 or > 16
            || memoryLimitBytes is < 268_435_456 or > 68_719_476_736
            || !AssistantEvaluationAzureResource.IsContainerApp(targetResource)
            || minimumReplicas != 1 || maximumReplicas != 1 || trafficWeight != 0
            || !Digest.IsMatch(artifactSet) || !Digest.IsMatch(candidateEvidenceSha)
            || indexIds.Length == 0 || indexIds.Any(item => !Digest.IsMatch(item))
            || indexIds.Distinct(StringComparer.Ordinal).Count() != indexIds.Length
            || Uri.CheckHostName(candidateHost) != UriHostNameType.Dns
            || !Fixed(candidateEvidenceSha,
                AssistantEvaluationIdentityDigest.TargetSha256(
                    targetResource, revision, revisionHostname, image, cpuCores,
                    memoryLimitBytes, minimumReplicas, maximumReplicas, trafficWeight,
                    codeCommit, artifactSet, candidateHost, candidateDeployment)))
            throw new InvalidDataException("Assistant evaluation target identity is malformed.");

        var candidateModel = RequiredObject(identity, "candidate_model");
        var graderModel = RequiredObject(identity, "grader_model");
        var candidateModelResource = BoundedString(candidateModel, 1_000, "resource_id");
        var candidateEndpointText = BoundedString(candidateModel, 1_000, "endpoint");
        var candidateEndpoint = AssistantEvaluationIdentityDigest.BareHttpsHost(
            candidateEndpointText);
        var candidateModelDeployment = BoundedString(candidateModel, 200, "deployment");
        var candidateSku = BoundedString(candidateModel, 200, "sku");
        var candidateFormat = BoundedString(candidateModel, 200, "model_format");
        var candidateModelName = BoundedString(candidateModel, 200, "model_name");
        var candidateModelVersion = BoundedString(candidateModel, 200, "model_version");
        var candidateModelEvidence = RequiredString(candidateModel, "evidence_sha256");
        var graderModelResource = BoundedString(graderModel, 1_000, "resource_id");
        var graderEndpointText = BoundedString(graderModel, 1_000, "endpoint");
        _ = AssistantEvaluationIdentityDigest.BareHttpsHost(graderEndpointText);
        var graderDeployment = BoundedString(graderModel, 200, "deployment");
        var graderSku = BoundedString(graderModel, 200, "sku");
        var graderFormat = BoundedString(graderModel, 200, "model_format");
        var graderModelName = BoundedString(graderModel, 200, "model_name");
        var graderModelVersion = BoundedString(graderModel, 200, "model_version");
        var graderModelEvidence = RequiredString(graderModel, "evidence_sha256");
        if (!string.Equals(candidateEndpoint, candidateHost, StringComparison.OrdinalIgnoreCase)
            || candidateModelDeployment != candidateDeployment
            || !AssistantEvaluationAzureResource.IsModelAccount(candidateModelResource)
            || !AssistantEvaluationAzureResource.IsModelAccount(graderModelResource)
            || string.Equals(candidateModelResource, graderModelResource,
                StringComparison.OrdinalIgnoreCase)
            || !ValidIdentity(candidateModelDeployment)
            || !ValidIdentity(graderDeployment)
            || candidateFormat != "OpenAI" || graderFormat != "OpenAI"
            || !Digest.IsMatch(candidateModelEvidence) || !Digest.IsMatch(graderModelEvidence)
            || !Fixed(candidateModelEvidence,
                AssistantEvaluationIdentityDigest.ModelSha256(
                    candidateModelResource, candidateEndpointText, candidateModelDeployment,
                    candidateSku, candidateFormat, candidateModelName, candidateModelVersion))
            || !Fixed(graderModelEvidence,
                AssistantEvaluationIdentityDigest.ModelSha256(
                    graderModelResource, graderEndpointText, graderDeployment,
                    graderSku, graderFormat, graderModelName, graderModelVersion))
            || string.Equals(candidateFormat, graderFormat,
                StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateModelName, graderModelName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateModelVersion, graderModelVersion,
                    StringComparison.OrdinalIgnoreCase)
            || candidateModelName != catalogPricing.Candidate.ModelName
            || candidateModelVersion != catalogPricing.Candidate.ModelVersion
            || candidateSku != catalogPricing.Candidate.Sku
            || graderModelName != catalogPricing.Grader.ModelName
            || graderModelVersion != catalogPricing.Grader.ModelVersion
            || graderSku != catalogPricing.Grader.Sku)
            throw new InvalidDataException(
                "Assistant evaluation candidate model route is inconsistent.");

        var admissionBytes = files[AdmissionFile];
        if (!Fixed(admissionSha256, Sha(admissionBytes)))
            throw new InvalidDataException(
                "Signed assistant evaluation report does not bind the admission bytes.");
        var admission = EvaluationAdmissionContract.Verify(
            admissionBytes,
            Encoding.UTF8.GetString(files[AdmissionSignatureFile]).Trim(),
            admissionAuthority,
            new EvaluationAdmissionIdentity(
                revision, image, codeCommit, artifactSet, catalogSha),
            runAt);
        EvaluationAdmissionContract.VerifyEvidenceIdentity(
            admission, candidateEvidenceSha,
            candidateModelEvidence, graderModelEvidence);
        if (!Fixed(admissionRunIdentity,
                EvaluationAdmissionContract.RunIdentity(admission)))
            throw new InvalidDataException(
                "Signed assistant evaluation admission run identity is invalid.");

        using var browserDocument = Parse(files[BrowserEvidenceFile], BrowserEvidenceFile);
        var browser = browserDocument.RootElement;
        VerifyBrowserProperties(browser);
        var browserRunAt = Utc(RequiredString(browser, "run_at"), "browser run_at");
        var browserVersion = BoundedString(browser, 100, "browser_version");
        var browserSamples = RequiredArray(browser, "samples_milliseconds").EnumerateArray()
            .Select(NonnegativeDoubleValue).ToArray();
        var browserLatency = ReadLatency(RequiredObject(browser, "latency"));
        var browserP95 = browserLatency.P95Milliseconds;
        if (RequiredString(browser, "schema") != "lex-assistant-browser-evidence/1"
            || browserRunAt < runAt.AddMinutes(-5)
            || browserRunAt > verifiedAt.AddMinutes(5)
            || !RequiredBoolean(browser, "passed")
            || RequiredString(browser, "revision_name") != revision
            || RequiredString(browser, "base_url") != $"https://{revisionHostname}"
            || RequiredString(browser, "code_commit") != codeCommit
            || !Fixed(RequiredString(browser, "artifact_manifest_set"), artifactSet)
            || !Fixed(RequiredString(browser, "candidate_evidence_sha256"), candidateEvidenceSha)
            || RequiredString(browser, "browser_name") != "chromium"
            || string.IsNullOrWhiteSpace(browserVersion)
            || RequiredInt(browser, "viewport_width") != 1440
            || RequiredInt(browser, "viewport_height") != 900
            || RequiredString(browser, "metric")
                != "operation_result_received_to_presented_ms"
            || browserSamples.Length is < 5 or > 20
            || browserLatency != LatencyOfSamples(browserSamples)
            || NonnegativeDouble(browser, "maximum_p95_milliseconds") != 500
            || browserP95 > 500)
            throw new InvalidDataException(
                "Signed assistant browser evidence claim is invalid or mismatched.");

        var expectedSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["artifact_manifest_set"] = artifactSet,
            ["admission_run_identity"] = admissionRunIdentity,
            ["admission_sha256"] = admissionSha256,
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

        // The public page independently recomputes the deterministic verdict from the signed rows.
        // A signature authenticates bytes; it does not make a contradictory passing boolean true.
        long resultCandidateInput = 0;
        long resultCandidateOutput = 0;
        long resultGraderInput = 0;
        long resultGraderOutput = 0;
        var outcomes = RequiredArray(report, "results").EnumerateArray().Select(item =>
        {
            var relevance = RequiredObject(item, "relevance");
            var score = NullableInt(relevance, "score");
            var cause = NullableString(relevance, "unavailable_cause");
            var candidateUsage = RequiredUsage(item, "candidate_usage");
            var graderUsage = RequiredUsage(item, "grader_usage");
            var gradingMode = RequiredString(item, "grading_mode");
            if (gradingMode == "llm"
                    && !AssistantEvaluationRelevanceContract.IsCoherent(
                        score, cause, graderUsage.InputTokens, graderUsage.OutputTokens)
                || gradingMode == "deterministic"
                    && (score is not null || cause is not null
                        || graderUsage.InputTokens != 0 || graderUsage.OutputTokens != 0)
                || gradingMode is not ("llm" or "deterministic"))
                throw new InvalidDataException(
                    "Assistant evaluation relevance evidence is incoherent.");
            try
            {
                resultCandidateInput = checked(
                    resultCandidateInput + candidateUsage.InputTokens);
                resultCandidateOutput = checked(
                    resultCandidateOutput + candidateUsage.OutputTokens);
                resultGraderInput = checked(resultGraderInput + graderUsage.InputTokens);
                resultGraderOutput = checked(resultGraderOutput + graderUsage.OutputTokens);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    "Assistant evaluation result usage overflowed.", exception);
            }
            var promptSha = RequiredString(item, "prompt_sha256");
            if (!Digest.IsMatch(promptSha))
                throw new InvalidDataException(
                    "Assistant evaluation prompt digest is invalid.");
            return new EvaluationResult(
                BoundedString(item, 100, "case_id"),
                RequiredInt(item, "repetition"),
                promptSha,
                gradingMode,
                RequiredBoolean(item, "passed"),
                score,
                candidateUsage,
                graderUsage,
                ReadTimings(item));
        }).ToArray();
        decimal expectedCandidateCost;
        decimal expectedGraderCost;
        try
        {
            expectedCandidateCost = resultCandidateInput
                    * catalogPricing.Candidate.Input.EurosPerMillion / 1_000_000m
                + resultCandidateOutput
                    * catalogPricing.Candidate.Output.EurosPerMillion / 1_000_000m;
            expectedGraderCost = resultGraderInput
                    * catalogPricing.Grader.Input.EurosPerMillion / 1_000_000m
                + resultGraderOutput
                    * catalogPricing.Grader.Output.EurosPerMillion / 1_000_000m;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Assistant evaluation result cost overflowed.", exception);
        }
        if (candidateInput == 0 && candidateOutput == 0
            || resultCandidateInput != candidateInput || resultCandidateOutput != candidateOutput
            || resultGraderInput != graderInput || resultGraderOutput != graderOutput
            || candidateInput > budget.MaximumCandidateInputTokens
            || candidateOutput > budget.MaximumCandidateOutputTokens
            || graderInput > budget.MaximumGraderInputTokens
            || graderOutput > budget.MaximumGraderOutputTokens
            || expectedCandidateCost != candidateCost || expectedGraderCost != graderCost)
            throw new InvalidDataException(
                "Assistant evaluation aggregate usage or cost evidence is invalid.");
        var byCase = outcomes.GroupBy(item => item.CaseId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (outcomes.Length != repetitionCount
            || byCase.Count != cases.Length
            || cases.Any(item => !byCase.TryGetValue(item.Id, out var rows)
                || rows.Length != item.Repetitions))
            throw new InvalidDataException(
                "Signed assistant evaluation results do not cover the signed catalog exactly.");
        foreach (var evaluationCase in cases)
        {
            var rows = byCase[evaluationCase.Id].OrderBy(item => item.Repetition).ToArray();
            var promptSha = PromptSha256(evaluationCase);
            var candidateInputCeiling = checked(evaluationCase.MaximumInputTokens
                + evaluationCase.History.Sum(message => message.MaximumInputTokens));
            var candidateOutputCeiling = checked(evaluationCase.MaximumOutputTokens
                + evaluationCase.History.Sum(message => message.MaximumOutputTokens));
            if (!rows.Select(item => item.Repetition)
                    .SequenceEqual(Enumerable.Range(1, evaluationCase.Repetitions))
                || rows.Any(item => !item.Passed
                    || !Fixed(item.PromptSha256, promptSha)
                    || item.GradingMode != evaluationCase.GradingMode
                    || item.CandidateUsage.InputTokens > candidateInputCeiling
                    || item.CandidateUsage.OutputTokens > candidateOutputCeiling
                    || item.GraderUsage.InputTokens
                        > evaluationCase.MaximumGraderInputTokens
                    || item.GraderUsage.OutputTokens
                        > evaluationCase.MaximumGraderOutputTokens
                    || evaluationCase.ExpectedSynthesis
                        && item.Timings.SynthesisMilliseconds is null
                    || !evaluationCase.ExpectedSynthesis
                        && item.Timings.SynthesisMilliseconds is not null
                    || item.Timings.PlannerMilliseconds > 12_000
                    || item.Timings.SubmitToFirstOperationResultMilliseconds
                        > budget.MaximumFirstOperationHardLatencyMilliseconds
                    || item.Timings.TotalMilliseconds
                        > evaluationCase.MaximumLatencyMilliseconds))
                throw new InvalidDataException(
                    "Assistant evaluation result contradicts its signed catalog case.");
        }
        var recomputedLatency = LatencyOf(outcomes.Select(item => item.Timings).ToArray());
        if (reportedLatency != recomputedLatency
            || reportedLatency.SubmitToFirstOperationResult.P95Milliseconds
                > budget.MaximumFirstOperationP95LatencyMilliseconds
            || reportedLatency.Synthesis.P95Milliseconds
                > budget.MaximumSynthesisP95LatencyMilliseconds
            || reportedLatency.TransportQueueResidual.P95Milliseconds
                > budget.MaximumTransportQueueResidualP95LatencyMilliseconds
            || reportedLatency.Total.P99Milliseconds
                > budget.MaximumTotalP99LatencyMilliseconds)
            throw new InvalidDataException(
                "Assistant evaluation latency evidence is inconsistent.");
        var caseOutcomes = cases.Select(item => new AssistantEvaluationCaseOutcome(
            item.Id,
            item.Question,
            item.Repetitions,
            byCase[item.Id].Count(row => row.Passed),
            [.. byCase[item.Id].OrderBy(row => row.Repetition)
                .Select(row => row.Relevance)])).ToArray();

        return new(release.Repository, release.Tag, release.HtmlUrl,
            release.Assets[ReportFile].BrowserDownloadUrl,
            release.Assets[ManifestFile].BrowserDownloadUrl,
            release.Assets[ManifestSignatureFile].BrowserDownloadUrl,
            runAtText, codeCommit, revision, revisionHostname, image, artifactSet, catalogSha,
            reportSha, candidateEvidenceSha, candidateHost, candidateDeployment,
            candidateModelName, candidateModelVersion, graderDeployment, graderModelName,
            graderModelVersion, indexIds, cases.Length, repetitionCount, candidateInput,
            candidateOutput, graderInput, graderOutput, totalCost, budget.MaximumCostEur,
            reportedLatency.SubmitToFirstOperationResult.P95Milliseconds,
            reportedLatency.Total.P99Milliseconds, browserP95, caseOutcomes);
    }

    private static void VerifyReportProperties(JsonElement report)
    {
        ExactProperties(report,
            "schema", "cases_sha256", "frozen_at", "run_at",
            "admission_run_identity", "admission_sha256", "identity", "preflight",
            "pricing", "actual_candidate_usage", "actual_grader_usage",
            "actual_candidate_cost_eur", "actual_grader_cost_eur", "actual_total_cost_eur",
            "latency", "results", "gate_failures", "activation_gate_passed");

        var identity = RequiredObject(report, "identity");
        ExactProperties(identity,
            "target", "index_manifest_ids", "candidate_model", "grader_model");
        ExactProperties(RequiredObject(identity, "target"),
            "resource_id", "revision_name", "revision_fqdn", "image", "cpu_cores",
            "memory_limit_bytes", "minimum_replicas", "maximum_replicas", "traffic_weight",
            "code_commit", "artifact_manifest_set", "candidate_model_host",
            "candidate_deployment", "evidence_sha256");
        foreach (var role in new[] { "candidate_model", "grader_model" })
            ExactProperties(RequiredObject(identity, role),
                "resource_id", "endpoint", "deployment", "sku", "model_format",
                "model_name", "model_version", "evidence_sha256");

        ExactProperties(RequiredObject(report, "preflight"),
            "reserved_candidate_input_tokens", "reserved_candidate_output_tokens",
            "reserved_grader_input_tokens", "reserved_grader_output_tokens",
            "estimated_candidate_cost_eur", "estimated_grader_cost_eur",
            "estimated_total_cost_eur");

        var pricing = RequiredObject(report, "pricing");
        ExactProperties(pricing,
            "schema", "currency", "source_uri", "retrieved_at", "valid_until",
            "candidate", "grader", "candidate_input_euros_per_million",
            "candidate_output_euros_per_million", "grader_input_euros_per_million",
            "grader_output_euros_per_million");
        foreach (var role in new[] { "candidate", "grader" })
        {
            var model = RequiredObject(pricing, role);
            ExactProperties(model,
                "model_name", "model_version", "sku", "input", "output");
            foreach (var axis in new[] { "input", "output" })
                ExactProperties(RequiredObject(model, axis),
                    "meter_id", "meter_name", "effective_start_date", "euros_per_million");
        }

        ExactProperties(RequiredObject(report, "actual_candidate_usage"),
            "input_tokens", "output_tokens", "total_tokens");
        ExactProperties(RequiredObject(report, "actual_grader_usage"),
            "input_tokens", "output_tokens", "total_tokens");

        var latency = RequiredObject(report, "latency");
        ExactProperties(latency, "planner", "mcp", "transport_queue_residual",
            "submit_to_first_operation_result", "synthesis", "total");
        foreach (var segment in new[]
                 {
                     "planner", "mcp", "transport_queue_residual",
                     "submit_to_first_operation_result", "synthesis", "total",
                 })
            ExactProperties(RequiredObject(latency, segment),
                "p50_milliseconds", "p95_milliseconds", "p99_milliseconds");

        foreach (var result in RequiredArray(report, "results").EnumerateArray())
        {
            ExactProperties(result,
                "case_id", "repetition", "prompt_sha256", "grading_mode", "passed",
                "failures", "relevance", "candidate_usage", "grader_usage", "timings");
            ExactProperties(RequiredObject(result, "relevance"),
                "score", "unavailable_cause");
            if (RequiredArray(result, "failures").GetArrayLength() != 0)
                throw new InvalidDataException(
                    "Passing assistant evaluation result contains failure details.");
            ExactProperties(RequiredObject(result, "candidate_usage"),
                "input_tokens", "output_tokens", "total_tokens");
            ExactProperties(RequiredObject(result, "grader_usage"),
                "input_tokens", "output_tokens", "total_tokens");
            ExactProperties(RequiredObject(result, "timings"),
                "planner_milliseconds", "mcp_milliseconds",
                "transport_queue_residual_milliseconds",
                "submit_to_first_operation_result_milliseconds",
                "synthesis_milliseconds", "total_milliseconds");
        }
    }

    private static void VerifyCatalogProperties(JsonElement catalog)
    {
        ExactProperties(catalog,
            "schema", "frozen_at", "authored_by", "author_id",
            "pricing", "budget", "cases");

        var pricing = RequiredObject(catalog, "pricing");
        ExactProperties(pricing,
            "schema", "currency", "source_uri", "retrieved_at", "valid_until",
            "candidate", "grader");
        foreach (var role in new[] { "candidate", "grader" })
        {
            var model = RequiredObject(pricing, role);
            ExactProperties(model,
                "model_name", "model_version", "sku", "input", "output");
            foreach (var axis in new[] { "input", "output" })
                ExactProperties(RequiredObject(model, axis),
                    "meter_id", "meter_name", "effective_start_date", "euros_per_million");
        }

        ExactProperties(RequiredObject(catalog, "budget"),
            "maximum_candidate_input_tokens", "maximum_candidate_output_tokens",
            "maximum_grader_input_tokens", "maximum_grader_output_tokens",
            "maximum_cost_eur", "maximum_first_operation_p95_latency_ms",
            "maximum_first_operation_hard_latency_ms",
            "maximum_synthesis_p95_latency_ms",
            "maximum_transport_queue_residual_p95_latency_ms",
            "maximum_total_p99_latency_ms");

        foreach (var item in RequiredArray(catalog, "cases").EnumerateArray())
        {
            ClosedProperties(item,
                ["id", "question", "repetitions", "maximum_input_tokens",
                    "maximum_output_tokens", "maximum_latency_ms", "expected_synthesis",
                    "expected", "grading"],
                "history");
            VerifyExpectedProperties(RequiredObject(item, "expected"));
            var grading = RequiredObject(item, "grading");
            ClosedProperties(grading,
                ["mode", "maximum_input_tokens", "maximum_output_tokens"], "rubric");
            if (grading.TryGetProperty("rubric", out var rubric))
                _ = BoundedStringValue(rubric, 4_000,
                    "Assistant evaluation catalog grading rubric is invalid.");
            if (!item.TryGetProperty("history", out var history)) continue;
            if (history.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(
                    "Assistant evaluation catalog history is not an array.");
            foreach (var message in history.EnumerateArray())
            {
                ClosedProperties(message,
                    ["role", "content", "maximum_input_tokens", "maximum_output_tokens"],
                    "expected_synthesis", "expected");
                var hasExpectedSynthesis = message.TryGetProperty(
                    "expected_synthesis", out _);
                var hasExpected = message.TryGetProperty("expected", out var expected);
                if (hasExpectedSynthesis != hasExpected)
                    throw new InvalidDataException(
                        "Assistant evaluation catalog setup contract is incomplete.");
                if (hasExpectedSynthesis)
                    _ = RequiredBoolean(message, "expected_synthesis");
                if (hasExpected)
                    VerifyExpectedProperties(expected);
            }
        }
    }

    private static void VerifyExpectedProperties(JsonElement expected)
    {
        OnlyProperties(expected,
            "tool", "legal_outcome", "transport_outcome", "effect", "arguments",
            "gap_status", "clarification", "population_minimum", "population_path",
            "forbidden_reply_contains", "argument_alternatives", "operations");
        foreach (var name in new[]
                 {
                     "tool", "legal_outcome", "transport_outcome", "effect", "gap_status",
                 })
            if (expected.TryGetProperty(name, out var value))
                _ = BoundedStringValue(value, 200,
                    "Assistant evaluation catalog expected string is invalid.");
        if (expected.TryGetProperty("clarification", out _))
            _ = RequiredBoolean(expected, "clarification");
        if (expected.TryGetProperty("population_minimum", out _)
            && RequiredInt(expected, "population_minimum") < 0)
            throw new InvalidDataException(
                "Assistant evaluation catalog population minimum is invalid.");
        if (expected.TryGetProperty("population_path", out var populationPath))
        {
            var path = BoundedStringValue(populationPath, 200,
                "Assistant evaluation catalog population path is invalid.");
            if (!path.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Assistant evaluation catalog population path is invalid.");
        }
        if (expected.TryGetProperty("arguments", out var arguments))
            VerifyStringMap(arguments);
        if (expected.TryGetProperty("argument_alternatives", out var alternatives))
            VerifyStringMapArray(alternatives);
        if (expected.TryGetProperty("forbidden_reply_contains", out var forbidden))
        {
            if (forbidden.ValueKind != JsonValueKind.Array
                || forbidden.GetArrayLength() > 8
                || forbidden.EnumerateArray().Any(value =>
                    value.ValueKind != JsonValueKind.String
                    || value.GetString() is not { Length: > 0 and <= 100 }))
                throw new InvalidDataException(
                    "Assistant evaluation catalog string list is invalid.");
        }
        if (!expected.TryGetProperty("operations", out var operations)) return;
        if (operations.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                "Assistant evaluation catalog operations are not an array.");
        foreach (var operation in operations.EnumerateArray())
        {
            ClosedProperties(operation,
                ["tool", "legal_outcome", "transport_outcome", "effect", "arguments"],
                "argument_alternatives");
            foreach (var name in new[]
                     {
                         "tool", "legal_outcome", "transport_outcome", "effect",
                     })
                _ = BoundedString(operation, 200, name);
            VerifyStringMap(RequiredObject(operation, "arguments"));
            if (operation.TryGetProperty("argument_alternatives", out var operationAlternatives))
                VerifyStringMapArray(operationAlternatives);
        }
    }

    private static void VerifyStringMapArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                "Assistant evaluation catalog map list is invalid.");
        foreach (var item in value.EnumerateArray()) VerifyStringMap(item);
    }

    private static void VerifyStringMap(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "Assistant evaluation catalog argument map is invalid.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || property.Name.Length is 0 or > 64
                || property.Value.ValueKind != JsonValueKind.String
                || property.Value.GetString() is not { Length: > 0 and <= 1_000 })
                throw new InvalidDataException(
                    "Assistant evaluation catalog argument map is ambiguous.");
    }

    private static void VerifyBrowserProperties(JsonElement browser)
    {
        ExactProperties(browser,
            "schema", "run_at", "base_url", "revision_name", "code_commit",
            "artifact_manifest_set", "candidate_evidence_sha256", "browser_name",
            "browser_version", "viewport_width", "viewport_height", "metric",
            "samples_milliseconds", "latency", "maximum_p95_milliseconds", "passed");
        ExactProperties(RequiredObject(browser, "latency"),
            "p50_milliseconds", "p95_milliseconds", "p99_milliseconds");
    }

    private static EvaluationPricing ReadPricing(JsonElement root, params string[] path)
    {
        var value = RequiredObject(root, path);
        var candidate = ReadModelPricing(RequiredObject(value, "candidate"));
        var grader = ReadModelPricing(RequiredObject(value, "grader"));
        var pricing = new EvaluationPricing(
            RequiredString(value, "schema"),
            RequiredString(value, "currency"),
            RequiredString(value, "source_uri"),
            RequiredString(value, "retrieved_at"),
            RequiredString(value, "valid_until"),
            candidate,
            grader);
        var retrieved = Utc(pricing.RetrievedAt, "pricing retrieved_at");
        var validUntil = Utc(pricing.ValidUntil, "pricing valid_until");
        if (pricing.Schema != "lex-assistant-eval-pricing/1"
            || pricing.Currency != "EUR"
            || pricing.SourceUri != "https://prices.azure.com/api/retail/prices"
            || validUntil <= retrieved || validUntil - retrieved > TimeSpan.FromDays(7))
            throw new InvalidDataException(
                "Assistant evaluation pricing identity is invalid.");
        return pricing;
    }

    private static EvaluationModelPricing ReadModelPricing(JsonElement model)
    {
        var pricing = new EvaluationModelPricing(
            BoundedString(model, 100, "model_name"),
            BoundedString(model, 100, "model_version"),
            RequiredString(model, "sku"),
            ReadMeterPrice(RequiredObject(model, "input")),
            ReadMeterPrice(RequiredObject(model, "output")));
        if (pricing.Sku != "GlobalStandard")
            throw new InvalidDataException(
                "Assistant evaluation pricing model identity is invalid.");
        return pricing;
    }

    private static EvaluationMeterPrice ReadMeterPrice(JsonElement meter)
    {
        var price = new EvaluationMeterPrice(
            BoundedString(meter, 100, "meter_id"),
            BoundedString(meter, 200, "meter_name"),
            RequiredString(meter, "effective_start_date"),
            RequiredDecimal(meter, "euros_per_million"));
        _ = Utc(price.EffectiveStartDate, "price effective_start_date");
        if (price.EurosPerMillion is <= 0 or > 1_000)
            throw new InvalidDataException(
                "Assistant evaluation meter price is invalid.");
        return price;
    }

    private static EvaluationBudget ReadBudget(JsonElement catalog)
    {
        var value = RequiredObject(catalog, "budget");
        var budget = new EvaluationBudget(
            NonnegativeLong(value, "maximum_candidate_input_tokens"),
            NonnegativeLong(value, "maximum_candidate_output_tokens"),
            NonnegativeLong(value, "maximum_grader_input_tokens"),
            NonnegativeLong(value, "maximum_grader_output_tokens"),
            RequiredDecimal(value, "maximum_cost_eur"),
            NonnegativeDouble(value, "maximum_first_operation_p95_latency_ms"),
            NonnegativeDouble(value, "maximum_first_operation_hard_latency_ms"),
            NonnegativeDouble(value, "maximum_synthesis_p95_latency_ms"),
            NonnegativeDouble(value,
                "maximum_transport_queue_residual_p95_latency_ms"),
            NonnegativeDouble(value, "maximum_total_p99_latency_ms"));
        if (budget.MaximumCandidateInputTokens is < 1 or > 1_000_000
            || budget.MaximumCandidateOutputTokens is < 1 or > 125_000
            || budget.MaximumGraderInputTokens is < 1 or > 1_000_000
            || budget.MaximumGraderOutputTokens is < 1 or > 392_000
            || budget.MaximumCostEur is <= 0 or > 10
            || budget.MaximumFirstOperationP95LatencyMilliseconds is < 1_000 or > 25_000
            || budget.MaximumFirstOperationHardLatencyMilliseconds
                < budget.MaximumFirstOperationP95LatencyMilliseconds
            || budget.MaximumFirstOperationHardLatencyMilliseconds > 25_000
            || budget.MaximumSynthesisP95LatencyMilliseconds is < 1_000 or > 60_000
            || budget.MaximumTransportQueueResidualP95LatencyMilliseconds is < 1 or > 1_500
            || budget.MaximumTotalP99LatencyMilliseconds
                < budget.MaximumFirstOperationHardLatencyMilliseconds
            || budget.MaximumTotalP99LatencyMilliseconds > 90_000)
            throw new InvalidDataException(
                "Assistant evaluation budget is outside the release envelope.");
        return budget;
    }

    private static EvaluationCase[] ReadCases(JsonElement catalog)
    {
        var cases = RequiredArray(catalog, "cases").EnumerateArray().Select(item =>
        {
            var history = item.TryGetProperty("history", out var historyValue)
                ? historyValue.EnumerateArray().Select(message => new EvaluationMessage(
                    RequiredString(message, "role"),
                    BoundedString(message, 1_000, "content"),
                    NonnegativeLong(message, "maximum_input_tokens"),
                    NonnegativeLong(message, "maximum_output_tokens"))).ToArray()
                : [];
            var grading = RequiredObject(item, "grading");
            var result = new EvaluationCase(
                BoundedString(item, 100, "id"),
                BoundedString(item, 1_000, "question"),
                RequiredInt(item, "repetitions"),
                NonnegativeLong(item, "maximum_input_tokens"),
                NonnegativeLong(item, "maximum_output_tokens"),
                NonnegativeDouble(item, "maximum_latency_ms"),
                RequiredBoolean(item, "expected_synthesis"),
                RequiredString(grading, "mode"),
                NonnegativeLong(grading, "maximum_input_tokens"),
                NonnegativeLong(grading, "maximum_output_tokens"),
                history);
            if (result.Repetitions is < 1 or > 3
                || result.MaximumInputTokens is < 1 or > 100_000
                || result.MaximumOutputTokens is < 1 or > 20_000
                || result.MaximumLatencyMilliseconds is < 1_000 or > 90_000
                || result.GradingMode is not ("deterministic" or "llm")
                || result.MaximumGraderInputTokens is < 1 or > 100_000
                || result.MaximumGraderOutputTokens is < 1 or > 20_000
                || history.Length > 8
                || history.Any(message => message.Role != "user"
                    || message.MaximumInputTokens is < 1 or > 100_000
                    || message.MaximumOutputTokens is < 1 or > 20_000))
                throw new InvalidDataException(
                    "Assistant evaluation case bounds are invalid.");
            return result;
        }).ToArray();
        if (cases.Length is < 1 or > 25
            || cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count()
                != cases.Length
            || cases.Select(item => NormalizeQuestion(item.Question))
                .Distinct(StringComparer.Ordinal).Count() != cases.Length
            || !cases.Any(item => item.ExpectedSynthesis)
            || !cases.Any(item => !item.ExpectedSynthesis))
            throw new InvalidDataException(
                "Assistant evaluation catalog cases are incomplete or ambiguous.");
        return cases;
    }

    private static string NormalizeQuestion(string value) => string.Join(' ',
        value.Trim().ToLowerInvariant().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    private static void VerifyPreflight(
        JsonElement report,
        IReadOnlyList<EvaluationCase> cases,
        EvaluationPricing pricing,
        EvaluationBudget budget)
    {
        long candidateInput;
        long candidateOutput;
        long graderInput;
        long graderOutput;
        decimal candidateCost;
        decimal graderCost;
        try
        {
            candidateInput = cases.Sum(item => checked(
                (item.MaximumInputTokens + item.History.Sum(message => message.MaximumInputTokens))
                * item.Repetitions));
            candidateOutput = cases.Sum(item => checked(
                (item.MaximumOutputTokens + item.History.Sum(message => message.MaximumOutputTokens))
                * item.Repetitions));
            graderInput = cases.Sum(item => item.GradingMode == "llm"
                ? checked(item.MaximumGraderInputTokens * item.Repetitions) : 0);
            graderOutput = cases.Sum(item => item.GradingMode == "llm"
                ? checked(item.MaximumGraderOutputTokens * item.Repetitions) : 0);
            candidateCost = candidateInput * pricing.Candidate.Input.EurosPerMillion / 1_000_000m
                + candidateOutput * pricing.Candidate.Output.EurosPerMillion / 1_000_000m;
            graderCost = graderInput * pricing.Grader.Input.EurosPerMillion / 1_000_000m
                + graderOutput * pricing.Grader.Output.EurosPerMillion / 1_000_000m;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Assistant evaluation preflight overflowed.", exception);
        }
        var preflight = RequiredObject(report, "preflight");
        if (candidateInput > budget.MaximumCandidateInputTokens
            || candidateOutput > budget.MaximumCandidateOutputTokens
            || graderInput > budget.MaximumGraderInputTokens
            || graderOutput > budget.MaximumGraderOutputTokens
            || candidateCost + graderCost > budget.MaximumCostEur
            || NonnegativeLong(preflight, "reserved_candidate_input_tokens") != candidateInput
            || NonnegativeLong(preflight, "reserved_candidate_output_tokens") != candidateOutput
            || NonnegativeLong(preflight, "reserved_grader_input_tokens") != graderInput
            || NonnegativeLong(preflight, "reserved_grader_output_tokens") != graderOutput
            || NonnegativeDecimal(preflight, "estimated_candidate_cost_eur") != candidateCost
            || NonnegativeDecimal(preflight, "estimated_grader_cost_eur") != graderCost
            || NonnegativeDecimal(preflight, "estimated_total_cost_eur")
                != candidateCost + graderCost)
            throw new InvalidDataException(
                "Assistant evaluation preflight is inconsistent with the signed catalog.");
    }

    private static EvaluationTimings ReadTimings(JsonElement result)
    {
        var value = RequiredObject(result, "timings");
        var timings = new EvaluationTimings(
            NonnegativeDouble(value, "planner_milliseconds"),
            NonnegativeDouble(value, "mcp_milliseconds"),
            NonnegativeDouble(value, "transport_queue_residual_milliseconds"),
            NonnegativeDouble(value, "submit_to_first_operation_result_milliseconds"),
            NullableNonnegativeDouble(value, "synthesis_milliseconds"),
            NonnegativeDouble(value, "total_milliseconds"));
        if (timings.TotalMilliseconds < timings.SubmitToFirstOperationResultMilliseconds
            || timings.TotalMilliseconds < timings.PlannerMilliseconds
            || timings.TotalMilliseconds < timings.McpMilliseconds
            || timings.SubmitToFirstOperationResultMilliseconds
                < timings.TransportQueueResidualMilliseconds
            || timings.SynthesisMilliseconds is not null
                && timings.TotalMilliseconds < timings.SynthesisMilliseconds)
            throw new InvalidDataException(
                "Assistant evaluation result timings are inconsistent.");
        return timings;
    }

    private static string PromptSha256(EvaluationCase evaluationCase)
    {
        var messages = evaluationCase.History
            .Select(message => $"{message.Role}\n{message.Content}")
            .Append($"user\n{evaluationCase.Question}");
        return Sha(Encoding.UTF8.GetBytes(string.Join("\n---\n", messages)));
    }

    private static EvaluationLatencySegments ReadLatencySegments(JsonElement report)
    {
        var latency = RequiredObject(report, "latency");
        return new EvaluationLatencySegments(
            ReadLatency(RequiredObject(latency, "planner")),
            ReadLatency(RequiredObject(latency, "mcp")),
            ReadLatency(RequiredObject(latency, "transport_queue_residual")),
            ReadLatency(RequiredObject(latency, "submit_to_first_operation_result")),
            ReadLatency(RequiredObject(latency, "synthesis")),
            ReadLatency(RequiredObject(latency, "total")));
    }

    private static EvaluationLatency ReadLatency(JsonElement value) => new(
        NonnegativeDouble(value, "p50_milliseconds"),
        NonnegativeDouble(value, "p95_milliseconds"),
        NonnegativeDouble(value, "p99_milliseconds"));

    private static EvaluationLatencySegments LatencyOf(
        IReadOnlyList<EvaluationTimings> values)
    {
        EvaluationLatency Segment(Func<EvaluationTimings, double> read) =>
            LatencyOfSamples(values.Select(read).ToArray());
        return new EvaluationLatencySegments(
            Segment(item => item.PlannerMilliseconds),
            Segment(item => item.McpMilliseconds),
            Segment(item => item.TransportQueueResidualMilliseconds),
            Segment(item => item.SubmitToFirstOperationResultMilliseconds),
            LatencyOfSamples(values.Select(item => item.SynthesisMilliseconds)
                .Where(value => value.HasValue).Select(value => value!.Value).ToArray()),
            Segment(item => item.TotalMilliseconds));
    }

    private static EvaluationLatency LatencyOfSamples(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return new(0, 0, 0);
        var ordered = values.Order().ToArray();
        double At(double quantile) => ordered[Math.Clamp(
            (int)Math.Ceiling(quantile * ordered.Length) - 1, 0, ordered.Length - 1)];
        return new(At(0.50), At(0.95), At(0.99));
    }

    private static void ClosedProperties(
        JsonElement value,
        IReadOnlyList<string> required,
        params string[] optional)
    {
        OnlyProperties(value, [.. required, .. optional]);
        if (required.Any(name => !value.TryGetProperty(name, out _)))
            throw new InvalidDataException(
                "Assistant evaluation evidence is missing a required property.");
    }

    private static void ExactProperties(JsonElement value, params string[] expected)
    {
        OnlyProperties(value, expected);
        if (value.GetPropertyCount() != expected.Length)
            throw new InvalidDataException(
                "Assistant evaluation evidence is missing a required property.");
    }

    private static void OnlyProperties(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Assistant evaluation report object is invalid.");
        var remaining = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name))
                throw new InvalidDataException(
                    "Assistant evaluation report contains an unknown or duplicate property.");
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
                AdmissionFile => EvaluationAdmissionContract.MaximumBytes,
                BrowserEvidenceFile or ReviewFile or ManifestFile => 256L * 1024,
                ReviewSignatureFile or AdmissionSignatureFile or ManifestSignatureFile
                    => 16L * 1024,
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

    private static string BoundedStringValue(
        JsonElement value,
        int maximum,
        string error)
    {
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } text
            || text.Length > maximum)
            throw new InvalidDataException(error);
        return text;
    }

    private static bool ValidIdentity(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        && value is not ("uncommitted" or "unverified" or "not supplied");

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

    private static int? NullableInt(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : throw new InvalidDataException(
                "Assistant evaluation nullable integer field is invalid.");
    }

    private static string? NullableString(JsonElement root, params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 and <= 100 } text)
            throw new InvalidDataException(
                "Assistant evaluation nullable string field is invalid.");
        return text;
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

    private static EvaluationUsage RequiredUsage(
        JsonElement root,
        params string[] path)
    {
        var usage = RequiredObject(root, path);
        var input = NonnegativeLong(usage, "input_tokens");
        var output = NonnegativeLong(usage, "output_tokens");
        if (!AssistantEvaluationRelevanceContract.IsValidUsage(input, output))
            throw new InvalidDataException("Assistant evaluation token usage is invalid.");
        if (usage.TryGetProperty("total_tokens", out _)
            && NonnegativeLong(usage, "total_tokens") != checked(input + output))
            throw new InvalidDataException("Assistant evaluation token total is inconsistent.");
        return new(input, output);
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
        return NonnegativeDoubleValue(value);
    }

    private static double NonnegativeDoubleValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || number < 0 || double.IsNaN(number) || double.IsInfinity(number))
            throw new InvalidDataException("Assistant evaluation measurement claim is invalid.");
        return number;
    }

    private static double? NullableNonnegativeDouble(
        JsonElement root,
        params string[] path)
    {
        var value = Path(root, path);
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
            || number < 0 || double.IsNaN(number) || double.IsInfinity(number))
            throw new InvalidDataException(
                "Assistant evaluation nullable measurement claim is invalid.");
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

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool Fixed(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(right.ToLowerInvariant()));
}
