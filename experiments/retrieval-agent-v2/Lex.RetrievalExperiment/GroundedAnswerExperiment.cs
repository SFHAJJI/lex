using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace Lex.RetrievalExperiment;

public static class GroundedAnswerExperiment
{
    private const string ComposerInstructions = """
        You are Lex's legal answer composer. The host gives you one user question and one exact,
        dated provision selected by deterministic retrieval. Write a concise answer in the user's
        language using only that evidence. Do not add a broader legal conclusion, exception or
        instrument from memory. Copy the evidence work, anchor, date, text hash and source URI
        exactly into one citation. Return the requested typed object only.
        """;

    private const string JudgeInstructions = """
        You are Lex's conditional grounding judge. Compare the user question, exact legal evidence
        and draft. Pass only if every legal claim is supported, the draft answers the question, and
        its citation exactly identifies the supplied work, date, anchor, text hash and source URI.
        Repair when removing or narrowing unsupported prose produces a fully supported answer from
        the same evidence. Refuse when the evidence cannot answer the question. A repair must return
        the complete replacement typed answer with the exact supplied citation. A pass or refusal
        has no replacement. Return the requested typed object only.
        """;

    public static async Task<JsonObject> RunAsync(
        string endpoint,
        string deployment,
        string executionReportPath,
        CancellationToken cancellationToken)
    {
        var source = JsonNode.Parse(File.ReadAllBytes(executionReportPath))?.AsObject()
            ?? throw new InvalidDataException("Execution report is empty.");
        var chat = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetChatClient(deployment);
        AIAgent composer = chat.AsAIAgent(
            instructions: ComposerInstructions,
            name: "lex-grounded-answer-composer",
            description: "Composes only from deterministically validated legal evidence.");
        AIAgent judge = chat.AsAIAgent(
            instructions: JudgeInstructions,
            name: "lex-grounding-judge",
            description: "Passes, repairs or refuses synthesized legal prose.");

        var results = new JsonArray();
        foreach (var sourceRun in source["results"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var result = new JsonObject
            {
                ["id"] = sourceRun["id"]?.DeepClone(),
                ["run"] = sourceRun["run"]?.DeepClone(),
                ["turns"] = new JsonArray(),
            };
            var turns = result["turns"]!.AsArray();
            try
            {
                var composerSession = await composer.CreateSessionAsync(cancellationToken);
                foreach (var sourceTurn in sourceRun["turns"]?.AsArray().OfType<JsonObject>() ?? [])
                {
                    var input = sourceTurn["input"]?.GetValue<string>() ?? "";
                    var execution = sourceTurn["execution"]?.AsObject()
                        ?? throw new InvalidDataException("Execution turn has no evidence object.");
                    var status = execution["status"]?.GetValue<string>();
                    if (status == "clarification_required")
                    {
                        turns.Add(new JsonObject
                        {
                            ["input"] = input,
                            ["action"] = "clarify",
                            ["clarification"] = execution["clarification"]?.DeepClone(),
                        });
                        continue;
                    }
                    if (status != "validated_candidate")
                    {
                        turns.Add(new JsonObject
                        {
                            ["input"] = input,
                            ["action"] = "gap",
                            ["final"] = JsonSerializer.SerializeToNode(new GroundedAnswerOutput(
                                GroundedAnswerStatus.Gap,
                                "Lex ne détient pas de disposition validée répondant à cette question.",
                                []), ResearchPlanContract.JsonOptions),
                        });
                        continue;
                    }

                    var evidence = GroundedEvidence.Extract(execution);
                    var evidencePrompt = GroundedEvidence.Prompt(execution, evidence);
                    var composeWatch = Stopwatch.StartNew();
                    GroundedAnswerOutput? draft = null;
                    InvalidDataException? composeContractError = null;
                    long composerInputTokens = 0;
                    long composerOutputTokens = 0;
                    var composerAttempts = 0;
                    while (draft is null && composerAttempts < 2)
                    {
                        composerAttempts++;
                        var prompt = composerAttempts == 1
                            ? $"Question:\n{input}\n\nValidated evidence:\n{evidencePrompt}"
                            : "The previous typed draft violated the answer or citation contract. "
                              + "Produce one complete answer using only the same validated evidence.";
                        var draftResponse = await composer.RunAsync<GroundedAnswerOutput>(
                            prompt,
                            composerSession,
                            ResearchPlanContract.JsonOptions,
                            cancellationToken: cancellationToken);
                        composerInputTokens += draftResponse.Usage?.InputTokenCount ?? 0;
                        composerOutputTokens += draftResponse.Usage?.OutputTokenCount ?? 0;
                        try
                        {
                            draft = GroundedAnswerContract.Validate(draftResponse.Result, [evidence]);
                        }
                        catch (InvalidDataException error)
                        {
                            composeContractError = error;
                        }
                    }
                    composeWatch.Stop();
                    var serializedSession = await composer.SerializeSessionAsync(
                        composerSession, ResearchPlanContract.JsonOptions, cancellationToken);
                    composerSession = await composer.DeserializeSessionAsync(
                        serializedSession, ResearchPlanContract.JsonOptions, cancellationToken);
                    if (draft is null)
                    {
                        turns.Add(new JsonObject
                        {
                            ["input"] = input,
                            ["action"] = "refused",
                            ["evidence"] = JsonSerializer.SerializeToNode(evidence,
                                ResearchPlanContract.JsonOptions),
                            ["composer_attempts"] = composerAttempts,
                            ["composer_elapsed_ms"] = composeWatch.Elapsed.TotalMilliseconds,
                            ["composer_input_tokens"] = composerInputTokens,
                            ["composer_output_tokens"] = composerOutputTokens,
                            ["composer_contract_error"] = composeContractError?.Message,
                            ["final"] = JsonSerializer.SerializeToNode(new GroundedAnswerOutput(
                                GroundedAnswerStatus.Gap,
                                "La réponse générée n'a pas satisfait le contrat de preuve après une correction.",
                                []), ResearchPlanContract.JsonOptions),
                            ["session_bytes"] = serializedSession.GetRawText().Length,
                            ["session_restored"] = true,
                        });
                        continue;
                    }

                    var judgeSession = await judge.CreateSessionAsync(cancellationToken);
                    var judgeWatch = Stopwatch.StartNew();
                    var judgmentResponse = await judge.RunAsync<GroundingJudgment>(
                        $"Question:\n{input}\n\nValidated evidence:\n{evidencePrompt}"
                        + $"\n\nDraft:\n{JsonSerializer.Serialize(draft, ResearchPlanContract.JsonOptions)}",
                        judgeSession,
                        ResearchPlanContract.JsonOptions,
                        cancellationToken: cancellationToken);
                    judgeWatch.Stop();
                    GroundingJudgment? judgment = null;
                    string? judgeContractError = null;
                    try
                    {
                        judgment = GroundingJudgmentContract.Validate(
                            judgmentResponse.Result, draft, [evidence]);
                    }
                    catch (InvalidDataException error)
                    {
                        judgeContractError = error.Message;
                    }
                    var final = judgment?.Disposition switch
                    {
                        JudgeDisposition.Pass => draft,
                        JudgeDisposition.Repair => judgment.Replacement!,
                        _ => new GroundedAnswerOutput(
                            GroundedAnswerStatus.Gap,
                            "La preuve validée ne suffit pas à répondre sans interprétation non étayée.",
                            []),
                    };
                    turns.Add(new JsonObject
                    {
                        ["input"] = input,
                        ["action"] = "synthesized",
                        ["evidence"] = JsonSerializer.SerializeToNode(evidence,
                            ResearchPlanContract.JsonOptions),
                        ["composer_attempts"] = composerAttempts,
                        ["composer_elapsed_ms"] = composeWatch.Elapsed.TotalMilliseconds,
                        ["composer_input_tokens"] = composerInputTokens,
                        ["composer_output_tokens"] = composerOutputTokens,
                        ["draft"] = JsonSerializer.SerializeToNode(draft,
                            ResearchPlanContract.JsonOptions),
                        ["judge_elapsed_ms"] = judgeWatch.Elapsed.TotalMilliseconds,
                        ["judge_input_tokens"] = judgmentResponse.Usage?.InputTokenCount,
                        ["judge_output_tokens"] = judgmentResponse.Usage?.OutputTokenCount,
                        ["judgment"] = JsonSerializer.SerializeToNode(judgment,
                            ResearchPlanContract.JsonOptions),
                        ["judge_contract_error"] = judgeContractError,
                        ["final"] = JsonSerializer.SerializeToNode(final,
                            ResearchPlanContract.JsonOptions),
                        ["session_bytes"] = serializedSession.GetRawText().Length,
                        ["session_restored"] = true,
                    });
                }
                result["status"] = "ok";
            }
            catch (Exception error)
            {
                result["status"] = "failed";
                result["error_type"] = error.GetType().Name;
                result["error"] = error.Message;
            }
            Console.Error.WriteLine(
                $"[grounding] {result["id"]} run={result["run"]} status={result["status"]}");
            results.Add(result);
        }

        return new JsonObject
        {
            ["schema"] = "lex-grounded-answer-experiment/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["framework"] = "Microsoft Agent Framework",
            ["framework_package"] = "Microsoft.Agents.AI.OpenAI",
            ["framework_version"] = "1.13.0",
            ["endpoint_host"] = new Uri(endpoint).Host,
            ["deployment"] = deployment,
            ["execution_report_sha256"] = WorkSearchExperiment.HashFile(executionReportPath),
            ["results"] = results,
        };
    }
}

public static class GroundedEvidence
{
    public static ValidatedEvidence Extract(JsonObject execution)
    {
        var call = execution["tool_calls"]?.AsArray().OfType<JsonObject>()
            .LastOrDefault(item => item["tool"]?.GetValue<string>() == "as_of")
            ?? throw new InvalidDataException("Validated execution has no as_of call.");
        var result = call["result"];
        var work = execution["selected_work"]?.GetValue<string>();
        var anchor = execution["selected_anchor"]?.GetValue<string>();
        var date = call["args"]?["date"]?.GetValue<string>();
        var provision = result?["provisions"]?.AsArray().OfType<JsonObject>()
            .FirstOrDefault(item => item["anchor"]?.GetValue<string>() == anchor);
        var hash = provision?["text_sha256"]?.GetValue<string>();
        var source = result?["document"]?["source_uri"]?.GetValue<string>();
        if (work is null || anchor is null || date is null || hash is null || source is null)
            throw new InvalidDataException("Validated execution is missing citation identity.");
        return new ValidatedEvidence(work, anchor, date, hash, source);
    }

