using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;

namespace Lex.Tests;

public sealed class OperationPolicyTests
{
    [Fact]
    public void Assistant_operation_bounds_are_a_subset_of_the_public_mcp_contract()
    {
        var fifty = string.Join(',', Enumerable.Range(1, 50).Select(index => $"a{index}"));
        var cases = new Dictionary<string, JsonObject>
        {
            ["search"] = new() { ["query"] = new string('q', 1_000), ["publisher"] = new string('p', 64), ["jurisdiction"] = new string('j', 64), ["language"] = new string('l', 16), ["works"] = fifty, ["limit"] = 50 },
            ["as_of"] = new() { ["work"] = "eu-eurlex:work", ["date"] = "2026-01-01", ["mode"] = "select", ["anchors"] = fifty, ["language"] = new string('l', 16) },
            ["timeline"] = new() { ["work"] = "eu-eurlex:work", ["limit"] = 200, ["offset"] = 100_000 },
            ["in_force_on"] = new() { ["date"] = "2026-01-01", ["limit"] = 100, ["offset"] = 100_000, ["publisher"] = new string('p', 64) },
            ["diff"] = new() { ["work"] = "eu-eurlex:work", ["from_date"] = "2025-01-01", ["to_date"] = "2026-01-01", ["anchor"] = new string('a', 512) },
            ["article_history"] = new() { ["work"] = "eu-eurlex:work", ["anchor"] = new string('a', 512) },
            ["provenance"] = new() { ["lex_id"] = "eu-eurlex:work:2026-01-01" },
            ["coverage"] = new() { ["publisher"] = new string('p', 64) },
            ["cited_by"] = new() { ["work"] = "eu-eurlex:work", ["limit"] = 100 },
            ["changes_in_period"] = new() { ["from_date"] = "2025-01-01", ["to_date"] = "2026-01-01", ["limit"] = 100, ["offset"] = 100_000 },
        };

        foreach (var (tool, proposed) in cases)
            McpInputPolicy.Validate(tool, OperationArguments.Normalize(tool, proposed));
    }

    [Fact]
    public void Assistant_rejects_values_one_past_mcp_bounds_before_execution()
    {
        var cases = new (string Tool, JsonObject Arguments)[]
        {
            ("coverage", new JsonObject { ["publisher"] = new string('p', 65) }),
            ("as_of", new JsonObject { ["work"] = "eu-eurlex:work", ["date"] = "2026-01-01", ["language"] = new string('l', 17) }),
            ("diff", new JsonObject { ["work"] = "eu-eurlex:work", ["from_date"] = "2025-01-01", ["to_date"] = "2026-01-01", ["anchor"] = new string('a', 513) }),
            ("as_of", new JsonObject { ["work"] = "eu-eurlex:work", ["date"] = "2026-01-01", ["mode"] = "select", ["anchors"] = string.Join(',', Enumerable.Range(1, 51).Select(index => $"a{index}")) }),
            ("in_force_on", new JsonObject { ["date"] = "2026-01-01", ["limit"] = 101 }),
            ("timeline", new JsonObject { ["work"] = "eu-eurlex:work", ["offset"] = 100_001 }),
        };

        foreach (var (tool, arguments) in cases)
            Assert.Throws<InvalidDataException>(() => OperationArguments.Normalize(tool, arguments));
    }

