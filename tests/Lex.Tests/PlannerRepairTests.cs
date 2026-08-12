using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;

namespace Lex.Tests;

/// <summary>
/// The bounded single corrective turn around the planner's own output contract, and the guarantee
/// that the corrected plan goes through exactly the same validation.
///
/// <para>These are the first tests to reach the raw-HTTP planning branch at all. Every other
/// assistant test injects an <c>IOperationPlanner</c> that returns an already-built
/// <see cref="OperationPlan"/>, which returns before this branch starts, so the branch the repair
/// turn lives in was previously unreachable from the suite. The send seam is the same shape as the
/// legal-tool seam that made MCP testable.</para>
/// </summary>
public sealed class PlannerRepairTests
{
    private static readonly CorpusVocabulary Vocabulary =
        new(["lu-legilux"], ["lu"]);

    private const string ReversedWindow =
        """{"operations":[{"tool":"diff","arguments":{"work_query":"CRR","from_date":"2024-12-31","to_date":"2020-01-01"}}]}""";

    private const string ArrayWorkQuery =
        """{"operations":[{"tool":"as_of","arguments":{"work_query":["GDPR"],"date":"2020-01-01"}}]}""";

    private const string MalformedDate =
        """{"operations":[{"tool":"as_of","arguments":{"work_query":"CRR","date":"2024"}}]}""";

    private const string MissingComparisonBound =
        """{"operations":[{"tool":"diff","arguments":{"work_query":"CRR","from_date":"2020-01-01"}}]}""";

    private const string NoWorkIdentity =
        """{"operations":[{"tool":"as_of","arguments":{"date":"2020-01-01"}}]}""";

    private const string ValidCoverage =
        """{"operations":[{"tool":"coverage","arguments":{}}]}""";

    private const string ValidBoundary =
        """{"operations":[{"tool":"legal_boundary","arguments":{"reason":"legal advice"}}]}""";

