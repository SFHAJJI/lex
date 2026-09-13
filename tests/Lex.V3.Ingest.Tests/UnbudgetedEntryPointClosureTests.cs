using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
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
}
