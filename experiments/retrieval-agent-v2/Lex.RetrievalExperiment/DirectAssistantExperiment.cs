using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Lex.RetrievalExperiment;

public static class DirectAssistantExperiment
{
    private const string Instructions = """
        You are Lex's legal-research agent. Search the verified Luxembourg and European-law
        corpus through search_legal_evidence before answering. Never select a law, date or
        article from memory.

        For each tool call:
        - provide one compact French and one compact English subject or instrument query;
        - provide one compact French and one compact English provision query;
        - keep article numbers and operative wording in provision queries;
        - use EU, LU or BOTH for jurisdiction;
        - preserve an explicit requested date as yyyy-MM-dd, otherwise omit it;
        - call at most twice, and use a second call only to reformulate a genuine failed search.

        If the tool returns validated_candidate, answer only from its evidence. Copy its
        selected_work, selected_anchor and date exactly into the typed response. If every call
        returns no_validated_candidate, return status gap with null work, anchor and date. Do not
        invent an absent instrument. Reply concisely in the user's language. Mention the official
        source title, provision and date when evidence is available. The deterministic host, not
        you, handles materially ambiguous questions before this turn.
        """;

    public static async Task<JsonObject> RunAsync(
        string endpoint,
        string deployment,
        string scenarioPath,
        string clarificationPath,
        string workIndexPath,
        string workVectorPath,
        string indexDirectory,
        string modelDirectory,
        DateOnly today,
        int runs,
        CancellationToken cancellationToken)
    {
        if (runs is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(runs));
        var root = JsonNode.Parse(File.ReadAllBytes(scenarioPath))?.AsObject()
            ?? throw new InvalidDataException("Scenario file is empty.");
        var dimensions = ClarificationPolicy.Load(clarificationPath);
        using var executor = new ResearchPlanExecutor(
            workIndexPath, workVectorPath, indexDirectory, modelDirectory);
        var researchTool = new DirectResearchTool(executor, today);
        var chat = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetChatClient(deployment);
        AIAgent agent = chat.AsAIAgent(
            instructions: Instructions,
            name: "lex-direct-legal-research-agent",
            description: "Resolves and validates legal evidence through one bounded tool.",
            tools:
            [
                AIFunctionFactory.Create(
                    researchTool.SearchLegalEvidence,
                    name: "search_legal_evidence",
                    description: "Resolve a work through reviewed names and semantic concepts, "
                        + "search its provisions, and validate the exact dated provision. Returns "
                        + "compact evidence or an explicit no_validated_candidate gap."),
            ]);

        var results = new JsonArray();
        foreach (var scenario in root["cases"]?.AsArray().OfType<JsonObject>()
                     .Where(item => item["surface"]?.GetValue<string>() == "assistant") ?? [])
        {
            var id = scenario["id"]?.GetValue<string>()
                ?? throw new InvalidDataException("Assistant scenario has no id.");
            var turns = scenario["turns"]?.AsArray().Select(item => item?.GetValue<string>()
                ?? throw new InvalidDataException($"{id} contains an empty turn.")).ToArray()
                ?? throw new InvalidDataException($"{id} has no turns.");
            for (var run = 1; run <= runs; run++)
            {
                var runResult = new JsonObject
                {
                    ["id"] = id,
                    ["run"] = run,
                    ["turns"] = new JsonArray(),
                };
                var resultTurns = runResult["turns"]!.AsArray();
                var conversationInputs = new List<string>();
                try
                {
                    var session = await agent.CreateSessionAsync(cancellationToken);
                    foreach (var input in turns)
                    {
                        conversationInputs.Add(input);
                        var stopwatch = Stopwatch.StartNew();
                        var clarification = ClarificationPolicy.Match(input, dimensions);
                        if (clarification is not null)
                        {
                            stopwatch.Stop();
                            resultTurns.Add(new JsonObject
                            {
                                ["input"] = input,
                                ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                                ["action"] = "clarify",
                                ["clarification"] = JsonSerializer.SerializeToNode(
                                    clarification, ResearchPlanContract.JsonOptions),
                                ["tool_calls"] = new JsonArray(),
                            });
                            continue;
                        }

                        var trace = new DirectTurnTrace(string.Join('\n', conversationInputs));
                        DirectTurnTrace.Current = trace;
                        try
                        {
                            var response = await agent.RunAsync<DirectAssistantOutput>(
                                input,
                                session,
                                ResearchPlanContract.JsonOptions,
                                cancellationToken: cancellationToken);
                            stopwatch.Stop();
                            var output = DirectAssistantContract.Validate(
                                response.Result,
                                trace.SelectedWork,
                                trace.SelectedAnchor,
                                trace.SelectedDate);
                            var serializedSession = await agent.SerializeSessionAsync(
                                session, ResearchPlanContract.JsonOptions, cancellationToken);
                            session = await agent.DeserializeSessionAsync(
                                serializedSession, ResearchPlanContract.JsonOptions, cancellationToken);
                            resultTurns.Add(ResultTurn(
                                scenario, input, stopwatch.Elapsed, response, output, trace, serializedSession));
                        }
                        finally
                        {
                            DirectTurnTrace.Current = null;
                        }
                    }
                    runResult["status"] = "ok";
                }
                catch (Exception error)
                {
                    runResult["status"] = "failed";
                    runResult["error_type"] = error.GetType().Name;
                    runResult["error"] = error.Message;
                }
                Console.Error.WriteLine(
                    $"[direct-agent] {id} run={run} status={runResult["status"]?.GetValue<string>()}");
                results.Add(runResult);
            }
        }

        return new JsonObject
        {
            ["schema"] = "lex-direct-agent-experiment/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["framework"] = "Microsoft Agent Framework",
            ["framework_package"] = "Microsoft.Agents.AI.OpenAI",
            ["framework_version"] = "1.13.0",
            ["endpoint_host"] = new Uri(endpoint).Host,
            ["deployment"] = deployment,
            ["today"] = today.ToString("yyyy-MM-dd"),
            ["scenario_sha256"] = WorkSearchExperiment.HashFile(scenarioPath),
            ["clarification_config_sha256"] = WorkSearchExperiment.HashFile(clarificationPath),
            ["work_index_sha256"] = WorkSearchExperiment.HashFile(workIndexPath),
            ["work_vector_sha256"] = WorkSearchExperiment.HashFile(workVectorPath),
            ["runs_per_scenario"] = runs,
            ["results"] = results,
        };
    }

