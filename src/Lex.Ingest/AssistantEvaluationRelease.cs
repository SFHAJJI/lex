using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lex.Evaluation;
using Lex.Index;

namespace Lex.Ingest;

/// <summary>
/// Revalidates signed assistant-evaluation evidence at the traffic boundary. The evaluator
/// produces the report; this verifier deliberately recomputes its closed, deterministic
/// invariants instead of trusting the report's activation boolean.
/// </summary>
public static class AssistantEvaluationReleaseVerifier
{
    public const string ReportFile = "assistant-eval-report.json";
    public const string CasesFile = "assistant-cases-v3.json";
    public const string ReviewFile = "assistant-cases-v3.review.json";
    public const string ReviewSignatureFile = "assistant-cases-v3.review.sig";
    public const string AdmissionFile = "assistant-eval-admission.json";
    public const string AdmissionSignatureFile = "assistant-eval-admission.sig";
    public const string BrowserEvidenceFile = "assistant-browser-evidence.json";
    public const string ArtifactKeyId = "keyvault-lex-v2";
    public static readonly IReadOnlyList<string> RequiredFiles =
        [ReportFile, CasesFile, ReviewFile, ReviewSignatureFile,
            AdmissionFile, AdmissionSignatureFile, BrowserEvidenceFile];
    private static readonly Regex RunIdentity = new(
        "^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
    private static readonly Regex Digest = new(
        "^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex RelevanceCause = new(
        "^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static ArtifactManifest VerifyArtifactSet(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        IReadOnlyList<ArtifactTrustRoot> trustRoots,
        AssistantCandidateRuntimeEvidence expectedTarget,
        AssistantEvaluationReport report,
        AssistantBrowserEvaluationEvidence browserEvidence)
    {
        var verified = ArtifactManifests.VerifyDirectory(
            rootDirectory, manifestPath, signaturePath, trustRoots);
        if (verified.Count != RequiredFiles.Count
            || RequiredFiles.Any(path => !verified.Contains(path)))
            throw new InvalidDataException(
                "Signed assistant evaluation artifact set is incomplete or contains unexpected files.");
        var manifest = ArtifactManifests.Parse(File.ReadAllBytes(manifestPath));
        var expectedSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["artifact_manifest_set"] = expectedTarget.ArtifactManifestSet,
            ["admission_run_identity"] = report.AdmissionRunIdentity,
            ["admission_sha256"] = report.AdmissionSha256,
            ["browser_evidence_sha256"] = Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(rootDirectory, BrowserEvidenceFile)))),
            ["candidate_evidence_sha256"] = expectedTarget.EvidenceSha256,
            ["candidate_revision"] = expectedTarget.RevisionName,
            ["cases_sha256"] = report.CasesSha256,
            ["purpose"] = "assistant-evaluation",
            ["report_schema"] = AssistantEvaluationRunner.ReportSchema,
        };
        if (manifest.KeyId != ArtifactKeyId
            || manifest.CodeCommit != expectedTarget.CodeCommit
            || manifest.Sources.Count != expectedSources.Count
            || expectedSources.Any(item => !manifest.Sources.TryGetValue(item.Key, out var value)
                || value != item.Value))
            throw new InvalidDataException(
                "Signed assistant evaluation manifest does not bind the candidate and report.");
        return manifest;
    }

    public static AssistantBrowserEvaluationEvidence VerifyBrowserEvidence(
        string path,
        AssistantCandidateRuntimeEvidence expectedTarget,
        AssistantEvaluationReport report,
        DateTimeOffset verifiedAt)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length is 0 or > 256 * 1024)
            throw new InvalidDataException(
                "Assistant browser evidence exceeds its byte limit.");
        AssistantBrowserEvaluationEvidence evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<AssistantBrowserEvaluationEvidence>(bytes, Json)
                ?? throw new InvalidDataException("Assistant browser evidence is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Assistant browser evidence is malformed.", exception);
        }
        var expectedBaseUrl = $"https://{expectedTarget.RevisionFqdn}";
        if (evidence.Schema != "lex-assistant-browser-evidence/1"
            || !DateTimeOffset.TryParse(evidence.RunAt, out var runAt)
            || runAt.Offset != TimeSpan.Zero
            || runAt < DateTimeOffset.Parse(report.RunAt).AddMinutes(-5)
            || runAt > verifiedAt.AddMinutes(5)
            || !string.Equals(evidence.BaseUrl, expectedBaseUrl,
                StringComparison.OrdinalIgnoreCase)
            || evidence.RevisionName != expectedTarget.RevisionName
            || evidence.CodeCommit != expectedTarget.CodeCommit
            || evidence.ArtifactManifestSet != expectedTarget.ArtifactManifestSet
            || evidence.CandidateEvidenceSha256 != expectedTarget.EvidenceSha256
            || evidence.BrowserName != "chromium"
            || string.IsNullOrWhiteSpace(evidence.BrowserVersion)
            || evidence.BrowserVersion.Length > 100
            || evidence.ViewportWidth != 1440 || evidence.ViewportHeight != 900
            || evidence.Metric != "operation_result_received_to_presented_ms"
            || evidence.MaximumP95Milliseconds != 500
            || evidence.SamplesMilliseconds is null
            || evidence.SamplesMilliseconds.Count is < 5 or > 20
            || evidence.SamplesMilliseconds.Any(value => value < 0
                || double.IsNaN(value) || double.IsInfinity(value))
            || evidence.Latency is null
            || evidence.Latency != BrowserLatency(evidence.SamplesMilliseconds)
            || evidence.Latency.P95Milliseconds > evidence.MaximumP95Milliseconds
            || !evidence.Passed)
            throw new InvalidDataException(
                "Assistant browser presentation evidence is invalid or does not bind the candidate.");
        return evidence;
    }

    private static AssistantEvaluationLatency BrowserLatency(
        IReadOnlyList<double> samples)
    {
        var ordered = samples.Order().ToArray();
        double At(double quantile) => ordered[Math.Clamp(
            (int)Math.Ceiling(quantile * ordered.Length) - 1, 0, ordered.Length - 1)];
        return new(At(0.50), At(0.95), At(0.99));
    }

    public static AssistantEvaluationReport VerifyReport(
        string reportPath,
        string admissionPath,
        string admissionSignaturePath,
        AssistantEvaluationSet caseSet,
        AssistantCandidateRuntimeEvidence expectedTarget,
        AssistantTargetAttestation targetAttestation,
        DateTimeOffset verifiedAt,
        bool allowOlderPreviouslyPromotedEvidence = false) => VerifyReportCore(
            reportPath, admissionPath, admissionSignaturePath, caseSet,
            expectedTarget, targetAttestation, verifiedAt,
            AdmissionAuthority(), allowOlderPreviouslyPromotedEvidence);

    internal static AssistantEvaluationReport VerifyReportForTest(
        string reportPath,
        string admissionPath,
        string admissionSignaturePath,
        AssistantEvaluationSet caseSet,
        AssistantCandidateRuntimeEvidence expectedTarget,
        AssistantTargetAttestation targetAttestation,
        DateTimeOffset verifiedAt,
        EvaluationAdmissionAuthority admissionAuthority,
        bool allowOlderPreviouslyPromotedEvidence = false) => VerifyReportCore(
            reportPath, admissionPath, admissionSignaturePath, caseSet,
            expectedTarget, targetAttestation, verifiedAt, admissionAuthority,
            allowOlderPreviouslyPromotedEvidence);

    private static AssistantEvaluationReport VerifyReportCore(
        string reportPath,
        string admissionPath,
        string admissionSignaturePath,
        AssistantEvaluationSet caseSet,
        AssistantCandidateRuntimeEvidence expectedTarget,
        AssistantTargetAttestation targetAttestation,
        DateTimeOffset verifiedAt,
        EvaluationAdmissionAuthority admissionAuthority,
        bool allowOlderPreviouslyPromotedEvidence)
    {
        ArgumentNullException.ThrowIfNull(caseSet);
        ArgumentNullException.ThrowIfNull(expectedTarget);
        ArgumentNullException.ThrowIfNull(targetAttestation);
        caseSet.EnsureReleaseReady();
        var bytes = File.ReadAllBytes(reportPath);
        if (bytes.Length is 0 or > 4 * 1024 * 1024)
            throw new InvalidDataException("Assistant evaluation report exceeds its byte limit.");
        AssistantEvaluationReport report;
        try
        {
            report = JsonSerializer.Deserialize<AssistantEvaluationReport>(bytes, Json)
                ?? throw new InvalidDataException("Assistant evaluation report is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Assistant evaluation report is malformed.", exception);
        }

        if (report.Schema != AssistantEvaluationRunner.ReportSchema
            || !FixedEquals(report.CasesSha256, caseSet.Sha256)
            || report.FrozenAt != caseSet.Catalog.FrozenAt
            || !DateTimeOffset.TryParse(report.RunAt, out var runAt)
            || runAt.Offset != TimeSpan.Zero
            || !RunIdentity.IsMatch(report.AdmissionRunIdentity ?? "")
            || !Digest.IsMatch(report.AdmissionSha256 ?? "")
            || runAt > verifiedAt.AddMinutes(5)
            || !allowOlderPreviouslyPromotedEvidence
                && verifiedAt - runAt > TimeSpan.FromDays(7))
            throw new InvalidDataException("Assistant evaluation report identity or freshness is invalid.");
        // Target is checked for null explicitly: the annotation is not enforced by the deserializer,
        // and this call would raise a null reference rather than the typed refusal a tampered report
        // must produce. Reported in review.
        if (report.Identity is null || report.Pricing is null || report.Preflight is null
            || report.Identity.Target is null
            || !report.Identity.Target.DescribesSameCandidateAs(expectedTarget)
            || report.Pricing != caseSet.Catalog.Pricing
            || !SameSet(report.Identity.IndexManifestIds, targetAttestation.IndexManifestIds))
            throw new InvalidDataException(
                "Assistant evaluation report does not describe the candidate being promoted.");
        AssistantEvaluationRunner.ValidateIdentity(report.Identity);
        report.Pricing.ValidateFor(report.Identity, runAt);
        var expectedPreflight = caseSet.Preflight(caseSet.Catalog.Pricing);
        if (report.Preflight != expectedPreflight)
            throw new InvalidDataException(
                "Assistant evaluation preflight or model pricing evidence is invalid.");
        VerifyAdmission(admissionPath, admissionSignaturePath, caseSet,
            expectedTarget, report, runAt, admissionAuthority);

        var expectedResults = caseSet.Catalog.Cases.Sum(item => item.Repetitions);
        if (report.Results is null || report.Results.Count != expectedResults
            || report.GateFailures is null || report.GateFailures.Count != 0
            || !report.ActivationGatePassed || report.Results.Any(item => !item.Passed
                || item.Failures is null || item.Failures.Count != 0))
            throw new InvalidDataException("Assistant evaluation report did not pass every release case.");

        foreach (var evaluationCase in caseSet.Catalog.Cases)
        {
            var results = report.Results.Where(item => item.CaseId == evaluationCase.Id)
                .OrderBy(item => item.Repetition).ToArray();
            if (results.Length != evaluationCase.Repetitions
                || !results.Select(item => item.Repetition)
                    .SequenceEqual(Enumerable.Range(1, evaluationCase.Repetitions))
                || results.Any(item => !FixedEquals(item.PromptSha256,
                        AssistantEvaluationRunner.PromptSha256(evaluationCase))
                    || item.GradingMode != evaluationCase.Grading.Mode
                    // A deterministic turn that called no model reports zero, so a repetition may
                    // read zero here, but only as an all-zero reading: no input beside real output
                    // is a model call understating the axis the candidate input budget is enforced
                    // on. The run-wide sum below is where a report claiming no spend at all fails.
                    || item.CandidateUsage.InputTokens < 0
                    || item.CandidateUsage.OutputTokens < 0
                    || (item.CandidateUsage.InputTokens == 0)
                        != (item.CandidateUsage.OutputTokens == 0)
                    // Relevance reports, so no score is refused here for being low. What is
                    // refused is an incoherent measurement: a judged case that is silent about
                    // both the score and why it has none, a score that arrived with a reason it
                    // is missing, a score outside the scale the grader is constrained to, or a
                    // cause that is free text rather than the machine token this published list
                    // is allowed to carry.
                    || evaluationCase.Grading.Mode == "llm"
                        && !CoherentRelevance(item.Relevance)
                    || evaluationCase.Grading.Mode == "deterministic"
                        && (item.Relevance is null
                            || item.Relevance.Score is not null
                            || item.Relevance.UnavailableCause is not null
                            || item.GraderUsage.InputTokens != 0
                            || item.GraderUsage.OutputTokens != 0)
                    || !ValidTimings(item.Timings)
                    || evaluationCase.ExpectedSynthesis == true
                        && item.Timings.SynthesisMilliseconds is null
                    || evaluationCase.ExpectedSynthesis == false
                        && item.Timings.SynthesisMilliseconds is not null
                    || item.Timings.PlannerMilliseconds
                        > AssistantEvaluationRunner.MaximumPlannerCallMilliseconds
                    || item.Timings.SubmitToFirstOperationResultMilliseconds
                        > caseSet.Catalog.Budget.MaximumFirstOperationHardLatencyMs
                    || item.Timings.TotalMilliseconds > evaluationCase.MaximumLatencyMs))
                throw new InvalidDataException(
                    $"Assistant evaluation evidence for case '{evaluationCase.Id}' is invalid.");
        }

        var candidateUsage = Sum(report.Results.Select(item => item.CandidateUsage));
        var graderUsage = Sum(report.Results.Select(item => item.GraderUsage));
        if (candidateUsage != report.ActualCandidateUsage
            || graderUsage != report.ActualGraderUsage
            // Recomputed from the per-result usages just above, so a report cannot declare spend it
            // did not measure: a run whose every repetition read zero fails here, which is where
            // the per-result check used to stop it before deterministic turns could report honestly.
            || candidateUsage.InputTokens <= 0
            || candidateUsage.OutputTokens <= 0
            || candidateUsage.InputTokens > caseSet.Catalog.Budget.MaximumCandidateInputTokens
            || candidateUsage.OutputTokens > caseSet.Catalog.Budget.MaximumCandidateOutputTokens
            || graderUsage.InputTokens > caseSet.Catalog.Budget.MaximumGraderInputTokens
            || graderUsage.OutputTokens > caseSet.Catalog.Budget.MaximumGraderOutputTokens
            || report.ActualCandidateCostEur != report.Pricing.CandidateCost(
                candidateUsage.InputTokens, candidateUsage.OutputTokens)
            || report.ActualGraderCostEur != report.Pricing.GraderCost(
                graderUsage.InputTokens, graderUsage.OutputTokens)
            || report.ActualCandidateCostEur + report.ActualGraderCostEur
                != report.ActualTotalCostEur
            || report.ActualTotalCostEur > caseSet.Catalog.Budget.MaximumCostEur)
            throw new InvalidDataException("Assistant evaluation usage or cost evidence is invalid.");

        var expectedLatency = AssistantEvaluationRunner.LatencyOf(
            report.Results.Select(item => item.Timings));
        if (report.Latency != expectedLatency
            || report.Latency.SubmitToFirstOperationResult.P95Milliseconds
                > caseSet.Catalog.Budget.MaximumFirstOperationP95LatencyMs
            || report.Latency.Synthesis.P95Milliseconds
                > caseSet.Catalog.Budget.MaximumSynthesisP95LatencyMs
            || report.Latency.TransportQueueResidual.P95Milliseconds
                > caseSet.Catalog.Budget.MaximumTransportQueueResidualP95LatencyMs
            || report.Latency.Total.P99Milliseconds
                > caseSet.Catalog.Budget.MaximumTotalP99LatencyMs)
            throw new InvalidDataException("Assistant evaluation latency evidence is invalid.");
        return report;
    }

    /// <summary>A judged repetition carries a score, or the machine token for why it has none.</summary>
    private static bool CoherentRelevance(AssistantEvaluationRelevance? relevance) =>
        relevance is not null
        && (relevance.Score is null) != (relevance.UnavailableCause is null)
        && relevance.Score is null or (>= 1 and <= 5)
        && (relevance.UnavailableCause is null
            || RelevanceCause.IsMatch(relevance.UnavailableCause));

    private static void VerifyAdmission(
        string admissionPath,
        string admissionSignaturePath,
        AssistantEvaluationSet caseSet,
        AssistantCandidateRuntimeEvidence expectedTarget,
        AssistantEvaluationReport report,
        DateTimeOffset runAt,
        EvaluationAdmissionAuthority authority)
    {
        var bytes = EvalAdmissionCli.ReadBounded(
            admissionPath, EvaluationAdmissionContract.MaximumBytes);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!FixedEquals(report.AdmissionSha256, sha256))
            throw new InvalidDataException(
                "Assistant evaluation report does not bind the signed admission bytes.");
        var identity = new EvaluationAdmissionIdentity(
            expectedTarget.RevisionName,
            expectedTarget.Image,
            expectedTarget.CodeCommit,
            expectedTarget.ArtifactManifestSet,
            caseSet.Sha256);
        var capability = EvaluationAdmissionContract.Verify(
            bytes, EvalAdmissionCli.ReadBoundedSignature(admissionSignaturePath),
            authority, identity, runAt);
        var expected = EvalAdmissionCli.Create(
            caseSet, authority, identity, capability.IssuedAt, capability.Nonce);
        var expectedBytes = EvaluationAdmissionContract.Serialize(expected);
        var runIdentity = EvaluationAdmissionContract.RunIdentity(capability);
        if (bytes.Length != expectedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(bytes, expectedBytes)
            || !FixedEquals(report.AdmissionRunIdentity, runIdentity))
            throw new InvalidDataException(
                "Signed evaluation admission drifted from the reviewed request plan. "
                + DescribeAdmissionDrift(bytes, expectedBytes,
                    report.AdmissionRunIdentity, runIdentity));
    }

    /// <summary>
    /// Names what differs between the signed admission and the plan re-derived from the reviewed
    /// catalog, so a refusal can be acted on instead of reconstructed.
    /// </summary>
    /// <remarks>
    /// The refusal used to report only that the two disagreed. Diagnosing one instance meant
    /// redeploying and re-running a 56-call evaluation to add a print statement, because the
    /// verifier is compiled from the evaluated commit. Only the top-level member and, for the
    /// request plan, the first differing request index are named: enough to act on, and nothing that
    /// is not already in evidence the reader holds.
    /// </remarks>
    private static string DescribeAdmissionDrift(
        ReadOnlySpan<byte> signed,
        ReadOnlySpan<byte> derived,
        string? signedRunIdentity,
        string derivedRunIdentity)
    {
        if (!FixedEquals(signedRunIdentity, derivedRunIdentity))
            return $"The report names run identity {Bounded(signedRunIdentity)} and the signed "
                + $"admission derives {Bounded(derivedRunIdentity)}.";
        JsonObject? left, right;
        try
        {
            left = JsonNode.Parse(signed.ToArray()) as JsonObject;
            right = JsonNode.Parse(derived.ToArray()) as JsonObject;
        }
        catch (JsonException)
        {
            return $"Signed admission is {signed.Length} bytes and the re-derived plan is "
                + $"{derived.Length} bytes; neither could be compared as JSON.";
        }
        if (left is null || right is null)
            return "One of the two admissions is not a JSON object.";
        var differing = left
            .Where(member => !JsonNode.DeepEquals(member.Value, right[member.Key]))
            .Select(member => member.Key)
            .Concat(right.Select(member => member.Key).Where(key => !left.ContainsKey(key)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (differing.Length == 0)
            return $"Members compare equal, so the difference is in encoding: signed is "
                + $"{signed.Length} bytes and re-derived is {derived.Length} bytes.";
        var detail = string.Join(", ", differing);
        if (differing.Contains("requests", StringComparer.Ordinal)
            && left["requests"] is JsonArray signedRequests
            && right["requests"] is JsonArray derivedRequests)
        {
            if (signedRequests.Count != derivedRequests.Count)
                return $"Differing members: {detail}. The signed plan has {signedRequests.Count} "
                    + $"requests and the reviewed catalog derives {derivedRequests.Count}.";
            for (var index = 0; index < signedRequests.Count; index++)
                if (!JsonNode.DeepEquals(signedRequests[index], derivedRequests[index]))
                    return $"Differing members: {detail}. First differing request is index "
                        + $"{index} of {signedRequests.Count}.";
        }
        return $"Differing members: {detail}.";
    }

    private static string Bounded(string? value) =>
        string.IsNullOrEmpty(value) ? "nothing"
            : value.Length <= 32 ? value : value[..32] + "...";

    private static EvaluationAdmissionAuthority AdmissionAuthority()
    {
        var authority = EvaluationReviewTrustStore.Load();
        return new EvaluationAdmissionAuthority(
            authority.ReviewerId, authority.KeyId,
            authority.FingerprintSha256, authority.PublicKeyPem);
    }

    private static bool ValidTimings(AssistantEvaluationTimings? timings) =>
        timings is not null
        && timings.PlannerMilliseconds >= 0
        && timings.McpMilliseconds >= 0
        && timings.TransportQueueResidualMilliseconds >= 0
        && timings.SubmitToFirstOperationResultMilliseconds >= 0
        && timings.SynthesisMilliseconds is null or >= 0
        && timings.TotalMilliseconds >= timings.SubmitToFirstOperationResultMilliseconds
        && timings.TotalMilliseconds >= timings.PlannerMilliseconds
        && timings.TotalMilliseconds >= timings.McpMilliseconds
        && timings.SubmitToFirstOperationResultMilliseconds
            >= timings.TransportQueueResidualMilliseconds
        && (timings.SynthesisMilliseconds is null
            || timings.TotalMilliseconds >= timings.SynthesisMilliseconds);

    public static async Task VerifyModelDeploymentsAsync(
        AzureModelDeploymentResolver resolver,
        AssistantEvaluationReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(report);
        var candidate = await resolver.ResolveAsync(
            report.Identity.CandidateModel.ResourceId,
            report.Identity.CandidateModel.Deployment,
            cancellationToken);
        var grader = await resolver.ResolveAsync(
            report.Identity.GraderModel.ResourceId,
            report.Identity.GraderModel.Deployment,
            cancellationToken);
        if (candidate != report.Identity.CandidateModel
            || grader != report.Identity.GraderModel)
            throw new InvalidDataException(
                "Assistant evaluation model evidence no longer matches Azure.");
    }

    private static AssistantModelUsage Sum(IEnumerable<AssistantModelUsage> usages)
    {
        var total = new AssistantModelUsage(0, 0);
        foreach (var usage in usages)
            total = total.Add(usage);
        return total;
    }

    private static bool SameSet(IReadOnlyList<string>? first, IReadOnlyList<string>? second) =>
        first is not null && second is not null
        && first.Count == second.Count
        && first.Order(StringComparer.Ordinal).SequenceEqual(
            second.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool FixedEquals(string? first, string? second)
    {
        if (first is null || second is null || first.Length != second.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
    }
}

public sealed record AssistantBrowserEvaluationEvidence(
    string Schema,
    string RunAt,
    string BaseUrl,
    string RevisionName,
    string CodeCommit,
    string ArtifactManifestSet,
    string CandidateEvidenceSha256,
    string BrowserName,
    string BrowserVersion,
    int ViewportWidth,
    int ViewportHeight,
    string Metric,
    IReadOnlyList<double> SamplesMilliseconds,
    AssistantEvaluationLatency Latency,
    double MaximumP95Milliseconds,
    bool Passed);
