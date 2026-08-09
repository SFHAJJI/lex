using System.Text.Json.Nodes;
using Lex.Ask;

namespace Lex.Tests;

/// <summary>
/// The AI-to-UI contract (D31, D51).
///
/// The assistant answers in prose AND sets the workspace, and the second half is the part with no
/// visible failure mode: if the mapping breaks, the answer still reads correctly while the
/// controls beneath it quietly disagree with it. A reader asked for EU regulations, was given EU
/// regulations, and sees an unfiltered Luxembourg scope. Nothing errors. That is why this is
/// tested rather than checked by eye once.
/// </summary>
public class UiEffectTests
{
    private static JsonObject Args(params (string K, string V)[] kv)
    {
        var o = new JsonObject();
        foreach (var (k, v) in kv) o[k] = v;
        return o;
    }

    private static JsonObject Changes(string types, int offset = 0) => new()
    {
        ["envelope"] = new JsonObject { ["status"] = "ok" },
        ["window"] = new JsonObject { ["from"] = "2025-01-01", ["to"] = "2026-01-01" },
        ["order"] = "by_churn",
        ["works_changed"] = 42,
        ["new_versions"] = 99,
        ["offset"] = offset,
        ["changes"] = new JsonArray(new JsonObject
        {
            ["work"] = "lu-legilux:loi-2006-07-31-n2",
            ["title"] = "Code du travail",
            ["versions_in_period"] = 3,
            ["versions_total"] = 56,
            ["first_change"] = "2025-02-01",
            ["last_change"] = "2025-11-01",
        }),
    };

    [Theory]
    [InlineData("!RECUEIL,!CODE_RECUEIL")]
    [InlineData("LOI,CODE")]
    [InlineData("REG")]
    [InlineData("DIR")]
    public void A_source_class_filter_reaches_the_same_mixed_corpus_control(string types)
    {
        var eff = UiMapper.From("changes_in_period", Args(("document_type", types)), Changes(types));

        Assert.NotNull(eff.Workspace);
        Assert.Equal(types, eff.Workspace!.SourceClass);
    }

    [Fact]
    public void An_unfiltered_ranking_has_no_filter_directive()
    {
        // The ranking itself owns navigation. No workspace filter means the complete corpus;
        // the browser state mapper clears any stale scope when it applies this effect.
        var eff = UiMapper.From("changes_in_period", new JsonObject(), Changes(""));

        Assert.NotNull(eff.Ranking);
        Assert.Null(eff.Workspace);
    }

    [Fact]
    public void Paging_is_carried_back_so_the_pager_agrees_with_the_rows()
    {
        var eff = UiMapper.From("changes_in_period",
            Args(("document_type", "LOI,CODE")), Changes("LOI,CODE", offset: 50));

        Assert.Equal(2, eff.Workspace!.Page);
    }

    [Fact]
    public void A_mixed_period_combines_publishers_and_keeps_each_jurisdiction()
    {
        JsonObject Part(string publisher, string jurisdiction, string work, int works) => new()
        {
            ["envelope"] = new JsonObject
            {
                ["status"] = "ok", ["publisher"] = publisher, ["jurisdiction"] = jurisdiction,
            },
            ["window"] = new JsonObject { ["from"] = "2025-01-01", ["to"] = "2026-01-01" },
            ["order"] = "by_churn", ["works_changed"] = works, ["new_versions"] = works,
            ["changes"] = new JsonArray(new JsonObject
            {
                ["work"] = work, ["versions_in_period"] = 1, ["versions_total"] = 2,
                ["first_change"] = "2025-06-01", ["last_change"] = "2025-06-01",
            }),
        };
        var result = new JsonArray(
            Part("lu-legilux", "LU", "lu-legilux:loi-1", 2),
            Part("eu-eurlex", "EU", "eu-eurlex:32025R0001", 3));

        var eff = UiMapper.From("changes_in_period", new JsonObject(), result);

        Assert.Equal(5, eff.Ranking!.WorksChanged);
        Assert.Equal(2, eff.Ranking.Rows.Count);
        Assert.Equal(["LU", "EU"], eff.Ranking.Rows.Select(row => row.Jurisdiction));
    }

    [Fact]
    public void A_single_publisher_period_stamps_its_jurisdiction_on_every_row()
    {
        var result = Changes("");
        result["envelope"]!["jurisdiction"] = "LU";

        var eff = UiMapper.From("changes_in_period",
            Args(("jurisdiction", "lu")), result);

        Assert.All(eff.Ranking!.Rows, row => Assert.Equal("LU", row.Jurisdiction));
    }

