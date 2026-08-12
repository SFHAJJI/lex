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
            var expected =
                $"Argument '{name}' must contain {minimum} to {maximum} characters.";

            OperationArguments.Normalize(action, With(action, name, LengthProbe(name, minimum)));
            OperationArguments.Normalize(action, With(action, name, LengthProbe(name, maximum)));

            foreach (var outside in new[] { minimum - 1, maximum + 1 })
                Assert.Equal(expected, Assert.Throws<InvalidDataException>(() =>
                    OperationArguments.Normalize(
                        action, With(action, name, LengthProbe(name, outside)))).Message);
        }
    }

    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_accepts_exactly_the_integer_range_the_schema_promises(string action)
    {
        foreach (var name in OperationArguments.AllowedFor(action)
                     .Where(OperationArguments.IsInteger).Order(StringComparer.Ordinal))
        {
            var (minimum, maximum) = OperationArguments.IntegerBoundsFor(action, name);

            OperationArguments.Normalize(action, With(action, name, minimum));
            OperationArguments.Normalize(action, With(action, name, maximum));

            foreach (var outside in new[] { minimum - 1, maximum + 1 })
                Assert.Equal(
                    $"Argument '{name}' must be between {minimum} and {maximum}.",
                    Assert.Throws<InvalidDataException>(() =>
                        OperationArguments.Normalize(action, With(action, name, outside))).Message);
        }
    }

    // "required" and the anyOf choices are new to the schema; they are only worth emitting if
    // they are exactly what the gate demands. Completeness is proved by normalizing an object
    // holding nothing else, soundness by removing each declared demand in turn.
    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_demands_exactly_the_arguments_the_schema_declares_required(string action)
    {
        var allowed = OperationArguments.AllowedFor(action);
        var required = OperationArguments.RequiredFor(action);
        var choices = OperationArguments.RequiredChoicesFor(action);
        Assert.All(required, name => Assert.Contains(name, allowed));
        Assert.All(choices, choice =>
        {
            Assert.NotEmpty(choice.Names);
            Assert.All(choice.Names, name => Assert.Contains(name, allowed));
        });

        OperationArguments.Normalize(action, MinimalArguments(action));

        foreach (var name in required)
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

    [Theory]
    [MemberData(nameof(EveryOperation))]
    public void The_gate_refuses_every_date_the_schema_pattern_refuses(string action)
    {
        foreach (var name in OperationArguments.RequiredFor(action)
                     .Where(OperationArguments.IsDate))
            foreach (var malformed in new[] { "2024", "2024-1-1", "01-01-2024", "2024/01/01" })
            {
                Assert.DoesNotMatch(OperationArguments.IsoDatePattern, malformed);
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

    // The gate itself must keep failing closed. The schema only stops the model proposing an
    // off-set value; it is guidance, not a decoding constraint, and must never become a coercion.
    [Theory]
    [InlineData("search", "time_scope", "current")]
    [InlineData("search", "retrieval_mode", "semantic")]
    [InlineData("search", "fuzzy", "true")]
    [InlineData("as_of", "mode", "text")]
    [InlineData("changes_in_period", "order", "churn")]
    public void An_argument_value_outside_the_closed_set_still_aborts_the_plan(
        string tool, string argument, string value)
    {
        var arguments = tool switch
        {
            "search" => new JsonObject { ["query"] = "renewable energy" },
            "as_of" => new JsonObject { ["work_query"] = "GDPR", ["date"] = "2021-01-01" },
            _ => new JsonObject { ["from_date"] = "2024-01-01", ["to_date"] = "2024-12-31" },
        };
        arguments[argument] = value;

        var rejected = Assert.Throws<InvalidDataException>(() =>
            OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(
                new JsonObject { ["tool"] = tool, ["arguments"] = arguments })));

        Assert.Contains("has an unsupported value", rejected.Message, StringComparison.Ordinal);
        Assert.Contains($"'{argument}'", rejected.Message, StringComparison.Ordinal);
    }

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
            ["operations"]!["items"]!["oneOf"]!.AsArray()
            .Select(node => new KeyValuePair<string, JsonObject>(
                node!["properties"]!["tool"]!["enum"]![0]!.GetValue<string>(), node.AsObject()));

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
        { McpStatus.UnknownPublisher, LegalOutcome.NotFound },
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
