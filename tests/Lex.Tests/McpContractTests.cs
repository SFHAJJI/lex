using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Lex.Tests;

// McpCore is the product: nine tools consumed by other people's agents, and it had no tests.
// The refusal taxonomy in particular is sold as an API contract — "a flagged wrong answer is
// still a wrong answer, so Lex refuses instead" — and every downstream behaviour keys off
// those exact status strings. A silent rename would break every client and no build would fail.
//
// These tests pin the contract, not the implementation: tool names, refusal statuses, and the
// promise that a hit never carries body text.
public class McpContractTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"lex-mcp-{Guid.NewGuid():N}.db");
    private readonly McpCore _core;
    private readonly LexIndexReader _reader;

    public McpContractTests()
    {
        const string buildIssues =
            "[{\"code\":\"publisher_metadata_unavailable\",\"work\":\"w3\",\"detail\":\"test gap\"}]";
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
            ["jurisdiction"] = "XX",
            ["scope_expected_works"] = "3",
            ["build_issues_json"] = buildIssues,
            ["build_issues_digest"] = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(buildIssues))),
        };
        DocRow Row(string key, string group, string from, string? to, bool text) =>
            new(key, "t-pub", group, $"urn:{group}", "REG", "en", from, to, "publisher",
                "2026-08-01T00:00:00Z", false, text, text, "abc", null, "https://example.org",
                group, group, null, from, null);
        ProvisionRow Prov(DocRow d, int seq, string anchor, string text) =>
            new($"{d.Key}|{d.Language}|{d.ValidFrom}", seq, anchor, $"{d.Key}#{anchor}", "article",
                anchor, null, null, null, d.Title, text,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text))));

        var docs = new[]
        {
            Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", "2021-12-31", true) with
                { Hierarchy = "secondary_law", Domains = "|finance|", ActForm = "REG", BindingStatus = "in_force" },
            Row("t-pub:w1:2022-01-01", "w1", "2022-01-01", null, true) with
                { Hierarchy = "secondary_law", Domains = "|finance|", ActForm = "REG", BindingStatus = "in_force" },
            Row("t-pub:w2:2019-06-01", "w2", "2019-06-01", null, false),   // held, but no text
        };
        var provisions = new[]
        {
            Prov(docs[0], 0, "art_1", "the thing shall apply everywhere"),
            Prov(docs[1], 0, "art_1", "the thing shall apply everywhere, revised"),
        };
        IndexBuilder.Build(_db, stamp, docs, provisions, [], [], StampSigner.CreateKeyPem());
        _reader = LexIndexReader.Open(_db);
        _core = new McpCore(new Dictionary<string, LexIndexReader> { ["t-pub"] = _reader });
    }

    [Fact]
    public void Public_tool_arguments_are_typed_closed_and_bounded_before_execution()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _core.CallTool("search",
            new JsonObject { ["query"] = "law", ["limit"] = int.MaxValue }));
        Assert.Throws<ArgumentException>(() => _core.CallTool("search",
            new JsonObject { ["query"] = new string('q', 1001) }));
        Assert.Throws<ArgumentException>(() => _core.CallTool("search",
            new JsonObject { ["query"] = "law", ["limit"] = "50" }));
        Assert.Throws<ArgumentException>(() => _core.CallTool("coverage",
            new JsonObject { ["unexpected"] = "value" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _core.CallTool("timeline",
            new JsonObject { ["work"] = "t-pub:w1", ["offset"] = -1 }));
        Assert.Throws<ArgumentException>(() => _core.CallTool("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2024-01-01", ["anchors"] = "art_1",
        }));

        foreach (var definition in _core.ToolDefs().OfType<JsonObject>())
            Assert.False(definition["inputSchema"]?["additionalProperties"]?.GetValue<bool>() ?? true);
    }

    [Fact]
    public void Full_text_response_has_one_total_budget_not_one_budget_per_provision()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-mcp-text-budget-{Guid.NewGuid():N}.db");
        try
        {
            var document = new DocRow(
                "budget:work:2024-01-01", "budget", "work", "urn:work", "REG", "en",
                "2024-01-01", null, "publisher", "2026-08-01T00:00:00Z", false,
                true, true, "record", "body", "https://example.test/work",
                "Budget work", "Budget work", null, "2024-01-01", null);
            var text = new string('x', 100_000);
            var provisions = Enumerable.Range(1, 3).Select(index => new ProvisionRow(
                $"{document.Key}|en|2024-01-01", index, $"art_{index}",
                $"{document.Key}#art_{index}", "article", $"Article {index}", null,
                null, null, document.Title, text,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text))))).ToArray();
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "budget", ["tier"] = "A",
                ["history_begins"] = "publisher", ["built_at"] = "2026-08-01T00:00:00Z",
                ["corpus_commit"] = "test",
            }, [document], provisions, [], [], StampSigner.CreateKeyPem());
            using var reader = LexIndexReader.Open(db);
            var core = new McpCore(new Dictionary<string, LexIndexReader> { ["budget"] = reader });

            var result = Assert.IsType<JsonObject>(core.CallTool("as_of", new JsonObject
            {
                ["work"] = "budget:work", ["date"] = "2024-06-01", ["mode"] = "full",
            }));
            var returned = Assert.IsType<JsonArray>(result["provisions"]);

            Assert.True(returned.Sum(item => item?["text"]?.GetValue<string>().Length ?? 0)
                <= 250_000);
            Assert.Contains(returned.OfType<JsonObject>(), item =>
                item["text_omitted"]?.GetValue<bool>() == true
                && item["text_bytes"]?.GetValue<int>() == 100_000);
            Assert.True(result["text_truncated"]!.GetValue<bool>());
            Assert.True(result["truncated"]!.GetValue<bool>());
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [Theory]
    [InlineData("title")]
    [InlineData("heading")]
    [InlineData("path")]
    [InlineData("source_uri")]
    [InlineData("citation")]
    public void Oversized_publisher_metadata_is_rejected_before_mount(string field)
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-mcp-metadata-{Guid.NewGuid():N}.db");
        try
        {
            var huge = new string('m', 1_000_000);
            var document = new DocRow(
                "metadata:work:2024-01-01", "metadata", "work", "urn:work", "REG", "en",
                "2024-01-01", null, "publisher", "2026-08-01T00:00:00Z", false,
                true, true, "record", "body",
                field == "source_uri" ? huge : "https://example.test/work",
                field == "title" ? huge : "Metadata work", "Metadata work", null,
                "2024-01-01", null);
            const string text = "bounded legal text";
            var provision = new ProvisionRow(
                $"{document.Key}|en|2024-01-01", 1, "art_1",
                $"{document.Key}#art_1", "article", "Article 1",
                field == "heading" ? huge : "Scope",
                field == "path" ? huge : "Part I", null, document.Title, text,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text))),
                field == "citation"
                    ? new JsonArray(new JsonObject
                    {
                        ["href"] = "https://example.test/eli/etat/leg/loi/2024/01/01/a1/jo?" + huge,
                        ["text"] = huge,
                    }).ToJsonString()
                    : null);
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "metadata", ["tier"] = "A",
                ["history_begins"] = "publisher", ["built_at"] = "2026-08-01T00:00:00Z",
                ["corpus_commit"] = "test",
            }, [document], [provision], [], [], StampSigner.CreateKeyPem());

            var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
            Assert.Contains("metadata", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Full_response_bounds_rows_and_citations_before_serialization()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-mcp-row-budget-{Guid.NewGuid():N}.db");
        try
        {
            var document = new DocRow(
                "bounded:work:2024-01-01", "bounded", "work", "urn:work", "REG", "en",
                "2024-01-01", null, "publisher", "2026-08-01T00:00:00Z", false,
                true, true, "record", "body", "https://example.test/work",
                "Bounded work", "Bounded work", null, "2024-01-01", null);
            const string citations =
                "[{\"href\":\"https://example.test/eli/etat/leg/loi/2024/01/01/a1/jo\",\"text\":\"citation\"}]";
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("x")));
            var provisions = Enumerable.Range(1, 2_001).Select(index => new ProvisionRow(
                $"{document.Key}|en|2024-01-01", index, $"art_{index}",
                $"{document.Key}#art_{index}", "article", index.ToString(), null,
                null, null, document.Title, "x", sha, citations)).ToArray();
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "bounded", ["tier"] = "A",
                ["history_begins"] = "publisher", ["built_at"] = "2026-08-01T00:00:00Z",
                ["corpus_commit"] = "test",
            }, [document], provisions, [], [], StampSigner.CreateKeyPem());
            using var reader = LexIndexReader.Open(db);
            var core = new McpCore(new Dictionary<string, LexIndexReader> { ["bounded"] = reader });

            var result = Assert.IsType<JsonObject>(core.CallTool("as_of", new JsonObject
            {
                ["work"] = "bounded:work", ["date"] = "2024-06-01", ["mode"] = "full",
            }));
            var returned = Assert.IsType<JsonArray>(result["provisions"]);
            var nestedCitations = returned.OfType<JsonObject>()
                .Sum(item => item["citations"]?.AsArray().Count ?? 0);

            Assert.Equal(2_000, returned.Count);
            Assert.Equal(2_001, result["total_provisions"]!.GetValue<int>());
            Assert.True(result["truncated"]!.GetValue<bool>());
            Assert.True(result["text_truncated"]!.GetValue<bool>());
            Assert.Equal(100, nestedCitations);
            Assert.Equal(100, result["citations_returned"]!.GetValue<int>());
            Assert.True(result["citations_truncated"]!.GetValue<bool>());
            Assert.Equal(3, reader.Provisions(LexIndexReader.RidOf(document), 3).Count);
            Assert.Single(reader.CitationsOf(
                LexIndexReader.RidOf(document), "art_1", 1));
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Mcp_bridge_distinguishes_bounded_tool_errors_from_sanitized_protocol_errors()
    {
        var known = new HashSet<string>(["search"], StringComparer.Ordinal);
        var invalid = McpSdkBridge.Invoke("search", new JsonObject(), known,
            (_, _) => throw new ArgumentOutOfRangeException("secret-field"));

        Assert.True(invalid.IsError);
        Assert.Equal(
            "Invalid tool arguments. Use the advertised schema and documented bounds.",
            Assert.IsType<TextContentBlock>(Assert.Single(invalid.Content)).Text);

        var unknown = Assert.Throws<McpProtocolException>(() => McpSdkBridge.Invoke(
            "attacker-tool", new JsonObject(), known, (_, _) => new JsonObject()));
        Assert.Equal(McpErrorCode.InvalidParams, unknown.ErrorCode);
        Assert.DoesNotContain("attacker-tool", unknown.ToString(), StringComparison.Ordinal);

        const string canary = "C:\\internal\\secret-token";
        var failure = Assert.Throws<McpProtocolException>(() => McpSdkBridge.Invoke(
            "search", new JsonObject(), known,
            (_, _) => throw new InvalidOperationException(canary)));
        Assert.Equal(McpErrorCode.InternalError, failure.ErrorCode);
        Assert.DoesNotContain(canary, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_bridge_propagates_transport_cancellation_to_the_legal_executor()
    {
        var known = new HashSet<string>(["search"], StringComparer.Ordinal);
        using var cancellation = new CancellationTokenSource();
        var observed = CancellationToken.None;
        static async ValueTask<JsonNode> Block(
            string _, JsonObject __, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new JsonObject();
        }

        async ValueTask<JsonNode> Observe(
            string tool, JsonObject arguments, CancellationToken cancellationToken)
        {
            observed = cancellationToken;
            return await Block(tool, arguments, cancellationToken);
        }

        var invocation = McpSdkBridge.InvokeAsync(
            "search", new JsonObject(), known, Observe, cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public async Task Invalid_mcp_input_fails_before_cancellation_or_reader_scheduling()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _core.CallToolAsync("search", new JsonObject
            {
                ["query"] = "privacy",
                ["limit"] = 51,
            }, cancellation.Token).AsTask());

        Assert.Equal("limit", error.ParamName);
    }

    [Fact]
    public async Task Isolated_sessions_are_bounded_to_the_selected_public_publisher_set()
    {
        var readers = Enumerable.Range(0, 100).ToDictionary(
            index => $"publisher-{index:D3}", _ => _reader, StringComparer.Ordinal);
        var opened = new List<string>();
        var core = new McpCore(readers, publisher => opened.Add(publisher));

        await core.CallToolAsync("coverage", new JsonObject
        {
            ["publisher"] = "publisher-042",
        }, CancellationToken.None);
        Assert.Equal(["publisher-042"], opened);

        opened.Clear();
        var coverage = await core.CallToolAsync(
            "coverage", new JsonObject(), CancellationToken.None);
        Assert.Equal(8, opened.Count);
        Assert.Equal(opened.Order(StringComparer.Ordinal), opened);
        Assert.All(coverage.AsArray().OfType<JsonObject>(), item =>
        {
            Assert.Equal(100, item["publisher_result_set"]?["total"]?.GetValue<int>());
            Assert.Equal(8, item["publisher_result_set"]?["returned"]?.GetValue<int>());
            Assert.True(item["publisher_result_set"]?["truncated"]?.GetValue<bool>());
        });

        opened.Clear();
        var missingAuthority = await Assert.ThrowsAsync<ArgumentException>(() =>
            core.CallToolAsync("timeline", new JsonObject
            {
                ["work"] = "w1",
            }, CancellationToken.None).AsTask());
        Assert.Equal("publisher", missingAuthority.ParamName);
        Assert.Empty(opened);

        var timeline = await core.CallToolAsync("timeline", new JsonObject
        {
            ["work"] = "w1",
            ["publisher"] = "publisher-042",
        }, CancellationToken.None);
        Assert.Equal(["publisher-042"], opened);
        Assert.Equal("ok", timeline["envelope"]?["status"]?.GetValue<string>());
    }

    public void Dispose()
    {
        _reader.Dispose();
        try { File.Delete(_db); } catch { /* the OS will reclaim it */ }
    }

    private JsonObject Call(string tool, JsonObject args)
    {
        var res = _core.CallTool(tool, args);
        return res as JsonObject ?? (res as JsonArray)!.OfType<JsonObject>().First();
    }

    private static string? Status(JsonObject o) =>
        (o["envelope"]?["status"] ?? o["status"])?.GetValue<string>();

    [Fact]
    public void Operation_contract_retains_real_search_and_aggregate_arrays()
    {
        var searchArguments = new JsonObject { ["query"] = "thing" };
        var search = _core.CallTool("search", searchArguments);
        var exact = RequestedOperation.Create(
            "exact", 0, "as_of", new JsonObject(), true,
            [SupportingCallRole.WorkResolution], [OperationEffect.Provision, OperationEffect.Gap]);
        var searchRun = OperationRun.Start(OperationPlan.Create("search-request", "en", [exact]));

        searchRun.ObserveSupportingCall("exact", SupportingCallRole.WorkResolution, "search",
            searchArguments, McpStatus.Ok, search);

        var changesArguments = new JsonObject
        {
            ["from_date"] = "2019-01-01",
            ["to_date"] = "2023-01-01",
        };
        var changes = _core.CallTool("changes_in_period", changesArguments);
        var ranking = RequestedOperation.Create(
            "ranking", 0, "changes_in_period", changesArguments, false, [],
            [OperationEffect.Ranking, OperationEffect.Gap]);
        var result = new OperationExecution(ranking).Complete(McpStatus.Ok, changes);

        Assert.Equal(JsonValueKind.Array, result.Payload?.ValueKind);
        Assert.NotEmpty(result.Payload?.EnumerateArray() ?? []);
        Assert.Single(searchRun.SupportingCalls);
        Assert.Equal(JsonValueKind.Array, searchRun.SupportingCalls[0].Payload.ValueKind);
    }

    [Fact]
    public void Empty_mount_refusal_uses_live_coverage_instead_of_stale_counts()
    {
        var empty = new McpCore(new Dictionary<string, LexIndexReader>());
        var result = Assert.IsType<JsonObject>(empty.CallTool("coverage", new JsonObject()));

        Assert.Equal("no_corpus_mounted", result["status"]?.GetValue<string>());
        Assert.Contains("coverage tool", result["hosted_endpoint_note"]?.GetValue<string>());
        Assert.DoesNotContain("1,409", result.ToJsonString());
        Assert.DoesNotContain("947 MB", result.ToJsonString());
    }

    [Fact]
    public void The_advertised_tool_list_is_the_contract()
    {
        var names = _core.ToolDefs().OfType<JsonObject>()
            .Select(t => t["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(
            ["as_of", "timeline", "in_force_on", "diff", "search",
             "article_history", "provenance", "coverage", "cited_by", "changes_in_period"],
            names);

        // Every tool must document itself: the descriptions ARE the routing layer for a model
        // choosing with tool_choice=auto, so an empty one silently degrades every client.
        Assert.All(_core.ToolDefs().OfType<JsonObject>(), t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t["description"]?.GetValue<string>()));
            Assert.NotNull(t["inputSchema"]?["properties"]);
        });
        var search = _core.ToolDefs().OfType<JsonObject>()
            .Single(tool => tool["name"]!.GetValue<string>() == "search");
        Assert.Equal(1, search["inputSchema"]!["properties"]!["limit"]!["minimum"]!.GetValue<int>());
        Assert.Equal(50, search["inputSchema"]!["properties"]!["limit"]!["maximum"]!.GetValue<int>());
        Assert.Equal(64,
            search["inputSchema"]!["properties"]!["publisher"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(["keyword", "hybrid"],
            search["inputSchema"]!["properties"]!["retrieval_mode"]!["enum"]!.AsArray()
                .Select(item => item!.GetValue<string>()));
        var asOf = _core.ToolDefs().OfType<JsonObject>()
            .Single(tool => tool["name"]!.GetValue<string>() == "as_of");
        Assert.Equal(10,
            asOf["inputSchema"]!["properties"]!["date"]!["maxLength"]!.GetValue<int>());
        Assert.Equal("^[0-9]{4}-[0-9]{2}-[0-9]{2}$",
            asOf["inputSchema"]!["properties"]!["date"]!["pattern"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("t-pub:nope", "2020-06-01", "unknown_work")]      // no such work at all
    [InlineData("t-pub:w1", "1900-01-01", "no_version_for_date")] // work held, not on that date
    public void As_of_refuses_with_the_documented_status(string work, string date, string expected)
        => Assert.Equal(expected, Status(Call("as_of", new JsonObject { ["work"] = work, ["date"] = date })));

    [Fact]
    public void A_work_without_text_says_so_rather_than_pretending_to_be_missing()
    {
        // "we do not have this law" and "we do not have its text" are different answers, and
        // conflating them is as wrong as inventing text.
        var o = Call("as_of", new JsonObject { ["work"] = "t-pub:w2", ["date"] = "2020-01-01" });
        Assert.Equal("text_not_available", Status(o));
        Assert.NotEqual("unknown_work", Status(o));
    }

    [Fact]
    public void A_real_publication_gate_remains_distinct_from_missing_text()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-withheld-{Guid.NewGuid():N}.db");
        try
        {
            var stamp = new Dictionary<string, string>
            {
                ["collection"] = "gated", ["tier"] = "A", ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
            };
            var doc = new DocRow("gated:w1:2020-01-01", "gated", "w1", "urn:w1", "REG", "en",
                "2020-01-01", null, "publisher", "2026-08-01T00:00:00Z", false,
                true, false, "abc", "body", "https://example.org",
                "Gated work", "Gated work", null, "2020-01-01", null);
            IndexBuilder.Build(db, stamp, [doc], [], [], [], StampSigner.CreateKeyPem());
            using var reader = LexIndexReader.Open(db);
            var core = new McpCore(new Dictionary<string, LexIndexReader> { ["gated"] = reader });
            var result = Assert.IsType<JsonObject>(core.CallTool("as_of", new JsonObject
            {
                ["work"] = "gated:w1", ["date"] = "2021-01-01",
            }));

            Assert.Equal("text_withheld", Status(result));
            Assert.NotNull(result["text_withheld_reason"]);
            Assert.Null(result["text_unavailable_reason"]);
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Search_reports_the_mode_actually_used_and_visible_fuzzy_expansions()
    {
        var noVectors = Call("search", new JsonObject
        {
            ["query"] = "thing", ["retrieval_mode"] = "hybrid",
        });
        Assert.Equal("keyword", noVectors["retrieval_mode"]!.GetValue<string>());
        var plan = Assert.IsType<JsonObject>(noVectors["query_plan"]);
        Assert.Equal("thing", plan["provision_query"]!.GetValue<string>());
        Assert.False(plan["has_strong_work_match"]!.GetValue<bool>());
        Assert.Equal("not_requested", plan["work_resolution_status"]!.GetValue<string>());
        Assert.True(plan["work_catalog_available"]!.GetValue<bool>());

        var unknown = Call("search", new JsonObject { ["query"] = "32024R9999" });
        var unknownPlan = Assert.IsType<JsonObject>(unknown["query_plan"]);
        Assert.Equal("unresolved", unknownPlan["work_resolution_status"]!.GetValue<string>());
        Assert.Equal("unresolved",
            unknownPlan["work_resolutions"]![0]!["status"]!.GetValue<string>());

        var typo = Call("search", new JsonObject { ["query"] = "everywher", ["fuzzy"] = "auto" });
        var expansions = Assert.IsType<JsonArray>(typo["query_expansions"]);
        Assert.Contains("everywher -> everywhere", expansions.Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void Global_resolution_preserves_known_and_unknown_mentions_across_publishers()
    {
        var euPath = Path.Combine(Path.GetTempPath(), $"lex-eu-{Guid.NewGuid():N}.db");
        var luPath = Path.Combine(Path.GetTempPath(), $"lex-lu-{Guid.NewGuid():N}.db");
        try
        {
            static DocRow Doc(string collection, string work, string title) => new(
                $"{collection}:{work}:2024-01-01", collection, work, $"urn:{work}", "REG", "fr",
                "2024-01-01", null, "publisher", "2026-08-08T00:00:00Z", false,
                true, true, "record", "body", "https://example.test", title, title,
                null, "2024-01-01", null);
            static Dictionary<string, string> Stamp(string collection) => new()
            {
                ["collection"] = collection, ["tier"] = "A", ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-08T00:00:00Z", ["corpus_commit"] = "test",
            };
            var gdpr = Doc("eu", "32016r0679", "Privacy regulation");
            var lu = Doc("lu", "local", "Local act");
            IndexBuilder.Build(euPath, Stamp("eu"), [gdpr], [], [], [], null,
                workSearch: new WorkSearchBuildOptions(
                    [new ReviewedWorkAliasRow("32016r0679", "fr", "GDPR", "test")],
                    [], new string('a', 64)));
            IndexBuilder.Build(luPath, Stamp("lu"), [lu], [], [], [], null);
            using var euReader = LexIndexReader.Open(euPath);
            using var luReader = LexIndexReader.Open(luPath);
            var core = new McpCore(new Dictionary<string, LexIndexReader>
                { ["eu"] = euReader, ["lu"] = luReader });

            var response = Assert.IsType<JsonArray>(core.CallTool("search", new JsonObject
            {
                ["query"] = "compare GDPR and 32024R9999 reporting obligations",
            }));
            var plan = Assert.IsType<JsonObject>(response[0]!["query_plan"]);
            Assert.Equal("unresolved", plan["global_work_resolution_status"]!.GetValue<string>());
            var resolutions = Assert.IsType<JsonArray>(plan["global_work_resolutions"]);
            Assert.Contains(resolutions.OfType<JsonObject>(), item =>
                item["mention"]!.GetValue<string>() == "gdpr"
                && item["status"]!.GetValue<string>() == "resolved");
            Assert.Contains(resolutions.OfType<JsonObject>(), item =>
                item["mention"]!.GetValue<string>() == "32024R9999"
                && item["status"]!.GetValue<string>() == "unresolved");
        }
        finally
        {
            try { File.Delete(euPath); } catch { }
            try { File.Delete(luPath); } catch { }
        }
    }

    [Fact]
    public void Coverage_reports_overfull_signed_inventory_as_a_mismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-overfull-{Guid.NewGuid():N}.db");
        try
        {
            var stamp = new Dictionary<string, string>
            {
                ["collection"] = "overfull", ["tier"] = "A", ["history_begins"] = "publisher",
                ["built_at"] = "2026-08-08T00:00:00Z", ["corpus_commit"] = "test",
                ["scope_expected_works"] = "1", ["build_issues_json"] = "[]",
                ["build_issues_digest"] = Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData("[]"u8)),
            };
            DocRow Doc(string work) => new($"overfull:{work}:2024-01-01", "overfull", work,
                $"urn:{work}", "REG", "en", "2024-01-01", null, "publisher",
                "2026-08-08T00:00:00Z", false, false, false, "record", null,
                "https://example.test", work, work, null, "2024-01-01", null);
            IndexBuilder.Build(path, stamp, [Doc("one"), Doc("stale")], [], [], [], null);
            using var reader = LexIndexReader.Open(path);
            var core = new McpCore(new Dictionary<string, LexIndexReader> { ["overfull"] = reader });

            var response = Assert.IsType<JsonArray>(core.CallTool("coverage", new JsonObject()));
            Assert.Equal("overfull", response[0]!["build_inventory_status"]!.GetValue<string>());
            Assert.False(response[0]!["build_complete"]!.GetValue<bool>());
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Search_filters_any_registered_jurisdiction_from_index_metadata()
    {
        var matching = Assert.IsType<JsonArray>(_core.CallTool("search", new JsonObject
        {
            ["query"] = "thing", ["jurisdiction"] = "xx",
        }));
        Assert.Single(matching);

        // A jurisdiction nobody mounts is not "we hold nothing here"; it is "that filter names
        // nothing". Returning [] made the two indistinguishable.
        var absent = Assert.IsType<JsonObject>(_core.CallTool("search", new JsonObject
        {
            ["query"] = "thing", ["jurisdiction"] = "YY",
        }));
        Assert.Equal(McpStatus.UnknownPublisher, absent["status"]!.GetValue<string>());
        Assert.Equal("YY", absent["requested_value"]!.GetValue<string>());
        Assert.Contains("XX", absent["mounted_jurisdictions"]!.AsArray()
            .Select(item => item!.GetValue<string>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(int.MaxValue)]
    public void Search_rejects_limits_outside_documented_bounds_before_multiplication(int limit)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _core.CallTool("search", new JsonObject
            {
                ["query"] = "thing",
                ["limit"] = limit,
            }));

        Assert.Contains("between 1 and 50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_rejects_an_unbounded_query_before_retrieval()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _core.CallTool("search", new JsonObject
            {
                ["query"] = new string('x', 1_001),
            }));

        Assert.Contains("1000 characters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exact_work_match_in_one_publisher_suppresses_unresolved_publisher_noise()
    {
        var euDb = Path.Combine(Path.GetTempPath(), $"lex-mcp-eu-{Guid.NewGuid():N}.db");
        var luDb = Path.Combine(Path.GetTempPath(), $"lex-mcp-lu-{Guid.NewGuid():N}.db");
        try
        {
            static DocRow Doc(string collection, string work, string title) => new(
                $"{collection}:{work}:2020-01-01", collection, work, $"urn:{work}", "REG",
                "fr", "2020-01-01", null, "publisher", "2026-08-08T00:00:00Z",
                false, true, true, "record", null, "https://example.invalid", title, title,
                null, "2020-01-01", null);
            static ProvisionRow Provision(DocRow doc, string text) => new(
                $"{doc.Key}|fr|2020-01-01", 0, "art_1", $"{doc.Key}#art_1", "article", "1",
                null, null, null, doc.Title, text,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text))));
            static Dictionary<string, string> Stamp(string collection) => new()
            {
                ["collection"] = collection, ["built_at"] = "2026-08-08T00:00:00Z",
                ["corpus_commit"] = "test",
            };

            var gdpr = Doc("eu", "gdpr", "General Data Protection Regulation");
            var guide = Doc("eu", "guide", "Guide to RGPD reporting");
            var unrelated = Doc("lu", "reporting", "Reporting Act");
            IndexBuilder.Build(euDb, Stamp("eu"), [gdpr, guide],
                [Provision(gdpr, "Controllers have reporting obligations.")], [], [], null,
                workSearch: new WorkSearchBuildOptions(
                    [new ReviewedWorkAliasRow("gdpr", "fr", "RGPD", "reviewer")], [],
                    new string('a', 64)));
            IndexBuilder.Build(luDb, Stamp("lu"), [unrelated],
                [Provision(unrelated, "Companies have reporting obligations.")], [], [], null);
            using var eu = LexIndexReader.Open(euDb);
            using var lu = LexIndexReader.Open(luDb);
            var core = new McpCore(new Dictionary<string, LexIndexReader>
                { ["eu"] = eu, ["lu"] = lu });

            var result = Assert.IsType<JsonArray>(core.CallTool("search", new JsonObject
            {
                ["query"] = "RGPD reporting obligations",
            }));
            var euResult = result.OfType<JsonObject>().Single(item =>
                item["envelope"]?["publisher"]?.GetValue<string>() == "eu");
            var luResult = result.OfType<JsonObject>().Single(item =>
                item["envelope"]?["publisher"]?.GetValue<string>() == "lu");

            var euHits = Assert.IsType<JsonArray>(euResult["hits"]);
            Assert.NotEmpty(euHits);
            Assert.All(euHits.OfType<JsonObject>(), hit =>
                Assert.Equal("gdpr", hit["work"]!.GetValue<string>()));
            Assert.Empty(Assert.IsType<JsonArray>(luResult["hits"]));
        }
        finally
        {
            try { File.Delete(euDb); } catch { }
            try { File.Delete(luDb); } catch { }
        }
    }

    [Theory]
    [InlineData("changes_in_period")]
    [InlineData("in_force_on")]
    public void Every_corpus_wide_tool_filters_registered_jurisdictions(string tool)
    {
        var common = tool == "changes_in_period"
            ? new JsonObject { ["from_date"] = "2019-01-01", ["to_date"] = "2026-01-01" }
            : new JsonObject { ["date"] = "2022-06-01" };
        common["jurisdiction"] = "xx";
        Assert.Single(Assert.IsType<JsonArray>(_core.CallTool(tool, common)));

        common["jurisdiction"] = "YY";
        var absent = Assert.IsType<JsonObject>(_core.CallTool(tool, common));
        Assert.Equal(McpStatus.UnknownPublisher, absent["status"]!.GetValue<string>());
        Assert.Equal(tool, absent["tool_called"]!.GetValue<string>());
    }

    // Every tool used to answer an unmatched publisher with the same bare `[]` that a genuinely
    // empty corpus produces, so nothing distinguished "this filter names nothing" from "Lex holds
    // nothing". Coverage most of all: its whole purpose is saying what is NOT held.
    [Theory]
    [InlineData("coverage")]
    [InlineData("search")]
    [InlineData("in_force_on")]
    [InlineData("changes_in_period")]
    public void An_unmounted_publisher_is_named_rather_than_answered_with_an_empty_set(string tool)
    {
        var arguments = tool switch
        {
            "search" => new JsonObject { ["query"] = "thing" },
            "in_force_on" => new JsonObject { ["date"] = "2022-06-01" },
            "changes_in_period" => new JsonObject
            {
                ["from_date"] = "2019-01-01", ["to_date"] = "2026-01-01",
            },
            _ => new JsonObject(),
        };
        arguments["publisher"] = "Luxembourg";

        var result = Assert.IsType<JsonObject>(_core.CallTool(tool, arguments));

        Assert.Equal(McpStatus.UnknownPublisher, result["status"]!.GetValue<string>());
        Assert.Equal("publisher", result["requested_filter"]!.GetValue<string>());
        Assert.Equal("Luxembourg", result["requested_value"]!.GetValue<string>());
        Assert.NotEmpty(result["mounted_publishers"]!.AsArray());
        Assert.Equal(LegalOutcome.NotFound,
            LegalOperationPolicy.OutcomeForStatus(
                LegalOperationPolicy.StatusForResult(result)));
    }

    // A publisher this server mounts, spelled with different case, names a mounted publisher.
    // Reader selection compares ordinally, so without canonicalising here the caller would be
    // told "T-PUB" is unmounted while the very reader it names answers every other call.
    [Fact]
    public void A_mounted_publisher_spelled_with_different_case_still_selects_its_reader()
    {
        var result = _core.CallTool("search", new JsonObject
        {
            ["query"] = "thing", ["publisher"] = "T-PUB",
        });

        var hits = Assert.IsType<JsonArray>(result);
        Assert.NotEmpty(hits);
        Assert.Equal(
            Assert.IsType<JsonArray>(_core.CallTool("search", new JsonObject
            {
                ["query"] = "thing", ["publisher"] = "t-pub",
            })).Count,
            hits.Count);
    }

    [Fact]
    public void Changes_in_period_applies_hierarchy_domain_and_form_before_counting()
    {
        var result = Call("changes_in_period", new JsonObject
        {
            ["from_date"] = "2019-01-01", ["to_date"] = "2026-01-01",
            ["hierarchy"] = "secondary_law", ["domain"] = "finance",
            ["act_form"] = "REG", ["binding_status"] = "in_force",
        });

        Assert.Equal(1, result["works_changed"]!.GetValue<int>());
        var row = Assert.Single(Assert.IsType<JsonArray>(result["changes"]));
        Assert.Equal("secondary_law", row!["hierarchy"]!.GetValue<string>());
    }

    [Fact]
    public void Search_as_of_returns_only_the_applicable_version()
    {
        var result = Call("search", new JsonObject
        {
            ["query"] = "thing", ["time_scope"] = "as_of", ["as_of"] = "2020-06-01",
        });
        var hits = Assert.IsType<JsonArray>(result["hits"]);
        Assert.NotEmpty(hits);
        Assert.All(hits.OfType<JsonObject>(), h => Assert.Equal("2020-01-01", h["valid_from"]!.GetValue<string>()));
    }

    [Fact]
    public void Diff_accepts_only_a_held_article_anchor_and_returns_it_as_typed_state()
    {
        var result = Call("diff", new JsonObject
        {
            ["work"] = "t-pub:w1", ["from_date"] = "2020-06-01",
            ["to_date"] = "2022-06-01", ["anchor"] = "art_1",
        });

        Assert.Equal("art_1", result["anchor"]!.GetValue<string>());
        Assert.Equal("t-pub:w1", result["work"]!.GetValue<string>());

        var missing = Call("diff", new JsonObject
        {
            ["work"] = "t-pub:w1", ["from_date"] = "2020-06-01",
            ["to_date"] = "2022-06-01", ["anchor"] = "art_92",
        });
        Assert.Equal("unknown_anchor", Status(missing));
        Assert.Contains("art_1", Assert.IsType<JsonArray>(missing["anchors_not_in_version"])
            .Select(item => item!.GetValue<string>()));
    }

    [Fact]
    public void Selecting_an_absent_anchor_is_distinguished_from_an_unknown_work()
    {
        var o = Call("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2020-06-01",
            ["mode"] = "select", ["anchors"] = "art_999",
        });
        Assert.Equal("anchor_not_in_version", Status(o));
    }

    // Every mode of as_of that has provisions must expose them under the same key, in the same
    // shape. full — the DEFAULT mode — returned only a concatenated body, so a client reading
    // `provisions` uniformly got nothing back for a law held in full and reported it missing.
    // Nothing else caught it: the call succeeded, the envelope said "ok", the payload was
    // well-formed JSON, and the bytes were all present under a different key.
    [Theory]
    [InlineData("full")]
    [InlineData("outline")]
    public void Every_mode_exposes_provisions_under_the_same_key(string mode)
    {
        var o = Call("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2022-06-01", ["mode"] = mode,
        });

        var provisions = Assert.IsType<JsonArray>(o["provisions"]);
        Assert.NotEmpty(provisions);
        Assert.Equal("art_1", provisions[0]!["anchor"]!.GetValue<string>());
    }

    [Fact]
    public void Outline_anchor_scope_is_applied_instead_of_returning_the_whole_document()
    {
        var selected = Call("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2022-06-01",
            ["mode"] = "outline", ["anchors"] = "art_1",
        });
        Assert.Equal("art_1", Assert.Single(selected["provisions"]!.AsArray())!["anchor"]!.GetValue<string>());

        var absent = Call("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2022-06-01",
            ["mode"] = "outline", ["anchors"] = "art_999",
        });
        Assert.Empty(absent["provisions"]!.AsArray());
        Assert.Equal("anchor_not_in_version", Status(absent));
        Assert.Equal("art_999", Assert.Single(absent["anchors_not_in_version"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public void Full_carries_the_text_and_carries_it_once()
    {
        var o = Call("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2022-06-01", ["mode"] = "full",
        });

        Assert.Equal("the thing shall apply everywhere, revised",
                     o["provisions"]![0]!["text"]!.GetValue<string>());
        // …and not a second time as a concatenated blob: one payload, one copy of the text.
        Assert.Null(o["document"]!["text"]);
    }

    [Fact]
    public void Article_history_refuses_when_no_per_article_history_is_held()
        => Assert.Contains(
            Status(Call("article_history", new JsonObject { ["work"] = "t-pub:w1", ["anchor"] = "art_1" })),
            new[] { "no_provision_history", "unknown_anchor" });

    [Fact]
    public void Search_returns_pointers_never_body_text()
    {
        var hits = Call("search", new JsonObject { ["query"] = "thing" })["hits"]!.AsArray();
        Assert.NotEmpty(hits);
        Assert.All(hits.OfType<JsonObject>(), h =>
        {
            Assert.NotNull(h["lex_id"]);
            Assert.NotNull(h["snippet"]);
            Assert.Null(h["text"]);      // full state comes from as_of, never from a hit
            Assert.Null(h["text_md"]);
        });
    }

    [Fact]
    public void Coverage_reports_what_is_missing_not_only_what_is_held()
    {
        var o = Call("coverage", []);
        Assert.Equal(3, o["versions"]!.GetValue<int>());
        Assert.Equal(3, o["scope_expected_works"]!.GetValue<int>());
        Assert.Equal("incomplete", o["build_inventory_status"]!.GetValue<string>());
        Assert.False(o["build_complete"]!.GetValue<bool>());
        Assert.Equal("w3", o["build_issues"]![0]!["work"]!.GetValue<string>());
        Assert.NotNull(o["known_gaps"]);
        // text stats must distinguish held-with-text from held-without
        Assert.Equal(2, o["text"]!["versions_with_text_served"]!.GetValue<int>());
        Assert.Equal(1, o["text"]!["versions_without_text"]!.GetValue<int>());
    }

    [Fact]
    public void Changes_in_period_answers_across_works_and_says_when_nothing_moved()
    {
        var moved = Call("changes_in_period", new JsonObject
        { ["from_date"] = "2019-01-01", ["to_date"] = "2023-01-01" });
        Assert.Equal(2, moved["works_changed"]!.GetValue<int>());
        Assert.Equal(3, moved["new_versions"]!.GetValue<int>());
        Assert.Equal(2, moved["population"]!["works_in_scope"]!.GetValue<int>());
        Assert.Contains("selected publisher",
            moved["population"]!["basis"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(
            moved["population"]!["known_exclusions"]!.GetValue<string>()));
        var rows = moved["changes"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.True(rows.Single(r => r["work"]!.GetValue<string>() == "t-pub:w1")["text_comparable"]!.GetValue<bool>());
        Assert.False(rows.Single(r => r["work"]!.GetValue<string>() == "t-pub:w2")["text_comparable"]!.GetValue<bool>());

        var quiet = Call("changes_in_period", new JsonObject
        { ["from_date"] = "2024-01-01", ["to_date"] = "2024-12-31" });
        Assert.Equal("no_changes_in_period", Status(quiet));
    }

    [Fact]
    public void A_missed_anchor_says_which_anchors_exist()
    {
        // Real failure this reproduces: an assistant asked for "art_1er" of the Code du travail,
        // which numbers its provisions L. 010-1 and has no Article 1 at all. It got an empty
        // provisions list and nothing else, fell back to full-text search, and answered out of an
        // unrelated electricity act. A refusal has to leave a next step.
        var miss = Call("as_of", new JsonObject
        {
            ["work"] = "t-pub:w1", ["date"] = "2022-06-01",
            ["mode"] = "select", ["anchors"] = "art_1er",
        });

        Assert.Equal("anchor_not_in_version", Status(miss));
        Assert.Equal("art_1er", miss["anchors_not_in_version"]![0]!.GetValue<string>());
        // "art_1er" and "art_1" share their digits, which is what the mismatch actually is:
        // a numbering convention, not a typo.
        var near = miss["nearest_anchors"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains("art_1", near);
        // And an article question is answered with articles. Matching digits alone answered
        // "art_1er" with "attachment_1", which is true and useless.
        Assert.All(near, a => Assert.StartsWith("art", a));
        Assert.False(string.IsNullOrWhiteSpace(miss["anchor_note"]?.GetValue<string>()));
    }

    // Readers run in collection order and shared one global row budget, so on any query the first
    // publisher matched it drained the whole limit and every later publisher returned an empty
    // hits array. A national corpus was not outranked, it was structurally excluded.
    [Fact]
    public void One_publisher_cannot_drain_the_whole_row_budget()
    {
        var first = Path.Combine(Path.GetTempPath(), $"lex-mcp-floor-a-{Guid.NewGuid():N}.db");
        var second = Path.Combine(Path.GetTempPath(), $"lex-mcp-floor-z-{Guid.NewGuid():N}.db");
        try
        {
            BuildBudgetIndex(first, "a-pub", "Alpha");
            BuildBudgetIndex(second, "z-pub", "Zeta");
            using var alpha = LexIndexReader.Open(first);
            using var zeta = LexIndexReader.Open(second);
            var core = new McpCore(new Dictionary<string, LexIndexReader>
            {
                ["a-pub"] = alpha,
                ["z-pub"] = zeta,
            });

            var shared = Assert.IsType<JsonArray>(core.CallTool("search", new JsonObject
            {
                ["query"] = "resilience obligations",
                ["limit"] = 8,
            }));
            var counts = shared.OfType<JsonObject>().ToDictionary(
                item => item["envelope"]!["publisher"]!.GetValue<string>(),
                item => item["hits"]!.AsArray().Count);

            Assert.Equal(2, counts.Count);
            Assert.All(counts, entry => Assert.InRange(entry.Value, 4, 8));
            Assert.True(counts.Values.Sum() <= 8, $"returned {counts.Values.Sum()} rows");
            Assert.All(shared.OfType<JsonObject>(), item => Assert.True(
                item["response_row_set"]!["returned"]!.GetValue<int>() <= 8));

            // Unchanged: once one publisher resolves the named work, the other is still
            // suppressed outright rather than filling its floor with unrelated law.
            var named = Assert.IsType<JsonArray>(core.CallTool("search", new JsonObject
            {
                ["query"] = "What does the Zeta Resilience Act 0002 require?",
                ["limit"] = 8,
            }));
            var namedCounts = named.OfType<JsonObject>().ToDictionary(
                item => item["envelope"]!["publisher"]!.GetValue<string>(),
                item => item["hits"]!.AsArray().Count);

            Assert.Equal(0, namedCounts["a-pub"]);
            Assert.True(namedCounts["z-pub"] > 0);
        }
        finally
        {
            try { File.Delete(first); } catch { }
            try { File.Delete(second); } catch { }
        }
    }

    private static void BuildBudgetIndex(string db, string collection, string name)
    {
        const string text = "resilience obligations apply here";
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text)));
        var docs = Enumerable.Range(1, 6).Select(index => new DocRow(
            $"{collection}:work-{index:D4}:2024-01-01", collection, $"work-{index:D4}",
            $"urn:{collection}:{index:D4}", "REG", "en", "2024-01-01", null, "publisher",
            "2026-08-01T00:00:00Z", false, true, true, "record", "body",
            "https://example.test/work", $"{name} Resilience Act {index:D4}",
            $"{name} Resilience Act {index:D4}", null, "2024-01-01", null)).ToArray();
        var provisions = docs.Select(doc => new ProvisionRow(
            $"{doc.Key}|en|2024-01-01", 0, "art_1", $"{doc.Key}#art_1", "article", "1",
            null, null, null, doc.Title, text, sha)).ToArray();
        IndexBuilder.Build(db, new Dictionary<string, string>
        {
            ["collection"] = collection, ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        }, docs, provisions, [], [], StampSigner.CreateKeyPem());
    }

    [Fact]
    public void A_scoped_search_stays_inside_its_scope()
    {
        // `works` is documented as restricting the search. The article-level pass honoured it and
        // the identifier/title fallback did not, so a caller that named its subject and matched
        // few articles got unrelated works back to fill the quota — on exactly the path a scoped
        // search is most likely to take, since scoping it makes hits rarer.
        var scoped = Call("search", new JsonObject { ["query"] = "w1", ["works"] = "t-pub:w2" });

        Assert.DoesNotContain("w1", scoped["hits"]!.AsArray()
            .Select(h => h!["work"]?.GetValue<string>() ?? ""));
    }

    // The per-work cap keeps one Code with thousands of articles from filling a corpus-wide result
    // set. Once the caller has named the works, there is nothing left to protect: the cap was
    // answering "search inside this law" with two articles regardless of how many matched, so a
    // scoped search for a term appearing in every article of a work returned 2 of them.
    [Fact]
    public void A_scoped_search_is_not_capped_at_two_articles_per_work()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-mcp-scoped-{Guid.NewGuid():N}.db");
        try
        {
            var document = new DocRow(
                "scoped:code:2024-01-01", "scoped", "code", "urn:code", "REG", "en",
                "2024-01-01", null, "publisher", "2026-08-01T00:00:00Z", false,
                true, true, "record", "body", "https://example.test/code",
                "A code", "A code", null, "2024-01-01", null);
            // Six articles, each carrying the search term, so the cap is the only thing that could
            // reduce the answer below six.
            var provisions = Enumerable.Range(1, 6).Select(index =>
            {
                var text = $"Article {index} concerns surveillance of the sector.";
                return new ProvisionRow(
                    $"{document.Key}|en|2024-01-01", index, $"art_{index}",
                    $"{document.Key}#art_{index}", "article", $"Article {index}", null,
                    null, null, document.Title, text,
                    Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(text))));
            }).ToArray();
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "scoped", ["tier"] = "A",
                ["history_begins"] = "publisher", ["built_at"] = "2026-08-01T00:00:00Z",
                ["corpus_commit"] = "test",
            }, [document], provisions, [], [], StampSigner.CreateKeyPem());
            using var reader = LexIndexReader.Open(db);
            var core = new McpCore(new Dictionary<string, LexIndexReader> { ["scoped"] = reader });

            var scoped = core.CallTool("search", new JsonObject
            {
                ["query"] = "surveillance", ["limit"] = 40, ["works"] = "scoped:code",
            })!.AsArray()[0]!.AsObject();
            Assert.Equal(6, scoped["hits"]!.AsArray().Count);

            // The cap still applies when no scope was given, which is the case it exists for.
            var unscoped = core.CallTool("search", new JsonObject
            {
                ["query"] = "surveillance", ["limit"] = 40,
            })!.AsArray()[0]!.AsObject();
            Assert.Equal(2, unscoped["hits"]!.AsArray().Count);
        }
        finally { try { File.Delete(db); } catch (IOException) { } }
    }

    [Fact]
    public void Every_envelope_carries_freshness_and_signature_state()
    {
        var env = Call("timeline", new JsonObject { ["work"] = "t-pub:w1" })["envelope"]!;
        Assert.Equal("t-pub", env["publisher"]!.GetValue<string>());
        Assert.NotNull(env["freshness"]!["built_at"]);
        Assert.True(env["freshness"]!["stamp_signature_valid"]!.GetValue<bool>());
    }
}
