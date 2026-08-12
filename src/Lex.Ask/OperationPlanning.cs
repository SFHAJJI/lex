using System.Text.Json.Nodes;

namespace Lex.Ask;

internal readonly record struct ModelTokenUsage(long InputTokens, long OutputTokens)
{
    public long TotalTokens => checked(InputTokens + OutputTokens);

    public ModelTokenUsage Add(ModelTokenUsage other) => new(
        checked(InputTokens + other.InputTokens),
        checked(OutputTokens + other.OutputTokens));
}

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
        string locale,
        CancellationToken cancellationToken);
}
