using System.Text.Json.Nodes;
using Lex.RetrievalExperiment;
using Microsoft.Data.Sqlite;

namespace Lex.RetrievalExperiment.Tests;

public sealed class EnrichmentContractTests
{
    private const string EvidenceSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Strict_parser_rejects_every_field_outside_the_discovery_contract()
    {
        var json = ValidJson();
        json["official_title"] = "A model must never write this";

        var error = Assert.Throws<InvalidDataException>(() => EnrichmentContract.Parse(json));

        Assert.Contains("official_title", error.Message);
    }

    [Fact]
    public void A_model_alias_cannot_receive_exact_name_rank_without_review()
    {
        var proposal = Proposal("alias", "RGPD") with { ReviewedBy = null };

        var result = EnrichmentContract.Validate([proposal]);

        var rejection = Assert.Single(result.Rejected);
        Assert.Equal("alias_requires_review", rejection.Reason);
        Assert.Empty(result.Accepted);
    }

    [Fact]
    public void A_reviewed_collision_free_alias_receives_exact_name_rank()
    {
        var proposal = Proposal("alias", "RGPD") with { ReviewedBy = "engineer:sfhajji" };

        var accepted = Assert.Single(EnrichmentContract.Validate([proposal]).Accepted);

        Assert.Equal("exact_alias", accepted.RankTier);
        Assert.Equal("rgpd", accepted.NormalizedValue);
    }

    [Fact]
    public void Repeatable_grounded_description_is_a_weak_discovery_signal()
    {
        var proposal = Proposal("description", "notification des violations de données") with
        {
            RepeatRuns = 3,
            AgreementRatio = 1,
        };

        var accepted = Assert.Single(EnrichmentContract.Validate([proposal]).Accepted);

        Assert.Equal("weak_discovery", accepted.RankTier);
        Assert.Equal("model-derived", accepted.Provenance);
    }

    [Fact]
    public void Alias_collisions_are_rejected_for_every_affected_work()
    {
        var first = Proposal("alias", "Data Act") with
        {
            Work = "eu-eurlex:32023r2854",
            Evidence = [new EvidenceAnchor("eu-eurlex:32023r2854", "2023-12-22", "art_1", EvidenceSha)],
            ReviewedBy = "engineer:sfhajji",
        };
        var second = Proposal("alias", "Data Act") with
        {
            Work = "eu-eurlex:another-work",
            Evidence = [new EvidenceAnchor("eu-eurlex:another-work", "2024-01-01", "art_1", EvidenceSha)],
            ReviewedBy = "engineer:sfhajji",
        };

        var result = EnrichmentContract.Validate([first, second]);

        Assert.Empty(result.Accepted);
        Assert.Equal(2, result.Rejected.Count(x => x.Reason == "alias_collision"));
    }

    [Theory]
    [InlineData("Règlement général -- données", "reglement general donnees")]
    [InlineData("  AI.Act  ", "ai act")]
    [InlineData("MiFID-II", "mifid ii")]
    public void Normalization_is_accent_punctuation_and_case_invariant(string value, string expected)
        => Assert.Equal(expected, EnrichmentContract.Normalize(value));

    [Fact]
    public void Accepted_export_is_stably_sorted_and_excludes_rejections()
    {
        var description = Proposal("description", "protection des données") with
        {
            RepeatRuns = 2,
            AgreementRatio = 1,
        };
        var alias = Proposal("alias", "RGPD") with { ReviewedBy = "engineer:sfhajji" };
        var rejected = Proposal("alias", "unreviewed");

        var first = EnrichmentContract.Export(EnrichmentContract.Validate([description, rejected, alias]).Accepted);
        var second = EnrichmentContract.Export(EnrichmentContract.Validate([alias, description, rejected]).Accepted);

        Assert.Equal(first, second);
        Assert.DoesNotContain("unreviewed", first);
        var rows = JsonNode.Parse(first)!.AsArray();
        Assert.Equal("alias", rows[0]!["kind"]!.GetValue<string>());
        Assert.Equal("description", rows[1]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Workbench_materializes_one_latest_publisher_record_per_work_and_language()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lex-enrichment-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "index-test.db");
        var output = Path.Combine(directory, "workbench.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE docs(
                      collection TEXT, group_key TEXT, group_identifier TEXT, language TEXT,
                      valid_from TEXT, withdrawn INTEGER, title TEXT, title_short TEXT, kind TEXT,
                      hierarchy TEXT, domains TEXT, act_form TEXT, binding_status TEXT,
                      consolidation_status TEXT, source_uri TEXT
                    );
                    INSERT INTO docs VALUES
                      ('eu-eurlex','32016r0679','32016R0679','fr','2016-05-04',0,'old title',NULL,
                       'REG','secondary_eu_law','|privacy|','regulation','in_force','published','https://old'),
                      ('eu-eurlex','32016r0679','32016R0679','fr','2018-01-01',0,'official title','GDPR',
                       'REG','secondary_eu_law','|privacy|','regulation','in_force','published','https://new'),
                      ('eu-eurlex','untitled','UNTITLED','fr','2020-01-01',0,NULL,NULL,
                       'REG',NULL,NULL,NULL,NULL,NULL,'https://untitled'),
                      ('eu-eurlex','withdrawn','X','fr','2020-01-01',1,'withdrawn',NULL,
                       'REG',NULL,NULL,NULL,NULL,NULL,'https://withdrawn');
                    """;
                command.ExecuteNonQuery();
            }

            EnrichmentWorkbench.Create([source], output);

            using var workbench = new SqliteConnection($"Data Source={output};Mode=ReadOnly;Pooling=False");
            workbench.Open();
            using var read = workbench.CreateCommand();
            read.CommandText = "SELECT work,language,title,title_short,latest_valid_from FROM publisher_works";
            using var row = read.ExecuteReader();
            Assert.True(row.Read());
            Assert.Equal("eu-eurlex:32016r0679", row.GetString(0));
            Assert.Equal("fr", row.GetString(1));
            Assert.Equal("official title", row.GetString(2));
            Assert.Equal("GDPR", row.GetString(3));
            Assert.Equal("2018-01-01", row.GetString(4));
            Assert.True(row.Read());
            Assert.Equal("eu-eurlex:untitled", row.GetString(0));
            Assert.True(row.IsDBNull(2));
            Assert.False(row.Read());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static EnrichmentProposal Proposal(string kind, string value) => new(
        Work: "eu-eurlex:32016r0679",
        Language: "fr",
        Kind: kind,
        Value: value,
        Evidence: [new EvidenceAnchor("eu-eurlex:32016r0679", "2016-05-04", "art_1", EvidenceSha)],
        ModelDeployment: "gpt-5-mini",
        PromptHash: EvidenceSha,
        Confidence: 0.9,
        RepeatRuns: 1,
        AgreementRatio: 1,
        ReviewedBy: null);

    private static JsonObject ValidJson() => new()
    {
        ["work"] = "eu-eurlex:32016r0679",
        ["language"] = "fr",
        ["kind"] = "description",
        ["value"] = "protection des données",
        ["evidence"] = new JsonArray(new JsonObject
        {
            ["work"] = "eu-eurlex:32016r0679",
            ["version"] = "2016-05-04",
            ["anchor"] = "art_1",
            ["text_sha256"] = EvidenceSha,
        }),
        ["model_deployment"] = "gpt-5-mini",
        ["prompt_hash"] = EvidenceSha,
        ["confidence"] = 0.9,
        ["repeat_runs"] = 2,
        ["agreement_ratio"] = 1,
    };
}
