using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Ingest;
using Lex.Index;
using Lex.Law;
using Lex.Mcp;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed partial class CorpusWriterTests : IDisposable
{
    private const string CodeCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-writer-{Guid.NewGuid():N}");

    public CorpusWriterTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Same_date_versions_are_keyed_by_stable_publisher_identity()
    {
        var first = Path.Combine(_dir, "first");
        var reversed = Path.Combine(_dir, "reversed");

        await new CorpusWriter(first, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false), default);
        await new CorpusWriter(reversed, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: true), default);

        Assert.Equal(await SameDateInventory(first), await SameDateInventory(reversed));
        Assert.All((await SameDateInventory(first)).Keys,
            key => Assert.Matches("^2025-07-28--[0-9a-f]{64}$", key));
    }

    [Fact]
    public async Task Version_key_is_stable_when_a_same_date_publisher_version_appears_later()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false, includeSecond: false), default);
        var before = Assert.Single((await SameDateInventory(_dir)).Keys);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false), default);
        var after = await SameDateInventory(_dir);

        Assert.Matches("^2025-07-28--[0-9a-f]{64}$", before);
        Assert.Contains(before, after.Keys);
        Assert.Equal(2, after.Count);
    }

    [Fact]
    public async Task No_change_poll_keeps_the_prior_materializer_identity_and_writes_no_bytes()
    {
        const string laterCodeCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var adapter = new OneVersionAdapter("in_force", "finance",
            bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>"));
        var first = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit);
        await first.WriteAsync(adapter, default);
        var before = Snapshot();

        var poll = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), laterCodeCommit);
        await poll.WriteAsync(adapter, default);

        Assert.True(poll.Accepted);
        Assert.False(poll.Committed);
        Assert.Equal(before, Snapshot());
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Equal(CodeCommit, manifest.IngesterCodeCommit);
    }

    [Fact]
    public async Task Work_title_fallback_never_crosses_an_expression_language_boundary()
    {
        var corpus = Path.Combine(_dir, "language-honest-corpus");
        var db = Path.Combine(_dir, "language-honest.db");
        await new CorpusWriter(
                corpus, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new CrossLanguageTitleAdapter(), default);

        var workMeta = JsonSerializer.Deserialize<WorkMeta>(
            await File.ReadAllTextAsync(Path.Combine(corpus, "works", "w1", "meta.json")),
            CorpusJson.Options)!;
        Assert.Equal("fr", workMeta.TitleLanguage);

        IndexFromCorpus.Build(corpus, null, db, null,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"),
            codeCommit: new string('d', 40));
        using var reader = LexIndexReader.Open(db);
        var response = Assert.IsType<JsonObject>(new McpCore(
            new Dictionary<string, LexIndexReader> { [reader.Collection] = reader })
            .CallTool("timeline", new JsonObject { ["work"] = "test:w1" }));
        var expressions = Assert.IsType<JsonArray>(response["versions"]![0]!["expressions"]);
        var english = Assert.Single(expressions.OfType<JsonObject>(), value =>
            value["language"]!.GetValue<string>() == "en");
        var french = Assert.Single(expressions.OfType<JsonObject>(), value =>
            value["language"]!.GetValue<string>() == "fr");

        Assert.Null(english["title"]);
        Assert.Equal("32024R0001", english["title_short"]!.GetValue<string>());
        Assert.Equal("Règlement de test", french["title"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Ingester_identity_requires_a_full_lowercase_commit(string value)
    {
        Assert.Throws<InvalidDataException>(() => new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), value));
    }

    [Fact]
    public async Task Fresh_migration_failure_leaves_the_disposable_checkout_unchanged()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot);
        var before = Inventory(corpusRoot);

        await Assert.ThrowsAsync<SourceEnumerationIncompleteException>(() => FreshCorpusMigration.RunAsync(
            corpusRoot, "test", new IncompleteAdapter(),
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_replaces_the_disposable_checkout_only_after_all_gates_pass()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot);
        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test",
            new ManyWorksAdapter(1),
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Contains("Attribution: test",
            File.ReadAllText(Path.Combine(corpusRoot, "NOTICE")),
            StringComparison.Ordinal);
        var version = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(corpusRoot, "works", "w1", "versions")));
        Assert.Matches("^2024-01-01--[0-9a-f]{64}$", Path.GetFileName(version));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(corpusRoot),
            path => Path.GetFileName(path).StartsWith(".lex-fresh-", StringComparison.Ordinal));
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(corpusRoot, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Equal(ManifestDoc.CurrentSchema, manifest.Schema);
        Assert.Equal(CodeCommit, manifest.IngesterCodeCommit);
        Assert.Equal(1, manifest.MigrationBaselineWorks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_migration_handles_the_recorded_256_character_nested_destination(
        bool removeParentBeforeCopy)
    {
        var body = SourceBodyFetch.Retrieved("<html>publisher text</html>");
        const string member = "CL2012R0648FR0200010.0001.doc.xml";
        const string workSlug = "32012r0648";
        const string language = "fr";
        var versionKeyShape = "2024-01-01--" + new string('a', 64);
        var destinationTail = Path.Combine(
            "works", workSlug, "versions", versionKeyShape,
            language + ".fmx4", member);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        var stageWithoutRootName = Path.Combine(
            parent, "..lex-fresh-stage-" + new string('a', 32), destinationTail);
        var rootNameLength = 256 - stageWithoutRootName.Length;
        Assert.InRange(rootNameLength, 8, 200);
        var unique = "lex" + Guid.NewGuid().ToString("N");
        var rootName = string.Concat(Enumerable.Repeat(
            unique, (rootNameLength + unique.Length - 1) / unique.Length))
            [..rootNameLength];
        var corpusRoot = Path.Combine(parent, rootName);
        try
        {
            var adapter = new AltThenPrimaryAdapter(body, member, workSlug, language);
            await new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
                .WriteAsync(adapter, default);

            var baselineVersion = Assert.Single(Directory.EnumerateDirectories(
                Path.Combine(corpusRoot, "works", workSlug, "versions")));
            var baselineMeta = JsonSerializer.Deserialize<VersionMeta>(
                await File.ReadAllTextAsync(Path.Combine(baselineVersion, "meta.json")),
                CorpusJson.Options)!;
            var nested = Assert.Single(
                Assert.Single(baselineMeta.Expressions).Observations,
                observation => observation.File?.Contains('/', StringComparison.Ordinal) == true);
            var expected = await File.ReadAllBytesAsync(Path.Combine(
                baselineVersion,
                nested.File!.Replace('/', Path.DirectorySeparatorChar)));

            var boundaryObserved = false;
            void RemoveNestedParent(string destination)
            {
                if (boundaryObserved
                    || !destination.EndsWith(member, StringComparison.Ordinal)) return;
                Assert.Equal(256, destination.Length);
                if (removeParentBeforeCopy)
                {
                    var nestedParent = Path.GetDirectoryName(destination)!;
                    Directory.CreateDirectory(nestedParent);
                    Directory.Delete(nestedParent);
                }
                boundaryObserved = true;
            }
            var report = await RunFreshWithStageWriteHook(
                corpusRoot, new AltThenPrimaryAdapter(
                    body, member, workSlug, language), RemoveNestedParent);

            Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
            Assert.True(boundaryObserved);
            var currentVersion = Assert.Single(Directory.EnumerateDirectories(
                Path.Combine(corpusRoot, "works", workSlug, "versions")));
            Assert.Equal(expected, await File.ReadAllBytesAsync(Path.Combine(
                currentVersion,
                nested.File.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            if (Directory.Exists(corpusRoot)) Directory.Delete(corpusRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Fresh_migration_rejects_a_complete_but_truncated_candidate()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot, works: 2);
        var before = Inventory(corpusRoot);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test",
                new ManyWorksAdapter(1),
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("missing work 'official:w2'", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_rejects_same_count_work_replacement_before_body_fetch()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot);
        var before = Inventory(corpusRoot);
        var replacement = new ManyWorksAdapter(1, first: 2);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", replacement,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("missing work 'official:w1'", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, replacement.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_rejects_a_missing_historical_state_before_body_fetch()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot,
            new HistoryAdapter(includeHistorical: true));
        var before = Inventory(corpusRoot);
        var currentOnly = new HistoryAdapter(includeHistorical: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", currentOnly,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("publisher version 'official:v0'", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, currentOnly.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_preserves_a_withdrawn_legacy_state_beside_its_same_date_replacement()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        var baseline = await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var current = new LegiluxReplacementAdapter(includeWithdrawn: false);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "lu-legilux", current,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(2, report.ActualVersions);
        Assert.Equal(1, report.CurrentVersions);
        Assert.Equal(2, report.Expressions);
        Assert.Equal(2, report.Observations);
        Assert.Empty(current.FetchedVersionIdentifiers);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(corpusRoot, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Equal(1, manifest.Works);
        Assert.Equal(1, manifest.Versions);
        Assert.Equal(1, manifest.Expressions);
        Assert.Equal(1, manifest.ExpressionsWithText);
        Assert.Equal(0, manifest.ExpressionsWithoutText);
        Assert.Equal(["fr"], manifest.Languages);
        Assert.Equal("2025-04-20", manifest.ValidFromEarliest);
        Assert.Equal("2025-04-20", manifest.ValidToLatest);
        var documentType = Assert.Single(manifest.DocumentTypes);
        Assert.Equal("CODE", documentType["code"].ToString());
        Assert.Equal("1", documentType["versions"].ToString());

        var versions = ReadVersionsByPublisherIdentity(corpusRoot);
        var withdrawn = versions[LegiluxReplacementAdapter.WithdrawnVersionIdentifier];
        var live = versions[LegiluxReplacementAdapter.LiveVersionIdentifier];
        Assert.Equal("withdrawn_from_source", withdrawn.Meta.Events.Last(entry =>
            entry.Event == "withdrawn_from_source" || entry.Event == "resighted").Event);
        Assert.DoesNotContain(live.Meta.Events,
            entry => entry.Event == "withdrawn_from_source");
        Assert.Equal(baseline.BodySha256,
            Assert.Single(withdrawn.Meta.Expressions).Observations.Single().Sha256);
        Assert.Equal(baseline.BodyBytes,
            await File.ReadAllBytesAsync(Path.Combine(withdrawn.Directory,
                Assert.Single(withdrawn.Meta.Expressions).Observations.Single().File!)));
        Assert.Equal(StableVersionKey("2025-04-20",
                LegiluxReplacementAdapter.WithdrawnVersionIdentifier),
            Path.GetFileName(withdrawn.Directory));
        Assert.Equal(StableVersionKey("2025-04-20",
                LegiluxReplacementAdapter.LiveVersionIdentifier),
            Path.GetFileName(live.Directory));
        Assert.NotEqual(withdrawn.Meta.LexId, live.Meta.LexId);
        var migration = Assert.Single(withdrawn.Meta.Events,
            entry => entry.Event == "metadata_revised"
                && entry.Detail == "fields=lex_id,publisher_version_identifier");
        Assert.Equal("2026-08-14T00:00:00Z", migration.ObservedFrom);
    }

    [Fact]
    public async Task Fresh_migration_refuses_a_tampered_withdrawn_identity_before_body_fetch()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        var baseline = await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(baseline.MetaPath), CorpusJson.Options)!;
        var expression = Assert.Single(meta.Expressions);
        expression.SourceUri =
            "https://example.test/eli/etat/leg/loi/1804/03/21/n1/consolide/20250420/fr";
        expression.Text.Url = expression.SourceUri;
        RefreshRecordHash(meta);
        await File.WriteAllTextAsync(baseline.MetaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        var before = Inventory(corpusRoot);
        var current = new LegiluxReplacementAdapter(includeWithdrawn: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("not an official ELI URI", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_refuses_tampered_tombstone_bytes_before_source_enumeration()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        var baseline = await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        await File.AppendAllTextAsync(baseline.BodyPath, "tampered");
        var current = new LegiluxReplacementAdapter(includeWithdrawn: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("Protected corpus baseline is not integrity-compatible",
            error.Message, StringComparison.Ordinal);
        Assert.Contains("sha256 mismatch", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, current.EnumerateCount);
        Assert.Equal(0, current.BodyFetchCount);
    }

    [Fact]
    public async Task Fresh_migration_refuses_a_withdrawn_identity_collision_before_body_fetch()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var before = Inventory(corpusRoot);
        var current = new LegiluxReplacementAdapter(
            includeWithdrawn: false, collideLegacyIdentity: true);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("indistinguishable states",
            error.Message, StringComparison.Ordinal);
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_keeps_a_wholly_withdrawn_missing_work_fail_closed()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var liveMetaPath = Path.Combine(corpusRoot, "works", "code-civil", "versions",
            "2025-04-20", "meta.json");
        var live = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(liveMetaPath), CorpusJson.Options)!;
        live.Events.Add(new EventEntry
        {
            Event = "withdrawn_from_source",
            ObservedFrom = "2026-08-13T08:05:02Z",
            Scope = "version",
            Detail = "publisher record absent from the current enumeration",
        });
        RefreshRecordHash(live);
        await File.WriteAllTextAsync(liveMetaPath,
            JsonSerializer.Serialize(live, CorpusJson.Options) + "\n");
        var manifestPath = Path.Combine(corpusRoot, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
        manifest.Works = 0;
        manifest.Versions = 0;
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");
        var integrity = CorpusIntegrity.Verify(corpusRoot);
        Assert.True(integrity.IsValid, string.Join(Environment.NewLine, integrity.Errors));
        var before = Inventory(corpusRoot);
        var current = new LegiluxReplacementAdapter(
            includeWithdrawn: false, includeWork: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("candidate is missing work", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_matches_distinct_legacy_same_date_states()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot,
            new SameDateAdapter(reverse: false));
        RenameSameDateVersionsToLegacyOrdinals(corpusRoot);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", new SameDateAdapter(reverse: true),
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(2, report.ActualVersions);
    }

    [Fact]
    public async Task Fresh_migration_rejects_indistinguishable_legacy_same_date_states()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot,
            new SameDateAdapter(reverse: false, shareSource: true));
        RenameSameDateVersionsToLegacyOrdinals(corpusRoot);
        var candidate = new SameDateAdapter(reverse: true, shareSource: true);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", candidate,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("indistinguishable states", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, candidate.BodyFetchCount);
    }

    [Fact]
    public async Task Fresh_v4_migration_matches_publisher_version_identity()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false), default);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test",
            new SameDateAdapter(reverse: true, shareSource: true),
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(2, report.ActualVersions);
    }

    [Fact]
    public async Task Fresh_migration_rejects_a_different_publisher_identity()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot);
        var before = Inventory(corpusRoot);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test",
                new ManyWorksAdapter(1, publisher: "other"),
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("does not match protected baseline", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_preserves_the_verified_backup_when_rollback_move_fails()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot);
        var initialSwapFailed = false;
        void Inject(string source, string destination)
        {
            if (source.Contains(".lex-fresh-stage-", StringComparison.Ordinal)
                && Path.GetFileName(source) == "manifest.json")
            {
                initialSwapFailed = true;
                throw new IOException("injected staged manifest move failure");
            }
            if (initialSwapFailed
                && source.Contains(".lex-fresh-backup-", StringComparison.Ordinal)
                && Path.GetFileName(source) == "manifest.json")
                throw new IOException("injected baseline restore failure");
        }

        var injected = typeof(FreshCorpusMigration).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "RunAsync"
                && method.GetParameters().Length == 7);
        var task = Assert.IsAssignableFrom<Task<CorpusIntegrityReport>>(
            injected.Invoke(null,
            [
                corpusRoot, "test", new ManyWorksAdapter(1),
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit,
                (Action<string, string>)Inject, CancellationToken.None,
            ]));
        await Assert.ThrowsAsync<IOException>(() => task);

        var backup = Assert.Single(Directory.EnumerateDirectories(_dir,
            ".candidate.lex-fresh-backup-*"));
        Assert.True(Directory.Exists(Path.Combine(backup, "works")));
        Assert.True(File.Exists(Path.Combine(backup, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(backup, "NOTICE")));
    }

    [Fact]
    public async Task Legacy_same_date_ordinal_layout_fails_closed_until_reingested()
    {
        var versions = Path.Combine(_dir, "works", "w1", "versions");
        Directory.CreateDirectory(Path.Combine(versions, "2025-07-28"));
        Directory.CreateDirectory(Path.Combine(versions, "2025-07-28--02"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
                .WriteAsync(new SameDateAdapter(reverse: false), default));

        Assert.Contains("full re-ingest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Normal_append_refuses_a_version_three_manifest()
    {
        await WriteLegacyBaselineAsync(_dir);
        var before = Snapshot();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir,
                    DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
                .WriteAsync(new ManyWorksAdapter(1), default));

        Assert.Contains("fresh-corpus migration", error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Snapshot());
    }

    private static async Task<SortedDictionary<string, string>> SameDateInventory(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var versions = Path.Combine(root, "works", "w1", "versions");
        foreach (var directory in Directory.EnumerateDirectories(versions))
        {
            var meta = JsonSerializer.Deserialize<VersionMeta>(
                await File.ReadAllTextAsync(Path.Combine(directory, "meta.json")), CorpusJson.Options)!;
            result.Add(Path.GetFileName(directory), meta.PublisherVersionIdentifier!);
        }
        return result;
    }

    [Fact]
    public async Task Existing_record_refreshes_normalized_metadata_with_an_append_only_event()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("publisher_metadata", "old-domain"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var path = Path.Combine(OneVersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(path), CorpusJson.Options)!;

        Assert.Equal("in_force", meta.Raw["binding_status"]);
        Assert.Equal("financial-services", meta.Raw["domains"]);
        var revision = Assert.Single(meta.Events, e => e.Event == "metadata_revised");
        Assert.Equal("2026-08-06T00:00:00Z", revision.ObservedFrom);
        Assert.Contains("raw.binding_status", revision.Detail);
        Assert.Contains("raw.domains", revision.Detail);
    }

    [Fact]
    public async Task Ingest_reports_a_real_expression_denominator_and_completion()
    {
        using var progress = new StringWriter();
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit, progress)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var lines = progress.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, line => line.Contains(
            "[progress] test: ingest expressions=0/1 percent=0.0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(
            "[progress] test: ingest expressions=1/1 percent=100.0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("eta=00:00:00 current=w1", StringComparison.Ordinal));

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.Expressions);
        Assert.Equal(0, manifest.ExpressionsWithText);
        Assert.Equal(1, manifest.ExpressionsWithoutText);
    }

    [Fact]
    public async Task Existing_record_adds_a_newly_available_language_by_identity()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services", ["en"]), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services", ["en", "fr"]), default);

        var path = Path.Combine(OneVersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(path), CorpusJson.Options)!;

        Assert.Equal(["en", "fr"], meta.Expressions.Select(e => e.Language));
        var added = Assert.Single(meta.Events, e => e.Event == "expression_added");
        Assert.Equal("fr", added.Scope);
        Assert.Equal("language=fr", added.Detail);
    }

    [Fact]
    public async Task Existing_record_refreshes_publisher_discovery_metadata_and_expression_short_title()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services", titleShort: "GDPR - Regulation"), default);
        var publisherMetadata = new[]
        {
            new PublisherMetadataRecord(
                "publisher_short_title",
                "http://publications.europa.eu/ontology/cdm#expression_title_short",
                "en",
                "gdpr, personal data protection",
                "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679"),
        };

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services", titleShort: "Regulation",
                publisherMetadata: publisherMetadata, documentRoles: ["delegated"]), default);

        var path = Path.Combine(OneVersionDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(await File.ReadAllTextAsync(path), CorpusJson.Options)!;

        Assert.Equal("Regulation", Assert.Single(meta.Expressions).TitleShort);
        Assert.Equal(publisherMetadata, meta.PublisherMetadata);
        Assert.Equal(["delegated"], meta.DocumentRoles);
        var revision = meta.Events.Last(e => e.Event == "metadata_revised");
        Assert.Contains("expressions.en.title_short", revision.Detail);
        Assert.Contains("publisher_metadata", revision.Detail);
        Assert.Contains("document_roles", revision.Detail);
    }

    [Fact]
    public async Task Missing_publisher_record_is_tombstoned_and_can_be_resighted()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit)
            .WriteAsync(new EmptyAdapter(), default);

        var path = Path.Combine(OneVersionDirectory, "meta.json");
        var withdrawn = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        Assert.Equal("withdrawn_from_source", withdrawn.Events[^1].Event);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var resighted = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        Assert.Equal("resighted", resighted.Events[^1].Event);
    }

    [Fact]
    public async Task Manifest_records_expected_works_that_produced_no_versions()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance", hasVersions: false), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.ScopeExpectedWorks);
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("no_versions", issue.Code);
        Assert.Equal("w1", issue.Work);
    }

    [Fact]
    public async Task Incomplete_enumeration_preserves_the_prior_clean_corpus_without_tombstones()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance"), default);
        var before = Snapshot();

        var failure = await Assert.ThrowsAsync<SourceEnumerationIncompleteException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
                .WriteAsync(new IncompleteAdapter(), default));

        Assert.Equal("incomplete_enumeration", failure.Issue.Code);
        Assert.Equal(before, Snapshot());
        var meta = await ReadVersionMeta();
        Assert.DoesNotContain(meta.Events, item => item.Event == "withdrawn_from_source");
    }

    [Fact]
    public async Task Failed_body_acquisition_rolls_back_every_candidate_file()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);
        var before = Snapshot();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
                .WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
                    titleHint: "Candidate title", bodyException: new HttpRequestException("network")), default));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Typed_metadata_only_outcome_is_signed_into_the_build_inventory()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: new SourceBodyFetch(SourceBodyStatus.PermanentNotFound,
                    Detail: "publisher returned 404", Attempts: 4)), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("body_not_found", issue.Code);
        Assert.Contains("attempts=4", issue.Detail);
        Assert.Equal(4, manifest.AcquisitionRetryMaximumAttempts);
        Assert.Equal("body_not_found", Assert.Single((await ReadVersionMeta()).Expressions).Text.Reason);
    }

    [Fact]
    public async Task Existing_record_restores_canonical_empty_metadata_and_text_url()
    {
        var adapter = new OneVersionAdapter("in_force", "financial-services");
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(adapter, default);
        var metaPath = Path.Combine(OneVersionDirectory, "meta.json");
        var stale = await ReadVersionMeta();
        stale.PublisherMetadata = [];
        stale.DocumentRoles = [];
        Assert.Single(stale.Expressions).Text.Url = "https://stale.example.test/body";
        RefreshRecordHash(stale);
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(stale, CorpusJson.Options) + "\n");

        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit)
            .WriteAsync(adapter, default);

        var current = await ReadVersionMeta();
        Assert.Null(current.PublisherMetadata);
        Assert.Null(current.DocumentRoles);
        var expression = Assert.Single(current.Expressions);
        Assert.Equal(expression.SourceUri, expression.Text.Url);
        var revision = current.Events.Last(item => item.Event == "metadata_revised");
        Assert.Contains("publisher_metadata", revision.Detail, StringComparison.Ordinal);
        Assert.Contains("document_roles", revision.Detail, StringComparison.Ordinal);
        Assert.Contains("expressions.en.text.url", revision.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("expressions.en.source_uri", revision.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Optional_manifestation_failure_does_not_hide_the_primary_body_failure()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance",
                bodyFetch: new SourceBodyFetch(
                    SourceBodyStatus.ParserFailure,
                    Detail: "primary identity did not match", Attempts: 2),
                altFetch: new SourceManifestationFetch(
                    SourceBodyStatus.PermanentNotFound,
                    Detail: "optional Formex returned 404")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("body_parser_failure", issue.Code);
        Assert.Contains("primary identity did not match", issue.Detail,
            StringComparison.Ordinal);
        Assert.DoesNotContain("optional Formex", issue.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_candidate_with_a_typed_issue_keeps_the_prior_corpus_selected()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);
        var before = Snapshot();

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit);
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
            titleHint: "Candidate title",
            bodyFetch: new SourceBodyFetch(SourceBodyStatus.PermanentNotFound,
                Detail: "publisher returned 404", Attempts: 4)), default,
            requireComplete: true);

        Assert.False(candidate.Committed);
        Assert.Equal("body_not_found", Assert.Single(candidate.BuildIssues).Code);
        Assert.Equal(before, Snapshot());
    }

    // The distinction that stopped the nightly. A publisher offering no XML for an expression is
    // not a failed acquisition, it is the publisher saying there is nothing to acquire in that
    // format, and the corpus already records that as Text.Reason with Available=false. Legilux
    // announces future-dated consolidations before their XML exists, so counting these as
    // acquisition failures discarded the whole candidate every night, for both publishers, with a
    // count that could only grow.
    [Fact]
    public async Task A_metadata_only_expression_does_not_discard_the_candidate()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit);
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
            titleHint: "Candidate title",
            bodyFetch: new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an XML manifestation for this expression.")),
            default, requireComplete: true);

        Assert.True(candidate.Committed);
        // Still recorded, so coverage stays honest: reported, not silently accepted.
        Assert.NotEmpty(candidate.BuildIssues);
        Assert.All(candidate.BuildIssues,
            issue => Assert.Equal("publisher_metadata_only", issue.Code));
        // The language the publisher offered nothing for carries the reason, and carries no
        // text: committing the candidate must not invent coverage it does not have.
        var expressions = (await ReadVersionMeta()).Expressions;
        var metadataOnly = Assert.Single(expressions,
            expression => expression.Text.Reason == "publisher_metadata_only");
        Assert.False(metadataOnly.Text.Available);
    }

    // The manifest's build_issues is the acquisition-failure record, and CorpusIntegrity bounds it
    // at 1000 entries. Writing coverage facts into it committed the corpus and then failed
    // integrity on the manifest, so the publish still produced nothing: 1,492 metadata-only
    // expressions against a limit of 1,000. Narrowing what blocks the gate was not enough,
    // because the gate was never what rejected the manifest.
    [Fact]
    public async Task Metadata_only_is_recorded_as_coverage_and_not_as_a_manifest_build_issue()
    {
        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit);
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
            bodyFetch: new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly,
                Detail: "The publisher did not enumerate an XML manifestation for this expression.")),
            default, requireComplete: true);

        Assert.True(candidate.Committed);

        var manifest = JsonNode.Parse(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")))!;
        var persisted = manifest["build_issues"]!.AsArray()
            .Select(issue => issue!["code"]!.GetValue<string>()).ToArray();
        Assert.DoesNotContain("publisher_metadata_only", persisted);

        // The fact is not lost, it is recorded where it belongs: on the expressions that carry no
        // text, and in the count the manifest publishes.
        Assert.Equal(2, manifest["expressions_without_text"]!.GetValue<int>());
        Assert.All((await ReadVersionMeta()).Expressions, expression =>
        {
            Assert.False(expression.Text.Available);
            Assert.Equal("publisher_metadata_only", expression.Text.Reason);
        });
    }

    // The other half, and the case that decides whether the narrowed gate is still a gate: one
    // expression the publisher offers nothing for, and one that genuinely failed to fetch. The
    // metadata-only issue must not rescue the candidate.
    [Fact]
    public async Task A_real_failure_beside_a_metadata_only_one_still_discards_the_candidate()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);
        var before = Snapshot();

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit);
        // fr and de, not en: the first write already observed en, and an expression with an
        // observation is skipped rather than refetched, so scripting en would never be reached.
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr", "de"],
            titleHint: "Candidate title",
            bodyFetchByLanguage: new Dictionary<string, SourceBodyFetch>(StringComparer.Ordinal)
            {
                ["fr"] = new(SourceBodyStatus.PublisherMetadataOnly,
                    Detail: "The publisher did not enumerate an XML manifestation for this expression."),
                ["de"] = new(SourceBodyStatus.RetryExhausted,
                    Detail: "publisher timed out", Attempts: 4),
            }), default, requireComplete: true);

        Assert.False(candidate.Committed);
        Assert.Equal(before, Snapshot());
        // Both were recorded; only the real failure decided the outcome.
        Assert.Contains(candidate.BuildIssues, issue => issue.Code == "publisher_metadata_only");
        Assert.Contains(candidate.BuildIssues, issue => issue.Code == "body_retry_exhausted");
    }

    [Fact]
    public void Legacy_manifest_without_expected_scope_keeps_inventory_unavailable()
    {
        var manifest = JsonSerializer.Deserialize<ManifestDoc>("""
            {
              "publisher":{"id":"test"},
              "tier":"A",
              "attribution":"test",
              "text_included":false,
              "text_public":false,
              "history_begins":"publisher",
              "ingester_version":"old"
            }
            """, CorpusJson.Options)!;

        Assert.Null(manifest.ScopeExpectedWorks);
    }

    [Fact]
    public async Task Manifest_counts_a_fetched_body_as_an_expression_with_text()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("<html>publisher text</html>")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.Expressions);
        Assert.Equal(1, manifest.ExpressionsWithText);
        Assert.Equal(0, manifest.ExpressionsWithoutText);
        Assert.True(File.Exists(Path.Combine(OneVersionDirectory, "en.html")));
    }

    [Fact]
    public async Task Empty_retrieved_body_is_a_typed_issue_not_stored_text()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: SourceBodyFetch.Retrieved("   ")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(0, manifest.ExpressionsWithText);
        Assert.Equal(1, manifest.ExpressionsWithoutText);
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("body_empty", issue.Code);

        var expr = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.False(expr.Text.Available);
        Assert.Equal("body_empty", expr.Text.Reason);
        Assert.Empty(expr.Observations);
        Assert.False(File.Exists(Path.Combine(OneVersionDirectory, "en.html")));
    }

    [Fact]
    public async Task Alt_manifestation_does_not_block_primary_body_backfill()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new AltThenPrimaryAdapter(new SourceBodyFetch(
                SourceBodyStatus.RetryExhausted, Detail: "network", Attempts: 2)), default);

        var versionDir = OneVersionDirectory;
        Assert.False(File.Exists(Path.Combine(versionDir, "en.html")));
        var first = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.True(first.Text.Available);                       // an alt manifestation IS observed text
        Assert.All(first.Observations, o => Assert.NotNull(o.Format));

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit)
            .WriteAsync(new AltThenPrimaryAdapter(
                SourceBodyFetch.Retrieved("<html>primary text</html>")), default);

        Assert.True(File.Exists(Path.Combine(versionDir, "en.html")));
        var second = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.Contains(second.Observations, o => o.Format is null);
    }

    private async Task<VersionMeta> ReadVersionMeta() => JsonSerializer.Deserialize<VersionMeta>(
        await File.ReadAllTextAsync(Path.Combine(OneVersionDirectory, "meta.json")),
        CorpusJson.Options)!;

    private string OneVersionDirectory => Path.Combine(
        _dir, "works", "w1", "versions", StableVersionKey("2024-01-01", "official:v1"));

    private static string StableVersionKey(string date, string publisherVersionIdentifier) =>
        date + "--" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(publisherVersionIdentifier)));

    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_dir, path).Replace('\\', '/'),
            path => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.Ordinal);

    private static Task WriteLegacyBaselineAsync(string root, int works = 1) =>
        WriteLegacyBaselineAsync(root, new ManyWorksAdapter(works));

    private static async Task WriteLegacyBaselineAsync(string root, ISourceAdapter adapter)
    {
        await new CorpusWriter(root,
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
            .WriteAsync(adapter, default);
        var path = Path.Combine(root, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        manifest.Schema = "lex-corpus/3";
        manifest.IngesterCodeCommit = null;
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");
        var integrity = CorpusIntegrity.Verify(root);
        Assert.True(integrity.IsValid, string.Join(Environment.NewLine, integrity.Errors));
    }

    private static SortedDictionary<string, string> Inventory(string root) =>
        new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(path))), StringComparer.Ordinal), StringComparer.Ordinal);

    private static void RenameSameDateVersionsToLegacyOrdinals(string root)
    {
        var versions = Path.Combine(root, "works", "w1", "versions");
        var directories = Directory.EnumerateDirectories(versions)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, directories.Length);
        Directory.Move(directories[0], Path.Combine(versions, "2025-07-28"));
        Directory.Move(directories[1], Path.Combine(versions, "2025-07-28--02"));
        var integrity = CorpusIntegrity.Verify(root);
        Assert.True(integrity.IsValid, string.Join(Environment.NewLine, integrity.Errors));
    }

    private static async Task<LegacyWithdrawalBaseline> WriteLegacyWithdrawalBaselineAsync(
        string root)
    {
        await new CorpusWriter(root,
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
            .WriteAsync(new LegiluxReplacementAdapter(includeWithdrawn: true), default);

        var versionsRoot = Path.Combine(root, "works", "code-civil", "versions");
        string? tombstoneTemporary = null;
        string? tombstoneFile = null;
        string? tombstoneSha = null;
        byte[]? tombstoneBytes = null;
        foreach (var directory in Directory.EnumerateDirectories(versionsRoot).ToArray())
        {
            var metaPath = Path.Combine(directory, "meta.json");
            var meta = JsonSerializer.Deserialize<VersionMeta>(
                await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
            var withdrawn = meta.PublisherVersionIdentifier
                == LegiluxReplacementAdapter.WithdrawnVersionIdentifier;
            var legacyKey = withdrawn ? "2025-04-20--02" : "2025-04-20";
            meta.PublisherVersionIdentifier = null;
            meta.LexId = $"lu-legilux:code-civil:{legacyKey}";
            if (withdrawn)
            {
                meta.Events.Add(new EventEntry
                {
                    Event = "withdrawn_from_source",
                    ObservedFrom = "2026-08-13T08:05:02Z",
                    Scope = "version",
                    Detail = "publisher record absent from the current enumeration",
                });
                var observation = Assert.Single(Assert.Single(meta.Expressions).Observations);
                tombstoneFile = observation.File!;
                tombstoneSha = observation.Sha256!;
                tombstoneBytes = await File.ReadAllBytesAsync(
                    Path.Combine(directory, tombstoneFile));
            }
            RefreshRecordHash(meta);
            await File.WriteAllTextAsync(metaPath,
                JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

            var temporary = directory + ".legacy";
            Directory.Move(directory, temporary);
            Directory.Move(temporary, Path.Combine(versionsRoot, legacyKey));
            if (withdrawn) tombstoneTemporary = Path.Combine(versionsRoot, legacyKey);
        }

        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
        manifest.Schema = "lex-corpus/3";
        manifest.IngesterCodeCommit = null;
        manifest.Versions = 1;
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");
        var integrity = CorpusIntegrity.Verify(root);
        Assert.True(integrity.IsValid, string.Join(Environment.NewLine, integrity.Errors));

        var tombstoneDirectory = Assert.IsType<string>(tombstoneTemporary);
        var file = Assert.IsType<string>(tombstoneFile);
        return new LegacyWithdrawalBaseline(
            Path.Combine(tombstoneDirectory, "meta.json"),
            Path.Combine(tombstoneDirectory, file),
            Assert.IsType<string>(tombstoneSha),
            Assert.IsType<byte[]>(tombstoneBytes));
    }

    private static Dictionary<string, (string Directory, VersionMeta Meta)>
        ReadVersionsByPublisherIdentity(string root) =>
        Directory.EnumerateDirectories(
                Path.Combine(root, "works", "code-civil", "versions"))
            .Select(directory => (Directory: directory, Meta:
                JsonSerializer.Deserialize<VersionMeta>(File.ReadAllText(
                    Path.Combine(directory, "meta.json")), CorpusJson.Options)!))
            .ToDictionary(item => item.Meta.PublisherVersionIdentifier!,
                item => item, StringComparer.Ordinal);

    private static void RefreshRecordHash(VersionMeta meta)
    {
        meta.RecordSha256 = null;
        meta.RecordSha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(meta, CorpusJson.Options))));
    }

    private sealed record LegacyWithdrawalBaseline(
        string MetaPath, string BodyPath, string BodySha256, byte[] BodyBytes);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class OneVersionAdapter(
        string bindingStatus,
        string domain,
        IReadOnlyList<string>? languages = null,
        string titleShort = "Work one",
        IReadOnlyList<PublisherMetadataRecord>? publisherMetadata = null,
        IReadOnlyList<string>? documentRoles = null,
        bool hasVersions = true,
        SourceBodyFetch? bodyFetch = null,
        string titleHint = "Work one",
        Exception? bodyException = null,
        IReadOnlyDictionary<string, SourceBodyFetch>? bodyFetchByLanguage = null,
        SourceManifestationFetch? altFetch = null) : ISourceAdapter
    {
        private readonly WorkRef _work = new(new Identifier("official:w1"), "w1", "REG", titleHint);

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            // A per-language script declares text inclusion just as a single fetch does. Without
            // this the whole version takes the metadata-only branch and a scripted real failure
            // is never reached, which is what made the mixed test pass for the wrong reason.
            [], languages ?? ["en"],
            TextIncluded: bodyFetch is not null || bodyException is not null
                          || bodyFetchByLanguage is not null,
            TextPublic: bodyFetch is not null || bodyException is not null
                        || bodyFetchByLanguage is not null,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return _work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
        {
            if (!hasVersions) return Task.FromResult<IReadOnlyList<VersionRecord>>([]);
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier("official:v1"), _work.Id, "REG", new DateOnly(2024, 1, 1), null,
                    "publisher", "true", new DateOnly(2024, 1, 1),
                    (languages ?? ["en"]).Select(language => new ExpressionRecord(
                        language, new DateOnly(2024, 1, 1), null, "publisher",
                        "Work one", titleShort, $"https://example.test/v1/{language}")).ToArray(),
                    [], new Dictionary<string, string>
                    {
                        ["binding_status"] = bindingStatus,
                        ["domains"] = domain,
                    }, publisherMetadata, documentRoles)
            ];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            if (bodyException is not null) throw bodyException;
            // Per-language override so one candidate can carry a real failure and a metadata-only
            // expression at once, which is the case that decides whether the narrowed gate is
            // still a gate.
            if (bodyFetchByLanguage?.TryGetValue(expression.Language, out var perLanguage) == true)
                return Task.FromResult(perLanguage);
            return Task.FromResult(bodyFetch
                ?? new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
        }


        public Task<SourceManifestationFetch> FetchAltManifestation(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(altFetch
                ?? new SourceManifestationFetch(SourceBodyStatus.PublisherMetadataOnly));
    }

    private sealed class SameDateAdapter(
        bool reverse, bool includeSecond = true, bool shareSource = false) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        private readonly WorkRef _work = new(new Identifier("official:w1"), "w1", "LOI", "Work one");
        public int BodyFetchCount { get; private set; }

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "LU", "https://example.test", Tier.A, "test", null),
            [], ["fr"], TextIncluded: true, TextPublic: true, HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return _work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
        {
            VersionRecord Version(string id) => new(
                new Identifier(id), _work.Id, "LOI", new DateOnly(2025, 7, 28), null,
                "publisher", null, null,
                [new ExpressionRecord("fr", new DateOnly(2025, 7, 28), null, "publisher",
                    $"Version {id}", null, shareSource
                        ? "https://example.test/shared"
                        : $"https://example.test/{id}")],
                [], new Dictionary<string, string>());
            IReadOnlyList<VersionRecord> versions = includeSecond
                ? [Version("official:v-a"), Version("official:v-b")]
                : [Version("official:v-a")];
            return Task.FromResult<IReadOnlyList<VersionRecord>>(
                reverse ? versions.Reverse().ToArray() : versions);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            BodyFetchCount++;
            return Task.FromResult(SourceBodyFetch.Retrieved(
                $"<html>{version.Id.Value}</html>"));
        }

        public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy) =>
            ResolveTestLegacyIdentity(legacy, expression =>
            {
                const string prefix = "https://example.test/";
                if (!expression.SourceUri.StartsWith(prefix, StringComparison.Ordinal)
                    || expression.SourceUri[prefix.Length..] == "shared")
                    throw new InvalidDataException(
                        "The test legacy same-date states are indistinguishable states.");
                return expression.SourceUri[prefix.Length..];
            });
    }

    private sealed class HistoryAdapter(bool includeHistorical) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        private readonly WorkRef _work = new(
            new Identifier("official:w1"), "w1", "REG", "Work one");
        public int BodyFetchCount { get; private set; }

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A,
                "test", null),
            [], ["en"], TextIncluded: true, TextPublic: true,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return _work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct)
        {
            VersionRecord Version(string id, DateOnly date) => new(
                new Identifier(id), _work.Id, "REG", date, null,
                "publisher", "true", date,
                [new ExpressionRecord("en", date, null, "publisher", "Work one",
                    "Work one", $"https://example.test/{id}/en")],
                [], new Dictionary<string, string>());
            IReadOnlyList<VersionRecord> versions = includeHistorical
                ? [Version("official:v0", new DateOnly(2023, 1, 1)),
                    Version("official:v1", new DateOnly(2024, 1, 1))]
                : [Version("official:v1", new DateOnly(2024, 1, 1))];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            BodyFetchCount++;
            return Task.FromResult(SourceBodyFetch.Retrieved(
                $"<html>{version.Id.Value}</html>"));
        }

        public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy) =>
            ResolveTestLegacyIdentity(legacy, expression =>
            {
                const string prefix = "https://example.test/";
                var suffix = "/" + expression.Language;
                if (!expression.SourceUri.StartsWith(prefix, StringComparison.Ordinal)
                    || !expression.SourceUri.EndsWith(suffix, StringComparison.Ordinal))
                    throw new InvalidDataException("The test history source URI is invalid.");
                return expression.SourceUri[prefix.Length..^suffix.Length];
            });
    }

    private sealed class LegiluxReplacementAdapter(
        bool includeWithdrawn,
        bool collideLegacyIdentity = false,
        bool includeWork = true,
        Action? beforeFirstBodyFetch = null) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        public const string WorkIdentifier =
            "http://data.legilux.public.lu/eli/etat/leg/code/civil";
        public const string WithdrawnVersionIdentifier =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/consolide/20250420";
        public const string LiveVersionIdentifier =
            "http://data.legilux.public.lu/eli/etat/leg/code/civil/20250420";
        private static readonly WorkRef Work = new(
            new Identifier(WorkIdentifier), "code-civil", "CODE", "Code civil");

        public int EnumerateCount { get; private set; }
        public int BodyFetchCount { get; private set; }
        public List<string> FetchedVersionIdentifiers { get; } = [];

        public PublisherDescriptor Describe() => new(
            new Publisher("lu-legilux", "Legilux", "LU",
                "https://legilux.public.lu", Tier.A, "test", null),
            [], ["fr"], TextIncluded: true, TextPublic: true,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            EnumerateCount++;
            if (!includeWork) yield break;
            yield return Work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct)
        {
            VersionRecord Version(string id, string publicSource) => new(
                new Identifier(id), Work.Id, "CODE", new DateOnly(2025, 4, 20), null,
                "publisher", "true", new DateOnly(2025, 4, 20),
                [new ExpressionRecord("fr", new DateOnly(2025, 4, 20), null,
                    "publisher", "Code civil", "Code civil", publicSource)],
                [], new Dictionary<string, string>());
            var live = Version(LiveVersionIdentifier,
                "https://legilux.public.lu/eli/etat/leg/code/civil/20250420/fr");
            IReadOnlyList<VersionRecord> versions = includeWithdrawn
                ? [Version(WithdrawnVersionIdentifier,
                    "https://legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/consolide/20250420/fr"),
                    live]
                : [live];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            if (BodyFetchCount == 0) beforeFirstBodyFetch?.Invoke();
            BodyFetchCount++;
            FetchedVersionIdentifiers.Add(version.Id.Value);
            return Task.FromResult(SourceBodyFetch.Retrieved(
                $"<html>{version.Id.Value}</html>"));
        }

        public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy) =>
            collideLegacyIdentity
                ? new Identifier(LiveVersionIdentifier)
                : new LegiluxAdapter().ResolveLegacyVersionIdentity(legacy);
    }

    private sealed class IncompleteAdapter : ISourceAdapter, ISourceBuildInventory
    {
        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en"], TextIncluded: true, TextPublic: true, HistoryBegins: "publisher");

        public SourceBuildInventory GetBuildInventory() => new(1, [], EnumerationComplete: false, RetryMaximumAttempts: 4);

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>([]);

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            throw new InvalidOperationException("No work may be fetched after an incomplete enumeration.");
    }

    private static async Task<CorpusIntegrityReport> RunFreshWithStageWriteHook(
        string corpusRoot, ISourceAdapter adapter, Action<string> beforeStageFileWrite)
    {
        var migrate = typeof(FreshCorpusMigration).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "RunAsync"
                && method.GetParameters().Length == 8);
        var task = Assert.IsAssignableFrom<Task<CorpusIntegrityReport>>(migrate.Invoke(null,
        [
            corpusRoot, "test", adapter,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit,
            null, beforeStageFileWrite, CancellationToken.None,
        ]));
        return await task;
    }

    private sealed class AltThenPrimaryAdapter(
        SourceBodyFetch bodyFetch,
        string manifestationMember = "main.xml",
        string workSlug = "w1",
        string language = "en") : ISourceAdapter
    {
        private readonly WorkRef _work = new(
            new Identifier("official:w1"), workSlug, "REG", "Work one");

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], [language], TextIncluded: true, TextPublic: true, HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return _work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
        {
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier("official:v1"), _work.Id, "REG", new DateOnly(2024, 1, 1), null,
                    "publisher", "true", new DateOnly(2024, 1, 1),
                    [new ExpressionRecord(language, new DateOnly(2024, 1, 1), null, "publisher",
                        "Work one", "Work one", $"https://example.test/v1/{language}")],
                    [], new Dictionary<string, string>(), null, null)
            ];
            return Task.FromResult(versions);
        }

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(bodyFetch);

        public Task<SourceManifestationFetch> FetchAltManifestation(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(SourceManifestationFetch.Retrieved(new ManifestationFetch(
                "fmx4", [new ManifestationMember(manifestationMember, "<xml/>"u8.ToArray())],
                $"https://example.test/v1/{language}/fmx4")));
    }

    private sealed class EmptyAdapter : ISourceAdapter
    {
        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en"], TextIncluded: false, TextPublic: false, HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>([]);

        public Task<SourceBodyFetch> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
    }

    private sealed class CrossLanguageTitleAdapter : ISourceAdapter
    {
        private static readonly WorkRef Work = new(
            new Identifier("official:w1"), "w1", "REG", "Règlement de test");

        public PublisherDescriptor Describe() => new(
            new Publisher("test", "Test", "EU", "https://example.test", Tier.A, "test", null),
            [], ["en", "fr"], TextIncluded: false, TextPublic: false,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return Work;
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>
            ([new VersionRecord(
                new Identifier("official:v1"), Work.Id, "REG",
                new DateOnly(2024, 1, 1), null, "publisher", "true",
                new DateOnly(2024, 1, 1),
                [new ExpressionRecord("en", new DateOnly(2024, 1, 1), null,
                     "publisher", null, "32024R0001", "https://example.test/v1/en"),
                 new ExpressionRecord("fr", new DateOnly(2024, 1, 1), null,
                     "publisher", "Règlement de test", "Règlement de test",
                     "https://example.test/v1/fr")],
                [], new Dictionary<string, string>())]);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            Task.FromResult(new SourceBodyFetch(SourceBodyStatus.PublisherMetadataOnly));
    }

    private sealed class ManyWorksAdapter(
        int count, string publisher = "test", int first = 1) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        public int BodyFetchCount { get; private set; }

        public PublisherDescriptor Describe() => new(
            new Publisher(publisher, publisher, "EU", "https://example.test", Tier.A,
                "test", null),
            [], ["en"], TextIncluded: true, TextPublic: true,
            HistoryBegins: "publisher");

        public async IAsyncEnumerable<WorkRef> EnumerateWorks(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var index = first; index < first + count; index++)
            {
                ct.ThrowIfCancellationRequested();
                yield return new WorkRef(new Identifier($"official:w{index}"),
                    $"w{index}", "REG", $"Work {index}");
            }
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionRecord>>(
            [
                new(new Identifier($"{work.Id.Value}:v1"), work.Id, "REG",
                    new DateOnly(2024, 1, 1), null, "publisher", "true",
                    new DateOnly(2024, 1, 1),
                    [new ExpressionRecord("en", new DateOnly(2024, 1, 1), null,
                        "publisher", work.TitleHint, work.TitleHint,
                        $"https://example.test/{work.Slug}/v1/en")],
                    [], new Dictionary<string, string>())
            ]);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            BodyFetchCount++;
            return Task.FromResult(SourceBodyFetch.Retrieved("""
                <html><body>
                <p class="title-article-norm">Article 1</p>
                <p>Protected official wording.</p>
                </body></html>
                """));
        }

        public Identifier ResolveLegacyVersionIdentity(LegacyVersionIdentity legacy)
        {
            var versionIdentifier = legacy.WorkIdentifier + ":v1";
            var slug = legacy.WorkIdentifier.StartsWith("official:", StringComparison.Ordinal)
                ? legacy.WorkIdentifier["official:".Length..]
                : throw new InvalidDataException("The test work identity is invalid.");
            return ResolveTestLegacyIdentity(legacy, expression =>
            {
                var expected = $"https://example.test/{slug}/v1/{expression.Language}";
                if (!string.Equals(expression.SourceUri, expected, StringComparison.Ordinal))
                    throw new InvalidDataException("The test work source URI is invalid.");
                return versionIdentifier;
            });
        }
    }
}
