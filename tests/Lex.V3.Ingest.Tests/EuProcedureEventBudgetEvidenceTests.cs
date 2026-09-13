using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// E8's EU procedure-event family, budgeted: the ceiling that family never had.
/// </summary>
/// <remarks>
/// <para>
/// AUDITED, NOT SUSPECTED. At integration 539d2c3c four of eleven executor entry points reserved
/// their robots fetch and forwarded a budget into the pass loop; this family was one of the seven
/// that did neither, and it is the one #417 names in its own title. The mechanism that hid it is
/// worth stating: RunPassesAsync takes its budget as an optional parameter defaulting to null, so an
/// entry point that omitted the argument charged nothing and refused nothing - and no test could see
/// it, because an absent ceiling behaves exactly like a generous one until a run is large enough to
/// need it.
/// </para>
/// <para>
/// EACH GUARD BELOW FAILS FOR ITS OWN MUTATION. Deleting the robots reservation is caught by the
/// door that must open no session at all; deleting or substituting the forwarded budget is caught by
/// cumulative accounting on a delivered run and by the mid-run stop, which are different
/// observations of the same omission rather than two spellings of one.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuProcedureEventBudgetEvidenceTests
{
    private const string Dossier =
        "http://publications.europa.eu/resource/cellar/1f7ba2c8-4d59-11ec-91ac-01aa75ed71a1";

    private const string Event =
        "http://publications.europa.eu/resource/cellar/a3d90b16-4d59-11ec-91ac-01aa75ed71a1";

    private const string FirstType = "http://publications.europa.eu/ontology/cdm#event_legal_type_one";

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("eu-procedure-event-budget-source/1");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:4b9d1e57-2c86-4a03-9f71-5e8d0a2c6b14",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }

    private static (EuRepeatedEnumerationExecutor Executor, EuAcquisitionTestFixture.ClassifyingHandler Handler)
        Harness(params string[] rows)
    {
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["ProcedureEvent"] = EuAcquisitionTestFixture.ScriptFor(
                "ProcedureEvent", rows.Length, rows, EuAcquisitionTestFixture.ProcedureEventProjection),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var executor = new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);
        return (executor, handler);
    }

    private static Task<EuEnumerationRunResult> RunAsync(
        EuRepeatedEnumerationExecutor executor, WireRequestBudget budget) =>
        executor.RunEuProcedureEventsAsync(
            new EuProcedureEventRunRequest(
                EuProcedureEventDiscoveryPlan.Create(),
                [Dossier],
                "urn:uuid:6f2a8c13-7d45-4e92-b0a6-31c58e7d9042",
                Source(),
                budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

    /// <summary>An exhausted budget opens no session, so the robots fetch is never sent.</summary>
    /// <remarks>
    /// THE ONLY GUARD THAT SEPARATES REFUSING BEFORE SENDING FROM SENDING FIRST. Deleting the
    /// reservation fails five of the six guards here, because it breaks every accounting
    /// relationship at once - measured, not assumed. What this one alone establishes is the
    /// POSITION: it asserts on the transport, and <c>SendCount == 0</c> is the only observation that
    /// distinguishes a door that stopped from a door that fetched robots and then discovered the
    /// exhaustion. Asserting the refusal code alone would pass on the latter.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheProcedureEventDoor()
    {
        var (executor, handler) = Harness(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));
        // Two is the floor OfWireRequests admits - a run's budget covers its robots fetch and at
        // least one product request - so the fixture spends both itself to reach exhaustion.
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends the budget itself.");
        Assert.IsTrue(budget.TryReserveAttempt(), "the fixture spends the budget itself.");
        Assert.IsTrue(budget.Exhausted, "the door must be met with an already-spent budget.");

        var result = await RunAsync(executor, budget);

        Assert.AreEqual(
            EuEnumerationRefusal.WireBudgetExhausted, result.Refusal?.Code,
            "an exhausted budget must refuse by name rather than by transport failure.");
        Assert.AreEqual(0, result.ProductRequestCount);
        Assert.AreEqual(
            0, handler.SendCount,
            "nothing may reach the publisher once the budget is spent - not even robots.");
    }

    /// <summary>A delivered run charges its robots fetch and every product request to one ceiling.</summary>
    /// <remarks>
    /// Fails when the budget is not forwarded into the pass loop, and fails when a different budget
    /// instance is forwarded in its place: either way the budget the caller holds shows only the one
    /// robots reservation while the transport shows more requests than that.
    /// </remarks>
    [TestMethod]
    public async Task ADeliveredRunChargesRobotsAndEveryProductRequestToTheCallersBudget()
    {
        var (executor, handler) = Harness(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));
        var budget = WireRequestBudget.OfWireRequests(100);

        var result = await RunAsync(executor, budget);

        Assert.IsNull(
            result.Refusal,
            "this guard needs a delivered run, and got " + result.Refusal?.Code);
        Assert.IsGreaterThan(0, result.ProductRequestCount);
        Assert.AreEqual(
            result.ProductRequestCount + 2, budget.Spent,
            "the spend is this run's product attempts plus its robots route: the policy item "
            + "reserved at the door and the 301 hop reserved by the session before it was sent.");
        // MEASURED TWICE, AND THE SECOND MEASUREMENT IS THE ONE THAT HOLDS. The first head of #579
        // asserted SendCount == Spent here and it failed at 6 against 5: the EU profile's robots
        // route is TWO HOPS - a 301 from publications.europa.eu/robots.txt to
        // op.europa.eu/robots.txt - and only the plan item was reserved, so one real send per
        // session went uncharged. That head pinned the gap as "two sends charged as one" instead of
        // closing it, and review named it as the defect it was. The session now reserves every
        // redirect hop at its own gate before sending it, so the transport and the budget agree
        // exactly - on this origin as on single-hop Legilux.
        Assert.AreEqual(
            budget.Spent, handler.SendCount,
            "every send is reserved before it goes out, the robots redirect hop included.");
    }

    /// <summary>The door stops mid-run at its ceiling instead of finishing the family.</summary>
    /// <remarks>
    /// A ceiling that only ever refuses before the first request is not a ceiling over the pass loop.
    /// This spends the budget partway through, so the stop has to come from inside RunPassesAsync -
    /// the half an omitted forward leaves unprotected.
    /// </remarks>
    [TestMethod]
    public async Task TheProcedureEventDoorStopsMidRunAtItsCeiling()
    {
        var (executor, handler) = Harness(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));
        var budget = WireRequestBudget.OfWireRequests(3);

        var result = await RunAsync(executor, budget);

        Assert.AreEqual(
            EuEnumerationRefusal.WireBudgetExhausted, result.Refusal?.Code,
            "a run that cannot afford its next request refuses by name.");
        Assert.AreEqual(3, budget.Spent, "the ceiling is spent exactly, never exceeded.");
        Assert.IsTrue(budget.Exhausted);

        // THE CEILING BOUNDS SENDS, NOT ONLY CHARGES. The first head of #579 measured four sends
        // against a ceiling of three here, because the robots redirect hop went uncharged, and
        // wrote down that a live ceiling would have to budget two sends per session by hand. The
        // session now reserves the hop itself, so three is three: robots, its 301 hop, and one
        // product request - with the second product request refused before it is sent.
        Assert.AreEqual(
            budget.Spent, handler.SendCount,
            "a ceiling of three admits exactly three sends.");
    }

    /// <summary>A refused run still reports its own terminal accounting.</summary>
    /// <remarks>
    /// The stopped run is the one whose cost most needs stating. This pins that a mid-run refusal
    /// reports the product attempts it made rather than zeroing them, which is what a live terminal
    /// index has to retain.
    /// </remarks>
    [TestMethod]
    public async Task ARefusedRunReportsTheProductAttemptsItAlreadyMade()
    {
        var (executor, _) = Harness(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));
        var budget = WireRequestBudget.OfWireRequests(3);

        var result = await RunAsync(executor, budget);

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            budget.Spent - 2, result.ProductRequestCount,
            "a refused run reports every product attempt it made, excluding only its robots "
            + "route: the policy item and the redirect hop that item needs on this origin.");
    }

    /// <summary>
    /// This origin's robots bootstrap costs two sends and is charged as two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FINDING, ISOLATED SO IT HAS ITS OWN GUARD. The two guards above observe the accounting
    /// as a relationship between charges and sends; this one states the bootstrap's own cost,
    /// separating it from the product requests so neither number can absorb a change in the other.
    /// </para>
    /// <para>
    /// It is a property of the EU profile's route, not of this family: the robots policy request is
    /// one PLAN ITEM - <c>RobotsRequestOrdinal</c> 0, product from 1 - and the 301 to op.europa.eu
    /// is a second HTTP send inside that single item. The reservation before
    /// <c>StartSessionAsync</c> charges the item's first send; the session charges the hop at its
    /// own gate, immediately before sending it, so the count is right however many hops the
    /// publisher's route grows or loses. The first head of #579 charged only the item and pinned the
    /// difference as "two sends charged as one"; review named that as the defect it was, because a
    /// ceiling that undercounts by one per session on one publisher is not a ceiling. This guard
    /// fails if either reservation is deleted, and fails the other way if the session ever reserves
    /// a hop it then does not send.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheRobotsBootstrapCostsTwoSendsAndIsChargedAsTwo()
    {
        var (executor, handler) = Harness(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));
        var budget = WireRequestBudget.OfWireRequests(100);

        var result = await RunAsync(executor, budget);

        Assert.IsNull(result.Refusal, "this guard needs a delivered run.");
        Assert.AreEqual(
            2, handler.SendCount - result.ProductRequestCount,
            "the bootstrap sends robots twice on this origin: a 301 and then the policy itself.");
        Assert.AreEqual(
            2, budget.Spent - result.ProductRequestCount,
            "and the budget charges both sends: the item at the door, the hop at the session's gate.");
    }

    /// <summary>The request refuses a null budget at construction.</summary>
    /// <remarks>
    /// A positional record checks nothing of its own, so a null passed with the null-forgiving
    /// operator would otherwise reach the pass loop as an optional budget that was simply not
    /// supplied - the ceiling off, and nothing saying so. The four Luxembourg requests each carry
    /// this guard; one of them shipped without it and had to be repaired.
    /// </remarks>
    [TestMethod]
    public void TheProcedureEventRequestRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuProcedureEventRunRequest(
            EuProcedureEventDiscoveryPlan.Create(),
            [Dossier],
            "urn:uuid:9c4e17a0-8b52-4d36-a1f9-70e2d5b8c631",
            Source(),
            null!));
}
