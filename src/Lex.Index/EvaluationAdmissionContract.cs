using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lex.Index;

namespace Lex.Evaluation;

public sealed record EvaluationAdmissionRequest(
    string IdempotencyKey,
    string RequestBodySha256,
    string InvocationId,
    int Turn,
    int MaximumInputTokens,
    int MaximumOutputTokens,
    decimal MaximumCostEur);

public sealed record EvaluationAdmissionCapability(
    string Schema,
    string KeyId,
    string ReviewerId,
    string CandidateRevision,
    string CandidateImage,
    string CodeCommit,
    string ArtifactManifestSet,
    string CatalogSha256,
    string TargetEvidenceSha256,
    string CandidateModelEvidenceSha256,
    string GraderModelEvidenceSha256,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Nonce,
    int MaxCalls,
    long MaximumCandidateInputTokens,
    long MaximumCandidateOutputTokens,
    decimal MaximumCostEur,
    IReadOnlyList<EvaluationAdmissionRequest> AllowedRequests);

public sealed record EvaluationAdmissionIdentity(
    string CandidateRevision,
    string CandidateImage,
    string CodeCommit,
    string ArtifactManifestSet,
    string CatalogSha256);

public sealed record EvaluationAdmissionAuthority(
    string ReviewerId,
    string KeyId,
    string FingerprintSha256,
    string PublicKeyPem)
{
    internal ArtifactTrustRoot Root => new(KeyId, FingerprintSha256, PublicKeyPem);
}

/// <summary>Closed coherence rules for the published assistant relevance measurement.</summary>
public static class AssistantEvaluationRelevanceContract
{
    private static readonly HashSet<string> BilledCauses = new(StringComparer.Ordinal)
    {
        "grader_finish_reason_absent", "grader_finish_reason_invalid_type",
        "grader_finish_reason_unknown", "grader_finish_reason_length",
        "grader_finish_reason_content_filter", "grader_grade_malformed",
        "grader_response_malformed", "grader_no_content", "grader_no_score", "grader_no_reason",
        "grader_score_out_of_range", "grader_unknown_billed_failure",
    };

    private static readonly HashSet<string> UnbilledCauses = new(StringComparer.Ordinal)
    {
        "grader_not_configured", "grader_usage_absent", "grader_usage_invalid",
        "grader_prefix_over_input_ceiling", "grader_evidence_over_input_ceiling",
        "grader_prompt_over_input_ceiling", "grader_request_over_transport_bound",
        "grader_not_executed_target_failure", "grader_empty_response", "grader_malformed_json",
        "grader_http_rejected", "grader_transport_unavailable", "grader_timeout",
        "grader_unknown_failure",
    };

    public static bool IsKnownCause(string? cause) =>
        cause is not null && (BilledCauses.Contains(cause) || UnbilledCauses.Contains(cause));

    public static bool IsValidUsage(long inputTokens, long outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0) return false;
        try
        {
            _ = checked(inputTokens + outputTokens);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool IsCoherent(
        int? score, string? unavailableCause, long inputTokens, long outputTokens)
    {
        if (!IsValidUsage(inputTokens, outputTokens)) return false;
        var hasUsage = inputTokens != 0 || outputTokens != 0;
        return score is >= 1 and <= 5
            ? unavailableCause is null && hasUsage
            : score is null && unavailableCause is not null
                && (hasUsage
                    ? BilledCauses.Contains(unavailableCause)
                    : UnbilledCauses.Contains(unavailableCause));
    }
}

public static partial class EvaluationAdmissionContract
{
    public const string Schema = "lex-assistant-eval-admission/2";
    // 25 cases and 48 repetitions enumerate 56 request identities; doubling them for the
    // runner's single pre-authorized retry per repetition serializes to about 41 KiB, so the
    // 32 KiB cap rejected the very admission the retry design produces. The cap exists to
    // bound parsing, not to price identities: 64 KiB keeps that bound with headroom.
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumSignatureCharacters = 512;
    public const int MaximumRequests = 128;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(30);

