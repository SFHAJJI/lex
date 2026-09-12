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
            result.ProductRequestCount + 1, budget.Spent,
            "the spend is this run's product attempts plus its one robots fetch.");
        // MEASURED, AND NOT WHAT I FIRST ASSERTED. I wrote SendCount == Spent here and it failed at
        // 6 against 5. The EU profile's robots route is TWO HOPS - a 301 from
        // publications.europa.eu/robots.txt to op.europa.eu/robots.txt - and the reservation above
        // charges the robots PLAN ITEM once, so one real send goes uncharged per session. Legilux is
        // single-hop, which is why the integrated Luxembourg accounting reconciles exactly and this
        // one cannot. Pinned rather than relaxed: if the publisher's route changes, this fails and
        // says so.
        Assert.AreEqual(
            budget.Spent + 1, handler.SendCount,
            "the EU robots route is two sends charged as one, so the transport runs one ahead.");
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

        // THE CEILING BOUNDS CHARGES, AND ON THIS ORIGIN THAT IS ONE FEWER THAN SENDS. A ceiling of
        // three let four requests reach the transport, because the two-hop robots route is charged
        // once. Stated as the arithmetic rather than as a literal so it cannot drift: a live ceiling
        // for this family has to budget sessions at two sends each, not one.
        Assert.AreEqual(
            budget.Spent + 1, handler.SendCount,
            "a ceiling of three admitted four sends: the robots redirect hop is uncharged.");
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
            budget.Spent - 1, result.ProductRequestCount,
            "a refused run reports every product attempt it made, excluding only its robots fetch.");
    }

    /// <summary>
    /// This origin's robots bootstrap costs two sends and is charged as one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FINDING, ISOLATED SO IT HAS ITS OWN GUARD. The two guards above observe it as an
    /// off-by-one between charges and sends; this one states the fact directly, separating the
    /// bootstrap from the product requests so neither number can absorb a change in the other.
    /// </para>
    /// <para>
    /// It is a property of the EU profile's route, not of this family: the robots policy request is
    /// one PLAN ITEM - <c>RobotsRequestOrdinal</c> 0, product from 1 - and the 301 to op.europa.eu
    /// is a second HTTP send inside that single item. The reservation before
    /// <c>StartSessionAsync</c> is the established invariant and charges the item once, which is
    /// exact on single-hop Legilux and one short here. A live ceiling for this family must therefore
    /// budget two sends per session; that is recorded in the live plan rather than papered over by
    /// reserving a hardcoded two, which would be wrong again the moment a hop is added or removed.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheRobotsBootstrapCostsTwoSendsAndIsChargedAsOne()
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
            1, budget.Spent - result.ProductRequestCount,
            "and the budget charges that bootstrap once, as a single plan item.");
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
