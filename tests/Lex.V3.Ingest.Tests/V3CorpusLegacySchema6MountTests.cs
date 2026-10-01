using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.LuxembourgIndexBuilderTests;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Schema 6 served with its build record absent (the panel's answer to Q-20261001-0656-codex). The data lane's running
/// EU population and the real bounded first mount wrote Luxembourg indexes of schema 6, the last before the event log
/// recorded builds; the reader serves one exactly as schema 6 checked it, with no observation, build time, source body
/// or log stamp invented, the operations that need them refusing with their typed codes, and no schema-6 index carried
/// forward as a predecessor. The real mount's three Luxembourg files are fixtures, byte for byte the files its build
/// report names; the index with states is the state fixture's built index rewritten into schema 6's tables, because no
/// schema-6 builder remains.
/// </summary>
[TestClass]
public sealed class V3CorpusLegacySchema6MountTests
{
    // The real bounded first mount's build report (C:\lex-v3\first-mount-decision95-restart-20260930\v3-corpus), its
    // files' own SHA-256: the fixtures must be those bytes.
    private const string RealIndexFileSha256 = "c7f405486cb3603babf23fdd3e94f03ba9ecb46f775d0302bf1935350d7b3e1a";
    private const string RealManifestFileSha256 = "ef03cacd22dc0ed4010e3532f5ad508efa40105027ccfa5129ff4fed039cd330";
    private const string RealCorpusFileSha256 = "312d4804568fa3175095d445a98b80926c7a39af93a0c0c66ee522a840b926b4";

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySchema6Mount", name + ".bin"));

    private static string MountDirectory(byte[] corpus, byte[] index, byte[] manifest)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-legacy6-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus);
        File.WriteAllBytes(Path.Combine(directory, V3CorpusMount.IndexFileName), index);
        File.WriteAllBytes(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), manifest);
        return directory;
    }

    private static SourceArtifactRef IndexRefOf(byte[] index)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(index));
        return new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
    }

    [TestMethod]
    public async Task TheRealBoundedFirstMountIsServedWithItsBuildRecordAbsent()
    {
        var index = Fixture("luxembourg-index.sqlite3");
        var manifestBytes = Fixture("luxembourg-capability-manifest.json");
        var corpusBytes = Fixture("lex-corpus-6.json");
        Assert.AreEqual(RealIndexFileSha256, Convert.ToHexStringLower(SHA256.HashData(index)));
        Assert.AreEqual(RealManifestFileSha256, Convert.ToHexStringLower(SHA256.HashData(manifestBytes)));
        Assert.AreEqual(RealCorpusFileSha256, Convert.ToHexStringLower(SHA256.HashData(corpusBytes)));

        // The reader, directly: schema 6, no build recorded, nothing of the log to hold to the corpus beyond its binding.
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        var indexRef = IndexRefOf(index);
        var manifestDigest = V3IndexCapabilityManifestArtifact.ComputeSha256(manifestBytes);
        var manifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(manifestDigest), manifestDigest), manifestBytes, PublisherId.LuLegilux, indexRef.Sha256);
        using (var reader = LuxembourgIndexReader.OpenAndVerify(indexRef, index, corpus.ArtifactRef, manifest))
        {
            Assert.AreEqual(LuxembourgIndexBuilder.LegacySchema6, reader.SchemaIdentity);
            Assert.IsFalse(reader.RecordsBuilds);
            Assert.AreEqual(0, reader.ResolveObservations().Count, "no observation is invented");
            reader.VerifyEventLogSources(corpus);
        }

        // It is never carried forward: schema 6 holds no log stamp.
        Assert.IsNull(LuxembourgIndexPredecessor.TryRead(indexRef, index, out var refusal, out _));
        Assert.AreEqual(LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers, refusal);

        // The mount: coverage says the index records no build time, and events serves its (empty) genesis log with the
        // build record named absent.
        var directory = MountDirectory(corpusBytes, index, manifestBytes);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var coverage = (await EnvelopeAsync(mount, "/api/v3/coverage", "coverage", new { })).Result!.Value;
            var history = coverage.GetProperty("history");
            Assert.IsFalse(history.GetProperty("log_records_builds").GetBoolean(), "a schema-6 log records no build");
            Assert.AreEqual(0, history.GetProperty("snapshots_in_log").GetInt32());
            Assert.AreEqual(V3CorpusMount.HistoryNotRecordedNote, history.GetProperty("note").GetString());
            Assert.AreEqual(indexRef.Sha256, coverage.GetProperty("mounted").GetProperty("index_sha256").GetString());
            var buildTime = coverage.GetProperty("not_held").EnumerateArray().Single(row => row.GetProperty("item").GetString() == "build_time_and_currency");
            Assert.AreEqual(V3CorpusMount.CoverageLegacyBuildTimeRow[1], buildTime.GetProperty("reason").GetString());

            var events = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { })).Result!.Value;
            var log = events.GetProperty("log");
            Assert.AreEqual(indexRef.Sha256, log.GetProperty("log_id").GetString());
            Assert.AreEqual("genesis", log.GetProperty("basis").GetString());
            Assert.AreEqual(JsonValueKind.Null, log.GetProperty("built_at").ValueKind, "no build time is invented");
            Assert.AreEqual(0, log.GetProperty("ancestors").GetArrayLength());
            Assert.AreEqual(0, events.GetProperty("events").GetArrayLength(), "the real mount holds no Luxembourg state");
            Assert.AreEqual(V3CorpusMount.EventsLegacyNote, events.GetProperty("legacy_note").GetString());
            CollectionAssert.Contains(events.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray(), "build_record");

            // No sentence of the answer claims a build time this index does not record (review of #878): the genesis and
            // silence notes and the upstream-health row are schema 6's own, and nothing says when a build ran.
            Assert.AreEqual(V3CorpusMount.EventsGenesisNoteLegacy, events.GetProperty("genesis_note").GetString());
            Assert.AreEqual(V3CorpusMount.EventsSilenceNoteLegacy, events.GetProperty("silence_note").GetString());
            var text = events.GetRawText();
            foreach (var claim in new[] { "when the build ran", "says when it ran", "built_at is when", "a build's time (log.built_at)" })
            {
                Assert.IsFalse(text.Contains(claim, StringComparison.Ordinal), $"a schema-6 events answer claims a build time: {claim}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The state fixture's built index in schema 6's tables, as schema 6 wrote them: the event log's three tables replaced by
    /// schema 6's one, holding its genesis log (state digests only), user_version 6 and the stamp re-sealed. Every other
    /// table is schema 8's unchanged, which is what schema 6 held.
    /// </summary>
    private static (byte[] Index, SourceArtifactRef Reference) Schema6Of(ReadOnlySpan<byte> schema8, Action<SqliteConnection>? tamper = null)
    {
        var bytes = MutateDatabase(schema8, connection =>
        {
            var states = ReadStates(connection);
            Execute(connection, "DROP TABLE observations");
            Execute(connection, "DROP TABLE log_stamp");
            Execute(connection, "DROP TABLE events");
            Execute(connection, LuxembourgIndexBuilder.LegacyEventLogDdl6);
            foreach (var value in LuxembourgIndexBuilder.LegacyGenesisEvents6(states))
            {
                Execute(connection, "INSERT INTO events VALUES($seq,$scope,$key,$event,NULL,$detail)",
                    ("$seq", value.Seq), ("$scope", value.Scope), ("$key", value.Key), ("$event", value.Event), ("$detail", value.DetailJson));
            }

            Execute(connection, "PRAGMA user_version=6");
            Execute(connection, "UPDATE stamp SET schema_identity=$schema WHERE stamp_id=1", ("$schema", LuxembourgIndexBuilder.LegacySchema6));
            tamper?.Invoke(connection);
            Restamp(connection);
        });
        return (bytes, IndexRefOf(bytes));
    }

    private static void Restamp(SqliteConnection connection) =>
        Execute(connection, "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1", ("$digest", LuxembourgIndexBuilder.HashLogicalRows(
            ReadMembers(connection), ReadArticles(connection), ReadStates(connection), ReadWorkTitles(connection), ReadRelations(connection),
            ReadWorkFacts(connection), ReadEvents(connection))));

    [TestMethod]
    public async Task ASchema6IndexWithStatesServesItsGenesisLogAndAsObservedRefusesSnapshotUnknown()
    {
        var (envelope, built, corpus) = await BuildStateEnvelopeAsync();
        var (index, reference) = Schema6Of(built.IndexBytes.Span);
        var manifest = RebindManifest(built.CapabilityManifest, reference.Sha256);
        using (var reader = LuxembourgIndexReader.OpenAndVerify(reference, index, corpus.ArtifactRef, manifest))
        {
            Assert.IsFalse(reader.RecordsBuilds);
            reader.VerifyEventLogSources(corpus);
            Assert.ThrowsExactly<InvalidOperationException>(() => reader.ResolveObservedStates("loi", 1), "no observation to fold to");
        }

        using var manifestStream = new MemoryStream();
        V3IndexCapabilityManifestArtifact.Write(manifestStream, manifest);
        var corpusBuild = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        var directory = MountDirectory(corpusBuild.CanonicalBytes.ToArray(), index, manifestStream.ToArray());
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var events = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { })).Result!.Value;
            var rows = events.GetProperty("events").EnumerateArray().ToArray();
            Assert.IsNotEmpty(rows);
            foreach (var row in rows)
            {
                Assert.AreEqual("first_sighting", row.GetProperty("event").GetString());
                CollectionAssert.AreEqual(new[] { "state_sha256" }, row.GetProperty("detail").EnumerateObject().Select(static p => p.Name).ToArray(),
                    "schema 6 named the state digest only; no source body is invented");
            }

            Assert.AreEqual(JsonValueKind.Null, events.GetProperty("log").GetProperty("built_at").ValueKind);
            Assert.AreEqual(V3CorpusMount.EventsLegacyNote, events.GetProperty("legacy_note").GetString());

            var workKey = rows[0].GetProperty("work_key").GetString()!;
            var date = rows[0].GetProperty("applicability_date").GetString()!;
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = $"/lu-legilux/{workKey}", date });
            Assert.IsNull(asOf.Refusal, "the state is served as before");
            var asObserved = await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier = $"/lu-legilux/{workKey}", date, snapshot = reference.Sha256 });
            Assert.AreEqual("snapshot_unknown", asObserved.Refusal?.Code);
            Assert.AreEqual(V3CorpusMount.AsObservedLegacyWhatWouldAnswer, asObserved.Refusal!.HelpfulPayload.GetProperty("what_would_answer").GetString());
            var drift = (await EnvelopeAsync(mount, "/api/v3/answer_drift", "answer_drift", new { })).Result!.Value;
            Assert.AreEqual(0, drift.GetProperty("invalidated_answers").GetArrayLength());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Schema 6 is read exactly, and only schema 6 and 8: its log must be its states' genesis log as schema 6 wrote it, its
    /// tables exactly schema 6's for user_version 6, and its stamp must name schema 6. The logical-rows stamp is re-sealed
    /// over every change, so only these checks can refuse.
    /// </summary>
    [TestMethod]
    public async Task ASchema6IndexIsReadExactlyOrRefused()
    {
        var (_, built, corpus) = await BuildStateEnvelopeAsync();
        var schema8 = built.IndexBytes.ToArray();
        foreach (var (what, bytes, expected) in new (string, byte[], string)[]
                 {
                     ("an event naming another state digest", Schema6Of(schema8, connection =>
                         Execute(connection, "UPDATE events SET detail_json=$detail WHERE seq=1", ("$detail", JsonSerializer.Serialize(new { state_sha256 = new string('9', 64) })))).Index,
                         "not the genesis log of its states"),
                     ("an event naming a source body schema 6 never recorded", Schema6Of(schema8, connection =>
                         Execute(connection, "UPDATE events SET detail_json=json_insert(detail_json,'$.source_body_sha256',json('[]')) WHERE seq=1")).Index,
                         "not the genesis log of its states"),
                     ("schema 6's tables under user_version 8", Schema6Of(schema8, connection => Execute(connection, "PRAGMA user_version=8")).Index,
                         "schema differs from the exact terminal schema"),
                     ("schema 6's tables under user_version 7, a schema never read", Schema6Of(schema8, connection => Execute(connection, "PRAGMA user_version=7")).Index,
                         "schema differs from the exact terminal schema"),
                     ("schema 6's tables with an observations table", Schema6Of(schema8, connection =>
                         Execute(connection, "CREATE TABLE observations (observation INTEGER NOT NULL PRIMARY KEY) STRICT")).Index,
                         "schema differs from the exact terminal schema"),
                     ("schema 6's tables stamped as schema 8", Schema6Of(schema8, connection =>
                         Execute(connection, "UPDATE stamp SET schema_identity=$schema WHERE stamp_id=1", ("$schema", LuxembourgIndexBuilder.Schema))).Index,
                         "stamp does not bind the expected corpus"),
                     ("schema 8's tables under user_version 6", MutateDatabase(schema8, connection =>
                     {
                         Execute(connection, "PRAGMA user_version=6");
                         Execute(connection, "UPDATE stamp SET schema_identity=$schema WHERE stamp_id=1", ("$schema", LuxembourgIndexBuilder.LegacySchema6));
                     }), "schema differs from the exact terminal schema"),
                 })
        {
            var reference = IndexRefOf(bytes);
            var exception = Assert.ThrowsExactly<InvalidDataException>(
                () => LuxembourgIndexReader.OpenAndVerify(reference, bytes, corpus.ArtifactRef, RebindManifest(built.CapabilityManifest, reference.Sha256)),
                what);
            StringAssert.Contains(exception.Message, expected, what);
        }

        // The untampered rewrite is read, so each refusal above is its own check's.
        var (index, indexRef) = Schema6Of(schema8);
        using var reader = LuxembourgIndexReader.OpenAndVerify(indexRef, index, corpus.ArtifactRef, RebindManifest(built.CapabilityManifest, indexRef.Sha256));
        Assert.AreEqual(LuxembourgIndexBuilder.LegacySchema6, reader.SchemaIdentity);
    }
}
