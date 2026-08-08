using System.Diagnostics;
using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace Lex.RetrievalExperiment;

public static class TypedPlanExperiment
{
    private const string Instructions = """
        You are Lex's legal-research planner. You do not answer legal questions and you do not
        choose a law. Return only the requested typed plan.

        A search plan:
        - action is "search";
        - states the user's intent without adding a legal conclusion;
        - identifies jurisdiction as EU, LU, both, or null;
        - preserves an explicit requested date as ISO yyyy-MM-dd, otherwise uses null;
        - contains two to four short, distinct retrieval queries;
        - includes both a French and an English legal formulation for EU research;
        - expands a known acronym in one query when useful;
        - contains two or three separate provision_queries, each no more than about twelve legal
          terms, for locating the operative article after the work is resolved;
        - provision_queries omit the instrument title, jurisdiction, publisher and date, but keep
          any article number and operative wording supplied by the user;
        - contains no work id, CELEX number, article, answer, citation, or invented metadata unless
          the user supplied it.

        A clarification plan:
        - action is "clarify";
        - is used only when jurisdiction or legal mechanism would materially change the research;
        - contains no queries or provision_queries;
        - asks one concise question with two to four concrete choices;
        - keeps a custom text answer possible.

        Do not clarify harmless ambiguity. For "today" or no date, use no date and let the
        deterministic executor disclose its current-date assumption. A short professional name
        such as RGPD, GDPR, DORA, or AI Act is sufficiently specific and must not trigger a
        clarification. By contrast, a broad request about favouring local manufacturing without a
        sector or legal mechanism is materially ambiguous: clarify whether the user means public
        procurement, renewable-energy auctions or support schemes, or State aid before searching.
        """;

    public static async Task<JsonObject> RunAsync(
        string endpoint,
        string deployment,
        string scenarioPath,
        string clarificationPath,
        int runs,
        CancellationToken cancellationToken)
    {
        if (runs is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(runs));
        var root = JsonNode.Parse(File.ReadAllBytes(scenarioPath))?.AsObject()
            ?? throw new InvalidDataException("Scenario file is empty.");
        var chat = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetChatClient(deployment);
        AIAgent agent = chat.AsAIAgent(
            instructions: Instructions,
            name: "lex-typed-research-planner",
            description: "Produces bounded legal-research or clarification plans.");
        var clarificationDimensions = ClarificationPolicy.Load(clarificationPath);
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
                var turnResults = runResult["turns"]!.AsArray();
                try
                {
                    var session = await agent.CreateSessionAsync(cancellationToken);
                    foreach (var turn in turns)
                    {
                        var stopwatch = Stopwatch.StartNew();
                        var response = await agent.RunAsync<LegalResearchPlan>(
                            turn,
                            session,
                            ResearchPlanContract.JsonOptions,
                            cancellationToken: cancellationToken);
                        stopwatch.Stop();
                        var plan = ClarificationPolicy.Apply(
                            response.Result, turn, clarificationDimensions);
                        var serializedSession = await agent.SerializeSessionAsync(
                            session, ResearchPlanContract.JsonOptions, cancellationToken);
                        session = await agent.DeserializeSessionAsync(
                            serializedSession, ResearchPlanContract.JsonOptions, cancellationToken);
                        turnResults.Add(new JsonObject
                        {
                            ["input"] = turn,
                            ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                            ["input_tokens"] = response.Usage?.InputTokenCount,
                            ["output_tokens"] = response.Usage?.OutputTokenCount,
                            ["plan"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
                                plan, ResearchPlanContract.JsonOptions)),
                            ["session_bytes"] = serializedSession.GetRawText().Length,
                            ["session_restored"] = true,
                        });
                        Console.Error.WriteLine(
                            $"[typed-plan] {id} run={run} action={plan.Action} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
                    }
                    runResult["status"] = "ok";
                }
                catch (Exception error)
                {
                    runResult["status"] = "failed";
                    runResult["error_type"] = error.GetType().Name;
                    runResult["error"] = error.Message;
                    Console.Error.WriteLine($"[typed-plan] {id} run={run} failed: {error.GetType().Name}");
                }
                results.Add(runResult);
            }
        }
        return new JsonObject
        {
            ["schema"] = "lex-typed-plan-experiment/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["framework"] = "Microsoft Agent Framework",
            ["framework_package"] = "Microsoft.Agents.AI.OpenAI",
            ["framework_version"] = "1.13.0",
            ["endpoint_host"] = new Uri(endpoint).Host,
            ["deployment"] = deployment,
            ["scenario_sha256"] = WorkSearchExperiment.HashFile(scenarioPath),
            ["clarification_config_sha256"] = WorkSearchExperiment.HashFile(clarificationPath),
            ["runs_per_scenario"] = runs,
            ["results"] = results,
        };
    }
}
