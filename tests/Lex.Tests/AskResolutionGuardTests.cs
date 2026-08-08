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
}
