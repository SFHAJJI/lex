using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgGazetteAcquisitionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteLuQueryReplaysOriginalManifestAndCorpusIdentitiesTwice(bool refusedListing)
    {
        var capture = await CaptureQueryAsync(refusedListing);
        foreach (var index in Enumerable.Range(0, 2))
        {
            var store = await CopyGazetteAsync(capture.Store);
            var digests = store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
            var result = await ReopenQueryAsync(store, capture);
            AssertQueryIdentity(capture.Run.Result, result);
            CollectionAssert.AreEqual(digests, store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("family")]
    [DataRow("documents")]
    [DataRow("gazette")]
    public async Task CompleteLuQueryRequiresRetainedAcquisitionDependencies(string missing)
    {
        var capture = await CaptureQueryAsync();
        var root = await QueryRootAsync(capture);
        var digest = missing switch
        {
            "root" => capture.Checkpoint.Sha256,
            "family" => root["families"]![0]!["checkpoint"]!["sha256"]!.GetValue<string>(),
            _ => root[missing]!["sha256"]!.GetValue<string>(),
        };
        var store = await CopyGazetteAsync(capture.Store, digest);
        try
        {
            await ReopenQueryAsync(store, capture);
            Assert.Fail("Missing acquisition evidence must refuse.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("profile")]
    [DataRow("range")]
    [DataRow("set")]
    [DataRow("renderer")]
    [DataRow("run_id")]
    [DataRow("run_digest")]
    [DataRow("selection_manifest")]
    [DataRow("selection_storage")]
    [DataRow("final_manifest")]
    [DataRow("final_storage")]
    [DataRow("corpus")]
    [DataRow("observed")]
    [DataRow("result")]
    public async Task RehashedLuQueryCatalogCannotSubstituteOriginalAssociations(string changed)
    {
        var capture = await CaptureQueryAsync();
        var root = await QueryRootAsync(capture);
        switch (changed)
        {
            case "schema": root["schema"] = "foreign/1"; break;
            case "profile": root["profile_sha256"] = new string('a', 64); break;
            case "range": root["families"]![0]!["range"]!["partition_id"] = "foreign"; break;
            case "set": root["families"]![0]!["set"] = "A"; break;
            case "renderer": root["families"]![0]!["renderer"]!["resource_id"] = NewUrn(); break;
            case "run_id": root["run"]!["resource_id"] = NewUrn(); break;
            case "run_digest": root["run"]!["sha256"] = new string('a', 64); break;
            case "selection_manifest": root["selection_manifest"]!["resource_id"] = NewUrn(); break;
            case "selection_storage": root["selection_content_sha256"] = new string('a', 64); break;
            case "final_manifest": root["final_manifest"]!["resource_id"] = NewUrn(); break;
            case "final_storage": root["final_content_sha256"] = new string('a', 64); break;
            case "corpus": root["corpus"]!["resource_id"] = NewUrn(); break;
            case "observed": root["observed"]!["resource_id"] = NewUrn(); break;
            default: root["result_sha256"] = new string('a', 64); break;
        }
        var receipt = await capture.Store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        try
        {
            await ReopenQueryAsync(capture.Store, capture with { Checkpoint = new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256) });
            Assert.Fail("Rehashed acquisition association must refuse.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    public async Task LuQueryRejectsOtherCallerRangesBeforeWrites()
    {
        var capture = await CaptureQueryAsync();
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgQueryExecutionAdapter.ReopenAcquisitionAsync(
            capture.Store, capture.Checkpoint, capture.Run.Profile, [], CancellationToken.None));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task LuQueryRejectsNullCatalogBeforeWrites()
    {
        var capture = await CaptureQueryAsync();
        var receipt = await capture.Store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenQueryAsync(capture.Store,
            capture with { Checkpoint = new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256) }));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task LuQueryWeakerCurrentCustodyCannotReproduceOriginalCorpusIdentity()
    {
        var capture = await CaptureQueryAsync();
        var store = await CopyGazetteAsync(capture.Store, weaker: true);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenQueryAsync(store, capture));
    }

    [TestMethod]
    public async Task LuQueryCancellationProducesNoWrites()
    {
        var capture = await CaptureQueryAsync();
        using var source = new CancellationTokenSource(); source.Cancel();
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgQueryExecutionAdapter.ReopenAcquisitionAsync(
            capture.Store, capture.Checkpoint, capture.Run.Profile, capture.Run.Ranges, source.Token));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task LuQueryCatalogHoldFailureIsTyped()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: "lex-lu-query-acquisition-checkpoint/1");
        var run = await RunAsync(GazetteAssertions(), (HttpStatusCode.OK, PdfBytes), custodyStore: store);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.AcquisitionCheckpointNotRetained, run.Result.Refusal!.Code);
        Assert.IsNull(run.Result.AcquisitionCheckpointRef);
    }

    [TestMethod]
    public async Task SegmentedLuManifestsKeepOriginalStorageRootsAcrossNewReceiptObservations()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var subjects = new[] { Act, Expression, ManifestationPdfA, ManifestationPdf }.Concat(
            Enumerable.Range(0, 600).Select(index => Parent + "/unclassified/" + index.ToString("D4") + new string('x', 1800))).ToArray();
        var run = await RunAsync(GazetteAssertions(), (HttpStatusCode.OK, PdfBytes), subjects: subjects,
            custodyStore: store, decorate: inner => new GazetteCustodyStore(inner) { AdvanceObservationPerCreate = true });
        Assert.IsNull(run.Result.Refusal, run.Result.Refusal?.Detail);
        Assert.IsNotNull(run.Result.AcquisitionCheckpointRef);
        var capture = new QueryCapture(store, run, run.Result.AcquisitionCheckpointRef);
        var root = await QueryRootAsync(capture);
        var storage = await store.ReadByDigestAsync(root["selection_content_sha256"]!.GetValue<string>(), CancellationToken.None);
        StringAssert.Contains(Encoding.UTF8.GetString(storage.Span), ChunkedDerivedArtifact.Schema);
        var copy = await CopyGazetteAsync(store);
        var reopened = await ReopenQueryAsync(new GazetteCustodyStore(copy) { AdvanceObservationPerCreate = true }, capture);
        AssertQueryIdentity(run.Result, reopened);
        Assert.AreEqual(run.Result.ScopeManifestReceipt!.Reference.ContentSha256, reopened.ScopeManifestReceipt!.Reference.ContentSha256);
    }

    private sealed record QueryCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        GazetteRun Run, SourceArtifactRef Checkpoint);
    private static async Task<QueryCapture> CaptureQueryAsync(bool refusedListing = false)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var run = await RunAsync(GazetteAssertions(), (refusedListing ? HttpStatusCode.NotFound : HttpStatusCode.OK, PdfBytes), custodyStore: store);
        Assert.IsNull(run.Result.Refusal, run.Result.Refusal?.Detail);
        Assert.IsNotNull(run.Result.AcquisitionCheckpointRef);
        return new(store, run, run.Result.AcquisitionCheckpointRef);
    }
    private static Task<LuxembourgQueryExecutionResult> ReopenQueryAsync(ICustodyStore store, QueryCapture capture) =>
        LuxembourgQueryExecutionAdapter.ReopenAcquisitionAsync(store, capture.Checkpoint, capture.Run.Profile,
            capture.Run.Ranges, CancellationToken.None);
    private static async Task<JsonNode> QueryRootAsync(QueryCapture capture) =>
        JsonNode.Parse((await capture.Store.ReadByDigestAsync(capture.Checkpoint.Sha256, CancellationToken.None)).Span)!;
    private static void AssertQueryIdentity(LuxembourgQueryExecutionResult original, LuxembourgQueryExecutionResult result)
    {
        Assert.IsNull(result.Refusal, result.Refusal?.Detail);
        Assert.AreEqual(original.ScopeManifestCanonicalSha256, result.ScopeManifestCanonicalSha256);
        Assert.AreEqual(original.CorpusRecordSetRef, result.CorpusRecordSetRef);
        Assert.AreEqual(original.ObservedObjectIdentitySetRef, result.ObservedObjectIdentitySetRef);
        Assert.AreEqual(original.CorpusRecordSet!.Set.Records[0].RunIdentity, result.CorpusRecordSet!.Set.Records[0].RunIdentity);
        Assert.AreEqual(ContractJson.Serialize(original.PopulationLedger), ContractJson.Serialize(result.PopulationLedger));
        Assert.AreEqual(ContractJson.Serialize(original.GazetteBodySetsByOrdinal), ContractJson.Serialize(result.GazetteBodySetsByOrdinal));
    }
}
