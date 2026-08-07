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
    public void An_unfiltered_ranking_leaves_the_controls_alone()
    {
        // Null means "do not touch it". An assistant that answers a general question must not
        // silently reset a filter the reader chose.
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
    public void Every_filter_argument_maps_to_the_visible_workspace_control()
    {
        var args = Args(
            ("jurisdiction", "eu"), ("hierarchy", "secondary_eu_law"),
            ("domain", "financial-services"), ("source_class", "REG"),
            ("act_form", "REG"), ("binding_status", "in_force"), ("language", "en"));

        var eff = UiMapper.From("search", args, new JsonObject
        {
            ["envelope"] = new JsonObject { ["status"] = "ok" }, ["hits"] = new JsonArray(),
        });

        Assert.Equal("eu", eff.Workspace!.Jurisdiction);
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
    public void Merging_a_turn_keeps_the_first_of_each_kind()
    {
        // One turn can call several tools. The workspace must end in ONE state, not the last one
        // that happened to be written.
        var merged = UiEffect.Merge([
            new UiEffect(Workspace: new WorkspaceView(Jurisdiction: "lu", SourceClass: "LOI,CODE")),
            new UiEffect(Ranking: new RankingView("2025-01-01", "2026-01-01", "by_churn", 1, 1, [])),
            new UiEffect(Workspace: new WorkspaceView(Jurisdiction: "eu", SourceClass: "REG")),
        ]);

        Assert.Equal("lu", merged.Workspace!.Jurisdiction);
        Assert.Equal("LOI,CODE", merged.Workspace.SourceClass);
        Assert.NotNull(merged.Ranking);
        Assert.False(merged.IsEmpty);
    }
}
