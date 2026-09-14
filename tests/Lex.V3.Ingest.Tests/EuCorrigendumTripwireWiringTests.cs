using System.Globalization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// #418 slice 6: the corrigendum tripwire joined into the EU query execution adapter. Every test
/// here drives the whole adapter over a scripted transport that answers by family and fails on any
/// request a run must not send. The closure is one seed plus fifty census-discovered states, fifty-one
/// objects, so every object-facts family is asked in two batches and the join is exercised where it
/// matters: two object-facts batches before two Expression-facts batches, one production per pair.
/// </summary>
[TestClass]
public sealed class EuCorrigendumTripwireWiringTests
{
    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:00000000-0000-4000-8000-0000000000f6", new string('6', 64));

    private const string WatermarkLexical = "2026-01-01T00:00:00.0000000+01:00";
    private const string ConsolidatedActResourceType =
        "http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT";
    private const string LanguageBase = "http://publications.europa.eu/resource/authority/language/";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string CorrigendumDate = "2018-05-23";
    private const int StateCount = 50;

    /// <summary>
    /// The exact wire order of a delivered run over the fifty-one-object closure, request by request:
    /// the census, then the two object-facts batches, then the two Expression-facts batches, then M, A,
    /// L and W, the witness, and the root's document fetch. Pinned as one string so that a join which
    /// moved a family, sent one twice, or interleaved the pairs is a diff here, not a count.
    /// </summary>
    private const string DeliveredSequence =
        "Robots Robots Census Census Census Census Robots Robots P P P P P Robots Robots P P P P Robots Robots X X X X Robots Robots X X X X Robots Robots M M M M Robots Robots M M M M Robots Robots A A A A Robots Robots A A A A Robots Robots L L L L Robots Robots L L L L Robots Robots W W W W Robots Robots Fetch Robots Robots Witness Witness Witness Witness";

    // ---- The join over a delivered run. ----

    [TestMethod]
    public async Task ADeliveredRunSendsEachBatchOnceInTheFactorysOrderAndCompletesOneProductionPerPair()
    {
        var run = await RunAsync();

        Assert.IsNull(run.Result.Refusal, Describe(run));
        Assert.AreEqual(DeliveredSequence, string.Join(" ", run.Handler.FamilySequence), Describe(run));
        // The first object-facts batch carries 650 rows: one page on the first pass, two on the
        // second, so five requests; the second batch's thirteen rows take four. Never a second
        // acquisition of either family.
        Assert.AreEqual(9, run.Handler.OccurrenceCountFor("P"));
        Assert.AreEqual(8, run.Handler.OccurrenceCountFor("X"));
        Assert.HasCount(2, run.Closure.Batches);

        var productions = run.Result.CorrigendumTripwires!.ProductionsByFamilyKey;
        CollectionAssert.AreEquivalent(
            run.Closure.Batches.Select(static batch => EuObjectFactsDiscoveryPlan.PartitionKeyFor(batch)).ToArray(),
            productions.Keys.ToArray(),
            "one production per paired batch, keyed by the batch's own family key.");
        foreach (var (key, production) in productions)
        {
            Assert.IsTrue(production.Delivered, $"{key}: {production.Refusal}: {production.Detail}");
            Assert.IsEmpty(production.TripwireSet!.Tripwires, "no corrigendum in this closure: an empty set is a delivered fact.");
            Assert.IsEmpty(production.TripwireSet.UnresolvedGaps);
            Assert.IsNotNull(production.Expressions!.RetainedDerivation);
            Assert.IsNotNull(production.RetainedTripwire);
            Assert.AreEqual(
                5, run.Result.FamilyOutcomes.Count(outcome => outcome.FamilyKey == key),
                "the key is the batch's own: its P, X, M, A and L outcomes all carry it, whichever family asked.");
        }

        // What today's run delivers, it still delivers: every family proven, the record set written.
        Assert.IsTrue(run.Result.FamilyOutcomes.All(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven));
        Assert.AreEqual(1 + 2 * 5 + 1, run.Result.FamilyOutcomes.Count, "the census, two batches of each of P, X, M, A and L, and W.");
        Assert.IsNotNull(run.Result.CorpusRecordSet);
        Assert.AreEqual(1 + StateCount, run.Result.CorpusRecordSet!.Set.Records.Count);
    }

