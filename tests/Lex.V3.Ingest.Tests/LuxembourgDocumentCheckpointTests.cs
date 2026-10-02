using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgDocumentGetTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(0, true)]
    public async Task SelectedDocumentCheckpointReplaysInTwoStoresWithOriginalRoutes(int shape, bool weaker)
    {
        var capture = await CaptureDocumentsAsync(shape);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var copy = await CopyDocumentsAsync(capture.Store, weaker: weaker);
            var digests = copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
            var sends = capture.Handler.SendCount;
            var result = await ReopenDocumentsAsync(copy, capture);
            Assert.IsNull(result.Refusal);
            Assert.AreEqual(shape == 4 ? 0 : 1, result.Outcomes!.Count);
            Assert.AreEqual(shape == 0 ? 1 : 0, result.HeldEvidenceByOrdinal!.Count);
            if (shape == 0)
            {
                CollectionAssert.AreEqual(capture.Route!.CopyCanonicalBytes(), result.HeldEvidenceByOrdinal[0].CopyCanonicalBytes());
                Assert.AreEqual(weaker ? CustodyProtection.NotEnforced : CustodyProtection.LockedTime,
                    result.Outcomes[0].Receipt!.PolicyEvidence.Protection);
            }
            else if (shape != 4) Assert.IsNotNull(result.Outcomes[0].Refusal);
            CollectionAssert.AreEqual(digests, copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
            Assert.AreEqual(sends, capture.Handler.SendCount);
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("route")]
    [DataRow("body")]
    [DataRow("request")]
    public async Task SelectedDocumentsRequireOriginalTransportEvidence(string missing)
    {
        var capture = await CaptureDocumentsAsync(0);
        var root = await DocumentRootAsync(capture);
        var digest = missing switch
        {
            "root" => capture.Checkpoint.Sha256,
            "route" => root["fetches"]![0]!["route"]!["sha256"]!.GetValue<string>(),
            "body" => capture.Route!.Hops[0].Sha256,
            _ => capture.Route!.Hops[0].LogicalRequestSha256,
        };
        var copy = await CopyDocumentsAsync(capture.Store, omit: digest);
        try { _ = await ReopenDocumentsAsync(copy, capture); Assert.Fail("Missing dependency must refuse."); }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("input")]
    [DataRow("result")]
    [DataRow("renderer")]
    [DataRow("ordinal")]
    [DataRow("address")]
    [DataRow("plan")]
    [DataRow("input_id")]
    [DataRow("run")]
    [DataRow("missing_fetch")]
    [DataRow("extra_fetch")]
    public async Task SelectedDocumentsRejectRehashedAssociations(string changed)
    {
        var capture = await CaptureDocumentsAsync(0);
        var root = await DocumentRootAsync(capture);
        var fetch = root["fetches"]![0]!;
        switch (changed)
        {
            case "schema": root["schema"] = "foreign/1"; break;
            case "input": root["input_sha256"] = new string('a', 64); break;
            case "result": root["result_sha256"] = new string('a', 64); break;
            case "renderer": root["renderer"]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000055"; break;
            case "ordinal": fetch["ordinal"] = 1; break;
            case "address": fetch["address"]!["sha256"] = new string('a', 64); break;
            case "plan": fetch["plan_id"] = "urn:uuid:00000000-0000-4000-8000-000000000055"; break;
            case "input_id": fetch["input_id"] = "urn:uuid:00000000-0000-4000-8000-000000000055"; break;
            case "run": fetch["run"]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000055"; break;
            case "missing_fetch": root["fetches"]!.AsArray().Clear(); break;
            default: root["fetches"]!.AsArray().Add(fetch.DeepClone()); break;
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        await capture.Store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenDocumentsAsync(capture.Store,
            capture with { Checkpoint = new SourceArtifactRef(capture.Checkpoint.ResourceId, CustodyDigest.Of(bytes)) }));
    }

    [TestMethod]
    public async Task SelectedDocumentsRejectDifferentSelectionBeforeWrites()
    {
        var capture = await CaptureDocumentsAsync(0);
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenDocumentsAsync(capture.Store,
            capture with { Manifest = BuildManifest(Address(), ScopeDisposition.TypedQuarantine).Manifest }));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task SelectedDocumentsRejectNullRootBeforeWrites()
    {
        var capture = await CaptureDocumentsAsync(0);
        var receipt = await capture.Store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenDocumentsAsync(capture.Store,
            capture with { Checkpoint = new SourceArtifactRef(capture.Checkpoint.ResourceId, receipt.Reference.ContentSha256) }));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task SelectedDocumentsCheckpointHoldFailureIsTyped()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: "lex-lu-selected-documents-checkpoint/1");
        var address = Address();
        var handler = new RobotsThenDocumentHandler((request, _) => BinaryResponse(request, HttpStatusCode.OK, "<akomaNtoso/>"u8.ToArray()));
        var adapter = new LuxembourgQueryExecutionAdapter(store,
            new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler), BuildProfile());
        var result = await adapter.RunDocumentAcquisitionWithCheckpointAsync(BuildManifest(address, ScopeDisposition.AcceptedSelected).Manifest,
            new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> { [ObjectRef()] = address },
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3201), LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(result.Checkpoint);
        Assert.IsNull(result.Data.Outcomes);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.DocumentCheckpointNotRetained, result.Data.Refusal!.Code);
    }

    [TestMethod]
    public async Task SelectedDocumentsCancellationCreatesNoWritesOrTraffic()
    {
        var capture = await CaptureDocumentsAsync(0);
        var writes = capture.Store.CreateCallCount;
        var sends = capture.Handler.SendCount;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgQueryExecutionAdapter.ReopenDocumentAcquisitionAsync(
            capture.Store, capture.Checkpoint, BuildProfile(), capture.Manifest, capture.Addresses, capture.Renderer, source.Token));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
        Assert.AreEqual(sends, capture.Handler.SendCount);
    }

    private sealed record DocumentCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        SourceArtifactRef Checkpoint, ScopeManifest Manifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> Addresses,
        MachineQueryRendererSource Renderer, RobotsThenDocumentHandler Handler, RoutedHttpEvidence? Route);

    private static async Task<DocumentCapture> CaptureDocumentsAsync(int shape)
    {
        var address = Address();
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new RobotsThenDocumentHandler((request, _) => BinaryResponse(request,
            shape == 1 ? HttpStatusCode.NotFound : shape == 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            "<akomaNtoso/>"u8.ToArray()))
        {
            RobotsText = shape == 3 ? "User-agent: *\nDisallow: /filestore/\n" : LuxembourgDocumentFetchRobotsBootstrapTests.RealRobotsTxt,
        };
        var renderer = LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(3201);
        var manifest = BuildManifest(address, shape == 4 ? ScopeDisposition.TypedQuarantine : ScopeDisposition.AcceptedSelected).Manifest;
        var addresses = new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> { [ObjectRef()] = address };
        var adapter = new LuxembourgQueryExecutionAdapter(store,
            new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler), BuildProfile());
        var result = await adapter.RunDocumentAcquisitionWithCheckpointAsync(manifest, addresses, renderer,
            LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(result.Data.Refusal, result.Data.Refusal?.Detail);
        Assert.IsNotNull(result.Checkpoint);
        return new(store, result.Checkpoint, manifest, addresses, renderer, handler, result.Data.HeldEvidenceByOrdinal!.GetValueOrDefault(0));
    }
    private static Task<(IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? Outcomes,
        IReadOnlyDictionary<int, RoutedHttpEvidence>? HeldEvidenceByOrdinal, LuxembourgQueryExecutionRefusalDetail? Refusal)>
        ReopenDocumentsAsync(ICustodyStore store, DocumentCapture capture) =>
        LuxembourgQueryExecutionAdapter.ReopenDocumentAcquisitionAsync(store, capture.Checkpoint,
            BuildProfile(), capture.Manifest, capture.Addresses, capture.Renderer, CancellationToken.None);
    private static async Task<JsonNode> DocumentRootAsync(DocumentCapture capture) =>
        JsonNode.Parse((await capture.Store.ReadByDigestAsync(capture.Checkpoint.Sha256, CancellationToken.None)).Span)!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyDocumentsAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(value => value != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
}
