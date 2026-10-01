using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgFirstMountAcquisitionTests
{
    [TestMethod]
    public async Task CompleteLuAcquisitionReopensInTwoIndependentCustodyCopies()
    {
        var capture = await CapturedVocabulary.Value;
        Assert.IsNotNull(capture.Result.CheckpointRef);
        foreach (var copy in new[] { await CopyVocabularyStoreAsync(capture.Store), await CopyVocabularyStoreAsync(capture.Store) })
        {
            var result = await LuxembourgFirstMountAcquisition.ReopenAsync(copy, capture.Result.CheckpointRef, ActRange, CancellationToken.None);
            Assert.IsTrue(result.Delivered, result.Detail);
            Assert.AreEqual(capture.Result.CheckpointRef, result.CheckpointRef);
            Assert.AreEqual(capture.Result.VocabularyEvidenceRef, result.VocabularyEvidenceRef);
            Assert.AreEqual(capture.Result.Run!.CorpusRecordSetRef, result.Run!.CorpusRecordSetRef);
            Assert.AreEqual(capture.Result.Run.ObservedObjectIdentitySetRef, result.Run.ObservedObjectIdentitySetRef);
            Assert.AreEqual(capture.Result.AknInventory!.IdentitySha256, result.AknInventory!.IdentitySha256);
            Assert.AreEqual(capture.Result.AknLegalContent!.IdentitySha256, result.AknLegalContent!.IdentitySha256);
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("vocabulary")]
    [DataRow("query")]
    [DataRow("query_renderer")]
    [DataRow("document_renderer")]
    public async Task CompleteLuReplayRequiresEveryOriginalPhase(string missing)
    {
        var capture = await CapturedVocabulary.Value;
        var root = await VocabularyRootAsync(capture.Store, capture.Result.CheckpointRef!);
        var digest = missing == "root" ? capture.Result.CheckpointRef!.Sha256 : root[missing]!["sha256"]!.GetValue<string>();
        var copy = await CopyVocabularyStoreAsync(capture.Store, omit: digest);
        try
        {
            _ = await LuxembourgFirstMountAcquisition.ReopenAsync(copy, capture.Result.CheckpointRef!, ActRange, CancellationToken.None);
            Assert.Fail("Missing original acquisition must refuse.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("act")]
    [DataRow("observation")]
    [DataRow("corpus")]
    [DataRow("observed")]
    [DataRow("inventory_sha256")]
    [DataRow("content_sha256")]
    [DataRow("null_query")]
    public async Task RehashedCompleteLuCatalogCannotChangeAcquisitionBindings(string changed)
    {
        var capture = await CapturedVocabulary.Value;
        var copy = await CopyVocabularyStoreAsync(capture.Store);
        var root = await VocabularyRootAsync(copy, capture.Result.CheckpointRef!);
        switch (changed)
        {
            case "schema": root[changed] = "lex-lu-first-mount-acquisition/99"; break;
            case "act": root[changed]!["name"] = "different-act"; break;
            case "null_query": root["query"] = null; break;
            case "inventory_sha256": case "content_sha256": root[changed] = new string('a', 64); break;
            default: root[changed]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000088"; break;
        }
        var held = await copy.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgFirstMountAcquisition.ReopenAsync(copy,
            new SourceArtifactRef(capture.Result.CheckpointRef!.ResourceId, held.Reference.ContentSha256), ActRange, CancellationToken.None));
    }

    [TestMethod]
    public async Task CompleteLuReplayRejectsWeakerCurrentBodyMembership()
    {
        var capture = await CapturedVocabulary.Value;
        var copy = await CopyVocabularyStoreAsync(capture.Store, weaker: true);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgFirstMountAcquisition.ReopenAsync(copy,
            capture.Result.CheckpointRef!, ActRange, CancellationToken.None));
    }

    [TestMethod]
    public async Task CompleteLuReplayReprovesAdaptiveSplitFamiliesAndTwoWorkDerivation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true, includeBlankNode: true, saturatedRoot: true);
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var captured = await Acquisition(store, handler).RunAsync(LuxembourgActRange.WholePopulation, renderers,
            LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(captured.Delivered, captured.Detail);
        Assert.IsTrue(captured.Run!.FamilyOutcomes.All(outcome => outcome.CoverLeafProofs!.Count > 1));
        var copy = await CopyVocabularyStoreAsync(store);
        var reopened = await LuxembourgFirstMountAcquisition.ReopenAsync(copy, captured.CheckpointRef!,
            LuxembourgActRange.WholePopulation, CancellationToken.None);
        Assert.AreEqual(captured.Run.CorpusRecordSetRef, reopened.Run!.CorpusRecordSetRef);
        Assert.AreEqual(captured.AknInventory!.IdentitySha256, reopened.AknInventory!.IdentitySha256);
        Assert.AreEqual(captured.AknLegalContent!.IdentitySha256, reopened.AknLegalContent!.IdentitySha256);
    }

    [TestMethod]
    public async Task CompleteLuReplayCancellationDoesNotWrite()
    {
        var capture = await CapturedVocabulary.Value;
        var copy = await CopyVocabularyStoreAsync(capture.Store);
        var writes = copy.WrittenDigestsInOrder.Count;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgFirstMountAcquisition.ReopenAsync(copy,
            capture.Result.CheckpointRef!, ActRange, cancellation.Token));
        Assert.AreEqual(writes, copy.WrittenDigestsInOrder.Count);
    }

    [TestMethod]
    public async Task CompleteLuCatalogHoldFailurePreservesPhaseEvidenceAndRefuses()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: "lex-lu-first-mount-acquisition/1");
        var handler = new LuxembourgFamilyHandler(PdfBytes());
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var result = await Acquisition(store, handler).RunAsync(ActRange, renderers,
            LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.AreEqual(LuxembourgFirstMountAcquisitionRefusal.AcquisitionCheckpointNotRetained, result.Refusal);
        Assert.IsNull(result.CheckpointRef);
        Assert.IsNotNull(result.VocabularyCheckpointRef);
        Assert.IsNotNull(result.Run!.AcquisitionCheckpointRef);
    }
}
