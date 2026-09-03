using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// R8's one bounded GET of the EUR-Lex legal notice, refrozen as a door over a proven route:
/// <see cref="EuLegalNoticeEvidence.FromRoute"/> takes a real <see cref="RoutedHttpEvidence"/>
/// together with the <see cref="HttpLogicalRequest"/> that produced it, the same shape
/// <c>RepresentationChainObservation.FromRoute</c> already established for item 9.
///
/// <see cref="ACleanCaptureHasTheExactClosedShapeAndRoundTrips"/> is not a synthetic fixture: the
/// requested URI, byte length, SHA-256, media type, Date header and captured timestamp are the real
/// values from the 2026-09-03 capture recorded in
/// <c>coordination/measurements/2026-09-03-eu-legal-notice-capture.md</c>, now routed through a
/// genuine <see cref="RoutedHttpHop"/> and a receipt-checked <see cref="RoutedHttpEvidence"/> instead
/// of being passed directly to a public constructor.
/// </summary>
[TestClass]
public sealed class EuLegalNoticeEvidenceTests
{
    private const string RealSha256 =
        "489c635573a9c4eb39e30702d0c2a62eaaff4632a0bd9b7e300d6e2a3111861f";

    private const string RealCapturedAt = "2026-09-03T16:55:19.3670000Z";

    private const string RealDateHeader = "Thu, 03 Sep 2026 16:55:19 GMT";

    private const string RealMediaType = "text/html; charset=UTF-8";

    private const ulong RealByteLength = 135_428;

