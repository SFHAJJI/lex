using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Derive;
using Lex.Ingest;
using Lex.Index;
using Lex.Law;
using Lex.Mcp;
using Lex.Sources.Legilux;
using Lex.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lex.Tests;

public sealed partial class CorpusWriterTests : IDisposable
{
    private const string CodeCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string EffectiveBodyUri = "https://publisher.example/effective/body";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lex-writer-{Guid.NewGuid():N}");

    private static SourceBodyFetch RetrievedBody(string text) =>
        SourceBodyFetch.Retrieved(
            Encoding.UTF8.GetBytes(text),
            new SourceHttpEvidence(
                200, "text/html", "utf-8", null, null,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), EffectiveBodyUri));

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
    public async Task Same_date_publisher_identity_replacement_preserves_the_verified_prior_state()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var before = Assert.Single(await SameDateInventory(_dir));
        var priorDirectory = Path.Combine(
            _dir, "works", "w1", "versions", before.Key);
        var priorMeta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(Path.Combine(priorDirectory, "meta.json")),
            CorpusJson.Options)!;
        var priorObservation = Assert.Single(
            Assert.Single(priorMeta.Expressions).Observations);
        var priorBytes = await File.ReadAllBytesAsync(
            Path.Combine(priorDirectory, priorObservation.File!));
        var priorEvents = priorMeta.Events.Select(entry =>
            JsonSerializer.Serialize(entry, CorpusJson.Options)).ToArray();

        var replacement = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-replacement-1");
        var replacementAdapter = new SameDateAdapter(
            reverse: false, includeFirst: false);
        await replacement.WriteAsync(replacementAdapter, default);

        var after = await SameDateInventory(_dir);
        Assert.True(replacement.Accepted);
        Assert.True(replacement.Committed);
        Assert.Equal(2, after.Count);
        Assert.Equal("official:v-a", after[before.Key]);
        Assert.Contains("official:v-b", after.Values);
        Assert.Equal(1, replacementAdapter.BodyFetchCount);
        var retained = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(Path.Combine(priorDirectory, "meta.json")),
            CorpusJson.Options)!;
        Assert.Equal(priorEvents, retained.Events.Take(priorEvents.Length)
            .Select(entry => JsonSerializer.Serialize(entry, CorpusJson.Options)));
        var pending = retained.Events[^1];
        Assert.Equal("absent_unconfirmed", pending.Event);
        Assert.Equal("2026-08-15T00:00:00Z", pending.FirstMissedAt);
        Assert.Equal(1, pending.RunsMissed);
        var replacementManifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Equal(1, replacementManifest.Works);
        Assert.Equal(2, replacementManifest.Versions);
        var retainedObservation = Assert.Single(
            Assert.Single(retained.Expressions).Observations);
        Assert.Equal(priorObservation.Sha256, retainedObservation.Sha256);
        Assert.Equal(priorBytes, await File.ReadAllBytesAsync(
            Path.Combine(priorDirectory, retainedObservation.File!)));
        var integrity = CorpusIntegrity.Verify(_dir);
        Assert.True(integrity.IsValid, string.Join(Environment.NewLine, integrity.Errors));
    }

    [Fact]
    public async Task V3_same_date_shared_uri_changed_bytes_append_a_file_replaced_event()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false, shareSource: true), default);
        var priorDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(_dir, "works", "w1", "versions")));
        var prior = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(Path.Combine(priorDirectory, "meta.json")),
            CorpusJson.Options)!;
        var priorObservation = Assert.Single(
            Assert.Single(prior.Expressions).Observations);
        var priorBytes = await File.ReadAllBytesAsync(
            Path.Combine(priorDirectory, priorObservation.File!));

        await new CorpusWriter(
                _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-replacement-2")
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeFirst: false, shareSource: true), default);

        var versions = Directory.EnumerateDirectories(
                Path.Combine(_dir, "works", "w1", "versions"))
            .Select(directory => (Directory: directory, Meta:
                JsonSerializer.Deserialize<VersionMeta>(File.ReadAllText(
                    Path.Combine(directory, "meta.json")), CorpusJson.Options)!))
            .ToDictionary(item => item.Meta.PublisherVersionIdentifier!,
                item => item, StringComparer.Ordinal);
        var replacement = versions["official:v-b"];
        var replacementObservation = Assert.Single(
            Assert.Single(replacement.Meta.Expressions).Observations);
        Assert.Equal(priorObservation.SourceUri, replacementObservation.SourceUri);
        Assert.NotEqual(priorObservation.Sha256, replacementObservation.Sha256);
        var eventEntry = Assert.Single(replacement.Meta.Events,
            entry => entry.Event == "file_replaced");
        Assert.Equal("fr", eventEntry.Scope);
        Assert.Contains(prior.LexId, eventEntry.Detail, StringComparison.Ordinal);
        Assert.Contains(priorObservation.Sha256!, eventEntry.Detail, StringComparison.Ordinal);
        Assert.Equal(priorBytes, await File.ReadAllBytesAsync(
            Path.Combine(priorDirectory, priorObservation.File!)));
    }

    [Fact]
    public async Task Same_date_replacement_rejects_an_unverified_prior_v4_identity()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var before = Assert.Single(await SameDateInventory(_dir));
        var metaPath = Path.Combine(_dir, "works", "w1", "versions", before.Key, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.PublisherVersionIdentifier = "official:tampered";
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, CorpusJson.Options));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(new SameDateAdapter(
                    reverse: false, includeFirst: false), default));

        Assert.Contains("identity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("record-sha")]
    [InlineData("observation-bytes")]
    [InlineData("work-meta")]
    [InlineData("manifest-publisher")]
    [InlineData("manifest-missing")]
    public async Task V3_replacement_rejects_a_tampered_baseline_before_body_fetch(
        string target)
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var priorDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(_dir, "works", "w1", "versions")));
        var metaPath = Path.Combine(priorDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        switch (target)
        {
            case "record-sha":
                meta.DocumentType = "TAMPERED";
                await File.WriteAllTextAsync(metaPath,
                    JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
                break;
            case "observation-bytes":
                await File.AppendAllTextAsync(Path.Combine(priorDirectory,
                    Assert.Single(Assert.Single(meta.Expressions).Observations).File!), "tampered");
                break;
            case "work-meta":
                var workPath = Path.Combine(_dir, "works", "w1", "meta.json");
                var work = JsonSerializer.Deserialize<WorkMeta>(
                    await File.ReadAllTextAsync(workPath), CorpusJson.Options)!;
                work.WorkIdentifier = "official:other";
                await File.WriteAllTextAsync(workPath,
                    JsonSerializer.Serialize(work, CorpusJson.Options) + "\n");
                break;
            case "manifest-publisher":
                var manifestPath = Path.Combine(_dir, "manifest.json");
                var manifest = JsonSerializer.Deserialize<ManifestDoc>(
                    await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
                manifest.Publisher["id"] = "other";
                await File.WriteAllTextAsync(manifestPath,
                    JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");
                break;
            case "manifest-missing":
                File.Delete(Path.Combine(_dir, "manifest.json"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
        var before = Inventory(_dir);
        var replacement = new SameDateAdapter(reverse: false, includeFirst: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(replacement, default));

        Assert.Equal(0, replacement.BodyFetchCount);
        Assert.Equal(before, Inventory(_dir));
    }

    [Theory]
    [InlineData("metadata-change")]
    [InlineData("body-change")]
    [InlineData("path-addition")]
    [InlineData("path-removal")]
    public async Task V3_append_rejects_every_post_fetch_baseline_change(string mutation)
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var priorDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(_dir, "works", "w1", "versions")));
        var metaPath = Path.Combine(priorDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        var bodyPath = Path.Combine(priorDirectory,
            Assert.Single(Assert.Single(meta.Expressions).Observations).File!);
        var unexpected = Path.Combine(priorDirectory, "unexpected.txt");
        var manifestBefore = await File.ReadAllBytesAsync(Path.Combine(_dir, "manifest.json"));
        void MutateBaseline()
        {
            switch (mutation)
            {
                case "metadata-change": File.AppendAllText(metaPath, " "); break;
                case "body-change": File.AppendAllText(bodyPath, "tampered"); break;
                case "path-addition": File.WriteAllText(unexpected, "tampered"); break;
                case "path-removal": File.Delete(bodyPath); break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }
        var replacement = new SameDateAdapter(
            reverse: false, includeFirst: false,
            beforeFirstBodyFetch: MutateBaseline);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-toctou-1")
                .WriteAsync(replacement, default));

        Assert.Contains("changed during ingest", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, replacement.BodyFetchCount);
        Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(Path.Combine(_dir, "manifest.json")));
        Assert.DoesNotContain("official:v-b", (await SameDateInventory(_dir)).Values);
    }

    [Fact]
    public async Task Candidate_commit_compares_the_baseline_after_the_commit_boundary_hook()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var priorDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(_dir, "works", "w1", "versions")));
        var metaPath = Path.Combine(priorDirectory, "meta.json");
        var replacement = new SameDateAdapter(reverse: false, includeFirst: false);
        var writer = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-cas-1");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            WriteWithCommitHook(writer, replacement,
                () => File.AppendAllText(metaPath, " ")));

        Assert.Contains("changed during ingest", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official:v-b", (await SameDateInventory(_dir)).Values);
    }

    [Fact]
    public async Task A_second_cooperative_writer_cannot_enter_the_same_corpus()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var firstAdapter = new SameDateAdapter(
            reverse: false, includeFirst: false,
            beforeFirstBodyFetch: () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            });
        var first = Task.Run(() => new CorpusWriter(
                _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-lock-1")
            .WriteAsync(firstAdapter, default));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new CorpusWriter(_dir,
                        DateTimeOffset.Parse("2026-08-15T00:05:00Z"), CodeCommit,
                        runIdentity: "nightly-lock-2")
                    .WriteAsync(new EmptyAdapter(), default));
            Assert.Contains("writer lock", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            release.Set();
            await first;
        }
    }

    [Fact]
    public async Task V3_replacement_rejects_a_coherently_rebound_coordinate_with_a_stale_record_sha()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var versions = Path.Combine(_dir, "works", "w1", "versions");
        var priorDirectory = Assert.Single(Directory.EnumerateDirectories(versions));
        var replacementKey = StableVersionKey("2025-07-28", "official:v-b");
        var reboundDirectory = Path.Combine(versions, replacementKey);
        Directory.Move(priorDirectory, reboundDirectory);
        var metaPath = Path.Combine(reboundDirectory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        meta.PublisherVersionIdentifier = "official:v-b";
        meta.LexId = $"test:w1:{replacementKey}";
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        var before = Inventory(_dir);
        var replacement = new SameDateAdapter(reverse: false, includeFirst: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(replacement, default));

        Assert.Equal(0, replacement.BodyFetchCount);
        Assert.Equal(before, Inventory(_dir));
    }

    [Theory]
    [InlineData("publisher")]
    [InlineData("work")]
    [InlineData("lex-id")]
    [InlineData("valid-from")]
    public async Task V3_existing_expected_coordinate_must_keep_every_identity_binding(
        string target)
    {
        var adapter = new SameDateAdapter(reverse: false, includeSecond: false);
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(adapter, default);
        var directory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(_dir, "works", "w1", "versions")));
        var metaPath = Path.Combine(directory, "meta.json");
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        switch (target)
        {
            case "publisher": meta.Publisher = "other"; break;
            case "work": meta.WorkIdentifier = "official:other"; break;
            case "lex-id": meta.LexId += ":other"; break;
            case "valid-from": meta.ValidFrom = "2025-07-29"; break;
            default: throw new ArgumentOutOfRangeException(nameof(target));
        }
        RefreshRecordHash(meta);
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        var before = Inventory(_dir);
        var refresh = new SameDateAdapter(reverse: false, includeSecond: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(refresh, default));

        Assert.Equal(0, refresh.BodyFetchCount);
        Assert.Equal(before, Inventory(_dir));
    }

    [Fact]
    public async Task V3_plan_rejects_a_version_bound_to_another_enumerated_work_before_body_fetch()
    {
        var before = Inventory(_dir);
        var mismatched = new SameDateAdapter(
            reverse: false, includeSecond: false,
            versionWorkIdentifier: "official:other");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(mismatched, default));

        Assert.Equal(0, mismatched.BodyFetchCount);
        Assert.Equal(before, Inventory(_dir));
    }

    [Fact]
    public async Task V3_append_rejects_rebinding_an_existing_slug_to_another_work_before_reuse()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var before = Inventory(_dir);
        var rebound = new SameDateAdapter(
            reverse: false, includeSecond: false,
            workIdentifier: "official:rebound-work");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(rebound, default));

        Assert.Equal(0, rebound.BodyFetchCount);
        Assert.Equal(before, Inventory(_dir));
    }

    [Fact]
    public async Task V3_contract_poll_records_fresh_observations_and_materializer_identity()
    {
        const string laterCodeCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var adapter = new OneVersionAdapter("in_force", "finance",
            bodyFetch: RetrievedBody("<html>publisher text</html>"));
        var first = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit);
        await first.WriteAsync(adapter, default);
        var before = Snapshot();

        var poll = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), laterCodeCommit);
        await poll.WriteAsync(adapter, default);

        Assert.True(poll.Accepted);
        Assert.True(poll.Committed);
        Assert.NotEqual(before, Snapshot());
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Equal(laterCodeCommit, manifest.IngesterCodeCommit);
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

    [Fact]
    public async Task Publisher_structural_empty_coverage_never_becomes_a_search_or_Mcp_provision()
    {
        var corpus = Path.Combine(_dir, "coverage-corpus");
        var articles = Path.Combine(_dir, "coverage-articles");
        var db = Path.Combine(_dir, "coverage.db");
        const string coverageAnchor = "publisherplaceholder";
        const string coverageWId = "/eli/etat/leg/loi/2025/01/01/n1/art_placeholder";
        var body = $$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"><act><body>
              <article id="{{coverageAnchor}}" wId="{{coverageWId}}"><num/><alinea><content><p/></content></alinea></article>
              <article id="real" wId="/eli/etat/leg/loi/2025/01/01/n1/art_1"><num>Art. 1.</num><alinea><content><p>Searchable legal wording.</p></content></alinea></article>
            </body></act></akomaNtoso>
            """;
        await new CorpusWriter(corpus, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("true", "finance",
                bodyFetch: RetrievedBody(body)), default);
        var corpusCommit = CommitGitDirectory(corpus);

        var stats = DeriveWriter.Derive(corpus, articles, "test",
            new string('b', 40), new string('d', 40), corpusCommit);
        Assert.Empty(stats.Errors);
        var derivedPath = Directory.EnumerateFiles(articles, "en.json", SearchOption.AllDirectories)
            .Single();
        var derived = JsonNode.Parse(await File.ReadAllTextAsync(derivedPath))!.AsObject();
        Assert.Equal("real", Assert.Single(derived["provisions"]!.AsArray())!["anchor"]!.GetValue<string>());
        var coverage = Assert.Single(derived["publisher_structural_empty_articles"]!.AsArray())!;
        Assert.Equal(coverageAnchor, coverage["anchor"]!.GetValue<string>());
        Assert.Equal(coverageWId, coverage["w_id"]!.GetValue<string>());

        var articlesCommit = CommitGitDirectory(articles);
        IndexFromCorpus.Build(corpus, articles, db, null,
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"),
            codeCommit: new string('c', 40), articlesCommit: articlesCommit,
            corpusCommit: corpusCommit);

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM provisions WHERE anchor=$anchor),
                  (SELECT COUNT(*) FROM fts WHERE fts MATCH $query),
                  (SELECT COUNT(*) FROM semantic_chunks)
                """;
            command.Parameters.AddWithValue("$anchor", coverageAnchor);
            command.Parameters.AddWithValue("$query", coverageAnchor);
            using var row = command.ExecuteReader();
            Assert.True(row.Read());
            Assert.Equal(0, row.GetInt32(0));
            Assert.Equal(0, row.GetInt32(1));
            Assert.Equal(0, row.GetInt32(2));
        }
        using var reader = LexIndexReader.Open(db);
        Assert.Empty(reader.SearchKeyword(
            coverageAnchor, FilterSet.All, 10, fuzzyAuto: false).Hits);
        var response = Assert.IsType<JsonObject>(new McpCore(
            new Dictionary<string, LexIndexReader> { [reader.Collection] = reader })
            .CallTool("as_of", new JsonObject
            {
                ["work"] = "test:w1", ["date"] = "2024-01-01", ["mode"] = "outline",
            }));
        var returned = Assert.IsType<JsonArray>(response["provisions"]);
        Assert.Equal("real", Assert.Single(returned)!["anchor"]!.GetValue<string>());
        Assert.DoesNotContain(coverageAnchor, response.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(coverageWId, response.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whitespace_empty_derived_provisions_remain_evidence_but_never_enter_runtime()
    {
        const string whitespace = " \r\n\t";
        const string emptyHeading = "Runtime exclusion canary heading";

        static void MakeWhitespaceOnly(JsonObject provision, string heading)
        {
            provision["heading"] = heading;
            provision["text_md"] = whitespace;
            provision["text_sha256"] = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(whitespace)));
        }

        async Task<(string Db, JsonObject Evidence)> Build(
            string name, string html, Action<JsonArray> edit,
            Action<JsonObject, JsonObject>? editHistory = null,
            bool expectInvalid = false,
            string? expectedInvalidMessage = null,
            bool catalogBeforeEdit = false)
        {
            var fixture = Path.Combine(_dir, name);
            var corpus = Path.Combine(fixture, "corpus");
            var articles = Path.Combine(fixture, "articles");
            var db = Path.Combine(fixture, "index-test.db");
            await new CorpusWriter(corpus, DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
                .WriteAsync(new OneVersionAdapter("true", "finance",
                    bodyFetch: RetrievedBody(html)), default);
            var corpusCommit = CommitGitDirectory(corpus);
            var stats = DeriveWriter.Derive(corpus, articles, "test",
                new string('b', 40), new string('d', 40), corpusCommit);
            Assert.Empty(stats.Errors);
            var derivedPath = Directory.EnumerateFiles(
                articles, "en.json", SearchOption.AllDirectories).Single();
            var evidence = JsonNode.Parse(await File.ReadAllTextAsync(derivedPath))!.AsObject();
            if (catalogBeforeEdit) CatalogBuilder.Build(articles);
            edit(evidence["provisions"]!.AsArray());
            await File.WriteAllTextAsync(derivedPath, evidence.ToJsonString());
            if (!catalogBeforeEdit) CatalogBuilder.Build(articles);
            if (editHistory is not null)
            {
                var historyPath = Path.Combine(articles, "test", "works", "w1", "history.json");
                var history = JsonNode.Parse(await File.ReadAllTextAsync(historyPath))!.AsObject();
                editHistory(evidence, history);
                await File.WriteAllTextAsync(historyPath, history.ToJsonString());
            }
            var articlesCommit = CommitGitDirectory(articles);
            void BuildIndex() => IndexFromCorpus.Build(corpus, articles, db, null,
                    DateTimeOffset.Parse("2026-08-14T00:00:00Z"),
                    codeCommit: new string('c', 40), articlesCommit: articlesCommit,
                    corpusCommit: corpusCommit);
            if (expectInvalid)
            {
                var error = Assert.ThrowsAny<Exception>(BuildIndex);
                if (expectedInvalidMessage is not null)
                    Assert.Contains(expectedInvalidMessage, error.Message,
                        StringComparison.Ordinal);
                Assert.False(File.Exists(db));
            }
            else
                BuildIndex();
            return (db, evidence);
        }

        var mixed = await Build("mixed", """
            <html><body>
            <p class="title-article-norm">Article 1</p><p>First searchable rule.</p>
            <p class="title-article-norm">Article 2</p>
            <p class="title-article-norm">Article 3</p><p>Third searchable rule.</p>
            </body></html>
            """, provisions => MakeWhitespaceOnly(
                Assert.IsType<JsonObject>(provisions[1]), emptyHeading),
            editHistory: (evidence, history) =>
            {
                var provisions = evidence["provisions"]!.AsArray();
                var real = Assert.IsType<JsonObject>(provisions[0]);
                var blank = Assert.IsType<JsonObject>(provisions[1]);
                var events = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "inserted",
                        ["anchor"] = real["anchor"]!.GetValue<string>(),
                        ["at_version"] = evidence["lex_id"]!.GetValue<string>(),
                    },
                    new JsonObject
                    {
                        ["type"] = "renumbered",
                        ["from"] = blank["anchor"]!.GetValue<string>(),
                        ["to"] = real["anchor"]!.GetValue<string>(),
                        ["text_sha256"] = blank["text_sha256"]!.GetValue<string>(),
                        ["at_version"] = evidence["lex_id"]!.GetValue<string>(),
                    });
                history["anchor_events"] = events.DeepClone();
                history["anchor_events_by_language"]!["en"] = events;
            });
        var mixedEvidence = mixed.Evidence["provisions"]!.AsArray();
        Assert.Equal(3, mixedEvidence.Count);
        Assert.Equal(whitespace, mixedEvidence[1]!["text_md"]!.GetValue<string>());
        Assert.Equal(emptyHeading, mixedEvidence[1]!["heading"]!.GetValue<string>());
        var emptyAnchor = mixedEvidence[1]!["anchor"]!.GetValue<string>();
        var emptyTextSha = mixedEvidence[1]!["text_sha256"]!.GetValue<string>();

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                   $"Data Source={mixed.Db};Mode=ReadOnly"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM provisions),
                  (SELECT COUNT(*) FROM provisions WHERE seq NOT IN (0,1)),
                  (SELECT COUNT(*) FROM provisions WHERE anchor=$empty),
                  (SELECT COUNT(*) FROM provision_states WHERE anchor=$empty),
                  (SELECT COUNT(*) FROM anchor_events
                    WHERE from_anchor=$empty OR to_anchor=$empty OR anchor=$empty),
                  (SELECT COUNT(*) FROM provision_states),
                  (SELECT COUNT(*) FROM anchor_events),
                  (SELECT COUNT(*) FROM lexical_states WHERE text_sha=$empty_sha),
                  (SELECT COUNT(*) FROM text_blobs WHERE text_sha=$empty_sha),
                  (SELECT COUNT(*) FROM semantic_chunks WHERE state_id IN
                    (SELECT state_id FROM lexical_states WHERE text_sha=$empty_sha)),
                  (SELECT COUNT(*) FROM fts WHERE rowid IN
                    (SELECT state_id FROM lexical_states WHERE text_sha=$empty_sha)),
                  (SELECT COUNT(*) FROM fts WHERE fts MATCH $canary),
                  (SELECT text_available FROM docs LIMIT 1),
                  (SELECT text_public FROM docs LIMIT 1)
                """;
            command.Parameters.AddWithValue("$empty", emptyAnchor);
            command.Parameters.AddWithValue("$empty_sha", emptyTextSha);
            command.Parameters.AddWithValue("$canary", "runtime exclusion canary heading");
            using var row = command.ExecuteReader();
            Assert.True(row.Read());
            Assert.Equal(2, row.GetInt32(0));
            Assert.Equal(0, row.GetInt32(1));
            Assert.Equal(0, row.GetInt32(2));
            Assert.Equal(0, row.GetInt32(3));
            Assert.Equal(0, row.GetInt32(4));
            Assert.Equal(2, row.GetInt32(5));
            Assert.Equal(1, row.GetInt32(6));
            Assert.Equal(0, row.GetInt32(7));
            Assert.Equal(0, row.GetInt32(8));
            Assert.Equal(0, row.GetInt32(9));
            Assert.Equal(0, row.GetInt32(10));
            Assert.Equal(0, row.GetInt32(11));
            Assert.Equal(1, row.GetInt32(12));
            Assert.Equal(1, row.GetInt32(13));
        }
        using (var reader = LexIndexReader.Open(mixed.Db))
        {
            var document = reader.AsOf("w1", new DateOnly(2024, 1, 1), FilterSet.All)!;
            Assert.Equal([0, 1], reader.Provisions(LexIndexReader.RidOf(document)).Select(p => p.Seq));
            var body = reader.BuildBody(document)!;
            Assert.Contains("First searchable rule.", body, StringComparison.Ordinal);
            Assert.Contains("Third searchable rule.", body, StringComparison.Ordinal);
            Assert.DoesNotContain(emptyHeading, body, StringComparison.Ordinal);
            Assert.Empty(reader.SearchKeyword(emptyHeading, FilterSet.All, 10, fuzzyAuto: false).Hits);
            var core = new McpCore(
                new Dictionary<string, LexIndexReader> { [reader.Collection] = reader });
            var response = Assert.IsType<JsonObject>(core.CallTool("as_of", new JsonObject
                {
                    ["work"] = "test:w1", ["date"] = "2024-01-01", ["mode"] = "full",
                }));
            Assert.Equal(2, response["total_provisions"]!.GetValue<int>());
            Assert.DoesNotContain(emptyHeading, response.ToJsonString(), StringComparison.Ordinal);
            Assert.Equal("3", reader.Stamp["derived_provisions"]);
            Assert.Equal("2", reader.Stamp["indexed_provisions"]);
            Assert.Equal("1", reader.Stamp["excluded_empty_provisions"]);
        }
        using (var site = new RuntimeIndexSite(Path.GetDirectoryName(mixed.Db)!))
        {
            var html = await site.Client.GetStringAsync("/test/w1/2024-01-01");
            Assert.Contains("First searchable rule.", html, StringComparison.Ordinal);
            Assert.Contains("Third searchable rule.", html, StringComparison.Ordinal);
            Assert.DoesNotContain(emptyHeading, html, StringComparison.Ordinal);
        }

        var allEmpty = await Build("all-empty", """
            <html><body>
            <p class="title-article-norm">Article 1</p><p>Temporary wording.</p>
            <p class="title-article-norm">Article 2</p><p>Temporary wording too.</p>
            </body></html>
            """, provisions =>
            {
                for (var index = 0; index < provisions.Count; index++)
                    MakeWhitespaceOnly(Assert.IsType<JsonObject>(provisions[index]),
                        $"All-empty canary {index + 1}");
            });
        Assert.All(allEmpty.Evidence["provisions"]!.AsArray(), provision =>
            Assert.True(string.IsNullOrWhiteSpace(provision!["text_md"]!.GetValue<string>())));
        using (var reader = LexIndexReader.Open(allEmpty.Db))
        {
            var document = reader.AsOf("w1", new DateOnly(2024, 1, 1), FilterSet.All)!;
            Assert.True(document.TextAvailable);
            Assert.False(document.TextPublic);
            Assert.Empty(reader.Provisions(LexIndexReader.RidOf(document)));
            Assert.Null(reader.BuildBody(document));
            Assert.False(reader.HasProvisionHistory("w1"));
            Assert.Empty(reader.SearchKeyword("All-empty canary", FilterSet.All, 10,
                fuzzyAuto: false).Hits);
            var core = new McpCore(
                new Dictionary<string, LexIndexReader> { [reader.Collection] = reader });
            var response = Assert.IsType<JsonObject>(core.CallTool("as_of", new JsonObject
                {
                    ["work"] = "test:w1", ["date"] = "2024-01-01", ["mode"] = "full",
                }));
            Assert.Equal("text_not_available",
                response["envelope"]!["status"]!.GetValue<string>());
            Assert.DoesNotContain("text_withheld", response.ToJsonString(),
                StringComparison.Ordinal);
            var search = Assert.IsType<JsonArray>(core.CallTool("search",
                new JsonObject { ["query"] = "Work one" }));
            var searchPart = Assert.Single(search.OfType<JsonObject>());
            var searchHit = Assert.Single(searchPart["hits"]!.AsArray())!.AsObject();
            Assert.Equal("w1", searchHit["work"]!.GetValue<string>());
            Assert.Contains("exact_title", searchHit["match_reasons"]!.AsArray()
                .Select(reason => reason!.GetValue<string>()));
            Assert.Contains("no safely derived provision text",
                searchHit["match_note"]!.GetValue<string>(),
                StringComparison.Ordinal);
            Assert.DoesNotContain("not publicly served", searchHit.ToJsonString(),
                StringComparison.Ordinal);
            var missing = Fragments.MissingTextBox(document,
                Fragments.PublisherTextGateOpen(reader));
            Assert.Contains("text_not_available", missing, StringComparison.Ordinal);
            Assert.Contains($"href=\"{EffectiveBodyUri}\"", missing,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Text withheld", missing, StringComparison.Ordinal);
            Assert.DoesNotContain("All-empty canary", missing, StringComparison.Ordinal);
            Assert.Equal("2", reader.Stamp["derived_provisions"]);
            Assert.Equal("0", reader.Stamp["indexed_provisions"]);
            Assert.Equal("2", reader.Stamp["excluded_empty_provisions"]);
        }
        using (var site = new RuntimeIndexSite(Path.GetDirectoryName(allEmpty.Db)!))
        {
            var html = await site.Client.GetStringAsync("/test/w1/2024-01-01");
            Assert.Contains("text_not_available", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"{EffectiveBodyUri}\"", html,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Text withheld", html, StringComparison.Ordinal);
            Assert.DoesNotContain("All-empty canary", html, StringComparison.Ordinal);
        }

        await Build("tampered-blank", """
            <html><body>
            <p class="title-article-norm">Article 1</p><p>Original text.</p>
            </body></html>
            """, provisions =>
            {
                var provision = Assert.IsType<JsonObject>(Assert.Single(provisions));
                provision["text_md"] = whitespace;
            }, expectInvalid: true,
            expectedInvalidMessage: "text_sha256 does not match text_md");

        foreach (var requiredIdentity in new[] { "provision_id", "anchor" })
            await Build($"malformed-blank-{requiredIdentity}", """
                <html><body>
                <p class="title-article-norm">Article 1</p><p>Original text.</p>
                </body></html>
                """, provisions =>
                {
                    var provision = Assert.IsType<JsonObject>(Assert.Single(provisions));
                    MakeWhitespaceOnly(provision, "Malformed blank evidence");
                    provision[requiredIdentity] = null;
                }, expectInvalid: true,
                expectedInvalidMessage: $"provision {requiredIdentity} is required",
                catalogBeforeEdit: requiredIdentity == "anchor");
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
        var notice = File.ReadAllText(Path.Combine(corpusRoot, "NOTICE"));
        Assert.Contains("Attribution: test", notice, StringComparison.Ordinal);
        Assert.Contains(
            "Historical observations make no retroactive transport-byte claim.",
            notice,
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
        Assert.Contains(
            "Historical observations make no retroactive transport-byte claim.",
            manifest.Modifications,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fresh_migration_retains_and_verifies_the_completed_run_ledger()
    {
        var corpusRoot = Path.Combine(_dir, "candidate-with-ledger");
        await WriteLegacyBaselineAsync(corpusRoot);

        await FreshCorpusMigration.RunAsync(
            corpusRoot, "test", new ManyWorksAdapter(1),
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit,
            "fresh-ledger-run-1", default);

        Assert.True(File.Exists(Path.Combine(corpusRoot, "completed-runs.json")));
        var report = CorpusIntegrity.Verify(corpusRoot);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
    }

    [Fact]
    public async Task Interrupted_fresh_publication_recovers_before_replay()
    {
        var corpusRoot = Path.Combine(_dir, "candidate-recovery");
        await WriteLegacyBaselineAsync(corpusRoot);
        var runAt = DateTimeOffset.Parse("2026-08-14T00:00:00Z");
        var migrate = typeof(FreshCorpusMigration).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "RunAsync"
                && method.GetParameters().Length == 11);
        var task = Assert.IsAssignableFrom<Task<CorpusIntegrityReport>>(migrate.Invoke(null,
        [
            corpusRoot, "test", new ManyWorksAdapter(1), runAt, CodeCommit,
            "fresh-recovery-run-1", null, null, null,
            (Action<int, string>)((_, path) =>
            {
                if (path.Contains("/versions/", StringComparison.Ordinal)
                    && path.EndsWith("/meta.json", StringComparison.Ordinal))
                    throw new InvalidOperationException("simulated process interruption");
            }),
            CancellationToken.None,
        ]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.True(Directory.Exists(Path.Combine(
            corpusRoot, ".lex-corpus-transaction")));
        Assert.False(File.Exists(Path.Combine(corpusRoot, "completed-runs.json")));

        var replay = new CorpusWriter(
            corpusRoot, runAt, CodeCommit, runIdentity: "fresh-recovery-run-1");
        await replay.WriteAsync(new ManyWorksAdapter(1), default);

        Assert.True(replay.Accepted);
        Assert.False(replay.Committed);
        Assert.False(Directory.Exists(Path.Combine(
            corpusRoot, ".lex-corpus-transaction")));
        var report = CorpusIntegrity.Verify(corpusRoot);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_migration_handles_the_recorded_256_character_nested_destination(
        bool removeParentBeforeCopy)
    {
        var body = RetrievedBody("<html>publisher text</html>");
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
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit,
            "fresh-migration-401", LegacyWithdrawalAudit(), default);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(2, report.ActualVersions);
        Assert.Equal(1, report.CurrentVersions);
        Assert.Equal(2, report.Expressions);
        Assert.Equal(3, report.Observations); // V3: two historical rows plus one fresh observation.
        Assert.Single(current.FetchedVersionIdentifiers); // V3 requires a fresh current observation.

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
        var corrected = withdrawn.Meta.Events
            .Where(entry => entry.RunIdentity is not null).ToArray();
        Assert.Equal(3, corrected.Length);
        Assert.Equal([1, 2, 3], corrected.Select(entry => entry.RunsMissed));
        Assert.Equal(["audit-run-1", "audit-run-2", "audit-run-3"],
            corrected.Select(entry => entry.RunIdentity));
    }

    [Fact]
    public async Task Fresh_migration_refuses_a_legacy_withdrawal_without_an_audit_contract()
    {
        var corpusRoot = Path.Combine(_dir, "candidate-without-audit");
        await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var before = Inventory(corpusRoot);
        var current = new LegiluxReplacementAdapter(includeWithdrawn: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit,
                "fresh-migration-402", historicalWithdrawalAudit: null, default));

        Assert.Contains("historical withdrawal audit", error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Inventory(corpusRoot));
    }

    [Theory]
    [InlineData("duplicate-run")]
    [InlineData("timestamp-run")]
    [InlineData("out-of-order")]
    [InlineData("before-legacy")]
    [InlineData("wrong-state")]
    public async Task Fresh_migration_rejects_invalid_historical_withdrawal_audit(
        string mutation)
    {
        var corpusRoot = Path.Combine(_dir, "candidate-invalid-audit-" + mutation);
        await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var audit = LegacyWithdrawalAudit();
        switch (mutation)
        {
            case "duplicate-run":
                audit.Entries[0].CompletedRuns[1].RunIdentity = "audit-run-1";
                break;
            case "timestamp-run":
                audit.Entries[0].CompletedRuns[1].RunIdentity = "2026-08-13T10:00:00Z";
                break;
            case "out-of-order":
                audit.Entries[0].CompletedRuns[1].CompletedAt = "2026-08-13T08:30:00Z";
                break;
            case "before-legacy":
                audit.Entries[0].CompletedRuns[0].CompletedAt = "2026-08-13T07:00:00Z";
                break;
            case "wrong-state":
                audit.Entries[0].PublisherVersionIdentifier = "official:other";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var before = Inventory(corpusRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux",
                new LegiluxReplacementAdapter(includeWithdrawn: false),
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit,
                "fresh-migration-403", audit, default));

        Assert.Equal(before, Inventory(corpusRoot));
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
        await ConvertCurrentCorpusToV4Async(corpusRoot);

        var report = await FreshCorpusMigration.RunAsync(
            corpusRoot, "test",
            new NoLegacyIdentityAdapter(
                new SameDateAdapter(reverse: true, shareSource: true)),
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
    public async Task Fresh_migration_prepublication_failure_preserves_the_verified_baseline()
    {
        var corpusRoot = Path.Combine(_dir, "candidate");
        await WriteLegacyBaselineAsync(corpusRoot);
        var before = Inventory(corpusRoot);
        void Inject(string source, string destination) =>
            throw new IOException("injected prepublication failure");

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

        Assert.Equal(before, Inventory(corpusRoot));
        Assert.False(Directory.Exists(Path.Combine(
            corpusRoot, ".lex-corpus-transaction")));
    }

    [Fact]
    public async Task V3_contract_fresh_migration_uses_the_recoverable_snapshot_transaction()
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
        var report = await task;

        Assert.True(report.IsValid,
            string.Join(Environment.NewLine, report.Errors));
        Assert.Empty(Directory.EnumerateDirectories(_dir,
            ".candidate.lex-fresh-backup-*"));
        Assert.False(Directory.Exists(Path.Combine(
            corpusRoot, ".lex-corpus-transaction")));
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
    public async Task Dated_state_without_a_scoped_expression_remains_in_the_corpus()
    {
        var candidate = new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit);

        await candidate.WriteAsync(
            new OneVersionAdapter("in_force", "financial-services", []),
            default, requireComplete: true);

        Assert.True(candidate.Committed);
        Assert.Empty(candidate.BuildIssues);
        var meta = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(Path.Combine(OneVersionDirectory, "meta.json")),
            CorpusJson.Options)!;
        Assert.Empty(meta.Expressions);
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Equal(1, manifest.Works);
        Assert.Equal(1, manifest.Versions);
        Assert.Equal(0, manifest.Expressions);
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
    public async Task V3_completed_absence_requires_three_successive_misses_before_withdrawal()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-101")
            .WriteAsync(new EmptyAdapter(), default);
        var first = await ReadVersionMeta();
        var firstMiss = first.Events[^1];
        Assert.Equal("absent_unconfirmed", firstMiss.Event);
        Assert.Equal("2026-08-03T00:00:00Z", firstMiss.FirstMissedAt);
        Assert.Equal(1, firstMiss.RunsMissed);
        Assert.Equal("nightly-101", firstMiss.RunIdentity);
        var firstIntegrity = CorpusIntegrity.Verify(_dir);
        Assert.True(firstIntegrity.IsValid);
        Assert.Equal(1, firstIntegrity.ManifestWorks);
        Assert.Equal(1, firstIntegrity.ManifestVersions);
        Assert.Equal(1, firstIntegrity.CurrentVersions);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-04T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-102")
            .WriteAsync(new EmptyAdapter(), default);
        var secondMiss = (await ReadVersionMeta()).Events[^1];
        Assert.Equal("absent_unconfirmed", secondMiss.Event);
        Assert.Equal(firstMiss.FirstMissedAt, secondMiss.FirstMissedAt);
        Assert.Equal(2, secondMiss.RunsMissed);
        Assert.Equal("nightly-102", secondMiss.RunIdentity);
        var secondIntegrity = CorpusIntegrity.Verify(_dir);
        Assert.True(secondIntegrity.IsValid);
        Assert.Equal(1, secondIntegrity.ManifestWorks);
        Assert.Equal(1, secondIntegrity.ManifestVersions);
        Assert.Equal(1, secondIntegrity.CurrentVersions);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-05T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-103")
            .WriteAsync(new EmptyAdapter(), default);
        var withdrawal = (await ReadVersionMeta()).Events[^1];
        Assert.Equal("withdrawn_from_source", withdrawal.Event);
        Assert.Equal(firstMiss.FirstMissedAt, withdrawal.FirstMissedAt);
        Assert.Equal(3, withdrawal.RunsMissed);
        Assert.Equal("nightly-103", withdrawal.RunIdentity);
        var thirdIntegrity = CorpusIntegrity.Verify(_dir);
        Assert.True(thirdIntegrity.IsValid);
        Assert.Equal(0, thirdIntegrity.ManifestWorks);
        Assert.Equal(0, thirdIntegrity.ManifestVersions);
        Assert.Equal(0, thirdIntegrity.CurrentVersions);
    }

    [Fact]
    public async Task Retrying_the_same_completed_run_identity_never_advances_absence()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-201")
            .WriteAsync(new EmptyAdapter(), default);
        var afterFirst = await ReadVersionMeta();
        var firstEventCount = afterFirst.Events.Count;

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-201")
            .WriteAsync(new EmptyAdapter(), default);
        var afterRetry = await ReadVersionMeta();

        Assert.Equal(firstEventCount, afterRetry.Events.Count);
        var miss = afterRetry.Events[^1];
        Assert.Equal(1, miss.RunsMissed);
        Assert.Equal("nightly-201", miss.RunIdentity);
        Assert.Equal("2026-08-02T00:00:00Z", miss.ObservedFrom);
    }

    [Fact]
    public async Task V3_contract_missing_publisher_record_requires_three_completed_runs_and_can_be_resighted()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-miss-1")
            .WriteAsync(new EmptyAdapter(), default);
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-04T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-miss-2")
            .WriteAsync(new EmptyAdapter(), default);
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-05T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-miss-3")
            .WriteAsync(new EmptyAdapter(), default);

        var path = Path.Combine(OneVersionDirectory, "meta.json");
        var withdrawn = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        Assert.Equal("withdrawn_from_source", withdrawn.Events[^1].Event);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-06T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-resight-1")
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        var resighted = JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        Assert.Equal("resighted", resighted.Events[^1].Event);
    }

    [Fact]
    public async Task Completed_run_ledger_is_cross_record_and_exact_replay_is_idempotent()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false), default);

        var first = new CorpusWriter(_dir,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-cross-record-1");
        await first.WriteAsync(new EmptyAdapter(), default);
        var afterFirst = Inventory(_dir);

        var replay = new CorpusWriter(_dir,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-cross-record-1");
        await replay.WriteAsync(new EmptyAdapter(), default);

        Assert.True(replay.Accepted);
        Assert.False(replay.Committed);
        Assert.Equal(afterFirst, Inventory(_dir));
        var ledger = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(_dir, "completed-runs.json")))!.AsObject();
        var run = Assert.Single(ledger["runs"]!.AsArray())!.AsObject();
        Assert.Equal("nightly-cross-record-1", run["run_identity"]!.GetValue<string>());
        Assert.Equal("2026-08-02T00:00:00Z", run["completed_at"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{64}$", run["enumeration_scope_sha256"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{64}$", run["entry_sha256"]!.GetValue<string>());
        Assert.All(Directory.EnumerateFiles(Path.Combine(_dir, "works"), "meta.json",
                SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}")),
            path => Assert.Single(JsonSerializer.Deserialize<VersionMeta>(
                    File.ReadAllText(path), CorpusJson.Options)!.Events,
                entry => entry.RunIdentity == "nightly-cross-record-1"));
    }

    [Fact]
    public async Task Interrupted_completion_rolls_forward_before_exact_replay()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services"), default);

        var interrupted = new CorpusWriter(_dir,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-recovery-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteWithPublishHook(interrupted, new EmptyAdapter(), (_, path) =>
            {
                if (path.Contains("/versions/", StringComparison.Ordinal)
                    && path.EndsWith("/meta.json", StringComparison.Ordinal))
                    throw new InvalidOperationException("simulated process interruption");
            }));

        Assert.True(Directory.Exists(Path.Combine(
            _dir, ".lex-corpus-transaction")));
        Assert.False(File.Exists(Path.Combine(_dir, "completed-runs.json")));
        Assert.Equal("nightly-recovery-1", (await ReadVersionMeta()).Events[^1].RunIdentity);

        var replay = new CorpusWriter(_dir,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-recovery-1");
        await replay.WriteAsync(new EmptyAdapter(), default);

        Assert.True(replay.Accepted);
        Assert.False(replay.Committed);
        Assert.False(Directory.Exists(Path.Combine(
            _dir, ".lex-corpus-transaction")));
        Assert.True(File.Exists(Path.Combine(_dir, "completed-runs.json")));
        var report = CorpusIntegrity.Verify(_dir);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
    }

    [Fact]
    public async Task Interrupted_after_ledger_publication_recovers_before_exact_replay()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services"), default);

        var completedAt = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        var interrupted = new CorpusWriter(
            _dir, completedAt, CodeCommit, runIdentity: "nightly-visible-recovery-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteWithPublishHook(interrupted, new EmptyAdapter(), (_, path) =>
            {
                if (path == "completed-runs.json")
                    throw new InvalidOperationException("simulated process interruption");
            }));

        Assert.True(Directory.Exists(Path.Combine(
            _dir, ".lex-corpus-transaction")));
        Assert.True(File.Exists(Path.Combine(_dir, "completed-runs.json")));

        var replay = new CorpusWriter(
            _dir, completedAt, CodeCommit, runIdentity: "nightly-visible-recovery-1");
        await replay.WriteAsync(new EmptyAdapter(), default);

        Assert.True(replay.Accepted);
        Assert.False(replay.Committed);
        Assert.False(Directory.Exists(Path.Combine(
            _dir, ".lex-corpus-transaction")));
        var report = CorpusIntegrity.Verify(_dir);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
    }

    [Fact]
    public async Task Reusing_completed_run_identity_with_different_time_or_scope_fails_before_advancement()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-bound-once-1")
            .WriteAsync(new EmptyAdapter(), default);
        var before = Inventory(_dir);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T01:00:00Z"), CodeCommit,
                    runIdentity: "nightly-bound-once-1")
                .WriteAsync(new EmptyAdapter(), default));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-bound-once-1")
                .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default));

        Assert.Equal(before, Inventory(_dir));
        Assert.Equal(1, (await ReadVersionMeta()).Events[^1].RunsMissed);
    }

    [Fact]
    public async Task Completed_run_scope_binds_the_complete_version_and_expression_record()
    {
        var completedAt = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        await new CorpusWriter(_dir, completedAt, CodeCommit,
                runIdentity: "nightly-full-record-1")
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services", ["en"]), default);
        var before = Inventory(_dir);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, completedAt, CodeCommit,
                    runIdentity: "nightly-full-record-1")
                .WriteAsync(new OneVersionAdapter(
                    "in_force", "financial-services", ["en", "fr"]), default));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, completedAt, CodeCommit,
                    runIdentity: "nightly-full-record-1")
                .WriteAsync(new OneVersionAdapter(
                    "in_force", "financial-services", ["en"],
                    validFrom: new DateOnly(2024, 1, 2)), default));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, completedAt, CodeCommit,
                    runIdentity: "nightly-full-record-1")
                .WriteAsync(new OneVersionAdapter(
                    "in_force", "financial-services", ["en"],
                    expressionSourceUri: "https://example.test/v1/en?revision=2"),
                    default));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, completedAt, CodeCommit,
                    runIdentity: "nightly-full-record-1")
                .WriteAsync(new OneVersionAdapter(
                    "in_force", "changed-source-record", ["en"]), default));

        Assert.Equal(before, Inventory(_dir));
    }

    [Fact]
    public async Task Integrity_binds_each_lifecycle_time_to_its_completed_run()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services"), default);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-time-binding-1")
            .WriteAsync(new EmptyAdapter(), default);

        var meta = await ReadVersionMeta();
        meta.Events[^1].ObservedFrom = "2026-08-02T00:00:01Z";
        meta.Events[^1].FirstMissedAt = "2026-08-02T00:00:01Z";
        RefreshRecordHash(meta);
        await File.WriteAllTextAsync(
            Path.Combine(OneVersionDirectory, "meta.json"),
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains(
            "completed-run ledger time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Integrity_binds_resighting_time_to_its_completed_run()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services"), default);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-resighting-time-1")
            .WriteAsync(new EmptyAdapter(), default);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-resighting-time-2")
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services"), default);

        var meta = await ReadVersionMeta();
        Assert.Equal("resighted", meta.Events[^1].Event);
        meta.Events[^1].ObservedFrom = "2026-08-03T00:00:01Z";
        RefreshRecordHash(meta);
        await File.WriteAllTextAsync(
            Path.Combine(OneVersionDirectory, "meta.json"),
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var report = CorpusIntegrity.Verify(_dir);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains(
            "completed-run ledger time", StringComparison.Ordinal));

        meta.Events[^1].ObservedFrom = "2026-08-03T00:00:00Z";
        meta.Events[^1].RunIdentity = null;
        RefreshRecordHash(meta);
        await File.WriteAllTextAsync(
            Path.Combine(OneVersionDirectory, "meta.json"),
            JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");

        var unbound = CorpusIntegrity.Verify(_dir);
        Assert.False(unbound.IsValid);
        Assert.Contains(unbound.Errors, error => error.Contains(
            "absence lifecycle is invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifest_identity_prevents_a_silent_completed_run_history_reset()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-history-root-1")
            .WriteAsync(new OneVersionAdapter(
                "in_force", "financial-services"), default);
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")),
            CorpusJson.Options)!;
        Assert.Matches("^[0-9a-f]{64}$", manifest.CompletedRunsSha256);

        File.Delete(Path.Combine(_dir, "completed-runs.json"));

        var report = CorpusIntegrity.Verify(_dir);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains(
            "missing or empty completed-run ledger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Distinct_completed_run_identities_must_be_observed_in_time_order()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-ordered-1")
            .WriteAsync(new EmptyAdapter(), default);
        var before = Inventory(_dir);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir,
                    DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-ordered-2")
                .WriteAsync(new EmptyAdapter(), default));

        Assert.Equal(before, Inventory(_dir));
    }

    [Fact]
    public async Task A_completed_run_identity_cannot_be_reused_after_resighting()
    {
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-reused-1")
            .WriteAsync(new EmptyAdapter(), default);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-resight-2")
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        var before = Inventory(_dir);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir,
                    DateTimeOffset.Parse("2026-08-04T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-reused-1")
                .WriteAsync(new EmptyAdapter(), default));

        Assert.Equal(before, Inventory(_dir));
    }

    [Fact]
    public async Task First_miss_lifecycle_reaches_index_reader_and_Mcp_provenance()
    {
        var corpus = Path.Combine(_dir, "first-miss-corpus");
        var db = Path.Combine(_dir, "first-miss.db");
        await new CorpusWriter(corpus,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        await new CorpusWriter(corpus,
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-first-miss-501")
            .WriteAsync(new EmptyAdapter(), default);
        var corpusCommit = CommitGitDirectory(corpus);

        IndexFromCorpus.Build(corpus, null, db, null,
            DateTimeOffset.Parse("2026-08-02T00:05:00Z"),
            codeCommit: new string('c', 40), corpusCommit: corpusCommit);

        using var reader = LexIndexReader.Open(db);
        var key = Assert.Single(reader.Timeline("w1")).Key;
        var indexed = Assert.Single(reader.Events(key),
            entry => entry.Event == "absent_unconfirmed");
        Assert.Equal("2026-08-02T00:00:00Z", indexed.FirstMissedAt);
        Assert.Equal(1, indexed.RunsMissed);
        Assert.Equal("nightly-first-miss-501", indexed.RunIdentity);

        var response = Assert.IsType<JsonObject>(new McpCore(
            new Dictionary<string, LexIndexReader> { [reader.Collection] = reader })
            .CallTool("provenance", new JsonObject { ["lex_id"] = key }));
        var events = Assert.IsType<JsonArray>(response["events"]);
        var served = Assert.Single(events.OfType<JsonObject>(), value =>
            value["event"]!.GetValue<string>() == "absent_unconfirmed");
        Assert.Equal("2026-08-02T00:00:00Z",
            served["first_missed_at"]!.GetValue<string>());
        Assert.Equal(1, served["runs_missed"]!.GetValue<int>());
        Assert.Equal("nightly-first-miss-501",
            served["run_identity"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_missing_record_requires_a_bounded_non_timestamp_run_identity()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit)
                .WriteAsync(new EmptyAdapter(), default));
        Assert.Throws<InvalidDataException>(() => new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
            runIdentity: "2026-08-02T00:00:00Z"));
        Assert.Throws<InvalidDataException>(() => new CorpusWriter(
            _dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
            runIdentity: new string('a', 129)));
    }

    [Fact]
    public async Task V3_resighting_preserves_pending_absence_history_and_resets_the_sequence()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-301")
            .WriteAsync(new EmptyAdapter(), default);
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-03T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-302")
            .WriteAsync(new EmptyAdapter(), default);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir,
                    DateTimeOffset.Parse("2026-08-04T00:00:00Z"), CodeCommit)
                .WriteAsync(new OneVersionAdapter(
                    "in_force", "financial-services"), default));
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-04T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-resighted-1")
            .WriteAsync(new OneVersionAdapter("in_force", "financial-services"), default);
        var resighted = await ReadVersionMeta();
        Assert.Equal(["absent_unconfirmed", "absent_unconfirmed"],
            resighted.Events.Where(entry => entry.Event == "absent_unconfirmed")
                .Select(entry => entry.Event));
        Assert.Equal("resighted", resighted.Events[^1].Event);
        Assert.Equal("nightly-resighted-1", resighted.Events[^1].RunIdentity);

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-05T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-303")
            .WriteAsync(new EmptyAdapter(), default);
        var restarted = (await ReadVersionMeta()).Events[^1];
        Assert.Equal("absent_unconfirmed", restarted.Event);
        Assert.Equal("2026-08-05T00:00:00Z", restarted.FirstMissedAt);
        Assert.Equal(1, restarted.RunsMissed);
        Assert.Equal("nightly-303", restarted.RunIdentity);
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
        Assert.DoesNotContain(meta.Events, item =>
            item.Event is "absent_unconfirmed" or "withdrawn_from_source");
    }

    [Fact]
    public async Task Changed_source_configuration_requires_fresh_migration_before_absence()
    {
        var originalScope = new string('a', 64);
        var narrowedScope = new string('b', 64);
        await new CorpusWriter(_dir,
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new ScopedInventoryAdapter(
                new ManyWorksAdapter(1), originalScope), default);
        var before = Snapshot();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(_dir,
                    DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-narrowed-scope-1")
                .WriteAsync(new ScopedInventoryAdapter(
                    new EmptyAdapter(), narrowedScope), default));

        Assert.Contains("fresh-corpus migration", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Failed_body_acquisition_rolls_back_every_candidate_file()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: RetrievedBody("<html>publisher text</html>")), default);
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
                bodyFetch: RetrievedBody("<html>publisher text</html>")), default);
        var before = Snapshot();

        var candidate = new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit);
        await candidate.WriteAsync(new OneVersionAdapter("in_force", "finance", ["en", "fr"],
            titleHint: "Candidate title",
            bodyFetch: new SourceBodyFetch(SourceBodyStatus.PermanentNotFound,
                Detail: "publisher returned 404", Attempts: 4)), default,
            requireComplete: true);

        Assert.False(candidate.Committed);
        Assert.Equal(2, candidate.BuildIssues.Count);
        Assert.All(candidate.BuildIssues,
            issue => Assert.Equal("body_not_found", issue.Code));
        Assert.Equal(before, Snapshot());
    }

    // The distinction that stopped the nightly. A publisher offering no XML for an expression is
    // not a failed acquisition, it is the publisher saying there is nothing to acquire in that
    // format, and the corpus already records that as Text.Reason with Available=false. Legilux
    // announces future-dated consolidations before their XML exists, so counting these as
    // acquisition failures discarded the whole candidate every night, for both publishers, with a
    // count that could only grow.
    [Fact]
    public async Task V3_contract_metadata_only_transition_publishes_only_unavailable_expressions()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: RetrievedBody("<html>publisher text</html>")), default);

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
        Assert.Equal(2, expressions.Count);
        Assert.All(expressions, expression =>
        {
            Assert.Equal("publisher_metadata_only", expression.Text.Reason);
            Assert.False(expression.Text.Available);
        });
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
                bodyFetch: RetrievedBody("<html>publisher text</html>")), default);
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
                bodyFetch: RetrievedBody("<html>publisher text</html>")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(1, manifest.Expressions);
        Assert.Equal(1, manifest.ExpressionsWithText);
        Assert.Equal(0, manifest.ExpressionsWithoutText);
        var observation = Assert.Single((await ReadVersionMeta()).Expressions)
            .Observations.Single(item => item.Format is null);
        Assert.True(File.Exists(Path.Combine(OneVersionDirectory, observation.File!)));
    }

    [Fact]
    public async Task Empty_retrieved_body_is_a_typed_issue_not_stored_text()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-08T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter("in_force", "finance",
                bodyFetch: RetrievedBody("   ")), default);

        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(Path.Combine(_dir, "manifest.json")), CorpusJson.Options)!;
        Assert.Equal(0, manifest.ExpressionsWithText);
        Assert.Equal(1, manifest.ExpressionsWithoutText);
        var issue = Assert.Single(manifest.BuildIssues);
        Assert.Equal("body_empty", issue.Code);

        var expr = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.False(expr.Text.Available);
        Assert.Equal("body_empty", expr.Text.Reason);
        var observation = Assert.Single(expr.Observations);
        Assert.Equal("body_empty", observation.Http?.AttemptOutcome);
        Assert.True(File.Exists(Path.Combine(OneVersionDirectory, observation.File!)));
    }

    [Fact]
    public async Task Alt_manifestation_does_not_block_primary_body_backfill()
    {
        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), CodeCommit)
            .WriteAsync(new AltThenPrimaryAdapter(new SourceBodyFetch(
                SourceBodyStatus.RetryExhausted, Detail: "network", Attempts: 2)), default);

        var versionDir = OneVersionDirectory;
        Assert.Empty(Directory.EnumerateFiles(
            versionDir, "en--*.html", SearchOption.TopDirectoryOnly));
        var first = Assert.Single((await ReadVersionMeta()).Expressions);
        Assert.True(first.Text.Available);                       // an alt manifestation IS observed text
        Assert.All(first.Observations, o => Assert.NotNull(o.Format));

        await new CorpusWriter(_dir, DateTimeOffset.Parse("2026-08-02T00:00:00Z"), CodeCommit)
            .WriteAsync(new AltThenPrimaryAdapter(
                RetrievedBody("<html>primary text</html>")), default);

        var second = Assert.Single((await ReadVersionMeta()).Expressions);
        var primary = Assert.Single(second.Observations, o => o.Format is null);
        Assert.True(File.Exists(Path.Combine(versionDir, primary.File!)));
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
        await ConvertTransportObservationsToLegacyAsync(root);
        var path = Path.Combine(root, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(path), CorpusJson.Options)!;
        manifest.Schema = "lex-corpus/3";
        manifest.Canon = null;
        manifest.ObservationRun = null;
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
        await ConvertTransportObservationsToLegacyAsync(root);

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
        manifest.Canon = null;
        manifest.ObservationRun = null;
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

    private static HistoricalWithdrawalAuditDocument LegacyWithdrawalAudit() => new()
    {
        Schema = HistoricalWithdrawalAuditDocument.CurrentSchema,
        Publisher = "lu-legilux",
        Entries =
        [
            new HistoricalWithdrawalAuditEntry
            {
                WorkIdentifier = LegiluxReplacementAdapter.WorkIdentifier,
                PublisherVersionIdentifier =
                    LegiluxReplacementAdapter.WithdrawnVersionIdentifier,
                CompletedRuns =
                [
                    new HistoricalWithdrawalAuditRun
                    {
                        RunIdentity = "audit-run-1",
                        CompletedAt = "2026-08-13T09:00:00Z",
                    },
                    new HistoricalWithdrawalAuditRun
                    {
                        RunIdentity = "audit-run-2",
                        CompletedAt = "2026-08-13T10:00:00Z",
                    },
                    new HistoricalWithdrawalAuditRun
                    {
                        RunIdentity = "audit-run-3",
                        CompletedAt = "2026-08-13T11:00:00Z",
                    },
                ],
            },
        ],
    };

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

    private static async Task ConvertCurrentCorpusToV4Async(string root)
    {
        await ConvertTransportObservationsToLegacyAsync(root);

        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ManifestDoc>(
            await File.ReadAllTextAsync(manifestPath), CorpusJson.Options)!;
        manifest.Schema = "lex-corpus/4";
        manifest.Canon = null;
        manifest.ObservationRun = null;
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, CorpusJson.Options) + "\n");
    }

    private static async Task ConvertTransportObservationsToLegacyAsync(string root)
    {
        var worksRoot = Path.Combine(root, "works");
        foreach (var workDirectory in Directory.EnumerateDirectories(worksRoot))
        foreach (var versionDirectory in Directory.EnumerateDirectories(
                     Path.Combine(workDirectory, "versions")))
        {
            var metaPath = Path.Combine(versionDirectory, "meta.json");
            var meta = JsonSerializer.Deserialize<VersionMeta>(
                await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
            foreach (var expression in meta.Expressions)
            foreach (var observation in expression.Observations
                         .Where(item => item.Format is null))
            {
                var extension = Path.GetExtension(observation.File);
                var legacyName = expression.Language + extension;
                var legacyPath = Path.Combine(versionDirectory, legacyName);
                Assert.False(File.Exists(legacyPath));
                File.Move(Path.Combine(versionDirectory, observation.File!), legacyPath);
                observation.File = legacyName;
                observation.Http = null;
            }
            RefreshRecordHash(meta);
            await File.WriteAllTextAsync(metaPath,
                JsonSerializer.Serialize(meta, CorpusJson.Options) + "\n");
        }
    }

    private sealed record LegacyWithdrawalBaseline(
        string MetaPath, string BodyPath, string BodySha256, byte[] BodyBytes);

    private sealed class NoLegacyIdentityAdapter(ISourceAdapter inner) : ISourceAdapter
    {
        public PublisherDescriptor Describe() => inner.Describe();

        public IAsyncEnumerable<WorkRef> EnumerateWorks(CancellationToken ct) =>
            inner.EnumerateWorks(ct);

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work,
            CancellationToken ct) => inner.FetchVersions(work, ct);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version,
            ExpressionRecord expression,
            CancellationToken ct) => inner.FetchBody(version, expression, ct);

        public Task<SourceManifestationFetch> FetchAltManifestation(
            VersionRecord version,
            ExpressionRecord expression,
            CancellationToken ct) => inner.FetchAltManifestation(version, expression, ct);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string CommitGitDirectory(string directory)
    {
        string Git(params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(10_000) && process.ExitCode == 0, error);
            return output.Trim().ToLowerInvariant();
        }

        Git("init");
        Git("config", "user.name", "Lex Test");
        Git("config", "user.email", "lex@example.invalid");
        Git("add", ".");
        Git("commit", "-m", "fixture");
        return Git("rev-parse", "HEAD");
    }

    private sealed class RuntimeIndexSite : WebApplicationFactory<Program>
    {
        private readonly string _indexDirectory;
        private readonly string _webRoot;
        public HttpClient Client { get; }

        public RuntimeIndexSite(string indexDirectory)
        {
            _indexDirectory = indexDirectory;
            _webRoot = Path.Combine(indexDirectory, "wwwroot");
            Directory.CreateDirectory(_webRoot);
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _indexDirectory);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://runtime.test");
            builder.UseWebRoot(_webRoot);
        }
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
        SourceManifestationFetch? altFetch = null,
        DateOnly? validFrom = null,
        string? expressionSourceUri = null) : ISourceAdapter
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
            var date = validFrom ?? new DateOnly(2024, 1, 1);
            IReadOnlyList<VersionRecord> versions =
            [
                new(
                    new Identifier("official:v1"), _work.Id, "REG", date, null,
                    "publisher", "true", date,
                    (languages ?? ["en"]).Select(language => new ExpressionRecord(
                        language, date, null, "publisher",
                        "Work one", titleShort, expressionSourceUri
                            ?? $"https://example.test/v1/{language}")).ToArray(),
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
        bool reverse,
        bool includeSecond = true,
        bool shareSource = false,
        bool includeFirst = true,
        string? versionWorkIdentifier = null,
        Action? beforeFirstBodyFetch = null,
        string? workIdentifier = null,
        bool omitSource = false) :
        ISourceAdapter, ILegacyVersionIdentityResolver
    {
        private readonly WorkRef _work = new(
            new Identifier(workIdentifier ?? "official:w1"), "w1", "LOI", "Work one");
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
                new Identifier(id),
                new Identifier(versionWorkIdentifier ?? _work.Id.Value),
                "LOI", new DateOnly(2025, 7, 28), null,
                "publisher", null, null,
                [new ExpressionRecord("fr", new DateOnly(2025, 7, 28), null, "publisher",
                    $"Version {id}", null, omitSource
                        ? null
                        : shareSource
                            ? "https://example.test/shared"
                            : $"https://example.test/{id}")],
                [], new Dictionary<string, string>());
            var versions = new List<VersionRecord>();
            if (includeFirst) versions.Add(Version("official:v-a"));
            if (includeSecond) versions.Add(Version("official:v-b"));
            if (versions.Count == 0)
                throw new InvalidOperationException("The synthetic adapter needs one version.");
            return Task.FromResult<IReadOnlyList<VersionRecord>>(
                reverse ? versions.AsEnumerable().Reverse().ToArray() : versions);
        }

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        {
            if (BodyFetchCount == 0) beforeFirstBodyFetch?.Invoke();
            BodyFetchCount++;
            return Task.FromResult(RetrievedBody(
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
            return Task.FromResult(RetrievedBody(
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
            return Task.FromResult(RetrievedBody(
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

    private static async Task WriteWithCommitHook(
        CorpusWriter writer, ISourceAdapter adapter, Action beforeCandidateCommit)
    {
        var write = typeof(CorpusWriter).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)
            .Single(method => method.Name == "WriteAsync"
                && method.GetParameters().Length == 5);
        var task = Assert.IsAssignableFrom<Task>(write.Invoke(writer,
        [
            adapter, CancellationToken.None, false, null, beforeCandidateCommit,
        ]));
        await task;
    }

    private static async Task WriteWithPublishHook(
        CorpusWriter writer,
        ISourceAdapter adapter,
        Action<int, string> afterCandidatePublish)
    {
        var write = typeof(CorpusWriter).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)
            .Single(method => method.Name == "WriteAsync"
                && method.GetParameters().Length == 7);
        var task = Assert.IsAssignableFrom<Task>(write.Invoke(writer,
        [
            adapter, CancellationToken.None, false, null, null, false,
            afterCandidatePublish,
        ]));
        await task;
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

    private sealed class ScopedInventoryAdapter(
        ISourceAdapter inner,
        string scopeSha256) : ISourceAdapter, ISourceBuildInventory
    {
        public PublisherDescriptor Describe() => inner.Describe();

        public IAsyncEnumerable<WorkRef> EnumerateWorks(CancellationToken ct) =>
            inner.EnumerateWorks(ct);

        public Task<IReadOnlyList<VersionRecord>> FetchVersions(
            WorkRef work, CancellationToken ct) => inner.FetchVersions(work, ct);

        public Task<SourceBodyFetch> FetchBody(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            inner.FetchBody(version, expression, ct);

        public Task<SourceManifestationFetch> FetchAltManifestation(
            VersionRecord version, ExpressionRecord expression, CancellationToken ct) =>
            inner.FetchAltManifestation(version, expression, ct);

        public SourceBuildInventory GetBuildInventory() => new(
            ExpectedWorks: inner is EmptyAdapter ? 0 : 1,
            Issues: [],
            SourceConfigurationKind: "engineering_scope",
            SourceConfigurationSha256: scopeSha256);
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
            return Task.FromResult(RetrievedBody("""
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
