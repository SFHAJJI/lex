using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Derive;
using Lex.Temporal;

namespace Lex.Tests;

public sealed class AknLuProfileV3Tests
{
    private const string LexId = "lu-legilux:synthetic-marker:2025-01-01";

    [Fact]
    public void Structurally_bounded_amendment_note_becomes_a_marker_only_gap()
    {
        var result = StructuredTextExtractor.Extract(
            Document(MarkerListItem("1. loi modifiee du 9 juillet 2004")),
            LexId,
            enableAknLuV3: true);

        Assert.Equal(AknLuProfileV3.ProfileId, result.ProfileId);
        Assert.Empty(result.Extraction.Provisions);
        var gap = Assert.Single(result.Extraction.ProvisionGaps ?? []);
        Assert.Equal("art_2", gap.Anchor);
        Assert.Equal("Art. 2.", gap.Num);
        Assert.Equal("article", gap.Type);
        Assert.Equal(ProvisionGapReason.MarkerOnly, gap.TextUnavailableReason);
        Assert.Equal(0, gap.DocumentOrder);
        Assert.DoesNotContain("loi modifiee", result.Extraction.Markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Marker_shape_with_unrecognised_note_is_quarantined_as_suspicious()
    {
        var result = StructuredTextExtractor.Extract(
            Document(MarkerListItem("Publisher annotation without a bounded date")),
            LexId,
            enableAknLuV3: true);

        Assert.Equal(AknLuProfileV3.ProfileId, result.ProfileId);
        var gap = Assert.Single(result.Extraction.ProvisionGaps ?? []);
        Assert.Equal(ProvisionGapReason.MarkerSuspicious, gap.TextUnavailableReason);
        Assert.DoesNotContain("Publisher annotation", result.Extraction.Markdown,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<mod/>")]
    [InlineData("<mod class=\"source\"/>")]
    [InlineData("<mod for=\"item\"/>")]
    [InlineData("<mod class=\"source\" for=\"item\">text</mod>")]
    [InlineData("<mod class=\"source\" for=\"item\" extra=\"x\"/>")]
    [InlineData("<mod class=\"source\" for=\"item\"/><mod class=\"source\" for=\"item\"/>")]
    [InlineData("<mod class=\"source\" for=\"item\"/><noteRef href=\"#M1\" marker=\"1\"/>")]
    public void Malformed_mod_token_cannot_prove_marker_only_even_when_the_note_text_matches(
        string marker)
    {
        var result = StructuredTextExtractor.Extract(
            Document("<li><ref href=\"https://publisher.example/act\">"
                + $"1. loi modifiee du 9 juillet 2004</ref>{marker}"
                + "<noteRef href=\"#M1\" marker=\"1\"/></li>"),
            LexId,
            enableAknLuV3: true);

        var gap = Assert.Single(result.Extraction.ProvisionGaps ?? []);
        Assert.Equal(ProvisionGapReason.MarkerSuspicious, gap.TextUnavailableReason);
        Assert.Empty(result.Extraction.Provisions);
    }

    [Theory]
    [InlineData("<mod class=\"source\" for=\"item\"/><ref href=\"https://publisher.example/act\">1. loi modifiee du 9 juillet 2004</ref><noteRef href=\"#M1\" marker=\"1\"/>")]
    [InlineData("<ref href=\"https://publisher.example/act\">1. loi modifiee du 9 juillet 2004</ref><mod class=\"source\" for=\"item\"/><noteRef href=\"#M2\" marker=\"1\"/>")]
    [InlineData("<ref href=\"https://publisher.example/act\" extra=\"x\">1. loi modifiee du 9 juillet 2004</ref><mod class=\"source\" for=\"item\"/><noteRef href=\"#M1\" marker=\"1\"/>")]
    public void Unbounded_source_marker_shapes_are_quarantined(string content)
    {
        var result = StructuredTextExtractor.Extract(
            Document($"<li>{content}</li>"), LexId, enableAknLuV3: true);

        Assert.Equal(ProvisionGapReason.MarkerSuspicious,
            Assert.Single(result.Extraction.ProvisionGaps ?? [])
                .TextUnavailableReason);
        Assert.Empty(result.Extraction.Provisions);
    }

    [Fact]
    public void Source_marker_hidden_inside_unknown_rich_markup_is_quarantined()
    {
        const string item = """
            <li><p><ref href="https://publisher.example/act">1. loi modifiee du 9 juillet 2004</ref>
              <mod class="source" for="item"/><noteRef href="#M1" marker="1"/></p></li>
            """;
        var result = StructuredTextExtractor.Extract(
            Document(item), LexId, enableAknLuV3: true);

        Assert.Equal(ProvisionGapReason.MarkerSuspicious,
            Assert.Single(result.Extraction.ProvisionGaps ?? [])
                .TextUnavailableReason);
        Assert.Empty(result.Extraction.Provisions);
    }

    [Fact]
    public void Text_pattern_without_publisher_marker_structure_remains_ordinary_akn_lu_2()
    {
        var xml = Document(
            "<li><ref href=\"https://publisher.example/act\">1. loi modifiee du 9 juillet 2004</ref></li>");
        var frozen = StructuredTextExtractor.Extract(xml, LexId);
        var candidate = StructuredTextExtractor.Extract(xml, LexId, enableAknLuV3: true);

        Assert.Equal(AknLuProfileV2.ProfileId, candidate.ProfileId);
        Assert.Empty(candidate.Extraction.ProvisionGaps ?? []);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(frozen),
            JsonSerializer.SerializeToUtf8Bytes(candidate));
        Assert.Contains("loi modifiee", Assert.Single(candidate.Extraction.Provisions).TextMd,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rich_modification_structure_is_not_mistaken_for_a_source_note()
    {
        const string item = """
            <li><p>Replacement wording follows.</p><mod class="substitution" for="x">
              <quotedText><embeddedStructure><alinea><content><p>Substantive wording.</p></content></alinea></embeddedStructure></quotedText>
            </mod></li>
            """;

        var candidate = StructuredTextExtractor.Extract(
            Document(item), LexId, enableAknLuV3: true);

        Assert.Equal(AknLuProfileV2.ProfileId, candidate.ProfileId);
        Assert.Empty(candidate.Extraction.ProvisionGaps ?? []);
        Assert.Single(candidate.Extraction.Provisions);
    }

    [Fact]
    public void One_marker_item_quarantines_the_whole_provision_not_a_partial_quote()
    {
        var result = StructuredTextExtractor.Extract(
            Document("<li><p>Apparently safe fragment.</p></li>" +
                MarkerListItem("1. loi du 30 novembre 2022")),
            LexId,
            enableAknLuV3: true);

        Assert.Empty(result.Extraction.Provisions);
        Assert.Single(result.Extraction.ProvisionGaps ?? []);
        Assert.DoesNotContain("Apparently safe fragment", result.Extraction.Markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Suspicious_candidate_dominates_a_proven_candidate_in_the_same_provision()
    {
        var result = StructuredTextExtractor.Extract(
            Document(MarkerListItem("1. loi du 30 novembre 2022")
                + MarkerListItem("Unrecognised publisher annotation")),
            LexId,
            enableAknLuV3: true);

        Assert.Equal(ProvisionGapReason.MarkerSuspicious,
            Assert.Single(result.Extraction.ProvisionGaps ?? []).TextUnavailableReason);
    }

    [Fact]
    public void Mixed_document_retains_safe_text_and_reports_partial_completeness()
    {
        var xml = $$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="art_1"><num>Art. 1.</num><alinea><content><p>Safe synthetic wording.</p></content></alinea></article>
              <article id="art_2"><num>Art. 2.</num><alinea><content><ol>{{MarkerListItem("1. loi modifiee du 9 juillet 2004")}}</ol></content></alinea></article>
            </body></act></akomaNtoso>
            """;

        var result = StructuredTextExtractor.Extract(xml, LexId, enableAknLuV3: true);

        Assert.Equal(AknLuProfileV3.ProfileId, result.ProfileId);
        Assert.Equal("partial", result.Extraction.TextCompleteness);
        Assert.Equal("art_1", Assert.Single(result.Extraction.Provisions).Anchor);
        Assert.Equal("art_2", Assert.Single(result.Extraction.ProvisionGaps ?? []).Anchor);
        Assert.Contains("Safe synthetic wording", result.Extraction.Markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("loi modifiee", result.Extraction.Markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Gap_only_document_reports_unavailable_completeness()
    {
        var result = StructuredTextExtractor.Extract(
            Document(MarkerListItem("1. loi du 30 novembre 2022")),
            LexId,
            enableAknLuV3: true);

        Assert.Equal("unavailable", result.Extraction.TextCompleteness);
    }

    [Fact]
    public void Opt_in_derivation_writes_a_textless_typed_gap_and_no_marker_markdown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lex-canon2-{Guid.NewGuid():N}");
        var corpus = Path.Combine(root, "corpus");
        var articles = Path.Combine(root, "articles");
        try
        {
            Directory.CreateDirectory(Path.Combine(corpus, "works", "synthetic", "versions"));
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), """
                { "schema": "lex-corpus/5", "canon": "canon/1", "build_issues": [],
                  "ingester_code_commit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }
                """);
            var work = Path.Combine(corpus, "works", "synthetic");
            File.WriteAllText(Path.Combine(work, "meta.json"),
                new JsonObject { ["title"] = "Synthetic work" }.ToJsonString());
            var versionKey = VersionIdentity.Create(
                new DateOnly(2025, 1, 1), "publisher:synthetic");
            var version = Path.Combine(work, "versions", versionKey);
            Directory.CreateDirectory(version);
            const string markerText = "1. loi modifiee du 9 juillet 2004";
            File.WriteAllText(Path.Combine(version, "fr.xml"),
                Document(MarkerListItem(markerText)));
            File.WriteAllText(Path.Combine(version, "meta.json"), new JsonObject
            {
                ["lex_id"] = $"lu-legilux:synthetic:{versionKey}",
                ["publisher"] = "lu-legilux",
                ["publisher_version_identifier"] = "publisher:synthetic",
                ["valid_from"] = "2025-01-01",
                ["work_identifier"] = "publisher:synthetic-work",
                ["expressions"] = new JsonArray(new JsonObject
                {
                    ["language"] = "fr",
                    ["source_uri"] = "https://publisher.example/synthetic",
                    ["observations"] = new JsonArray(new JsonObject
                    {
                        ["file"] = "fr.xml",
                        ["sha256"] = new string('b', 64),
                        ["source_uri"] = "https://publisher.example/synthetic",
                    }),
                }),
            }.ToJsonString());

            var stats = DeriveWriter.Derive(
                corpus, articles, "lu-legilux",
                new string('c', 40), new string('d', 40), new string('e', 40),
                stagedFileWritten: null,
                enableAknLuV3: true);

            Assert.Empty(stats.Errors);
            Assert.Equal(1, stats.ProvisionGaps);
            var outputDirectory = Path.Combine(
                articles, "lu-legilux", "works", "synthetic", "versions", versionKey);
            var markdown = File.ReadAllText(Path.Combine(outputDirectory, "fr.md"));
            Assert.DoesNotContain(markerText, markdown, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(outputDirectory, "fr.json")));
            Assert.Equal("unavailable",
                json.RootElement.GetProperty("text_completeness").GetString());
            Assert.Empty(json.RootElement.GetProperty("provisions").EnumerateArray());
            var gap = Assert.Single(json.RootElement.GetProperty("provision_gaps").EnumerateArray());
            Assert.Equal("lex-provision-gap/1",
                gap.GetProperty("schema").GetString());
            Assert.Equal("art_2", gap.GetProperty("anchor").GetString());
            Assert.Equal(ProvisionGapReason.MarkerOnly,
                gap.GetProperty("text_unavailable_reason").GetString());
            Assert.False(gap.TryGetProperty("text_md", out _));
            Assert.False(gap.TryGetProperty("text_sha256", out _));
            Assert.False(gap.TryGetProperty("md_span", out _));
            Assert.False(gap.TryGetProperty("citations", out _));
            Assert.Equal(DerivationGeneration.Canon2,
                DerivationGeneration.ReadPublisher(
                    articles, "lu-legilux").ArticlesCanon);
            using var generation = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(articles, DerivationGeneration.FileName)));
            Assert.Equal(DerivationGeneration.SchemaId,
                generation.RootElement.GetProperty("schema").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string MarkerListItem(string note) =>
        $"<li><ref href=\"https://publisher.example/act\">{note}</ref>" +
        "<mod class=\"source\" for=\"item\"/><noteRef href=\"#M1\" marker=\"1\"/></li>";

    private static string Document(string listItems) => $$"""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
          <article id="art_2"><num>Art. 2.</num><alinea><content><ol>{{listItems}}</ol></content></alinea></article>
        </body></act></akomaNtoso>
        """;
}
