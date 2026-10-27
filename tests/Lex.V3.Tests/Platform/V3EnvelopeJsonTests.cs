using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3EnvelopeJsonTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string RegistryDigest = "627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059";
    private const string BuilderCommit = "1111111111111111111111111111111111111111";
    private const string ServingCommit = "2222222222222222222222222222222222222222";
    private const string ExpectedSuccess = """{"context":{"commits":{"index_builder_source_commit":"1111111111111111111111111111111111111111","serving_runtime_source_commit":"2222222222222222222222222222222222222222"},"freshness":{"channels":[{"age_days":6,"lag_days":null,"lag_observations":0,"newest_completed_observation":"2026-09-10T08:00:00Z","publisher":"lu-legilux","stale":false},{"age_days":37,"lag_days":null,"lag_observations":0,"newest_completed_observation":"2026-08-11T00:00:00Z","publisher":"eu-eurlex","stale":true}],"index_built_at":"2026-09-10T08:00:01Z","last_checked_against_publisher":null,"maximum_data_age_days":30,"observed_at":"2026-09-17T00:00:00.0000000Z","stale":true,"upstream_health":"current"},"jurisdiction":"lu","provisional":false,"publisher":"lu-legilux","snapshot":{"snapshot_id":"snapshot","snapshot_sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"},"status":"success","timeline_semantics":"publisher_applicability"},"object_type":"envelope","operation_id":"resolve","refusal":null,"registry_schema":"lex-v3-operation-registry/2","registry_sha256":"627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059","request_ref":"req","result":{"object_type":"work_resolution","schema":"lex-v3-resolve-result/1","value":{"work_id":"lu-legilux:arr\u00EAt\u00E9 \u0026 co"}},"schema":"lex-v3-envelope/1","verdict":"answer","version":"v3"}""" + "\n";
    private const string ExpectedRefusal = """{"context":{"commits":{"index_builder_source_commit":null,"serving_runtime_source_commit":null},"freshness":{"channels":[],"index_built_at":null,"last_checked_against_publisher":null,"maximum_data_age_days":30,"observed_at":"2026-09-17T00:00:00.0000000Z","stale":false,"upstream_health":"current"},"jurisdiction":"lu","provisional":false,"publisher":"lu-legilux","snapshot":{"snapshot_id":"snapshot","snapshot_sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"},"status":"refusal","timeline_semantics":"publisher_applicability"},"object_type":"envelope","operation_id":"resolve","refusal":{"code":"no_corpus_mounted","helpful_payload":{"required_corpus":"lu"},"schema":"lex-v3-refusal/1"},"registry_schema":"lex-v3-operation-registry/2","registry_sha256":"627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059","request_ref":"req","result":null,"schema":"lex-v3-envelope/1","verdict":"refuse","version":"v3"}""" + "\n";
    private readonly V3OperationRegistry _registry = V3OperationRegistry.Reviewed;

    [TestMethod]
    public void RestAndMcpAreByteIdenticalAndRoundTripStrictly()
    {
        var envelope = Success();
        var rest = V3EnvelopeJson.ProjectRest(envelope, _registry);
        var mcp = V3EnvelopeJson.ProjectMcp(envelope, _registry);

        CollectionAssert.AreEqual(rest, mcp);
        var reopened = V3EnvelopeJson.ParseAndVerify(rest, _registry);
        Assert.AreEqual(envelope.OperationId, reopened.OperationId);
        CollectionAssert.AreEqual(rest, V3EnvelopeJson.ProjectRest(reopened, _registry));
    }

    [TestMethod]
    public void SuccessAndRefusalHaveLiteralCanonicalWireForms()
    {
        Assert.AreEqual(RegistryDigest, _registry.Sha256);
        Assert.AreEqual(ExpectedSuccess, Encoding.UTF8.GetString(V3EnvelopeJson.ProjectRest(Success(), _registry)));
        Assert.AreEqual(ExpectedRefusal, Encoding.UTF8.GetString(V3EnvelopeJson.ProjectMcp(Refusal(), _registry)));
    }

    [TestMethod]
    public void PayloadMemberOrderDoesNotChangeCanonicalProjection()
    {
        using var left = JsonDocument.Parse("{\"z\":1,\"a\":{\"y\":2,\"b\":3}}");
        using var right = JsonDocument.Parse("{\"a\":{\"b\":3,\"y\":2},\"z\":1}");
        var builder = new V3EnvelopeBuilder(_registry);

        CollectionAssert.AreEqual(
            V3EnvelopeJson.ProjectRest(builder.Success(
                "req", "resolve", Context(), V3Verdicts.Answer,
                "lex-v3-resolve-result/1", "work_resolution", left.RootElement), _registry),
            V3EnvelopeJson.ProjectMcp(builder.Success(
                "req", "resolve", Context(), V3Verdicts.Answer,
                "lex-v3-resolve-result/1", "work_resolution", right.RootElement), _registry));
    }

    [TestMethod]
    public void ReaderRejectsNonCanonicalOrBindingDrift()
    {
        var canonical = V3EnvelopeJson.ProjectRest(Success(), _registry);
        var nonCanonical = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("\"context\":", " \"context\":", StringComparison.Ordinal));
        Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(nonCanonical, _registry));

        var node = JsonNode.Parse(canonical)!.AsObject();
        node["registry_sha256"] = Digest;
        var drift = Encoding.UTF8.GetBytes(node.ToJsonString() + "\n");
        Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(drift, _registry));
    }

    [TestMethod]
    public void ReaderRejectsDuplicateMembers()
    {
        var duplicate = ExpectedSuccess.Replace(
            "\"schema\":\"lex-v3-envelope/1\"",
            "\"schema\":\"lex-v3-envelope/1\",\"schema\":\"lex-v3-envelope/1\"",
            StringComparison.Ordinal);

        var exception = Assert.ThrowsExactly<JsonException>(() =>
            V3EnvelopeJson.ParseAndVerify(Encoding.UTF8.GetBytes(duplicate), _registry));
        StringAssert.Contains(exception.Message, "Duplicate");
    }

    [TestMethod]
    public void UnknownProjectionFailsClosed()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            V3EnvelopeJson.Project(Success(), _registry, (V3EnvelopeProjectionKind)0));
    }

    private V3Envelope Success()
    {
        using var result = JsonDocument.Parse("{\"work_id\":\"lu-legilux:arrêté & co\"}");
        return new V3EnvelopeBuilder(_registry).Success(
            "req", "resolve", MountedContext(), V3Verdicts.Answer,
            "lex-v3-resolve-result/1", "work_resolution", result.RootElement);
    }

    /// <summary>
    /// A context with the whole freshness header (R87) and both commits (R23): a Luxembourg channel 6 days old at the answer
    /// and an EU one 37 days old, past the maximum of 30, so the answer is stale.
    /// </summary>
    private static V3EnvelopeContext MountedContext() => new(
        PublisherId.LuLegilux,
        "success",
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(
            DateTimeOffset.Parse("2026-09-17T00:00:00Z"),
            "current",
            DateTimeOffset.Parse("2026-09-10T08:00:01Z"),
            [
                new V3FreshnessChannel(PublisherId.LuLegilux, DateTimeOffset.Parse("2026-09-10T08:00:00Z")),
                new V3FreshnessChannel(PublisherId.EuEurLex, DateTimeOffset.Parse("2026-08-11T00:00:00Z")),
            ]),
        new V3Commits(BuilderCommit, ServingCommit));

    [TestMethod]
    public void TheFreshnessHeaderCountsEachChannelsAgeAndStalenessAtTheAnswer()
    {
        var freshness = MountedContext().Freshness;
        Assert.AreEqual(6, freshness.AgeDays(freshness.Channels[0]));
        Assert.AreEqual(false, freshness.IsStale(freshness.Channels[0]));
        Assert.AreEqual(37, freshness.AgeDays(freshness.Channels[1]));
        Assert.AreEqual(true, freshness.IsStale(freshness.Channels[1]));
        Assert.IsTrue(freshness.Stale);

        // The maximum is the owner's answer to O11 of 2026-10-19, 30 days (option A): exactly the maximum is not past it; one
        // second more is (R87: past the maximum).
        var completed = DateTimeOffset.Parse("2026-09-07T00:00:00Z");
        var atMaximum = new V3Freshness(completed.AddDays(30), "stale", null, [new V3FreshnessChannel(PublisherId.EuEurLex, completed)]);
        Assert.AreEqual(30, atMaximum.AgeDays(atMaximum.Channels[0]));
        Assert.IsFalse(atMaximum.Stale);
        var past = new V3Freshness(completed.AddDays(30).AddSeconds(1), "stale", null, [new V3FreshnessChannel(PublisherId.EuEurLex, completed)]);
        Assert.IsTrue(past.Stale);
        // The 10 days held before the owner's answer are no longer stale.
        var tenDays = new V3Freshness(completed.AddDays(10).AddSeconds(1), "stale", null, [new V3FreshnessChannel(PublisherId.EuEurLex, completed)]);
        Assert.IsFalse(tenDays.Stale);

        // A channel whose completion nothing states has no age and is never said to be fresh or stale.
        var unstated = new V3Freshness(completed, "stale", null, [new V3FreshnessChannel(PublisherId.LuLegilux, null)]);
        Assert.IsNull(unstated.AgeDays(unstated.Channels[0]));
        Assert.IsNull(unstated.IsStale(unstated.Channels[0]));
        Assert.IsFalse(unstated.Stale);
        Assert.AreEqual(0, unstated.Channels[0].LagObservations);

        // A completion after the answer's instant is age 0.
        var ahead = new V3Freshness(completed, "stale", null, [new V3FreshnessChannel(PublisherId.LuLegilux, completed.AddHours(2))]);
        Assert.AreEqual(0, ahead.AgeDays(ahead.Channels[0]));
    }

    [TestMethod]
    public void TheFreshnessAndCommitsHoldTheirShapes()
    {
        var observed = DateTimeOffset.Parse("2026-09-17T00:00:00Z");
        // Each channel once, Luxembourg first.
        Assert.ThrowsExactly<ArgumentException>(() => new V3Freshness(observed, "stale", null,
            [new V3FreshnessChannel(PublisherId.EuEurLex, null), new V3FreshnessChannel(PublisherId.LuLegilux, null)]));
        Assert.ThrowsExactly<ArgumentException>(() => new V3Freshness(observed, "stale", null,
            [new V3FreshnessChannel(PublisherId.LuLegilux, null), new V3FreshnessChannel(PublisherId.LuLegilux, null)]));
        // Instants in whole seconds, as a build's time is written.
        Assert.ThrowsExactly<ArgumentException>(() => new V3FreshnessChannel(PublisherId.LuLegilux, observed.AddMilliseconds(5)));
        Assert.ThrowsExactly<ArgumentException>(() => new V3Freshness(observed, "stale", observed.AddMilliseconds(5), []));
        // A commit is a full lowercase commit, or nothing.
        Assert.ThrowsExactly<ArgumentException>(() => new V3Commits("abc1234", null));
        Assert.ThrowsExactly<ArgumentException>(() => new V3Commits(null, new string('A', 40)));
        Assert.IsNull(V3Commits.None.IndexBuilderSourceCommit);
        Assert.IsTrue(V3Commits.IsCommit(BuilderCommit));
        Assert.IsFalse(V3Commits.IsCommit("unversioned"));
    }

    [TestMethod]
    public void TheServingRuntimeCommitIsTheAnsweringServersOwn()
    {
        var context = MountedContext().WithServingRuntimeSourceCommit(null);
        Assert.AreEqual(BuilderCommit, context.Commits.IndexBuilderSourceCommit);
        Assert.IsNull(context.Commits.ServingRuntimeSourceCommit);
        var stamped = context.WithServingRuntimeSourceCommit(ServingCommit);
        Assert.AreEqual(ServingCommit, stamped.Commits.ServingRuntimeSourceCommit);
        Assert.AreEqual(BuilderCommit, stamped.Commits.IndexBuilderSourceCommit);
        Assert.AreSame(stamped, stamped.WithServingRuntimeSourceCommit(ServingCommit));
    }

    [TestMethod]
    public void TheReaderRejectsAFreshnessOrCommitsItDidNotWrite()
    {
        var canonical = Encoding.UTF8.GetString(V3EnvelopeJson.ProjectRest(Success(), _registry));
        // A stale flag, an age, a lag range or a re-check the platform did not compute is not the canonical form.
        foreach (var (from, to) in new[]
                 {
                     ("\"stale\":true,\"upstream_health\"", "\"stale\":false,\"upstream_health\""),
                     ("\"age_days\":37", "\"age_days\":36"),
                     ("\"lag_days\":null,\"lag_observations\":0,\"newest_completed_observation\":\"2026-08-11", "\"lag_days\":{\"max\":2,\"min\":1},\"lag_observations\":0,\"newest_completed_observation\":\"2026-08-11"),
                     ("\"last_checked_against_publisher\":null", "\"last_checked_against_publisher\":\"2026-09-16\""),
                 })
        {
            Assert.IsTrue(canonical.Contains(from, StringComparison.Ordinal), from);
            Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(Encoding.UTF8.GetBytes(canonical.Replace(from, to, StringComparison.Ordinal)), _registry), to);
        }

        // A short commit is not a commit.
        Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(
            Encoding.UTF8.GetBytes(canonical.Replace(BuilderCommit, BuilderCommit[..12], StringComparison.Ordinal)), _registry));
        // The commits and the freshness are closed member sets.
        Assert.ThrowsExactly<JsonException>(() => V3EnvelopeJson.ParseAndVerify(
            Encoding.UTF8.GetBytes(canonical.Replace("\"commits\":{", "\"commits\":{\"corpus_commit\":null,", StringComparison.Ordinal)), _registry));
    }

    private V3Envelope Refusal()
    {
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");
        return new V3EnvelopeBuilder(_registry).Refusal(
            "req", "resolve", Context("refusal"), "lex-v3-refusal/1",
            "no_corpus_mounted", helpful.RootElement);
    }

    private static V3EnvelopeContext Context(string status = "success") => new(
        PublisherId.LuLegilux,
        status,
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference("snapshot", Digest),
        "lu",
        false,
        new V3Freshness(DateTimeOffset.Parse("2026-09-17T00:00:00Z"), "current"));
}
