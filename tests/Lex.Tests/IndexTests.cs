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
        public int EncodeCalls { get; private set; }
        public int BatchCalls { get; private set; }
        public List<int> BatchSizes { get; } = [];
        public List<int?> BatchPaddings { get; } = [];
        public long CountedCharacters { get; private set; }
        public long PrefixCharacters { get; private set; }
        public int LargestPrefixInput { get; private set; }
        public int CountTokens(string text)
        {
            CountedCharacters += text.Length;
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 2;
        }
        public void ResetTokenMetrics()
        {
            CountedCharacters = 0;
            PrefixCharacters = 0;
            LargestPrefixInput = 0;
        }
        public int PrefixLengthForTokens(string text, int maxTokens)
        {
            PrefixCharacters += text.Length;
            LargestPrefixInput = Math.Max(LargestPrefixInput, text.Length);
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
            EncodeCalls++;
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
        public IReadOnlyList<float[]> EncodeBatch(
            IReadOnlyList<string> texts, EmbeddingInputKind kind, int? padToTokens = null)
        {
            BatchCalls++;
            BatchSizes.Add(texts.Count);
            BatchPaddings.Add(padToTokens);
            return texts.Select(text => Encode(text, kind)).ToArray();
        }
        public void Dispose() { }
    }

    private sealed class BlockingEncoder : ITextEncoder
    {
        private readonly FakeEncoder _inner = new();
        private int _blocked;
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public string ModelId => _inner.ModelId;
        public string ModelRevision => _inner.ModelRevision;
        public int Dimensions => _inner.Dimensions;
        public int CountTokens(string text) => _inner.CountTokens(text);
        public int PrefixLengthForTokens(string text, int maxTokens)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("test did not release the blocking tokenizer");
            }
            return _inner.PrefixLengthForTokens(text, maxTokens);
        }
        public int SuffixStartForTokens(string text, int maxTokens) =>
            _inner.SuffixStartForTokens(text, maxTokens);
        public float[] Encode(string text, EmbeddingInputKind kind) => _inner.Encode(text, kind);
        public IReadOnlyList<float[]> EncodeBatch(
            IReadOnlyList<string> texts, EmbeddingInputKind kind, int? padToTokens = null) =>
            _inner.EncodeBatch(texts, kind, padToTokens);
        public void Dispose()
        {
            Release.Set();
            Entered.Dispose();
            Release.Dispose();
            _inner.Dispose();
        }
    }

    private static DocRow Row(string key, string group, string from, string? to, string kind = "REG", string? title = null,
                              bool text = false, bool withdrawn = false) =>
        new(key, "t-pub", group, $"urn:{group}", kind, "en", from, to, "publisher",
            "2026-08-01T00:00:00Z", Withdrawn: withdrawn, TextAvailable: text, TextPublic: text,
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
            Row("t-pub:w3:2025-01-01", "w3", "2025-01-01", null, title: "withdrawn secret thing", text: true,
                withdrawn: true),
        };
        var provisions = new[]
        {
            Prov(docs[0], 0, "art_1", "the thing shall apply everywhere"),
            Prov(docs[0], 1, "art_2", "penalties for the thing are mild"),
            Prov(docs[1], 0, "art_1", "the thing shall apply everywhere, revised"),
            Prov(docs[2], 0, "art_1", "a different directive thing entirely"),
            Prov(docs[3], 0, "art_1", "withdrawn secret thing"),
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

    [Fact]
    public void Search_facets_are_derived_from_mounted_index_values()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        var first = Row("t-pub:w1:2020-01-01", "w1", "2020-01-01", null, text: true) with
        {
            Hierarchy = "secondary_law", Domains = "|employment|tax|", ActForm = "REG",
            BindingStatus = "in_force",
        };
        var second = Row("t-pub:w2:2020-01-01", "w2", "2020-01-01", null, text: true) with
        {
            Domains = "|tax|consumer|", ActForm = "DIR", BindingStatus = "in_force",
        };
        IndexBuilder.Build(_db, stamp, [first, second],
            [Prov(first, 0, "art_1", "first"), Prov(second, 0, "art_1", "second")],
            [], [], StampSigner.CreateKeyPem());

        using var reader = LexIndexReader.Open(_db);
        var facets = reader.SearchFacets();

        Assert.Equal(["en"], facets.Languages);
        Assert.Equal(["secondary_law"], facets.Hierarchies);
        Assert.Equal(["consumer", "employment", "tax"], facets.Domains);
        Assert.Equal(["DIR", "REG"], facets.ActForms);
        Assert.Equal(["in_force"], facets.BindingStatuses);
    }

    [Fact]
    public void Provision_history_defaults_deterministically_and_can_select_a_language()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "t-pub", ["tier"] = "A", ["history_begins"] = "publisher",
            ["built_at"] = "2026-08-01T00:00:00Z", ["corpus_commit"] = "test",
        };
        var states = new[]
        {
            new ProvisionStateRow("w1", "en", true, "art_1", "2020-01-01", "2020-12-31", "en-1", null, null, false),
            new ProvisionStateRow("w1", "en", true, "art_1", "2021-01-01", null, "en-2", null, null, false),
            new ProvisionStateRow("w1", "fr", false, "art_1", "2020-01-01", null, "fr-1", null, null, false),
        };
        var events = new[]
        {
            new AnchorEventRow("w1", "en", true, "inserted", null, null, "art_1", "en-1", "2020-01-01"),
            new AnchorEventRow("w1", "fr", false, "inserted", null, null, "art_1", "fr-1", "2020-01-01"),
        };
        IndexBuilder.Build(_db, stamp, [], [], [], [], StampSigner.CreateKeyPem(), states, events);

        using var reader = LexIndexReader.Open(_db);
        Assert.All(reader.ProvisionStates("w1", "art_1"), state => Assert.Equal("en", state.Language));
        Assert.Single(reader.ProvisionStates("w1", "art_1", "fr"));
        Assert.All(reader.AnchorEvents("w1", "art_1", "fr"), e => Assert.Equal("fr", e.Language));
    }

    [Fact]
    public void Withdrawn_records_are_not_public_search_or_catalogue_candidates()
    {
        using var r = Build();

        Assert.Empty(r.Search("withdrawn secret", FilterSet.All, 10));
        Assert.Empty(r.SearchWorksByIdentifierOrTitle("w3", FilterSet.All, 10));
        Assert.Null(r.AsOf("w3", new DateOnly(2025, 2, 1), FilterSet.All));
        Assert.DoesNotContain(r.GroupsPage(10, 0, FilterSet.All), row => row.GroupKey == "w3");
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
        Assert.Equal("32768", reader.Stamp["embedding_max_batch_tokens"]);
        Assert.Empty(reader.Search("employment termination", FilterSet.All, 10));
        var result = reader.SearchHybrid("employment termination", FilterSet.All, 10);
        Assert.Equal("hybrid", result.RetrievalMode);
        Assert.Equal("employment", result.Hits[0].Doc.GroupKey);
        Assert.Contains("semantic", result.Hits[0].MatchReasons);
    }

    [Fact]
    public void Identical_chunks_share_one_embedding_and_vector_ordinal()
    {
        var first = Row("t-pub:first:2020-01-01", "first", "2020-01-01", null, text: true);
        var second = Row("t-pub:second:2020-01-01", "second", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new FakeEncoder();
        var progress = new List<SemanticBuildProgress>();
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [first, second],
            [Prov(first, 0, "art_1", "shared exact wording"),
             Prov(second, 0, "art_9", "shared exact wording")],
            [], [], null, semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", Progress: progress.Add));

        Assert.Equal(1, encoder.EncodeCalls);
        Assert.Equal(1, encoder.BatchCalls);
        AssertStage(progress, SemanticBuildStage.Preparation, 1);
        AssertStage(progress, SemanticBuildStage.Embeddings, 1);
        AssertStage(progress, SemanticBuildStage.Database, 4);
        AssertStage(progress, SemanticBuildStage.Finalization, 3);
        using var vectorReader = new SemanticVectorReader(vectors);
        Assert.Equal(1, vectorReader.Count);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COUNT(DISTINCT vector_ordinal) FROM semantic_chunks";
        using var result = command.ExecuteReader();
        Assert.True(result.Read());
        Assert.Equal(2, result.GetInt32(0));
        Assert.Equal(1, result.GetInt32(1));

        using var reader = LexIndexReader.Open(_db, encoder, vectors);
        var hybrid = reader.SearchHybrid("shared exact wording", FilterSet.All, 10);
        Assert.Equal(["first", "second"], hybrid.Hits.Select(hit => hit.Doc.GroupKey).Order());

        static void AssertStage(
            IReadOnlyList<SemanticBuildProgress> updates, SemanticBuildStage stage, long total)
        {
            var stageUpdates = updates.Where(update => update.Stage == stage).ToList();
            Assert.NotEmpty(stageUpdates);
            Assert.Equal(0, stageUpdates[0].Completed);
            Assert.Equal(total, stageUpdates[0].Total);
            Assert.Null(stageUpdates[0].EstimatedRemaining);
            Assert.Equal(total, stageUpdates[^1].Completed);
            Assert.Equal(total, stageUpdates[^1].Total);
            Assert.Equal(100, stageUpdates[^1].Percent);
            Assert.Equal(TimeSpan.Zero, stageUpdates[^1].EstimatedRemaining);
        }
    }

    [Fact]
    public async Task Preparation_heartbeat_reports_the_current_item_before_it_finishes()
    {
        var doc = Row("t-pub:heartbeat:2020-01-01", "heartbeat", "2020-01-01", null, text: true);
        var provision = Prov(doc, 0, "annex_1", "one deliberately blocked preparation item");
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new BlockingEncoder();
        var progress = new System.Collections.Concurrent.ConcurrentQueue<SemanticBuildProgress>();
        var build = Task.Run(() => IndexBuilder.Build(
            _db,
            new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], [provision], [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha",
                Progress: progress.Enqueue,
                ProgressHeartbeatInterval: TimeSpan.FromMilliseconds(20))));

        try
        {
            Assert.True(encoder.Entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(
                () => progress.Any(update => update.IsHeartbeat), TimeSpan.FromSeconds(2)));
            var heartbeat = progress.First(update => update.IsHeartbeat);
            Assert.Equal(SemanticBuildStage.Preparation, heartbeat.Stage);
            Assert.Equal(0, heartbeat.Completed);
            Assert.Equal(1, heartbeat.Total);
            Assert.Equal(provision.TextSha, heartbeat.CurrentItem);
            Assert.Equal(provision.TextMd.Length, heartbeat.CurrentItemCharacters);
            Assert.True(heartbeat.CurrentItemElapsed > TimeSpan.Zero);
            Assert.Null(heartbeat.EstimatedRemaining);
        }
        finally
        {
            encoder.Release.Set();
        }
        await build;
    }

    [Fact]
    public void Semantic_embeddings_are_batched_without_changing_vector_ordinals()
    {
        var doc = Row("t-pub:batch:2020-01-01", "batch", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new FakeEncoder();
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc],
            [Prov(doc, 0, "art_1", "first distinct wording"),
             Prov(doc, 1, "art_2", "second distinct wording"),
             Prov(doc, 2, "art_3", "third distinct wording")],
            [], [], null, semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", BatchSize: 2));

        Assert.Equal(3, encoder.EncodeCalls);
        Assert.Equal(2, encoder.BatchCalls);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT vector_ordinal FROM semantic_chunks ORDER BY state_id";
        using var result = command.ExecuteReader();
        var ordinals = new List<long>();
        while (result.Read()) ordinals.Add(result.GetInt64(0));
        Assert.Equal([0L, 1L, 2L], ordinals);
    }

    [Fact]
    public void Semantic_embeddings_use_stable_token_buckets_only_after_chunking()
    {
        var doc = Row("t-pub:buckets:2020-01-01", "buckets", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        var shortText = "short provision";
        var mediumText = string.Join(' ', Enumerable.Repeat("medium", 40));
        var longText = string.Join(' ', Enumerable.Repeat("long", 90));
        using var encoder = new FakeEncoder();

        Assert.Equal(shortText, Assert.Single(SemanticChunker.Split(shortText, encoder)).Text);
        Assert.Equal(mediumText, Assert.Single(SemanticChunker.Split(mediumText, encoder)).Text);
        Assert.Equal(longText, Assert.Single(SemanticChunker.Split(longText, encoder)).Text);

        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc],
            [Prov(doc, 0, "art_1", shortText),
             Prov(doc, 1, "art_2", mediumText),
             Prov(doc, 2, "art_3", longText)],
            [], [], null, semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", BatchSize: 16));

        Assert.Equal([32, 64, 128], encoder.BatchPaddings);
    }

    [Fact]
    public void Semantic_embedding_batches_are_bounded_by_padded_token_budget()
    {
        var doc = Row("t-pub:token-budget:2020-01-01", "token-budget", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new FakeEncoder();
        var provisions = Enumerable.Range(0, 130)
            .Select(index => Prov(doc, index, $"art_{index + 1}",
                string.Join(' ', Enumerable.Repeat($"word{index}", 200))))
            .ToArray();

        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], provisions, [], [], null, semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha",
                BatchSize: 256, MaxBatchTokens: 32_768));

        Assert.Equal([128, 2], encoder.BatchSizes);
        Assert.All(encoder.BatchPaddings, padding => Assert.Equal(256, padding));
    }

    [Fact]
    public void Semantic_embedding_cache_resumes_by_chunk_and_provider_without_changing_artifact_bytes()
    {
        var doc = Row("t-pub:cache:2020-01-01", "cache", "2020-01-01", null, text: true);
        var provisions = new[]
        {
            Prov(doc, 0, "art_1", "first cached wording"),
            Prov(doc, 1, "art_2", "second cached wording"),
        };
        var vectors1 = Path.ChangeExtension(_db, ".first.vectors");
        var db2 = Path.Combine(Path.GetTempPath(), $"lex-cache-test-{Guid.NewGuid():N}.db");
        var vectors2 = Path.ChangeExtension(db2, ".vectors");
        var db3 = Path.Combine(Path.GetTempPath(), $"lex-cache-provider-test-{Guid.NewGuid():N}.db");
        var vectors3 = Path.ChangeExtension(db3, ".vectors");
        var db4 = Path.Combine(Path.GetTempPath(), $"lex-cache-profile-test-{Guid.NewGuid():N}.db");
        var vectors4 = Path.ChangeExtension(db4, ".vectors");
        var cache = Path.Combine(Path.GetTempPath(), $"lex-embeddings-{Guid.NewGuid():N}.db");
        _extra.AddRange([
            vectors1, db2, vectors2, db3, vectors3, db4, vectors4,
            cache, cache + "-wal", cache + "-shm"
        ]);

        using (var first = new FakeEncoder())
        {
            IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
                [doc], provisions, [], [], null, semantic: new SemanticBuildOptions(
                    first, vectors1, "model-sha", "tokenizer-sha", BatchSize: 2,
                    ExecutionProvider: "cpu;runtime=test", EmbeddingCachePath: cache));
            Assert.Equal(2, first.EncodeCalls);
        }

        using (var resumed = new FakeEncoder())
        {
            IndexBuilder.Build(db2, new Dictionary<string, string> { ["collection"] = "t-pub" },
                [doc], provisions, [], [], null, semantic: new SemanticBuildOptions(
                    resumed, vectors2, "model-sha", "tokenizer-sha", BatchSize: 2,
                    ExecutionProvider: "cpu;runtime=test", EmbeddingCachePath: cache));
            Assert.Equal(0, resumed.EncodeCalls);
        }
        Assert.Equal(File.ReadAllBytes(vectors1), File.ReadAllBytes(vectors2));

        using var otherProvider = new FakeEncoder();
        IndexBuilder.Build(db3, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], provisions, [], [], null, semantic: new SemanticBuildOptions(
                otherProvider, vectors3, "model-sha", "tokenizer-sha", BatchSize: 2,
                ExecutionProvider: "directml:1;runtime=test", EmbeddingCachePath: cache));
        Assert.Equal(2, otherProvider.EncodeCalls);

        using var otherProfile = new FakeEncoder();
        IndexBuilder.Build(db4, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], provisions, [], [], null, semantic: new SemanticBuildOptions(
                otherProfile, vectors4, "model-sha", "tokenizer-sha", BatchSize: 2,
                ExecutionProvider: "cpu;runtime=test", EmbeddingCachePath: cache,
                EmbeddingProfile: "test-profile/other"));
        Assert.Equal(2, otherProfile.EncodeCalls);
    }

    [Fact]
    public void Semantic_embedding_batch_size_must_be_positive()
    {
        var doc = Row("t-pub:batch:2020-01-01", "batch", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new FakeEncoder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => IndexBuilder.Build(
            _db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], [Prov(doc, 0, "art_1", "wording")], [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", BatchSize: 0)));

        Assert.Contains("batch size", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, encoder.EncodeCalls);
    }

    [Fact]
    public void Semantic_embedding_batch_token_budget_must_be_positive()
    {
        var doc = Row("t-pub:batch-budget:2020-01-01", "batch-budget", "2020-01-01", null, text: true);
        var vectors = Path.ChangeExtension(_db, ".vectors");
        _extra.Add(vectors);
        using var encoder = new FakeEncoder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => IndexBuilder.Build(
            _db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [doc], [Prov(doc, 0, "art_1", "wording")], [], [], null,
            semantic: new SemanticBuildOptions(
                encoder, vectors, "model-sha", "tokenizer-sha", MaxBatchTokens: 0)));

        Assert.Contains("token budget", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, encoder.EncodeCalls);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(384)]
    public void Semantic_vector_hamming_reads_complete_blocks_and_tail(int dimensions)
    {
        var vectors = Path.ChangeExtension(_db, $".{dimensions}.vectors");
        _extra.Add(vectors);
        var stored = Enumerable.Range(0, dimensions)
            .Select(i => i % 3 == 0 ? -0.5f : 0.5f).ToArray();
        var query = Enumerable.Range(0, dimensions)
            .Select(i => i % 5 == 0 ? -0.5f : 0.5f).ToArray();
        using (var writer = new SemanticVectorWriter(vectors, dimensions))
            Assert.Equal(0, writer.Write(stored));

        using var reader = new SemanticVectorReader(vectors);
        var expected = Enumerable.Range(0, dimensions)
            .Count(i => (stored[i] >= 0) != (query[i] >= 0));
        Assert.Equal(expected, reader.HammingDistance(0, SemanticVectorReader.Binary(query)));
    }

    [Fact]
    public void Semantic_vector_scan_returns_a_bounded_deterministic_candidate_set()
    {
        var vectors = Path.ChangeExtension(_db, ".nearest.vectors");
        _extra.Add(vectors);
        using (var writer = new SemanticVectorWriter(vectors, 4))
        {
            writer.Write([1, 1, 1, 1]);
            writer.Write([-1, 1, 1, 1]);
            writer.Write([-1, -1, 1, 1]);
            writer.Write([-1, -1, -1, 1]);
        }

        using var reader = new SemanticVectorReader(vectors);
        var nearest = reader.NearestByHamming(SemanticVectorReader.Binary([1, 1, 1, 1]), 2);
        Assert.Equal([(0L, 0), (1L, 1)], nearest);
    }

    [Fact]
    public void Official_uppercase_celex_resolves_the_normalized_corpus_work()
    {
        var doc = Row("eu-eurlex:32006l0112:2025-01-01", "32006l0112", "2025-01-01", null, text: true);
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "eu-eurlex" },
            [doc], [Prov(doc, 0, "art_1", "value added tax")], [], [], null);

        using var reader = LexIndexReader.Open(_db);
        Assert.True(reader.WorkExists("32006L0112"));
        Assert.NotNull(reader.AsOf("eu-eurlex:32006L0112", new DateOnly(2026, 8, 7), FilterSet.All));
        Assert.Single(reader.Timeline("32006L0112"));
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
    public void Exact_treaty_celex_matches_the_publisher_identifier_with_a_slash()
    {
        var treaty = Row("t-pub:12012e-txt:2012-10-26", "12012e-txt", "2012-10-26", null,
            kind: "TREATY", title: "Treaty on the Functioning of the European Union") with
        {
            GroupIdentifier = "http://publications.europa.eu/resource/celex/12012E/TXT",
        };
        IndexBuilder.Build(_db, new Dictionary<string, string> { ["collection"] = "t-pub" },
            [treaty], [], [], [], null);
        using var reader = LexIndexReader.Open(_db);

        var result = reader.SearchKeyword("12012E/TXT", FilterSet.All, 10, fuzzyAuto: true);
        Assert.Equal("12012e-txt", Assert.Single(result.Hits).Doc.GroupKey);
        Assert.Equal(["exact_identifier"], result.Hits[0].MatchReasons);
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
    public void Linear_chunk_preparation_preserves_the_existing_chunk_contract()
    {
        using var encoder = new FakeEncoder();
        var paragraph = string.Join(' ', Enumerable.Range(0, 900).Select(i => $"term{i}"));
        var cases = new[]
        {
            "short provision",
            paragraph,
            $"short opening\n\n{paragraph}\n\nshort closing",
            string.Join("\n\n", Enumerable.Repeat("a compact legal paragraph", 150)),
        };

        foreach (var text in cases)
            Assert.Equal(LegacyChunkTexts(text, encoder),
                SemanticChunker.Split(text, encoder).Select(chunk => chunk.Text));

        static IReadOnlyList<string> LegacyChunkTexts(string text, ITextEncoder encoder)
        {
            if (encoder.CountTokens("passage: " + text) <= SemanticChunker.MaxTokens)
                return [text];
            var pieces = new List<string>();
            foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var rest = paragraph;
                while (encoder.CountTokens("passage: " + rest) > SemanticChunker.MaxTokens)
                {
                    var take = Math.Max(1, encoder.PrefixLengthForTokens(
                        "passage: " + rest, SemanticChunker.MaxTokens) - "passage: ".Length);
                    take = Math.Min(take, rest.Length);
                    pieces.Add(rest[..take].Trim());
                    var overlapStart = encoder.SuffixStartForTokens(
                        rest[..take], SemanticChunker.OverlapTokens);
                    rest = rest[overlapStart..].Trim();
                }
                if (rest.Length > 0) pieces.Add(rest);
            }

            var chunks = new List<string>();
            var current = new List<string>();
            foreach (var piece in pieces)
            {
                var candidate = string.Join("\n\n", current.Append(piece));
                if (current.Count > 0
                    && encoder.CountTokens("passage: " + candidate) > SemanticChunker.MaxTokens)
                {
                    chunks.Add(string.Join("\n\n", current));
                    var overlap = TakeWhileAccumulated(current.AsEnumerable().Reverse(),
                        part => encoder.CountTokens(part), SemanticChunker.OverlapTokens).Reverse().ToList();
                    current = overlap.Count > 0
                        && encoder.CountTokens("passage: " + string.Join("\n\n", overlap.Append(piece)))
                            <= SemanticChunker.MaxTokens
                        ? overlap : [];
                }
                current.Add(piece);
            }
            if (current.Count > 0) chunks.Add(string.Join("\n\n", current));
            return chunks;

            static IEnumerable<T> TakeWhileAccumulated<T>(
                IEnumerable<T> values, Func<T, int> cost, int maximum)
            {
                var total = 0;
                foreach (var value in values)
                {
                    var next = cost(value);
                    if (total + next > maximum) yield break;
                    total += next;
                    yield return value;
                }
            }
        }
    }

    [Fact]
    public void Oversized_single_paragraph_does_not_rescan_every_remaining_suffix()
    {
        using var encoder = new FakeEncoder();
        var text = string.Join(' ', Enumerable.Repeat("annex", 20_000));
        encoder.ResetTokenMetrics();

        var chunks = SemanticChunker.Split(text, encoder);

        Assert.True(chunks.Count > 50);
        Assert.True(encoder.CountedCharacters < text.Length * 10L,
            $"Chunk preparation rescanned {encoder.CountedCharacters:N0} characters for a {text.Length:N0}-character provision.");
        Assert.True(encoder.PrefixCharacters < text.Length * 4L,
            $"Prefix probes copied {encoder.PrefixCharacters:N0} characters for a {text.Length:N0}-character provision.");
        Assert.True(encoder.LargestPrefixInput <= 4_096 + "passage: ".Length,
            $"A prefix probe received {encoder.LargestPrefixInput:N0} characters.");
        Assert.All(chunks, chunk => Assert.True(
            encoder.CountTokens("passage: " + chunk.Text) <= SemanticChunker.MaxTokens));
    }

    [Fact]
    public void Multi_megabyte_annex_uses_bounded_prefix_windows()
    {
        using var encoder = new FakeEncoder();
        var text = string.Join(' ', Enumerable.Repeat("annex", 500_000));
        encoder.ResetTokenMetrics();

        var chunks = SemanticChunker.Split(text, encoder);

        Assert.True(chunks.Count > 1_000);
        Assert.True(encoder.PrefixCharacters < text.Length * 4L);
        Assert.True(encoder.LargestPrefixInput <= 4_096 + "passage: ".Length);
    }

    [Fact]
    public void Empty_extracted_text_remains_a_deterministic_chunk()
    {
        using var encoder = new FakeEncoder();

        var chunk = Assert.Single(SemanticChunker.Split("", encoder));

        Assert.Equal("", chunk.Text);
        Assert.Equal(0, chunk.Index);
        Assert.Equal(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData([])), chunk.Sha256);
    }

    /// <summary>
    /// Reproduces the boundary combination returned by the pinned SentencePiece tokenizer for
    /// CRR Article 261 (text sha e1bc517d...): the prefix stops six characters before the end,
    /// but that prefix is shorter than the requested overlap so its suffix begins at zero.
    /// </summary>
    private sealed class ZeroOverlapBoundaryEncoder : ITextEncoder
    {
        public string ModelId => "test/non-progress";
        public string ModelRevision => "1";
        public int Dimensions => 1;
        public int CountTokens(string text) => 2;
        public int PrefixLengthForTokens(string text, int maxTokens) => text.Length == 75 ? 69 : text.Length;
        public int SuffixStartForTokens(string text, int maxTokens) => 0;
        public float[] Encode(string text, EmbeddingInputKind kind) => [1f];
        public void Dispose() { }
    }

    [Fact]
    public void A_zero_overlap_boundary_still_advances_without_losing_text()
    {
        using var encoder = new ZeroOverlapBoundaryEncoder();
        var text = new string('x', 66);

        var chunks = SemanticChunker.Split(text, encoder);

        Assert.Equal(text, Assert.Single(chunks).Text.Replace("\n\n", "", StringComparison.Ordinal));
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
    // /browse promised "Browse everything" and delivered seven curated links against thousands of works.
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
        Assert.True(amended.TextComparable);
        Assert.Equal(2, reissued.VersionsInPeriod);
        Assert.Equal(1, reissued.DistinctTexts);     // two versions, one wording
        Assert.True(reissued.TextComparable);
    }

    [Fact]
    public void Period_filters_use_mounted_v3_metadata_before_ranking_and_counting()
    {
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = "eu-test", ["jurisdiction"] = "EU", ["tier"] = "A",
            ["history_begins"] = "publisher", ["built_at"] = "2026-08-01T00:00:00Z",
            ["corpus_commit"] = "test",
        };
        var finance = Row("eu-test:reg:2025-01-01", "reg", "2025-01-01", null,
            kind: "REG", title: "Finance regulation", text: true) with
        {
            Hierarchy = "secondary_eu_law", Domains = "|financial-services|", ActForm = "REG",
            BindingStatus = "in_force",
        };
        var treaty = Row("eu-test:treaty:2025-01-01", "treaty", "2025-01-01", null,
            kind: "TREATY", title: "Treaty", text: true) with
        {
            Hierarchy = "primary_eu_law", Domains = "|institutional|", ActForm = "TREATY",
            BindingStatus = "in_force",
        };
        IndexBuilder.Build(_db, stamp, [finance, treaty],
            [Prov(finance, 0, "art_1", "capital rule"), Prov(treaty, 0, "art_1", "institutional rule")],
            [], [], StampSigner.CreateKeyPem());

        using var reader = LexIndexReader.Open(_db);
        var filter = new FilterSet(null, null, null, "en", null,
            "secondary_eu_law", "REG", "in_force", "financial-services");
        var rows = reader.ChangesInPeriod("2025-01-01", "2025-12-31", null, true, 25, 0, filter);
        var totals = reader.ChangeTotals("2025-01-01", "2025-12-31", null, filter);

        var row = Assert.Single(rows);
        Assert.Equal("reg", row.GroupKey);
        Assert.Equal("secondary_eu_law", row.Hierarchy);
        Assert.Equal("|financial-services|", row.Domains);
        Assert.Equal((1, 1), totals);
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
