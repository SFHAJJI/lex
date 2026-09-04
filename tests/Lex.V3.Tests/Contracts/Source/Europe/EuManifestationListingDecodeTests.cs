using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// 050dd964-4f94-4c61-ab50-89217a0d90e2) and for four sibling acts between 2003 and 2006; the
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

    // ---- The decode against the office's own retained response bytes. ----

    /// <summary>
    /// Family M's decode run against the exact SPARQL response bytes the publisher returned, rather
    /// than against a token set reconstructed in code.
    /// </summary>
    /// <remarks>
    /// The three fixtures under <c>Fixtures/EuManifestationListing</c> are the unmodified
    /// <c>application/sparql-results+json</c> bodies the family-M query shape received from
    /// <c>publications.europa.eu</c> on 2026-09-04 under User-Agent Lex/0.1, retained and evented in
    /// PROBE_RESULT lex-event-20260904T193609083Z-6d8f89361bd9473f8657a0f11b628ce3. Each one's
    /// SHA-256 is pinned below, so a fixture edited by hand fails here before its content is ever
    /// decoded.
    /// </remarks>
    [TestMethod]
    [DataRow(
        "32003L0088",
        "050dd964-4f94-4c61-ab50-89217a0d90e2",
        "ce1638196cf8585407f5fc98a47c79ac8665a25aa18a6bbbb92accf8e0433241",
        "fmx4,html,pdf,pdfa1a,print,xhtml")]
    [DataRow(
        "32008R0593",
        "3db0a06f-cae9-433d-a229-dde3e68d6dc7",
        "d7374d60c4e65e24379cb1615e2ee185ceed2de6e18a4a4b54aaeea562e810b0",
        "fmx4,pdf,pdfa1a,print,xhtml")]
    [DataRow(
        "31995L0046",
        "775a4724-2086-4a06-9213-1a4e6489053b",
        "72b2fb408a21d362205c37befbfc4b995183700417f5e756eb50b2a50b58cec2",
        "fmx4,html,pdf,pdfa1a,pdfa1b,print,xhtml")]
    public void TheRetainedPublisherResponseDecodesToTheTypesTheOfficeReallyListed(
        string celex, string cellarKey, string expectedDigest, string expectedTokens)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "EuManifestationListing",
            celex + "-manifestation-listing.json");
        var bytes = File.ReadAllBytes(path);
        Assert.AreEqual(
            expectedDigest,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            $"the retained {celex} listing response has been modified since it was observed.");

        var parentIri = "http://publications.europa.eu/resource/cellar/" + cellarKey;
        var rows = RowsFromSparqlJson(bytes);
        Assert.IsTrue(rows.Count > 0, "the retained response must carry rows.");

        var decoded = EuManifestationListingDecode.TryDecode(
            new HashSet<string>([parentIri], StringComparer.Ordinal),
            rows,
            ManifestationProfile,
            Evidence("retained"),
            out var refusal,
            out var iri,
            out var token);

        Assert.AreEqual(EuManifestationListingRefusal.None, refusal);
        Assert.IsNull(iri);
        Assert.IsNull(
            token,
            "every type the office really listed for these works must be in the closed vocabulary.");
        Assert.IsNotNull(decoded);

        // Every listed token round-trips through the closed vocabulary, so this asserts what the
        // office actually said rather than what this test hoped it said.
        var listed = rows
            .Select(row => row.Terms[1].Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.AreEqual(expectedTokens, string.Join(',', listed));

        var observation = decoded![parentIri];
        Assert.AreEqual(EuFormatBodyAdmission.BodyAdmitted, observation.Admission);
        foreach (var candidate in observation.OrderedCandidates)
        {
            Assert.IsTrue(
                listed.Contains(EuManifestationListingDecode.ListedTypeTokens
                    .First(entry => entry.Value == candidate).Key),
                $"{candidate} is a candidate but the office did not list it.");
        }
    }

    /// <summary>
    /// Reads the publisher's own <c>application/sparql-results+json</c> body into family M's row
    /// shape, in the projection order the plan declares. Deliberately minimal: it reads the bytes
    /// the office sent rather than re-deriving them.
    /// </summary>
    private static IReadOnlyList<RepeatedEnumerationRow> RowsFromSparqlJson(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var bindings = document.RootElement.GetProperty("results").GetProperty("bindings");
        var rows = new List<RepeatedEnumerationRow>(bindings.GetArrayLength());
        foreach (var binding in bindings.EnumerateArray())
        {
            string? Value(string name) =>
                binding.TryGetProperty(name, out var cell) ? cell.GetProperty("value").GetString() : null;
            string? Datatype(string name) =>
                binding.TryGetProperty(name, out var cell) && cell.TryGetProperty("datatype", out var d)
                    ? d.GetString()
                    : null;

            var value = Value("value");
            var terms = new[]
            {
                RepeatedEnumerationRdfTerm.Iri(Value("parent")!),
                value is null
                    ? RepeatedEnumerationRdfTerm.Unbound()
                    : RepeatedEnumerationRdfTerm.Literal(value, Datatype("value"), null),
                RepeatedEnumerationRdfTerm.Literal(Value("value_kind")!, null, null),
                RepeatedEnumerationRdfTerm.Literal(Value("datatype_iri") ?? "", null, null),
                RepeatedEnumerationRdfTerm.Literal(Value("language_tag") ?? "", null, null),
                RepeatedEnumerationRdfTerm.Literal(Value("parent")!, null, null),
                RepeatedEnumerationRdfTerm.Literal(Value("value_kind")!, null, null),
                RepeatedEnumerationRdfTerm.Literal(value ?? "", null, null),
                RepeatedEnumerationRdfTerm.Literal(Value("datatype_iri") ?? "", null, null),
                RepeatedEnumerationRdfTerm.Literal(Value("language_tag") ?? "", null, null),
            };
            rows.Add(new RepeatedEnumerationRow(
                Array.AsReadOnly(terms),
                Array.AsReadOnly(new[] { terms[0], terms[1] }),
                Array.AsReadOnly(terms[5..10])));
        }

        return rows;
    }

    // ---- The decode itself, against hand-built rows in the same shape. ----

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

        // The manifest row's single address is the FIRST candidate, never any other. The companion
        // assertion that OrderedCandidates[0] equals Format is gone: EuFormatObservation's own
        // constructor guard already enforces it, so it could not fail here and said nothing.
        Assert.AreEqual(EuManifestationFormat.Xhtml, observation.Format);

        // The evidence assertion that used to sit here compared the ref this test had just handed
        // in against itself and could not fail either. Whether a disposition names family M's own
        // delivery evidence rather than a sibling family's is decided by the ADAPTER, so it is
        // asserted where it can fail: EuQueryExecutionAdapterTests
        // .AMintedFormatDispositionNamesFamilyMsOwnDeliveryEvidenceAndNotFamilyPs.
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
        // a ladder rung. On 2026-09-04 application/pdf;type=pdfa1a answered 404 on all seven acts it
        // was probed on, every one of which lists pdfa1a; pdfa1b was never probed at all. See
        // TryMediaTypeFor's own remarks for what is evidence and what is merely unprobed.
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

    /// <summary>
    /// An unadmitted manifestation type refuses THAT WORK by name and lets every other Work through.
    /// </summary>
    /// <remarks>
    /// D1-05d's REVIEW_RESULT lex-event-20260904T192428840Z-a6a8ebd26c58436aafd109a55303c12e defect
    /// two: this used to refuse the whole decode, so one new type listed anywhere in the office's
    /// catalogue would have refused every EU run. The token is still named, and still never dropped
    /// silently; what changed is the blast radius.
    /// </remarks>
    [TestMethod]
    public void AnUnadmittedManifestationTypeQuarantinesOnlyItsOwnWork()
    {
        var rows = new[]
        {
            ListedRow(WorkingTimeRoot, "epub3"),
            ListedRow(WorkingTimeRoot, "xhtml"),
            ListedRow(RomeOneRoot, "xhtml"),
        };

        var decoded = Decode(rows, out var refusal, out var iri, out var token);

        Assert.AreEqual(
            EuManifestationListingRefusal.None,
            refusal,
            "one Work's unknown token must never refuse the whole decode.");
        Assert.IsNull(iri);
        Assert.AreEqual("epub3", token, "the offending token is still reported, never dropped.");
        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded!);

        // The offending Work is quarantined, by name, with no candidates.
        var quarantined = decoded[WorkingTimeRoot];
        Assert.AreEqual(EuFormatBodyAdmission.BodyNotAdmitted, quarantined.Admission);
        Assert.AreEqual("listing_type_not_admitted:epub3", quarantined.ReasonCode);
        Assert.HasCount(0, quarantined.OrderedCandidates);
        Assert.AreEqual(
            EuManifestationFormat.Xhtml,
            quarantined.Format,
            "the named format must be one the Work really listed, and never print.");

        // Its sibling in the same batch is untouched.
        var sibling = decoded[RomeOneRoot];
        Assert.AreEqual(EuFormatBodyAdmission.BodyAdmitted, sibling.Admission);
        CollectionAssert.AreEqual(
            new[] { EuManifestationFormat.Xhtml }, sibling.OrderedCandidates.ToArray());
    }

    /// <summary>
    /// A Work whose listing is print plus an unknown token must NOT reach never-ingest: an unread
    /// listing licenses no permanent exclusion, because the unknown token may itself be a body
    /// format. It reaches the typed gap instead, through the vocabulary's own documented floor.
    /// </summary>
    [TestMethod]
    public void AnUnreadableListingNeverNamesPrintAndSoNeverReachesNeverIngest()
    {
        var printAndUnknown = EuManifestationListingDecode.ObserveUnreadableListing(
            [EuManifestationFormat.Print], "epub3", Evidence("floor"));
        Assert.AreNotEqual(EuManifestationFormat.Print, printAndUnknown.Format);
        Assert.IsFalse(
            EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(printAndUnknown.Format),
            "an unread listing must never reach never_ingest.");
        Assert.AreEqual(EuFormatBodyAdmission.BodyNotAdmitted, printAndUnknown.Admission);

        // It names NoneAdmitted, the member that means exactly "none of what was listed is admitted
        // here", and never a format the office did not list for this Work. Until RULING
        // lex-event-20260904T201230364Z-8afe287d7c9b49509a410204e7ee729d this named Formex4, which
        // invented a publisher fact in a branch nobody could reach yet.
        Assert.AreEqual(EuManifestationFormat.NoneAdmitted, printAndUnknown.Format);

        // A listing this vocabulary knows nothing at all about takes the same answer.
        var nothingKnown = EuManifestationListingDecode.ObserveUnreadableListing(
            [], "epub3", Evidence("floor"));
        Assert.AreEqual(EuManifestationFormat.NoneAdmitted, nothingKnown.Format);
        Assert.IsFalse(
            EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(nothingKnown.Format));
        Assert.AreEqual("listing_type_not_admitted:epub3", nothingKnown.ReasonCode);
        Assert.HasCount(0, nothingKnown.OrderedCandidates);

        // And a Work that DID list something admitted still names that, never NoneAdmitted: the
        // member is the answer for an unreadable listing, not for every refused one.
        var oneKnown = EuManifestationListingDecode.ObserveUnreadableListing(
            [EuManifestationFormat.Xhtml, EuManifestationFormat.Print], "epub3", Evidence("floor"));
        Assert.AreEqual(EuManifestationFormat.Xhtml, oneKnown.Format);
    }

    /// <summary>
    /// The four properties that hold <see cref="EuManifestationFormat.NoneAdmitted"/> in place. It is
    /// this vocabulary's own answer for a listing it cannot read, so it must never be reachable from
    /// a publisher token, never addressable, never a ladder rung, and never a permanent exclusion.
    /// </summary>
    [TestMethod]
    public void NoneAdmittedIsUnreachableFromThePublisherAndNeverFetchableOrPermanent()
    {
        // One: no publisher token decodes into it. A Work whose listing literally contains the
        // string "none_admitted" is an unreadable listing, not an admitted one.
        Assert.IsFalse(
            EuManifestationListingDecode.ListedTypeTokens.ContainsKey("none_admitted"),
            "a publisher literal must never decode into NoneAdmitted.");
        var spoofed = Decode(
            [ListedRow(WorkingTimeRoot, "none_admitted"), ListedRow(WorkingTimeRoot, "xhtml")],
            out var refusal,
            out _,
            out var token);
        Assert.AreEqual(EuManifestationListingRefusal.None, refusal);
        Assert.AreEqual("none_admitted", token, "the spoofed token is refused by name like any other.");
        Assert.AreEqual(EuFormatBodyAdmission.BodyNotAdmitted, spoofed![WorkingTimeRoot].Admission);

        // Two: it can mint no request.
        Assert.IsFalse(
            EuDocumentFetchAddress.TryMediaTypeFor(EuManifestationFormat.NoneAdmitted, out _),
            "NoneAdmitted must have no Accept token; there is nothing to ask the office for.");

        // Three: it is not a rung.
        CollectionAssert.DoesNotContain(
            EuManifestationListingDecode.FormatLadder.ToArray(), EuManifestationFormat.NoneAdmitted);

        // Four: it is a typed gap, never never_ingest. Only print is permanently excluded, because
        // only print is physically incapable of carrying a body; an unread listing is an open
        // question.
        Assert.IsFalse(
            EuManifestationScope.FormatsThatCanNeverCarryABody.Contains(
                EuManifestationFormat.NoneAdmitted),
            "an unreadable listing must stay a typed gap pending a reviewed profile.");

        // And Observe, the admitted-listing door, can never produce it either.
        foreach (var format in Enum.GetValues<EuManifestationFormat>())
        {
            if (format == EuManifestationFormat.NoneAdmitted)
            {
                continue;
            }

            Assert.AreNotEqual(
                EuManifestationFormat.NoneAdmitted,
                EuManifestationListingDecode.Observe([format], Evidence("observe")).Format,
                $"a listing of {format} alone must never be reported as NoneAdmitted.");
        }
    }

    /// <summary>
    /// A publisher token that is not a bounded contract identifier still produces a usable reason
    /// code rather than throwing, so a hostile or merely odd token cannot crash a run.
    /// </summary>
    [TestMethod]
    public void AnOddPublisherTokenIsBoundedIntoTheReasonCodeRatherThanThrowing()
    {
        var wild = EuManifestationListingDecode.ObserveUnreadableListing(
            [EuManifestationFormat.Xhtml], "a b\n\u00e9/" + new string('z', 200), Evidence("wild"));

        StringAssert.StartsWith(wild.ReasonCode, "listing_type_not_admitted:");
        Assert.IsTrue(
            wild.ReasonCode.Length <= 256 && wild.ReasonCode.All(c => c is >= ' ' and <= '~'),
            "the reason code must stay a bounded printable-ASCII contract identifier.");
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
        // Every member EXCEPT NoneAdmitted, which is this vocabulary's own answer for an unreadable
        // listing rather than anything the office can say. A token for it would let a publisher
        // literal decode straight into "none of what was listed is admitted".
        Assert.HasCount(
            Enum.GetValues<EuManifestationFormat>().Length - 1,
            EuManifestationListingDecode.ListedTypeTokens);
        Assert.IsFalse(
            EuManifestationListingDecode.ListedTypeTokens.Values.Contains(
                EuManifestationFormat.NoneAdmitted),
            "NoneAdmitted must never be reachable from a publisher token.");
        foreach (var format in Enum.GetValues<EuManifestationFormat>())
        {
            if (format == EuManifestationFormat.NoneAdmitted)
            {
                continue;
            }

            Assert.IsTrue(
                EuManifestationListingDecode.ListedTypeTokens.Values.Contains(format),
                $"{format} has no listed-type token.");
        }

        // The seven tokens observed live on 2026-09-04 across the eight Works whose listings this
        // slice read, spanning 1995 to 2008.
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
