using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>events</c> and <c>answer_drift</c> on a mounted corpus. The index's log is a genesis log: one
/// <c>first_sighting</c> per held state, numbered from 1 in the states' order, with no observation time.
/// It is polled by a cursor that names its log, at least once; a cursor from another log refuses
/// <c>snapshot_unknown</c>. <c>answer_drift</c> enumerates nothing from such a log and says why.
/// </summary>
[TestClass]
public sealed class V3CorpusEventsMountTests
{
    private static string EventsTarget => V3RestRouteBinding.Events.RawTarget;

    private static string DriftTarget => V3RestRouteBinding.AnswerDrift.RawTarget;

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public void TheEventsAndDriftRoutesAreServedBindings()
    {
        Assert.AreEqual("/api/v3/events", EventsTarget);
        Assert.AreEqual("/api/v3/answer_drift", DriftTarget);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Events));
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.AnswerDrift));
    }

    [TestMethod]
    public async Task TheGenesisLogHoldsOneFirstSightingPerStateInStateOrderWithNoObservationTime()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 100), "other", workLeaf: "n9");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var indexSha256 = (await EnvelopeAsync(mount, V3RestRouteBinding.Coverage.RawTarget, "coverage", new { }))
            .Result!.Value.GetProperty("mounted").GetProperty("index_sha256").GetString()!;

        var envelope = await EnvelopeAsync(mount, EventsTarget, "events", new { });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        Assert.AreEqual("event", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        var log = body.GetProperty("log");
        Assert.AreEqual(indexSha256, log.GetProperty("log_id").GetString(), "the log is named by the index that holds it.");
        Assert.AreEqual("genesis", log.GetProperty("basis").GetString());
        Assert.AreEqual(JsonValueKind.Null, log.GetProperty("predecessor_index_sha256").ValueKind);
        Assert.AreEqual(0, log.GetProperty("observations_compared").GetInt32());
        Assert.AreEqual(3, log.GetProperty("events_held").GetInt64());
        Assert.AreEqual(3, log.GetProperty("last_seq").GetInt64());

        var events = body.GetProperty("events").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, events.Select(static value => value.GetProperty("seq").GetInt64()).ToArray());
        foreach (var value in events)
        {
            Assert.AreEqual("first_sighting", value.GetProperty("event").GetString());
            Assert.AreEqual("state", value.GetProperty("scope").GetString());
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("observed_from").ValueKind, "no observation time is held, and none is invented.");
            Assert.AreEqual($"{indexSha256}:{value.GetProperty("seq").GetInt64()}", value.GetProperty("cursor").GetString());
            var permalink = value.GetProperty("permalink").GetString()!;
            Assert.AreEqual(
                $"{value.GetProperty("stable_coordinate").GetString()}--{value.GetProperty("state_sha256").GetString()}", permalink);
            var verified = await EnvelopeAsync(mount, V3RestRouteBinding.Verify.RawTarget, "verify", new { identifier = permalink });
            Assert.IsNull(verified.Refusal, $"{permalink}: {verified.Refusal?.Code}");
        }

        var keys = events.Select(static value => (
            value.GetProperty("work_key").GetString()!,
            value.GetProperty("applicability_date").GetString()!,
            value.GetProperty("expression_iri").GetString()!,
            value.GetProperty("language").GetString()!)).ToArray();
        CollectionAssert.AreEqual(
            keys.OrderBy(static key => key.Item1, StringComparer.Ordinal)
                .ThenBy(static key => key.Item2, StringComparer.Ordinal)
                .ThenBy(static key => key.Item3, StringComparer.Ordinal)
                .ThenBy(static key => key.Item4, StringComparer.Ordinal).ToArray(),
            keys,
            "the log is in the states table's key order.");
        Assert.IsTrue(
            events.Any(value => value.GetProperty("state_sha256").GetString() == fixture.StateSha256),
            "the fixture's own state is sighted.");
        Assert.IsFalse(body.GetProperty("has_more").GetBoolean());
        Assert.AreEqual($"{indexSha256}:3", body.GetProperty("next_after").GetString());
        CollectionAssert.AreEqual(
            V3EventRegistry.Mintable.ToArray(),
            body.GetProperty("event_names").GetProperty("mintable").EnumerateArray().Select(static value => value.GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first_sighting" },
            body.GetProperty("event_names").GetProperty("in_this_log").EnumerateArray().Select(static value => value.GetString()).ToArray());
        StringAssert.Contains(body.GetProperty("delivery").GetString(), "at least once");
        StringAssert.Contains(body.GetProperty("delivery").GetString(), "a new build starts a new log");
        StringAssert.Contains(body.GetProperty("event_names").GetProperty("note").GetString(), "this build mints first_sighting only");
        StringAssert.Contains(body.GetProperty("silence_note").GetString(), "says nothing about whether the publisher changed anything");
        CollectionAssert.AreEqual(
            new[] { "observation_times", "revision_events", "work_level_events", "upstream_health" },
            body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());
    }

    [TestMethod]
    public async Task APageWalksTheLogOnceAndTheSameCursorAnswersTheSameEventsAgain()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 800), "latest");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var first = (await EnvelopeAsync(mount, EventsTarget, "events", new { limit = 2 })).Result!.Value;
        CollectionAssert.AreEqual(new long[] { 1, 2 }, Seqs(first));
        Assert.IsTrue(first.GetProperty("has_more").GetBoolean());
        var logId = first.GetProperty("log").GetProperty("log_id").GetString()!;
        Assert.AreEqual($"{logId}:2", first.GetProperty("next_after").GetString());

        var second = (await EnvelopeAsync(mount, EventsTarget, "events", new { after = $"{logId}:2", limit = 2 })).Result!.Value;
        CollectionAssert.AreEqual(new long[] { 3 }, Seqs(second));
        Assert.IsFalse(second.GetProperty("has_more").GetBoolean());
        Assert.AreEqual($"{logId}:3", second.GetProperty("next_after").GetString());

        var again = (await EnvelopeAsync(mount, EventsTarget, "events", new { after = $"{logId}:2", limit = 2 })).Result!.Value;
        Assert.AreEqual(second.GetProperty("events").GetRawText(), again.GetProperty("events").GetRawText(), "at least once: the same cursor answers the same events.");

        var end = (await EnvelopeAsync(mount, EventsTarget, "events", new { after = $"{logId}:3" })).Result!.Value;
        Assert.IsEmpty(Seqs(end));
        Assert.AreEqual($"{logId}:3", end.GetProperty("next_after").GetString(), "at the end the cursor to poll next stays where it is.");

        var start = (await EnvelopeAsync(mount, EventsTarget, "events", new { after = $"{logId}:0" })).Result!.Value;
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, Seqs(start), "seq 0 reads the log from its first event.");
    }

    [TestMethod]
    public async Task AnEventNameNarrowsTheLogAndANameNotHeldAnswersEmptyWithTheCursorAtTheEnd()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var sightings = (await EnvelopeAsync(mount, EventsTarget, "events", new { @event = "first_sighting" })).Result!.Value;
        CollectionAssert.AreEqual(new long[] { 1 }, Seqs(sightings));
        Assert.AreEqual("first_sighting", sightings.GetProperty("requested_event").GetString());

        var revisions = (await EnvelopeAsync(mount, EventsTarget, "events", new { @event = "validity_revised" })).Result!.Value;
        Assert.IsEmpty(Seqs(revisions));
        var logId = revisions.GetProperty("log").GetProperty("log_id").GetString()!;
        Assert.AreEqual($"{logId}:1", revisions.GetProperty("next_after").GetString());
    }

    [TestMethod]
    public async Task ACursorFromAnotherLogRefusesSnapshotUnknownAndAMalformedRequestIsASchemaFailure()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var logId = (await EnvelopeAsync(mount, EventsTarget, "events", new { })).Result!.Value
            .GetProperty("log").GetProperty("log_id").GetString()!;
        var otherLog = new string('0', 64);

        foreach (var (target, operation) in new[] { (EventsTarget, "events"), (DriftTarget, "answer_drift") })
        {
            var foreign = await EnvelopeAsync(mount, target, operation, new { after = $"{otherLog}:1" });
            Assert.AreEqual("snapshot_unknown", foreign.Refusal?.Code, operation);
            Assert.AreEqual(otherLog, foreign.Refusal!.HelpfulPayload.GetProperty("snapshot_id").GetString());
            StringAssert.Contains(foreign.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(), "a cursor from this log");
        }

        foreach (var (scenario, parameters) in new (string, object)[]
                 {
                     ("a sequence number this log never handed out", new { after = $"{logId}:2" }),
                     ("a cursor that is not {log}:{seq}", new { after = "yesterday" }),
                     ("a sequence number with a leading zero", new { after = $"{logId}:01" }),
                     ("the coverage event, which this pipeline never mints", new { @event = "coverage_changed" }),
                     ("a name outside the registry", new { @event = "amended" }),
                     ("a limit of zero", new { limit = 0 }),
                     ("a limit over the ceiling", new { limit = 201 }),
                     ("an undeclared parameter", new { since = "2024-01-01" }),
                 })
        {
            var context = await PostAsync(mount, EventsTarget, JsonSerializer.Serialize(new { operation_id = "events", parameters }));
            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode, scenario);
            using var problem = JsonDocument.Parse(ResponseBytes(context));
            Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString(), scenario);
        }
    }

    [TestMethod]
    public async Task TheDriftOfAGenesisLogIsAnEmptyListWithItsBasisNeverAClaimThatNothingDrifted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await EnvelopeAsync(mount, DriftTarget, "answer_drift", new { });
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        Assert.AreEqual("answer_drift", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.AreEqual(0, body.GetProperty("invalidated_answers").GetArrayLength());
        Assert.AreEqual(0, body.GetProperty("revising_events_considered").GetInt64());
        CollectionAssert.AreEqual(
            new[] { "validity_revised", "interval_closed" },
            body.GetProperty("revising_event_types").EnumerateArray().Select(static value => value.GetString()).ToArray());
        Assert.IsFalse(body.GetProperty("asserts_no_drift_in_law").GetBoolean());
        Assert.IsFalse(body.GetProperty("asserts_publisher_unrevised").GetBoolean());
        StringAssert.Contains(body.GetProperty("basis").GetString(), "genesis log");
        StringAssert.Contains(body.GetProperty("what_would_answer").GetString(), "a later build");
        var logId = body.GetProperty("log").GetProperty("log_id").GetString()!;
        Assert.AreEqual($"{logId}:2", body.GetProperty("next_after").GetString());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("work_key").ValueKind);

        var work = (await EnvelopeAsync(mount, DriftTarget, "answer_drift", new { identifier = $"/lu-legilux/{fixture.WorkKey}" })).Result!.Value;
        Assert.AreEqual(fixture.WorkKey, work.GetProperty("work_key").GetString());
        Assert.AreEqual(0, work.GetProperty("invalidated_answers").GetArrayLength());

        var unknown = await EnvelopeAsync(mount, DriftTarget, "answer_drift", new { identifier = "/lu-legilux/no-such-work" });
        Assert.AreEqual("identifier_unknown", unknown.Refusal?.Code, "an identifier refuses as dossier refuses.");
    }

    [TestMethod]
    public async Task AToolCallAnswersTheEnvelopeTheRestRouteAnswers()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (target, operation) in new[] { (EventsTarget, "events"), (DriftTarget, "answer_drift") })
        {
            var rest = await EnvelopeAsync(mount, target, operation, new { limit = 1 });
            var mcp = await PostAsync(mount, V3ApiHandler.McpRawTarget, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = operation, arguments = new { limit = 1 } },
            }));
            using var rpc = JsonDocument.Parse(ResponseBytes(mcp));
            var structured = rpc.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.AreEqual(rest.Verdict, structured.GetProperty("verdict").GetString(), operation);
            Assert.IsTrue(JsonElement.DeepEquals(rest.Result!.Value, structured.GetProperty("result").GetProperty("value")), operation);
        }
    }

    [TestMethod]
    public async Task WithoutTheLuxembourgIndexBothRefuseNoCorpusMounted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        Assert.AreEqual("no_corpus_mounted", (await EnvelopeAsync(mount, EventsTarget, "events", new { })).Refusal?.Code);
        Assert.AreEqual("no_corpus_mounted", (await EnvelopeAsync(mount, DriftTarget, "answer_drift", new { })).Refusal?.Code);
    }

    private static long[] Seqs(JsonElement body) =>
        body.GetProperty("events").EnumerateArray().Select(static value => value.GetProperty("seq").GetInt64()).ToArray();
}
