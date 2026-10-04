using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgObservedObjectIdentitySetTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(3000, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    [DataRow(3000, true)]
    public async Task RebuiltObservedIdentitiesKeepOriginalReferenceAndCurrentCustody(int count, bool weaker)
    {
        var observations = Enumerable.Range(0, count).Select(index => Observation($"https://data.legilux.lu/eli/rebuild/{index:D5}")).ToArray();
        var originalStore = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var original = await new LuxembourgObservedObjectIdentitySetWriter(originalStore).WriteAsync(RunIdentity, observations, CancellationToken.None);
        Assert.IsNull(original.Refusal, original.Refusal?.Detail);
        var originalBytes = await originalStore.ReadByDigestAsync(original.RetainedSetReceipt!.Reference.ContentSha256, CancellationToken.None);
        foreach (var store in new[] { new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker),
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker) })
        {
            var result = await new LuxembourgObservedObjectIdentitySetWriter(store).RebuildAsync(RunIdentity,
                observations.Reverse().Concat(observations.Take(1)).ToArray(), original.SetRef!, CancellationToken.None);
            Assert.IsNull(result.Refusal, result.Refusal?.Detail);
            Assert.AreEqual(original.SetRef, result.SetRef);
            Assert.AreEqual(weaker ? CustodyProtection.NotEnforced : CustodyProtection.LockedTime, result.RetainedSetReceipt!.PolicyEvidence.Protection);
            Assert.AreEqual(count, result.VerifiedSet!.Set.ObjectRefSha256Values.Count);
            var bytes = await store.ReadByDigestAsync(result.RetainedSetReceipt.Reference.ContentSha256, CancellationToken.None);
            CollectionAssert.AreEqual(originalBytes.ToArray(), bytes.ToArray());
            var read = await new LuxembourgObservedObjectIdentitySetReader(store).ReadAsync(
                result.RetainedSetReceipt, result.SetRef!, RunIdentity, CancellationToken.None);
            Assert.IsNull(read.Refusal, read.Refusal?.Detail);
            Assert.AreEqual(original.SetRef, read.VerifiedSet!.SetRef);
        }
    }

    [TestMethod]
    [DataRow("digest")]
    [DataRow("run")]
    [DataRow("objects")]
    public async Task ChangedObservedIdentityInputsRefuseBeforeWriting(string changed)
    {
        var original = await new LuxembourgObservedObjectIdentitySetWriter(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .WriteAsync(RunIdentity, [Observation("https://data.legilux.lu/eli/a")], CancellationToken.None);
        Assert.IsNull(original.Refusal);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var run = changed == "run" ? new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", RunIdentity.Sha256) : RunIdentity;
        var reference = changed == "digest" ? new SourceArtifactRef(original.SetRef!.ResourceId, new string('a', 64)) : original.SetRef!;
        var observation = Observation(changed == "objects" ? "https://data.legilux.lu/eli/b" : "https://data.legilux.lu/eli/a");
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => new LuxembourgObservedObjectIdentitySetWriter(store)
            .RebuildAsync(run, [observation], reference, CancellationToken.None));
        Assert.AreEqual(0, store.CreateCallCount);
    }

    [TestMethod]
    public async Task ObservedIdentityRebuildStillRequiresSuccessfulCurrentHold()
    {
        var original = await new LuxembourgObservedObjectIdentitySetWriter(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .WriteAsync(RunIdentity, [], CancellationToken.None);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failWriteDigest: (_, _) => true);
        var result = await new LuxembourgObservedObjectIdentitySetWriter(store).RebuildAsync(RunIdentity, [], original.SetRef!, CancellationToken.None);
        Assert.AreEqual(LuxembourgObservedObjectIdentitySetWriteRefusalKind.IdentitySetNotRetained, result.Refusal!.Kind);
        Assert.IsNull(result.VerifiedSet);
        Assert.IsNull(result.SetRef);
    }

    [TestMethod]
    public async Task CancelledObservedIdentityRebuildWritesNothing()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new LuxembourgObservedObjectIdentitySetWriter(store)
            .RebuildAsync(RunIdentity, [], RunIdentity, cancellation.Token));
        Assert.AreEqual(0, store.CreateCallCount);
    }

    [TestMethod]
    public async Task OrdinaryIdentityWritesStillMintDistinctReferences()
    {
        var writer = new LuxembourgObservedObjectIdentitySetWriter(new EuAcquisitionTestFixture.EuInMemoryCustodyStore());
        var first = await writer.WriteAsync(RunIdentity, [], CancellationToken.None);
        var second = await writer.WriteAsync(RunIdentity, [], CancellationToken.None);
        Assert.AreEqual(first.SetRef!.Sha256, second.SetRef!.Sha256);
        Assert.AreNotEqual(first.SetRef.ResourceId, second.SetRef.ResourceId);
    }

    [TestMethod]
    public async Task ObservedIdentityRebuildRequiresAnOriginalReference()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => new LuxembourgObservedObjectIdentitySetWriter(store)
            .RebuildAsync(RunIdentity, [], null!, CancellationToken.None));
        Assert.AreEqual(0, store.CreateCallCount);
    }
}
