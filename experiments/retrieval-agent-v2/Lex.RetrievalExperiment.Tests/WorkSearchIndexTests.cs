using System.Security.Cryptography;
using Lex.Index;
using Lex.RetrievalExperiment;

namespace Lex.RetrievalExperiment.Tests;

public sealed class WorkSearchIndexTests
{
    [Fact]
    public void Reviewed_alias_and_discovery_find_the_base_act_while_rectificatif_remains_findable()
    {
        var path = Path.Combine(Path.GetTempPath(), "lex-work-search-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            WorkSearchIndex.Create(path, Sources(), Discoveries(), Aliases());

            var alias = WorkSearchIndex.Search(path, "RGPD", "fr", 10);
            var fullName = WorkSearchIndex.Search(path,
                "Règlement Général sur la Protection des Données (RGPD)", "fr", 10);
            var embeddedAlias = WorkSearchIndex.Search(path,
                "Que disait Article 33 du RGPD au 15 mars 2019 ?", "fr", 10);
            var descriptive = WorkSearchIndex.Search(path,
                "notifier à l'autorité une violation de données personnelles dans les 72 heures", "fr", 10);
            var rectificatif = WorkSearchIndex.Search(path,
                "rectificatif règlement UE 2016/679", "fr", 10);

            Assert.Equal("eu-eurlex:32016r0679", alias[0].Work);
            Assert.Equal("exact_alias", alias[0].Reason);
            Assert.Equal("eu-eurlex:32016r0679", fullName[0].Work);
            Assert.Equal("eu-eurlex:32016r0679", embeddedAlias[0].Work);
            Assert.Equal("contained_alias", embeddedAlias[0].Reason);
            Assert.Equal("eu-eurlex:32016r0679", descriptive[0].Work);
            Assert.Equal("eu-eurlex:32016r0679r(02)", rectificatif[0].Work);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Work_search_build_is_byte_deterministic_and_rejects_alias_collisions()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-work-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "a.db");
        var second = Path.Combine(root, "b.db");
        try
        {
            WorkSearchIndex.Create(first, Sources(), Discoveries(), Aliases());
            WorkSearchIndex.Create(second, Sources().Reverse().ToArray(), Discoveries(), Aliases());

            Assert.Equal(SHA256.HashData(File.ReadAllBytes(first)), SHA256.HashData(File.ReadAllBytes(second)));

            var collision = Aliases().Append(new ReviewedWorkAlias(
                "eu-eurlex:32016r0679r(02)", "fr", "RGPD", "owner:sfhajji")).ToArray();
            var error = Assert.Throws<InvalidDataException>(() =>
                WorkSearchIndex.Create(Path.Combine(root, "collision.db"), Sources(), Discoveries(), collision));
            Assert.Contains("collision", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hybrid_fusion_keeps_exact_names_pinned_and_can_recover_a_semantic_work()
    {
        var gdpr = new WorkSearchHit("eu-eurlex:32016r0679", "fr", "GDPR", "exact_alias", -1000);
        var noise = new WorkSearchHit("eu-eurlex:noise", "fr", "Noise", "work_fts", -20);
        var semanticGdpr = new WorkSemanticHit(gdpr.Work, gdpr.Language, gdpr.Title, 0.92);

        var pinned = WorkHybridSearch.Fuse([gdpr, noise], [new(noise.Work, "fr", "Noise", 0.99), semanticGdpr], 10);
        var lexicalGdpr = gdpr with { Reason = "work_fts", Score = -5 };
        var recovered = WorkHybridSearch.Fuse([noise, lexicalGdpr], [semanticGdpr], 10);

        Assert.Equal(gdpr.Work, pinned[0].Work);
        Assert.Equal("exact_alias", pinned[0].Reason);
        Assert.Equal(gdpr.Work, recovered[0].Work);
        Assert.Equal("keyword+semantic_work", recovered[0].Reason);

        var concept = WorkHybridSearch.Fuse([], [
            semanticGdpr with { EvidenceKind = "concept", EvidenceValue = "notification des violations" },
        ], 10);
        var workMeaning = WorkHybridSearch.Fuse([], [semanticGdpr], 10);
        Assert.Equal("semantic_concept", concept[0].Reason);
        Assert.True(concept[0].Score < workMeaning[0].Score);
    }

    [Fact]
    public void Work_vectors_keep_each_discovery_concept_separate_from_the_base_work_vector()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-work-vectors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var index = Path.Combine(root, "work.db");
        var vectors = Path.Combine(root, "work.vectors");
        try
        {
            WorkSearchIndex.Create(index, Sources(), Discoveries(), Aliases());
            using var encoder = new ConstantEncoder();

            var build = WorkVectorIndex.Build(index, vectors, encoder);

            Assert.Equal(Sources().Length + Discoveries().Length, build.Count);
            using var reader = new SemanticVectorReader(vectors);
            Assert.Equal(build.Count, reader.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("rectificatif règlement UE 2016/679", "corrigendum")]
    [InlineData("GDPR", null)]
    public void Query_intent_detects_only_an_explicit_document_role(string query, string? expected) =>
        Assert.Equal(expected, WorkQueryIntent.DocumentRole(EnrichmentContract.Normalize(query)));

    [Theory]
    [InlineData("Que disait l'article 33 du RGPD ?", "art_33")]
    [InlineData("Read Art. 6 today", "art_6")]
    [InlineData("RGPD notification violation", null)]
    public void Query_intent_preserves_only_an_explicit_article_reference(string query, string? expected) =>
        Assert.Equal(expected, WorkQueryIntent.ArticleAnchor(query));

    [Fact]
    public void Query_intent_requires_two_queries_to_agree_before_promoting_an_inferred_article()
    {
        Assert.Equal("art_33", WorkQueryIntent.ConsensusArticleAnchor(
            ["article 33 notification", "Article 33 breach notification", "article 34 communication"]));
        Assert.Null(WorkQueryIntent.ConsensusArticleAnchor(
            ["article 5 principles", "article 6 lawfulness", "article 15 access"]));
    }

    private static WorkSearchSource[] Sources() =>
    [
        new("eu-eurlex:32016r0679", "fr", "32016r0679", "32016R0679",
            "Règlement (UE) 2016/679 du Parlement européen et du Conseil", "GDPR",
            "secondary_eu_law", "protection des données", "regulation", "REG"),
        new("eu-eurlex:32016r0679r(02)", "fr", "32016r0679r(02)", "32016R0679R(02)",
            "Rectificatif au règlement (UE) 2016/679 du Parlement européen et du Conseil", null,
            "secondary_eu_law", "protection des données", "regulation", "CORRIGENDUM"),
    ];

    private static WorkDiscovery[] Discoveries() =>
    [
        new("eu-eurlex:32016r0679", "fr", "concept",
            "notification des violations de données et communication aux personnes concernées"),
        new("eu-eurlex:32016r0679", "fr", "concept",
            "protection des données à caractère personnel"),
    ];

    private static ReviewedWorkAlias[] Aliases() =>
    [
        new("eu-eurlex:32016r0679", "fr", "RGPD", "owner:sfhajji"),
        new("eu-eurlex:32016r0679", "fr",
            "Règlement général sur la protection des données (RGPD)", "owner:sfhajji"),
    ];

    private sealed class ConstantEncoder : ITextEncoder
    {
        public string ModelId => "fake";
        public string ModelRevision => "1";
        public int Dimensions => 2;
        public int CountTokens(string text) => text.Length;
        public int PrefixLengthForTokens(string text, int maxTokens) => Math.Min(text.Length, maxTokens);
        public int SuffixStartForTokens(string text, int maxTokens) => Math.Max(0, text.Length - maxTokens);
        public float[] Encode(string text, EmbeddingInputKind kind) => [1, 0];
        public void Dispose() { }
    }
}