    // Indented output writes newlines with Environment.NewLine unless told otherwise, so the same
    // admission serialised on Windows and on Linux differed by one byte per line, the carriage
    // return CRLF adds ahead of LF. The request body digests are taken over this exact
    // serialisation, so an admission signed on a Windows
    // workstation could never be verified by a Linux runner: publication refused with the plan
    // having drifted, when only the line separator had. A signed canonical form cannot depend on
    // the machine that produced it.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        NewLine = "\n",
    };

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex NoncePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*(?::[0-9]+)?/[A-Za-z0-9._/-]+(?:@sha256:[0-9a-f]{64}|:sha-[0-9a-f]{12,64})$", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePattern();

    public static byte[] Serialize(EvaluationAdmissionCapability capability)
    {
        ValidateStructure(capability);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(capability, Json);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException(
                "Evaluation admission capability exceeds its byte limit.");
        return bytes;
    }

    public static EvaluationAdmissionCapability Verify(
        ReadOnlySpan<byte> bytes,
        string signature,
        EvaluationAdmissionAuthority authority,
        EvaluationAdmissionIdentity expectedIdentity,
        DateTimeOffset now)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
            throw new InvalidDataException(
                "Evaluation admission capability is outside its byte limit.");
        if (string.IsNullOrWhiteSpace(signature)
            || signature.Length > MaximumSignatureCharacters)
            throw new CryptographicException(
                "Evaluation admission signature is outside its byte limit.");

        var capability = Parse(bytes);
        if (!Exact(capability.KeyId, authority.KeyId)
            || !Exact(capability.ReviewerId, authority.ReviewerId))
            throw new CryptographicException(
                "Evaluation admission signer is not authorized by this release.");
        ArtifactManifests.VerifySignature(
            bytes, signature, capability.KeyId, [authority.Root]);