    /// <summary>One planner response envelope, in the shape the chat-completions v1 surface
    /// returns it.</summary>
    private static JsonNode Envelope(
        string planJson,
        string finishReason = "stop",
        long completionTokens = 120,
        long promptTokens = 4000,
        string? callId = "call_plan_1")
    {
        var call = new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "submit_operation_plan",
                ["arguments"] = planJson,
            },
        };
        if (callId is not null) call["id"] = callId;
        return new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = finishReason,
                ["message"] = new JsonObject { ["tool_calls"] = new JsonArray(call) },
            }),
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = promptTokens,
                ["completion_tokens"] = completionTokens,
            },
        };
    }

    /// <summary>A recording planner transport: one canned response per attempt, every request
    /// captured exactly as it was sent.</summary>
    private sealed class RecordingSend(params JsonNode[] responses)
    {
        public List<JsonObject> Requests { get; } = [];

        public Task<JsonNode?> SendAsync(JsonObject request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.DeepClone().AsObject());
            var index = Requests.Count - 1;
            Assert.True(index < responses.Length,
                $"The planner was called {Requests.Count} times; {responses.Length} were allowed.");
            return Task.FromResult<JsonNode?>(responses[index].DeepClone());
        }
    }

    private static JsonObject PlannerRequest(string question = "Compare Article 92 of CRR between 2020 and 2024.")
        => new()
        {
            ["model"] = "gpt-5-mini",
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = "You plan operations for Lex." },
                new JsonObject { ["role"] = "user", ["content"] = question }),
            ["tools"] = AskService.PlannerTools(Vocabulary),
            ["tool_choice"] = new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "submit_operation_plan" },
            },
            ["max_completion_tokens"] = 4000,
            ["reasoning_effort"] = "low",
        };

    private static async Task<(OperationPlan Plan, bool Repaired)> PlanAsync(
        RecordingSend send,
        TimeSpan? deadline = null,
        JsonObject? request = null)
    {
        var (plan, _, repaired) = await AskService.PlanWithOneRepairAsync(
            request ?? PlannerRequest(),
            send.SendAsync,
            deadline ?? TimeSpan.FromSeconds(12),
            "req-1",
            "en",
            Vocabulary,
            new DateOnly(2026, 3, 4),
            CancellationToken.None);
        return (plan, repaired);
    }

    // A plan the gate refuses, followed by a corrected one, is an answered question rather than a
    // refusal blamed on the user.
    [Fact]
    public async Task A_rejected_plan_is_corrected_once_and_the_corrected_plan_is_accepted()
    {
        var send = new RecordingSend(Envelope(ArrayWorkQuery), Envelope(ValidCoverage));

        var (plan, repaired) = await PlanAsync(send);

        Assert.True(repaired);
        Assert.Equal(2, send.Requests.Count);
        var operation = Assert.Single(plan.Operations);
        Assert.Equal("coverage", operation.Tool);
    }

    // The whole point of the change, end to end: the same two planner responses, driven through
    // the public entry point, produce a completed legal operation instead of the invalid-request
    // gap, and the trace says the plan came from the second attempt.
    [Fact]
    public async Task A_repaired_plan_answers_the_question_instead_of_refusing_it()
    {
        var send = new RecordingSend(Envelope(ArrayWorkQuery), Envelope(ValidCoverage));
        var service = Service(send);

        var response = await service.AskAsync(
            History("What does Lex hold?"), "repair-client", "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        var operation = Assert.Single(response.Body["operations"]!.AsArray())!;
        Assert.Equal("succeeded", operation["legal_outcome"]?.GetValue<string>());
        Assert.Equal("inventory", operation["result_class"]?.GetValue<string>());
        var phase = response.Body["trace"]!.AsArray()
            .OfType<JsonObject>()
            .Single(item => item["phase"]?.GetValue<string>() == "operation_plan");
        Assert.True(phase["planner_retry"]?.GetValue<bool>());
        // Both attempts are billed, so the reported usage stays honest about what planning cost.
        Assert.Equal(8000, response.Body["model_usage"]!["input_tokens"]!.GetValue<long>());
        Assert.Equal(240, response.Body["model_usage"]!["output_tokens"]!.GetValue<long>());
    }

    // Two consecutive violations end exactly where one violation ended before this change: the
    // same 200, the same gap, the same explanation, and no retry metadata.
    [Fact]
    public async Task Two_consecutive_invalid_plans_produce_the_unchanged_refusal()
    {
        var send = new RecordingSend(Envelope(ArrayWorkQuery), Envelope(ReversedWindow));
        var service = Service(send);

        var response = await service.AskAsync(
            History("What does Lex hold?"), "refusal-client", "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Equal("This request does not map to a valid legal operation.",
            response.Body["reply"]?.GetValue<string>());
        var operation = Assert.Single(response.Body["operations"]!.AsArray())!;
        Assert.Equal("invalid_request", operation["legal_outcome"]?.GetValue<string>());
        Assert.Equal("gap", operation["disposition"]?.GetValue<string>());
        var phase = Assert.Single(response.Body["trace"]!.AsArray())!.AsObject();
        Assert.Equal("operation_plan", phase["phase"]?.GetValue<string>());
        Assert.Equal("invalid_request", phase["status"]?.GetValue<string>());
        Assert.Null(phase["planner_retry"]);
    }

    // One retry, never two. The bound is a for loop, not a recursion and not a predicate that
    // could be widened by accident.
    [Fact]
    public async Task Exactly_one_repair_is_attempted_and_never_a_second()
    {
        Assert.Equal(2, AskService.MaximumPlannerAttempts);
        var send = new RecordingSend(
            Envelope(ArrayWorkQuery), Envelope(ArrayWorkQuery), Envelope(ValidCoverage));

        await Assert.ThrowsAsync<InvalidDataException>(() => PlanAsync(send));

        Assert.Equal(2, send.Requests.Count);
    }

    // The corrective turn resends the whole original request and appends exactly two messages: the
    // assistant tool_call verbatim and the tool message answering its id. A user turn there is a
    // 400 from chat completions, and tool-role content is data rather than instruction.
    [Fact]
    public async Task The_corrective_turn_carries_the_violation_and_the_original_request()
    {
        const string question = "Compare Article 92 of CRR between 2020 and 2024.";
        var send = new RecordingSend(Envelope(MissingComparisonBound), Envelope(ValidCoverage));

        await PlanAsync(send, request: PlannerRequest(question));

        var first = send.Requests[0];
        var second = send.Requests[1];
        foreach (var key in new[]
                 { "model", "tools", "tool_choice", "max_completion_tokens", "reasoning_effort" })
            Assert.Equal(first[key]!.ToJsonString(), second[key]!.ToJsonString());

        var before = first["messages"]!.AsArray();
        var after = second["messages"]!.AsArray();
        Assert.Equal(before.Count + 2, after.Count);
        // Nothing is removed and nothing is rephrased: the system prompt and the user's own words
        // are resent byte-identically.
        for (var index = 0; index < before.Count; index++)
            Assert.Equal(before[index]!.ToJsonString(), after[index]!.ToJsonString());
        Assert.Equal(question, after[1]!["content"]!.GetValue<string>());

        var assistant = after[^2]!.AsObject();
        Assert.Equal("assistant", assistant["role"]!.GetValue<string>());
        var resent = Assert.Single(assistant["tool_calls"]!.AsArray())!;
        Assert.Equal("call_plan_1", resent["id"]!.GetValue<string>());
        Assert.Equal("submit_operation_plan", resent["function"]!["name"]!.GetValue<string>());

        var correction = after[^1]!.AsObject();
        Assert.Equal("tool", correction["role"]!.GetValue<string>());
        Assert.Equal("call_plan_1", correction["tool_call_id"]!.GetValue<string>());
        var content = JsonNode.Parse(correction["content"]!.GetValue<string>())!.AsObject();
        Assert.Equal("invalid_operation_plan", content["error"]!.GetValue<string>());
        Assert.Equal("Argument 'to_date' must be an ISO date.",
            content["violation"]!.GetValue<string>());
        var instruction = content["instruction"]!.GetValue<string>();
        // The two load-bearing clauses: the escape hatches stay visible, and satisfying the
        // constraint by swapping the two dates is forbidden outright.
        Assert.Contains("Derive every value from the user's own words.", instruction,
            StringComparison.Ordinal);
        Assert.Contains("Do not reorder, truncate, invent or delete a date", instruction,
            StringComparison.Ordinal);
        Assert.Contains("legal_boundary", instruction, StringComparison.Ordinal);
        Assert.Contains("clarification", instruction, StringComparison.Ordinal);
    }

    // The safety property, proved rather than asserted: drive the same violation twice and the
    // message coming out of attempt two is character-identical to the one attempt one produced.
    // This fails the moment anyone threads an "is retry" flag through the argument gate.
    //
    // Driven over every RETRYABLE class that decides which law or which provision is answered,
    // because "the second plan is validated identically" is a claim about all of them and one
    // example cannot carry it: a work identity of the wrong shape, a work identity that is absent
    // entirely, and a comparison carrying only one of its two bounds. Each is refused on attempt
    // one, sent back for correction, repeated by the planner, and refused again in the same words.
    // The two classes that are never retried are covered by the test below this one instead.
    [Theory]
    [InlineData(ArrayWorkQuery, "Argument 'work_query' must be a string. Received Array.")]
    [InlineData(NoWorkIdentity, "Operation 'as_of' requires a work identity.")]
    [InlineData(MissingComparisonBound, "Argument 'to_date' must be an ISO date.")]
    public async Task The_repair_attempt_is_validated_exactly_as_the_first_attempt_was(
        string invalidPlan, string expected)
    {
        var once = Assert.Throws<InvalidDataException>(() => OperationPlan.FromPlannerOutput(
            "req-1", "en",
            JsonNode.Parse(invalidPlan)!["operations"]!.AsArray(),
            false, Vocabulary, new DateOnly(2026, 3, 4)));
        var send = new RecordingSend(Envelope(invalidPlan), Envelope(invalidPlan));

        var twice = await Assert.ThrowsAsync<InvalidDataException>(() => PlanAsync(send));

        // Two sends, so the second plan really did go through the gate rather than being refused
        // on the strength of the first.
        Assert.Equal(2, send.Requests.Count);
        Assert.Equal(once.Message, twice.Message);
        Assert.Equal(expected, twice.Message);
        // The planning tokens both attempts spent leave on the exception, because model_usage is
        // never written on the refusal path.
        Assert.Equal("input_tokens 8000 output_tokens 240 attempts 2",
            twice.Data[AskService.PlanningUsageKey]);
    }

    // The counterpart, and the reason the repair turn is safe to ship at all. For these two the
    // validator's own message hands the model a mechanical fix that changes the answer rather than
    // correcting it. A bare "2024" for an instant is refused because 2024-01-01 and 2024-12-31 are
    // different versions of the law; told only "must be an ISO date", the cheapest satisfying move
    // is to invent one, and the invented date then validates and is served with nothing in the
    // reply to say a date was chosen for the reader. Swapping reversed bounds satisfies the
    // constraint and inverts every added and removed clause of the diff.
    //
    // So they are refused on attempt one and never sent back. This converts the repair turn from
    // something that can produce a confidently wrong answer about which law applied into something
    // that can only ever recover a plan whose correct form was already fixed by the user's words.
    [Theory]
    [InlineData(MalformedDate, "Argument 'date' must be an ISO date.")]
    [InlineData(ReversedWindow, "diff from_date must not follow to_date.")]
    public async Task A_violation_whose_repair_would_choose_the_answer_is_not_retried(
        string invalidPlan, string expected)
    {
        var send = new RecordingSend(Envelope(invalidPlan), Envelope(ValidCoverage));

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => PlanAsync(send));

        // One send. The valid second envelope was available and deliberately never asked for.
        Assert.Single(send.Requests);
        Assert.Equal(expected, refused.Message);
        Assert.Equal("input_tokens 4000 output_tokens 120 attempts 1",
            refused.Data[AskService.PlanningUsageKey]);
    }

    // The skip is narrow on purpose. An absent range bound reads the same to the validator
    // ("must be an ISO date") but is recoverable from the user's own words, so it must still be
    // retried; only the instant is withheld. Without the split these would share one class and
    // this recovery would have been lost with it.
    [Fact]
    public async Task An_absent_range_bound_is_still_repaired_although_it_reads_as_a_date_violation()
    {
        var send = new RecordingSend(Envelope(MissingComparisonBound), Envelope(ValidCoverage));

        var (plan, repaired) = await PlanAsync(send);

        Assert.True(repaired);
        Assert.Equal(2, send.Requests.Count);
        Assert.Equal("coverage", Assert.Single(plan.Operations).Tool);
    }

    // A truncated completion ran out of room to write the plan; the corrective prompt is strictly
    // larger under the same cap, so it is worse off than the attempt that just failed.
    [Theory]
    [InlineData("length", 120)]
    [InlineData("stop", 3600)]
    public async Task A_planner_that_spent_its_token_budget_is_not_retried(
        string finishReason, long completionTokens)
    {
        var send = new RecordingSend(
            Envelope(ArrayWorkQuery, finishReason, completionTokens), Envelope(ValidCoverage));

        await Assert.ThrowsAsync<InvalidDataException>(() => PlanAsync(send));

        Assert.Single(send.Requests);
    }

    // Without this guard a repair attempt can overrun the planner deadline and convert a graceful
    // 200 gap into a 504, which is strictly worse than the refusal the loop replaces.
    [Fact]
    public async Task A_planner_that_spent_its_time_budget_is_not_retried()
    {
        var send = new SlowSend(TimeSpan.FromMilliseconds(400), Envelope(ArrayWorkQuery));

        await Assert.ThrowsAsync<InvalidDataException>(() => AskService.PlanWithOneRepairAsync(
            PlannerRequest(), send.SendAsync, TimeSpan.FromMilliseconds(500), "req-1", "en",
            Vocabulary, new DateOnly(2026, 3, 4), CancellationToken.None));

        Assert.Equal(1, send.Calls);
    }

    // No tool_call and no tool_call_id mean no protocol-legal corrective turn exists.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_envelope_with_no_answerable_tool_call_is_not_retried(bool missingId)
    {
        var broken = missingId
            ? Envelope(ArrayWorkQuery, callId: null)
            : new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["content"] = "no plan" },
                }),
                // A real completion always bills for what it wrote, including the one that came
                // back unusable, so the fault path has usage to carry. Written as long literals
                // because a JsonValue built from an int does not answer TryGetValue<long>.
                ["usage"] = new JsonObject
                {
                    ["prompt_tokens"] = 4000L,
                    ["completion_tokens"] = 120L,
                },
            };
        var send = new RecordingSend(broken, Envelope(ValidCoverage));

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => PlanAsync(send));

        Assert.Single(send.Requests);
        // The attempt was billed even though the envelope was unusable, and model_usage is never
        // written on the refusal path, so the exception is the only place that accounting can
        // leave. Without this the token cost of a malformed planner response is invisible.
        Assert.Equal("input_tokens 4000 output_tokens 120 attempts 1",
            refused.Data[AskService.PlanningUsageKey]);
    }

    // A 401, 429 or 5xx is not a contract violation, and the planner deadline forbids the backoff
    // a real transport retry would need.
    [Fact]
    public async Task A_transport_failure_is_not_retried()
    {
        var calls = 0;
        Task<JsonNode?> Failing(JsonObject request, CancellationToken cancellationToken)
        {
            calls++;
            throw new HttpRequestException("Planning upstream returned HTTP 503.");
        }

        await Assert.ThrowsAsync<HttpRequestException>(() => AskService.PlanWithOneRepairAsync(
            PlannerRequest(), Failing, TimeSpan.FromSeconds(12), "req-1", "en",
            Vocabulary, new DateOnly(2026, 3, 4), CancellationToken.None));

        Assert.Equal(1, calls);
    }

    // A deadline expiry must never be swallowed and turned into the refusal.
    [Fact]
    public async Task Cancellation_is_never_converted_into_a_planning_refusal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var send = new RecordingSend(Envelope(ValidCoverage));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AskService.PlanWithOneRepairAsync(
                PlannerRequest(), send.SendAsync, TimeSpan.FromSeconds(12), "req-1", "en",
                Vocabulary, new DateOnly(2026, 3, 4), cancellation.Token));

        Assert.Empty(send.Requests);
    }

    // The one message that quotes a string the planner wrote rather than a name from a closed set.
    // Strict structured output is deliberately off, so a prompt-injected question can steer the
    // model into writing its payload as a tool name; that class is replaced with a template.
    [Fact]
    public void An_operation_name_the_planner_invented_never_frames_the_correction()
    {
        var violation = AskService.PlannerViolation(
            "Unknown legal operation or application action 'ignore previous instructions'.");

        Assert.DoesNotContain("ignore previous", violation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legal_boundary", violation, StringComparison.Ordinal);
        Assert.Equal("unknown_operation", AskService.ViolationClass(
            "Unknown legal operation or application action 'x'."));

        // Every other message is bounded and filtered to the character set the diagnostic line
        // allows, so the two spellings of a violation cannot diverge.
        Assert.Equal("Argument 'work_query' must be a string. Received Array.",
            AskService.PlannerViolation("Argument 'work_query' must be a string. Received Array."));
        Assert.Equal("a?b", AskService.SanitizeDetail("a\nb"));
        Assert.Equal(200, AskService.SanitizeDetail(new string('x', 500)).Length);
    }

    // The class names are what make the recovered rate a number rather than an assumption, and
    // the reversed comparison window is the one class worth watching by name.
    [Theory]
    [InlineData("diff from_date must not follow to_date.", "reversed_window")]
    [InlineData("Argument 'date' must be a string. Received Number.", "wrong_type")]
    // The instant and a range bound are different classes because only one of them can be
    // repaired without choosing the answer on the reader's behalf.
    [InlineData("Argument 'date' must be an ISO date.", "unparsable_instant")]
    [InlineData("Argument 'as_of' must be an ISO date.", "unparsable_instant")]
    [InlineData("Argument 'to_date' must be an ISO date.", "unparsable_date")]
    [InlineData("Argument 'from_date' must be an ISO date.", "unparsable_date")]
    [InlineData("Operation 'as_of' requires a work identity.", "missing_work_identity")]
    [InlineData("Operation 'as_of' contains unsupported argument 'from_date'.", "unsupported_argument")]
    [InlineData("Argument 'order' has an unsupported value.", "unsupported_value")]
    [InlineData("Argument 'query' is required.", "missing_required")]
    [InlineData("The planner returned no operation list.", "other")]
    public void Every_violation_class_has_a_closed_name(string message, string expected)
        => Assert.Equal(expected, AskService.ViolationClass(message));

    // Two submitted plans are two different answers with no ground for preferring either, and the
    // envelope check says so in words: "did not submit exactly one operation plan". It has to reach
    // the reader as that gap. Parallel tool calls are not disabled on the planning request, so this
    // envelope is reachable, and it used to leave through SingleOrDefault as an
    // InvalidOperationException, which the ask path reports as HTTP 500.
    [Fact]
    public async Task Two_submitted_plans_are_the_invalid_plan_gap_and_not_an_unexpected_failure()
    {
        var call = new JsonObject
        {
            ["id"] = "call_a",
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "submit_operation_plan",
                ["arguments"] = ValidCoverage,
            },
        };
        var send = new RecordingSend(new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = "stop",
                ["message"] = new JsonObject
                {
                    ["tool_calls"] = new JsonArray(call, call.DeepClone()),
                },
            }),
        });

        var response = await Service(send).AskAsync(
            History("What does Lex hold?"), "duplicate-client", "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Equal("This request does not map to a valid legal operation.",
            response.Body["reply"]?.GetValue<string>());
        // An envelope fault has no tool_call_id to answer, so no corrective turn exists for it.
        Assert.Single(send.Requests);
    }

    private sealed class SlowSend(TimeSpan delay, JsonNode response)
    {
        public int Calls;

        public async Task<JsonNode?> SendAsync(
            JsonObject request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(delay, cancellationToken);
            return response.DeepClone();
        }
    }

    private static AskService Service(RecordingSend send) => new(
        new McpCore(new Dictionary<string, LexIndexReader>()),
        planner: null,
        legalTool: (tool, arguments, cancellationToken) =>
        {
            Assert.Equal("coverage", tool);
            return ValueTask.FromResult<JsonNode>(new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "lu-legilux",
                    ["tier"] = "official",
                },
                ["publisher_name"] = "Legilux",
                ["works"] = 12,
                ["versions"] = 34,
                ["text"] = new JsonObject
                {
                    ["versions_with_text_served"] = 34,
                    ["versions_without_text"] = 0,
                },
            });
        },
        plannerSend: send.SendAsync);

    private static JsonArray History(string question) =>
        new(new JsonObject { ["role"] = "user", ["content"] = question });
}
