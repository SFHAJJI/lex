using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;

namespace Lex.Tests;

public sealed class OperationPolicyTests
{
    [Fact]
    public void Deterministic_answers_surface_bounded_provision_history_and_in_force_facts()
    {
        var evidence = new[]
        {
            new EvidenceContext(
                Publisher: "eu-eurlex", Jurisdiction: "EU",
                TimelineSemantics: "official_consolidation_state",
                RequestedDate: "2021-01-01", RequestedFromDate: null,
                RequestedToDate: null, ObservedAt: null, ValidFrom: "2018-05-25",
                ValidTo: null, Provisional: false, SourceUri: null,
                ExtractionProfile: null, RecordSha256: null, BodySha256: null,
                TextSha256: null, ArtifactManifestId: null, ContentDigest: null,
                SignatureValid: true),
        };
        var provisionOperation = RequestedOperation.CreatePlanned(
            "req:op-1", 0, "as_of", new JsonObject
            {
                ["work"] = "eu-eurlex:32016r0679",
                ["date"] = "2021-01-01",
                ["mode"] = "select",
                ["anchors"] = "art_6",
            });
        var provision = new ProvisionView(
            new Subject("eu-eurlex:32016r0679", "GDPR", "2021-01-01", "art_6", "en"),
            "2018-05-25", null,
            [new ProvisionItem("art_6", "Article 6", "Lawfulness of processing",
                "Processing shall be lawful only if and to the extent that at least one condition applies.",
                new string('a', 64))],
            "https://law.example/gdpr#art_6", evidence);

        var provisionReply = OperationAnswerPolicy.Render(
            "en", [new OperationExecution(provisionOperation).Complete(McpStatus.Ok, new JsonObject())],
            [new UiEffect(Provision: provision)]);

        Assert.Contains("Article 6", provisionReply, StringComparison.Ordinal);
        Assert.Contains("Lawfulness of processing", provisionReply, StringComparison.Ordinal);
        Assert.Contains("Processing shall be lawful", provisionReply, StringComparison.Ordinal);
        Assert.Contains("2021-01-01", provisionReply, StringComparison.Ordinal);

        var historyOperation = RequestedOperation.CreatePlanned(
            "req:op-2", 0, "article_history", new JsonObject
            {
                ["work"] = "lu-legilux:constitution-1868-10-17-n1",
                ["anchor"] = "art_11",
                ["language"] = "fr",
            });
        var history = new HistoryView(
            new Subject("lu-legilux:constitution-1868-10-17-n1", "Constitution", null,
                "art_11", "fr"),
            "art_11", 6,
            [
                new HistoryState("1919-05-20", "1948-06-01", new string('b', 64), null),
                new HistoryState("2023-07-01", null, new string('c', 64), null),
            ],
            [evidence[0] with { Publisher = "lu-legilux", Jurisdiction = "LU",
                TimelineSemantics = "publisher_applicability" }]);

        var historyReply = OperationAnswerPolicy.Render(
            "en", [new OperationExecution(historyOperation).Complete(McpStatus.Ok, new JsonObject())],
            [new UiEffect(History: history)]);

        Assert.Contains("6", historyReply, StringComparison.Ordinal);
        Assert.Contains("1919-05-20", historyReply, StringComparison.Ordinal);
        Assert.Contains("2023-07-01", historyReply, StringComparison.Ordinal);
        Assert.Contains("publisher", historyReply, StringComparison.OrdinalIgnoreCase);

        var inForceOperation = RequestedOperation.CreatePlanned(
            "req:op-3", 0, "in_force_on", new JsonObject
            {
                ["date"] = "2024-06-01",
                ["jurisdiction"] = "EU",
            });
        var inForce = new InForceView(
            "2024-06-01", 12, [], "ok", evidence);
        var inForceReply = OperationAnswerPolicy.Render(
            "en", [new OperationExecution(inForceOperation).Complete(McpStatus.Ok, new JsonObject())],
            [new UiEffect(InForce: inForce)]);

        Assert.Contains("12", inForceReply, StringComparison.Ordinal);
        Assert.Contains("2024-06-01", inForceReply, StringComparison.Ordinal);
        Assert.Contains("publisher-observed", inForceReply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not an exhaustive legal-effect", inForceReply,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deterministic_answers_preserve_the_legal_boundary_and_timeline_facts()
    {
        var boundary = RequestedOperation.CreateApplication(
            "req:op-1", 0, ApplicationDisposition.LegalBoundary, new JsonObject());
        var boundaryResult = new OperationExecution(boundary)
            .CompleteLegal(LegalOutcome.LegalBoundary);
        const string boundaryExplanation =
            "Lex can retrieve verified legal text, but it cannot give legal advice, decide compliance, or apply law to your facts.";

        Assert.Equal(boundaryExplanation, OperationAnswerPolicy.Render(
            "en", [boundaryResult],
            [new UiEffect(Gap: new GapView(
                "legal_boundary", null, null, boundaryExplanation, []))]));

        var timelineOperation = RequestedOperation.CreatePlanned(
            "req:op-2", 0, "timeline", new JsonObject { ["work"] = "eu-eurlex:32016r0679" });
        var timelineResult = new OperationExecution(timelineOperation)
            .Complete(McpStatus.Ok, new JsonObject());
        var timeline = new TimelineView(
            new Subject("eu-eurlex:32016r0679", "GDPR", null, null),
            [
                new TimelineState(null, "2016-05-04", "2018-05-24", "GDPR", "en", null, null),
                new TimelineState(null, "2018-05-25", null, "GDPR", "en", null, null),
            ],
            2, false,
            [new EvidenceContext(
                Publisher: "eu-eurlex", Jurisdiction: "EU",
                TimelineSemantics: "official_consolidation_state",
                RequestedDate: null, RequestedFromDate: null, RequestedToDate: null,
                ObservedAt: null, ValidFrom: null, ValidTo: null, Provisional: false,
                SourceUri: null, ExtractionProfile: null, RecordSha256: null,
                BodySha256: null, TextSha256: null, ArtifactManifestId: null,
                ContentDigest: null, SignatureValid: true)]);

        var rendered = OperationAnswerPolicy.Render(
            "en", [timelineResult], [new UiEffect(Timeline: timeline)]);
        Assert.Contains("2", rendered, StringComparison.Ordinal);
        Assert.Contains("2016-05-04", rendered, StringComparison.Ordinal);
        Assert.Contains("2018-05-25", rendered, StringComparison.Ordinal);
        Assert.Contains("publisher", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deterministic_search_facts_are_fully_localized_in_french()
    {
        var operation = RequestedOperation.CreatePlanned(
            "req:op-1", 0, "search", new JsonObject { ["query"] = "délégué" });
        var result = new OperationExecution(operation).Complete(McpStatus.Ok, new JsonObject());
        var fact = new SearchFact(
            "eu-eurlex:32016r0679", "eu-eurlex:32016r0679:2018-05-25", "art_39",
            "Article 39", "Missions du délégué", "Le délégué informe le responsable.",
            "Règlement général sur la protection des données", "2018-05-25", null, null);

        var rendered = OperationAnswerPolicy.Render(
            "fr", [result], [new UiEffect(Workspace: new WorkspaceView(
                Query: "délégué", Results: [fact]))]);

        Assert.Contains(" dans Règlement général", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(" in ", rendered, StringComparison.Ordinal);
    }

    // A navigation is the one answer that shows no law of its own, so its scope IS its identity.
    // Told only that "the matching results are open", a reader cannot tell the workspace Lex
    // opened on the wrong query, in the wrong corpus or at the wrong instant from the one they
    // asked for, which is the same blindness the named provision and diff lines exist to remove.
    [Fact]
    public void A_navigation_that_shows_no_law_still_names_its_scope_and_its_instant()
    {
        var arguments = new JsonObject
        {
            ["query"] = "CRR",
            ["jurisdiction"] = "EU",
            ["language"] = "en",
            ["time_scope"] = "as_of",
            ["as_of"] = "2021-01-01",
        };
        var effect = UiMapper.From("search", arguments, new JsonArray(new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
            ["hits"] = new JsonArray(),
        }));
        var operation = RequestedOperation.CreatePlanned("req:op-1", 0, "search", arguments);
        var result = new OperationExecution(operation).Complete(McpStatus.Ok, new JsonObject());

        // The instant reaches the workspace directive, so the controls land where the prose says.
        Assert.Equal("2021-01-01", effect.Workspace?.Date);

        var rendered = OperationAnswerPolicy.Render("en", [result], [effect]);

        Assert.Contains("CRR", rendered, StringComparison.Ordinal);
        Assert.Contains("EU", rendered, StringComparison.Ordinal);
        Assert.Contains("2021-01-01", rendered, StringComparison.Ordinal);
        // Navigation quotes nothing, and the query is the assistant's own words rather than a
        // publisher's. The typography has to say so: the quotation marks the provision line uses
        // for verified text are the reader's only signal of what came from the publisher, so a
        // model-authored search term may never wear them.
        Assert.Contains("[CRR]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain('“', rendered);
        Assert.DoesNotContain('”', rendered);

        var french = OperationAnswerPolicy.Render("fr", [result], [effect]);

        Assert.Contains("[CRR]", french, StringComparison.Ordinal);
        Assert.Contains("2021-01-01", french, StringComparison.Ordinal);
        Assert.DoesNotContain('«', french);
        Assert.DoesNotContain(" in ", french, StringComparison.Ordinal);
    }

    // article_history filters to ONE language expression and returns no text at all, so the
    // expression is the only part of its identity a reader can check and the only part a wrong
    // selection still gets plausibly right: art_11 of this Constitution exists in French, German
    // and Luxembourgish, and the three do not share a history.
    [Fact]
    public void A_provision_history_names_the_language_expression_it_describes()
    {
        var arguments = new JsonObject
        {
            ["work"] = "lu-legilux:constitution-1868-10-17-n1",
            ["anchor"] = "art_11",
            ["language"] = "fr",
        };
        var effect = UiMapper.From("article_history", arguments, new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["status"] = McpStatus.Ok,
                ["timeline_semantics"] = "publisher_applicability",
            },
            ["work"] = "lu-legilux:constitution-1868-10-17-n1",
            ["anchor"] = "art_11",
            ["language"] = "fr",
            ["distinct_texts"] = 6,
            ["truncated"] = false,
            ["states"] = new JsonArray(
                new JsonObject { ["valid_from"] = "1919-05-20", ["valid_to"] = "1948-06-01" },
                new JsonObject { ["valid_from"] = "1948-06-02", ["valid_to"] = "2004-11-28" },
                new JsonObject { ["valid_from"] = "2004-11-29", ["valid_to"] = "2006-07-22" },
                new JsonObject { ["valid_from"] = "2006-07-23", ["valid_to"] = "2007-04-02" },
                new JsonObject { ["valid_from"] = "2007-04-03", ["valid_to"] = "2023-06-30" },
                new JsonObject { ["valid_from"] = "2023-07-01" }),
        });
        var operation = RequestedOperation.CreatePlanned(
            "req:op-1", 0, "article_history", arguments);
        var result = new OperationExecution(operation).Complete(McpStatus.Ok, new JsonObject());

        var rendered = OperationAnswerPolicy.Render("en", [result], [effect]);

        Assert.Contains("fr expression", rendered, StringComparison.Ordinal);
        Assert.Contains("lu-legilux:constitution-1868-10-17-n1", rendered, StringComparison.Ordinal);
        Assert.Contains("art_11", rendered, StringComparison.Ordinal);
        // "When did this change?" is answered by the dates, not by their outer bounds: the same
        // count and span describe a different history for every distribution of dates inside it.
        Assert.Contains("1919-05-20", rendered, StringComparison.Ordinal);
        Assert.Contains("1948-06-02", rendered, StringComparison.Ordinal);
        Assert.Contains("2023-07-01", rendered, StringComparison.Ordinal);

        var french = OperationAnswerPolicy.Render("fr", [result], [effect]);

        Assert.Contains("l'expression fr", french, StringComparison.Ordinal);

        // A view that holds fewer states than the publisher counted may not read as the whole
        // list, so a bounded history keeps its span rather than listing part of the set.
        var bounded = OperationAnswerPolicy.Render("en", [result],
            [effect with { History = effect.History! with
            {
                States = [effect.History.States[0], effect.History.States[^1]],
                Truncated = true,
            } }]);

        Assert.DoesNotContain("2004-11-29", bounded, StringComparison.Ordinal);
        Assert.Contains("last returned state beginning 2023-07-01", bounded,
            StringComparison.Ordinal);
        Assert.Contains("bounded response is truncated", bounded, StringComparison.Ordinal);

        // A history the publisher holds in no stated language invents one nowhere.
        var unstated = OperationAnswerPolicy.Render("en", [result],
            [effect with { History = effect.History! with
            {
                Subject = effect.History.Subject with { Language = null },
            } }]);

        Assert.DoesNotContain("expression", unstated, StringComparison.Ordinal);
    }

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
        };

        foreach (var (tool, arguments) in cases)
            Assert.Throws<InvalidDataException>(() => OperationArguments.Normalize(tool, arguments));

        // A page bound one past the range is recovered rather than refused, but the arguments
        // that leave here must still pass the public contract unchanged. That is the line the
        // recovery policy may never cross: this gate got more forgiving, MCP did not.
        foreach (var (tool, arguments) in new (string, JsonObject)[]
                 {
                     ("in_force_on", new JsonObject { ["date"] = "2026-01-01", ["limit"] = 101 }),
                     ("timeline", new JsonObject { ["work"] = "eu-eurlex:work", ["offset"] = 100_001 }),
                 })
            McpInputPolicy.Validate(tool, OperationArguments.Normalize(tool, arguments));
    }

    [Theory]
    [InlineData("as_of", "version_key")]
    [InlineData("diff", "from_version_key")]
    [InlineData("diff", "to_version_key")]
    public void Exact_version_keys_remain_opaque_between_planner_and_mcp(
        string tool, string field)
    {
        const string exact = " 2024-01-01~publisher-state/01 ";
        var arguments = tool == "as_of"
            ? new JsonObject
            {
                ["work"] = "eu-eurlex:work",
                ["date"] = "2024-01-01",
            }
            : new JsonObject
            {
                ["work"] = "eu-eurlex:work",
                ["from_date"] = "2024-01-01",
                ["to_date"] = "2025-01-01",
            };
        arguments[field] = exact;

        var normalized = OperationArguments.Normalize(tool, arguments);

        Assert.Equal(exact, normalized[field]!.GetValue<string>());
        McpInputPolicy.Validate(tool, normalized);
        Assert.Equal(LegalOperationCatalog.MaximumVersionKeyLength,
            LegalOperationCatalog.Get(tool).PlannerInputSchema()["properties"]![field]!
                ["maxLength"]!.GetValue<int>());
    }

    [Fact]
    public void Publisher_metadata_filter_is_mcp_only_and_uses_the_shared_uri_contract()
    {
        var search = LegalOperationCatalog.Get("search");
        var argument = Assert.Single(search.McpArguments,
            candidate => candidate.Name == "publisher_metadata_identifier");
        Assert.False(argument.Planner);
        Assert.True(argument.RequiresAbsoluteHttpUri);
        Assert.Equal(LegalOperationCatalog.MaximumPublisherMetadataIdentifierLength,
            argument.MaximumLength);
        Assert.Null(search.PlannerInputSchema()["properties"]![argument.Name]);

        const string prefix = "https://example.test/";
        var maximum = prefix + new string('a', argument.MaximumLength - prefix.Length);
        McpInputPolicy.Validate("search", new JsonObject
        {
            ["query"] = "energy",
            [argument.Name] = maximum,
        });
        Assert.ThrowsAny<ArgumentException>(() => McpInputPolicy.Validate("search", new JsonObject
        {
            ["query"] = "energy",
            [argument.Name] = maximum + "a",
        }));
        Assert.ThrowsAny<ArgumentException>(() => McpInputPolicy.Validate("search", new JsonObject
        {
            ["query"] = "energy",
            [argument.Name] = "ftp://example.test/not-authorized",
        }));

        var schema = search.McpToolDefinition()["inputSchema"]!["properties"]![argument.Name]!;
        Assert.Equal(LegalOperationCatalog.AbsoluteHttpUriPattern,
            schema["pattern"]!.GetValue<string>());
    }

    [Fact]
    public void Every_shared_catalog_bound_is_identical_at_both_runtime_gates()
    {
        foreach (var operation in LegalOperationCatalog.Operations)
        foreach (var argument in operation.PlannerArguments.Where(planner =>
                     operation.McpArguments.Any(mcp => mcp.Name == planner.Name)))
        {
            // Dates and enums have tighter structural/value constraints than their storage
            // length. Their complete parity is asserted by the schema/value/date tests below.
            if (argument.IsDate || argument.AllowedValues is not null) continue;

            var proposed = MinimalArguments(operation.Name);
            proposed[argument.Name] = MaximumValue(argument);
            var executable = ExecutableForMcp(
                operation.Name, OperationArguments.Normalize(operation.Name, proposed));

            McpInputPolicy.Validate(operation.Name, executable);

            var overPlanner = MinimalArguments(operation.Name);
            overPlanner[argument.Name] = MaximumPlusOneValue(argument);
            if (argument.Kind == LegalArgumentKind.Integer)
            {
                // Page bounds are safely repaired to their canonical defaults; the repaired
                // result, not the rejected proposal, is what reaches MCP.
                McpInputPolicy.Validate(operation.Name, ExecutableForMcp(operation.Name,
                    OperationArguments.Normalize(operation.Name, overPlanner)));
            }
            else
            {
                Assert.Throws<InvalidDataException>(() =>
                    OperationArguments.Normalize(operation.Name, overPlanner));
            }

            var overMcp = executable.DeepClone().AsObject();
            overMcp[argument.Name] = MaximumPlusOneValue(argument);
            Assert.ThrowsAny<ArgumentException>(() =>
                McpInputPolicy.Validate(operation.Name, overMcp));
        }

        static JsonNode MaximumValue(LegalArgumentDefinition argument) =>
            argument.Kind == LegalArgumentKind.Integer
                ? JsonValue.Create(argument.Maximum!.Value)!
                : JsonValue.Create(BoundedString(argument, argument.MaximumLength))!;

        static JsonNode MaximumPlusOneValue(LegalArgumentDefinition argument) =>
            argument.Kind == LegalArgumentKind.Integer
                ? JsonValue.Create(argument.Maximum!.Value + 1)!
                : JsonValue.Create(BoundedString(argument, argument.MaximumLength + 1))!;

        static string BoundedString(LegalArgumentDefinition argument, int length)
        {
            var prefix = argument.Name is "work" or "lex_id" ? "p:" : "";
            var value = new StringBuilder(prefix + new string('x', length - prefix.Length));
            if (argument.MaximumListValues is not null)
                for (var index = 400; index < value.Length; index += 401) value[index] = ',';
            return value.ToString();
        }

        static JsonObject ExecutableForMcp(string tool, JsonObject normalized)
        {
            var executable = normalized.DeepClone().AsObject();
            if (executable.Remove("work_query"))
                executable[tool == "provenance" ? "lex_id" : "work"] =
                    tool == "provenance" ? "p:w:2024-01-01" : "p:w";
            if (executable.Remove("article_number")) executable["anchors"] = "art_1";
            return executable;
        }
    }

    // The planner schema and the argument allowlist are one contract in two places. When they
    // drift the model emits arguments Normalize rejects, and one rejection aborts the whole plan.
    [Fact]
    public void Every_planner_tool_offers_exactly_the_arguments_its_operation_allows()
    {
        var parameters = AskService.PlannerTools()[0]!["function"]!["parameters"]!.AsObject();
        // anyOf, not oneOf: oneOf is outside the keyword set a strict schema may use, and the
        // rewrite is only sound because every branch pins tool to its own distinct const, so at
        // most one branch can ever match. That distinctness is asserted below, and the closed
        // object at each level is asserted here.
        Assert.False(parameters["additionalProperties"]!.GetValue<bool>());
        var branches = parameters["properties"]!["operations"]!["items"]!["anyOf"]!.AsArray();

        var tools = new List<string>();
        foreach (var node in branches)
        {
            var branch = node!.AsObject();
            Assert.Equal(["tool", "arguments"],
                branch["required"]!.AsArray().Select(item => item!.GetValue<string>()));
            Assert.False(branch["additionalProperties"]!.GetValue<bool>());
            Assert.Null(branch["properties"]!["tool"]!["enum"]);
            var tool = branch["properties"]!["tool"]!["const"]!.GetValue<string>();
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
        // The property that makes anyOf equivalent to oneOf here.
        Assert.Equal(tools.Count, tools.Distinct(StringComparer.Ordinal).Count());
        // gap is application-owned. Navigation is a typed effect of an authoritative operation,
        // never a legal operation of its own.
        Assert.Equal(["gap"],
            OperationArguments.Actions.Except(tools, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    // The schema restricting the model's choice is not the same thing as the plan refusing
    // everything else, and only the second is an invariant.
    [Theory]
    [InlineData("navigate")]
    [InlineData("gap")]
    [InlineData("as_of_v2")]
    [InlineData("")]
    public void A_plan_may_only_name_a_tool_the_planner_was_offered(string tool)
    {
        var operations = new JsonArray(new JsonObject
        {
            ["tool"] = tool,
            ["arguments"] = new JsonObject { ["work"] = "eu-eurlex:32013r0575" },
        });

        var error = Assert.Throws<InvalidDataException>(() =>
            OperationPlan.FromPlannerOutput("req-1", "en", operations));
        Assert.Contains("was not offered", error.Message, StringComparison.Ordinal);
    }

    // And every name the planner IS offered still builds, so the check bounds the surface
    // without narrowing it.
    [Fact]
    public void Every_offered_tool_still_builds_a_plan()
    {
        foreach (var tool in AskService.PlannerToolNames)
        {
            // Minimal valid arguments per tool, taken from OperationArguments.Allowed and
            // Choices rather than assumed: search takes a query, not a work.
            var arguments = tool switch
            {
                "clarification" => new JsonObject
                {
                    ["question"] = "Which instrument?",
                    // two to four labels, per the clarification contract
                    ["options"] = new JsonArray("eu-eurlex:32013r0575", "eu-eurlex:32012r0648"),
                },
                "legal_boundary" => new JsonObject { ["reason"] = "advice" },
                "coverage" => new JsonObject(),
                "search" => new JsonObject { ["query"] = "capital requirements" },
                "in_force_on" => new JsonObject { ["date"] = "2024-01-01" },
                "changes_in_period" => new JsonObject
                {
                    ["from_date"] = "2024-01-01",
                    ["to_date"] = "2024-12-31",
                },
                "provenance" => new JsonObject { ["work_query"] = "CRR" },
                "diff" => new JsonObject
                {
                    ["work"] = "eu-eurlex:32013r0575",
                    ["from_date"] = "2020-01-01",
                    ["to_date"] = "2024-12-31",
                },
                "article_history" => new JsonObject
                {
                    ["work"] = "eu-eurlex:32013r0575",
                    ["article_number"] = "92",
                },
                _ => new JsonObject { ["work"] = "eu-eurlex:32013r0575" },
            };
            var operations = new JsonArray(new JsonObject
            {
                ["tool"] = tool,
                ["arguments"] = arguments,
            });

            var plan = OperationPlan.FromPlannerOutput("req-1", "en", operations);
            Assert.Single(plan.Operations);
        }
    }

    // The value half of the same contract. Names were generated from the allowlist; values were
    // not, so the model was shown an open string where Normalize accepts two or three literals,
    // picked "semantic" or "current", and one rejection aborted the whole plan.
    [Fact]
    public void Every_planner_argument_offers_exactly_the_values_its_validator_accepts()
    {
        var closed = new List<string>();
        foreach (var branch in PlannerBranches(CorpusVocabulary.Unconstrained))
        {
            var properties = branch.Value["properties"]!["arguments"]!["properties"]!.AsObject();
            foreach (var (name, value) in properties)
            {
                var expected = OperationArguments.AllowedValuesFor(name);
                if (expected is null)
                {
                    Assert.Null(value!["enum"]);
                    continue;
                }
                closed.Add(name);
                Assert.Equal(expected, value!["enum"]!.AsArray()
                    .Select(item => item!.GetValue<string>()));
                Assert.Equal("string", value["type"]!.GetValue<string>());
            }
        }

        Assert.Equal(
            ["fuzzy", "mode", "order", "retrieval_mode", "time_scope"],
            closed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string> EveryPlannerTool
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var tool in AskService.PlannerToolNames) data.Add(tool);
            return data;
        }
    }

    public static TheoryData<string> EveryOperation
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var action in OperationArguments.Actions) data.Add(action);
            return data;
        }
    }

    // The length half of the same contract, and the round that cost the most: names and values
    // were generated from the gate, bounds were not, so the model saw a bare {"type":"string"}
    // where Normalize enforces a maximum, an ISO format and a required name. Every constraint the
    // gate enforces that JSON Schema can assert must be emitted, read from the gate's accessors.
    [Theory]
    [MemberData(nameof(EveryPlannerTool))]
    public void The_planner_schema_asserts_every_bound_the_gate_can_express(string tool)
    {
        var schema = AskService.PlannerArgumentSchema(tool);

        foreach (var (name, property) in schema["properties"]!.AsObject())
        {
            if (OperationArguments.IsInteger(name))
            {
                var (minimum, maximum) = OperationArguments.IntegerBoundsFor(tool, name);
                Assert.Equal("integer", property!["type"]!.GetValue<string>());
                Assert.Equal(minimum, property["minimum"]!.GetValue<int>());
                Assert.Equal(maximum, property["maximum"]!.GetValue<int>());
                continue;
            }
            if (name == "options")
            {
                Assert.Equal("array", property!["type"]!.GetValue<string>());
                Assert.Equal(OperationArguments.MinimumOptionCount,
                    property["minItems"]!.GetValue<int>());
                Assert.Equal(OperationArguments.MaximumOptionCount,
                    property["maxItems"]!.GetValue<int>());
                Assert.Equal(OperationArguments.MinimumStringLength,
                    property["items"]!["minLength"]!.GetValue<int>());
                Assert.Equal(OperationArguments.MaximumOptionLength,
                    property["items"]!["maxLength"]!.GetValue<int>());
                continue;
            }
            Assert.Equal("string", property!["type"]!.GetValue<string>());
            Assert.Equal(OperationArguments.MinimumStringLength,
                property["minLength"]!.GetValue<int>());
            Assert.Equal(OperationArguments.MaximumLengthFor(name),
                property["maxLength"]!.GetValue<int>());
            Assert.Equal(
                OperationArguments.IsDate(name) ? OperationArguments.IsoDatePattern : null,
                property["pattern"]?.GetValue<string>());
        }

        Assert.Equal(
            OperationArguments.RequiredFor(tool),
            schema["required"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? []);

        // One anyOf per "at least one of" gate, AND-ed under allOf when a tool has two of them.
        var emitted = new List<JsonArray>();
        if (schema["allOf"] is JsonArray all)
            emitted.AddRange(all.Select(item => item!["anyOf"]!.AsArray()));
        else if (schema["anyOf"] is JsonArray any) emitted.Add(any);
        var choices = OperationArguments.RequiredChoicesFor(tool);
        Assert.Equal(choices.Count, emitted.Count);
        for (var index = 0; index < choices.Count; index++)
            Assert.Equal(choices[index].Names, emitted[index].Select(item =>
                Assert.Single(item!["required"]!.AsArray())!.GetValue<string>()));
    }

    // The other direction: a bound the schema promises must be the bound the gate actually
    // enforces. This fails the moment someone tightens a length in Normalize without the schema
    // following, because MaximumLengthFor is what both of them read.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_accepts_exactly_the_string_lengths_the_schema_promises(string action)
    {
        foreach (var name in OperationArguments.AllowedFor(action).Order(StringComparer.Ordinal))
        {
            if (OperationArguments.IsInteger(name) || name == "options") continue;
            // A closed value set is the tighter constraint; a date is probed by its pattern.
            if (OperationArguments.AllowedValuesFor(name) is not null) continue;
            if (OperationArguments.IsDate(name)) continue;
            var minimum = OperationArguments.MinimumStringLength;
            var maximum = OperationArguments.MaximumLengthFor(name);

            OperationArguments.Normalize(action, With(action, name, LengthProbe(name, minimum)));
            OperationArguments.Normalize(action, With(action, name, LengthProbe(name, maximum)));

            // Over-length stays fatal: truncating a work_query or an article_number looks up a
            // different law. Under-length is the other half of the bound and is a recovery, not a
            // refusal; it is proved in the empty-value test below.
            Assert.Equal($"Argument '{name}' must contain {minimum} to {maximum} characters.",
                Assert.Throws<InvalidDataException>(() =>
                    OperationArguments.Normalize(
                        action, With(action, name, LengthProbe(name, maximum + 1)))).Message);
        }
    }

    // D5. The schema still says minLength 1, so the gate is the looser of the two here, which is
    // the safe direction: an empty string is not a value the model chose, it is a slot it left
    // blank, and erasing the key preserves every bit of intent the model actually expressed. The
    // gates below then treat it as the absence it is, which is why some of these still refuse.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void An_empty_or_whitespace_argument_is_absence_rather_than_a_refusal(string action)
    {
        var today = new DateOnly(2026, 3, 4);
        foreach (var name in OperationArguments.AllowedFor(action).Order(StringComparer.Ordinal))
        {
            if (OperationArguments.IsInteger(name) || name == "options") continue;
            foreach (var blank in new[] { "", "   ", "\t" })
            {
                var absent = With(action, name, blank);
                absent.Remove(name);

                // The whole claim, stated once: a blank value is indistinguishable from a key
                // that was never sent. Where absence is itself a refusal the refusal survives,
                // with the message for a missing argument rather than one about characters.
                Assert.Equal(
                    Outcome(action, absent, today),
                    Outcome(action, With(action, name, blank), today));
            }
        }
    }

    private static string Outcome(string action, JsonObject arguments, DateOnly today)
    {
        try
        {
            return OperationArguments.Normalize(action, arguments, today: today).ToJsonString();
        }
        catch (InvalidDataException refused)
        {
            Assert.DoesNotContain("characters", refused.Message, StringComparison.Ordinal);
            return $"refused: {refused.Message}";
        }
    }

    // ",,," is empty-shaped too: it names no anchor and no work. Dropping it lets mode fall back
    // to full rather than refusing, which shows the reader more law rather than none.
    [Fact]
    public void A_list_argument_that_names_nothing_is_absence_rather_than_a_refusal()
    {
        var normalized = OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["date"] = "2024-01-01",
            ["mode"] = "select",
            ["anchors"] = " , , ",
        });

        Assert.Null(normalized["anchors"]);
        Assert.Equal("full", normalized["mode"]!.GetValue<string>());
    }

