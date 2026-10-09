using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Replay G1, G3 and G4 over a chain of mounts in build order, taking nothing from how the chain was built: the fixture
/// chain of <see cref="V3ReplayGuaranteesTests"/> on every run, and the chain <c>V3_REPLAY_CHAIN</c> names when it names
/// one (mount directories separated by <c>;</c>, earliest first), such as the real chained canary builds. On a real chain
/// the checks find what the publisher's data holds, and the counts say how much each check covered.
/// <list type="bullet">
/// <item>G1: every <c>file_replaced</c> in the latest log names the version it replaced, which still verifies on the
/// latest mount. When the text changed, the answer names a version at the same coordinate that replaced it.</item>
/// <item>G3: each earlier build's events are the latest log's prefix, row for row. Every state the log names, held or
/// replaced, verifies on the latest mount.</item>
/// <item>G4: for every Luxembourg state each build's own mount holds (its <c>timeline</c> of every work its events name),
/// <c>as_observed</c> at that build's snapshot, asked of the latest mount, answers the state that build's mount answers,
/// field for field, bounded by that build's time.</item>
/// </list>
/// The chain must keep every build's text beside its latest mount (no generation dropped by the retention line), which
/// the checks assert first. When <c>V3_REPLAY_OUT</c> names a file, the counts are written there as evidence.
/// </summary>
[TestClass]
public sealed class V3ReplayOverChainTests
{
    private const string ChainVariable = "V3_REPLAY_CHAIN";
    private const string OutVariable = "V3_REPLAY_OUT";

    /// <summary>What each check covered over one chain.</summary>
    internal sealed record ReplayCounts(
        int Builds,
        int[] EventsPerBuild,
        int FileReplaced,
        int FileReplacedWithNewVersion,
        int StatesNamed,
        int SnapshotsCompared);

    [TestMethod]
    public async Task TheReplayChecksHoldOverTheFixtureChain()
    {
        using var chain = await V3ReplayGuaranteesTests.ChainAsync();
        var counts = await ReplayAsync(chain.Mounts);
        Assert.AreEqual(3, counts.Builds);
        Assert.AreEqual(2, counts.FileReplaced, "the second and third builds each replaced the file");
        Assert.AreEqual(1, counts.FileReplacedWithNewVersion, "only the third changed its text");
        Assert.AreEqual(2, counts.StatesNamed, "the original version and the reworded one");
        Assert.AreEqual(3, counts.SnapshotsCompared, "one state in each build");
        Assert.IsTrue(counts.EventsPerBuild.Zip(counts.EventsPerBuild.Skip(1)).All(static pair => pair.First < pair.Second), "each build appends");
    }

