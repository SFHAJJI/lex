using System.Net;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S1-A10, Decision 83: the robots verdict is computed against the document URL this route actually
/// requests, not against the shared witness that merely selects the profile.
/// </summary>
/// <remarks>
/// <para>
/// THE GAP THESE CLOSE, and it was found by the reviewer rather than by me. The first S1-A10 slice
/// evaluated the redirect TARGET and stopped there, on my own reasoning that no case distinguished
/// the initial bound URL from the session's start position. That reasoning was wrong. Every EU
/// document fetch started its session from a SHARED witness whose address is the placeholder
/// <c>celex/00000000</c>, so <c>BootstrapRobotsAsync</c> computed its verdict against that
/// placeholder and the real <c>/resource/{ps-name}/{ps-id}</c> path was never asked about at all.
/// Profile-shape admission is not publisher permission, and a robots file can distinguish two
/// perfectly admitted resource paths.
/// </para>
/// <para>
/// The repair is that a document fetch starts its session from its own bound request. It costs no
/// extra traffic — one session was already opened per document, and the robots URL derives from the
/// profile origin rather than the path — so only the path the verdict is computed against changes.
/// </para>
/// <para>
/// These two cases are the pair the reviewer asked for, and they need a robots body that separates
/// the two paths, because the publisher's real file allows both. The scripted policy below allows
/// the witness placeholder and disallows one exact real document path, which is the shape that
/// makes the difference observable at all.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuDocumentFetchBoundUrlRobotsTests
{
    private const string Gdpr = "32016R0679";

    /// <summary>
    /// Allows everything except the one real document path, so the shared witness placeholder is
    /// permitted while the bound URL is denied.
    /// </summary>
    private static byte[] RobotsAllowingWitnessButDenyingGdpr() => Encoding.UTF8.GetBytes(
        "User-agent: *\nAllow: /\nDisallow: /resource/celex/" + Gdpr + "\n");

    [TestMethod]
    public async Task ADisallowedBoundDocumentUrlIsRefusedEvenWhenTheSharedWitnessPathIsAllowed()
    {
        var handler = new RobotsRouteThenProductHandler(RobotsAllowingWitnessButDenyingGdpr());
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await executor.RunDocumentFetchAsync(
            BoundFor(Gdpr),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused,
            result.Refusal,
            "the publisher disallows this exact document path, so the fetch must refuse.");
        Assert.IsNull(
            result.Evidence,
            "a refused fetch carries no route evidence, because no product request was sent. "
                + "Evidence here would mean the disallowed GET went out.");

        // Said twice, from two directions. This count is the transport's own account of what left
        // the process, rather than a second reading of the same result object.
        Assert.AreEqual(
            0,
            handler.ProductRequestCount,
            "the disallowed document URL must never be requested.");
    }

    [TestMethod]
    public async Task APermittedBoundDocumentUrlIsStillRequested()
    {
        // The converse, and the reason the guard above is not simply "refuse everything": a
        // different document under the same profile, which this policy permits, still sends.
        var handler = new RobotsRouteThenProductHandler(RobotsAllowingWitnessButDenyingGdpr());
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await executor.RunDocumentFetchAsync(
            BoundFor("32003L0088"),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.AreNotEqual(
            EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused,
            result.Refusal,
            "a permitted document path must not be refused at the robots bootstrap.");
        Assert.AreEqual(
            1,
            handler.ProductRequestCount,
            "the permitted document URL must actually be requested. If this is zero the guard has "
                + "become an over-refusal and the route no longer fetches anything.");
    }

    private static BoundMachineRequest BoundFor(string celex)
    {
        var address = EuDocumentFetchAddress.TryCreate(
            "celex", celex, EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng, out var refusal)
            ?? throw new AssertFailedException($"Address minting refused: {refusal}.");
        var bytes = Encoding.UTF8.GetBytes("fixture-eu-bound-url-robots-renderer-source/1\n");
        var sourceRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000ea",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        return new EuDocumentFetchPlan(address).Bind(
            "urn:uuid:00000000-0000-4000-8000-0000000000eb",
            "urn:uuid:00000000-0000-4000-8000-0000000000ec",
            MachineQueryRendererSource.Open(sourceRef, bytes)).Request;
    }

    /// <summary>
    /// Drives the profile's own declared robots route — the 301 to <c>op.europa.eu</c> and then the
    /// real 200 — with a scripted policy body, and counts every request that is not part of it.
    /// </summary>
    /// <remarks>
    /// THE ROUTE MATTERS, AND MY FIRST ATTEMPT AT THIS HANDLER DID NOT DRIVE IT. Answering the first
    /// robots request with a 200 makes the bootstrap refuse for a stale-profile reason that has
    /// nothing to do with the policy body, which would have made the disallowed case below pass for
    /// entirely the wrong reason while the permitted case failed. The two-hop shape here is the same
    /// one <c>EuDocumentFetchReachabilityTests</c> negotiates.
    /// </remarks>
    private sealed class RobotsRouteThenProductHandler(byte[] robots) : HttpMessageHandler
    {
        private int _ordinal;

        internal int ProductRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ordinal = _ordinal++;
            return Task.FromResult(ordinal switch
            {
                0 => Declared(
                    request, HttpStatusCode.MovedPermanently, [],
                    location: "https://op.europa.eu/robots.txt"),
                1 => Declared(request, HttpStatusCode.OK, robots, contentType: "text/plain;charset=UTF-8"),
                _ => Product(request),
            });
        }

        private HttpResponseMessage Product(HttpRequestMessage request)
        {
            ProductRequestCount++;
            return Declared(
                request, HttpStatusCode.OK, "<akomaNtoso/>"u8.ToArray(),
                contentType: "application/xhtml+xml");
        }

        private static HttpResponseMessage Declared(
            HttpRequestMessage request,
            HttpStatusCode status,
            byte[] body,
            string? location = null,
            string? contentType = null)
        {
            var content = new ByteArrayContent(body);
            content.Headers.TryAddWithoutValidation(
                "Content-Length",
                body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (contentType is not null)
            {
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            var response = new HttpResponseMessage(status)
            {
                Version = HttpVersion.Version11,
                RequestMessage = request,
                Content = content,
            };
            if (location is not null)
            {
                response.Headers.TryAddWithoutValidation("Location", location);
            }

            return response;
        }
    }
}
