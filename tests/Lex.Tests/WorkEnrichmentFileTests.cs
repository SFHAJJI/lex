using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Lex.Ingest;

namespace Lex.Tests;

public sealed class WorkEnrichmentFileTests : IDisposable
{
    private readonly List<string> _files = [];
    private readonly List<string> _directories = [];

    private static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Lex.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return root!;
    }

    private static string ProductionArtifact() =>
        Path.Combine(RepoRoot(), "config", "eu-work-enrichment.json");

    [Fact]
    public void Production_eu_aliases_are_reviewed_data_not_model_discovery()
    {
        var options = WorkEnrichmentFile.Load(ProductionArtifact(), "eu-eurlex");

        Assert.Equal(31, options.ReviewedAliases.Count);
        Assert.Empty(options.Discovery);
        Assert.Contains(options.ReviewedAliases, alias =>
            alias.Work == "32016r0679" && alias.Language == "fr" && alias.Value == "RGPD");
        Assert.All(options.ReviewedAliases,
            alias => Assert.StartsWith("repository-review:", alias.ReviewedBy, StringComparison.Ordinal));
    }

    [Fact]
    public void Production_weak_enrichment_decision_is_bound_to_empty_discovery_and_frozen_cases()
    {
        var root = RepoRoot();
        var decision = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, "evals", "weak-enrichment-decision.json")))!.AsObject();
        Assert.Equal("lex-weak-enrichment-decision/1", decision["schema"]!.GetValue<string>());
        Assert.Equal("eu-eurlex", decision["collection"]!.GetValue<string>());
        Assert.Equal("config/eu-work-enrichment.json",
            decision["enrichment_file"]!.GetValue<string>());
        Assert.Equal("evals/retrieval-cases.json", decision["cases_file"]!.GetValue<string>());
        string DeclaredPath(string property) => Path.Combine(root,
            decision[property]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
        var enrichmentPath = DeclaredPath("enrichment_file");
        var casesPath = DeclaredPath("cases_file");
        var enrichment = WorkEnrichmentFile.Load(enrichmentPath, "eu-eurlex");
        var casesSha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(casesPath)));

        Assert.Empty(enrichment.Discovery);
        Assert.Equal(0, decision["discovery_records"]!.GetValue<int>());
        Assert.Equal("not_eligible", decision["decision"]!.GetValue<string>());
        Assert.False(decision["production_activation"]!.GetValue<bool>());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(enrichmentPath))),
            decision["enrichment_sha256"]!.GetValue<string>());
        Assert.Equal(casesSha, decision["cases_sha256"]!.GetValue<string>());
    }

    [Fact]
    public void Production_aliases_are_filtered_to_the_held_scope_and_language()
    {
        var held = new HashSet<(string Work, string Language)>
        {
            ("32016r0679", "en"),
        };

        var options = WorkEnrichmentFile.Load(
            ProductionArtifact(), "eu-eurlex", held);

        var alias = Assert.Single(options.ReviewedAliases);
        Assert.Equal("32016r0679", alias.Work);
        Assert.Equal("en", alias.Language);
        Assert.Equal("GDPR", alias.Value);
        Assert.Empty(options.Discovery);
    }

    [Fact]
    public void Every_production_alias_exactly_resolves_its_reviewed_work()
    {
        var options = WorkEnrichmentFile.Load(ProductionArtifact(), "eu-eurlex");
        var collisions = options.ReviewedAliases
            .GroupBy(alias => (alias.Language, Value: WorkSearch.Normalize(alias.Value)))
            .Where(group => group.Select(alias => alias.Work).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToArray();
        Assert.Empty(collisions);
        var docs = options.ReviewedAliases.Select(alias => (alias.Work, alias.Language))
            .Distinct().Select(pair => new DocRow(
                $"eu-eurlex:{pair.Work}:2024-01-01", "eu-eurlex", pair.Work,
                $"urn:celex:{pair.Work}", "REG", pair.Language, "2024-01-01", null,
                "official_consolidation_state", "2026-08-08T00:00:00Z", false, true, true,
                "record-sha", null, "https://example.invalid", $"Official title {pair.Work}",
                null, null, "2024-01-01", null)).ToArray();
        var db = TempPath();
        IndexBuilder.Build(db, new Dictionary<string, string>
        {
            ["collection"] = "eu-eurlex",
            ["jurisdiction"] = "EU",
            ["built_at"] = "2026-08-08T00:00:00Z",
            ["corpus_commit"] = "test",
        }, docs, [], [], [], null, workSearch: options);

        using var reader = LexIndexReader.Open(db);
        foreach (var alias in options.ReviewedAliases)
        {
            var scoped = reader.SearchKeyword(alias.Value,
                FilterSet.All with { Language = alias.Language }, 10, false);
            Assert.Equal([alias.Work], scoped.QueryPlan!.WorkConstraints);
            Assert.Contains(scoped.Hits, hit => hit.Doc.GroupKey == alias.Work
                && hit.MatchReasons.Contains("exact_alias"));

            var unscoped = reader.SearchKeyword(alias.Value, FilterSet.All, 10, false);
            Assert.Equal([alias.Work], unscoped.QueryPlan!.WorkConstraints);
            Assert.All(unscoped.Hits.Where(hit => hit.MatchReasons.Contains("exact_alias")),
                hit => Assert.Equal(alias.Work, hit.Doc.GroupKey));
        }
    }

    [Fact]
    public void Reviewed_aliases_refuse_a_pre_migration_corpus()
    {
        var corpus = Path.Combine(Path.GetTempPath(), $"lex-stale-corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(corpus);
        _directories.Add(corpus);
        var manifest = new ManifestDoc
        {
            Publisher = new Dictionary<string, string> { ["id"] = "eu-eurlex" },
            Tier = "A",
            Attribution = "test",
            TextIncluded = true,
            TextPublic = true,
            HistoryBegins = "2024-01-01",
            IngesterVersion = "0.1.0",
        };
        File.WriteAllText(Path.Combine(corpus, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, CorpusJson.Options));

        var error = Assert.Throws<InvalidDataException>(() => IndexFromCorpus.Build(
            corpus, null, TempPath(), null, DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
            workEnrichmentPath: ProductionArtifact()));

        Assert.Contains("Re-ingest", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_file_is_hashed_and_scoped_to_one_publisher()
    {
        var path = Write("""
            {
              "schema": "lex-work-enrichment/1",
              "aliases": [
                {
                  "collection": "eu-eurlex",
                  "work": "32016r0679",
                  "language": "fr",
                  "value": "RGPD",
                  "reviewed_by": "sfhajji"
                },
                {
                  "collection": "lu-legilux",
                  "work": "loi-2018-08-01-a686",
                  "language": "fr",
                  "value": "Loi CNPD",
                  "reviewed_by": "sfhajji"
                }
              ],
              "discovery": [
                {
                  "collection": "eu-eurlex",
                  "work": "32016r0679",
                  "language": "fr",
                  "kind": "concept",
                  "value": "notification des violations de données",
                  "model_deployment": "gpt-5-mini",
                  "prompt_sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "schema_sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "generated_at": "2026-08-08T00:00:00Z",
                  "confidence": 0.92,
                  "repeat_runs": 3,
                  "agreement_ratio": 1.0,
                  "evidence": [
                    {
                      "version": "eu-eurlex:32016r0679:2016-05-04",
                      "anchor": "art_33",
                      "text_sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                    }
                  ]
                }
              ]
            }
            """);

        var options = WorkEnrichmentFile.Load(path, "eu-eurlex");

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            options.EnrichmentDigest);
        Assert.Equal("RGPD", Assert.Single(options.ReviewedAliases).Value);
        Assert.Equal("notification des violations de données", Assert.Single(options.Discovery).Value);
    }

    [Fact]
    public void Unknown_fields_are_rejected_instead_of_silently_ignored()
    {
        var path = Write("""
            {
              "schema": "lex-work-enrichment/1",
              "aliases": [],
              "discovery": [],
              "authoritative_legal_status": "invented"
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            WorkEnrichmentFile.Load(path, "eu-eurlex"));

        Assert.Contains("could not be parsed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reviewed_artifact_production_is_byte_repeatable_and_loadable()
    {
        var input = Write("""
            {
              "schema": "lex-work-enrichment/1",
              "aliases": [
                {
                  "collection": "eu-eurlex",
                  "work": "32022r2554",
                  "language": "en",
                  "value": "DORA",
                  "reviewed_by": "reviewer"
                },
                {
                  "collection": "eu-eurlex",
                  "work": "32016r0679",
                  "language": "fr",
                  "value": "RGPD",
                  "reviewed_by": "reviewer"
                }
              ],
              "discovery": []
            }
            """);
        var first = TempPath();
        var second = TempPath();

        WorkEnrichmentFile.BuildReviewedArtifact(input, first, "eu-eurlex");
        WorkEnrichmentFile.BuildReviewedArtifact(input, second, "eu-eurlex");

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.EndsWith("\n", File.ReadAllText(first), StringComparison.Ordinal);
        var loaded = WorkEnrichmentFile.Load(first, "eu-eurlex");
        Assert.Equal(["RGPD", "DORA"], loaded.ReviewedAliases.Select(alias => alias.Value));
        Assert.Empty(loaded.Discovery);
    }

    [Fact]
    public void Reviewed_artifact_production_refuses_to_overwrite_an_output()
    {
        var input = Write("""
            {
              "schema": "lex-work-enrichment/1",
              "aliases": [],
              "discovery": []
            }
            """);
        var output = Write("keep me");

        Assert.Throws<IOException>(() =>
            WorkEnrichmentFile.BuildReviewedArtifact(input, output, "eu-eurlex"));
        Assert.Equal("keep me", File.ReadAllText(output));
    }

    [Fact]
    public void Produced_artifact_merges_only_when_its_evidence_is_held()
    {
        const string text = "The controller shall notify a personal data breach.";
        var textSha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var input = Write($$"""
            {
              "schema": "lex-work-enrichment/1",
              "aliases": [],
              "discovery": [
                {
                  "collection": "eu-eurlex",
                  "work": "32016r0679",
                  "language": "fr",
                  "kind": "concept",
                  "value": "notification de violation de donnees",
                  "model_deployment": "reviewed-model",
                  "prompt_sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "schema_sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "generated_at": "2026-08-08T00:00:00Z",
                  "confidence": 0.9,
                  "repeat_runs": 3,
                  "agreement_ratio": 1.0,
                  "evidence": [
                    {
                      "version": "eu-eurlex:32016r0679:2016-05-04",
                      "anchor": "art_33",
                      "text_sha256": "{{textSha}}"
                    }
                  ]
                }
              ]
            }
            """);
        var artifact = TempPath();
        var db = TempPath();
        WorkEnrichmentFile.BuildReviewedArtifact(input, artifact, "eu-eurlex");
        var options = WorkEnrichmentFile.Load(artifact, "eu-eurlex");
        var doc = new DocRow(
            "eu-eurlex:32016r0679:2016-05-04", "eu-eurlex", "32016r0679",
            "urn:celex:32016r0679", "REG", "fr", "2016-05-04", null,
            "official_consolidation_state", "2026-08-08T00:00:00Z", false, true, true,
            "record-sha", null, "https://example.invalid", "Reglement 2016/679", null,
            null, "2016-05-04", null);
        var provision = new ProvisionRow(
            $"{doc.Key}|fr|2016-05-04", 0, "art_33", $"{doc.Key}#art_33", "article",
            "33", null, null, null, doc.Title, text, textSha);

        IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "eu-eurlex",
                ["jurisdiction"] = "EU",
                ["built_at"] = "2026-08-08T00:00:00Z",
                ["corpus_commit"] = "test",
            }, [doc], [provision], [], [], null, workSearch: options);

        using var reader = LexIndexReader.Open(db);
        Assert.Equal(options.EnrichmentDigest, reader.Stamp["enrichment_digest"]);
    }

    private string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-enrichment-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _files.Add(path);
        return path;
    }

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-enrichment-{Guid.NewGuid():N}.json");
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _files)
            try { File.Delete(file); } catch { /* temporary test artifact */ }
        foreach (var directory in _directories)
            try { Directory.Delete(directory, recursive: true); } catch { /* temporary test corpus */ }
    }
}