    [TestMethod]
    public async Task ACorrigendumStateYieldsADatedTripwireInItsBatchsProductionAndNoOther()
    {
        var closure = BuildClosure();
        var corrigendum = new Corrigendum(closure.State(1), Languages: [LanguageBase + "DEU", LanguageBase + "EST"]);
        var run = await RunAsync(new Options(Corrigendum: corrigendum));

        Assert.IsNull(run.Result.Refusal, Describe(run));
        var productions = run.Result.CorrigendumTripwires!.ProductionsByFamilyKey;
        var first = productions[EuObjectFactsDiscoveryPlan.PartitionKeyFor(run.Closure.Batches[0])];
        var second = productions[EuObjectFactsDiscoveryPlan.PartitionKeyFor(run.Closure.Batches[1])];
        Assert.HasCount(1, first.TripwireSet!.Tripwires, "the corrigendum and the work it corrects are both in the first batch.");
        var tripwire = first.TripwireSet.TripwireFor(run.Closure.Root)!;
        Assert.HasCount(2, tripwire.Lines);
        Assert.AreEqual(2, tripwire.DatedCount);
        Assert.AreEqual(CorrigendumDate, tripwire.Lines[0].PublisherCorrigendumDate!.RawLexical);
        Assert.IsTrue(tripwire.Lines.All(line => line.CorrigendumWorkRoot == corrigendum.Iri));
        Assert.IsEmpty(first.TripwireSet.UnresolvedGaps);
        Assert.IsEmpty(second.TripwireSet!.Tripwires, "the second batch holds one state that corrects nothing.");
        Assert.AreEqual(DeliveredSequence, string.Join(" ", run.Handler.FamilySequence), "a corrigendum changes rows, never traffic.");
    }

    // ---- Refusals, by name, in the order the adapter has always used. ----

