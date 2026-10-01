using System.Text.Json;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusEventsChainMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>as_observed</c> by build snapshot (predecessor chaining, the fifth slice), over the chained mount the event tests
/// use: a predecessor whose log held the fixture's work at an earlier date only, and the real build chained to it, which
/// sights the act's own state and closes the earlier one. Snapshot 1 is the predecessor's index digest, built at
/// <see cref="LuxembourgIndexBuilderTests.BuiltAtText"/>; snapshot 2 the mounted index, built at
/// <see cref="LuxembourgIndexBuilderTests.LaterText"/>.
/// </summary>
[TestClass]
public sealed class V3CorpusAsObservedMountTests
{
    private const string Route = "/api/v3/as_observed";

    private static async Task<(string ActDate, string MountedSha256)> ActAsync(V3CorpusMount mount)
    {
        var events = (await EnvelopeAsync(mount, "/api/v3/events", "events", new { })).Result!.Value;
        return (events.GetProperty("events")[2].GetProperty("applicability_date").GetString()!, events.GetProperty("log").GetProperty("log_id").GetString()!);
    }

    [TestMethod]
    public async Task AnEarlierSnapshotAnswersWithTheStateItHeldNamedWithoutTextAndBoundedByItsBuildTime()
    {
        await using var fixture = await ChainedMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var (actDate, _) = await ActAsync(mount);

        var envelope = await EnvelopeAsync(mount, Route, "as_observed", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = actDate, snapshot = fixture.PredecessorSha256 });
        Assert.AreEqual("version_state", envelope.Result!.ObjectType);
        var answer = envelope.Result.Value;
        var snapshot = answer.GetProperty("snapshot");
        Assert.AreEqual(fixture.PredecessorSha256, snapshot.GetProperty("snapshot_id").GetString());
        Assert.AreEqual(1, snapshot.GetProperty("observation").GetInt64());
        Assert.AreEqual(2, snapshot.GetProperty("observations_in_log").GetInt64());
        Assert.IsFalse(snapshot.GetProperty("mounted").GetBoolean());
        Assert.AreEqual(LuxembourgIndexBuilderTests.BuiltAtText, snapshot.GetProperty("observed_no_later_than").GetString(), "the predecessor's build time, its upper bound");
        Assert.IsFalse(snapshot.GetProperty("observation_time_held").GetBoolean());
        Assert.IsFalse(answer.TryGetProperty("observed_at", out _), "no observation time is stated");

        // At that snapshot the log held the work at the earlier date only: on the act's date, that state applied, with no
        // later one, and only the predecessor held it, so it is named without text.
        var state = answer.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(EarlierDate, state.GetProperty("applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, state.GetProperty("next_applicability_date").ValueKind, "no later state was held at that snapshot");
        Assert.AreEqual(fixture.HeldStateSha256, state.GetProperty("state_sha256").GetString());
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}/{EarlierDate}--{fixture.HeldStateSha256}", state.GetProperty("permalink").GetString());
        Assert.IsFalse(state.GetProperty("text_held").GetBoolean());
        Assert.IsFalse(state.TryGetProperty("articles", out _), "no text of a state the mounted index does not hold");
        Assert.AreEqual(1, state.GetProperty("source_body_sha256").GetArrayLength());
    }

    [TestMethod]
    public async Task TheMountedSnapshotQuotesWhatItHoldsAndKeepsTheEarlierStateTheLogStillHolds()
    {
        await using var fixture = await ChainedMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var (actDate, mountedSha256) = await ActAsync(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var current = (await EnvelopeAsync(mount, Route, "as_observed", new { identifier, date = actDate, snapshot = mountedSha256 })).Result!.Value;
        Assert.AreEqual(LuxembourgIndexBuilderTests.LaterText, current.GetProperty("snapshot").GetProperty("observed_no_later_than").GetString());
        Assert.IsTrue(current.GetProperty("snapshot").GetProperty("mounted").GetBoolean());
        var held = current.GetProperty("states").EnumerateArray().Single();
        Assert.IsTrue(held.GetProperty("text_held").GetBoolean());
        Assert.AreEqual(actDate, held.GetProperty("applicability_date").GetString());
        Assert.IsTrue(held.GetProperty("articles").GetArrayLength() > 0, "the mounted state is served in full, as as_of serves it");
        var asOf = (await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier, date = actDate })).Result!.Value.GetProperty("states")[0];
        Assert.AreEqual(asOf.GetProperty("permalink").GetString(), held.GetProperty("permalink").GetString(), "the same state as as_of on the mounted snapshot");

        // Between the two dates the log still holds the earlier state (absence is not a withdrawal), which as_of cannot
        // answer from the mounted states; at the mounted snapshot its next date is the act's.
        var earlier = (await EnvelopeAsync(mount, Route, "as_observed", new { identifier, date = "2010-06-15", snapshot = mountedSha256 })).Result!.Value
            .GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(EarlierDate, earlier.GetProperty("applicability_date").GetString());
        Assert.AreEqual(actDate, earlier.GetProperty("next_applicability_date").GetString());
        Assert.IsFalse(earlier.GetProperty("text_held").GetBoolean());
    }

    [TestMethod]
    public async Task ATimeOrAnUnknownSnapshotRefusesSnapshotUnknownAndADateBeforeTheHistoryNoVersion()
    {
        await using var fixture = await ChainedMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var byTime = await EnvelopeAsync(mount, Route, "as_observed", new { identifier, date = "2024-02-01", at = "2026-10-01T08:30:00Z" });
        Assert.AreEqual("snapshot_unknown", byTime.Refusal?.Code);
        Assert.AreEqual("2026-10-01T08:30:00Z", byTime.Refusal!.HelpfulPayload.GetProperty("snapshot_id").GetString());
        StringAssert.Contains(byTime.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(), "no time can be placed in a snapshot without guessing");

        var foreign = await EnvelopeAsync(mount, Route, "as_observed", new { identifier, date = "2024-02-01", snapshot = new string('f', 64) });
        Assert.AreEqual("snapshot_unknown", foreign.Refusal?.Code);

        var before = await EnvelopeAsync(mount, Route, "as_observed", new { identifier, date = "1999-12-31", snapshot = fixture.PredecessorSha256 });
        Assert.AreEqual("no_version_for_date", before.Refusal?.Code);
        Assert.AreEqual(EarlierDate, before.Refusal!.HelpfulPayload.GetProperty("history_begins").GetString(), "the history the snapshot held");

        var language = await EnvelopeAsync(mount, Route, "as_observed", new { identifier, date = "2024-02-01", snapshot = fixture.PredecessorSha256, language = "deu" });
        Assert.AreEqual("language_not_available", language.Refusal?.Code);

        foreach (var parameters in new object[]
                 {
                     new { identifier, date = "2024-02-01" },
                     new { identifier, date = "2024-02-01", snapshot = fixture.PredecessorSha256, at = "2026-10-01T08:30:00Z" },
                     new { identifier, date = "2024-02-01", snapshot = "not-a-digest" },
                 })
        {
            var context = await PostAsync(mount, Route, JsonSerializer.Serialize(new { operation_id = "as_observed", parameters }));
            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode, JsonSerializer.Serialize(parameters));
        }
    }
}