    // D6. A page bound carries no legal meaning and truncation is visible to the reader, because
    // the in-force and ranking views report the full count beside the rows they show. So an
    // unusable one falls back to the action's canonical value instead of destroying the plan. What
    // may never happen is a bound outside the range reaching MCP, which is what the last
    // assertion in each loop is for.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_accepts_exactly_the_integer_range_the_schema_promises(string action)
    {
        foreach (var name in OperationArguments.AllowedFor(action)
                     .Where(OperationArguments.IsInteger).Order(StringComparer.Ordinal))
        {
            var (minimum, maximum) = OperationArguments.IntegerBoundsFor(action, name);
            Assert.Equal(minimum, OperationArguments
                .Normalize(action, With(action, name, minimum))[name]!.GetValue<int>());
            Assert.Equal(maximum, OperationArguments
                .Normalize(action, With(action, name, maximum))[name]!.GetValue<int>());
            var canonical = OperationArguments.Normalize(action, MinimalArguments(action))[name]
                ?.GetValue<int>();

            // A number the model quoted is the one unusable shape that is not a mistake about the
            // page, so it is read rather than discarded.
            Assert.Equal(maximum, OperationArguments.Normalize(
                action, With(action, name, maximum.ToString(CultureInfo.InvariantCulture)))[name]!
                .GetValue<int>());
            foreach (var unusable in new JsonNode[]
                     {
                         minimum - 1, maximum + 1, "all", 1.5, new JsonObject(), true,
                     })
            {
                var recovered = OperationArguments.Normalize(
                    action, With(action, name, unusable))[name]?.GetValue<int>();

                Assert.Equal(canonical, recovered);
                if (recovered is { } value)
                    Assert.InRange(value, minimum, maximum);
            }
        }
    }

