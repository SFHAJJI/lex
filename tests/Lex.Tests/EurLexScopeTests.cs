using System.Text.Json;
using Lex.Law;
using Lex.Sources.EurLex;

namespace Lex.Tests;

public sealed class EurLexScopeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-eu-scope-{Guid.NewGuid():N}");

    public EurLexScopeTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Engineering_scope_keeps_languages_histories_and_waves_explicit()
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
        Assert.Contains(scope.Exclusions, e => e.Kind == "out_of_scope_language");
    }

    [Fact]
    public void Engineering_scope_reasons_never_become_corpus_legal_metadata()
    {
        var raw = EurLexAdapter.SourceRaw(
            "32016R0679", "REG", "in_force", "published");

        Assert.DoesNotContain("domains", raw.Keys);
        Assert.DoesNotContain("scope_reasons", raw.Keys);
        Assert.DoesNotContain("financial-services", raw.Values);
        Assert.Equal("32016R0679", raw["celex"]);
    }

    [Fact]
    public void Work_metadata_query_is_bounded_and_rejects_cap_plus_one()
    {
        var query = EurLexAdapter.WorkMetadataQuery(
            "(\"32016R0679\" <http://publications.europa.eu/resource/celex/32016R0679>)");
        var oversized = Enumerable.Range(0, 20_001)
            .Select(index => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["base"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList();

        Assert.Contains("LIMIT 20001", query, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(
            () => EurLexAdapter.RequireBoundedWorkMetadataRows(oversized));
        Assert.Contains("exceeds 20,000 records", error.Message, StringComparison.Ordinal);
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
    public void Pinned_cellar_rows_preserve_bilingual_discovery_metadata_without_splitting_short_titles()
    {
        var titles = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lang"] = "en",
                ["title_short"] = "gdpr, personal data, personal data protection",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lang"] = "fr",
                ["title_short"] = "rgdp, GDPR, Protection des données à caractère personnel",
            },
        };
        var subjects = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "eurovoc",
                ["identifier"] = "http://eurovoc.europa.eu/5181",
                ["lang"] = "en",
                ["label"] = "protection of personal data",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "directory",
                ["identifier"] = "http://publications.europa.eu/resource/authority/dir-eu-legal-act/152020",
                ["lang"] = "fr",
                ["label"] = "Protection des données à caractère personnel",
            },
        };

        var metadata = EurLexAdapter.BuildPublisherMetadata("32016R0679", titles, subjects);

        Assert.Contains(metadata, item => item.Kind == "publisher_short_title"
            && item.Language == "en"
            && item.Label == "gdpr, personal data, personal data protection");
        Assert.DoesNotContain(metadata, item => item.Kind == "publisher_short_title"
            && item.Label == "gdpr");
        Assert.Contains(metadata, item => item.Kind == "eurovoc"
            && item.Identifier == "http://eurovoc.europa.eu/5181"
            && item.Language == "en");
        Assert.Contains(metadata, item => item.Kind == "directory"
            && item.Language == "fr"
            && item.SourceUri == item.Identifier);
    }

    [Fact]
    public void Cellar_taxonomy_rows_preserve_alt_broader_subdomain_and_domain_as_weak_typed_metadata()
    {
        const string concept = "http://eurovoc.europa.eu/5181";
        var subjects = new[]
        {
            Subject("eurovoc", concept, "en", "data protection"),
            Subject("eurovoc_alt_label", concept, "en", "data breach"),
            Subject("eurovoc_broader", "http://eurovoc.europa.eu/2472", "en", "information policy"),
            Subject("eurovoc_subdomain", "http://eurovoc.europa.eu/100222", "en",
                "3231 information and information processing"),
            Subject("eurovoc_domain", "http://eurovoc.europa.eu/100150", "en",
                "32 EDUCATION AND COMMUNICATIONS"),
        };

        var metadata = EurLexAdapter.BuildPublisherMetadata("32016R0679", [], subjects);

        Assert.Equal(5, metadata.Count);
        Assert.Equal(subjects.Select(row => row["kind"]).Order(StringComparer.Ordinal),
            metadata.Select(item => item.Kind));
        Assert.All(metadata, item =>
        {
            Assert.Equal("en", item.Language);
            Assert.Equal(item.Identifier, item.SourceUri);
        });

        static Dictionary<string, string> Subject(
            string kind, string identifier, string language, string label) => new(StringComparer.Ordinal)
        {
            ["kind"] = kind, ["identifier"] = identifier,
            ["lang"] = language, ["label"] = label,
        };
    }

    [Fact]
    public void Cellar_taxonomy_rows_fail_closed_when_the_official_shape_is_unknown_or_incomplete()
    {
        static Dictionary<string, string> Row(
            string kind, string identifier, string? language = "en", string? label = "label")
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
                { ["kind"] = kind, ["identifier"] = identifier };
            if (language is not null) row["lang"] = language;
            if (label is not null) row["label"] = label;
            return row;
        }

        Assert.Throws<InvalidDataException>(() => EurLexAdapter.BuildPublisherMetadata(
            "32016R0679", [], [Row("invented", "https://example.test/concept")]));
        Assert.Throws<InvalidDataException>(() => EurLexAdapter.BuildPublisherMetadata(
            "32016R0679", [], [Row("eurovoc", "not-a-uri")]));
        Assert.Throws<InvalidDataException>(() => EurLexAdapter.BuildPublisherMetadata(
            "32016R0679", [], [Row("eurovoc", "https://example.test/concept", label: null)]));
    }

    [Fact]
    public void Cellar_resource_type_and_relationships_produce_only_controlled_document_roles()
    {
        Assert.Equal(["amending", "consolidated", "delegated"],
            EurLexAdapter.DocumentRoles(
                "http://publications.europa.eu/resource/authority/resource-type/REG_DEL",
                amending: true, correcting: false, consolidated: true));
        Assert.Equal(["corrigendum", "implementing"],
            EurLexAdapter.DocumentRoles(
                "http://publications.europa.eu/resource/authority/resource-type/REG_IMPL",
                amending: false, correcting: true, consolidated: false));
    }

    [Fact]
    public void Corpus_display_titles_are_derived_only_from_the_official_title()
    {
        var title = EurLexAdapter.OfficialDisplayTitle(
            "Regulation (EU) 2016/679 of the European Parliament and of the Council",
            "32016R0679");

        Assert.Equal("Regulation (EU) 2016/679", title);
        Assert.DoesNotContain("GDPR", title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("32016R0679", "32016r0679")]
    [InlineData("12012E/TXT", "12012e-txt")]
    public void Celex_identifiers_have_path_safe_stable_work_slugs(string celex, string expected)
    {
        Assert.Equal(expected, EurLexAdapter.NormalizeWorkSlug(celex));
    }

    [Theory]
    [InlineData("32016R0679", "https://publications.europa.eu/resource/celex/32016R0679")]
    [InlineData("12012E/TXT", "https://publications.europa.eu/resource/celex/12012E%2FTXT")]
    public void Cellar_resource_url_keeps_celex_as_one_encoded_path_segment(string celex, string expected)
    {
        Assert.Equal(expected, EurLexAdapter.CellarResourceUrl(celex));
    }

    [Theory]
    [InlineData("https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32006L0112R%2810%29", true)]
    [InlineData("https://publications.europa.eu/resource/celex/32006R1791", true)]
    [InlineData("http://eur-lex.europa.eu/legal-content/FR/TXT/", false)]
    [InlineData("https://europa.eu.example.org/legal-content/FR/TXT/", false)]
    [InlineData("https://example.org/", false)]
    public void Body_fallback_accepts_only_https_eu_institutional_hosts(string uri, bool accepted)
    {
        Assert.Equal(accepted, EurLexAdapter.OfficialEuUri(uri) is not null);
    }

    [Fact]
    public async Task Bounded_reader_accepts_the_limit_and_rejects_the_next_byte()
    {
        var exact = await EurLexAdapter.ReadBounded(new MemoryStream(new byte[8]), 8, default);
        var over = await EurLexAdapter.ReadBounded(new MemoryStream(new byte[9]), 8, default);

        Assert.False(exact.LimitExceeded);
        Assert.Equal(8, exact.Bytes?.Length);
        Assert.True(over.LimitExceeded);
        Assert.Null(over.Bytes);
        Assert.Equal(9, over.BytesRead);
    }

    [Fact]
    public void Sparql_alias_keeps_primary_celex_as_one_encoded_path_segment()
    {
        Assert.Equal("http://publications.europa.eu/resource/celex/12012E%2FTXT",
            EurLexAdapter.CelexAliasUri("12012E/TXT"));
    }

    [Fact]
    public void Relationship_closure_requires_an_expression_in_the_reviewed_languages()
    {
        var query = EurLexAdapter.RelationshipClosureQuery(
            ["32016R0679"],
            ["resource_legal_corrects_resource_legal"],
            ["en", "fr"]);

        Assert.Contains("cdm:expression_belongs_to_work ?related", query);
        Assert.Contains("cdm:expression_uses_language ?relatedLanguage", query);
        Assert.Contains("/language/ENG>", query);
        Assert.Contains("/language/FRA>", query);
        Assert.DoesNotContain("/language/DEU>", query);
    }

    [Fact]
    public void Original_state_is_kept_only_when_it_extends_temporal_coverage()
    {
        Assert.True(EurLexAdapter.ShouldIncludeOriginalState(
            new DateOnly(2014, 9, 17), [new DateOnly(2024, 5, 20), new DateOnly(2024, 10, 18)]));
        Assert.False(EurLexAdapter.ShouldIncludeOriginalState(
            new DateOnly(2014, 9, 17), [new DateOnly(2014, 9, 17), new DateOnly(2024, 10, 18)]));
        Assert.False(EurLexAdapter.ShouldIncludeOriginalState(
            new DateOnly(2025, 1, 1), [new DateOnly(2024, 10, 18)]));
        Assert.True(EurLexAdapter.ShouldIncludeOriginalState(new DateOnly(2014, 9, 17), []));
    }

    [Fact]
    public void Same_date_publisher_states_survive_with_the_next_distinct_date_as_their_boundary()
    {
        var sameDate = new DateOnly(2025, 7, 28);
        var nextDate = new DateOnly(2025, 8, 4);
        var coordinates = EurLexAdapter.ConsolidatedCoordinates(
        [
            ("02025R0001-20250728", sameDate),
            ("02025R0001-20250728R(01)", sameDate),
            ("02025R0001-20250804", nextDate),
        ]);

        Assert.Equal(3, coordinates.Count);
        var siblings = coordinates.Where(version => version.Date == sameDate).ToArray();
        Assert.Equal(2, siblings.Length);
        Assert.Equal(2, siblings.Select(version => version.Celex).Distinct().Count());
        Assert.All(siblings, version => Assert.Equal(nextDate.AddDays(-1), version.ValidTo));
        Assert.All(siblings, version => Assert.True(version.ValidTo >= version.Date));
        Assert.Null(coordinates.Single(version => version.Date == nextDate).ValidTo);
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

        Assert.Equal(SourceBodyStatus.PublisherMetadataOnly, result.Status);
        Assert.Null(result.Value);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
