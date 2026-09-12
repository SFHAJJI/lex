using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the canary's two runs may claim about what they cost, and how that is checked.
/// </summary>
/// <remarks>
/// <para>
/// THE SHAPE UNDER TEST IS THE CANARY'S OWN: a whole-class inventory run and one inventory-issued
/// batch, sharing ONE budget instance, because a ceiling over a sweep is the only ceiling worth
/// stating about a sweep. Two independent budgets would each be honoured and together bound nothing.
/// </para>
/// <para>
/// AND THE CHECK IS AGAINST THE TRANSPORT, not against the counters agreeing with each other. Every
/// identity below could hold perfectly while the runs sent a different number of requests than they
/// recorded; the send count is the only witness that is not part of the accounting being checked.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestBudgetEvidenceTests
{
    /// <summary>The ceiling agreed for the bounded canary attempt.</summary>
    private const int CanaryCeiling = 250;

    /// <summary>
    /// The two runs share one budget, and the snapshots reconcile to the transport's own sends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identity is cumulative, not per-run: the graph snapshot includes everything the inventory
    /// reserved. A reader comparing one snapshot against one run's own request count is wrong by
    /// exactly what the earlier run spent, which is why this subtracts rather than compares.
    /// </para>
    /// <para>
    /// THE INVENTORY IDENTITY IS ASSERTED AFTER THE GRAPH RUN HAS FINISHED. That ordering is the
    /// whole point: if the result held the live budget instead of a snapshot, the inventory's own
    /// figure would have moved by the time anyone read it, and it would read as though the inventory
    /// had sent the graph's requests too.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheCanarysTwoRunsShareOneBudgetAndReconcileToTheTransport()
    {
        var subjects = new[] { Request(1), Request(2) };
        var handler = CanaryTransport(subjects);
        var custody = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var clock = new LuxembourgAcquisitionTestFixture.FixedTimeProvider();
        var budget = WireRequestBudget.OfWireRequests(CanaryCeiling);

        var inventory = await new LuxembourgOpinionRequestInventoryProducer(custody, clock, handler)
            .RunAsync(
                new LuxembourgOpinionRequestInventoryRunRequest(
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(), budget),
                Witness(),
                CancellationToken.None);
        Assert.IsTrue(inventory.Delivered, $"{inventory.Refusal}: {inventory.Detail}");

        var graph = await new LuxembourgOpinionRequestGraphProducer(custody, clock, handler)
            .RunAsync(
                LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                    LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                    inventory.AddressableInOrder(),
                    inventory.Citation!,
                    0,
                    NewUrn(),
                    Source(),
                    budget),
                Witness(),
                CancellationToken.None);
        Assert.IsTrue(graph.Delivered, $"{graph.Refusal}: {graph.Detail}");

        Assert.AreEqual(
            inventory.ProductRequestCount + 1,
            inventory.WireBudget.Spent,
            "the inventory's own reservations are its product attempts plus the one robots fetch its "
                + "session opened - and this still reads true after a later run spent more.");
        Assert.AreEqual(
            inventory.WireBudget.Spent + graph.ProductRequestCount + 1,
            graph.WireBudget.Spent,
            "the graph snapshot is cumulative: everything the inventory reserved, plus its own "
                + "attempts, plus its own session's robots fetch.");
        Assert.AreEqual(
            inventory.ProductRequestCount + graph.ProductRequestCount + 2,
            graph.WireBudget.Spent,
            "so the finished canary is both runs' attempts plus exactly two robots fetches.");
        Assert.AreEqual(
            2,
            handler.RobotsSends,
            "and those two must be two ACTUAL robots sends, not two increments nobody made.");

        Assert.AreEqual(CanaryCeiling, inventory.WireBudget.Limit);
        Assert.AreEqual(CanaryCeiling, graph.WireBudget.Limit, "one budget, so one limit throughout.");
        Assert.IsGreaterThanOrEqualTo(
            inventory.WireBudget.Spent, graph.WireBudget.Spent, "a reservation is never returned.");
        Assert.IsLessThanOrEqualTo(
            graph.WireBudget.Limit, graph.WireBudget.Spent, "the canary never exceeded its ceiling.");

        Assert.AreEqual(
            handler.SendCount,
            graph.WireBudget.Spent,
            "THE WITNESS THAT IS NOT PART OF THE ACCOUNTING. Spent counts reservations taken "
                + "immediately before send; if it disagrees with what the transport actually sent, "
                + "that is a finding rather than a canary.");
    }

    /// <summary>
    /// A refused run still says what it spent.
    /// </summary>
    /// <remarks>
    /// The runs that most need reconciling are the ones that stopped, and a refusal that dropped its
    /// cost would leave the wire spend of a failed attempt unaccountable. The successful two-session
    /// identity is deliberately NOT applied here: no session completed, so there is nothing to
    /// reconcile it against, and asserting it anyway would be arithmetic dressed as evidence.
    /// </remarks>
    [TestMethod]
    public async Task ARefusedRunStillCarriesItsSnapshot()
    {
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((_, request) =>
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Version = System.Net.HttpVersion.Version11,
                RequestMessage = request,
                Content = new System.Net.Http.StringContent(string.Empty),
            });
        var budget = WireRequestBudget.OfWireRequests(CanaryCeiling);

        var inventory = await new LuxembourgOpinionRequestInventoryProducer(
                new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
                new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
                handler)
            .RunAsync(
                new LuxembourgOpinionRequestInventoryRunRequest(
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(), budget),
                Witness(),
                CancellationToken.None);

        Assert.IsFalse(inventory.Delivered, "this publisher answered 503 to everything.");
        Assert.AreEqual(CanaryCeiling, inventory.WireBudget.Limit);
        Assert.IsGreaterThan(
            0, inventory.WireBudget.Spent, "a run that reached the publisher spent something.");
        Assert.AreEqual(
            handler.SendCount,
            inventory.WireBudget.Spent,
            "and a refusal reconciles to the transport exactly as a delivery does.");
    }

    /// <summary>
    /// A run stopped BY the ceiling says so, in the snapshot Item 7 will read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SIGNAL CLAUSE 6 DEPENDS ON. "Any exhaustion or incomplete run is a dated
    /// magnitude/refusal finding" is a question the canary receipt has to be able to answer, and
    /// before this regression <c>Exhausted</c> could be replaced with a constant false and the whole
    /// suite still passed - so the one flag a finding would rest on had never been shown capable of
    /// failing.
    /// </para>
    /// <para>
    /// Driven to the EXACT ceiling rather than past it: a budget of two covers this run's robots
    /// fetch and its count, and the page the count implies is the request that has nothing left to
    /// reserve. So the stop lands on a request the run genuinely wanted to send, which is the only
    /// arrangement where Spent == Limit means the ceiling was reached rather than merely quoted.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ARunStoppedByTheCeilingCarriesASnapshotThatSaysSo()
    {
        var subjects = new[] { Request(1), Request(2) };
        var handler = CanaryTransport(subjects);
        var budget = WireRequestBudget.OfWireRequests(2);

        var inventory = await new LuxembourgOpinionRequestInventoryProducer(
                new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
                new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
                handler)
            .RunAsync(
                new LuxembourgOpinionRequestInventoryRunRequest(
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(), budget),
                Witness(),
                CancellationToken.None);

        Assert.IsFalse(inventory.Delivered, "the run could not afford the page its count implied.");
        StringAssert.Contains(
            inventory.Detail!,
            nameof(EuEnumerationRefusal.WireBudgetExhausted),
            "and it must have stopped on the CEILING, not on something else that also refuses.");

        Assert.AreEqual(
            budget.Limit, inventory.WireBudget.Spent, "every wire request the ceiling allowed.");
        Assert.IsTrue(
            inventory.WireBudget.Exhausted,
            "THE FLAG A FINDING RESTS ON. A run that stopped at its ceiling and reports otherwise "
                + "would be recorded as an ordinary refusal, and the magnitude nobody measured would "
                + "go unnoticed.");
        Assert.AreEqual(
            handler.SendCount,
            inventory.WireBudget.Spent,
            "reconciled to the transport like every other snapshot: reservations are taken "
                + "immediately before send, so at the ceiling they are exactly the sends.");
    }

    /// <summary>The public door refuses a null budget rather than reading one.</summary>
    /// <remarks>
    /// Kept where the result constructors' guards were removed, because the difference is
    /// reachability: a caller compiling without nullable annotations reaches this without ever
    /// writing <c>null!</c>. Asserted directly at the boundary, so the guard is a claim a test can
    /// falsify rather than a line nothing drives.
    /// </remarks>
    [TestMethod]
    public void TheSnapshotDoorRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => WireBudgetSnapshot.Of(null!));

    /// <summary>A snapshot is a reading, not a handle on the budget it was read from.</summary>
    /// <remarks>
    /// Pinned on the type rather than only through a run: a result exposing
    /// <see cref="WireRequestBudget"/> itself would let any holder of retained evidence reserve
    /// against a live ceiling, and would report the sweep's final figure for every run in it.
    /// </remarks>
    [TestMethod]
    public void TheSnapshotCarriesNoHandleOnTheBudget()
    {
        var budget = WireRequestBudget.OfWireRequests(CanaryCeiling);
        var before = WireBudgetSnapshot.Of(budget);
        budget.TryReserveAttempt();
        var after = WireBudgetSnapshot.Of(budget);

        Assert.AreEqual(0, before.Spent, "the first reading was taken before anything was reserved.");
        Assert.AreEqual(1, after.Spent, "the second reading saw the reservation.");
        Assert.AreNotEqual(before, after, "two readings of a changed budget are not the same value.");

        Assert.IsFalse(
            typeof(WireBudgetSnapshot).GetProperties()
                .Any(static value => value.PropertyType == typeof(WireRequestBudget)),
            "a property typed as the live budget would hand a reservation door to a reader.");
        CollectionAssert.AreEquivalent(
            new[] { "Limit", "Spent", "Exhausted" },
            typeof(WireBudgetSnapshot).GetProperties().Select(static value => value.Name).ToArray());
    }

    /// <summary>
    /// Robots by PATH, then the inventory session's script, then the batch session's.
    /// </summary>
    /// <remarks>
    /// Two sessions run through one transport here, which is what makes the send count a shared
    /// witness. Robots is answered on its own path rather than on send ordinal 0, because the second
    /// session's robots fetch is not the transport's first send.
    /// </remarks>
    private static CountingHandler CanaryTransport(IReadOnlyList<string> subjects)
    {
        var inventoryPage = RowsJson(
            ["request", "request_kind", "multiplicity", "key_1", "key_2"],
            subjects.Select(InventoryRow).ToArray());
        var inventoryCount = CountJson(subjects.Count);

        // KEY ORDER, or the run refuses CursorDidNotAdvance before reaching anything this test is
        // about: jolux#referralDate sorts before rdf-syntax-ns#type for the same subject.
        var graphRows = new[]
        {
            GraphRow(subjects[0], ReferralDate, "2004-03-11", iri: false),
            GraphRow(subjects[0], RdfType, RequestClass, iri: true),
            GraphRow(subjects[1], RdfType, RequestClass, iri: true),
        };
        var graphPage = RowsJson(GraphProjection, graphRows);
        var graphCount = CountJson(graphRows.Length);

        var scripted = new[]
        {
            inventoryCount, inventoryPage, inventoryCount, inventoryPage,
            graphCount, graphPage, graphCount, graphPage,
        };
        var product = 0;
        return new CountingHandler(request =>
        {
            var index = Interlocked.Increment(ref product) - 1;
            Assert.IsLessThan(
                scripted.Length, index, "the canary asked for more responses than this script holds.");
            return LuxembourgAcquisitionTestFixture.JsonResponse(request, scripted[index]);
        });
    }

    /// <summary>Counts robots sends apart from product sends, so the two can be reconciled.</summary>
    private sealed class CountingHandler(
        Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond)
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
            Interlocked.Increment(ref _sendCount);
            if (request.RequestUri!.AbsolutePath.EndsWith("robots.txt", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _robotsSends);
                return Task.FromResult(RobotsAllowAll(request));
            }

            return Task.FromResult(respond(request));
        }
    }

    private static System.Net.Http.HttpResponseMessage RobotsAllowAll(
        System.Net.Http.HttpRequestMessage request)
    {
        var bytes = Encoding.UTF8.GetBytes("User-agent: *\nAllow: /\n");
        var content = new System.Net.Http.ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation(
            "Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture));
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Version = System.Net.HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    private const string ReferralDate =
        "http://data.legilux.public.lu/resource/ontology/jolux#referralDate";

    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    private static readonly string RequestClass =
        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri;

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    private static readonly string[] GraphProjection =
    [
        "request", "request_kind", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
        "multiplicity",
        "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
    ];

    private static string GraphRow(string subject, string predicate, string value, bool iri)
    {
        var valueKind = iri ? IriKind : "literal";
        var valueJson = iri
            ? "{\"type\":\"uri\",\"value\":\"" + value + "\"}"
            : "{\"type\":\"literal\",\"value\":\"" + value + "\"}";
        return "{\"request\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
            + "\"request_kind\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
            + "\"predicate\":{\"type\":\"uri\",\"value\":\"" + predicate + "\"},"
            + "\"value\":" + valueJson + ","
            + "\"value_kind\":{\"type\":\"literal\",\"value\":\"" + valueKind + "\"},"
            + "\"datatype_iri\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"language_tag\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"multiplicity\":{\"type\":\"typed-literal\","
            + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
            + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
            + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
            + "\"key_3\":{\"type\":\"literal\",\"value\":\"" + predicate + "\"},"
            + "\"key_4\":{\"type\":\"literal\",\"value\":\""
            + LuxembourgPublisherCursorCodec.ComputeKey(value) + "\"},"
            + "\"key_5\":{\"type\":\"literal\",\"value\":\"" + valueKind + "\"},"
            + "\"key_6\":{\"type\":\"literal\",\"value\":\"\"},"
            + "\"key_7\":{\"type\":\"literal\",\"value\":\"\"}}";
    }

    private static string InventoryRow(string subject) =>
        "{\"request\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
        + "\"request_kind\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
        + "\"multiplicity\":{\"type\":\"typed-literal\","
        + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
        + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
        + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"}}";

    private static string CountJson(long count) =>
        "{\"head\":{\"link\":[],\"vars\":[\"count\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{\"count\":"
        + "{\"type\":\"typed-literal\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\","
        + "\"value\":\"" + count.ToString(CultureInfo.InvariantCulture) + "\"}}]}}";

    private static string RowsJson(string[] projection, IReadOnlyList<string> rows) =>
        "{\"head\":{\"link\":[],\"vars\":[\""
        + string.Join("\",\"", projection)
        + "\"]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + string.Join(',', rows) + "]}}";

    private static BoundMachineRequest Witness()
    {
        var (witnessPlan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9805);
        return witnessPlan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9805)).Request;
    }

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-opinion-request-budget-evidence-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
