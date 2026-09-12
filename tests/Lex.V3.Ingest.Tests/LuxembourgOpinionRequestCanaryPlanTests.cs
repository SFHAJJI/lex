using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the bounded canary may do next, pinned without touching a publisher.
/// </summary>
/// <remarks>
/// EVERY CASE HERE IS OFFLINE, deliberately. These are the rules that decide whether live traffic
/// happens at all, so proving them by sending traffic would be proving a gate by walking through it.
/// The live runner has no decision of its own: it acquires exactly the ordinals this returns.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestCanaryPlanTests
{
    /// <summary>
    /// No refusal this family can express opens the gate.
    /// </summary>
    /// <remarks>
    /// DRIVEN OFF THE ENUM, not off a list I wrote. A refusal added later is covered the day it is
    /// added, and cannot become "proceed" by being overlooked here - which is the failure mode a
    /// hand-listed set of cases has and this does not.
    /// </remarks>
    [TestMethod]
    public void NoInventoryRefusalOpensTheGate()
    {
        var refusals = Enum.GetValues<LuxembourgOpinionRequestInventoryRefusal>()
            .Where(static value => value != LuxembourgOpinionRequestInventoryRefusal.None)
            .ToArray();

        Assert.IsGreaterThan(1, refusals.Length, "this family has more than one way to refuse.");

        foreach (var refusal in refusals)
        {
            var decision = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
                LuxembourgOpinionRequestInventoryResult.Refused(
                    refusal, "refused for this case", productRequestCount: 3, UnspentBudget()));

            Assert.AreEqual(
                0,
                decision.BatchOrdinalsToAcquire.Count,
                $"{refusal} must issue no batch: acquiring against an inventory that refused would "
                    + "acquire against a class nobody proved.");
            Assert.AreEqual("InventoryRefused", decision.Verdict);
        }
    }

    /// <summary>
    /// An inventory that spent the whole ceiling issues no batch, and says it is a magnitude finding.
    /// </summary>
    /// <remarks>
    /// The case that looks like success and is not. The rows came back, so <c>Delivered</c> is true;
    /// what did not come back is any budget for the batch. Letting the runner proceed here would
    /// spend the batch's first request on a robots fetch it could not afford and report the ceiling
    /// as a batch failure.
    /// </remarks>
    [TestMethod]
    public void ADeliveredInventoryThatSpentTheCeilingStillIssuesNoBatch()
    {
        var exhausted = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(exhausted.TryReserveAttempt());
        Assert.IsTrue(exhausted.TryReserveAttempt());

        var decision = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            DeliveredInventory(Subjects(3), WireBudgetSnapshot.Of(exhausted)));

        Assert.AreEqual(0, decision.BatchOrdinalsToAcquire.Count);
        Assert.AreEqual("BudgetExhaustedDuringInventory", decision.Verdict);
        StringAssert.Contains(
            decision.Reason,
            "magnitude finding",
            "clause 6: an exhausted run measured something, and is not a canary.");
    }

    /// <summary>A clean inventory earns exactly one batch, and it is the first.</summary>
    /// <remarks>
    /// One, because the canary exists to measure the single unknown - rows per batch - and a second
    /// batch measures nothing the first did not while doubling the traffic.
    /// </remarks>
    [TestMethod]
    public void ACleanInventoryEarnsExactlyTheFirstBatch()
    {
        var decision = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            DeliveredInventory(Subjects(120), UnspentBudget()));

        CollectionAssert.AreEqual(new[] { 0 }, decision.BatchOrdinalsToAcquire.ToArray());
        Assert.AreEqual("ProceedToOneBatch", decision.Verdict);
    }

    /// <summary>An empty class earns no batch.</summary>
    /// <remarks>
    /// Not merely tidy: <c>ForBatch</c> would have no ordinal to name, and a canary reporting
    /// "completed" over a class with no members would be a green result that exercised nothing.
    /// </remarks>
    [TestMethod]
    public void AnEmptyClassEarnsNoBatch()
    {
        var decision = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            DeliveredInventory([], UnspentBudget()));

        Assert.AreEqual(0, decision.BatchOrdinalsToAcquire.Count);
        Assert.AreEqual("InventoryEnumeratedNothing", decision.Verdict);
    }

    /// <summary>A stopped canary concludes as its own verdict, never as the batch's.</summary>
    [TestMethod]
    public void AStoppedCanaryKeepsItsOwnVerdict()
    {
        var inventory = LuxembourgOpinionRequestInventoryResult.Refused(
            LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused,
            "the publisher refused",
            productRequestCount: 1,
            SpentBudget(2));
        var stopped = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);

        Assert.AreEqual(
            "InventoryRefused",
            LuxembourgOpinionRequestCanaryPlan.Conclude(stopped, inventory, []),
            "a canary that never reached a batch cannot conclude anything about batches.");
    }

    /// <summary>
    /// A batch stopped by the shared ceiling is a different finding from a batch that refused.
    /// </summary>
    /// <remarks>
    /// Collapsing the two would lose the only thing an under-budgeted attempt actually measured -
    /// that the class is bigger than the ceiling was set for - and would report it as though the
    /// publisher or the data were at fault.
    /// </remarks>
    [TestMethod]
    public void ExhaustionDuringTheBatchIsNotTheSameFindingAsARefusal()
    {
        // COHERENT COUNTS THROUGHOUT, now that the verdict derives the reconciliation from these
        // very inputs: 24 inventory attempts + 1 robots = 25 spent, and the batch continues from
        // there. Incoherent numbers would make every case below an accounting finding instead of
        // the thing it is named for.
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));
        var proceed = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);

        Assert.AreEqual(
            "BudgetExhaustedDuringBatch",
            LuxembourgOpinionRequestCanaryPlan.Conclude(
                proceed,
                inventory,
                [
                    LuxembourgOpinionRequestGraphResult.Refused(
                        LuxembourgOpinionRequestGraphRefusal.EnumerationRefused,
                        "stopped at the ceiling",
                        productRequestCount: 224,
                        ExhaustedBudget()),
                ]));

        Assert.AreEqual(
            "BatchRefused",
            LuxembourgOpinionRequestCanaryPlan.Conclude(
                proceed,
                inventory,
                [
                    LuxembourgOpinionRequestGraphResult.Refused(
                        LuxembourgOpinionRequestGraphRefusal.MatrixNotCompleted,
                        "the rows did not complete a matrix",
                        productRequestCount: 6,
                        SpentBudget(32)),
                ]),
            "a batch that had budget left refused on its own contents.");
    }

    /// <summary>
    /// A batch that DELIVERED on its last reservation is an exhaustion finding, not a completion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REVIEWER'S CASE, AND IT IS REACHABLE. A reservation is taken immediately before its send,
    /// so a final attempt that succeeds leaves <c>Spent == Limit</c> and <c>Delivered</c> true at the
    /// same instant. Reading completion first called that <c>CanaryCompleted</c> and discarded the
    /// only thing it measured: the class sits exactly at the ceiling, so the next batch would have
    /// had nothing to spend.
    /// </para>
    /// <para>
    /// I had already written the same shape one function up, for a delivered inventory that spent
    /// the ceiling, and did not carry the reasoning down to the batch.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABatchDeliveredOnItsLastReservationIsAnExhaustionFinding()
    {
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));
        var proceed = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);

        var snapshot = ExhaustedBudget();
        Assert.AreEqual(snapshot.Limit, snapshot.Spent, "the batch ended exactly at its ceiling.");

        Assert.AreEqual(
            "BudgetExhaustedDuringBatch",
            LuxembourgOpinionRequestCanaryPlan.Conclude(
                proceed,
                inventory,
                [
                    LuxembourgOpinionRequestGraphResult.Completed(
                        DeliveredCoverage(), productRequestCount: 224, snapshot),
                ]),
            "clause 6: a run that reached its ceiling is a magnitude finding even when its rows "
                + "arrived, because the ceiling is what it actually measured.");
    }

    /// <summary>
    /// An inventory refused BY the ceiling is an exhaustion finding, not an ordinary refusal.
    /// </summary>
    /// <remarks>
    /// The same precedence defect the reviewer found in <c>Conclude</c>, sitting one function up and
    /// not named in the review. A run stopped by the budget refuses like any other run, so checking
    /// refusal first reported the magnitude as though the publisher or the data were at fault.
    /// </remarks>
    [TestMethod]
    public void AnInventoryRefusedByTheCeilingIsAnExhaustionFinding()
    {
        var atTheCeiling = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(atTheCeiling.TryReserveAttempt());
        Assert.IsTrue(atTheCeiling.TryReserveAttempt());

        var decision = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused,
                "WireBudgetExhausted: budget 2 reached after 2 wire request(s)",
                productRequestCount: 1,
                WireBudgetSnapshot.Of(atTheCeiling)));

        Assert.AreEqual(0, decision.BatchOrdinalsToAcquire.Count);
        Assert.AreEqual("BudgetExhaustedDuringInventory", decision.Verdict);
        StringAssert.Contains(
            decision.Reason,
            "magnitude finding",
            "a run stopped by its own ceiling measured the ceiling, not a publisher failure.");
    }

    /// <summary>
    /// A canary whose two accountings agree completes; one whose accountings drift does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO MECHANISMS ARE INDEPENDENT, WHICH IS THE WHOLE POINT. The budget grants reservations;
    /// the glue increments a counter after each attempt. Clause 5 exists so those two are checked
    /// against each other and fail closed if they drift - side-by-side arithmetic in an evidence
    /// index does not implement that check, it only makes it available to someone who thinks to do
    /// it.
    /// </para>
    /// <para>
    /// The mismatching case fabricates the drift deliberately. I could not produce one through the
    /// real path - an exception escaping the attempt aborts the producer before any result exists -
    /// and that is precisely why the guard is not allowed to depend on being reachable today: it is
    /// a cross-check between two mechanisms, and it has to hold if either one ever changes.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnAccountingDriftOutranksCompletion()
    {
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));
        var proceed = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);

        // Agreeing: 24 inventory attempts + 6 batch attempts + 2 robots fetches = 32 reservations.
        var agreeing = LuxembourgOpinionRequestGraphResult.Completed(
            DeliveredCoverage(), productRequestCount: 6, SpentBudget(32));

        Assert.IsTrue(LuxembourgOpinionRequestCanaryPlan.Reconcile(inventory, [agreeing]).Reconciles);
        Assert.AreEqual(
            "CanaryCompleted",
            LuxembourgOpinionRequestCanaryPlan.Conclude(proceed, inventory, [agreeing]));

        // THE UNEQUAL-COUNT CASE. One more reservation was granted than anything recorded using it.
        var drifting = LuxembourgOpinionRequestGraphResult.Completed(
            DeliveredCoverage(), productRequestCount: 6, SpentBudget(33));

        var reconciliation = LuxembourgOpinionRequestCanaryPlan.Reconcile(inventory, [drifting]);
        Assert.IsFalse(reconciliation.Reconciles, "33 reservations against 32 accounted-for requests.");
        Assert.AreEqual(33, reconciliation.FinalBudgetSpent);
        Assert.AreEqual(32, reconciliation.ExpectedIfEverySessionCompleted);

        Assert.AreEqual(
            "AccountingDidNotReconcile",
            LuxembourgOpinionRequestCanaryPlan.Conclude(proceed, inventory, [drifting]),
            "a canary whose own numbers disagree has not demonstrated the path, whatever its rows "
                + "say: the figures that would evidence the run are the figures in dispute.");
    }

    /// <summary>
    /// The verdict cannot be told that a drift agrees, because nothing can say so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HOLE THIS CLOSES. <c>Reconciles</c> was a constructor parameter, so a record reading
    /// <c>(spent 33, expected 32, reconciles true)</c> was expressible and the verdict believed it;
    /// and because the record was an argument, one computed over a different pair of runs could be
    /// handed to a verdict about these. My own test helper did exactly that, which is the part worth
    /// recording: the suite was demonstrating the hole while passing.
    /// </para>
    /// <para>
    /// Both doors are shut by construction rather than by a check. <c>Reconciles</c> is derived from
    /// the counts, so it cannot contradict them; and <c>Conclude</c> derives the whole record from
    /// the runs it is concluding, so there is nowhere to put an unrelated one.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ADriftCannotBeCertifiedAsAgreementByACaller()
    {
        var drifting = LuxembourgOpinionRequestCanaryPlan.Reconcile(
            DeliveredInventory(Subjects(120), SpentBudget(25)),
            [
                LuxembourgOpinionRequestGraphResult.Completed(
                    DeliveredCoverage(), productRequestCount: 6, SpentBudget(33)),
            ]);

        Assert.IsFalse(drifting.Reconciles);
        Assert.IsFalse(
            typeof(LuxembourgOpinionRequestCanaryReconciliation).GetProperties()
                .Any(static value => value.Name == "Reconciles" && value.CanWrite),
            "a settable agreement is an agreement that need not match its own counts.");

        Assert.IsEmpty(
            typeof(LuxembourgOpinionRequestCanaryReconciliation)
                .GetConstructors(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance),
            "no public constructor, so the only way to obtain one is to derive it from real runs.");

        Assert.IsFalse(
            typeof(LuxembourgOpinionRequestCanaryPlan)
                .GetMethod(nameof(LuxembourgOpinionRequestCanaryPlan.Conclude))!
                .GetParameters()
                .Any(static value =>
                    value.ParameterType == typeof(LuxembourgOpinionRequestCanaryReconciliation)),
            "and the verdict takes no reconciliation, so none can be supplied to it at all.");
    }

    /// <summary>A stopped canary reconciles over the one session it opened.</summary>
    /// <remarks>
    /// Counting two sessions here would invent a robots fetch nobody sent, and turn every stopped
    /// canary into a false accounting finding.
    /// </remarks>
    [TestMethod]
    public void AStoppedCanaryReconcilesOverOneSession()
    {
        var reconciliation = LuxembourgOpinionRequestCanaryPlan.Reconcile(
            LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused,
                "the publisher refused",
                productRequestCount: 4,
                SpentBudget(5)),
            []);

        Assert.AreEqual(1, reconciliation.SessionsOpened);
        Assert.AreEqual(5, reconciliation.ExpectedIfEverySessionCompleted, "4 attempts + 1 robots.");
        Assert.IsTrue(reconciliation.Reconciles);
    }

    /// <summary>A budget that has granted every reservation its ceiling allows.</summary>
    private static WireBudgetSnapshot ExhaustedBudget() =>
        SpentBudget(LuxembourgOpinionRequestCanaryPlan.WireCeiling);

    /// <summary>A budget that has granted exactly this many reservations.</summary>
    private static WireBudgetSnapshot SpentBudget(int reservations)
    {
        var budget = WireRequestBudget.OfWireRequests(
            LuxembourgOpinionRequestCanaryPlan.WireCeiling);
        for (var taken = 0; taken < reservations; taken++)
        {
            Assert.IsTrue(budget.TryReserveAttempt(), "the canary ceiling covers this many.");
        }

        return WireBudgetSnapshot.Of(budget);
    }

    /// <summary>
    /// The full sweep acquires every batch the citation issues, and no more.
    /// </summary>
    /// <remarks>
    /// The ordinals come from the assignment the inventory's own citation issues, so a sweep cannot
    /// be aimed at a batch the class never had. 120 members at capacity 50 is three batches.
    /// </remarks>
    [TestMethod]
    public void TheFullSweepAcquiresEveryBatchTheCitationIssues()
    {
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));

        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory(inventory)
                .BatchOrdinalsToAcquire.ToArray(),
            "three batches for 120 members at capacity 50.");
    }

    /// <summary>
    /// The full-sweep gate inherits every stop the one-batch gate applies.
    /// </summary>
    /// <remarks>
    /// THE POINT OF DELEGATING RATHER THAN RESTATING. Two gates that each decided when traffic is
    /// allowed would be two rules that must agree, and every defect in this slice has been two
    /// things that were supposed to agree and did not. Checked against <c>AfterInventory</c> itself
    /// across every refusal the family can express, plus exhaustion and the empty class, so the two
    /// cannot diverge without this failing.
    /// </remarks>
    [TestMethod]
    public void TheFullSweepGateInheritsEveryStop()
    {
        var exhausted = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(exhausted.TryReserveAttempt());
        Assert.IsTrue(exhausted.TryReserveAttempt());

        var stopped = new List<LuxembourgOpinionRequestInventoryResult>
        {
            DeliveredInventory(Subjects(3), WireBudgetSnapshot.Of(exhausted)),
            DeliveredInventory([], UnspentBudget()),
        };
        foreach (var refusal in Enum.GetValues<LuxembourgOpinionRequestInventoryRefusal>()
                     .Where(static value => value != LuxembourgOpinionRequestInventoryRefusal.None))
        {
            stopped.Add(LuxembourgOpinionRequestInventoryResult.Refused(
                refusal, "refused for this case", productRequestCount: 3, UnspentBudget()));
        }

        foreach (var inventory in stopped)
        {
            var one = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);
            var every = LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory(inventory);

            Assert.AreEqual(0, every.BatchOrdinalsToAcquire.Count, every.Verdict);
            Assert.AreEqual(
                one.Verdict, every.Verdict,
                "the two gates must reach the same verdict on the same inventory.");
        }
    }

    /// <summary>
    /// A sweep that returned fewer batches than it was cleared for is not a completed sweep.
    /// </summary>
    /// <remarks>
    /// Reported BEFORE any per-batch verdict, because a cover built over a truncated set is not a
    /// cover whatever the batches say. The batch handed in below is a clean delivery with agreeing
    /// accounting, so nothing except the shortfall can be what produces the verdict.
    /// </remarks>
    [TestMethod]
    public void ASweepThatStoppedShortIsNotACompletedSweep()
    {
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));
        var cleared = LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory(inventory);
        Assert.AreEqual(3, cleared.BatchOrdinalsToAcquire.Count);

        var onlyOne = new[]
        {
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 3, SpentBudget(30)),
        };

        Assert.AreEqual(
            "SweepStoppedBeforeEveryBatch",
            LuxembourgOpinionRequestCanaryPlan.Conclude(cleared, inventory, onlyOne),
            "one batch back out of three cleared is a stopped sweep, not a completed one.");
    }

    /// <summary>The accounting is one mechanism over N batches, not a second one for sweeps.</summary>
    /// <remarks>
    /// A sweep of three batches opens four sessions: the inventory's and one per batch. Counting
    /// them any other way would invent or lose a robots fetch per batch.
    /// </remarks>
    [TestMethod]
    public void TheAccountingGeneralisesToManyBatches()
    {
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));
        var batches = new[]
        {
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(30)),
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(35)),
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(40)),
        };

        var reconciliation = LuxembourgOpinionRequestCanaryPlan.Reconcile(inventory, batches);

        Assert.AreEqual(4, reconciliation.SessionsOpened, "one inventory session plus three batches.");
        Assert.AreEqual(36, reconciliation.RecordedProductRequests, "24 + 4 + 4 + 4.");
        Assert.AreEqual(40, reconciliation.FinalBudgetSpent, "the last batch's cumulative reading.");
        Assert.AreEqual(40, reconciliation.ExpectedIfEverySessionCompleted, "36 + 4.");
        Assert.IsTrue(reconciliation.Reconciles);
    }

    /// <summary>
    /// The two scopes never share a name a reader would act on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFECT THIS CLOSES. The retained evidence index of a completed 156-batch sweep read
    /// <c>gate: ProceedToOneBatch</c> and <c>verdict: CanaryCompleted</c>. Every measured field in
    /// that artifact was correct; the two words a reader starts from were not, and both named the
    /// one-batch canary. Accurate data under a false heading is worse than missing data, because
    /// nobody goes looking for what they have already been told.
    /// </para>
    /// <para>
    /// Checked as a pair, not individually: the requirement is that the labels DIFFER by scope, so
    /// asserting each in isolation would still pass if both were renamed to the same thing.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheOneBatchAndFullSweepPathsCarryDistinctTruthfulNames()
    {
        var inventory = DeliveredInventory(Subjects(120), SpentBudget(25));

        var one = LuxembourgOpinionRequestCanaryPlan.AfterInventory(inventory);
        var every = LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory(inventory);

        Assert.AreEqual("ProceedToOneBatch", one.Verdict);
        Assert.AreEqual("ProceedToEveryBatch", every.Verdict);
        Assert.AreNotEqual(one.Verdict, every.Verdict, "a widened gate must say so in its own name.");
        Assert.AreEqual(LuxembourgOpinionRequestAcquisitionScope.OneBatch, one.Scope);
        Assert.AreEqual(LuxembourgOpinionRequestAcquisitionScope.EveryBatch, every.Scope);

        // One batch back for the canary; three for the sweep it cleared.
        var oneBatch = new[]
        {
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(30)),
        };
        var threeBatches = new[]
        {
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(30)),
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(35)),
            LuxembourgOpinionRequestGraphResult.Completed(
                DeliveredCoverage(), productRequestCount: 4, SpentBudget(40)),
        };

        var canaryVerdict = LuxembourgOpinionRequestCanaryPlan.Conclude(one, inventory, oneBatch);
        var sweepVerdict = LuxembourgOpinionRequestCanaryPlan.Conclude(every, inventory, threeBatches);

        Assert.AreEqual("CanaryCompleted", canaryVerdict, "the one-batch name is kept, not reused.");
        Assert.AreEqual("SweepCompleted", sweepVerdict, "a completed sweep is not a completed canary.");
        Assert.AreNotEqual(
            canaryVerdict, sweepVerdict,
            "the two completed paths must not report the same word to a reader of the evidence.");
    }

    /// <summary>A stop keeps its shared name, and still reports the scope it was asked for.</summary>
    /// <remarks>
    /// The stop verdicts are true of either scope - a refused inventory issues no batch whatever was
    /// asked for - so renaming them per scope would invent a distinction the evidence does not have.
    /// Only the scope moves, which is what a reader needs to know what was attempted.
    /// </remarks>
    [TestMethod]
    public void AStopKeepsItsSharedNameButReportsTheScopeAttempted()
    {
        var refused = LuxembourgOpinionRequestInventoryResult.Refused(
            LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused,
            "the publisher refused",
            productRequestCount: 1,
            SpentBudget(2));

        var one = LuxembourgOpinionRequestCanaryPlan.AfterInventory(refused);
        var every = LuxembourgOpinionRequestCanaryPlan.EveryBatchAfterInventory(refused);

        Assert.AreEqual("InventoryRefused", one.Verdict);
        Assert.AreEqual("InventoryRefused", every.Verdict, "the stop is true of either scope.");
        Assert.AreEqual(LuxembourgOpinionRequestAcquisitionScope.OneBatch, one.Scope);
        Assert.AreEqual(
            LuxembourgOpinionRequestAcquisitionScope.EveryBatch, every.Scope,
            "but the reader still learns which was attempted.");
    }

    /// <summary>
    /// A real completed matrix over one typed request.
    /// </summary>
    /// <remarks>
    /// Built through <c>TryComplete</c> rather than faked, because the case under test is a
    /// DELIVERED batch: a stand-in that never satisfied the coverage door would prove the verdict
    /// for a result the producers could not return.
    /// </remarks>
    private static LuxembourgOpinionRequestCoverage DeliveredCoverage()
    {
        var requests = Subjects(1);
        var assignment = LuxembourgOpinionRequestBatchAssignment.Over(requests, Citation(requests))[0];
        var (proof, delivered) = AbsenceFixtures.OpinionRequestGraphRows(
            assignment.PartitionKey,
            [(requests[0], RdfType, RequestClass, true)]);

        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            proof, delivered, assignment, out var refusal, out var detail);
        Assert.IsNotNull(coverage, $"{refusal}: {detail}");
        return coverage;
    }

    private static LuxembourgOpinionRequestInventoryCitation Citation(IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    private static readonly string RequestClass =
        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri;

    private static WireBudgetSnapshot UnspentBudget() =>
        WireBudgetSnapshot.Of(
            WireRequestBudget.OfWireRequests(LuxembourgOpinionRequestCanaryPlan.WireCeiling));

    private static LuxembourgOpinionRequestInventoryResult DeliveredInventory(
        IReadOnlyList<string> subjects,
        WireBudgetSnapshot budget)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, _) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, ordered);

        return LuxembourgOpinionRequestInventoryProducer.DecodeRows(
            [.. ordered.Select(DecodedRow)],
            LuxembourgOpinionRequestInventoryDiscoveryPlan.Create().CreateDeliveryProfile(),
            proof,
            productRequestCount: 24,
            budget);
    }

    /// <summary>One decoded row in the inventory's own projected order.</summary>
    private static RepeatedEnumerationRow DecodedRow(string subject)
    {
        var key = new[]
        {
            RepeatedEnumerationRdfTerm.Literal(subject, null, null),
            RepeatedEnumerationRdfTerm.Literal(
                LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind, null, null),
        };
        return new RepeatedEnumerationRow(
            [
                RepeatedEnumerationRdfTerm.Iri(subject),
                key[1],
                RepeatedEnumerationRdfTerm.Literal(
                    "1", "http://www.w3.org/2001/XMLSchema#integer", null),
                key[0],
                key[1],
            ],
            key,
            key);
    }

    private static IReadOnlyList<string> Subjects(int count) =>
        Enumerable.Range(0, count)
            .Select(static index =>
                $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
}
