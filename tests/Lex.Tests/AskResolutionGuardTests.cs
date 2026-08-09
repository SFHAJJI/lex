using System.Text.Json.Nodes;
using Lex.Ask;

namespace Lex.Tests;

public sealed class AskResolutionGuardTests
{
    [Fact]
    public void Ambiguous_candidates_cannot_reach_work_specific_tools_before_confirmation()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("ambiguous",
            ("reporting act", "ambiguous", new[] { "eu:first", "eu:second" })));

        Assert.False(guard.Allows("as_of", new JsonObject { ["work"] = "eu:first" }));
        Assert.False(guard.Allows("timeline", new JsonObject { ["work"] = "eu:second" }));
        Assert.True(guard.Allows("search", new JsonObject { ["query"] = "32024R9999" }));
    }

    [Fact]
    public void Mixed_resolution_allows_only_the_explicitly_resolved_work()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("unresolved",
            ("gdpr", "resolved", new[] { "eu:32016r0679" }),
            ("32024R9999", "unresolved", Array.Empty<string>())));

        Assert.True(guard.Allows("diff", new JsonObject { ["work"] = "eu:32016r0679" }));
        Assert.False(guard.Allows("as_of", new JsonObject { ["work"] = "eu:other" }));
    }

    [Fact]
    public void Resolved_search_authorizes_only_the_resolved_work()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("resolved",
            ("gdpr", "resolved", new[] { "eu-eurlex:32016r0679" })));

        Assert.True(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32016r0679" }));
        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32022r2554" }));
    }

    [Fact]
    public void Weak_discovery_without_a_provision_cannot_authorize_a_follow_up()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32022r2554:2024-01-01",
                ["anchor"] = null,
                ["match_reasons"] = new JsonArray("semantic_concept"),
            },
        };
        guard.ObserveSearch(result);

        Assert.False(guard.Allows("timeline",
            new JsonObject { ["work"] = "eu-eurlex:32022r2554" }));
    }

    [Fact]
    public void Weak_candidates_produce_a_bounded_clarification_with_the_attempted_work_first()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            WeakHit("lu-legilux:first:2026-01-01", "First possible instrument"),
            WeakHit("lu-legilux:second:2026-01-01", "Second possible instrument"),
            WeakHit("lu-legilux:third:2026-01-01", "Third possible instrument"),
            WeakHit("lu-legilux:fourth:2026-01-01", "Fourth possible instrument"),
            WeakHit("lu-legilux:fifth:2026-01-01", "Fifth possible instrument"),
        };
        guard.ObserveSearch(result);

        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "lu-legilux:fourth:2026-01-01" }));
        var clarification = guard.ClarificationFor("lu-legilux:fourth:2026-01-01");

        Assert.NotNull(clarification);
        Assert.InRange(clarification.Display.Options.Count, 2, 4);
        Assert.StartsWith("Fourth possible instrument", clarification.Display.Options[0]);
        Assert.Equal("lu-legilux:fourth", clarification.Choices[0].Value);
        Assert.All(clarification.Display.Options, option => Assert.InRange(option.Length, 1, 100));
    }

    [Fact]
    public void Model_reformulation_can_supply_choices_without_authorizing_one()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("not_requested"), isRawUserQuery: true);
        var reformulated = SearchResult("resolved",
            ("data protection law", "resolved", new[] { "lu-legilux:data" }));
        reformulated[0]!["hits"] = new JsonArray
        {
            WeakHit("lu-legilux:data:2026-01-01", "Data protection law"),
            WeakHit("lu-legilux:commerce:2026-01-01", "Electronic commerce law"),
        };
        guard.ObserveSearch(reformulated, isRawUserQuery: false);

        Assert.False(guard.Allows("as_of", new JsonObject { ["work"] = "lu-legilux:data" }));
        var clarification = guard.ClarificationFor("lu-legilux:data");

        Assert.NotNull(clarification);
        Assert.Equal(2, clarification.Display.Options.Count);
        Assert.StartsWith("Data protection law", clarification.Display.Options[0]);
        Assert.Equal("lu-legilux:data", clarification.Choices[0].Value);
    }

    [Fact]
    public void Latest_reformulation_candidates_replace_broad_raw_candidates_at_the_front()
    {
        var guard = new AskService.WorkResolutionGuard();
        var raw = SearchResult("not_requested");
        raw[0]!["hits"] = new JsonArray(Enumerable.Range(1, 8)
            .Select(index => (JsonNode)WeakHit(
                $"lu-legilux:broad-{index}:2026-01-01", $"Broad candidate {index}"))
            .ToArray());
        guard.ObserveSearch(raw, isRawUserQuery: true);
        var reformulated = SearchResult("resolved");
        reformulated[0]!["hits"] = new JsonArray
        {
            WeakHit("lu-legilux:specific:2026-01-01", "Specific candidate"),
            WeakHit("lu-legilux:second-specific:2026-01-01", "Second specific candidate"),
        };
        guard.ObserveSearch(reformulated, isRawUserQuery: false);

        var clarification = guard.ClarificationFor(null);

        Assert.NotNull(clarification);
        Assert.Equal(new[] { "lu-legilux:specific", "lu-legilux:second-specific" },
            clarification.Choices.Take(2).Select(choice => choice.Value));
    }

    [Fact]
    public void Full_candidate_values_survive_bounded_distinct_display_labels()
    {
        var shared = new string('a', 110);
        var first = $"lu-legilux:{shared}1";
        var second = $"lu-legilux:{shared}2";
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            WeakHit(first + ":2026-01-01", "Same title"),
            WeakHit(second + ":2026-01-01", "Same title"),
        };
        guard.ObserveSearch(result);

        var clarification = guard.ClarificationFor(first);

        Assert.NotNull(clarification);
        Assert.Equal(new[] { first, second }, clarification.Choices.Select(choice => choice.Value));
        Assert.Equal(2, clarification.Display.Options.Distinct().Count());
        Assert.All(clarification.Display.Options, option => Assert.True(option.Length <= 100));
    }

    [Fact]
    public void A_single_weak_candidate_creates_an_explicit_confirmation_and_non_selection()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            WeakHit("lu-legilux:only:2026-01-01", "Only possible instrument"),
        };
        guard.ObserveSearch(result);

        var clarification = guard.ClarificationFor("lu-legilux:only");

        Assert.NotNull(clarification);
        Assert.Equal(2, clarification.Choices.Count);
        Assert.Equal("lu-legilux:only", clarification.Choices[0].Value);
        Assert.True(AskService.WorkResolutionGuard.IsExplicitNonSelection(
            clarification.Choices[1].Value));
    }

    [Fact]
    public void Explicit_canonical_confirmation_authorizes_only_the_selected_work()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("not_requested"), isRawUserQuery: true);

        guard.ObserveUserConfirmation("Clarification choice: lu-legilux:code-environnement");

        Assert.True(guard.Allows("as_of",
            new JsonObject { ["work"] = "lu-legilux:code-environnement" }));
        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "lu-legilux:code-penal" }));
    }

    [Theory]
    [InlineData(978)]
    [InlineData(979)]
    [InlineData(1000)]
    public void Full_length_canonical_confirmation_stays_within_the_search_query_bound(int length)
    {
        var selected = "lu-legilux:" + new string('a', length - "lu-legilux:".Length);
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("not_requested"), isRawUserQuery: true);

        guard.ObserveUserConfirmation(selected);

        Assert.Equal(length, selected.Length);
        Assert.True(guard.Allows("as_of", new JsonObject { ["work"] = selected }));
        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "lu-legilux:another" }));
    }

    [Fact]
    public void Direct_provision_evidence_can_authorize_problem_first_retrieval()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2024-01-01",
                ["anchor"] = "art_33",
                ["match_reasons"] = new JsonArray("keyword"),
            },
        };
        guard.ObserveSearch(result);

        Assert.True(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32016r0679" }));
    }

    [Fact]
    public void A_model_generated_name_remains_a_candidate_not_an_authority()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.ObserveSearch(SearchResult("not_requested"), isRawUserQuery: true);
        guard.ObserveSearch(SearchResult("resolved",
            ("DORA", "resolved", new[] { "eu-eurlex:32022r2554" })), isRawUserQuery: false);

        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32022r2554" }));
    }

    private static JsonArray SearchResult(string status,
        params (string Mention, string Status, string[] Candidates)[] resolutions) =>
    [
        new JsonObject
        {
            ["query_plan"] = new JsonObject
            {
                ["global_work_resolution_status"] = status,
                ["global_work_resolutions"] = new JsonArray(resolutions.Select(item =>
                    (JsonNode)new JsonObject
                    {
                        ["mention"] = item.Mention,
                        ["status"] = item.Status,
                        ["candidates"] = new JsonArray(item.Candidates.Select(candidate =>
                            (JsonNode)candidate).ToArray()),
                    }).ToArray()),
            },
        },
    ];

    private static JsonObject WeakHit(string lexId, string title) => new()
    {
        ["lex_id"] = lexId,
        ["title"] = title,
        ["anchor"] = null,
        ["match_reasons"] = new JsonArray("work_metadata"),
    };
}
