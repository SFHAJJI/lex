using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The entry points #579 measured as unbudgeted, each now met with an already-spent ceiling.
/// </summary>
/// <remarks>
/// <para>
/// WHAT EACH TEST ESTABLISHES IS THE POSITION, NOT THE REFUSAL. Asserting the refusal code alone
/// passes on a door that fetches robots, discovers the exhaustion afterwards and then reports it
/// honestly - which is precisely the behaviour #579 records as the exposure. Only
/// <c>SendCount == 0</c> separates a door that stopped from a door that sent and then noticed, so
/// every test here asserts on the transport.
/// </para>
/// <para>
/// The budget floor is two: <c>OfWireRequests</c> admits no run whose ceiling cannot cover its robots
/// fetch plus one product request, so each fixture spends both reservations itself to reach the
/// exhausted state the door must be met with.
/// </para>
/// </remarks>
[TestClass]
public sealed class UnbudgetedEntryPointClosureTests
{
    private const string Act =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string EuEli = "http://data.europa.eu/eli/dir/2016/680/oj";

    /// <summary>
    /// A watermark in the offset-bearing lexical form the plan's frozen-order semantics require.
    /// A seconds-precision form refuses as <c>StartPositionShapeWithoutFrozenOrderSemantics</c>.
    /// </summary>
    private const string BoundaryWatermark = "2023-03-03T20:43:13.158+01:00";

    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheCaseLawDoor()
    {
        var (executor, handler) = Harness();
        var result = await executor.RunCaseLawLinksAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [Act],
                NewUrn(),
                EuAcquisitionTestFixture.BuildRendererSource(1),
                Spent()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        AssertStoppedBeforeTheWire(result, handler);
    }

    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheNationalImplementingMeasureDoor()
    {
        var (executor, handler) = Harness();
        var result = await executor.RunNationalImplementingMeasuresAsync(
            new EuNationalImplementingMeasureRunRequest(
                EuNationalImplementingMeasureDiscoveryPlan.Create(),
                NewUrn(),
                EuAcquisitionTestFixture.BuildRendererSource(1),
                Spent()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        AssertStoppedBeforeTheWire(result, handler);
    }

    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheTranspositionIdentityDoor()
    {
        var (executor, handler) = Harness();
        var result = await executor.RunLuxembourgTranspositionIdentitiesAsync(
            new LuxembourgTranspositionIdentityRunRequest(
                LuxembourgTranspositionIdentityDiscoveryPlan.Create(),
                [EuEli],
                NewUrn(),
                EuAcquisitionTestFixture.BuildRendererSource(1),
                Spent()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        AssertStoppedBeforeTheWire(result, handler);
    }

    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheOpinionDoor()
    {
        var (executor, handler) = Harness();
        var result = await executor.RunLuxembourgOpinionsAsync(
            new LuxembourgOpinionRunRequest(
                LuxembourgOpinionDiscoveryPlan.Create(),
                NewUrn(),
                EuAcquisitionTestFixture.BuildRendererSource(1),
                Spent()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        AssertStoppedBeforeTheWire(result, handler);
    }

    // ---- #579's EU half: the four doors that took no ceiling at all. ----

    /// <summary>A spent ceiling stops the census partition before its session.</summary>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheCensusPartitionDoor()
    {
        var (executor, handler) = Harness();
        var (plan, planId) = EuAcquisitionTestFixture.BuildCensusPlan();

        var result = await executor.RunCensusPartitionAsync(
            new EuCensusPartitionRunRequest(
                plan, planId, "32016L0680", EuAcquisitionTestFixture.BuildRendererSource(1), Spent()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        AssertStoppedBeforeTheWire(result, handler);
    }

    /// <summary>A spent ceiling stops the object-facts partition before its session.</summary>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheObjectFactsPartitionDoor()
    {
        var (executor, handler) = Harness();
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();

        var result = await executor.RunObjectFactsPartitionAsync(
            new EuObjectFactsPartitionRunRequest(
                plan, planId, EuObjectFactsQuerySet.ObjectFacts, [Act],
                EuAcquisitionTestFixture.BuildRendererSource(1), Spent()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        AssertStoppedBeforeTheWire(result, handler);
    }

    /// <summary>A spent ceiling stops the witness traversal before its session.</summary>
    /// <remarks>
    /// This door had the least predictable cost in the file and no ceiling at all:
    /// <c>MaximumWitnessPageRequests</c> bounds EACH BATCH rather than the run, so a pack of eighty
    /// two batches could walk eighty two times that many pages with nothing counting the total.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheWitnessTraversalDoor()
    {
        var (executor, handler) = Harness();

        var result = await executor.RunWitnessTraversalAsync(
            [WitnessPlan()],
            EuAcquisitionTestFixture.BuildRendererSource(7),
            EuAcquisitionTestFixture.SourceWitness(),
            Spent(),
            CancellationToken.None);

        Assert.AreEqual(
            EuWitnessTraversalRefusal.WireBudgetExhausted,
            result.Refusal?.Code,
            "an exhausted budget must refuse by name rather than by transport failure.");
        Assert.AreEqual(0, result.ProductRequestCount);
        Assert.AreEqual(
            0, handler.SendCount,
            "nothing may reach the publisher once the budget is spent - not even robots.");
    }

    /// <summary>A spent ceiling stops the document fetch before its session.</summary>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheDocumentFetchDoor()
    {
        var (executor, handler) = Harness();

        var result = await executor.RunDocumentFetchAsync(
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            Spent(),
            CancellationToken.None);

        Assert.AreEqual(
            EuDocumentFetchAttemptRefusal.WireBudgetExhausted,
            result.Refusal,
            "an exhausted budget must refuse by name rather than by transport failure.");
        Assert.IsNull(result.Evidence);
        Assert.AreEqual(
            0, handler.SendCount,
            "nothing may reach the publisher once the budget is spent - not even robots.");
    }

    /// <summary>
    /// The document fetch charges EVERY attempt, so its retry loop cannot outrun the ceiling -
    /// and every send is reserved, redirect hops included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST REQUEST BEING CHARGED SAYS NOTHING ABOUT THE FOURTH. A ceiling taken once per call
    /// would be wrong by the profile's whole retry allowance at its own limit, and this door is a
    /// BODY fetch where one request costs orders of magnitude more bytes than a SPARQL page. The
    /// budget here affords robots, its redirect hop, and exactly one attempt; the transport fails
    /// before headers, so the profile would allow another; the ceiling is what stops it.
    /// </para>
    /// <para>
    /// THE FIRST VERSION OF THIS TEST PINNED A CONTRADICTION AND CALLED IT A DESIGN CHOICE. It
    /// measured three sends against two reservations - the EU robots negotiation is answered with a
    /// 301, and the session sent the second hop with nothing reserving it - and wrote "a wire
    /// ceiling bounds reservations, not sockets" beside the numbers. Review pointed at the type:
    /// <c>WireRequestBudget</c> says it reserves EVERY wire request; #579 defines request ceilings
    /// as bounding requests; the owner's accepted 60 is recorded as a wire ceiling. A comment does
    /// not get to redefine a contract to match a measurement. The session now reserves every
    /// redirect hop it sends, and the number this test pins is the only one worth pinning:
    /// reservations equal sends.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheDocumentFetchReservesEveryAttemptAndNotOnlyTheFirst()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            _ => throw new HttpRequestException("transport failed before headers"));
        var executor = new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

        // Robots, its redirect hop, and exactly one attempt.
        var budget = WireRequestBudget.OfWireRequests(3);

        var result = await executor.RunDocumentFetchAsync(
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            budget,
            CancellationToken.None);

        Assert.AreEqual(
            EuDocumentFetchAttemptRefusal.WireBudgetExhausted,
            result.Refusal,
            "the SECOND attempt must be refused by the ceiling, not sent and then regretted.");
        Assert.AreEqual(
            3, handler.SendCount,
            "robots, the 301 hop it is answered with, and exactly one product attempt; the retry did not.");
        Assert.AreEqual(
            handler.SendCount, budget.Spent,
            "every send was reserved, the redirect hop included: reservations equal sends.");
        Assert.IsTrue(budget.Exhausted, "and the ceiling is what the second attempt met.");
    }

    /// <summary>
    /// A redirect hop the ceiling cannot afford is not sent, and the evidence names why.
    /// </summary>
    /// <remarks>
    /// The session's own gate, exercised at the session's own door rather than through an
    /// executor, because this is the one place the typed outcome is visible: an executor folds a
    /// session that did not start into "robots bootstrap refused", which is true, while the
    /// retained evidence carries the exact reason. The budget affords robots and nothing else; the
    /// 301 hop is the request it cannot afford.
    /// </remarks>
    [TestMethod]
    public async Task ARedirectHopTheCeilingCannotAffordIsNotSentAndIsNamedInTheEvidence()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal));
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends one reservation itself...");
        Assert.IsTrue(budget.TryReserveAttempt(), "...and the caller's own robots reservation is the other.");

        var start = await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
            EuAcquisitionTestFixture.SourceWitness(),
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            handler,
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            budget,
            CancellationToken.None);

        Assert.AreNotEqual(
            OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, start.Kind,
            "a session whose robots route could not be completed has not started.");
        Assert.AreEqual(
            1, handler.SendCount,
            "the first robots request went out; the hop it was redirected to did not.");
        Assert.AreEqual(2, budget.Spent, "and nothing was charged for what was not sent.");

        var outcome = start.Evidence?.Outcome as IncompleteHttpRouteOutcome;
        Assert.IsNotNull(outcome, "the route ended incomplete, with the hop it did send retained.");
        Assert.AreEqual(
            HttpRouteIncompleteReason.RedirectTargetNotSentWireBudgetExhausted,
            outcome!.Reason,
            "and the evidence says the ceiling, not the transport, is why.");
        Assert.HasCount(1, start.Evidence!.Hops, "exactly the hop that was sent is retained.");
    }

    /// <summary>
    /// A document fetch stopped at a redirect hop refuses as the ceiling, not as a transport failure.
    /// </summary>
    /// <remarks>
    /// The document channel is the only product channel that follows a redirect at all (same
    /// origin only), so it is the only one that can meet the session's gate mid-attempt. Without
    /// the mapping this pins, the door reported <c>ObservationNotExecuted</c> - "the transport
    /// failed" - where the ceiling had held. The GET is answered 303 to the same origin; the budget
    /// affords robots, its hop, and the GET itself, and not the GET's successor.
    /// </remarks>
    [TestMethod]
    public async Task ADocumentFetchStoppedAtARedirectHopRefusesByNameNotAsATransportFailure()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            request => SameOriginSeeOther(request));
        var executor = new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

        // Robots, its hop, and the GET. The GET's own 303 successor is the one request too many.
        var budget = WireRequestBudget.OfWireRequests(3);

        var result = await executor.RunDocumentFetchAsync(
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            budget,
            CancellationToken.None);

        Assert.AreEqual(
            EuDocumentFetchAttemptRefusal.WireBudgetExhausted,
            result.Refusal,
            $"the ceiling held at the redirect hop and must be named as such: {result.Detail}");
        StringAssert.Contains(result.Detail!, "redirect hop");
        Assert.AreEqual(3, handler.SendCount, "robots, its hop, the GET; not the GET's successor.");
        Assert.AreEqual(handler.SendCount, budget.Spent, "reservations equal sends.");
    }

    /// <summary>
    /// A redirect the route refuses on its own grounds is not charged to the ceiling: reservations
    /// equal sends even when the next hop is refused before it could be sent.
    /// </summary>
    /// <remarks>
    /// THE GATE'S POSITION, PINNED. It sits after every admission check because a reservation is a
    /// promise that a send follows; reserve first and refuse the target afterwards, and the ceiling
    /// is charged for a request that never went out. That was written in a comment in the first
    /// pass and nothing tested it. Here the GET is answered 303 to a DIFFERENT origin, which the
    /// document channel's own policy refuses as <c>RedirectTargetOriginNotAdmitted</c>; the budget
    /// affords one more than was sent, and must still hold exactly what was sent.
    /// </remarks>
    [TestMethod]
    public async Task ARedirectTheRouteRefusesIsNotChargedToTheCeiling()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            request => OffOriginSeeOther(request));
        var executor = new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

        // Robots, its hop, the GET - and one to spare, so a reservation made for a refused hop
        // would be visible as Spent exceeding SendCount rather than as exhaustion.
        var budget = WireRequestBudget.OfWireRequests(4);

        var result = await executor.RunDocumentFetchAsync(
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            budget,
            CancellationToken.None);

        // The premise: the route refused the target on its own grounds, not the ceiling's.
        var outcome = result.Evidence?.Outcome as IncompleteHttpRouteOutcome;
        Assert.IsNotNull(outcome, $"the off-origin redirect must end the route incomplete: {result.Refusal} {result.Detail}");
        Assert.AreEqual(HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted, outcome!.Reason);

        Assert.AreEqual(3, handler.SendCount, "robots, its hop, the GET; the refused target never went out.");
        Assert.AreEqual(
            handler.SendCount, budget.Spent,
            "and nothing was reserved for it: a gate placed before the admission checks would read 4 here.");
        Assert.IsFalse(budget.Exhausted);
    }

    /// <summary>A complete 303 to another origin, which the document channel's policy refuses.</summary>
    private static HttpResponseMessage OffOriginSeeOther(HttpRequestMessage request)
    {
        var content = new ByteArrayContent([]);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
        content.Headers.TryAddWithoutValidation("Content-Length", "0");
        return new HttpResponseMessage(System.Net.HttpStatusCode.SeeOther)
        {
            Version = System.Net.HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
            Headers = { Location = new Uri("https://op.europa.eu/resource/celex/00000000/elsewhere") },
        };
    }

    /// <summary>
    /// A list whose indexer and enumerator disagree cannot slip a second budget past the preflight.
    /// </summary>
    /// <remarks>
    /// FOUND IN REVIEW. <c>IReadOnlyList</c> promises nothing about immutability, and the adapter
    /// validated the list by indexer and then executed it by enumerator. A hostile list showed the
    /// run's budget to the first and another budget to the second: the preflight passed, the run
    /// sent on a counter the preflight never saw, and the refusal that came back named the wrong
    /// thing. The adapter now snapshots the list once at entry and reads only the snapshot.
    /// </remarks>
    [TestMethod]
    public async Task AListWhoseTwoViewsDisagreeCannotSlipASecondBudgetPastThePreflight()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var adapter = new EuQueryExecutionAdapter(
            store,
            new EuRepeatedEnumerationExecutor(
                store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler));

        var runBudget = WireRequestBudget.OfWireRequests(500);
        var otherBudget = WireRequestBudget.OfWireRequests(500);
        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var (objectFactsPlan, objectFactsPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();

        (EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness) Seed(WireRequestBudget budget) =>
            (new EuCensusPartitionRunRequest(
                    censusPlan, censusPlanId, "32016L0680",
                    EuAcquisitionTestFixture.BuildRendererSource(1), budget),
                EuAcquisitionTestFixture.SourceWitness());

        var hostile = new TwoFacedList(indexed: Seed(runBudget), enumerated: Seed(otherBudget));

        var result = await adapter.RunAsync(
            hostile,
            new EuObjectFactsBatchPolicy(
                objectFactsPlan, objectFactsPlanId,
                EuAcquisitionTestFixture.BuildRendererSource(2),
                EuAcquisitionTestFixture.SourceWitness()),
            EuAcquisitionTestFixture.BuildRendererSource(9),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1009),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new UnreachableEvidenceResolver(),
            runBudget,
            CancellationToken.None);

        Assert.AreEqual(
            EuQueryExecutionRefusal.CensusRequestCarriesADifferentWireBudget,
            result.Refusal?.Code,
            "the view the run would have executed is the view the preflight must judge.");
        Assert.AreEqual(0, handler.SendCount, "nothing reached the publisher on either counter.");
        Assert.AreEqual(0, runBudget.Spent);
        Assert.AreEqual(0, otherBudget.Spent);
    }

