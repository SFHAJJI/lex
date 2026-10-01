using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgFirstMountAcquisitionTests
{
    private static readonly Lazy<Task<VocabularyCapture>> CapturedVocabulary = new(() => CaptureVocabularyAsync());

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VocabularyReplayReprovesTheFourFamiliesInCopiedCustody(bool weaker)
    {
        var capture = await CapturedVocabulary.Value;
        var copy = await CopyVocabularyStoreAsync(capture.Store, weaker: weaker);
        var digests = copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
        var sends = capture.Handler.FamiliesSeen.Count + capture.Handler.DocumentRequests.Count;
        var first = await RestoreVocabularyAsync(copy, capture);
        var second = await RestoreVocabularyAsync(copy, capture);
        Assert.AreEqual(capture.Result.VocabularyEvidenceRef, first.Snapshot.ObservationRef);
        Assert.AreEqual(first.Snapshot.ObservationRef, second.Snapshot.ObservationRef);
        CollectionAssert.AreEquivalent(capture.Result.Profile!.ObservedIriVocabulary.ToArray(), first.ObservedIriVocabulary.ToArray());
        CollectionAssert.AreEquivalent(first.ObservedIriVocabulary.ToArray(), second.ObservedIriVocabulary.ToArray());
        CollectionAssert.AreEqual(digests, copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(sends, capture.Handler.FamiliesSeen.Count + capture.Handler.DocumentRequests.Count);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("observation")]
    [DataRow("plan")]
    [DataRow("P")]
    [DataRow("T")]
    [DataRow("C")]
    [DataRow("O")]
    public async Task VocabularyReplayCannotReplaceMissingPublisherEvidence(string missing)
    {
        var capture = await CapturedVocabulary.Value;
        var root = await VocabularyRootAsync(capture.Store, capture.Result.VocabularyCheckpointRef!);
        var digest = missing switch
        {
            "root" => capture.Result.VocabularyCheckpointRef!.Sha256,
            "observation" => capture.Result.VocabularyEvidenceRef!.Sha256,
            "plan" => root["plan_wire_sha256"]!.GetValue<string>(),
            _ => root["families"]!.AsArray().Single(value => value!["family"]!.GetValue<string>() == missing)!["checkpoint"]!["sha256"]!.GetValue<string>(),
        };
        var copy = await CopyVocabularyStoreAsync(capture.Store, omit: digest);
        try { _ = await RestoreVocabularyAsync(copy, capture); Assert.Fail("Missing original evidence must refuse."); }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("null_families")]
    [DataRow("missing_family")]
    [DataRow("extra_family")]
    [DataRow("reordered")]
    [DataRow("family")]
    [DataRow("run")]
    [DataRow("profile")]
    [DataRow("plan")]
    [DataRow("count")]
    public async Task RehashedVocabularyRootsMustStillReproduceOriginalObservation(string changed)
    {
        var capture = await CapturedVocabulary.Value;
        var copy = await CopyVocabularyStoreAsync(capture.Store);
        var root = await VocabularyRootAsync(copy, capture.Result.VocabularyCheckpointRef!);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-lu-vocabulary-checkpoint/99"; break;
            case "null_families": root["families"] = null; break;
            case "missing_family": root["families"]!.AsArray().RemoveAt(0); break;
            case "extra_family": root["families"]!.AsArray().Add(root["families"]![0]!.DeepClone()); break;
            case "reordered":
                var families = root["families"]!.AsArray();
                var first = families[0]!.DeepClone(); families[0] = families[1]!.DeepClone(); families[1] = first; break;
            case "family": root["families"]![0]!["family"] = "S"; break;
            case "run": root["families"]![0]!["run"]!["sha256"] = new string('a', 64); break;
            case "profile": root["families"]![0]!["profile"]!["sha256"] = new string('a', 64); break;
            case "plan": root["plan"]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000088"; break;
            case "count": root["families"]![0]!["product_request_count"] = 999; break;
        }
        var held = await copy.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgFirstMountAcquisition.ReopenVocabularyAsync(
            copy, new SourceArtifactRef(capture.Result.VocabularyCheckpointRef!.ResourceId, held.Reference.ContentSha256),
            capture.Result.VocabularyEvidenceRef!, capture.Renderers.Query, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VocabularyReplayRequiresOriginalObservationAndRenderer(bool renderer)
    {
        var capture = await CapturedVocabulary.Value;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgFirstMountAcquisition.ReopenVocabularyAsync(
            capture.Store, capture.Result.VocabularyCheckpointRef!, renderer ? capture.Result.VocabularyEvidenceRef! :
                new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000087", capture.Result.VocabularyEvidenceRef!.Sha256),
            renderer ? EuAcquisitionTestFixture.BuildRendererSource(983) : capture.Renderers.Query, CancellationToken.None));
    }

    [TestMethod]
    public async Task VocabularyReplayCancellationPropagates()
    {
        var capture = await CapturedVocabulary.Value;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgFirstMountAcquisition.ReopenVocabularyAsync(
            capture.Store, capture.Result.VocabularyCheckpointRef!, capture.Result.VocabularyEvidenceRef!, capture.Renderers.Query, source.Token));
    }

    [TestMethod]
    public async Task VocabularyCheckpointHoldFailureStopsBeforePopulationTraffic()
    {
        var capture = await CaptureVocabularyAsync(failCheckpoint: true);
        Assert.AreEqual(LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused, capture.Result.Refusal);
        Assert.IsNull(capture.Result.VocabularyCheckpointRef);
        StringAssert.Contains(capture.Result.Detail, "checkpoint not retained");
        CollectionAssert.AreEquivalent(new[] { "P", "T", "C", "O" }, capture.Handler.FamiliesSeen.ToArray());
        Assert.HasCount(0, capture.Handler.DocumentRequests);
    }

    private sealed record VocabularyCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgFirstMountAcquisitionResult Result, LuxembourgRendererSources Renderers, LuxembourgFamilyHandler Handler);
    private static Task<VerifiedLuxembourgSourceProfile> RestoreVocabularyAsync(ICustodyStore store, VocabularyCapture capture) =>
        LuxembourgFirstMountAcquisition.ReopenVocabularyAsync(store, capture.Result.VocabularyCheckpointRef!,
            capture.Result.VocabularyEvidenceRef!, capture.Renderers.Query, CancellationToken.None);
    private static async Task<JsonNode> VocabularyRootAsync(ICustodyStore store, SourceArtifactRef reference) =>
        JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(reference.Sha256, CancellationToken.None)).Span))!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyVocabularyStoreAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
    private static async Task<VocabularyCapture> CaptureVocabularyAsync(bool failCheckpoint = false)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: failCheckpoint ? "lex-lu-vocabulary-checkpoint/1" : null);
        var handler = new LuxembourgFamilyHandler(PdfBytes());
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var result = await Acquisition(store, handler).RunAsync(ActRange, renderers,
            LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (!failCheckpoint) { Assert.IsTrue(result.Delivered, result.Detail); Assert.IsNotNull(result.VocabularyCheckpointRef); }
        return new(store, result, renderers, handler);
    }
}
