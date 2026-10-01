using System.Net;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LuxembourgDocumentFetchRouteReaderTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(5, false)]
    [DataRow(1, true)]
    public async Task CopiedRoutesReopenTwiceWithOriginalOutcomesAndNoTraffic(int shape, bool unenforced)
    {
        var (store, route, address, handler, bound) = await AcquireAsync(shape);
        var copy = await CopyAsync(store, unenforced: unenforced);
        var writes = copy.CreateCallCount;
        var requests = handler.RequestCount;
        var first = await ReopenAsync(copy, route, address, bound);
        var second = await ReopenAsync(copy, route, address, bound);
        CollectionAssert.AreEqual(route.CopyCanonicalBytes(), first.Evidence!.CopyCanonicalBytes());
        CollectionAssert.AreEqual(first.Evidence.CopyCanonicalBytes(), second.Evidence!.CopyCanonicalBytes());
        Assert.AreEqual(shape == 4, first.RetryAllowanceSpent);
        Assert.AreEqual(first.RetryAllowanceSpent, second.RetryAllowanceSpent);
        Assert.AreEqual(route.Outcome.GetType(), first.Evidence.Outcome.GetType());
        Assert.AreEqual(writes, copy.CreateCallCount);
        Assert.AreEqual(requests, handler.RequestCount);
        Assert.IsNull(first.Refusal);
    }

    [TestMethod]
    [DataRow("route")]
    [DataRow("first_request")]
    [DataRow("address")]
    [DataRow("body")]
    [DataRow("receipt")]
    [DataRow("request_policy")]
    [DataRow("redirect_policy")]
    public async Task EveryTransportDependencyMustStillExist(string missing)
    {
        var (store, route, address, _, bound) = await AcquireAsync(1);
        var request = HttpLogicalRequest.ParseAndVerify((await store.ReadByDigestAsync(route.Hops[0].LogicalRequestSha256, CancellationToken.None)).Span);
        var digest = missing switch
        {
            "route" => CustodyDigest.Of(route.CopyCanonicalBytes()),
            "first_request" => route.Hops[0].LogicalRequestSha256,
            "address" => address.ArtifactRef.Sha256,
            "body" => route.Hops[^1].Sha256,
            "receipt" => route.Hops[^1].DurableWriteReceiptSha256,
            "request_policy" => request.RequestPolicySha256,
            _ => request.RedirectPolicySha256,
        };
        var copy = await CopyAsync(store, omit: digest);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => ReopenAsync(copy, route, address, bound));
    }

    [TestMethod]
    [DataRow("run")]
    [DataRow("request")]
    [DataRow("address")]
    [DataRow("format")]
    [DataRow("provenance")]
    [DataRow("plan")]
    [DataRow("input")]
    [DataRow("renderer")]
    public async Task CallerPinsCannotSelectAnotherRunRequestOrRepresentation(string changed)
    {
        var (store, route, address, _, bound) = await AcquireAsync(0);
        var other = Address(changed == "address" ? StoreUri.Replace("a439", "a440", StringComparison.Ordinal) : StoreUri,
            changed == "format" ? LuxembourgUserFormatToken.Xml : LuxembourgUserFormatToken.XmlAkomaNtoso,
            changed == "provenance" ? "/eli/etat/leg/loi/2017/03/14/a440/jo" : ActPath);
        var expectedBound = changed is "plan" or "input" or "renderer"
            ? new LuxembourgDocumentFetchPlan(address).Bind(
                changed == "plan" ? "urn:uuid:00000000-0000-4000-8000-000000000083" : "urn:uuid:00000000-0000-4000-8000-000000000081",
                changed == "input" ? "urn:uuid:00000000-0000-4000-8000-000000000084" : "urn:uuid:00000000-0000-4000-8000-000000000082",
                LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(changed == "renderer" ? 8402 : 8401)).Request
            : bound;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgDocumentFetchRouteReader.ReopenAsync(
            store, Reference(route), changed == "run" ? new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000099", new string('a',64)) : route.RunIdentity,
            changed == "request" ? new string('a',64) : route.Hops[0].LogicalRequestSha256, other, expectedBound, CancellationToken.None));
    }

    [TestMethod]
    public async Task RehashedUnrelatedReceiptCannotStandInForTheTerminalWrite()
    {
        var (store, route, address, _, bound) = await AcquireAsync(1);
        var unrelated = await store.CreateAsync("unrelated"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var receipt = Encoding.UTF8.GetBytes(ContractJson.Serialize(unrelated));
        await store.CreateAsync(receipt, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var original = Encoding.UTF8.GetString(route.CopyCanonicalBytes());
        var replacement = original.Replace(route.Hops[^1].DurableWriteReceiptSha256, CustodyDigest.Of(receipt), StringComparison.Ordinal);
        Assert.AreNotEqual(original, replacement);
        var bytes = Encoding.UTF8.GetBytes(replacement);
        _ = RoutedHttpEvidence.ParseAndVerify(bytes); // The receipt gate, not framing, must reject it.
        await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgDocumentFetchRouteReader.ReopenAsync(store,
            new SourceArtifactRef(Reference(route).ResourceId, CustodyDigest.Of(bytes)), route.RunIdentity,
            route.Hops[0].LogicalRequestSha256, address, bound, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancellationDoesNotOpenATransport()
    {
        var (store, route, address, handler, bound) = await AcquireAsync(0);
        var requests = handler.RequestCount;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgDocumentFetchRouteReader.ReopenAsync(
            store, Reference(route), route.RunIdentity, route.Hops[0].LogicalRequestSha256, address, bound, source.Token));
        Assert.AreEqual(requests, handler.RequestCount);
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("limit")]
    [DataRow("duplicate_limit")]
    [DataRow("kind")]
    [DataRow("header")]
    public async Task RehashedUnsupportedPolicyOrNegotiatedRequestCannotBecomeALuxembourgFetch(string changed)
    {
        var (store, route, address, _, bound) = await AcquireAsync(0);
        var request = HttpLogicalRequest.ParseAndVerify((await store.ReadByDigestAsync(
            route.Hops[0].LogicalRequestSha256, CancellationToken.None)).Span);
        var originalPolicy = Encoding.UTF8.GetString((await store.ReadByDigestAsync(request.RequestPolicySha256, CancellationToken.None)).Span);
        var profile = OfficialMachineQuerySourceProfiles.ResolveFor(MachineQueryBinder.OpenIdentity(bound));
        var policy = changed switch
        {
            "profile" => originalPolicy.Replace(profile.ArtifactRef.Sha256, new string('a', 64), StringComparison.Ordinal),
            "limit" => originalPolicy.Replace("maximum_attempts=4", "maximum_attempts=3", StringComparison.Ordinal),
            "duplicate_limit" => originalPolicy + "maximum_attempts=4\n",
            "kind" => originalPolicy.Replace("machine_query_get", "robots_get", StringComparison.Ordinal),
            _ => originalPolicy,
        };
        var policyBytes = Encoding.UTF8.GetBytes(policy);
        await store.CreateAsync(policyBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var replacement = HttpLogicalRequest.Create(request.Uri, request.Method,
            changed == "header" ? [request.Headers[0], new HttpLogicalRequestHeader("accept", "application/xml")] : request.Headers,
            request.Body, CustodyDigest.Of(policyBytes), request.RedirectPolicySha256);
        var requestBytes = replacement.CopyCanonicalBytes();
        await store.CreateAsync(requestBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var digest = CustodyDigest.Of(requestBytes);
        var routeBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(route.CopyCanonicalBytes())
            .Replace(route.Hops[0].LogicalRequestSha256, digest, StringComparison.Ordinal));
        _ = RoutedHttpEvidence.ParseAndVerify(routeBytes);
        await store.CreateAsync(routeBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var writes = store.CreateCallCount;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgDocumentFetchRouteReader.ReopenAsync(store,
            new SourceArtifactRef(Reference(route).ResourceId, CustodyDigest.Of(routeBytes)), route.RunIdentity,
            digest, address, bound, CancellationToken.None));
        Assert.AreEqual(writes, store.CreateCallCount);
    }

    private static SourceArtifactRef Reference(RoutedHttpEvidence route) =>
        new("urn:uuid:00000000-0000-4000-8000-000000000098", CustodyDigest.Of(route.CopyCanonicalBytes()));
    private static Task<LuxembourgDocumentGetAttemptResult> ReopenAsync(ICustodyStore store, RoutedHttpEvidence route, LuxembourgDocumentFetchAddress address, BoundMachineRequest bound) =>
        LuxembourgDocumentFetchRouteReader.ReopenAsync(store, Reference(route), route.RunIdentity, route.Hops[0].LogicalRequestSha256, address, bound, CancellationToken.None);
    private const string StoreUri = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml";
    private const string ActPath = "/eli/etat/leg/loi/2017/03/14/a439/jo";
    private static LuxembourgDocumentFetchAddress Address(string uri = StoreUri,
        LuxembourgUserFormatToken token = LuxembourgUserFormatToken.XmlAkomaNtoso, string actPath = ActPath) =>
        LuxembourgDocumentFetchAddress.Create(LuxembourgFileUri.RequireValid(uri), token, LuxembourgLegalValue.Officiel, actPath);
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore source,
        string? omit = null, bool unenforced = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => unenforced);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, RoutedHttpEvidence Route,
        LuxembourgDocumentFetchAddress Address, RouteHandler Handler, BoundMachineRequest Bound)> AcquireAsync(int shape)
    {
        var address = Address();
        var bound = new LuxembourgDocumentFetchPlan(address).Bind("urn:uuid:00000000-0000-4000-8000-000000000081",
            "urn:uuid:00000000-0000-4000-8000-000000000082", LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(8401));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new RouteHandler(shape);
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var result = await executor.RunDocumentGetAsync(bound.Request, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNotNull(result.Evidence, result.Detail);
        if (shape == 3)
        {
            Assert.IsInstanceOfType<IncompleteHttpRouteOutcome>(result.Evidence.Outcome);
            Assert.AreEqual(2, handler.RequestCount, "the unsupported redirect was never followed");
        }
        else
        {
            Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(result.Evidence.Outcome);
            Assert.AreEqual(shape == 2 ? 404 : shape == 4 ? 503 : 200, result.Evidence.Hops[^1].Status);
            Assert.AreEqual(shape == 4, result.RetryAllowanceSpent);
        }
        return (store, result.Evidence, address, handler, bound.Request);
    }
    private sealed class RouteHandler(int shape) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ordinal = RequestCount++;
            if (ordinal == 0) return Task.FromResult(Response(request, HttpStatusCode.OK,
                Encoding.UTF8.GetBytes(LuxembourgDocumentFetchRobotsBootstrapTests.RealRobotsTxt), contentType: "text/plain;charset=UTF-8"));
            if (shape == 3) return Task.FromResult(Response(request, HttpStatusCode.SeeOther, [], "https://example.org/elsewhere"));
            if (shape == 5 && ordinal == 1) throw new HttpRequestException("scripted pre-header failure");
            if (shape == 4 || shape == 1 && ordinal == 1)
                return Task.FromResult(Response(request, HttpStatusCode.ServiceUnavailable, "retry"u8.ToArray(), contentType: "text/plain"));
            return Task.FromResult(Response(request, shape == 2 ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                "<akomaNtoso/>"u8.ToArray(), contentType: "application/xml"));
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