    /// <summary>
    /// A complete 303 to the same origin: a declared empty body, so the hop is finished rather than
    /// incomplete and the session's redirect logic - and the gate under test - is reached at all.
    /// </summary>
    /// <remarks>
    /// The first draft of this response carried no Content-Length, and the session correctly ended
    /// the route as HopIncomplete before any redirect logic ran; the gate under test was never
    /// reached and the test passed for the wrong reason's absence. The fixture's own TextResponse
    /// declares the length for exactly this reason, and this mirrors it.
    /// </remarks>
    private static HttpResponseMessage SameOriginSeeOther(HttpRequestMessage request)
    {
        var content = new ByteArrayContent([]);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
        content.Headers.TryAddWithoutValidation("Content-Length", "0");
        return new HttpResponseMessage(System.Net.HttpStatusCode.SeeOther)
        {
            Version = System.Net.HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
            Headers = { Location = new Uri(request.RequestUri!, "/resource/celex/00000000/redirected") },
        };
    }

    /// <summary>A list that shows one tuple to its indexer and another to its enumerator.</summary>
    private sealed class TwoFacedList(
        (EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness) indexed,
        (EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness) enumerated)
        : IReadOnlyList<(EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness)>
    {
        public int Count => 1;

        public (EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness) this[int index] => indexed;

        public IEnumerator<(EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness)> GetEnumerator()
        {
            yield return enumerated;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A traversal stopped part-way through refuses by name rather than throwing.
    /// </summary>
    /// <remarks>
    /// THE SPENT-BUDGET TESTS ABOVE DO NOT REACH THIS PATH, which is why it needs its own. They are
    /// met with an exhausted ceiling at the door and never enter the walk, so the glue's own
    /// <c>WireBudgetExhausted</c> outcome never arrives. Here robots and the first page fit and the
    /// second page does not, so the glue returns that outcome into the traversal's failure switch.
    /// <para>
    /// Before #579 that switch had no arm for the kind and ended in
    /// <c>throw new ArgumentOutOfRangeException</c>. The kind was genuinely unreachable then, because
    /// no EU traversal passed a budget at all; making the budget required made it reachable in the
    /// same change. Without the arm added beside it, the first real ceiling stop on a live run would
    /// have been a crash where a refusal was the entire point.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ATraversalStoppedMidWalkRefusesRatherThanThrowing()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
            {
                ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                    "Witness",
                    EuAcquisitionTestFixture.WitnessEmptyTraversalScript(Act, BoundaryWatermark)),
            });
        var executor = new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