    [Fact]
    public void Separate_publisher_rankings_merge_into_one_cross_corpus_effect()
    {
        static RankingView Ranking(string work, string jurisdiction, int changed, int versions) =>
            new("2024-01-01", "2024-12-31", "by_churn", changed, versions,
            [
                new RankingRow(work, $"{jurisdiction} law", versions, versions,
                    "2024-01-01", "2024-12-31", null, null,
                    Jurisdiction: jurisdiction),
            ]);

        var merged = UiEffect.Merge([
            new UiEffect(Ranking: Ranking("lu-legilux:one", "LU", 210, 240),
                Workspace: new WorkspaceView(Jurisdiction: "lu")),
            new UiEffect(Ranking: Ranking("eu-eurlex:two", "EU", 187, 200),
                Workspace: new WorkspaceView(Jurisdiction: "eu")),
            new UiEffect(Ranking: Ranking("third-publisher:three", "THIRD", 3, 5),
                Workspace: new WorkspaceView(Jurisdiction: "third")),
        ]);

        Assert.Equal(400, merged.Ranking!.WorksChanged);
        Assert.Equal(445, merged.Ranking.NewVersions);
        Assert.Equal(["EU", "LU", "THIRD"], merged.Ranking.Rows
            .Select(row => row.Jurisdiction).Order());
        Assert.NotNull(merged.Workspace);
        Assert.Null(merged.Workspace.Jurisdiction);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unfiltered_ranking_dominates_a_redundant_filtered_ranking(bool broadFirst)
    {
        static UiEffect Broad() => new(Ranking: new RankingView(
            "2024-01-01", "2024-12-31", "by_churn", 397, 440, []));
        static UiEffect Luxembourg() => new(Ranking: new RankingView(
                "2024-01-01", "2024-12-31", "by_churn", 210, 240, []),
            Workspace: new WorkspaceView(Jurisdiction: "lu"));

        var merged = UiEffect.Merge(broadFirst
            ? [Broad(), Luxembourg()]
            : [Luxembourg(), Broad()]);

        Assert.Equal(397, merged.Ranking!.WorksChanged);
        Assert.Null(merged.Workspace);
    }

    [Fact]
    public void Every_filter_argument_maps_to_the_visible_workspace_control()
    {
        var args = Args(
            ("query", "capital requirements"),
            ("jurisdiction", "eu"), ("hierarchy", "secondary_eu_law"),
            ("domain", "financial-services"), ("source_class", "REG"),
            ("act_form", "REG"), ("binding_status", "in_force"), ("language", "en"));

        var eff = UiMapper.From("search", args, new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = "ok" }, ["hits"] = new JsonArray(),
        });

