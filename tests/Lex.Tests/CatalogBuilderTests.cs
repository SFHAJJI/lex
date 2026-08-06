using System.Text.Json.Nodes;
using Lex.Derive;
using Xunit;

namespace Lex.Tests;

public sealed class CatalogBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void Bilingual_wordings_have_independent_temporal_histories()
    {
        WriteExpression("2020-01-01", "en", "en-v1", "2020-12-31");
        WriteExpression("2020-01-01", "fr", "fr-stable", "2020-12-31");
        WriteExpression("2021-01-01", "en", "en-v2", null);
        WriteExpression("2021-01-01", "fr", "fr-stable", null);

        var stats = CatalogBuilder.Build(_root);

        Assert.Equal(1, stats.Works);
        Assert.Equal(3, stats.HistoryStates);
        var history = JsonNode.Parse(File.ReadAllText(Path.Combine(
            _root, "test-publisher", "works", "work-1", "history.json")))!;
        Assert.Equal("en", history["primary_language"]!.GetValue<string>());
        Assert.Equal(2, history["anchors_by_language"]!["en"]!["art_1"]!.AsArray().Count);
        Assert.Single(history["anchors_by_language"]!["fr"]!["art_1"]!.AsArray());
        Assert.Equal(2, history["anchors"]!["art_1"]!.AsArray().Count);
        Assert.Empty(history["anchor_events_by_language"]!["en"]!.AsArray());
        Assert.Empty(history["anchor_events_by_language"]!["fr"]!.AsArray());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temporary test data */ }
    }

    private void WriteExpression(string date, string language, string textSha, string? validTo)
    {
        var directory = Path.Combine(
            _root, "test-publisher", "works", "work-1", "versions", date);
        Directory.CreateDirectory(directory);
        var document = new JsonObject
        {
            ["lex_id"] = $"test-publisher:work-1:{date}",
            ["title"] = "Test instrument",
            ["valid_from"] = date,
            ["valid_to"] = validTo,
            ["generator"] = new JsonObject { ["profile"] = "test/1" },
            ["provisions"] = new JsonArray(new JsonObject
            {
                ["anchor"] = "art_1",
                ["text_sha256"] = textSha,
                ["article_valid_from"] = date,
            }),
        };
        File.WriteAllText(Path.Combine(directory, $"{language}.json"), document.ToJsonString());
    }
}