    // "required" and the anyOf choices are only worth emitting if they are exactly what the gate
    // does with a missing argument. Two things can happen and the schema asks for both: the plan
    // is refused, or the point in time is completed to today. Nothing else may be silently filled
    // in, which is what the last loop asserts by removing each declared demand in turn.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_demands_exactly_the_arguments_the_schema_declares_required(string action)
    {
        var today = new DateOnly(2026, 3, 4);
        var allowed = OperationArguments.AllowedFor(action);
        var required = OperationArguments.RequiredFor(action);
        var defaulted = OperationArguments.DefaultedDatesFor(action);
        var choices = OperationArguments.RequiredChoicesFor(action);
        Assert.All(required, name => Assert.Contains(name, allowed));
        Assert.All(defaulted, name =>
        {
            Assert.Contains(name, allowed);
            // Every completed argument is a single point in time. A comparison bound is never
            // completed: today is not a window, and inventing one answers a different question.
            Assert.True(OperationArguments.IsDate(name));
            Assert.DoesNotContain(name, new[] { "from_date", "to_date" });
        });
        Assert.All(choices, choice =>
        {
            Assert.NotEmpty(choice.Names);
            Assert.All(choice.Names, name => Assert.Contains(name, allowed));
        });

        OperationArguments.Normalize(action, MinimalArguments(action));

        foreach (var name in defaulted)
        {
            var arguments = MinimalArguments(action);
            arguments.Remove(name);

            Assert.Equal("2026-03-04", OperationArguments
                .Normalize(action, arguments, today: today)[name]!.GetValue<string>());
        }
        foreach (var name in required.Except(defaulted, StringComparer.Ordinal))
        {
            var arguments = MinimalArguments(action);
            arguments.Remove(name);
            Assert.Throws<InvalidDataException>(() =>
                OperationArguments.Normalize(action, arguments));
        }
        foreach (var choice in choices)
        {
            var arguments = MinimalArguments(action);
            foreach (var name in choice.Names) arguments.Remove(name);
            Assert.Equal(choice.Message, Assert.Throws<InvalidDataException>(() =>
                OperationArguments.Normalize(action, arguments)).Message);
        }
    }

    // The pattern is the assertion the planner actually honours; "format": "date" is an
    // annotation. It must agree with DateOnly.TryParseExact on every axis a regex can reach.
    [Fact]
    public void The_iso_date_pattern_matches_the_gate_on_every_structural_axis()
    {
        foreach (var value in new[] { "2024-01-01", "2024-12-31", "2000-02-29", "0001-01-01" })
        {
            Assert.Matches(OperationArguments.IsoDatePattern, value);
            Assert.True(DateOnly.TryParseExact(
                value, OperationArguments.IsoDateFormat, out _));
        }
        foreach (var value in new[]
                 {
                     "2024", "2024-1-1", "2024-13-01", "2024-00-01", "2024-01-32", "2024-01-00",
                     "01-01-2024", "2024/01/01", " 2024-01-01", "2024-01-01 ", "24-01-01",
                 })
        {
            Assert.DoesNotMatch(OperationArguments.IsoDatePattern, value);
            Assert.False(DateOnly.TryParseExact(
                value, OperationArguments.IsoDateFormat, out _));
        }

        // The one axis no regex reaches. The gate stays the only authority on the calendar.
        Assert.Matches(OperationArguments.IsoDatePattern, "2026-02-30");
        Assert.False(DateOnly.TryParseExact(
            "2026-02-30", OperationArguments.IsoDateFormat, out _));
    }

    // M2, and the sharpest line in the recovery policy. An absent date is completed; a date that
    // is PRESENT and does not parse is refused, because "2024" could mean 2024-01-01 or
    // 2024-12-31 and either choice silently selects a different version of the law. Every date
    // argument the action accepts is probed, not only the ones it requires, so a bad
    // search.as_of cannot slip past on the way to MCP.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_refuses_every_date_the_schema_pattern_refuses(string action)
    {
        foreach (var name in OperationArguments.AllowedFor(action)
                     .Where(OperationArguments.IsDate).Order(StringComparer.Ordinal))
            foreach (var malformed in new[]
                     {
                         "2024", "2024-1-1", "01-01-2024", "2024/01/01", "12/03/2021", "2021-3-1",
                         "yesterday", "2026-02-30",
                     })
            {
                Assert.Equal($"Argument '{name}' must be an ISO date.",
                    Assert.Throws<InvalidDataException>(() =>
                        OperationArguments.Normalize(
                            action, With(action, name, malformed))).Message);
            }
    }

    [Fact]
    public void The_gate_bounds_clarification_options_exactly_as_the_schema_declares()
    {
        var accepted = MinimalArguments("clarification");
        accepted["options"] = new JsonArray(
            new string('x', OperationArguments.MaximumOptionLength), "second");

        OperationArguments.Normalize("clarification", accepted);

        foreach (var options in new[]
                 {
                     new JsonArray(new string('x', OperationArguments.MaximumOptionLength + 1), "b"),
                     new JsonArray("only one"),
                     new JsonArray("a", "b", "c", "d", "e"),
                 })
        {
            var rejected = MinimalArguments("clarification");
            rejected["options"] = options;
            Assert.Throws<InvalidDataException>(() =>
                OperationArguments.Normalize("clarification", rejected));
        }
    }

    // The three shapes seen in production. The schema now stops the model proposing them; the
    // gate must keep refusing them, with the same messages, if it ever does.
    [Fact]
    public void A_malformed_date_and_an_over_length_work_identity_still_abort_the_plan()
    {
        var cases = new (string Tool, JsonObject Arguments, string Message)[]
        {
            ("diff", new JsonObject
            {
                ["work_query"] = "CRR",
                ["from_date"] = "2020",
                ["to_date"] = "2024-12-31",
            }, "Argument 'from_date' must be an ISO date."),
            ("as_of", new JsonObject
            {
                ["work"] = new string('w', 1_001),
                ["date"] = "2024-01-01",
            }, "Argument 'work' must contain 1 to 1000 characters."),
            ("as_of", new JsonObject
            {
                ["work_query"] = new string('w', 901),
                ["date"] = "2024-01-01",
            }, "Argument 'work_query' must contain 1 to 900 characters."),
        };

        foreach (var (tool, arguments, message) in cases)
        {
            var rejected = Assert.Throws<InvalidDataException>(() =>
                OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
                    new JsonObject { ["tool"] = tool, ["arguments"] = arguments })));

