using System.Security.Cryptography;
using System.Text;
using Lex.Index;

namespace Lex.Tests;

public sealed class WorkSearchTests : IDisposable
{
    private static readonly string EnrichmentDigest = new('e', 64);
    private readonly List<string> _files = [];

    private sealed class TestEncoder : ITextEncoder
    {
        public string ModelId => "test/work-search";
        public string ModelRevision => "1";
        public int Dimensions => 8;
        public int CountTokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 2;
        public int PrefixLengthForTokens(string text, int maxTokens) => text.Length;
        public int SuffixStartForTokens(string text, int maxTokens) => 0;
        public float[] Encode(string text, EmbeddingInputKind kind)
        {
            var result = new float[Dimensions];
            foreach (var character in text) result[character % Dimensions]++;
            var norm = MathF.Sqrt(result.Sum(value => value * value));
            for (var index = 0; index < result.Length; index++) result[index] /= norm;
            return result;
        }
        public IReadOnlyList<float[]> EncodeBatch(
            IReadOnlyList<string> texts, EmbeddingInputKind kind, int? padToTokens = null) =>
            texts.Select(text => Encode(text, kind)).ToArray();
        public void Dispose() { }
    }

    [Theory]
    [InlineData("Règlement Général (RGPD)", "reglement general rgpd")]
    [InlineData("  AI-Act / IA  ", "ai act ia")]
    public void Work_names_are_normalized_without_accents_or_punctuation(string value, string expected) =>
        Assert.Equal(expected, WorkSearch.Normalize(value));

    [Fact]
    public void Reviewed_alias_collision_is_rejected_at_build_time()
    {
        var db = TempDb();
        var first = Doc("eu:32016r0679:2016-05-04", "32016r0679", "General Data Protection Regulation");
        var second = Doc("eu:32022r2554:2022-12-27", "32022r2554", "Digital Operational Resilience Act");
        var aliases = new[]
        {
            Alias("32016r0679", "fr", "RGPD"),
            Alias("32022r2554", "fr", "R.G.P.D."),
        };

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            db, Stamp(), [first, second], [], [], [], null,
            workSearch: new WorkSearchBuildOptions(aliases, [], EnrichmentDigest)));

        Assert.Contains("alias collision", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reviewed_alias_inside_a_long_query_pins_the_base_work_before_its_corrigendum()
    {
        var db = TempDb();
        var regulation = Doc("eu:32016r0679:2016-05-04", "32016r0679",
            "General Data Protection Regulation");
        var corrigendum = Doc("eu:32016r0679r(02):2018-05-23", "32016r0679r(02)",
            "Rectificatif au règlement (UE) 2016/679");
        var provisions = new[]
        {
            Provision(regulation, "Protection des personnes physiques à l'égard du traitement des données."),
            Provision(corrigendum, "Rectificatif au règlement relatif à la protection des données."),
        };

        IndexBuilder.Build(db, Stamp(), [regulation, corrigendum], provisions, [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Règlement Général sur la Protection des Données (RGPD)",
            FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("32016r0679", result.Hits[0].Doc.GroupKey);
        Assert.Contains("contained_alias", result.Hits[0].MatchReasons);
        Assert.Equal(EnrichmentDigest, reader.Stamp["enrichment_digest"]);
        Assert.Empty(result.QueryExpansions);
    }

    [Fact]
    public void Weak_discovery_improves_recall_but_does_not_outrank_direct_legal_wording()
    {
        var db = TempDb();
        var direct = Doc("eu:direct:2020-01-01", "direct", "Direct evidence act");
        var tagged = Doc("eu:tagged:2020-01-01", "tagged", "Unrelated formal title");
        IndexBuilder.Build(db, Stamp(), [direct, tagged],
            [Provision(direct, "This law regulates photovoltaic procurement."),
             Provision(tagged, "This law regulates reporting obligations.")], [], [], null,
            workSearch: new WorkSearchBuildOptions([], [
                new WorkDiscoveryRow("tagged", "fr", "concept", "photovoltaic procurement",
                    "test-model", new string('a', 64), new string('b', 64),
                    "2026-08-08T00:00:00Z", 0.91, 3, 1.0,
                    [new WorkEvidenceAnchor(tagged.Key, "art_1",
                        Provision(tagged, "This law regulates reporting obligations.").TextSha)])
            ], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("photovoltaic procurement", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("direct", result.Hits[0].Doc.GroupKey);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "tagged"
            && hit.MatchReasons.Contains("work_discovery"));
    }

    [Fact]
    public void Enrichment_cannot_change_the_authoritative_content_digest()
    {
        var plainDb = TempDb();
        var enrichedDb = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        var provision = Provision(doc, "Authoritative publisher wording.");

        IndexBuilder.Build(plainDb, Stamp(), [doc], [provision], [], [], null);
        IndexBuilder.Build(enrichedDb, Stamp(), [doc], [provision], [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var plain = LexIndexReader.Open(plainDb);
        using var enriched = LexIndexReader.Open(enrichedDb);
        Assert.Equal(plain.Stamp["content_digest"], enriched.Stamp["content_digest"]);
    }

    [Fact]
    public void Earlier_v3_indexes_without_the_optional_work_catalog_still_mount()
    {
        var db = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Personal data protection.")],
            [], [], null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE work_fts;
                DROP TABLE work_discovery;
                DROP TABLE work_names;
                DROP TABLE work_records;
                """;
            command.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("32016R0679", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("32016r0679", Assert.Single(result.Hits).Doc.GroupKey);
        Assert.Contains("exact_identifier", result.Hits[0].MatchReasons);
    }

    [Fact]
    public void Hybrid_lookup_keeps_a_contained_reviewed_alias_deterministic()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var regulation = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        var neighbour = Doc("eu:32019r2175:2019-12-27", "32019r2175", "Amending regulation");
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [regulation, neighbour],
            [Provision(regulation, "Personal data protection."),
             Provision(neighbour, "Amends several data protection rules.")], [], [], null,
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"),
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db, encoder, vectors);
        var result = reader.SearchHybrid("show me RGPD", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("keyword", result.RetrievalMode);
        Assert.Equal("32016r0679", result.Hits[0].Doc.GroupKey);
        Assert.Contains("contained_alias", result.Hits[0].MatchReasons);
    }

    private string TempDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-work-search-{Guid.NewGuid():N}.db");
        _files.Add(path);
        return path;
    }

    private string TempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-work-search-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    private static Dictionary<string, string> Stamp() => new()
    {
        ["collection"] = "eu",
        ["jurisdiction"] = "EU",
        ["built_at"] = "2026-08-08T00:00:00Z",
        ["corpus_commit"] = "test",
    };

    private static ReviewedWorkAliasRow Alias(string work, string language, string value) =>
        new(work, language, value, "test-reviewer");

    private static DocRow Doc(string key, string work, string title) => new(
        key, "eu", work, $"urn:celex:{work}", "REG", "fr", key[^10..], null,
        "official_consolidation_state", "2026-08-08T00:00:00Z", false, true, true,
        "record-sha", null, "https://example.invalid", title, title, null, key[^10..], null);

    private static ProvisionRow Provision(DocRow doc, string text)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new ProvisionRow($"{doc.Key}|{doc.Language}|{doc.ValidFrom}", 0, "art_1",
            $"{doc.Key}#art_1", "article", "1", null, null, null, doc.Title, text, sha);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in _files)
            try { File.Delete(file); } catch { /* temporary test artifact */ }
    }
}
