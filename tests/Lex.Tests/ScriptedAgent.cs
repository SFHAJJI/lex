using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Lex.Tests;

/// <summary>
/// An <see cref="AIAgent"/> that returns a scripted sequence of raw model outputs.
///
/// <para>Deliberately shallow: it overrides only the one call the finalizer makes and returns
/// TEXT, so the real structured-output deserialization, the real contract validation and the
/// real judge mapping all still run. A fake that returned a ready-made
/// <c>AgentAnswerDraft</c> would skip exactly the machinery worth testing.</para>
/// </summary>
internal sealed class ScriptedAgent : AIAgent
{
    private readonly Queue<string> _outputs;

    internal ScriptedAgent(params string[] outputs) => _outputs = new(outputs);

    /// <summary>How many times the finalizer actually called the model.</summary>
    internal int Calls { get; private set; }

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        Calls++;
        if (_outputs.Count == 0)
            throw new InvalidOperationException(
                $"The finalizer made call {Calls}, more than this script provides.");
        var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, _outputs.Dequeue()))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 },
        };
        return Task.FromResult(response);
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The finalizer does not stream.");

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(
        CancellationToken cancellationToken) =>
        new(new ScriptedSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The finalizer does not persist sessions.");

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The finalizer does not persist sessions.");

    private sealed class ScriptedSession : AgentSession;
}
