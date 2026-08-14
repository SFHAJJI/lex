using System.Security.Cryptography;
using System.Text;
using Lex.Index;

namespace Lex.Tests;

public sealed class WorkSearchTests : IDisposable
{
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
    public void Publisher_discovery_is_searchable_but_cannot_become_an_exact_work_constraint()
    {
        var db = TempDb();
        var source = "https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32016R0679";
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "Regulation (EU) 2016/679") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow(
                    "eurovoc",
                    "http://eurovoc.europa.eu/5181",
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
        Assert.Equal("eurovoc", row.GetString(0));
        Assert.Equal(source, row.GetString(5));
    }

    [Fact]
    public void Publisher_metadata_URI_filter_never_turns_a_citation_identity_into_discovery()
    {
        var db = TempDb();
        const string identity = "https://data.legilux.public.lu/eli/etat/leg/code/civil";
        var doc = Doc("lu:loi-1804:2024-01-01", "loi-1804", "Code civil") with
        {
            Language = "fr",
            PublisherMetadata =
            [
                new PublisherMetadataRow(
                    "legilux_same_as", identity, null, "code-civil", identity,
                    CitationIdentity: true),
            ],
        };
        IndexBuilder.Build(db, Stamp(), [doc],
            [Provision(doc, "obligations civiles")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "obligations civiles",
            FilterSet.All with { PublisherMetadataIdentifier = identity },
            10,
            fuzzyAuto: false);

        Assert.Empty(result.Hits);
    }

    [Fact]
    public void Unique_official_short_title_segment_is_a_source_backed_strong_identity()
    {
        var db = TempDb();
        var source = "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32022R2554";
        var doc = Doc("eu:32022r2554:2024-01-01", "32022r2554", "Digital resilience instrument") with
        {
            Language = "en",
            PublisherMetadata =
            [
                new PublisherMetadataRow("publisher_short_title",
                    "http://publications.europa.eu/ontology/cdm#expression_title_short",
                    "en", "DORA, Digital operational Resilience Act", source),
            ],
        };
        IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("DORA", FilterSet.All, 10, false);

        Assert.True(result.QueryPlan!.HasStrongWorkMatch);
        Assert.Equal(["32022r2554"], result.QueryPlan.WorkConstraints);
        var resolution = Assert.Single(result.QueryPlan.WorkResolutions!);
        Assert.Equal("resolved", resolution.Status);
        Assert.Equal("publisher_short_title", resolution.Kind);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32022r2554"
            && hit.MatchReasons.Contains("exact_publisher_short_title"));
    }

    [Fact]
    public void Colliding_official_short_title_segment_is_ambiguous_not_arbitrarily_authoritative()
    {
        var db = TempDb();
        DocRow Crd(string work) => Doc($"eu:{work}:2024-01-01", work, $"Directive {work}") with
        {
            Language = "en",
            PublisherMetadata =
            [
                new PublisherMetadataRow("publisher_short_title",
                    "http://publications.europa.eu/ontology/cdm#expression_title_short",
                    "en", "CRD", $"https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:{work.ToUpperInvariant()}"),
            ],
        };
        IndexBuilder.Build(db, Stamp(),
            [Crd("32006l0048"), Crd("32009l0111"), Crd("32010l0076")],
            [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("CRD", FilterSet.All, 10, false);

        Assert.False(result.QueryPlan!.HasStrongWorkMatch);
        Assert.Empty(result.QueryPlan.WorkConstraints);
        var resolution = Assert.Single(result.QueryPlan.WorkResolutions!);
        Assert.Equal("ambiguous", resolution.Status);
        Assert.Equal("publisher_short_title", resolution.Kind);
        Assert.Equal(["32006l0048", "32009l0111", "32010l0076"], resolution.Candidates);
    }

    [Fact]
    public void Official_short_title_segmentation_is_literal_deterministic_and_bounded()
    {
        var db = TempDb();
        var doc = Doc("eu:segments:2024-01-01", "segments", "Segmented instrument") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow("publisher_short_title",
                    "http://publications.europa.eu/ontology/cdm#expression_title_short",
                    "fr", string.Join(',', Enumerable.Range(1, 17).Select(index => $"Alias {index}")),
                    "https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:SEGMENTS"),
            ],
        };

        Assert.Throws<InvalidDataException>(() =>
            IndexBuilder.Build(db, Stamp(), [doc], [], [], [], null));
    }

