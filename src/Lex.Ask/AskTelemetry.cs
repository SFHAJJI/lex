using System.Diagnostics;

namespace Lex.Ask;

internal static class AskTelemetry
{
    public const string ActivitySourceName = "Lex.Ask";
    private static readonly ActivitySource Source = new(ActivitySourceName);

    public static Activity? StartPlan() =>
        Source.StartActivity("lex.plan", ActivityKind.Internal);

    public static void SetPlanTags(Activity? activity, OperationPlan plan) =>
        activity?.SetTag("lex.plan_shape", PlanShape(plan));

    internal static void SetFailure(Activity? activity) =>
        activity?.SetStatus(ActivityStatusCode.Error);

    internal static string PlanShape(OperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var application = plan.Operations.Count(operation => operation.Disposition is not null);
        var legal = plan.Operations.Length - application;
        if (legal == 0 && plan.Operations.Length == 1)
            return plan.Operations[0].Disposition switch
            {
                ApplicationDisposition.Clarification => "clarification",
                ApplicationDisposition.Gap => "gap",
                ApplicationDisposition.LegalBoundary => "legal_boundary",
                _ => throw new InvalidDataException("The typed plan has no disposition."),
            };
        if (legal == 0) return "application_mixed";
        if (application > 0) return plan.SynthesisRequested ? "mixed_synthesis" : "mixed";
        if (legal == 1)
            return plan.SynthesisRequested ? "single_legal_synthesis" : "single_legal";
        return plan.SynthesisRequested ? "multi_legal_synthesis" : "multi_legal";
    }
}