        if (!Exact(capability.CandidateRevision, expectedIdentity.CandidateRevision)
            || !Exact(capability.CandidateImage, expectedIdentity.CandidateImage)
            || !Exact(capability.CodeCommit, expectedIdentity.CodeCommit)
            || !Exact(capability.ArtifactManifestSet, expectedIdentity.ArtifactManifestSet)
            || !Exact(capability.CatalogSha256, expectedIdentity.CatalogSha256))
            throw new InvalidDataException(
                "Evaluation admission does not match this exact candidate release.");
        var utcNow = now.ToUniversalTime();
        if (capability.IssuedAt.Offset != TimeSpan.Zero
            || capability.ExpiresAt.Offset != TimeSpan.Zero
            || capability.ExpiresAt <= capability.IssuedAt
            || capability.ExpiresAt - capability.IssuedAt > MaximumLifetime
            || capability.IssuedAt > utcNow.AddMinutes(2)
            || capability.ExpiresAt <= utcNow)
            throw new InvalidDataException(
                "Evaluation admission is expired or outside its short-lived window.");
        return capability;
    }

    public static EvaluationAdmissionCapability Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
            throw new InvalidDataException(
                "Evaluation admission capability is outside its byte limit.");
        EvaluationAdmissionCapability capability;
        try
        {
            capability = JsonSerializer.Deserialize<EvaluationAdmissionCapability>(bytes, Json)
                ?? throw new InvalidDataException(
                    "Evaluation admission capability is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Evaluation admission capability is malformed.", exception);
        }
        ValidateStructure(capability);
        return capability;
    }

    public static byte[] RequestBody(string message) =>
        JsonSerializer.SerializeToUtf8Bytes(new AskMessage(message), Json);

    public static string RequestBodySha256(string message) =>
        Convert.ToHexStringLower(SHA256.HashData(RequestBody(message)));

    public static string RunIdentity(EvaluationAdmissionCapability capability) =>
        RunIdentity(capability.Nonce);

    public static string RunIdentity(string nonce) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(nonce)))[..16];

    /// <summary>Requires the signed admission to name the exact live Azure evidence.</summary>
    public static void VerifyEvidenceIdentity(
        EvaluationAdmissionCapability capability,
        string targetEvidenceSha256,
        string candidateModelEvidenceSha256,
        string graderModelEvidenceSha256)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!Matches(DigestPattern(), targetEvidenceSha256)
            || !Matches(DigestPattern(), candidateModelEvidenceSha256)
            || !Matches(DigestPattern(), graderModelEvidenceSha256)
            || !Exact(capability.TargetEvidenceSha256, targetEvidenceSha256)
            || !Exact(capability.CandidateModelEvidenceSha256,
                candidateModelEvidenceSha256)
            || !Exact(capability.GraderModelEvidenceSha256,
                graderModelEvidenceSha256))
            throw new InvalidDataException(
                "Evaluation admission does not bind the exact Azure target and model evidence.");
    }

    private static void ValidateStructure(EvaluationAdmissionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (capability.Schema != Schema
            || !Matches(KeyPattern(), capability.KeyId)
            || string.IsNullOrWhiteSpace(capability.ReviewerId)
            || capability.ReviewerId.Length > 200
            || !Matches(RevisionPattern(), capability.CandidateRevision)
            || !Matches(ImagePattern(), capability.CandidateImage)
            || !Matches(CommitPattern(), capability.CodeCommit)
            || !Matches(DigestPattern(), capability.ArtifactManifestSet)
            || !Matches(DigestPattern(), capability.CatalogSha256)
            || !Matches(DigestPattern(), capability.TargetEvidenceSha256)
            || !Matches(DigestPattern(), capability.CandidateModelEvidenceSha256)
            || !Matches(DigestPattern(), capability.GraderModelEvidenceSha256)
            || !Matches(NoncePattern(), capability.Nonce)
            || capability.MaxCalls is < 1 or > MaximumRequests
            || capability.AllowedRequests is null
            || capability.AllowedRequests.Count != capability.MaxCalls
            || capability.MaximumCandidateInputTokens <= 0
            || capability.MaximumCandidateOutputTokens <= 0
            || capability.MaximumCostEur <= 0 || capability.MaximumCostEur > 100)
            throw new InvalidDataException(
                "Evaluation admission capability has an invalid release or budget contract.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        long input = 0;
        long output = 0;
        decimal cost = 0;
        foreach (var request in capability.AllowedRequests)
        {
            if (request is null
                || !Matches(KeyPattern(), request.IdempotencyKey)
                || !Matches(DigestPattern(), request.RequestBodySha256)
                || !Matches(KeyPattern(), request.InvocationId)
                || request.Turn is < 1 or > 9
                || request.MaximumInputTokens is < 1 or > 100_000
                || request.MaximumOutputTokens is < 1 or > 20_000
                || request.MaximumCostEur <= 0 || request.MaximumCostEur > 10
                || !keys.Add(request.IdempotencyKey))
                throw new InvalidDataException(
                    "Evaluation admission has an invalid or duplicate allowed request.");
            // Retry identities are authorized but not reserved: the token budget covers one
            // pass over the catalog, and a retry spends only when a repetition actually
            // retries. The cost ceiling still sums every authorized request.
            if (!request.InvocationId.EndsWith("-retry", StringComparison.Ordinal))
            {
                input = checked(input + request.MaximumInputTokens);
                output = checked(output + request.MaximumOutputTokens);
            }
            cost = checked(cost + request.MaximumCostEur);
        }
        if (input > capability.MaximumCandidateInputTokens
            || output > capability.MaximumCandidateOutputTokens
            || cost > capability.MaximumCostEur)
            throw new InvalidDataException(
                "Evaluation admission request reservations exceed its signed budget.");

        foreach (var invocation in capability.AllowedRequests
                     .GroupBy(request => request.InvocationId, StringComparer.Ordinal))
        {
            var turns = invocation.Select(request => request.Turn)
                .Order().ToArray();
            if (!turns.SequenceEqual(Enumerable.Range(1, turns.Length)))
                throw new InvalidDataException(
                    "Evaluation admission invocation turns must be unique and consecutive.");
        }
    }

    private static bool Matches(Regex pattern, string? value) =>
        value is not null && pattern.IsMatch(value);

    private static bool Exact(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(left)),
            SHA256.HashData(Encoding.UTF8.GetBytes(right)));

    private sealed record AskMessage(string Message);
}
