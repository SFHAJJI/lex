using System.Text.Json;
using Lex.Law;
using Lex.Sources.EurLex;

namespace Lex.Tests;

public sealed class EurLexScopeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-eu-scope-{Guid.NewGuid():N}");

    public EurLexScopeTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Reviewed_scope_keeps_languages_histories_and_waves_explicit()
    {
        var scope = EurLexScopeConfig.Load();

        Assert.Equal("lex-eu-scope/1", scope.Schema);
        Assert.Equal(["en", "fr"], scope.Languages);
        Assert.True(scope.History.IncludeOriginal);
        Assert.True(scope.History.IncludeAllOfficialConsolidations);
        Assert.True(scope.History.IncludeUnamended);
        Assert.False(scope.History.ManufactureConsolidations);
        Assert.Equal(2, scope.ActiveDomains(1).Count());
        Assert.Contains(scope.Domains, d => d.Id == "financial-services" && d.Wave == 2);
        Assert.Contains(scope.Exclusions, e => e.Kind == "citation");
    }

    [Fact]
    public void Enabling_synthetic_consolidation_is_rejected()
    {
        var path = Path.Combine(_dir, "unsafe.json");
        var safe = EurLexScopeConfig.Load();
        var unsafeScope = safe with { History = safe.History with { ManufactureConsolidations = true } };
        File.WriteAllText(path, JsonSerializer.Serialize(unsafeScope,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        Assert.Throws<InvalidDataException>(() => EurLexScopeConfig.Load(path));
    }

    [Theory]
    [InlineData("true", "in_force")]
    [InlineData("1", "in_force")]
    [InlineData("false", "not_in_force")]
    [InlineData("0", "not_in_force")]
    [InlineData(null, "unknown")]
    [InlineData("publisher-specific", "unknown")]
    public void Publisher_binding_status_is_normalized_for_search_filters(string? source, string expected)
    {
        Assert.Equal(expected, EurLexAdapter.NormalizeBindingStatus(source));
    }

    [Fact]
    public async Task Original_expressions_skip_the_incompatible_consolidation_manifestation()
    {
        var date = new DateOnly(2024, 1, 1);
        var identifier = new Identifier("http://publications.europa.eu/resource/celex/32024R0001");
        var expression = new ExpressionRecord(
            "en",
            date,
            null,
            "publisher",
            "Test regulation",
            "Test regulation",
            "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32024R0001");
        var version = new VersionRecord(
            identifier,
            identifier,
            "REG",
            date,
            null,
            "publisher",
            "true",
            date,
            [expression],
            [],
            new Dictionary<string, string>
            {
                ["celex"] = "32024R0001",
                ["consolidation_status"] = "not_published_or_not_required",
            });

        var result = await new EurLexAdapter().FetchAltManifestation(version, expression, default);

        Assert.Null(result);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
