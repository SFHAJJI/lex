using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05d: family M's decode, and the closed format ladder it mints.
/// </summary>
/// <remarks>
/// Every listing fixture here is a real observation, not an invention. The six-token listing is what
/// the family-M query shape returned live on 2026-09-04 for CELEX 32003L0088 (Cellar
/// 050dd964-4f94-4c61-ab50-89217a0d90e2) and for four sibling acts in the 1995 to 2008 band; the
/// five-token one is 32008R0593's own; the single unbound row is exactly what the query's
/// <c>FILTER NOT EXISTS</c> branch returned for a well-formed Cellar IRI the store holds no
/// manifestation for. Contracts-only: nothing here calls a store or a publisher endpoint.
/// </remarks>
[TestClass]
public sealed class EuManifestationListingDecodeTests
{
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    // 32003L0088, the Working Time Directive: Appendix A seed, pre-2004, and the object whose real
    // listing names xhtml AND html while the office serves only the second of them.
    private const string WorkingTimeRoot =
        "http://publications.europa.eu/resource/cellar/050dd964-4f94-4c61-ab50-89217a0d90e2";

    // 32008R0593, Rome I: the canary that forced the fall-through design.
    private const string RomeOneRoot =
        "http://publications.europa.eu/resource/cellar/3db0a06f-cae9-433d-a229-dde3e68d6dc7";

