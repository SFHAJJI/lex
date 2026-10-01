using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPartitionCoverCheckpointTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NullCoverAndLeafCheckpointsRefuseBeforeWrites(bool leaf)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var checkpoint = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
        var expected = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('a', 64));
        var writes = store.WrittenDigestsInOrder.Count;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(async () =>
        {
            if (leaf)
                await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(store, checkpoint, expected, expected, CancellationToken.None);
            else
                await LuxembourgPartitionCoverCheckpoint.RestoreAsync(store, checkpoint,
                    new LuxembourgQueryPartitionRange("root", Cursor("a"), Cursor("z")), expected, expected, CancellationToken.None);
        });
        Assert.AreEqual(writes, store.WrittenDigestsInOrder.Count);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CopiedCoverReplaysActualHistoryAndCurrentProtection(bool split, bool unenforced)
    {
        var (store, cover, checkpoint, handler) = await AcquireAsync(split);
        var copy = await CopyAsync(store, unenforced: unenforced);
        var sends = handler.SendCount;
        var first = await RestoreAsync(copy, cover, checkpoint);
        var second = await RestoreAsync(copy, cover, checkpoint);
        Assert.AreEqual(3L, first.LeafDeliveredRowCountSum);
        Assert.AreEqual(LuxembourgPartitionCoverBasis.LeafTilingOnly, first.Basis);
        Assert.AreEqual(unenforced ? CustodyMembership.RetainedUnenforced : CustodyMembership.Floored, first.RetainedFloor);
        CollectionAssert.AreEqual(cover.Chain.Leaves.ToArray(), first.Chain.Leaves.ToArray());
        CollectionAssert.AreEqual(cover.Chain.SplitHistory.ToArray(), first.Chain.SplitHistory.ToArray());
        for (var i = 0; i < first.LeafReceipts.Count; i++)
        {
            Assert.AreEqual(ContractJson.Serialize(cover.LeafReceipts[i].Delivery), ContractJson.Serialize(first.LeafReceipts[i].Delivery));
            Assert.AreEqual(ContractJson.Serialize(first.LeafReceipts[i].Delivery), ContractJson.Serialize(second.LeafReceipts[i].Delivery));
        }
        if (split)
        {
            Assert.AreEqual("root", first.Chain.Leaves[1].PartitionId, "an ancestor ID may be reused legally");
            Assert.AreEqual(0L, first.LeafReceipts[0].TryProveFamilyEnumeration("left", out _)!.DeliveredRowCount);
            Assert.AreSame(first.Chain.Leaves[0].EndExclusive, first.Chain.Leaves[1].StartInclusive);
        }
        Assert.AreEqual(sends, handler.SendCount);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("leaf")]
    [DataRow("observation")]
    public async Task MissingCustodyCannotBeReplacedByTheSavedHistory(string missing)
    {
        var (store, cover, checkpoint, _) = await AcquireAsync(true);
        var root = await RootAsync(store, checkpoint);
        var digest = missing switch
        {
            "root" => checkpoint.Sha256,
            "leaf" => root["leaves"]![1]!["sha256"]!.GetValue<string>(),
            _ => cover.LeafReceipts[2].Delivery.CountB.HttpEvidenceRef.Sha256,
        };
        var copy = await CopyAsync(store, digest);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => RestoreAsync(copy, cover, checkpoint));
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("run")]
    [DataRow("profile")]
    public async Task RestoreRequiresTheCallersOriginalScope(string changed)
    {
        var (store, cover, checkpoint, _) = await AcquireAsync(true);
        var other = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('a', 64));
        var root = changed == "root" ? new LuxembourgQueryPartitionRange("another", Cursor("a"), Cursor("z")) : cover.Chain.RootRange;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgPartitionCoverCheckpoint.RestoreAsync(
            store, checkpoint, root, changed == "run" ? other : cover.RunIdentity,
            changed == "profile" ? other : cover.InterpretationProfileRef, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("boundary")]
    [DataRow("parent")]
    [DataRow("reorder")]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("schema")]
    [DataRow("null_splits")]
    [DataRow("null_leaves")]
    public async Task RehashedHistoryStillHasToMatchTheOriginalLeafQueries(string changed)
    {
        var (store, cover, checkpoint, _) = await AcquireAsync(true);
        var root = await RootAsync(store, checkpoint);
        switch (changed)
        {
            case "boundary": root["splits"]![0]!["boundary"]!["key1"] = "n"; break;
            case "parent": root["splits"]![1]!["leaf_partition_id"] = "missing"; break;
            case "reorder":
                var first = root["leaves"]![0]!.DeepClone();
                root["leaves"]![0] = root["leaves"]![1]!.DeepClone();
                root["leaves"]![1] = first;
                break;
            case "duplicate": root["leaves"]![1] = root["leaves"]![0]!.DeepClone(); break;
            case "missing": root["leaves"]!.AsArray().RemoveAt(1); break;
            case "schema": root["schema"] = "lex-lu-partition-cover-checkpoint/99"; break;
            case "null_splits": root["splits"] = null; break;
            case "null_leaves": root["leaves"] = null; break;
        }
        var held = await store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => RestoreAsync(store, cover,
            new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held.Reference.ContentSha256)));
        if (changed == "boundary") StringAssert.Contains(exception.Message, "boundaries differ");
    }

    [TestMethod]
    public async Task CancelledRestoreDoesNotWriteOrSend()
    {
        var (store, cover, checkpoint, handler) = await AcquireAsync(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writes = store.CreateCallCount;
        var sends = handler.SendCount;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgPartitionCoverCheckpoint.RestoreAsync(
            store, checkpoint, cover.Chain.RootRange, cover.RunIdentity, cover.InterpretationProfileRef, cancellation.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
        Assert.AreEqual(sends, handler.SendCount);
    }

    private static Task<LuxembourgPartitionCover> RestoreAsync(ICustodyStore store, LuxembourgPartitionCover original, SourceArtifactRef checkpoint) =>
        LuxembourgPartitionCoverCheckpoint.RestoreAsync(store, checkpoint, original.Chain.RootRange,
            original.RunIdentity, original.InterpretationProfileRef, CancellationToken.None);

    private static async Task<JsonNode> RootAsync(ICustodyStore store, SourceArtifactRef checkpoint) =>
        JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(checkpoint.Sha256, CancellationToken.None)).Span))!;

    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool unenforced = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => unenforced);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private static LuxembourgQueryCursor Cursor(string key) => new(key, "", "", "", "", "");

    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, LuxembourgPartitionCover Cover,
        SourceArtifactRef Checkpoint, LuxembourgAcquisitionTestFixture.SequencedHandler Handler)> AcquireAsync(bool split)
    {
        var (plan, id, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var source = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var root = new LuxembourgQueryPartitionRange("root", Cursor("a"), Cursor("z"));
        var chain = LuxembourgPartitionChain.Root(root);
        if (split) chain = chain.SplitLeaf("root", Cursor("m"), "left", "right").SplitLeaf("right", Cursor("t"), "root", "last");
        var request = new LuxembourgPartitionRunRequest(plan, id, LuxembourgAcquisitionTestFixture.SubjectsSetId, root, source);
        var witness = plan.BindCount(id, $"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}", request.SetId,
            LuxembourgQueryPass.Pass1, root, source).Request;
        string[][] leafRows = split ? [[], ["n"], ["u", "v"]] : [["b", "n", "u"]];
        var responses = new List<string>();
        foreach (var rows in leafRows)
        foreach (var pass in new[] { LuxembourgQueryPass.Pass1, LuxembourgQueryPass.Pass2 })
        {
            responses.Add(LuxembourgAcquisitionTestFixture.CountJson(rows.Length));
            if (rows.Length > 0) responses.Add(LuxembourgAcquisitionTestFixture.RowsJson(rows));
            responses.Add(LuxembourgAcquisitionTestFixture.EmptyRowsJson());
        }
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, message) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(message, responses[ordinal - 1]));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var results = await executor.RunCoverAsync(request, chain, witness, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        foreach (var result in results) Assert.IsNull(result.Refusal, result.Refusal?.CoreRefusalDetail);
        var cover = LuxembourgPartitionCover.TryCreate(chain, results.Select(result => result.Receipt!).ToArray(), null, out var refusal);
        Assert.IsNotNull(cover, refusal.ToString());
        var checkpoint = await LuxembourgPartitionCoverCheckpoint.WriteAsync(store, cover, results, CancellationToken.None);
        return (store, cover, checkpoint, handler);
    }
}
