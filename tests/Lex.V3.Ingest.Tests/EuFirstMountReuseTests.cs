using System.Net;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuFirstMountAcquisitionTests
{
    [TestMethod]
    public async Task ReusedPopulationFetchesOnlyOneNewRightsNoticeAndReopensItsNewCatalog()
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        using var handler = RightsOnlyHandler();
        var budget = WireRequestBudget.OfWireRequests(10);
        var renewed = await Acquisition(copy, handler).ReuseAsync(capture.Result.CheckpointRef!, [capture.Seed], capture.Renderers.DocumentFetch.CopyBytes(), budget, CancellationToken.None);
        Assert.IsTrue(renewed.Delivered, renewed.Detail);
        Assert.AreEqual(1, handler.RightsRequests.Count(uri => uri == NoticeUri));
        Assert.AreEqual(0, handler.AdapterRequests + handler.FormexEnumerationRequests + handler.FormexPackageRequests);
        Assert.AreEqual(handler.RightsRequests.Count, budget.Spent);
        Assert.AreEqual(capture.Result.Run!.CorpusRecordSetRef, renewed.Run!.CorpusRecordSetRef);
        Assert.AreEqual(capture.Result.Formex!.AcquiredExpressionCount, renewed.Formex!.AcquiredExpressionCount);
        Assert.AreNotEqual(capture.Result.CheckpointRef, renewed.CheckpointRef);
        CollectionAssert.AreNotEqual(capture.Result.LegalNotice!.Route!.CopyCanonicalBytes(), renewed.LegalNotice!.Route!.CopyCanonicalBytes());
        var replay = await EuFirstMountAcquisition.ReopenAsync(copy, renewed.CheckpointRef!, [capture.Seed], CancellationToken.None);
        Assert.IsTrue(replay.Delivered, replay.Detail);
        CollectionAssert.AreEqual(renewed.LegalNotice.Route.CopyCanonicalBytes(), replay.LegalNotice!.Route!.CopyCanonicalBytes());
        Assert.AreEqual(1, handler.RightsRequests.Count(uri => uri == NoticeUri));
    }

    [TestMethod]
    public async Task ReuseKeepsConsolidatedEnglishAndFrenchBodiesWithoutCelex()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var original = await AcquireConsolidatedAsync(store, missingStateCelex: true);
        Assert.IsTrue(original.Delivered, original.Detail);
        var copy = await CopyAcquisitionStoreAsync(store);
        using var handler = RightsOnlyHandler();
        var budget = WireRequestBudget.OfWireRequests(10);
        var renewed = await Acquisition(copy, handler).ReuseAsync(original.CheckpointRef!,
            [ConsolidatedSeed], await File.ReadAllBytesAsync(Path.Combine(CheckoutRoot(), EuRendererSources.RendererFiles[3])), budget, CancellationToken.None);
        Assert.IsTrue(renewed.Delivered, renewed.Detail);
        Assert.AreEqual(4, renewed.Formex!.AcquiredExpressionCount);
        Assert.AreEqual(1, handler.RightsRequests.Count(uri => uri == NoticeUri));
        Assert.AreEqual(0, handler.AdapterRequests + handler.FormexEnumerationRequests + handler.FormexPackageRequests);
        Assert.AreEqual(handler.RightsRequests.Count, budget.Spent);
        var replay = await EuFirstMountAcquisition.ReopenAsync(copy, renewed.CheckpointRef!,
            [ConsolidatedSeed], CancellationToken.None);
        Assert.IsTrue(replay.Delivered, replay.Detail);
        Assert.AreEqual(4, replay.Formex!.AcquiredExpressionCount);
        CollectionAssert.AreEqual(EuropeIndexBuilder.ProjectStates(original.Run!.ObservedWorkFacts),
            EuropeIndexBuilder.ProjectStates(replay.Run!.ObservedWorkFacts));
        Assert.IsNull(EuropeIndexBuilder.ProjectStates(replay.Run.ObservedWorkFacts)
            .Single(state => state.PublisherWorkIri == ConsolidatedWork).PublisherWorkCelex);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidRetainedPopulationRefusesBeforeRightsTraffic(bool wrongScope)
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store, omit: wrongScope ? null : capture.Result.CheckpointRef!.Sha256);
        using var handler = RightsOnlyHandler();
        var budget = WireRequestBudget.OfWireRequests(10);
        try
        {
            await Acquisition(copy, handler).ReuseAsync(capture.Result.CheckpointRef!,
                [wrongScope ? "foreign" : capture.Seed], capture.Renderers.DocumentFetch.CopyBytes(), budget, CancellationToken.None);
            Assert.Fail("Invalid retained population must refuse before any publisher request.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
        Assert.AreEqual(0, budget.Spent);
        Assert.AreEqual(0, handler.RightsRequests.Count + handler.AdapterRequests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReusedPopulationDoesNotBypassTheNewBuildRightsGate(bool disallowed)
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        using var handler = RightsOnlyHandler(disallowed ? "User-agent: *\nDisallow: /\n" : null,
            disallowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        var result = await Acquisition(copy, handler).ReuseAsync(capture.Result.CheckpointRef!, [capture.Seed], capture.Renderers.DocumentFetch.CopyBytes(),
            WireRequestBudget.OfWireRequests(10), CancellationToken.None);
        Assert.AreEqual(EuFirstMountAcquisitionRefusal.LegalNoticeRefused, result.Refusal);
        Assert.IsNull(result.CheckpointRef);
        Assert.AreEqual(0, handler.AdapterRequests + handler.FormexEnumerationRequests + handler.FormexPackageRequests);
        Assert.AreEqual(disallowed ? 0 : 1, handler.RightsRequests.Count(uri => uri == NoticeUri));
    }

    [TestMethod]
    public async Task ReuseCancellationAndWeakerCustodyCannotReachThePublisher()
    {
        var capture = await RetainedAcquisition.Value;
        using var handler = RightsOnlyHandler();
        var budget = WireRequestBudget.OfWireRequests(10);
        var weak = await CopyAcquisitionStoreAsync(capture.Store, weaker: true);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => Acquisition(weak, handler).ReuseAsync(
            capture.Result.CheckpointRef!, [capture.Seed], capture.Renderers.DocumentFetch.CopyBytes(), budget, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Acquisition(capture.Store, handler).ReuseAsync(
            capture.Result.CheckpointRef!, [capture.Seed], capture.Renderers.DocumentFetch.CopyBytes(), budget, cancellation.Token));
        Assert.AreEqual(0, budget.Spent);
        Assert.AreEqual(0, handler.RightsRequests.Count + handler.AdapterRequests);
    }

    [TestMethod]
    public async Task ChangedCurrentRendererRefusesBeforeAnyRightsRequest()
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        using var handler = RightsOnlyHandler();
        var budget = WireRequestBudget.OfWireRequests(10);
        var changed = capture.Renderers.DocumentFetch.CopyBytes().ToArray();
        changed[0] ^= 1;
        var error = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => Acquisition(copy, handler).ReuseAsync(
            capture.Result.CheckpointRef!, [capture.Seed], changed, budget, CancellationToken.None));
        StringAssert.Contains(error.Message, "renderer source differs");
        Assert.AreEqual(0, budget.Spent);
        Assert.AreEqual(0, handler.RightsRequests.Count + handler.AdapterRequests
            + handler.FormexEnumerationRequests + handler.FormexPackageRequests);
    }

    private static CompositeHandler RightsOnlyHandler(string? robots = null, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(), new Dictionary<string, string[]>(), robots, status);
}