            Assert.Equal(message, rejected.Message);
        }
    }

    // D1, the whole of the date recovery, against the two operations that carry a single point in
    // time. Thirteen of the sixteen refusals the audit measured were this: the model omitted a
    // date and the plan died reporting a format error about a value nobody sent.
    [Theory]
    [InlineData("as_of", """{"work_query":"GDPR","article_number":"6"}""")]
    [InlineData("in_force_on", "{}")]
    public void An_operation_with_no_date_is_answered_as_the_law_stands_today(
        string tool, string argumentsJson)
    {
        var today = new DateOnly(2026, 3, 4);

        var normalized = OperationArguments.Normalize(
            tool, JsonNode.Parse(argumentsJson)!.AsObject(), out var repairs, today: today);

        Assert.Equal("2026-03-04", normalized["date"]!.GetValue<string>());
        Assert.Contains($"{tool}.date defaulted", repairs);
        // The date is not merely accepted, it is the one MCP is handed, and MCP demands one.
        McpInputPolicy.Validate(tool, Executable(tool, normalized));
    }

    [Fact]
    public void A_dateless_operation_freezes_into_a_plan_and_reaches_execution()
    {
        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
                new JsonObject
                {
                    ["tool"] = "as_of",
                    ["arguments"] = new JsonObject { ["work_query"] = "GDPR" },
                },
                new JsonObject
                {
                    ["tool"] = "in_force_on",
                    ["arguments"] = new JsonObject(),
                }),
            today: new DateOnly(2026, 3, 4));

        Assert.Equal(["as_of", "in_force_on"], plan.Operations.Select(item => item.Tool));
        Assert.All(plan.Operations, operation =>
            Assert.Equal("2026-03-04", operation.Arguments.GetProperty("date").GetString()));
        // With no article to narrow it, the whole text is what the reader gets. mode=select with
        // nothing selected showed them none of it.
        Assert.Equal("full", plan.Operations[0].Arguments.GetProperty("mode").GetString());
    }

    // Left uninjected, the substituted instant is today in UTC, which is the same date the
    // planner prompt promises the model. The two are one value per request.
    [Fact]
    public void The_substituted_instant_is_today_in_utc()
    {
        var before = DateOnly.FromDateTime(DateTime.UtcNow);

        var normalized = OperationArguments.Normalize(
            "as_of", new JsonObject { ["work_query"] = "GDPR" });

        Assert.Contains(normalized["date"]!.GetValue<string>(),
            new[] { before, DateOnly.FromDateTime(DateTime.UtcNow) }
                .Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    // Dr2. The archetype from the audit: as_of fetches exactly one work, its identity is carried
    // by work_query, and the plan that results is byte-identical to the plan written without the
    // stray. Byte-identical is the assertion, because it is also the proof that no default
    // quietly filled the slot the stray occupied.
    [Fact]
    public void A_stray_argument_from_another_operation_is_dropped_and_recorded()
    {
        var today = new DateOnly(2026, 3, 4);
        var clean = OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            ["date"] = "2019-01-01",
        }, today: today);

        var recovered = OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            ["date"] = "2019-01-01",
            ["publisher"] = "eu-eurlex",
        }, out var repairs, today: today);

        Assert.Equal(clean.ToJsonString(), recovered.ToJsonString());
        Assert.Equal(["as_of.publisher dropped"], repairs);
    }

    // Dr0, and the one interaction in this design that could silently change the law: as_of
    // carrying search's spelling of the same concept. Dropping that key would hand the slot to
    // the date default and answer a 2019 question with today's law, labelled today. It is
    // renamed instead, so the instant the model supplied is the instant that is answered.
    [Fact]
    public void The_other_spelling_of_the_point_in_time_is_renamed_rather_than_dropped()
    {
        var normalized = OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            ["as_of"] = "2019-01-01",
        }, out var repairs, today: new DateOnly(2026, 3, 4));

        Assert.Equal("2019-01-01", normalized["date"]!.GetValue<string>());
        Assert.Null(normalized["as_of"]);
        Assert.Equal(["as_of.as_of aliased"], repairs.ToArray());

        // A blank date is absence here as it is everywhere else, so the alias still wins rather
        // than colliding with an empty string and letting the default take the slot.
        Assert.Equal("2019-01-01", OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            ["date"] = "  ",
            ["as_of"] = "2019-01-01",
        }, today: new DateOnly(2026, 3, 4))["date"]!.GetValue<string>());
    }

    // The same instant under either spelling, refused for the same reason. "as_of": 2019 is a
    // present value naming a point in time, not a slot the model left blank, so it may not become
    // today's law: a non-string is refused under the alias exactly as it is under date, and only
    // JSON null and a blank string are read as absence.
    //
    // The message names the JSON kind that actually arrived. Without it two different defects
    // ("as_of" written as a number, work_query written as an array) are one indistinguishable log
    // line, which is how a pre-existing branch was read as a new violation class. A JsonValueKind
    // name is one of eight compile-time constants chosen by the shape of the JSON, never by its
    // content, so it carries no user data onto the diagnostic line.
    [Theory]
    [InlineData("as_of")]
    [InlineData("date")]
    public void An_instant_the_planner_did_not_write_as_a_string_aborts_the_plan(string name)
    {
        foreach (var (value, kind) in new (JsonNode Value, string Kind)[]
                 {
                     (2019, "Number"),
                     (true, "True"),
                     (false, "False"),
                     (new JsonArray("2019-01-01"), "Array"),
                     (new JsonObject(), "Object"),
                 })
            Assert.Equal($"Argument '{name}' must be a string. Received {kind}.",
                Assert.Throws<InvalidDataException>(() =>
                    OperationArguments.Normalize("as_of", new JsonObject
                    {
                        ["work_query"] = "GDPR",
                        [name] = value,
                    }, today: new DateOnly(2026, 3, 4))).Message);

        // JSON null is the absence it looks like, under either spelling.
        Assert.Equal("2026-03-04", OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            [name] = null,
        }, today: new DateOnly(2026, 3, 4))["date"]!.GetValue<string>());
    }

    // A dropped stray is the one repair whose name the planner chose rather than the gate, and a
    // model can be talked into naming a key after the question it was asked. These lines go to
    // stderr and into the reply trace, so a name no operation declares is reported as a
    // placeholder: the count survives, the user's words do not reach the log.
    [Fact]
    public void A_repair_line_never_carries_a_name_the_planner_invented()
    {
        OperationArguments.Normalize("coverage", new JsonObject
        {
            ["publisher"] = "lu-legilux",
            ["note about the client Jean Dupont"] = "x",
            ["another invented key"] = "y",
        }, out var repairs, today: new DateOnly(2026, 3, 4));

        Assert.Equal(["coverage.unrecognized dropped", "coverage.unrecognized dropped"],
            repairs.ToArray());

        // A name another operation does declare is still named, because that is the drift signal
        // the line exists to carry.
        OperationArguments.Normalize("coverage", new JsonObject
        {
            ["publisher"] = "lu-legilux",
            ["language"] = "fr",
        }, out var named, today: new DateOnly(2026, 3, 4));

        Assert.Equal(["coverage.language dropped"], named.ToArray());
    }

    // The conditional half of D1: time_scope=as_of is the only shape that gives search a point in
    // time, and it used to abort the plan when the model named the scope but not the instant.
    [Fact]
    public void A_point_in_time_search_with_no_instant_searches_the_law_as_it_stands_today()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = "data protection",
            ["time_scope"] = "as_of",
        }, out var repairs, today: new DateOnly(2026, 3, 4));

        McpInputPolicy.Validate("search", normalized);
        Assert.Equal("2026-03-04", normalized["as_of"]!.GetValue<string>());
        Assert.Equal(["search.as_of defaulted"], repairs.ToArray());

        // An all_versions search is left open: an instant it never asked for would narrow it.
        Assert.Null(OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = "data protection",
        }, today: new DateOnly(2026, 3, 4))["as_of"]);
    }

    [Fact]
    public void Two_different_instants_on_one_operation_abort_the_plan()
    {
        var rejected = Assert.Throws<InvalidDataException>(() =>
            OperationArguments.Normalize("as_of", new JsonObject
            {
                ["work_query"] = "GDPR",
                ["date"] = "2019-01-01",
                ["as_of"] = "2024-06-30",
            }));

        Assert.Equal("Operation 'as_of' contains unsupported argument 'as_of'.",
            rejected.Message);

        // The same instant written twice is not a contradiction and does not need one.
        Assert.Equal("2019-01-01", OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            ["date"] = "2019-01-01",
            ["as_of"] = "2019-01-01",
        })["date"]!.GetValue<string>());
    }

    // M3. A comparison window has no default. Today is not a window, and inventing one answers a
    // question the reader did not ask; nor is the pair swapped when it is reversed, because that
    // exchanges the before and the after and inverts every added and removed clause in the diff.
    [Theory]
    [InlineData("diff", """{"work_query":"CRR","to_date":"2024-12-31"}""")]
    [InlineData("diff", """{"work_query":"CRR","from_date":"2020-01-01"}""")]
    [InlineData("diff", """{"work_query":"CRR"}""")]
    [InlineData("diff", """{"work_query":"CRR","from_date":"","to_date":"2024-12-31"}""")]
    [InlineData("changes_in_period", """{"to_date":"2024-12-31"}""")]
    [InlineData("changes_in_period", """{"from_date":"2024-01-01"}""")]
    public void A_comparison_window_is_never_completed_for_the_reader(
        string tool, string argumentsJson)
    {
        Assert.Throws<InvalidDataException>(() => OperationArguments.Normalize(
            tool, JsonNode.Parse(argumentsJson)!.AsObject(), today: new DateOnly(2026, 3, 4)));

        Assert.Equal("diff from_date must not follow to_date.",
            Assert.Throws<InvalidDataException>(() => OperationArguments.Normalize(
                "diff", new JsonObject
                {
                    ["work_query"] = "CRR",
                    ["from_date"] = "2024-12-31",
                    ["to_date"] = "2020-01-01",
                })).Message);
    }

    // The blast radius the recovery policy exists to remove: one stray key on the last operation
    // used to destroy the four valid operations before it.
    [Fact]
    public void One_repairable_operation_no_longer_destroys_the_rest_of_the_plan()
    {
        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
                JsonNode.Parse("""{"tool":"coverage","arguments":{}}"""),
                JsonNode.Parse("""{"tool":"search","arguments":{"query":"data protection"}}"""),
                JsonNode.Parse(
                    """{"tool":"as_of","arguments":{"work_query":"GDPR","publisher":"eu-eurlex","language":"en"}}""")),
            today: new DateOnly(2026, 3, 4));

        Assert.Equal(["coverage", "search", "as_of"], plan.Operations.Select(item => item.Tool));
        Assert.Equal(["as_of.publisher dropped", "as_of.date defaulted"],
            plan.Operations[2].Repairs.ToArray());
        Assert.All(plan.Operations.Take(2), operation => Assert.Empty(operation.Repairs));
    }

    // The standing invariant behind the never-dropped set: a value may never be dropped if a
    // default would then fill the same slot. Proved structurally rather than by example, because
    // the next widening of the droppable set is where it would be broken.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void No_argument_a_default_fills_can_be_dropped_as_a_stray(string action)
    {
        var allowed = OperationArguments.AllowedFor(action);
        var minimal = MinimalArguments(action);
        var today = new DateOnly(2026, 3, 4);
        var filled = OperationArguments.Normalize(
            action, MinimalArguments(action), out var baseline, today: today);

        // Everything a default fills is an argument this action accepts, so it can only ever
        // arrive as a value, never as a droppable stray.
        foreach (var name in filled.Select(item => item.Key).Except(
                     minimal.Select(item => item.Key), StringComparer.Ordinal))
            Assert.Contains(name, allowed);

        // And every stray this action does drop leaves the frozen arguments untouched.
        foreach (var stray in new[] { "publisher", "language", "order", "limit", "reason" })
        {
            if (allowed.Contains(stray)) continue;
            var arguments = MinimalArguments(action);
            arguments[stray] = stray == "limit" ? 7 : "eu-eurlex";

            var recovered = OperationArguments.Normalize(
                action, arguments, out var repairs, today: today);

            Assert.Equal(filled.ToJsonString(), recovered.ToJsonString());
            Assert.Equal([$"{action}.{stray} dropped", .. baseline], repairs.ToArray());
        }
    }

    // A repair is a fact about the plan, not a debug aid: it has to survive into the frozen
    // operation so AskService can count it, and it may never carry a value or user text.
    [Fact]
    public void Every_repair_is_recorded_on_the_frozen_operation_without_a_value()
    {
        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(new JsonObject
        {
            ["tool"] = "search",
            ["arguments"] = new JsonObject
            {
                ["query"] = "renewable energy",
                ["retrieval_mode"] = "semantic",
                ["limit"] = "not a number",
                ["mode"] = "full",
            },
        }));

        var operation = Assert.Single(plan.Operations);
        // Name recovery runs first, then per-value recovery, then the closed sets, because a
        // bad value has to be gone before a default can refill it.
        Assert.Equal(
            ["search.mode dropped", "search.limit dropped", "search.retrieval_mode dropped"],
            operation.Repairs.ToArray());
        Assert.All(operation.Repairs, repair =>
        {
            Assert.DoesNotContain("semantic", repair, StringComparison.Ordinal);
            Assert.DoesNotContain("renewable", repair, StringComparison.Ordinal);
        });
        Assert.Equal("keyword", operation.Arguments.GetProperty("retrieval_mode").GetString());
        Assert.Equal(10, operation.Arguments.GetProperty("limit").GetInt32());
    }

    // The arguments that leave this gate are handed to a legal operation, so a repaired plan has
    // to satisfy the public contract exactly as an unrepaired one does.
    [Fact]
    public void A_fully_repaired_operation_still_satisfies_the_public_mcp_contract()
    {
        var normalized = OperationArguments.Normalize("in_force_on", new JsonObject
        {
            ["date"] = "   ",
            ["limit"] = "9000",
            ["offset"] = -3,
            ["publisher"] = "lu-legilux",
        }, out var repairs, today: new DateOnly(2026, 3, 4));

        McpInputPolicy.Validate("in_force_on", normalized);
        Assert.Equal("2026-03-04", normalized["date"]!.GetValue<string>());
        Assert.Equal(50, normalized["limit"]!.GetValue<int>());
        Assert.Equal(0, normalized["offset"]!.GetValue<int>());
        Assert.Equal(
            ["in_force_on.date dropped", "in_force_on.limit dropped", "in_force_on.offset dropped",
                "in_force_on.date defaulted"],
            repairs);
    }

    // Keeping a quoted number is still a repair. Counting it separately is what distinguishes a
    // planner writing the wrong JSON shape from a planner asking for a page nobody can serve.
    [Fact]
    public void A_page_bound_the_planner_quoted_is_kept_and_counted_as_its_own_repair()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = "renewable energy",
            ["limit"] = "25",
        }, out var repairs);

        Assert.Equal(25, normalized["limit"]!.GetValue<int>());
        Assert.Equal(["search.limit coerced"], repairs);
    }

    [Fact]
    public void A_page_bound_the_planner_wrote_correctly_is_not_a_repair()
    {
        OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = "renewable energy",
            ["limit"] = 25,
        }, out var repairs);

        Assert.Empty(repairs);
    }

    // A quoted number outside the action's bounds is the page nobody can serve, so it is dropped
    // rather than coerced, and it is counted under the verb that says so.
    [Fact]
    public void A_quoted_page_bound_outside_its_range_is_dropped_rather_than_coerced()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = "renewable energy",
            ["limit"] = "9000",
        }, out var repairs);

        Assert.Equal(10, normalized["limit"]!.GetValue<int>());
        Assert.Equal(["search.limit dropped"], repairs);
    }

    // work_query is resolved to a work before execution; MCP is never shown it.
    private static JsonObject Executable(string tool, JsonObject normalized)
    {
        var arguments = normalized.DeepClone().AsObject();
        if (arguments.Remove("work_query")) arguments["work"] = "eu-eurlex:32016r0679";
        if (tool == "as_of" && arguments.Remove("article_number"))
            arguments["anchors"] = "art_6";
        return arguments;
    }

    private static JsonObject MinimalArguments(string action)
    {
        var arguments = new JsonObject();
        foreach (var name in OperationArguments.RequiredFor(action))
            arguments[name] = SampleValue(name);
        foreach (var choice in OperationArguments.RequiredChoicesFor(action))
            arguments[choice.Names[0]] = SampleValue(choice.Names[0]);
        return arguments;
    }

    private static JsonObject With(string action, string name, JsonNode value)
    {
        var arguments = MinimalArguments(action);
        arguments[name] = value;
        return arguments;
    }

    private static JsonNode SampleValue(string name) => name switch
    {
        "options" => new JsonArray("first", "second"),
        _ when OperationArguments.IsInteger(name) => 1,
        _ when OperationArguments.IsDate(name) => "2024-01-01",
        _ when OperationArguments.AllowedValuesFor(name) is { } values => values[0],
        "article_number" => "92",
        _ => "eu-eurlex:32013r0575",
    };

    // anchors and works are comma-separated lists inside one string, and the gate caps each item
    // as well as the whole value, so a probe of the whole-value maximum has to be chunked. That
    // split is the constraint JSON Schema cannot express and it stays validator-only.
    private static string LengthProbe(string name, int length)
    {
        var probe = new StringBuilder(new string('x', length));
        if (name is not ("anchors" or "works")) return probe.ToString();
        for (var index = 400; index < probe.Length; index += 401) probe[index] = ',';
        return probe.ToString();
    }

    public static TheoryData<string, string, string> AdvertisedPlannerValues
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (tool, argument) in new[]
                     {
                         ("search", "retrieval_mode"), ("search", "time_scope"),
                         ("search", "fuzzy"), ("as_of", "mode"),
                         ("changes_in_period", "order"),
                     })
                foreach (var value in OperationArguments.AllowedValuesFor(argument)!)
                    data.Add(tool, argument, value);
            return data;
        }
    }

    // A schema may never advertise a value the gate rejects. Both conditionally coupled values
    // are supplied with the argument the second gate requires, which is what the property
    // descriptions on time_scope and mode tell the model to do.
    [Theory]
    [MemberData(nameof(AdvertisedPlannerValues))]
    public void Every_value_the_planner_schema_advertises_freezes_into_a_plan(
        string tool, string argument, string value)
    {
        var arguments = tool switch
        {
            "search" => new JsonObject { ["query"] = "renewable energy" },
            "as_of" => new JsonObject
            {
                ["work_query"] = "GDPR", ["date"] = "2021-01-01", ["article_number"] = "6",
            },
            _ => new JsonObject { ["from_date"] = "2024-01-01", ["to_date"] = "2024-12-31" },
        };
        arguments[argument] = value;
        if (value == "as_of") arguments["as_of"] = "2024-01-01";

        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
            new JsonObject { ["tool"] = tool, ["arguments"] = arguments }));

        Assert.Equal(tool, Assert.Single(plan.Operations).Tool);
    }

    // D7. Each of these governs how the corpus is searched or how much of one document is
    // rendered, never which law or which instant, so an invented value falls back to the default
    // rather than destroying the plan. time_scope belongs here because its default widens the
    // version set, and a wider search can add a hit but never hide one.
    [Theory]
    [InlineData("search", "time_scope", "current", null)]
    [InlineData("search", "retrieval_mode", "semantic", "keyword")]
    [InlineData("search", "fuzzy", "true", "auto")]
    [InlineData("as_of", "mode", "text", "full")]
    public void A_tuning_value_outside_the_closed_set_falls_back_to_its_default(
        string tool, string argument, string value, string? expected)
    {
        var arguments = ShippedArguments(tool);
        arguments[argument] = value;

        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
            new JsonObject { ["tool"] = tool, ["arguments"] = arguments }));

        var operation = Assert.Single(plan.Operations);
        Assert.Equal(expected, operation.Arguments.TryGetProperty(argument, out var frozen)
            ? frozen.GetString() : null);
        Assert.Contains($"{tool}.{argument} dropped", operation.Repairs);
    }

    // order is the one closed set that does not qualify, and the reason is worth keeping: it
    // interacts with limit. Dropping it yields by_date, so a top-20 by recency goes to a reader
    // who asked which laws changed most, with the rows they asked for silently outside the
    // window. That is a plausible-looking answer to a different question. The prompt names
    // order=by_churn explicitly, so a bad value here means the model misread the request.
    [Fact]
    public void An_order_outside_the_closed_set_still_aborts_the_plan()
    {
        var arguments = ShippedArguments("changes_in_period");
        arguments["order"] = "churn";

        var rejected = Assert.Throws<InvalidDataException>(() =>
            OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
                new JsonObject { ["tool"] = "changes_in_period", ["arguments"] = arguments })));

        Assert.Contains("has an unsupported value", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("'order'", rejected.Message, StringComparison.Ordinal);
    }

    private static JsonObject ShippedArguments(string tool) => tool switch
    {
        "search" => new JsonObject { ["query"] = "renewable energy" },
        "as_of" => new JsonObject { ["work_query"] = "GDPR", ["date"] = "2021-01-01" },
        _ => new JsonObject { ["from_date"] = "2024-01-01", ["to_date"] = "2024-12-31" },
    };

    // Four copies of these literals exist. This is the net for the next edit to any of them.
    [Fact]
    public void The_public_mcp_schema_and_the_assistant_value_sets_cannot_drift()
    {
        var tools = new McpCore(new Dictionary<string, LexIndexReader>()).ToolDefs()
            .OfType<JsonObject>().ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tool in tools)
            foreach (var (name, schema) in tool["inputSchema"]!["properties"]!.AsObject())
            {
                if (schema!["enum"] is not JsonArray values) continue;
                seen.Add(name);
                Assert.Equal(OperationArguments.AllowedValuesFor(name),
                    values.Select(item => item!.GetValue<string>()));
            }

        Assert.Equal(["fuzzy", "mode", "order", "retrieval_mode", "time_scope"],
            seen.Order(StringComparer.Ordinal));
    }

    // Layer one of the hallucinated-publisher defect: the planner was handed publisher as a bare
    // string, emitted "EU" or "Luxembourg", and every layer below read the resulting zero-reader
    // selection as an empty corpus.
    [Fact]
    public void The_planner_sees_only_the_publishers_and_jurisdictions_actually_mounted()
    {
        var vocabulary = new CorpusVocabulary(
            ["lu-legilux", "eu-eurlex"], ["LU", "EU"]);
        var corpusScoped = 0;

        foreach (var branch in PlannerBranches(vocabulary))
            foreach (var (name, schema) in
                     branch.Value["properties"]!["arguments"]!["properties"]!.AsObject())
            {
                if (name is not ("publisher" or "jurisdiction")) continue;
                corpusScoped++;
                // Ordinal order, so the emitted schema is byte-identical between processes.
                Assert.Equal(
                    name == "publisher"
                        ? new[] { "eu-eurlex", "lu-legilux" }
                        : ["EU", "LU"],
                    schema!["enum"]!.AsArray().Select(item => item!.GetValue<string>()));
                Assert.Contains("OMIT", schema["description"]!.GetValue<string>(),
                    StringComparison.Ordinal);
            }

        // search, changes_in_period and in_force_on carry both; coverage carries publisher alone.
        Assert.Equal(7, corpusScoped);
    }

    // A zero-index build mounts nothing, and `"enum": []` is invalid JSON Schema that some model
    // APIs reject outright: planning would break entirely on exactly the build that can least
    // afford it. No mounted set means no constraint.
    [Fact]
    public void An_unmounted_build_constrains_no_corpus_filter_rather_than_emitting_an_empty_enum()
    {
        var schema = AskService.PlannerArgumentSchema("coverage",
            new CorpusVocabulary([], []));

        Assert.Null(schema["properties"]!["publisher"]!["enum"]);
        Assert.Equal("string", schema["properties"]!["publisher"]!["type"]!.GetValue<string>());
    }

    // Layer two: the schema is a request, not a guarantee. A mounted id spelled with different
    // case is restored before the plan is frozen; a value nothing mounts is neither rewritten nor
    // thrown, because throwing here aborts every other operation in the same plan.
    [Fact]
    public void A_corpus_filter_is_canonicalised_against_the_mounted_set_before_the_plan_freezes()
    {
        var vocabulary = new CorpusVocabulary(["lu-legilux", "eu-eurlex"], ["LU", "EU"]);

        Assert.Equal("lu-legilux", OperationArguments.Normalize(
            "coverage", new JsonObject { ["publisher"] = "LU-Legilux" },
            vocabulary)["publisher"]!.GetValue<string>());
        Assert.Equal("LU", OperationArguments.Normalize(
            "search", new JsonObject { ["query"] = "travail", ["jurisdiction"] = "lu" },
            vocabulary)["jurisdiction"]!.GetValue<string>());

        var unmatched = OperationArguments.Normalize(
            "coverage", new JsonObject { ["publisher"] = "Luxembourg" }, vocabulary);
        Assert.Equal("Luxembourg", unmatched["publisher"]!.GetValue<string>());
    }

    // Layer three's standalone safety net: whatever emptied a coverage payload, an inventory of
    // nothing is a gap. CoverageView([]) is summed into "Lex mounts 0 works and 0 verified
    // versions", a false statement about the product's own holdings.
    [Fact]
    public void An_empty_coverage_payload_is_never_rendered_as_a_count_of_the_holdings()
    {
        foreach (var payload in new[] { "[]", """{"status":"unknown_publisher"}""" })
        {
            var effect = UiMapper.From(
                "coverage", new JsonObject(), JsonNode.Parse(payload)!, "en");

            Assert.Null(effect.Coverage);
            Assert.NotNull(effect.Gap);
            Assert.NotEmpty(effect.Gap!.Explanation);
        }
    }

    private static IEnumerable<KeyValuePair<string, JsonObject>> PlannerBranches(
        CorpusVocabulary vocabulary) =>
        AskService.PlannerTools(vocabulary)[0]!["function"]!["parameters"]!["properties"]!
            ["operations"]!["items"]!["anyOf"]!.AsArray()
            .Select(node => new KeyValuePair<string, JsonObject>(
                node!["properties"]!["tool"]!["const"]!.GetValue<string>(), node.AsObject()));

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

    // M4. A stray whose name is a date, a work identity, an article number, an anchor or works
    // still aborts, because none of them can be dropped without answering a different question.
    [Fact]
    public void A_planner_argument_outside_the_allowlist_still_aborts_the_plan()
    {
        var rejected = Assert.Throws<InvalidDataException>(() => OperationPlan.FromPlannerOutput(
            "req-1", "en", new JsonArray(JsonNode.Parse(
                """{"tool":"search","arguments":{"query":"renewable energy","article_number":"92"}}"""))));

        Assert.Contains("'search'", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("'article_number'", rejected.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string> NeverDroppedStrayNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in new[]
                     {
                         "date", "as_of", "from_date", "to_date", "work", "work_query", "lex_id",
                         "article_number", "anchor", "anchors", "works",
                     })
                data.Add(name);
            return data;
        }
    }

    // coverage accepts publisher and nothing else, so every one of these is a stray there. Each
    // names the instant, the law or the provision: dropping a date lets the default substitute
    // today for an instant the model supplied, dropping a work identity resolves a different law,
    // dropping an anchor or an article number returns a different provision, and dropping works
    // widens a scan the user restricted to named laws.
    [Theory]
    [MemberData(nameof(NeverDroppedStrayNames))]
    public void A_stray_naming_the_law_the_provision_or_the_instant_is_never_dropped(string name)
    {
        var rejected = Assert.Throws<InvalidDataException>(() => OperationArguments.Normalize(
            "coverage", new JsonObject { [name] = "2019-01-01" }));

        Assert.Equal($"Operation 'coverage' contains unsupported argument '{name}'.",
            rejected.Message);
    }

    public static TheoryData<string, LegalOutcome> StatusCases => new()
    {
        { McpStatus.Ok, LegalOutcome.Succeeded },
        { McpStatus.NoResult, LegalOutcome.SucceededEmpty },
        { McpStatus.NoChangesInPeriod, LegalOutcome.SucceededEmpty },
        { McpStatus.ProfilesDiffer, LegalOutcome.NotComparable },
        { McpStatus.AmbiguousVersion, LegalOutcome.NeedsClarification },
        { McpStatus.UnknownWork, LegalOutcome.NotFound },
        { McpStatus.UnknownAnchor, LegalOutcome.NotFound },
        { McpStatus.UnknownPublisher, LegalOutcome.NotFound },
        { McpStatus.NoVersionForDate, LegalOutcome.NotAvailable },
        { McpStatus.AnchorNotInVersion, LegalOutcome.NotAvailable },
        { McpStatus.NoProvisionHistory, LegalOutcome.NotAvailable },
        { McpStatus.TextNotAvailable, LegalOutcome.NotAvailable },
        { McpStatus.TextWithheld, LegalOutcome.NotAvailable },
        { McpStatus.NoCorpusMounted, LegalOutcome.NotAvailable },
        { McpStatus.RetrievalModeUnavailable, LegalOutcome.NotAvailable },
        { McpStatus.FilterNotSupportedByIndex, LegalOutcome.NotAvailable },
        { LegalOperationStatus.PartialResult, LegalOutcome.Succeeded },
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
        Assert.Equal(McpStatus.All.Append(LegalOperationStatus.PartialResult).Order(),
            StatusCases.Select(item => (string)item[0]).Order());
        Assert.Throws<InvalidDataException>(() =>
            LegalOperationPolicy.OutcomeForStatus("outside_observed_window"));
        Assert.Throws<InvalidDataException>(() =>
            LegalOperationPolicy.OutcomeForStatus("plausible_but_unknown"));
    }

    [Theory]
    [InlineData("search", LegalResultClass.Search)]
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
    public void An_ambiguous_publisher_version_dominates_successful_aggregate_rows()
    {
        var payload = new JsonArray(
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["status"] = McpStatus.AmbiguousVersion,
                },
            });

        Assert.Equal(McpStatus.AmbiguousVersion,
            LegalOperationPolicy.StatusForResult(payload));
        Assert.Equal(LegalOutcome.NeedsClarification,
            LegalOperationPolicy.OutcomeForResult(McpStatus.AmbiguousVersion, payload));
    }

    [Fact]
    public void One_catalog_generates_both_public_and_planner_argument_contracts()
    {
        var publicTools = new McpCore(new Dictionary<string, LexIndexReader>()).ToolDefs();

        Assert.Equal(
            LegalOperationCatalog.McpToolDefinitions().ToJsonString(),
            publicTools.ToJsonString());
        foreach (var operation in LegalOperationCatalog.Operations)
            Assert.Equal(
                operation.PlannerInputSchema().ToJsonString(),
                AskService.PlannerArgumentSchema(operation.Name).ToJsonString());
    }

    [Fact]
    public void Catalog_names_and_projection_ordinals_are_unique_complete_and_stable()
    {
        Assert.Equal(LegalOperationCatalog.Operations.Count,
            LegalOperationCatalog.Operations.Select(operation => operation.Name)
                .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, LegalOperationCatalog.Operations.Count),
            LegalOperationCatalog.Operations.Select(operation => operation.PlannerOrdinal)
                .Order());
        Assert.Equal(Enumerable.Range(0, LegalOperationCatalog.Operations.Count),
            LegalOperationCatalog.Operations.Select(operation => operation.McpOrdinal)
                .Order());
        Assert.Equal(
            LegalOperationCatalog.Operations.OrderBy(operation => operation.PlannerOrdinal)
                .Select(operation => operation.Name),
            LegalOperationCatalog.ToolNames);
        Assert.Equal(
            LegalOperationCatalog.Operations.OrderBy(operation => operation.McpOrdinal)
                .Select(operation => operation.Name),
            LegalOperationCatalog.McpToolDefinitions().OfType<JsonObject>()
                .Select(tool => tool["name"]!.GetValue<string>()));
    }

    [Fact]
    public void Work_specific_planner_projection_exposes_only_closed_subject_references()
    {
        var references = new[] { "subject_1", "subject_2" };

        foreach (var operation in LegalOperationCatalog.Operations
                     .Where(operation => operation.RequiresWorkResolution))
        {
            var schema = operation.PlannerInputSchema(subjectReferences: references);
            var properties = schema["properties"]!.AsObject();

            Assert.DoesNotContain("work", properties.Select(item => item.Key));
            Assert.DoesNotContain("work_query", properties.Select(item => item.Key));
            Assert.DoesNotContain("lex_id", properties.Select(item => item.Key));
            Assert.Equal(references, properties["subject_ref"]!["enum"]!.AsArray()
                .Select(item => item!.GetValue<string>()));
            Assert.Contains("subject_ref", schema["required"]!.AsArray()
                .Select(item => item!.GetValue<string>()));
        }
    }

    [Fact]
    public void Closed_subject_prompt_never_teaches_forbidden_identity_arguments()
    {
        var prompt = AskService.PlannerPrompt("law.test", new DateOnly(2026, 8, 14));

        Assert.Contains("subject_ref", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("work_query=", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("article_number=", prompt, StringComparison.Ordinal);
        Assert.Contains("Never output work, work_query, lex_id or article_number", prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Mcp_2_advertises_the_closed_status_contract()
    {
        Assert.Equal("2.0.0", McpSdkBridge.ServerVersion);
        Assert.DoesNotContain("outside_observed_window", McpSdkBridge.ServerInstructions);
        Assert.Contains(McpStatus.RetrievalModeUnavailable, McpSdkBridge.ServerInstructions);
        Assert.Contains(McpStatus.FilterNotSupportedByIndex, McpSdkBridge.ServerInstructions);
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
    public void Quarantined_hybrid_search_is_an_explicit_user_visible_gap()
    {
        var effect = UiMapper.From("search", new JsonObject
        {
            ["query"] = "personal data", ["retrieval_mode"] = "hybrid",
        }, new JsonArray(new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "eu-eurlex",
                ["status"] = McpStatus.RetrievalModeUnavailable,
            },
            ["requested_retrieval_mode"] = "hybrid",
            ["retrieval_unavailable_reason"] = "benchmark_gate_failed",
            ["hits"] = new JsonArray(),
        }));

        Assert.Equal(McpStatus.RetrievalModeUnavailable, effect.Gap?.Status);
        Assert.Contains("signed retrieval benchmark", effect.Gap?.Explanation);
    }

    [Fact]
    public void Unsupported_index_filter_is_an_explicit_user_visible_gap()
    {
        var effect = UiMapper.From("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        }, new JsonArray(new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("domain"),
            ["hits"] = new JsonArray(),
        }), "fr");

        Assert.Equal(McpStatus.FilterNotSupportedByIndex, effect.Gap?.Status);
        Assert.Contains("filtre", effect.Gap?.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mixed_publisher_filter_refusal_preserves_supported_search_as_typed_partial_result()
    {
        var payload = new JsonArray(
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "lu-legilux",
                    ["jurisdiction"] = "LU",
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                },
                ["unsupported_filters"] = new JsonArray("domain"),
                ["hits"] = new JsonArray(),
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "eu-eurlex",
                    ["jurisdiction"] = "EU",
                    ["status"] = McpStatus.Ok,
                },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:32016r0679:2016-05-04",
                    ["anchor"] = "art_1",
                    ["title"] = "Synthetic held result",
                }),
            });

        Assert.Equal(LegalOperationStatus.PartialResult,
            LegalOperationPolicy.StatusForResult(payload));
        Assert.Equal(LegalOutcome.Succeeded,
            LegalOperationPolicy.OutcomeForResult(LegalOperationStatus.PartialResult, payload));
        var effect = UiMapper.From("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        }, payload);

        Assert.Null(effect.Gap);
        Assert.Equal("eu-eurlex:32016r0679:2016-05-04",
            Assert.Single(effect.Workspace?.Results ?? []).LexId);
        var limitation = Assert.Single(effect.PublisherLimitations ?? []);
        Assert.Equal(McpStatus.FilterNotSupportedByIndex, limitation.Status);
        Assert.Equal("lu-legilux", limitation.Publisher);
        Assert.Equal("LU", limitation.Jurisdiction);
        Assert.Equal(["domain"], limitation.UnsupportedFilters);

        var serialized = JsonSerializer.Serialize(effect);
        Assert.DoesNotContain("explanation", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mixed_publisher_summaries_preserve_supported_documents_for_synthesis()
    {
        static JsonObject Refusal() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["jurisdiction"] = "LU",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("domain"),
        };

        static JsonObject Supported(string tool) => tool switch
        {
            "search" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:32016r0679:2016-05-04",
                    ["anchor"] = "art_1",
                    ["snippet"] = "Bounded publisher excerpt",
                }),
            },
            "changes_in_period" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["work"] = "eu-eurlex:32016r0679",
                    ["versions_in_period"] = 1,
                    ["versions_total"] = 2,
                    ["first_change"] = "2024-01-01",
                    ["last_change"] = "2024-01-01",
                }),
            },
            "in_force_on" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["works"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:32016r0679:2016-05-04",
                    ["valid_from"] = "2016-05-04",
                }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        static (string? Status, JsonArray Docs) Summarize(JsonNode payload)
        {
            var method = typeof(AskService).GetMethod("Summarize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("AskService.Summarize was not found.");
            var value = (ValueTuple<string?, JsonArray>)method.Invoke(null, [payload])!;
            return (value.Item1, value.Item2);
        }

        foreach (var tool in new[] { "search", "changes_in_period", "in_force_on" })
        {
            var payload = new JsonArray(Refusal(), Supported(tool));
            var summary = Summarize(payload);

            Assert.Equal(LegalOperationStatus.PartialResult, summary.Status);
            Assert.NotEmpty(summary.Docs);
            var ledger = new AgentEvidenceLedger();
            ledger.Observe(tool, summary.Status, summary.Docs, payload, new JsonObject());
            var expectedKind = tool switch
            {
                "search" => AgentEvidenceKind.Pointer,
                "changes_in_period" => AgentEvidenceKind.Ranking,
                "in_force_on" => AgentEvidenceKind.Timeline,
                _ => throw new ArgumentOutOfRangeException(nameof(tool)),
            };
            Assert.Contains(ledger.Evidence, item => item.Kind == expectedKind);
            Assert.Contains(ledger.Evidence, item => item is
            {
                Kind: AgentEvidenceKind.Coverage,
                RequiresCoverageDisclosure: true,
                Excerpt: not null,
            } && item.Excerpt.Contains("publisher_limitations", StringComparison.Ordinal));
            if (tool == "changes_in_period")
                Assert.DoesNotContain(ledger.Evidence, item => item.Kind == AgentEvidenceKind.Ranking
                    && item.Title?.Contains("lu-legilux", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void Partial_summaries_exclude_rows_carried_by_refused_publishers()
    {
        static JsonObject Refusal(string tool)
        {
            var refusal = new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "lu-legilux",
                    ["jurisdiction"] = "LU",
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                },
                ["unsupported_filters"] = new JsonArray("domain"),
            };
            refusal[tool switch
            {
                "search" => "hits",
                "changes_in_period" => "changes",
                "in_force_on" => "works",
                _ => throw new ArgumentOutOfRangeException(nameof(tool)),
            }] = new JsonArray(tool switch
            {
                "search" => new JsonObject
                {
                    ["lex_id"] = "lu-legilux:refused:2024-01-01",
                    ["anchor"] = "art_1",
                    ["snippet"] = "stale row from a refused publisher",
                },
                "changes_in_period" => new JsonObject
                {
                    ["work"] = "lu-legilux:refused",
                    ["versions_in_period"] = 1,
                    ["versions_total"] = 1,
                    ["first_change"] = "2024-01-01",
                    ["last_change"] = "2024-01-01",
                },
                "in_force_on" => new JsonObject
                {
                    ["lex_id"] = "lu-legilux:refused:2024-01-01",
                    ["valid_from"] = "2024-01-01",
                },
                _ => throw new ArgumentOutOfRangeException(nameof(tool)),
            });
            return refusal;
        }

        static JsonObject Supported(string tool) => tool switch
        {
            "search" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:supported:2024-01-01",
                    ["anchor"] = "art_1",
                    ["snippet"] = "supported row",
                }),
            },
            "changes_in_period" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["work"] = "eu-eurlex:supported",
                    ["versions_in_period"] = 1,
                    ["versions_total"] = 1,
                    ["first_change"] = "2024-01-01",
                    ["last_change"] = "2024-01-01",
                }),
            },
            "in_force_on" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["works"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:supported:2024-01-01",
                    ["valid_from"] = "2024-01-01",
                }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        static (string? Status, JsonArray Docs) Summarize(JsonNode payload)
        {
            var method = typeof(AskService).GetMethod("Summarize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("AskService.Summarize was not found.");
            var value = (ValueTuple<string?, JsonArray>)method.Invoke(null, [payload])!;
            return (value.Item1, value.Item2);
        }

        foreach (var tool in new[] { "search", "changes_in_period", "in_force_on" })
        {
            var payload = new JsonArray(Refusal(tool), Supported(tool));
            var summary = Summarize(payload);

            Assert.Equal(LegalOperationStatus.PartialResult, summary.Status);
            Assert.Contains(summary.Docs.OfType<JsonObject>(), item =>
                item["lex_id"]?.GetValue<string>()?.StartsWith(
                    "eu-eurlex:supported", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(summary.Docs.OfType<JsonObject>(), item =>
                item["lex_id"]?.GetValue<string>()?.StartsWith(
                    "lu-legilux:refused", StringComparison.Ordinal) == true);

            var ledger = new AgentEvidenceLedger();
            ledger.Observe(tool, summary.Status, summary.Docs, payload, new JsonObject());
            Assert.DoesNotContain(ledger.Evidence, item =>
                string.Equals(item.Work, "lu-legilux:refused", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Refusal_with_untyped_companion_remains_full_gap_and_untyped_rows_never_join_partial()
    {
        static JsonObject Refusal() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["jurisdiction"] = "LU",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("domain"),
            ["hits"] = new JsonArray(),
        };

        var refusalWithUntypedEmpty = new JsonArray(Refusal(), new JsonObject
        {
            ["hits"] = new JsonArray(),
        });
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            LegalOperationPolicy.StatusForResult(refusalWithUntypedEmpty));

        var refused = UiMapper.From("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        }, refusalWithUntypedEmpty);

        Assert.Equal(McpStatus.FilterNotSupportedByIndex, refused.Gap?.Status);
        Assert.Null(refused.Workspace);
        Assert.Null(refused.PublisherLimitations);

        var genuinePartial = new JsonArray(
            Refusal(),
            new JsonObject
            {
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "untyped:must-not-enter:2024-01-01",
                    ["anchor"] = "art_1",
                }),
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:supported:2024-01-01",
                    ["anchor"] = "art_1",
                }),
            });
        Assert.Equal(LegalOperationStatus.PartialResult,
            LegalOperationPolicy.StatusForResult(genuinePartial));

        var partial = UiMapper.From("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        }, genuinePartial);

        Assert.Equal("eu-eurlex:supported:2024-01-01",
            Assert.Single(partial.Workspace?.Results ?? []).LexId);
        Assert.Single(partial.PublisherLimitations ?? []);
    }

    [Fact]
    public void Lone_partial_result_publisher_cannot_reach_search_ui()
    {
        static JsonObject PublisherResult() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "unproved-publisher",
                ["status"] = LegalOperationStatus.PartialResult,
            },
            ["hits"] = new JsonArray(new JsonObject
            {
                ["lex_id"] = "unproved:partial:2024-01-01",
                ["anchor"] = "art_1",
                ["snippet"] = "must never become a UI fact",
            }),
        };

        foreach (var aggregate in new[] { false, true })
        {
            var publisherResult = PublisherResult();
            Assert.False(LegalOperationPolicy.IsProvenSuccessfulPublisherResult(
                publisherResult, "search"));
            JsonNode payload = aggregate
                ? new JsonArray(publisherResult)
                : publisherResult;
            var arguments = new JsonObject { ["query"] = "synthetic" };

            var effect = UiMapper.From("search", arguments, payload);

            Assert.Empty(effect.Workspace?.Results ?? []);
            var operation = RequestedOperation.CreatePlanned(
                $"req:lone-partial:{aggregate}", 0, "search", arguments);
            var execution = new OperationExecution(operation)
                .Complete(LegalOperationStatus.PartialResult, payload);
            var reply = OperationAnswerPolicy.Render("en", [execution], [effect]);
            Assert.DoesNotContain("unproved:partial", reply, StringComparison.Ordinal);
            Assert.DoesNotContain("must never become a UI fact", reply, StringComparison.Ordinal);
            Assert.DoesNotContain("Lex found 1", reply, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Publisher_shaped_untyped_search_results_cannot_reach_ui_or_summaries()
    {
        static (string? Status, JsonArray Docs) Summarize(JsonNode payload)
        {
            var method = typeof(AskService).GetMethod("Summarize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("AskService.Summarize was not found.");
            var value = (ValueTuple<string?, JsonArray>)method.Invoke(null, [payload])!;
            return (value.Item1, value.Item2);
        }

        var statusShapes = new (string Name, Func<JsonNode?> Create)[]
        {
            ("missing", () => null),
            ("numeric", () => JsonValue.Create(42)),
            ("boolean", () => JsonValue.Create(true)),
            ("array", () => new JsonArray(McpStatus.Ok)),
            ("object", () => new JsonObject { ["value"] = McpStatus.Ok }),
        };

        foreach (var (name, createStatus) in statusShapes)
        foreach (var aggregate in new[] { false, true })
        {
            var envelope = new JsonObject { ["publisher"] = "unproved-publisher" };
            if (createStatus() is { } status)
                envelope["status"] = status;
            var publisherResult = new JsonObject
            {
                ["envelope"] = envelope,
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = $"unproved:{name}:2024-01-01",
                    ["anchor"] = "art_1",
                    ["snippet"] = $"{name} status must never become synthesis evidence",
                }),
            };
            Assert.False(LegalOperationPolicy.IsProvenSuccessfulPublisherResult(
                publisherResult, "search"));
            JsonNode payload = aggregate
                ? new JsonArray(publisherResult)
                : publisherResult;

            var effect = UiMapper.From("search",
                new JsonObject { ["query"] = "synthetic" }, payload);
            var summary = Summarize(payload);

            Assert.Empty(effect.Workspace?.Results ?? []);
            Assert.DoesNotContain($"unproved:{name}", JsonSerializer.Serialize(effect),
                StringComparison.Ordinal);
            Assert.Empty(summary.Docs);
        }
    }

    [Fact]
    public void Full_refusal_ignores_statusless_and_numeric_status_search_hits()
    {
        static JsonObject Refusal() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["jurisdiction"] = "LU",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("domain"),
            ["hits"] = new JsonArray(),
        };

        static (string? Status, JsonArray Docs) Summarize(JsonNode payload)
        {
            var method = typeof(AskService).GetMethod("Summarize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("AskService.Summarize was not found.");
            var value = (ValueTuple<string?, JsonArray>)method.Invoke(null, [payload])!;
            return (value.Item1, value.Item2);
        }

        foreach (var companionStatus in new JsonNode?[]
                 {
                     null, JsonValue.Create(42), JsonValue.Create(LegalOperationStatus.PartialResult),
                 })
        foreach (var reverse in new[] { false, true })
        {
            var companion = new JsonObject
            {
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "untyped:must-not-answer:2024-01-01",
                    ["anchor"] = "art_1",
                }),
            };
            if (companionStatus is not null)
                companion["envelope"] = new JsonObject
                {
                    ["status"] = companionStatus.DeepClone(),
                };
            var payload = reverse
                ? new JsonArray(companion, Refusal())
                : new JsonArray(Refusal(), companion);
            var arguments = new JsonObject
            {
                ["query"] = "synthetic",
                ["domain"] = "finance",
            };

            Assert.Equal(McpStatus.FilterNotSupportedByIndex,
                LegalOperationPolicy.StatusForResult(payload));
            var effect = UiMapper.From("search", arguments, payload);
            Assert.Equal(McpStatus.FilterNotSupportedByIndex, effect.Gap?.Status);
            Assert.Null(effect.Workspace);
            Assert.Null(effect.PublisherLimitations);

            var summary = Summarize(payload);
            Assert.Equal(McpStatus.FilterNotSupportedByIndex, summary.Status);
            Assert.Empty(summary.Docs);

            var operation = RequestedOperation.CreatePlanned(
                $"req:full-refusal:{reverse}:{companionStatus is not null}", 0,
                "search", arguments);
            var execution = new OperationExecution(operation)
                .Complete(McpStatus.FilterNotSupportedByIndex, payload);
            var reply = OperationAnswerPolicy.Render("en", [execution], [effect]);
            Assert.DoesNotContain("Lex found 1", reply, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Full_refusal_ledger_stores_only_typed_facts_not_rejected_companion_rows()
    {
        static (string? Status, JsonArray Docs) Summarize(JsonNode payload)
        {
            var method = typeof(AskService).GetMethod("Summarize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("AskService.Summarize was not found.");
            var value = (ValueTuple<string?, JsonArray>)method.Invoke(null, [payload])!;
            return (value.Item1, value.Item2);
        }

        static string RowField(string tool) => tool switch
        {
            "search" => "hits",
            "changes_in_period" => "changes",
            "in_force_on" => "works",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        static JsonObject Row(string tool, string marker) => tool switch
        {
            "search" => new JsonObject
            {
                ["lex_id"] = $"unproved:{marker}:2024-01-01",
                ["anchor"] = "art_1",
                ["snippet"] = marker,
            },
            "changes_in_period" => new JsonObject
            {
                ["work"] = $"unproved:{marker}",
                ["versions_in_period"] = 1,
                ["versions_total"] = 1,
                ["title"] = marker,
            },
            "in_force_on" => new JsonObject
            {
                ["lex_id"] = $"unproved:{marker}:2024-01-01",
                ["valid_from"] = "2024-01-01",
                ["title"] = marker,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        static JsonObject Refusal(string tool)
        {
            var result = new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "refusing-publisher",
                    ["jurisdiction"] = "LU",
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                },
                ["unsupported_filters"] = new JsonArray("domain"),
            };
            result[RowField(tool)] = new JsonArray(Row(tool, "raw-refusal-row"));
            return result;
        }

        static JsonObject Companion(string tool, JsonNode? status)
        {
            var envelope = new JsonObject { ["publisher"] = "unproved-publisher" };
            if (status is not null)
                envelope["status"] = status;
            var result = new JsonObject { ["envelope"] = envelope };
            result[RowField(tool)] = new JsonArray(Row(tool, "raw-rejected-companion"));
            return result;
        }

        var companionStatuses = new (string Name, Func<JsonNode?> Create)[]
        {
            ("missing", () => null),
            ("numeric", () => JsonValue.Create(42)),
            ("partial", () => JsonValue.Create(LegalOperationStatus.PartialResult)),
        };
        var leaks = new List<string>();

        foreach (var tool in new[] { "search", "changes_in_period", "in_force_on" })
        foreach (var (name, createStatus) in companionStatuses)
        foreach (var reverse in new[] { false, true })
        {
            var companion = Companion(tool, createStatus());
            var payload = reverse
                ? new JsonArray(companion, Refusal(tool))
                : new JsonArray(Refusal(tool), companion);
            var arguments = tool switch
            {
                "search" => new JsonObject { ["query"] = "synthetic", ["domain"] = "finance" },
                "changes_in_period" => new JsonObject
                {
                    ["from_date"] = "2024-01-01",
                    ["to_date"] = "2024-12-31",
                    ["domain"] = "finance",
                },
                _ => new JsonObject { ["date"] = "2024-01-01", ["domain"] = "finance" },
            };

            Assert.Equal(McpStatus.FilterNotSupportedByIndex,
                LegalOperationPolicy.StatusForResult(payload));
            var effect = UiMapper.From(tool, arguments, payload);
            Assert.Equal(McpStatus.FilterNotSupportedByIndex, effect.Gap?.Status);
            Assert.Null(effect.Workspace);
            Assert.Null(effect.Ranking);
            Assert.Null(effect.InForce);

            var summary = Summarize(payload);
            Assert.Equal(McpStatus.FilterNotSupportedByIndex, summary.Status);
            Assert.Empty(summary.Docs);
            var ledger = new AgentEvidenceLedger();
            ledger.Observe(tool, summary.Status, summary.Docs, payload, arguments);
            var evidence = Assert.Single(ledger.Evidence);
            Assert.Equal(AgentEvidenceKind.Coverage, evidence.Kind);
            Assert.True(evidence.RequiresCoverageDisclosure);
            var excerpt = Assert.IsType<string>(evidence.Excerpt);
            var label = $"{tool}:{name}:{reverse}:synthesis";
            if (excerpt.Contains("raw-rejected-companion", StringComparison.Ordinal)
                || excerpt.Contains("raw-refusal-row", StringComparison.Ordinal))
            {
                leaks.Add(label);
                continue;
            }

            var facts = JsonNode.Parse(excerpt) as JsonObject;
            if (facts is null
                || facts["status"]?.GetValue<string>() != McpStatus.FilterNotSupportedByIndex
                || facts["publisher_limitations"] is not JsonArray limitations
                || limitations.Count != 1)
            {
                leaks.Add(label);
                continue;
            }
            var limitation = Assert.IsType<JsonObject>(limitations[0]);
            Assert.Equal(McpStatus.FilterNotSupportedByIndex,
                limitation["status"]?.GetValue<string>());
            Assert.Equal(tool, limitation["tool"]?.GetValue<string>());
            Assert.Equal("refusing-publisher", limitation["publisher"]?.GetValue<string>());
            Assert.Equal("LU", limitation["jurisdiction"]?.GetValue<string>());
            Assert.Equal("domain", Assert.Single(
                Assert.IsType<JsonArray>(limitation["unsupported_filters"]))?.GetValue<string>());
        }

        Assert.Empty(leaks);
    }

    [Fact]
    public void Contradictory_successful_empty_publishers_contribute_no_ui_or_synthesis_rows()
    {
        static JsonObject Refusal() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["jurisdiction"] = "LU",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("domain"),
        };

        static (string? Status, JsonArray Docs) Summarize(JsonNode payload)
        {
            var method = typeof(AskService).GetMethod("Summarize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("AskService.Summarize was not found.");
            var value = (ValueTuple<string?, JsonArray>)method.Invoke(null, [payload])!;
            return (value.Item1, value.Item2);
        }

        static JsonObject Supported(string tool) => tool switch
        {
            "search" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:supported-search:2024-01-01",
                    ["anchor"] = "art_1",
                }),
            },
            "changes_in_period" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["works_changed"] = 1,
                ["new_versions"] = 1,
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["work"] = "eu-eurlex:supported-period",
                    ["versions_in_period"] = 1,
                    ["versions_total"] = 1,
                }),
            },
            "in_force_on" => new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["total_works_in_force"] = 1,
                ["works"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:supported-force:2024-01-01",
                    ["valid_from"] = "2024-01-01",
                }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        var attacks = new (string Tool, JsonObject Result)[]
        {
            ("search", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
                ["lex_id"] = "eu-eurlex:stale-without-row-shape:2024-01-01",
                ["anchor"] = "art_1",
            }),
            ("search", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:stale-search:2024-01-01",
                    ["anchor"] = "art_1",
                }),
            }),
            ("changes_in_period", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoChangesInPeriod },
                ["works_changed"] = 0,
                ["new_versions"] = 0,
                ["changes"] = new JsonArray(new JsonObject
                {
                    ["work"] = "eu-eurlex:stale-period",
                    ["versions_in_period"] = 1,
                    ["versions_total"] = 1,
                }),
            }),
            ("changes_in_period", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoChangesInPeriod },
                ["works_changed"] = 1,
                ["new_versions"] = 1,
                ["changes"] = new JsonArray(),
            }),
            ("in_force_on", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
                ["total_works_in_force"] = 0,
                ["works"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:stale-force:2024-01-01",
                    ["valid_from"] = "2024-01-01",
                }),
            }),
            ("in_force_on", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
                ["total_works_in_force"] = 1,
                ["works"] = new JsonArray(),
            }),
        };

        foreach (var (tool, contradictory) in attacks)
        foreach (var reverse in new[] { false, true })
        {
            var payload = reverse
                ? new JsonArray(contradictory.DeepClone(), Refusal())
                : new JsonArray(Refusal(), contradictory.DeepClone());
            var arguments = tool switch
            {
                "search" => new JsonObject { ["query"] = "synthetic" },
                "changes_in_period" => new JsonObject
                {
                    ["from_date"] = "2024-01-01",
                    ["to_date"] = "2024-12-31",
                },
                _ => new JsonObject { ["date"] = "2024-01-01" },
            };

            Assert.Equal(LegalOperationStatus.PartialResult,
                LegalOperationPolicy.StatusForResult(payload));
            var effect = UiMapper.From(tool, arguments, payload);
            Assert.Equal(McpStatus.FilterNotSupportedByIndex, effect.Gap?.Status);
            Assert.Null(effect.Workspace);
            Assert.Null(effect.Ranking);
            Assert.Null(effect.InForce);
            Assert.Null(effect.PublisherLimitations);

            var summary = Summarize(payload);
            Assert.Empty(summary.Docs);
            var ledger = new AgentEvidenceLedger();
            ledger.Observe(tool, summary.Status, summary.Docs, payload, arguments);
            Assert.DoesNotContain(ledger.Evidence, item => item.Kind is
                AgentEvidenceKind.Pointer or AgentEvidenceKind.Ranking or
                AgentEvidenceKind.Timeline);

            Assert.Throws<InvalidDataException>(() =>
                UiMapper.From(tool, arguments, contradictory.DeepClone()));
            Assert.Empty(Summarize(contradictory).Docs);

            var supportedPayload = reverse
                ? new JsonArray(Supported(tool), contradictory.DeepClone())
                : new JsonArray(contradictory.DeepClone(), Supported(tool));
            var supportedEffect = UiMapper.From(tool, arguments, supportedPayload);
            Assert.Null(supportedEffect.Gap);
            if (tool == "search")
                Assert.Single(supportedEffect.Workspace?.Results ?? []);
            if (tool == "changes_in_period")
            {
                Assert.Single(supportedEffect.Ranking?.Rows ?? []);
                Assert.Equal(1, supportedEffect.Ranking?.WorksChanged);
                Assert.Equal(1, supportedEffect.Ranking?.NewVersions);
            }
            if (tool == "in_force_on")
            {
                Assert.Single(supportedEffect.InForce?.Rows ?? []);
                Assert.Equal(1, supportedEffect.InForce?.Total);
            }
            Assert.Single(Summarize(supportedPayload).Docs);
        }
    }

    [Fact]
    public void Valid_successful_empty_publishers_remain_partial_in_either_order()
    {
        static JsonObject Refusal() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["jurisdiction"] = "LU",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("domain"),
        };

        var valid = new (string Tool, JsonObject Result)[]
        {
            ("search", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
                ["hits"] = new JsonArray(),
            }),
            ("changes_in_period", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoChangesInPeriod },
                ["works_changed"] = 0,
                ["new_versions"] = 0,
                ["changes"] = new JsonArray(),
            }),
            ("in_force_on", new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.NoResult },
                ["total_works_in_force"] = 0,
                ["works"] = new JsonArray(),
            }),
        };

        foreach (var (tool, successfulEmpty) in valid)
        foreach (var reverse in new[] { false, true })
        {
            var payload = reverse
                ? new JsonArray(successfulEmpty.DeepClone(), Refusal())
                : new JsonArray(Refusal(), successfulEmpty.DeepClone());
            var effect = UiMapper.From(tool, new JsonObject(), payload);

            Assert.Null(effect.Gap);
            Assert.Single(effect.PublisherLimitations ?? []);
            if (tool == "search") Assert.Empty(effect.Workspace?.Results ?? []);
            if (tool == "changes_in_period") Assert.Empty(effect.Ranking?.Rows ?? []);
            if (tool == "in_force_on") Assert.Empty(effect.InForce?.Rows ?? []);
        }
    }

    [Fact]
    public void Partial_refusal_with_only_unknown_filter_names_keeps_generic_limitation()
    {
        var arguments = new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        };
        var payload = new JsonArray(
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "lu-legilux",
                    ["jurisdiction"] = "LU",
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                },
                ["unsupported_filters"] = new JsonArray("unknown_filter"),
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:supported:2024-01-01",
                    ["anchor"] = "art_1",
                }),
            });
        var operation = RequestedOperation.CreatePlanned("req:generic-limitation", 0,
            "search", arguments);
        var execution = new OperationExecution(operation)
            .Complete(LegalOperationStatus.PartialResult, payload);
        var effect = UiMapper.From(operation, arguments, payload);

        var limitation = Assert.Single(effect.PublisherLimitations ?? []);
        Assert.Equal("lu-legilux", limitation.Publisher);
        Assert.Empty(limitation.UnsupportedFilters);
        Assert.Single(effect.Workspace?.Results ?? []);

        var english = OperationAnswerPolicy.Render("en", [execution], [effect]);
        Assert.Contains("Capability limitation", english, StringComparison.Ordinal);
        Assert.Contains("did not run this operation", english, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown_filter", english, StringComparison.Ordinal);
        var french = OperationAnswerPolicy.Render("fr", [execution], [effect]);
        Assert.Contains("Limite de capacité", french, StringComparison.Ordinal);
        Assert.Contains("n'a pas exécuté cette opération", french, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown_filter", french, StringComparison.Ordinal);
    }

    [Fact]
    public void Deterministic_partial_reply_discloses_limited_publisher_and_filter()
    {
        var arguments = new JsonObject { ["query"] = "synthetic", ["domain"] = "finance" };
        var payload = new JsonArray(
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "lu-legilux",
                    ["jurisdiction"] = "LU",
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                },
                ["unsupported_filters"] = new JsonArray("domain"),
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "eu-eurlex",
                    ["jurisdiction"] = "EU",
                    ["status"] = McpStatus.Ok,
                },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:32016r0679:2016-05-04",
                    ["anchor"] = "art_1",
                    ["title"] = "Synthetic held result",
                }),
            });
        var operation = RequestedOperation.CreatePlanned("req:partial", 0, "search", arguments);
        var result = new OperationExecution(operation)
            .Complete(LegalOperationStatus.PartialResult, payload);
        var effect = UiMapper.From(operation, arguments, payload);

        var rendered = OperationAnswerPolicy.Render("en", [result], [effect]);

        Assert.Contains("lu-legilux", rendered, StringComparison.Ordinal);
        Assert.Contains("domain", rendered, StringComparison.Ordinal);
        Assert.Contains("Lex coverage", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not evidence", rendered, StringComparison.OrdinalIgnoreCase);

        var french = OperationAnswerPolicy.Render("fr", [result], [effect]);
        Assert.Contains("Limite de capacité", french, StringComparison.Ordinal);
        Assert.Contains("lu-legilux", french, StringComparison.Ordinal);
        Assert.Contains("domain", french, StringComparison.Ordinal);
        Assert.Contains("couverture de Lex", french, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Publisher_limitation_merge_deduplicates_equal_filter_values()
    {
        var first = new PublisherLimitationView(
            McpStatus.FilterNotSupportedByIndex, "search", "lu-legilux", "LU",
            new[] { "domain" });
        var second = new PublisherLimitationView(
            McpStatus.FilterNotSupportedByIndex, "search", "lu-legilux", "LU",
            new[] { "domain" });

        var merged = UiEffect.Merge([
            new UiEffect(PublisherLimitations: [first]),
            new UiEffect(PublisherLimitations: [second]),
        ]);

        Assert.Single(merged.PublisherLimitations ?? []);

        var bounded = UiEffect.Merge(Enumerable.Range(0, 12).Select(index =>
            new UiEffect(PublisherLimitations:
            [
                new PublisherLimitationView(
                    McpStatus.FilterNotSupportedByIndex, "search", $"publisher-{index}", "EU",
                    new[] { "domain" }),
            ])));
        Assert.Equal(8, bounded.PublisherLimitations?.Count);
    }

    [Fact]
    public void Partial_limitation_is_force_appended_after_optional_synthesis_once()
    {
        var limitation = new PublisherLimitationView(
            McpStatus.FilterNotSupportedByIndex, "search", "lu-legilux", "LU",
            new[] { "domain" });
        var method = typeof(AskService).GetMethod("WithPublisherLimitations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var once = (string)method!.Invoke(null,
            ["Synthesized supported answer.", "en", new[] { limitation }])!;
        var twice = (string)method.Invoke(null,
            [once, "en", new[] { limitation }])!;

        Assert.Contains("lu-legilux", once, StringComparison.Ordinal);
        Assert.Contains("domain", once, StringComparison.Ordinal);
        Assert.Equal(once, twice);

        var bodyMethod = typeof(AskService).GetMethod("Body",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(bodyMethod);
        var body = (JsonObject)bodyMethod!.Invoke(null,
            ["Synthesized supported answer.", "en", new JsonArray(),
                new List<UiEffect> { new(PublisherLimitations: [limitation]) }, null, null])!;
        Assert.Equal(once, body["reply"]?.GetValue<string>());
    }

    [Fact]
    public void Mixed_publisher_limitation_ignores_malformed_untrusted_fields()
    {
        var payload = new JsonArray(
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "bad publisher!",
                    ["jurisdiction"] = new string('x', 65),
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                },
                ["unsupported_filters"] = new JsonArray(
                    42, "domain", "unknown_filter", new string('x', 65), "domain"),
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["publisher"] = "eu-eurlex",
                    ["jurisdiction"] = "EU",
                    ["status"] = McpStatus.Ok,
                },
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["lex_id"] = "eu-eurlex:32016r0679:2016-05-04",
                    ["anchor"] = "art_1",
                }),
            });

        var effect = UiMapper.From("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        }, payload);

        var limitation = Assert.Single(effect.PublisherLimitations ?? []);
        Assert.Null(limitation.Publisher);
        Assert.Null(limitation.Jurisdiction);
        Assert.Equal(["domain"], limitation.UnsupportedFilters);
        Assert.Single(effect.Workspace?.Results ?? []);
    }

    [Fact]
    public void Mixed_publisher_filter_refusal_co_delivers_supported_period_and_in_force_rows()
    {
        static JsonObject Refusal() => new()
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "lu-legilux",
                ["jurisdiction"] = "LU",
                ["status"] = McpStatus.FilterNotSupportedByIndex,
            },
            ["unsupported_filters"] = new JsonArray("hierarchy"),
        };

        var period = UiMapper.From("changes_in_period", new JsonObject
        {
            ["from_date"] = "2024-01-01",
            ["to_date"] = "2024-12-31",
        }, new JsonArray(Refusal(), new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "eu-eurlex",
                ["jurisdiction"] = "EU",
                ["status"] = McpStatus.Ok,
            },
            ["window"] = new JsonObject
            {
                ["from"] = "2024-01-01", ["to"] = "2024-12-31",
            },
            ["order"] = "by_date",
            ["works_changed"] = 1,
            ["new_versions"] = 1,
            ["changes"] = new JsonArray(new JsonObject
            {
                ["work"] = "32016r0679",
                ["versions_in_period"] = 1,
                ["versions_total"] = 2,
                ["first_change"] = "2024-01-01",
                ["last_change"] = "2024-01-01",
            }),
        }));

        Assert.Null(period.Gap);
        Assert.Single(period.Ranking?.Rows ?? []);
        Assert.Single(period.PublisherLimitations ?? []);

        var inForce = UiMapper.From("in_force_on", new JsonObject
        {
            ["date"] = "2024-01-01",
        }, new JsonArray(Refusal(), new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["publisher"] = "eu-eurlex",
                ["jurisdiction"] = "EU",
                ["status"] = McpStatus.Ok,
            },
            ["total_works_in_force"] = 1,
            ["works"] = new JsonArray(new JsonObject
            {
                ["work"] = "32016r0679",
                ["valid_from"] = "2016-05-04",
            }),
        }));

        Assert.Null(inForce.Gap);
        Assert.Single(inForce.InForce?.Rows ?? []);
        Assert.Single(inForce.PublisherLimitations ?? []);
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
    public void Navigate_is_not_an_executable_operation()
    {
        Assert.DoesNotContain("navigate", OperationArguments.Actions);
        Assert.Throws<InvalidDataException>(() => RequestedOperation.CreatePlanned(
            "nav", 0, "navigate", new JsonObject
            {
                ["work"] = "eu-eurlex:32013r0575",
                ["date"] = "2024-01-01",
            }));
    }

    [Fact]
    public void Ambiguous_publisher_versions_offer_bounded_exact_choices_in_english_and_french()
    {
        var asOfPayload = new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.AmbiguousVersion },
            ["work"] = "eu-eurlex:32013r0575",
            ["version_choices"] = new JsonArray(Enumerable.Range(1, 21)
                .Select(index => (JsonNode)new JsonObject
                {
                    ["version_key"] = $"2024-01-01~{index}",
                }).ToArray()),
            ["choices_truncated"] = true,
        };
        var asOf = UiMapper.From("as_of", new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["date"] = "2024-01-01",
        }, asOfPayload, "en");

        Assert.Equal(McpStatus.AmbiguousVersion, asOf.Gap?.Status);
        Assert.Contains("exact publisher version", asOf.Gap?.Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20, asOf.Gap?.Available.Count);
        Assert.Equal("version_key=2024-01-01~1", asOf.Gap?.Available[0]);

        var diffPayload = new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = McpStatus.AmbiguousVersion },
            ["work"] = "eu-eurlex:32013r0575",
            ["from_version_choices"] = new JsonArray(new JsonObject
            {
                ["version_key"] = "2020-01-01~1",
            }),
            ["to_version_choices"] = new JsonArray(new JsonObject
            {
                ["version_key"] = "2024-01-01~2",
            }),
        };
        var diff = UiMapper.From("diff", new JsonObject
        {
            ["work"] = "eu-eurlex:32013r0575",
            ["from_date"] = "2020-01-01",
            ["to_date"] = "2024-01-01",
        }, diffPayload, "fr");

        Assert.Contains("limite de comparaison", diff.Gap?.Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["from_version_key=2020-01-01~1", "to_version_key=2024-01-01~2"],
            diff.Gap?.Available);

        var inForce = UiMapper.From("in_force_on", new JsonObject
        {
            ["date"] = "2024-01-01",
        }, new JsonArray(
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["works"] = new JsonArray(new JsonObject
                {
                    ["work"] = "lu-legilux:held-work",
                }),
            },
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["status"] = McpStatus.AmbiguousVersion,
                    ["jurisdiction"] = "EU",
                },
                ["ambiguous_works"] = new JsonArray(new JsonObject
                {
                    ["work"] = "eu-eurlex:32013r0575",
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["version_key"] = "2024-01-01~3",
                    }),
                }),
            }));

        Assert.Equal(McpStatus.AmbiguousVersion, inForce.Gap?.Status);
        Assert.Null(inForce.InForce);
        Assert.Equal(["eu-eurlex:32013r0575: version_key=2024-01-01~3"],
            inForce.Gap?.Available);
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
        Assert.Throws<InvalidDataException>(() =>
            new OperationExecution(exact).CompleteLegal(LegalOutcome.Succeeded));
        Assert.Throws<InvalidDataException>(() =>
            new OperationExecution(comparison).CompleteLegal(LegalOutcome.NotComparable));
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