    private static readonly RepeatedEnumerationInterpretationProfile ManifestationProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ManifestationFacts);

    private static SourceArtifactRef Evidence(string label) =>
        new(
            $"urn:uuid:{new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("guid:" + label))[..16])}",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("evidence:" + label))).ToLowerInvariant());

    private static RepeatedEnumerationRow ListedRow(string parentIri, string listedType)
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parentIri),
            RepeatedEnumerationRdfTerm.Literal(listedType, XsdString, null),
            RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            RepeatedEnumerationRdfTerm.Literal(XsdString, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal(parentIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            RepeatedEnumerationRdfTerm.Literal(listedType, null, null),
            RepeatedEnumerationRdfTerm.Literal(XsdString, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1] }),
            Array.AsReadOnly(terms[5..10]));
    }

    private static RepeatedEnumerationRow AbsenceRow(string parentIri)
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parentIri),
            RepeatedEnumerationRdfTerm.Unbound(),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal(parentIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1] }),
            Array.AsReadOnly(terms[5..10]));
    }

    private static IReadOnlyDictionary<string, EuFormatObservation>? Decode(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        out EuManifestationListingRefusal refusal,
        out string? offendingIri,
        out string? offendingToken,
        params string[] closure) =>
        EuManifestationListingDecode.TryDecode(
            new HashSet<string>(closure.Length == 0 ? [WorkingTimeRoot, RomeOneRoot] : closure, StringComparer.Ordinal),
            rows,
            ManifestationProfile,
            Evidence("m"),
            out refusal,
            out offendingIri,
            out offendingToken);

    // ---- The decode itself, against a retained listing fixture. ----

    [TestMethod]
    public void TheRealSixTokenListingDecodesToTheLadderInItsClosedOrder()
    {
        var rows = new[]
        {
            ListedRow(WorkingTimeRoot, "fmx4"),
            ListedRow(WorkingTimeRoot, "html"),
            ListedRow(WorkingTimeRoot, "pdf"),
            ListedRow(WorkingTimeRoot, "pdfa1a"),
            ListedRow(WorkingTimeRoot, "print"),
            ListedRow(WorkingTimeRoot, "xhtml"),
        };

        var decoded = Decode(rows, out var refusal, out var iri, out var token);

        Assert.AreEqual(EuManifestationListingRefusal.None, refusal);
        Assert.IsNull(iri);
        Assert.IsNull(token);
        Assert.IsNotNull(decoded);
        Assert.HasCount(1, decoded!);

        var observation = decoded[WorkingTimeRoot];
        Assert.AreEqual(EuFormatBodyAdmission.BodyAdmitted, observation.Admission);
        Assert.AreEqual("listing_offers_wording_format", observation.ReasonCode);

        // The whole point of the slice: the ladder, in the closed order, filtered to this Work's own
        // listing. xhtml first even though the office listed fmx4 first; html second even though the
        // office listed it second-of-six; pdf last even though the office listed it third, because
        // the ruled order is XHTML, html, PDF/A, PDF and this Work lists no pdfa2a. fmx4, pdfa1a and
        // print are absent because this route can address none of them (see
        // EuDocumentFetchAddress.TryMediaTypeFor for what was observed for each).
        CollectionAssert.AreEqual(
            new[]
            {
                EuManifestationFormat.Xhtml, EuManifestationFormat.Html, EuManifestationFormat.Pdf,
            },
            observation.OrderedCandidates.ToArray());

        // The manifest row's single address is the FIRST candidate, never any other.
        Assert.AreEqual(EuManifestationFormat.Xhtml, observation.Format);
        Assert.AreEqual(observation.OrderedCandidates[0], observation.Format);

        // Every disposition names the observation it came from: family M's own delivery evidence.
        Assert.AreEqual(Evidence("m"), observation.EvidenceRef);
    }

    [TestMethod]
    public void RomeOneListsNoHtmlSoItsLadderSkipsThatRung()
    {
        // 32008R0593's real listing: fmx4, pdf, pdfa1a, print, xhtml. No html at all, which is why
        // the first canary could not see that html is itself listed-but-unservable elsewhere. The
        // ladder therefore skips its second rung entirely and goes xhtml then pdf: a rung the office
        // does not list is never attempted.
        var rows = new[]
        {
            ListedRow(RomeOneRoot, "fmx4"),
            ListedRow(RomeOneRoot, "pdf"),
            ListedRow(RomeOneRoot, "pdfa1a"),
            ListedRow(RomeOneRoot, "print"),
            ListedRow(RomeOneRoot, "xhtml"),
        };

        var decoded = Decode(rows, out var refusal, out _, out _);

        Assert.AreEqual(EuManifestationListingRefusal.None, refusal);
        CollectionAssert.AreEqual(
            new[] { EuManifestationFormat.Xhtml, EuManifestationFormat.Pdf },
            decoded![RomeOneRoot].OrderedCandidates.ToArray());
    }

    [TestMethod]
    public void TwoWorksInOneBatchEachGetTheirOwnListing()
    {
        var rows = new[]
        {
            ListedRow(WorkingTimeRoot, "html"),
            ListedRow(WorkingTimeRoot, "print"),
            ListedRow(RomeOneRoot, "print"),
            ListedRow(RomeOneRoot, "xhtml"),
        };

        var decoded = Decode(rows, out var refusal, out _, out _);

        Assert.AreEqual(EuManifestationListingRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded!);
        CollectionAssert.AreEqual(
            new[] { EuManifestationFormat.Html }, decoded![WorkingTimeRoot].OrderedCandidates.ToArray());
        CollectionAssert.AreEqual(
            new[] { EuManifestationFormat.Xhtml }, decoded[RomeOneRoot].OrderedCandidates.ToArray());
    }

    // ---- Each arm of the closed preference order, driven. ----

    [TestMethod]
    public void EachArmOfTheClosedPreferenceOrderIsDrivenIndividually()
    {
        var ladderRef = Evidence("ladder");

        // Arm one: xhtml wins over everything below it.
        Assert.AreEqual(
            EuManifestationFormat.Xhtml,
            EuManifestationListingDecode.Observe(
                [EuManifestationFormat.Xhtml, EuManifestationFormat.Html, EuManifestationFormat.PdfA2a],
                ladderRef).Format);

        // Arm two: html wins when xhtml is not listed. This is the pre-2004 arm.
        Assert.AreEqual(
            EuManifestationFormat.Html,
            EuManifestationListingDecode.Observe(
                [EuManifestationFormat.Html, EuManifestationFormat.PdfA2a], ladderRef).Format);

        // Arm three: PDF/A wins when neither wording format is listed, and still loses to them.
        Assert.AreEqual(
            EuManifestationFormat.PdfA2a,
            EuManifestationListingDecode.Observe([EuManifestationFormat.PdfA2a], ladderRef).Format);

        // Arm four: PDF, the ruled fourth rung, wins only when nothing above it is listed, and sorts
        // after PDF/A when both are.
        Assert.AreEqual(
            EuManifestationFormat.Pdf,
            EuManifestationListingDecode.Observe([EuManifestationFormat.Pdf], ladderRef).Format);
        CollectionAssert.AreEqual(
            new[] { EuManifestationFormat.PdfA2a, EuManifestationFormat.Pdf },
            EuManifestationListingDecode.Observe(
                [EuManifestationFormat.Pdf, EuManifestationFormat.PdfA2a], ladderRef)
                .OrderedCandidates.ToArray());

        // Arm five: print ALONE is never a body source, at any position.
        var printOnly = EuManifestationListingDecode.Observe([EuManifestationFormat.Print], ladderRef);
        Assert.AreEqual(EuManifestationFormat.Print, printOnly.Format);
        Assert.AreEqual(EuFormatBodyAdmission.BodyNotAdmitted, printOnly.Admission);
        Assert.AreEqual("listing_offers_print_only", printOnly.ReasonCode);
        Assert.HasCount(0, printOnly.OrderedCandidates);

        // Arm six: a listing this route cannot address as a wording body is a typed GAP, never the
        // permanent exclusion print alone is. Formex is real and parseable; this slice just has no
        // reviewed way to read it yet.
        var formexOnly = EuManifestationListingDecode.Observe(
            [EuManifestationFormat.Formex4, EuManifestationFormat.Print], ladderRef);
        Assert.AreEqual(EuManifestationFormat.Formex4, formexOnly.Format);
        Assert.AreEqual(EuFormatBodyAdmission.BodyNotAdmitted, formexOnly.Admission);
        Assert.AreEqual("listing_offers_no_addressable_wording_format", formexOnly.ReasonCode);
        Assert.HasCount(0, formexOnly.OrderedCandidates);
    }

    [TestMethod]
    public void ThePrintOnlyArmReachesNeverIngestAndTheFormexOnlyArmReachesTypedQuarantine()
    {
        // The join is consumed unchanged; this proves the two BodyNotAdmitted arms are genuinely
        // different answers downstream and not one answer wearing two reason codes.
        var ladderRef = Evidence("join");
        var printOnly = EuManifestationListingDecode.Observe([EuManifestationFormat.Print], ladderRef);
        var formexOnly = EuManifestationListingDecode.Observe([EuManifestationFormat.Formex4], ladderRef);

        Assert.IsTrue(
            EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(printOnly.Format),
            "print alone must land in the never-carries-a-body set, which is what makes it never_ingest.");
        Assert.IsFalse(
            EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(formexOnly.Format),
            "Formex must NOT land there: a typed gap is not a permanent exclusion.");
    }

    [TestMethod]
    public void EveryLadderMemberIsAddressableAndEveryAddressableFormatCarriesItsObservedAcceptToken()
    {
        // The ladder can never contain a rung this route has no Accept token for: that is what stops
        // a candidate list minting a request nobody has observed.
        foreach (var candidate in EuManifestationListingDecode.FormatLadder)
        {
            Assert.IsTrue(
                EuDocumentFetchAddress.TryMediaTypeFor(candidate, out _),
                $"{candidate} is on the ladder but has no admitted Accept token.");
        }

        Assert.IsTrue(EuDocumentFetchAddress.TryMediaTypeFor(EuManifestationFormat.Xhtml, out var xhtml));
        Assert.AreEqual(EuManifestationMediaType.XhtmlXml, xhtml);
        Assert.IsTrue(EuDocumentFetchAddress.TryMediaTypeFor(EuManifestationFormat.Html, out var html));
        Assert.AreEqual(EuManifestationMediaType.TextHtml, html);
        Assert.IsTrue(EuDocumentFetchAddress.TryMediaTypeFor(EuManifestationFormat.PdfA2a, out var pdfa2a));
        Assert.AreEqual(EuManifestationMediaType.PdfTypePdfa2a, pdfa2a);
        Assert.IsTrue(EuDocumentFetchAddress.TryMediaTypeFor(EuManifestationFormat.Pdf, out var pdf));
        Assert.AreEqual(EuManifestationMediaType.ApplicationPdf, pdf);

        // All four ruled rungs, in the ruled order, after RULING
        // lex-event-20260904T185339315Z-87d1510eccdc42a5947c41d2d8580744 admitted application/pdf.
        CollectionAssert.AreEqual(
            new[]
            {
                EuManifestationFormat.Xhtml, EuManifestationFormat.Html,
                EuManifestationFormat.PdfA2a, EuManifestationFormat.Pdf,
            },
            EuManifestationListingDecode.FormatLadder.ToArray());

        // Recorded rather than hidden: these three still have no admitted token, and none of them is
        // a ladder rung. pdfa1a and pdfa1b have never been observed serving at all: on 2026-09-04
        // application/pdf;type=pdfa1a answered 404 on all five acts probed, every one of which lists
        // pdfa1a. See TryMediaTypeFor's own remarks for each.
        foreach (var unaddressable in new[]
                 {
                     EuManifestationFormat.PdfA1a, EuManifestationFormat.PdfA1b,
                     EuManifestationFormat.Print,
                 })
        {
            Assert.IsFalse(
                EuDocumentFetchAddress.TryMediaTypeFor(unaddressable, out _),
                $"{unaddressable} must have no admitted Accept token on this route.");
        }
    }

    // ---- Typed absence: the office lists nothing. ----

    [TestMethod]
    public void AWorkTheOfficeListsNothingForCarriesNoFormatObservationAtAll()
    {
        var decoded = Decode([AbsenceRow(WorkingTimeRoot)], out var refusal, out _, out _);

        Assert.AreEqual(EuManifestationListingRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(0, decoded!);
        Assert.IsFalse(
            decoded.ContainsKey(WorkingTimeRoot),
            "an office that lists nothing must produce NO observation: inventing an empty listing " +
            "would turn 'nobody offers this a body' into 'we looked at a format'.");
    }

    [TestMethod]
    public void AnEmptyListingCannotBeObservedAtAll()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => EuManifestationListingDecode.Observe([], Evidence("empty")));
    }

    // ---- Refusals: nothing is dropped silently. ----

    [TestMethod]
    public void AManifestationTypeOutsideTheClosedVocabularyIsRefusedByName()
    {
        var rows = new[]
        {
            ListedRow(WorkingTimeRoot, "xhtml"),
            ListedRow(WorkingTimeRoot, "epub3"),
        };

        var decoded = Decode(rows, out var refusal, out var iri, out var token);

        Assert.IsNull(decoded, "an unknown listed type must refuse the whole decode, never be dropped.");
        Assert.AreEqual(EuManifestationListingRefusal.ManifestationTypeNotInVocabulary, refusal);
        Assert.AreEqual("epub3", token, "the refusal must name the offending token.");
        Assert.IsNull(iri);
    }

    [TestMethod]
    public void AListingRowNamingAParentOutsideTheClosureIsRefusedByName()
    {
        var decoded = Decode(
            [ListedRow(RomeOneRoot, "xhtml")],
            out var refusal,
            out var iri,
            out var token,
            WorkingTimeRoot);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuManifestationListingRefusal.ListingParentNotInClosure, refusal);
        Assert.AreEqual(RomeOneRoot, iri);
        Assert.IsNull(token);
    }

    [TestMethod]
    public void AnIriValuedOrLanguageTaggedManifestationTypeIsRefusedRatherThanReinterpreted()
    {
        // The office answers a plain xsd:string literal. An IRI-valued manifestation type is a
        // publisher shape change: reading STR() off it would silently invent a token.
        var iriValued = new[]
        {
            new RepeatedEnumerationRow(
                Array.AsReadOnly(new[]
                {
                    RepeatedEnumerationRdfTerm.Iri(WorkingTimeRoot),
                    RepeatedEnumerationRdfTerm.Iri("http://publications.europa.eu/resource/authority/xhtml"),
                    RepeatedEnumerationRdfTerm.Literal("iri", null, null),
                    RepeatedEnumerationRdfTerm.Literal("", null, null),
                    RepeatedEnumerationRdfTerm.Literal("", null, null),
                    RepeatedEnumerationRdfTerm.Literal(WorkingTimeRoot, null, null),
                    RepeatedEnumerationRdfTerm.Literal("iri", null, null),
                    RepeatedEnumerationRdfTerm.Literal("x", null, null),
                    RepeatedEnumerationRdfTerm.Literal("", null, null),
                    RepeatedEnumerationRdfTerm.Literal("", null, null),
                }),
                Array.AsReadOnly(Array.Empty<RepeatedEnumerationRdfTerm>()),
                Array.AsReadOnly(Array.Empty<RepeatedEnumerationRdfTerm>())),
        };

        Assert.IsNull(Decode(iriValued, out var iriRefusal, out _, out _));
        Assert.AreEqual(EuManifestationListingRefusal.ListingRowTermKindMismatch, iriRefusal);

        var languageTagged = new[]
        {
            new RepeatedEnumerationRow(
                Array.AsReadOnly(new[]
                {
                    RepeatedEnumerationRdfTerm.Iri(WorkingTimeRoot),
                    RepeatedEnumerationRdfTerm.Literal("xhtml", null, "en"),
                    RepeatedEnumerationRdfTerm.Literal("literal", null, null),
                    RepeatedEnumerationRdfTerm.Literal("", null, null),
                    RepeatedEnumerationRdfTerm.Literal("en", null, null),
                    RepeatedEnumerationRdfTerm.Literal(WorkingTimeRoot, null, null),
                    RepeatedEnumerationRdfTerm.Literal("literal", null, null),
                    RepeatedEnumerationRdfTerm.Literal("xhtml", null, null),
                    RepeatedEnumerationRdfTerm.Literal("", null, null),
                    RepeatedEnumerationRdfTerm.Literal("en", null, null),
                }),
                Array.AsReadOnly(Array.Empty<RepeatedEnumerationRdfTerm>()),
                Array.AsReadOnly(Array.Empty<RepeatedEnumerationRdfTerm>())),
        };

        Assert.IsNull(Decode(languageTagged, out var langRefusal, out _, out _));
        Assert.AreEqual(EuManifestationListingRefusal.ListingRowTermKindMismatch, langRefusal);
    }

    [TestMethod]
    public void ARowClaimingBothARealTypeAndTheAbsenceMarkerIsRefused()
    {
        var rows = new[] { ListedRow(WorkingTimeRoot, "xhtml"), AbsenceRow(WorkingTimeRoot) };

        Assert.IsNull(Decode(rows, out var refusal, out var iri, out _));
        Assert.AreEqual(EuManifestationListingRefusal.ListingContradictsItsOwnAbsenceRow, refusal);
        Assert.AreEqual(WorkingTimeRoot, iri);
    }

    // ---- The candidate-ladder guard both carriers share. ----

    [TestMethod]
    public void ACandidateListMustBeAStrictlyIncreasingSubsequenceOfTheClosedLadder()
    {
        var evidenceRef = Evidence("guard");

        // Out of ladder order.
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormatDisposition(
            EuManifestationFormat.Html,
            EuFormatBodyAdmission.BodyAdmitted,
            "listing_offers_wording_format",
            evidenceRef,
            [EuManifestationFormat.Html, EuManifestationFormat.Xhtml]));

        // A repeat.
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormatDisposition(
            EuManifestationFormat.Xhtml,
            EuFormatBodyAdmission.BodyAdmitted,
            "listing_offers_wording_format",
            evidenceRef,
            [EuManifestationFormat.Xhtml, EuManifestationFormat.Xhtml]));

        // A format that is not on the ladder at all. Formex is the example precisely because it is
        // a real, parseable body format this route simply has no reviewed way to read yet: being off
        // the ladder is not the same as being unreadable.
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormatDisposition(
            EuManifestationFormat.Xhtml,
            EuFormatBodyAdmission.BodyAdmitted,
            "listing_offers_wording_format",
            evidenceRef,
            [EuManifestationFormat.Xhtml, EuManifestationFormat.Formex4]));

        // A first candidate that is not the row's own single address.
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormatObservation(
            EuManifestationFormat.Html,
            EuFormatBodyAdmission.BodyAdmitted,
            "listing_offers_wording_format",
            evidenceRef,
            [EuManifestationFormat.Xhtml, EuManifestationFormat.Html]));

        // A class-level policy row carries no candidates, and that is admitted.
        var classLevel = new EuFormatDisposition(
            EuManifestationFormat.Print,
            EuFormatBodyAdmission.BodyNotAdmitted,
            "print_is_physical",
            evidenceRef);
        Assert.HasCount(0, classLevel.OrderedCandidates);
    }

    [TestMethod]
    public void EveryClosedFormatHasExactlyOneListedTypeTokenAndTheyRoundTrip()
    {
        Assert.HasCount(
            Enum.GetValues<EuManifestationFormat>().Length,
            EuManifestationListingDecode.ListedTypeTokens);
        foreach (var format in Enum.GetValues<EuManifestationFormat>())
        {
            Assert.IsTrue(
                EuManifestationListingDecode.ListedTypeTokens.Values.Contains(format),
                $"{format} has no listed-type token.");
        }

        // The seven tokens observed live on 2026-09-04 across the 1995 to 2008 band.
        foreach (var (token, expected) in new (string, EuManifestationFormat)[]
                 {
                     ("fmx4", EuManifestationFormat.Formex4),
                     ("html", EuManifestationFormat.Html),
                     ("pdf", EuManifestationFormat.Pdf),
                     ("pdfa1a", EuManifestationFormat.PdfA1a),
                     ("pdfa1b", EuManifestationFormat.PdfA1b),
                     ("print", EuManifestationFormat.Print),
                     ("xhtml", EuManifestationFormat.Xhtml),
                 })
        {
            Assert.AreEqual(expected, EuManifestationListingDecode.ListedTypeTokens[token]);
        }
    }
}
