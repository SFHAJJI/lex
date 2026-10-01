using DocumentAcquisitionData = (System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.CorpusAcquisitionOutcome>? Outcomes, System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.Europe.EuDocumentLadderResult>? LadderResults, System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.Europe.EuMintedRowAccounting>? MintedRows, Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail? Refusal);
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuQueryExecutionAdapterTests
{
    private static readonly Lazy<Task<LadderCapture>> CapturedLadder = new(() => CaptureLadderAsync(5));

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(5, false)]
    [DataRow(0, true)]
    public async Task DocumentLadderReplayPreservesTypedOutcomesAndCurrentBodyCustody(int shape, bool weaker)
    {
        var capture = shape == 5 ? await CapturedLadder.Value : await CaptureLadderAsync(shape);
        var copy = await CopyLadderStoreAsync(capture.Store, weaker: weaker);
        var digests = copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
        var sends = capture.Handler.Sends;
        var first = await RestoreLadderAsync(copy, capture);
        var second = await RestoreLadderAsync(copy, capture);
        Assert.IsNull(first.Refusal);
        Assert.AreEqual(LadderSummary(capture.Result), LadderSummary(first));
        Assert.AreEqual(LadderSummary(first), LadderSummary(second));
        CollectionAssert.AreEqual(digests, copy.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(sends, capture.Handler.Sends);
        if (weaker) Assert.AreEqual(CustodyMembership.RetainedUnenforced,
            CorpusBodyRecord.Held(first.Outcomes!.Values.Single().Receipt!).Floor);
        if (shape == 5) Assert.HasCount(2, first.LadderResults!.Values.Single().Attempted);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("route")]
    [DataRow("body")]
    public async Task DocumentLadderReplayRefusesMissingEvidence(string missing)
    {
        var capture = await CapturedLadder.Value;
        var root = await LadderRootAsync(capture.Store, capture.Checkpoint!);
        var digest = missing switch
        {
            "root" => capture.Checkpoint!.Sha256,
            "route" => root["fetches"]![0]!["route"]!["sha256"]!.GetValue<string>(),
            _ => capture.Result.Outcomes!.Values.Single().Receipt!.Reference.ContentSha256,
        };
        var copy = await CopyLadderStoreAsync(capture.Store, omit: digest);
        try { _ = await RestoreLadderAsync(copy, capture); Assert.Fail("Missing evidence must refuse."); }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException) { }
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("input")]
    [DataRow("result")]
    [DataRow("ordinal")]
    [DataRow("uri")]
    [DataRow("accept")]
    [DataRow("run")]
    [DataRow("null_fetches")]
    [DataRow("missing_fetch")]
    [DataRow("extra_fetch")]
    [DataRow("reordered")]
    public async Task RehashedDocumentLaddersCannotChangeSelectionOrAttemptOrder(string changed)
    {
        var capture = await CapturedLadder.Value;
        var copy = await CopyLadderStoreAsync(capture.Store);
        var root = await LadderRootAsync(copy, capture.Checkpoint!);
        switch (changed)
        {
            case "schema": root["schema"] = "lex-eu-document-ladders-checkpoint/99"; break;
            case "input": root["input_sha256"] = new string('a', 64); break;
            case "result": root["result_sha256"] = new string('a', 64); break;
            case "ordinal": root["fetches"]![0]!["ordinal"] = 1; break;
            case "uri": root["fetches"]![0]!["uri"] = "https://publications.europa.eu/resource/celex/32003L0088"; break;
            case "accept": root["fetches"]![0]!["accept"] = "text/html"; break;
            case "run": root["fetches"]![0]!["run"]!["sha256"] = new string('a', 64); break;
            case "null_fetches": root["fetches"] = null; break;
            case "missing_fetch": root["fetches"]!.AsArray().RemoveAt(0); break;
            case "extra_fetch": root["fetches"]!.AsArray().Add(root["fetches"]![0]!.DeepClone()); break;
            case "reordered":
                var fetches = root["fetches"]!.AsArray();
                var first = fetches[0]!.DeepClone(); fetches[0] = fetches[1]!.DeepClone(); fetches[1] = first; break;
        }
        var held = await copy.CreateAsync(Encoding.UTF8.GetBytes(root.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuQueryExecutionAdapter.ReopenDocumentAcquisitionAsync(
            copy, new SourceArtifactRef(capture.Checkpoint!.ResourceId, held.Reference.ContentSha256), capture.Manifest,
            capture.Addresses, capture.Renderer, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("renderer")]
    [DataRow("addresses")]
    [DataRow("manifest")]
    public async Task DocumentLadderReplayRequiresTheOriginalCheckedInputs(string changed)
    {
        var capture = await CapturedLadder.Value;
        var other = changed == "manifest" ? await CaptureLadderAsync(0, seedOrdinal: 1) : capture;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuQueryExecutionAdapter.ReopenDocumentAcquisitionAsync(
            capture.Store, capture.Checkpoint!, other.Manifest,
            changed == "addresses" ? new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>() : capture.Addresses,
            changed == "renderer" ? EuAcquisitionTestFixture.BuildRendererSource(997) : capture.Renderer, CancellationToken.None));
    }

    [TestMethod]
    public async Task DocumentLadderCheckpointCancellationPropagates()
    {
        var capture = await CapturedLadder.Value;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuQueryExecutionAdapter.ReopenDocumentAcquisitionAsync(
            capture.Store, capture.Checkpoint!, capture.Manifest, capture.Addresses, capture.Renderer, source.Token));
    }

    [TestMethod]
    public async Task DocumentLadderCheckpointHoldFailureHasItsOwnTypedRefusal()
    {
        var capture = await CaptureLadderAsync(0, failCheckpoint: true);
        Assert.AreEqual(EuQueryExecutionRefusal.DocumentCheckpointNotRetained, capture.Result.Refusal?.Code);
        Assert.IsNull(capture.Checkpoint);
    }

    private static string LadderSummary(DocumentAcquisitionData result) => ContractJson.Serialize(new
    {
        Outcomes = result.Outcomes!.OrderBy(pair => pair.Key).Select(pair => new
        { pair.Key, pair.Value.Refusal, Body = pair.Value.Receipt?.Reference.ContentSha256 }).ToArray(),
        Ladders = result.LadderResults!.OrderBy(pair => pair.Key).ToArray(),
        Minted = result.MintedRows!.OrderBy(pair => pair.Key).ToArray(),
    });
    private sealed record LadderCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, ScopeManifest Manifest,
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> Addresses, MachineQueryRendererSource Renderer,
        DocumentAcquisitionData Result, SourceArtifactRef? Checkpoint, RecordingLadderHandler Handler);
    private sealed class RecordingLadderHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal int Sends { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Sends++; return base.SendAsync(request, cancellationToken); }
    }
    private static Task<DocumentAcquisitionData> RestoreLadderAsync(ICustodyStore store, LadderCapture capture) =>
        EuQueryExecutionAdapter.ReopenDocumentAcquisitionAsync(store, capture.Checkpoint!, capture.Manifest, capture.Addresses,
            capture.Renderer, CancellationToken.None);
    private static async Task<JsonNode> LadderRootAsync(ICustodyStore store, SourceArtifactRef reference) =>
        JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(reference.Sha256, CancellationToken.None)).Span))!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyLadderStoreAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
    private static async Task<LadderCapture> CaptureLadderAsync(int shape, bool failCheckpoint = false, int seedOrdinal = 0)
    {
        var root = EuPackRootCanonicalForm.TryCanonicalize(EuAppendixASeedMap.SeedsInCelexOrder[seedOrdinal].WorkRoot, out _)!;
        var profile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(root, profile);
        var manifest = ScopeReducer.Reduce(profile, [CompleteEnumerationRef], [input.ObjectRef], [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var ladder = new List<EuDocumentFetchAddress> { address };
        if (shape == 5) ladder.Add(EuDocumentFetchAddress.TryCreate("cellar", ExtractCellarKeyForTest(root),
            EuManifestationMediaType.TextHtml, EuDocumentLanguage.Eng, out _)!);
        var addresses = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> { [input.ObjectRef] = ladder };
        var calls = 0;
        byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", name));
        HttpResponseMessage Respond(HttpRequestMessage request)
        {
            calls++;
            if (shape == 1 || (shape == 5 && calls == 1)) return EuAcquisitionTestFixture.BinaryResponse(request,
                HttpStatusCode.NotFound, Fixture("gdpr-pdfa2a-404-body.bin"));
            if (shape == 2) return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.BadRequest, Fixture("gdpr-wrong-token-400-body.bin"));
            if (shape == 3) return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.ServiceUnavailable, []);
            return EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, Fixture("gdpr-xhtml-200-body.bin"),
                shape == 5 ? "text/html" : "application/xhtml+xml");
        }
        HttpMessageHandler inner = shape == 4 ? new DocumentFetchRobotsDenyingHandler() :
            new EuAcquisitionTestFixture.ClassifyingHandler(new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(), Respond);
        var handler = new RecordingLadderHandler(inner);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: failCheckpoint ? "lex-eu-document-ladders-checkpoint/1" : null);
        var renderer = EuAcquisitionTestFixture.BuildRendererSource(996);
        var adapter = new EuQueryExecutionAdapter(store, new EuRepeatedEnumerationExecutor(store,
            new EuAcquisitionTestFixture.FixedTimeProvider(), handler));
        var result = await adapter.RunDocumentAcquisitionWithCheckpointAsync(manifest, addresses, renderer,
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (!failCheckpoint) { Assert.IsNull(result.Data.Refusal, result.Data.Refusal?.Detail); Assert.IsNotNull(result.Checkpoint); }
        return new(store, manifest, addresses, renderer, result.Data, result.Checkpoint, handler);
    }
}
