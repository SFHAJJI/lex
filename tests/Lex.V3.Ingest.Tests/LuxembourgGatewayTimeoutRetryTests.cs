using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Legilux SPARQL gateway's read timeout: an HTTP 500 whose body is exactly the gateway's 120-byte envelope with a
/// fresh id. A page or a second-pass COUNT that answers it is sent again as the same plan item, at most three sends in
/// all, each after the profile's maximum retry delay; then the leaf is split. A first-pass COUNT is split at once.
/// Eight envelopes without an admitted page stop the run. Anything that is not exactly the envelope refuses as before.
/// </summary>
/// <remarks>
/// The clock runs every timer of 30 seconds or less at once and records it, and never runs a longer one, so the
/// cool-downs, the session's backoff and its pacing are visible without waiting, and the 60-second request timeout
/// stays dormant.
/// </remarks>
public sealed partial class LuxembourgRepeatedEnumerationExecutorTests
{
    // Retained by the 2026-09-30 count probe (custody object 79282f1f...), byte for byte.
    private const string RetainedGatewayTimeout =
        "{\"meta\":\"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c61\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}";

    [TestMethod]
    public void TheRetainedGatewayTimeoutIsRecognizedByItsTemplate()
    {
        var retained = Encoding.UTF8.GetBytes(RetainedGatewayTimeout);
        Assert.AreEqual("79282f1f294d538e86d19d118e951f2ee21b2c334112c05daf910874b0d57210",
            Convert.ToHexStringLower(SHA256.HashData(retained)));
        Assert.HasCount(120, retained);
        Assert.IsTrue(LuxembourgGatewayTimeouts.IsEnvelope(retained));
        Assert.IsTrue(LuxembourgGatewayTimeouts.IsEnvelope(Encoding.UTF8.GetBytes(Envelope())), "every response carries a fresh id");
    }