    private static JsonObject ResultTurn(
        JsonObject scenario,
        string input,
        TimeSpan elapsed,
        AgentResponse<DirectAssistantOutput> response,
        DirectAssistantOutput output,
        DirectTurnTrace trace,
        JsonElement serializedSession)
    {
        var expectedWork = scenario["expected_work"]?.GetValue<string>();
        var expectedAnchors = scenario["expected_anchor_any"]?.AsArray()
            .Select(item => item?.GetValue<string>()).Where(item => item is not null).ToHashSet()
            ?? [];
        return new JsonObject
        {
            ["input"] = input,
            ["elapsed_ms"] = elapsed.TotalMilliseconds,
            ["action"] = "answer",
            ["input_tokens"] = response.Usage?.InputTokenCount,
            ["output_tokens"] = response.Usage?.OutputTokenCount,
            ["tool_call_count"] = trace.Calls.Count,
            ["tool_calls"] = new JsonArray(trace.Calls.Select(call => (JsonNode)call.DeepClone()).ToArray()),
            ["output"] = JsonSerializer.SerializeToNode(output, ResearchPlanContract.JsonOptions),
            ["expected_work"] = expectedWork,
            ["work_correct"] = expectedWork is null ? null : output.Work == expectedWork,
            ["anchor_correct"] = expectedAnchors.Count == 0 ? null
                : output.Anchor is not null && expectedAnchors.Contains(output.Anchor),
            ["session_bytes"] = serializedSession.GetRawText().Length,
            ["session_restored"] = true,
        };
    }
}
