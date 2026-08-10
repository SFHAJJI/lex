using System.Text.Json.Nodes;

namespace Lex.Ask;

internal interface IOperationPlanner
{
    Task<OperationPlan> PlanAsync(
        JsonArray history,
        string host,
        string requestId,
        CancellationToken cancellationToken);
}

internal interface IOperationSynthesizer
{
    Task<AgentFinalization> SynthesizeAsync(
        string question,
        string deterministicDraft,
        IReadOnlyList<AgentEvidence> evidence,
        CancellationToken cancellationToken);
}