    [Fact]
    public void Direct_legal_text_precedes_matching_weak_publisher_taxonomy()
    {
        var db = TempDb();
        const string subject =
            "https://data.legilux.public.lu/resource/authority/legal-subject/hydrogen";
        var direct = Doc("lu:direct:2024-01-01", "direct", "Hydrogen decree");
        var taxonomy = Doc("lu:taxonomy:2024-01-01", "taxonomy", "Industrial decree") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow("legilux_subject_level2_theme", subject, "fr",
                    "hydrogen safety obligations", subject),
            ],
        };
        IndexBuilder.Build(db, Stamp(), [direct, taxonomy],
            [Provision(direct, "Operators must comply with hydrogen safety obligations.")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword("hydrogen safety obligations", FilterSet.All, 10, false);

        Assert.Equal("direct", result.Hits[0].Doc.GroupKey);
        Assert.Contains("keyword", result.Hits[0].MatchReasons);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "taxonomy"
            && hit.MatchReasons.Contains("work_metadata"));
        Assert.False(result.QueryPlan!.HasStrongWorkMatch);
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
                new PublisherMetadataRow("publisher_short_title",
                    "http://publications.europa.eu/ontology/cdm#expression_title_short", "fr",
                    "legacyname", source),
            ],
        };
        var later = Doc("eu:32016r0679:2022-01-01", "32016r0679", "Privacy regulation") with
        {
            PublisherMetadata =
            [
                new PublisherMetadataRow("publisher_short_title",
                    "http://publications.europa.eu/ontology/cdm#expression_title_short", "fr",
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
                new PublisherMetadataRow("eurovoc",
                    "https://publications.europa.eu/resource/authority/eurovoc/1", "fr", "energy",
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
                    new PublisherMetadataRow("eurovoc", $"{source}/{index}", "fr", "alpha", source),
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
                    new PublisherMetadataRow("eurovoc", $"{source}/{index}", "fr",
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
    public void Official_short_title_inside_a_long_query_pins_the_base_work_before_its_corrigendum()
    {
        var db = TempDb();
        var regulation = WithShortTitles(Doc("eu:32016r0679:2016-05-04", "32016r0679",
            "General Data Protection Regulation"), "RGPD");
        var corrigendum = Doc("eu:32016r0679r(02):2018-05-23", "32016r0679r(02)",
            "Rectificatif au règlement (UE) 2016/679");
        var provisions = new[]
        {
            Provision(regulation, "Protection des personnes physiques à l'égard du traitement des données."),
            Provision(corrigendum, "Rectificatif au règlement relatif à la protection des données."),
        };

        IndexBuilder.Build(db, Stamp(), [regulation, corrigendum], provisions, [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Règlement Général sur la Protection des Données (RGPD)",
            FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("32016r0679", result.Hits[0].Doc.GroupKey);
        Assert.Contains("contained_publisher_short_title", result.Hits[0].MatchReasons);
        Assert.DoesNotContain("enrichment_digest", reader.Stamp.Keys);
        Assert.Equal("0", reader.Stamp["work_vector_records"]);
        Assert.DoesNotContain("vector_layout", reader.Stamp.Keys);
        Assert.Empty(result.QueryExpansions);
    }

    // A publisher that prefixes a consolidation banner onto the title leaves the work with no
    // name anyone would ever cite: the stored string is longer than any citation, and the
    // contained pass needs the stored name to sit inside the query.
    [Fact]
    public void A_consolidation_banner_does_not_hide_the_work_its_own_name()
    {
        var db = TempDb();
        const string title = "Version consolidée applicable au 31/10/2002 : "
            + "Loi du 5 avril 1993 relative au secteur financier";
        var work = Doc("lu:loi-1993-04-05-n1:2024-01-01", "loi-1993-04-05-n1", title);
        IndexBuilder.Build(db, Stamp(), [work],
            [Provision(work, "Les professionnels du secteur financier.")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var exact = reader.SearchKeyword(
            "Loi du 5 avril 1993 relative au secteur financier", FilterSet.All, 10, false);
        var inSentence = reader.SearchKeyword(
            "Que dit la Loi du 5 avril 1993 relative au secteur financier sur les PSF ?",
            FilterSet.All, 10, false);
        var bannered = reader.SearchKeyword(title, FilterSet.All, 10, false);

        Assert.Equal(["loi-1993-04-05-n1"], exact.QueryPlan!.WorkConstraints);
        Assert.Contains(exact.Hits, hit => hit.MatchReasons.Contains("exact_title"));
        Assert.Equal(["loi-1993-04-05-n1"], inSentence.QueryPlan!.WorkConstraints);
        Assert.Contains(inSentence.Hits, hit => hit.MatchReasons.Contains("contained_title"));
        Assert.Equal(["loi-1993-04-05-n1"], bannered.QueryPlan!.WorkConstraints);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM work_names WHERE kind='official_title'";
        Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Theory]
    [InlineData("Version consolidée applicable au 31/10/2002 : Loi du 5 avril 1993 relative au secteur financier", true)]
    [InlineData("Version rectifiée applicable au 18/03/1979 : Loi du 5 avril 1993 relative au secteur financier", true)]
    // A colon is not a banner. This title carries an enumerated subdivision, not a publisher
    // prefix, and splitting it would invent a name the publisher never used.
    [InlineData("Loi du 4 mars 1982: a) portant création d'un fonds spécial", false)]
    [InlineData("Loi du 5 avril 1993 relative au secteur financier", false)]
    public void Only_a_dated_consolidation_banner_adds_a_second_official_title(
        string title, bool split)
    {
        var db = TempDb();
        IndexBuilder.Build(db, Stamp(),
            [Doc("lu:work:2024-01-01", "work", title)], [], [], [], null);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM work_names WHERE kind='official_title'";

        Assert.Equal(split ? 2L : 1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    // The citation convention inserts "modifiée" into a title the publisher writes without it.
    // One token defeated the contiguous contained match and identity resolution collapsed.
    [Fact]
    public void An_amendment_qualifier_in_a_citation_still_resolves_the_named_work()
    {
        var db = TempDb();
        const string title = "Loi du 12 novembre 2004 relative à la lutte contre le blanchiment "
            + "et contre le financement du terrorisme";
        var work = Doc("lu:loi-2004-11-12-n1:2024-01-01", "loi-2004-11-12-n1", title);
        IndexBuilder.Build(db, Stamp(), [work],
            [Provision(work, "Obligations de vigilance des professionnels.")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var official = reader.SearchKeyword(title, FilterSet.All, 10, false);
        var cited = reader.SearchKeyword(
            "loi modifiée du 12 novembre 2004 relative à la lutte contre le blanchiment "
            + "et contre le financement du terrorisme", FilterSet.All, 10, false);
        var inSentence = reader.SearchKeyword(
            "Que doivent faire les professionnels sous la loi modifiée du 12 novembre 2004 "
            + "relative à la lutte contre le blanchiment et contre le financement du terrorisme "
            + "en 2020 ?", FilterSet.All, 10, false);

        Assert.Equal(["loi-2004-11-12-n1"], official.QueryPlan!.WorkConstraints);
        Assert.Equal(["loi-2004-11-12-n1"], cited.QueryPlan!.WorkConstraints);
        Assert.Equal(["loi-2004-11-12-n1"], inSentence.QueryPlan!.WorkConstraints);
        Assert.DoesNotContain("modifiee", inSentence.QueryPlan.ProvisionQuery,
            StringComparison.Ordinal);
        Assert.DoesNotContain("blanchiment", inSentence.QueryPlan.ProvisionQuery,
            StringComparison.Ordinal);
    }

    // The second pass is additive on purpose: a genuine stored title can itself carry the
    // qualifier, and a destructive strip would make that work unfindable by its own name.
    [Fact]
    public void A_stored_title_that_carries_the_qualifier_keeps_resolving_from_its_own_name()
    {
        var db = TempDb();
        const string qualified = "Loi modifiée du 7 juillet 1971 portant, en matière répressive "
            + "et administrative, extension de la compétence";
        var withQualifier = Doc("lu:loi-1971-07-07-n1:2024-01-01", "loi-1971-07-07-n1", qualified);
        var other = Doc("lu:loi-1971-07-08-n1:2024-01-01", "loi-1971-07-08-n1",
            "Loi du 8 juillet 1971 portant approbation d'une convention");
        IndexBuilder.Build(db, Stamp(), [withQualifier, other], [], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var exact = reader.SearchKeyword(qualified, FilterSet.All, 10, false);
        var stripped = reader.SearchKeyword(
            "Loi du 7 juillet 1971 portant, en matière répressive et administrative, "
            + "extension de la compétence", FilterSet.All, 10, false);

        Assert.Equal(["loi-1971-07-07-n1"], exact.QueryPlan!.WorkConstraints);
        Assert.DoesNotContain("loi-1971-07-08-n1", stripped.QueryPlan!.WorkConstraints);
    }

    // "modifiant" and "modificatif" announce an amending act, not an amended one. Only the fixed
    // qualifier forms a citation inserts may be dropped, and never outside an act-form slot.
    [Theory]
    [InlineData("loi modifiee du 12 novembre 2004", "loi du 12 novembre 2004")]
    [InlineData("loi coordonnee du 5 avril 1993", "loi du 5 avril 1993")]
    [InlineData("loi modifiant la loi du 12 novembre 2004", "loi modifiant la loi du 12 novembre 2004")]
    [InlineData("reglement modificatif du 12 novembre 2004", "reglement modificatif du 12 novembre 2004")]
    [InlineData("directive modifiee 2015 849", "directive modifiee 2015 849")]
    [InlineData("amending directive 2015 849", "amending directive 2015 849")]
    public void The_citation_form_drops_only_an_amendment_qualifier(string query, string expected)
        => Assert.Equal(expected, WorkSearch.NormalizeCitation(query));

    [Theory]
    [InlineData("loi modifiant du 12 novembre 2004 relative à la lutte contre le blanchiment et contre le financement du terrorisme")]
    [InlineData("règlement modificatif du 12 novembre 2004 relative à la lutte contre le blanchiment et contre le financement du terrorisme")]
    [InlineData("amending Directive 2015/849 on the prevention of money laundering")]
    public void The_citation_pass_does_not_invent_a_resolution(string query)
    {
        var db = TempDb();
        var work = Doc("lu:loi-2004-11-12-n1:2024-01-01", "loi-2004-11-12-n1",
            "Loi du 12 novembre 2004 relative à la lutte contre le blanchiment "
            + "et contre le financement du terrorisme");
        IndexBuilder.Build(db, Stamp(), [work],
            [Provision(work, "Obligations de vigilance des professionnels.")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(query, FilterSet.All, 10, false);

        Assert.Empty(result.QueryPlan!.WorkConstraints);
    }

    // A code carries no date in its name, so the digit rule made the most-cited national
    // instruments unnameable inside a sentence while prose headings must stay weak.
    [Theory]
    [InlineData("Code du travail", "Quels délais de préavis le Code du travail impose-t-il ?", true)]
    [InlineData("Constitution du Grand-Duché de Luxembourg",
        "Que prévoit la Constitution du Grand-Duché de Luxembourg sur la liberté de la presse ?", true)]
    [InlineData("Reporting obligations",
        "Which reporting obligations apply to a controller?", false)]
    [InlineData("Cours et Tribunaux", "Que publient les Cours et Tribunaux ?", false)]
    [InlineData("Regulation (EU) 2016/679",
        "What does Regulation (EU) 2016/679 require?", true)]
    public void An_act_form_name_is_an_identity_inside_a_sentence_but_prose_is_not(
        string title, string query, bool resolves)
    {
        var db = TempDb();
        var work = Doc("lu:work:2024-01-01", "work", title);
        IndexBuilder.Build(db, Stamp(), [work], [Provision(work, "Texte de la disposition.")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(query, FilterSet.All, 10, false);

        Assert.Equal(resolves ? ["work"] : (string[])[], result.QueryPlan!.WorkConstraints);
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
    public void Multiple_official_short_titles_resolve_as_multiple_explicit_work_constraints()
    {
        var db = TempDb();
        var gdpr = WithShortTitles(Doc("eu:gdpr:2024-01-01", "gdpr", "Privacy regulation"), "GDPR");
        var dora = WithShortTitles(Doc("eu:dora:2024-01-01", "dora", "Resilience regulation"), "DORA");
        IndexBuilder.Build(db, Stamp(), [gdpr, dora],
            [Provision(gdpr, "Reporting obligations."), Provision(dora, "Reporting obligations.")],
            [], [], null);

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
        var gdpr = WithShortTitles(Doc("eu:gdpr:2024-01-01", "gdpr", "Privacy regulation"),
            "GDPR", "RGPD");
        IndexBuilder.Build(db, Stamp(), [gdpr], [Provision(gdpr, "Reporting obligations.")],
            [], [], null);

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
        var gdpr = WithShortTitles(
            Doc("eu:32016r0679:2024-01-01", "32016r0679", "Privacy regulation"), "GDPR");
        IndexBuilder.Build(db, Stamp(), [gdpr], [Provision(gdpr, "Reporting obligations.")],
            [], [], null);

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
        var regulation = WithShortTitles(Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR") with
        {
            DocumentRoles = ["delegated"],
        }, "RGPD");
        var article = Provision(regulation,
            "The controller shall notify a personal data breach to the supervisory authority.",
            anchor: "art_33", number: "33");
        IndexBuilder.Build(db, Stamp(), [regulation], [article], [], [], null);

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
    public void Short_official_name_inside_article_comparison_resolves_the_named_work()
    {
        var db = TempDb();
        var crr = WithShortTitles(Doc("eu:32013r0575:2020-01-01", "32013r0575",
            "Regulation (EU) No 575/2013"), "CRR");
        IndexBuilder.Build(db, Stamp(), [crr],
            [Provision(crr, "Institutions shall comply with own funds requirements.",
                "art_92", "92")], [], [], null);

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
        var regulation = WithShortTitles(Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR") with
        {
            DocumentRoles = ["delegated"],
        }, "RGPD");
        var unrelated = Doc("eu:unrelated:2020-01-01", "unrelated", "Reporting Act");
        IndexBuilder.Build(db, Stamp(), [regulation, unrelated],
            [Provision(regulation, "Controllers have reporting obligations."),
             Provision(unrelated, "Companies have reporting obligations.")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "RGPD reporting obligations", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("reporting obligations", result.QueryPlan!.ProvisionQuery);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679"
            && hit.Provision.Anchor == "art_1");
        Assert.DoesNotContain(result.Hits, hit => hit.Doc.GroupKey == "unrelated");
    }

    [Fact]
    public void A_numbered_official_title_inside_a_long_question_resolves_the_work()
    {
        var db = TempDb();
        var gdpr = Doc("eu:32016r0679:2016-05-04", "32016r0679", "Regulation (EU) 2016/679");
        var other = Doc("eu:32024r0607:2024-02-15", "32024r0607", "Regulation (EU) 2024/607");
        IndexBuilder.Build(db, Stamp(), [gdpr, other],
            [Provision(gdpr, "Le traitement n'est licite que si ...", "art_6", "6"),
             Provision(other, "Champ d'application.", "art_6", "6")], [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "What does Article 6 of Regulation (EU) 2016/679 say?", FilterSet.All, 10,
            fuzzyAuto: false);

        Assert.Equal("resolved", result.QueryPlan!.WorkResolutionStatus);
        Assert.True(result.QueryPlan.HasStrongWorkMatch);
        Assert.Equal(["32016r0679"], result.QueryPlan.WorkConstraints);
        Assert.Equal("6", result.QueryPlan.ArticleNumber);
        Assert.Contains(result.Hits, hit => hit.Doc.GroupKey == "32016r0679"
            && hit.Provision.Anchor == "art_6");
        Assert.DoesNotContain(result.Hits, hit => hit.Doc.GroupKey == "32024r0607");
    }

    // The safety boundary of the rule above, stated separately so it cannot be loosened by
    // accident: without its own number a contained title is prose, not a designation.
    [Fact]
    public void A_contained_title_without_its_own_number_still_never_scopes_a_topical_question()
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

        Assert.Equal("not_requested", result.QueryPlan!.WorkResolutionStatus);
        Assert.False(result.QueryPlan.HasStrongWorkMatch);
        Assert.Empty(result.QueryPlan.WorkConstraints);
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
        var regulation = WithShortTitles(
            Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR"), "RGPD");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Personal data breach notification.", "art_33", "33")],
            [], [], null);

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
        var regulation = WithShortTitles(Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR") with
        {
            DocumentRoles = ["delegated"],
        }, "RGPD");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Controllers have reporting obligations.")], [], [], null);

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
        var regulation = WithShortTitles(Doc("eu:act:2020-01-01", "act", "Example Act"), "Example");
        IndexBuilder.Build(db, Stamp(), [regulation],
            [Provision(regulation, "Specific rule.", "art_6a", "6a")], [], [], null);

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
        var code = WithShortTitles(Doc("eu:code:2020-01-01", "code", "Employment Code"),
            "Code emploi");
        IndexBuilder.Build(db, Stamp(), [code],
            [Provision(code, "Employment notice rules.", "art_l_111-1", "L. 111-1")],
            [], [], null);

        using var reader = LexIndexReader.Open(db);
        var result = reader.SearchKeyword(
            "Article L. 111-1 du Code emploi", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("art_l_111-1", result.Hits[0].Provision.Anchor);
        Assert.Equal("l 111 1", result.QueryPlan!.ArticleNumber);
        Assert.Equal(["code"], result.QueryPlan.WorkConstraints);
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
            [], [], null);
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
    public void Extended_work_catalog_without_source_backed_citation_identity_requires_rebuild()
    {
        var db = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        IndexBuilder.Build(db, Stamp(), [doc],
            [Provision(doc, "Personal data protection.")], [], [], null);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE work_publisher_metadata DROP COLUMN citation_identity;
                UPDATE stamp SET v='2' WHERE k='work_catalog_version';
                """;
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(db));
        Assert.Contains("work catalog version 2", error.Message, StringComparison.Ordinal);
        Assert.Contains("rebuild", error.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Hybrid_lookup_keeps_a_contained_official_short_title_deterministic()
    {
        var db = TempDb();
        var vectors = TempFile(".vectors");
        var regulation = WithShortTitles(
            Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR"), "RGPD");
        var neighbour = Doc("eu:32019r2175:2019-12-27", "32019r2175", "Amending regulation");
        using var encoder = new TestEncoder();
        IndexBuilder.Build(db, Stamp(), [regulation, neighbour],
            [Provision(regulation, "Personal data protection."),
             Provision(neighbour, "Amends several data protection rules.")], [], [], null,
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"));

        using var reader = LexIndexReader.Open(db, encoder, vectors);
        var result = reader.SearchHybrid("show me RGPD", FilterSet.All, 10, fuzzyAuto: false);

        Assert.Equal("keyword", result.RetrievalMode);
        Assert.Equal("32016r0679", result.Hits[0].Doc.GroupKey);
        Assert.Contains("contained_publisher_short_title", result.Hits[0].MatchReasons);
    }

    [Fact]
    public void A_partial_work_catalog_is_rejected_instead_of_silently_disabled()
    {
        var db = TempDb();
        var doc = Doc("eu:32016r0679:2016-05-04", "32016r0679", "GDPR");
        IndexBuilder.Build(db, Stamp(), [doc], [Provision(doc, "Personal data protection.")],
            [], [], null);
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
            [], [], null);
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
            semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"));
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
                encoder, vectors, "model-sha", "tokenizer-sha", MaxBatchTokens: 4)));

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

    private static DocRow WithShortTitles(DocRow doc, params string[] values) => doc with
    {
        PublisherMetadata =
        [
            new PublisherMetadataRow(
                "publisher_short_title",
                "http://publications.europa.eu/ontology/cdm#expression_title_short",
                doc.Language,
                string.Join(", ", values),
                doc.SourceUri ?? "https://example.invalid"),
        ],
    };

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
