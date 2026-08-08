using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Lex.Ingest;

namespace Lex.Tests;

public sealed class WorkEnrichmentFileTests : IDisposable
{
    private readonly List<string> _files = [];

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
    }
}
