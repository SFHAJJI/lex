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
            Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", "2021-12-31", true),
            Row("t-pub:w1:2022-01-01", "w1", "2022-01-01", null, true),
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
    public void The_advertised_tool_list_is_the_contract()
    {
        var names = _core.ToolDefs().OfType<JsonObject>()
            .Select(t => t["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(
            ["as_of", "timeline", "in_force_on", "diff", "search",
             "article_history", "provenance", "coverage", "changes_in_period"],
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
        Assert.Equal("text_withheld", Status(o));
        Assert.NotEqual("unknown_work", Status(o));
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

        var quiet = Call("changes_in_period", new JsonObject
        { ["from_date"] = "2024-01-01", ["to_date"] = "2024-12-31" });
        Assert.Equal("no_changes_in_period", Status(quiet));
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
