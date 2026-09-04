using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06c-EU: the EU document-fetch route (SCOPE_RULING
/// lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef). Proves the actual GET-sending
/// logic end to end through a scripted transport, exactly the discipline
/// <see cref="RoutedHttpAcquisitionSessionTests"/> already uses for the two SPARQL channels: real
/// production code (<see cref="RoutedHttpAcquisitionSession"/>, <see cref="EuDocumentFetchPlan"/>,
/// <see cref="OfficialMachineQuerySourceProfiles"/>) driven by a scripted <see cref="HttpMessageHandler"/>,
/// never a hand-rolled substitute for the session itself.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EuDocumentFetchReachabilityTests
{
    /// <summary>
    /// Item 5's required reachability proof: a completed document-fetch GET whose terminal status
    /// is 404 (PROVEN, <c>review/23-research-temporal.md</c> section 1.2: "Missing manifestation
    /// returns 404") becomes the closed, named <see cref="EuDocumentFetchRefusal.ManifestationNotFound"/>
    /// member carrying the real observed status, not a generic HTTP-status wrapper.
    /// </summary>
    [TestMethod]
    public async Task ManifestationNotFoundReachabilityMatchesTheProvenObserved404()
    {
        var bound = BindDocumentFetchRequest(out _);
        using var session = Session(
            bound,
            DocumentFetchHandler(HttpStatusCode.NotFound, string.Empty),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);

        var evidence = (RoutedHttpEvidence)result.GetType()
            .GetProperty("Evidence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(result)!;
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(404, evidence.Hops[^1].Status);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.AreEqual(EuDocumentFetchRefusal.ManifestationNotFound, outcome.Refusal);
        Assert.AreEqual(404, outcome.ObservedStatus);
    }

    /// <summary>
    /// The 400 sibling of the required proof above: PROVEN, "<c>Accept:
    /// application/pdf;mtype=pdfa1a</c> returned 400 (wrong token: the spec uses <c>type=</c> for
    /// PDF and <c>mtype=</c> for zip packages)". Closed and named as
    /// <see cref="EuDocumentFetchRefusal.WrongAcceptToken"/>, distinct from
    /// <see cref="EuDocumentFetchRefusal.ManifestationNotFound"/> above even though both terminate a
    /// completed, non-redirect route.
    /// </summary>
    [TestMethod]
    public async Task WrongAcceptTokenReachabilityMatchesTheProvenObserved400()
    {
        var bound = BindDocumentFetchRequest(out _);
        using var session = Session(
            bound,
            DocumentFetchHandler(HttpStatusCode.BadRequest, string.Empty),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);

        var evidence = (RoutedHttpEvidence)result.GetType()
            .GetProperty("Evidence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(result)!;
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(400, evidence.Hops[^1].Status);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.AreEqual(EuDocumentFetchRefusal.WrongAcceptToken, outcome.Refusal);
        Assert.AreEqual(400, outcome.ObservedStatus);
    }

    /// <summary>
    /// Item 1's own requirement: "follow the observed 303 chain only to hosts in the route's own
    /// closed admitted set". A 303 to a target on the SAME host this route's own first hop already
    /// started at (the real, proven shape: <c>/resource/celex/{id}</c> redirecting to
    /// <c>/resource/cellar/{uuid}/rdf/object/full</c> on the identical host) is followed to its
    /// terminal 200, with every hop retained on the resulting evidence.
    /// </summary>
    [TestMethod]
    public async Task SameOriginRedirectIsFollowedToItsTerminalHopAndEveryHopIsRetained()
    {
        var bound = BindDocumentFetchRequest(out var address);
        var cellarTarget = "https://" + EuDocumentFetchAddress.AdmittedHost
            + "/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1/rdf/object/full";
        using var session = Session(
            bound,
            new SequenceHandler((ordinal, outbound) => ordinal switch
            {
                0 => DeclaredResponse(
                    outbound, HttpStatusCode.MovedPermanently, "moved",
                    location: "https://op.europa.eu/robots.txt"),
                1 => DeclaredResponse(
                    outbound, HttpStatusCode.OK, "User-agent: *\nAllow: /\n",
                    contentType: "text/plain;charset=UTF-8"),
                2 => DeclaredResponse(
                    outbound, HttpStatusCode.SeeOther, string.Empty, location: cellarTarget),
                _ => DeclaredResponse(
                    outbound, HttpStatusCode.OK, "<html/>", contentType: "application/xhtml+xml"),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);

        var evidence = (RoutedHttpEvidence)result.GetType()
            .GetProperty("Evidence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(result)!;
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(2, evidence.Hops.Count);
        Assert.AreEqual(address.ResourceUri, evidence.Hops[0].RequestUri);
        Assert.AreEqual(303, evidence.Hops[0].Status);
        Assert.AreEqual(cellarTarget, evidence.Hops[1].RequestUri);
        Assert.AreEqual(200, evidence.Hops[1].Status);
    }

    /// <summary>
    /// The other half of item 1: a well-formed absolute-HTTPS redirect target on a genuinely
    /// different host is a typed refusal, never silently followed.
    /// <see cref="HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted"/>.
    /// </summary>
    [TestMethod]
    public async Task OffOriginRedirectIsRefusedAsATypedRouteOutcomeNeverFollowed()
    {
        var bound = BindDocumentFetchRequest(out _);
        const string offOriginTarget = "https://not-publications.europa.eu.example.invalid/elsewhere";
        using var session = Session(
            bound,
            new SequenceHandler((ordinal, outbound) => ordinal switch
            {
                0 => DeclaredResponse(
                    outbound, HttpStatusCode.MovedPermanently, "moved",
                    location: "https://op.europa.eu/robots.txt"),
                1 => DeclaredResponse(
                    outbound, HttpStatusCode.OK, "User-agent: *\nAllow: /\n",
                    contentType: "text/plain;charset=UTF-8"),
                _ => DeclaredResponse(
                    outbound, HttpStatusCode.SeeOther, string.Empty, location: offOriginTarget),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);

        var evidence = (RoutedHttpEvidence)result.GetType()
            .GetProperty("Evidence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(result)!;
        var outcome = Assert.IsInstanceOfType<IncompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted, outcome.Reason);
        Assert.AreEqual(1, evidence.Hops.Count);
        Assert.AreEqual(303, evidence.Hops[0].Status);
    }

    /// <summary>
    /// Item 1: the switch gains exactly one new member and admits the real resource-fetch shape;
    /// every other input still throws exactly as before.
    /// </summary>
    [TestMethod]
    public void ResolveForAdmitsTheDocumentFetchShapeAndStillThrowsForEverythingElse()
    {
        var bound = BindDocumentFetchRequest(out var address);
        var identity = MachineQueryBinder.OpenIdentity(bound);
        var resolved = OfficialMachineQuerySourceProfiles.ResolveFor(identity);
        Assert.AreEqual(OfficialMachineQuerySourceProfileId.EuropeanUnionDocumentFetch, resolved.Id);
        Assert.AreEqual(HttpRequestMethod.Get, resolved.Method);
        Assert.AreEqual(address.ResourceUri, identity.RequestedUri);

        Assert.IsTrue(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/resource/celex/32016R0679"));
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/resource/celex"));
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://example.invalid/resource/celex/32016R0679"));
    }

    private static BoundMachineRequest BindDocumentFetchRequest(out EuDocumentFetchAddress address)
    {
        address = EuDocumentFetchAddress.TryCreate(
            "celex",
            "32016R0679",
            EuManifestationMediaType.XhtmlXml,
            EuDocumentLanguage.Eng,
            out var refusal) ?? throw new AssertFailedException($"Address minting refused: {refusal}.");
        var plan = new EuDocumentFetchPlan(address);
        var sourceBytes = Encoding.UTF8.GetBytes("fixture-eu-document-fetch-renderer-source/1\n");
        var sourceRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000fe",
            Sha256(sourceBytes));
        var rendererSource = MachineQueryRendererSource.Open(sourceRef, sourceBytes);
        var bound = plan.Bind(
            "urn:uuid:00000000-0000-4000-8000-0000000000fc",
            "urn:uuid:00000000-0000-4000-8000-0000000000fd",
            rendererSource);
        return bound.Request;
    }

    private static SequenceHandler DocumentFetchHandler(HttpStatusCode terminalStatus, string body) =>
        new((ordinal, outbound) => ordinal switch
        {
            0 => DeclaredResponse(
                outbound, HttpStatusCode.MovedPermanently, "moved",
                location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredResponse(
                outbound, HttpStatusCode.OK, "User-agent: *\nAllow: /\n",
                contentType: "text/plain;charset=UTF-8"),
            _ => DeclaredResponse(outbound, terminalStatus, body),
        });

    private static HttpResponseMessage DeclaredResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body,
        string? location = null,
        string? contentType = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (contentType is not null)
        {
            Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Type", contentType));
        }

        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
        if (location is not null)
        {
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("Location", location));
        }

        return response;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static RoutedHttpAcquisitionSession Session(
        BoundMachineRequest request,
        HttpMessageHandler handler,
        ICustodyStore custody,
        TimeProvider timeProvider,
        bool usesPinnedHandler)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (RoutedHttpAcquisitionSession)constructor.Invoke(
            [request, custody, handler, timeProvider, usesPinnedHandler]);
    }

    private static Task<RoutedHttpAcquisitionSession.StartResult> BootstrapAsync(
        RoutedHttpAcquisitionSession session) =>
        (Task<RoutedHttpAcquisitionSession.StartResult>)typeof(RoutedHttpAcquisitionSession).GetMethod(
            "BootstrapRobotsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(session, [CancellationToken.None])!;

    private sealed class SequenceHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Requests.Count;
            Requests.Add(request);
            return Task.FromResult(respond(ordinal, request));
        }
    }
}
