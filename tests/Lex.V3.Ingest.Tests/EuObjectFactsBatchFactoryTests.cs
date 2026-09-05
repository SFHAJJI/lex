using System;
using System.Linq;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The batch factory's own contract: what it asks each family about, and that the batches it mints
/// do not depend on the order it was handed the objects.
/// </summary>
/// <remarks>
/// THE ORDER TEST EXISTS BECAUSE A MUTATION SURVIVED. Reversing the factory's sort left the whole
/// ingest suite green, which means the factory's own doc claimed a reproducible partition key that
/// nothing held. The claim is real and load bearing, since
/// <c>EuObjectFactsDiscoveryPlan.PartitionKeyFor</c> digests the batch's sorted members and a
/// reader diffing two runs reads a moved key as a moved corpus. A claim in a comment that no test
/// holds is exactly the shape this repository keeps being caught by, so it is held here.
/// </remarks>
[TestClass]
public sealed class EuObjectFactsBatchFactoryTests
{
    private static EuObjectFactsBatchPolicy Policy()
    {
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        return new EuObjectFactsBatchPolicy(
            plan, planId, EuAcquisitionTestFixture.BuildRendererSource(7001),
            EuAcquisitionTestFixture.SourceWitness());
    }

    [TestMethod]
    public void TheBatchesDoNotDependOnTheOrderTheObjectsArrivedIn()
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)!;
        var forward = new[] { root, root + "/state-a", root + "/state-b" };
        var reversed = forward.Reverse().ToArray();

        var first = EuObjectFactsBatchFactory.Build(Policy(), forward, [root]);
        var second = EuObjectFactsBatchFactory.Build(Policy(), reversed, [root]);

        Assert.AreEqual(
            Render(first),
            Render(second),
            "the same objects in a different order must mint the same batches, or the partition "
                + "key moves and a reader diffing two runs sees a corpus that did not move.");

        // NOT ENOUGH ON ITS OWN, and a mutation proved it: reversing the factory's sort left the
        // comparison above green, because both calls go through the SAME sort and therefore agree
        // with each other whatever it does. Two runs of a wrong rule agree perfectly. So the ORDER
        // ITSELF is pinned against a literal the test computes independently.
        var expected = new[] { root, root + "/state-a", root + "/state-b" };
        Assert.AreEqual(
            expected.Length,
            expected.Distinct(StringComparer.Ordinal).Count(),
            "the fixture's own objects must be distinct for this to mean anything.");
        CollectionAssert.AreEqual(
            expected,
            first.First(static request => request.Set == EuObjectFactsQuerySet.ObjectFacts)
                .BatchObjects.ToArray(),
            "the batch must be in ASCENDING ORDINAL order, which is what PartitionKeyFor digests.");
    }

    [TestMethod]
    public void DuplicatesCollapseRatherThanPaddingTheBatchTwice()
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)!;
        var state = root + "/state-a";

        var once = EuObjectFactsBatchFactory.Build(Policy(), [root, state], [root]);
        var twice = EuObjectFactsBatchFactory.Build(Policy(), [root, state, state, root], [root]);

        Assert.AreEqual(Render(once), Render(twice), "a repeated object is one member, not two.");
    }

    [TestMethod]
    public void FamilyWIsAskedOnlyAboutPackRootsAndTheOthersAboutTheWholeObjectSet()
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)!;
        var state = root + "/state-a";

        var requests = EuObjectFactsBatchFactory.Build(Policy(), [root, state], [root]);

        var watermark = requests
            .Where(static request => request.Set == EuObjectFactsQuerySet.RootWatermark)
            .ToArray();
        Assert.AreEqual(1, watermark.Length, "one batch of roots.");
        CollectionAssert.AreEqual(
            new[] { root },
            watermark[0].BatchObjects.ToArray(),
            "family W reads a pack root's own watermark; a consolidated state is not a pack root "
                + "and the plan refuses one in this batch.");

        foreach (var set in EuObjectFactsBatchFactory.SetsOverObservedObjects)
        {
            var forSet = requests.Where(request => request.Set == set).ToArray();
            Assert.AreEqual(1, forSet.Length, set + " must have exactly one batch here.");
            CollectionAssert.AreEqual(
                new[] { root, state },
                forSet[0].BatchObjects.ToArray(),
                set + " is asked about the whole observed object set, states included.");
        }
    }

    [TestMethod]
    public void ABatchNeverExceedsTheCapacityTheParameterCeilingAllows()
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)!;
        var many = Enumerable.Range(0, EuObjectFactsDiscoveryPlan.BatchCapacity + 7)
            .Select(index => root + "/state-" + index.ToString("D3", null))
            .Append(root)
            .ToArray();

        var requests = EuObjectFactsBatchFactory.Build(Policy(), many, [root]);

        foreach (var request in requests)
        {
            Assert.IsTrue(
                request.BatchObjects.Count <= EuObjectFactsDiscoveryPlan.BatchCapacity,
                "a batch of " + request.BatchObjects.Count + " members cannot be bound: every "
                    + "member travels as its own parameter and the plan's ceiling is "
                    + EuObjectFactsDiscoveryPlan.BatchCapacity + ".");
        }

        var pBatches = requests.Count(static request =>
            request.Set == EuObjectFactsQuerySet.ObjectFacts);
        Assert.AreEqual(2, pBatches, "capacity plus eight objects is two batches, not one.");
    }

    private static string Render(System.Collections.Generic.IReadOnlyList<EuObjectFactsPartitionRunRequest> requests) =>
        string.Join("\n", requests.Select(static request =>
            request.Set + ": " + string.Join(",", request.BatchObjects)));
}
