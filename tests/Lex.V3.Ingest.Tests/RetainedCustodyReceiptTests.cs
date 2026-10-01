using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class RetainedCustodyReceiptTests
{
    [TestMethod]
    public async Task OriginalReceiptIdentitySurvivesAFreshVerifiedHold()
    {
        var inner = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var store = new LuxembourgGazetteAcquisitionTests.GazetteCustodyStore(inner) { AdvanceObservationPerCreate = true };
        var original = await store.CreateAsync("original body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var digest = await RetainedCustodyReceipt.HoldAsync(store, original, CancellationToken.None);
        var current = await store.CreateAsync("original body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        Assert.AreNotEqual(DurableBlobWriteReceiptDigest.Of(original), DurableBlobWriteReceiptDigest.Of(current));
        var restored = await RetainedCustodyReceipt.ReopenAsync(store, digest, current, CancellationToken.None);
        Assert.AreEqual(DurableBlobWriteReceiptDigest.Of(original), DurableBlobWriteReceiptDigest.Of(restored));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OtherBytesOrWeakerCurrentMembershipCannotUseHistoricalReceipt(bool weaker)
    {
        var source = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var original = await source.CreateAsync("original body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var digest = await RetainedCustodyReceipt.HoldAsync(source, original, CancellationToken.None);
        var target = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        await target.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var current = await target.CreateAsync(weaker ? "original body"u8.ToArray() : "different body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => RetainedCustodyReceipt.ReopenAsync(target, digest, current, CancellationToken.None));
    }

    [TestMethod]
    public async Task ACheckedDigestContainingNullCannotBecomeAReceipt()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var current = await store.CreateAsync("body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var malformed = await store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => RetainedCustodyReceipt.ReopenAsync(store,
            malformed.Reference.ContentSha256, current, CancellationToken.None));
    }
}
