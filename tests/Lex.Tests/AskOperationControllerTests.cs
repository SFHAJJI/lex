using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;

namespace Lex.Tests;

public sealed class AskOperationControllerTests : IDisposable
{
    private readonly string _db = Path.Combine(
        Path.GetTempPath(), $"lex-ask-plan-{Guid.NewGuid():N}.db");
    private readonly LexIndexReader _reader;
    private readonly McpCore _core;

    public AskOperationControllerTests()
    {
        var first = Doc("2020-01-01", "2023-12-31", "old capital requirement");
        var second = Doc("2024-01-01", null, "new capital requirement");
        var gdpr = Doc("32016r0679", "2018-05-25", null,
            "lawful processing of personal data");
        var dora = Doc("32022r2554", "2024-01-01", null,
            "operational resilience requirements zebrafalcon");
        var whole = Doc("32024r0001", "2024-01-01", null,
            "This whole document is authoritative publisher text.");
        IndexBuilder.Build(_db, new Dictionary<string, string>
        {
            ["collection"] = "eu-eurlex",
            ["jurisdiction"] = "EU",
            ["tier"] = "A",
            ["history_begins"] = "publisher",
            ["built_at"] = "2023-06-01T00:00:00Z",
            ["corpus_commit"] = "test",
        }, [first, second, gdpr, dora, whole],
        [
            Provision(first, "old capital requirement"),
            Provision(second, "new capital requirement"),
            Provision(gdpr, "lawful processing of personal data", "art_6", "Article 6"),
            Provision(dora, "operational resilience requirements zebrafalcon"),
        ], [], [], StampSigner.CreateKeyPem(),
        provisionStates:
        [
            new ProvisionStateRow("32013r0575", "en", true, "art_92",
                "2020-01-01", "2023-12-31", Hash("old capital requirement"),
                first.Key, null, false),
            new ProvisionStateRow("32013r0575", "en", true, "art_92",
                "2024-01-01", null, Hash("new capital requirement"),
                second.Key, null, false),
        ],
        workSearch: new WorkSearchBuildOptions(
            [
                new ReviewedWorkAliasRow("32013r0575", "en", "CRR", "test"),
                new ReviewedWorkAliasRow("32016r0679", "en", "GDPR", "test"),
                new ReviewedWorkAliasRow("32022r2554", "en", "DORA", "test"),
                new ReviewedWorkAliasRow("32024r0001", "en", "WHOLE", "test"),
            ],
            [], new string('a', 64)));
        _reader = LexIndexReader.Open(_db);
        _core = new McpCore(new Dictionary<string, LexIndexReader>
        {
            ["eu-eurlex"] = _reader,
        });
    }

