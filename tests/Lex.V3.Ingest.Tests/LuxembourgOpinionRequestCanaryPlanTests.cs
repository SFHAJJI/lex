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
        var stopped = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            LuxembourgOpinionRequestInventoryResult.Refused(
                LuxembourgOpinionRequestInventoryRefusal.EnumerationRefused,
                "the publisher refused",
                productRequestCount: 1,
                UnspentBudget()));

        Assert.AreEqual(
            "InventoryRefused",
            LuxembourgOpinionRequestCanaryPlan.Conclude(stopped, batch: null),
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
        var proceed = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            DeliveredInventory(Subjects(120), UnspentBudget()));

        var spent = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(spent.TryReserveAttempt());
        Assert.IsTrue(spent.TryReserveAttempt());

        Assert.AreEqual(
            "BudgetExhaustedDuringBatch",
            LuxembourgOpinionRequestCanaryPlan.Conclude(
                proceed,
                LuxembourgOpinionRequestGraphResult.Refused(
                    LuxembourgOpinionRequestGraphRefusal.EnumerationRefused,
                    "stopped at the ceiling",
                    productRequestCount: 1,
                    WireBudgetSnapshot.Of(spent))));

        Assert.AreEqual(
            "BatchRefused",
            LuxembourgOpinionRequestCanaryPlan.Conclude(
                proceed,
                LuxembourgOpinionRequestGraphResult.Refused(
                    LuxembourgOpinionRequestGraphRefusal.MatrixNotCompleted,
                    "the rows did not complete a matrix",
                    productRequestCount: 6,
                    UnspentBudget())),
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
        var proceed = LuxembourgOpinionRequestCanaryPlan.AfterInventory(
            DeliveredInventory(Subjects(120), UnspentBudget()));

        var atTheCeiling = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(atTheCeiling.TryReserveAttempt());
        Assert.IsTrue(atTheCeiling.TryReserveAttempt());
        var snapshot = WireBudgetSnapshot.Of(atTheCeiling);
        Assert.AreEqual(snapshot.Limit, snapshot.Spent, "the batch ended exactly at its ceiling.");

        Assert.AreEqual(
            "BudgetExhaustedDuringBatch",
            LuxembourgOpinionRequestCanaryPlan.Conclude(
                proceed,
                LuxembourgOpinionRequestGraphResult.Completed(
                    DeliveredCoverage(), productRequestCount: 1, snapshot)),
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
