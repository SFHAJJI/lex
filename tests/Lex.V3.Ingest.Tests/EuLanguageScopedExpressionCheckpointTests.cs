using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuLanguageScopedExpressionProducerTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    public async Task CheckpointRebuildsBothOriginalArtifacts(bool withObjects, bool objectFirst, bool empty)
    {
        var (original, store, handler) = await AcquireCheckpointAsync(withObjects, objectFirst, empty);
        var copy = await CopyCheckpointStoreAsync(store);
        var sends = handler.OccurrenceCountFor("X") + handler.OccurrenceCountFor("P");
        var first = await EuLanguageScopedExpressionProducer.ReopenAsync(copy, original.CheckpointRef!, CancellationToken.None);
        var second = await EuLanguageScopedExpressionProducer.ReopenAsync(copy, original.CheckpointRef!, CancellationToken.None);
        foreach (var reopened in new[] { first, second })
        {
            Assert.IsTrue(reopened.Delivered, reopened.Detail);
            Assert.AreEqual(0, reopened.ProductRequestCount);
            Assert.AreEqual(original.CheckpointRef, reopened.CheckpointRef);
            CollectionAssert.AreEqual(original.Derivation!.DerivationBytes.ToArray(), reopened.Derivation!.DerivationBytes.ToArray());
            CollectionAssert.AreEqual(original.Derivation.EpisodeBytes.ToArray(), reopened.Derivation.EpisodeBytes.ToArray());
            CollectionAssert.AreEquivalent(original.ObjectsAskedAbout!.ToArray(), reopened.ObjectsAskedAbout!.ToArray());
            Assert.ThrowsExactly<InvalidOperationException>(() => reopened.ExpressionsOf(OtherWork));
        }
        Assert.AreEqual(sends, handler.OccurrenceCountFor("X") + handler.OccurrenceCountFor("P"));
    }

    [TestMethod]
    public async Task ChangedCustodyProtectionCannotClaimTheOriginalDerivationBytes()
    {
        var (original, store, _) = await AcquireCheckpointAsync(true);
        var weaker = await CopyCheckpointStoreAsync(store, weaker: true);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            EuLanguageScopedExpressionProducer.ReopenAsync(weaker, original.CheckpointRef!, CancellationToken.None));
        StringAssert.Contains(exception.Message, "differs from the original");
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("expression")]
    [DataRow("objects")]
    [DataRow("renderer")]
    [DataRow("derivation")]
    [DataRow("episode")]
    public async Task ProducerCheckpointRequiresItsOriginalArtifacts(string missing)
    {
        var (original, store, _) = await AcquireCheckpointAsync(true);
        var root = await ProductionRootAsync(store, original);
        var digest = missing switch
        {
            "root" => original.CheckpointRef!.Sha256,
            "expression" => root["expression"]!["checkpoint"]!["sha256"]!.GetValue<string>(),
            "objects" => root["objects"]!["checkpoint"]!["sha256"]!.GetValue<string>(),
            "renderer" => root["expression"]!["renderer"]!["sha256"]!.GetValue<string>(),
            "derivation" => root["derivation_sha256"]!.GetValue<string>(),
            "episode" => root["episode_sha256"]!.GetValue<string>(),
            _ => throw new InvalidOperationException(),
        };
        var copy = await CopyCheckpointStoreAsync(store, digest);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            EuLanguageScopedExpressionProducer.ReopenAsync(copy, original.CheckpointRef!, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("batch")]
    [DataRow("plan_identity")]
    [DataRow("run")]
    [DataRow("profile")]
    [DataRow("derivation")]
    [DataRow("episode")]
    [DataRow("omit_objects")]
    public async Task RehashedProductionCannotChangeOriginalPairing(string change)
    {
        var (original, store, _) = await AcquireCheckpointAsync(true);
        var root = await ProductionRootAsync(store, original);
        switch (change)
        {
            case "schema": root["schema"] = "lex-eu-expression-production-checkpoint/99"; break;
            case "batch": root["expression"]!["batch"]![0] = OtherWork; break;
            case "plan_identity": root["expression"]!["plan_resource_id"] = $"urn:uuid:{Guid.NewGuid():D}"; break;
            case "run": root["expression"]!["run"]!["resource_id"] = $"urn:uuid:{Guid.NewGuid():D}"; break;
            case "profile": root["expression"]!["profile"]!["resource_id"] = $"urn:uuid:{Guid.NewGuid():D}"; break;
            case "derivation": root["derivation_sha256"] = root["episode_sha256"]!.DeepClone(); break;
            case "episode": root["episode_sha256"] = root["derivation_sha256"]!.DeepClone(); break;
            case "omit_objects": root["objects"] = null; break;
        }
        var held = await store.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuLanguageScopedExpressionProducer.ReopenAsync(
            store, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held.Reference.ContentSha256), CancellationToken.None));
        if (change == "batch") StringAssert.Contains(exception.Message, "batch does not match");
        if (change == "plan_identity") StringAssert.Contains(exception.Message, "plan identity differs");
        if (change is "derivation" or "episode" or "omit_objects") StringAssert.Contains(exception.Message, "differs from the original");
    }

    [TestMethod]
    public async Task CancelledProductionRestoreDoesNotWrite()
    {
        var (original, store, _) = await AcquireCheckpointAsync(false);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var writes = store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            EuLanguageScopedExpressionProducer.ReopenAsync(store, original.CheckpointRef!, cancel.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
    }

    private static async Task<JsonNode> ProductionRootAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore store,
        EuLanguageScopedExpressionProductionResult original) => JsonNode.Parse(Encoding.UTF8.GetString(
            (await store.ReadByDigestAsync(original.CheckpointRef!.Sha256, CancellationToken.None)).Span))!;

    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyCheckpointStoreAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private static async Task<(EuLanguageScopedExpressionProductionResult Result,
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, EuAcquisitionTestFixture.ClassifyingHandler Handler)>
        AcquireCheckpointAsync(bool withObjects, bool objectFirst = false, bool empty = false)
    {
        var rows = empty ? Array.Empty<string>() : ThreeLanguageRows();
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(withObjects ? ScriptsWithWorkDate(rows) : Scripts(rows));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var expression = Request(EuObjectFactsQuerySet.ExpressionFacts, budget);
        var objects = withObjects ? Request(EuObjectFactsQuerySet.ObjectFacts, budget) : null;
        EuLanguageScopedExpressionProductionResult result;
        if (objectFirst)
        {
            var pairing = new EuLanguageScopedExpressionProducer.Pairing(producer, expression, objects!, EuAcquisitionTestFixture.SourceWitness());
            _ = await pairing.RunObjectFactsAsync(CancellationToken.None);
            (result, _, _, _) = await pairing.RunExpressionFactsAndDeriveAsync(CancellationToken.None);
        }
        else result = await producer.RunAsync(expression, objects, EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);
        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsNotNull(result.CheckpointRef);
        return (result, store, handler);
    }
}