    // The planner schema and the argument allowlist are one contract in two places. When they
    // drift the model emits arguments Normalize rejects, and one rejection aborts the whole plan.
    [Fact]
    public void Every_planner_tool_offers_exactly_the_arguments_its_operation_allows()
    {
        var branches = AskService.PlannerTools()[0]!["function"]!["parameters"]!["properties"]!
            ["operations"]!["items"]!["oneOf"]!.AsArray();

        var tools = new List<string>();
        foreach (var node in branches)
        {
            var branch = node!.AsObject();
            Assert.Equal(["tool", "arguments"],
                branch["required"]!.AsArray().Select(item => item!.GetValue<string>()));
            var tool = Assert.Single(branch["properties"]!["tool"]!["enum"]!.AsArray()
                .Select(item => item!.GetValue<string>()));
            tools.Add(tool);

            var schema = branch["properties"]!["arguments"]!.AsObject();
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());
            var properties = schema["properties"]!.AsObject();
            Assert.Equal(
                OperationArguments.AllowedFor(tool).Order(StringComparer.Ordinal),
                properties.Select(item => item.Key).Order(StringComparer.Ordinal));
            foreach (var (name, value) in properties)
                Assert.Equal(
                    name switch
                    {
                        "limit" or "offset" => "integer",
                        "options" => "array",
                        _ => "string",
                    },
                    value!["type"]!.GetValue<string>());
        }

