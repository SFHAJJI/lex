using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuWitnessCheckpointTests
{
    private const string Boundary = "2024-12-31T20:10:26.804+01:00";

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public async Task CopiedWitnessReplaysEmptyBatchesAndCrossingsTwice(int shape, bool unenforced)
    {
        var (store, result, plans, renderer, handler) = await AcquireAsync(shape);
        var copy = await CopyAsync(store, unenforced: unenforced);
        var writes = copy.CreateCallCount;
        var sends = handler.FamilySequence.Count;
        var first = await RestoreAsync(copy, result, plans, renderer);
        var second = await RestoreAsync(copy, result, plans, renderer);
        Assert.AreEqual(ContractJson.Serialize(result.Entries!.CanonicalEntries), ContractJson.Serialize(first.Entries!.CanonicalEntries));
        Assert.AreEqual(ContractJson.Serialize(first.Entries.CanonicalEntries), ContractJson.Serialize(second.Entries!.CanonicalEntries));
        Assert.AreEqual(result.DeliveryEvidenceSha256, first.DeliveryEvidenceSha256);
        Assert.AreEqual(result.Elapsed, first.Elapsed);
        Assert.AreEqual(result.AcquisitionRunRef, first.AcquisitionRunRef);
        Assert.AreEqual(result.CheckpointRef, first.CheckpointRef);
        Assert.AreEqual(0, first.ProductRequestCount);
        Assert.AreEqual(writes, copy.CreateCallCount, "replay reads original evidence without minting receipts or observations");
        Assert.AreEqual(sends, handler.FamilySequence.Count);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("page")]
    [DataRow("input")]
    [DataRow("request")]
    [DataRow("body")]
    [DataRow("receipt")]
    public async Task MissingOriginalWitnessEvidenceRefuses(string missing)
    {
        var (store, result, plans, renderer, _) = await AcquireAsync(1);
        var root = await RootAsync(store, result);
        var evidence = root["pages"]![0]!["evidence"]!;
        var pageDigest = evidence["http_evidence_ref"]!["sha256"]!.GetValue<string>();
        var routeBytes = await store.ReadByDigestAsync(pageDigest, CancellationToken.None);
        var route = Lex.V3.Contracts.Source.Http.RoutedHttpEvidence.ParseAndVerify(routeBytes.Span);
        var digest = missing switch
        {
            "root" => result.CheckpointRef!.Sha256,
            "page" => pageDigest,
            "input" => evidence["query_input_ref"]!["sha256"]!.GetValue<string>(),
            "request" => evidence["logical_request_ref"]!["sha256"]!.GetValue<string>(),
            "body" => route.Hops[0].Sha256,
            _ => route.Hops[0].DurableWriteReceiptSha256,
        };
        var copy = await CopyAsync(store, omit: digest);
        if (missing == "root") await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => RestoreAsync(copy, result, plans, renderer));
        else await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => RestoreAsync(copy, result, plans, renderer));
    }

    [TestMethod]
    [DataRow("plans")]
    [DataRow("run")]
    [DataRow("renderer")]
    public async Task CallerPinsHaveToNameThisTraversal(string changed)
    {
        var (store, result, plans, renderer, _) = await AcquireAsync(1);
        var other = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('a', 64));
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuRepeatedEnumerationExecutor.RestoreWitnessTraversalAsync(
            store, result.CheckpointRef!, changed == "plans" ? plans.Reverse().ToArray() : plans,
            changed == "run" ? other : result.AcquisitionRunRef!, changed == "renderer" ? other : renderer.Reference, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("missing_terminal")]
    [DataRow("duplicate")]
    [DataRow("reorder")]
    [DataRow("batch")]
    [DataRow("entries")]
    [DataRow("evidence")]
    [DataRow("other_run_page")]
    [DataRow("unconsumed_page")]
    [DataRow("null_pages")]
    public async Task RehashedWitnessHistoryStillMustReproduceTheOriginalTraversal(string changed)
    {
        var (store, result, plans, renderer, _) = await AcquireAsync(1);
        var root = await RootAsync(store, result);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-eu-witness-checkpoint/99"; break;
            case "missing_terminal": root["pages"]!.AsArray().RemoveAt(3); break;
            case "duplicate": root["pages"]![1] = root["pages"]![0]!.DeepClone(); break;
            case "reorder":
                var first = root["pages"]![0]!.DeepClone();
                root["pages"]![0] = root["pages"]![1]!.DeepClone(); root["pages"]![1] = first; break;
            case "batch": root["pages"]![0]!["batch_ordinal"] = 1; break;
            case "entries": root["entries_sha256"] = new string('a', 64); break;
            case "evidence": root["delivery_evidence_sha256"] = new string('a', 64); break;
            case "null_pages": root["pages"] = null; break;
            case "other_run_page":
            case "unconsumed_page":
                var (otherStore, otherResult, _, _, _) = await AcquireAsync(1);
                foreach (var digest in otherStore.WrittenDigestsInOrder.Distinct())
                    await store.CreateAsync(await otherStore.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
                var otherPage = (await RootAsync(otherStore, otherResult))["pages"]![0]!.DeepClone();
                if (changed == "other_run_page") root["pages"]![0] = otherPage;
                else { root["pages"]!.AsArray().Add(otherPage); root["product_request_count"] = 5; }
                break;
        }
        var held = await store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuRepeatedEnumerationExecutor.RestoreWitnessTraversalAsync(
            store, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held.Reference.ContentSha256), plans,
            result.AcquisitionRunRef!, renderer.Reference, CancellationToken.None));
        if (changed == "unconsumed_page") StringAssert.Contains(exception.Message, "unconsumed pages");
    }

    [TestMethod]
    public async Task CancellationDoesNotWriteOrSend()
    {
        var (store, result, plans, renderer, handler) = await AcquireAsync(0);
        var writes = store.CreateCallCount;
        var sends = handler.FamilySequence.Count;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuRepeatedEnumerationExecutor.RestoreWitnessTraversalAsync(
            store, result.CheckpointRef!, plans, result.AcquisitionRunRef!, renderer.Reference, cancellation.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
        Assert.AreEqual(sends, handler.FamilySequence.Count);
    }

    [TestMethod]
    public async Task FailedWitnessCheckpointHoldHasItsOwnRefusal()
    {
        var (_, result, _, _, _) = await AcquireAsync(0, failCheckpoint: true);
        Assert.AreEqual(EuWitnessTraversalRefusal.CheckpointNotRetained, result.Refusal!.Code);
        Assert.IsNull(result.Entries);
        Assert.IsNull(result.CheckpointRef);
        StringAssert.Contains(result.Refusal.Detail!, "hold refused");
    }

    private static Task<EuWitnessTraversalResult> RestoreAsync(ICustodyStore store, EuWitnessTraversalResult original,
        EuWatermarkWitnessPlan[] plans, MachineQueryRendererSource renderer) => EuRepeatedEnumerationExecutor.RestoreWitnessTraversalAsync(
            store, original.CheckpointRef!, plans, original.AcquisitionRunRef!, renderer.Reference, CancellationToken.None);
    private static async Task<JsonNode> RootAsync(ICustodyStore store, EuWitnessTraversalResult result) =>
        JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(result.CheckpointRef!.Sha256, CancellationToken.None)).Span))!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool unenforced = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => unenforced);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, EuWitnessTraversalResult Result,
        EuWatermarkWitnessPlan[] Plans, MachineQueryRendererSource Renderer, EuAcquisitionTestFixture.ClassifyingHandler Handler)> AcquireAsync(
            int shape, bool failCheckpoint = false)
    {
        var roots = EuAppendixASeedMap.SeedsInCelexOrder.Take(2).Select(seed => EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!).ToArray();
        var boundary = EuWatermarkCursor.TryOpen(Boundary, roots[0], out _)!;
        EuWatermarkWitnessPlan Plan(params string[] members) => EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint, EuWatermarkWitnessPlan.WatermarkPredicateIri,
            EuWatermarkWitnessPlan.SortedResultWindowRows, boundary, members, out _)!;
        var plans = shape == 1 ? new[] { Plan(roots[0]), Plan(roots[1]) } : new[] { Plan(roots) };
        var empty = EuAcquisitionTestFixture.WitnessRowsJson([]);
        var boundaryPage = EuAcquisitionTestFixture.WitnessRowsJson([EuAcquisitionTestFixture.WitnessRow(roots[0], Boundary)]);
        const string later = "2025-01-01T20:10:26.804+01:00";
        var crossing = EuAcquisitionTestFixture.WitnessRowsJson([EuAcquisitionTestFixture.WitnessRow(roots[0], Boundary), EuAcquisitionTestFixture.WitnessRow(roots[1], later)]);
        var last = EuAcquisitionTestFixture.WitnessRowsJson([EuAcquisitionTestFixture.WitnessRow(roots[1], later)]);
        string[] pages = shape switch { 0 => [empty, empty], 1 => [boundaryPage, boundaryPage, empty, empty], _ => [crossing, last, last] };
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
            { ["Witness"] = new("Witness", pages) });
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: failCheckpoint ? "lex-eu-witness-checkpoint/1" : null);
        var renderer = EuAcquisitionTestFixture.BuildRendererSource(8301);
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var result = await executor.RunWitnessTraversalAsync(plans, renderer, EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (!failCheckpoint)
        {
            Assert.IsNull(result.Refusal, result.Refusal?.Detail);
            Assert.IsNotNull(result.CheckpointRef);
            Assert.IsNotNull(result.AcquisitionRunRef);
        }
        return (store, result, plans, renderer, handler);
    }
}
