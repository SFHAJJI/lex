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
        public List<int> BatchSizes { get; } = [];
        public List<int?> BatchPaddings { get; } = [];
        public List<string[]> BatchTexts { get; } = [];
        public int CountTokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 2;
        public int PrefixLengthForTokens(string text, int maxTokens)
        {
            var words = 0;
            for (var index = 0; index < text.Length; index++)
                if ((index == 0 || char.IsWhiteSpace(text[index - 1]))
                    && !char.IsWhiteSpace(text[index])
                    && ++words >= Math.Max(1, maxTokens - 2))
                {
                    var end = text.IndexOf(' ', index);
                    return end < 0 ? text.Length : end;
                }
            return text.Length;
        }
        public int SuffixStartForTokens(string text, int maxTokens)
        {
            var words = 0;
            for (var index = text.Length - 1; index >= 0; index--)
                if (!char.IsWhiteSpace(text[index])
                    && (index == 0 || char.IsWhiteSpace(text[index - 1]))
                    && ++words >= Math.Max(1, maxTokens - 2))
                    return index;
            return 0;
        }
        public float[] Encode(string text, EmbeddingInputKind kind)
        {
            var result = new float[Dimensions];
            foreach (var token in WorkSearch.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var slot = token switch
                {
                    "solar" or "photovoltaic" or "tender" or "procurement" => 0,
                    "privacy" or "personal" or "data" => 1,
                    _ => 2 + Math.Abs(StringComparer.Ordinal.GetHashCode(token) % (Dimensions - 2)),
                };
                result[slot]++;
            }
            var norm = MathF.Sqrt(result.Sum(value => value * value));
            for (var index = 0; index < result.Length; index++) result[index] /= norm;
            return result;
        }
        public IReadOnlyList<float[]> EncodeBatch(
            IReadOnlyList<string> texts, EmbeddingInputKind kind, int? padToTokens = null)
        {
            BatchSizes.Add(texts.Count);
            BatchPaddings.Add(padToTokens);
            BatchTexts.Add(texts.ToArray());
            return texts.Select(text => Encode(text, kind)).ToArray();
        }
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
    public void Publisher_discovery_is_searchable_but_cannot_become_an_exact_work_constraint()
    {
        var db = TempDb();
        var source = "https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32016R0679";
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "Regulation (EU) 2016/679") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow(
                    "publisher_short_title",
                    "http://publications.europa.eu/ontology/cdm#expression_title_short",
                    "fr",
                    "gdpr, personal data, personal data protection",
                    source),
            ],
        };
        IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("GDPR", FilterSet.All, 10, fuzzyAuto: false);

        Assert.False(result.QueryPlan!.HasStrongWorkMatch);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679"
            && hit.MatchReasons.Contains("work_metadata"));
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind,identifier,value,language,valid_from,source_uri
            FROM work_publisher_metadata
            """;
        using var row = command.ExecuteReader();
        Assert.True(row.Read());
        Assert.Equal("publisher_short_title", row.GetString(0));
        Assert.Equal(source, row.GetString(5));
    }

    [Fact]
    public void Publisher_discovery_respects_the_requested_version_date()
    {
        var db = TempDb();
        var source = "https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32016R0679";
        var earlier = Doc("eu:32016r0679:2020-01-01", "32016r0679", "Privacy regulation") with
        {
            ValidTo = "2022-01-01",
            PublisherMetadata =
            [
                new PublisherMetadataRow("publisher_short_title", "short-title", "fr",
                    "legacyname", source),
            ],
        };
        var later = Doc("eu:32016r0679:2022-01-01", "32016r0679", "Privacy regulation") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow("publisher_short_title", "short-title", "fr",
                    "modernname", source),
            ],
        };
        IndexBuilder.Build(db, Stamp(), [earlier, later], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var historical = FilterSet.All with { AsOf = new DateOnly(2021, 6, 1) };

        Assert.Contains(reader.SearchKeyword("legacyname", historical, 10, false).Hits,
            hit => hit.Doc.ValidFrom == "2020-01-01");
        Assert.Empty(reader.SearchKeyword("modernname", historical, 10, false).Hits);
    }

    [Fact]
    public void Publisher_metadata_normalization_is_bound_by_the_content_digest()
    {
        var db = TempDb();
        var doc = Doc("eu:delegated:2024-01-01", "delegated", "Delegated regulation") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow("eurovoc", "subject-1", "fr", "energy",
                    "https://publications.europa.eu/resource/authority/eurovoc/1"),
            ],
            DocumentRoles = ["delegated"],
        };
        IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null);
        string committed;
        using (var reader = LexIndexReader.Open(db))
        {
            committed = reader.Stamp["content_digest"];
            Assert.Equal(committed, reader.ComputeContentDigest());
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE work_publisher_metadata SET normalized='gdpr'";
            command.ExecuteNonQuery();
        }

        using var tampered = LexIndexReader.Open(db);
        Assert.NotEqual(committed, tampered.ComputeContentDigest());
    }

    [Fact]
    public void Document_roles_are_bound_by_the_content_digest()
    {
        var db = TempDb();
        var doc = Doc("eu:delegated:2024-01-01", "delegated", "Delegated regulation") with
        {
            DocumentRoles = ["delegated"],
        };
        IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null);
        string committed;
        using (var reader = LexIndexReader.Open(db))
            committed = reader.Stamp["content_digest"];
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE document_roles SET role='implementing'";
            command.ExecuteNonQuery();
        }

        using var tampered = LexIndexReader.Open(db);
        Assert.NotEqual(committed, tampered.ComputeContentDigest());
    }

    [Fact]
    public void Build_issue_inventory_digest_is_verified_when_an_index_mounts()
    {
        var db = TempDb();
        const string issues = "[{\"code\":\"no_versions\",\"work\":\"missing\"}]";
        var stamp = Stamp();
        stamp["build_issues_json"] = issues;
        stamp["build_issues_digest"] = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(issues)));
        IndexBuilder.Build(db, stamp, [], [], [], [], null);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE stamp SET v='[]' WHERE k='build_issues_json'";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("[{\"code\":1,\"work\":\"w1\"}]")]
    public void Malformed_signed_build_issue_inventory_is_rejected_at_mount(string issues)
    {
        var db = TempDb();
        var stamp = Stamp();
        stamp["build_issues_json"] = issues;
        stamp["build_issues_digest"] = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(issues)));
        IndexBuilder.Build(db, stamp, [], [], [], [], null);

        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
    }

    [Fact]
    public void Oversized_signed_build_issue_inventory_is_rejected_at_mount()
    {
        var db = TempDb();
        var issues = "[{\"code\":\"gap\",\"work\":\"w1\",\"detail\":\""
                     + new string('x', 2001) + "\"}]";
        var stamp = Stamp();
        stamp["build_issues_json"] = issues;
        stamp["build_issues_digest"] = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(issues)));
        IndexBuilder.Build(db, stamp, [], [], [], [], null);

        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("-1")]
    [InlineData("1000001")]
    public void Invalid_expected_work_count_is_rejected_at_mount(string expected)
    {
        var db = TempDb();
        var stamp = Stamp();
        stamp["scope_expected_works"] = expected;
        stamp["build_issues_json"] = "[]";
        stamp["build_issues_digest"] = Convert.ToHexStringLower(SHA256.HashData("[]"u8));
        IndexBuilder.Build(db, stamp, [], [], [], [], null);

        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
    }

    [Fact]
    public void Expected_work_count_requires_the_signed_issue_inventory()
    {
        var db = TempDb();
        var stamp = Stamp();
        stamp["scope_expected_works"] = "1";
        IndexBuilder.Build(db, stamp, [], [], [], [], null);

        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
    }

    [Fact]
    public void Fully_legacy_inventory_absence_remains_mountable_and_unavailable()
    {
        var db = TempDb();
        IndexBuilder.Build(db, Stamp(), [], [], [], [], null);

        using var reader = LexIndexReader.Open(db);

        Assert.Null(reader.Coverage().ExpectedWorks);
        Assert.Empty(reader.Coverage().BuildIssues!);
    }

    [Fact]
    public void Role_intent_filters_provision_retrieval_by_publisher_document_role()
    {
        var db = TempDb();
        var delegated = Doc("eu:delegated:2024-01-01", "delegated", "Delegated regulation") with
        {
            DocumentRoles = ["delegated"],
        };
        var implementing = Doc("eu:implementing:2024-01-01", "implementing", "Implementing regulation") with
        {
            DocumentRoles = ["implementing"],
        };
        IndexBuilder.Build(db, Stamp(), [delegated, implementing],
            [Provision(delegated, "Operators have reporting obligations."),
             Provision(implementing, "Operators have reporting obligations.")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "delegated regulation reporting obligations", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("delegated", result.QueryPlan!.RoleIntent);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "delegated");
        Assert.DoesNotContain(result.Hits, hit => hit.Doc.GroupKey == "implementing");
    }

    [Fact]
    public void Ordinary_execution_language_is_not_inferred_as_an_implementing_document_role()
    {
        var db = TempDb();
        var doc = Doc("eu:ordinary:2024-01-01", "ordinary", "Ordinary regulation");
        IndexBuilder.Build(db, Stamp(), [doc],
            [Provision(doc, "The execution of the contract remains subject to review.")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("execution contract", FilterSet.All, 10, false);

        Assert.Null(result.QueryPlan!.RoleIntent);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "ordinary");
    }

    [Fact]
    public void Document_role_filter_is_valid_across_catalogue_query_entry_points()
    {
        var db = TempDb();
        var doc = Doc("eu:delegated:2024-01-01", "delegated", "Delegated regulation") with
        {
            DocumentRoles = ["delegated"],
        };
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Reporting obligations.")],
            [], [], null);
        using var reader = LexIndexReader.Open(db);
        var filter = FilterSet.All with { DocumentRole = "delegated" };

        Assert.Single(reader.InForceOn(new DateOnly(2025, 1, 1), filter, 10, 0).Rows);
        Assert.Single(reader.GroupsPage(10, 0, filter));
        Assert.Single(reader.SearchWorksByIdentifierOrTitle("delegated", filter, 10));
        Assert.Single(reader.ChangesInPeriod(
            "2023-01-01", "2025-01-01", null, false, 10, filter: filter));
    }

    [Fact]
    public void Publisher_discovery_cannot_starve_a_canonical_title_match()
    {
        var db = TempDb();
        var source = "https://publications.europa.eu/resource/authority/eurovoc/1";
        var noisy = Enumerable.Range(0, 30).Select(index =>
            Doc($"eu:noise-{index}:2024-01-01", $"noise-{index}", $"Noise regulation {index}") with
            {
                PublisherMetadata =
                [
                    new PublisherMetadataRow("eurovoc", $"subject-{index}", "fr", "alpha", source),
                ],
            }).ToArray();
        var target = Doc("eu:target:2024-01-01", "target", "Alpha Gamma Regulation");
        IndexBuilder.Build(db, Stamp(), noisy.Append(target).ToArray(), [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("alpha gamma", FilterSet.All, 5, false);

        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "target");
    }

    [Fact]
    public void Publisher_metadata_search_has_a_fixed_candidate_ceiling()
    {
        var db = TempDb();
        var source = "https://publications.europa.eu/resource/authority/eurovoc/1";
        var docs = Enumerable.Range(0, 1_200).Select(index =>
            Doc($"eu:work-{index}:2024-01-01", $"work-{index}", $"Regulation {index}") with
            {
                PublisherMetadata =
                [
                    new PublisherMetadataRow("eurovoc", $"subject-{index}", "fr",
                        "sharedterm", source),
                ],
            }).ToArray();
        IndexBuilder.Build(db, Stamp(), docs, [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("sharedterm", FilterSet.All, 10, false);

        Assert.Equal(10, result.Hits.Count);
        Assert.All(result.Hits, hit => Assert.Contains("work_metadata", hit.MatchReasons));
    }

    [Fact]
    public void Parsed_role_intent_cannot_override_an_explicit_conflicting_filter()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var delegated = Doc("eu:delegated:2024-01-01", "delegated", "Delegated regulation") with
        {
            DocumentRoles = ["delegated"],
        };
        var implementing = Doc("eu:implementing:2024-01-01", "implementing", "Implementing regulation") with
        {
            DocumentRoles = ["implementing"],
        };
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [delegated, implementing],
            [Provision(delegated, "Operators have reporting obligations."),
             Provision(implementing, "Operators have reporting obligations.")], [], [], null,
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"));

        using var reader = LexIndexReader.Open(db, encoder, vectors);
        var filter = FilterSet.All with { DocumentRole = "implementing" };

        Assert.Empty(reader.SearchKeyword(
            "delegated regulation reporting obligations", filter, 10, false).Hits);
        Assert.Empty(reader.SearchHybrid(
            "delegated regulation reporting obligations", filter, 10, false).Hits);
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
        Assert.Equal("0", reader.Stamp["work_vector_records"]);
        Assert.DoesNotContain("vector_layout", reader.Stamp.Keys);
        Assert.Empty(result.QueryExpansions);
    }

    [Fact]
    public void Duplicate_official_titles_are_reported_as_ambiguous_not_auto_selected()
    {
        var db = TempDb();
        var first = Doc("eu:first:2024-01-01", "first", "Reporting Act");
        var second = Doc("eu:second:2024-01-01", "second", "Reporting Act");
        IndexBuilder.Build(db, Stamp(), [first, second], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("Reporting Act", FilterSet.All, 10, false);

        Assert.Equal("ambiguous", result.QueryPlan!.WorkResolutionStatus);
        Assert.False(result.QueryPlan.HasStrongWorkMatch);
        Assert.Empty(result.QueryPlan.WorkConstraints);
        var resolution = Assert.Single(result.QueryPlan.WorkResolutions!);
        Assert.Equal(["first", "second"], resolution.Candidates);
        Assert.All(result.Hits, hit => Assert.Contains(hit.MatchReasons,
            reason => reason.StartsWith("ambiguous_", StringComparison.Ordinal)));
    }

    [Fact]
    public void Multiple_reviewed_names_resolve_as_multiple_explicit_work_constraints()
    {
        var db = TempDb();
        var gdpr = Doc("eu:gdpr:2024-01-01", "gdpr", "Privacy regulation");
        var dora = Doc("eu:dora:2024-01-01", "dora", "Resilience regulation");
        IndexBuilder.Build(db, Stamp(), [gdpr, dora],
            [Provision(gdpr, "Reporting obligations."), Provision(dora, "Reporting obligations.")],
            [], [], null, workSearch: new WorkSearchBuildOptions(
                [Alias("gdpr", "fr", "GDPR"), Alias("dora", "fr", "DORA")],
                [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "compare GDPR and DORA reporting obligations", FilterSet.All, 10, false);

        Assert.Equal("resolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Equal(["dora", "gdpr"], result.QueryPlan.WorkConstraints);
        Assert.Equal(2, result.QueryPlan.WorkResolutions!.Count);
        Assert.All(result.QueryPlan.WorkResolutions, item => Assert.Equal("resolved", item.Status));
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "gdpr");
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "dora");
    }

    [Fact]
    public void Multiple_names_for_one_work_remain_distinct_resolutions_with_one_constraint()
    {
        var db = TempDb();
        var gdpr = Doc("eu:gdpr:2024-01-01", "gdpr", "Privacy regulation");
        IndexBuilder.Build(db, Stamp(), [gdpr], [Provision(gdpr, "Reporting obligations.")],
            [], [], null, workSearch: new WorkSearchBuildOptions(
                [Alias("gdpr", "fr", "GDPR"), Alias("gdpr", "fr", "RGPD")],
                [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "compare GDPR and RGPD reporting obligations", FilterSet.All, 10, false);

        Assert.Equal("resolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Equal(["gdpr"], result.QueryPlan.WorkConstraints);
        Assert.Equal(["gdpr", "rgpd"], result.QueryPlan.WorkResolutions!
            .Select(item => item.Mention).ToArray());
        Assert.All(result.QueryPlan.WorkResolutions!, item => Assert.Equal(["gdpr"], item.Candidates));
    }

    [Fact]
    public void Mixed_known_and_unknown_identifiers_preserve_each_resolution_state()
    {
        var db = TempDb();
        var gdpr = Doc("eu:32016r0679:2024-01-01", "32016r0679", "Privacy regulation");
        IndexBuilder.Build(db, Stamp(), [gdpr], [Provision(gdpr, "Reporting obligations.")],
            [], [], null, workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "GDPR")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "compare GDPR and 32024R9999 reporting obligations", FilterSet.All, 10, false);

        Assert.Equal("unresolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Equal(["32016r0679"], result.QueryPlan.WorkConstraints);
        Assert.Contains(result.QueryPlan.WorkResolutions!, item =>
            item.Mention == "gdpr" && item.Status == "resolved");
        Assert.Contains(result.QueryPlan.WorkResolutions!, item =>
            item.Mention == "32024R9999" && item.Status == "unresolved");
    }

    [Fact]
    public void Multiple_unknown_identifiers_in_prose_are_each_reported_unresolved()
    {
        var db = TempDb();
        IndexBuilder.Build(db, Stamp(), [], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "compare 32024R9999 with 32023L9998", FilterSet.All, 10, false);

        Assert.Equal("unresolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Equal(["32024R9999", "32023L9998"], result.QueryPlan.WorkResolutions!
            .Select(item => item.Mention).ToArray());
        Assert.All(result.QueryPlan.WorkResolutions!, item => Assert.Equal("unresolved", item.Status));
    }

    [Fact]
    public void Unknown_exact_legal_identifier_is_explicitly_unresolved()
    {
        var db = TempDb();
        IndexBuilder.Build(db, Stamp(), [], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("32024R9999", FilterSet.All, 10, false);

        Assert.Equal("unresolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Empty(result.QueryPlan.WorkConstraints);
        Assert.Equal("32024R9999", Assert.Single(result.QueryPlan.WorkResolutions!).Mention);
    }

    [Fact]
    public void Known_exact_identifier_keeps_identity_evidence_and_resolves_case_insensitively()
    {
        var db = TempDb();
        var gdpr = Doc("eu:32016r0679:2024-01-01", "32016r0679", "Privacy regulation");
        IndexBuilder.Build(db, Stamp(), [gdpr], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("32016R0679", FilterSet.All, 10, false);

        Assert.Equal("resolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Equal(["32016r0679"], result.QueryPlan.WorkConstraints);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679"
            && hit.MatchReasons.Contains("exact_identifier"));
    }

    [Fact]
    public void Article_intent_inside_a_named_work_query_returns_the_requested_provision()
    {
        var db = TempDb();
        var regulation = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR") with
        {
            DocumentRoles = ["delegated"],
        };
        var article = Provision(regulation,
            "The controller shall notify a personal data breach to the supervisory authority.",
            anchor: "art_33", number: "33");
        IndexBuilder.Build(db, Stamp(), [regulation], [article], [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "What does delegated Article 33 RGPD require?", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("art_33", result.Hits[0].Provision.Anchor);
        Assert.Contains("article_intent", result.Hits[0].MatchReasons);
        Assert.Equal("33", result.QueryPlan!.ArticleNumber);
        Assert.Equal("delegated", result.QueryPlan.RoleIntent);
        Assert.Equal(["32016r0679"], result.QueryPlan.WorkConstraints);
    }

    [Fact]
    public void Short_reviewed_alias_inside_article_comparison_resolves_the_named_work()
    {
        var db = TempDb();
        var crr = Doc("eu:32013r0575:2020-01-01", "32013r0575",
            "Regulation (EU) No 575/2013");
        IndexBuilder.Build(db, Stamp(), [crr],
            [Provision(crr, "Institutions shall comply with own funds requirements.",
                "art_92", "92")], [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32013r0575", "fr", "CRR")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Compare Article 92 of the CRR between 2020 and 2024.",
            FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("resolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.Equal(["32013r0575"], result.QueryPlan.WorkConstraints);
        Assert.Equal("92", result.QueryPlan.ArticleNumber);
        Assert.Equal("compare of between 2020 and 2024", result.QueryPlan.ProvisionQuery);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32013r0575"
            && hit.Provision.Anchor == "art_92"
            && hit.MatchReasons.Contains("article_intent"));
    }

    [Fact]
    public void Named_work_resolution_scopes_residual_provision_search()
    {
        var db = TempDb();
        var regulation = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR") with
        {
            DocumentRoles = ["delegated"],
        };
        var unrelated = Doc("eu:unrelated:2020-01-01", "unrelated", "Reporting Act");
        IndexBuilder.Build(db, Stamp(), [regulation, unrelated],
            [Provision(regulation, "Controllers have reporting obligations."),
             Provision(unrelated, "Companies have reporting obligations.")], [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "RGPD reporting obligations", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("reporting obligations", result.QueryPlan!.ProvisionQuery);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679"
            && hit.Provision.Anchor == "art_1");
        Assert.DoesNotContain(result.Hits, hit => hit.Doc.GroupKey == "unrelated");
    }

    [Fact]
    public void A_contained_generic_official_title_does_not_become_an_authoritative_scope()
    {
        var db = TempDb();
        var titleOnly = Doc("eu:title:2020-01-01", "title", "Reporting obligations");
        var direct = Doc("eu:direct:2020-01-01", "direct", "Companies Act");
        IndexBuilder.Build(db, Stamp(), [titleOnly, direct],
            [Provision(titleOnly, "Unrelated administrative wording."),
             Provision(direct, "Companies have reporting obligations.")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "what are reporting obligations for companies", FilterSet.All, 10, fuzzyAuto: false);

        Assert.False(result.QueryPlan!.HasStrongWorkMatch);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "direct"
            && hit.Provision.Anchor == "art_1");
    }

    [Fact]
    public void A_missing_requested_article_never_falls_through_to_a_different_article()
    {
        var db = TempDb();
        var regulation = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Personal data breach notification.", "art_33", "33")],
            [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Article 99 RGPD breach", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("99", result.QueryPlan!.ArticleNumber);
        Assert.DoesNotContain(result.Hits, hit => hit.Provision.Anchor == "art_33");
    }

    [Fact]
    public void Unscoped_article_intent_never_returns_a_different_numbered_article()
    {
        var db = TempDb();
        var numbered = Doc("eu:numbered:2020-01-01", "numbered", "Numbered Act");
        var wording = Doc("eu:wording:2020-01-01", "wording", "Breach Act");
        IndexBuilder.Build(db, Stamp(), [numbered, wording],
            [Provision(numbered, "Unrelated wording.", "art_33", "33"),
             Provision(wording, "Personal data breach notification.", "art_1", "1")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Article 33 breach", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("33", result.QueryPlan!.ArticleNumber);
        Assert.DoesNotContain(result.Hits, hit => hit.Provision.Anchor == "art_1");
    }

    [Fact]
    public void Unscoped_article_intent_can_rank_matching_residual_text()
    {
        var db = TempDb();
        var regulation = Doc("eu:consent:2020-01-01", "consent", "Consent Act");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Conditions for consent apply on the same date.", "art_7", "7")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Article 7 consent", FilterSet.All, 10, fuzzyAuto: false);

        var article = Assert.Single(result.Hits, hit => hit.Provision.Anchor == "art_7");
        Assert.Equal(new[] { "article_intent", "keyword" }, article.MatchReasons);
    }

    [Fact]
    public void Conversational_article_follow_up_does_not_search_anaphoric_date_words()
    {
        var db = TempDb();
        var regulation = Doc("eu:consent:2020-01-01", "consent", "Consent Act");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Conditions for consent.", "art_7", "7")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "What about Article 7 on the same date?", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("", result.QueryPlan!.ProvisionQuery);
        var article = Assert.Single(result.Hits, hit => hit.Provision.Anchor == "art_7");
        Assert.Equal(new[] { "article_intent" }, article.MatchReasons);
    }

    [Fact]
    public void Role_intent_is_removed_from_the_residual_provision_query()
    {
        var db = TempDb();
        var regulation = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR") with
        {
            DocumentRoles = ["delegated"],
        };
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Controllers have reporting obligations.")], [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "delegated RGPD reporting obligations", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("delegated", result.QueryPlan!.RoleIntent);
        Assert.Equal("reporting obligations", result.QueryPlan.ProvisionQuery);
        Assert.Contains(result.Hits, hit => hit.Provision.Anchor == "art_1");
    }

    [Fact]
    public void Article_intent_accepts_digit_suffixed_numbers()
    {
        var db = TempDb();
        var regulation = Doc("eu:act:2020-01-01", "act", "Example Act");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Specific rule.", "art_6a", "6a")], [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("act", "fr", "Example")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Article 6a Example", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("6a", result.QueryPlan!.ArticleNumber);
        Assert.Equal("art_6a", result.Hits[0].Provision.Anchor);
        Assert.Contains("article_intent", result.Hits[0].MatchReasons);
    }

    [Fact]
    public void Article_intent_normalizes_lettered_code_numbers_without_guessing_the_work()
    {
        var db = TempDb();
        var code = Doc("eu:code:2020-01-01", "code", "Employment Code");
        IndexBuilder.Build(db, Stamp(), [code],
            [Provision(code, "Employment notice rules.", "art_l_111-1", "L. 111-1")],
            [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("code", "fr", "Code emploi")], [], EnrichmentDigest));

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Article L. 111-1 du Code emploi", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("art_l_111-1", result.Hits[0].Provision.Anchor);
        Assert.Equal("l 111 1", result.QueryPlan!.ArticleNumber);
        Assert.Equal(["code"], result.QueryPlan.WorkConstraints);
    }

    [Fact]
    public void Weak_discovery_is_quarantined_from_ordinary_keyword_search()
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
        Assert.DoesNotContain(result.Hits, hit => hit.MatchReasons.Contains("work_discovery"));
    }

    [Fact]
    public void Weak_discovery_is_bounded_per_work_kind_and_evidence_set()
    {
        var db = TempDb();
        var doc = Doc("eu:target:2020-01-01", "target", "Target regulation");
        var provision = Provision(doc, "Authoritative publisher wording.");
        WorkDiscoveryRow Discovery(int index, IReadOnlyList<WorkEvidenceAnchor>? evidence = null) =>
            new("target", "fr", "concept", $"bounded concept {index}", "test-model",
                new string('a', 64), new string('b', 64), "2026-08-08T00:00:00Z",
                0.91, 3, 1.0, evidence ??
                [new WorkEvidenceAnchor(doc.Key, provision.Anchor, provision.TextSha)]);

        var tooManyConcepts = Enumerable.Range(0, WorkSearch.MaxDiscoveryPerKind + 1)
            .Select(index => Discovery(index)).ToArray();
        var kindError = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            db, Stamp(), [doc], [provision], [], [], null,
            workSearch: new WorkSearchBuildOptions([], tooManyConcepts, EnrichmentDigest)));
        Assert.Contains("per-kind", kindError.Message, StringComparison.OrdinalIgnoreCase);

        var tooMuchEvidence = Enumerable.Range(0, WorkSearch.MaxEvidenceAnchors + 1)
            .Select(index => new WorkEvidenceAnchor(doc.Key, provision.Anchor, provision.TextSha))
            .ToArray();
        var evidenceError = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            TempDb(), Stamp(), [doc], [provision], [], [], null,
            workSearch: new WorkSearchBuildOptions([], [Discovery(0, tooMuchEvidence)],
                EnrichmentDigest)));
        Assert.Contains("evidence", evidenceError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Weak_discovery_rows_are_bound_by_the_signed_content_digest()
    {
        var db = TempDb();
        var doc = Doc("eu:target:2020-01-01", "target", "Target regulation");
        var provision = Provision(doc, "Authoritative publisher wording.");
        var discovery = new WorkDiscoveryRow(
            "target", "fr", "concept", "bounded concept", "test-model",
            new string('a', 64), new string('b', 64), "2026-08-08T00:00:00Z",
            0.91, 3, 1.0,
            [new WorkEvidenceAnchor(doc.Key, provision.Anchor, provision.TextSha)]);
        IndexBuilder.Build(db, Stamp(), [doc], [provision], [], [], StampSigner.CreateKeyPem(),
            workSearch: new WorkSearchBuildOptions([], [discovery], EnrichmentDigest));
        string committed;
        using (var reader = LexIndexReader.Open(db))
        {
            committed = reader.Stamp["content_digest"];
            Assert.Equal(committed, reader.ComputeContentDigest());
        }
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE work_discovery SET value='tampered concept'";
            command.ExecuteNonQuery();
        }
        using var tampered = LexIndexReader.Open(db);
        Assert.NotEqual(committed, tampered.ComputeContentDigest());
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
        IndexBuilder.Build(db, Stamp(), [doc],
            [Provision(doc, "Personal data protection and execution measures.")],
            [], [], null);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM stamp WHERE k IN (
                  'work_search_records','work_vector_records','vector_layout',
                  'work_catalog_version','publisher_metadata_records','document_role_records',
                  'weak_discovery_records');
                DROP TABLE work_fts;
                DROP TABLE work_discovery;
                DROP TABLE work_vectors;
                DROP TABLE work_names;
                DROP TABLE work_records;
                DROP TABLE work_publisher_metadata;
                DROP TABLE document_roles;
                """;
            command.ExecuteNonQuery();
        }
        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("32016R0679", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("32016r0679", Assert.Single(result.Hits).Doc.GroupKey);
        Assert.Contains("exact_identifier", result.Hits[0].MatchReasons);
        Assert.False(result.QueryPlan!.WorkCatalogAvailable);
        Assert.Equal("resolved", result.QueryPlan.WorkResolutionStatus);

        var conversational = reader.SearchKeyword(
            "what does personal data", FilterSet.All, 10, fuzzyAuto: false);
        Assert.Equal("personal data", conversational.QueryPlan!.ProvisionQuery);
        Assert.Contains(conversational.Hits, hit => hit.Doc.GroupKey == "32016r0679");
    }

    [Fact]
    public void Earlier_five_table_work_catalogs_still_mount_without_new_metadata_tables()
    {
        var db = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "General Data Protection Regulation");
        IndexBuilder.Build(db, Stamp(), [doc],
            [Provision(doc, "Personal data protection and execution measures.")],
            [], [], null,
            workSearch: new WorkSearchBuildOptions(
                [Alias("32016r0679", "fr", "RGPD")], [], EnrichmentDigest));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE work_fts;
                CREATE VIRTUAL TABLE work_fts USING fts5(
                  group_key UNINDEXED, language UNINDEXED,
                  identifiers, aliases, titles, facets, discovery,
                  tokenize='unicode61 remove_diacritics 2');
                INSERT INTO work_fts(rowid,group_key,language,identifiers,aliases,titles,facets,discovery)
                  SELECT work_id,group_key,language,group_identifier,'RGPD',title,'',''
                  FROM work_records;
                DROP TABLE work_publisher_metadata;
                DROP TABLE document_roles;
                DELETE FROM stamp WHERE k IN (
                  'work_catalog_version','publisher_metadata_records','document_role_records',
                  'weak_discovery_records');
                """;
            command.ExecuteNonQuery();
        }

        using (var reader = LexIndexReader.Open(db))
        {
            var result = reader.SearchKeyword("RGPD personal data", FilterSet.All, 10, false);
            Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679");
            Assert.Contains(reader.SearchKeyword("execution measures", FilterSet.All, 10, false).Hits,
                hit => hit.Doc.GroupKey == "32016r0679");
        }
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO stamp(k,v) VALUES ('weak_discovery_records','0')";
            command.ExecuteNonQuery();
        }
        using (LexIndexReader.Open(db)) { }
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE stamp SET v='1' WHERE k='weak_discovery_records'";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
    }

    [Fact]
    public void Hybrid_falls_back_when_an_earlier_v3_index_has_no_work_catalog()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Personal data protection.")],
            [], [], null,
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"));
        using (var legacyVectors = new SemanticVectorWriter(vectors, encoder.Dimensions))
            legacyVectors.Write(encoder.Encode("Personal data protection.", EmbeddingInputKind.Passage));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM stamp WHERE k IN (
                  'work_search_records','work_vector_records','vector_layout',
                  'work_catalog_version','publisher_metadata_records','document_role_records',
                  'weak_discovery_records');
                DROP TABLE work_fts;
                DROP TABLE work_discovery;
                DROP TABLE work_vectors;
                DROP TABLE work_names;
                DROP TABLE work_records;
                DROP TABLE work_publisher_metadata;
                DROP TABLE document_roles;
                """;
            command.ExecuteNonQuery();
        }
        using var reader = LexIndexReader.Open(db, encoder, vectors);
        var result = reader.SearchHybrid("personal data", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679");
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

    [Fact]
    public void Hybrid_quarantines_weak_concept_vectors_from_ordinary_search()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var target = Doc("eu:target:2020-01-01", "target", "Net-zero industry rules");
        var neighbour = Doc("eu:neighbour:2020-01-01", "neighbour", "Reporting rules");
        var targetProvision = Provision(target, "Manufacturers submit annual reports.");
        var neighbourProvision = Provision(neighbour, "Operators keep accounting records.");
        var progress = new List<SemanticBuildProgress>();
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [target, neighbour], [targetProvision, neighbourProvision],
            [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", Progress: progress.Add),
            workSearch: new WorkSearchBuildOptions([], [
                new WorkDiscoveryRow("target", "fr", "concept", "solar tender criteria",
                    "test-model", new string('a', 64), new string('b', 64),
                    "2026-08-08T00:00:00Z", 0.91, 3, 1.0,
                    [new WorkEvidenceAnchor(target.Key, "art_1", targetProvision.TextSha)])
            ], EnrichmentDigest));

        using var allVectors = new SemanticVectorReader(vectors);
        Assert.Equal(5, allVectors.Count);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MIN(vector_ordinal),MAX(vector_ordinal),COUNT(*) FROM work_vectors";
            using var row = command.ExecuteReader();
            Assert.True(row.Read());
            Assert.Equal(2, row.GetInt64(0));
            Assert.Equal(4, row.GetInt64(1));
            Assert.Equal(3, row.GetInt64(2));
        }
        using var reader = LexIndexReader.Open(db, encoder, vectors);
        var result = reader.SearchHybrid("photovoltaic procurement", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("hybrid", result.RetrievalMode);
        Assert.DoesNotContain(result.Hits, hit => hit.MatchReasons.Contains("semantic_concept"));
        Assert.Contains(progress, item => item.Stage == SemanticBuildStage.WorkEmbeddings
            && item.Completed == 3 && item.Total == 3);
        Assert.Equal("3", reader.Stamp["work_vector_records"]);
        Assert.Equal("lex-vectors/1-mixed-provision-work", reader.Stamp["vector_layout"]);
    }

    [Fact]
    public void A_partial_work_catalog_is_rejected_instead_of_silently_disabled()
    {
        var db = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Personal data protection.")],
            [], [], null,
            workSearch: new WorkSearchBuildOptions([], [], EnrichmentDigest));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE work_fts";
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
        Assert.Contains("partial work catalog", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_current_artifact_cannot_masquerade_as_legacy_after_losing_its_work_catalog()
    {
        var db = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Personal data protection.")],
            [], [], null,
            workSearch: new WorkSearchBuildOptions([], [], EnrichmentDigest));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE work_fts;
                DROP TABLE work_discovery;
                DROP TABLE work_vectors;
                DROP TABLE work_names;
                DROP TABLE work_records;
                DROP TABLE work_publisher_metadata;
                DROP TABLE document_roles;
                """;
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
        Assert.Contains("inconsistent work catalog", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hybrid_rejects_a_work_vector_ordinal_outside_the_single_artifact()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var doc = Doc("eu:target:2020-01-01", "target", "Net-zero industry rules");
        var provision = Provision(doc, "Manufacturers submit annual reports.");
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [doc], [provision], [], [], null,
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"),
            workSearch: new WorkSearchBuildOptions([], [
                new WorkDiscoveryRow("target", "fr", "concept", "solar tender criteria",
                    "test-model", new string('a', 64), new string('b', 64),
                    "2026-08-08T00:00:00Z", 0.91, 3, 1.0,
                    [new WorkEvidenceAnchor(doc.Key, "art_1", provision.TextSha)])
            ], EnrichmentDigest));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE work_vectors SET vector_ordinal=999 WHERE work_vector_id=1";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db, encoder, vectors));
    }

    [Fact]
    public void Hybrid_rejects_a_work_vector_without_a_held_work_identity()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var doc = Doc("eu:target:2020-01-01", "target", "Net-zero industry rules");
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Reporting duties.")], [], [], null,
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE work_vectors SET work_id=999 WHERE work_vector_id=1";
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() =>
            LexIndexReader.Open(db, encoder, vectors));
        Assert.Contains("work identity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_vector_batches_refuse_inputs_above_the_configured_token_budget()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var doc = Doc("eu:target:2020-01-01", "target",
            "A deliberately long official work title for the budget test");
        using var encoder = new TestEncoder();

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            db, Stamp(), [doc], [], [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", MaxBatchTokens: 4),
            workSearch: new WorkSearchBuildOptions([], [], EnrichmentDigest)));

        Assert.Contains("work-vector input", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_vector_metadata_is_bounded_before_it_reaches_the_encoder_limit()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var longTitle = string.Join(' ', Enumerable.Repeat("privacy", 700));
        var doc = Doc("eu:target:2020-01-01", "target", longTitle) with
        {
            Hierarchy = "energy policy",
        };
        using var encoder = new TestEncoder();

        IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha",
                BatchSize: 32, MaxBatchTokens: 32_768));

        Assert.All(encoder.BatchPaddings, padding => Assert.InRange(padding!.Value, 1, 512));
        Assert.Contains(encoder.BatchTexts.SelectMany(batch => batch), text =>
            text.StartsWith("subjects: energy policy", StringComparison.Ordinal)
            && text.Contains("\nnames: privacy", StringComparison.Ordinal));
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM work_vectors";
        Assert.Equal(1, Convert.ToInt64(command.ExecuteScalar()));
        using var reader = LexIndexReader.Open(db, encoder, vectors);
        Assert.Equal("lex-vectors/1-mixed-provision-work", reader.Stamp["vector_layout"]);
    }

    [Fact]
    public void Public_metadata_bounds_admit_observed_official_title_and_anchor_lengths()
    {
        var db = TempDb();
        var title = new string('t', 5_315);
        var anchor = "art_" + new string('a', 301);
        var doc = Doc("eu:target:2020-01-01", "target", title);

        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Held text.", anchor)],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var held = Assert.Single(reader.Provisions(LexIndexReader.RidOf(doc)));
        Assert.Equal(title, held.WorkTitle);
        Assert.Equal(anchor, held.Anchor);
    }

    [Fact]
    public void Oversized_stamp_metadata_is_rejected_before_mount()
    {
        var db = TempDb();
        var doc = Doc("eu:target:2020-01-01", "target", "Held title");
        IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO stamp(k,v) VALUES ('oversized_notice',$value)";
            command.Parameters.AddWithValue("$value", new string('x', 4_097));
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
        Assert.Contains("oversized stamp metadata", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Isolated_query_session_has_its_own_connection_and_does_not_own_shared_vectors()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var doc = Doc("eu:target:2020-01-01", "target", "Personal data rules");
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Personal data protection.")],
            [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha"));

        using var reader = LexIndexReader.Open(db, encoder, vectors);
        using (var session = reader.CreateIsolatedSession())
        {
            Assert.Equal(reader.Collection, session.Collection);
            Assert.NotEmpty(session.SearchKeyword(
                "personal data", FilterSet.All, 5, fuzzyAuto: false).Hits);
        }

        Assert.NotEmpty(reader.SearchHybrid("personal data", FilterSet.All, 5).Hits);
    }

    [Fact]
    public void Work_vector_batches_use_fixed_padding_and_split_at_the_token_budget()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        using var encoder = new TestEncoder();
        var docs = new[]
        {
            Doc("eu:first:2020-01-01", "first", "First work"),
            Doc("eu:second:2020-01-01", "second", "Second work"),
            Doc("eu:third:2020-01-01", "third", "Third work"),
        };

        IndexBuilder.Build(db, Stamp(), docs, [], [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha",
                BatchSize: 32, MaxBatchTokens: 64));

        Assert.Equal([2, 1], encoder.BatchSizes);
        Assert.Equal([32, 32], encoder.BatchPaddings);
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

    private static ProvisionRow Provision(
        DocRow doc, string text, string anchor = "art_1", string number = "1")
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new ProvisionRow($"{doc.Key}|{doc.Language}|{doc.ValidFrom}", 0, anchor,
            $"{doc.Key}#{anchor}", "article", number, null, null, null, doc.Title, text, sha);
    }

    public void Dispose()
    {
        foreach (var file in _files)
            try { File.Delete(file); } catch { /* temporary test artifact */ }
    }
}