    /// <summary>
    /// A refused object-facts batch is a refused family outcome, its Expression sibling is still
    /// attempted (and every other family after it), and the run refuses the family, exactly as it
    /// did before the join. The pinned outcome list is in the factory's order: the census, P1, P2,
    /// X1, X2, M1, M2, A1, A2, L1, L2, W.
    /// </summary>
    [TestMethod]
    public async Task ARefusedObjectFactsBatchStillHasItsExpressionSiblingAttemptedAndRefusesTheFamily()
    {
        var run = await RunAsync(new Options(RefusingFamily: "P", RefusingBatch: 0));

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ObjectFactsFamilyNotProven, run.Result.Refusal!.Code);
        Assert.IsNull(run.Result.CorrigendumTripwires);
        Assert.AreEqual(
            "Proven ExecutorRefused(PartitionRequired) Proven Proven Proven Proven Proven Proven Proven Proven Proven Proven",
            Outcomes(run.Result), Describe(run));
        // The refused first object-facts batch stops at its count page; everything after it is
        // still asked, in the factory's order, and nothing is asked twice.
        Assert.AreEqual(
            "Robots Robots Census Census Census Census Robots Robots P Robots Robots P P P P Robots Robots X X X X Robots Robots X X X X Robots Robots M M M M Robots Robots M M M M Robots Robots A A A A Robots Robots A A A A Robots Robots L L L L Robots Robots L L L L Robots Robots W W W W",
            string.Join(" ", run.Handler.FamilySequence), Describe(run));
        Assert.AreEqual(8, run.Handler.OccurrenceCountFor("X"), "both Expression batches attempted, each once.");
    }

    [TestMethod]
    public async Task ARefusedExpressionFactsBatchStaysAFamilyNotProvenWithNoProduction()
    {
        var run = await RunAsync(new Options(RefusingFamily: "X", RefusingBatch: 0));

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ObjectFactsFamilyNotProven, run.Result.Refusal!.Code);
        Assert.IsNull(run.Result.CorrigendumTripwires);
        Assert.AreEqual(
            "Proven Proven Proven ExecutorRefused(PartitionRequired) Proven Proven Proven Proven Proven Proven Proven Proven",
            Outcomes(run.Result), Describe(run));
        // Both object-facts batches proved (the first over two second-pass pages) before the refused
        // Expression batch stopped at its count page; its sibling and every later family still ran.
        Assert.AreEqual(
            "Robots Robots Census Census Census Census Robots Robots P P P P P Robots Robots P P P P Robots Robots X Robots Robots X X X X Robots Robots M M M M Robots Robots M M M M Robots Robots A A A A Robots Robots A A A A Robots Robots L L L L Robots Robots L L L L Robots Robots W W W W",
            string.Join(" ", run.Handler.FamilySequence), Describe(run));
        Assert.AreEqual(9, run.Handler.OccurrenceCountFor("P"), "both object-facts batches proved, each once (five and four requests), before the refused Expression batch.");
    }

    /// <summary>
    /// A ceiling reached exactly between the last object-facts batch and the first Expression-facts
    /// batch: the budget is the number of requests the pinned delivered sequence sends before the
    /// first Expression session's own robots request, so that session cannot start, and neither can
    /// anything after it. The outcome list and the product request count are what the adapter has
    /// always reported there.
    /// </summary>
    [TestMethod]
    public async Task ACeilingReachedBetweenTheObjectAndExpressionBatchesRefusesWithTheExactOutcomes()
    {
        var sequence = DeliveredSequence.Split(' ');
        var boundary = Array.IndexOf(sequence, "X");
        while (sequence[boundary - 1] == "Robots")
        {
            boundary--;
        }

        var run = await RunAsync(new Options(Budget: WireRequestBudget.OfWireRequests(boundary)));

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ObjectFactsFamilyNotProven, run.Result.Refusal!.Code);
        Assert.IsNull(run.Result.CorrigendumTripwires);
        Assert.AreEqual(boundary, run.Budget.Spent, "every request before the boundary was sent, and none after it.");
        Assert.AreEqual(boundary, run.Handler.SendCount);
        Assert.AreEqual(string.Join(" ", sequence.Take(boundary)), string.Join(" ", run.Handler.FamilySequence));
        // The census and both object-facts batches proved; every family after the boundary, the
        // two Expression batches first, is the executor's own ceiling refusal, in the factory's order.
        Assert.AreEqual(
            "Proven Proven Proven " + string.Join(" ", Enumerable.Repeat("ExecutorRefused(WireBudgetExhausted)", 9)),
            Outcomes(run.Result), Describe(run));
    }

    /// <summary>
    /// A fold refusal after both of a batch's runs proved is the run's refusal by name, carrying the
    /// producer's code and the batch's key: here the publisher states, for the same corrigendum, a
    /// Corrects edge and the marker that it corrects nothing.
    /// </summary>
    [TestMethod]
    public async Task AFoldRefusalRefusesTheRunByNameWithTheBatchKey()
    {
        var closure = BuildClosure();
        var corrigendum = new Corrigendum(closure.State(1), Languages: [LanguageBase + "DEU"], EdgeBesideMarker: true);
        var run = await RunAsync(new Options(Corrigendum: corrigendum));

        Assert.IsNotNull(run.Result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.CorrigendumTripwireProductionRefused, run.Result.Refusal!.Code);
        StringAssert.Contains(run.Result.Refusal.Detail, nameof(EuCorrigendumTripwireProductionRefusal.TripwireRefused));
        StringAssert.Contains(run.Result.Refusal.Detail, EuObjectFactsDiscoveryPlan.PartitionKeyFor(run.Closure.Batches[0]));
        Assert.IsNull(run.Result.CorrigendumTripwires);
        Assert.IsTrue(
            run.Result.FamilyOutcomes.Count(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven) >= 4,
            "both runs of the refusing pair proved; the refusal is the production's, not a family's.");
    }

    // ---- The delivery door: exact totality, minted from the frozen pairing. ----

    [TestMethod]
    public async Task TheCompletionRefusesAnEmptyMissingExtraOrRefusedProductionAndAcceptsTheExactPair()
    {
        var run = await RunAsync();
        Assert.IsNull(run.Result.Refusal, Describe(run));
        var delivered = run.Result.CorrigendumTripwires!.ProductionsByFamilyKey;
        var keys = run.Closure.Batches.Select(static batch => EuObjectFactsDiscoveryPlan.PartitionKeyFor(batch)).ToArray();
        var expected = new HashSet<string>(keys, StringComparer.Ordinal);
        var refused = EuCorrigendumTripwireProductionResult.Refused(
            EuCorrigendumTripwireProductionRefusal.TripwireRefused, "a refused production", null, 0);

        var empty = Assert.ThrowsExactly<ArgumentException>(() => new EuCorrigendumTripwireCompletion(
            expected, new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)));
        StringAssert.Contains(empty.Message, "2 paired Expression-facts batch(es) have no production");

        var missingSecond = Assert.ThrowsExactly<ArgumentException>(() => new EuCorrigendumTripwireCompletion(
            expected, new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal) { [keys[0]] = delivered[keys[0]] }));
        StringAssert.Contains(missingSecond.Message, keys[1]);
        var missingFirst = Assert.ThrowsExactly<ArgumentException>(() => new EuCorrigendumTripwireCompletion(
            expected, new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal) { [keys[1]] = delivered[keys[1]] }));
        StringAssert.Contains(missingFirst.Message, keys[0]);

        var extra = Assert.ThrowsExactly<ArgumentException>(() => new EuCorrigendumTripwireCompletion(
            expected, new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)
            {
                [keys[0]] = delivered[keys[0]], [keys[1]] = delivered[keys[1]], ["eu-object-facts-batch-extra"] = delivered[keys[0]],
            }));
        StringAssert.Contains(extra.Message, "belong to no paired batch");

        var oneRefused = Assert.ThrowsExactly<ArgumentException>(() => new EuCorrigendumTripwireCompletion(
            expected, new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)
            {
                [keys[0]] = delivered[keys[0]], [keys[1]] = refused,
            }));
        StringAssert.Contains(oneRefused.Message, nameof(EuCorrigendumTripwireProductionRefusal.TripwireRefused));

        var exact = new EuCorrigendumTripwireCompletion(
            expected, new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)
            {
                [keys[0]] = delivered[keys[0]], [keys[1]] = delivered[keys[1]],
            });
        Assert.HasCount(2, exact.ProductionsByFamilyKey);
    }

    [TestMethod]
    public void PublicResultFactoriesTakeTheCompletionAndNeverABareDictionary()
    {
        var factories = typeof(EuQueryExecutionResult)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var delivered = factories.Single(static method => method.Name == nameof(EuQueryExecutionResult.Delivered));
        Assert.IsTrue(delivered.GetParameters().Any(static parameter => parameter.ParameterType == typeof(EuCorrigendumTripwireCompletion)));
        Assert.IsFalse(factories.Any(static method => method.GetParameters().Any(static parameter =>
            parameter.ParameterType == typeof(IReadOnlyDictionary<string, EuCorrigendumTripwireProductionResult>))));
    }

    // ---- The pairing, on its own. ----

    [TestMethod]
    public void PairingIsByIdenticalObjectListOnlyAndLeavesEverythingElseSingle()
    {
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        EuObjectFactsPartitionRunRequest Request(EuObjectFactsQuerySet set, params string[] objects) =>
            new(plan, planId, set, objects, EuAcquisitionTestFixture.BuildRendererSource(4186), budget);

        var requests = new[]
        {
            Request(EuObjectFactsQuerySet.ObjectFacts, "http://publications.europa.eu/resource/cellar/a", "http://publications.europa.eu/resource/cellar/b"),
            Request(EuObjectFactsQuerySet.ObjectFacts, "http://publications.europa.eu/resource/cellar/c"),
            Request(EuObjectFactsQuerySet.ExpressionFacts, "http://publications.europa.eu/resource/cellar/a", "http://publications.europa.eu/resource/cellar/b"),
            Request(EuObjectFactsQuerySet.ExpressionFacts, "http://publications.europa.eu/resource/cellar/c", "http://publications.europa.eu/resource/cellar/d"),
            Request(EuObjectFactsQuerySet.ManifestationFacts, "http://publications.europa.eu/resource/cellar/a", "http://publications.europa.eu/resource/cellar/b"),
        };

        var (pairs, singles) = EuQueryExecutionAdapter.PairExpressionAndObjectBatches(requests);

        Assert.AreEqual("(2,0)", string.Join(" ", pairs.Select(static pair => $"({pair.ExpressionIndex},{pair.ObjectIndex})")));
        CollectionAssert.AreEqual(new[] { 1, 3, 4 }, singles.ToArray(), "an Expression batch with no partner over the same objects stays single.");
    }

    [TestMethod]
    public async Task TheObjectFirstPairingAttemptsTheExpressionRunAfterARefusedObjectRun()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["X"] = EuAcquisitionTestFixture.ScriptFor("X", 0, [], EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["P"] = new EuAcquisitionTestFixture.FamilyScript("P", [EuAcquisitionTestFixture.EuCountJson(1_000_000)]),
        });
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var producer = new EuLanguageScopedExpressionProducer(executor, store);
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var pairing = new EuLanguageScopedExpressionProducer.Pairing(
            producer, SingleRequest(EuObjectFactsQuerySet.ExpressionFacts, budget), SingleRequest(EuObjectFactsQuerySet.ObjectFacts, budget),
            EuAcquisitionTestFixture.SourceWitness());

        var objectRun = await pairing.RunObjectFactsAsync(CancellationToken.None);
        Assert.IsNull(objectRun.Receipt);
        Assert.AreEqual(EuEnumerationRefusal.PartitionRequired, objectRun.Refusal!.Code);

        var (result, expressionRun, _, _) = await pairing.RunExpressionFactsAndDeriveAsync(CancellationToken.None);
        Assert.IsNotNull(expressionRun.Receipt, "the Expression run was made after the refused object run, as an adapter's independent runs always were.");
        Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.ObjectFactsEnumerationRefused, result.Refusal);
        Assert.IsTrue(handler.OccurrenceCountFor("X") > 0);
    }

    [TestMethod]
    public async Task TheObjectFirstPairingRefusesTheWrongOrderASecondObjectRunAndAMismatchedPair()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["X"] = EuAcquisitionTestFixture.ScriptFor("X", 0, [], EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor("P", 0, [], EuAcquisitionTestFixture.ObjectFactsProjection),
        });
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var producer = new EuLanguageScopedExpressionProducer(executor, store);
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var witness = EuAcquisitionTestFixture.SourceWitness();

        var mismatched = Assert.ThrowsExactly<ArgumentException>(() => new EuLanguageScopedExpressionProducer.Pairing(
            producer, SingleRequest(EuObjectFactsQuerySet.ObjectFacts, budget), SingleRequest(EuObjectFactsQuerySet.ObjectFacts, budget), witness));
        StringAssert.Contains(mismatched.Message, nameof(EuLanguageScopedExpressionProductionRefusal.ExpressionFactsRequestIsNotTheExpressionFamily));

        var pairing = new EuLanguageScopedExpressionProducer.Pairing(
            producer, SingleRequest(EuObjectFactsQuerySet.ExpressionFacts, budget), SingleRequest(EuObjectFactsQuerySet.ObjectFacts, budget), witness);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pairing.RunExpressionFactsAndDeriveAsync(CancellationToken.None));
        Assert.AreEqual(0, handler.SendCount, "refused before any traffic.");

        _ = await pairing.RunObjectFactsAsync(CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pairing.RunObjectFactsAsync(CancellationToken.None));
        Assert.AreEqual(4, handler.OccurrenceCountFor("P"), "one object-facts run, two passes of a count and a page each, never a second run.");
    }

    /// <summary>
    /// The public Expression-first run is unchanged by the join: an Expression refusal ends it before
    /// the object family is asked, so the shared tail that both orders now use does not move that.
    /// </summary>
    [TestMethod]
    public async Task TheExpressionFirstRunStillStopsBeforeTheObjectFamilyWhenTheExpressionRunRefuses()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["X"] = new EuAcquisitionTestFixture.FamilyScript("X", [EuAcquisitionTestFixture.EuCountJson(1_000_000)]),
            ["P"] = EuAcquisitionTestFixture.ScriptFor("P", 0, [], EuAcquisitionTestFixture.ObjectFactsProjection),
        });
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var producer = new EuLanguageScopedExpressionProducer(executor, store);
        var budget = EuAcquisitionTestFixture.TestWireBudget();

        var result = await producer.RunAsync(
            SingleRequest(EuObjectFactsQuerySet.ExpressionFacts, budget), SingleRequest(EuObjectFactsQuerySet.ObjectFacts, budget),
            EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);

        Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.ExpressionFactsEnumerationRefused, result.Refusal);
        Assert.AreEqual(0, handler.OccurrenceCountFor("P"), "the object family is never asked after an Expression refusal.");
        Assert.AreEqual(1, handler.OccurrenceCountFor("X"));
    }

    // ---- Fixtures. ----

    private sealed record Options(
        Corrigendum? Corrigendum = null,
        string? RefusingFamily = null,
        int RefusingBatch = 0,
        WireRequestBudget? Budget = null);

    /// <summary>A census-discovered state that corrects the root on a stated date, with expressions in these languages.</summary>
    private sealed record Corrigendum(string Iri, IReadOnlyList<string> Languages, bool EdgeBesideMarker = false);

    private sealed record WiringRun(
        EuQueryExecutionResult Result,
        EuAcquisitionTestFixture.ClassifyingHandler Handler,
        WireRequestBudget Budget,
        Closure Closure);

    /// <summary>The seed's root plus fifty states, in the factory's own order and batches.</summary>
    private sealed record Closure(string Celex, string Root, IReadOnlyList<string> Objects, IReadOnlyList<IReadOnlyList<string>> Batches)
    {
        public string State(int ordinal) => Root + "/state-" + ordinal.ToString("D3", CultureInfo.InvariantCulture);
    }

    private static Closure BuildClosure()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var root = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        var objects = Enumerable.Range(1, StateCount)
            .Select(ordinal => root + "/state-" + ordinal.ToString("D3", CultureInfo.InvariantCulture))
            .Append(root)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var batches = objects
            .Chunk(EuObjectFactsDiscoveryPlan.BatchCapacity)
            .Select(static chunk => (IReadOnlyList<string>)chunk)
            .ToArray();
        return new Closure(seed.Celex, root, objects, batches);
    }

    private static string Outcomes(EuQueryExecutionResult result) => string.Join(" ", result.FamilyOutcomes.Select(static outcome =>
        outcome.Kind + (outcome.ExecutorRefusal is null ? string.Empty : "(" + outcome.ExecutorRefusal.Code + ")")
        + (outcome.ProofRefusal is null ? string.Empty : "(" + outcome.ProofRefusal + ")")));

    private static EuObjectFactsPartitionRunRequest SingleRequest(EuObjectFactsQuerySet set, WireRequestBudget budget)
    {
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        return new EuObjectFactsPartitionRunRequest(
            plan, planId, set, ["http://publications.europa.eu/resource/cellar/work-4186"],
            EuAcquisitionTestFixture.BuildRendererSource(4186), budget);
    }

    /// <summary>
    /// The publisher's object-facts rows for one object: the root is a Regulation; a state is a
    /// consolidation based on the root; the corrigendum state also corrects the root on a stated
    /// date, and, when the fixture says so, states beside that edge that it corrects nothing.
    /// </summary>
    private static IEnumerable<string> ObjectFactsRowsFor(Closure closure, string objectIri, Corrigendum? corrigendum)
    {
        var isRoot = string.Equals(objectIri, closure.Root, StringComparison.Ordinal);
        var isCorrigendum = corrigendum is not null && string.Equals(objectIri, corrigendum.Iri, StringComparison.Ordinal);
        var predicates = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Concat(EuAcquisitionTestFixture.RelationPredicates)
            .OrderBy(static predicate => predicate, StringComparer.Ordinal);
        foreach (var predicate in predicates)
        {
            if (predicate == EuAcquisitionTestFixture.WorkHasResourceType)
            {
                yield return EuAcquisitionTestFixture.ObjectFactRow(
                    objectIri, predicate, isRoot ? EuAcquisitionTestFixture.RegulationResourceType : ConsolidatedActResourceType);
            }
            else if (predicate == EuAcquisitionTestFixture.ConsolidatedBasedOnPredicate)
            {
                yield return EuAcquisitionTestFixture.ObjectFactRow(objectIri, predicate, isRoot ? null : closure.Root);
            }
            else if (isCorrigendum && predicate == EuAcquisitionTestFixture.CorrectsPredicate)
            {
                yield return EuAcquisitionTestFixture.ObjectFactRow(objectIri, predicate, closure.Root);
                if (corrigendum!.EdgeBesideMarker)
                {
                    yield return EuAcquisitionTestFixture.ObjectFactRow(objectIri, predicate, null);
                }
            }
            else if (isCorrigendum && predicate == EuAcquisitionTestFixture.WorkDateDocument)
            {
                yield return EuAcquisitionTestFixture.ObjectFactLiteralRow(objectIri, predicate, CorrigendumDate, XsdDate);
            }
            else
            {
                yield return EuAcquisitionTestFixture.ObjectFactRow(objectIri, predicate, null);
            }
        }
    }

    private static IReadOnlyList<string> ExpressionRowsFor(Closure closure, IReadOnlyList<string> batch, Corrigendum? corrigendum)
    {
        var rows = new List<string>();
        foreach (var parent in batch)
        {
            if (string.Equals(parent, closure.Root, StringComparison.Ordinal))
            {
                // The derivation reads every expression's language; an expression the publisher
                // states without one is its decode refusal, which the join makes the run's.
                rows.Add(EuAcquisitionTestFixture.ExpressionFactRow(parent, parent + ".0001.01/DOC_1"));
                rows.Add(EuAcquisitionTestFixture.ExpressionLanguageRow(parent, parent + ".0001.01/DOC_1", EuAcquisitionTestFixture.EnglishLanguageAuthority));
            }
            else if (corrigendum is not null && string.Equals(parent, corrigendum.Iri, StringComparison.Ordinal))
            {
                for (var index = 0; index < corrigendum.Languages.Count; index++)
                {
                    var expression = parent + ".000" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".01/DOC_1";
                    rows.Add(EuAcquisitionTestFixture.ExpressionFactRow(parent, expression));
                    rows.Add(EuAcquisitionTestFixture.ExpressionLanguageRow(parent, expression, corrigendum.Languages[index]));
                }
            }
        }

        return rows;
    }

    private static EuAcquisitionTestFixture.FamilyScript Concat(IEnumerable<EuAcquisitionTestFixture.FamilyScript> passes)
    {
        var list = passes.ToArray();
        return new EuAcquisitionTestFixture.FamilyScript(list[0].FamilyTag, list.SelectMany(static pass => pass.ResponseBodies).ToArray());
    }

    /// <summary>
    /// One batch's full script: for each of the two passes, the count and then the rows in pages of
    /// that pass's own limit, a page shorter than the limit being terminal and a page exactly at it
    /// followed by the empty successor the executor then asks for. A fifty-object batch carries 650
    /// object-facts rows, one page on the first pass and two on the second.
    /// </summary>
    private static EuAcquisitionTestFixture.FamilyScript PagedScript(string tag, IReadOnlyList<string> rows, string[] projection)
    {
        var bodies = new List<string>();
        foreach (var limit in new[] { (int)EuConsolidationDiscoveryPlan.Pass1PageLimit, (int)EuConsolidationDiscoveryPlan.Pass2PageLimit })
        {
            bodies.Add(EuAcquisitionTestFixture.EuCountJson(rows.Count));
            if (rows.Count == 0)
            {
                bodies.Add(EuAcquisitionTestFixture.EmptyRowsJson(projection));
                continue;
            }

            foreach (var page in rows.Chunk(limit))
            {
                bodies.Add(EuAcquisitionTestFixture.RowsJson(projection, page));
            }

            if (rows.Count % limit == 0)
            {
                bodies.Add(EuAcquisitionTestFixture.EmptyRowsJson(projection));
            }
        }

        return new EuAcquisitionTestFixture.FamilyScript(tag, bodies);
    }

    private static string Describe(WiringRun run) =>
        $"code={run.Result.Refusal?.Code} detail={run.Result.Refusal?.Detail} outcomes=[{Outcomes(run.Result)}] "
        + $"sequence=[{string.Join(" ", run.Handler.FamilySequence)}]";

    /// <summary>The bodies a batch that refuses as partition-required consumes: its count page, and nothing after.</summary>
    private static EuAcquisitionTestFixture.FamilyScript Refusing(string tag) =>
        new(tag, [EuAcquisitionTestFixture.EuCountJson(1_000_000)]);

    private static async Task<WiringRun> RunAsync(Options? options = null)
    {
        options ??= new Options();
        var budget = options.Budget ?? EuAcquisitionTestFixture.TestWireBudget();
        var closure = BuildClosure();
        var corrigendum = options.Corrigendum;

        EuAcquisitionTestFixture.FamilyScript PerBatch(
            string tag, string[] projection, Func<IReadOnlyList<string>, IReadOnlyList<string>> rowsOf) =>
            Concat(closure.Batches.Select((batch, index) =>
            {
                if (string.Equals(options.RefusingFamily, tag, StringComparison.Ordinal) && index == options.RefusingBatch)
                {
                    return Refusing(tag);
                }

                return PagedScript(tag, rowsOf(batch), projection);
            }));

        var censusRows = closure.Objects
            .Where(objectIri => !string.Equals(objectIri, closure.Root, StringComparison.Ordinal))
            .Select(state => EuAcquisitionTestFixture.CensusFamilyRow(closure.Celex, closure.Root, state))
            .ToArray();
        var rootManifestations = EuAcquisitionTestFixture.RealBandListedTypes
            .OrderBy(static type => type, StringComparer.Ordinal)
            .Select(type => EuAcquisitionTestFixture.ManifestationFactsRow(closure.Root, type))
            .ToArray();
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", censusRows.Length, censusRows, EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = PerBatch("P", EuAcquisitionTestFixture.ObjectFactsProjection,
                batch => batch.SelectMany(objectIri => ObjectFactsRowsFor(closure, objectIri, corrigendum)).ToArray()),
            ["X"] = PerBatch("X", EuAcquisitionTestFixture.ExpressionFactsProjection,
                batch => ExpressionRowsFor(closure, batch, corrigendum)),
            ["M"] = PerBatch("M", EuAcquisitionTestFixture.ManifestationFactsProjection,
                batch => batch.Contains(closure.Root, StringComparer.Ordinal) ? rootManifestations : []),
            ["A"] = Concat(closure.Batches.Select(batch => EuAcquisitionTestFixture.AxiomAbsenceScriptFor([.. batch]))),
            ["L"] = Concat(closure.Batches.Select(batch => EuAcquisitionTestFixture.LocatedAmendmentAbsenceScriptFor([.. batch]))),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", 1, [EuAcquisitionTestFixture.RootWatermarkRow(closure.Root, WatermarkLexical)],
                EuAcquisitionTestFixture.RootWatermarkProjection),
            // The witness walks the pack's own objects in its own batches, each traversal confirming
            // the census bound as its empty successor: two pages per witness batch.
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                Enumerable.Range(0, (closure.Objects.Count + EuWatermarkWitnessPlan.BatchCapacity - 1) / EuWatermarkWitnessPlan.BatchCapacity)
                    .SelectMany(_ => EuAcquisitionTestFixture.WitnessEmptyTraversalScript(closure.Root, WatermarkLexical))
                    .ToArray()),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, closure.Celex, EuAcquisitionTestFixture.BuildRendererSource(41), budget);
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            new EuObjectFactsBatchPolicy(pPlan, pPlanId, EuAcquisitionTestFixture.BuildRendererSource(2), EuAcquisitionTestFixture.SourceWitness()),
            EuAcquisitionTestFixture.BuildRendererSource(49),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1049),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            budget,
            CancellationToken.None);
        return new WiringRun(result, handler, budget, closure);
    }

    private sealed class PermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 && value.All(static character => char.IsAsciiHexDigitLower(character));
    }
}