    [TestMethod]
    [DataRow(2, 1)]
    [DataRow(2, 2)]
    [DataRow(4, 1)]
    [DataRow(4, 2)]
    [DataRow(5, 1)]
    [DataRow(5, 2)]
    public async Task AGatewayTimeoutIsRetriedAsTheSamePlanItemThenAdmitted(int faultAt, int timeouts)
    {
        // The leaf's six product requests: the first-pass COUNT, its page of one row and its empty last page, then the
        // same three for the second pass. The request at faultAt (a first-pass page, the second-pass COUNT or a
        // second-pass page) answers the gateway's read timeout `timeouts` times before its real answer.
        var script = new List<Func<HttpRequestMessage, HttpResponseMessage>>();
        for (var position = 1; position <= 6; position++)
        {
            if (position == faultAt)
            {
                for (var timeout = 0; timeout < timeouts; timeout++) script.Add(static req => EnvelopeResponse(req, Envelope()));
            }

            var body = position switch
            {
                1 or 4 => LuxembourgAcquisitionTestFixture.CountJson(1),
                2 or 5 => LuxembourgAcquisitionTestFixture.RowsJson("b"),
                _ => LuxembourgAcquisitionTestFixture.EmptyRowsJson(),
            };
            script.Add(req => JsonResponse(req, body));
        }

        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = Scripted(script);
        var clock = new GatewayClock();
        var budget = WireRequestBudget.OfWireRequests(20);
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunPartitionAsync(request, witness, budget, CancellationToken.None);

        Assert.IsNotNull(result.Receipt, result.Refusal?.ToString());
        Assert.AreEqual(7 + timeouts, handler.SendCount, "robots, six product requests and each retry");
        Assert.AreEqual(7 + timeouts, budget.Spent, "every retry is charged to the run's one wire budget before it is sent");
        Assert.AreEqual(6 + timeouts, result.ProductRequestCount);
        Assert.AreEqual(timeouts, clock.CoolDowns, "each retry first waits the profile's maximum retry delay");
        CollectionAssert.IsSubsetOf(timeouts == 1 ? new[] { TimeSpan.FromSeconds(1) } : new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) },
            clock.Delays.ToArray(), "and then the session's own backoff");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARequestThatKeepsTimingOutIsSplitAfterThreeSends(bool onTheSecondPassCount)
    {
        // The root's first pass is a COUNT of 0 and its empty page; then either that page or the second-pass COUNT
        // answers the envelope on three sends, and a fourth send is never made: the leaf splits, and each child proves
        // both passes over its half.
        var script = new List<Func<HttpRequestMessage, HttpResponseMessage>>
        {
            static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(0)),
        };
        if (onTheSecondPassCount) script.Add(static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()));
        for (var send = 0; send < LuxembourgGatewayTimeouts.MaximumSendsPerRequest; send++)
            script.Add(static req => EnvelopeResponse(req, Envelope()));
        for (var child = 0; child < 2; child++)
        {
            script.Add(static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(0)));
            script.Add(static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()));
            script.Add(static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(0)));
            script.Add(static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()));
        }

        var (root, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = Scripted(script);
        var clock = new GatewayClock();
        var budget = WireRequestBudget.OfWireRequests(30);
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(root, witness, budget, CancellationToken.None);

        Assert.AreEqual(2, result.Chain.Leaves.Count, "the leaf that kept timing out was split once");
        Assert.IsTrue(result.Results.All(static leaf => leaf.Receipt is not null && leaf.Refusal is null),
            string.Join(",", result.Results.Select(static leaf => leaf.Refusal?.CoreRefusalDetail)));
        Assert.AreEqual(script.Count + 1, handler.SendCount, "robots and every scripted request, and no fourth send");
        Assert.AreEqual(script.Count + 1, budget.Spent);
        Assert.AreEqual(script.Count, result.ProductRequestCount, "the abandoned leaf's requests are counted too");
        Assert.AreEqual(2, clock.CoolDowns);
        var cover = LuxembourgPartitionCover.TryCreate(result.Chain,
            result.Results.Select(static leaf => leaf.Receipt!).ToArray(), null, out var refusal);
        Assert.IsNotNull(cover, refusal.ToString());
        Assert.AreEqual(LuxembourgPartitionCoverBasis.LeafTilingOnly, cover.Basis);
    }

    [TestMethod]
    public async Task WithoutSplittingAPageThatKeepsTimingOutAsksForSmallerLeaves()
    {
        var script = new List<Func<HttpRequestMessage, HttpResponseMessage>>
        {
            static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(0)),
        };
        for (var send = 0; send < LuxembourgGatewayTimeouts.MaximumSendsPerRequest; send++)
            script.Add(static req => EnvelopeResponse(req, Envelope()));
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = Scripted(script);
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, new GatewayClock(), handler)
            .RunPartitionAsync(request, witness, WireRequestBudget.OfWireRequests(20), CancellationToken.None);

        Assert.AreEqual(LuxembourgEnumerationRefusal.PartitionRequired, result.Refusal?.Code, result.Refusal?.CoreRefusalDetail);
        Assert.AreEqual(500, result.Refusal!.TerminalStatus);
        Assert.AreEqual(5, handler.SendCount, "robots, the COUNT and three sends of the page");
        StringAssert.Contains(result.Refusal.CoreRefusalDetail, "smaller proven leaves required");
    }

    [TestMethod]
    public async Task EightGatewayTimeoutsWithoutAnAdmittedPageStopTheRun()
    {
        // Every product request answers the envelope. First-pass COUNTs are not retried, so the cover splits after each
        // until the eighth envelope since the last admitted page opens the breaker: that leaf refuses, the cover stops,
        // and the rest of the run sends no SPARQL at all.
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // Robots by path, not by ordinal: each cover's session fetches its own.
        var handler = new LuxembourgAcquisitionTestFixture.SequencedHandler(static (_, req) => req.RequestUri!.AbsolutePath == "/robots.txt"
            ? EnvelopeResponse(req, "User-agent: *\nAllow: /\n", HttpStatusCode.OK, "text/plain")
            : EnvelopeResponse(req, Envelope()));
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, new GatewayClock(), handler);
        var result = await executor.RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(50), CancellationToken.None);

        Assert.AreEqual(1 + LuxembourgGatewayTimeouts.ConsecutiveLimit, handler.SendCount, "robots and eight COUNTs");
        var stopped = result.Results.First(static leaf => leaf.Refusal is not null).Refusal!;
        Assert.AreEqual(LuxembourgEnumerationRefusal.StatusNotAdmitted, stopped.Code, stopped.CoreRefusalDetail);
        StringAssert.Contains(stopped.CoreRefusalDetail, "this run stops");
        Assert.IsTrue(result.Results.All(static leaf => leaf.Receipt is null));

        var later = await executor.RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(50), CancellationToken.None);
        Assert.AreEqual(2 + LuxembourgGatewayTimeouts.ConsecutiveLimit, handler.SendCount, "a later cover of the run fetches only its robots");
        Assert.AreEqual(LuxembourgEnumerationRefusal.ObservationNotExecuted, later.Results.Single().Refusal?.Code, later.Results.Single().Refusal?.CoreRefusalDetail);
    }

    [TestMethod]
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55D19366-71EC-4828-8502-6262C0C13C61\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}")]
    [DataRow(500, "{\"meta\":\"error\",\"title\":\"Read timed out\",\"code\":\"error.unknown\"}")]
    // The id's shape at the full 120 bytes: a dash moved off position 8, and a lowercase letter that is not hex.
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55d1936-671ec-4828-8502-6262c0c13c61\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}")]
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c6g\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}")]
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c6\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}")]
    [DataRow(500, "{\"meta\": \"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c61\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}")]
    [DataRow(500, RetainedGatewayTimeout + "\n")]
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c61\",\"title\":\"Read timed out.\",\"code\":\"error.unknown\",\"data\":null}")]
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c61\",\"title\":\"Read timed out\",\"code\":\"error.timeout\",\"data\":null}")]
    [DataRow(500, "{\"meta\":\"error\",\"id\":\"55d19366-71ec-4828-8502-6262c0c13c61\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":{}}")]
    [DataRow(500, "<html>challenge</html>")]
    [DataRow(500, "Virtuoso 40001 Error SR...: Transaction deadlock, from SQL built-in function.")]
    [DataRow(502, RetainedGatewayTimeout)]
    [DataRow(503, RetainedGatewayTimeout)]
    [DataRow(504, RetainedGatewayTimeout)]
    public async Task ABodyThatIsNotExactlyTheGatewayTimeoutIsRefusedOnItsFirstSend(int status, string body)
    {
        var script = new List<Func<HttpRequestMessage, HttpResponseMessage>>
        {
            static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            req => EnvelopeResponse(req, body, (HttpStatusCode)status),
        };
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = Scripted(script);
        var clock = new GatewayClock();
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunPartitionAsync(request, witness, WireRequestBudget.OfWireRequests(20), CancellationToken.None);

        Assert.AreEqual(LuxembourgEnumerationRefusal.StatusNotAdmitted, result.Refusal?.Code, result.Refusal?.CoreRefusalDetail);
        Assert.AreEqual(status, result.Refusal!.TerminalStatus);
        Assert.AreEqual(3, handler.SendCount, "robots, the COUNT and one send of the page");
        Assert.AreEqual(0, clock.CoolDowns);
    }

    [TestMethod]
    public async Task AnAdmittedPageResetsTheBreakerSoScatteredTimeoutsNeverStopTheRun()
    {
        // Each partition's six requests answer the envelope twice before their real answer at both pages and the
        // second-pass COUNT: six envelopes per partition, never more than four since an admitted page. Two partitions on
        // one executor (one run) see twelve envelopes, so a breaker that counted them all would have stopped the second.
        var script = new List<Func<HttpRequestMessage, HttpResponseMessage>>();
        for (var partition = 0; partition < 2; partition++)
        {
            for (var position = 1; position <= 6; position++)
            {
                if (position is 2 or 4 or 5)
                {
                    script.Add(static req => EnvelopeResponse(req, Envelope()));
                    script.Add(static req => EnvelopeResponse(req, Envelope()));
                }

                var body = position switch
                {
                    1 or 4 => LuxembourgAcquisitionTestFixture.CountJson(1),
                    2 or 5 => LuxembourgAcquisitionTestFixture.RowsJson("b"),
                    _ => LuxembourgAcquisitionTestFixture.EmptyRowsJson(),
                };
                script.Add(req => JsonResponse(req, body));
            }
        }

        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        // Robots by path: each partition's session fetches its own.
        var products = 0;
        var handler = new LuxembourgAcquisitionTestFixture.SequencedHandler((_, req) => req.RequestUri!.AbsolutePath == "/robots.txt"
            ? EnvelopeResponse(req, "User-agent: *\nAllow: /\n", HttpStatusCode.OK, "text/plain")
            : script[Interlocked.Increment(ref products) - 1](req));
        var clock = new GatewayClock();
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, clock, handler);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var (request, witness) = BuildRequest();
            var result = await executor.RunPartitionAsync(request, witness, WireRequestBudget.OfWireRequests(30), CancellationToken.None);
            Assert.IsNotNull(result.Receipt, result.Refusal?.CoreRefusalDetail ?? result.Refusal?.Code.ToString());
        }

        Assert.AreEqual(script.Count, products, "every scripted request was sent, retries included");
        Assert.AreEqual(12, clock.CoolDowns);
    }

    [TestMethod]
    public async Task TheEighthTimeoutSinceTheLastAdmittedPageIsNotRetriedAndStopsTheRun()
    {
        // Six first-pass COUNTs answer the envelope (each splits the leftmost leaf); the seventh leaf's COUNT succeeds,
        // which does not reset the breaker; its page answers the envelope (the seventh), is retried once and answers it
        // again (the eighth). The breaker is now open: no third send, and the run stops.
        var script = new List<Func<HttpRequestMessage, HttpResponseMessage>>();
        for (var split = 0; split < 6; split++) script.Add(static req => EnvelopeResponse(req, Envelope()));
        script.Add(static req => JsonResponse(req, LuxembourgAcquisitionTestFixture.CountJson(1)));
        script.Add(static req => EnvelopeResponse(req, Envelope()));
        script.Add(static req => EnvelopeResponse(req, Envelope()));
        var (request, witness) = BuildRequest();
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var handler = Scripted(script);
        var clock = new GatewayClock();
        var result = await new LuxembourgRepeatedEnumerationExecutor(store, clock, handler)
            .RunAdaptiveCoverAsync(request, witness, WireRequestBudget.OfWireRequests(50), CancellationToken.None);

        Assert.AreEqual(1 + script.Count, handler.SendCount, "robots and the nine scripted requests: the page was sent twice, not three times");
        Assert.AreEqual(1, clock.CoolDowns, "one retry, then the breaker vetoed the next");
        var stopped = result.Results.First(static leaf => leaf.Refusal is not null).Refusal!;
        Assert.AreEqual(LuxembourgEnumerationRefusal.StatusNotAdmitted, stopped.Code, stopped.CoreRefusalDetail);
        StringAssert.Contains(stopped.CoreRefusalDetail, "this run stops");
        Assert.AreEqual(1, result.Results.Count(static leaf => leaf.Refusal?.Code == LuxembourgEnumerationRefusal.StatusNotAdmitted),
            "the leaves after it were not attempted");
    }

    private static string Envelope() =>
        $"{{\"meta\":\"error\",\"id\":\"{Guid.NewGuid():D}\",\"title\":\"Read timed out\",\"code\":\"error.unknown\",\"data\":null}}";

    private static HttpResponseMessage EnvelopeResponse(HttpRequestMessage request, string body,
        HttpStatusCode status = HttpStatusCode.InternalServerError, string mediaType = "application/json")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        content.Headers.TryAddWithoutValidation("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new HttpResponseMessage(status) { Version = HttpVersion.Version11, RequestMessage = request, Content = content };
    }

    private static LuxembourgAcquisitionTestFixture.SequencedHandler Scripted(
        IReadOnlyList<Func<HttpRequestMessage, HttpResponseMessage>> script) =>
        LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            ordinal <= script.Count ? script[ordinal - 1](request) : throw new AssertFailedException($"Unexpected send {ordinal}."));

    /// <summary>
    /// Advances two seconds per reading, like the fixture clocks, runs every timer due within 30 seconds at once
    /// (advancing by its delay and recording it), and never runs a longer one.
    /// </summary>
    private sealed class GatewayClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        private readonly List<TimeSpan> _delays = [];
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        internal IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_delays) return _delays.ToArray();
            }
        }

        internal int CoolDowns => Delays.Count(static delay => delay == TimeSpan.FromSeconds(30));

        public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero && dueTime <= TimeSpan.FromSeconds(30))
            {
                lock (_delays) _delays.Add(dueTime);
                Interlocked.Add(ref _ticks, dueTime.Ticks);
                return new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
            }

            return new Timer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