    [TestMethod]
    public void ACleanCaptureHasTheExactClosedShapeAndRoundTrips()
    {
        var (evidence, request) = RealRoute();
        var routedEvidenceSha256 = Convert.ToHexString(
            SHA256.HashData(evidence.CopyCanonicalBytes())).ToLowerInvariant();

        var notice = EuLegalNoticeEvidence.FromRoute(evidence, request);
        var bytes = notice.CopyCanonicalBytes();
        var expected =
            "{\"schema\":\"lex-eu-legal-notice-evidence/2\"," +
            "\"requested_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"," +
            "\"effective_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"," +
            "\"language_selection\":\"en\"," +
            "\"media_type\":{\"kind\":\"single\",\"value\":\"text/html; charset=UTF-8\"}," +
            "\"observed_date\":{\"kind\":\"single\",\"value\":\"Thu, 03 Sep 2026 16:55:19 GMT\"}," +
            "\"policy_effective_date\":{\"kind\":\"absent\"}," +
            "\"source_policy_version\":{\"kind\":\"absent\"}," +
            "\"byte_length\":135428," +
            $"\"sha256\":\"{RealSha256}\"," +
            $"\"durable_write_receipt_sha256\":\"{WriteReceiptDigest(RealSha256, RealByteLength)}\"," +
            $"\"routed_evidence_sha256\":\"{routedEvidenceSha256}\"," +
            $"\"captured_at\":\"{RealCapturedAt}\"}}\n";
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), bytes);

        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        CollectionAssert.AreEqual(
            new[]
            {
                "schema", "requested_uri", "effective_uri", "language_selection", "media_type",
                "observed_date", "policy_effective_date", "source_policy_version", "byte_length",
                "sha256", "durable_write_receipt_sha256", "routed_evidence_sha256", "captured_at",
            },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());

        var reopened = EuLegalNoticeEvidence.ParseAndVerify(bytes);
        CollectionAssert.AreEqual(bytes, reopened.CopyCanonicalBytes());
        Assert.AreEqual(notice.CanonicalSha256, reopened.CanonicalSha256);
    }

    [TestMethod]
    public void RoutedEvidenceSha256ChangesWithTheReferencedRouteRatherThanBeingACopiedLiteral()
    {
        // The refreeze objection was that the prior version bound a digest of bytes nothing held.
        // This proves RoutedEvidenceSha256 is a genuine function of the evidence object FromRoute
        // was actually given, not a value that would pass unchanged if a hostile caller substituted
        // a different (but still individually valid) route: two runs that differ only in their
        // request ordinal must disagree on RoutedEvidenceSha256.
        var (evidenceOne, requestOne) = RealRoute(requestOrdinal: 1);
        var (evidenceTwo, requestTwo) = RealRoute(requestOrdinal: 2);

        var noticeOne = EuLegalNoticeEvidence.FromRoute(evidenceOne, requestOne);
        var noticeTwo = EuLegalNoticeEvidence.FromRoute(evidenceTwo, requestTwo);

        Assert.AreNotEqual(noticeOne.RoutedEvidenceSha256, noticeTwo.RoutedEvidenceSha256);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(evidenceOne.CopyCanonicalBytes())).ToLowerInvariant(),
            noticeOne.RoutedEvidenceSha256);
    }

    [TestMethod]
    public void DurableWriteReceiptSha256IsCarriedFromTheTerminalHopsOwnProvenReceipt()
    {
        var (evidence, request) = RealRoute();
        var notice = EuLegalNoticeEvidence.FromRoute(evidence, request);

        Assert.AreEqual(evidence.Hops[^1].DurableWriteReceiptSha256, notice.DurableWriteReceiptSha256);
    }

    [TestMethod]
    public void ObservedDateIsAbsentWhenThePublisherSendsNoDateHeader()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("no-date"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: RealMediaType, contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));
        var notice = EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request);

        Assert.IsInstanceOfType<RoutedHttpAbsentHeader>(notice.ObservedDate);
    }

    [TestMethod]
    public void PolicyEffectiveDateAndSourcePolicyVersionAreAlwaysAbsentFromFromRouteToday()
    {
        // Honest about what one bounded GET without a page-content parser can support: FromRoute
        // has no source for either field, so every real instance it mints carries both absent.
        var (evidence, request) = RealRoute();
        var notice = EuLegalNoticeEvidence.FromRoute(evidence, request);

        Assert.IsInstanceOfType<RoutedHttpAbsentHeader>(notice.PolicyEffectiveDate);
        Assert.IsInstanceOfType<RoutedHttpAbsentHeader>(notice.SourcePolicyVersion);
    }

    [TestMethod]
    public void ParseAndVerifyRoundTripsAPresentPolicyEffectiveDateAndSourcePolicyVersion()
    {
        // The typed-absence union's other branch, driven the same way the existing schema/media-type
        // tamper tests drive a refusal: edit the canonical JSON directly and re-parse. This is the
        // only way to reach the "present" branch, because FromRoute (the only production door) never
        // mints one today; ParseAndVerify must still round-trip it correctly for the day a
        // page-content parser starts supplying real values.
        var (evidence, request) = RealRoute();
        var notice = EuLegalNoticeEvidence.FromRoute(evidence, request);
        var json = Encoding.UTF8.GetString(notice.CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace(
                    "\"policy_effective_date\":{\"kind\":\"absent\"}",
                    "\"policy_effective_date\":{\"kind\":\"single\",\"value\":\"2011-12-12\"}",
                    StringComparison.Ordinal)
                .Replace(
                    "\"source_policy_version\":{\"kind\":\"absent\"}",
                    "\"source_policy_version\":{\"kind\":\"single\",\"value\":\"2011/833/EU\"}",
                    StringComparison.Ordinal));

        var reopened = EuLegalNoticeEvidence.ParseAndVerify(tampered);

        Assert.IsInstanceOfType<RoutedHttpSingleHeader>(reopened.PolicyEffectiveDate);
        Assert.AreEqual("2011-12-12", ((RoutedHttpSingleHeader)reopened.PolicyEffectiveDate).Value);
        Assert.IsInstanceOfType<RoutedHttpSingleHeader>(reopened.SourcePolicyVersion);
        Assert.AreEqual("2011/833/EU", ((RoutedHttpSingleHeader)reopened.SourcePolicyVersion).Value);
        CollectionAssert.AreEqual(tampered, reopened.CopyCanonicalBytes());
    }

    [TestMethod]
    public void ParseAndVerifyRefusesTheWrongSchema()
    {
        var (evidence, request) = RealRoute();
        var json = Encoding.UTF8.GetString(EuLegalNoticeEvidence.FromRoute(evidence, request).CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace(
                "\"lex-eu-legal-notice-evidence/2\"",
                "\"lex-eu-legal-notice-evidence/3\"",
                StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "wrong schema");
    }

    [TestMethod]
    public void ParseAndVerifyRefusesAWrongRequestedUri()
    {
        var (evidence, request) = RealRoute();
        var json = Encoding.UTF8.GetString(EuLegalNoticeEvidence.FromRoute(evidence, request).CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace(
                "\"requested_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"",
                "\"requested_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr\"",
                StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "exact R8 requested URI");
    }

    [TestMethod]
    public void ParseAndVerifyRefusesAWrongLanguageSelection()
    {
        var (evidence, request) = RealRoute();
        var json = Encoding.UTF8.GetString(EuLegalNoticeEvidence.FromRoute(evidence, request).CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace("\"language_selection\":\"en\"", "\"language_selection\":\"fr\"", StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "exact R8 language selection");
    }

    [TestMethod]
    public void ParseAndVerifyRefusesAMediaTypeThatIsNotASingleObservedHeader()
    {
        var (evidence, request) = RealRoute();
        var json = Encoding.UTF8.GetString(EuLegalNoticeEvidence.FromRoute(evidence, request).CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace(
                "\"media_type\":{\"kind\":\"single\",\"value\":\"text/html; charset=UTF-8\"}",
                "\"media_type\":{\"kind\":\"multiple\",\"value\":\"text/html; charset=UTF-8\"}",
                StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "one observed single header value");
    }

    [TestMethod]
    public void ParseAndVerifyRefusesBytesThatAreNotTheExactCanonicalEncoding()
    {
        var (evidence, request) = RealRoute();
        var json = Encoding.UTF8.GetString(EuLegalNoticeEvidence.FromRoute(evidence, request).CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace("\"byte_length\":135428", "\"byte_length\": 135428", StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "exact canonical typed representation");
    }

    [TestMethod]
    public void FromRouteAndParseAndVerifyRejectNullArguments()
    {
        var (evidence, request) = RealRoute();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => EuLegalNoticeEvidence.FromRoute(null!, request));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => EuLegalNoticeEvidence.FromRoute(evidence, null!));
    }

    [TestMethod]
    public void FromRouteRefusesANonGetRequest()
    {
        var postRequest = HttpLogicalRequest.Create(
            EuLegalNoticeEvidence.RequestedUri,
            HttpRequestMethod.Post,
            [
                new HttpLogicalRequestHeader("user-agent", "Lex/0.1 (+https://github.com/SFHAJJI/lex)"),
                new HttpLogicalRequestHeader("content-type", "application/x-www-form-urlencoded"),
            ],
            new HttpLogicalRequestBody(3, Digest('a')),
            Digest('1'),
            Digest('2'));
        var hop = RoutedHttpHop.Create(
            0, Uuid("post"), null, RequestDigest(postRequest), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: RealMediaType, contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), postRequest));
        StringAssert.Contains(thrown.Message, "only be minted from a GET");
    }

    [TestMethod]
    public void FromRouteRefusesARequestThatIsNotTheOneTheTerminalHopActuallySent()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var otherRequest = LogicalRequestFor("https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
        var hop = RoutedHttpHop.Create(
            0, Uuid("mismatched"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: RealMediaType, contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), otherRequest));
        StringAssert.Contains(thrown.Message, "not the one the terminal hop actually sent");
    }

    [TestMethod]
    public void FromRouteRefusesARouteThatDidNotStartAtTheExactR8Uri()
    {
        // The route is otherwise perfectly self-consistent (the request digest ties correctly, the
        // status is 200, the media type is text/html); only the first hop's own RequestUri is not
        // the pinned R8 string.
        var wrongUri = "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr";
        var request = LogicalRequestFor(wrongUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("wrong-start"), null, RequestDigest(request), wrongUri, 200,
            Headers(contentType: RealMediaType, contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request));
        StringAssert.Contains(thrown.Message, "exact R8 URI");
    }

    [TestMethod]
    public void FromRouteRefusesANonTwoHundredTerminalStatus()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("not-found"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 404,
            Headers(contentType: RealMediaType, contentLength: "9"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(9), 9, Digest('e'), WriteReceiptDigest(Digest('e'), 9), 9, Digest('e'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request));
        StringAssert.Contains(thrown.Message, "complete 200 response");
    }

    [TestMethod]
    public void FromRouteRefusesAnAbsentMediaType()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("no-content-type"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request));
        StringAssert.Contains(thrown.Message, "text/html media type");
    }

    [TestMethod]
    public void FromRouteRefusesANonTextHtmlMediaType()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("json"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: "application/json", contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request));
        StringAssert.Contains(thrown.Message, "text/html media type");
    }

    [TestMethod]
    public void FromRouteRefusesAMediaTypeThatOnlyStartsWithTextHtmlAsASubstring()
    {
        // StartsWith alone would wrongly admit this; the check must compare the exact media-type
        // token before any ";" parameter, not merely a string prefix.
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("lookalike"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: "text/html-lookalike", contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request));
        StringAssert.Contains(thrown.Message, "text/html media type");
    }

    [TestMethod]
    public void FromRouteRefusesAMultipleValuedMediaType()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("multi-content-type"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            HeadersWithMultipleContentType(), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(hop), request));
        StringAssert.Contains(thrown.Message, "text/html media type");
    }

    [TestMethod]
    public void ByteLengthMustBeBoundedAndPositive()
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);

        var tooBig = EuLegalNoticeEvidence.MaximumNoticeBytes + 1;
        var bigHop = RoutedHttpHop.Create(
            0, Uuid("too-big"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: RealMediaType, contentLength: tooBig.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion((ulong)tooBig), (ulong)tooBig, Digest('a'),
            WriteReceiptDigest(Digest('a'), (ulong)tooBig), (ulong)tooBig, Digest('a'));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(bigHop), request));

        var zeroHop = RoutedHttpHop.Create(
            0, Uuid("zero"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: RealMediaType, contentLength: "0"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest, WriteReceiptDigest(EmptyDigest, 0), 0, EmptyDigest);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => EuLegalNoticeEvidence.FromRoute(EvidenceFor(zeroHop), request));

        // The boundary itself is legal.
        var boundaryHop = RoutedHttpHop.Create(
            0, Uuid("boundary"), null, RequestDigest(request), EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(
                contentType: RealMediaType,
                contentLength: EuLegalNoticeEvidence.MaximumNoticeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion((ulong)EuLegalNoticeEvidence.MaximumNoticeBytes),
            (ulong)EuLegalNoticeEvidence.MaximumNoticeBytes, Digest('b'),
            WriteReceiptDigest(Digest('b'), (ulong)EuLegalNoticeEvidence.MaximumNoticeBytes),
            (ulong)EuLegalNoticeEvidence.MaximumNoticeBytes, Digest('b'));
        var notice = EuLegalNoticeEvidence.FromRoute(EvidenceFor(boundaryHop), request);
        Assert.AreEqual((ulong)EuLegalNoticeEvidence.MaximumNoticeBytes, notice.ByteLength);
    }

    [TestMethod]
    public void ToArtifactRefBindsTheCallerSuppliedResourceIdToTheCanonicalDigestNotTheNoticeDigest()
    {
        var (evidence, request) = RealRoute();
        var notice = EuLegalNoticeEvidence.FromRoute(evidence, request);
        var reference = notice.ToArtifactRef("urn:uuid:00000000-0000-4000-8000-0000000000aa");

        Assert.AreEqual("urn:uuid:00000000-0000-4000-8000-0000000000aa", reference.ResourceId);
        Assert.AreEqual(notice.CanonicalSha256, reference.Sha256);
        Assert.AreNotEqual(notice.Sha256, reference.Sha256);
    }

    [TestMethod]
    public void TheProducedReferenceConstructsARealRightsDispositionAgainstTheCapture()
    {
        // The gap this type closes, made concrete: EuRightsDisposition already declares a
        // SourceArtifactRef evidence slot with nothing producing it. This wires a real, route-proven
        // capture through to a real disposition, and proves the consumer contract survived the
        // refreeze unchanged.
        var (evidence, request) = RealRoute();
        var notice = EuLegalNoticeEvidence.FromRoute(evidence, request);
        var reference = notice.ToArtifactRef("urn:uuid:00000000-0000-4000-8000-0000000000bb");

        var disposition = new EuRightsDisposition(
            EuContentClass.OriginalLegalText,
            EuRightsDisposition.BasisFor(EuContentClass.OriginalLegalText),
            reference);

        Assert.AreEqual(EuReuseBasis.EurLexLegalNoticePermission, disposition.Basis);
        Assert.AreEqual(notice.CanonicalSha256, disposition.EvidenceRef.Sha256);
    }

    /// <summary>
    /// The real 2026-09-03 capture, routed: zero redirects, a Date header, text/html, 135,428 bytes,
    /// backed by a genuine receipt-checked <see cref="RoutedHttpEvidence"/>. See the companion
    /// measurement file for the full curl transcript this reproduces.
    /// </summary>
    private static (RoutedHttpEvidence Evidence, HttpLogicalRequest Request) RealRoute(ulong requestOrdinal = 1)
    {
        var request = LogicalRequestFor(EuLegalNoticeEvidence.RequestedUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("real-capture-" + requestOrdinal), null, RequestDigest(request),
            EuLegalNoticeEvidence.RequestedUri, 200,
            Headers(contentType: RealMediaType, contentLength: RealByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture), date: RealDateHeader),
            RealCapturedAt, RealCapturedAt,
            new DeclaredContentLengthHttpCompletion(RealByteLength), RealByteLength, RealSha256,
            WriteReceiptDigest(RealSha256, RealByteLength), RealByteLength, RealSha256);
        return (EvidenceForOrdinal(requestOrdinal, hop), request);
    }

    private static RoutedHttpResponseHeaders Headers(
        string? contentType = null,
        string? contentLength = null,
        string? date = null,
        string? location = null)
    {
        RoutedHttpHeaderField Field(string? value) => value is null
            ? new RoutedHttpAbsentHeader()
            : new RoutedHttpSingleHeader(value);
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            Field(contentType),
            Field(contentLength),
            absent,
            absent,
            absent,
            absent,
            absent,
            Field(location),
            absent,
            absent,
            Field(date),
            absent,
            absent);
    }

    private static RoutedHttpResponseHeaders HeadersWithMultipleContentType()
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            new RoutedHttpMultipleHeader(["text/html", "application/xhtml+xml"]),
            new RoutedHttpSingleHeader("3"),
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent);
    }

    private static string Digest(char value) => new(value, 64);

    private static readonly string EmptyDigest =
        Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();

    private static string Time(int secondsOffset) =>
        $"2026-09-03T10:00:{secondsOffset:D2}.0000000Z";

    private static string Uuid(string tag)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tag));
        var guid = new Guid(hash[..16]);
        return "urn:uuid:" + guid.ToString("D");
    }

    private static HttpLogicalRequest LogicalRequestFor(string uri) =>
        HttpLogicalRequest.Create(
            uri,
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("user-agent", "Lex/0.1 (+https://github.com/SFHAJJI/lex)")],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));

    private static string RequestDigest(HttpLogicalRequest request) =>
        Convert.ToHexString(SHA256.HashData(request.CopyCanonicalBytes())).ToLowerInvariant();

    /// <summary>
    /// A genuine, internally consistent <see cref="DurableBlobWriteReceipt"/> for exactly the given
    /// content digest and length, so a hop built from it satisfies Decision 80's receipt check at
    /// <see cref="RoutedHttpEvidence.Create"/>. Mirrors the helper of the same name in
    /// RepresentationChainContractTests.cs and RoutedHttpEvidenceContractTests.cs; kept local rather
    /// than shared so each contract test file depends only on the Contracts types it already
    /// imports.
    /// </summary>
    private static string WriteReceiptDigest(string contentSha256, ulong length)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            contentSha256,
            checked((long)length),
            CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            policyKey: null,
            CustodyProtection.NotEnforced,
            new DateTimeOffset(2026, 9, 3, 16, 55, 0, TimeSpan.Zero),
            protectedUntil: null);
        var receipt = new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt))))
            .ToLowerInvariant();
    }

    private static RoutedHttpEvidence EvidenceFor(params RoutedHttpHop[] hops) =>
        EvidenceForOrdinal(1, hops);

    private static RoutedHttpEvidence EvidenceForOrdinal(
        ulong requestOrdinal, params RoutedHttpHop[] hops) =>
        RoutedHttpEvidence.Create(
            new SourceArtifactRef(Uuid("run-identity-" + requestOrdinal), Digest('1')),
            requestOrdinal,
            0,
            hops,
            new CompleteHttpRouteOutcome(),
            ReceiptsFor(hops));

    private static Dictionary<string, DurableBlobWriteReceipt> ReceiptsFor(IEnumerable<RoutedHttpHop> hops)
    {
        var receipts = new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal);
        foreach (var hop in hops)
        {
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                hop.Sha256,
                checked((long)hop.Length),
                CustodyClass.NightlyFloor90d);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                new DateTimeOffset(2026, 9, 3, 16, 55, 0, TimeSpan.Zero),
                protectedUntil: null);
            receipts[hop.ObservationId] =
                new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
        }

        return receipts;
    }
}