    public static TheoryData<string, string, string, string, string, string, string> OperationCases =>
        new()
        {
            {
                "search", "Find capital requirement provisions.",
                """{"query":"capital requirement","publisher":"eu-eurlex","limit":3}""",
                "navigate", "succeeded", "workspace", "workspace"
            },
            {
                "navigate", "Open CRR as it stood on 1 January 2021.",
                """{"work_query":"CRR","date":"2021-01-01"}""",
                "navigate", "succeeded", "workspace", "workspace"
            },
            {
                "as_of", "Show Article 6 of the GDPR as it stood on 1 January 2021.",
                """{"work_query":"GDPR","article_number":"6","date":"2021-01-01","mode":"full"}""",
                "exact_text", "succeeded", "provision", "provision"
            },
            {
                "timeline", "Show the CRR timeline.",
                """{"work_query":"CRR"}""",
                "timeline", "succeeded", "timeline", "timeline"
            },
            {
                "article_history", "When did Article 92 of CRR change?",
                """{"work_query":"CRR","article_number":"92"}""",
                "timeline", "succeeded", "history", "history"
            },
            {
                "changes_in_period", "Which EU laws changed most during 2024?",
                """{"from_date":"2024-01-01","to_date":"2024-12-31","order":"by_churn"}""",
                "ranking", "succeeded", "ranking", "ranking"
            },
            {
                "in_force_on", "Which EU laws were in force on 1 June 2024?",
                """{"date":"2024-06-01","publisher":"eu-eurlex"}""",
                "inventory", "succeeded", "in_force", "in_force"
            },
            {
                "coverage", "Show mounted legal coverage.", "{}",
                "inventory", "succeeded", "coverage", "coverage"
            },
            {
                "cited_by", "Which provisions cite CRR?", """{"work_query":"CRR"}""",
                "inventory", "succeeded_empty", "cited_by", "cited_by"
            },
            {
                "provenance", "Verify eu-eurlex:32013r0575:2020-01-01.",
                """{"lex_id":"eu-eurlex:32013r0575:2020-01-01"}""",
                "verification", "succeeded", "verification", "verification"
            },
        };

    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task Every_legal_operation_has_one_canonical_typed_result(
        string tool,
        string question,
        string argumentsJson,
        string expectedResultClass,
        string expectedOutcome,
        string expectedEffect,
        string expectedView)
    {
        var arguments = JsonNode.Parse(argumentsJson)!.AsObject();
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = tool,
            ["arguments"] = arguments,
        }));
        var service = new AskService(_core, planner);

        var response = await service.AskAsync(History(question), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Equal(1, planner.Calls);
        var operation = Assert.IsType<JsonObject>(Assert.Single(
            Assert.IsType<JsonArray>(response.Body["operations"])));
        Assert.Equal(tool, operation["tool"]?.GetValue<string>());
        Assert.Equal(expectedResultClass, operation["result_class"]?.GetValue<string>());
        Assert.Equal(expectedOutcome, operation["legal_outcome"]?.GetValue<string>());
        Assert.Contains(Assert.IsType<JsonArray>(operation["effects"]).OfType<JsonValue>(),
            effect => effect.GetValue<string>() == expectedEffect);
        Assert.NotNull(operation["ui"]?[expectedView]);
        Assert.False(string.IsNullOrWhiteSpace(response.Body["reply"]?.GetValue<string>()));

        var primary = Assert.Single(Assert.IsType<JsonArray>(response.Body["trace"])
            .OfType<JsonObject>(), item => item["phase"]?.GetValue<string>() == "primary");
        Assert.Equal(tool, primary["tool"]?.GetValue<string>());
        Assert.Null(primary["args"]?["work_query"]);
        if (LegalOperationPolicy.RequiresWorkResolution(tool))
        {
            var authority = tool == "provenance"
                ? primary["args"]?["lex_id"]?.GetValue<string>()
                : primary["args"]?["work"]?.GetValue<string>();
            Assert.StartsWith(tool == "as_of"
                ? "eu-eurlex:32016r0679" : "eu-eurlex:32013r0575", authority);
        }
    }

    [Fact]
    public async Task Aggregate_intent_executes_without_an_irrelevant_work_search()
    {
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "changes_in_period",
            ["arguments"] = new JsonObject
            {
                ["from_date"] = "2024-01-01",
                ["to_date"] = "2024-12-31",
                ["order"] = "by_churn",
            },
        }));
        var service = new AskService(_core, planner);

        var response = await service.AskAsync(
            History("Which EU laws changed most in 2024?"), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.True(planner.Completed);
        var trace = Assert.IsType<JsonArray>(response.Body["trace"]);
        Assert.Equal("operation_plan", trace[0]!["phase"]!.GetValue<string>());
        Assert.DoesNotContain(trace.OfType<JsonObject>(), item =>
            item["phase"]?.GetValue<string>() == "work_resolution");
        Assert.Equal(3, response.Body["ui"]?["ranking"]?["works_changed"]?.GetValue<int>());
        var operation = Assert.Single(Assert.IsType<JsonArray>(response.Body["operations"]));
        Assert.Equal("ranking", operation!["result_class"]!.GetValue<string>());
        Assert.Equal("succeeded", operation["legal_outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reviewed_alias_and_article_resolve_before_one_authoritative_diff()
    {
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "diff",
            ["arguments"] = new JsonObject
            {
                ["work_query"] = "CRR",
                ["article_number"] = "92",
                ["from_date"] = "2020-01-01",
                ["to_date"] = "2024-12-31",
            },
        }));
        var service = new AskService(_core, planner);

        var response = await service.AskAsync(
            History("Compare Article 92 of CRR between 2020 and 2024."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        var trace = Assert.IsType<JsonArray>(response.Body["trace"])
            .OfType<JsonObject>().ToArray();
        Assert.Equal(["operation_plan", "work_resolution", "primary"],
            trace.Select(item => item["phase"]!.GetValue<string>()));
        Assert.Equal("eu-eurlex:32013r0575",
            trace[2]["args"]?["work"]?.GetValue<string>());
        Assert.Equal("art_92", trace[2]["args"]?["anchor"]?.GetValue<string>());
        Assert.Equal("comparison",
            response.Body["operations"]?[0]?["result_class"]?.GetValue<string>());
        Assert.NotNull(response.Body["ui"]?["diff"]);
        Assert.Null(response.Body["clarification"]);
        Assert.Contains("provisional", response.Body["reply"]?.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
        var evidence = response.Body["operations"]?[0]?["ui"]?["diff"]?["evidence"]?.AsArray();
        Assert.Equal(2, evidence?.Count);
        Assert.All(evidence?.OfType<JsonObject>() ?? [], item =>
        {
            Assert.NotNull(item["record_sha256"]);
            Assert.NotNull(item["source_uri"]);
        });
    }

    [Fact]
    public async Task Incomparable_profiles_remain_a_typed_comparison_gap()
    {
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "diff",
            ["arguments"] = new JsonObject
            {
                ["work_query"] = "CRR",
                ["article_number"] = "92",
                ["from_date"] = "2020-01-01",
                ["to_date"] = "2024-12-31",
            },
        }));
        async ValueTask<JsonNode> LegalTool(
            string tool, JsonObject arguments, CancellationToken cancellationToken)
        {
            if (tool != "diff")
                return await _core.CallToolAsync(tool, arguments, cancellationToken);
            return new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.ProfilesDiffer },
                ["anchor"] = "art_92",
                ["from"] = new JsonObject
                {
                    ["valid_from"] = "2020-01-01",
                    ["title"] = "Regulation (EU) No 575/2013",
                },
                ["to"] = new JsonObject { ["valid_from"] = "2024-01-01" },
            };
        }
        var service = new AskService(_core, planner, legalTool: LegalTool);

        var response = await service.AskAsync(
            History("Compare Article 92 of CRR between 2020 and 2024."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        var operation = Assert.IsType<JsonObject>(response.Body["operations"]?[0]);
        Assert.Equal("not_comparable", operation["legal_outcome"]?.GetValue<string>());
        Assert.Equal(McpStatus.ProfilesDiffer,
            operation["ui"]?["gap"]?["status"]?.GetValue<string>());
        Assert.NotNull(operation["ui"]?["diff"]);
        Assert.Contains("do not support a reliable provision comparison",
            response.Body["reply"]?.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compound_plan_preserves_a_completed_result_when_another_needs_clarification()
    {
        var planner = new StaticPlanner("en", new JsonArray(
            new JsonObject
            {
                ["tool"] = "coverage",
                ["arguments"] = new JsonObject(),
            },
            new JsonObject
            {
                ["tool"] = "timeline",
                ["arguments"] = new JsonObject { ["work_query"] = "unknown instrument" },
            }));
        var service = new AskService(_core, planner);

        var response = await service.AskAsync(
            History("Show coverage and the timeline of an unknown instrument."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        var operations = Assert.IsType<JsonArray>(response.Body["operations"]);
        Assert.Equal(2, operations.Count);
        Assert.Equal("succeeded", operations[0]!["legal_outcome"]!.GetValue<string>());
        Assert.Equal("needs_clarification", operations[1]!["legal_outcome"]!.GetValue<string>());
        Assert.NotNull(operations[0]!["ui"]?["coverage"]);
        Assert.NotNull(operations[1]!["ui"]?["gap"]);
        Assert.NotNull(response.Body["clarification"]);
    }

    [Fact]
    public async Task Cancellation_preserves_completed_operations_and_terminally_marks_the_rest()
    {
        var planner = new StaticPlanner("en", new JsonArray(
            new JsonObject { ["tool"] = "coverage", ["arguments"] = new JsonObject() },
            new JsonObject { ["tool"] = "coverage", ["arguments"] = new JsonObject() }));
        var service = new AskService(_core, planner);
        using var cancellation = new CancellationTokenSource();
        var steps = 0;

        var response = await service.AskAsync(History("Show coverage twice."),
            Guid.NewGuid().ToString(), "law.test", cancellation.Token,
            new AskService.AskProgressCallbacks(Step: (_, _) =>
            {
                if (++steps == 1) cancellation.Cancel();
                return ValueTask.CompletedTask;
            }));

        Assert.Equal(499, response.Status);
        var operations = Assert.IsType<JsonArray>(response.Body["operations"]);
        Assert.Equal("succeeded", operations[0]!["legal_outcome"]?.GetValue<string>());
        Assert.NotNull(operations[0]!["ui"]?["coverage"]);
        Assert.Equal("not_evaluated", operations[1]!["legal_outcome"]?.GetValue<string>());
        Assert.Equal("cancelled", operations[1]!["transport_outcome"]?.GetValue<string>());
        Assert.NotNull(operations[1]!["ui"]?["gap"]);
    }

    [Fact]
    public async Task A_model_introduced_law_name_is_a_candidate_not_work_authority()
    {
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "timeline",
            ["arguments"] = new JsonObject { ["work_query"] = "CRR" },
        }));
        var service = new AskService(_core, planner);

        var response = await service.AskAsync(
            History("Show me the timeline for that one."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Equal("needs_clarification",
            response.Body["operations"]?[0]?["legal_outcome"]?.GetValue<string>());
        Assert.NotNull(response.Body["clarification"]);
        Assert.DoesNotContain(Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>(),
            item => item["phase"]?.GetValue<string>() == "primary");
    }

    [Fact]
    public async Task Planner_reformulation_cannot_replace_an_exact_current_work()
    {
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "timeline",
            ["arguments"] = new JsonObject { ["work_query"] = "DORA" },
        }));
        var service = new AskService(_core, planner);

        var response = await service.AskAsync(
            History("Show the timeline of CRR."), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        var primary = Assert.Single(
            Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>(),
            item => item["phase"]?.GetValue<string>() == "primary");
        Assert.Equal("eu-eurlex:32013r0575", primary["args"]?["work"]?.GetValue<string>());
    }

    [Fact]
    public async Task Vague_follow_up_keeps_carried_authority_despite_a_noisy_direct_hit()
    {
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "timeline",
            ["arguments"] = new JsonObject { ["work_query"] = "DORA" },
        }));
        var service = new AskService(_core, planner);
        var history = new JsonArray(
            new JsonObject { ["role"] = "user", ["content"] = "Show CRR." },
            new JsonObject { ["role"] = "assistant", ["content"] = "CRR is open." },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Show its timeline for operational resilience requirements.",
            });

        var response = await service.AskAsync(history, Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        var primary = Assert.Single(
            Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>(),
            item => item["phase"]?.GetValue<string>() == "primary");
        Assert.Equal("eu-eurlex:32013r0575", primary["args"]?["work"]?.GetValue<string>());
    }

    [Fact]
    public async Task Fresh_problem_first_subject_replaces_unrelated_carried_authority()
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "timeline",
            ["arguments"] = new JsonObject { ["work_query"] = "DORA" },
        })));
        var history = new JsonArray(
            new JsonObject { ["role"] = "user", ["content"] = "Show CRR." },
            new JsonObject { ["role"] = "assistant", ["content"] = "CRR is open." },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Show the timeline for operational resilience requirements.",
            });

        var response = await service.AskAsync(history, Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        var primary = Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>()
            .SingleOrDefault(item => item["phase"]?.GetValue<string>() == "primary");
        if (primary is null)
            Assert.Equal("needs_clarification",
                response.Body["operations"]?[0]?["legal_outcome"]?.GetValue<string>());
        else
            Assert.Equal("eu-eurlex:32022r2554", primary["args"]?["work"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Focused_direct_provision_evidence_can_resolve_a_problem_first_subject(
        bool hasUnrelatedPrior)
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "timeline",
            ["arguments"] = new JsonObject { ["work_query"] = "zebrafalcon" },
        })));
        var history = hasUnrelatedPrior
            ? new JsonArray(
                new JsonObject { ["role"] = "user", ["content"] = "Show CRR." },
                new JsonObject { ["role"] = "assistant", ["content"] = "CRR is open." },
                new JsonObject { ["role"] = "user", ["content"] = "Show the subject's timeline." })
            : History("Show the subject's timeline.");

        var response = await service.AskAsync(history, Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        var primary = Assert.Single(
            Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>(),
            item => item["phase"]?.GetValue<string>() == "primary");
        Assert.Equal("eu-eurlex:32022r2554", primary["args"]?["work"]?.GetValue<string>());
        Assert.Null(response.Body["clarification"]);
    }

    [Fact]
    public async Task Whole_document_text_and_empty_aggregate_are_terminal_typed_results()
    {
        var exact = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "as_of",
            ["arguments"] = new JsonObject
            {
                ["work_query"] = "WHOLE",
                ["date"] = "2024-06-01",
            },
        })));
        // A mounted jurisdiction with nothing in the window: a real, empty legal population.
        var empty = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "changes_in_period",
            ["arguments"] = new JsonObject
            {
                ["from_date"] = "2015-01-01",
                ["to_date"] = "2015-12-31",
                ["jurisdiction"] = "EU",
            },
        })));

        var exactResponse = await exact.AskAsync(History("Show WHOLE on 1 June 2024."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);
        var emptyResponse = await empty.AskAsync(History("What changed in the EU during 2015?"),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, exactResponse.Status);
        Assert.Equal("text_not_available",
            exactResponse.Body["operations"]?[0]?["ui"]?["gap"]?["status"]?.GetValue<string>());
        Assert.Equal("not_available", exactResponse.Body["operations"]?[0]?["legal_outcome"]
            ?.GetValue<string>());
        Assert.Equal(200, emptyResponse.Status);
        Assert.Equal("succeeded_empty", emptyResponse.Body["operations"]?[0]?["legal_outcome"]
            ?.GetValue<string>());
        Assert.Equal(0, emptyResponse.Body["operations"]?[0]?["ui"]?["ranking"]?["works_changed"]
            ?.GetValue<int>());
    }

    // An unmounted jurisdiction is a fact about the request, never an empty legal population.
    // Reported as succeeded_empty it renders as "0 works changed in a population of 0": a
    // confident zero about a corpus that was never consulted.
    [Fact]
    public async Task An_unmounted_jurisdiction_filter_is_a_gap_not_an_empty_population()
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "changes_in_period",
            ["arguments"] = new JsonObject
            {
                ["from_date"] = "2024-01-01",
                ["to_date"] = "2024-12-31",
                ["jurisdiction"] = "ZZ",
            },
        })));

        var response = await service.AskAsync(History("What changed in ZZ during 2024?"),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Equal("not_found",
            response.Body["operations"]?[0]?["legal_outcome"]?.GetValue<string>());
        Assert.Equal("unknown_publisher",
            response.Body["operations"]?[0]?["ui"]?["gap"]?["status"]?.GetValue<string>());
        Assert.Null(response.Body["operations"]?[0]?["ui"]?["ranking"]);
    }

    [Fact]
    public async Task Real_nested_mcp_evidence_and_future_state_remain_visible()
    {
        async Task<JsonObject> Ask(string question, string tool, JsonObject arguments)
        {
            var service = new AskService(_core, new StaticPlanner("en", new JsonArray(
                new JsonObject { ["tool"] = tool, ["arguments"] = arguments })));
            var response = await service.AskAsync(History(question), Guid.NewGuid().ToString(),
                "law.test", CancellationToken.None);
            Assert.Equal(200, response.Status);
            Assert.True(response.Body["reply"]?.GetValue<string>()
                    ?.Contains("provisional", StringComparison.OrdinalIgnoreCase) == true,
                response.Body.ToJsonString());
            return Assert.IsType<JsonObject>(response.Body["operations"]?[0]?["ui"]);
        }

        var timeline = await Ask("Show DORA's timeline.", "timeline",
            new JsonObject { ["work_query"] = "DORA" });
        var history = await Ask("When did Article 92 of CRR change?", "article_history",
            new JsonObject { ["work_query"] = "CRR", ["article_number"] = "92" });
        var inForce = await Ask("What was in force on 1 June 2024?", "in_force_on",
            new JsonObject { ["date"] = "2024-06-01" });
        var ranking = await Ask("What changed during 2024?", "changes_in_period",
            new JsonObject { ["from_date"] = "2024-01-01", ["to_date"] = "2024-12-31" });

        foreach (var evidence in new[]
                 {
                     timeline["timeline"]?["evidence"], history["history"]?["evidence"],
                     inForce["in_force"]?["evidence"], ranking["ranking"]?["evidence"],
                 })
            Assert.Contains(Assert.IsType<JsonArray>(evidence).OfType<JsonObject>(),
                item => item["provisional"]?.GetValue<bool>() == true);
        Assert.Contains(timeline["timeline"]?["evidence"]!.AsArray().OfType<JsonObject>() ?? [],
            item => item["record_sha256"] is not null && item["source_uri"] is not null);
        Assert.Contains(history["history"]?["evidence"]!.AsArray().OfType<JsonObject>() ?? [],
            item => item["text_sha256"] is not null && item["record_sha256"] is not null);
        Assert.Contains(inForce["in_force"]?["evidence"]!.AsArray().OfType<JsonObject>() ?? [],
            item => item["record_sha256"] is not null && item["source_uri"] is not null);
    }

    [Fact]
    public async Task Common_french_requests_use_french_application_templates()
    {
        var coverage = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "coverage",
            ["arguments"] = new JsonObject(),
        })));
        var boundary = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "legal_boundary",
            ["arguments"] = new JsonObject { ["reason"] = "avis juridique" },
        })));

        var coverageResponse = await coverage.AskAsync(History("Affichez la couverture."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);
        var boundaryResponse = await boundary.AskAsync(History("Dois-je respecter le CRR ?"),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal("fr", coverageResponse.Body["trace"]?[0]?["locale"]?.GetValue<string>());
        Assert.StartsWith("Lex monte", coverageResponse.Body["reply"]?.GetValue<string>());
        Assert.Equal("fr", boundaryResponse.Body["trace"]?[0]?["locale"]?.GetValue<string>());
        Assert.StartsWith("Lex peut", boundaryResponse.Body["reply"]?.GetValue<string>());
    }

    [Fact]
    public async Task Request_locale_is_server_owned_not_planner_owned()
    {
        var service = new AskService(_core, new StaticPlanner("fr", new JsonArray(new JsonObject
        {
            ["tool"] = "coverage",
            ["arguments"] = new JsonObject(),
        })));

        var response = await service.AskAsync(History("Show coverage."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal("en", response.Body["trace"]?[0]?["locale"]?.GetValue<string>());
        Assert.StartsWith("Lex mounts", response.Body["reply"]?.GetValue<string>());
    }

    [Fact]
    public async Task Pure_legal_advice_is_a_typed_boundary_with_no_legal_state_call()
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "legal_boundary",
            ["arguments"] = new JsonObject { ["reason"] = "legal advice" },
        })));

        var response = await service.AskAsync(
            History("Should I treat this as compliant?"), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        var operation = Assert.Single(Assert.IsType<JsonArray>(response.Body["operations"]));
        Assert.Equal("legal_boundary", operation!["disposition"]?.GetValue<string>());
        Assert.Equal("legal_boundary", operation["legal_outcome"]?.GetValue<string>());
        Assert.NotNull(operation["ui"]?["gap"]);
        Assert.DoesNotContain(Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>(),
            item => item["phase"]?.GetValue<string>() is "primary" or "work_resolution");
    }

    [Fact]
    public async Task Explicit_synthesis_runs_once_after_the_authoritative_result()
    {
        var synthesizer = new RecordingSynthesizer();
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "diff",
            ["arguments"] = new JsonObject
            {
                ["work_query"] = "CRR",
                ["article_number"] = "92",
                ["from_date"] = "2020-01-01",
                ["to_date"] = "2024-12-31",
            },
        }), synthesis: true), synthesizer);

        var response = await service.AskAsync(
            History("Compare Article 92 of CRR and summarize the differences."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(1, synthesizer.Calls);
        Assert.NotEmpty(synthesizer.Evidence);
        Assert.Contains("descriptive synthesis", response.Body["reply"]?.GetValue<string>());
        Assert.NotNull(response.Body["ui"]?["diff"]);
        Assert.Equal(120, response.Body["model_usage"]?["input_tokens"]?.GetValue<long>());
        Assert.Equal(30, response.Body["model_usage"]?["output_tokens"]?.GetValue<long>());
        Assert.Equal(150, response.Body["model_usage"]?["total_tokens"]?.GetValue<long>());
    }

    [Fact]
    public async Task Stream_reports_each_typed_operation_before_optional_synthesis()
    {
        var order = new List<string>();
        var synthesizer = new OrderedSynthesizer(order);
        JsonObject? streamedOperation = null;
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "coverage",
            ["arguments"] = new JsonObject(),
        }), synthesis: true), synthesizer);
        var progress = new AskService.AskProgressCallbacks(
            OperationResult: (operation, _) =>
            {
                order.Add("operation_result");
                streamedOperation = operation;
                return ValueTask.CompletedTask;
            },
            Synthesis: (status, _) =>
            {
                order.Add($"synthesis_{status}");
                return ValueTask.CompletedTask;
            });

        var response = await service.AskAsync(
            History("Show coverage and summarize it."), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None, progress, "stream-request");

        Assert.Equal(200, response.Status);
        Assert.Equal("stream-request:op-1", streamedOperation?["operation_id"]?.GetValue<string>());
        Assert.Equal("succeeded", streamedOperation?["legal_outcome"]?.GetValue<string>());
        Assert.NotNull(streamedOperation?["ui"]?["coverage"]);
        Assert.Equal(
            ["operation_result", "synthesis_started", "synthesizer", "synthesis_completed"], order);
    }

    [Fact]
    public async Task Unexpected_primary_failure_preserves_every_terminal_operation_result()
    {
        var planner = new StaticPlanner("en", new JsonArray(
            new JsonObject
            {
                ["tool"] = "legal_boundary",
                ["arguments"] = new JsonObject { ["reason"] = "advice" },
            },
            new JsonObject
            {
                ["tool"] = "coverage",
                ["arguments"] = new JsonObject(),
            }));
        static ValueTask<JsonNode> Fail(
            string _, JsonObject __, CancellationToken ___) =>
            ValueTask.FromException<JsonNode>(new InvalidOperationException("injected failure"));
        var service = new AskService(_core, planner, legalTool: Fail);
        var streamed = new List<JsonObject>();
        var response = await service.AskAsync(
            History("Advise me and show coverage."), "client", "law.test",
            CancellationToken.None,
            new AskService.AskProgressCallbacks(OperationResult: (result, _) =>
            {
                streamed.Add(result.DeepClone().AsObject());
                return ValueTask.CompletedTask;
            }));

        Assert.Equal(502, response.Status);
        Assert.Equal(2, streamed.Count);
        Assert.Equal("legal_boundary", streamed[0]["legal_outcome"]?.GetValue<string>());
        Assert.Equal("completed", streamed[0]["transport_outcome"]?.GetValue<string>());
        Assert.Equal("not_evaluated", streamed[1]["legal_outcome"]?.GetValue<string>());
        Assert.Equal("upstream_failed", streamed[1]["transport_outcome"]?.GetValue<string>());
        Assert.NotNull(streamed[1]["ui"]?["gap"]);
        Assert.Equal(2, response.Body["operations"]?.AsArray().Count);
    }

    [Fact]
    public async Task Primary_outline_is_described_as_a_table_of_contents_not_exact_text()
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "as_of",
            ["arguments"] = new JsonObject
            {
                ["work"] = "eu-eurlex:32013r0575",
                ["date"] = "2024-01-01",
                ["mode"] = "outline",
            },
        })));

        var response = await service.AskAsync(
            History("Show the CRR table of contents on 1 January 2024."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Contains("table of contents", response.Body["reply"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exact publisher text", response.Body["reply"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Body["ui"]?["provision"]?["outline_only"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Invalid_planner_arguments_fail_before_legal_calls_as_a_typed_result()
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "coverage",
            ["arguments"] = new JsonObject { ["work"] = "unexpected" },
        })));

        var response = await service.AskAsync(
            History("Show coverage."), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        Assert.Equal("invalid_request",
            response.Body["operations"]?[0]?["legal_outcome"]?.GetValue<string>());
        Assert.NotNull(response.Body["ui"]?["gap"]);
        Assert.DoesNotContain(Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>(),
            item => item["phase"]?.GetValue<string>() == "primary");
    }

    [Fact]
    public async Task Server_defaults_are_frozen_before_execution()
    {
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "changes_in_period",
            ["arguments"] = new JsonObject
            {
                ["from_date"] = "2024-01-01",
                ["to_date"] = "2024-12-31",
            },
        })));

        var response = await service.AskAsync(
            History("What changed in 2024?"), Guid.NewGuid().ToString(),
            "law.test", CancellationToken.None);

        var trace = Assert.IsType<JsonArray>(response.Body["trace"]).OfType<JsonObject>().ToArray();
        Assert.Equal("!RECUEIL,!CODE_RECUEIL",
            trace[0]["operations"]?[0]?["arguments"]?["source_class"]?.GetValue<string>());
        Assert.Equal(trace[0]["operations"]?[0]?["arguments"]?.ToJsonString(),
            trace[1]["args"]?.ToJsonString());
    }

    public void Dispose()
    {
        _reader.Dispose();
        try { File.Delete(_db); } catch { }
    }

    private static JsonArray History(string question) =>
    [
        new JsonObject { ["role"] = "user", ["content"] = question },
    ];

    private static DocRow Doc(string from, string? to, string body) =>
        Doc("32013r0575", from, to, body);

    private static DocRow Doc(string work, string from, string? to, string body) => new(
        $"eu-eurlex:{work}:{from}", "eu-eurlex", work, work.ToUpperInvariant(),
        "REG", "en", from, to, "official_consolidation_state", from, false,
        true, true, Hash(body + "record"), Hash(body), $"https://example.test/{work}",
        work switch
        {
            "32013r0575" => "Regulation (EU) No 575/2013",
            "32016r0679" => "Regulation (EU) 2016/679",
            "32022r2554" => "Digital Operational Resilience Act",
            _ => "Whole Text Regulation",
        },
        work switch
        {
            "32013r0575" => "Capital Requirements Regulation",
            "32016r0679" => "General Data Protection Regulation",
            "32022r2554" => "DORA",
            _ => "Whole Text Act",
        }, null, from, null);

    private static ProvisionRow Provision(
        DocRow document, string text, string anchor = "art_92", string num = "Article 92") => new(
        $"{document.Key}|{document.Language}|{document.ValidFrom}", 0, anchor,
        $"{document.Key}#{anchor}", "article", num, null, null, null,
        document.Title, text, Hash(text));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class StaticPlanner(
        string locale,
        JsonArray operations,
        bool synthesis = false) : IOperationPlanner
    {
        public bool Completed { get; private set; }
        public int Calls { get; private set; }

        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Completed = true;
            Calls++;
            return Task.FromResult(OperationPlan.FromPlannerOutput(
                requestId, locale, operations.DeepClone().AsArray(), synthesis));
        }
    }

    private sealed class RecordingSynthesizer : IOperationSynthesizer
    {
        public int Calls { get; private set; }
        public IReadOnlyList<AgentEvidence> Evidence { get; private set; } = [];

        public Task<AgentFinalization> SynthesizeAsync(
            string question,
            string deterministicDraft,
            IReadOnlyList<AgentEvidence> evidence,
            CancellationToken cancellationToken)
        {
            Calls++;
            Evidence = evidence;
            return Task.FromResult(new AgentFinalization(
                new AgentAnswerDraft(
                    AgentAnswerStatus.Answer,
                    "A grounded descriptive synthesis is available from the verified comparison.",
                    [], [], null, null),
                SynthesisFailed: false,
                new ModelTokenUsage(120, 30)));
        }
    }

    [Fact]
    public async Task First_result_deadline_cancels_a_blocked_legal_executor_without_late_results()
    {
        var admission = new AskAdmissionController(
            TimeProvider.System, perClientDaily: 10, globalDaily: 10, concurrent: 1);
        var planner = new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "coverage",
            ["arguments"] = new JsonObject(),
        }));
        static async ValueTask<JsonNode> Blocked(
            string tool, JsonObject arguments, CancellationToken cancellationToken)
        {
            _ = tool;
            _ = arguments;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new JsonObject();
        }
        var service = new AskService(_core, planner, admission: admission,
            plannerDeadline: TimeSpan.FromSeconds(1),
            firstResultDeadline: TimeSpan.FromMilliseconds(100), legalTool: Blocked);
        var observed = 0;
        var started = System.Diagnostics.Stopwatch.StartNew();

        var response = await service.AskAsync(
            History("Show coverage."), "deadline-client", "law.test",
            CancellationToken.None,
            new AskService.AskProgressCallbacks(OperationResult: (_, _) =>
            {
                Interlocked.Increment(ref observed);
                return ValueTask.CompletedTask;
            }));

        Assert.Equal(504, response.Status);
        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(500), started.Elapsed.ToString());
        var operation = Assert.Single(response.Body["operations"]!.AsArray());
        Assert.Equal("timed_out", operation!["transport_outcome"]?.GetValue<string>());
        Assert.Equal("not_evaluated", operation["legal_outcome"]?.GetValue<string>());
        Assert.Equal(1, observed);
        await Task.Delay(150);
        Assert.Equal(1, observed);
        using var recovered = admission.TryAdmit("probe").Lease;
        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task Untrusted_text_channels_never_become_activity_data()
    {
        const string currentCanary = "CURRENT_USER_CANARY_91D7";
        const string transcriptCanary = "RESTORED_TRANSCRIPT_CANARY_4B2A";
        const string publisherCanary = "PUBLISHER_TEXT_CANARY_C838";
        const string metadataCanary = "METADATA_CANARY_67F1";
        const string toolCanary = "TOOL_RESULT_CANARY_A24E";
        var recorded = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AskService.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => recorded.Add(activity.DisplayName + " "
                + string.Join(' ', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))),
        };
        ActivitySource.AddActivityListener(listener);

        var payload = _core.CallTool("coverage", new JsonObject()).DeepClone();
        var publisher = payload.AsArray()[0]!.AsObject();
        publisher["known_exclusions"] = metadataCanary;
        publisher["build_issues"] = new JsonArray(new JsonObject
        {
            ["code"] = toolCanary,
            ["work"] = "test",
            ["detail"] = publisherCanary,
        });
        ValueTask<JsonNode> LegalTool(
            string tool, JsonObject arguments, CancellationToken cancellationToken)
        {
            _ = tool;
            _ = arguments;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload.DeepClone());
        }
        var service = new AskService(_core, new StaticPlanner("en", new JsonArray(
            new JsonObject { ["tool"] = "coverage", ["arguments"] = new JsonObject() })),
            legalTool: LegalTool);
        var history = new JsonArray(
            new JsonObject { ["role"] = "user", ["content"] = "Show coverage." },
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = $"Quoted untrusted data: {transcriptCanary}",
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = $"Show coverage. Quoted untrusted data: {currentCanary}",
            });

        var response = await service.AskAsync(
            history, "198.51.100.24", "law.test", CancellationToken.None);

        Assert.Equal(200, response.Status);
        var telemetry = string.Join('\n', recorded);
        foreach (var canary in new[]
                 {
                     currentCanary, transcriptCanary, publisherCanary, metadataCanary, toolCanary,
                 })
            Assert.DoesNotContain(canary, telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100.24", telemetry, StringComparison.Ordinal);
    }

    private sealed class OrderedSynthesizer(List<string> order) : IOperationSynthesizer
    {
        public Task<AgentFinalization> SynthesizeAsync(
            string question,
            string deterministicDraft,
            IReadOnlyList<AgentEvidence> evidence,
            CancellationToken cancellationToken)
        {
            order.Add("synthesizer");
            return Task.FromResult(new AgentFinalization(
                new AgentAnswerDraft(
                    AgentAnswerStatus.Answer, deterministicDraft, [], [], null, null),
                SynthesisFailed: false));
        }
    }
}
