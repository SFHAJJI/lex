using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What a run of the OpinionRequest family can be asked to sweep, before any of it is sent.
/// </summary>
/// <remarks>
/// The executor is the first place this family can reach the publisher, so the question these cases
/// ask is not whether a run works but whether a run can be aimed at something the inventory never
/// proved. Nothing here sends anything.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestRunRequestTests
{
    private const string PlanResourceId = "urn:uuid:6a1f0c74-58b2-4d39-9e70-2b5c84d1af63";

    /// <summary>
    /// Every batch a run can name comes out of the inventory it cites.
    /// </summary>
    /// <remarks>
    /// THE ONLY DOOR. A run names a batch by ordinal into the batches the proven inventory itself
    /// assigns, so the members and the citation cannot disagree - there is no overload taking a
    /// caller's list of requests.
    /// </remarks>
    [TestMethod]
    public void EveryBatchARunCanNameComesOutOfTheInventoryItCites()
    {
        var population = Subjects(Capacity + 10);
        var inventory = Inventory(population);
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var source = RendererSource();
        var assigned = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory);

        Assert.AreEqual(2, assigned.Count, "sixty requests is two batches.");

        for (var ordinal = 0; ordinal < assigned.Count; ordinal++)
        {
            var request = LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                plan, population, inventory, ordinal, PlanResourceId, source);

            CollectionAssert.AreEqual(
                assigned[ordinal].Requests.ToArray(), request.BatchRequests.ToArray());
            Assert.AreEqual(inventory, request.Inventory, "one inventory, not two values.");
            Assert.AreEqual(ordinal, request.BatchOrdinal);
        }
    }

    /// <summary>There is no ordinal naming requests the inventory never contained.</summary>
    [TestMethod]
    public void NoOrdinalNamesRequestsTheInventoryNeverContained()
    {
        var population = Subjects(Capacity + 10);
        var inventory = Inventory(population);
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var source = RendererSource();
        var assigned = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                plan, population, inventory, assigned.Count, PlanResourceId, source));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                plan, population, inventory, -1, PlanResourceId, source));
    }

    /// <summary>
    /// A population this citation does not digest cannot be swept at all.
    /// </summary>
    /// <remarks>
    /// The gate one door earlier than the draft family's. There, the run request reaches into a
    /// production result for its citation; here it takes a population and a citation and lets the
    /// assignment door refuse the pairing - so a run cannot be aimed at a class the inventory never
    /// enumerated, and a batch cover measured afterwards cannot be widened by a caller either.
    /// </remarks>
    [TestMethod]
    public void APopulationTheCitationDoesNotDigestCannotBeSwept()
    {
        var population = Subjects(4);
        var inventory = Inventory(population);

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestGraphRunRequest.ForBatch(
                LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
                [.. population, Request(900)],
                inventory,
                0,
                PlanResourceId,
                RendererSource()),
            "a population with a member the citation never digested is not this inventory's.");
    }

    /// <summary>
    /// The batch a run binds is the plan's canonical form, not the caller's spelling.
    /// </summary>
    /// <remarks>
    /// The publisher is asked about the canonical batch and answers in it, so a membership check
    /// against the raw request order would refuse honest rows. The assignment is already
    /// canonicalised, and this pins that the request exposes that form rather than re-deriving one.
    /// </remarks>
    [TestMethod]
    public void TheBoundBatchIsThePlansCanonicalFormOfItsMembers()
    {
        var population = Subjects(6);
        var inventory = Inventory(population);
        var request = LuxembourgOpinionRequestGraphRunRequest.ForBatch(
            LuxembourgOpinionRequestGraphDiscoveryPlan.Create(),
            population,
            inventory,
            0,
            PlanResourceId,
            RendererSource());

        CollectionAssert.AreEqual(
            LuxembourgOpinionRequestGraphDiscoveryPlan
                .RequestedPartitionMembers(request.BatchRequests).ToArray(),
            request.BatchRequests.ToArray(),
            "canonicalising the members again must be a no-op, or the run asks one thing and "
                + "checks another.");
    }

    /// <summary>
    /// The subject this family checks batch membership on is the first cursor key.
    /// </summary>
    /// <remarks>
    /// The executor is passed ordinal 0 for this family, and that number is only right because
    /// <c>key_1</c> is <c>STR(?request)</c>. Pinned against the plan's rendered template rather than
    /// against the constant beside it: a bare 0 agreeing with a bare 0 proves nothing about which
    /// column carries the subject.
    /// </remarks>
    [TestMethod]
    public void BatchMembershipIsCheckedOnTheKeyThatCarriesTheRequest()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        Assert.AreEqual(
            "key_1", profile.CursorVariables[0],
            "ordinal 0 must name the first cursor key.");
        StringAssert.Contains(
            plan.PageTemplate,
            "BIND(STR(?request) AS ?key_1)",
            "and that key must be the request itself, or membership is checked on the wrong column.");
    }

    /// <summary>The inventory run names no selection, because the question is the class.</summary>
    /// <remarks>
    /// A caller able to narrow the inventory could narrow what "complete" means for every batch,
    /// cover and absence downstream. The record carries a plan, a resource id and a renderer source,
    /// and there is nowhere to put a subject list.
    /// </remarks>
    [TestMethod]
    public void TheInventoryRunCarriesNoSelection()
    {
        var request = new LuxembourgOpinionRequestInventoryRunRequest(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.Create(), PlanResourceId, RendererSource());

        Assert.AreEqual(
            3,
            typeof(LuxembourgOpinionRequestInventoryRunRequest).GetProperties().Length,
            "a fourth property on this record is a selection arriving by another name.");
        Assert.IsNotNull(request.Plan);
    }

    private const int Capacity = LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity;

    private static MachineQueryRendererSource RendererSource() =>
        LuxembourgAcquisitionTestFixture.BuildRendererSource(9801);

    private static LuxembourgOpinionRequestInventoryCitation Inventory(IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private static IReadOnlyList<string> Subjects(int count) =>
        Enumerable.Range(0, count).Select(Request)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