        Assert.Equal([
            "search", "as_of", "diff", "timeline", "article_history", "changes_in_period",
            "in_force_on", "coverage", "cited_by", "provenance", "legal_boundary",
            "clarification",
        ], tools);
        // navigate and gap are application-internal actions and stay off the planner surface.
        Assert.Equal(["gap", "navigate"],
            OperationArguments.Actions.Except(tools, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    public static TheoryData<string, string> ShippedPlannerOperations => new()
    {
        { "search", """{"tool":"search","arguments":{"query":"renewable energy","limit":10}}""" },
        { "as_of", """{"tool":"as_of","arguments":{"work_query":"GDPR","article_number":"6","date":"2021-01-01"}}""" },
        { "diff", """{"tool":"diff","arguments":{"work_query":"CRR","article_number":"92","from_date":"2020-01-01","to_date":"2024-12-31"}}""" },
        { "article_history", """{"tool":"article_history","arguments":{"work_query":"CRR","article_number":"92"}}""" },
        { "changes_in_period", """{"tool":"changes_in_period","arguments":{"from_date":"2024-01-01","to_date":"2024-12-31","order":"by_churn"}}""" },
    };

    [Theory]
    [MemberData(nameof(ShippedPlannerOperations))]
    public void Shipped_planner_output_shapes_freeze_into_a_plan(string tool, string operation)
    {
        var plan = OperationPlan.FromPlannerOutput(
            "req-1", "en", new JsonArray(JsonNode.Parse(operation)));

        Assert.Equal(tool, Assert.Single(plan.Operations).Tool);
    }

    [Fact]
    public void A_planner_argument_outside_the_allowlist_still_aborts_the_plan()
    {
        var rejected = Assert.Throws<InvalidDataException>(() => OperationPlan.FromPlannerOutput(
            "req-1", "en", new JsonArray(JsonNode.Parse(
                """{"tool":"search","arguments":{"query":"renewable energy","order":"by_churn"}}"""))));

        Assert.Contains("'search'", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("'order'", rejected.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, LegalOutcome> StatusCases => new()
    {
        { McpStatus.Ok, LegalOutcome.Succeeded },
        { McpStatus.NoResult, LegalOutcome.SucceededEmpty },
        { McpStatus.NoChangesInPeriod, LegalOutcome.SucceededEmpty },
        { McpStatus.ProfilesDiffer, LegalOutcome.NotComparable },
        { McpStatus.UnknownWork, LegalOutcome.NotFound },
        { McpStatus.UnknownAnchor, LegalOutcome.NotFound },
        { McpStatus.NoVersionForDate, LegalOutcome.NotAvailable },
        { McpStatus.AnchorNotInVersion, LegalOutcome.NotAvailable },
        { McpStatus.NoProvisionHistory, LegalOutcome.NotAvailable },
        { McpStatus.TextNotAvailable, LegalOutcome.NotAvailable },
        { McpStatus.TextWithheld, LegalOutcome.NotAvailable },
        { McpStatus.NoCorpusMounted, LegalOutcome.NotAvailable },
    };

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void Every_public_mcp_status_has_one_legal_outcome(
        string status, LegalOutcome expected)
    {
        Assert.Equal(expected, LegalOperationPolicy.OutcomeForStatus(status));
    }

    [Fact]
    public void The_status_table_is_exhaustive_and_rejects_removed_or_invented_values()
    {
        Assert.Equal(McpStatus.All.Order(), StatusCases.Select(item => (string)item[0]).Order());
        Assert.Throws<InvalidDataException>(() =>
            LegalOperationPolicy.OutcomeForStatus("outside_observed_window"));
        Assert.Throws<InvalidDataException>(() =>
            LegalOperationPolicy.OutcomeForStatus("plausible_but_unknown"));
    }

    [Theory]
    [InlineData("search", LegalResultClass.Navigate)]
    [InlineData("as_of", LegalResultClass.ExactText)]
    [InlineData("diff", LegalResultClass.Comparison)]
    [InlineData("timeline", LegalResultClass.Timeline)]
    [InlineData("article_history", LegalResultClass.Timeline)]
    [InlineData("changes_in_period", LegalResultClass.Ranking)]
    [InlineData("in_force_on", LegalResultClass.Inventory)]
    [InlineData("coverage", LegalResultClass.Inventory)]
    [InlineData("cited_by", LegalResultClass.Inventory)]
    [InlineData("provenance", LegalResultClass.Verification)]
    public void Every_public_tool_has_one_primary_result_class(
        string tool, LegalResultClass expected)
    {
        Assert.Equal(expected, LegalOperationPolicy.ResultClassFor(tool));
    }

    [Fact]
    public void The_live_public_tool_catalog_is_covered_by_the_operation_policy()
    {
        var tools = new McpCore(new Dictionary<string, LexIndexReader>())
            .ToolDefs().OfType<JsonObject>()
            .Select(item => item["name"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(10, tools.Length);
        Assert.All(tools, tool => _ = LegalOperationPolicy.ResultClassFor(tool));
    }

    [Fact]
    public void Mcp_2_advertises_the_closed_status_contract()
    {
        Assert.Equal("2.0.0", McpSdkBridge.ServerVersion);
        Assert.DoesNotContain("outside_observed_window", McpSdkBridge.ServerInstructions);
    }

    [Fact]
    public void A_plan_is_ordered_bounded_and_carries_immutable_arguments()
    {
        var arguments = new JsonObject { ["work"] = "eu-eurlex:32013r0575" };
        var operation = RequestedOperation.Create(
            "op-1", 0, "diff", arguments, requiresWorkResolution: true,
            [SupportingCallRole.WorkResolution, SupportingCallRole.AnchorResolution],
            [OperationEffect.Diff, OperationEffect.Gap]);
        var plan = OperationPlan.Create("req-1", "en", [operation]);
        arguments["work"] = "tampered";

        Assert.Equal("eu-eurlex:32013r0575",
            plan.Operations[0].Arguments.GetProperty("work").GetString());
        var outOfOrder = RequestedOperation.Create(
            "op-2", 1, "coverage", new JsonObject(), false, [],
            [OperationEffect.Coverage, OperationEffect.Gap]);
        Assert.Throws<InvalidDataException>(() => OperationPlan.Create("req-2", "en",
            [outOfOrder]));
    }

    [Fact]
    public void Planner_output_is_converted_to_server_owned_contracts_before_execution()
    {
        var operations = new JsonArray(
            new JsonObject
            {
                ["tool"] = "changes_in_period",
                ["arguments"] = new JsonObject
                {
                    ["from_date"] = "2024-01-01",
                    ["to_date"] = "2024-12-31",
                    ["order"] = "by_churn",
                },
            },
            new JsonObject
            {
                ["tool"] = "diff",
                ["arguments"] = new JsonObject
                {
                    ["work_query"] = "CRR",
                    ["article_number"] = "92",
                    ["from_date"] = "2020-01-01",
                    ["to_date"] = "2024-12-31",
                },
            });

        var plan = OperationPlan.FromPlannerOutput("req-1", "en", operations);

        Assert.Equal(["changes_in_period", "diff"], plan.Operations.Select(item => item.Tool));
        Assert.False(plan.Operations[0].RequiresWorkResolution);
        Assert.True(plan.Operations[1].RequiresWorkResolution);
        Assert.Contains(SupportingCallRole.AnchorResolution, plan.Operations[1].SupportingCalls);
        Assert.Equal(
            [OperationEffect.Ranking, OperationEffect.Gap, OperationEffect.Workspace],
            plan.Operations[0].Effects.ToArray());
    }

    [Theory]
    [InlineData("as_of", "{}")]
    [InlineData("diff", "{\"work_query\":\"CRR\",\"from_date\":\"2020\",\"to_date\":\"2024-12-31\"}")]
    [InlineData("article_history", "{\"work_query\":\"CRR\"}")]
    [InlineData("search", "{}")]
    public void Planner_output_fails_closed_before_any_execution(string tool, string argumentsJson)
    {
        var operations = new JsonArray(new JsonObject
        {
            ["tool"] = tool,
            ["arguments"] = JsonNode.Parse(argumentsJson),
        });

        Assert.Throws<InvalidDataException>(() =>
            OperationPlan.FromPlannerOutput("req", "en", operations));
    }

    [Fact]
    public void A_plan_rejects_unbounded_identifiers_arguments_and_operation_counts()
    {
        Assert.Throws<InvalidDataException>(() => RequestedOperation.Create(
            new string('o', RequestedOperation.MaximumIdentifierLength + 1), 0, "coverage",
            new JsonObject(), false, [], [OperationEffect.Coverage, OperationEffect.Gap]));
        Assert.Throws<InvalidDataException>(() => RequestedOperation.Create(
            "op", 0, "coverage",
            new JsonObject { ["publisher"] = new string('x', RequestedOperation.MaximumArgumentBytes) },
            false, [], [OperationEffect.Coverage, OperationEffect.Gap]));

        var operations = Enumerable.Range(0, OperationPlan.MaximumOperations + 1)
            .Select(index => RequestedOperation.Create(
                $"op-{index}", index, "coverage", new JsonObject(), false, [],
                [OperationEffect.Coverage, OperationEffect.Gap]));
        Assert.Throws<InvalidDataException>(() => OperationPlan.Create("req", "en", operations));
        Assert.Throws<InvalidDataException>(() => RequestedOperation.Create(
            "op", 0, "as_of", new JsonObject(), true, [], [OperationEffect.Ranking]));
        Assert.Throws<InvalidDataException>(() => RequestedOperation.Create(
            "op", 0, "coverage", new JsonObject(), false, [], [OperationEffect.Gap]));
        Assert.Throws<InvalidDataException>(() => RequestedOperation.Create(
            "op", 0, "coverage", new JsonObject(), false, [], [OperationEffect.Coverage]));

        Assert.Throws<InvalidDataException>(() => OperationPlan.FromPlannerOutput(
            "req", "en", new JsonArray(new JsonObject
            {
                ["tool"] = "diff",
                ["arguments"] = new JsonObject
                {
                    ["work_query"] = new string('w', 901),
                    ["article_number"] = "92",
                    ["from_date"] = "2020-01-01",
                    ["to_date"] = "2024-12-31",
                },
            })));
        Assert.Throws<InvalidDataException>(() => OperationPlan.FromPlannerOutput(
            "req", "en", new JsonArray(new JsonObject
            {
                ["tool"] = "as_of",
                ["arguments"] = new JsonObject
                {
                    ["work_query"] = "CRR",
                    ["article_number"] = new string('9', 65),
                },
            })));
    }

    [Fact]
    public void No_mounted_corpus_is_a_typed_readiness_gap()
    {
        var effect = UiMapper.From("coverage", new JsonObject(),
            new JsonObject { ["status"] = McpStatus.NoCorpusMounted });

        Assert.Equal(McpStatus.NoCorpusMounted, effect.Gap?.Status);
        Assert.Contains("no verified legal index", effect.Gap?.Explanation);
        Assert.Throws<InvalidDataException>(() => UiMapper.From("as_of", new JsonObject(),
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = "invented_future_status" },
                ["provisions"] = new JsonArray { new JsonObject { ["anchor"] = "art_1" } },
            }));
    }

    [Fact]
    public void Successful_empty_aggregates_remain_typed_empty_results_not_corpus_gaps()
    {
        var ranking = UiMapper.From("changes_in_period", new JsonObject(), new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.NoChangesInPeriod },
            ["window"] = new JsonObject { ["from"] = "2024-01-01", ["to"] = "2024-12-31" },
            ["order"] = "by_churn",
            ["works_changed"] = 0,
            ["new_versions"] = 0,
            ["changes"] = new JsonArray(),
        });
        var cited = UiMapper.From("cited_by", new JsonObject(), new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
            ["cited_work"] = "t:w",
            ["citing_articles"] = 0,
            ["citations"] = new JsonArray(),
        });

        Assert.Equal(McpStatus.NoChangesInPeriod, ranking.Ranking?.Status);
        Assert.Empty(ranking.Ranking?.Rows ?? []);
        Assert.Null(ranking.Gap);
        Assert.Equal(McpStatus.NoResult, cited.CitedBy?.Status);
        Assert.Empty(cited.CitedBy?.Rows ?? []);
        Assert.Null(cited.Gap);
    }

    [Fact]
    public void Coverage_and_provenance_have_explicit_typed_panel_effects()
    {
        var coverage = UiMapper.From("coverage", new JsonObject(), new JsonArray(new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["tier"] = "A",
                ["status"] = McpStatus.Ok,
                ["freshness"] = new JsonObject { ["stamp_signature_valid"] = true },
            },
            ["publisher_name"] = "Legilux",
            ["works"] = 1399,
            ["versions"] = 4705,
            ["text"] = new JsonObject
            {
                ["versions_with_text_served"] = 1000,
                ["versions_without_text"] = 3705,
            },
            ["known_gaps"] = new JsonArray("as-published acts"),
        }));
        var provenance = UiMapper.From("provenance", new JsonObject(), new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
            ["document"] = new JsonObject
            {
                ["lex_id"] = "lu-legilux:w:2024-01-01",
                ["title"] = "A law",
                ["source_uri"] = "https://example.test/source",
                ["record_sha256"] = "record",
                ["body_sha256"] = "body",
            },
            ["stamp"] = new JsonObject
            {
                ["signature_valid"] = true,
                ["algorithm"] = "ECDSA-P256-SHA256",
            },
        });

