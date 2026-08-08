using System.Text.Json.Nodes;
using Lex.Ask;

namespace Lex.Tests;

public sealed class AgentEvidenceLedgerTests
{
    [Fact]
    public void Search_hits_are_pointer_evidence_only()
    {
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("search", null,
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2025-01-01",
                ["anchor"] = "art_33",
                ["permalink"] = "https://law.soufien.lu/eu-eurlex/32016r0679/2025-01-01#art_33",
            },
        ]);

        var evidence = Assert.Single(ledger.Evidence);
        Assert.Equal(AgentEvidenceKind.Pointer, evidence.Kind);
        Assert.Equal("art_33", evidence.Anchor);
    }

    [Fact]
    public void Exact_provision_reads_become_legal_text_evidence()
    {
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("as_of", "ok",
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2025-01-01",
                ["valid_from"] = "2025-01-01",
                ["pinpoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["anchor"] = "art_33",
                        ["text_sha256"] = "abc123",
                        ["quote"] = "Notification text.",
                        ["permalink"] = "https://law.soufien.lu/eu-eurlex/32016r0679/2025-01-01#art_33",
                    },
                },
            },
        ]);

        var evidence = Assert.Single(ledger.Evidence);
        Assert.Equal(AgentEvidenceKind.LegalText, evidence.Kind);
        Assert.Equal("eu-eurlex:32016r0679", evidence.Work);
        Assert.Equal("art_33", evidence.Anchor);
        Assert.Equal("abc123", evidence.TextSha256);
    }

    [Fact]
    public void Outline_reads_support_timeline_claims_not_text_claims()
    {
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("as_of", "ok",
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2025-01-01",
                ["valid_from"] = "2025-01-01",
                ["permalink"] = "https://law.soufien.lu/eu-eurlex/32016r0679/2025-01-01",
                ["pinpoints"] = new JsonArray
                {
                    new JsonObject { ["anchor"] = "art_33", ["text_sha256"] = "abc123" },
                },
            },
        ]);

        Assert.Equal(AgentEvidenceKind.Timeline, Assert.Single(ledger.Evidence).Kind);
    }

    [Fact]
    public void Retrieval_failures_are_coverage_evidence_requiring_disclosure()
    {
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("as_of", "no_version_for_date", []);

        var evidence = Assert.Single(ledger.Evidence);
        Assert.Equal(AgentEvidenceKind.Coverage, evidence.Kind);
        Assert.True(evidence.RequiresCoverageDisclosure);
    }

    [Fact]
    public void Coverage_evidence_preserves_the_bounded_tool_payload()
    {
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("coverage", "ok", [], new JsonObject
        {
            ["works"] = 15,
            ["build_inventory_status"] = "complete",
        });

        var evidence = Assert.Single(ledger.Evidence);
        Assert.Contains("\"works\":15", evidence.Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"build_inventory_status\":\"complete\"", evidence.Excerpt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ranking_evidence_preserves_raw_window_and_aggregate_counts_when_paginated()
    {
        var raw = new JsonObject
        {
            ["from_date"] = "2020-01-01",
            ["to_date"] = "2020-12-31",
            ["order"] = "by_churn",
            ["works_changed"] = 41,
            ["new_versions"] = 73,
            ["shown"] = 1,
            ["offset"] = 20,
        };
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("changes_in_period", "ok",
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679",
                ["snippet"] = "2 new versions",
            },
        ], raw);

        var aggregate = ledger.Evidence.First(item => item.Title?.Contains(
            "aggregate ranking result", StringComparison.Ordinal) == true);
        Assert.Contains("\"works_changed\":41", aggregate.Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"new_versions\":73", aggregate.Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"offset\":20", aggregate.Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"from_date\":\"2020-01-01\"", aggregate.Excerpt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ranking_aggregates_are_preserved_per_publisher_when_first_rows_exceed_the_bound()
    {
        static JsonObject Publisher(string name, int works, int versions, string rowText) => new()
        {
            ["envelope"] = new JsonObject { ["publisher"] = name, ["status"] = "ok" },
            ["window"] = new JsonObject { ["from"] = "2020-01-01", ["to"] = "2020-12-31" },
            ["works_changed"] = works,
            ["new_versions"] = versions,
            ["shown"] = 1,
            ["offset"] = 0,
            ["changes"] = new JsonArray { new JsonObject { ["title"] = rowText } },
        };

        var raw = new JsonArray
        {
            Publisher("eu-eurlex", 15, 25, new string('x', 9_000)),
            Publisher("lu-legilux", 7, 11, "short row"),
        };
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("changes_in_period", "ok", [], raw);

        var aggregates = ledger.Evidence.Where(item => item.Title?.Contains(
            "aggregate ranking result", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(2, aggregates.Length);
        Assert.Contains("\"works_changed\":15", aggregates[0].Excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 100), aggregates[0].Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"works_changed\":7", aggregates[1].Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"new_versions\":11", aggregates[1].Excerpt, StringComparison.Ordinal);
        Assert.Contains("\"publisher\":\"lu-legilux\"", aggregates[1].Excerpt,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("anchor_not_in_version")]
    [InlineData("unknown_anchor")]
    [InlineData("no_provision_history")]
    public void Missing_provision_states_require_gap_disclosure(string status)
    {
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("as_of", status,
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2025-01-01",
                ["pinpoints"] = new JsonArray
                {
                    new JsonObject { ["anchor"] = "art_33", ["text_sha256"] = "abc123" },
                },
            },
        ]);

        var evidence = Assert.Single(ledger.Evidence);
        Assert.Equal(AgentEvidenceKind.Coverage, evidence.Kind);
        Assert.True(evidence.RequiresCoverageDisclosure);
    }

    [Fact]
    public void Legal_text_prompt_uses_bounded_exact_tool_text_not_only_the_ui_preview()
    {
        var fullText = new string('x', 8_100);
        var raw = new JsonObject
        {
            ["document"] = new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2025-01-01",
            },
            ["provisions"] = new JsonArray
            {
                new JsonObject { ["anchor"] = "art_33", ["text"] = fullText },
            },
        };
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("as_of", "ok",
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2025-01-01",
                ["pinpoints"] = new JsonArray
                {
                    new JsonObject { ["anchor"] = "art_33", ["quote"] = "short preview" },
                },
            },
        ], raw);

        Assert.Equal(8_000, Assert.Single(ledger.Evidence).Excerpt?.Length);
    }

    [Fact]
    public void Exact_text_is_bound_to_the_matching_document_when_anchors_repeat()
    {
        var raw = new JsonArray
        {
            AsOf("eu-eurlex:first:2025-01-01", "first text"),
            AsOf("eu-eurlex:second:2025-01-01", "second text"),
        };
        var ledger = new AgentEvidenceLedger();
        ledger.Observe("as_of", "ok",
        [
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:second:2025-01-01",
                ["pinpoints"] = new JsonArray { new JsonObject { ["anchor"] = "art_1" } },
            },
        ], raw);

        Assert.Equal("second text", Assert.Single(ledger.Evidence).Excerpt);
    }

    private static JsonObject AsOf(string lexId, string text) => new()
    {
        ["document"] = new JsonObject { ["lex_id"] = lexId },
        ["provisions"] = new JsonArray
        {
            new JsonObject { ["anchor"] = "art_1", ["text"] = text },
        },
    };
}
