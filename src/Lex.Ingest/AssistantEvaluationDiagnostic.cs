using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Ingest;

public sealed record AssistantEvaluationDiagnosticGrade(
    int Score,
    AssistantModelUsage Usage,
    string FinishReason);

public interface IAssistantEvaluationDiagnosticGrader
{
    Task<AssistantEvaluationDiagnosticGrade> GradeDiagnosticAsync(
        AssistantEvaluationCase evaluationCase,
        JsonObject response,
        CancellationToken cancellationToken);
}

public sealed record AssistantEvaluationDiagnosticCaseResult(
    string CaseId,
    int Repetition,
    string PromptSha256,
    string TargetFailureCategory,
    string GraderFailureCategory,
    int? Grade,
    string? FinishReason,
    AssistantModelUsage GraderUsage);

public sealed record AssistantEvaluationDiagnosticReport(
    string Schema,
    string Purpose,
    bool Publishable,
    bool MeasurementCompleted,
    string CasesSha256,
    string FrozenAt,
    string RunAt,
    AssistantEvaluationIdentity Identity,
    string AdmissionRunIdentity,
    int GraderMaximumOutputTokens,
    AssistantEvaluationPreflight Preflight,
    IReadOnlyList<AssistantEvaluationDiagnosticCaseResult> Results);

public static class AssistantEvaluationDiagnosticRunner
{
    public const string ReportSchema = "lex-assistant-eval-diagnostic/1";
    public const int GraderMaximumOutputTokens = 8_000;

    public static async Task<AssistantEvaluationDiagnosticReport> RunAsync(
        AssistantEvaluationSet caseSet,
        IAssistantEvaluationTarget target,
        IAssistantEvaluationDiagnosticGrader? grader,
        AssistantEvaluationIdentity identity,
        AssistantEvaluationPricing pricing,
        DateTimeOffset runAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caseSet);
        ArgumentNullException.ThrowIfNull(target);
        AssistantEvaluationRunner.ValidateIdentity(identity);
        pricing.ValidateFor(identity, runAt);

        var reviewedPreflight = caseSet.Preflight(pricing);
        var reservedGraderOutput = caseSet.Catalog.Cases.Sum(item =>
            item.Grading.Mode == "llm"
                ? checked((long)GraderMaximumOutputTokens * item.Repetitions)
                : 0);
        var candidateCost = pricing.CandidateCost(
            reviewedPreflight.ReservedCandidateInputTokens,
            reviewedPreflight.ReservedCandidateOutputTokens);
        var graderCost = pricing.GraderCost(
            reviewedPreflight.ReservedGraderInputTokens,
            reservedGraderOutput);
        var totalCost = checked(candidateCost + graderCost);
        if (totalCost > caseSet.Catalog.Budget.MaximumCostEur)
            throw new InvalidDataException(
                "Diagnostic assistant evaluation estimated cost exceeds the reviewed EUR budget.");
        var preflight = new AssistantEvaluationPreflight(
            reviewedPreflight.ReservedCandidateInputTokens,
            reviewedPreflight.ReservedCandidateOutputTokens,
            reviewedPreflight.ReservedGraderInputTokens,
            reservedGraderOutput,
            candidateCost,
            graderCost,
            totalCost);

