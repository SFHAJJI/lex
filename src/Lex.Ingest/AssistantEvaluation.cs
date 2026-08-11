using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lex.Index;

namespace Lex.Ingest;

public sealed record AssistantEvaluationSet(
    AssistantEvaluationCatalog Catalog,
    string Sha256,
    AssistantEvaluationReviewAttestation? Review,
    bool ReviewSignatureValid)
{
    public void EnsureReleaseReady()
    {
        if (Review is null || !ReviewSignatureValid
            || Review.Schema != "lex-assistant-eval-review/1"
            || Review.Decision != "approved"
            || !string.Equals(Review.CasesSha256, Sha256, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(Review.Reviewer)
            || string.IsNullOrWhiteSpace(Review.ReviewerId)
            || string.Equals(Review.ReviewerId, Catalog.AuthorId, StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(Review.ReviewedAt, out var reviewed)
            || reviewed.Offset != TimeSpan.Zero
            || !DateTimeOffset.TryParse(Catalog.FrozenAt, out var frozen)
            || reviewed < frozen
            || string.IsNullOrWhiteSpace(Review.Attestation)
            || Review.Attestation.Length > 2_000)
            throw new InvalidDataException(
                "Assistant evaluation cases require a dated, digest-bound independent approval.");
    }

    public AssistantEvaluationPreflight Preflight(
        AssistantEvaluationPricing pricing)
    {
        EnsureReleaseReady();
        if (pricing != Catalog.Pricing)
            throw new InvalidDataException(
                "Assistant evaluation pricing must match the independently reviewed catalog.");
        pricing.Validate();
        var candidateInput = Catalog.Cases.Sum(item =>
            checked((long)item.MaximumInputTokens * item.Repetitions));
        var candidateOutput = Catalog.Cases.Sum(item =>
            checked((long)item.MaximumOutputTokens * item.Repetitions));
        var graderInput = Catalog.Cases.Sum(item => item.Grading.Mode == "llm"
            ? checked((long)item.Grading.MaximumInputTokens * item.Repetitions) : 0);
        var graderOutput = Catalog.Cases.Sum(item => item.Grading.Mode == "llm"
            ? checked((long)item.Grading.MaximumOutputTokens * item.Repetitions) : 0);
        var candidateCost = pricing.CandidateCost(candidateInput, candidateOutput);
        var graderCost = pricing.GraderCost(graderInput, graderOutput);
        if (candidateInput > Catalog.Budget.MaximumCandidateInputTokens
            || candidateOutput > Catalog.Budget.MaximumCandidateOutputTokens)
            throw new InvalidDataException("Assistant candidate token preflight exceeds its budget.");
        if (graderInput > Catalog.Budget.MaximumGraderInputTokens
            || graderOutput > Catalog.Budget.MaximumGraderOutputTokens)
            throw new InvalidDataException("Assistant grader token preflight exceeds its budget.");
        if (candidateCost + graderCost > Catalog.Budget.MaximumCostEur)
            throw new InvalidDataException("Assistant evaluation estimated cost exceeds its EUR budget.");
        return new(candidateInput, candidateOutput, graderInput, graderOutput,
            candidateCost, graderCost, candidateCost + graderCost);
    }
}

public sealed record AssistantEvaluationCatalog(
    string Schema,
    string FrozenAt,
    string AuthoredBy,
    string AuthorId,
    AssistantEvaluationPricing Pricing,
    AssistantEvaluationBudget Budget,
    IReadOnlyList<AssistantEvaluationCase> Cases)
{
    private static readonly Regex Identifier = new(
        "^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Tools =
    [
        "search", "navigate", "as_of", "diff", "timeline", "article_history",
        "changes_in_period", "in_force_on", "coverage", "cited_by", "provenance",
        "legal_boundary",
    ];
    private static readonly HashSet<string> Outcomes =
    [
        "succeeded", "succeeded_empty", "needs_clarification", "not_available",
        "not_comparable", "not_found", "invalid_request", "legal_boundary",
    ];
    private static readonly HashSet<string> Effects =
    [
        "provision", "diff", "history", "timeline", "ranking", "in_force",
        "cited_by", "coverage", "workspace", "verification", "gap",
    ];

    public static AssistantEvaluationSet Load(
        string path,
        string? reviewPath = null,
        string? reviewSignaturePath = null) =>
        LoadCore(path, reviewPath, reviewSignaturePath,
            reviewPath is null ? null : EvaluationReviewTrustStore.Load());

    internal static AssistantEvaluationSet LoadForTest(
        string path,
        string? reviewPath,
        string? reviewSignaturePath,
        IReadOnlyList<ArtifactTrustRoot>? trustRoots,
        string? reviewerId) =>
        LoadCore(path, reviewPath, reviewSignaturePath,
            trustRoots is { Count: 1 } && reviewerId is not null
                ? new EvaluationReviewAuthority(reviewerId, trustRoots[0].KeyId,
                    trustRoots[0].FingerprintSha256, trustRoots[0].PublicKeyPem)
                : null);

    private static AssistantEvaluationSet LoadCore(
        string path,
        string? reviewPath,
        string? reviewSignaturePath,
        EvaluationReviewAuthority? authority)
    {
        var bytes = File.ReadAllBytes(path);
        AssistantEvaluationCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<AssistantEvaluationCatalog>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Assistant evaluation catalog is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Assistant evaluation catalog is malformed.", exception);
        }
        catalog.Validate();
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        AssistantEvaluationReviewAttestation? review = null;
        var reviewSignatureValid = false;
        if (reviewPath is not null)
        {
            if (reviewSignaturePath is null || authority is null)
                throw new InvalidDataException(
                    "Assistant evaluation review signature and trust roots are required together.");
            var reviewBytes = File.ReadAllBytes(reviewPath);
            if (reviewBytes.Length > 64 * 1024)
                throw new InvalidDataException("Assistant evaluation review exceeds its byte limit.");
            try
            {
                review = JsonSerializer.Deserialize<AssistantEvaluationReviewAttestation>(
                    reviewBytes, JsonOptions)
                    ?? throw new InvalidDataException("Assistant evaluation review is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Assistant evaluation review is malformed.", exception);
            }
            ArtifactManifests.VerifySignature(
                reviewBytes,
                File.ReadAllText(reviewSignaturePath),
                review.KeyId,
                [authority.Root]);
            if (!string.Equals(review.ReviewerId, authority.ReviewerId,
                    StringComparison.Ordinal))
                throw new CryptographicException(
                    "Assistant evaluation reviewer identity is not authorized by this release.");
            reviewSignatureValid = true;
        }
        else if (reviewSignaturePath is not null || authority is not null)
            throw new InvalidDataException(
                "Assistant evaluation review, signature and trust roots are required together.");
        return new(catalog, sha, review, reviewSignatureValid);
    }

    private void Validate()
    {
        if (Schema != "lex-assistant-eval/3")
            throw new InvalidDataException("Assistant evaluation schema must be lex-assistant-eval/3.");
        if (!DateTimeOffset.TryParse(FrozenAt, out var frozen) || frozen.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Assistant evaluation frozen_at must be an explicit UTC instant.");
        if (string.IsNullOrWhiteSpace(AuthoredBy) || AuthoredBy.Length > 200
            || string.IsNullOrWhiteSpace(AuthorId) || AuthorId.Length > 200)
            throw new InvalidDataException("Assistant evaluation authored_by is required and bounded.");
        if (Pricing is null || Budget is null || Cases is null)
            throw new InvalidDataException(
                "Assistant evaluation pricing, budget and cases are required.");
        Pricing.Validate();
        if (Budget.MaximumCandidateInputTokens is < 1 or > 1_000_000
            || Budget.MaximumCandidateOutputTokens is < 1 or > 100_000
            || Budget.MaximumGraderInputTokens is < 1 or > 1_000_000
            || Budget.MaximumGraderOutputTokens is < 1 or > 100_000
            || Budget.MaximumCostEur is <= 0 or > 10
            || Budget.MaximumFirstOperationP95LatencyMs is < 1_000 or > 25_000
            || Budget.MaximumFirstOperationHardLatencyMs
                < Budget.MaximumFirstOperationP95LatencyMs
            || Budget.MaximumFirstOperationHardLatencyMs > 25_000
            || Budget.MaximumSynthesisP95LatencyMs is < 1_000 or > 45_000
            || Budget.MaximumTransportQueueResidualP95LatencyMs is < 1 or > 1_500
            || Budget.MaximumTotalP99LatencyMs
                < Budget.MaximumFirstOperationHardLatencyMs
            || Budget.MaximumTotalP99LatencyMs > 90_000)
            throw new InvalidDataException("Assistant evaluation budget exceeds the release envelope.");
        if (Cases.Count is < 1 or > 20)
            throw new InvalidDataException("Assistant evaluation requires 1 to 20 cases.");
        if (!Cases.Any(item => item.ExpectedSynthesis == true)
            || !Cases.Any(item => item.ExpectedSynthesis == false))
            throw new InvalidDataException(
                "Assistant evaluation requires explicit synthesis and non-synthesis cases.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var questions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Cases)
        {
            if (!Identifier.IsMatch(item.Id) || !ids.Add(item.Id))
                throw new InvalidDataException($"Assistant evaluation case id '{item.Id}' is invalid or duplicated.");
            if (string.IsNullOrWhiteSpace(item.Question) || item.Question.Length > 1_000
                || !questions.Add(Normalize(item.Question)))
                throw new InvalidDataException($"Assistant evaluation case '{item.Id}' has an invalid or duplicate question.");
            if (item.History is { Count: > 8 }
                || item.History?.Any(message => message is null
                    || message.Role is not ("user" or "assistant")
                    || string.IsNullOrWhiteSpace(message.Content)
                    || message.Content.Length > 4_000) == true)
                throw new InvalidDataException(
                    $"Assistant evaluation case '{item.Id}' has invalid restored history.");
            if (item.Repetitions is < 1 or > 3
                || item.MaximumInputTokens is < 1 or > 100_000
                || item.MaximumOutputTokens is < 1 or > 20_000
                || item.MaximumLatencyMs is < 1_000 or > 90_000)
                throw new InvalidDataException($"Assistant evaluation case '{item.Id}' has invalid execution bounds.");
            if (item.ExpectedSynthesis is null)
                throw new InvalidDataException(
                    $"Assistant evaluation case '{item.Id}' must declare expected_synthesis.");
            if (item.Expected is null || !Tools.Contains(item.Expected.Tool)
                || !Outcomes.Contains(item.Expected.LegalOutcome)
                || !Effects.Contains(item.Expected.Effect)
                || item.Expected.TransportOutcome != "completed")
                throw new InvalidDataException($"Assistant evaluation case '{item.Id}' has an invalid expected contract.");
            if (item.Expected.Arguments is null || item.Expected.Arguments.Count > 12
                || item.Expected.Arguments.Any(argument =>
                    string.IsNullOrWhiteSpace(argument.Key) || argument.Key.Length > 64
                    || string.IsNullOrWhiteSpace(argument.Value) || argument.Value.Length > 1_000))
                throw new InvalidDataException($"Assistant evaluation case '{item.Id}' has invalid expected arguments.");
            if (item.Expected.PopulationMinimum is < 0)
                throw new InvalidDataException($"Assistant evaluation case '{item.Id}' has an invalid population bound.");
            if (item.Expected.PopulationMinimum is not null
                && (string.IsNullOrWhiteSpace(item.Expected.PopulationPath)
                    || item.Expected.PopulationPath.Length > 200
                    || !item.Expected.PopulationPath.StartsWith("/", StringComparison.Ordinal)))
                throw new InvalidDataException(
                    $"Assistant evaluation case '{item.Id}' requires a bounded JSON population path.");
            if (item.Expected.PopulationMinimum is null
                && item.Expected.PopulationPath is not null)
                throw new InvalidDataException(
                    $"Assistant evaluation case '{item.Id}' has a population path without a bound.");
            if (item.Expected.ForbiddenReplyContains is { Count: > 8 }
                || item.Expected.ForbiddenReplyContains?.Any(value =>
                    string.IsNullOrWhiteSpace(value) || value.Length > 100) == true)
                throw new InvalidDataException(
                    $"Assistant evaluation case '{item.Id}' has invalid forbidden reply markers.");
            if (item.Grading is null
                || item.Grading.Mode is not ("deterministic" or "llm")
                || item.Grading.Threshold is < 1 or > 5
                || item.Grading.Mode == "deterministic" && item.Grading.Threshold != 5
                || item.Grading.Mode == "llm" && string.IsNullOrWhiteSpace(item.Grading.Rubric)
                || item.Grading.Mode == "llm" && item.Grading.MaximumInputTokens < 4_096
                || item.Grading.Rubric?.Length > 4_000
                || item.Grading.MaximumInputTokens is < 1 or > 100_000
                || item.Grading.MaximumOutputTokens is < 1 or > 20_000)
                throw new InvalidDataException($"Assistant evaluation case '{item.Id}' has an invalid grading contract.");
        }
    }

    private static string Normalize(string value) => string.Join(' ',
        value.Trim().ToLowerInvariant().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

public sealed record AssistantEvaluationReviewAttestation(
    string Schema,
    string KeyId,
    string CasesSha256,
    string Reviewer,
    string ReviewerId,
    string ReviewedAt,
    string Decision,
    string Attestation);

public sealed record AssistantEvaluationBudget(
    long MaximumCandidateInputTokens,
    long MaximumCandidateOutputTokens,
    long MaximumGraderInputTokens,
    long MaximumGraderOutputTokens,
    decimal MaximumCostEur,
    double MaximumFirstOperationP95LatencyMs,
    double MaximumFirstOperationHardLatencyMs,
    double MaximumSynthesisP95LatencyMs,
    double MaximumTransportQueueResidualP95LatencyMs,
    double MaximumTotalP99LatencyMs);

public sealed record AssistantEvaluationCase(
    string Id,
    string Question,
    int Repetitions,
    int MaximumInputTokens,
    int MaximumOutputTokens,
    double MaximumLatencyMs,
    bool? ExpectedSynthesis,
    AssistantEvaluationExpected Expected,
    AssistantEvaluationGrading Grading,
    IReadOnlyList<AssistantEvaluationMessage>? History = null);

public sealed record AssistantEvaluationMessage(string Role, string Content);

public sealed record AssistantEvaluationExpected(
    string Tool,
    string LegalOutcome,
    string TransportOutcome,
    string Effect,
    IReadOnlyDictionary<string, string> Arguments,
    string? GapStatus = null,
    bool? Clarification = null,
    int? PopulationMinimum = null,
    string? PopulationPath = null,
    IReadOnlyList<string>? ForbiddenReplyContains = null);

public sealed record AssistantEvaluationGrading(
    string Mode,
    int Threshold,
    int MaximumInputTokens,
    int MaximumOutputTokens,
    string? Rubric = null);

public sealed record AssistantEvaluationPreflight(
    long ReservedCandidateInputTokens,
    long ReservedCandidateOutputTokens,
    long ReservedGraderInputTokens,
    long ReservedGraderOutputTokens,
    decimal EstimatedCandidateCostEur,
    decimal EstimatedGraderCostEur,
    decimal EstimatedTotalCostEur);

public sealed record AssistantEvaluationMeterPrice(
    string MeterId,
    string MeterName,
    string EffectiveStartDate,
    decimal EurosPerMillion)
{
    internal void Validate(string role)
    {
        if (string.IsNullOrWhiteSpace(MeterId) || MeterId.Length > 100
            || string.IsNullOrWhiteSpace(MeterName) || MeterName.Length > 200
            || !DateTimeOffset.TryParse(EffectiveStartDate, out var effective)
            || effective.Offset != TimeSpan.Zero
            || EurosPerMillion <= 0 || EurosPerMillion > 1_000)
            throw new InvalidDataException($"Assistant {role} price evidence is invalid.");
    }
}

public sealed record AssistantEvaluationModelPricing(
    string ModelName,
    string ModelVersion,
    string Sku,
    AssistantEvaluationMeterPrice Input,
    AssistantEvaluationMeterPrice Output)
{
    internal void Validate(string role)
    {
        if (string.IsNullOrWhiteSpace(ModelName) || ModelName.Length > 100
            || string.IsNullOrWhiteSpace(ModelVersion) || ModelVersion.Length > 100
            || Sku != "GlobalStandard" || Input is null || Output is null)
            throw new InvalidDataException($"Assistant {role} model pricing is invalid.");
        Input.Validate($"{role} input");
        Output.Validate($"{role} output");
    }
}

public sealed record AssistantEvaluationPricing(
    string Schema,
    string Currency,
    string SourceUri,
    string RetrievedAt,
    string ValidUntil,
    AssistantEvaluationModelPricing Candidate,
    AssistantEvaluationModelPricing Grader)
{
    public decimal CandidateInputEurosPerMillion => Candidate.Input.EurosPerMillion;
    public decimal CandidateOutputEurosPerMillion => Candidate.Output.EurosPerMillion;
    public decimal GraderInputEurosPerMillion => Grader.Input.EurosPerMillion;
    public decimal GraderOutputEurosPerMillion => Grader.Output.EurosPerMillion;

    public void Validate()
    {
        if (Schema != "lex-assistant-eval-pricing/1" || Currency != "EUR"
            || SourceUri != "https://prices.azure.com/api/retail/prices"
            || !DateTimeOffset.TryParse(RetrievedAt, out var retrieved)
            || retrieved.Offset != TimeSpan.Zero
            || !DateTimeOffset.TryParse(ValidUntil, out var validUntil)
            || validUntil.Offset != TimeSpan.Zero
            || validUntil <= retrieved
            || validUntil - retrieved > TimeSpan.FromDays(7)
            || Candidate is null || Grader is null)
            throw new InvalidDataException(
                "Assistant evaluation pricing must be a current reviewed EUR Retail Prices snapshot.");
        Candidate.Validate("candidate");
        Grader.Validate("grader");
    }

    public void ValidateFor(AssistantEvaluationIdentity identity, DateTimeOffset runAt)
    {
        Validate();
        var retrieved = DateTimeOffset.Parse(RetrievedAt);
        var validUntil = DateTimeOffset.Parse(ValidUntil);
        if (runAt.Offset != TimeSpan.Zero || runAt < retrieved || runAt > validUntil
            || Candidate.ModelName != identity.CandidateModel.ModelName
            || Candidate.ModelVersion != identity.CandidateModel.ModelVersion
            || Candidate.Sku != identity.CandidateModel.Sku
            || Grader.ModelName != identity.GraderModel.ModelName
            || Grader.ModelVersion != identity.GraderModel.ModelVersion
            || Grader.Sku != identity.GraderModel.Sku)
            throw new InvalidDataException(
                "Assistant evaluation pricing does not bind the evaluated model releases and run time.");
    }

    public decimal CandidateCost(long input, long output) =>
        input * CandidateInputEurosPerMillion / 1_000_000m
        + output * CandidateOutputEurosPerMillion / 1_000_000m;

    public decimal GraderCost(long input, long output) =>
        input * GraderInputEurosPerMillion / 1_000_000m
        + output * GraderOutputEurosPerMillion / 1_000_000m;
}
