using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuQueryExecutionAdapterTests
{
    private static readonly Lazy<Task<RunCapture>> RetainedEuRun = new(() => CaptureEuRunAsync(false));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteRunReplaysInSeparateStoresWithOriginalIdentitiesAndNoTraffic(bool served)
    {
        var capture = served ? await CaptureEuRunAsync(true) : await RetainedEuRun.Value;
        var originalSends = capture.Handler.Sends;
        foreach (var iteration in Enumerable.Range(0, 2))
        {
            var store = await CopyLadderStoreAsync(capture.Store);
            var digests = store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
            var result = await EuQueryExecutionAdapter.ReopenAsync(store, capture.Result.AcquisitionCheckpointRef!, capture.Seeds, CancellationToken.None);
            Assert.IsNull(result.Refusal, result.Refusal?.Detail);
            Assert.AreEqual(capture.Result.AcquisitionCheckpointRef, result.AcquisitionCheckpointRef);
            Assert.AreEqual(capture.Result.CorpusRecordSetRef, result.CorpusRecordSetRef);
            Assert.AreEqual(capture.Result.DocumentAcquisitionCheckpointRef, result.DocumentAcquisitionCheckpointRef);
            Assert.AreEqual(capture.Result.ScopeManifestCanonicalSha256, result.ScopeManifestCanonicalSha256);
            Assert.AreEqual(ContractJson.Serialize(capture.Result.FamilyOutcomes), ContractJson.Serialize(result.FamilyOutcomes));
            CollectionAssert.AreEqual((await capture.Store.ReadByDigestAsync(capture.Result.CorpusRecordSetReceipt!.Reference.ContentSha256, CancellationToken.None)).ToArray(),
                (await store.ReadByDigestAsync(result.CorpusRecordSetReceipt!.Reference.ContentSha256, CancellationToken.None)).ToArray());
            CollectionAssert.AreEqual(digests, store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
            Assert.AreEqual(originalSends, capture.Handler.Sends);
            var production = result.CorrigendumTripwires!.ProductionsByFamilyKey.Values.First();
            CollectionAssert.AreEqual(production.Expressions!.Derivation!.DerivationBytes.ToArray(),
                production.TripwireSet!.Derivation.DerivationBytes.ToArray());
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("census")]
    [DataRow("objects")]
    [DataRow("tripwires")]
    [DataRow("witness")]
    [DataRow("documents")]
    [DataRow("object_renderer")]
    [DataRow("witness_renderer")]
    public async Task CompleteRunRequiresEveryRetainedDependency(string missing)
    {
        var capture = await RetainedEuRun.Value;
        var root = await RunRootAsync(capture.Store, capture.Result.AcquisitionCheckpointRef!);
        var digest = missing switch
        {
            "root" => capture.Result.AcquisitionCheckpointRef!.Sha256,
            "census" or "objects" or "tripwires" => root[missing]![0]!["checkpoint"]!["sha256"]!.GetValue<string>(),
            _ => root[missing]!["sha256"]!.GetValue<string>(),
        };
        var copy = await CopyLadderStoreAsync(capture.Store, omit: digest);
        try { _ = await EuQueryExecutionAdapter.ReopenAsync(copy, capture.Result.AcquisitionCheckpointRef!, capture.Seeds, CancellationToken.None); Assert.Fail("Missing evidence must refuse."); }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("result")]
    [DataRow("manifest")]
    [DataRow("run")]
    [DataRow("corpus")]
    [DataRow("seed")]
    [DataRow("object_plan")]
    [DataRow("census_profile")]
    [DataRow("object_run")]
    [DataRow("batch")]
    [DataRow("set")]
    [DataRow("tripwire_family")]
    [DataRow("missing_object")]
    [DataRow("extra_object")]
    [DataRow("reordered_objects")]
    [DataRow("extra_tripwire")]
    [DataRow("null_census")]
    [DataRow("null_witness")]
    public async Task CompleteRunRejectsRehashedDependencyAndIdentityChanges(string changed)
    {
        var capture = await RetainedEuRun.Value;
        var copy = await CopyLadderStoreAsync(capture.Store);
        var root = await RunRootAsync(copy, capture.Result.AcquisitionCheckpointRef!);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-eu-acquisition-checkpoint/99"; break;
            case "result": root["result_sha256"] = new string('a', 64); break;
            case "manifest" or "run" or "corpus": root[changed]!["sha256"] = new string('a', 64); break;
            case "seed": root["census"]![0]!["celex"] = "32016R0679"; break;
            case "object_plan": root["object_plan_resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000998"; break;
            case "census_profile": root["census"]![0]!["profile"]!["sha256"] = new string('a', 64); break;
            case "object_run": root["objects"]![0]!["run"]!["sha256"] = new string('a', 64); break;
            case "batch": root["objects"]![0]!["batch"]![0] = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000998"; break;
            case "set": root["objects"]![0]!["set"] = "ExpressionFacts"; break;
            case "tripwire_family": root["tripwires"]![0]!["family_key"] = "foreign"; break;
            case "missing_object": root["objects"]!.AsArray().RemoveAt(0); break;
            case "extra_object": root["objects"]!.AsArray().Add(root["objects"]![0]!.DeepClone()); break;
            case "reordered_objects":
                var entries = root["objects"]!.AsArray(); var first = entries[0]!.DeepClone(); entries[0] = entries[1]!.DeepClone(); entries[1] = first; break;
            case "extra_tripwire": root["tripwires"]!.AsArray().Add(root["tripwires"]![0]!.DeepClone()); break;
            case "null_census": root["census"] = null; break;
            case "null_witness": root["witness"] = null; break;
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var receipt = await copy.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuQueryExecutionAdapter.ReopenAsync(copy,
            new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000998", receipt.Reference.ContentSha256), capture.Seeds, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("census")]
    [DataRow("tripwires")]
    public async Task CompleteRunRejectsValidDependenciesSubstitutedFromAnotherAcquisition(string changed)
    {
        var capture = await RetainedEuRun.Value;
        var other = await CaptureEuRunAsync(false);
        var copy = await CopyLadderStoreAsync(capture.Store);
        foreach (var digest in other.Store.WrittenDigestsInOrder.Distinct())
            await copy.CreateAsync(await other.Store.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        var root = await RunRootAsync(copy, capture.Result.AcquisitionCheckpointRef!);
        var foreign = await RunRootAsync(copy, other.Result.AcquisitionCheckpointRef!);
        root[changed]![0] = foreign[changed]![0]!.DeepClone();
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var receipt = await copy.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuQueryExecutionAdapter.ReopenAsync(copy,
            new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000997", receipt.Reference.ContentSha256), capture.Seeds, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task CompleteRunRequiresTheContainingBuildsExactSeedScope(int shape)
    {
        var capture = await RetainedEuRun.Value;
        string[] seeds = shape switch { 0 => [], 1 => [capture.Seeds[0], capture.Seeds[0]], _ => ["32016R0679"] };
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuQueryExecutionAdapter.ReopenAsync(capture.Store,
            capture.Result.AcquisitionCheckpointRef!, seeds, CancellationToken.None));
    }

    [TestMethod]
    public async Task CompleteRunDoesNotClaimOriginalBytesAfterCustodyProtectionChanges()
    {
        var capture = await RetainedEuRun.Value;
        var copy = await CopyLadderStoreAsync(capture.Store, weaker: true);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuQueryExecutionAdapter.ReopenAsync(copy,
            capture.Result.AcquisitionCheckpointRef!, capture.Seeds, CancellationToken.None));
    }

    [TestMethod]
    public async Task CompleteRunCheckpointCancellationPropagates()
    {
        var capture = await RetainedEuRun.Value;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuQueryExecutionAdapter.ReopenAsync(capture.Store,
            capture.Result.AcquisitionCheckpointRef!, capture.Seeds, source.Token));
    }

    [TestMethod]
    public async Task CompleteRunCheckpointHoldFailureHasItsOwnRefusal()
    {
        var capture = await CaptureEuRunAsync(false, true);
        Assert.AreEqual(EuQueryExecutionRefusal.AcquisitionCheckpointNotRetained, capture.Result.Refusal?.Code);
        Assert.IsNull(capture.Result.AcquisitionCheckpointRef);
    }

    [TestMethod]
    public async Task CompleteRunMustRetainAnUnusedRendererBeforePublishingItsCheckpoint()
    {
        var capture = await CaptureEuRunAsync(false, failUnusedRenderer: true);
        Assert.AreEqual(EuQueryExecutionRefusal.AcquisitionCheckpointNotRetained, capture.Result.Refusal?.Code);
        StringAssert.Contains(capture.Result.Refusal!.Detail, "renderer hold refused");
        Assert.IsNull(capture.Result.AcquisitionCheckpointRef);
    }

    private static async Task<JsonNode> RunRootAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore store, SourceArtifactRef reference) =>
        JsonNode.Parse((await store.ReadByDigestAsync(reference.Sha256, CancellationToken.None)).Span)!;
    private sealed record RunCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, EuQueryExecutionResult Result,
        string[] Seeds, RecordingLadderHandler Handler);
    private static async Task<RunCapture> CaptureEuRunAsync(bool served, bool failRoot = false, bool failUnusedRenderer = false)
    {
        // ONE BUDGET FOR THE WHOLE RUN. The adapter refuses a census request
        // carrying a different instance, because two counters reading the same
        // limit bound that many requests each and neither bounds the run.
        var runWireBudget = EuAcquisitionTestFixture.TestWireBudget();

        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000001.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        // Family P: all nine object-authority predicates plus all four read relation predicates,
        // exactly one outcome row each (a bound value or the explicit unbound marker) -- the shape
        // EuCellarObjectDecode.TryBuildPredicateObservation and TryBuildRelationFamilyObservation both
        // require per object. resource_legal_type carries the one bound value this test's own
        // TryResolveRecordForm reads back to resolve EuActForm.Regulation.
        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);

        Assert.AreEqual(13, pRows.Count, "family P must carry exactly 13 predicate outcomes for one object.");

        // #418 slice 6: the derivation now reads every expression's language, so this expression
        // states one; German, which the body policy does not serve, keeps this run's no-fetch world.
        var xRows = new[]
        {
            EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri),
            EuAcquisitionTestFixture.ExpressionLanguageRow(
                rootIri, expressionIri, "http://publications.europa.eu/resource/authority/language/" + (served ? "ENG" : "DEU")),
        };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows,
                EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows,
                EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows,
                EuAcquisitionTestFixture.RootWatermarkProjection),
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["A"] = EuAcquisitionTestFixture.AxiomAbsenceScriptFor(rootIri),
            ["L"] = EuAcquisitionTestFixture.LocatedAmendmentAbsenceScriptFor(rootIri),
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
            // Defect 3's own fix drives a real witness traversal from the census bound
            // (watermarkLexical, this same root) on every delivered run now, not just when a test is
            // specifically about the witness. Scripted here as a clean confirmed-empty traversal:
            // nothing changed between the census bound and this run's own send.
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(
                    rootIri, watermarkLexical)),
        };

        var handler = new RecordingLadderHandler(new EuAcquisitionTestFixture.ClassifyingHandler(scripts,
            request => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK,
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-xhtml-200-body.bin")),
                "application/xhtml+xml")));
        var unusedRendererSha = EuAcquisitionTestFixture.BuildRendererSource(1009).Reference.Sha256;
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
            failWriteDigest: (digest, _) => failUnusedRenderer && digest == unusedRendererSha,
            failSchema: failRoot ? "lex-eu-acquisition-checkpoint/1" : null);
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(1),
            runWireBudget);

        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(2),
            runWireBudget);

        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(3),
            runWireBudget);

        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(4),
            runWireBudget);

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(104),
            runWireBudget);
        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            new EuObjectFactsBatchPolicy(
                pPlan, pPlanId, EuAcquisitionTestFixture.BuildRendererSource(2),
                EuAcquisitionTestFixture.SourceWitness()),
            EuAcquisitionTestFixture.BuildRendererSource(9),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1009),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            runWireBudget,
            CancellationToken.None);
        if (!failRoot && !failUnusedRenderer) { Assert.IsNull(result.Refusal, result.Refusal?.Detail); Assert.IsNotNull(result.AcquisitionCheckpointRef); }
        return new(store, result, [seed.Celex], handler);
    }
}
