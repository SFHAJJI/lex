using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgInFileRightsReaderTests
{
    private const string Manifestation = "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226/fr/xml";
    private const string Licence = "http://creativecommons.org/licenses/by/4.0/";

    [TestMethod]
    [DataRow(LuxembourgUserFormatToken.Xml)]
    [DataRow(LuxembourgUserFormatToken.XmlAkomaNtoso)]
    public async Task BothAknLabelsReadTheRetainedManifestationsOwnDeclaration(LuxembourgUserFormatToken format)
    {
        var result = await ReadAsync(Document(), format);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.Observed, result.Status);
        CollectionAssert.AreEqual(new[] { Licence }, result.LicenceIris.ToArray());
        Assert.AreEqual(Manifestation, result.ManifestationIri);
    }

    [TestMethod]
    [DataRow(LuxembourgUserFormatToken.Pdf)]
    [DataRow(LuxembourgUserFormatToken.PdfA)]
    public async Task APdfRouteCannotSupplyAnAknDeclarationEvenIfItsBytesLookLikeXml(LuxembourgUserFormatToken format)
    {
        var result = await ReadAsync(Document(), format);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.UnsupportedRepresentation, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    public async Task AnUnrecognizedXmlNamespaceCannotSupplyRights()
    {
        var result = await ReadAsync(Document().Replace("http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13",
            "https://example.invalid/akn-lookalike", StringComparison.Ordinal));
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.UnsupportedRepresentation, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    public async Task TwoIdentificationBlocksCannotBeResolvedByChoosingTheFirst()
    {
        var result = await ReadAsync(Document().Replace("</identification>", "</identification><identification/>", StringComparison.Ordinal));
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    [DataRow("act")]
    [DataRow("meta")]
    [DataRow("FRBRManifestation")]
    public async Task ExtraDocumentOrIdentityContainersCannotBorrowTheFirstDeclarationsRights(string container)
    {
        var xml = container switch
        {
            "act" => Document().Replace("</akomaNtoso>", "<act><body>unidentified second work</body></act></akomaNtoso>", StringComparison.Ordinal),
            "meta" => Document().Replace("</meta>", "</meta><meta/>", StringComparison.Ordinal),
            _ => Document().Replace("</FRBRManifestation>", "</FRBRManifestation><FRBRManifestation/>", StringComparison.Ordinal),
        };
        var result = await ReadAsync(xml);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    [DataRow("FRBRthis")]
    [DataRow("uriThis")]
    public async Task EitherMismatchedManifestationIdentityCannotSupplyRights(string identity)
    {
        var xml = Document();
        xml = identity == "FRBRthis"
            ? xml.Replace($"value=\"{Manifestation}\"", $"value=\"{Manifestation}/other\"", StringComparison.Ordinal)
            : xml.Replace($">{Manifestation}</s:jolux>", $">{Manifestation}/other</s:jolux>", StringComparison.Ordinal);
        var result = await ReadAsync(xml);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    public async Task AnInternalEntityCannotManufactureALicenceDeclaration()
    {
        var xml = $"<!DOCTYPE akomaNtoso [<!ENTITY licence '{Licence}'>]>" + Document().Replace(Licence, "&licence;", StringComparison.Ordinal);
        var result = await ReadAsync(xml);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.MalformedXml, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    public async Task LicenceLikeTextOutsideTheManifestationMetadataDoesNotAuthorize()
    {
        var declaration = $"<s:jolux s:name=\"license\">{Licence}</s:jolux>";
        var xml = Document().Replace(declaration, "", StringComparison.Ordinal)
            .Replace("<body/>", $"<body>{declaration}</body>", StringComparison.Ordinal);
        var result = await ReadAsync(xml);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.Observed, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    public async Task ACorruptedRetainedBodyCannotSupplyARightsReading()
    {
        var bytes = Encoding.UTF8.GetBytes(Document());
        var digest = CustodyDigest.Of(bytes, CancellationToken.None);
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore { CorruptContentSha256 = digest };
        await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgInFileRightsReader.ReadAsync(store,
            new SourceArtifactRef("urn:uuid:67aa87ec-cf5b-477f-8a4c-c2c659c52f07", digest), Manifestation,
            new SourceArtifactRef("urn:uuid:67aa87ec-cf5b-477f-8a4c-c2c659c52f08", new string('a', 64)),
            LuxembourgUserFormatToken.XmlAkomaNtoso, CancellationToken.None));
    }

    [TestMethod]
    public async Task MarkupInsideALicenceValueIsNotAnExactPublisherIri()
    {
        var result = await ReadAsync(Document().Replace(Licence, $"<b>{Licence}</b>", StringComparison.Ordinal));
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.InvalidLicenceIri, result.Status);
        Assert.IsEmpty(result.LicenceIris);
    }

    [TestMethod]
    public async Task AnInvalidLicenceIriIsRetainedAsAReadingFailure()
    {
        var result = await ReadAsync(Document().Replace(Licence, "not an IRI", StringComparison.Ordinal));
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.InvalidLicenceIri, result.Status);
    }

    [TestMethod]
    public async Task MalformedContentAfterTheMetadataCannotAuthorize()
    {
        var result = await ReadAsync(Document() + "<unclosed>");
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.MalformedXml, result.Status);
    }

    [TestMethod]
    public async Task TheExistingPublisherFixtureIsReadWithoutTranscribingItsLicence()
    {
        var result = await ReadBytesAsync(LuxembourgDocumentFetchFixtures.XmlBody(),
            "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml",
            LuxembourgUserFormatToken.XmlAkomaNtoso);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.Observed, result.Status);
        CollectionAssert.AreEqual(new[] { Licence }, result.LicenceIris.ToArray());
    }

    internal static string Document() => $$"""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:s="http://www.scl.lu">
          <act><meta><identification>
            <FRBRManifestation><FRBRthis value="{{Manifestation}}"/></FRBRManifestation>
            <s:JOLUXManifestation>
              <s:jolux s:name="uriThis">{{Manifestation}}</s:jolux>
              <s:jolux s:name="license">{{Licence}}</s:jolux>
            </s:JOLUXManifestation>
          </identification></meta><body/></act>
        </akomaNtoso>
        """;

    private static Task<LuxembourgInFileRightsReading> ReadAsync(string xml,
        LuxembourgUserFormatToken format = LuxembourgUserFormatToken.XmlAkomaNtoso) =>
        ReadBytesAsync(Encoding.UTF8.GetBytes(xml), Manifestation, format);

    private static async Task<LuxembourgInFileRightsReading> ReadBytesAsync(byte[] bytes, string manifestation,
        LuxembourgUserFormatToken format)
    {
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        return await LuxembourgInFileRightsReader.ReadAsync(store,
            new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256),
            manifestation, new SourceArtifactRef("urn:uuid:67aa87ec-cf5b-477f-8a4c-c2c659c52f07", new string('a', 64)),
            format, CancellationToken.None);
    }
}
