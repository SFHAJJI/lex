using System.Globalization;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The dated EU catalog (<c>lex-eu-first-mount-acquisition/3</c>): a run that was not resumed states when it retained its
/// catalog, the reopen and every later reader carry that instant, a reuse never moves it forward, and the catalogs written
/// before (<c>/1</c>, undated) reopen and renew exactly as they always did.
/// </summary>
public sealed partial class EuFirstMountAcquisitionTests
{
    private const string DatedCatalogSchema = "lex-eu-first-mount-acquisition/3";
    private const string UndatedCatalogSchema = "lex-eu-first-mount-acquisition/1";

    [TestMethod]
    public async Task AFreshRunRetainsADatedCatalogWhoseReopenStatesTheSameInstant()
    {
        var capture = await RetainedAcquisition.Value;
        Assert.IsNull(capture.Result.Resumption, "a run that was not resumed states no resumption");
        Assert.IsNotNull(capture.Result.CatalogedAt, "a run that was not resumed states when it retained its catalog");
        var root = await AcquisitionRootAsync(capture.Store, capture.Result.CheckpointRef!);
        Assert.AreEqual(DatedCatalogSchema, root["schema"]!.GetValue<string>());
        Assert.AreEqual(capture.Result.CatalogedAt, DateTimeOffset.Parse(root["cataloged_at"]!.GetValue<string>(), CultureInfo.InvariantCulture));

        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        var reopened = await EuFirstMountAcquisition.ReopenAsync(copy, capture.Result.CheckpointRef!, [capture.Seed], CancellationToken.None);
        Assert.AreEqual(capture.Result.CatalogedAt, reopened.CatalogedAt);
        Assert.IsNull(reopened.Resumption);

        // The same bytes held again reopen: the refusals below are the changes', not the round trip's.
        var control = await HoldAcquisitionRootAsync(copy, root);
        Assert.AreEqual(capture.Result.CheckpointRef!.Sha256, control.Sha256, "a dated catalog round-trips to its own canonical bytes");
    }

    [TestMethod]
    [DataRow("time_removed")]
    [DataRow("labelled_undated")]
    [DataRow("labelled_resumed")]
    public async Task ADatedCatalogCannotLoseItsTimeOrBeRelabelled(string changed)
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        var root = (JsonObject)await AcquisitionRootAsync(copy, capture.Result.CheckpointRef!);
        switch (changed)
        {
            case "time_removed": root.Remove("cataloged_at"); break;
            case "labelled_undated": root["schema"] = UndatedCatalogSchema; break;
            case "labelled_resumed": root["schema"] = "lex-eu-first-mount-acquisition/2"; break;
        }

        var checkpoint = await HoldAcquisitionRootAsync(copy, root);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFirstMountAcquisition.ReopenAsync(
            copy, checkpoint, [capture.Seed], CancellationToken.None));
    }

    [TestMethod]
    public async Task AReuseOfADatedPopulationKeepsItsTimeAndAnUndatedOneIsRenewedAsTheCatalogItWas()
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        using (var handler = RightsOnlyHandler())
        {
            var renewed = await Acquisition(copy, handler).ReuseAsync(capture.Result.CheckpointRef!, [capture.Seed],
                capture.Renderers.DocumentFetch.CopyBytes(), WireRequestBudget.OfWireRequests(10), CancellationToken.None);
            Assert.IsTrue(renewed.Delivered, renewed.Detail);
            Assert.AreNotEqual(capture.Result.CheckpointRef, renewed.CheckpointRef, "a renewal holds a new catalog");
            Assert.AreEqual(capture.Result.CatalogedAt, renewed.CatalogedAt, "a renewal fetches only the rights notice: the time is the population's");
            var renewedRoot = await AcquisitionRootAsync(copy, renewed.CheckpointRef!);
            Assert.AreEqual(DatedCatalogSchema, renewedRoot["schema"]!.GetValue<string>());
            var reopened = await EuFirstMountAcquisition.ReopenAsync(copy, renewed.CheckpointRef!, [capture.Seed], CancellationToken.None);
            Assert.AreEqual(capture.Result.CatalogedAt, reopened.CatalogedAt);
        }

        // A catalog written before catalogs were dated: the same population as /1, which states no time.
        var undatedRoot = (JsonObject)await AcquisitionRootAsync(copy, capture.Result.CheckpointRef!);
        undatedRoot.Remove("cataloged_at");
        undatedRoot["schema"] = UndatedCatalogSchema;
        var undated = await HoldAcquisitionRootAsync(copy, undatedRoot);
        var undatedReopened = await EuFirstMountAcquisition.ReopenAsync(copy, undated, [capture.Seed], CancellationToken.None);
        Assert.IsTrue(undatedReopened.Delivered, undatedReopened.Detail);
        Assert.IsNull(undatedReopened.CatalogedAt, "an undated catalog states no completion");
        Assert.IsNull(undatedReopened.Resumption);
        using (var handler = RightsOnlyHandler())
        {
            var renewed = await Acquisition(copy, handler).ReuseAsync(undated, [capture.Seed],
                capture.Renderers.DocumentFetch.CopyBytes(), WireRequestBudget.OfWireRequests(10), CancellationToken.None);
            Assert.IsTrue(renewed.Delivered, renewed.Detail);
            Assert.IsNull(renewed.CatalogedAt, "a renewal never dates what its population did not state");
            var renewedRoot = await AcquisitionRootAsync(copy, renewed.CheckpointRef!);
            Assert.AreEqual(UndatedCatalogSchema, renewedRoot["schema"]!.GetValue<string>(), "an undated population is renewed as the /1 it was");
            Assert.IsNull(renewedRoot["cataloged_at"]);
        }
    }

    /// <summary>
    /// A catalog's JSON as its writer writes it: the writer leaves an instant's <c>+</c> as it is, where a node written back
    /// escapes it, so a tampered catalog differs from its canonical form only by the change the test makes.
    /// </summary>
    internal static string CanonicalJson(JsonNode node) =>
        node.ToJsonString().Replace("\\u002B", "+", StringComparison.Ordinal);
}
