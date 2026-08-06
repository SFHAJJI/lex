using Lex.Index;

namespace Lex.Tests;

public class IndexTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"lex-test-{Guid.NewGuid():N}.db");

    private sealed class FakeEncoder : ITextEncoder
    {
        public string ModelId => "test/e5";
        public string ModelRevision => "test-revision";
        public int Dimensions => 8;
        public int CountTokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 2;
        public int PrefixLengthForTokens(string text, int maxTokens)
        {
            var words = 0;
            for (var i = 0; i < text.Length; i++)
                if ((i == 0 || char.IsWhiteSpace(text[i - 1])) && !char.IsWhiteSpace(text[i])
                    && ++words >= Math.Max(1, maxTokens - 2))
                {
                    var end = text.IndexOf(' ', i);
                    return end < 0 ? text.Length : end;
                }
            return text.Length;
        }
        public int SuffixStartForTokens(string text, int maxTokens)
        {
            var wanted = Math.Max(1, maxTokens - 2);
            var words = 0;
            for (var i = text.Length - 1; i >= 0; i--)
                if (!char.IsWhiteSpace(text[i]) && (i == 0 || char.IsWhiteSpace(text[i - 1])) && ++words >= wanted)
                    return i;
            return 0;
        }
        public float[] Encode(string text, EmbeddingInputKind kind)
        {
            var vector = new float[Dimensions];
            foreach (var token in text.ToLowerInvariant().Split([' ', ',', '.', ':'], StringSplitOptions.RemoveEmptyEntries))
            {
                var slot = token switch
                {
                    "dismissal" or "employment" or "termination" or "notice" => 0,
                    "bank" or "capital" or "reserves" or "financial" => 1,
                    "privacy" or "personal" or "data" => 2,
                    _ => 3 + Math.Abs(StringComparer.Ordinal.GetHashCode(token) % 5),
                };
                vector[slot] += 1;
            }
            var norm = MathF.Sqrt(vector.Sum(x => x * x));
            for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
            return vector;
        }
        public void Dispose() { }
    }

    private static DocRow Row(string key, string group, string from, string? to, string kind = "REG", string? title = null, bool text = false) =>
        new(key, "t-pub", group, $"urn:{group}", kind, "en", from, to, "publisher",
            "2026-08-01T00:00:00Z", Withdrawn: false, TextAvailable: text, TextPublic: text,
            RecordSha: "abc", BodySha: null, SourceUri: "https://example.org", Title: title ?? group,
            TitleShort: title ?? group, Body: null, PublicationDate: from, StatusNote: null);

    private static ProvisionRow Prov(DocRow d, int seq, string anchor, string text, string? num = null) =>
        new(Rid: $"{d.Key}|{d.Language}|{d.ValidFrom}", Seq: seq, Anchor: anchor,
            ProvisionId: $"{d.Key}#{anchor}", PType: "article", Num: num ?? anchor, Heading: null,
            Path: null, ArticleValidFrom: null, WorkTitle: d.Title, TextMd: text,
            TextSha: Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));

    private LexIndexReader Build()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        var docs = new[]
        {
            Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", "2021-12-31", title: "first thing", text: true),
            Row("t-pub:w1:2022-01-01", "w1", "2022-01-01", null, title: "first thing revised", text: true),
            Row("t-pub:w2:2019-06-01", "w2", "2019-06-01", null, kind: "DIR", title: "second thing", text: true),
        };
        var provisions = new[]
        {
            Prov(docs[0], 0, "art_1", "the thing shall apply everywhere"),
            Prov(docs[0], 1, "art_2", "penalties for the thing are mild"),
            Prov(docs[1], 0, "art_1", "the thing shall apply everywhere, revised"),
            Prov(docs[2], 0, "art_1", "a different directive thing entirely"),
        };
        IndexBuilder.Build(_db, stamp, docs, provisions, [], [], StampSigner.CreateKeyPem());
        return LexIndexReader.Open(_db);
    }

    [Fact]
    public void Signature_round_trip_is_valid()
    {
        using var r = Build();
        Assert.True(r.SignatureValid);
    }

    // A signature over the stamp's metadata says nothing about the text the index serves.
    // The stamp therefore commits to a digest of the content, and this is the test that the
    // commitment is real: edit one article's text in the database and the recomputed digest
    // must stop matching the signed one. Without it, "every served hash is attributable"
    // would be a claim with no mechanism behind it.
    [Fact]
    public void Editing_article_text_breaks_the_content_digest()
    {
        string signed;
        using (var r = Build())
        {
            Assert.True(r.SignatureValid);
            signed = r.Stamp["content_digest"];
            Assert.Equal(signed, r.ComputeContentDigest());
        }

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE provisions SET text_sha = 'tampered' WHERE seq = 0";
            Assert.True(cmd.ExecuteNonQuery() > 0);
        }

        using (var r = LexIndexReader.Open(_db))
        {
            Assert.True(r.SignatureValid);                      // the stamp itself is untouched
            Assert.NotEqual(signed, r.ComputeContentDigest());  // but the contents no longer match it
        }
    }

    [Fact]
    public void Editing_a_version_three_blob_is_refused_even_when_occurrence_hashes_are_unchanged()
    {
        using (var reader = Build())
            Assert.Equal(reader.Stamp["content_digest"], reader.ComputeContentDigest());

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE text_blobs SET payload=X'00' WHERE rowid=(SELECT MIN(rowid) FROM text_blobs)";
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        using var tampered = LexIndexReader.Open(_db);
        Assert.ThrowsAny<Exception>(() => tampered.ComputeContentDigest());
    }

    [Fact]
    public void AsOf_stabs_the_correct_version_and_distinguishes_refusals()
    {
        using var r = Build();
        Assert.Equal("t-pub:w1:2020-01-01", r.AsOf("w1", new DateOnly(2021, 6, 1), FilterSet.All)!.Key);
        Assert.Equal("t-pub:w1:2022-01-01", r.AsOf("w1", new DateOnly(2024, 1, 1), FilterSet.All)!.Key);
        // no_version_for_date vs unknown_work
        Assert.Null(r.AsOf("w1", new DateOnly(1999, 1, 1), FilterSet.All));
        Assert.True(r.WorkExists("w1"));
        Assert.False(r.WorkExists("nope"));
        // work-level and version-level lex_ids both resolve (§9)
        Assert.NotNull(r.AsOf("t-pub:w1", new DateOnly(2021, 6, 1), FilterSet.All));
    }

    [Fact]
    public void InForceOn_is_computed_from_dates_and_deduplicated_by_work()
    {
        using var r = Build();
        var (rows, total) = r.InForceOn(new DateOnly(2023, 1, 1), FilterSet.All, 50, 0);
        Assert.Equal(2, total);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Key == "t-pub:w1:2022-01-01");     // the version valid on that date
        var (dirOnly, dirTotal) = r.InForceOn(new DateOnly(2023, 1, 1), new FilterSet(null, null, "DIR", null), 50, 0);
        Assert.Equal(1, dirTotal);
        Assert.Equal("w2", dirOnly.Single().GroupKey);
    }

    [Fact]
    public void Search_filters_before_ranking_and_hits_are_provision_level()
    {
        using var r = Build();
        var all = r.Search("thing", FilterSet.All, 10);
        Assert.True(all.Count >= 3);
        Assert.All(all, h => Assert.False(string.IsNullOrEmpty(h.Prov.Anchor)));
        var dirHits = r.Search("thing", new FilterSet(null, null, "DIR", null), 10);
        Assert.All(dirHits, h => Assert.Equal("DIR", h.Doc.Kind));
        Assert.Contains(r.Search("thing", new FilterSet(null, null, "REG,DIR", null), 10), h => h.Doc.Kind == "REG");
        Assert.All(r.Search("thing", new FilterSet(null, null, "!REG", null), 10), h => Assert.NotEqual("REG", h.Doc.Kind));
    }

    [Fact]
    public void Provisions_round_trip_and_body_reconstruction()
    {
        using var r = Build();
        var d = r.AsOf("w1", new DateOnly(2020, 6, 1), FilterSet.All)!;
        var provs = r.Provisions(LexIndexReader.RidOf(d));
        Assert.Equal(2, provs.Count);
        Assert.Equal(["art_1", "art_2"], provs.Select(p => p.Anchor));
        var body = r.BuildBody(d)!;
        Assert.Contains("the thing shall apply everywhere", body);
        Assert.Contains("penalties for the thing are mild", body);
        Assert.Null(d.Body);   // never stored on the row; reconstruction is explicit
    }

    [Fact]
    public void Version_three_stores_repeated_wording_once_but_preserves_each_occurrence()
    {
        var first = Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", "2021-12-31", text: true);
        var second = Row("t-pub:w1:2022-01-01", "w1", "2022-01-01", null, text: true);
        var repeated = string.Join(' ', Enumerable.Repeat("authoritative repeated provision wording", 80));
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [first, second], [Prov(first, 0, "art_1", repeated), Prov(second, 0, "art_1", repeated)],
            [], [], null);

        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db};Mode=ReadOnly"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM provisions), (SELECT COUNT(*) FROM lexical_states), (SELECT COUNT(*) FROM text_blobs), (SELECT encoding FROM text_blobs)";
            using var row = cmd.ExecuteReader();
            Assert.True(row.Read());
            Assert.Equal(2, row.GetInt32(0));
            Assert.Equal(1, row.GetInt32(1));
            Assert.Equal(1, row.GetInt32(2));
            Assert.Equal("br4", row.GetString(3));
        }

        using var reader = LexIndexReader.Open(_db);
        Assert.Equal(repeated, Assert.Single(reader.Provisions(LexIndexReader.RidOf(first))).TextMd);
        Assert.Equal(repeated, Assert.Single(reader.Provisions(LexIndexReader.RidOf(second))).TextMd);
        Assert.Single(reader.Search("authoritative", FilterSet.All, 10));
    }

    [Fact]
    public void Version_three_keeps_small_text_raw_and_filters_normalized_legal_metadata()
    {
        var doc = Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", null, text: true) with
        {
            Hierarchy = "eu_secondary",
            Domains = "|financial_services|aml|",
            ActForm = "regulation",
            BindingStatus = "binding",
            ConsolidationStatus = "published"
        };
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], [Prov(doc, 0, "art_1", "short exact text")], [], [], null);

        using var reader = LexIndexReader.Open(_db);
        var filter = new FilterSet(null, null, null, null, null, "eu_secondary", "regulation", "binding", "aml");
        var hit = Assert.Single(reader.Search("short", filter, 10));
        Assert.Equal("eu_secondary", hit.Doc.Hierarchy);
        Assert.Equal("published", hit.Doc.ConsolidationStatus);

        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db};Mode=ReadOnly");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT encoding FROM text_blobs";
        Assert.Equal("raw", cmd.ExecuteScalar());
    }

    [Fact]
    public void Version_two_indexes_remain_mountable_and_reconstruct_exact_text()
    {
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE stamp(k TEXT PRIMARY KEY, v TEXT NOT NULL);
                INSERT INTO stamp VALUES ('schema','lex-index/2'),('collection','legacy');
                CREATE TABLE docs(
                  key TEXT, collection TEXT, group_key TEXT, group_identifier TEXT, kind TEXT,
                  language TEXT, valid_from TEXT, valid_to TEXT, valid_time_source TEXT,
                  observed_from TEXT, withdrawn INTEGER, text_available INTEGER, text_public INTEGER,
                  record_sha TEXT, body_sha TEXT, source_uri TEXT, title TEXT, title_short TEXT,
                  publication_date TEXT, status_note TEXT, rid TEXT, profile TEXT);
                INSERT INTO docs VALUES(
                  'legacy:w1:2020-01-01','legacy','w1','urn:w1','REG','en','2020-01-01',NULL,
                  'publisher','2026-08-01T00:00:00Z',0,1,1,'abc',NULL,'https://example.org',
                  'Legacy work','Legacy work','2020-01-01',NULL,'legacy:w1:2020-01-01|en|2020-01-01',NULL);
                CREATE TABLE provisions(
                  rid TEXT, seq INTEGER, anchor TEXT, provision_id TEXT, ptype TEXT, num TEXT,
                  heading TEXT, path TEXT, article_valid_from TEXT, work_title TEXT, text_md TEXT, text_sha TEXT);
                INSERT INTO provisions VALUES(
                  'legacy:w1:2020-01-01|en|2020-01-01',0,'art_1','legacy:w1:2020-01-01#art_1',
                  'article','1',NULL,NULL,NULL,'Legacy work','legacy authoritative text',
                  '74d8dfb2885a60eaeb9379e231531497f34991316439fc46fd431c2f97e643d1');
                """;
            cmd.ExecuteNonQuery();
        }

        using var reader = LexIndexReader.Open(_db);
        var doc = reader.AsOf("w1", new DateOnly(2024, 1, 1), FilterSet.All)!;
        Assert.Equal("legacy authoritative text", Assert.Single(reader.Provisions(LexIndexReader.RidOf(doc))).TextMd);
        Assert.Null(doc.Hierarchy);
    }

    [Fact]
    public void Hybrid_search_finds_a_concept_without_azure_or_a_generative_model()
    {
        var employment = Row("t-pub:employment:2020-01-01", "employment", "2020-01-01", null, text: true);
        var banking = Row("t-pub:banking:2020-01-01", "banking", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new FakeEncoder();
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [employment, banking],
            [Prov(employment, 0, "art_1", "dismissal notice periods apply"),
             Prov(banking, 0, "art_1", "bank capital reserves are required")],
            [], [], null, semantic: new SemanticBuildOptions(encoder, vectors, "model-sha", "tokenizer-sha"));

        using var reader = LexIndexReader.Open(_db, encoder, vectors);
        Assert.Empty(reader.Search("employment termination", FilterSet.All, 10));
        var result = reader.SearchHybrid("employment termination", FilterSet.All, 10);
        Assert.Equal("hybrid", result.RetrievalMode);
        Assert.Equal("employment", result.Hits[0].Doc.GroupKey);
        Assert.Contains("semantic", result.Hits[0].MatchReasons);
    }

    [Fact]
    public void Fuzzy_fallback_is_visible_and_protects_identifiers()
    {
        var doc = Row("t-pub:privacy:2020-01-01", "privacy", "2020-01-01", null, text: true);
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], [Prov(doc, 0, "art_1", "protection of personal information")], [], [], null);
        using var reader = LexIndexReader.Open(_db);

        var typo = reader.SearchKeyword("protecton", FilterSet.All, 10, fuzzyAuto: true);
        Assert.Equal("protecton -> protection", Assert.Single(typo.QueryExpansions));
        Assert.Equal("privacy", Assert.Single(typo.Hits).Doc.GroupKey);
        Assert.Contains("fuzzy", typo.Hits[0].MatchReasons);

        var identifier = reader.SearchKeyword("32022R2555", FilterSet.All, 10, fuzzyAuto: true);
        Assert.Empty(identifier.QueryExpansions);
        var quotation = reader.SearchKeyword("\"protecton information\"", FilterSet.All, 10, fuzzyAuto: true);
        Assert.Empty(quotation.QueryExpansions);
        var disabled = reader.SearchHybrid("protecton", FilterSet.All, 10, fuzzyAuto: false);
        Assert.Empty(disabled.QueryExpansions);
    }

    [Fact]
    public void Semantic_chunks_are_deterministic_and_bounded()
    {
        using var encoder = new FakeEncoder();
        var paragraph = string.Join(' ', Enumerable.Repeat("employment notice rule", 100));
        var text = paragraph + "\n\n" + paragraph + "\n\nshort final paragraph";
        var first = SemanticChunker.Split(text, encoder);
        var second = SemanticChunker.Split(text, encoder);

        Assert.True(first.Count > 1);
        Assert.Equal(first.Select(c => c.Sha256), second.Select(c => c.Sha256));
        Assert.All(first, c => Assert.True(
            encoder.CountTokens("passage: " + c.Text) <= SemanticChunker.MaxTokens));
    }

    [Fact]
    public void Unknown_schema_is_refused_explicitly()
    {
        var stamp = new Dictionary<string, string> { ["collection"] = "t-pub" };
        IndexBuilder.Build(_db, stamp, [], [], [], [], null);
        // sabotage the schema stamp
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE stamp SET v='lex-index/999' WHERE k='schema'";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => LexIndexReader.Open(_db));
    }

    // ---- the catalogue ----
    //
    // /browse promised "Browse everything" and delivered seven curated links against 1,409 works.
    // The page that replaced it asks one question per reader choice, and every one of those
    // choices reaches an aggregate over versions, which is where a listing quietly goes wrong:
    // counting versions instead of works, losing the newest title, or paging a total that does
    // not match the rows.

    [Fact]
    public void The_catalogue_lists_works_not_versions()
    {
        using var r = Build();
        var (rows, total) = r.Catalogue(new FilterSet(null, null, null, null), null,
                                        CatalogueOrder.Name, 50, 0);

        // Three versions across two works. A listing of works must say two.
        Assert.Equal(2, total);
        Assert.Equal(2, rows.Count);

        var w1 = rows.Single(x => x.GroupKey == "w1");
        Assert.Equal(2, w1.Versions);
        Assert.Equal("2020-01-01", w1.FirstFrom);
        Assert.Equal("2022-01-01", w1.LastFrom);
        // The title comes from the NEWEST version, pinned by a window function. A GROUP BY
        // carrying more than one MIN/MAX leaves SQLite free to hand back either row's title.
        Assert.Equal("first thing revised", w1.Title);
    }

    [Fact]
    public void Filters_narrow_the_catalogue_and_its_total_together()
    {
        using var r = Build();
        var (rows, total) = r.Catalogue(new FilterSet(null, null, "DIR", null), null,
                                        CatalogueOrder.Name, 50, 0);

        // A pager built on a total that ignored the filter would offer pages that render empty.
        Assert.Equal(1, total);
        Assert.Equal("w2", Assert.Single(rows).GroupKey);
    }

    [Fact]
    public void The_text_filter_separates_held_wording_from_record_only()
    {
        using var r = Build();
        var f = new FilterSet(null, null, null, null);

        Assert.Equal(2, r.Catalogue(f, true, CatalogueOrder.Name, 50, 0).Total);
        Assert.Empty(r.Catalogue(f, false, CatalogueOrder.Name, 50, 0).Rows);
    }

    [Theory]
    [InlineData(CatalogueOrder.MostVersions, "w1")]   // 2 versions beats 1
    [InlineData(CatalogueOrder.MostRecent, "w1")]     // last change 2022 beats 2019
    [InlineData(CatalogueOrder.Oldest, "w2")]         // first seen 2019 beats 2020
    [InlineData(CatalogueOrder.Name, "w1")]
    public void Every_ordering_puts_a_predictable_work_first(CatalogueOrder order, string first)
    {
        using var r = Build();
        var (rows, _) = r.Catalogue(new FilterSet(null, null, null, null), null, order, 50, 0);
        Assert.Equal(first, rows[0].GroupKey);
    }

    [Fact]
    public void Paging_walks_every_work_exactly_once()
    {
        using var r = Build();
        var f = new FilterSet(null, null, null, null);
        var a = r.Catalogue(f, null, CatalogueOrder.Name, 1, 0).Rows;
        var b = r.Catalogue(f, null, CatalogueOrder.Name, 1, 1).Rows;

        Assert.Single(a);
        Assert.Single(b);
        Assert.NotEqual(a[0].GroupKey, b[0].GroupKey);
    }

    [Fact]
    public void The_type_list_counts_works_so_the_filter_labels_are_honest()
    {
        using var r = Build();
        var kinds = r.CatalogueKinds(null).ToDictionary(x => x.Kind, x => x.Works);

        // w1 has two REG versions. A filter chip saying "REG 2" beside a list of one work reads
        // as a bug, so the chip counts works.
        Assert.Equal(1, kinds["REG"]);
        Assert.Equal(1, kinds["DIR"]);
    }

    // ---- what a "what changed" row must be compared against ----
    //
    // A row reports how many versions a work gained in a window. Opening it used to compare
    // first_change with last_change, and those are the SAME DATE whenever a work moved exactly
    // once, which is the ordinary case: 92% of regulation rows in a recent window. The comparison
    // then ran a version against itself and truthfully reported no differences, so the report's
    // primary action was broken for most of its rows while looking like it worked.

    [Fact]
    public void A_work_that_moved_once_still_has_something_to_compare_against()
    {
        using var r = Build();
        // w1 has versions at 2020-01-01 and 2022-01-01. A window holding only the second one
        // reports one change, and its baseline must be the first.
        var row = Assert.Single(r.ChangesInPeriod("2021-06-01", "2023-01-01", null, true, 50));

        Assert.Equal(1, row.VersionsInPeriod);
        Assert.Equal(row.FirstChange, row.LastChange);      // the shape that used to break
        Assert.Equal("2020-01-01", row.Baseline);           // and what makes it comparable
    }

    [Fact]
    public void The_baseline_is_the_state_before_the_window_not_inside_it()
    {
        using var r = Build();
        var row = r.ChangesInPeriod("2019-01-01", "2023-01-01", null, true, 50)
                   .Single(x => x.GroupKey == "w1");

        // Both of w1's versions are inside this window, so the window opens on its first-ever
        // version and there is no earlier state. Null, not the window's own first change: a
        // caller must be able to tell "nothing to compare" from "compare against this".
        Assert.Equal(2, row.VersionsInPeriod);
        Assert.Null(row.Baseline);
    }

    [Fact]
    public void A_baseline_never_falls_inside_the_window()
    {
        using var r = Build();
        foreach (var row in r.ChangesInPeriod("2019-01-01", "2026-01-01", null, true, 50))
            if (row.Baseline is { } b)
                Assert.True(string.CompareOrdinal(b, row.FirstChange) < 0,
                    $"{row.GroupKey}: baseline {b} is not strictly before first change {row.FirstChange}");
    }

    [Fact]
    public void A_reissue_with_the_same_wording_is_distinguishable_from_an_amendment()
    {
        // A publisher can issue a new consolidation without altering a word. The row then says
        // "2 new versions" and the comparison says nothing changed, and both are true. Sending a
        // reader into that comparison makes working software look broken, so the report has to be
        // able to tell a reissue from an amendment before it offers the comparison.
        using var r = BuildReissued();

        var amended = r.ChangesInPeriod("2019-01-01", "2026-01-01", null, true, 50)
                       .Single(x => x.GroupKey == "amended");
        var reissued = r.ChangesInPeriod("2019-01-01", "2026-01-01", null, true, 50)
                        .Single(x => x.GroupKey == "reissued");

        Assert.Equal(2, amended.VersionsInPeriod);
        Assert.Equal(2, amended.DistinctTexts);      // two versions, two wordings
        Assert.Equal(2, reissued.VersionsInPeriod);
        Assert.Equal(1, reissued.DistinctTexts);     // two versions, one wording
    }

    private LexIndexReader BuildReissued()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        DocRow D(string group, string from, string bodySha) =>
            new($"t-pub:{group}:{from}", "t-pub", group, $"urn:{group}", "RGD", "fr", from, null,
                "publisher", "2026-08-01T00:00:00Z", Withdrawn: false, TextAvailable: true,
                TextPublic: true, RecordSha: "rec", BodySha: bodySha,
                SourceUri: "https://example.org", Title: group, TitleShort: group, Body: null,
                PublicationDate: from, StatusNote: null);

        var docs = new[]
        {
            D("amended", "2020-01-01", "aaa"), D("amended", "2021-01-01", "bbb"),
            D("reissued", "2020-01-01", "ccc"), D("reissued", "2021-01-01", "ddd"),
        };
        // The file hash is deliberately DIFFERENT on both of "reissued" versions while their
        // article text is identical, which is the real shape: a consolidated document carries a
        // header naming the date it was produced, so a pure reissue changes the file and not one
        // word of the law. Counting file hashes called that an amendment.
        var provisions = new[]
        {
            Prov(docs[0], 0, "art_1", "the original wording"),
            Prov(docs[1], 0, "art_1", "the amended wording"),
            Prov(docs[2], 0, "art_1", "wording that never moved"),
            Prov(docs[3], 0, "art_1", "wording that never moved"),
        };
        var db2 = Path.Combine(Path.GetTempPath(), $"lex-reissue-{Guid.NewGuid():N}.db");
        _extra.Add(db2);
        IndexBuilder.Build(db2, stamp, docs, provisions, [], [], StampSigner.CreateKeyPem());
        return LexIndexReader.Open(db2);
    }

    // ---- which language a version is served in when it exists in several ----
    //
    // The Constitution is one of three suggested starting points on the front page. It holds 37
    // French versions, one German and one Luxembourgish, and all three share the date 2023-07-01.
    // Ordering by language alone put "de" first, so the front page greeted readers in German.

    [Fact]
    public void An_unspecified_language_gets_the_one_the_work_is_mostly_written_in()
    {
        using var r = BuildMultilingual();
        var doc = r.AsOf("w3", new DateOnly(2024, 6, 1), new FilterSet(null, null, null, null));

        Assert.NotNull(doc);
        Assert.Equal("fr", doc!.Language);
    }

    [Fact]
    public void An_explicit_language_still_wins_over_the_preference()
    {
        using var r = BuildMultilingual();
        var doc = r.AsOf("w3", new DateOnly(2024, 6, 1), new FilterSet(null, null, null, "de"));

        Assert.Equal("de", doc!.Language);
    }

    /// <summary>A work published mostly in French, with one German and one Luxembourgish version
    /// sharing a date, which is the Constitution's exact shape.</summary>
    private LexIndexReader BuildMultilingual()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        DocRow L(string from, string lang) =>
            new($"t-pub:w3:{from}:{lang}", "t-pub", "w3", "urn:w3", "Constitution", lang, from, null,
                "publisher", "2026-08-01T00:00:00Z", Withdrawn: false, TextAvailable: true,
                TextPublic: true, RecordSha: "abc", BodySha: null, SourceUri: "https://example.org",
                Title: "a constitution", TitleShort: "a constitution", Body: null,
                PublicationDate: from, StatusNote: null);

        var docs = new[]
        {
            L("2019-01-01", "fr"), L("2020-01-01", "fr"), L("2021-01-01", "fr"),
            L("2024-01-01", "de"), L("2024-01-01", "fr"), L("2024-01-01", "lb"),
        };
        var db2 = Path.Combine(Path.GetTempPath(), $"lex-lang-{Guid.NewGuid():N}.db");
        _extra.Add(db2);
        IndexBuilder.Build(db2, stamp, docs, [], [], [], StampSigner.CreateKeyPem());
        return LexIndexReader.Open(db2);
    }

    private readonly List<string> _extra = [];

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch { /* temp file */ }
        foreach (var f in _extra) { try { File.Delete(f); } catch { /* temp file */ } }
    }
}
