using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuCorrigendumTripwireProducerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TripwireCheckpointRebuildsBothArtifactsFromItsOwnPairing(bool objectFirst)
    {
        var (original, store, handler) = await AcquireTripwireCheckpointAsync(objectFirst);
        var copy = await CopyTripwireStoreAsync(store);
        var sends = handler.OccurrenceCountFor("X") + handler.OccurrenceCountFor("P");
        for (var i = 0; i < 2; i++)
        {
            var reopened = await EuCorrigendumTripwireProducer.ReopenAsync(copy, original.CheckpointRef!, CancellationToken.None);
            Assert.IsTrue(reopened.Delivered, reopened.Detail);
            Assert.AreEqual(0, reopened.ProductRequestCount);
            Assert.AreEqual(original.CheckpointRef, reopened.CheckpointRef);
            Assert.AreEqual(original.Expressions!.CheckpointRef, reopened.Expressions!.CheckpointRef);
            CollectionAssert.AreEqual(original.TripwireSet!.CanonicalBytes.ToArray(), reopened.TripwireSet!.CanonicalBytes.ToArray());
            CollectionAssert.AreEqual(original.TripwireSet.LineageBytes.ToArray(), reopened.TripwireSet.LineageBytes.ToArray());
        }
        Assert.AreEqual(sends, handler.OccurrenceCountFor("X") + handler.OccurrenceCountFor("P"));
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("expressions")]
    [DataRow("canonical")]
    [DataRow("lineage")]
    public async Task TripwireCheckpointRequiresOriginalArtifacts(string missing)
    {
        var (original, store, _) = await AcquireTripwireCheckpointAsync(false);
        var root = await TripwireRootAsync(store, original);
        var digest = missing switch
        {
            "root" => original.CheckpointRef!.Sha256,
            "expressions" => root["expressions"]!["sha256"]!.GetValue<string>(),
            "canonical" => root["canonical_sha256"]!.GetValue<string>(),
            "lineage" => root["lineage_sha256"]!.GetValue<string>(),
            _ => throw new InvalidOperationException(),
        };
        var copy = await CopyTripwireStoreAsync(store, digest);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            EuCorrigendumTripwireProducer.ReopenAsync(copy, original.CheckpointRef!, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("canonical")]
    [DataRow("lineage")]
    [DataRow("other_pairing")]
    public async Task RehashedTripwireCannotChangeItsHeldOutputOrPairing(string change)
    {
        var (original, store, _) = await AcquireTripwireCheckpointAsync(false);
        var root = await TripwireRootAsync(store, original);
        switch (change)
        {
            case "schema": root["schema"] = "lex-eu-tripwire-production-checkpoint/99"; break;
            case "canonical": root["canonical_sha256"] = root["lineage_sha256"]!.DeepClone(); break;
            case "lineage": root["lineage_sha256"] = root["canonical_sha256"]!.DeepClone(); break;
            case "other_pairing":
                var (other, _, _) = await AcquireTripwireCheckpointAsync(false, store);
                root["expressions"] = (await TripwireRootAsync(store, other))["expressions"]!.DeepClone();
                break;
        }
        var held = await store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuCorrigendumTripwireProducer.ReopenAsync(
            store, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held.Reference.ContentSha256), CancellationToken.None));
        if (change != "schema") StringAssert.Contains(exception.Message, "differs from the original");
    }

    [TestMethod]
    public async Task CancelledTripwireRestoreDoesNotWrite()
    {
        var (original, store, _) = await AcquireTripwireCheckpointAsync(false);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var writes = store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            EuCorrigendumTripwireProducer.ReopenAsync(store, original.CheckpointRef!, cancel.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CheckpointHoldFailureRefusesTheProduction(bool expressionCheckpoint)
    {
        var schema = expressionCheckpoint ? "lex-eu-expression-production-checkpoint/1" : "lex-eu-tripwire-production-checkpoint/1";
        var store = new CountingCustodyStore(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), failSchema: schema);
        var (result, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows(), store);
        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(expressionCheckpoint ? EuCorrigendumTripwireProductionRefusal.ExpressionProductionRefused
            : EuCorrigendumTripwireProductionRefusal.TripwireNotRetained, result.Refusal);
        Assert.AreEqual(1, store.FailedCreateCount);
        Assert.IsNull(result.CheckpointRef);
        Assert.AreEqual(!expressionCheckpoint, result.Expressions!.Delivered);
        StringAssert.Contains(result.Detail, "checkpoint hold refused");
        if (expressionCheckpoint)
            Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.DerivationNotRetained, result.Expressions.Refusal);
    }

    private static async Task<JsonNode> TripwireRootAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore store,
        EuCorrigendumTripwireProductionResult original) => JsonNode.Parse(Encoding.UTF8.GetString(
            (await store.ReadByDigestAsync(original.CheckpointRef!.Sha256, CancellationToken.None)).Span))!;

    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyTripwireStoreAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private static async Task<(EuCorrigendumTripwireProductionResult Result,
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, EuAcquisitionTestFixture.ClassifyingHandler Handler)>
        AcquireTripwireCheckpointAsync(bool objectFirst, EuAcquisitionTestFixture.EuInMemoryCustodyStore? store = null)
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(FourLanguageRows(), CorrigendumRows()));
        store ??= new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuCorrigendumTripwireProducer(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var expression = Request(EuObjectFactsQuerySet.ExpressionFacts, budget);
        var objects = Request(EuObjectFactsQuerySet.ObjectFacts, budget);
        EuCorrigendumTripwireProductionResult result;
        if (objectFirst)
        {
            var pairing = producer.BeginPairing(expression, objects, EuAcquisitionTestFixture.SourceWitness());
            _ = await pairing.RunObjectFactsAsync(CancellationToken.None);
            (result, _) = await pairing.RunExpressionFactsAndProduceAsync(CancellationToken.None);
        }
        else result = await producer.RunAsync(expression, objects, EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);
        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsNotNull(result.CheckpointRef);
        return (result, store, handler);
    }
}
