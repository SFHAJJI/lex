using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// R8's one bounded GET of the EUR-Lex legal notice, frozen as the class-level evidence
/// <see cref="EuRightsDisposition"/> and <see cref="EuRightsExceptionDisposition"/> already declare
/// a reference slot for.
///
/// <see cref="ACleanCaptureHasTheExactClosedShapeAndRoundTrips"/> is not a synthetic fixture: the
/// requested URI, byte length and SHA-256 are the real values from the 2026-09-03 capture recorded
/// in the companion measurement file, so this test is also the executable record that the capture
/// parses back through this type unchanged.
/// </summary>
[TestClass]
public sealed class EuLegalNoticeEvidenceTests
{
    private const string RealSha256 =
        "489c635573a9c4eb39e30702d0c2a62eaaff4632a0bd9b7e300d6e2a3111861f";

    [TestMethod]
    public void ACleanCaptureHasTheExactClosedShapeAndRoundTrips()
    {
        var evidence = RealCapture();
        var bytes = evidence.CopyCanonicalBytes();
        var expected =
            "{\"schema\":\"lex-eu-legal-notice-evidence/1\"," +
            "\"requested_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"," +
            "\"effective_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"," +
            "\"redirects\":[]," +
            "\"final_status\":200," +
            "\"media_type\":{\"kind\":\"single\",\"value\":\"text/html; charset=UTF-8\"}," +
            "\"publisher_date\":{\"kind\":\"single\",\"value\":\"Thu, 03 Sep 2026 16:55:19 GMT\"}," +
            "\"publisher_last_modified\":{\"kind\":\"absent\"}," +
            "\"byte_length\":135428," +
            $"\"sha256\":\"{RealSha256}\"," +
            "\"captured_at\":\"2026-09-03T16:55:19.3670000Z\"}\n";
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), bytes);

        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        CollectionAssert.AreEqual(
            new[]
            {
                "schema", "requested_uri", "effective_uri", "redirects", "final_status",
                "media_type", "publisher_date", "publisher_last_modified", "byte_length", "sha256",
                "captured_at",
            },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());

        var reopened = EuLegalNoticeEvidence.ParseAndVerify(bytes);
        CollectionAssert.AreEqual(bytes, reopened.CopyCanonicalBytes());
        Assert.AreEqual(evidence.CanonicalSha256, reopened.CanonicalSha256);
    }

    [TestMethod]
    public void ACaptureWithARedirectChainRoundTrips()
    {
        var hop = new EuLegalNoticeRedirectHop(
            0,
            EuLegalNoticeEvidence.RequestedUri,
            301,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
        var evidence = new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr",
            [hop],
            200,
            new RoutedHttpSingleHeader("text/html; charset=UTF-8"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            42,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z");

        var reopened = EuLegalNoticeEvidence.ParseAndVerify(evidence.CopyCanonicalBytes());
        Assert.AreEqual(1, reopened.Redirects.Count);
        Assert.AreEqual(301, reopened.Redirects[0].Status);
        Assert.AreEqual(
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr",
            reopened.EffectiveUri);
    }

    [TestMethod]
    public void ParseAndVerifyRefusesTheWrongSchema()
    {
        var json = Encoding.UTF8.GetString(RealCapture().CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace(
                "\"lex-eu-legal-notice-evidence/1\"",
                "\"lex-eu-legal-notice-evidence/2\"",
                StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "wrong schema");
    }

    [TestMethod]
    public void ParseAndVerifyRefusesAMediaTypeThatIsNotASingleObservedHeader()
    {
        // Same two properties in the same order ("kind", "value"), so this clears the property-name
        // shape check and reaches the semantic one: "multiple" is not "single".
        var json = Encoding.UTF8.GetString(RealCapture().CopyCanonicalBytes());
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
        // A byte-identical round trip is the whole point of a canonical form. This document keeps
        // every key in its exact required order and every field individually valid, so it clears
        // RequireExactPropertyNames; the extra space after "final_status" is still real: the
        // canonical writer never emits one, so re-encoding the parsed fields cannot reproduce
        // these exact input bytes.
        var json = Encoding.UTF8.GetString(RealCapture().CopyCanonicalBytes());
        var tampered = Encoding.UTF8.GetBytes(
            json.Replace("\"final_status\":200", "\"final_status\": 200", StringComparison.Ordinal));

        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => EuLegalNoticeEvidence.ParseAndVerify(tampered));
        StringAssert.Contains(thrown.Message, "exact canonical typed representation");
    }

    [TestMethod]
    public void TheRequestedUriIsPinnedAndAnyOtherValueIsRefused()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr",
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
        StringAssert.Contains(thrown.Message, "exact R8 URI");
    }

    [TestMethod]
    public void TheEffectiveUriMustBeAnAbsoluteHttpsUri()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            "http://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en",
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
    }

    [TestMethod]
    public void ARedirectHopMustBeRequestedAtItsPredecessorsExactUri()
    {
        // The one hop present claims to have been requested at the wrong URI: neither the
        // requested URI (there is no predecessor hop) nor anything a predecessor produced.
        var hop = new EuLegalNoticeRedirectHop(
            0,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=de",
            301,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr",
            [hop],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
        StringAssert.Contains(thrown.Message, "predecessor");
    }

    [TestMethod]
    public void TheRedirectChainMustTerminateAtTheDeclaredEffectiveUri()
    {
        var hop = new EuLegalNoticeRedirectHop(
            0,
            EuLegalNoticeEvidence.RequestedUri,
            301,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            // Declared effective URI does not match where the one hop's Location actually points.
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=de",
            [hop],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
        StringAssert.Contains(thrown.Message, "does not terminate");
    }

    [TestMethod]
    public void RedirectHopsMustBeOrderedFromZeroWithoutAGap()
    {
        var hop = new EuLegalNoticeRedirectHop(
            1, // should be 0
            EuLegalNoticeEvidence.RequestedUri,
            301,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr",
            [hop],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
        StringAssert.Contains(thrown.Message, "ordered from zero");
    }

    [TestMethod]
    public void TooManyRedirectHopsAreRefused()
    {
        var hops = Enumerable.Range(0, EuLegalNoticeEvidence.MaximumRedirectHops + 1)
            .Select(index => new EuLegalNoticeRedirectHop(
                (ulong)index,
                "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en",
                301,
                "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en"))
            .ToArray();
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en",
            hops,
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
        StringAssert.Contains(thrown.Message, "at most");
    }

    [TestMethod]
    public void ARedirectHopStatusMustBeAThreeDigitRedirectRange()
    {
        foreach (var status in new[] { 200, 299, 400 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EuLegalNoticeRedirectHop(
                0,
                EuLegalNoticeEvidence.RequestedUri,
                status,
                "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr"));
        }

        // The boundaries are legal.
        _ = new EuLegalNoticeRedirectHop(
            0, EuLegalNoticeEvidence.RequestedUri, 300,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
        _ = new EuLegalNoticeRedirectHop(
            0, EuLegalNoticeEvidence.RequestedUri, 399,
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=fr");
    }

    [TestMethod]
    public void FinalStatusMustBeExactlyTwoHundred()
    {
        foreach (var status in new[] { 0, 199, 201, 404, 599 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EuLegalNoticeEvidence(
                EuLegalNoticeEvidence.RequestedUri,
                EuLegalNoticeEvidence.RequestedUri,
                [],
                status,
                new RoutedHttpSingleHeader("text/html"),
                new RoutedHttpAbsentHeader(),
                new RoutedHttpAbsentHeader(),
                1,
                new string('a', 64),
                "2026-09-03T16:55:19.3670000Z"));
        }
    }

    [TestMethod]
    public void MediaTypeCannotBeNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            null!,
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
    }

    [TestMethod]
    public void PublisherDateCannotBeNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            null!,
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
    }

    [TestMethod]
    public void PublisherLastModifiedCannotBeNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            null!,
            1,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z"));
    }

    [TestMethod]
    public void PublisherDateAndLastModifiedIndependentlyRecordPresenceOrAbsence()
    {
        // The real capture behind this type observed exactly this combination: a Date header and
        // no Last-Modified. Both branches of the union are exercised, not asserted in the abstract.
        var evidence = RealCapture();
        Assert.IsInstanceOfType<RoutedHttpSingleHeader>(evidence.PublisherDate);
        Assert.IsInstanceOfType<RoutedHttpAbsentHeader>(evidence.PublisherLastModified);
    }

    [TestMethod]
    public void ByteLengthMustBeBoundedAndPositive()
    {
        foreach (var length in new ulong[] { 0, EuLegalNoticeEvidence.MaximumNoticeBytes + 1 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EuLegalNoticeEvidence(
                EuLegalNoticeEvidence.RequestedUri,
                EuLegalNoticeEvidence.RequestedUri,
                [],
                200,
                new RoutedHttpSingleHeader("text/html"),
                new RoutedHttpAbsentHeader(),
                new RoutedHttpAbsentHeader(),
                length,
                new string('a', 64),
                "2026-09-03T16:55:19.3670000Z"));
        }

        // The boundary itself is legal.
        _ = new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            EuLegalNoticeEvidence.MaximumNoticeBytes,
            new string('a', 64),
            "2026-09-03T16:55:19.3670000Z");
    }

    [TestMethod]
    public void Sha256MustBeAValidDigest()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            "not-a-digest",
            "2026-09-03T16:55:19.3670000Z"));
    }

    [TestMethod]
    public void CapturedAtMustBeTheExactTimestampForm()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuLegalNoticeEvidence(
            EuLegalNoticeEvidence.RequestedUri,
            EuLegalNoticeEvidence.RequestedUri,
            [],
            200,
            new RoutedHttpSingleHeader("text/html"),
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            1,
            new string('a', 64),
            "2026-09-03"));
    }

    [TestMethod]
    public void ToArtifactRefBindsTheCallerSuppliedResourceIdToTheCanonicalDigestNotTheNoticeDigest()
    {
        var evidence = RealCapture();
        var reference = evidence.ToArtifactRef("urn:uuid:00000000-0000-4000-8000-0000000000aa");

        Assert.AreEqual("urn:uuid:00000000-0000-4000-8000-0000000000aa", reference.ResourceId);
        Assert.AreEqual(evidence.CanonicalSha256, reference.Sha256);
        // The two digests answer different questions and must not collapse into one number: the
        // notice's own bytes versus this evidence record's own canonical encoding of them.
        Assert.AreNotEqual(evidence.Sha256, reference.Sha256);
    }

    [TestMethod]
    public void TheProducedReferenceConstructsARealRightsDispositionAgainstTheCapture()
    {
        // The gap this type closes, made concrete: EuRightsDisposition already declares a
        // SourceArtifactRef evidence slot with nothing producing it. This wires a real capture
        // through to a real disposition instead of the tests' placeholder digest.
        var evidence = RealCapture();
        var reference = evidence.ToArtifactRef("urn:uuid:00000000-0000-4000-8000-0000000000bb");

        var disposition = new EuRightsDisposition(
            EuContentClass.OriginalLegalText,
            EuRightsDisposition.BasisFor(EuContentClass.OriginalLegalText),
            reference);

        Assert.AreEqual(EuReuseBasis.EurLexLegalNoticePermission, disposition.Basis);
        Assert.AreEqual(evidence.CanonicalSha256, disposition.EvidenceRef.Sha256);
    }

    /// <summary>
    /// The real 2026-09-03 capture: zero redirects, a Date header, no Last-Modified, 135,428 bytes.
    /// See the companion measurement file for the full curl transcript this reproduces.
    /// </summary>
    private static EuLegalNoticeEvidence RealCapture() => new(
        EuLegalNoticeEvidence.RequestedUri,
        EuLegalNoticeEvidence.RequestedUri,
        [],
        200,
        new RoutedHttpSingleHeader("text/html; charset=UTF-8"),
        new RoutedHttpSingleHeader("Thu, 03 Sep 2026 16:55:19 GMT"),
        new RoutedHttpAbsentHeader(),
        135_428,
        RealSha256,
        "2026-09-03T16:55:19.3670000Z");
}
