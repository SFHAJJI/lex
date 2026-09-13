using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// #419 slice 6b: the Gazette body producer. From an act's body join and the bodies the fetch loop
/// held, every Gazette-PDF listing gets its outcome and the act gets its set; a stray or doubled
/// acquisition, bytes custody does not hold, or a retention the disposition cannot establish refuse
/// by name. Offline: nothing is fetched.
/// </summary>
[TestClass]
public sealed class LuxembourgGazetteBodyProducerTests
{
    private const string ActLoi1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    private const string LanguageFra = "http://publications.europa.eu/resource/authority/language/FRA";
    private const string LanguageDeu = "http://publications.europa.eu/resource/authority/language/DEU";
    private const string FormatXml = "http://data.legilux.public.lu/resource/authority/user-format/xml";
    private const string FormatPdfA = "http://data.legilux.public.lu/resource/authority/user-format/pdfa";
    private const string FormatPdf = "http://data.legilux.public.lu/resource/authority/user-format/pdf";
    private const string CcBy40 = "http://creativecommons.org/licenses/by/4.0/";
    private const string LicenceScl = "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly SourceArtifactRef Run = Artifact("cbe6e64c-789a-4c73-854d-19e464728a50", '1');
    private static readonly SourceArtifactRef SparqlEnumeration = Artifact("388b94c5-c812-494a-8414-11659c742d7f", '3');
    private static readonly SourceArtifactRef InFileEnumeration = Artifact("43b2af70-9a13-4a0f-a202-f5bb00199239", '4');
    private static readonly SourceArtifactRef SparqlEvidence = Artifact("5f4c1a2e-0b7d-4e7f-9c1a-2b3c4d5e6f70", '5');
    private static readonly SourceArtifactRef InFileEvidence = Artifact("6a5b2c3d-1e8f-4a0b-8d2c-3e4f5a6b7c81", '6');
    private static readonly SourceArtifactRef RouteRun = Artifact("9d8e7f60-4b12-4d3e-bf5a-6b7c8d9e0fa4", '9');

    [TestMethod]
    public async Task OneFetchedListingAndOneUnfetchedProduceTheActsSet()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"), Candidate(ActLoi1, "fr", "pdf"));
        var pdfa = Listing(join, FormatPdfA);
        var (acquisition, receipt) = await HeldAcquisitionAsync(store, pdfa, "gazette pdfa bytes");