        Assert.Equal(1399, Assert.Single(coverage.Coverage!.Publishers).Works);
        Assert.True(coverage.Coverage.Publishers[0].SignatureValid);
        Assert.Equal("lu-legilux:w:2024-01-01", provenance.Verification?.LexId);
        Assert.True(provenance.Verification?.SignatureValid);
    }

    [Fact]
    public void Navigate_maps_the_resolved_subject_to_one_typed_workspace_destination()
    {
        var operation = RequestedOperation.CreatePlanned("nav", 0, "navigate",
            new JsonObject
            {
                ["work"] = "eu-eurlex:32013r0575",
                ["date"] = "2024-01-01",
                ["article_number"] = "92",
            });
        var executed = new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["date"] = "2024-01-01",
            ["anchor"] = "art_92",
        };

        var effect = UiMapper.From(operation, executed,
            new JsonObject { ["status"] = McpStatus.Ok });

        Assert.Equal("eu-eurlex:32013r0575", effect.Workspace?.Work);
        Assert.Equal("2024-01-01", effect.Workspace?.Date);
        Assert.Equal("art_92", effect.Workspace?.Anchor);
    }

    [Fact]
    public void Legal_effects_preserve_bounded_evidence_and_future_state_semantics()
    {
        var arguments = new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["date"] = "2027-01-01",
            ["mode"] = "select",
            ["anchors"] = "art_92",
        };
        var payload = new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "eu-eurlex",
                ["jurisdiction"] = "EU",
                ["timeline_semantics"] = "official_consolidation_state",
                ["provisional"] = true,
                ["freshness"] = new JsonObject
                {
                    ["last_confirmed_at"] = "2026-08-10T00:00:00Z",
                    ["stamp_signature_valid"] = true,
                },
                ["artifact"] = new JsonObject
                {
                    ["manifest_set_id"] = new string('a', 64),
                    ["content_digest"] = new string('b', 64),
                },
            },
            ["document"] = new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32013r0575:2026-01-01",
                ["valid_from"] = "2026-01-01",
                ["source_uri"] = "https://example.test/source",
                ["extraction_profile"] = "eurlex-xhtml-v1",
                ["record_sha256"] = "record",
                ["body_sha256"] = "body",
            },
            ["provisions"] = new JsonArray(new JsonObject
            {
                ["anchor"] = "art_92",
                ["text"] = "Publisher text",
                ["text_sha256"] = "text",
            }),
        };
        var operation = RequestedOperation.CreatePlanned(
            "op", 0, "as_of", arguments);
        var result = new OperationExecution(operation).Complete(McpStatus.Ok, payload);
        var effect = UiMapper.From(operation, arguments, payload);

        var evidence = Assert.Single(effect.Provision?.Evidence ?? []);
        Assert.Equal("eu-eurlex", evidence.Publisher);
        Assert.Equal("official_consolidation_state", evidence.TimelineSemantics);
        Assert.Equal("2027-01-01", evidence.RequestedDate);
        Assert.True(evidence.Provisional);
        Assert.Equal(new string('a', 64), evidence.ArtifactManifestId);
        Assert.Contains("publisher version dates",
            OperationAnswerPolicy.Render("en", [result],
            [
                new UiEffect(Ranking: new RankingView(
                    "2027-01-01", "2027-12-31", "by_churn", 1, 1, [],
                    Evidence: effect.Provision?.Evidence))
            ]));
        Assert.Contains("provisional",
            OperationAnswerPolicy.Render("en", [result], [effect]),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void French_gaps_are_localized_and_invalid_signatures_remain_failed()
    {
        var gap = UiMapper.From("as_of",
            new JsonObject { ["date"] = "2020-01-01" },
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.UnknownWork },
                ["work"] = "eu-eurlex:unknown",
            },
            "fr");
        var verification = UiMapper.From("provenance", new JsonObject(),
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["document"] = new JsonObject { ["lex_id"] = "eu-eurlex:w:2024-01-01" },
                ["stamp"] = new JsonObject { ["signature_valid"] = false },
            });

        Assert.Contains("détient", gap.Gap?.Explanation);
        Assert.False(verification.Verification?.SignatureValid);
    }

    [Fact]
    public void An_execution_completes_once_and_transport_failure_cannot_claim_legal_success()
    {
        var requested = RequestedOperation.Create(
            "op-1", 0, "as_of", new JsonObject { ["work"] = "t:w", ["date"] = "2024-01-01" },
            requiresWorkResolution: true, [SupportingCallRole.WorkResolution],
            [OperationEffect.Provision, OperationEffect.Gap]);
        var execution = new OperationExecution(requested);
        var result = execution.CompleteTransport(TransportOutcome.TimedOut);

        Assert.Equal(LegalOutcome.NotEvaluated, result.LegalOutcome);
        Assert.Equal(TransportOutcome.TimedOut, result.TransportOutcome);
        Assert.Equal([OperationEffect.Gap], result.Effects.ToArray());
        Assert.Throws<InvalidOperationException>(() => execution.Complete(
            McpStatus.Ok, new JsonObject { ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok } }));
    }

    [Theory]
    [InlineData(LegalOutcome.NeedsClarification)]
    [InlineData(LegalOutcome.InvalidRequest)]
    [InlineData(LegalOutcome.LegalBoundary)]
    public void Application_owned_legal_outcomes_are_terminal_without_an_invented_mcp_status(
        LegalOutcome outcome)
    {
        var requested = RequestedOperation.Create(
            "op-1", 0, "diff", new JsonObject(), true,
            [SupportingCallRole.WorkResolution], [OperationEffect.Diff, OperationEffect.Gap]);
        var execution = new OperationExecution(requested);

        var result = execution.CompleteLegal(outcome);

        Assert.Equal(outcome, result.LegalOutcome);
        Assert.Equal(TransportOutcome.Completed, result.TransportOutcome);
        Assert.Equal([OperationEffect.Gap], result.Effects.ToArray());
    }

    [Fact]
    public void Application_completion_cannot_bypass_an_authoritative_tool_result()
    {
        var exact = RequestedOperation.Create(
            "exact", 0, "as_of", new JsonObject(), true, [],
            [OperationEffect.Provision, OperationEffect.Gap]);
        var comparison = RequestedOperation.Create(
            "comparison", 0, "diff", new JsonObject(), true, [],
            [OperationEffect.Diff, OperationEffect.Gap]);
        var navigation = RequestedOperation.Create(
            "navigation", 0, "navigate", new JsonObject(), true,
            [SupportingCallRole.WorkResolution], [OperationEffect.Workspace, OperationEffect.Gap]);

        Assert.Throws<InvalidDataException>(() =>
            new OperationExecution(exact).CompleteLegal(LegalOutcome.Succeeded));
        Assert.Throws<InvalidDataException>(() =>
            new OperationExecution(comparison).CompleteLegal(LegalOutcome.NotComparable));
        Assert.Equal(LegalOutcome.Succeeded,
            new OperationExecution(navigation).CompleteLegal(LegalOutcome.Succeeded).LegalOutcome);
    }

    [Fact]
    public void Supporting_calls_cannot_complete_or_reorder_requested_operations()
    {
        var first = RequestedOperation.Create("op-a", 0, "changes_in_period", new JsonObject(),
            false, [], [OperationEffect.Ranking, OperationEffect.Gap]);
        var second = RequestedOperation.Create("op-b", 1, "diff", new JsonObject(),
            true, [SupportingCallRole.WorkResolution], [OperationEffect.Diff, OperationEffect.Gap]);
        var plan = OperationPlan.Create("req", "fr", [first, second]);
        var run = OperationRun.Start(plan);

        run.ObserveSupportingCall("op-b", SupportingCallRole.WorkResolution, "search",
            new JsonObject { ["query"] = "CRR" }, McpStatus.Ok, new JsonObject());

        Assert.All(run.Executions, item => Assert.Equal(OperationExecutionState.Pending, item.State));
        Assert.Equal(["op-a", "op-b"], run.Executions.Select(item => item.Request.OperationId));
    }

    [Fact]
    public void Supporting_calls_reject_wrong_tools_modes_and_contradictory_payloads()
    {
        var requested = RequestedOperation.Create(
            "op", 0, "diff", new JsonObject(), true,
            [SupportingCallRole.WorkResolution, SupportingCallRole.AnchorResolution],
            [OperationEffect.Diff, OperationEffect.Gap]);
        var run = OperationRun.Start(OperationPlan.Create("req", "en", [requested]));

        Assert.Throws<InvalidDataException>(() => run.ObserveSupportingCall(
            "op", SupportingCallRole.WorkResolution, "diff", new JsonObject(),
            McpStatus.Ok, new JsonObject()));
        Assert.Throws<InvalidDataException>(() => run.ObserveSupportingCall(
            "op", SupportingCallRole.AnchorResolution, "as_of",
            new JsonObject { ["work"] = "t:w", ["mode"] = "full" },
            McpStatus.Ok, new JsonObject()));
        Assert.Throws<InvalidDataException>(() => run.ObserveSupportingCall(
            "op", SupportingCallRole.WorkResolution, "search",
            new JsonObject { ["query"] = "CRR" }, McpStatus.Ok,
            new JsonObject { ["status"] = McpStatus.UnknownWork }));
    }

    [Fact]
    public void A_primary_result_cannot_claim_success_over_a_failure_payload()
    {
        var requested = RequestedOperation.Create(
            "op", 0, "provenance", new JsonObject(), true,
            [SupportingCallRole.WorkResolution], [OperationEffect.Verification, OperationEffect.Gap]);
        var execution = new OperationExecution(requested);

        Assert.Throws<InvalidDataException>(() => execution.Complete(McpStatus.Ok,
            new JsonObject { ["status"] = McpStatus.UnknownWork }));
        Assert.Equal(OperationExecutionState.Pending, execution.State);
    }

    [Fact]
    public void Cancellation_terminally_completes_each_remaining_operation_in_user_order()
    {
        var operations = Enumerable.Range(0, 2).Select(index => RequestedOperation.Create(
            $"op-{index}", index, index == 0 ? "coverage" : "timeline", new JsonObject(),
            index == 1, [], index == 0
                ? [OperationEffect.Coverage, OperationEffect.Gap]
                : [OperationEffect.Timeline, OperationEffect.Gap])).ToArray();
        var run = OperationRun.Start(OperationPlan.Create("req", "en", operations));

        var results = run.CompletePending(TransportOutcome.Cancelled);

        Assert.Equal(["op-0", "op-1"], results.Select(item => item.OperationId));
        Assert.All(results, item =>
        {
            Assert.Equal(LegalOutcome.NotEvaluated, item.LegalOutcome);
            Assert.Equal(TransportOutcome.Cancelled, item.TransportOutcome);
        });
    }

    [Fact]
    public async Task Cancellation_and_legal_completion_race_without_losing_later_operations()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var operations = Enumerable.Range(0, 3).Select(index => RequestedOperation.Create(
                $"op-{index}", index, "coverage", new JsonObject(), false, [],
                [OperationEffect.Coverage, OperationEffect.Gap])).ToArray();
            var run = OperationRun.Start(OperationPlan.Create($"req-{attempt}", "en", operations));
            var payload = new JsonObject { ["status"] = McpStatus.Ok };

            await Task.WhenAll(
                Task.Run(() => run.TryComplete("op-0", McpStatus.Ok, payload, out _)),
                Task.Run(() => run.CompletePending(TransportOutcome.Cancelled)));

            Assert.All(run.Executions, execution =>
                Assert.Equal(OperationExecutionState.Completed, execution.State));
            Assert.Equal(["op-0", "op-1", "op-2"],
                run.Executions.Select(execution => execution.Result?.OperationId));
        }
    }

    [Fact]
    public void Completion_and_ui_mapping_enforce_the_frozen_effects()
    {
        var requested = RequestedOperation.Create(
            "op", 0, "changes_in_period", new JsonObject(), false, [],
            [OperationEffect.Ranking, OperationEffect.Workspace, OperationEffect.Gap]);
        var execution = new OperationExecution(requested);
        var payload = new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.NoChangesInPeriod },
            ["window"] = new JsonObject { ["from"] = "2024-01-01", ["to"] = "2024-12-31" },
            ["changes"] = new JsonArray(),
        };

        var result = execution.Complete(McpStatus.NoChangesInPeriod, payload);
        var effect = UiMapper.From(requested, payload);

        Assert.Equal([OperationEffect.Ranking], result.Effects.ToArray());
        Assert.NotNull(effect.Ranking);
        Assert.Throws<InvalidDataException>(() => UiMapper.ValidateEffects(
            requested, new UiEffect(Provision: new ProvisionView(
                new Subject("t:w", null, null, null), "2024-01-01", null, [], null))));
    }
}