        // Robots and the first page fit; the empty-successor confirmation's second page does not.
        var budget = WireRequestBudget.OfWireRequests(2);

        var result = await executor.RunWitnessTraversalAsync(
            [WitnessPlan()],
            EuAcquisitionTestFixture.BuildRendererSource(7),
            EuAcquisitionTestFixture.SourceWitness(),
            budget,
            CancellationToken.None);

        Assert.AreEqual(
            EuWitnessTraversalRefusal.WireBudgetExhausted,
            result.Refusal?.Code,
            "the mid-walk stop must arrive as this refusal, not as an unhandled kind.");
        Assert.IsNull(result.Entries, "a stopped traversal delivers no entry set.");
        Assert.IsTrue(budget.Exhausted);
        Assert.IsGreaterThan(
            0, handler.SendCount,
            "the premise: this test is about a walk that STARTED, unlike the spent-budget ones.");
    }

    /// <summary>
    /// A census request carrying a different budget instance refuses the run before any traffic.
    /// </summary>
    /// <remarks>
    /// ONE RUN, ONE CEILING, AND REFERENCE EQUALITY IS WHAT SAYS SO. A budget is a mutable counter,
    /// not a number: two instances both reading 500 bound 500 requests EACH, so a run driven with
    /// one while its seeds carry another can spend both allowances and stay inside both. Comparing
    /// <c>Limit</c> would admit exactly that, which is why the check is on identity and why this
    /// test hands the two sides equal limits rather than different ones -- a check on the number
    /// would pass here and the defect would ship.
    /// </remarks>
    [TestMethod]
    public async Task ACensusRequestCarryingADifferentBudgetInstanceRefusesTheRun()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var adapter = new EuQueryExecutionAdapter(
            store,
            new EuRepeatedEnumerationExecutor(
                store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler));

        var runBudget = WireRequestBudget.OfWireRequests(500);
        var seedBudget = WireRequestBudget.OfWireRequests(500);
        Assert.AreEqual(
            runBudget.Limit, seedBudget.Limit,
            "the premise: equal limits, so only identity can tell these apart.");

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var (objectFactsPlan, objectFactsPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();

        var result = await adapter.RunAsync(
            [(new EuCensusPartitionRunRequest(
                    censusPlan, censusPlanId, "32016L0680",
                    EuAcquisitionTestFixture.BuildRendererSource(1), seedBudget),
                EuAcquisitionTestFixture.SourceWitness())],
            new EuObjectFactsBatchPolicy(
                objectFactsPlan, objectFactsPlanId,
                EuAcquisitionTestFixture.BuildRendererSource(2),
                EuAcquisitionTestFixture.SourceWitness()),
            EuAcquisitionTestFixture.BuildRendererSource(9),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1009),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new UnreachableEvidenceResolver(),
            runBudget,
            CancellationToken.None);

        Assert.AreEqual(
            EuQueryExecutionRefusal.CensusRequestCarriesADifferentWireBudget,
            result.Refusal?.Code,
            "a run with two ceilings has none, and must say so.");
        Assert.AreEqual(
            0, handler.SendCount,
            "refused before the first request, not reported after the seeds had spent theirs.");
        Assert.AreEqual(0, runBudget.Spent, "and neither counter was charged.");
        Assert.AreEqual(0, seedBudget.Spent);
    }

    /// <summary>
    /// The shared observation door cannot be entered without a ceiling, and the compiler is what
    /// says so.
    /// </summary>
    /// <remarks>
    /// THE KEYSTONE, PINNED BECAUSE IT IS AN ABSENCE. While
    /// <c>RepeatedEnumerationDeliveryReopenGlue.ObserveAsync</c> carried
    /// <c>WireRequestBudget? budget = null</c>, every caller that simply forgot it compiled, ran and
    /// sent whatever it liked - which is how four EU doors came to send unbounded. Making the
    /// parameter required is what turns "is there a ceiling on this path" into a question the
    /// compiler answers rather than one a reader answers by tracing five executors.
    /// <para>
    /// A behavioural test cannot observe that no unbudgeted caller exists, because such a caller
    /// would not compile. So this reads the parameter itself: a later convenience default would
    /// silently re-open every path at once, and it fails here instead.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheSharedObservationDoorTakesNoOptionalCeiling()
    {
        var overloads = typeof(RepeatedEnumerationDeliveryReopenGlue)
            .GetMethods(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public)
            .Where(static method => method.Name == "ObserveAsync")
            .ToArray();

        Assert.AreEqual(2, overloads.Length, "both overloads are the door; neither may be exempt.");

        foreach (var overload in overloads)
        {
            var budget = overload.GetParameters()
                .SingleOrDefault(static parameter => parameter.ParameterType == typeof(WireRequestBudget)
                    || parameter.ParameterType == typeof(WireRequestBudget));
            Assert.IsNotNull(
                budget,
                $"{overload.Name} must take a ceiling, and exactly one.");
            Assert.IsFalse(
                budget!.IsOptional,
                "an optional ceiling is one a caller may forget, which is the whole of #579.");
            Assert.IsFalse(
                budget.ParameterType.IsGenericType
                    && budget.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>),
                "nor a nullable one.");
        }
    }

    /// <summary>One witness batch plan, enough to reach the door and no further.</summary>
    private static EuWatermarkWitnessPlan WitnessPlan() =>
        EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows,
            EuWatermarkCursor.TryOpen(BoundaryWatermark, Act, out var cursorRefusal)
                ?? throw new InvalidOperationException($"the fixture cursor refused as {cursorRefusal}."),
            [Act],
            out var planRefusal)
        ?? throw new InvalidOperationException($"the fixture batch refused as {planRefusal}.");

    // ---- The budget is required, not merely documented. ----

    /// <summary>
    /// Every newly budgeted request refuses a null ceiling at construction.
    /// </summary>
    /// <remarks>
    /// A POSITIONAL RECORD DOES NOT CHECK ITS OWN PARAMETERS. A sibling request in this file's own
    /// subject area documented its budget as REQUIRED while <c>new(..., null!)</c> threw nothing and
    /// reached the pass loop with the ceiling simply absent. These four are guarded rather than
    /// described, and this is what says so.
    /// </remarks>
    [TestMethod]
    public void EveryNewlyBudgetedRequestRefusesANullCeiling()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuCaseLawRunRequest(
            EuCaseLawDiscoveryPlan.Create(), [Act], NewUrn(),
            EuAcquisitionTestFixture.BuildRendererSource(1), null!));

        Assert.ThrowsExactly<ArgumentNullException>(() => new EuNationalImplementingMeasureRunRequest(
            EuNationalImplementingMeasureDiscoveryPlan.Create(), NewUrn(),
            EuAcquisitionTestFixture.BuildRendererSource(1), null!));

        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgTranspositionIdentityRunRequest(
            LuxembourgTranspositionIdentityDiscoveryPlan.Create(), [EuEli], NewUrn(),
            EuAcquisitionTestFixture.BuildRendererSource(1), null!));

        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgOpinionRunRequest(
            LuxembourgOpinionDiscoveryPlan.Create(), NewUrn(),
            EuAcquisitionTestFixture.BuildRendererSource(1), null!));

        // #579's two: the census and object-facts partitions, which had no budget parameter at all
        // rather than an optional one.
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuCensusPartitionRunRequest(
            EuAcquisitionTestFixture.BuildCensusPlan().Plan, NewUrn(), "32016L0680",
            EuAcquisitionTestFixture.BuildRendererSource(1), null!));

        Assert.ThrowsExactly<ArgumentNullException>(() => new EuObjectFactsPartitionRunRequest(
            EuAcquisitionTestFixture.BuildObjectFactsPlan().Plan, NewUrn(),
            EuObjectFactsQuerySet.ObjectFacts, [Act],
            EuAcquisitionTestFixture.BuildRendererSource(1), null!));
    }

    // ---- Fixtures. ----

    private static void AssertStoppedBeforeTheWire(
        EuEnumerationRunResult result, EuAcquisitionTestFixture.ClassifyingHandler handler)
    {
        Assert.AreEqual(
            EuEnumerationRefusal.WireBudgetExhausted,
            result.Refusal?.Code,
            "an exhausted budget must refuse by name rather than by transport failure.");
        Assert.AreEqual(0, result.ProductRequestCount);
        Assert.AreEqual(
            0,
            handler.SendCount,
            "nothing may reach the publisher once the budget is spent - not even robots.");
    }

    /// <summary>A ceiling already spent down to nothing, at the floor OfWireRequests admits.</summary>
    private static WireRequestBudget Spent()
    {
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends the budget itself.");
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends the budget itself.");
        Assert.IsTrue(budget.Exhausted, "the door must be met with an already-spent budget.");
        return budget;
    }

    private static (EuRepeatedEnumerationExecutor Executor, EuAcquisitionTestFixture.ClassifyingHandler Handler)
        Harness()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal));
        var executor = new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);
        return (executor, handler);
    }

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");

    /// <summary>
    /// A resolver that throws on every member, because the run it is handed to must refuse before
    /// reaching scope reduction at all.
    /// </summary>
    /// <remarks>
    /// A PERMISSIVE STUB WOULD HAVE MADE THE TEST WEAKER. If the budget check were moved later, a
    /// permissive resolver would let the run proceed through reduction and the test would still see
    /// its refusal at the end - passing while the run had already done work and, with real requests
    /// scripted, sent them. This throws instead, so "refused before anything else happened" is
    /// enforced rather than hoped for.
    /// </remarks>
    private sealed class UnreachableEvidenceResolver : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef =>
            throw new InvalidOperationException("the run must refuse before scope reduction.");

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            throw new InvalidOperationException("the run must refuse before scope reduction.");

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            throw new InvalidOperationException("the run must refuse before scope reduction.");

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            throw new InvalidOperationException("the run must refuse before scope reduction.");

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            throw new InvalidOperationException("the run must refuse before scope reduction.");
    }
}
