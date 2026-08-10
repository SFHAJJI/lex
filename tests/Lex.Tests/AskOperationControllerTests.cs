using System.Security.Cryptography;
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
        }, [first, second, dora, whole],
        [
            Provision(first, "old capital requirement"),
            Provision(second, "new capital requirement"),
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
            _ => { if (++steps == 1) cancellation.Cancel(); });

        Assert.Equal(200, response.Status);
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
        var empty = new AskService(_core, new StaticPlanner("en", new JsonArray(new JsonObject
        {
            ["tool"] = "changes_in_period",
            ["arguments"] = new JsonObject
            {
                ["from_date"] = "2024-01-01",
                ["to_date"] = "2024-12-31",
                ["jurisdiction"] = "ZZ",
            },
        })));

        var exactResponse = await exact.AskAsync(History("Show WHOLE on 1 June 2024."),
            Guid.NewGuid().ToString(), "law.test", CancellationToken.None);
        var emptyResponse = await empty.AskAsync(History("What changed in ZZ during 2024?"),
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
            ["tool"] = "coverage", ["arguments"] = new JsonObject(),
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
            "32022r2554" => "Digital Operational Resilience Act",
            _ => "Whole Text Regulation",
        },
        work switch
        {
            "32013r0575" => "Capital Requirements Regulation",
            "32022r2554" => "DORA",
            _ => "Whole Text Act",
        }, null, from, null);

    private static ProvisionRow Provision(DocRow document, string text) => new(
        $"{document.Key}|{document.Language}|{document.ValidFrom}", 0, "art_92",
        $"{document.Key}#art_92", "article", "Article 92", null, null, null,
        document.Title, text, Hash(text));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class StaticPlanner(
        string locale,
        JsonArray operations,
        bool synthesis = false) : IOperationPlanner
    {
        public bool Completed { get; private set; }

        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Completed = true;
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
                SynthesisFailed: false));
        }
    }
}
