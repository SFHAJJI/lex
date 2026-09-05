using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06c-LU-2 items 1 and 2: minting one object's document-fetch address from the store's own
/// <c>jolux:isExemplifiedBy</c> file URI, through the real production WEMI join
/// (<see cref="LuxembourgWemiTopology.Resolve"/>) and the real selection ladder. The assertions
/// below are the publisher's own predicate shapes, not a stand-in for them: work isRealizedBy
/// expression, expression isEmbodiedBy manifestation, manifestation userFormat and isExemplifiedBy
/// item, plus the legalValue marker the ladder ranks on.
/// </summary>
[TestClass]
public sealed class LuxembourgDocumentFetchAddressMintingTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string UserFormatPrefix =
        "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string LegalValuePrefix =
        "http://data.legilux.public.lu/resource/authority/statut-version/";
    private const string LanguageFra = "http://publications.europa.eu/resource/authority/language/FRA";

    // The act, its French expression, and its two real manifestations. Every IRI here follows the
    // publisher's own shapes: /eli/... for the work chain, /filestore/... for the items.
    private const string Act = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";
    private const string Expression = Act + "/fr";
    private const string ManifestationXml = Expression + "/xml";
    private const string ManifestationPdf = Expression + "/pdf";
    private const string ItemXml =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
        + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml";
    private const string ItemPdf =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/pdf/"
        + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-pdf.pdf";

    private static readonly SourceArtifactRef ObservationRef = new(
        "urn:uuid:6c4a1e77-2f83-4d90-b6a5-8e1c30f7d452",
        Convert.ToHexStringLower(SHA256.HashData("lu-minting-observation"u8.ToArray())));

    /// <summary>
    /// The whole path, end to end: the store lists an xml-akomantoso and a pdf manifestation for one
    /// expression, both marked officiel; the ladder takes the XML one, and the minted address names
    /// the exact token, the www-host fetch URI, and the act's own ELI page path.
    /// </summary>
    [TestMethod]
    public void TheXmlManifestationIsMintedFromTheStoresOwnIsExemplifiedByFileUri()
    {
        var address = Mint(TwoManifestationAssertions(
            xmlToken: "xml-akomantoso", xmlLegalValue: "officiel",
            pdfToken: "pdf", pdfLegalValue: "officiel"));

        Assert.IsNotNull(address);
        Assert.AreEqual(LuxembourgUserFormatToken.XmlAkomaNtoso, address!.UserFormatToken);
        Assert.AreEqual(LuxembourgLegalValue.Officiel, address.LegalValue);
        Assert.AreEqual(ItemXml, address.StoreFileUri.Value.AbsoluteUri);
        Assert.AreEqual(
            "https://legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
            + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml",
            address.FetchUri.AbsoluteUri,
            "the fetch address changes only host and scheme; the filestore path is the store's own.");
        Assert.AreEqual("/eli/etat/leg/loi/2017/03/14/a439/jo", address.ActEliPagePath);
    }

    /// <summary>
    /// The PDF-only act shape, grounded in a real fetch: rgd 1977/11/16/n3 offers one pdf
    /// manifestation marked officiel, and its file was retrieved whole (13a73ea1.., 124,932 bytes,
    /// PROBE_RESULT lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143). The IRIs below
    /// are that act's own, including the filestore path under the memorial a67 issue rather than
    /// under the act's own ELI path, which is exactly why the address carries the act page path
    /// separately.
    /// </summary>
    [TestMethod]
    public void APdfOnlyActMintsItsPdfAndKeepsTheActPagePathSeparateFromTheFilestorePath()
    {
        const string act = "http://data.legilux.public.lu/eli/etat/leg/rgd/1977/11/16/n3/jo";
        const string expression = act + "/fr";
        const string manifestation = expression + "/pdf";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/memorial/1977/a67/fr/pdf/"
            + "eli-etat-leg-memorial-1977-a67-fr-pdf.pdf";

        var address = LuxembourgQueryExecutionAdapter.MintDocumentFetchAddress(
            ObjectRef(act),
            LuxembourgWemiTopology.Resolve(
                act,
                [
                    Iri(act, RdfType, Jolux + "Act"),
                    Iri(act, Jolux + "isRealizedBy", expression),
                    Iri(expression, RdfType, Jolux + "Expression"),
                    Iri(expression, Jolux + "language", LanguageFra),
                    Iri(expression, Jolux + "isEmbodiedBy", manifestation),
                    Iri(manifestation, RdfType, Jolux + "Manifestation"),
                    Iri(manifestation, Jolux + "userFormat", UserFormatPrefix + "pdf"),
                    Iri(manifestation, Jolux + "isExemplifiedBy", item),
                    Iri(manifestation, Jolux + "legalValue", LegalValuePrefix + "officiel"),
                ],
                ObservationRef),
            [Iri(manifestation, Jolux + "legalValue", LegalValuePrefix + "officiel")]);

        Assert.IsNotNull(address);
        Assert.AreEqual(LuxembourgUserFormatToken.Pdf, address!.UserFormatToken);
        Assert.AreEqual(item, address.StoreFileUri.Value.AbsoluteUri);
        Assert.AreEqual("/eli/etat/leg/rgd/1977/11/16/n3/jo", address.ActEliPagePath);
        StringAssert.Contains(
            address.FetchUri.AbsolutePath,
            "/memorial/1977/a67/",
            "the file really does live outside its own act's ELI path, which is the case that "
            + "makes the act page path un-derivable from the fetch path.");
    }

    /// <summary>
    /// A manifestation the store lists with no legalValue marker IS a candidate, carrying the typed
    /// <see cref="LuxembourgLegalValue.Unstated"/> state, and format order still decides: the
    /// unmarked Akoma Ntoso XML beats the officiel PDF.
    /// </summary>
    /// <remarks>
    /// THIS TEST PREVIOUSLY ASSERTED THE OPPOSITE AND GUARDED A DEFECT. It was named
    /// <c>AManifestationWithNoLegalValueMarkerIsNotACandidate</c> and asserted that the marked PDF
    /// won, on the reasoning that an unmarked file must not be silently promoted over a marked one.
    /// That reasoning held only while legal value outranked format. With format primary (RULING
    /// lex-event-20260904T194018108Z-62079c93ce9d405ca1fb326cfea41bd9) nothing is promoted: the
    /// unmarked file keeps its own format's place. The drop it defended removed the very
    /// manifestations D49 prefers, since 99.5 percent of plain xml and 42 percent of xml-akomantoso
    /// carry no marker.
    /// </remarks>
    [TestMethod]
    public void AManifestationWithNoLegalValueMarkerIsACandidateCarryingUnstated()
    {
        var address = Mint(TwoManifestationAssertions(
            xmlToken: "xml-akomantoso", xmlLegalValue: null,
            pdfToken: "pdf", pdfLegalValue: "officiel"));

        Assert.IsNotNull(address);
        Assert.AreEqual(LuxembourgUserFormatToken.XmlAkomaNtoso, address!.UserFormatToken);
        Assert.AreEqual(LuxembourgLegalValue.Unstated, address.LegalValue);
        Assert.AreEqual(ItemXml, address.StoreFileUri.Value.AbsoluteUri);
    }

    /// <summary>
    /// THE NAMED ACT, and the whole repair in one object: loi 2021/09/09/a676/jo/fr, retained at
    /// C:/lex-v3/scratch/probe-d1-04e as digest
    /// f36772f7377f0d30f827a74594165219b42374e4a420fc9f39cd890d059d5efe (PROBE_RESULT
    /// lex-event-20260904T194555823Z-c9118e0eeb5a4433bc7488ae768ea6ea). Its four manifestations are
    /// reproduced below exactly as that retained response lists them: pdfa marked DEFINITIF, and
    /// html, xml-akomantoso and docx with NO legalValue at all, every one carrying a file. Note the
    /// publisher's own shapes, kept rather than tidied: the xml-akomantoso manifestation's IRI ends
    /// in <c>/xml</c>, and its file is <c>...-jo-fr-xml.xml</c>.
    /// <para>
    /// Under the previous ladder the XML was dropped for having no marker and the definitif PDF/A
    /// was the only survivor, so this 2021 Luxembourg law would have been held as a PDF/A instead
    /// of its Akoma Ntoso XML. This one object exercises the drop, the absent state,
    /// format-over-legal-value and a definitif marker together.
    /// </para>
    /// </summary>
    [TestMethod]
    public void TheRealLoi2021MintsItsUnmarkedAkomaNtosoXmlRatherThanItsDefinitifPdfA()
    {
        const string act = "http://data.legilux.public.lu/eli/etat/leg/loi/2021/09/09/a676/jo";
        const string expression = act + "/fr";
        const string store = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2021/09/09/a676/jo/fr/";
        var assertions = new List<LuxembourgObservedAssertion>
        {
            Iri(act, RdfType, Jolux + "Act"),
            Iri(act, Jolux + "isRealizedBy", expression),
            Iri(expression, RdfType, Jolux + "Expression"),
            Iri(expression, Jolux + "language", LanguageFra),
        };

        // (manifestation IRI suffix, userFormat token, legalValue or null, file name)
        (string Suffix, string Token, string? LegalValue, string File)[] manifestations =
        [
            ("/pdfa", "pdfa", "definitif", "pdfa/eli-etat-leg-loi-2021-09-09-a676-jo-fr-pdfa.pdf"),
            ("/html", "html", null, "html/eli-etat-leg-loi-2021-09-09-a676-jo-fr-html.html"),
            ("/xml", "xml-akomantoso", null, "xml/eli-etat-leg-loi-2021-09-09-a676-jo-fr-xml.xml"),
            ("/docx", "docx", null, "docx/eli-etat-leg-loi-2021-09-09-a676-jo-fr-docx.docx"),
        ];
        foreach (var (suffix, token, legalValue, file) in manifestations)
        {
            var manifestation = expression + suffix;
            assertions.Add(Iri(expression, Jolux + "isEmbodiedBy", manifestation));
            assertions.Add(Iri(manifestation, RdfType, Jolux + "Manifestation"));
            assertions.Add(Iri(manifestation, Jolux + "userFormat", UserFormatPrefix + token));
            assertions.Add(Iri(manifestation, Jolux + "isExemplifiedBy", store + file));
            if (legalValue is not null)
            {
                assertions.Add(Iri(manifestation, Jolux + "legalValue", LegalValuePrefix + legalValue));
            }
        }

        var address = LuxembourgQueryExecutionAdapter.MintDocumentFetchAddress(
            ObjectRef(act),
            LuxembourgWemiTopology.Resolve(act, assertions.ToArray(), ObservationRef),
            assertions);

        Assert.IsNotNull(address, "the act's own Akoma Ntoso XML must be minted, not dropped.");
        Assert.AreEqual(LuxembourgUserFormatToken.XmlAkomaNtoso, address!.UserFormatToken);
        Assert.AreEqual(LuxembourgLegalValue.Unstated, address.LegalValue);
        Assert.AreEqual(
            store + "xml/eli-etat-leg-loi-2021-09-09-a676-jo-fr-xml.xml",
            address.StoreFileUri.Value.AbsoluteUri);
        Assert.AreEqual("/eli/etat/leg/loi/2021/09/09/a676/jo", address.ActEliPagePath);
        Assert.AreNotEqual(
            LuxembourgUserFormatToken.PdfA,
            address.UserFormatToken,
            "the definitif PDF/A is what the pre-repair ladder selected for this real act.");
    }

    /// <summary>
    /// The drop branch is not dead, it just means something narrower now: the publisher stating TWO
    /// DIFFERENT legal values for one manifestation is the store disagreeing with itself, and
    /// neither value is chosen. Absence and conflict used to be the same answer, which is what let
    /// "no marker" be handled as "unusable".
    /// </summary>
    [TestMethod]
    public void AManifestationWithTwoConflictingLegalValuesIsRefusedAsACandidate()
    {
        var assertions = TwoManifestationAssertions(
            xmlToken: "xml-akomantoso", xmlLegalValue: "officiel",
            pdfToken: "pdf", pdfLegalValue: "officiel")
            .Append(Iri(ManifestationXml, Jolux + "legalValue", LegalValuePrefix + "definitif"))
            .ToArray();

        var address = LuxembourgQueryExecutionAdapter.MintDocumentFetchAddress(
            ObjectRef(Act),
            LuxembourgWemiTopology.Resolve(Act, assertions, ObservationRef),
            assertions);

        Assert.IsNotNull(address);
        Assert.AreEqual(
            LuxembourgUserFormatToken.Pdf,
            address!.UserFormatToken,
            "the self-contradicting XML is refused, so the PDF is the only candidate left.");
    }

    /// <summary>
    /// A store token this route does not select is dropped rather than mapped to a nearby one. Here
    /// the only other manifestation is html, so the pdf is minted; with html silently treated as a
    /// wording candidate this would mint the html item instead.
    /// </summary>
    [TestMethod]
    public void ANonWordingStoreTokenIsDroppedRatherThanMappedToANearbyOne()
    {
        var address = Mint(TwoManifestationAssertions(
            xmlToken: "html", xmlLegalValue: "officiel",
            pdfToken: "pdf", pdfLegalValue: "officiel"));

        Assert.IsNotNull(address);
        Assert.AreEqual(LuxembourgUserFormatToken.Pdf, address!.UserFormatToken);
        Assert.AreEqual(ItemPdf, address.StoreFileUri.Value.AbsoluteUri);
    }

    /// <summary>
    /// An object whose store offers no selectable manifestation mints no address at all, and its
    /// manifest row therefore keeps the typed absence rather than a fabricated target.
    /// </summary>
    [TestMethod]
    public void AnObjectWithNoSelectableManifestationMintsNoAddress()
    {
        var address = LuxembourgQueryExecutionAdapter.MintDocumentFetchAddress(
            ObjectRef(Act),
            LuxembourgWemiTopology.Resolve(Act, [Iri(Act, RdfType, Jolux + "Act")], ObservationRef),
            []);

        Assert.IsNull(address);
    }

    /// <summary>
    /// The minted address projects onto the manifest row as a NON-NEGOTIATING minted address: host
    /// and resource path, and no Accept pair, because this route sends neither header. The row is
    /// Minted, not NotMinted: a Luxembourg row that still said "no publisher route yet" once the
    /// route exists would be false.
    /// </summary>
    [TestMethod]
    public void TheMintedAddressProjectsOntoTheManifestRowWithoutInventingAnAcceptPair()
    {
        var row = Mint(TwoManifestationAssertions(
            xmlToken: "xml-akomantoso", xmlLegalValue: "officiel",
            pdfToken: "pdf", pdfLegalValue: "officiel"))!.ToScopeManifestFetchAddress();

        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, row.Status);
        Assert.AreEqual("legilux.public.lu", row.Host);
        Assert.AreEqual(
            "/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
            + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml",
            row.ResourcePath);
        Assert.IsNull(row.AcceptMediaType);
        Assert.IsNull(row.AcceptLanguage);
        Assert.IsNull(row.NotMintedReason);
    }

    /// <summary>
    /// The legalValue predicate IRI this adapter reads is the store's own jolux one, not an
    /// authority IRI that merely looks like it. Pins the literal against the ontology prefix the
    /// resolver itself uses, which is what the adapter's own remark promises.
    /// </summary>
    [TestMethod]
    public void TheLegalValuePredicateIriIsTheStoresOwnJoluxLegalValuePredicate()
    {
        // Read through a variable so the comparison is not constant-folded away: the point is that
        // the adapter's literal equals the ontology prefix plus the predicate's local name, both
        // spelled independently here.
        var actual = LuxembourgQueryExecutionAdapter.JoluxLegalValue;
        Assert.AreEqual(Jolux + "legalValue", actual);
        Assert.IsNotNull(
            LuxembourgAuthorityIri.TryParseLegalValue(LegalValuePrefix + "officiel"),
            "and the OBJECT of that predicate is the statut-version authority, a different IRI "
            + "family from the predicate itself.");
    }

    private static LuxembourgDocumentFetchAddress? Mint(LuxembourgObservedAssertion[] assertions) =>
        LuxembourgQueryExecutionAdapter.MintDocumentFetchAddress(
            ObjectRef(Act),
            LuxembourgWemiTopology.Resolve(Act, assertions, ObservationRef),
            assertions);

    private static LuxembourgObservedAssertion[] TwoManifestationAssertions(
        string xmlToken, string? xmlLegalValue, string pdfToken, string? pdfLegalValue)
    {
        var assertions = new List<LuxembourgObservedAssertion>
        {
            Iri(Act, RdfType, Jolux + "Act"),
            Iri(Act, Jolux + "isRealizedBy", Expression),
            Iri(Expression, RdfType, Jolux + "Expression"),
            Iri(Expression, Jolux + "language", LanguageFra),
            Iri(Expression, Jolux + "isEmbodiedBy", ManifestationXml),
            Iri(Expression, Jolux + "isEmbodiedBy", ManifestationPdf),
            Iri(ManifestationXml, RdfType, Jolux + "Manifestation"),
            Iri(ManifestationXml, Jolux + "userFormat", UserFormatPrefix + xmlToken),
            Iri(ManifestationXml, Jolux + "isExemplifiedBy", ItemXml),
            Iri(ManifestationPdf, RdfType, Jolux + "Manifestation"),
            Iri(ManifestationPdf, Jolux + "userFormat", UserFormatPrefix + pdfToken),
            Iri(ManifestationPdf, Jolux + "isExemplifiedBy", ItemPdf),
        };
        if (xmlLegalValue is not null)
        {
            assertions.Add(Iri(ManifestationXml, Jolux + "legalValue", LegalValuePrefix + xmlLegalValue));
        }

        if (pdfLegalValue is not null)
        {
            assertions.Add(Iri(ManifestationPdf, Jolux + "legalValue", LegalValuePrefix + pdfLegalValue));
        }

        return assertions.ToArray();
    }

    private static LuxembourgObservedAssertion Iri(string subject, string predicate, string value) =>
        new(subject, predicate, LuxembourgAssertionObjectKind.Iri, value, string.Empty, string.Empty, ObservationRef);

    private static SourceObjectRef ObjectRef(string publisherUri)
    {
        var canonicalKey = "lu-minting:" + publisherUri;
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(ObservationRef, "lu_minting_root"),
            publisherUri,
            canonicalKey,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey))),
            ObservationRef,
            null);
    }
}
