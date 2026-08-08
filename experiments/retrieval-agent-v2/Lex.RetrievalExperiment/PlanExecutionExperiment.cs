using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Lex.RetrievalExperiment;

public static class PlanExecutionExperiment
{
    public static JsonObject Run(
        string planReportPath,
        string scenarioPath,
        string workIndexPath,
        string workVectorPath,
        string indexDirectory,
        string modelDirectory,
        DateOnly today)
    {
        var plans = JsonNode.Parse(File.ReadAllBytes(planReportPath))?.AsObject()
            ?? throw new InvalidDataException("Typed-plan report is empty.");
        var scenarioRoot = JsonNode.Parse(File.ReadAllBytes(scenarioPath))?.AsObject()
            ?? throw new InvalidDataException("Scenario file is empty.");
        var scenarios = scenarioRoot["cases"]?.AsArray().OfType<JsonObject>()
            .Where(item => item["surface"]?.GetValue<string>() == "assistant")
            .ToDictionary(item => item["id"]?.GetValue<string>()
                ?? throw new InvalidDataException("Assistant scenario has no id."), StringComparer.Ordinal)
            ?? throw new InvalidDataException("Scenario cases are missing.");
        using var executor = new ResearchPlanExecutor(
            workIndexPath, workVectorPath, indexDirectory, modelDirectory);
        var results = new JsonArray();
        foreach (var run in plans["results"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var id = run["id"]?.GetValue<string>() ?? throw new InvalidDataException("Plan result has no id.");
            if (!scenarios.TryGetValue(id, out var scenario))
                throw new InvalidDataException($"Unknown assistant scenario in plan report: {id}");
            var result = new JsonObject
            {
                ["id"] = id,
                ["run"] = run["run"]?.DeepClone(),
                ["planner_status"] = run["status"]?.DeepClone(),
                ["turns"] = new JsonArray(),
            };
            var resultTurns = result["turns"]!.AsArray();
            var sourceTurns = run["turns"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            var conversationInputs = new List<string>();
            for (var turnIndex = 0; turnIndex < sourceTurns.Length; turnIndex++)
            {
                var sourceTurn = sourceTurns[turnIndex];
                var input = sourceTurn["input"]?.GetValue<string>() ?? "";
                conversationInputs.Add(input);
                var planNode = sourceTurn["plan"];
                if (planNode is null) continue;
                var plan = ResearchPlanContract.Parse(planNode.ToJsonString());
                var stopwatch = Stopwatch.StartNew();
                var execution = executor.Execute(plan, today, string.Join("\n", conversationInputs));
                stopwatch.Stop();
                var expectedWork = scenario["expected_work"]?.GetValue<string>();
                var expectedAnchors = scenario["expected_anchor_any"]?.AsArray()
                    .Select(item => item?.GetValue<string>()).Where(item => item is not null).ToHashSet()
                    ?? [];
                var selectedWork = execution["selected_work"]?.GetValue<string>();
                var selectedAnchor = execution["selected_anchor"]?.GetValue<string>();
                resultTurns.Add(new JsonObject
                {
                    ["input"] = sourceTurn["input"]?.DeepClone(),
                    ["plan"] = planNode.DeepClone(),
                    ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                    ["expected_work"] = expectedWork,
                    ["work_correct"] = expectedWork is null ? null : selectedWork == expectedWork,
                    ["anchor_correct"] = expectedAnchors.Count == 0 ? null
                        : selectedAnchor is not null && expectedAnchors.Contains(selectedAnchor),
                    ["execution"] = execution,
                });
                Console.Error.WriteLine(
                    $"[typed-exec] {id} turn={turnIndex + 1} selected={selectedWork ?? "none"} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
            }
            result["clarification_correct"] = ClarificationCorrect(scenario, sourceTurns);
            results.Add(result);
        }
        return new JsonObject
        {
            ["schema"] = "lex-typed-plan-execution-experiment/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["today"] = today.ToString("yyyy-MM-dd"),
            ["plan_report_sha256"] = WorkSearchExperiment.HashFile(planReportPath),
            ["scenario_sha256"] = WorkSearchExperiment.HashFile(scenarioPath),
            ["work_index_sha256"] = WorkSearchExperiment.HashFile(workIndexPath),
            ["work_vector_sha256"] = WorkSearchExperiment.HashFile(workVectorPath),
            ["results"] = results,
        };
    }

    private static bool ClarificationCorrect(JsonObject scenario, IReadOnlyList<JsonObject> turns)
    {
        var expected = scenario["clarification"]?.GetValue<string>() ?? "not_required";
        var actions = turns.Select(turn => turn["plan"]?["action"]?.GetValue<string>()).ToArray();
        return expected switch
        {
            "required_once" => actions.Count(action => action == "clarify") == 1,
            "forbidden" or "not_required" => actions.All(action => action != "clarify"),
            "allowed_once" => actions.Count(action => action == "clarify") <= 1,
            _ => throw new InvalidDataException($"Unknown clarification expectation: {expected}"),
        };
    }
}
