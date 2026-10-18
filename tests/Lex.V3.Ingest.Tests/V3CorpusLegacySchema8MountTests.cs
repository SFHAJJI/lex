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
/// written for these tests, no publisher text) run through the acquisition fixture, and committed byte for byte with
/// that code's answers over it and over the real bounded first mount (schema 6). So the head's schema-8 reading (its exact
/// DDL, state digest /1, log /2 and logical rows) is checked against bytes it did not produce, and every answer over
/// schema 8 and 6 against the answer the code before index 9 sent, whole envelope for whole envelope.
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
    /// adds are served on index 9 only.
    /// </summary>
    [TestMethod]
    public async Task EveryAnswerOverTheSchema8AndSchema6MountsIsTheOneTheCodeBeforeIndex9Sent()
    {
        using var recorded = JsonDocument.Parse(Fixture("base-answers.json"));
        var root = recorded.RootElement;
        Assert.AreEqual("3b7913ec", root.GetProperty("recorded_at_commit").GetString());
        Assert.AreEqual(IndexFileSha256, root.GetProperty("index_sha256").GetString());
        Assert.AreEqual(StateSha256, root.GetProperty("state_sha256").GetString());

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

    private static async Task ReplayAsync(byte[] corpus, byte[] index, byte[] manifest, JsonElement rows)
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
                Assert.AreEqual(row.GetProperty("body").GetString(), body, $"{trace} {operation}: the answer the code before index 9 sent");
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
