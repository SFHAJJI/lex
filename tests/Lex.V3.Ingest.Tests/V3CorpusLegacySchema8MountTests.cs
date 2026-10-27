using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A schema-8 mount that this code did not write (the review of #980, F1 and F2): its three files were built and verified
/// by the code before index 9 (v3/integration 3b7913ec), from a synthetic act (<c>synthetic-loi-2026-01-01-a1.xml</c>,
/// written for these tests, no publisher text) run through the acquisition fixture, and committed byte for byte with the
/// answers over it and over the real bounded first mount (schema 6) of the integration head this branch is based on
/// (5a659831, which also read the files with its own checks; its answers are 3b7913ec's but for the registry digest and
/// #978's evidence bundle). So the head's schema-8 reading (its exact DDL, state digest /1, log /2 and logical rows) is
/// checked against bytes it did not produce, and every answer over schema 8 and 6 against the answer the code without
/// index 9 sends, whole envelope for whole envelope. A later merge that moves those answers on purpose records them again
/// at its base (the writer is LegacyMountAnswers with the files read, not rebuilt).
/// </summary>
[TestClass]
public sealed class V3CorpusLegacySchema8MountTests
{
    // The files' own SHA-256, as 3b7913ec wrote them (base-answers.json records the same).
    internal const string IndexFileSha256 = "6c248196ef5a0f8d189634e02609fa7709960153aa57b92f7b396ea239b73453";
    private const string ManifestFileSha256 = "3b049c8b80dc46ea1659e60b5cf6ea14d4ff93f33cf67167190fd0c99154b1ef";
    private const string CorpusFileSha256 = "82a07d3c165d490776761cdc0bf0a68e6e47b0da4bf6c0b30ba042d053bcebab";
    private const string WorkKey = "loi-2026-01-01-a1";
    private const string StateSha256 = "084f808885bf9c974e93c79efa6ba2f7d8503b1e9f1970a5ddadbe6bd77e6784";

    internal static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySchema8Mount", name));

    private static byte[] Schema6(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySchema6Mount", name + ".bin"));

    private static SourceArtifactRef RefOf(byte[] bytes)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
    }

    [TestMethod]
    public void TheCommittedSchema8IndexIsReadExactlyAsSchema8CheckedIt()
    {
        var index = Fixture("luxembourg-index.sqlite3.bin");
        var manifestBytes = Fixture("luxembourg-capability-manifest.json.bin");
        var corpusBytes = Fixture("lex-corpus-6.json.bin");
        Assert.AreEqual(IndexFileSha256, Convert.ToHexStringLower(SHA256.HashData(index)));
        Assert.AreEqual(ManifestFileSha256, Convert.ToHexStringLower(SHA256.HashData(manifestBytes)));
        Assert.AreEqual(CorpusFileSha256, Convert.ToHexStringLower(SHA256.HashData(corpusBytes)));

        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        var indexRef = RefOf(index);
        var manifestDigest = V3IndexCapabilityManifestArtifact.ComputeSha256(manifestBytes);
        var manifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(manifestDigest), manifestDigest), manifestBytes, PublisherId.LuLegilux, indexRef.Sha256);
        using (var reader = LuxembourgIndexReader.OpenAndVerify(indexRef, index, corpus.ArtifactRef, manifest))
        {
            Assert.AreEqual(LuxembourgIndexBuilder.LegacySchema8, reader.SchemaIdentity);
            Assert.IsTrue(reader.RecordsBuilds);
            Assert.IsFalse(reader.RecordsCatalogueStates);
            Assert.AreEqual(LuxembourgIndexBuilder.LegacyEventLogSchema8, reader.LogSchema);
            reader.VerifyEventLogSources(corpus);
            var state = reader.ResolveWorkStates(WorkKey).Single();
            Assert.AreEqual(StateSha256, state.StateSha256, "state digest /1, as the code before index 9 derived it");
            Assert.AreEqual("2026-02-03", state.ApplicabilityDate);
            Assert.HasCount(2, state.ArticleIdentities, "the third article's shape is not admitted");
            Assert.IsNull(reader.ResolveObservations().Single().DerivedAt, "schema 8 records no derivation time");
            var row = reader.ResolveCatalogueStates(WorkKey).Single();
            Assert.IsNull(row.StateKind, "schema 8 holds none of index 9's fields");
            Assert.AreEqual("held", row.TextStatus);
            Assert.IsEmpty(reader.ResolveIntervals(WorkKey));
            Assert.AreEqual(1L, reader.ResolveCoverage().States);
        }

        // A schema-8 index is never carried forward by an index 9 build: its log is /2.
        Assert.IsNull(LuxembourgIndexPredecessor.TryRead(indexRef, index, out var refusal, out _));
        Assert.AreEqual(LuxembourgIndexBuildRefusal.PredecessorSchemaDiffers, refusal);
    }

    /// <summary>
    /// Every recorded request, asked again of the same committed files, answers the same bytes: the schema-8 mount (its
    /// provenance's /1 derivation, its events with no derivation time and the scope and genesis note it had, as_of,
    /// as_observed and the rest) and the schema-6 mount (its events and as_observed's refusal). The few sentences index 9
    /// adds are served on index 9 only. Recorded again at b1b41312 (search results by act, on 8e6d3d27): its two schema-8
    /// search answers gained, additively, each hit's extract, the page's acts, the population searched and their rules, and
    /// the ranking sentence states the new order (one state's hits together); no other row and no other member moved. Recorded
    /// again at 34d493d0 (freshness and the commits, plan 6.1 and 6.5, on 0b908870): all 38 rows, in their context alone, which
    /// gained `commits` (both null: no build report, and a test's host names no service commit) and the freshness header's new
    /// members (the schema-8 log's build time, the Luxembourg channel with no completion stated, no lag, no re-check; the
    /// schema-6 index records no build time); no result, refusal or registry digest moved. Recorded again at 496eeced (the
    /// owner's maximum data age of 30 days, O11, 2026-10-19): all 38 rows, each by its `maximum_data_age_days` alone, 10 to
    /// 30. Recorded again at 9b28c5c3 (the API's gaps, the plan's items 6.10 and 8.6, 2026-10-27): all 38 rows, each by its
    /// registry identity alone (`lex-v3-operation-registry/2` and its digest), and the two coverage answers by their operations
    /// note and rows too (an operation served from no data refuses inside the envelope, its row naming the reason and the mode);
    /// no other member moved. Recorded again at 320c0f20 (the same branch over v3/integration 473a9fae, #995: `rate_limited`'s
    /// payload schema typed as the limit sends it): all 38 rows by the registry digest alone. Recorded again at ee44bf68 (R77 and
    /// R57, writer/catalogue-and-compare): the two schema-8 searches by the exact-reference rule's text alone (it names the
    /// record of an act only the catalogue holds), the schema-8 diff by its new text_rule alone (R57: what changed carries its
    /// served text), and the five schema-6 answers about loi-2017-03-14-a439, which that index's catalogue names and whose
    /// states it does not hold, from identifier_unknown to text_not_available with the act's record (R77); no other row and no
    /// other member moved. Recorded again at a9179b39: the same five schema-6 answers by the record's two new members alone
    /// (document_type_codes, in_force_status_codes: the publisher's own word for its type and status). Recorded again at caaff24d
    /// (the review of #1003): all 38 rows by the registry digest (diff's request schema gained `page`, F4), the schema-8 diff by
    /// its text_rule and text_page too (the wording in pages), and the five schema-6 answers about loi-2017-03-14-a439 by the
    /// record's reason too (F1: its file is held and the index holds its four articles and no state, file_articles_no_state, as
    /// Coverage counts it, in place of catalogue_cc_by_not_acquired), its body receipt as retained_transport_evidence, its
    /// disposition members and its rules' text; no other member moved. Recorded again at 222a3cf6 (search with capitals and
    /// accents ignored, the plan's item 4.4, over #1003): the two schema-8 searches by their matching, lanes and extract
    /// sentences alone, no hit moved; no other row moved. The answers are those
    /// production sends: recorded on Linux, as the release image
    /// runs (globalization-invariant, UTC; the language library changes no byte), and every system is held to them. Resolve
    /// of a rooted path was the one answer Windows sent otherwise, until a rooted path was read as an identifier on every
    /// system (<c>LooksLikeIdentifier</c>).
    /// </summary>
    [TestMethod]
    public async Task EveryAnswerOverTheSchema8AndSchema6MountsIsTheOneTheCodeWithoutIndex9Sends()
    {
        using var recorded = JsonDocument.Parse(Fixture("base-answers.json"));
        var root = recorded.RootElement;
        Assert.AreEqual("3b7913ec", root.GetProperty("written_at_commit").GetString());
        Assert.AreEqual("222a3cf6", root.GetProperty("recorded_at_commit").GetString());
        Assert.AreEqual(IndexFileSha256, root.GetProperty("index_sha256").GetString());
        Assert.AreEqual(StateSha256, root.GetProperty("state_sha256").GetString());

        StringAssert.StartsWith(root.GetProperty("recorded_under").GetString(), "Linux", "the answers production sends");
        Assert.IsFalse(root.TryGetProperty("windows_answers", out _), "no system answers otherwise");

        var eight = root.GetProperty("schema8_answers");
        Assert.AreEqual(LegacyMountAnswers.Schema8Requests(IndexFileSha256).Count, eight.GetArrayLength());
        await ReplayAsync(
            Fixture("lex-corpus-6.json.bin"), Fixture("luxembourg-index.sqlite3.bin"), Fixture("luxembourg-capability-manifest.json.bin"), eight);
        var six = root.GetProperty("schema6_answers");
        Assert.AreEqual(LegacyMountAnswers.Schema6Requests.Count, six.GetArrayLength());
        await ReplayAsync(
            Schema6("lex-corpus-6.json"), Schema6("luxembourg-index.sqlite3"), Schema6("luxembourg-capability-manifest.json"), six);

        // What the review named, said outright as well as by the bytes.
        JsonElement Answer(JsonElement rows, string operation) =>
            JsonDocument.Parse(rows.EnumerateArray().First(row => row.GetProperty("operation").GetString() == operation)
                .GetProperty("body").GetString()!).RootElement.GetProperty("result").GetProperty("value");
        Assert.AreEqual(V3CorpusMount.ProvenanceDerivation, Answer(eight, "provenance").GetProperty("derivation").GetString());
        var events = Answer(eight, "events");
        Assert.IsFalse(events.GetProperty("log").TryGetProperty("derived_at", out _), "no derivation time on schema 8");
        Assert.AreEqual(V3CorpusMount.EventsScope, events.GetProperty("scope").GetString());
        Assert.AreEqual(V3CorpusMount.EventsGenesisNote, events.GetProperty("genesis_note").GetString());
        Assert.IsFalse(Answer(six, "events").GetProperty("log").TryGetProperty("derived_at", out _), "no derivation time on schema 6");
    }

    /// <summary>
    /// Records the answers <see cref="EveryAnswerOverTheSchema8AndSchema6MountsIsTheOneTheCodeWithoutIndex9Sends"/> replays,
    /// only when <c>V3_RECORD_LEGACY_ANSWERS</c> names a file: every request of <see cref="LegacyMountAnswers"/> over the two
    /// committed mounts, answered by this code, written as <c>{schema8_answers, schema6_answers}</c> for the fixture's rows. It
    /// is run where production runs (Linux, globalization-invariant, UTC) when a change moves those answers on purpose, and the
    /// rows replace the fixture's; it checks nothing, so a run that records fails rather than passes.
    /// </summary>
    [TestMethod]
    public async Task TheAnswersAreRecordedWhereTheRecordingAsks()
    {
        var output = Environment.GetEnvironmentVariable("V3_RECORD_LEGACY_ANSWERS");
        if (string.IsNullOrWhiteSpace(output))
        {
            Assert.Inconclusive("V3_RECORD_LEGACY_ANSWERS names no file, so no answer is recorded.");
        }

        async Task<System.Text.Json.Nodes.JsonArray> RecordAsync(byte[] corpus, byte[] index, byte[] manifest, IReadOnlyList<(string, object)> requests, string prefix)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-legacy8-record-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus);
                await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.IndexFileName), index);
                await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), manifest);
                using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
                Assert.IsNotNull(mount);
                return await LegacyMountAnswers.RecordAsync(mount, requests, prefix);
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            }
        }

        var recorded = new System.Text.Json.Nodes.JsonObject
        {
            ["schema8_answers"] = await RecordAsync(
                Fixture("lex-corpus-6.json.bin"), Fixture("luxembourg-index.sqlite3.bin"), Fixture("luxembourg-capability-manifest.json.bin"),
                LegacyMountAnswers.Schema8Requests(IndexFileSha256), "schema8"),
            ["schema6_answers"] = await RecordAsync(
                Schema6("lex-corpus-6.json"), Schema6("luxembourg-index.sqlite3"), Schema6("luxembourg-capability-manifest.json"),
                LegacyMountAnswers.Schema6Requests, "schema6"),
        };
        await File.WriteAllTextAsync(output!, recorded.ToJsonString());
        Assert.Fail($"V3_RECORD_LEGACY_ANSWERS recorded {output} and checked nothing: run again without the variable, which is the only run that checks anything.");
    }

    /// <summary>
    /// The review of #1003, F1, on the real bounded first mount (schema 6): its one act, <c>loi/2017/03/14/a439/jo</c>, whose file
    /// the corpus holds (acquired, <c>agreed_same_run_cc_by</c>) and whose four admitted articles its index holds with no state of
    /// it, is recorded with the reason Coverage counts that file under, counted here over the same committed files: a file with
    /// text, of which no state is served, with the receipt of the file's body as its retained transport evidence; never as a
    /// version of which no file is held (<c>catalogue_cc_by_not_acquired</c>, which the record said before the repair).
    /// </summary>
    [TestMethod]
    public async Task TheSchema6ActsRecordGivesTheReasonCoverageCountsItsHeldFileUnder()
    {
        var corpusBytes = Schema6("lex-corpus-6.json");
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-legacy6-record-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpusBytes);
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.IndexFileName), Schema6("luxembourg-index.sqlite3"));
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), Schema6("luxembourg-capability-manifest.json"));
            const string Act = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";
            var members = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes).Set.Members;
            var file = members.Single(member => member.LuxembourgRights?.SelectedWemi.RootIri == Act);
            Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, file.Outcome, "the fixture's act is acquired");
            var coverage = V3TextYield.FromMount(members, Path.Combine(directory, V3CorpusMount.IndexFileName), null)
                .Publishers.Single().Classes.Single(static row => row.Family == "loi" && row.Kind == "as_published");
            Assert.HasCount(0, coverage.VersionsNotHeld!, "Coverage counts the act's file, not a version of which no file is held");
            Assert.AreEqual((1L, 1L, 4L), (coverage.Files, coverage.FilesWithText, coverage.Articles), "a file with text: its four articles");

            using (var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None))
            {
                Assert.IsNotNull(mount);
                foreach (var identifier in new[] { "/lu-legilux/loi-2017-03-14-a439", Act })
                {
                    var request = JsonSerializer.Serialize(new { operation_id = "dossier", parameters = new { identifier } });
                    var (status, body) = await LegacyMountAnswers.RespondAsync(mount, "dossier", request, "schema6-record");
                    Assert.AreEqual(200, status, identifier);
                    using var envelope = JsonDocument.Parse(body);
                    var refusal = envelope.RootElement.GetProperty("refusal");
                    Assert.AreEqual("text_not_available", refusal.GetProperty("code").GetString(), identifier);
                    var payload = refusal.GetProperty("helpful_payload");
                    Assert.AreEqual(V3CorpusMount.FileArticlesNoState, payload.GetProperty("reason").GetString(), identifier);
                    Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("disposition").ValueKind, identifier);
                    CollectionAssert.AreEqual(new[] { V3CorpusMount.FileArticlesNoState },
                        payload.GetProperty("version_reasons").EnumerateArray().Select(static value => value.GetString()).ToArray(), identifier);
                    Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("version_dispositions").EnumerateArray().Single().ValueKind, identifier);
                    Assert.AreEqual(file.BodyReceiptSha256, payload.GetProperty("retained_transport_evidence").GetString(), identifier);
                    CollectionAssert.AreEqual(new[] { "new_official_observation" },
                        payload.GetProperty("what_would_answer").EnumerateArray().Select(static value => value.GetString()).ToArray(), identifier);
                }
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task ReplayAsync(
        byte[] corpus, byte[] index, byte[] manifest, JsonElement rows)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-legacy8-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus);
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.IndexFileName), index);
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), manifest);
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            foreach (var row in rows.EnumerateArray())
            {
                var operation = row.GetProperty("operation").GetString()!;
                var trace = row.GetProperty("trace").GetString()!;
                var (status, body) = await LegacyMountAnswers.RespondAsync(mount, operation, row.GetProperty("request").GetString()!, trace);
                Assert.AreEqual(row.GetProperty("http_status").GetInt32(), status, $"{trace} {operation}");
                var answer = row.GetProperty("body").GetString();
                Assert.AreEqual(answer, body, $"{trace} {operation}: the answer the code without index 9 sends");
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
