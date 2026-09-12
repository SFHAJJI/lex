using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The two entry points that let the OpinionRequest family be run at all, driven end to end.
/// </summary>
/// <remarks>
/// <para>
/// THE ADMIT PATH IS ASSERTED FIRST AND DELIBERATELY. Every refusal case below would still pass on
/// an entry point that refuses everything, so the honest delivery is written first: it is the only
/// one that fails if a binder orders its parameters wrongly or a plan and its profile disagree.
/// </para>
/// <para>
/// These exist because the first head of this slice had none. I claimed the entry points could not
/// be reached offline and matched the draft graph family, which also has none; the case-law and
/// procedure-event families next door have had scripted-transport entry-point tests all along, and
/// a reviewer pointed at them. The claim was wrong and it was load-bearing: with no test through
/// these methods, changing the membership ordinal to 1 left every test green while the partition
/// guard read the subject's KIND instead of the subject.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestExecutorEntryPointTests
{
    private const string ReferralDate =
        LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    /// <summary>An honest whole-class delivery is admitted and receipted.</summary>
    [TestMethod]
    public async Task TheInventoryEntryPointCanDeliver()
    {
        var subjects = new[] { Request(1), Request(2) };
        var handler = InventoryTransport(subjects);
        var executor = ExecutorFor(handler);

        var result = await executor.RunLuxembourgOpinionRequestInventoryAsync(
            new LuxembourgOpinionRequestInventoryRunRequest(
                LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(),
                WireRequestBudget.OfWireRequests(1000)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(
            result.Refusal,
            $"the inventory must be able to succeed: {result.Refusal?.Code} "
                + result.Refusal?.CoreRefusalDetail);
        Assert.IsNotNull(result.Receipt);
        Assert.IsGreaterThan(0, result.ProductRequestCount);
    }

    /// <summary>An honest delivery for an inventory-issued batch is admitted and receipted.</summary>
    [TestMethod]
    public async Task TheGraphEntryPointCanDeliverForAnInventoryIssuedBatch()
    {
        var batch = new[] { Request(1), Request(2) };
        var handler = GraphTransport(
            GraphRow(batch[0], ReferralDate, "2004-03-11"),
            GraphRow(batch[1], ReferralDate, "2004-04-01"));

        var result = await RunGraphAsync(handler, batch);

        Assert.IsNull(
            result.Refusal,
            $"an honest batch delivery must be admitted: {result.Refusal?.Code} "
                + result.Refusal?.CoreRefusalDetail);
        Assert.IsNotNull(result.Receipt);
        Assert.IsGreaterThan(0, result.ProductRequestCount);
    }

    /// <summary>
    /// A row naming a request outside the batch refuses, and the offending request is retained.
    /// </summary>
    /// <remarks>
    /// THE SAFETY BOUNDARY THIS FAMILY'S BATCHING EXISTS FOR. A delivery carrying a subject this run
    /// never asked about is not a bigger answer, it is an answer to a different question - and an
    /// absence derived over it would be about a class this batch never bounded.
    /// </remarks>
    [TestMethod]
    public async Task ARowNamingARequestOutsideTheBatchRefusesAndNamesIt()
    {
        var batch = new[] { Request(1), Request(2) };
        var stranger = Request(97);
        var handler = GraphTransport(
            GraphRow(batch[0], ReferralDate, "2004-03-11"),
            GraphRow(stranger, ReferralDate, "2004-05-05"));

        var result = await RunGraphAsync(handler, batch);

        Assert.IsNotNull(result.Refusal, "a row from outside the batch is not an honest delivery.");
        Assert.AreEqual(
            EuEnumerationRefusal.DeliveredRowOutsidePartition,
            result.Refusal.Code);
        Assert.AreEqual(
            stranger,
            result.Refusal.OffendingKey,
            "the refusal must name the request that did not belong, not merely count it.");
    }

    /// <summary>
    /// A run stops at its budget, and the transport confirms nothing further was sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CEILING IS THE PATH'S, NOT A PLAN'S. The executor reads the publisher's count and then
    /// continues paging on its own until a short page or its page bound, so a figure derived outside
    /// the run bounds nothing - which is how I came to publish 120 as a ceiling when the structural
    /// worst case for the same packet is 22,276.
    /// </para>
    /// <para>
    /// Asserted on the TRANSPORT's own send count, not on the refusal alone. A stop that refuses
    /// while the requests still went out is a receipt, and the whole point is that they do not go.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ARunStopsAtItsBudgetAndSendsNothingFurther()
    {
        var batch = new[] { Request(1), Request(2) };

        // Two wire requests: the robots fetch, then one product request. The second product request
        // this run would make has no budget left to reserve.
        var handler = GraphTransport(
            GraphRow(batch[0], ReferralDate, "2004-03-11"),
            GraphRow(batch[1], ReferralDate, "2004-04-01"));
        var executor = ExecutorFor(handler);

        var result = await executor.RunLuxembourgOpinionRequestGraphAsync(
            LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                batch,
                InventoryOver(batch),
                0,
                NewUrn(),
                Source(),
                WireRequestBudget.OfWireRequests(2)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuEnumerationRefusal.WireBudgetExhausted, result.Refusal.Code);
        Assert.AreEqual(
            2, handler.SendCount,
            "the budget is a stop: robots plus one product request, and nothing after it.");
    }

    /// <summary>A budget that cannot cover robots and one product request is refused outright.</summary>
    /// <remarks>
    /// A ceiling of one would refuse every run before it began, which is a configuration error
    /// rather than a delivery outcome - so it is rejected where it is written, not where it fires.
    /// </remarks>
    [TestMethod]
    public void ABudgetTooSmallToSendAnythingIsRefusedWhereItIsWritten()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => WireRequestBudget.OfWireRequests(1));

        // NOTHING IS SPENT UNTIL SOMETHING IS ABOUT TO BE SENT. This pinned 1 on the first head,
        // because robots was charged in the constructor. That is right for one session and wrong by
        // one for every session after it, which is what let a reused budget under-count.
        Assert.AreEqual(
            0, WireRequestBudget.OfWireRequests(2).Spent,
            "a budget that has been built has not yet sent anything.");
    }

    /// <summary>
    /// A budget reused across runs still holds, because robots is charged per session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFECT THIS REPLACES. Robots was spent once at construction, but a session sends one
    /// robots fetch and every entry-point call opens its own session - so N runs sharing a budget
    /// put N robots fetches on the wire while charging one. Measured on the transport at the time:
    /// two runs sent 10 and the budget counted 9.
    /// </para>
    /// <para>
    /// Asserted against <see cref="WireRequestBudget.Limit"/> itself rather than a hand-written
    /// number, because the claim is not "two sends happened", it is "the ceiling was never
    /// exceeded". A literal would keep agreeing with itself if the limit later moved.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AReusedBudgetStillBoundsTheWire()
    {
        var batch = new[] { Request(1), Request(2) };
        var handler = GraphTransportServingRobotsByPath(
            GraphRow(batch[0], ReferralDate, "2004-03-11"),
            GraphRow(batch[1], ReferralDate, "2004-04-01"));
        var executor = ExecutorFor(handler);
        var budget = WireRequestBudget.OfWireRequests(2);

        var outcomes = new List<EuEnumerationRefusal?>();
        for (var run = 0; run < 2; run++)
        {
            var result = await executor.RunLuxembourgOpinionRequestGraphAsync(
                LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                    LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                    batch,
                    InventoryOver(batch),
                    0,
                    NewUrn(),
                    Source(),
                    budget),
                LuxembourgSourceWitness(),
                CancellationToken.None);
            outcomes.Add(result.Refusal?.Code);
        }

        Assert.IsLessThanOrEqualTo(
            budget.Limit,
            handler.SendCount,
            "a second run reusing a spent budget must not put another robots fetch on the wire.");
        CollectionAssert.AreEqual(
            new EuEnumerationRefusal?[]
            {
                EuEnumerationRefusal.WireBudgetExhausted,
                EuEnumerationRefusal.WireBudgetExhausted,
            },
            outcomes.ToArray(),
            "the second run is refused before it opens a session, not after it has sent robots.");
    }

    /// <summary>
    /// The inventory door charges its own session's robots too.
    /// </summary>
    /// <remarks>
    /// Written separately from the graph door rather than folded into it. A single test that ran
    /// inventory and then graph under one budget would pass with either reservation present, so it
    /// could not say which door was holding - and this is the door a canary opens first.
    /// </remarks>
    [TestMethod]
    public async Task AReusedBudgetStillBoundsTheWireOnTheInventoryDoor()
    {
        var subjects = new[] { Request(1), Request(2) };
        var handler = TransportServingRobotsByPath(
            InventoryProjection, subjects.Select(InventoryRow).ToArray());
        var executor = ExecutorFor(handler);
        var budget = WireRequestBudget.OfWireRequests(2);

        var codes = new List<EuEnumerationRefusal?>();
        for (var run = 0; run < 2; run++)
        {
            var result = await executor.RunLuxembourgOpinionRequestInventoryAsync(
                new LuxembourgOpinionRequestInventoryRunRequest(
                    LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(), budget),
                LuxembourgSourceWitness(),
                CancellationToken.None);
            codes.Add(result.Refusal?.Code);
        }

        Assert.IsLessThanOrEqualTo(
            budget.Limit,
            handler.SendCount,
            "the inventory door must not open a second session on a spent budget.");
        CollectionAssert.AreEqual(
            new EuEnumerationRefusal?[]
            {
                EuEnumerationRefusal.WireBudgetExhausted,
                EuEnumerationRefusal.WireBudgetExhausted,
            },
            codes.ToArray());
    }

    /// <summary>
    /// A run request cannot be built without the budget it documents as required.
    /// </summary>
    /// <remarks>
    /// A POSITIONAL RECORD CHECKS NOTHING. This one documented the budget as required and then
    /// accepted null positionally; the null reached the shared pass loop, whose budget parameter is
    /// optional by type, and was read there as "no ceiling asked for". The ceiling was off and
    /// nothing said so, which is the one failure mode a ceiling must not have. Proven on the
    /// constructor rather than on a run, because a run that cannot be built cannot send.
    /// </remarks>
    [TestMethod]
    public void ARunRequestCannotBeBuiltWithoutItsBudget()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new LuxembourgOpinionRequestInventoryRunRequest(
                LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), NewUrn(), Source(), null!),
            "the inventory door accepted null here.");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                [Request(1)],
                InventoryOver([Request(1)]),
                0,
                NewUrn(),
                Source(),
                null!),
            "and the graph door must keep refusing it, or the two disagree again.");
    }

    /// <summary>
    /// The membership position is resolved by name, so a keyset change cannot silently rewire it.
    /// </summary>
    /// <remarks>
    /// The first head of this slice passed a literal ordinal. Written as an index it survives a
    /// keyset that gains a column in front of the subject, and then checks batch membership against
    /// whatever now sits at position zero - here, the subject's KIND, which every row in a batch
    /// shares, so no delivery would ever fall outside a batch again.
    /// </remarks>
    [TestMethod]
    public void TheMembershipPositionIsResolvedByNameAndFailsClosed()
    {
        var profile = LuxembourgOpinionRequestGraphDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(
            profile.CursorVariables.IndexOf("key_1"),
            EuRepeatedEnumerationExecutor.OpinionRequestGraphBatchMembershipKeyOrdinal(profile),
            "the ordinal is the position key_1 actually occupies.");

        // A profile whose keys were renamed stands in for any keyset that stopped carrying the
        // request at key_1. Built from this family's own profile so nothing but the names differs.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => EuRepeatedEnumerationExecutor.OpinionRequestGraphBatchMembershipKeyOrdinal(
                ProfileWithoutRequestKey()),
            "a profile that does not carry the request at key_1 is refused rather than run against.");
    }

    private static RepeatedEnumerationInterpretationProfile ProfileWithoutRequestKey()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();
        var renamed = profile.CursorVariables.Select(static value => "z_" + value).ToArray();
        return new RepeatedEnumerationInterpretationProfile(
            profile.Schema,
            profile.Dialect,
            profile.ExpectedMediaType,
            profile.CursorEnvelopeIdentity,
            profile.MaximumDeliverableRows,
            profile.ThresholdDetectorIdentity,
            profile.CountQueryFamilyRef,
            profile.PageQueryFamilyRef,
            profile.CountVariable,
            profile.ProjectionVariables.Select(static value =>
                value.StartsWith("key_", StringComparison.Ordinal) ? "z_" + value : value).ToArray(),
            renamed,
            renamed,
            profile.SelectionParameterNames,
            profile.PassParameterName,
            profile.CursorParameterNames,
            profile.HasCursorParameterName,
            profile.TerminalPagePolicy);
    }

    /// <summary>
    /// Robots by PATH, not by send ordinal.
    /// </summary>
    /// <remarks>
    /// <c>AllowRobotsThenHandler</c> answers robots for send ordinal 0 only, which leaves a second
    /// session's robots fetch answered with a product body. That would make a reuse test fail on
    /// robots parsing rather than on the ceiling it is measuring.
    /// </remarks>
    private static LuxembourgAcquisitionTestFixture.SequencedHandler GraphTransportServingRobotsByPath(
        params string[] rows) =>
        TransportServingRobotsByPath(GraphProjection, rows);

    private static LuxembourgAcquisitionTestFixture.SequencedHandler TransportServingRobotsByPath(
        string[] projection,
        string[] rows)
    {
        var scripted = new[] { CountJson(rows.Length), RowsJson(projection, rows) };
        var product = 0;
        return new LuxembourgAcquisitionTestFixture.SequencedHandler((_, request) =>
            request.RequestUri!.AbsolutePath.EndsWith("robots.txt", StringComparison.Ordinal)
                ? RobotsAllowAll(request)
                : LuxembourgAcquisitionTestFixture.JsonResponse(
                    request, scripted[Interlocked.Increment(ref product) - 1 & 1]));
    }

    private static System.Net.Http.HttpResponseMessage RobotsAllowAll(
        System.Net.Http.HttpRequestMessage request)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("User-agent: *\nAllow: /\n");
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

    private static Task<EuEnumerationRunResult> RunGraphAsync(
        LuxembourgAcquisitionTestFixture.SequencedHandler handler,
        IReadOnlyList<string> batch)
    {
        var executor = ExecutorFor(handler);
        return executor.RunLuxembourgOpinionRequestGraphAsync(
            LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                batch,
                InventoryOver(batch),
                0,
                NewUrn(),
                Source(),
                WireRequestBudget.OfWireRequests(1000)),
            LuxembourgSourceWitness(),
            CancellationToken.None);
    }

    private static EuRepeatedEnumerationExecutor ExecutorFor(
        LuxembourgAcquisitionTestFixture.SequencedHandler handler) =>
        new(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
            handler);

    /// <summary>
    /// Robots, then two passes of count-and-page over the same rows.
    /// </summary>
    /// <remarks>
    /// Both passes deliver the same rows at different page limits, which is what makes their
    /// agreement evidence rather than repetition.
    /// </remarks>
    private static LuxembourgAcquisitionTestFixture.SequencedHandler GraphTransport(params string[] rows) =>
        Transport(GraphProjection, rows);

    private static LuxembourgAcquisitionTestFixture.SequencedHandler InventoryTransport(
        IReadOnlyList<string> subjects) =>
        Transport(InventoryProjection, subjects.Select(InventoryRow).ToArray());

    private static LuxembourgAcquisitionTestFixture.SequencedHandler Transport(
        string[] projection,
        string[] rows)
    {
        var count = CountJson(rows.Length);
        var page = RowsJson(projection, rows);
        var scripted = new[] { count, page, count, page };
        return LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
        {
            var index = ordinal - 1;
            Assert.IsLessThan(
                scripted.Length, index, "the run asked for more responses than this script holds.");
            return LuxembourgAcquisitionTestFixture.JsonResponse(request, scripted[index]);
        });
    }

    private static readonly string[] GraphProjection =
    [
        "request", "request_kind", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
        "multiplicity",
        "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
    ];

    private static readonly string[] InventoryProjection =
    [
        "request", "request_kind", "multiplicity", "key_1", "key_2",
    ];

    private static string GraphRow(string subject, string predicate, string value) =>
        "{\"request\":{\"type\":\"uri\",\"value\":\"" + subject + "\"},"
        + "\"request_kind\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
        + "\"predicate\":{\"type\":\"uri\",\"value\":\"" + predicate + "\"},"
        + "\"value\":{\"type\":\"literal\",\"value\":\"" + value + "\"},"
        + "\"value_kind\":{\"type\":\"literal\",\"value\":\"literal\"},"
        + "\"datatype_iri\":{\"type\":\"literal\",\"value\":\"\"},"
        + "\"language_tag\":{\"type\":\"literal\",\"value\":\"\"},"
        + "\"multiplicity\":{\"type\":\"typed-literal\","
        + "\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\",\"value\":\"1\"},"
        + "\"key_1\":{\"type\":\"literal\",\"value\":\"" + subject + "\"},"
        + "\"key_2\":{\"type\":\"literal\",\"value\":\"" + IriKind + "\"},"
        + "\"key_3\":{\"type\":\"literal\",\"value\":\"" + predicate + "\"},"
        + "\"key_4\":{\"type\":\"literal\",\"value\":\""
        + LuxembourgPublisherCursorCodec.ComputeKey(value) + "\"},"
        + "\"key_5\":{\"type\":\"literal\",\"value\":\"literal\"},"
        + "\"key_6\":{\"type\":\"literal\",\"value\":\"\"},"
        + "\"key_7\":{\"type\":\"literal\",\"value\":\"\"}}";

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
        + string.Join("\",\"", projection) + "\"]},"
        + "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":["
        + string.Join(',', rows) + "]}}";

    private static LuxembourgOpinionRequestInventoryCitation InventoryOver(IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = Lex.V3.Tests.Contracts.Source.Absence.AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (witnessPlan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9601);
        return witnessPlan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9601)).Request;
    }

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-opinion-request-executor-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
