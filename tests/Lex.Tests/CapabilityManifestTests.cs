using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Ingest;
using Lex.Mcp;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lex.Tests;

public sealed class CapabilityManifestTests : IDisposable
{
    private readonly List<string> _paths = [];

    [Fact]
    public void Thin_fixture_manifest_is_handwritten_and_exact()
    {
        var db = Path();
        var document = Doc("fixture", "en", "2024-01-01", null) with
        {
            Hierarchy = "secondary_law",
            ActForm = "REG",
            BindingStatus = "in_force",
        };
        var expected = new[]
        {
            Entry("act_form", "*", "all_versions", null, null, 1, 1),
            Entry("act_form", "*", "as_of", "2024-01-01", null, 1, 1),
            Entry("act_form", "en", "all_versions", null, null, 1, 1),
            Entry("act_form", "en", "as_of", "2024-01-01", null, 1, 1),
            Entry("binding_status", "*", "all_versions", null, null, 1, 1),
            Entry("binding_status", "*", "as_of", "2024-01-01", null, 1, 1),
            Entry("binding_status", "en", "all_versions", null, null, 1, 1),
            Entry("binding_status", "en", "as_of", "2024-01-01", null, 1, 1),
            Entry("domain", "*", "all_versions", null, null, 1, 0),
            Entry("domain", "*", "as_of", "2024-01-01", null, 1, 0),
            Entry("domain", "en", "all_versions", null, null, 1, 0),
            Entry("domain", "en", "as_of", "2024-01-01", null, 1, 0),
            Entry("hierarchy", "*", "all_versions", null, null, 1, 1),
            Entry("hierarchy", "*", "as_of", "2024-01-01", null, 1, 1),
            Entry("hierarchy", "en", "all_versions", null, null, 1, 1),
            Entry("hierarchy", "en", "as_of", "2024-01-01", null, 1, 1),
        };

        IndexBuilder.Build(db, Stamp("fixture"), [document], [], [], [],
            StampSigner.CreateKeyPem(),
            capabilityExpectation: CapabilityBuildExpectation.Fixture(expected));

        using var reader = LexIndexReader.Open(db);
        Assert.Equal(expected, reader.CapabilityManifest);
        Assert.Equal(CapabilityManifest.Schema, reader.Stamp["capability_manifest_schema"]);
        Assert.Equal(expected.Length.ToString(), reader.Stamp["capability_manifest_rows"]);
        Assert.Equal(64, reader.CapabilityManifestDigest.Length);

        var badDb = Path();
        var wrong = expected.Select(row => row.Filter == "domain"
            ? row with { PopulatedRows = 1 }
            : row).ToArray();
        Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            badDb, Stamp("fixture"), [document], [], [], [], StampSigner.CreateKeyPem(),
            capabilityExpectation: CapabilityBuildExpectation.Fixture(wrong)));
    }

    [Fact]
    public void Production_allowlist_must_match_all_slices_exactly()
    {
        var policy = CapabilityBuildExpectation.Production(
            "production", ["domain"], new string('a', 64));
        var supported = Doc("production", "en", "2024-01-01", null) with
        {
            Hierarchy = "secondary_law",
            ActForm = "REG",
            BindingStatus = "in_force",
        };
        IndexBuilder.Build(Path(), Stamp("production"), [supported], [], [], [],
            StampSigner.CreateKeyPem(), capabilityExpectation: policy);

        var domainUnexpectedlyPopulated = supported with { Domains = "|finance|" };
        var changed = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            Path(), Stamp("production"), [domainUnexpectedlyPopulated], [], [], [],
            StampSigner.CreateKeyPem(), capabilityExpectation: policy));
        Assert.Contains("domain", changed.Message, StringComparison.Ordinal);

        var unexpectedGap = supported with { Hierarchy = null };
        var gap = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            Path(), Stamp("production"), [unexpectedGap], [], [], [],
            StampSigner.CreateKeyPem(), capabilityExpectation: policy));
        Assert.Contains("hierarchy", gap.Message, StringComparison.Ordinal);

        var inverted = supported with
        {
            Key = "production:inverted:2024-02-01",
            ValidFrom = "2024-02-01",
            ValidTo = "2024-01-31",
        };
        Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            Path(), Stamp("production"), [inverted], [], [], [],
            StampSigner.CreateKeyPem(), capabilityExpectation: policy));
    }

    [Fact]
    public void Empty_manifest_stamps_every_governed_filter_as_unsupported()
    {
        var db = Path();
        IndexBuilder.Build(db, Stamp("empty"), [], [], [], [], StampSigner.CreateKeyPem());

        using var reader = LexIndexReader.Open(db);
        Assert.Empty(reader.CapabilityManifest);
        Assert.Equal(string.Join(',', CapabilityManifest.GovernedFilters),
            reader.Stamp["capability_manifest_unsupported_filters"]);
        var filters = new FilterSet(null, null, null, "en", null,
            "secondary_law", "REG", "in_force", "finance");
        Assert.Equal(CapabilityManifest.GovernedFilters,
            reader.UnsupportedFilters(filters, CapabilityTimeScope.AllVersions));
    }

    [Fact]
    public void Production_policy_loader_is_bounded_strict_and_collection_scoped()
    {
        var valid = Policy("""
            {
              "schema": "lex-capability-policy/1",
              "collections": {
                "eu-eurlex": { "unsupported_filters": ["domain"] }
              }
            }
            """);
        var expectation = LoadPolicy(valid, "eu-eurlex");
        Assert.Equal("production", expectation.Tier);
        Assert.Equal("eu-eurlex", expectation.Collection);
        Assert.Equal(["domain"], expectation.UnsupportedFilters);
        Assert.Equal(64, expectation.PolicySha256.Length);

        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            Policy("""{"schema":1,"collections":{"eu-eurlex":{"unsupported_filters":[]}}}"""),
            "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            Policy("""{"schema":"lex-capability-policy/1","extra":true,"collections":{"eu-eurlex":{"unsupported_filters":[]}}}"""),
            "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            Policy("""{"schema":"lex-capability-policy/1","collections":{"eu-eurlex":{"unsupported_filters":["hierarchy","domain"]}}}"""),
            "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            Policy("""{"schema":"lex-capability-policy/1","collections":{"eu-eurlex":{"unsupported_filters":["invented"]}}}"""),
            "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            Policy("""{"schema":"lex-capability-policy/1","collections":{"eu-eurlex":{"unsupported_filters":["domain"]},"eu-eurlex":{"unsupported_filters":[]}}}"""),
            "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"missing-capability-policy-{Guid.NewGuid():N}.json"),
            "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(
            Policy(new string(' ', 64 * 1024 + 1)), "eu-eurlex"));
        Assert.Throws<InvalidDataException>(() => LoadPolicy(valid, "lu-legilux"));

        Assert.Throws<ArgumentException>(() => IndexFromCorpus.Build(
            "missing-corpus", null, "missing-index.db", null, DateTimeOffset.UnixEpoch,
            capabilityExpectation: expectation,
            capabilityPolicyPath: valid));
    }

    [Fact]
    public void Runtime_gate_is_scoped_by_language_and_effective_period()
    {
        var db = Path();
        var english = Doc("scope-en", "en", "2020-01-01", null) with
        {
            Hierarchy = "secondary_law",
        };
        var french = Doc("scope-fr", "fr", "2020-01-01", null);
        var provisions = new[]
        {
            Provision(english, "syntheticneedle applies"),
            Provision(french, "syntheticneedle applies"),
        };
        IndexBuilder.Build(db, Stamp("scope"), [english, french], provisions, [], [],
            StampSigner.CreateKeyPem());
        using var reader = LexIndexReader.Open(db);
        var core = new McpCore(new Dictionary<string, LexIndexReader>
        {
            ["scope"] = reader,
        });

        var aggregate = Result(core.CallTool("search", new JsonObject
        {
            ["query"] = "syntheticneedle",
            ["hierarchy"] = "secondary_law",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            aggregate["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal("hierarchy", aggregate["unsupported_filters"]![0]!.GetValue<string>());
        Assert.Empty(aggregate["hits"]!.AsArray());
        var refusedPopulation = Assert.IsType<JsonObject>(aggregate["population"]);
        Assert.Equal("mounted_scope_before_unsupported_filters",
            refusedPopulation["basis"]!.GetValue<string>());
        Assert.Equal(1, refusedPopulation["works_in_scope"]!.GetValue<int>());
        Assert.False(refusedPopulation["scope_filters_applied"]!.GetValue<bool>());
        Assert.False(refusedPopulation["query_ran"]!.GetValue<bool>());

        var englishAtDate = Result(core.CallTool("search", new JsonObject
        {
            ["query"] = "syntheticneedle",
            ["hierarchy"] = "secondary_law",
            ["language"] = "en",
            ["time_scope"] = "as_of",
            ["as_of"] = "2021-06-01",
        }));
        Assert.Equal(McpStatus.Ok,
            englishAtDate["envelope"]!["status"]!.GetValue<string>());
        Assert.NotEmpty(englishAtDate["hits"]!.AsArray());
        Assert.Equal(1, englishAtDate["population"]!["works_in_scope"]!.GetValue<int>());
        Assert.True(englishAtDate["population"]!["scope_filters_applied"]!.GetValue<bool>());
        Assert.True(englishAtDate["population"]!["query_ran"]!.GetValue<bool>());

        var frenchAtDate = Result(core.CallTool("search", new JsonObject
        {
            ["query"] = "syntheticneedle",
            ["hierarchy"] = "secondary_law",
            ["language"] = "fr",
            ["time_scope"] = "as_of",
            ["as_of"] = "2021-06-01",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            frenchAtDate["envelope"]!["status"]!.GetValue<string>());

        var outsideHeldPeriod = Result(core.CallTool("search", new JsonObject
        {
            ["query"] = "syntheticneedle",
            ["hierarchy"] = "secondary_law",
            ["language"] = "en",
            ["time_scope"] = "as_of",
            ["as_of"] = "2019-06-01",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            outsideHeldPeriod["envelope"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void Every_filtering_operation_gates_before_query_execution()
    {
        var db = Path();
        var document = Doc("gate", "en", "2024-01-01", null);
        IndexBuilder.Build(db, Stamp("gate"), [document], [], [], [], StampSigner.CreateKeyPem());
        using var reader = LexIndexReader.Open(db);
        var core = new McpCore(new Dictionary<string, LexIndexReader> { ["gate"] = reader });

        var inForce = Result(core.CallTool("in_force_on", new JsonObject
        {
            ["date"] = "2025-01-01",
            ["domain"] = "finance",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            inForce["envelope"]!["status"]!.GetValue<string>());

        var changes = Result(core.CallTool("changes_in_period", new JsonObject
        {
            ["from_date"] = "2024-01-01",
            ["to_date"] = "2025-01-01",
            ["domain"] = "finance",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            changes["envelope"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void Period_gate_checks_every_intersecting_slice_at_inclusive_boundaries()
    {
        var db = Path();
        var older = Doc("period", "en", "2020-01-01", "2020-12-31");
        var newer = Doc("period", "en", "2021-01-01", null) with
        {
            Hierarchy = "secondary_law",
        };
        IndexBuilder.Build(db, Stamp("period"), [older, newer], [], [], [],
            StampSigner.CreateKeyPem());
        using var reader = LexIndexReader.Open(db);
        var filter = new FilterSet(null, null, null, "en", null,
            "secondary_law", null, null, null);

        Assert.Empty(reader.UnsupportedFiltersInPeriod(
            filter, new DateOnly(2021, 1, 1), new DateOnly(2021, 1, 1)));
        Assert.Equal(["hierarchy"], reader.UnsupportedFiltersInPeriod(
            filter, new DateOnly(2020, 12, 31), new DateOnly(2021, 1, 1)));
        Assert.Equal(["hierarchy"], reader.UnsupportedFiltersInPeriod(
            filter, new DateOnly(2020, 12, 31), new DateOnly(2020, 12, 31)));
        Assert.Equal(["hierarchy"], reader.UnsupportedFiltersInPeriod(
            filter, new DateOnly(2019, 1, 1), new DateOnly(2019, 12, 31)));

        var core = new McpCore(new Dictionary<string, LexIndexReader>
        {
            ["period"] = reader,
        });
        var supported = Result(core.CallTool("changes_in_period", new JsonObject
        {
            ["from_date"] = "2021-01-01",
            ["to_date"] = "2021-12-31",
            ["language"] = "en",
            ["hierarchy"] = "secondary_law",
        }));
        Assert.NotEqual(McpStatus.FilterNotSupportedByIndex,
            supported["envelope"]!["status"]!.GetValue<string>());

        var crossing = Result(core.CallTool("changes_in_period", new JsonObject
        {
            ["from_date"] = "2020-12-31",
            ["to_date"] = "2021-01-01",
            ["language"] = "en",
            ["hierarchy"] = "secondary_law",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            crossing["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal("period", crossing["time_scope"]!.GetValue<string>());
    }

    [Fact]
    public void Manifest_structure_rejects_overlapping_effective_periods()
    {
        var older = Doc("overlap", "en", "2020-01-01", "2020-12-31");
        var newer = Doc("overlap", "en", "2021-01-01", null) with
        {
            Hierarchy = "secondary_law",
        };
        var rows = CapabilityManifest.Build([older, newer]);
        var malformed = rows.Select(row => row.Filter == "hierarchy"
                && row.Language == "en"
                && row.TimeScope == CapabilityManifest.AsOf
                && row.PeriodStart == "2020-01-01"
            ? row with { PeriodEnd = "2021-01-01" }
            : row).ToArray();

        var error = Assert.Throws<InvalidDataException>(
            () => CapabilityManifest.ValidateStructure(malformed));
        Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_structure_rejects_an_open_period_before_the_final_slice()
    {
        var older = Doc("open-middle", "en", "2020-01-01", "2020-12-31");
        var newer = Doc("open-middle", "en", "2021-01-01", null) with
        {
            Hierarchy = "secondary_law",
        };
        var rows = CapabilityManifest.Build([older, newer]);
        var malformed = rows.Select(row => row.Filter == "hierarchy"
                && row.Language == "en"
                && row.TimeScope == CapabilityManifest.AsOf
                && row.PeriodStart == "2020-01-01"
            ? row with { PeriodEnd = null }
            : row).ToArray();

        var error = Assert.Throws<InvalidDataException>(
            () => CapabilityManifest.ValidateStructure(malformed));
        Assert.Contains("open period", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_structure_rejects_adjacent_slices_that_should_be_merged()
    {
        var older = Doc("unmerged", "en", "2020-01-01", "2020-12-31");
        var newer = Doc("unmerged", "en", "2021-01-01", null) with
        {
            Hierarchy = "secondary_law",
        };
        var rows = CapabilityManifest.Build([older, newer]);
        var malformed = rows.Select(row => row.Filter == "hierarchy"
                && row.Language == "en"
                && row.TimeScope == CapabilityManifest.AsOf
                && row.PeriodStart == "2021-01-01"
            ? row with { PopulatedRows = 0 }
            : row).ToArray();

        var error = Assert.Throws<InvalidDataException>(
            () => CapabilityManifest.ValidateStructure(malformed));
        Assert.Contains("unmerged", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_structure_requires_aggregate_and_matching_language_sets()
    {
        var english = Doc("languages-en", "en", "2020-01-01", null);
        var french = Doc("languages-fr", "fr", "2020-01-01", null);
        var rows = CapabilityManifest.Build([english, french]);
        var missingAggregate = rows.Where(row => row.Filter != "hierarchy"
            || row.Language != CapabilityManifest.AllLanguages).ToArray();
        var missingLanguage = rows.Where(row => row.Filter != "hierarchy"
            || row.Language != "fr").ToArray();

        var aggregateError = Assert.Throws<InvalidDataException>(
            () => CapabilityManifest.ValidateStructure(missingAggregate));
        Assert.Contains("aggregate language", aggregateError.Message,
            StringComparison.OrdinalIgnoreCase);
        var languageError = Assert.Throws<InvalidDataException>(
            () => CapabilityManifest.ValidateStructure(missingLanguage));
        Assert.Contains("language set", languageError.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_build_rejects_reserved_or_unsafe_source_languages()
    {
        foreach (var language in new[] { CapabilityManifest.AllLanguages, "fr_FR", "-fr" })
            Assert.Throws<InvalidDataException>(() => CapabilityManifest.Build(
                [Doc($"bad-language-{language}", language, "2020-01-01", null)]));
    }

    [Fact]
    public void Reader_rejects_unknown_signed_capability_stamp_claims()
    {
        var db = Path();
        var key = StampSigner.CreateKeyPem();
        var document = Doc("unknown-stamp", "en", "2024-01-01", null);
        IndexBuilder.Build(db, Stamp("unknown-stamp"), [document], [], [], [], key);
        using (var connection = new SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO stamp(k,v) VALUES ('capability_future_claim','unexpected')";
            insert.ExecuteNonQuery();
            Resign(connection, key);
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
        Assert.Contains("unknown capability", error.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_build_meets_release_scale_sweep_budget()
    {
        var first = new DateOnly(2024, 1, 1);
        var documents = Enumerable.Range(0, 20_000).Select(index =>
        {
            var from = first.AddDays(index % 365);
            return Doc($"scale-{index}", index % 2 == 0 ? "en" : "fr",
                from.ToString("yyyy-MM-dd"), from.AddDays(30).ToString("yyyy-MM-dd")) with
            {
                Hierarchy = "secondary_law",
                Domains = "|finance|",
                ActForm = "REG",
                BindingStatus = "in_force",
            };
        }).ToArray();

        var stopwatch = Stopwatch.StartNew();
        var rows = CapabilityManifest.Build(documents);
        stopwatch.Stop();

        Assert.NotEmpty(rows);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"Release-scale capability sweep took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Reader_rejects_manifest_tampering_and_legacy_indexes_fail_closed()
    {
        var db = Path();
        var document = Doc("tamper", "en", "2024-01-01", null);
        IndexBuilder.Build(db, Stamp("tamper"), [document], [], [], [], StampSigner.CreateKeyPem());

        using (var connection = new SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE capability_manifest SET populated_rows=1 WHERE rowid=(SELECT rowid FROM capability_manifest LIMIT 1)";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));

        var legacy = Path();
        IndexBuilder.Build(legacy, Stamp("legacy"), [document with
        {
            Key = "legacy:work:2024-01-01",
            Collection = "legacy",
        }], [], [], [], StampSigner.CreateKeyPem());
        using (var connection = new SqliteConnection($"Data Source={legacy}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE capability_manifest; DELETE FROM stamp WHERE k LIKE 'capability_%'";
            command.ExecuteNonQuery();
        }
        using var legacyReader = LexIndexReader.Open(legacy);
        var core = new McpCore(new Dictionary<string, LexIndexReader> { ["legacy"] = legacyReader });
        var result = Result(core.CallTool("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["domain"] = "finance",
        }));
        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            result["envelope"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void Search_refuses_publisher_metadata_filter_when_legacy_catalog_cannot_apply_it()
    {
        var db = Path();
        var key = StampSigner.CreateKeyPem();
        var document = Doc("legacy-metadata", "en", "2024-01-01", null);
        IndexBuilder.Build(db, Stamp("legacy-metadata"), [document], [], [], [], key);

        using (var connection = new SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE work_publisher_metadata;
                DROP TABLE document_roles;
                DELETE FROM stamp
                WHERE k IN ('work_catalog_version', 'publisher_metadata_records',
                            'document_role_records');
                """;
            command.ExecuteNonQuery();
            Resign(connection, key);
        }

        using var reader = LexIndexReader.Open(db);
        var core = new McpCore(new Dictionary<string, LexIndexReader>
            { ["legacy-metadata"] = reader });
        var result = Result(core.CallTool("search", new JsonObject
        {
            ["query"] = "synthetic",
            ["publisher_metadata_identifier"] = "https://example.test/concept/1",
        }));

        Assert.Equal(McpStatus.FilterNotSupportedByIndex,
            result["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal(["publisher_metadata_identifier"], result["unsupported_filters"]!.AsArray()
            .Select(filter => filter!.GetValue<string>()));
        var population = Assert.IsType<JsonObject>(result["population"]);
        Assert.Equal("mounted_scope_before_unsupported_filters",
            population["basis"]!.GetValue<string>());
        Assert.Equal(1, population["works_in_scope"]!.GetValue<int>());
        Assert.False(population["scope_filters_applied"]!.GetValue<bool>());
        Assert.False(population["query_ran"]!.GetValue<bool>());
    }

    public void Dispose()
    {
        foreach (var path in _paths)
            try { File.Delete(path); } catch { }
    }

    private string Path()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"lex-capability-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return path;
    }

    private string Policy(string contents)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"lex-capability-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        _paths.Add(path);
        return path;
    }

    private static CapabilityBuildExpectation LoadPolicy(string path, string collection)
    {
        var type = typeof(IndexFromCorpus).Assembly.GetType(
            "Lex.Ingest.CapabilityPolicy", throwOnError: true)!;
        var method = type.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("CapabilityPolicy.Load was not found.");
        try
        {
            return Assert.IsType<CapabilityBuildExpectation>(
                method.Invoke(null, [path, collection]));
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static Dictionary<string, string> Stamp(string collection) => new()
    {
        ["collection"] = collection,
        ["tier"] = "test",
        ["history_begins"] = "publisher",
        ["built_at"] = "2026-08-28T00:00:00Z",
        ["corpus_commit"] = "test",
    };

    private static DocRow Doc(string collection, string language, string from, string? to) => new(
        $"{collection}:work:{from}", collection, "work", "urn:work", "REG", language,
        from, to, "publisher", "2026-08-28T00:00:00Z", false, true, true,
        "record", "body", "https://example.test/work", "Test work", "Test work", null,
        from, null);

    private static ProvisionRow Provision(DocRow document, string text) => new(
        $"{document.Key}|{document.Language}|{document.ValidFrom}", 0, "art_1",
        $"{document.Key}#art_1", "article", "1", null, null, null, document.Title, text,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

    private static CapabilityManifestEntry Entry(
        string filter, string language, string timeScope, string? periodStart,
        string? periodEnd, long eligible, long populated) => new(
            filter, language, timeScope, periodStart, periodEnd, eligible, populated);

    private static JsonObject Result(JsonNode result) => result switch
    {
        JsonObject value => value,
        JsonArray values => Assert.IsType<JsonObject>(Assert.Single(values)),
        _ => throw new Xunit.Sdk.XunitException("Expected one publisher result."),
    };

    private static void Resign(SqliteConnection connection, string key)
    {
        var stamp = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT k,v FROM stamp";
            using var rows = read.ExecuteReader();
            while (rows.Read()) stamp[rows.GetString(0)] = rows.GetString(1);
        }
        var (signature, publicKey) = StampSigner.Sign(stamp, key);
        foreach (var (name, value) in new[]
                 { (Name: "signature", Value: signature), (Name: "public_key", Value: publicKey) })
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE stamp SET v=$value WHERE k=$name";
            update.Parameters.AddWithValue("$name", name);
            update.Parameters.AddWithValue("$value", value);
            update.ExecuteNonQuery();
        }
    }
}
