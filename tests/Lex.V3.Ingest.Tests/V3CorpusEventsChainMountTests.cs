using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>events</c> and <c>answer_drift</c> over a chained log (predecessor chaining, the fourth slice). The mount holds the
/// state fixture's act built on a predecessor whose log held the same work at an earlier date, so the chained build
/// sights the act's state (first_sighting) and closes the earlier state's interval (interval_closed) through the real
/// builder. The predecessor's log is crafted from the fixture's genesis log and is a valid log by its own stamp and
/// replay; nothing here is a hand-made row of the chained index.
/// </summary>
[TestClass]
public sealed class V3CorpusEventsChainMountTests
{
    internal const string EarlierDate = "2001-01-01";

    internal sealed record ChainedMount(string Directory, string PredecessorSha256, string WorkKey, string Language, string HeldStateSha256)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    internal static async Task<ChainedMount> ChainedMountAsync()
    {
        var (envelope, first, _) = await LuxembourgIndexBuilderTests.BuildStateEnvelopeAsync();
        string[] key = [];
        var heldState = new string('e', 64);
        var (log, reference) = LuxembourgIndexBuilderTests.Tampered(first.IndexBytes.ToArray(), connection =>
        {
            var held = LuxembourgIndexBuilderTests.ReadEvents(connection)[0];
            key = JsonSerializer.Deserialize<string[]>(held.Key)!;
            var earlier = new[] { key[0], EarlierDate, key[2], key[3] };
            var detail = System.Text.Json.Nodes.JsonNode.Parse(held.DetailJson)!;
            detail["state_sha256"] = heldState;
            LuxembourgIndexBuilderTests.Execute(connection, "UPDATE events SET key=$key, detail_json=$detail WHERE seq=1",
                ("$key", JsonSerializer.Serialize(earlier)), ("$detail", detail.ToJsonString()));
        });
        var predecessor = LuxembourgIndexPredecessor.TryRead(reference, log, out var readRefusal, out var readDetail);
        Assert.IsNotNull(predecessor, $"{readRefusal}: {readDetail}");
        var chained = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, LuxembourgIndexBuilderTests.Later, out var refusal, out var detail);
        Assert.IsNotNull(chained, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");

        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-chained-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus.CanonicalBytes.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.IndexFileName), chained.IndexBytes.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), chained.CapabilityManifestBytes.ToArray());
        // The crafted predecessor is no mount, so none of the earlier builds is held; the record says so (each absent).
        await V3CorpusMountWriter.WriteRetentionRecordAsync(directory, chained.IndexRef, chained.IndexBytes, CancellationToken.None);
        return new ChainedMount(directory, reference.Sha256, key[0], key[3], heldState);
    }

    [TestMethod]
    public async Task EventsOverAChainedLogNameItsAncestryAndItsRevisionsAndHonourAnAncestorsCursor()
    {
        await using var fixture = await ChainedMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var answer = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { })).Result!.Value;
        var log = answer.GetProperty("log");
        Assert.AreEqual(V3EventRegistry.ChainedBasis, log.GetProperty("basis").GetString());
        Assert.AreEqual(fixture.PredecessorSha256, log.GetProperty("predecessor_index_sha256").GetString());
        Assert.AreEqual(1, log.GetProperty("observations_compared").GetInt32());
        var ancestor = log.GetProperty("ancestors").EnumerateArray().Single();
        Assert.AreEqual(fixture.PredecessorSha256, ancestor.GetProperty("log_id").GetString());
        Assert.AreEqual(1, ancestor.GetProperty("last_seq").GetInt64(), "the predecessor held one event");
        Assert.AreEqual(LuxembourgIndexBuilderTests.LaterText, log.GetProperty("built_at").GetString(), "when this build ran");
        Assert.AreEqual(LuxembourgIndexBuilderTests.BuiltAtText, ancestor.GetProperty("built_at").GetString(), "when the predecessor's build ran");

        var events = answer.GetProperty("events").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "first_sighting", "interval_closed", "first_sighting" }, events.Select(static e => e.GetProperty("event").GetString()).ToArray());
        Assert.AreEqual(EarlierDate, events[1].GetProperty("applicability_date").GetString(), "the earlier state, which this build lacks, stays held and closes");
        var closed = events[1].GetProperty("detail");
        Assert.IsTrue(closed.GetProperty("derived").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, closed.GetProperty("previous_to").ValueKind, "it was the latest state");
        Assert.AreEqual(events[2].GetProperty("applicability_date").GetString(), closed.GetProperty("new_to").GetString(), "it closes at the act's own date");
        Assert.IsFalse(string.IsNullOrEmpty(answer.GetProperty("chained_note").GetString()));
        CollectionAssert.Contains(answer.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray(), "withdrawal_events");

        // A cursor of the ancestor log reads on in this log; past the ancestor's last event it was never handed out; a
        // cursor of a log this one does not carry is snapshot_unknown.
        var onward = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { after = $"{fixture.PredecessorSha256}:1" })).Result!.Value;
        CollectionAssert.AreEqual(new long[] { 2, 3 }, onward.GetProperty("events").EnumerateArray().Select(static e => e.GetProperty("seq").GetInt64()).ToArray());
        StringAssert.StartsWith(onward.GetProperty("next_after").GetString(), log.GetProperty("log_id").GetString() + ":");
        var beyond = await PostAsync(mount, "/api/v3/events", JsonSerializer.Serialize(new { operation_id = "events", parameters = new { after = $"{fixture.PredecessorSha256}:2" } }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, beyond.Response.StatusCode, "the ancestor never handed out seq 2");
        var foreign = await EnvelopeAsync(mount, "/api/v3/events", "events", new { after = $"{new string('f', 64)}:1" });
        Assert.AreEqual("snapshot_unknown", foreign.Refusal?.Code);
    }

    [TestMethod]
    public async Task AnswerDriftOverAChainedLogEnumeratesTheDatesARevisionMoved()
    {
        await using var fixture = await ChainedMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var events = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { })).Result!.Value.GetProperty("events").EnumerateArray().ToArray();
        var current = events[2];
        var answer = (await EnvelopeAsync(mount, "/api/v3/answer_drift", "answer_drift", new { })).Result!.Value;
        Assert.AreEqual(1, answer.GetProperty("revising_events_considered").GetInt64());
        StringAssert.StartsWith(answer.GetProperty("basis").GetString(), "enumerated from the log's validity_revised and interval_closed events");
        var row = answer.GetProperty("invalidated_answers").EnumerateArray().Single();
        Assert.AreEqual("interval_closed", row.GetProperty("event").GetString());
        Assert.AreEqual(fixture.WorkKey, row.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.Language, row.GetProperty("language").GetString());
        Assert.AreEqual(current.GetProperty("applicability_date").GetString(), row.GetProperty("dates_from").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("dates_to_exclusive").ValueKind, "open: every later date moved");
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}/{EarlierDate}--{fixture.HeldStateSha256}", row.GetProperty("answered_before").GetString());
        CollectionAssert.AreEqual(new[] { current.GetProperty("permalink").GetString() }, row.GetProperty("answered_now").EnumerateArray().Select(static p => p.GetString()).ToArray());
        Assert.IsTrue(row.GetProperty("derived").GetBoolean());
        Assert.IsFalse(answer.GetProperty("has_more").GetBoolean());

        var after = (await EnvelopeAsync(mount, "/api/v3/answer_drift", "answer_drift", new { after = $"{answer.GetProperty("log").GetProperty("log_id").GetString()}:2" })).Result!.Value;
        Assert.AreEqual(0, after.GetProperty("invalidated_answers").GetArrayLength(), "after the revising event, none");
    }
}
