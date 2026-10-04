using System.Net;
using System.Net.Http;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A partition cover longer than one robots generation: a session's robots permission lasts 24 hours from its robots
/// fetch, so the cover replaces its session between leaves once less than 12 hours remain, and restarts a leaf whole,
/// once, when its session expired while it ran. Each leaf's two passes stay one run; the runs form contiguous blocks,
/// which the cover proof and its <c>/2</c> checkpoint accept. The clock jumps when a scripted response is served, and
/// runs every timer of 30 seconds or less at once.
/// </summary>
public sealed partial class LuxembourgRepeatedEnumerationExecutorTests
{
    // The root's first-pass COUNT reports a capacity failure, so the adaptive cover splits it into two leaves before any
    // leaf runs; each leaf then proves both passes over an empty range: COUNT 0, an empty page, twice.
    private const string CapacityFailure = "{\"meta\":\"error\",\"title\":\"Read timed out\",\"code\":\"error.unknown\"}";

    [TestMethod]
    public async Task ASessionsRemainingRobotsValidityFallsWithItsClockAndIsZeroAtTwentyFourHours()
    {
        var (_, witness) = BuildRequest();
        var clock = new RotationClock();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        using var handler = new RotationHandler(static (_, request) => throw new AssertFailedException("No product request."));
        var start = await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(witness, store, handler, clock,
            WireRequestBudget.OfWireRequests(5), CancellationToken.None);
        using var session = start.Session ?? throw new AssertFailedException("The session did not start.");

        var fresh = session.RobotsPolicyRemaining;
        Assert.IsTrue(fresh > TimeSpan.FromHours(23) && fresh <= TimeSpan.FromHours(24), fresh.ToString());
        clock.Advance(TimeSpan.FromHours(13));
        var later = session.RobotsPolicyRemaining;
        Assert.IsTrue(later < TimeSpan.FromHours(11) && later > TimeSpan.FromHours(10), later.ToString());
        clock.Advance(TimeSpan.FromHours(11));
        Assert.AreEqual(TimeSpan.Zero, session.RobotsPolicyRemaining, "at 24 hours the generation is spent, as the send gate says");
    }

    [TestMethod]
    public async Task ACoverOutlastingHalfARobotsGenerationReplacesItsSessionBetweenLeaves()
    {
        var clock = new RotationClock();
        // Product requests: 1 the root's COUNT (split); 2-5 the left leaf; 6-9 the right leaf. Serving the left leaf's last
        // request moves the clock 13 hours on, so less than 12 hours of the first session's robots validity remain.
        using var handler = new RotationHandler((ordinal, request) =>
        {
            if (ordinal == 5) clock.Advance(TimeSpan.FromHours(13));
            return LeafScript(ordinal, request);
        });
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var budget = WireRequestBudget.OfWireRequests(30);
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, budget, CancellationToken.None);

        Assert.AreEqual(2, result.Chain.Leaves.Count);
        Assert.IsTrue(result.Results.All(static leaf => leaf.Receipt is not null), string.Join(",", result.Results.Select(static leaf => leaf.Refusal?.Code)));
        Assert.AreEqual(2, handler.RobotsFetches, "the second session fetched and evaluated robots for itself");
        Assert.AreEqual(9, handler.ProductRequests);
        Assert.AreEqual(11, budget.Spent, "both robots fetches and every product request were charged");
        var left = result.Results[0].Receipt!.Delivery.RunIdentity;
        var right = result.Results[1].Receipt!.Delivery.RunIdentity;
        Assert.AreNotEqual(left, right, "the right leaf ran on the new session");

        var cover = LuxembourgPartitionCover.TryCreate(result.Chain, result.Results.Select(static leaf => leaf.Receipt!).ToArray(),
            null, out var refusal) ?? throw new AssertFailedException(refusal.ToString());
        Assert.AreEqual(left, cover.RunIdentity, "the cover names its first leaf's run");

