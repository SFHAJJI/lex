using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgGazetteAcquisitionTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    public async Task GazetteReplaysCopiedCustodyWithOriginalRoutesAndDispositions(int shape, bool weaker)
    {
        var capture = await CaptureGazetteAsync(shape);
        Assert.AreEqual(shape is 2 or 3 or 4 ? 1 : 2, capture.Handler.Documents);
        foreach (var iteration in Enumerable.Range(0, 2))
        {
            var store = await CopyGazetteAsync(capture.Store, weaker: weaker);
            var digests = store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray();
            var result = await LuxembourgQueryExecutionAdapter.ReopenGazetteAcquisitionAsync(store, capture.Checkpoint,
                capture.Profile, capture.Resolved, capture.Manifest, capture.Addresses, capture.Held, capture.Renderer, CancellationToken.None);
            Assert.IsNull(result.Refusal);
            Assert.AreEqual(capture.ResultJson, ContractJson.Serialize(new { result.Sets, result.FetchRefusals, result.ContradictoryLegalValues }));
            CollectionAssert.AreEqual(digests, store.WrittenDigestsInOrder.Distinct().Order(StringComparer.Ordinal).ToArray());
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("route")]
    [DataRow("body")]
    [DataRow("reused_receipt")]
    public async Task GazetteMissingCustodyRefuses(string missing)
    {
        var capture = await CaptureGazetteAsync(0);
        var root = await GazetteRootAsync(capture);
        var digest = missing switch
        {
            "root" => capture.Checkpoint.Sha256,
            "route" => root["fetches"]![0]!["route"]!["sha256"]!.GetValue<string>(),
            "body" => Sha256(PdfBytes),
            _ => capture.Held.Single().Value.Hops[^1].DurableWriteReceiptSha256,
        };
        var store = await CopyGazetteAsync(capture.Store, digest);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenGazetteAsync(store, capture));
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("input")]
    [DataRow("result")]
    [DataRow("renderer")]
    [DataRow("ordinal")]
    [DataRow("address")]
    [DataRow("plan")]
    [DataRow("run")]
    [DataRow("missing_fetch")]
    [DataRow("extra_fetch")]
    public async Task GazetteRehashedCheckpointCannotSubstituteInputsOrRoutes(string changed)
    {
        var capture = await CaptureGazetteAsync(0);
        var root = await GazetteRootAsync(capture);
        var fetch = root["fetches"]![0]!;
        switch (changed)
        {
            case "schema": root["schema"] = "foreign/1"; break;
            case "input": root["input_sha256"] = new string('a', 64); break;
            case "result": root["result_sha256"] = new string('a', 64); break;
            case "renderer": root["renderer"]!["resource_id"] = NewUrn(); break;
            case "ordinal": fetch["ordinal"] = 99; break;
            case "address": fetch["address"]!["sha256"] = new string('a', 64); break;
            case "plan": fetch["plan_id"] = NewUrn(); break;
            case "run": fetch["run"]!["resource_id"] = NewUrn(); break;
            case "missing_fetch": root["fetches"]!.AsArray().Clear(); break;
            default: root["fetches"]!.AsArray().Add(fetch.DeepClone()); break;
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        await capture.Store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenGazetteAsync(capture.Store,
            capture with { Checkpoint = new SourceArtifactRef(NewUrn(), CustodyDigest.Of(bytes)) }));
    }

    [TestMethod]
    public async Task GazetteRejectsChangedReuseMapBeforeWrites()
    {
        var capture = await CaptureGazetteAsync(0);
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenGazetteAsync(capture.Store,
            capture with { Held = new Dictionary<int, RoutedHttpEvidence>() }));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task GazetteRejectsNullRootBeforeWrites()
    {
        var capture = await CaptureGazetteAsync(0);
        var receipt = await capture.Store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var writes = capture.Store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => ReopenGazetteAsync(capture.Store,
            capture with { Checkpoint = new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256) }));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    [TestMethod]
    public async Task GazetteCheckpointHoldFailureIsTyped()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failSchema: "lex-lu-gazette-checkpoint/1");
        await CaptureGazetteAsync(0, store, expectHoldFailure: true);
    }

    [TestMethod]
    public async Task GazetteCancellationCreatesNoWrites()
    {
        var capture = await CaptureGazetteAsync(0);
        var writes = capture.Store.CreateCallCount;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgQueryExecutionAdapter.ReopenGazetteAcquisitionAsync(
            capture.Store, capture.Checkpoint, capture.Profile, capture.Resolved, capture.Manifest,
            capture.Addresses, capture.Held, capture.Renderer, source.Token));
        Assert.AreEqual(writes, capture.Store.CreateCallCount);
    }

    private sealed record GazetteCapture(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        SourceArtifactRef Checkpoint, VerifiedLuxembourgSourceProfile Profile, LuxembourgProfileResolution.Resolved Resolved,
        ScopeManifest Manifest, IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> Addresses,
        IReadOnlyDictionary<int, RoutedHttpEvidence> Held, MachineQueryRendererSource Renderer,
        GazetteCheckpointHandler Handler, string ResultJson);

    private static async Task<GazetteCapture> CaptureGazetteAsync(int shape,
        EuAcquisitionTestFixture.EuInMemoryCustodyStore? suppliedStore = null, bool expectHoldFailure = false)
    {
        var store = suppliedStore ?? new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var evidence = new SourceArtifactRef(NewUrn(), new string('1', 64));
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(evidence, evidence,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        var objectRef = new SourceObjectRef(SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Jolux,
            new SourceRegistryMemberRef(evidence, "legal_resource"), Act, Act, Sha256(Encoding.UTF8.GetBytes(Act)), evidence, null);
        var assertions = GazetteAssertions(shape == 2 ? LicenceScl : null,
            shape == 3 ? [LegalValues + "officiel", LegalValues + "definitif"] : null)
            .Select(value => new LuxembourgObservedAssertion(value.Item1, value.Item2, LuxembourgAssertionObjectKind.Iri,
                value.Item3, string.Empty, string.Empty, evidence)).ToArray();
        var rights = new LuxembourgRightsChannelObservation(ManifestationPdfA, evidence, evidence, [CcBy]);
        var observation = new LuxembourgResourceObservation(objectRef, evidence, assertions, [],
            new LuxembourgSparqlRightsChannelObservations(evidence, evidence, [rights]),
            new LuxembourgInFileRightsChannelObservations(evidence, evidence, [rights]));
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(AbsenceFixtures.Proof(), [observation])));
        var address = LuxembourgDocumentFetchAddress.Create(LuxembourgFileUri.RequireValid(ItemPdfA),
            LuxembourgAuthorityIri.TryParseUserFormat(Formats + "pdfa")!.Value, LuxembourgLegalValue.Officiel, new Uri(Act).AbsolutePath);
        var addresses = new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> { [objectRef] = address };
        var manifest = profile.ReduceScope(resolved, new GazetteScopeEvidence(evidence),
            new Dictionary<SourceObjectRef, ScopeManifestFetchAddress> { [objectRef] = address.ToScopeManifestFetchAddress() }).Manifest;
        var renderer = LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(420);
        var handler = new GazetteCheckpointHandler(shape);
        var adapter = new LuxembourgQueryExecutionAdapter(store,
            new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler), profile);
        var documents = await adapter.RunDocumentAcquisitionWithCheckpointAsync(manifest, addresses, renderer,
            LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(documents.Data.Refusal, documents.Data.Refusal?.Detail);
        Assert.HasCount(1, documents.Data.HeldEvidenceByOrdinal!);
        var result = await adapter.RunGazetteAcquisitionWithCheckpointAsync(resolved, manifest, addresses,
            documents.Data.HeldEvidenceByOrdinal!, renderer, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        if (expectHoldFailure)
        {
            Assert.IsNull(result.Checkpoint);
            Assert.AreEqual(LuxembourgQueryExecutionRefusal.GazetteCheckpointNotRetained, result.Data.Refusal!.Code);
        }
        else
        {
            Assert.IsNull(result.Data.Refusal, result.Data.Refusal?.Detail);
            Assert.IsNotNull(result.Checkpoint);
        }
        return new(store, result.Checkpoint!, profile, resolved, manifest, addresses,
            documents.Data.HeldEvidenceByOrdinal!, renderer, handler,
            ContractJson.Serialize(new { result.Data.Sets, result.Data.FetchRefusals, result.Data.ContradictoryLegalValues }));
    }

    private static async Task ReopenGazetteAsync(ICustodyStore store, GazetteCapture capture) =>
        _ = await LuxembourgQueryExecutionAdapter.ReopenGazetteAcquisitionAsync(store, capture.Checkpoint,
            capture.Profile, capture.Resolved, capture.Manifest, capture.Addresses, capture.Held, capture.Renderer, CancellationToken.None);
    private static async Task<JsonNode> GazetteRootAsync(GazetteCapture capture) =>
        JsonNode.Parse((await capture.Store.ReadByDigestAsync(capture.Checkpoint.Sha256, CancellationToken.None)).Span)!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyGazetteAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(value => value != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }

    private sealed class GazetteCheckpointHandler(int shape) : HttpMessageHandler
    {
        internal int Documents { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath == "/robots.txt")
                return Task.FromResult(Response(request, Encoding.UTF8.GetBytes(shape == 4 && Documents == 1
                    ? "User-agent: *\nDisallow: /filestore/\n" : "User-agent: *\nAllow: /\n"), "text/plain"));
            Documents++;
            var pdfa = request.RequestUri.AbsoluteUri.Contains("/pdfa/", StringComparison.Ordinal);
            Assert.IsTrue(pdfa || shape is 0 or 1, "Withheld/contradictory/robots-denied listing must not be fetched.");
            Assert.AreEqual(pdfa ? 1 : 2, Documents);
            return Task.FromResult(Response(request, pdfa ? PdfABytes : PdfBytes, "application/pdf",
                !pdfa && shape == 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        }
    }

    // This fixture exercises acquisition replay; evidence admission is covered by the resolver tests.
    private sealed class GazetteScopeEvidence(SourceArtifactRef enumeration) : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef => enumeration;
        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) => true;
        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) => true;
        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) => true;
        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) => binding.CompleteEnumerationRef == enumeration;
    }
}
