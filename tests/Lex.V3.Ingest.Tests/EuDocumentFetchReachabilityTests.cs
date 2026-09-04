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
/// lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef, defect fixes per SCOPE_RULING
/// lex-event-20260904T130546972Z-c72fad2da5b34344af802c068d8fbf08). Proves the actual GET-sending
/// logic end to end through a scripted transport, exactly the discipline
/// <see cref="RoutedHttpAcquisitionSessionTests"/> already uses for the two SPARQL channels: real
/// production code (<see cref="RoutedHttpAcquisitionSession"/>, <see cref="EuDocumentFetchPlan"/>,
/// <see cref="OfficialMachineQuerySourceProfiles"/>) driven by a scripted <see cref="HttpMessageHandler"/>,
/// never a hand-rolled substitute for the session itself.
/// </summary>
/// <remarks>
/// Defect 2's own fix: every scripted status/header/body pairing below is one of the real canary
/// observations retained under <c>Fixtures/EuDocumentFetch/</c> (copied byte-for-byte from
/// <c>C:/lex-v3/scratch/probe-d1-06c-eu/</c> and <c>C:/lex-v3/scratch/probe-d1-06c/eu-robots.txt</c>
/// on 2026-09-04), never a fabricated status, header or body. <see cref="LoadFixture"/> re-hashes
/// every loaded file on every test run and fails closed if the checked-in bytes ever drift from the
/// cited digest, so the citation is enforced, not merely asserted once at authoring time.
/// <para>
/// One deliberate substitution from the reviewer's own evidence list: the 1995 directive's real
/// "once the right format is requested" 200 case is proven here through its RDF/XML manifestation
/// (<c>old-rdfxml-200-body.bin</c>, real 303-then-200, Accept <c>application/rdf+xml</c>, an admitted
/// <see cref="EuManifestationMediaType.RdfXml"/> member) rather than through the reviewer-cited
/// <c>old-manif-direct2-body.bin</c>/<c>old-v2ladder-body.bin</c> (digest
/// <c>a86b2053b2f354b26afa4a6fbcaaab1b879f70cfb2be033a231814feebec2a98</c>, also real and also
/// independently reproduced by two separate probes). That pair's own real observed
/// <c>Content-Type</c> is <c>text/html</c>, which is not one of <see cref="EuManifestationMediaType"/>'s
/// closed eight members and so cannot be reached by any <c>Accept</c> this route is capable of
/// sending -- inventing a ninth member to fit it would be exactly the un-grounded widening
/// <c>EuManifestationMediaType</c>'s own doc comment refuses. The RDF/XML case is real, admitted, and
/// exercises the identical http-Location upgrade (defect 1) the html case would have.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuDocumentFetchReachabilityTests
{
    private const string GdprCelex = "32016R0679";
    private const string OldDirectiveCelex = "31995L0046";
    private const string NewActCelex = "32026R1965";

    // ---- Real observed Location headers, transcribed verbatim from the retained headers files. ----
    private const string GdprXhtmlLocation =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.03/DOC_1";
    private const string GdprFmx4Location =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02/zip";
    private const string OldRdfXmlLocation =
        "http://publications.europa.eu/resource/cellar/775a4724-2086-4a06-9213-1a4e6489053b.0008.01/rdf/object/full";
    private const string NewXhtmlLocation =
        "http://publications.europa.eu/resource/cellar/3649f0b6-a1af-11f1-b25c-01aa75ed71a1.0006.03/DOC_1";
    private const string NewPdfa2aLocation =
        "http://publications.europa.eu/resource/cellar/3649f0b6-a1af-11f1-b25c-01aa75ed71a1.0006.01/DOC_1";
    private const string NewFmx4Location =
        "http://publications.europa.eu/resource/cellar/3649f0b6-a1af-11f1-b25c-01aa75ed71a1.0006.02/zip";

    /// <summary>
    /// Defect 1's own driving test. The real xhtml canary (2026-09-04, GDPR CELEX 32016R0679):
    /// the office's own 303 Location is literally <c>http://</c> on the identical admitted host. Before
    /// the fix, <c>TryCreateRedirectRequest</c> refused this unconditionally (RequireAbsoluteHttpsUri
    /// runs first) and the route ended <c>RedirectRefused</c>, never reaching the real 200 body. After
    /// the fix the http Location is followed as https -- never sent in plaintext -- and the route
    /// completes at the exact real observed terminal, with the real 806864-byte body and its cited
    /// digest.
    /// </summary>
    [TestMethod]
    public async Task GdprXhtmlRedirectCarriesARealHttpLocationAndIsUpgradedAndFollowedToTheRealTerminal200()
    {
        var body = LoadFixture(
            "gdpr-xhtml-200-body.bin",
            "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d");
        Assert.AreEqual(806864, body.Length);

        var bound = BindDocumentFetchRequest(
            GdprCelex, EuManifestationMediaType.XhtmlXml, out var address);
        using var session = Session(
            bound,
            DocumentFetchHandler((ordinal, outbound) => ordinal switch
            {
                2 => DeclaredBinaryResponse(
                    outbound, HttpStatusCode.SeeOther, [], location: GdprXhtmlLocation),
                _ => DeclaredBinaryResponse(
                    outbound, HttpStatusCode.OK, body, contentType: "application/xhtml+xml;charset=UTF-8"),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);

        var evidence = Evidence(result);
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(2, evidence.Hops.Count);
        Assert.AreEqual(address.ResourceUri, evidence.Hops[0].RequestUri);
        Assert.AreEqual(303, evidence.Hops[0].Status);

        // The observed Location (http) stays exactly what the office sent...
        var observedLocation = Assert.IsInstanceOfType<RoutedHttpSingleHeader>(evidence.Hops[0].Headers.Location);
        Assert.AreEqual(GdprXhtmlLocation, observedLocation.Value);
        StringAssert.StartsWith(observedLocation.Value, "http://");

        // ...while the hop actually followed is the upgrade: same host and path, https scheme, and
        // RoutedHttpHop.Create's own RequireAbsoluteHttpsUri proves no hop can ever be minted with a
        // plaintext http RequestUri, so this hop's mere existence proves http itself was never sent.
        Assert.AreEqual(
            "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.03/DOC_1",
            evidence.Hops[1].RequestUri);
        Assert.AreEqual(200, evidence.Hops[1].Status);
        Assert.AreEqual(806864UL, evidence.Hops[1].Length);
        Assert.AreEqual(
            "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d",
            evidence.Hops[1].Sha256);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.IsNull(outcome.Refusal);
        Assert.AreEqual(200, outcome.ObservedStatus);
    }

    /// <summary>
    /// The fmx4 sibling of the driving test above: a different real http Location
    /// (<c>.../3e485e15-....0006.02/zip</c>) on the same GDPR object, proving the fix is not
    /// special-cased to one manifestation family.
    /// </summary>
    [TestMethod]
    public async Task GdprFmx4RedirectCarriesARealHttpLocationAndIsUpgradedAndFollowedToTheRealTerminal200()
    {
        var body = LoadFixture(
            "gdpr-fmx4-200-body.bin",
            "4cbf7280014b0bd3d20fc8c1d6a7c08cdcd8aaacab5ee356c07ed7d840994541");
        Assert.AreEqual(90312, body.Length);

        var bound = BindDocumentFetchRequest(
            GdprCelex, EuManifestationMediaType.ZipMtypeFmx4, out _);
        using var session = Session(
            bound,
            DocumentFetchHandler((ordinal, outbound) => ordinal switch
            {
                2 => DeclaredBinaryResponse(
                    outbound, HttpStatusCode.SeeOther, [], location: GdprFmx4Location),
                _ => DeclaredBinaryResponse(
                    outbound, HttpStatusCode.OK, body, contentType: "application/zip;charset=UTF-8"),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var evidence = Evidence(result);
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(
            "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02/zip",
            evidence.Hops[1].RequestUri);
        Assert.AreEqual(200, evidence.Hops[1].Status);
        Assert.AreEqual(90312UL, evidence.Hops[1].Length);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.IsNull(outcome.Refusal);
        Assert.AreEqual(200, outcome.ObservedStatus);
    }

    /// <summary>
    /// GDPR pdfa2a: the office answered a real, direct 404 (no redirect at all) for this Accept type.
    /// </summary>
    [TestMethod]
    public async Task GdprPdfa2aReachabilityMatchesTheRealObserved404WithNoRedirect()
    {
        var body = LoadFixture(
            "gdpr-pdfa2a-404-body.bin",
            "c5c14115c74483c4075e3bcef29f9863d4853d0fa4827b78e90bf798e3a10f91");
        Assert.AreEqual(214, body.Length);

        var bound = BindDocumentFetchRequest(GdprCelex, EuManifestationMediaType.PdfTypePdfa2a, out _);
        using var session = Session(
            bound,
            DocumentFetchHandler((_, outbound) => DeclaredBinaryResponse(outbound, HttpStatusCode.NotFound, body)),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var evidence = Evidence(result);
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(1, evidence.Hops.Count, "the real probe observed no redirect for this Accept type.");
        Assert.AreEqual(404, evidence.Hops[0].Status);
        Assert.AreEqual(214UL, evidence.Hops[0].Length);
        Assert.AreEqual(
            "c5c14115c74483c4075e3bcef29f9863d4853d0fa4827b78e90bf798e3a10f91",
            evidence.Hops[0].Sha256);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.AreEqual(EuDocumentFetchRefusal.RequestedRepresentationNotServed, outcome.Refusal);
        Assert.AreEqual(404, outcome.ObservedStatus);
    }

    /// <summary>
    /// The 400 sibling: PROVEN, "<c>Accept: application/pdf;mtype=pdfa1a</c> returned 400 (wrong
    /// token: the spec uses <c>type=</c> for PDF and <c>mtype=</c> for zip packages)". Real 171-byte
    /// office error body, no redirect.
    /// </summary>
    [TestMethod]
    public async Task GdprWrongAcceptTokenReachabilityMatchesTheRealObserved400()
    {
        var body = LoadFixture(
            "gdpr-wrong-token-400-body.bin",
            "b8c90925aeadb27528f960535a159e627fcc94671d4a8a5e1f270a4b05c2eb23");
        Assert.AreEqual(171, body.Length);

        // The wrong-token probe itself used Accept: application/pdf;mtype=pdfa1a, a token this
        // route's own closed EuManifestationMediaType enum has no member for (mtype is only ever
        // paired with zip; type is only ever paired with pdf, exactly why the office refused it).
        // The bound request below still exercises the identical real 400 body and status through this
        // route's own admitted PdfTypePdfa2a channel, which is what EuDocumentFetchOutcome.Classify
        // actually reads (the observed status), never the Accept text itself.
        var bound = BindDocumentFetchRequest(GdprCelex, EuManifestationMediaType.PdfTypePdfa2a, out _);
        using var session = Session(
            bound,
            DocumentFetchHandler((_, outbound) => DeclaredBinaryResponse(outbound, HttpStatusCode.BadRequest, body)),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var evidence = Evidence(result);
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(1, evidence.Hops.Count);
        Assert.AreEqual(400, evidence.Hops[0].Status);
        Assert.AreEqual(171UL, evidence.Hops[0].Length);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.AreEqual(EuDocumentFetchRefusal.WrongAcceptToken, outcome.Refusal);
        Assert.AreEqual(400, outcome.ObservedStatus);
    }

    /// <summary>
    /// The 1995 directive (CELEX 31995L0046): xhtml and pdfa2a share one real observed 404 body/shape
    /// ("does not hold a content datastream of the requested type"), independently retained from two
    /// separate real probes and confirmed byte-identical here.
    /// </summary>
    [TestMethod]
    public async Task OldDirectiveXhtmlAndPdfa2aShareTheRealObserved404Body()
    {
        var xhtmlBody = LoadFixture(
            "old-xhtml-404-body.bin",
            "7da546e1547658968cbccbd3011280f0efb86300a9bee628573eb4c0c2bb19da");
        var pdfa2aBody = LoadFixture(
            "old-pdfa2a-404-body.bin",
            "7da546e1547658968cbccbd3011280f0efb86300a9bee628573eb4c0c2bb19da");
        Assert.AreEqual(214, xhtmlBody.Length);
        CollectionAssert.AreEqual(xhtmlBody, pdfa2aBody, "the two real probes observed byte-identical bodies.");

        foreach (var mediaType in new[] { EuManifestationMediaType.XhtmlXml, EuManifestationMediaType.PdfTypePdfa2a })
        {
            var bound = BindDocumentFetchRequest(OldDirectiveCelex, mediaType, out _);
            using var session = Session(
                bound,
                DocumentFetchHandler(
                    (_, outbound) => DeclaredBinaryResponse(outbound, HttpStatusCode.NotFound, xhtmlBody)),
                new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
                new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
                usesPinnedHandler: false);

            var started = await BootstrapAsync(session);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

            var item = session.OpenPlanItem(bound);
            var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
            var evidence = Evidence(result);
            Assert.AreEqual(1, evidence.Hops.Count, mediaType.ToString());
            Assert.AreEqual(404, evidence.Hops[0].Status, mediaType.ToString());

            var outcome = EuDocumentFetchOutcome.Classify(evidence);
            Assert.AreEqual(EuDocumentFetchRefusal.RequestedRepresentationNotServed, outcome.Refusal, mediaType.ToString());
        }
    }

    /// <summary>
    /// The 1995 directive's fmx4 404 carries its own distinct real body/wording ("Not found
    /// manifestation ... matching manifestation type [[fmx4]]"), independently retained and proven
    /// distinct from the xhtml/pdfa2a shape above.
    /// </summary>
    [TestMethod]
    public async Task OldDirectiveFmx4CarriesItsOwnDistinctReal404Body()
    {
        var fmx4Body = LoadFixture(
            "old-fmx4-404-body.bin",
            "23c63773cf77b03b9a785dd0c069fe79774e243949e17bb76dbbe0e91bea6d74");
        Assert.AreEqual(209, fmx4Body.Length);

        var bound = BindDocumentFetchRequest(OldDirectiveCelex, EuManifestationMediaType.ZipMtypeFmx4, out _);
        using var session = Session(
            bound,
            DocumentFetchHandler((_, outbound) => DeclaredBinaryResponse(outbound, HttpStatusCode.NotFound, fmx4Body)),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var evidence = Evidence(result);
        Assert.AreEqual(404, evidence.Hops[0].Status);
        Assert.AreEqual(209UL, evidence.Hops[0].Length);
        Assert.AreEqual(
            "23c63773cf77b03b9a785dd0c069fe79774e243949e17bb76dbbe0e91bea6d74",
            evidence.Hops[0].Sha256);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.AreEqual(EuDocumentFetchRefusal.RequestedRepresentationNotServed, outcome.Refusal);
    }

    /// <summary>
    /// Even the 1995 directive succeeds once the right manifestation is requested: its real RDF/XML
    /// object metadata, reached through a real http-Location 303 (the same upgrade defect 1 fixes) to
    /// a real 3388-byte body. See this file's own remarks for why this admitted-media-type case
    /// stands in for the reviewer-cited (but non-admitted-media-type) html observation.
    /// </summary>
    [TestMethod]
    public async Task OldDirectiveSucceedsWithItsRealRdfXmlManifestation()
    {
        var body = LoadFixture(
            "old-rdfxml-200-body.bin",
            "1277674bb408efb0cb2f039b7df6bc60d2ddf93a658483e636a49f94ffff52ca");
        Assert.AreEqual(3388, body.Length);

        var bound = BindDocumentFetchRequest(OldDirectiveCelex, EuManifestationMediaType.RdfXml, out _);
        using var session = Session(
            bound,
            DocumentFetchHandler((ordinal, outbound) => ordinal switch
            {
                2 => DeclaredBinaryResponse(outbound, HttpStatusCode.SeeOther, [], location: OldRdfXmlLocation),
                _ => DeclaredBinaryResponse(
                    outbound, HttpStatusCode.OK, body, contentType: "application/rdf+xml;charset=UTF-8"),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var evidence = Evidence(result);
        Assert.AreEqual(2, evidence.Hops.Count);
        Assert.AreEqual(
            "https://publications.europa.eu/resource/cellar/775a4724-2086-4a06-9213-1a4e6489053b.0008.01/rdf/object/full",
            evidence.Hops[1].RequestUri);
        Assert.AreEqual(200, evidence.Hops[1].Status);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.IsNull(outcome.Refusal);
        Assert.AreEqual(200, outcome.ObservedStatus);
    }

    /// <summary>
    /// The 2026 act (CELEX 32026R1965): all three canary formats succeed for real. Each real
    /// http-Location redirect is upgraded and followed to its real observed 200 body.
    /// </summary>
    [TestMethod]
    public async Task NewActXhtmlSucceedsWithItsRealObserved200()
    {
        var body = LoadFixture(
            "new-xhtml-200-body.bin",
            "dd520158036710918d3e26a97798ac8568dcd8a85d523812356d5150a82b0a7b");
        Assert.AreEqual(50087, body.Length);
        await AssertNewActSucceeds(
            EuManifestationMediaType.XhtmlXml, NewXhtmlLocation, body,
            "application/xhtml+xml;charset=UTF-8",
            "3649f0b6-a1af-11f1-b25c-01aa75ed71a1.0006.03/DOC_1");
    }

    [TestMethod]
    public async Task NewActPdfa2aSucceedsWithItsRealObserved200()
    {
        var body = LoadFixture(
            "new-pdfa2a-200-body.bin",
            "72f2fc7053b7532f4515e00c365c3f996e54fab075ddb47a9cd1bd151cd4255f");
        Assert.AreEqual(992748, body.Length);
        await AssertNewActSucceeds(
            EuManifestationMediaType.PdfTypePdfa2a, NewPdfa2aLocation, body,
            "application/pdf;type=pdfa2a;charset=UTF-8",
            "3649f0b6-a1af-11f1-b25c-01aa75ed71a1.0006.01/DOC_1");
    }

    [TestMethod]
    public async Task NewActFmx4SucceedsWithItsRealObserved200()
    {
        var body = LoadFixture(
            "new-fmx4-200-body.bin",
            "c659de4cc28be83f58758f362b60150933f196afb97422158f0d984d7906976c");
        Assert.AreEqual(11266, body.Length);
        await AssertNewActSucceeds(
            EuManifestationMediaType.ZipMtypeFmx4, NewFmx4Location, body,
            "application/zip;charset=UTF-8",
            "3649f0b6-a1af-11f1-b25c-01aa75ed71a1.0006.02/zip");
    }

    private static async Task AssertNewActSucceeds(
        EuManifestationMediaType mediaType,
        string realLocation,
        byte[] body,
        string contentType,
        string expectedUpgradedSuffix)
    {
        var bound = BindDocumentFetchRequest(NewActCelex, mediaType, out _);
        using var session = Session(
            bound,
            DocumentFetchHandler((ordinal, outbound) => ordinal switch
            {
                2 => DeclaredBinaryResponse(outbound, HttpStatusCode.SeeOther, [], location: realLocation),
                _ => DeclaredBinaryResponse(outbound, HttpStatusCode.OK, body, contentType: contentType),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var evidence = Evidence(result);
        Assert.AreEqual(2, evidence.Hops.Count);
        Assert.AreEqual(
            "https://publications.europa.eu/resource/cellar/" + expectedUpgradedSuffix,
            evidence.Hops[1].RequestUri);
        Assert.AreEqual(200, evidence.Hops[1].Status);
        Assert.AreEqual((ulong)body.Length, evidence.Hops[1].Length);

        var outcome = EuDocumentFetchOutcome.Classify(evidence);
        Assert.IsNull(outcome.Refusal);
        Assert.AreEqual(200, outcome.ObservedStatus);
    }

    /// <summary>
    /// The other half of item 1: a well-formed absolute-HTTPS redirect target on a genuinely
    /// different host is a typed refusal, never silently followed. This is a structural boundary
    /// condition with no real canary counterpart (the office never actually redirects off its own
    /// host), so it stays a deliberately synthetic, clearly-labelled edge case rather than a claimed
    /// observation. <see cref="HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted"/>.
    /// </summary>
    [TestMethod]
    public async Task OffOriginRedirectIsRefusedAsATypedRouteOutcomeNeverFollowed()
    {
        var bound = BindDocumentFetchRequest(GdprCelex, EuManifestationMediaType.XhtmlXml, out _);
        const string offOriginTarget = "https://not-publications.europa.eu.example.invalid/elsewhere";
        using var session = Session(
            bound,
            DocumentFetchHandler((ordinal, outbound) => ordinal switch
            {
                2 => DeclaredBinaryResponse(outbound, HttpStatusCode.SeeOther, [], location: offOriginTarget),
                _ => throw new AssertFailedException("No further hop is expected after an off-origin refusal."),
            }),
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore(),
            new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var item = session.OpenPlanItem(bound);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);

        var evidence = Evidence(result);
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
        var bound = BindDocumentFetchRequest(GdprCelex, EuManifestationMediaType.XhtmlXml, out var address);
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

    // ---- Shared plumbing. ----

    private static BoundMachineRequest BindDocumentFetchRequest(
        string celex, EuManifestationMediaType mediaType, out EuDocumentFetchAddress address)
    {
        address = EuDocumentFetchAddress.TryCreate(
            "celex",
            celex,
            mediaType,
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

    /// <summary>
    /// The real robots negotiation (302-to-op.europa.eu, then a real 200) every document-fetch
    /// session bootstraps through before its first product request, exactly as the retained
    /// <c>eu-robots.txt</c> fixture proves (see <see cref="RobotsFixtureBytesMatchTheRetainedCanaryDigest"/>).
    /// Ordinal 0/1 are that bootstrap; ordinal 2 onward is the caller's own product response.
    /// </summary>
    private static SequenceHandler DocumentFetchHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> productResponses) =>
        new((ordinal, outbound) => ordinal switch
        {
            0 => DeclaredBinaryResponse(
                outbound, HttpStatusCode.MovedPermanently, [], location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredBinaryResponse(
                outbound, HttpStatusCode.OK, RobotsFixtureBytes(), contentType: "text/plain;charset=UTF-8"),
            _ => productResponses(ordinal, outbound),
        });

    /// <summary>
    /// Defect fold-in: cites the retained robots digest directly, and proves the embedded robots
    /// fixture used to bootstrap every test above is the real <c>publications.europa.eu</c> robots.txt,
    /// not a fabricated two-line stand-in.
    /// </summary>
    [TestMethod]
    public void RobotsFixtureBytesMatchTheRetainedCanaryDigest()
    {
        var bytes = RobotsFixtureBytes();
        Assert.IsTrue(bytes.Length > 1_500, "the real robots.txt carries many real disallow groups.");
        StringAssert.Contains(Encoding.UTF8.GetString(bytes), "User-agent: *");
    }

    private static byte[] RobotsFixtureBytes() => LoadFixture(
        "eu-robots.txt",
        "de63106ad6607ba0bf3e313c31871d96ccc7e949ee0e29fa0b1c85a450305a75");

    /// <summary>
    /// Loads one retained canary fixture and re-hashes it on every run: a fixture whose checked-in
    /// bytes ever drifted from the digest cited in the reviewer's own evidence list fails here, loudly,
    /// rather than silently asserting against whatever the file happens to contain.
    /// </summary>
    private static byte[] LoadFixture(string fileName, string expectedSha256)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", fileName);
        var bytes = File.ReadAllBytes(path);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.AreEqual(
            expectedSha256, actual, $"Fixture '{fileName}' no longer matches its retained canary digest.");
        return bytes;
    }

    private static HttpResponseMessage DeclaredBinaryResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        byte[] body,
        string? location = null,
        string? contentType = null)
    {
        var content = new ByteArrayContent(body);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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

    private static RoutedHttpEvidence Evidence(object attemptResult) =>
        (RoutedHttpEvidence)attemptResult.GetType()
            .GetProperty("Evidence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(attemptResult)!;

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
