using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuEnumerationCheckpointTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public async Task IndependentStoreReopensBothPassesWithoutWritesOrTransport(int rows)
    {
        var (store, result, handler) = await AcquireAsync(rows);
        var original = result.Receipt!.Delivery;
        Assert.IsNotNull(result.CheckpointRef);
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        foreach (var digest in store.WrittenDigestsInOrder.Distinct())
            await copy.CreateAsync(await store.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        var readOnly = new ReadOnlyStore(copy);
        var reopened = await EuEnumerationCheckpoint.OpenAsync(readOnly, result.CheckpointRef,
            original.RunIdentity, original.InterpretationProfileRef, CancellationToken.None);
        var repeated = await EuEnumerationCheckpoint.OpenAsync(readOnly, result.CheckpointRef,
            original.RunIdentity, original.InterpretationProfileRef, CancellationToken.None);
        Assert.AreEqual(ContractJson.Serialize(original), ContractJson.Serialize(reopened));
        Assert.AreEqual(ContractJson.Serialize(reopened), ContractJson.Serialize(repeated));
        Assert.AreEqual(rows, reopened.DeliveredRowCountA);
        Assert.AreEqual(rows, reopened.DeliveredRowCountB);
        Assert.AreEqual(4, handler.OccurrenceCountFor("P"));
        Assert.AreEqual(0, readOnly.Writes);
    }

    [TestMethod]
    [DataRow("checkpoint", false)]
    [DataRow("count", false)]
    [DataRow("second_page", false)]
    [DataRow("payload", false)]
    [DataRow("query", false)]
    [DataRow("payload", true)]
    public async Task MissingOrChangedDependencyRefuses(string target, bool corrupt)
    {
        var (store, result, _) = await AcquireAsync(3);
        var delivery = result.Receipt!.Delivery;
        var page = await new RepeatedEnumerationDeliveryReopenGlue(store)
            .ReopenPageEvidenceAsync(delivery.PagesB.Pages[0].Evidence, CancellationToken.None);
        var digest = target switch
        {
            "checkpoint" => result.CheckpointRef!.Sha256,
            "count" => delivery.CountA.QueryPlanRef.Sha256,
            "second_page" => delivery.PagesB.Pages[0].Evidence.HttpEvidenceRef.Sha256,
            "payload" => page.HttpEvidence.Hops[0].Sha256,
            "query" => page.LogicalRequest.Body.Sha256,
            _ => throw new InvalidOperationException(),
        };
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuEnumerationCheckpoint.OpenAsync(
            new ReadOnlyStore(store, digest, corrupt), result.CheckpointRef!, delivery.RunIdentity,
            delivery.InterpretationProfileRef, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("run")]
    [DataRow("profile")]
    public async Task CallerIdentityMustMatch(string changed)
    {
        var (store, result, _) = await AcquireAsync(3);
        var delivery = result.Receipt!.Delivery;
        var other = new SourceArtifactRef("urn:uuid:10000000-0000-0000-0000-000000000001", new string('a', 64));
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuEnumerationCheckpoint.OpenAsync(
            new ReadOnlyStore(store), result.CheckpointRef!, changed == "run" ? other : delivery.RunIdentity,
            changed == "profile" ? other : delivery.InterpretationProfileRef, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("duplicate_pass")]
    [DataRow("partition")]
    [DataRow("schema")]
    public async Task RehashedRootCannotReplaceTheUnderlyingChecks(string change)
    {
        var (store, result, _) = await AcquireAsync(3);
        var delivery = result.Receipt!.Delivery;
        var root = JsonNode.Parse(Encoding.UTF8.GetString(
            (await store.ReadByDigestAsync(result.CheckpointRef!.Sha256, CancellationToken.None)).Span))!;
        if (change == "duplicate_pass")
        {
            root["count_b"] = root["count_a"]!.DeepClone();
            root["pages_b"] = root["pages_a"]!.DeepClone();
        }
        else if (change == "partition") root["partition_key"] = "other-partition";
        else root["schema"] = "lex-eu-enumeration-checkpoint/99";
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var held = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var changed = new SourceArtifactRef("urn:uuid:10000000-0000-0000-0000-000000000002", held.Reference.ContentSha256);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuEnumerationCheckpoint.OpenAsync(
            new ReadOnlyStore(store), changed, delivery.RunIdentity, delivery.InterpretationProfileRef,
            CancellationToken.None));
        if (change == "duplicate_pass") StringAssert.Contains(exception.Message, "repeats an observation");
        if (change == "partition") StringAssert.Contains(exception.Message, "Recomputed enumeration identity");
    }

    [TestMethod]
    public async Task CancelledReopenDoesNotReadOrWrite()
    {
        var (store, result, _) = await AcquireAsync(0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var readOnly = new ReadOnlyStore(store);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuEnumerationCheckpoint.OpenAsync(
            readOnly, result.CheckpointRef!, result.Receipt!.Delivery.RunIdentity,
            result.Receipt.Delivery.InterpretationProfileRef, cancellation.Token));
        Assert.AreEqual(0, readOnly.Reads);
        Assert.AreEqual(0, readOnly.Writes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestoredReceiptUsesCurrentStoreProtectionAndCanProveTheSameFamily(bool unenforced)
    {
        var (store, result, handler) = await AcquireAsync(3);
        var original = result.Receipt!;
        Assert.AreEqual(CustodyMembership.Floored, original.RetainedFloor);
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => unenforced);
        foreach (var digest in store.WrittenDigestsInOrder.Distinct())
            await copy.CreateAsync(await store.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        var restored = await EuEnumerationCheckpoint.RestoreReceiptAsync(copy, result.CheckpointRef!,
            original.Delivery.RunIdentity, original.Delivery.InterpretationProfileRef, CancellationToken.None);
        Assert.AreEqual(ContractJson.Serialize(original.Delivery), ContractJson.Serialize(restored.Delivery));
        Assert.AreEqual(unenforced ? CustodyMembership.RetainedUnenforced : CustodyMembership.Floored,
            restored.RetainedFloor);
        CollectionAssert.AreEquivalent(original.RetainedMembership.Keys.ToArray(), restored.RetainedMembership.Keys.ToArray());
        var proof = restored.TryProveFamilyEnumeration(restored.Delivery.PartitionKey, out var refusal);
        Assert.IsNotNull(proof, refusal.ToString());
        Assert.AreEqual(restored.RetainedFloor, proof.RetainedFloor);
        Assert.AreEqual(original.Delivery.DeliveredRowCountA, proof.DeliveredRowCount);
        Assert.AreEqual(4, handler.OccurrenceCountFor("P"));
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestoreDoesNotIssueAReceiptWhenCurrentCustodyFails(bool substitutesReceipt)
    {
        var (store, result, _) = await AcquireAsync(3);
        var failing = new FaultedWriteStore(store, substitutesReceipt);
        Task Restore() => EuEnumerationCheckpoint.RestoreReceiptAsync(failing, result.CheckpointRef!,
            result.Receipt!.Delivery.RunIdentity, result.Receipt.Delivery.InterpretationProfileRef, CancellationToken.None);
        if (substitutesReceipt) await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(Restore);
        else await Assert.ThrowsExactlyAsync<CustodyRequiredException>(Restore);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CheckpointWriteDoesNotAcceptFailedOrSubstitutedCustody(bool substitutesReceipt)
    {
        var (store, result, _) = await AcquireAsync(3);
        var (plan, _) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        Task Write() => EuEnumerationCheckpoint.WriteAsync(new FaultedWriteStore(store, substitutesReceipt),
            result.Receipt!.Delivery, plan.CreateDeliveryProfile(EuObjectFactsQuerySet.ObjectFacts), CancellationToken.None);
        if (substitutesReceipt) await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(Write);
        else await Assert.ThrowsExactlyAsync<CustodyRequiredException>(Write);
    }

    private sealed class FaultedWriteStore(ICustodyStore inner, bool substitute) : ICustodyStore
    {
        public Task<DurableBlobWriteReceipt> CreateAsync(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass,
            CancellationToken cancellationToken) => substitute
            ? inner.CreateAsync(Encoding.UTF8.GetBytes("unrelated receipt"), custodyClass, cancellationToken)
            : throw new CustodyRequiredException("Deliberate current hold failure.");
        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);
        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }
    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        EuEnumerationRunResult Result, EuAcquisitionTestFixture.ClassifyingHandler Handler)> AcquireAsync(int count)
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(EuAppendixASeedMap.SeedsInCelexOrder[0].WorkRoot, out _)!;
        var rows = Enumerable.Range(0, count).Select(n => EuAcquisitionTestFixture.ObjectFactRow(root,
            EuAcquisitionTestFixture.WorkIsAboutConceptEurovoc,
            "http://eurovoc.europa.eu/" + n.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>
        {
            ["P"] = EuAcquisitionTestFixture.ScriptFor("P", count, rows, EuAcquisitionTestFixture.ObjectFactsProjection),
        };
        var (plan, id) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var result = await executor.RunObjectFactsPartitionAsync(new EuObjectFactsPartitionRunRequest(
            plan, id, EuObjectFactsQuerySet.ObjectFacts, [root], EuAcquisitionTestFixture.BuildRendererSource(9901),
            EuAcquisitionTestFixture.TestWireBudget()), EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);
        Assert.IsNull(result.Refusal, result.Refusal?.CoreRefusalDetail);
        Assert.IsNotNull(result.Receipt);
        Assert.IsNotNull(result.CheckpointRef);
        return (store, result, handler);
    }

    private sealed class ReadOnlyStore(ICustodyStore inner, string? failedDigest = null, bool corrupt = false) : ICustodyStore
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public Task<DurableBlobWriteReceipt> CreateAsync(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            Writes++;
            throw new InvalidOperationException("Offline verification must not write custody.");
        }
        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            ReadByDigestAsync(reference.ContentSha256, cancellationToken);
        public async Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken)
        {
            Reads++;
            if (contentSha256 == failedDigest)
            {
                if (!corrupt) throw new FileNotFoundException("Deliberately missing held dependency.");
                return Encoding.UTF8.GetBytes("changed");
            }
            return await inner.ReadByDigestAsync(contentSha256, cancellationToken);
        }
    }
}
