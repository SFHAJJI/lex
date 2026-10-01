using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuFirstMountAcquisitionTests
{
    private static readonly Lazy<Task<AcquisitionCapture>> RetainedAcquisition = new(() => CaptureAcquisitionAsync());

    [TestMethod]
    public async Task CompleteEuAcquisitionRestoresTheSameRunAssociationInTwoIndependentStores()
    {
        var capture = await RetainedAcquisition.Value;
        for (var index = 0; index < 2; index++)
        {
            var copy = await CopyAcquisitionStoreAsync(capture.Store);
            var digests = copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
            var result = await EuFirstMountAcquisition.ReopenAsync(copy, capture.Result.CheckpointRef!, [capture.Seed], CancellationToken.None);
            Assert.IsTrue(result.Delivered, result.Detail);
            Assert.AreEqual(capture.Result.CheckpointRef, result.CheckpointRef);
            Assert.AreSame(result.Run, result.Formex!.Reconciliation!.Run);
            Assert.AreEqual(capture.Result.Run!.CorpusRecordSetRef, result.Run!.CorpusRecordSetRef);
            Assert.AreEqual(capture.Result.Run.ScopeManifestCanonicalSha256, result.Run.ScopeManifestCanonicalSha256);
            Assert.AreEqual(capture.Result.Formex!.AcquiredExpressionCount, result.Formex.AcquiredExpressionCount);
            Assert.AreEqual(result.Run.CorpusRecordSet!.Set.Records[0].RunIdentity, result.LegalNotice!.Route!.RunIdentity);
            CollectionAssert.AreEqual(capture.Result.LegalNotice!.Route!.CopyCanonicalBytes(), result.LegalNotice.Route.CopyCanonicalBytes());
            CollectionAssert.AreEqual(digests, copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("query")]
    [DataRow("formex")]
    [DataRow("rights")]
    [DataRow("renderer")]
    public async Task CompleteEuAcquisitionRequiresItsRetainedComponents(string missing)
    {
        var capture = await RetainedAcquisition.Value;
        var root = await AcquisitionRootAsync(capture.Store, capture.Result.CheckpointRef!);
        var digest = missing switch
        {
            "root" => capture.Result.CheckpointRef!.Sha256,
            "renderer" => root["renderers"]![5]!["reference"]!["sha256"]!.GetValue<string>(),
            _ => root[missing]!["sha256"]!.GetValue<string>(),
        };
        var copy = await CopyAcquisitionStoreAsync(capture.Store, omit: digest);
        try
        {
            await EuFirstMountAcquisition.ReopenAsync(copy, capture.Result.CheckpointRef!, [capture.Seed], CancellationToken.None);
            Assert.Fail("Missing acquisition evidence must refuse.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("seeds")]
    [DataRow("duplicate_seed")]
    [DataRow("corpus_run")]
    [DataRow("null_query")]
    [DataRow("null_renderers")]
    [DataRow("renderer_role")]
    [DataRow("renderer_identity")]
    [DataRow("formex_renderer")]
    public async Task RehashedAcquisitionCatalogCannotChangeComponentBindings(string changed)
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        var root = await AcquisitionRootAsync(copy, capture.Result.CheckpointRef!);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-eu-first-mount-acquisition/99"; break;
            case "seeds": root["seeds"]![0] = "foreign"; break;
            case "duplicate_seed": root["seeds"]!.AsArray().Add(capture.Seed); break;
            case "corpus_run": root["corpus_run"]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000999"; break;
            case "null_query": root["query"] = null; break;
            case "null_renderers": root["renderers"] = null; break;
            case "renderer_role": root["renderers"]![0]!["file"] = "foreign"; break;
            case "renderer_identity": root["renderers"]![0]!["reference"]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000999"; break;
            case "formex_renderer": root["renderers"]![4]!["reference"] = root["renderers"]![0]!["reference"]!.DeepClone(); break;
        }
        var checkpoint = await HoldAcquisitionRootAsync(copy, root);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFirstMountAcquisition.ReopenAsync(
            copy, checkpoint, [capture.Seed], CancellationToken.None));
    }

    [TestMethod]
    [DataRow("query")]
    [DataRow("formex")]
    [DataRow("rights")]
    public async Task CompleteEuAcquisitionRejectsValidComponentsFromAnotherRun(string component)
    {
        var capture = await RetainedAcquisition.Value;
        var foreign = await CaptureAcquisitionAsync(suppliedRenderers: capture.Renderers);
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        foreach (var digest in foreign.Store.WrittenDigestsInOrder.Distinct())
            await copy.CreateAsync(await foreign.Store.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var root = await AcquisitionRootAsync(copy, capture.Result.CheckpointRef!);
        var other = await AcquisitionRootAsync(copy, foreign.Result.CheckpointRef!);
        root[component] = other[component]!.DeepClone();
        var checkpoint = await HoldAcquisitionRootAsync(copy, root);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFirstMountAcquisition.ReopenAsync(
            copy, checkpoint, [capture.Seed], CancellationToken.None));
    }

    [TestMethod]
    public async Task NullAcquisitionCatalogRefusesBeforeWrites()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var checkpoint = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
        var writes = store.WrittenDigestsInOrder.Count;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFirstMountAcquisition.ReopenAsync(store, checkpoint, ["32016R0679"], CancellationToken.None));
        Assert.AreEqual(writes, store.WrittenDigestsInOrder.Count);
    }

    [TestMethod]
    public async Task CompleteEuAcquisitionChecksCallerScopeBeforeReplayWrites()
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        var writes = copy.WrittenDigestsInOrder.Count;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFirstMountAcquisition.ReopenAsync(copy,
            capture.Result.CheckpointRef!, [], CancellationToken.None));
        Assert.AreEqual(writes, copy.WrittenDigestsInOrder.Count);
    }

    [TestMethod]
    public async Task CompleteEuAcquisitionHonoursCancellationBeforeWrites()
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store);
        var writes = copy.WrittenDigestsInOrder.Count;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuFirstMountAcquisition.ReopenAsync(copy,
            capture.Result.CheckpointRef!, [capture.Seed], cancellation.Token));
        Assert.AreEqual(writes, copy.WrittenDigestsInOrder.Count);
    }

    [TestMethod]
    public async Task WeakerCurrentCustodyCannotClaimOriginalAcquisitionBytes()
    {
        var capture = await RetainedAcquisition.Value;
        var copy = await CopyAcquisitionStoreAsync(capture.Store, weaker: true);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFirstMountAcquisition.ReopenAsync(copy,
            capture.Result.CheckpointRef!, [capture.Seed], CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedAcquisitionCatalogHoldPreventsDelivery(bool source)
    {
        var capture = await CaptureAcquisitionAsync(failRoot: !source, failLegacySource: source);
        Assert.AreEqual(EuFirstMountAcquisitionRefusal.AcquisitionCheckpointNotRetained, capture.Result.Refusal);
        Assert.IsNull(capture.Result.CheckpointRef);
        Assert.IsNotNull(capture.Result.Run);
        Assert.IsTrue(capture.Result.Formex!.Delivered);
    }

    private static async Task<AcquisitionCapture> CaptureAcquisitionAsync(bool failRoot = false, bool failLegacySource = false, EuRendererSources? suppliedRenderers = null)
    {
        var root = EuAxiomWiringHarness.SeedRoot(null);
        var seed = EuAxiomWiringHarness.Seed(null).Celex;
        var expression = root + ".0001";
        var legacyDigest = Sha256(File.ReadAllBytes(Path.Combine(CheckoutRoot(), EuRendererSources.RendererFiles[5])));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
            failWriteDigest: (digest, ordinal) => failLegacySource && digest == legacyDigest && ordinal > 1,
            failSchema: failRoot ? "lex-eu-first-mount-acquisition/1" : null);
        var handler = new CompositeHandler(EuAxiomWiringHarness.Scripts(root,
            static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot), expressionIri: expression),
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [expression] = ["fmx4", "xhtml"] });
        var renderers = suppliedRenderers ?? await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        if (suppliedRenderers is not null)
            foreach (var file in EuRendererSources.RendererFiles)
                await store.CreateAsync(await File.ReadAllBytesAsync(Path.Combine(CheckoutRoot(), file)),
                    CustodyClass.NightlyFloor90d, CancellationToken.None);
        var result = await Acquisition(store, handler).RunAsync(seed, renderers,
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (!failRoot && !failLegacySource)
        {
            Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
            Assert.IsNotNull(result.CheckpointRef);
        }
        return new(store, result, handler, seed, renderers);
    }

    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAcquisitionStoreAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore original, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in original.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await original.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private static async Task<JsonNode> AcquisitionRootAsync(ICustodyStore store, SourceArtifactRef checkpoint) =>
        JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(checkpoint.Sha256, CancellationToken.None)).Span))!;

    private static async Task<SourceArtifactRef> HoldAcquisitionRootAsync(ICustodyStore store, JsonNode root)
    {
        var receipt = await store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }

    private sealed record AcquisitionCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        EuFirstMountAcquisitionResult Result, CompositeHandler Handler, string Seed, EuRendererSources Renderers);
}
