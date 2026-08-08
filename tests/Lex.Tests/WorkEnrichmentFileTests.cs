using System.Security.Cryptography;
using System.Text;
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

    private string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-enrichment-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _files)
            try { File.Delete(file); } catch { /* temporary test artifact */ }
    }
}
