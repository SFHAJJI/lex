using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A schema-5 EU mount that this code did not write (the plan's item 2.1: the EU index /6 adds the act's force dates, and an
/// index /5, derive-c's among them, is read exactly as before). Its three files were built and verified by the code before
/// index /6 (v3/integration 24118293), from the retained GDPR envelope with its synthetic French package (one article,
/// "Texte français de test", no publisher text) through the acquisition fixture, and committed byte for byte with every
/// answer over them that the code before index /6 sent. So the head's schema-5 reading (its exact DDL, its states and its
/// logical rows) is checked against bytes it did not produce, and every answer over schema 5 against the answer the code
/// without force dates sends, whole envelope for whole envelope: no <c>force_dates</c> member, and the not-held row that says
/// no force date is held. A later change that moves one of those answers on purpose records them again at its base: the
/// answers were recorded again at v3/integration e504d188 (#989 gives every answer its freshness and commits context), by
/// that code with this class alone added, on Linux, the files unchanged; and again at 9b28c5c3 (the API's gaps, the plan's
/// items 6.10 and 8.6), on Linux, each row moving by its registry identity alone and coverage by its operations note and rows.
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeSchema5MountTests
{
    // The files' own SHA-256, as 24118293 wrote them (base-answers.json records the index's).
    internal const string IndexFileSha256 = "a008d71134cd3b5f1f1d69cf97499d04944729f6b92652f45fc1b19c5e38db3a";
    private const string ManifestFileSha256 = "75e969a05392973e7692d1a44a38ab9d85abf01fb9bc8daf2987627aeeeb317b";
    private const string CorpusFileSha256 = "7665cc484a2808c89313b2a6f7e5ef8cdf2e995dc363090958d63a564648753d";
    private const string Act = "32016R0679";

    // The commit the answers were last recorded at, on Linux: e504d188, the code without force dates, then 9b28c5c3 (the API's
    // gaps, 2026-10-27), where every row moved by its registry identity alone (lex-v3-operation-registry/2 and its digest) and
    // coverage by its operations note and rows too, then 320c0f20 (over #995: the registry digest alone), then ee44bf68 (R77 and
    // R57, writer/catalogue-and-compare: the exact-reference rule now names the record of an act only the catalogue holds, so
    // the one search answer with a reference moved by that rule's text alone), then caaff24d (the review of #1003: diff's
    // request schema gained `page`, so every row moved by the registry digest alone).
    private const string RecordedAtCommit = "caaff24d";

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuSchema5Mount", name));

    /// <summary>The requests asked of the schema-5 mount, in order; <c>verify</c> asks the permalink the timeline names.</summary>
    internal static IReadOnlyList<(string Operation, object Parameters)> Requests(string permalink) =>
    [
        ("coverage", new { }),
        ("resolve", new { identifier = Act }),
        ("dossier", new { identifier = Act }),
        ("dossier", new { identifier = Act, language = "fra" }),
        ("timeline", new { identifier = Act }),
        ("as_of", new { identifier = Act, date = "2016-04-27", language = "fra" }),
        ("as_of", new { identifier = Act, date = "2024-01-01" }),
        ("as_of", new { identifier = Act, date = "2016-01-01" }),
        ("evidence_bundle", new { identifier = Act, date = "2016-04-27", language = "fra" }),
        ("evidence_bundle", new { identifier = Act, date = "2020-06-01" }),
        ("search", new { query = "Texte français de test", language = "fra" }),
        ("search", new { query = "Texte français de test", language = "fra", identifier = Act }),
        ("verify", new { identifier = permalink }),
        ("in_force_on", new { identifier = Act, date = "2018-05-25" }),
        ("status_on", new { identifier = Act, date = "2018-05-25" }),
    ];

    private static readonly string[] Operations =
        ["coverage", "resolve", "dossier", "dossier", "timeline", "as_of", "as_of", "as_of", "evidence_bundle", "evidence_bundle",
         "search", "search", "verify", "in_force_on", "status_on"];

    [TestMethod]
    public void TheCommittedSchema5IndexIsReadExactlyAsSchema5CheckedIt()
    {
        var index = Fixture("europe-index.sqlite3.bin");
        var manifestBytes = Fixture("europe-capability-manifest.json.bin");
        var corpusBytes = Fixture("lex-corpus-6.json.bin");
        Assert.AreEqual(IndexFileSha256, Convert.ToHexStringLower(SHA256.HashData(index)));
        Assert.AreEqual(ManifestFileSha256, Convert.ToHexStringLower(SHA256.HashData(manifestBytes)));
        Assert.AreEqual(CorpusFileSha256, Convert.ToHexStringLower(SHA256.HashData(corpusBytes)));

        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        var indexRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(IndexFileSha256), IndexFileSha256);
        var manifestDigest = V3IndexCapabilityManifestArtifact.ComputeSha256(manifestBytes);
        var manifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(manifestDigest), manifestDigest), manifestBytes, PublisherId.EuEurLex, IndexFileSha256);
        using var reader = EuropeIndexReader.OpenAndVerify(indexRef, index, corpus.ArtifactRef, manifest);
        Assert.IsTrue(reader.HasStates);
        Assert.IsTrue(reader.HasArticleByteDigests);
        Assert.AreEqual(1, reader.ArticleCount);
        Assert.AreEqual(EuropeIndexStateDateStatus.OriginalWording, reader.ReadStates(Act).Single().DateStatus);
    }

    /// <summary>
    /// Every recorded request, asked again of the same committed files, answers the same bytes. The answers were recorded at
    /// the base (24118293 on Windows, again at e504d188, 9b28c5c3, 320c0f20, ee44bf68 and caaff24d on Linux); nothing in an EU answer reads the system (paths, culture or
    /// clock: the handler answers at one fixed observation time), and every system is held to them.
    /// </summary>
    [TestMethod]
    public async Task EveryEuAnswerOverTheSchema5MountIsTheOneTheCodeWithoutForceDatesSends()
    {
        using var recorded = JsonDocument.Parse(Fixture("base-answers.json"));
        var root = recorded.RootElement;
        Assert.AreEqual("24118293", root.GetProperty("written_at_commit").GetString());
        Assert.AreEqual(RecordedAtCommit, root.GetProperty("recorded_at_commit").GetString());
        Assert.AreEqual(IndexFileSha256, root.GetProperty("index_sha256").GetString());
        var rows = root.GetProperty("answers");
        CollectionAssert.AreEqual(Operations, rows.EnumerateArray().Select(static row => row.GetProperty("operation").GetString()).ToArray());

        var directory = await MountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            foreach (var row in rows.EnumerateArray())
            {
                var operation = row.GetProperty("operation").GetString()!;
                var trace = row.GetProperty("trace").GetString()!;
                var (status, body) = await LegacyMountAnswers.RespondAsync(mount, operation, row.GetProperty("request").GetString()!, trace);
                Assert.AreEqual(row.GetProperty("http_status").GetInt32(), status, $"{trace} {operation}");
                Assert.AreEqual(row.GetProperty("body").GetString(), body, $"{trace} {operation}: the answer the code without force dates sends");
                Assert.DoesNotContain("\"force_dates\":{", body, $"{trace} {operation}: an index /5 holds no force date");
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Records the answers the replay reads, only when <c>V3_RECORD_EU_SCHEMA5_ANSWERS</c> names a file: every request of
    /// <see cref="Requests"/> over the committed mount, answered by this code. It checks nothing, so a run that records fails.
    /// </summary>
    [TestMethod]
    public async Task TheEuAnswersAreRecordedWhereTheRecordingAsks()
    {
        var output = Environment.GetEnvironmentVariable("V3_RECORD_EU_SCHEMA5_ANSWERS");
        if (string.IsNullOrWhiteSpace(output))
        {
            Assert.Inconclusive("V3_RECORD_EU_SCHEMA5_ANSWERS names no file, so no answer is recorded.");
        }

        var directory = await MountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (_, timeline) = await LegacyMountAnswers.RespondAsync(mount, "timeline",
                JsonSerializer.Serialize(new { operation_id = "timeline", parameters = new { identifier = Act } }), "eu5-permalink");
            var permalink = JsonNode.Parse(timeline)!["result"]!["value"]!["languages"]![0]!["wordings"]![0]!["permalink"]!.GetValue<string>();
            var recorded = new JsonObject
            {
                ["written_at_commit"] = "24118293",
                ["recorded_at_commit"] = RecordedAtCommit,
                ["recorded_under"] = RuntimeInformation.OSDescription,
                ["index_sha256"] = IndexFileSha256,
                ["answers"] = await LegacyMountAnswers.RecordAsync(mount, Requests(permalink), "eu5"),
            };
            await File.WriteAllTextAsync(output!, recorded.ToJsonString());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }

        Assert.Fail($"V3_RECORD_EU_SCHEMA5_ANSWERS recorded {output} and checked nothing: run again without the variable.");
    }

    private static async Task<string> MountAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-eu5-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), Fixture("lex-corpus-6.json.bin"));
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName), Fixture("europe-index.sqlite3.bin"));
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeCapabilityManifestFileName), Fixture("europe-capability-manifest.json.bin"));
        return directory;
    }
}
