using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
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
    /// The document fetch charges EVERY attempt, so its retry loop cannot outrun the ceiling.
    /// </summary>
    /// <remarks>
    /// THE FIRST REQUEST BEING CHARGED SAYS NOTHING ABOUT THE FOURTH. A ceiling taken once per call
    /// would be wrong by the profile's whole retry allowance at its own limit, and this door is a
    /// BODY fetch where one request costs orders of magnitude more bytes than a SPARQL page. The
    /// budget here affords robots plus exactly one attempt; the transport fails before headers, so
    /// the profile would allow another; the ceiling is what stops it.
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

        // Robots plus exactly one attempt. OfWireRequests admits no smaller ceiling.
        var budget = WireRequestBudget.OfWireRequests(2);

        var result = await executor.RunDocumentFetchAsync(
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            budget,
            CancellationToken.None);

        Assert.AreEqual(
            EuDocumentFetchAttemptRefusal.WireBudgetExhausted,
            result.Refusal,
            "the SECOND attempt must be refused by the ceiling, not sent and then regretted.");
        // THREE SENDS FOR TWO RESERVATIONS, AND THE DIFFERENCE IS WORTH STATING. The EU robots
        // negotiation is answered with a 301 from publications.europa.eu to op.europa.eu, so it
        // costs two sockets for the one reservation it charges; the single product attempt is the
        // third. A wire ceiling bounds RESERVATIONS, not sockets, and this is the test that makes
        // that visible rather than leaving a reader to assume they are the same number. Bounding
        // bytes rather than requests is #580 and is not attempted here.
        Assert.AreEqual(
            3, handler.SendCount,
            "robots through its redirect, plus exactly one product attempt; the retry did not.");
        Assert.AreEqual(
            2, budget.Spent, "two reservations were charged, the robots fetch included.");
        Assert.IsTrue(budget.Exhausted, "and the ceiling is what the second attempt met.");
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
