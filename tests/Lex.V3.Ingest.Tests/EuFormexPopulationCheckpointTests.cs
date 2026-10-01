using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuFormexPackagePopulationProducerTests
{
    private static readonly Lazy<Task<PopulationCapture>> PopulationAnnexCapture = new(() => CapturePopulationAsync(2));

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(2, true)]
    public async Task PopulationReplayRepeatsAllTypedOutcomesWithoutNewArtifactsOrRequests(int shape, bool weaker)
    {
        var capture = shape == 2 ? await PopulationAnnexCapture.Value : await CapturePopulationAsync(shape);
        var copy = await CopyPackageStoreAsync(capture.Store, weaker: weaker);
        var digests = copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
        var sends = PopulationSends(capture.Handler);
        var first = await RestorePopulationAsync(copy, capture);
        var second = await RestorePopulationAsync(copy, capture);
        Assert.IsTrue(first.Delivered);
        Assert.AreSame(capture.Run, first.Reconciliation!.Run);
        Assert.AreEqual(capture.Result.CreateOutcomeDiagnosticsJson(), first.CreateOutcomeDiagnosticsJson());
        Assert.AreEqual(first.CreateOutcomeDiagnosticsJson(), second.CreateOutcomeDiagnosticsJson());
        Assert.AreEqual(capture.Result.CheckpointRef, first.CheckpointRef);
        Assert.AreEqual(0, first.ProductRequestCount);
        CollectionAssert.AreEqual(digests, copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray(),
            "Receipt refresh may repeat holds, but replay must introduce no new artifact identities.");
        Assert.AreEqual(sends, PopulationSends(capture.Handler));
        CollectionAssert.AreEqual(capture.Result.AnnexClassifications.Select(value => value.IdentitySha256).ToArray(),
            first.AnnexClassifications.Select(value => value.IdentitySha256).ToArray());
        for (var i = 0; i < first.Reconciliation!.Outcomes.Count; i++)
            Assert.AreSame(capture.Result.Reconciliation!.Outcomes[i].Expression, first.Reconciliation.Outcomes[i].Expression);
        Assert.AreEqual(capture.Result.Acquisitions.Count, first.Acquisitions.Count);
        if (weaker) Assert.IsTrue(first.Enumerations.All(value => value.Proof!.RetainedFloor == CustodyMembership.RetainedUnenforced),
            "Restored enumeration floors must describe the copied store, not the original receipts.");
        if (shape == 3) Assert.AreEqual(1, first.NotEnumeratedExpressionCount);
        if (shape == 2) Assert.HasCount(1, first.AnnexClassifications);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("enumeration")]
    [DataRow("package")]
    [DataRow("zip")]
    public async Task PopulationReplayRequiresNestedOriginalEvidence(string missing)
    {
        var capture = await PopulationAnnexCapture.Value;
        var root = await PackageRootAsync(capture.Store, capture.Result.CheckpointRef!);
        var digest = missing switch
        {
            "root" => capture.Result.CheckpointRef!.Sha256,
            "enumeration" => root["enumerations"]![0]!["checkpoint"]!["sha256"]!.GetValue<string>(),
            "package" => root["packages"]![0]!["checkpoint"]!["sha256"]!.GetValue<string>(),
            _ => capture.Result.Acquisitions[0].Outcome.AcquiredInventory!.SourceReceipt.Reference.ContentSha256,
        };
        var copy = await CopyPackageStoreAsync(capture.Store, omit: digest);
        try { _ = await RestorePopulationAsync(copy, capture); Assert.Fail("Missing acquisition evidence must refuse."); }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException) { }
    }

    [TestMethod]
    [DataRow("run")]
    [DataRow("manifestation_renderer")]
    [DataRow("document_renderer")]
    [DataRow("celex")]
    public async Task PopulationReplayBindsOriginalCallerInputs(string changed)
    {
        var capture = await PopulationAnnexCapture.Value;
        var otherRun = changed == "run" ? (await RunWithTwoExpressionsAsync()).Run : capture.Run;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFormexPackagePopulationProducer.ReopenAsync(
            capture.Store, capture.Result.CheckpointRef!, otherRun,
            changed == "manifestation_renderer" ? RendererSource() : capture.ManifestationRenderer,
            changed == "document_renderer" ? RendererSource() : capture.DocumentRenderer,
            changed == "celex" ? null : capture.Celex, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("input")]
    [DataRow("result")]
    [DataRow("family")]
    [DataRow("expression")]
    [DataRow("run")]
    [DataRow("profile")]
    [DataRow("null_enumerations")]
    [DataRow("null_packages")]
    [DataRow("duplicate_enumeration")]
    [DataRow("missing_enumeration")]
    [DataRow("reordered_enumerations")]
    [DataRow("extra_package")]
    [DataRow("missing_package")]
    [DataRow("package_expression")]
    public async Task PopulationReplayRejectsRehashedAssociationChanges(string changed)
    {
        var capture = await PopulationAnnexCapture.Value;
        var copy = await CopyPackageStoreAsync(capture.Store);
        var root = await PackageRootAsync(copy, capture.Result.CheckpointRef!);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-eu-formex-population-checkpoint/99"; break;
            case "input": root["input_sha256"] = new string('a', 64); break;
            case "result": root["result_sha256"] = new string('a', 64); break;
            case "family": root["enumerations"]![0]!["family"] = "foreign"; break;
            case "expression": root["enumerations"]![0]!["expression_sha256"] = new string('a', 64); break;
            case "run": root["enumerations"]![0]!["run"]!["sha256"] = new string('a', 64); break;
            case "profile": root["enumerations"]![0]!["profile"]!["sha256"] = new string('a', 64); break;
            case "null_enumerations": root["enumerations"] = null; break;
            case "null_packages": root["packages"] = null; break;
            case "duplicate_enumeration": root["enumerations"]!.AsArray().Add(root["enumerations"]![0]!.DeepClone()); break;
            case "missing_enumeration": root["enumerations"]!.AsArray().RemoveAt(0); break;
            case "reordered_enumerations":
                var entries = root["enumerations"]!.AsArray();
                var first = entries[0]!.DeepClone(); entries[0] = entries[1]!.DeepClone(); entries[1] = first; break;
            case "extra_package": root["packages"]!.AsArray().Add(root["packages"]![0]!.DeepClone()); break;
            case "missing_package": root["packages"]!.AsArray().Clear(); break;
            case "package_expression": root["packages"]![0]!["expression_sha256"] = new string('a', 64); break;
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        var held = await copy.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuFormexPackagePopulationProducer.ReopenAsync(
            copy, new SourceArtifactRef(capture.Result.CheckpointRef!.ResourceId, held.Reference.ContentSha256),
            capture.Run, capture.ManifestationRenderer, capture.DocumentRenderer, capture.Celex, CancellationToken.None));
    }

    [TestMethod]
    public async Task PopulationCheckpointCancellationPropagates()
    {
        var capture = await PopulationAnnexCapture.Value;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuFormexPackagePopulationProducer.ReopenAsync(
            capture.Store, capture.Result.CheckpointRef!, capture.Run, capture.ManifestationRenderer,
            capture.DocumentRenderer, capture.Celex, source.Token));
    }

    [TestMethod]
    [DataRow("lex-eu-formex-population-checkpoint/1")]
    [DataRow("lex-eu-formex-package-checkpoint/1")]
    public async Task PopulationCannotDeliverWithoutItsCompleteCheckpointClosure(string failSchema)
    {
        var capture = await CapturePopulationAsync(1, failSchema);
        Assert.IsFalse(capture.Result.Delivered);
        Assert.AreEqual(EuFormexPackagePopulationRefusal.CheckpointNotRetained, capture.Result.Refusal);
        Assert.IsNull(capture.Result.CheckpointRef);
    }

    private sealed record PopulationCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        EuQueryExecutionResult Run, EuFormexPackagePopulationResult Result, FormexEnumerationHandler Handler,
        MachineQueryRendererSource ManifestationRenderer, MachineQueryRendererSource DocumentRenderer, string? Celex);
    private static int PopulationSends(FormexEnumerationHandler handler) => handler.Enumerations.Count +
        handler.PackageRequests.Count + handler.PdfRequests.Count + handler.RobotsSends + handler.OtherRequests.Count;
    private static Task<EuFormexPackagePopulationResult> RestorePopulationAsync(ICustodyStore store, PopulationCapture capture) =>
        EuFormexPackagePopulationProducer.ReopenAsync(store, capture.Result.CheckpointRef!, capture.Run,
            capture.ManifestationRenderer, capture.DocumentRenderer, capture.Celex, CancellationToken.None);
    private static async Task<PopulationCapture> CapturePopulationAsync(int shape, string? failSchema = null)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: failSchema);
        var (run, english, other) = await RunWithTwoExpressionsAsync(
            shape == 2 ? await FixtureAsync("new-xhtml-200-body.bin") : null, store,
            shape == 3 ? "http://publications.europa.eu/resource/authority/language/DEU" : FrenchAuthority);
        var zip = shape == 2 ? await FixtureAsync("new-fmx4-200-body.bin") : null;
        var handler = new FormexEnumerationHandler(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [english.Identity.PublisherExpressionId] = shape == 0 ? ["xhtml"] : shape == 2 ? ["fmx4", "pdfa2a"] : ["fmx4"],
            [other.Identity.PublisherExpressionId] = ["xhtml"],
        }, [english, other], request => zip is not null && request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
            ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, zip, "application/zip") : null);
        var manifestation = RendererSource(); var document = RendererSource();
        var celex = shape == 4 ? null : WorkCelex;
        var result = celex is null
            ? await Producer(store, handler).RunAsync(run, manifestation, document, EuAcquisitionTestFixture.SourceWitness(),
                EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None)
            : await Producer(store, handler).RunAsync(run, manifestation, document, celex, EuAcquisitionTestFixture.SourceWitness(),
                EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (failSchema is null) { Assert.IsTrue(result.Delivered, result.Detail); Assert.IsNotNull(result.CheckpointRef); }
        return new(store, run, result, handler, manifestation, document, celex);
    }
}