        cancellationToken.ThrowIfCancellationRequested();
        await target.VerifyReleaseIdentityAsync(identity, cancellationToken);
        var runIdentity = target.AdmissionRunIdentity;
        if (runIdentity is null || runIdentity.Length != 16
            || runIdentity.Any(character =>
                character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
            throw new InvalidDataException(
                "Diagnostic assistant evaluation requires a signed admission run identity.");

        // Consume every admission-authorized candidate request before the deliberately
        // longer diagnostic grader calls can age the short-lived admission capability.
        var pending = new List<PendingResult>();
        foreach (var evaluationCase in caseSet.Catalog.Cases)
            for (var repetition = 1; repetition <= evaluationCase.Repetitions; repetition++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonObject? response = null;
                var targetFailure = "none";
                try
                {
                    var invocation = await target.InvokeAsync(
                        evaluationCase,
                        AssistantEvaluationRequestPlan.BaseKey(
                            runIdentity, evaluationCase.Id, repetition),
                        cancellationToken);
                    if (invocation.StatusCode != 200)
                        targetFailure = "http_failure";
                    else if (invocation.Response is null)
                        targetFailure = "invalid_response";
                    else
                        response = invocation.Response;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    targetFailure = "timeout";
                }
                catch (HttpRequestException)
                {
                    targetFailure = "transport";
                }
                // A named stage refusal is the same category as the InvalidDataException it
                // replaced; only the official report reads its cause.
                catch (Exception exception) when (exception
                    is InvalidDataException or AssistantEvaluationStageException)
                {
                    targetFailure = "invalid_response";
                }
                catch (JsonException)
                {
                    targetFailure = "invalid_response";
                }
                catch
                {
                    targetFailure = "unknown";
                }
                pending.Add(new PendingResult(
                    evaluationCase, repetition, response, targetFailure));
            }

        var results = new List<AssistantEvaluationDiagnosticCaseResult>(pending.Count);
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var graderFailure = item.TargetFailure == "none"
                ? item.Case.Grading.Mode == "llm" ? "none" : "not_required"
                : "not_run";
            int? grade = null;
            string? finishReason = null;
            var graderUsage = new AssistantModelUsage(0, 0);
            if (graderFailure == "none")
            {
                if (grader is null)
                    graderFailure = "not_configured";
                else
                {
                    try
                    {
                        var measured = await grader.GradeDiagnosticAsync(
                            item.Case, item.Response!, cancellationToken);
                        finishReason = ClosedFinishReason(measured.FinishReason);
                        graderUsage = BoundedUsage(measured.Usage, item.Case);
                        if (finishReason == "length")
                            graderFailure = "truncated";
                        else if (finishReason == "content_filter")
                            graderFailure = "content_filtered";
                        else if (finishReason != "stop")
                            graderFailure = "unknown";
                        else if (measured.Score is < 1 or > 5
                            || measured.Usage.InputTokens is <= 0
                            || measured.Usage.OutputTokens is <= 0
                            || measured.Usage.InputTokens
                                > item.Case.Grading.MaximumInputTokens
                            || measured.Usage.OutputTokens > GraderMaximumOutputTokens)
                            graderFailure = "invalid_response";
                        else
                            grade = measured.Score;
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        graderFailure = "timeout";
                    }
                    catch (AssistantEvaluationDiagnosticResponseException exception)
                    {
                        (graderFailure, finishReason) =
                            (exception.FailureCategory, exception.FinishReason) switch
                            {
                                ("truncated", "length") => ("truncated", "length"),
                                ("content_filtered", "content_filter") =>
                                    ("content_filtered", "content_filter"),
                                ("invalid_response", "stop") =>
                                    ("invalid_response", "stop"),
                                ("invalid_response", null) =>
                                    ("invalid_response", null),
                                _ => ("unknown", null),
                            };
                        graderUsage = BoundedUsage(exception.Usage, item.Case);
                    }
                    catch (HttpRequestException)
                    {
                        graderFailure = "transport";
                    }
                    // Same category as the InvalidDataException it replaced; the diagnostic report
                    // classifies, the official report is the one that records the cause.
                    catch (Exception exception) when (exception
                        is InvalidDataException or AssistantEvaluationStageException)
                    {
                        graderFailure = "invalid_response";
                    }
                    catch (JsonException)
                    {
                        graderFailure = "invalid_response";
                    }
                    catch
                    {
                        graderFailure = "unknown";
                    }
                }
            }
            results.Add(new AssistantEvaluationDiagnosticCaseResult(
                item.Case.Id,
                item.Repetition,
                AssistantEvaluationRunner.PromptSha256(item.Case),
                item.TargetFailure,
                graderFailure,
                grade,
                finishReason,
                graderUsage));
        }

        var completed = results.All(result =>
            result.TargetFailureCategory == "none"
            && result.GraderFailureCategory is "none" or "not_required");
        return new AssistantEvaluationDiagnosticReport(
            ReportSchema,
            "diagnostic_only",
            Publishable: false,
            MeasurementCompleted: completed,
            caseSet.Sha256,
            caseSet.Catalog.FrozenAt,
            runAt.ToUniversalTime().ToString("O"),
            identity,
            runIdentity,
            GraderMaximumOutputTokens,
            preflight,
            results);
    }

    private sealed record PendingResult(
        AssistantEvaluationCase Case,
        int Repetition,
        JsonObject? Response,
        string TargetFailure);

    private static string? ClosedFinishReason(string? finishReason) =>
        finishReason is "stop" or "length" or "content_filter"
            ? finishReason
            : null;

    private static AssistantModelUsage BoundedUsage(
        AssistantModelUsage usage,
        AssistantEvaluationCase evaluationCase) =>
        usage.InputTokens is >= 0
            && usage.InputTokens <= evaluationCase.Grading.MaximumInputTokens
            && usage.OutputTokens is >= 0
            && usage.OutputTokens <= GraderMaximumOutputTokens
                ? usage
                : new AssistantModelUsage(0, 0);
}

internal sealed class AssistantEvaluationDiagnosticResponseException(
    string failureCategory,
    string? finishReason,
    AssistantModelUsage usage) : Exception(
        "Diagnostic assistant grader returned an unusable response.")
{
    internal string FailureCategory { get; } = failureCategory;
    internal string? FinishReason { get; } = finishReason;
    internal AssistantModelUsage Usage { get; } = usage;
}
