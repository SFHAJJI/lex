using System.Security.Cryptography;
using System.Text.Json;
using Lex.Evaluation;

namespace Lex.Ingest;

public static class EvalAdmissionCli
{
    public static bool IsCommand(string? value) =>
        value is "create-admission" or "verify-admission";

    public static int Run(
        string[] args,
        DateTimeOffset now)
    {
        if (args.Length == 0 || !IsCommand(args[0]))
            throw new ArgumentException(
                "Evaluation admission command must be create-admission or verify-admission.");
        string Required(string name)
        {
            var indexes = args.Select((value, index) => (value, index))
                .Where(item => item.value == name).Select(item => item.index).ToArray();
            if (indexes.Length != 1 || indexes[0] + 1 >= args.Length
                || args[indexes[0] + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{name} required exactly once");
            return args[indexes[0] + 1];
        }

        var cases = Required("--cases");
        var review = Required("--review-attestation");
        var reviewSignature = Required("--review-signature");
        var caseSet = AssistantEvaluationCatalog.Load(
            cases, review, reviewSignature);
        var identity = new EvaluationAdmissionIdentity(
            Required("--candidate-revision"),
            Required("--candidate-image"),
            Required("--code-commit"),
            Required("--artifact-manifest-set"),
            caseSet.Sha256);
        var authority = EvaluationReviewTrustStore.Load();
        var admissionAuthority = new EvaluationAdmissionAuthority(
            authority.ReviewerId, authority.KeyId,
            authority.FingerprintSha256, authority.PublicKeyPem);

        if (args[0] == "create-admission")
        {
            var output = Path.GetFullPath(Required("--out"));
            var capability = Create(
                caseSet, admissionAuthority, identity, now, NewNonce());
            AtomicWrite(output, EvaluationAdmissionContract.Serialize(capability));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "created",
                admission = output,
                expires_at = capability.ExpiresAt,
                max_calls = capability.MaxCalls,
                catalog_sha256 = capability.CatalogSha256,
            }));
            return 0;
        }

        var admissionPath = Path.GetFullPath(Required("--admission"));
        var signaturePath = Path.GetFullPath(Required("--signature"));
        var bytes = ReadBounded(admissionPath, EvaluationAdmissionContract.MaximumBytes);
        var signature = File.ReadAllText(signaturePath);
        if (signature.Length > EvaluationAdmissionContract.MaximumSignatureCharacters + 2)
            throw new InvalidDataException(
                "Evaluation admission signature exceeds its byte limit.");
        var verified = EvaluationAdmissionContract.Verify(
            bytes, signature.Trim(), admissionAuthority, identity, now);
        caseSet.EnsureReleaseReady();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "passed",
            revision = verified.CandidateRevision,
            expires_at = verified.ExpiresAt,
            max_calls = verified.MaxCalls,
            catalog_sha256 = verified.CatalogSha256,
        }));
        return 0;
    }

    public static EvaluationAdmissionCapability Create(
        AssistantEvaluationSet caseSet,
        EvaluationAdmissionAuthority authority,
        EvaluationAdmissionIdentity identity,
        DateTimeOffset issuedAt,
        string nonce)
    {
        ArgumentNullException.ThrowIfNull(caseSet);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(identity);
        caseSet.EnsureReleaseReady();
        var preflight = caseSet.Preflight(caseSet.Catalog.Pricing);
        if (!string.Equals(identity.CatalogSha256, caseSet.Sha256,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Evaluation admission identity does not bind the reviewed catalog.");
        var planned = AssistantEvaluationRequestPlan.Build(
            caseSet.Catalog, nonce);
        var requests = planned.Select(request => new EvaluationAdmissionRequest(
            request.IdempotencyKey,
            EvaluationAdmissionContract.RequestBodySha256(request.Message),
            request.InvocationId,
            request.Turn,
            request.MaximumInputTokens,
            request.MaximumOutputTokens,
            caseSet.Catalog.Pricing.CandidateCost(
                request.MaximumInputTokens, request.MaximumOutputTokens)))
            .ToArray();
        var maximumCost = requests.Sum(request => request.MaximumCostEur);
        if (preflight.ReservedCandidateInputTokens
                != requests.Sum(request => (long)request.MaximumInputTokens)
            || preflight.ReservedCandidateOutputTokens
                != requests.Sum(request => (long)request.MaximumOutputTokens)
            || maximumCost != preflight.EstimatedCandidateCostEur)
            throw new InvalidDataException(
                "Evaluation admission request plan drifted from the reviewed preflight budget.");
        return new EvaluationAdmissionCapability(
            EvaluationAdmissionContract.Schema,
            authority.KeyId,
            authority.ReviewerId,
            identity.CandidateRevision,
            identity.CandidateImage,
            identity.CodeCommit,
            identity.ArtifactManifestSet,
            identity.CatalogSha256,
            issuedAt.ToUniversalTime(),
            issuedAt.ToUniversalTime().AddMinutes(20),
            nonce,
            requests.Length,
            preflight.ReservedCandidateInputTokens,
            preflight.ReservedCandidateOutputTokens,
            maximumCost,
            requests);
    }

    private static string NewNonce()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException(
                "Evaluation admission file is absent or outside its byte limit.");
        return File.ReadAllBytes(path);
    }

    private static void AtomicWrite(string output, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }
}

public sealed record AssistantEvaluationPlannedRequest(
    string IdempotencyKey,
    string Message,
    string InvocationId,
    int Turn,
    int MaximumInputTokens,
    int MaximumOutputTokens);

public static class AssistantEvaluationRequestPlan
{
    public static IReadOnlyList<AssistantEvaluationPlannedRequest> Build(
        AssistantEvaluationCatalog catalog,
        string nonce)
    {
        var runIdentity = EvaluationAdmissionContract.RunIdentity(nonce);
        var requests = new List<AssistantEvaluationPlannedRequest>();
        foreach (var evaluationCase in catalog.Cases)
        for (var repetition = 1; repetition <= evaluationCase.Repetitions; repetition++)
        {
            var baseKey = BaseKey(runIdentity, evaluationCase.Id, repetition);
            var turn = 0;
            foreach (var setup in evaluationCase.History ?? [])
            {
                turn++;
                requests.Add(new AssistantEvaluationPlannedRequest(
                    SetupKey(baseKey, turn), setup.Content,
                    baseKey, turn,
                    setup.MaximumInputTokens, setup.MaximumOutputTokens));
            }
            turn++;
            requests.Add(new AssistantEvaluationPlannedRequest(
                baseKey, evaluationCase.Question,
                baseKey, turn,
                evaluationCase.MaximumInputTokens,
                evaluationCase.MaximumOutputTokens));
        }
        return requests;
    }

    public static string BaseKey(
        string runIdentity,
        string caseId,
        int repetition) => $"eval-{runIdentity}-{caseId}-{repetition}";

    public static string SetupKey(string baseKey, int turn) =>
        $"{baseKey}-setup-{turn}";
}
