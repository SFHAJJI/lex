using System.Net;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuDocumentFetchRouteReaderTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(1, true)]
    public async Task CopiedRoutesReopenTwiceWithOriginalOutcomesAndNoTraffic(int shape, bool unenforced)
    {
        var (store, route, address, handler) = await AcquireAsync(shape);
        var copy = await CopyAsync(store, unenforced: unenforced);
        var writes = copy.CreateCallCount;
        var requests = handler.RequestCount;
        var first = await ReopenAsync(copy, route, address);
        var second = await ReopenAsync(copy, route, address);
        CollectionAssert.AreEqual(route.CopyCanonicalBytes(), first.Evidence!.CopyCanonicalBytes());
        CollectionAssert.AreEqual(first.Evidence.CopyCanonicalBytes(), second.Evidence!.CopyCanonicalBytes());
        Assert.AreEqual(route.Hops.Count, first.HopWriteReceiptsByObservationId!.Count);
        Assert.AreEqual(route.Outcome.GetType(), first.Evidence.Outcome.GetType());
        Assert.AreEqual(writes, copy.CreateCallCount);
        Assert.AreEqual(requests, handler.RequestCount);
        Assert.IsNull(first.Refusal);
    }

    [TestMethod]
    [DataRow("route")]
    [DataRow("first_request")]
    [DataRow("terminal_request")]
    [DataRow("body")]
    [DataRow("receipt")]
    [DataRow("request_policy")]
    [DataRow("redirect_policy")]
    public async Task EveryTransportDependencyMustStillExist(string missing)
    {
        var (store, route, address, _) = await AcquireAsync(1);
        var request = HttpLogicalRequest.ParseAndVerify((await store.ReadByDigestAsync(route.Hops[0].LogicalRequestSha256, CancellationToken.None)).Span);
        var digest = missing switch
        {
            "route" => CustodyDigest.Of(route.CopyCanonicalBytes()),
            "first_request" => route.Hops[0].LogicalRequestSha256,
            "terminal_request" => route.Hops[^1].LogicalRequestSha256,
            "body" => route.Hops[^1].Sha256,
            "receipt" => route.Hops[^1].DurableWriteReceiptSha256,
            "request_policy" => request.RequestPolicySha256,
            _ => request.RedirectPolicySha256,
        };
        var copy = await CopyAsync(store, omit: digest);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => ReopenAsync(copy, route, address));
    }

    [TestMethod]
    [DataRow("run")]
    [DataRow("request")]
    [DataRow("address")]
    [DataRow("media")]
    [DataRow("language")]
    public async Task CallerPinsCannotSelectAnotherRunRequestOrRepresentation(string changed)
    {
        var (store, route, address, _) = await AcquireAsync(0);
        var other = Address(changed == "address" ? "32003L0088" : "32016R0679",
            changed == "media" ? EuManifestationMediaType.TextHtml : EuManifestationMediaType.XhtmlXml,
            changed == "language" ? EuDocumentLanguage.Fra : EuDocumentLanguage.Eng);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuDocumentFetchRouteReader.ReopenAsync(
            store, Reference(route), changed == "run" ? new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000099", new string('a',64)) : route.RunIdentity,
            changed == "request" ? new string('a',64) : route.Hops[0].LogicalRequestSha256, other, CancellationToken.None));
    }

    [TestMethod]
    public async Task RehashedUnrelatedReceiptCannotStandInForTheTerminalWrite()
    {
        var (store, route, address, _) = await AcquireAsync(1);
        var unrelated = await store.CreateAsync("unrelated"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var receipt = Encoding.UTF8.GetBytes(ContractJson.Serialize(unrelated));
        await store.CreateAsync(receipt, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var original = Encoding.UTF8.GetString(route.CopyCanonicalBytes());
        var replacement = original.Replace(route.Hops[^1].DurableWriteReceiptSha256, CustodyDigest.Of(receipt), StringComparison.Ordinal);
        Assert.AreNotEqual(original, replacement);
        var bytes = Encoding.UTF8.GetBytes(replacement);
        _ = RoutedHttpEvidence.ParseAndVerify(bytes); // The receipt gate, not framing, must reject it.
        await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => EuDocumentFetchRouteReader.ReopenAsync(store,
            new SourceArtifactRef(Reference(route).ResourceId, CustodyDigest.Of(bytes)), route.RunIdentity,
            route.Hops[0].LogicalRequestSha256, address, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancellationDoesNotOpenATransport()
    {
        var (store, route, address, handler) = await AcquireAsync(0);
        var requests = handler.RequestCount;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuDocumentFetchRouteReader.ReopenAsync(
            store, Reference(route), route.RunIdentity, route.Hops[0].LogicalRequestSha256, address, source.Token));
        Assert.AreEqual(requests, handler.RequestCount);
    }

    private static SourceArtifactRef Reference(RoutedHttpEvidence route) =>
        new("urn:uuid:00000000-0000-4000-8000-000000000098", CustodyDigest.Of(route.CopyCanonicalBytes()));
    private static Task<EuDocumentFetchAttemptResult> ReopenAsync(ICustodyStore store, RoutedHttpEvidence route, EuDocumentFetchAddress address) =>
        EuDocumentFetchRouteReader.ReopenAsync(store, Reference(route), route.RunIdentity, route.Hops[0].LogicalRequestSha256, address, CancellationToken.None);
    private static EuDocumentFetchAddress Address(string celex = "32016R0679", EuManifestationMediaType media = EuManifestationMediaType.XhtmlXml,
        EuDocumentLanguage language = EuDocumentLanguage.Eng) => EuDocumentFetchAddress.TryCreate("celex", celex, media, language, out _)!;
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore source,
        string? omit = null, bool unenforced = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => unenforced);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, RoutedHttpEvidence Route,
        EuDocumentFetchAddress Address, RouteHandler Handler)> AcquireAsync(int shape)
    {
        var address = Address();
        var bound = new EuDocumentFetchPlan(address).Bind("urn:uuid:00000000-0000-4000-8000-000000000081",
            "urn:uuid:00000000-0000-4000-8000-000000000082", EuAcquisitionTestFixture.BuildRendererSource(8401));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new RouteHandler(shape);
        var executor = new EuRepeatedEnumerationExecutor(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var result = await executor.RunDocumentFetchAsync(bound.Request, bound.Request, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNotNull(result.Evidence, result.Detail);
        if (shape == 3)
        {
            Assert.IsInstanceOfType<IncompleteHttpRouteOutcome>(result.Evidence.Outcome);
            Assert.AreEqual(3, handler.RequestCount, "the foreign redirect was never sent");
        }
        else
        {
            Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(result.Evidence.Outcome);
            Assert.AreEqual(shape == 2 ? 404 : 200, result.Evidence.Hops[^1].Status);
        }
        return (store, result.Evidence, address, handler);
    }
    private sealed class RouteHandler(int shape) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ordinal = RequestCount++;
            if (ordinal == 0) return Task.FromResult(Response(request, HttpStatusCode.MovedPermanently, [], "https://op.europa.eu/robots.txt"));
            if (ordinal == 1) return Task.FromResult(Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n"u8.ToArray(), contentType: "text/plain;charset=UTF-8"));
            if (shape == 1 && ordinal == 2) return Task.FromResult(Response(request, HttpStatusCode.SeeOther, [],
                "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.03/DOC_1"));
            if (shape == 3) return Task.FromResult(Response(request, HttpStatusCode.SeeOther, [], "https://example.org/elsewhere"));
            return Task.FromResult(Response(request, shape == 2 ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                "<html>retained transport fixture</html>"u8.ToArray(), contentType: "application/xhtml+xml"));
        }
        private static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode status, byte[] body,
            string? location = null, string? contentType = null)
        {
            var content = new ByteArrayContent(body);
            content.Headers.TryAddWithoutValidation("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (contentType is not null) content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            var response = new HttpResponseMessage(status) { Version = HttpVersion.Version11, RequestMessage = request, Content = content };
            if (location is not null) response.Headers.TryAddWithoutValidation("Location", location);
            return response;
        }
    }
}