        Assert.Equal("capital requirements", eff.Workspace!.Query);
        Assert.Equal("eu", eff.Workspace.Jurisdiction);
        Assert.Equal("secondary_eu_law", eff.Workspace.Hierarchy);
        Assert.Equal("financial-services", eff.Workspace.Domain);
        Assert.Equal("REG", eff.Workspace.SourceClass);
        Assert.Equal("REG", eff.Workspace.ActForm);
        Assert.Equal("in_force", eff.Workspace.BindingStatus);
        Assert.Equal("en", eff.Workspace.Language);
    }

    [Fact]
    public void A_language_narrowed_search_sets_the_language_control()
    {
        // This one matters beyond tidiness: the Constitution exists in French, German and
        // Luxembourgish, so an answer drawn from the German text beside a control saying "any"
        // misrepresents which text was read.
        var eff = UiMapper.From("search", Args(("language", "de")), new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = "ok" },
            ["hits"] = new JsonArray(),
        });

        Assert.Equal("de", eff.Workspace!.Language);
        Assert.Null(eff.Workspace.SourceClass);
    }

    [Fact]
    public void A_whole_work_timeline_opens_the_law_and_its_version_rail()
    {
        var eff = UiMapper.From("timeline", Args(("work", "eu-eurlex:32013r0575")),
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["status"] = "ok", ["publisher"] = "eu-eurlex",
                },
                ["work"] = "32013r0575",
                ["total_count"] = 2,
                ["versions"] = new JsonArray(
                    new JsonObject
                    {
                        ["title"] = "Regulation (EU) No 575/2013",
                        ["valid_from"] = "2020-06-27", ["valid_to"] = "2021-06-26",
                        ["permalink"] = "https://law.soufien.lu/eu-eurlex/32013r0575/2020-06-27",
                    },
                    new JsonObject
                    {
                        ["title"] = "Regulation (EU) No 575/2013",
                        ["valid_from"] = "2021-06-27", ["valid_to"] = null,
                        ["permalink"] = "https://law.soufien.lu/eu-eurlex/32013r0575/2021-06-27",
                    }),
            });

        Assert.Equal("eu-eurlex:32013r0575", eff.Timeline!.Subject.Work);
        Assert.Null(eff.Timeline.Subject.Date);
    }

    [Fact]
    public void Cited_by_becomes_its_own_view()
    {
        var eff = UiMapper.From("cited_by", Args(("work", "lu-legilux:code-penal")), new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = "ok" },
            ["cited_work"] = "lu-legilux:code-penal",
            ["citing_articles"] = 2,
            ["citations"] = new JsonArray(
                new JsonObject
                {
                    ["work"] = "lu-legilux:loi-1980-03-07-n1", ["title"] = "Cours et Tribunaux",
                    ["valid_from"] = "2026-09-16", ["anchor"] = "art_37", ["num"] = "Art. 37.",
                },
                new JsonObject
                {
                    ["work"] = "lu-legilux:loi-1980-03-07-n1", ["title"] = "Cours et Tribunaux",
                    ["valid_from"] = "2026-09-16", ["anchor"] = "art_74-2", ["num"] = "Art. 74-2.",
                }),
        });

        Assert.Equal(2, eff.CitedBy!.CitingArticles);
        Assert.Equal("art_37", eff.CitedBy.Rows[0].Anchor);
        Assert.Equal("lu-legilux:code-penal", eff.CitedBy.CitedWork);
    }

    [Fact]
    public void An_empty_result_produces_no_view_at_all()
    {
        // A view with nothing in it would replace whatever the reader was looking at with a blank
        // panel, which reads as breakage rather than as "that found nothing".
        var eff = UiMapper.From("cited_by", Args(("work", "lu-legilux:nothing")), new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = "no_result" },
            ["citations"] = new JsonArray(),
        });

        Assert.Null(eff.CitedBy);
    }

    [Fact]
    public void An_as_of_outline_remains_a_navigable_provision_view_without_legal_text()
    {
        var eff = UiMapper.From("as_of",
            Args(("work", "lu-legilux:code-environnement"), ("date", "2026-08-09")),
            new JsonObject
            {
                ["document"] = new JsonObject
                {
                    ["work"] = "lu-legilux:code-environnement",
                    ["title"] = "Code de l'environnement",
                    ["valid_from"] = "2026-01-01",
                },
                ["provisions"] = new JsonArray(new JsonObject
                {
                    ["anchor"] = "art_1", ["num"] = "Art. 1", ["heading"] = "Scope",
                    ["text"] = null, ["text_sha256"] = "abc",
                }),
            });

        Assert.NotNull(eff.Provision);
        Assert.Single(eff.Provision.Provisions);
        Assert.Equal("", eff.Provision.Provisions[0].Text);
        Assert.Equal("art_1", eff.Provision.Provisions[0].Anchor);
    }

    [Fact]
    public void A_half_resolved_diff_still_maps()
    {
        // A diff whose second side did not resolve used to throw inside the mapper, which loses
        // the whole answer — prose included — over a missing sub-object. It must degrade to a view
        // with the fields it does have.
        var eff = UiMapper.From("diff",
            Args(("work", "lu-legilux:loi-2006-07-31-n2"), ("from_date", "2024-01-01"), ("to_date", "2025-01-01")),
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = "ok" },
                ["from"] = new JsonObject { ["valid_from"] = "2024-01-01", ["title"] = "Code du travail" },
                // no "to" at all
            });

        Assert.Equal("2024-01-01", eff.Diff!.FromDate);
        Assert.Equal("2025-01-01", eff.Diff.ToDate);
        Assert.Equal("Code du travail", eff.Diff.Subject.Title);
    }

    [Fact]
    public void An_article_diff_keeps_the_requested_window_and_verified_anchor()
    {
        var eff = UiMapper.From("diff",
            Args(("work", "eu-eurlex:32013r0575"), ("from_date", "2020-01-01"),
                ("to_date", "2024-12-31"), ("anchor", "art_92"), ("language", "en")),
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = "profiles_differ" },
                ["anchor"] = "art_92",
                ["from"] = new JsonObject
                {
                    ["valid_from"] = "2019-06-27", ["title"] = "Regulation (EU) No 575/2013",
                },
                ["to"] = new JsonObject { ["valid_from"] = "2024-07-09", ["language"] = "en" },
            });

        Assert.Equal("art_92", eff.Diff!.Subject.Anchor);
        Assert.Equal("2020-01-01", eff.Diff.FromDate);
        Assert.Equal("2024-12-31", eff.Diff.ToDate);
        Assert.Equal("profiles_differ", eff.Diff.Status);
        Assert.Equal("en", eff.Diff.Subject.Language);
    }

    [Fact]
    public void A_rendered_ranking_owns_the_final_workspace_scope()
    {
        // One turn can call several tools. The workspace must end in ONE state, not the last one
        // that happened to be written.
        var merged = UiEffect.Merge([
            new UiEffect(Workspace: new WorkspaceView(Jurisdiction: "lu", SourceClass: "LOI,CODE")),
            new UiEffect(Ranking: new RankingView("2025-01-01", "2026-01-01", "by_churn", 1, 1, [])),
            new UiEffect(Workspace: new WorkspaceView(Jurisdiction: "eu", SourceClass: "REG")),
        ]);

        Assert.Null(merged.Workspace);
        Assert.NotNull(merged.Ranking);
        Assert.False(merged.IsEmpty);
    }
}
