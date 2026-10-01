using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuFormexPackagePopulationProducerTests
{
    private static readonly Lazy<Task<PackageCapture>> AnnexCapture = new(() => CapturePackageAsync(2));

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(5, false)]
    [DataRow(6, false)]
    [DataRow(7, false)]
    [DataRow(2, true)]
    public async Task PackageReplayRepeatsTypedAndAnnexResultsInCopiedCustody(int shape, bool weaker)
    {
        var capture = shape == 2 ? await AnnexCapture.Value : await CapturePackageAsync(shape);
        var copy = await CopyPackageStoreAsync(capture.Store, weaker: weaker);
        var writes = copy.CreateCallCount;
        var sends = capture.Handler.PackageRequests.Count + capture.Handler.PdfRequests.Count + capture.Handler.RobotsSends;
        var first = await RestorePackageAsync(copy, capture);
        var second = await RestorePackageAsync(copy, capture);
        Assert.AreSame(capture.Enumeration.Expression, first.Outcome.Expression);
        Assert.AreEqual(capture.Result.Outcome.Kind, first.Outcome.Kind);
        Assert.AreEqual(capture.Result.Outcome.NotAcquiredReason, first.Outcome.NotAcquiredReason);
        Assert.AreEqual(capture.Result.Outcome.Detail, first.Outcome.Detail);
        Assert.AreEqual(capture.Result.Outcome.AcquiredInventory?.IdentitySha256, first.Outcome.AcquiredInventory?.IdentitySha256);
        Assert.AreEqual(capture.Result.AnnexClassification?.IdentitySha256, first.AnnexClassification?.IdentitySha256);
        Assert.AreEqual(first.AnnexClassification?.IdentitySha256, second.AnnexClassification?.IdentitySha256);
        Assert.AreEqual(capture.Result.AnnexClassification?.ProfileRef, first.AnnexClassification?.ProfileRef);
        Assert.AreEqual(capture.Result.CheckpointRef, first.CheckpointRef);
        Assert.AreEqual(0, first.ProductRequestCount);
        Assert.AreEqual(writes, copy.CreateCallCount, "offline derivation reuses captured identities and makes no custody writes");
        Assert.AreEqual(sends, capture.Handler.PackageRequests.Count + capture.Handler.PdfRequests.Count + capture.Handler.RobotsSends);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HistoricalPackageCheckpointKeepsRequiredCelexAndOriginalAnnexIdentity(bool missingCelex)
    {
        var capture = await CapturePackageAsync(2, missingCelex: missingCelex);
        var root = await PackageRootAsync(capture.Store, capture.Result.CheckpointRef!);
        root["schema"] = "lex-eu-formex-package-checkpoint/1";
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        var held = await capture.Store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var checkpoint = new SourceArtifactRef(capture.Result.CheckpointRef!.ResourceId, held.Reference.ContentSha256);
        Task<EuFormexPackageAcquisitionResult> Reopen() => EuFormexPackageAcquisitionProducer.ReopenAsync(
            capture.Store, checkpoint, capture.Enumeration, capture.Corpus, capture.Celex,
            capture.Renderer, CancellationToken.None);
        if (missingCelex)
        {
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => Reopen());
            return;
        }
        var reopened = await Reopen();
        Assert.AreEqual(capture.Result.Outcome.AcquiredInventory!.IdentitySha256,
            reopened.Outcome.AcquiredInventory!.IdentitySha256);
        Assert.AreEqual(capture.Result.AnnexClassification!.IdentitySha256,
            reopened.AnnexClassification!.IdentitySha256);
        Assert.AreEqual(0, reopened.ProductRequestCount);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("zip_route")]
    [DataRow("zip_body")]
    [DataRow("pdf_route")]
    [DataRow("pdf_body")]
    [DataRow("xhtml_body")]
    public async Task PackageReplayCannotReplaceMissingAcquisitionEvidence(string missing)
    {
        var capture = await AnnexCapture.Value;
        var root = await PackageRootAsync(capture.Store, capture.Result.CheckpointRef!);
        var digest = missing switch
        {
            "root" => capture.Result.CheckpointRef!.Sha256,
            "zip_route" => root["fetches"]![0]!["route"]!["sha256"]!.GetValue<string>(),
            "zip_body" => capture.Result.Outcome.AcquiredInventory!.SourceReceipt.Reference.ContentSha256,
            "pdf_route" => root["fetches"]![1]!["route"]!["sha256"]!.GetValue<string>(),
            "pdf_body" => capture.Result.AnnexClassification!.PdfReceipt.Reference.ContentSha256,
            _ => capture.Corpus!.Set.Records.First(record => record.Body.Receipt is not null).Body.Receipt!.Reference.ContentSha256,
        };
        var copy = await CopyPackageStoreAsync(capture.Store, omit: digest);
        try
        {
            _ = await RestorePackageAsync(copy, capture);
            Assert.Fail("A missing original acquisition dependency must refuse.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("celex")]
    [DataRow("corpus")]
    [DataRow("renderer")]
    [DataRow("enumeration")]
    public async Task PackageReplayRequiresTheOriginalCallerInputs(string changed)
    {
        var capture = await AnnexCapture.Value;
        var enumeration = changed == "enumeration" ? (await CapturePackageAsync(1)).Enumeration : capture.Enumeration;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFormexPackageAcquisitionProducer.ReopenAsync(
            capture.Store, capture.Result.CheckpointRef!, enumeration, changed == "corpus" ? null : capture.Corpus,
            changed == "celex" ? "32003L0088" : WorkCelex,
            changed == "renderer" ? EuAcquisitionTestFixture.BuildRendererSource(8502) : capture.Renderer, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("input")]
    [DataRow("result")]
    [DataRow("count")]
    [DataRow("null_fetches")]
    [DataRow("extra_profile")]
    [DataRow("missing_fetch")]
    [DataRow("address")]
    [DataRow("profile")]
    [DataRow("run")]
    public async Task RehashedPackageAssociationsMustStillRepeatTheOriginalDerivation(string changed)
    {
        var capture = await AnnexCapture.Value;
        var copy = await CopyPackageStoreAsync(capture.Store);
        var root = await PackageRootAsync(copy, capture.Result.CheckpointRef!);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-eu-formex-package-checkpoint/99"; break;
            case "input": root["input_sha256"] = new string('a',64); break;
            case "result": root["result_sha256"] = new string('a',64); break;
            case "count": root["product_request_count"] = 99; break;
            case "null_fetches": root["fetches"] = null; break;
            case "extra_profile": root["profiles"]!.AsArray().Add(root["profiles"]![0]!.DeepClone()); break;
            case "missing_fetch": root["fetches"]!.AsArray().RemoveAt(1); break;
            case "address": root["fetches"]![0]!["uri"] = "https://publications.europa.eu/resource/celex/32003L0088"; break;
            case "profile": root["profiles"]![0]!["sha256"] = new string('a',64); break;
            case "run": root["fetches"]![0]!["run"]!["sha256"] = new string('a',64); break;
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        var held = await copy.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFormexPackageAcquisitionProducer.ReopenAsync(
            copy, new SourceArtifactRef(capture.Result.CheckpointRef!.ResourceId, held.Reference.ContentSha256), capture.Enumeration,
            capture.Corpus, WorkCelex, capture.Renderer, CancellationToken.None));
    }

    [TestMethod]
    public async Task PackageCheckpointCancellationPropagates()
    {
        var capture = await AnnexCapture.Value;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuFormexPackageAcquisitionProducer.ReopenAsync(
            capture.Store, capture.Result.CheckpointRef!, capture.Enumeration, capture.Corpus, WorkCelex, capture.Renderer, source.Token));
    }

    [TestMethod]
    public async Task PackageCheckpointHoldFailureHasADistinctTypedOutcome()
    {
        var capture = await CapturePackageAsync(0, failCheckpoint: true);
        Assert.AreEqual(EuFormexPackageNotAcquiredReason.CheckpointNotRetained, capture.Result.Outcome.NotAcquiredReason);
        Assert.IsNull(capture.Result.CheckpointRef);
        Assert.AreEqual(0, capture.Handler.PackageRequests.Count);
    }

    [TestMethod]
    public async Task SuccessfulPopulationRetainsItsExactPackageCheckpointAssociations()
    {
        var (population, _, _, _) = await AcquireEnglishAsync(_ => null);
        Assert.HasCount(1, population.Acquisitions);
        var acquisition = population.Acquisitions[0];
        Assert.IsNotNull(acquisition.CheckpointRef);
        Assert.IsTrue(population.Reconciliation!.Outcomes.Any(outcome => ReferenceEquals(outcome, acquisition.Outcome)));
        var copied = new[] { acquisition };
        var attached = population.WithAcquisitions(copied);
        copied[0] = null!;
        Assert.AreSame(acquisition, attached.Acquisitions[0]);
        Assert.ThrowsExactly<ArgumentException>(() => population.WithAcquisitions([acquisition, acquisition]));
        var foreign = new EuFormexPackageAcquisitionResult(EuFormexPackageOutcome.NotAcquired(acquisition.Outcome.Expression,
            EuFormexPackageNotAcquiredReason.BodyNotHeld, "other result"), 0);
        Assert.ThrowsExactly<ArgumentException>(() => population.WithAcquisitions([foreign]));
    }

    [TestMethod]
    public async Task ProvenAnnexTransportAndReplayKeepMissingWorkCelexAbsent()
    {
        var capture = await CapturePackageAsync(2, missingCelex: true);
        Assert.IsNotNull(capture.Result.AnnexClassification, capture.Result.Outcome.Detail);
        Assert.IsNull(capture.Result.AnnexClassification.Binding.WorkCelex);
        var copy = await CopyPackageStoreAsync(capture.Store);
        var first = await RestorePackageAsync(copy, capture);
        var second = await RestorePackageAsync(copy, capture);
        Assert.IsNotNull(first.AnnexClassification, first.Outcome.Detail);
        Assert.IsNull(first.AnnexClassification.Binding.WorkCelex);
        Assert.AreEqual(capture.Result.AnnexClassification.IdentitySha256, first.AnnexClassification.IdentitySha256);
        Assert.AreEqual(first.AnnexClassification.IdentitySha256, second.AnnexClassification?.IdentitySha256);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFormexPackageAcquisitionProducer.ReopenAsync(
            copy, capture.Result.CheckpointRef!, capture.Enumeration, capture.Corpus, WorkCelex,
            capture.Renderer, CancellationToken.None));
    }

    private sealed record PackageCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        EuFormexManifestationEnumerationResult Enumeration, VerifiedCorpusRecordSet? Corpus,
        EuFormexPackageAcquisitionResult Result, FormexEnumerationHandler Handler, MachineQueryRendererSource Renderer, string? Celex);
    private static Task<EuFormexPackageAcquisitionResult> RestorePackageAsync(ICustodyStore store, PackageCapture capture) =>
        EuFormexPackageAcquisitionProducer.ReopenAsync(store, capture.Result.CheckpointRef!, capture.Enumeration,
            capture.Corpus, capture.Celex, capture.Renderer, CancellationToken.None);
    private static async Task<JsonNode> PackageRootAsync(ICustodyStore store, SourceArtifactRef reference) =>
        JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(reference.Sha256, CancellationToken.None)).Span))!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyPackageStoreAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
    private static async Task<PackageCapture> CapturePackageAsync(int shape, bool failCheckpoint = false, bool missingCelex = false)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: failCheckpoint ? "lex-eu-formex-package-checkpoint/2" : null);
        var annex = shape is 2 or 5 or 7;
        var (run, english, french) = await RunWithTwoExpressionsAsync(
            annex && shape != 7 ? await FixtureAsync("new-xhtml-200-body.bin") : null, store);
        var package = annex ? await FixtureAsync("new-fmx4-200-body.bin") : null;
        var handler = new FormexEnumerationHandler(new Dictionary<string,string[]>(StringComparer.Ordinal)
        { [english.Identity.PublisherExpressionId] = annex && shape != 5 ? ["fmx4", "pdfa2a"] : ["fmx4"] }, [english, french], request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)) return null;
            if (shape == 3) return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, []);
            if (shape == 4) return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, "not a ZIP"u8.ToArray(), "application/zip");
            return package is null ? null : EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, package, "application/zip");
        });
        var renderer = RendererSource();
        var enumeration = await new EuFormexManifestationEnumerationProducer(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler)
            .RunAsync(new EuFormexManifestationRunRequest(EuFormexManifestationDiscoveryPlan.Create(), english,
                "urn:uuid:00000000-0000-4000-8000-000000000085", renderer, EuAcquisitionTestFixture.TestWireBudget()),
                EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);
        Assert.IsTrue(enumeration.IsFormexEligible, enumeration.Detail);
        var corpus = shape == 0 ? null : run.CorpusRecordSet;
        var result = await new EuFormexPackageAcquisitionProducer(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler)
            .RunAsync(enumeration, corpus, missingCelex ? null : WorkCelex, renderer, shape == 6 ? WireRequestBudget.OfWireRequests(2) : EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (!failCheckpoint)
        {
            Assert.IsNotNull(result.CheckpointRef, result.Outcome.Detail);
            if (shape == 2) Assert.IsNotNull(result.AnnexClassification, result.Outcome.Detail);
        }
        return new(store, enumeration, corpus, result, handler, renderer, missingCelex ? null : WorkCelex);
    }
}