        // Its checkpoint names the two run blocks, and restores each leaf under its own run.
        var checkpoint = await LuxembourgPartitionCoverCheckpoint.WriteAsync(store, cover, result.Results, CancellationToken.None);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, CancellationToken.None);
        StringAssert.Contains(Encoding.UTF8.GetString(bytes.Span), "\"lex-lu-partition-cover-checkpoint/2\"");
        var restored = await LuxembourgPartitionCoverCheckpoint.RestoreAsync(store, checkpoint, result.Chain.RootRange, left,
            cover.InterpretationProfileRef, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { left, right }, restored.LeafReceipts.Select(static receipt => receipt.Delivery.RunIdentity).ToArray());
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgPartitionCoverCheckpoint.RestoreAsync(
            store, checkpoint, result.Chain.RootRange, right, cover.InterpretationProfileRef, CancellationToken.None),
            "the caller's run must be the first block's");
    }

    [TestMethod]
    public async Task ALeafWhoseSessionExpiresWhileItRunsIsRestartedWholeOnceOnAFreshSession()
    {
        var clock = new RotationClock();
        // Serving the left leaf's first COUNT (request 2) moves the clock 25 hours on: the page after it is refused by the
        // session's send gate before anything goes out, and the leaf starts again on a new session.
        using var handler = new RotationHandler((ordinal, request) =>
        {
            if (ordinal == 2) clock.Advance(TimeSpan.FromHours(25));
            // After the cut-off COUNT (2), the leaf starts over: request n answers what request n - 1 would have.
            return LeafScript(ordinal <= 2 ? ordinal : ordinal - 1, request);
        });
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(30), CancellationToken.None);

        Assert.IsTrue(result.Results.All(static leaf => leaf.Receipt is not null), string.Join(",", result.Results.Select(static leaf => leaf.Refusal?.CoreRefusalDetail ?? leaf.Refusal?.Code.ToString())));
        Assert.AreEqual(2, handler.RobotsFetches);
        Assert.AreEqual(10, handler.ProductRequests, "the root's COUNT, the cut-off COUNT, then both leaves whole on the new session");
        Assert.AreEqual(result.Results[0].Receipt!.Delivery.RunIdentity, result.Results[1].Receipt!.Delivery.RunIdentity,
            "both leaves ran on the fresh session");
        Assert.IsNotNull(LuxembourgPartitionCover.TryCreate(result.Chain, result.Results.Select(static leaf => leaf.Receipt!).ToArray(), null, out _));
    }

    [TestMethod]
    public async Task ALeafThatExpiresTwiceIsRefusedRatherThanRestartedAgain()
    {
        var clock = new RotationClock();
        // The left leaf's first COUNT on each session moves the clock 25 hours on (requests 2 and 3).
        using var handler = new RotationHandler((ordinal, request) =>
        {
            if (ordinal is 2 or 3) clock.Advance(TimeSpan.FromHours(25));
            return ordinal == 1 ? LeafScript(1, request) : JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(0));
        });
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(30), CancellationToken.None);

        Assert.AreEqual(LuxembourgEnumerationRefusal.ObservationNotExecuted, result.Results[0].Refusal?.Code);
        Assert.AreEqual(2, handler.RobotsFetches, "one restart, not a loop");
        Assert.AreEqual(3, handler.ProductRequests);
        Assert.IsTrue(result.Results.Skip(1).All(static leaf => leaf.Refusal?.Code == LuxembourgEnumerationRefusal.ObservationNotExecuted));
    }

    [TestMethod]
    public async Task ARobotsRefusalAtAReplacementRefusesEveryRemainingLeafWithoutSending()
    {
        var clock = new RotationClock();
        using var handler = new RotationHandler((ordinal, request) =>
        {
            if (ordinal == 5) clock.Advance(TimeSpan.FromHours(13));
            return LeafScript(ordinal, request);
        }, robots: static (fetch, request) => fetch == 1
            ? PlainResponse(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n")
            : PlainResponse(request, HttpStatusCode.ServiceUnavailable, "maintenance"));
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(30), CancellationToken.None);

        Assert.IsNotNull(result.Results[0].Receipt, "the left leaf was proved on the first session");
        Assert.AreEqual(LuxembourgEnumerationRefusal.RobotsBootstrapRefused, result.Results[1].Refusal?.Code);
        Assert.AreEqual(result.Chain.Leaves.Count, result.Results.Count);
        Assert.AreEqual(5, handler.ProductRequests, "nothing was sent under the refused policy (Decision 67)");
    }

    [TestMethod]
    public async Task ARunBlockCheckpointThatDoesNotDescribeItsBlocksIsRefused()
    {
        var clock = new RotationClock();
        using var handler = new RotationHandler((ordinal, request) =>
        {
            if (ordinal == 5) clock.Advance(TimeSpan.FromHours(13));
            return LeafScript(ordinal, request);
        });
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(30), CancellationToken.None);
        var cover = LuxembourgPartitionCover.TryCreate(result.Chain, result.Results.Select(static leaf => leaf.Receipt!).ToArray(),
            null, out var refusal) ?? throw new AssertFailedException(refusal.ToString());
        var written = await LuxembourgPartitionCoverCheckpoint.WriteAsync(store, cover, result.Results, CancellationToken.None);
        var original = ContractJson.Deserialize<RunBlockCheckpoint>(Encoding.UTF8.GetString(
            (await CustodyRestore.ReadByDigestCheckedAsync(store, written.Sha256, CancellationToken.None)).Span));
        var (first, second) = (original.Segments[0].Run, original.Segments[1].Run);

        async Task<SourceArtifactRef> HoldAsync(RunBlockCheckpoint document)
        {
            var receipt = await store.CreateAsync(Encoding.UTF8.GetBytes(ContractJson.Serialize(document)), CustodyClass.NightlyFloor90d,
                CancellationToken.None);
            return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
        }

        Task<LuxembourgPartitionCover> RestoreAsync(SourceArtifactRef checkpoint) => LuxembourgPartitionCoverCheckpoint.RestoreAsync(
            store, checkpoint, result.Chain.RootRange, first, cover.InterpretationProfileRef, CancellationToken.None);

        // The control: the same blocks written again through this record restore, so each refusal below is its own clause's.
        Assert.AreEqual(2, (await RestoreAsync(await HoldAsync(original with { Segments = [.. original.Segments] }))).LeafReceipts.Count);
        foreach (var (name, blocks) in new (string, RunBlock[])[]
        {
            ("one block for runs that differ", [new RunBlock(first, 2)]),
            ("a run in two blocks", [new RunBlock(first, 1), new RunBlock(first, 1)]),
            ("blocks that do not add up to the leaves", [new RunBlock(first, 1), new RunBlock(second, 2)]),
            ("an empty block", [new RunBlock(first, 2), new RunBlock(second, 0)]),
        })
        {
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => RestoreAsync(HoldAsync(original with { Segments = blocks }).Result), name);
        }
    }

    [TestMethod]
    public async Task ABudgetSpentBeforeAReplacementSessionRefusesTheRemainingLeavesWithoutARobotsFetch()
    {
        var clock = new RotationClock();
        using var handler = new RotationHandler((ordinal, request) =>
        {
            if (ordinal == 5) clock.Advance(TimeSpan.FromHours(13));
            return LeafScript(ordinal, request);
        });
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // One robots fetch, the root's COUNT and the left leaf's four requests: nothing is left for a second session.
        var budget = WireRequestBudget.OfWireRequests(6);
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, budget, CancellationToken.None);

        Assert.IsNotNull(result.Results[0].Receipt, "the left leaf was proved on the first session");
        Assert.AreEqual(LuxembourgEnumerationRefusal.WireBudgetExhausted, result.Results[1].Refusal?.Code);
        Assert.AreEqual(result.Chain.Leaves.Count, result.Results.Count);
        Assert.AreEqual(1, handler.RobotsFetches, "the replacement's robots fetch was never sent");
        Assert.AreEqual(5, handler.ProductRequests);
        Assert.AreEqual(6, budget.Spent);
    }

    /// <summary>The <c>/2</c> cover checkpoint's shape, to write a tampered one through the same serializer.</summary>
    private sealed record RunBlockCheckpoint(string Schema, LuxembourgQueryPartitionRange Root,
        IReadOnlyList<LuxembourgPartitionSplitStep> Splits, IReadOnlyList<SourceArtifactRef> Leaves,
        IReadOnlyList<RunBlock> Segments, SourceArtifactRef Profile);

    private sealed record RunBlock(SourceArtifactRef Run, int LeafCount);

    // The cover's product requests by ordinal: the root's COUNT answers a capacity failure (so it splits), then each leaf
    // answers COUNT 0, an empty page, COUNT 0 and an empty page.
    private static HttpResponseMessage LeafScript(int ordinal, HttpRequestMessage request)
    {
        if (ordinal == 1)
        {
            var failure = JsonResponse(request, CapacityFailure);
            failure.StatusCode = HttpStatusCode.InternalServerError;
            return failure;
        }

        return JsonResponse(request, (ordinal - 2) % 2 == 0
            ? LuxembourgAcquisitionTestFixture.CountJson(0)
            : LuxembourgAcquisitionTestFixture.EmptyRowsJson());
    }

    private static HttpResponseMessage PlainResponse(HttpRequestMessage request, HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new HttpResponseMessage(status) { Version = HttpVersion.Version11, RequestMessage = request, Content = content };
    }

    /// <summary>Answers robots.txt by path (each session fetches its own) and every other request from the product script by ordinal.</summary>
    private sealed class RotationHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> product,
        Func<int, HttpRequestMessage, HttpResponseMessage>? robots = null) : HttpMessageHandler
    {
        private int _robots;
        private int _products;

        internal int RobotsFetches => Volatile.Read(ref _robots);

        internal int ProductRequests => Volatile.Read(ref _products);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath == "/robots.txt")
            {
                var fetch = Interlocked.Increment(ref _robots);
                return Task.FromResult(robots?.Invoke(fetch, request) ?? PlainResponse(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n"));
            }

            return Task.FromResult(product(Interlocked.Increment(ref _products), request));
        }
    }

    /// <summary>Two seconds per reading, a jump on request, and every timer of 30 seconds or less run at once.</summary>
    private sealed class RotationClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks);

        internal void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            dueTime > TimeSpan.Zero && dueTime <= TimeSpan.FromSeconds(30)
                ? new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan)
                : new Timer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
}
