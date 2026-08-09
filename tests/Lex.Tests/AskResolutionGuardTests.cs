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
    public void An_empty_resolution_preflight_fails_closed_for_work_specific_tools()
    {
        var guard = new AskService.WorkResolutionGuard();

        guard.ObserveSearch(new JsonArray());

        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32016r0679" }));
        Assert.True(guard.Allows("coverage", new JsonObject()));
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
    public void Aggregate_result_answers_without_confirming_an_unrelated_search_candidate()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            WeakHit("lu-legilux:unrelated:2024-04-21", "Unrelated title match"),
        };
        guard.ObserveSearch(result, isRawUserQuery: true);

        Assert.NotNull(guard.ClarificationFor(null));

        guard.ObserveWorkIndependentAnswer();

        Assert.Null(guard.ClarificationFor(null));
        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "lu-legilux:unrelated" }));
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
    public void A_bare_article_number_is_not_direct_evidence_of_a_work()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            new JsonObject
            {
                ["lex_id"] = "lu-legilux:unrelated:2026-01-01",
                ["anchor"] = "art_7",
                ["title"] = "Unrelated Act",
                ["match_reasons"] = new JsonArray("article_intent"),
            },
        };

        guard.ObserveSearch(result);

        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "lu-legilux:unrelated" }));
        var clarification = Assert.IsType<AskService.WorkResolutionGuard.GuardClarification>(
            guard.ClarificationFor(null));
        Assert.Equal("lu-legilux:unrelated", clarification.Choices[0].Value);
    }

    [Fact]
    public void Only_an_article_without_a_strong_work_is_a_preplanning_ambiguity()
    {
        var bare = SearchResult("not_requested");
        bare[0]!["query_plan"]!["article_number"] = "7";
        bare[0]!["query_plan"]!["has_strong_work_match"] = false;
        var named = bare.DeepClone();
        named[0]!["query_plan"]!["has_strong_work_match"] = true;

        Assert.True(AskService.HasUnscopedArticleIntent(bare));
        Assert.False(AskService.HasUnscopedArticleIntent(named));
        Assert.False(AskService.HasUnscopedArticleIntent(SearchResult("not_requested")));
    }

    [Fact]
    public void Article_intent_with_matching_residual_text_is_direct_evidence()
    {
        var guard = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2021-01-01",
                ["anchor"] = "art_7",
                ["match_reasons"] = new JsonArray("article_intent", "keyword"),
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

    [Fact]
    public void Prior_user_resolution_can_authorize_the_same_work_on_a_follow_up()
    {
        var prior = new AskService.WorkResolutionGuard();
        prior.ObservePriorUserSearch(SearchResult("resolved",
            ("GDPR", "resolved", new[] { "eu-eurlex:32016r0679" })));
        var current = new AskService.WorkResolutionGuard();

        current.AuthorizePriorWorks(prior.ResolvedWorks);
        current.ObserveSearch(SearchResult("not_requested"), isRawUserQuery: true);

        Assert.True(current.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32016r0679" }));
        Assert.False(current.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32022r2554" }));
    }

    [Fact]
    public void Unused_prior_context_does_not_suppress_a_new_weak_clarification()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.AuthorizePriorWorks(["eu-eurlex:32016r0679"]);
        var current = SearchResult("not_requested");
        current[0]!["hits"] = new JsonArray
        {
            WeakHit("eu-eurlex:32022r2554:2024-01-01", "DORA"),
        };
        guard.ObserveSearch(current, isRawUserQuery: true);

        var clarification = guard.ClarificationFor(null);

        Assert.NotNull(clarification);
        Assert.Equal("eu-eurlex:32022r2554", clarification.Choices[0].Value);
    }

    [Fact]
    public void Using_the_prior_work_for_a_typed_follow_up_suppresses_unrelated_candidates()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.AuthorizePriorWorks(["eu-eurlex:32016r0679"]);
        var current = SearchResult("not_requested");
        current[0]!["hits"] = new JsonArray
        {
            WeakHit("eu-eurlex:32022r2554:2024-01-01", "DORA"),
        };
        guard.ObserveSearch(current, isRawUserQuery: true);

        Assert.True(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:32016r0679" }));
        Assert.Null(guard.ClarificationFor(null));
    }

    [Fact]
    public void Prior_context_quarantines_unscoped_raw_hits_until_a_focused_search()
    {
        var guard = new AskService.WorkResolutionGuard();
        guard.AuthorizePriorWorks(["eu-eurlex:32016r0679"]);
        var raw = SearchResult("not_requested");
        raw[0]!["hits"] = new JsonArray
        {
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:unrelated:2026-01-01",
                ["anchor"] = "art_7",
                ["match_reasons"] = new JsonArray("article_intent", "keyword"),
            },
            WeakHit("lu-legilux:also-unrelated:2026-01-01", "Unrelated title"),
        };

        guard.ObserveCurrentUserSearch(raw, hasPriorContext: true);

        Assert.False(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:unrelated" }));
        Assert.Null(guard.ClarificationFor(null));

        guard.ObserveSearch(raw, isRawUserQuery: false);
        Assert.True(guard.Allows("as_of",
            new JsonObject { ["work"] = "eu-eurlex:unrelated" }));
    }

    [Fact]
    public void Prior_weak_or_problem_first_hits_do_not_become_conversation_authority()
    {
        var prior = new AskService.WorkResolutionGuard();
        var result = SearchResult("not_requested");
        result[0]!["hits"] = new JsonArray
        {
            WeakHit("eu-eurlex:32022r2554:2024-01-01", "DORA"),
            new JsonObject
            {
                ["lex_id"] = "eu-eurlex:32016r0679:2021-01-01",
                ["anchor"] = "art_6",
                ["match_reasons"] = new JsonArray("keyword"),
            },
        };

        prior.ObservePriorUserSearch(result);

        Assert.Empty(prior.ResolvedWorks);
        Assert.Null(prior.ClarificationFor(null));
    }

    [Fact]
    public void Conversation_reconstruction_uses_the_most_recent_resolved_user_turn_only()
    {
        var queries = new[] { "Show GDPR", "thanks", "Show DORA", "Article 7 on the same date" };
        var searched = new List<string>();

        var works = AskService.ResolvePriorUserWorks(queries, query =>
        {
            searched.Add(query);
            return query switch
            {
                "Show DORA" => SearchResult("resolved",
                    ("DORA", "resolved", new[] { "eu-eurlex:32022r2554" })),
                "Show GDPR" => SearchResult("resolved",
                    ("GDPR", "resolved", new[] { "eu-eurlex:32016r0679" })),
                _ => SearchResult("not_requested"),
            };
        });

        Assert.Equal(new[] { "Show DORA" }, searched);
        Assert.Equal(new[] { "eu-eurlex:32022r2554" }, works);
    }

    [Fact]
    public void Conversation_reconstruction_ignores_assistant_like_weak_evidence_and_resolver_errors()
    {
        var queries = new[] { "old question", "failing question", "current follow-up" };

        var works = AskService.ResolvePriorUserWorks(queries, query =>
        {
            if (query == "failing question") return null;
            var result = SearchResult("not_requested");
            result[0]!["hits"] = new JsonArray
            {
                WeakHit("eu-eurlex:32022r2554:2024-01-01", "DORA"),
            };
            return result;
        });

        Assert.Empty(works);
    }

    [Fact]
    public void Conversation_reconstruction_never_replays_more_than_three_prior_queries()
    {
        var queries = new[] { "one", "two", "three", "four", "five", "current" };
        var searched = new List<string>();

        var works = AskService.ResolvePriorUserWorks(queries, query =>
        {
            searched.Add(query);
            return SearchResult("not_requested");
        });

        Assert.Empty(works);
        Assert.Equal(new[] { "five", "four", "three" }, searched);
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
