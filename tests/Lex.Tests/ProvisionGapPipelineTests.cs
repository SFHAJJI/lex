using Lex.Derive;
using Lex.Ask;
using Lex.Ingest;
using Lex.Index;
using Lex.Mcp;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Tests;

public sealed class ProvisionGapPipelineTests : IDisposable
{
    private readonly string _db = Path.Combine(
        Path.GetTempPath(), $"lex-gap-{Guid.NewGuid():N}.db");

    [Fact]
    public void Dedicated_gap_table_carries_coordinates_without_text_or_hash()
    {
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db,
            Stamp(),
            [document],
            [Provision(document, 0, "art_1", "Safe synthetic wording.")],
            [],
            [],
            StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(document, 1, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using var reader = LexIndexReader.Open(_db);
        Assert.True(reader.SignatureValid);
        Assert.Equal(2, reader.ProvisionCount(LexIndexReader.RidOf(document)));
        var gap = Assert.Single(reader.ProvisionGaps(LexIndexReader.RidOf(document)));
        Assert.Equal("art_2", gap.Anchor);
        Assert.Equal(ProvisionGapReason.MarkerOnly, gap.TextUnavailableReason);
        Assert.Equal(["art_1"],
            reader.ProvisionAnchors(LexIndexReader.RidOf(document), 10));
        Assert.Equal(["art_1", "art_2"],
            reader.ServingProvisionAnchors(LexIndexReader.RidOf(document), 10));

        using var connection = new SqliteConnection($"Data Source={_db};Mode=ReadOnly");
        connection.Open();
        using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT name FROM pragma_table_info('provision_gaps') ORDER BY cid";
        using var rows = columns.ExecuteReader();
        var names = new List<string>();
        while (rows.Read()) names.Add(rows.GetString(0));
        Assert.Equal(
            ["rid", "seq", "anchor", "provision_id", "eli", "ptype", "num", "heading",
                "path", "article_valid_from", "text_unavailable_reason"],
            names);
        Assert.DoesNotContain(names, name => name.Contains("text_md", StringComparison.Ordinal)
            || name.Contains("text_sha", StringComparison.Ordinal));

        foreach (var table in new[] { "fts", "lexical_states", "semantic_chunks" })
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table}";
            var expected = table == "semantic_chunks" ? 0 : 1;
            Assert.Equal(expected, Convert.ToInt32(count.ExecuteScalar()));
        }
    }

    [Fact]
    public void Gap_capability_is_signed_even_when_the_audited_count_is_zero()
    {
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db, Stamp(), [document],
            [Provision(document, 0, "art_1", "Safe synthetic wording.")], [], [],
            StampSigner.CreateKeyPem(), provisionGaps: GapInput([]));

        using var reader = LexIndexReader.Open(_db);
        Assert.Equal("lex-index/4", reader.Stamp["schema"]);
        Assert.Equal(DerivationGeneration.Canon2, reader.Stamp["articles_canon"]);
        Assert.Equal("lex-provision-gap/1", reader.Stamp["provision_gap_schema"]);
        Assert.Equal("0", reader.Stamp["provision_gap_rows"]);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([])),
            reader.Stamp["provision_gap_sha256"]);

        var result = Assert.IsType<JsonObject>(new McpCore(
            new Dictionary<string, LexIndexReader> { ["t-pub"] = reader })
            .CallTool("as_of", new JsonObject
            {
                ["work"] = "t-pub:work",
                ["date"] = "2025-01-01",
                ["mode"] = "outline",
            }));
        Assert.Equal("complete", result["text_completeness"]!.GetValue<string>());
        Assert.Equal(1, result["total_text_provisions"]!.GetValue<int>());
        Assert.Equal(0, result["total_provision_gaps"]!.GetValue<int>());
    }

    [Fact]
    public void Verifier_binds_the_expected_articles_canon()
    {
        IndexBuilder.Build(
            _db, Stamp(), [Document(textPublic: true)], [], [], [],
            StampSigner.CreateKeyPem(), provisionGaps: GapInput([]));

        var accepted = IndexStampVerifier.Verify(_db,
            new IndexStampVerificationInputs(
                ExpectedArticlesCanon: DerivationGeneration.Canon2));
        Assert.True(accepted.IsValid);

        var mismatched = IndexStampVerifier.Verify(_db,
            new IndexStampVerificationInputs(
                ExpectedArticlesCanon: DerivationGeneration.Canon1));
        Assert.False(mismatched.IsValid);
        Assert.Contains(mismatched.ProvenanceErrors, error =>
            error.Contains("articles_canon does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_verifier_treats_generation_three_as_canon_one_without_a_stamp()
    {
        IndexBuilder.Build(
            _db, LegacyStamp(), [Document(textPublic: true)], [], [], [],
            StampSigner.CreateKeyPem());

        var accepted = IndexStampVerifier.Verify(_db,
            new IndexStampVerificationInputs(
                ExpectedArticlesCanon: DerivationGeneration.Canon1));
        Assert.True(accepted.IsValid);

        var mismatched = IndexStampVerifier.Verify(_db,
            new IndexStampVerificationInputs(
                ExpectedArticlesCanon: DerivationGeneration.Canon2));
        Assert.False(mismatched.IsValid);
        Assert.Contains(mismatched.ProvenanceErrors, error =>
            error.Contains("stamp articles_canon is absent", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_generation_remains_schema_three_and_implies_canon_one()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"lex-generation-{Guid.NewGuid():N}");
        try
        {
            UpdateGeneration(root, "legacy", [AknLuProfile.ProfileId]);

            using var document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root, DerivationGeneration.FileName)));
            Assert.Equal(DerivationGeneration.PreviousSchemaId,
                document.RootElement.GetProperty("schema").GetString());
            Assert.False(document.RootElement.GetProperty("publishers")
                .GetProperty("legacy").TryGetProperty("articles_canon", out _));
            Assert.Equal(DerivationGeneration.Canon1,
                DerivationGeneration.ReadPublisher(root, "legacy").ArticlesCanon);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Canon_two_upgrades_the_generation_without_relabelling_legacy_entries()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"lex-generation-{Guid.NewGuid():N}");
        try
        {
            UpdateGeneration(root, "legacy", [AknLuProfile.ProfileId]);
            UpdateGeneration(root, "marker", [AknLuProfileV3.ProfileId],
                DerivationGeneration.Canon2);

            using var document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root, DerivationGeneration.FileName)));
            Assert.Equal(DerivationGeneration.SchemaId,
                document.RootElement.GetProperty("schema").GetString());
            var publishers = document.RootElement.GetProperty("publishers");
            Assert.Equal(DerivationGeneration.Canon1,
                publishers.GetProperty("legacy").GetProperty("articles_canon").GetString());
            Assert.Equal(DerivationGeneration.Canon2,
                publishers.GetProperty("marker").GetProperty("articles_canon").GetString());
            Assert.Equal(DerivationGeneration.Canon1,
                DerivationGeneration.ReadPublisher(root, "legacy").ArticlesCanon);
            Assert.Equal(DerivationGeneration.Canon2,
                DerivationGeneration.ReadPublisher(root, "marker").ArticlesCanon);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("canon/unknown")]
    public void Generation_four_rejects_missing_or_unknown_articles_canon(
        string? replacement)
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"lex-generation-{Guid.NewGuid():N}");
        try
        {
            UpdateGeneration(root, "marker", [AknLuProfileV3.ProfileId],
                DerivationGeneration.Canon2);
            var path = Path.Combine(root, DerivationGeneration.FileName);
            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var publisher = document["publishers"]!["marker"]!.AsObject();
            if (replacement is null)
                publisher.Remove("articles_canon");
            else
                publisher["articles_canon"] = replacement;
            File.WriteAllText(path, document.ToJsonString());

            var error = Assert.Throws<InvalidDataException>(() =>
                DerivationGeneration.ReadPublisher(root, "marker"));
            Assert.Contains("articles_canon", error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Gap_capability_requires_canon_two_generation_evidence()
    {
        var missingError = Assert.Throws<InvalidDataException>(() =>
            ProvisionGapIndexInput.FromGenerationEvidence(
                null, GenerationSha256, ArticlesCommit, []));
        Assert.Contains("canon/2", missingError.Message, StringComparison.Ordinal);

        var legacyError = Assert.Throws<InvalidDataException>(() =>
            ProvisionGapIndexInput.FromGenerationEvidence(
                DerivationGeneration.Canon1,
                GenerationSha256, ArticlesCommit, []));
        Assert.Contains("canon/2", legacyError.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            ProvisionGapIndexInput.FromGenerationEvidence(
                DerivationGeneration.Canon2,
                "not-a-digest", ArticlesCommit, []));
        Assert.Throws<InvalidDataException>(() =>
            ProvisionGapIndexInput.FromGenerationEvidence(
                DerivationGeneration.Canon2,
                GenerationSha256, "not-a-commit", []));

        var accepted = ProvisionGapIndexInput.FromGenerationEvidence(
            DerivationGeneration.Canon2,
            GenerationSha256, ArticlesCommit, []);
        Assert.Equal(GenerationSha256, accepted.GenerationSha256);
        Assert.Equal(ArticlesCommit, accepted.ArticlesCommit);

        var downgradeError = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, Stamp(), [Document(textPublic: true)], [], [], [],
            StampSigner.CreateKeyPem()));
        Assert.Contains("gap-aware index", downgradeError.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Gap_capability_must_match_the_signed_generation_and_articles_identity()
    {
        var input = GapInput([]);

        foreach (var missing in new[] { "generation_sha256", "articles_commit" })
        {
            var stamp = Stamp();
            stamp.Remove(missing);
            var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
                _db, stamp, [Document(textPublic: true)], [], [], [],
                StampSigner.CreateKeyPem(), provisionGaps: input));
            Assert.Contains(missing, error.Message, StringComparison.Ordinal);
        }

        foreach (var mismatch in new[] { "generation_sha256", "articles_commit" })
        {
            var stamp = Stamp();
            stamp[mismatch] = mismatch == "generation_sha256"
                ? new string('a', 64)
                : new string('b', 40);
            var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
                _db, stamp, [Document(textPublic: true)], [], [], [],
                StampSigner.CreateKeyPem(), provisionGaps: input));
            Assert.Contains(mismatch, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gap_identity_validation_and_signing_use_one_immutable_stamp_snapshot()
    {
        var validated = Stamp();
        var enumerated = Stamp();
        enumerated["generation_sha256"] = new string('a', 64);
        var splitView = new SplitViewStamp(validated, enumerated);

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, splitView, [Document(textPublic: true)], [], [], [],
            StampSigner.CreateKeyPem(), provisionGaps: GapInput([])));

        Assert.Contains("generation_sha256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_gap_bearing_article_requires_exact_coordinate_source_and_profile()
    {
        const string lexId = "lu-legilux:work:2025-01-01";
        const string versionKey = "2025-01-01--synthetic";
        const string observationFile = "fr--synthetic.xml";
        const string observationSha =
            "1111111111111111111111111111111111111111111111111111111111111111";
        const string sourceUri = "https://publisher.example/synthetic.xml";
        ObservationEntry Observation(
            string file,
            string? format = null,
            string? attemptOutcome = null) => new()
        {
            File = file,
            Sha256 = observationSha,
            SourceUri = sourceUri,
            RetrievedAt = "2026-08-28T00:00:00Z",
            ObservedFrom = "2026-08-28T00:00:00Z",
            Format = format,
            Http = attemptOutcome is null ? null : new HttpObservationEvidence
            {
                StatusCode = 500,
                FetchedAt = "2026-08-28T00:00:00Z",
                AttemptOutcome = attemptOutcome,
                Attempts = 1,
            },
        };
        var firstObservation = Observation("first.xml");
        var nestedObservation = Observation("fr.xml/synthetic.xml");
        var unsupportedObservation = Observation("synthetic.pdf", format: "pdf");
        var alternateStreamObservation = Observation("fr:body.xml");
        var failedObservation = Observation("failed.xml",
            attemptOutcome: "body_parser_failure");
        var selectedObservation = Observation(observationFile);
        var meta = new VersionMeta
        {
            LexId = lexId,
            WorkIdentifier = "urn:synthetic",
            Publisher = "lu-legilux",
            ValidFrom = "2025-01-01",
            ValidTimeSource = "publisher",
        };
        var expression = new ExpressionMeta
        {
            Language = "fr",
            ValidFrom = "2025-01-01",
            ValidTo = null,
            ValidTimeSource = "publisher",
            Text = new TextInfo { Available = true },
            Observations =
            [
                firstObservation,
                nestedObservation,
                unsupportedObservation,
                alternateStreamObservation,
                failedObservation,
                selectedObservation,
            ],
        };
        foreach (var invalidLeaf in new[]
                 { "fr:body.xml", "fr?.xml", "fr*.xml", "fr|body.xml", "fr<1>.xml" })
            Assert.Null(PrimaryBodyObservationSelector.Select(
                new[] { Observation(invalidLeaf) },
                observation => new PrimaryBodyObservationShape(
                    observation.File,
                    observation.Format,
                    observation.Http is not null,
                    observation.Http?.AttemptOutcome)));
        Assert.Same(selectedObservation, PrimaryBodyObservationSelector.Select(
            expression.Observations,
            observation => new PrimaryBodyObservationShape(
                observation.File,
                observation.Format,
                observation.Http is not null,
                observation.Http?.AttemptOutcome)));
        var valid = new JsonObject
        {
            ["schema"] = DeriveWriter.SchemaId,
            ["lex_id"] = lexId,
            ["language"] = "fr",
            ["valid_from"] = "2025-01-01",
            ["valid_to"] = null,
            ["valid_time_source"] = "publisher",
            ["derived_from"] = new JsonObject
            {
                ["corpus_repo"] = "lex-corpus-lu-legilux",
                ["path"] = $"works/work/versions/{versionKey}/{observationFile}",
                ["sha256"] = observationSha,
                ["source_uri"] = sourceUri,
            },
            ["generator"] = new JsonObject
            {
                ["profile"] = AknLuProfileV3.ProfileId,
                ["tool"] = "lex derive",
            },
        };

        static JsonElement Element(JsonObject value) =>
            JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();
        var validator = typeof(IndexFromCorpus).GetMethod(
            "ValidateGapBearingDerivedArticle",
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic)!;
        void Validate(JsonObject value, ObservationEntry? boundObservation = null)
        {
            try
            {
                validator.Invoke(null,
                [
                    Element(value), "synthetic-derived.json", "lu-legilux",
                    "work", versionKey, meta, expression,
                    boundObservation ?? selectedObservation,
                ]);
            }
            catch (System.Reflection.TargetInvocationException error)
                when (error.InnerException is InvalidDataException inner)
            {
                throw inner;
            }
        }

        Validate(valid);
        var attacks = new Action<JsonObject>[]
        {
            value => value["schema"] = "lex-articles/unknown",
            value => value["lex_id"] = "lu-legilux:other:2025-01-01",
            value => value["language"] = "de",
            value => value["valid_from"] = "2025-01-02",
            value => value["valid_to"] = "2025-12-31",
            value => value["valid_time_source"] = "unknown",
            value => value["derived_from"]!["corpus_repo"] = "lex-corpus-other",
            value => value["derived_from"]!["path"] =
                $"works/other/versions/{versionKey}/{observationFile}",
            value => value["derived_from"]!["sha256"] = new string('2', 64),
            value => value["derived_from"]!["source_uri"] =
                "https://publisher.example/other.xml",
            value => value["generator"] = null,
            value => value["generator"]!["profile"] = AknLuProfileV2.ProfileId,
        };
        foreach (var attack in attacks)
        {
            var candidate = (JsonObject)valid.DeepClone();
            attack(candidate);
            Assert.Throws<InvalidDataException>(() => Validate(candidate));
        }

        foreach (var invalidBinding in new[]
        {
            firstObservation,
            nestedObservation,
            unsupportedObservation,
            alternateStreamObservation,
            failedObservation,
        })
        {
            var bindingAttack = (JsonObject)valid.DeepClone();
            bindingAttack["derived_from"]!["path"] =
                $"works/work/versions/{versionKey}/{invalidBinding.File}";
            bindingAttack["derived_from"]!["sha256"] = invalidBinding.Sha256;
            bindingAttack["derived_from"]!["source_uri"] = invalidBinding.SourceUri;
            Assert.Throws<InvalidDataException>(() =>
                Validate(bindingAttack, invalidBinding));
        }
    }

    [Fact]
    public void Legacy_index_cannot_acquire_an_unsigned_empty_gap_table()
    {
        IndexBuilder.Build(
            _db, LegacyStamp(), [Document(textPublic: true)], [], [], [],
            StampSigner.CreateKeyPem());
        using (var legacyReader = LexIndexReader.Open(_db))
            Assert.Equal("lex-index/3", legacyReader.Stamp["schema"]);
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var table = connection.CreateCommand();
            table.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='provision_gaps'";
            Assert.Null(table.ExecuteScalar());
            table.CommandText = """
                CREATE TABLE provision_gaps(
                  rid TEXT NOT NULL, seq INTEGER NOT NULL, anchor TEXT NOT NULL,
                  provision_id TEXT NOT NULL, eli TEXT, ptype TEXT NOT NULL, num TEXT,
                  heading TEXT, path TEXT, article_valid_from TEXT,
                  text_unavailable_reason TEXT NOT NULL,
                  PRIMARY KEY(rid,seq), UNIQUE(rid,anchor));
                """;
            table.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("cannot carry provision-gap capability", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Version_four_requires_its_gap_table_even_at_zero_rows()
    {
        var key = StampSigner.CreateKeyPem();
        IndexBuilder.Build(
            _db, Stamp(), [Document(textPublic: true)], [], [], [], key,
            provisionGaps: GapInput([]));
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE provision_gaps";
            drop.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("missing its provision_gaps table", error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("articles_canon")]
    [InlineData("provision_gap_schema")]
    [InlineData("provision_gap_rows")]
    [InlineData("provision_gap_sha256")]
    public void Version_four_requires_each_signed_gap_stamp(string missingKey)
    {
        var key = StampSigner.CreateKeyPem();
        IndexBuilder.Build(
            _db, Stamp(), [Document(textPublic: true)], [], [], [], key,
            provisionGaps: GapInput([]));
        RewriteSignedStamp(_db, key, stamp => stamp.Remove(missingKey));

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("invalid provision-gap stamp evidence", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Correctly_signed_version_three_cannot_smuggle_gap_structures()
    {
        var document = Document(textPublic: false);
        var key = StampSigner.CreateKeyPem();
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps:
            GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        RewriteSignedStamp(_db, key,
            stamp => stamp["schema"] = IndexBuilder.PreviousSchemaVersion);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("cannot carry provision-gap capability", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Gap_identity_is_bound_into_the_signed_content_digest()
    {
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using (var inspect = new SqliteConnection($"Data Source={_db};Mode=ReadOnly"))
        {
            inspect.Open();
            using var stamped = inspect.CreateCommand();
            stamped.CommandText = "SELECT v FROM stamp WHERE k='provision_gap_sha256'";
            Assert.Equal(stamped.ExecuteScalar() as string,
                IndexBuilder.ProvisionGapDigest(inspect));
            stamped.CommandText = "SELECT v FROM stamp WHERE k='provision_gap_schema'";
            Assert.Equal("lex-provision-gap/1", stamped.ExecuteScalar() as string);
            stamped.CommandText = "SELECT v FROM stamp WHERE k='provision_gap_rows'";
            Assert.Equal("1", stamped.ExecuteScalar() as string);
            stamped.CommandText = "SELECT COUNT(*) FROM provision_gaps";
            Assert.Equal(1L, Convert.ToInt64(stamped.ExecuteScalar()));
        }

        using var before = LexIndexReader.Open(_db);
        Assert.Equal(before.Stamp["content_digest"], before.ComputeContentDigest());
        before.Dispose();

        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = "UPDATE provision_gaps SET text_unavailable_reason='marker_suspicious'";
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("provision-gap stamp evidence", error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("seq", 7)]
    [InlineData("anchor", "art_changed")]
    [InlineData("eli", "https://publisher.example/changed#art_2")]
    [InlineData("ptype", "section")]
    [InlineData("num", "Art. 7.")]
    [InlineData("heading", "Synthetic heading")]
    [InlineData("path", "Chapter II")]
    [InlineData("article_valid_from", "2025-02-01")]
    public void Public_content_digest_binds_every_gap_metadata_field(
        string column, object changedValue)
    {
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using var connection = new SqliteConnection($"Data Source={_db}");
        connection.Open();
        var before = new System.Text.StringBuilder();
        IndexBuilder.AppendProvisionGapContentDigest(connection, before);
        using var mutate = connection.CreateCommand();
        mutate.CommandText = $"UPDATE provision_gaps SET {column}=$value";
        mutate.Parameters.AddWithValue("$value", changedValue);
        Assert.Equal(1, mutate.ExecuteNonQuery());
        var after = new System.Text.StringBuilder();
        IndexBuilder.AppendProvisionGapContentDigest(connection, after);

        Assert.NotEqual(before.ToString(), after.ToString());
    }

    [Fact]
    public void Gap_and_text_at_the_same_coordinate_fail_closed()
    {
        var document = Document(textPublic: true);

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db,
            Stamp(),
            [document],
            [Provision(document, 0, "art_1", "Safe synthetic wording.")],
            [],
            [],
            StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(document, 1, "art_1", ProvisionGapReason.MarkerOnly),
            ])));

        Assert.Contains("both text and a gap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gap_identity_must_name_its_exact_parent_document_and_anchor()
    {
        var document = Document(textPublic: false);
        var gap = Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly)
            with { ProvisionId = "t-pub:other:2025-01-01#art_2" };

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([gap])));

        Assert.Contains("exact parent document", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Canon_two_text_identity_must_name_its_exact_parent_document_and_anchor()
    {
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db, Stamp(), [document],
            [Provision(document, 0, "art_1", "Accepted synthetic wording.")],
            [], [], StampSigner.CreateKeyPem(), provisionGaps: GapInput([]));
        SqliteConnection.ClearAllPools();
        var acceptedBytes = File.ReadAllBytes(_db);
        var provision = Provision(document, 0, "art_1", "Safe synthetic wording.")
            with { ProvisionId = "t-pub:other:2025-01-01#art_1" };

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, Stamp(), [document], [provision], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([])));

        Assert.Contains("exact parent document", error.Message,
            StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(acceptedBytes, File.ReadAllBytes(_db));
    }

    [Fact]
    public void Canon_two_orphan_text_is_rejected_before_replacing_the_accepted_artifact()
    {
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db, Stamp(), [document],
            [Provision(document, 0, "art_1", "Accepted synthetic wording.")],
            [], [], StampSigner.CreateKeyPem(), provisionGaps: GapInput([]));
        SqliteConnection.ClearAllPools();
        var acceptedBytes = File.ReadAllBytes(_db);
        var orphan = Provision(document, 0, "art_1", "Unaccepted synthetic wording.")
            with { Rid = "missing|fr|2025-01-01" };

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, Stamp(), [document], [orphan], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([])));

        Assert.Contains("no parent document", error.Message, StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(acceptedBytes, File.ReadAllBytes(_db));
    }

    [Fact]
    public void Gap_aware_indexes_require_an_absolute_document_source()
    {
        var document = Document(textPublic: false) with { SourceUri = null };

        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ])));

        Assert.Contains("source_uri", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_independently_rejects_a_source_less_gap_parent()
    {
        var key = StampSigner.CreateKeyPem();
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = "UPDATE docs SET source_uri=NULL";
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("source_uri", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("orphan")]
    [InlineData("misnamed")]
    [InlineData("order_collision")]
    [InlineData("anchor_collision")]
    public void Reader_recomputes_gap_relational_invariants_for_a_freshly_signed_index(
        string attack)
    {
        var key = StampSigner.CreateKeyPem();
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db, Stamp(), [document],
            [Provision(document, 0, "art_1", "Safe synthetic wording.")],
            [], [], key,
            provisionGaps: GapInput([
                Gap(document, 1, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = attack switch
            {
                "orphan" => "UPDATE provision_gaps SET rid='missing|fr|2025-01-01'",
                "misnamed" => "UPDATE provision_gaps SET provision_id='t-pub:other#art_2'",
                "order_collision" => "UPDATE provision_gaps SET seq=0",
                "anchor_collision" => "UPDATE provision_gaps SET anchor='art_1', "
                    + "provision_id='t-pub:work:2025-01-01#art_1'",
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("identity or ordering contract", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_requires_the_gap_primary_and_unique_schema_contract()
    {
        var key = StampSigner.CreateKeyPem();
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = """
                ALTER TABLE provision_gaps RENAME TO provision_gaps_old;
                CREATE TABLE provision_gaps(
                  rid TEXT, seq INTEGER, anchor TEXT, provision_id TEXT, eli TEXT,
                  ptype TEXT, num TEXT, heading TEXT, path TEXT,
                  article_valid_from TEXT, text_unavailable_reason TEXT);
                INSERT INTO provision_gaps SELECT * FROM provision_gaps_old;
                DROP TABLE provision_gaps_old;
                """;
            mutate.ExecuteNonQuery();
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("column contract", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_rejects_a_freshly_resigned_misnamed_text_coordinate()
    {
        var key = StampSigner.CreateKeyPem();
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db, Stamp(), [document],
            [Provision(document, 0, "art_1", "Synthetic wording.")],
            [], [], key, provisionGaps: GapInput([]));
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText =
                "UPDATE provisions SET provision_id='t-pub:other#art_1'";
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() =>
            LexIndexReader.Open(_db));
        Assert.Contains("identity or ordering", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_rejects_an_extra_generated_gap_column_after_fresh_resigning()
    {
        var key = StampSigner.CreateKeyPem();
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = """
                ALTER TABLE provision_gaps RENAME TO provision_gaps_old;
                CREATE TABLE provision_gaps(
                  rid TEXT NOT NULL, seq INTEGER NOT NULL, anchor TEXT NOT NULL,
                  provision_id TEXT NOT NULL, eli TEXT, ptype TEXT NOT NULL, num TEXT,
                  heading TEXT, path TEXT, article_valid_from TEXT,
                  text_unavailable_reason TEXT NOT NULL,
                  armed TEXT GENERATED ALWAYS AS ('hidden') VIRTUAL,
                  PRIMARY KEY(rid,seq), UNIQUE(rid,anchor));
                INSERT INTO provision_gaps(
                  rid,seq,anchor,provision_id,eli,ptype,num,heading,path,
                  article_valid_from,text_unavailable_reason)
                SELECT rid,seq,anchor,provision_id,eli,ptype,num,heading,path,
                       article_valid_from,text_unavailable_reason
                FROM provision_gaps_old;
                DROP TABLE provision_gaps_old;
                CREATE INDEX ix_provision_gaps_rid ON provision_gaps(rid,seq);
                """;
            mutate.ExecuteNonQuery();
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("column contract", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_independently_requires_the_gap_coordinate_unique_constraint()
    {
        var key = StampSigner.CreateKeyPem();
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));

        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = """
                ALTER TABLE provision_gaps RENAME TO provision_gaps_old;
                CREATE TABLE provision_gaps(
                  rid TEXT NOT NULL, seq INTEGER NOT NULL, anchor TEXT NOT NULL,
                  provision_id TEXT NOT NULL, eli TEXT, ptype TEXT NOT NULL, num TEXT,
                  heading TEXT, path TEXT, article_valid_from TEXT,
                  text_unavailable_reason TEXT NOT NULL,
                  PRIMARY KEY(rid,seq));
                INSERT INTO provision_gaps SELECT * FROM provision_gaps_old;
                DROP TABLE provision_gaps_old;
                CREATE INDEX ix_provision_gaps_rid ON provision_gaps(rid,seq);
                """;
            mutate.ExecuteNonQuery();
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("unique coordinate contract", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_rejects_unbounded_public_gap_metadata_before_replacing_artifact()
    {
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        SqliteConnection.ClearAllPools();
        var accepted = File.ReadAllBytes(_db);

        var error = Assert.Throws<InvalidDataException>(() =>
        IndexBuilder.Build(
            _db,
            Stamp(),
            [document],
            [],
            [],
            [],
            StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(document, 0, new string('a', 513), ProvisionGapReason.MarkerOnly),
            ])));

        Assert.Contains("provision-gap metadata", error.Message,
            StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(accepted, File.ReadAllBytes(_db));
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("https://publisher.example/work#art_2", "2025-1-2")]
    public void Builder_rejects_invalid_public_gap_uri_or_date_before_replacing_artifact(
        string eli, string? articleValidFrom)
    {
        var document = Document(textPublic: false);
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        SqliteConnection.ClearAllPools();
        var accepted = File.ReadAllBytes(_db);
        var gap = Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly) with
        {
            Eli = eli,
            ArticleValidFrom = articleValidFrom,
        };
        var error = Assert.Throws<InvalidDataException>(() => IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([gap])));

        Assert.Contains("provision-gap metadata", error.Message,
            StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(accepted, File.ReadAllBytes(_db));
    }

    [Fact]
    public void Persisted_gap_readback_rejects_invalid_metadata_before_signing()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE provision_gaps(
                  rid TEXT NOT NULL, seq INTEGER NOT NULL, anchor TEXT NOT NULL,
                  provision_id TEXT NOT NULL, eli TEXT, ptype TEXT NOT NULL, num TEXT,
                  heading TEXT, path TEXT, article_valid_from TEXT,
                  text_unavailable_reason TEXT NOT NULL);
                INSERT INTO provision_gaps VALUES(
                  'p:w:2025-01-01|fr|2025-01-01',0,'art_2',
                  'p:w:2025-01-01#art_2','javascript:alert(1)','article',NULL,
                  NULL,NULL,'2025-01-01','marker_only');
                """;
            schema.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidDataException>(() =>
            IndexBuilder.ValidatePersistedProvisionGaps(connection));
        Assert.Contains("provision-gap metadata", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_independently_rejects_resigned_invalid_gap_metadata()
    {
        var document = Document(textPublic: false);
        var key = StampSigner.CreateKeyPem();
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText =
                "UPDATE provision_gaps SET eli='javascript:alert(1)'";
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() =>
            LexIndexReader.Open(_db));
        Assert.Contains("provision-gap metadata contract", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_rejects_non_text_storage_in_nullable_gap_metadata()
    {
        var document = Document(textPublic: false);
        var key = StampSigner.CreateKeyPem();
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = "UPDATE provision_gaps SET num=X'31'";
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("provision-gap metadata contract", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_rejects_gap_sequence_values_outside_the_public_integer_range()
    {
        var document = Document(textPublic: false);
        var key = StampSigner.CreateKeyPem();
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], key,
            provisionGaps: GapInput([
                Gap(document, 0, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            using var mutate = connection.CreateCommand();
            mutate.CommandText = "UPDATE provision_gaps SET seq=9223372036854775807";
            Assert.Equal(1, mutate.ExecuteNonQuery());
        }
        ResignCurrentV4(key);

        var error = Assert.Throws<InvalidDataException>(() => LexIndexReader.Open(_db));
        Assert.Contains("provision-gap metadata contract", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Article_gap_parser_accepts_only_the_textless_closed_schema()
    {
        using var accepted = JsonDocument.Parse("""
            {
              "text_completeness": "unavailable",
              "provision_gaps": [{
                "schema": "lex-provision-gap/1",
                "document_order": 4,
                "anchor": "art_2",
                "provision_id": "t-pub:work:2025-01-01#art_2",
                "eli": null,
                "type": "article",
                "num": "Art. 2.",
                "heading": null,
                "path": ["Chapter I"],
                "article_valid_from": null,
                "text_unavailable_reason": "marker_only"
              }]
            }
            """);

        var gap = Assert.Single(IndexFromCorpus.ParseProvisionGaps(
            accepted.RootElement,
            "t-pub:work:2025-01-01|fr|2025-01-01",
            "synthetic.json"));
        Assert.Equal(4, gap.Seq);
        Assert.Equal("art_2", gap.Anchor);
        Assert.Equal("Chapter I", gap.Path);

        using var wrongParent = JsonDocument.Parse(accepted.RootElement.GetRawText().Replace(
            "t-pub:work:2025-01-01#art_2",
            "t-pub:other:2025-01-01#art_2",
            StringComparison.Ordinal));
        var wrongParentError = Assert.Throws<InvalidDataException>(() =>
            IndexFromCorpus.ParseProvisionGaps(
                wrongParent.RootElement,
                "t-pub:work:2025-01-01|fr|2025-01-01",
                "wrong-parent.json"));
        Assert.Contains("exact parent document", wrongParentError.Message,
            StringComparison.Ordinal);

        using var duplicate = JsonDocument.Parse(
            accepted.RootElement.GetRawText().Replace(
                "\"anchor\": \"art_2\",",
                "\"anchor\": \"art_2\", \"anchor\": \"art_2\",",
                StringComparison.Ordinal));
        var duplicateError = Assert.Throws<InvalidDataException>(() =>
            IndexFromCorpus.ParseProvisionGaps(
                duplicate.RootElement,
                "t-pub:work:2025-01-01|fr|2025-01-01",
                "duplicate.json"));
        Assert.Contains("exact textless schema", duplicateError.Message,
            StringComparison.Ordinal);

        using var armed = JsonDocument.Parse("""
            {
              "text_completeness": "unavailable",
              "provision_gaps": [{
                "schema": "lex-provision-gap/1",
                "document_order": 4,
                "anchor": "art_2",
                "provision_id": "t-pub:work:2025-01-01#art_2",
                "eli": null,
                "type": "article",
                "num": "Art. 2.",
                "heading": null,
                "path": [],
                "article_valid_from": null,
                "text_unavailable_reason": "marker_only",
                "text_md": "Armed text must never enter the gap schema."
              }]
            }
            """);
        var error = Assert.Throws<InvalidDataException>(() =>
            IndexFromCorpus.ParseProvisionGaps(
                armed.RootElement,
                "t-pub:work:2025-01-01|fr|2025-01-01",
                "armed.json"));
        Assert.Contains("exact textless schema", error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("outline")]
    [InlineData("select")]
    [InlineData("full")]
    public void As_of_exposes_the_typed_gap_at_its_anchor_without_certified_text(
        string mode)
    {
        var document = Document(textPublic: true);
        IndexBuilder.Build(
            _db,
            Stamp(),
            [document],
            [Provision(document, 0, "art_1", "Safe synthetic wording.")],
            [],
            [],
            StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(document, 1, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        using var reader = LexIndexReader.Open(_db);
        var core = new McpCore(
            new Dictionary<string, LexIndexReader> { ["t-pub"] = reader });
        var arguments = new JsonObject
        {
            ["work"] = "t-pub:work",
            ["date"] = "2025-01-01",
            ["mode"] = mode,
        };
        if (mode == "select") arguments["anchors"] = "art_2";

        var result = Assert.IsType<JsonObject>(core.CallTool("as_of", arguments));

        Assert.Equal(mode == "select" ? "text_not_available" : "ok",
            result["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal("partial", result["text_completeness"]!.GetValue<string>());
        Assert.Equal(2, result["total_provisions"]!.GetValue<int>());
        Assert.Equal(1, result["total_text_provisions"]!.GetValue<int>());
        Assert.Equal(1, result["total_provision_gaps"]!.GetValue<int>());
        if (mode == "select")
        {
            var reason = result["text_unavailable_reason"]!.GetValue<string>();
            Assert.Contains("requested coordinate or coordinates", reason,
                StringComparison.Ordinal);
            Assert.DoesNotContain("no safely derived provision text is available;",
                reason, StringComparison.Ordinal);
        }
        if (mode != "select")
        {
            var provision = Assert.Single(result["provisions"]!.AsArray()
                .OfType<JsonObject>());
            Assert.Equal(0, provision["document_order"]!.GetValue<int>());
        }
        var gap = Assert.Single(result["provision_gaps"]!.AsArray()
            .OfType<JsonObject>());
        Assert.Equal(1, gap["document_order"]!.GetValue<int>());
        Assert.Equal("art_2", gap["anchor"]!.GetValue<string>());
        Assert.False(gap["text_available"]!.GetValue<bool>());
        Assert.Equal(ProvisionGapReason.MarkerOnly,
            gap["text_unavailable_reason"]!.GetValue<string>());
        Assert.Equal("https://publisher.example/work#art_2",
            gap["eli"]!.GetValue<string>());
        Assert.Null(gap["text"]);
        Assert.Null(gap["text_sha256"]);
        Assert.Null(gap["text_omitted"]);
        Assert.Null(gap["permalink"]);
    }

    [Theory]
    [InlineData(true, 1, 1999)]
    [InlineData(false, 0, 2000)]
    public void Partial_combined_cap_preserves_gap_totals_and_bounded_ui_truth(
        bool textFirst, int returnedTextCount, int returnedGapCount)
    {
        var document = Document(textPublic: true);
        var textSequence = textFirst ? 0 : 2000;
        var firstGapSequence = textFirst ? 1 : 0;
        var gaps = Enumerable.Range(firstGapSequence, 2000)
            .Select(index => Gap(document, index, $"art_{index + 1}",
                ProvisionGapReason.MarkerOnly))
            .ToArray();
        IndexBuilder.Build(
            _db, Stamp(), [document],
            [Provision(document, textSequence, "safe_text", "Safe synthetic wording.")],
            [], [], StampSigner.CreateKeyPem(), provisionGaps: GapInput(gaps));
        using var reader = LexIndexReader.Open(_db);
        var core = new McpCore(
            new Dictionary<string, LexIndexReader> { ["t-pub"] = reader });
        var arguments = new JsonObject
        {
            ["work"] = "t-pub:work",
            ["date"] = "2025-01-01",
            ["mode"] = "full",
        };

        var result = Assert.IsType<JsonObject>(core.CallTool("as_of", arguments));

        Assert.Equal("ok", result["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal("partial", result["text_completeness"]!.GetValue<string>());
        Assert.Equal(2000, result["total_provision_gaps"]!.GetValue<int>());
        Assert.Equal(returnedTextCount, result["provisions"]!.AsArray().Count);
        Assert.Equal(returnedGapCount, result["provision_gaps"]!.AsArray().Count);
        Assert.True(result["truncated"]!.GetValue<bool>());
        Assert.Equal(!textFirst, result["text_truncated"]?.GetValue<bool>() ?? false);

        var effect = UiMapper.From("as_of", arguments, result);
        var view = Assert.IsType<ProvisionView>(effect.Provision);
        Assert.Equal(2000, view.TotalProvisionGaps);
        Assert.Equal(returnedTextCount, view.Provisions.Count);
        Assert.Equal(returnedGapCount, view.ProvisionGaps!.Count);
        var answer = OperationAnswerPolicy.Render(
            "en",
            [new OperationResult("as-of", 0, null, null, LegalOutcome.Succeeded,
                TransportOutcome.Completed, [], null)],
            [effect]);
        Assert.Contains("bounded response", answer, StringComparison.Ordinal);
        Assert.Equal(!textFirst,
            answer.Contains("omits some held publisher text", StringComparison.Ordinal));
    }

    [Fact]
    public void Gap_only_structural_paging_never_claims_legal_text_was_truncated()
    {
        var document = Document(textPublic: false, textAvailable: false);
        var gaps = Enumerable.Range(0, 2001)
            .Select(index => Gap(document, index, $"art_{index + 1}",
                ProvisionGapReason.MarkerSuspicious))
            .ToArray();
        IndexBuilder.Build(
            _db, Stamp(), [document], [], [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput(gaps));
        using var reader = LexIndexReader.Open(_db);
        var core = new McpCore(
            new Dictionary<string, LexIndexReader> { ["t-pub"] = reader });

        var result = Assert.IsType<JsonObject>(core.CallTool("as_of", new JsonObject
        {
            ["work"] = "t-pub:work",
            ["date"] = "2025-01-01",
            ["mode"] = "full",
        }));

        Assert.Equal("text_not_available",
            result["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal("unavailable", result["text_completeness"]!.GetValue<string>());
        Assert.Equal(2001, result["total_provision_gaps"]!.GetValue<int>());
        Assert.Equal(2000, result["provision_gaps"]!.AsArray().Count);
        Assert.True(result["truncated"]!.GetValue<bool>());
        Assert.Null(result["text_truncated"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("art_2")]
    public void Diff_fails_closed_when_either_side_contains_a_typed_gap(string? anchor)
    {
        var before = Document(textPublic: true) with
        {
            Key = "t-pub:work:2025-01-01",
            ValidFrom = "2025-01-01",
            ValidTo = "2025-02-01",
        };
        var after = Document(textPublic: true) with
        {
            Key = "t-pub:work:2025-02-01",
            ValidFrom = "2025-02-01",
        };
        IndexBuilder.Build(
            _db, Stamp(), [before, after],
            [
                Provision(before, 0, "art_1", "Safe wording before."),
                Provision(after, 0, "art_1", "Safe wording after."),
            ],
            [], [], StampSigner.CreateKeyPem(),
            provisionGaps:
            GapInput([
                Gap(before, 1, "art_2", ProvisionGapReason.MarkerOnly),
                Gap(after, 1, "art_2", ProvisionGapReason.MarkerSuspicious),
            ]));
        using var reader = LexIndexReader.Open(_db);
        var arguments = new JsonObject
        {
            ["work"] = "t-pub:work",
            ["from_date"] = "2025-01-15",
            ["to_date"] = "2025-02-15",
        };
        if (anchor is not null) arguments["anchor"] = anchor;

        var result = Assert.IsType<JsonObject>(new McpCore(
            new Dictionary<string, LexIndexReader> { ["t-pub"] = reader })
            .CallTool("diff", arguments));

        Assert.Equal("text_not_available",
            result["envelope"]!["status"]!.GetValue<string>());
        Assert.False(result["provision_level_comparable"]!.GetValue<bool>());
        Assert.Contains("typed text gaps", result["note"]!.GetValue<string>(),
            StringComparison.Ordinal);
        if (anchor is not null)
        {
            Assert.True(result["anchor_from_present"]!.GetValue<bool>());
            Assert.True(result["anchor_to_present"]!.GetValue<bool>());
            Assert.False(result["anchor_from_text_available"]!.GetValue<bool>());
            Assert.False(result["anchor_to_text_available"]!.GetValue<bool>());
            Assert.Null(result["anchor_text_equal"]);
            Assert.Null(result["changed"]);
        }
    }

    [Fact]
    public void Diff_retains_both_profile_and_typed_gap_limitations()
    {
        var before = Document(textPublic: true) with
        {
            Key = "t-pub:work:2025-01-01",
            ValidFrom = "2025-01-01",
            ValidTo = "2025-02-01",
            Profile = "akn-lu/2",
        };
        var after = Document(textPublic: true) with
        {
            Key = "t-pub:work:2025-02-01",
            ValidFrom = "2025-02-01",
            Profile = "akn-lu/3",
        };
        IndexBuilder.Build(
            _db, Stamp(), [before, after],
            [
                Provision(before, 0, "art_1", "Safe wording before."),
                Provision(after, 0, "art_1", "Safe wording after."),
            ],
            [], [], StampSigner.CreateKeyPem(),
            provisionGaps: GapInput([
                Gap(after, 1, "art_2", ProvisionGapReason.MarkerOnly),
            ]));
        using var reader = LexIndexReader.Open(_db);

        var result = Assert.IsType<JsonObject>(new McpCore(
            new Dictionary<string, LexIndexReader> { ["t-pub"] = reader })
            .CallTool("diff", new JsonObject
            {
                ["work"] = "t-pub:work",
                ["from_date"] = "2025-01-15",
                ["to_date"] = "2025-02-15",
            }));

        Assert.Equal("profiles_differ",
            result["envelope"]!["status"]!.GetValue<string>());
        Assert.Equal(["profiles_differ", "typed_text_gap"],
            result["comparison_limitations"]!.AsArray()
                .Select(value => value!.GetValue<string>()));
        Assert.Contains("typed text gaps", result["note"]!.GetValue<string>(),
            StringComparison.Ordinal);
        Assert.False(result["provision_level_comparable"]!.GetValue<bool>());
    }

    private static Dictionary<string, string> Stamp() => new()
    {
        ["collection"] = "t-pub",
        ["tier"] = "A",
        ["history_begins"] = "publisher",
        ["built_at"] = "2026-08-28T00:00:00Z",
        ["corpus_commit"] = "test",
        ["generation_sha256"] = GenerationSha256,
        ["articles_commit"] = ArticlesCommit,
        ["articles_canon"] = DerivationGeneration.Canon2,
    };

    private static Dictionary<string, string> LegacyStamp() => new()
    {
        ["collection"] = "t-pub",
        ["tier"] = "A",
        ["history_begins"] = "publisher",
        ["built_at"] = "2026-08-28T00:00:00Z",
        ["corpus_commit"] = "test",
    };

    private static ProvisionGapIndexInput GapInput(
        IEnumerable<ProvisionGapRow> rows) =>
        ProvisionGapIndexInput.FromGenerationEvidence(
            DerivationGeneration.Canon2,
            GenerationSha256, ArticlesCommit, rows);

    private const string GenerationSha256 =
        "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string ArticlesCommit =
        "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    private sealed class SplitViewStamp(
        IReadOnlyDictionary<string, string> reads,
        IReadOnlyDictionary<string, string> enumeration)
        : IReadOnlyDictionary<string, string>
    {
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            enumeration.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
        public int Count => reads.Count;
        public bool ContainsKey(string key) => reads.ContainsKey(key);
        public bool TryGetValue(string key, out string value) =>
            reads.TryGetValue(key, out value!);
        public string this[string key] => reads[key];
        public IEnumerable<string> Keys => reads.Keys;
        public IEnumerable<string> Values => reads.Values;
    }

    private static void UpdateGeneration(
        string root,
        string publisher,
        IReadOnlyList<string> profiles,
        string canon = DerivationGeneration.Canon1)
    {
        Directory.CreateDirectory(Path.Combine(root, publisher));
        DerivationGeneration.UpdatePublisherWithLocksHeld(
            root,
            publisher,
            new string('a', 40),
            new string('b', 64),
            new string('c', 40),
            new string('d', 40),
            new string('e', 40),
            profiles,
            canon);
    }

    private static DocRow Document(bool textPublic, bool textAvailable = true) => new(
        "t-pub:work:2025-01-01", "t-pub", "work", "urn:work", "LAW", "fr",
        "2025-01-01", null, "publisher", "2026-08-28T00:00:00Z",
        Withdrawn: false, TextAvailable: textAvailable, TextPublic: textPublic,
        RecordSha: "record", BodySha: "body", SourceUri: "https://publisher.example/work",
        Title: "Synthetic work", TitleShort: "Synthetic", Body: null,
        PublicationDate: "2025-01-01", StatusNote: null,
        Profile: AknLuProfileV3.ProfileId);

    private static ProvisionRow Provision(
        DocRow document, int seq, string anchor, string text) => new(
        LexIndexReader.RidOf(document), seq, anchor, $"{document.Key}#{anchor}",
        "article", "Art. 1.", null, null, null, document.Title, text,
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text))));

    private static ProvisionGapRow Gap(
        DocRow document, int seq, string anchor, string reason) => new(
        LexIndexReader.RidOf(document), seq, anchor, $"{document.Key}#{anchor}",
        "https://publisher.example/work#art_2", "article", "Art. 2.", null,
        null, null, reason);

    private static void RewriteSignedStamp(
        string databasePath,
        string privateKeyPem,
        Action<Dictionary<string, string>> rewrite)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        var stamp = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT k,v FROM stamp";
            using var rows = read.ExecuteReader();
            while (rows.Read()) stamp[rows.GetString(0)] = rows.GetString(1);
        }
        stamp.Remove("signature");
        stamp.Remove("public_key");
        rewrite(stamp);
        var (signature, publicKey) = StampSigner.Sign(stamp, privateKeyPem);
        stamp["signature"] = signature;
        stamp["public_key"] = publicKey;
        Assert.True(StampSigner.Verify(stamp));

        using var transaction = connection.BeginTransaction();
        using var replace = connection.CreateCommand();
        replace.Transaction = transaction;
        replace.CommandText = "DELETE FROM stamp";
        replace.ExecuteNonQuery();
        replace.CommandText = "INSERT INTO stamp(k,v) VALUES ($key,$value)";
        replace.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter(
            "$key", Microsoft.Data.Sqlite.SqliteType.Text));
        replace.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter(
            "$value", Microsoft.Data.Sqlite.SqliteType.Text));
        foreach (var (name, value) in stamp)
        {
            replace.Parameters["$key"].Value = name;
            replace.Parameters["$value"].Value = value;
            replace.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void ResignCurrentV4(string privateKeyPem)
    {
        string gapDigest;
        string contentDigest;
        using (var connection = new SqliteConnection($"Data Source={_db}"))
        {
            connection.Open();
            gapDigest = IndexBuilder.ProvisionGapDigest(connection);
            contentDigest = IndexBuilder.ContentDigestV4(connection);
        }
        RewriteSignedStamp(_db, privateKeyPem, stamp =>
        {
            stamp["provision_gap_sha256"] = gapDigest;
            stamp["content_digest"] = contentDigest;
        });
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_db)) try { File.Delete(_db); } catch { }
        var vectors = Path.ChangeExtension(_db, ".vectors");
        if (File.Exists(vectors)) try { File.Delete(vectors); } catch { }
    }
}
