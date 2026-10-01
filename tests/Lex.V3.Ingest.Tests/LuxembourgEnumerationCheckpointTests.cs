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
public sealed class LuxembourgEnumerationCheckpointTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(3, false)]
    [DataRow(700, false)]
    [DataRow(3, true)]
    public async Task ReopensOriginalTemplatePassesAndCurrentProtection(int count, bool unenforced)
    {
        var (store, result, handler) = await AcquireAsync(count);
        var original = result.Receipt!;
        var copy = await CopyAsync(store, unenforced: unenforced);
        var sends = handler.SendCount;
        var first = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(copy, result.CheckpointRef!,
            original.Delivery.RunIdentity, original.Delivery.InterpretationProfileRef, CancellationToken.None);
        var second = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(copy, result.CheckpointRef!,
            original.Delivery.RunIdentity, original.Delivery.InterpretationProfileRef, CancellationToken.None);
        Assert.AreEqual(ContractJson.Serialize(original.Delivery), ContractJson.Serialize(first.Delivery));
        Assert.AreEqual(ContractJson.Serialize(first.Delivery), ContractJson.Serialize(second.Delivery));
        Assert.AreEqual(unenforced ? CustodyMembership.RetainedUnenforced : CustodyMembership.Floored, first.RetainedFloor);
        var proof = first.TryProveFamilyEnumeration(first.Delivery.PartitionKey, out var refusal);
        Assert.IsNotNull(proof, refusal.ToString());
        Assert.AreEqual((long)count, proof.DeliveredRowCount);
        Assert.AreEqual(first.RetainedFloor, proof.RetainedFloor);
        Assert.AreEqual(sends, handler.SendCount);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("plan_wire")]
    [DataRow("plan_framed")]
    [DataRow("renderer")]
    [DataRow("second_page")]
    [DataRow("query_body")]
    [DataRow("payload")]
    public async Task EveryRetainedDependencyMustStillExist(string missing)
    {
        var (store, result, _) = await AcquireAsync(3);
        var root = await RootAsync(store, result);
        var delivery = result.Receipt!.Delivery;
        var page = await new RepeatedEnumerationDeliveryReopenGlue(store)
            .ReopenPageEvidenceAsync(delivery.PagesB.Pages[0].Evidence, CancellationToken.None);
        var digest = missing switch
        {
            "root" => result.CheckpointRef!.Sha256,
            "plan_wire" => root["plan_wire_sha256"]!.GetValue<string>(),
            "plan_framed" => root["plan_ref"]!["sha256"]!.GetValue<string>(),
            "renderer" => root["renderer_source_ref"]!["sha256"]!.GetValue<string>(),
            "second_page" => delivery.PagesB.Pages[0].Evidence.HttpEvidenceRef.Sha256,
            "query_body" => page.LogicalRequest.Body.Sha256,
            "payload" => page.HttpEvidence.Hops[0].Sha256,
            _ => throw new InvalidOperationException(),
        };
        var copy = await CopyAsync(store, digest);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(
            copy, result.CheckpointRef!, delivery.RunIdentity, delivery.InterpretationProfileRef, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("run")]
    [DataRow("profile")]
    public async Task CallerPinsCannotBeTakenFromAnotherAcquisition(string change)
    {
        var (store, result, _) = await AcquireAsync(3);
        var delivery = result.Receipt!.Delivery;
        var other = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('a', 64));
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(
            store, result.CheckpointRef!, change == "run" ? other : delivery.RunIdentity,
            change == "profile" ? other : delivery.InterpretationProfileRef, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("duplicate_pass")]
    [DataRow("partition")]
    [DataRow("set")]
    [DataRow("renderer_identity")]
    [DataRow("schema")]
    public async Task RehashedCheckpointCannotOverrideTheCheckedBindings(string change)
    {
        var (store, result, _) = await AcquireAsync(3);
        var root = await RootAsync(store, result);
        switch (change)
        {
            case "duplicate_pass": root["count_b"] = root["count_a"]!.DeepClone(); root["pages_b"] = root["pages_a"]!.DeepClone(); break;
            case "partition": root["partition_key"] = "another-partition"; break;
            case "set": root["set_id"] = "A"; break;
            case "renderer_identity": root["renderer_source_ref"]!["resource_id"] = $"urn:uuid:{Guid.NewGuid():D}"; break;
            case "schema": root["schema"] = "lex-lu-enumeration-checkpoint/99"; break;
        }
        var held = await store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var delivery = result.Receipt!.Delivery;
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(
            store, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held.Reference.ContentSha256),
            delivery.RunIdentity, delivery.InterpretationProfileRef, CancellationToken.None));
        if (change == "duplicate_pass") StringAssert.Contains(exception.Message, "repeats an observation");
        if (change == "partition") StringAssert.Contains(exception.Message, "comparison identity differs");
    }

    [TestMethod]
    public async Task CancelledRestoreDoesNotHoldAnything()
    {
        var (store, result, handler) = await AcquireAsync(0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writes = store.CreateCallCount;
        var sends = handler.SendCount;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(
            store, result.CheckpointRef!, result.Receipt!.Delivery.RunIdentity,
            result.Receipt.Delivery.InterpretationProfileRef, cancellation.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
        Assert.AreEqual(sends, handler.SendCount);
    }

    private static async Task<JsonNode> RootAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore store,
        LuxembourgEnumerationRunResult result) => JsonNode.Parse(Encoding.UTF8.GetString(
            (await store.ReadByDigestAsync(result.CheckpointRef!.Sha256, CancellationToken.None)).Span))!;

    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool unenforced = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => unenforced);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgEnumerationRunResult Result, LuxembourgAcquisitionTestFixture.SequencedHandler Handler)> AcquireAsync(int count)
    {
        var (plan, id, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var source = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var partition = LuxembourgAcquisitionTestFixture.FullRange();
        var request = new LuxembourgPartitionRunRequest(plan, id, LuxembourgAcquisitionTestFixture.SubjectsSetId, partition, source);
        var witness = plan.BindCount(id, $"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}", request.SetId,
            LuxembourgQueryPass.Pass1, partition, source).Request;
        var rows = Enumerable.Range(0, count).Select(n => "a" + n.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "-é-𐀀").ToArray();
        var responses = new List<string>();
        foreach (var pass in new[] { LuxembourgQueryPass.Pass1, LuxembourgQueryPass.Pass2 })
        {
            responses.Add(LuxembourgAcquisitionTestFixture.CountJson(count));
            var limit = checked((int)plan.PageLimitFor(pass));
            for (var index = 0; index < rows.Length; index += limit)
                responses.Add(LuxembourgAcquisitionTestFixture.RowsJson(rows.Skip(index).Take(limit).ToArray()));
            responses.Add(LuxembourgAcquisitionTestFixture.EmptyRowsJson());
        }
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, message) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(message, responses[ordinal - 1]));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var result = await executor.RunPartitionAsync(request, witness, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(result.Refusal, result.Refusal?.CoreRefusalDetail);
        Assert.IsNotNull(result.Receipt);
        Assert.IsNotNull(result.CheckpointRef);
        return (store, result, handler);
    }
}