    [TestMethod]
    public async Task TheReplayChecksHoldOverTheChainTheEnvironmentNames()
    {
        var named = Environment.GetEnvironmentVariable(ChainVariable);
        if (string.IsNullOrWhiteSpace(named))
        {
            Assert.Inconclusive($"{ChainVariable} names no chain.");
        }

        var mounts = named.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.IsGreaterThanOrEqualTo(2, mounts.Length, "a chain is at least two builds");
        var counts = await ReplayAsync(mounts);
        Assert.IsGreaterThan(0, counts.SnapshotsCompared, "the chain holds at least one Luxembourg state");
        if (Environment.GetEnvironmentVariable(OutVariable) is { Length: > 0 } output)
        {
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { chain = mounts, counts }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task<ReplayCounts> ReplayAsync(IReadOnlyList<string> directories)
    {
        using var latest = await OpenAsync(directories[^1]);
        var history = (await ResultAsync(latest, "coverage", new { })).GetProperty("history");
        Assert.AreEqual(directories.Count, history.GetProperty("snapshots_in_log").GetInt32(), "the latest log records one build for each mount of the chain");
        Assert.AreEqual(0, history.GetProperty("snapshots_without_text").GetInt32(), "every build of the chain keeps its text beside the latest mount");

        var log = (await ResultAsync(latest, "events", new { limit = 1 })).GetProperty("log");
        var snapshots = log.GetProperty("ancestors").EnumerateArray().Select(static ancestor => ancestor.GetProperty("log_id").GetString()!)
            .Append(log.GetProperty("log_id").GetString()!).ToArray();
        var builtAts = log.GetProperty("ancestors").EnumerateArray().Select(static ancestor => ancestor.GetProperty("built_at").GetString()!)
            .Append(log.GetProperty("built_at").GetString()!).ToArray();
        var latestEvents = await EventsAsync(latest);

        // G3: each earlier build's events are the latest log's prefix, row for row; only the cursor differs, since it names
        // the log it was read from.
        var eventsPerBuild = new int[directories.Count];
        eventsPerBuild[^1] = latestEvents.Length;
        for (var build = 0; build < directories.Count - 1; build++)
        {
            using var earlier = await OpenAsync(directories[build]);
            var rows = await EventsAsync(earlier);
            eventsPerBuild[build] = rows.Length;
            Assert.IsLessThanOrEqualTo(latestEvents.Length, rows.Length, $"build {build + 1}: the latest log holds every event it held");
            foreach (var (row, carried) in rows.Zip(latestEvents))
            {
                Assert.AreEqual(WithoutCursor(row), WithoutCursor(carried), $"build {build + 1}: event {row.GetProperty("seq").GetInt64()} is carried unchanged");
            }
        }

        // G3: every state the log names, held or replaced, verifies on the latest mount.
        var named = latestEvents.Where(static row => row.GetProperty("state_sha256").ValueKind == JsonValueKind.String)
            .Select(static row => row.GetProperty("permalink").GetString()!)
            .Concat(latestEvents.Select(static row => row.GetProperty("replaced_permalink")).Where(static value => value.ValueKind == JsonValueKind.String).Select(static value => value.GetString()!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var permalink in named)
        {
            Assert.AreEqual("digest_matches", (await ResultAsync(latest, "verify", new { identifier = permalink })).GetProperty("verdict").GetString(), permalink);
        }

        // G1: every replaced file names the version it replaced, which still verifies; a changed text names its replacement
        // at the same coordinate.
        var replaced = latestEvents.Where(static row => row.GetProperty("event").GetString() == "file_replaced").ToArray();
        var withNewVersion = 0;
        foreach (var row in replaced)
        {
            var before = row.GetProperty("replaced_permalink").GetString()!;
            var after = row.GetProperty("permalink").GetString()!;
            CollectionAssert.AreNotEqual(
                row.GetProperty("detail").GetProperty("replaced_source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray(),
                row.GetProperty("detail").GetProperty("source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray(),
                $"{after}: a replaced file's bodies differ");
            var verified = await ResultAsync(latest, "verify", new { identifier = before });
            Assert.AreEqual("digest_matches", verified.GetProperty("verdict").GetString(), before);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                withNewVersion++;
                var coordinate = row.GetProperty("stable_coordinate").GetString()!;
                StringAssert.StartsWith(verified.GetProperty("superseded_by").GetProperty("permalink").GetString(), coordinate + "--",
                    $"{before}: names the version that replaced it at {coordinate}");
            }
        }

        // G4: every state each build's own mount holds, asked of the latest mount at that build's snapshot.
        var compared = 0;
        for (var build = 0; build < directories.Count; build++)
        {
            using var then = build == directories.Count - 1 ? null : await OpenAsync(directories[build]);
            var mount = then ?? latest;
            var works = (await EventsAsync(mount)).Where(static row => row.GetProperty("work_key").ValueKind == JsonValueKind.String)
                .Select(static row => row.GetProperty("work_key").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var work in works)
            {
                var timeline = await EnvelopeAsync(mount, "/api/v3/timeline", "timeline", new { identifier = $"/lu-legilux/{work}" });
                if (timeline.Refusal is not null)
                {
                    // A work this build's log names but its mount no longer holds (absence is not a withdrawal).
                    continue;
                }

                foreach (var state in timeline.Result!.Value.GetProperty("states").EnumerateArray())
                {
                    var observed = await ResultAsync(latest, "as_observed", new
                    {
                        identifier = $"/lu-legilux/{work}",
                        date = state.GetProperty("applicability_date").GetString(),
                        language = state.GetProperty("language").GetString(),
                        snapshot = snapshots[build],
                    });
                    var snapshot = observed.GetProperty("snapshot");
                    Assert.AreEqual(build + 1, snapshot.GetProperty("observation").GetInt64());
                    Assert.AreEqual(builtAts[build], snapshot.GetProperty("observed_no_later_than").GetString(), $"build {build + 1}: bounded by its build time");
                    var answered = observed.GetProperty("states").EnumerateArray().Single();
                    Assert.IsTrue(answered.GetProperty("text_held").GetBoolean(), $"build {build + 1}, {work}: its state is kept in full");
                    Assert.AreEqual(JsonNode.Parse(state.GetRawText())!.ToJsonString(), WithoutTextSource(answered),
                        $"build {build + 1}, {work} at {state.GetProperty("applicability_date").GetString()}: the state that build's mount answers");
                    compared++;
                }
            }
        }

        return new ReplayCounts(directories.Count, eventsPerBuild, replaced.Length, withNewVersion, named.Length, compared);
    }

    private static async Task<V3CorpusMount> OpenAsync(string directory)
    {
        var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
        Assert.IsNotNull(mount, directory);
        return mount;
    }

    private static async Task<JsonElement> ResultAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters);
        Assert.IsNull(envelope.Refusal, $"{operation}: {envelope.Refusal?.Code}");
        return envelope.Result!.Value.Clone();
    }

    /// <summary>Every event a mount's log holds, read page by page.</summary>
    private static async Task<JsonElement[]> EventsAsync(V3CorpusMount mount)
    {
        var rows = new List<JsonElement>();
        string? after = null;
        while (true)
        {
            var page = after is null
                ? await ResultAsync(mount, "events", new { limit = 200 })
                : await ResultAsync(mount, "events", new { limit = 200, after });
            rows.AddRange(page.GetProperty("events").EnumerateArray());
            if (!page.GetProperty("has_more").GetBoolean())
            {
                return [.. rows];
            }

            after = page.GetProperty("next_after").GetString();
        }
    }

    private static string WithoutCursor(JsonElement row)
    {
        var node = JsonNode.Parse(row.GetRawText())!.AsObject();
        node.Remove("cursor");
        return node.ToJsonString();
    }

    /// <summary>An <c>as_observed</c> state without what says where its text was read from, which a mount's own answers do not carry.</summary>
    private static string WithoutTextSource(JsonElement state)
    {
        var node = JsonNode.Parse(state.GetRawText())!.AsObject();
        node.Remove("text_held");
        node.Remove("text_from");
        return node.ToJsonString();
    }
}
