using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Mcp;
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
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
            ["jurisdiction"] = "XX",
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

        var typo = Call("search", new JsonObject { ["query"] = "everywher", ["fuzzy"] = "auto" });
        var expansions = Assert.IsType<JsonArray>(typo["query_expansions"]);
        Assert.Contains("everywher -> everywhere", expansions.Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void Search_filters_any_registered_jurisdiction_from_index_metadata()
    {
        var matching = Assert.IsType<JsonArray>(_core.CallTool("search", new JsonObject
        {
            ["query"] = "thing", ["jurisdiction"] = "xx",
        }));
        Assert.Single(matching);

        var absent = Assert.IsType<JsonArray>(_core.CallTool("search", new JsonObject
        {
            ["query"] = "thing", ["jurisdiction"] = "YY",
        }));
        Assert.Empty(absent);
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
        Assert.Empty(Assert.IsType<JsonArray>(_core.CallTool(tool, common)));
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

    [Fact]
    public void Every_envelope_carries_freshness_and_signature_state()
    {
        var env = Call("timeline", new JsonObject { ["work"] = "t-pub:w1" })["envelope"]!;
        Assert.Equal("t-pub", env["publisher"]!.GetValue<string>());
        Assert.NotNull(env["freshness"]!["built_at"]);
        Assert.True(env["freshness"]!["stamp_signature_valid"]!.GetValue<bool>());
    }
}
