using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;

namespace Lex.Tests;

/// <summary>
/// Synthesis is bounded by a deadline of its own.
///
/// <para>The first-result deadline is disarmed the moment an operation reports, so by the time
/// the optional descriptive layer runs, the token it inherited cancels only when the client
/// disconnects. A composer that retries and then a judge, both against a hung upstream, would
/// hold the request open indefinitely while the verified answer sat ready to serve.</para>
/// </summary>
public sealed class SynthesisDeadlineTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"lex-{Guid.NewGuid():N}.db");
    private readonly LexIndexReader _reader;
    private readonly McpCore _core;

    public SynthesisDeadlineTests()
    {
        IndexBuilder.Build(_db, new Dictionary<string, string>
        {
            ["collection"] = "eu-eurlex",
            ["jurisdiction"] = "EU",
            ["built_at"] = "2026-08-13T00:00:00Z",
            ["corpus_commit"] = "test",
        }, [], [], [], [], null);
        _reader = LexIndexReader.Open(_db);
        _core = new McpCore(new Dictionary<string, LexIndexReader> { ["eu-eurlex"] = _reader });
    }

    [Fact]
    public async Task A_synthesis_that_outruns_its_budget_still_serves_the_verified_answer()
    {
        var service = new AskService(_core, new DeadlinePlanner(), new HangingSynthesizer(),
            synthesisDeadline: TimeSpan.FromMilliseconds(150));

        var response = await service.AskAsync(
            History("Show coverage."), Guid.NewGuid().ToString(), "law.test",
            CancellationToken.None);

        // The request completes rather than hanging, and the reader gets the verified results
        // with the prose declared unavailable rather than an error.
        Assert.Equal(200, response.Status);
        var reply = response.Body["reply"]!.GetValue<string>();
        Assert.Contains("verified results remain open", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_that_disconnects_is_not_treated_as_a_slow_composer()
    {
        using var caller = new CancellationTokenSource();
        var service = new AskService(_core, new DeadlinePlanner(),
            new HangingSynthesizer(caller), synthesisDeadline: TimeSpan.FromSeconds(30));

        var response = await service.AskAsync(
            History("Show coverage."), Guid.NewGuid().ToString(), "law.test", caller.Token);

        // AskAsync never throws for cancellation; it answers 499 for a caller cancel and 504 for
        // a timeout. The point of the inner rethrow is that this stays 499. Swallowing the
        // caller's cancellation as a synthesis failure would return 200 with prose declared
        // unavailable, which reports a served answer to a client that has gone away and makes a
        // disconnect indistinguishable from a slow model in the transport outcomes.
        Assert.Equal(499, response.Status);
    }

    [Fact]
    public void The_deadline_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AskService(
            _core, new DeadlinePlanner(), synthesisDeadline: TimeSpan.Zero));
    }

    private static JsonArray History(string question) =>
        new(new JsonObject { ["role"] = "user", ["content"] = question });

    private sealed class DeadlinePlanner : IOperationPlanner
    {
        public Task<OperationPlan> PlanAsync(
            JsonArray history, string host, string requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationPlan.FromPlannerOutput(requestId, "en",
                new JsonArray(new JsonObject
                {
                    ["tool"] = "coverage",
                    ["arguments"] = new JsonObject(),
                }), synthesisRequested: true));
    }

    private sealed class HangingSynthesizer(CancellationTokenSource? cancelCaller = null)
        : IOperationSynthesizer
    {
        public async Task<AgentFinalization> SynthesizeAsync(
            string question,
            string deterministicDraft,
            IReadOnlyList<AgentEvidence> evidence,
            string locale,
            CancellationToken cancellationToken)
        {
            cancelCaller?.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        try { File.Delete(_db); } catch (IOException) { }
    }
}
