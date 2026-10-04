using System.Net;
using System.Net.Http;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A document phase's GETs through one shared session at a time: one robots fetch serves the phase, every document's URL
/// is still evaluated literally against that policy before anything is sent for it (Decision 83), the robots route is
/// retained so each verdict is re-derived offline, and the session is replaced every hour.
/// </summary>
public sealed partial class LuxembourgDocumentGetTests
{
    private static LuxembourgDocumentFetchAddress ActAddress(string act) => Address(
        storeUri: StoreXmlUri.Replace("/a439/", $"/{act}/", StringComparison.Ordinal).Replace("-a439-", $"-{act}-", StringComparison.Ordinal),
        actPagePath: ActEliPagePath.Replace("/a439/", $"/{act}/", StringComparison.Ordinal));

    private static BoundMachineRequest Bind(LuxembourgDocumentFetchAddress address) =>
        new LuxembourgDocumentFetchPlan(address).Bind($"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}",
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3001)).Request;

    private static HttpResponseMessage XmlBody(HttpRequestMessage request)
    {
        var bytes = "<akomaNtoso/>"u8.ToArray();
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/xml");
        content.Headers.TryAddWithoutValidation("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version11, RequestMessage = request, Content = content };
    }

    [TestMethod]
    public async Task ADocumentPhasesSharedSessionFetchesRobotsOnceAndEveryGetReopensUnderItsVerdict()
    {
        var store = new FileSystemCustodyStore(Path.Combine(Path.GetTempPath(), "lex-v3-lu-docs-" + Guid.NewGuid().ToString("N")));
        var handler = new RobotsThenDocumentHandler(static (request, _) => XmlBody(request));
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var addresses = new[] { ActAddress("a439"), ActAddress("a440"), ActAddress("a441") };
        var requests = addresses.Select(Bind).ToArray();
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var results = new List<LuxembourgDocumentGetAttemptResult>();
        using (var batch = executor.OpenDocumentGetBatch())
        {
            foreach (var request in requests)
                results.Add(await batch.RunAsync(request, budget, CancellationToken.None));
        }

        Assert.AreEqual(4, handler.SendCount, "one robots fetch for the phase, then the three documents");
        Assert.IsTrue(results.All(static result => result.Evidence is not null), string.Join(",", results.Select(static result => result.Detail)));
        var robots = results[0].RobotsRoute ?? throw new AssertFailedException("The shared session retained its robots fetch.");
        Assert.IsTrue(results.All(result => result.RobotsRoute == robots));
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3 }, results.Select(static result => result.Evidence!.RequestOrdinal).ToArray());
        Assert.AreEqual(1, results.Select(static result => result.Evidence!.RunIdentity).Distinct().Count(), "one run");

        // Each GET reopens offline under the verdict its run's retained policy gave its exact path.
        for (var index = 0; index < requests.Length; index++)
        {
            var route = results[index].Evidence!;
            var held = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(route.CopyCanonicalBytes()));
            var reopened = await LuxembourgDocumentFetchRouteReader.ReopenAdmittedAsync(store, held, route.RunIdentity,
                route.Hops[0].LogicalRequestSha256, addresses[index], requests[index], robots, CancellationToken.None);
            Assert.AreEqual(route.RequestOrdinal, reopened.Evidence!.RequestOrdinal);
            Assert.AreEqual(robots, reopened.RobotsRoute);
        }

        // A later product request of a shared session is not a session of its own: the per-document door refuses it.
        var second = results[1].Evidence!;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgDocumentFetchRouteReader.ReopenAsync(store,
            new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(second.CopyCanonicalBytes())), second.RunIdentity,
            second.Hops[0].LogicalRequestSha256, addresses[1], requests[1], CancellationToken.None));
    }

    [TestMethod]
    public async Task ADocumentTheSharedPolicyDisallowsIsRefusedWithNothingSentAndTheRefusalReopens()
    {
        var store = new FileSystemCustodyStore(Path.Combine(Path.GetTempPath(), "lex-v3-lu-docs-" + Guid.NewGuid().ToString("N")));
        var handler = new RobotsThenDocumentHandler(static (request, _) => XmlBody(request))
        {
            RobotsText = "User-agent: *\nDisallow: /filestore/eli/etat/leg/loi/2017/03/14/a440/\n",
        };
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var addresses = new[] { ActAddress("a439"), ActAddress("a440"), ActAddress("a441") };
        var requests = addresses.Select(Bind).ToArray();
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var results = new List<LuxembourgDocumentGetAttemptResult>();
        using (var batch = executor.OpenDocumentGetBatch())
        {
            foreach (var request in requests)
                results.Add(await batch.RunAsync(request, budget, CancellationToken.None));
        }

        Assert.AreEqual(3, handler.SendCount, "robots, then only the two allowed documents");
        Assert.AreEqual(LuxembourgDocumentGetAttemptRefusal.RobotsDisallowed, results[1].Refusal);
        Assert.AreEqual(addresses[1].FetchUri.PathAndQuery, results[1].DeniedRobotsPath);
        Assert.IsNotNull(results[0].Evidence);
        Assert.IsNotNull(results[2].Evidence);
        var robots = results[1].RobotsRoute ?? throw new AssertFailedException("The refusal names the policy that refused it.");

        var refused = await LuxembourgDocumentFetchRouteReader.ReopenRobotsRefusalAsync(store, robots, addresses[1], requests[1],
            CancellationToken.None);
        Assert.AreEqual(results[1].Detail, refused.Detail);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgDocumentFetchRouteReader.ReopenRobotsRefusalAsync(
            store, robots, addresses[0], requests[0], CancellationToken.None), "the policy allows that document, so no refusal reopens");
    }

    [TestMethod]
    public async Task TheSharedSessionIsReplacedEveryHourAndAGetDoesNotReopenUnderAnotherRunsPolicy()
    {
        var store = new FileSystemCustodyStore(Path.Combine(Path.GetTempPath(), "lex-v3-lu-docs-" + Guid.NewGuid().ToString("N")));
        var clock = new JumpingClock();
        var handler = new RobotsThenDocumentHandler((request, _) =>
        {
            clock.Advance(TimeSpan.FromMinutes(61));
            return XmlBody(request);
        });
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, clock, handler);
        var addresses = new[] { ActAddress("a439"), ActAddress("a440") };
        var requests = addresses.Select(Bind).ToArray();
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var results = new List<LuxembourgDocumentGetAttemptResult>();
        using (var batch = executor.OpenDocumentGetBatch())
        {
            foreach (var request in requests)
                results.Add(await batch.RunAsync(request, budget, CancellationToken.None));
        }

        Assert.AreEqual(4, handler.SendCount, "each document on its own session: an hour passed between them");
        Assert.AreNotEqual(results[0].RobotsRoute, results[1].RobotsRoute);
        Assert.AreNotEqual(results[0].Evidence!.RunIdentity, results[1].Evidence!.RunIdentity);

        var second = results[1].Evidence!;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgDocumentFetchRouteReader.ReopenAdmittedAsync(store,
            new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(second.CopyCanonicalBytes())), second.RunIdentity,
            second.Hops[0].LogicalRequestSha256, addresses[1], requests[1], results[0].RobotsRoute!, CancellationToken.None),
            "the first session's policy did not admit the second session's GET");
    }

    /// <summary>Two seconds per reading, like the fixture clocks, and a jump on request.</summary>
    private sealed class JumpingClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks);

        internal void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
