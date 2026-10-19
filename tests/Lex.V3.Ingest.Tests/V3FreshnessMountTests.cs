using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The freshness header and the commits every answer names (the specification of record's R87 and R23; the plan's items 6.1
/// and 6.5), read from the mount alone: the index's build time from its log, and, from the build report beside the mount once
/// it is held to the mounted artifacts, each channel's newest completed acquisition and the commit the index was built from.
/// Over the committed schema-8 mount (a real build's index, its log's one build) and the EU fixture mount.
/// </summary>
[TestClass]
public sealed class V3FreshnessMountTests
{
    private static readonly DateTimeOffset ObservedAt = LegacyMountAnswers.ObservedAt;
    private const string BuilderCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string ServingCommit = "89abcdef0123456789abcdef0123456789abcdef";

    /// <summary>The committed schema-8 mount in a directory of its own, with <paramref name="report"/> beside it when one is given.</summary>
    private static async Task<string> Schema8MountAsync(string? report)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-freshness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), V3CorpusLegacySchema8MountTests.Fixture("lex-corpus-6.json.bin"));
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.IndexFileName), V3CorpusLegacySchema8MountTests.Fixture("luxembourg-index.sqlite3.bin"));
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), V3CorpusLegacySchema8MountTests.Fixture("luxembourg-capability-manifest.json.bin"));
        if (report is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, V3CorpusMount.BuildReportFileName), report);
        }

        return directory;
    }

    /// <summary>The schema-8 fixture corpus's artifact digest, the one a build report names as its corpus.</summary>
    private static string CorpusSha256 =>
        VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(V3CorpusLegacySchema8MountTests.Fixture("lex-corpus-6.json.bin")).ArtifactRef.Sha256;

    /// <summary>A report in the shape the mount writer writes (V3CorpusMountWriter.RenderReport), with what the test names.</summary>
    private static string Report(
        string? corpus = null, string? luxembourgIndex = null, string? europeIndex = null,
        string? luxembourgCatalogedAt = null, string? europeCatalogedAt = null, string? indexBuilderSource = null, string? toolSource = null)
    {
        var report = new Dictionary<string, object?>
        {
            ["schema"] = "lex-v3-first-mount-report/1",
            ["corpus"] = corpus is null ? null : new { resourceId = "urn:uuid:00000000-0000-0000-0000-000000000000", Sha256 = corpus },
            ["luxembourgIndex"] = luxembourgIndex is null ? null : new { resourceId = "urn:uuid:00000000-0000-0000-0000-000000000001", Sha256 = luxembourgIndex },
        };
        if (europeIndex is not null) report["europeIndex"] = new { resourceId = "urn:uuid:00000000-0000-0000-0000-000000000002", Sha256 = europeIndex };
        if (indexBuilderSource is not null) report["indexBuilderSource"] = indexBuilderSource;
        if (luxembourgCatalogedAt is not null || europeCatalogedAt is not null)
        {
            report["resumption"] = new
            {
                statement = "This population was not observed in one window.",
                europe = europeCatalogedAt is null ? null : new { resumedAt = "2026-10-01T00:00:00+00:00", catalogedAt = europeCatalogedAt },
                luxembourg = luxembourgCatalogedAt is null ? null : new { resumedAt = "2026-10-01T00:00:00+00:00", catalogedAt = luxembourgCatalogedAt },
            };
        }

        if (toolSource is not null) report["derivation"] = new { toolSource };
        return JsonSerializer.Serialize(report);
    }

    private static async Task<JsonElement> ContextOfAsync(V3CorpusMount? mount, string operation, object parameters, string? servingCommit = null)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext { TraceIdentifier = "freshness" };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(servingCommit), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        var sent = ((MemoryStream)context.Response.Body).ToArray();
        _ = V3EnvelopeJson.ParseAndVerify(sent, V3OperationRegistry.Reviewed);
        using var document = JsonDocument.Parse(sent);
        return document.RootElement.GetProperty("context").Clone();
    }

    private static void Delete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task WithoutABuildReportAnAnswerNamesTheIndexBuildTimeAndNoCompletionOrCommit()
    {
        var directory = await Schema8MountAsync(null);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            var context = await ContextOfAsync(mount, "coverage", new { });
            var freshness = context.GetProperty("freshness");

            // The build time is the mounted log's, as coverage's history names the mounted build's.
            using var coverage = JsonDocument.Parse((await LegacyMountAnswers.RespondAsync(mount!, "coverage", "{\"operation_id\":\"coverage\",\"parameters\":{}}", "c")).Body);
            var mounted = coverage.RootElement.GetProperty("result").GetProperty("value").GetProperty("history").GetProperty("snapshots_with_text")
                .EnumerateArray().Single(row => row.GetProperty("retained_as").EnumerateArray().Any(reason => reason.GetString() == "mounted"));
            Assert.AreEqual(mounted.GetProperty("built_at").GetString(), freshness.GetProperty("index_built_at").GetString());

            var channel = freshness.GetProperty("channels").EnumerateArray().Single();
            Assert.AreEqual("lu-legilux", channel.GetProperty("publisher").GetString());
            Assert.AreEqual(JsonValueKind.Null, channel.GetProperty("newest_completed_observation").ValueKind, "no report states a completion");
            Assert.AreEqual(JsonValueKind.Null, channel.GetProperty("age_days").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, channel.GetProperty("stale").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, channel.GetProperty("lag_days").ValueKind, "lag not yet measured");
            Assert.AreEqual(0, channel.GetProperty("lag_observations").GetInt32());
            Assert.IsFalse(freshness.GetProperty("stale").GetBoolean());
            Assert.AreEqual(V3Freshness.MaximumDataAgeDays, freshness.GetProperty("maximum_data_age_days").GetInt32());
            Assert.AreEqual(JsonValueKind.Null, freshness.GetProperty("last_checked_against_publisher").ValueKind, "no monthly re-check has run (R69)");

            var commits = context.GetProperty("commits");
            Assert.AreEqual(JsonValueKind.Null, commits.GetProperty("index_builder_source_commit").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, commits.GetProperty("serving_runtime_source_commit").ValueKind, "a test's host names no commit");
        }
        finally
        {
            Delete(directory);
        }
    }

    [TestMethod]
    public async Task ABoundReportDatesTheChannelAndNamesTheIndexBuilderInEveryAnswer()
    {
        // Twelve days and a second before the answer: past the maximum. Read down to the whole second.
        var directory = await Schema8MountAsync(Report(
            CorpusSha256, V3CorpusLegacySchema8MountTests.IndexFileSha256,
            luxembourgCatalogedAt: "2026-10-03T06:59:59.9876543+00:00", indexBuilderSource: BuilderCommit));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            foreach (var (operation, parameters) in new (string, object)[]
                     {
                         ("coverage", new { }),
                         ("resolve", new { identifier = "/lu-legilux/loi-2026-01-01-a1" }),
                         ("resolve", new { identifier = "/lu-legilux/no-such-work" }),
                     })
            {
                var context = await ContextOfAsync(mount, operation, parameters, ServingCommit);
                var freshness = context.GetProperty("freshness");
                var channel = freshness.GetProperty("channels").EnumerateArray().Single();
                Assert.AreEqual("2026-10-03T06:59:59Z", channel.GetProperty("newest_completed_observation").GetString(), operation);
                Assert.AreEqual(12, channel.GetProperty("age_days").GetInt32());
                Assert.IsTrue(channel.GetProperty("stale").GetBoolean());
                Assert.IsTrue(freshness.GetProperty("stale").GetBoolean(), "past 10 days, the answer is stale");
                Assert.AreEqual(BuilderCommit, context.GetProperty("commits").GetProperty("index_builder_source_commit").GetString());
                Assert.AreEqual(ServingCommit, context.GetProperty("commits").GetProperty("serving_runtime_source_commit").GetString());
            }

            Assert.AreEqual(BuilderCommit, mount.Freshness.IndexBuilderSourceCommit);
        }
        finally
        {
            Delete(directory);
        }
    }

    [TestMethod]
    public async Task AReDerivedMountsToolSourceStandsForTheBuilderAndAnUnversionedBuildForNone()
    {
        foreach (var (report, expected) in new[]
                 {
                     (Report(CorpusSha256, V3CorpusLegacySchema8MountTests.IndexFileSha256, toolSource: BuilderCommit), BuilderCommit),
                     (Report(CorpusSha256, V3CorpusLegacySchema8MountTests.IndexFileSha256, indexBuilderSource: "unversioned"), null),
                     (Report(CorpusSha256, V3CorpusLegacySchema8MountTests.IndexFileSha256, indexBuilderSource: BuilderCommit[..12]), null),
                 })
        {
            var directory = await Schema8MountAsync(report);
            try
            {
                using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
                Assert.AreEqual(expected, mount!.Freshness.IndexBuilderSourceCommit, report);
                Assert.IsNull(mount.Freshness.Channels.Single().NewestCompletedObservation, "a half acquired in one window is not dated");
            }
            finally
            {
                Delete(directory);
            }
        }
    }

    [TestMethod]
    public async Task AReportThatNamesNoCorpusStatesNothingAndOneThatNamesAnotherArtifactRefusesTheMount()
    {
        // "{}", as a test fixture writes to say a mount is built: nothing is read from it.
        var empty = await Schema8MountAsync("{}");
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(empty, CancellationToken.None);
            Assert.IsNull(mount!.Freshness.IndexBuilderSourceCommit);
            Assert.IsNull(mount.Freshness.Channels.Single().NewestCompletedObservation);
        }
        finally
        {
            Delete(empty);
        }

        var other = new string('e', 64);
        foreach (var report in new[]
                 {
                     Report(other, V3CorpusLegacySchema8MountTests.IndexFileSha256, luxembourgCatalogedAt: "2026-10-12T00:00:00+00:00"),
                     Report(CorpusSha256, other, luxembourgCatalogedAt: "2026-10-12T00:00:00+00:00"),
                     Report(CorpusSha256, V3CorpusLegacySchema8MountTests.IndexFileSha256, europeIndex: other),
                     Report(CorpusSha256, V3CorpusLegacySchema8MountTests.IndexFileSha256, luxembourgCatalogedAt: "not a time"),
                     "[]",
                     "{not json",
                 })
        {
            var directory = await Schema8MountAsync(report);
            try
            {
                await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(directory, CancellationToken.None), report);
            }
            finally
            {
                Delete(directory);
            }
        }
    }

    [TestMethod]
    public void TheReportIsReadDownToTheSecondAndOnlyForTheHalvesTheMountHolds()
    {
        var corpus = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(new string('a', 64)), new string('a', 64));
        var luxembourg = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(new string('b', 64)), new string('b', 64));
        var europe = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(new string('c', 64)), new string('c', 64));
        var report = Encoding.UTF8.GetBytes(Report(
            corpus.Sha256, luxembourg.Sha256, europe.Sha256,
            luxembourgCatalogedAt: "2026-10-12T18:17:04.765317+00:00", europeCatalogedAt: "2026-10-05T05:50:08.2151584+02:00",
            indexBuilderSource: BuilderCommit));

        var both = V3CorpusMount.ReadBuildReport(report, corpus, luxembourg, europe);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-12T18:17:04Z"), both.LuxembourgCompletedAt);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-05T03:50:08Z"), both.EuropeCompletedAt, "read in UTC");
        Assert.AreEqual(BuilderCommit, both.IndexBuilderSourceCommit);

        // A report naming an EU index the mount does not hold describes another mount.
        Assert.ThrowsExactly<InvalidDataException>(() => V3CorpusMount.ReadBuildReport(report, corpus, luxembourg, null));
    }

    /// <summary>
    /// A real mount's build report read as the server reads it, held to the digests the report names, which the measure
    /// checks against the mounted files' own (only when <c>V3_FRESHNESS_MOUNT</c> names a mount directory; the population's
    /// report is not committed). Prints what every answer over that mount would name.
    /// </summary>
    [TestMethod]
    public void ARealMountsBuildReportIsReadAsTheServerReadsIt()
    {
        var directory = Environment.GetEnvironmentVariable("V3_FRESHNESS_MOUNT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive("V3_FRESHNESS_MOUNT names no mount, so no real build report is read.");
        }

        var bytes = File.ReadAllBytes(Path.Combine(directory, V3CorpusMount.BuildReportFileName));
        using var report = JsonDocument.Parse(bytes);
        SourceArtifactRef? Named(string member) =>
            report.RootElement.TryGetProperty(member, out var named) && named.ValueKind == JsonValueKind.Object
                ? new SourceArtifactRef(named.GetProperty("resourceId").GetString()!, named.GetProperty("Sha256").GetString()!)
                : null;
        var facts = V3CorpusMount.ReadBuildReport(bytes, Named("corpus")!, Named("luxembourgIndex"), Named("europeIndex"));
        Console.WriteLine($"luxembourg completed at {facts.LuxembourgCompletedAt:O}; europe completed at {facts.EuropeCompletedAt:O}; index built from {facts.IndexBuilderSourceCommit ?? "(not recorded)"}");
    }

    [TestMethod]
    public async Task AnEuMountNamesItsChannelAndAnUnmountedServerNone()
    {
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = europe;
        using var mount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        var context = await ContextOfAsync(mount, "dossier", new { identifier = "32016R0679" }, ServingCommit);
        var channel = context.GetProperty("freshness").GetProperty("channels").EnumerateArray().Single();
        Assert.AreEqual("eu-eurlex", channel.GetProperty("publisher").GetString());
        Assert.AreEqual(JsonValueKind.Null, channel.GetProperty("newest_completed_observation").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, context.GetProperty("freshness").GetProperty("index_built_at").ValueKind, "an EU index records no build time");

        // With no corpus mounted the refusal names no channel and no index, and still the service's commit.
        var unmounted = await ContextOfAsync(null, "coverage", new { }, ServingCommit);
        Assert.AreEqual(0, unmounted.GetProperty("freshness").GetProperty("channels").GetArrayLength());
        Assert.AreEqual(ServingCommit, unmounted.GetProperty("commits").GetProperty("serving_runtime_source_commit").GetString());
        Assert.AreEqual(JsonValueKind.Null, unmounted.GetProperty("commits").GetProperty("index_builder_source_commit").ValueKind);
    }
}
