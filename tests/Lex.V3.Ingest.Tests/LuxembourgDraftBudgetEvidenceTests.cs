using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the draft family may send, and what it says it sent.
/// </summary>
/// <remarks>
/// <para>
/// THE ASYMMETRY THIS CLOSES. The OpinionRequest doors reserved every wire request against an
/// enforced ceiling; their draft siblings opened a session first and ran the pass loop with no budget
/// at all. Clause (c) has to run exactly those two doors, so the acceptance path went through the one
/// family the ceiling did not cover.
/// </para>
/// <para>
/// Every case here is offline. A ceiling is proven by showing requests were NOT sent, which a live
/// run cannot demonstrate and a fake transport can.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftBudgetEvidenceTests
{
    /// <summary>
    /// A spent budget opens no session and sends nothing at all.
    /// </summary>
    /// <remarks>
    /// The reservation sits before <c>StartSessionAsync</c>, so the refusal happens with the
    /// transport untouched. Asserted on the transport's own send count, because a refusal that
    /// arrived after robots went out would be a receipt rather than a ceiling.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheInventoryDoor()
    {
        var handler = new CountingHandler();
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt());
        Assert.IsTrue(budget.TryReserveAttempt());

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.AreEqual(0, handler.SendCount, "not one request, not even robots.");
        Assert.IsFalse(result.Delivered);
        StringAssert.Contains(
            result.Detail ?? string.Empty,
            nameof(EuEnumerationRefusal.WireBudgetExhausted),
            "and it must stop on the CEILING, not on something else that also refuses.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
    }

    /// <summary>The same door, at the ceiling mid-pass rather than before it.</summary>
    /// <remarks>
    /// A budget of two covers the robots fetch and the count; the page that count implies is the
    /// request with nothing left to reserve. That places the stop on a request the run genuinely
    /// wanted to send, which is the only arrangement where <c>Spent == Limit</c> means the ceiling
    /// was reached rather than merely quoted.
    /// </remarks>
    [TestMethod]
    public async Task TheInventoryDoorStopsMidPassAtItsCeiling()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(2);

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(2, handler.SendCount, "robots and the count; the page was never sent.");
        Assert.AreEqual(1, handler.RobotsSends);
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
        Assert.AreEqual(
            handler.SendCount, result.WireBudget.Spent,
            "reservations are taken immediately before send, so at the ceiling they are the sends.");
    }

    /// <summary>A refused run still carries its snapshot, and it reconciles.</summary>
    /// <remarks>
    /// The runs that most need reconciling are the ones that stopped. A refusal that dropped its
    /// cost would leave the wire spend of a failed attempt unaccountable.
    /// </remarks>
    [TestMethod]
    public async Task ARefusedInventoryRunCarriesItsSnapshot()
    {
        var handler = new CountingHandler(_ => null);
        var budget = WireRequestBudget.OfWireRequests(100);

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered, "this publisher answered 503 to every product request.");
        Assert.AreEqual(100, result.WireBudget.Limit);
        Assert.IsGreaterThan(0, result.WireBudget.Spent);
        Assert.AreEqual(
            handler.SendCount, result.WireBudget.Spent,
            "a refusal reconciles to the transport exactly as a delivery would.");
    }

    /// <summary>
    /// Retries are reserved like any other attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source profile permits four attempts per bound request, so a ceiling counting only bound
    /// requests would be wrong by that factor at exactly its own boundary.
    /// </para>
    /// <para>
    /// DRIVEN BY A PRE-HEADER FAILURE, NOT A 503. A 503 is a completed response and is not retried
    /// here, so a case built on one would have proved nothing about retries while appearing to. The
    /// connection error is the shape that actually reaches the retry path.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task RetryAttemptsAreReservedToo()
    {
        var handler = new CountingHandler(throwPreHeader: true);
        var budget = WireRequestBudget.OfWireRequests(3);

        var result = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(
            3, handler.SendCount,
            "robots plus two attempts at the count, and the ceiling stopped the rest of the retries.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
    }

    /// <summary>One budget across an inventory and a batch accounts cumulatively.</summary>
    /// <remarks>
    /// The identity the whole ceiling argument rests on: reservations equal recorded product
    /// attempts plus one robots fetch per session opened. Checked against the transport, which is
    /// the only witness not part of the accounting being checked.
    /// </remarks>
    [TestMethod]
    public async Task OneBudgetAcrossInventoryAndBatchAccountsCumulatively()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(4);

        var first = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);
        var firstSpent = first.WireBudget.Spent;

        var second = await InventoryProducer(handler).RunAsync(
            InventoryRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.AreEqual(
            4, second.WireBudget.Spent, "the shared ceiling, reached across the two runs together.");
        Assert.IsGreaterThanOrEqualTo(
            firstSpent, second.WireBudget.Spent, "a reservation is never returned.");
        Assert.AreEqual(
            handler.SendCount,
            second.WireBudget.Spent,
            "THE WITNESS OUTSIDE THE ACCOUNTING. A second run receiving a fresh budget would send "
                + "another robots fetch this figure never charged.");
        Assert.IsLessThanOrEqualTo(
            budget.Limit, handler.SendCount, "the ceiling held across both runs.");
    }

    /// <summary>The draft graph's only construction door refuses a null budget.</summary>
    [TestMethod]
    public void TheDraftGraphBatchDoorRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgDraftGraphRunRequest.ForBatch(
                LuxembourgDraftGraphDiscoveryPlan.Create(),
                DeliveredInventory(),
                0,
                "urn:uuid:3f8a1c65-24b7-4e09-9d31-8c5b06e2fa47",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9303),
                null!));

    /// <summary>The draft inventory request refuses a null budget at construction.</summary>
    /// <remarks>
    /// A positional record checks nothing of its own, so the documented requirement had to be made
    /// one. Its OpinionRequest sibling carried exactly this defect until it was repaired.
    /// </remarks>
    [TestMethod]
    public void TheDraftInventoryRequestRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgInitialDraftInventoryRunRequest(
                LuxembourgInitialDraftInventoryDiscoveryPlan.Create(),
                "urn:uuid:1d7e4b90-6c25-4a83-b0f1-27e95a3c8d64",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9304),
                null!));

    /// <summary>
    /// The draft GRAPH door reserves before its session too.
    /// </summary>
    /// <remarks>
    /// Added because the first version of this class tested only the inventory door: deleting the
    /// graph door's reservation survived every case here. Two doors were repaired and only one was
    /// covered, which is the same one-of-a-pair omission this slice keeps producing.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheGraphDoor()
    {
        var handler = new CountingHandler();
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt());
        Assert.IsTrue(budget.TryReserveAttempt());

        var result = await GraphProducer(handler).RunAsync(
            GraphRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.AreEqual(0, handler.SendCount, "not one request, not even robots.");
        Assert.IsFalse(result.Delivered);
        StringAssert.Contains(
            result.Detail ?? string.Empty,
            nameof(EuEnumerationRefusal.WireBudgetExhausted),
            "the graph door must stop on the ceiling, like its sibling.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
    }

    /// <summary>The graph door's pass loop reserves every attempt it makes.</summary>
    /// <remarks>
    /// A budget of two covers robots and the count; the page the count implies has nothing left to
    /// reserve. Without the budget reaching <c>RunPassesAsync</c> the page would go out and the
    /// transport would show three sends against a ceiling of two.
    /// </remarks>
    [TestMethod]
    public async Task TheGraphDoorStopsMidPassAtItsCeiling()
    {
        var handler = new CountingHandler(_ => LuxembourgAcquisitionTestFixture.CountJson(2));
        var budget = WireRequestBudget.OfWireRequests(2);

        var result = await GraphProducer(handler).RunAsync(
            GraphRequest(budget), LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(2, handler.SendCount, "robots and the count; the page was never sent.");
        Assert.AreEqual(budget.Limit, result.WireBudget.Spent);
        Assert.IsTrue(result.WireBudget.Exhausted);
        Assert.AreEqual(
            handler.SendCount, result.WireBudget.Spent,
            "reservations are taken immediately before send, so at the ceiling they are the sends.");
    }

    private static LuxembourgDraftGraphProducer GraphProducer(CountingHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    private static LuxembourgDraftGraphRunRequest GraphRequest(WireRequestBudget budget) =>
        LuxembourgDraftGraphRunRequest.ForBatch(
            LuxembourgDraftGraphDiscoveryPlan.Create(),
            LuxembourgDraftGraphProducerTests.InventoryOf(
                "http://data.legilux.public.lu/eli/dl/pl/2000/0001"),
            0,
            "urn:uuid:7a2e5c81-3f46-4b09-8d17-52c9e0b4a36d",
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9307),
            budget);

    private static LuxembourgInitialDraftInventoryProducer InventoryProducer(CountingHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    private static LuxembourgInitialDraftInventoryRunRequest InventoryRequest(WireRequestBudget budget) =>
        new(LuxembourgInitialDraftInventoryDiscoveryPlan.Create(),
            "urn:uuid:2b6f9e13-58a4-4c72-9e05-1f83d40b7a29",
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9305),
            budget);

    /// <summary>An inventory result standing in for a delivered one, for the null-door case only.</summary>
    private static LuxembourgInitialDraftInventoryResult DeliveredInventory() =>
        LuxembourgInitialDraftInventoryResult.Refused(
            LuxembourgInitialDraftInventoryRefusal.EnumerationRefused,
            "not reached: the null budget is refused first",
            productRequestCount: 0,
            LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9306);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:4e91c7a2-0b36-4d58-8f27-63a519e0cb84",
            "urn:uuid:9c14f6d8-7a23-4b05-91e6-8d02537ace1b",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9306)).Request;
    }

    /// <summary>Counts robots apart from product sends, and answers however a case needs.</summary>
    /// <remarks>
    /// <c>null</c> from <paramref name="respond"/> means 503, which fails pre-header and is the one
    /// shape that exercises the retry path.
    /// </remarks>
    private sealed class CountingHandler(
        Func<int, string?>? respond = null,
        bool throwPreHeader = false)
        : System.Net.Http.HttpMessageHandler
    {
        private int _sendCount;
        private int _robotsSends;

        internal int SendCount => Volatile.Read(ref _sendCount);

        internal int RobotsSends => Volatile.Read(ref _robotsSends);

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount) - 1;
            if (request.RequestUri!.AbsolutePath.EndsWith("robots.txt", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _robotsSends);
                return Task.FromResult(RobotsAllowAll(request));
            }

            if (throwPreHeader)
            {
                throw new System.Net.Http.HttpRequestException(
                    System.Net.Http.HttpRequestError.ConnectionError, "simulated pre-header failure");
            }

            var body = respond?.Invoke(ordinal);
            return Task.FromResult(body is null
                ? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    Version = System.Net.HttpVersion.Version11,
                    RequestMessage = request,
                    Content = new System.Net.Http.StringContent(string.Empty),
                }
                : LuxembourgAcquisitionTestFixture.JsonResponse(request, body));
        }

        private static System.Net.Http.HttpResponseMessage RobotsAllowAll(
            System.Net.Http.HttpRequestMessage request)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("User-agent: *\nAllow: /\n");
            var content = new System.Net.Http.ByteArrayContent(bytes);
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
            content.Headers.TryAddWithoutValidation(
                "Content-Length",
                bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Version = System.Net.HttpVersion.Version11,
                RequestMessage = request,
                Content = content,
            };
        }
    }
}