        var result = await new LuxembourgGazetteBodyProducer(store).RunAsync(join, [acquisition], CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal} {result.Detail}");
        var set = result.Set!;
        Assert.AreEqual(ActLoi1, set.PublisherActIri);
        Assert.IsNull(set.ActGap);
        Assert.AreEqual(2, set.Bodies.Count);
        Assert.AreEqual(1, set.AdmittedCount);
        Assert.AreEqual(0, set.RejectedCount);
        Assert.AreEqual(1, set.GapCount);
        var admitted = set.Bodies.Single(static b => b.Outcome == LuxembourgGazetteBodyOutcome.Admitted);
        Assert.AreEqual(pdfa.WemiCandidate.ManifestationIri, admitted.ManifestationIri);
        Assert.AreEqual(receipt.Reference.ContentSha256, admitted.TransportByteSha256);
        Assert.AreEqual(LuxembourgGazetteBodyGapReason.BodyNotRetained,
            set.Bodies.Single(static b => b.Outcome == LuxembourgGazetteBodyOutcome.TypedGap).GapReason);
    }

    [TestMethod]
    public async Task AnAcquisitionForABodyTheJoinDoesNotListRefusesNamingEveryOffenderInOrdinalOrder()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listed = Listing(join, FormatPdf);
        var (fine, _) = await HeldAcquisitionAsync(store, listed, "listed");
        // Two bodies this join does not list as Gazette PDFs: another language's pdf, and an xml.
        var other = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "de", "pdf"), Candidate(ActLoi1, "fr", "pdf"), Candidate(ActLoi1, "de", "pdfa"));
        var (strayDe, _) = await HeldAcquisitionAsync(store, other.Candidates.Single(c => c.WemiCandidate.LanguageIri == LanguageDeu && c.WemiCandidate.FormatIri == FormatPdf), "de");
        var (strayDeA, _) = await HeldAcquisitionAsync(store, other.Candidates.Single(c => c.WemiCandidate.LanguageIri == LanguageDeu && c.WemiCandidate.FormatIri == FormatPdfA), "dea");
        var producer = new LuxembourgGazetteBodyProducer(store);

        var forward = await producer.RunAsync(join, [fine, strayDe, strayDeA], CancellationToken.None);
        var reversed = await producer.RunAsync(join, [strayDeA, strayDe, fine], CancellationToken.None);

        Assert.AreEqual(LuxembourgGazetteBodyProductionRefusal.AcquisitionForUnlistedBody, forward.Refusal);
        Assert.IsNull(forward.Set);
        StringAssert.Contains(forward.Detail, strayDe.ManifestationIri);
        StringAssert.Contains(forward.Detail, strayDeA.ManifestationIri);
        Assert.AreEqual(forward.Detail, reversed.Detail, "every offender, in ordinal order, whatever order delivered them.");
        // Ordinal on the "manifestation|item" key: after "…/de/pdf", 'a' (97) precedes '|' (124),
        // so the pdfa key sorts before the pdf key - the opposite of what a reader might expect.
        Assert.IsTrue(
            forward.Detail!.IndexOf("/de/pdfa|", StringComparison.Ordinal) < forward.Detail.IndexOf("/de/pdf|", StringComparison.Ordinal),
            "ordinal: .../de/pdfa| before .../de/pdf|.");
    }

    [TestMethod]
    public async Task TwoAcquisitionsForOneListingRefuse()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = Listing(join, FormatPdf);
        var (first, _) = await HeldAcquisitionAsync(store, listing, "first");
        var (second, _) = await HeldAcquisitionAsync(store, listing, "second");

        var result = await new LuxembourgGazetteBodyProducer(store).RunAsync(join, [first, second], CancellationToken.None);

        Assert.AreEqual(LuxembourgGazetteBodyProductionRefusal.AcquisitionDeliveredTwice, result.Refusal);
        StringAssert.Contains(result.Detail, listing.WemiCandidate.ManifestationIri);
    }

    [TestMethod]
    public async Task BytesCustodyDoesNotHoldRefuse()
    {
        var elsewhere = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = Listing(join, FormatPdf);
        // The receipt is real - minted by another store - but this producer's store never held the bytes.
        var (acquisition, _) = await HeldAcquisitionAsync(elsewhere, listing, "held elsewhere");

        var result = await new LuxembourgGazetteBodyProducer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(join, [acquisition], CancellationToken.None);

        Assert.AreEqual(LuxembourgGazetteBodyProductionRefusal.RetainedBytesUnavailable, result.Refusal);
        StringAssert.Contains(result.Detail, listing.WemiCandidate.ManifestationIri);
    }

    /// <summary>A retention the disposition cannot establish refuses by name — never a quiet "not retained".</summary>
    [TestMethod]
    public async Task ARetentionTheDispositionCannotEstablishRefusesByName()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = Listing(join, FormatPdf);
        var (held, _) = await HeldAcquisitionAsync(store, listing, "the bytes");
        var (otherHeld, _) = await HeldAcquisitionAsync(store, listing, "other bytes");
        // Both receipts are in custody; the route names the other one.
        var mismatched = new LuxembourgGazetteBodyAcquisition(
            held.ManifestationIri, held.ItemIri, held.OfficialAddress, held.OfficialRequest, held.TerminalRequest,
            otherHeld.SourceEvidence, held.RetainedTransportBytes);

        var result = await new LuxembourgGazetteBodyProducer(store).RunAsync(join, [mismatched], CancellationToken.None);

        Assert.AreEqual(LuxembourgGazetteBodyProductionRefusal.RetentionNotEstablished, result.Refusal);
        Assert.IsNull(result.Set);
        StringAssert.Contains(result.Detail, "not the exact receipt bound to the terminal hop");
    }

    [TestMethod]
    public async Task AFetchOfABodyTheJoinWithholdsRefuses()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinLicenceScl(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        var listing = Listing(join, FormatPdf);
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.Withheld, listing.Disposition, "the premise: withheld by the join.");
        var (acquisition, _) = await HeldAcquisitionAsync(store, listing, "fetched anyway");

        var result = await new LuxembourgGazetteBodyProducer(store).RunAsync(join, [acquisition], CancellationToken.None);

        Assert.AreEqual(LuxembourgGazetteBodyProductionRefusal.RetentionNotEstablished, result.Refusal);
        StringAssert.Contains(result.Detail, "withholds");
    }

    [TestMethod]
    public async Task AnActWithNoGazetteListingProducesItsActGap()
    {
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "xml"));

        var result = await new LuxembourgGazetteBodyProducer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore())
            .RunAsync(join, [], CancellationToken.None);

        Assert.IsTrue(result.Produced);
        Assert.AreEqual(0, result.Set!.Bodies.Count);
        Assert.AreEqual(LuxembourgGazetteActGapReason.NoGazettePdfCandidate, result.Set.ActGap);
    }

    [TestMethod]
    public async Task TwoDeliveryOrdersProduceOneSet()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdfa"), Candidate(ActLoi1, "fr", "pdf"));
        var (a, _) = await HeldAcquisitionAsync(store, Listing(join, FormatPdfA), "pdfa");
        var (b, _) = await HeldAcquisitionAsync(store, Listing(join, FormatPdf), "pdf");
        var producer = new LuxembourgGazetteBodyProducer(store);

        var forward = await producer.RunAsync(join, [a, b], CancellationToken.None);
        var reversed = await producer.RunAsync(join, [b, a], CancellationToken.None);

        Assert.IsTrue(forward.Produced && reversed.Produced);
        CollectionAssert.AreEqual(
            forward.Set!.Bodies.Select(static x => x.IdentitySha256).ToArray(),
            reversed.Set!.Bodies.Select(static x => x.IdentitySha256).ToArray());
        Assert.AreEqual(2, forward.Set.AdmittedCount);
    }

    [TestMethod]
    public void EveryRefusalMemberHasItsExactWireToken() =>
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"", "\"acquisition_for_unlisted_body\"", "\"acquisition_delivered_twice\"",
                "\"retained_bytes_unavailable\"", "\"retention_not_established\"",
            },
            Enum.GetValues<LuxembourgGazetteBodyProductionRefusal>().Select(static m => ContractJson.Serialize(m)).ToArray());

    [TestMethod]
    public async Task NullsAreCallerContractViolations()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgGazetteBodyProducer(store);
        var join = JoinAgreedCcBy(ActLoi1, Candidate(ActLoi1, "fr", "pdf"));
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgGazetteBodyProducer(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => producer.RunAsync(null!, [], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => producer.RunAsync(join, null!, CancellationToken.None));
        var (acquisition, _) = await HeldAcquisitionAsync(store, Listing(join, FormatPdf), "x");
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgGazetteBodyAcquisition(
            acquisition.ManifestationIri, acquisition.ItemIri, acquisition.OfficialAddress, acquisition.OfficialRequest,
            acquisition.TerminalRequest, acquisition.SourceEvidence, null!));
    }

    // ---- fixtures (the 6a recipes, with receipts minted by the custody store under test) ----

    private static LuxembourgBodyCandidateResolution Listing(LuxembourgBodyJoinResolution join, string formatIri) =>
        LuxembourgGazetteBodySet.GazetteCandidatesOf(join).Single(c => c.WemiCandidate.FormatIri == formatIri);

    /// <summary>Holds <paramref name="text"/> in the store and builds the acquisition the fetch loop would hand over.</summary>
    private static async Task<(LuxembourgGazetteBodyAcquisition acquisition, DurableBlobWriteReceipt receipt)> HeldAcquisitionAsync(
        ICustodyStore store, LuxembourgBodyCandidateResolution listing, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var address = LuxembourgDocumentFetchAddress.Create(
            LuxembourgFileUri.RequireValid(listing.WemiCandidate.ItemIri),
            LuxembourgAuthorityIri.TryParseUserFormat(listing.WemiCandidate.FormatIri)!.Value,
            LuxembourgLegalValue.Unstated,
            new Uri(listing.WemiCandidate.RootIri, UriKind.Absolute).AbsolutePath);
        var request = HttpLogicalRequest.Create(
            address.FetchUri.AbsoluteUri,
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("user-agent", "lex-v3-tests")],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));
        var length = checked((ulong)bytes.Length);
        var absent = new RoutedHttpAbsentHeader();
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            Sha256(request.CopyCanonicalBytes()),
            address.FetchUri.AbsoluteUri,
            200,
            new RoutedHttpResponseHeaders(
                new RoutedHttpSingleHeader("application/pdf"),
                new RoutedHttpSingleHeader(length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent),
            "2026-09-13T12:00:00.0000000Z",
            "2026-09-13T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(length),
            length,
            receipt.Reference.ContentSha256,
            DurableBlobWriteReceiptDigest.Of(receipt),
            length,
            receipt.Reference.ContentSha256);
        var evidence = RoutedHttpEvidence.Create(
            RouteRun, 1, 0, [hop], new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });
        return (new LuxembourgGazetteBodyAcquisition(
            listing.WemiCandidate.ManifestationIri, listing.WemiCandidate.ItemIri, address, request, request, evidence, receipt), receipt);
    }

    private static LuxembourgWemiCandidate Candidate(string act, string language, string format) =>
        new(
            act,
            act + "/" + language,
            act + "/" + language + "/" + format,
            "http://data.legilux.public.lu/filestore/" + act[(act.LastIndexOf("leg/", StringComparison.Ordinal) + 4)..].Replace('/', '-') + "-" + language + "." + format,
            language == "fr" ? LanguageFra : LanguageDeu,
            format switch { "pdfa" => FormatPdfA, "pdf" => FormatPdf, _ => FormatXml },
            Run,
            LuxembourgWemiCandidateDisposition.StructurallyConsistent,
            []);

    private static LuxembourgWemiTopologyResolution Topology(params LuxembourgWemiCandidate[] candidates) => new(
        candidates,
        [],
        [],
        candidates.Select(static c => c.ExpressionIri).Distinct(StringComparer.Ordinal).OrderBy(static v => v, StringComparer.Ordinal).ToArray(),
        candidates.Select(static c => c.ManifestationIri).Distinct(StringComparer.Ordinal).OrderBy(static v => v, StringComparer.Ordinal).ToArray());

    private static LuxembourgBodyJoinResolution JoinWith(string act, string licence, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => new LuxembourgRightsChannelObservation(c.ManifestationIri, Run, SparqlEvidence, [licence])).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration, candidates.Select(c => new LuxembourgRightsChannelObservation(c.ManifestationIri, Run, InFileEvidence, [licence])).ToArray()));

    private static LuxembourgBodyJoinResolution JoinAgreedCcBy(string act, params LuxembourgWemiCandidate[] candidates) => JoinWith(act, CcBy40, candidates);

    private static LuxembourgBodyJoinResolution JoinLicenceScl(string act, params LuxembourgWemiCandidate[] candidates) => JoinWith(act, LicenceScl, candidates);

    private static string Digest(char value) => new(value, 64);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new("urn:uuid:" + id, new string(digestCharacter, 64));
}