    public static string Prompt(JsonObject execution, ValidatedEvidence evidence)
    {
        var call = execution["tool_calls"]!.AsArray().OfType<JsonObject>()
            .Last(item => item["tool"]?.GetValue<string>() == "as_of");
        var result = call["result"]!;
        var provision = result["provisions"]!.AsArray().OfType<JsonObject>()
            .First(item => item["anchor"]?.GetValue<string>() == evidence.Anchor);
        var boundedText = BoundText(provision["text"]?.GetValue<string>() ?? "", 8_000);
        return new JsonObject
        {
            ["work"] = evidence.Work,
            ["anchor"] = evidence.Anchor,
            ["date"] = evidence.Date,
            ["text_sha256"] = evidence.TextSha256,
            ["source_uri"] = evidence.SourceUri,
            ["timeline_semantics"] = result["envelope"]?["timeline_semantics"]?.DeepClone(),
            ["title"] = result["document"]?["title"]?.DeepClone(),
            ["valid_from"] = result["document"]?["valid_from"]?.DeepClone(),
            ["valid_to"] = result["document"]?["valid_to"]?.DeepClone(),
            ["number"] = provision["num"]?.DeepClone(),
            ["heading"] = provision["heading"]?.DeepClone(),
            ["text"] = boundedText.Text,
            ["text_truncated"] = boundedText.Truncated,
        }.ToJsonString();
    }

    internal static (string Text, bool Truncated) BoundText(string value, int maximum) =>
        value.Length <= maximum
            ? (value, false)
            : (string.Concat(value.AsSpan(0, maximum), "…"), true);
}
