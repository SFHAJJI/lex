using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// In-file rights reading /2 (LU-DATA-DESIGN v2 §1.1, "Rules /2"), one fact per test, over synthetic files in the publisher's
/// shape. Reading /1 stays as the acquisition ran it: the last test pins that it still refuses the twin-block file /2 binds.
/// </summary>
/// <remarks>LEGAL CARE. Every file here is written in this file; the identities are run 15's shapes, the wording invented.</remarks>
[TestClass]
public sealed class LuxembourgInFileRightsReaderV2Tests
{
    private const string Akn = "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    private const string Expression = "http://data.legilux.public.lu/eli/etat/leg/loi/2021/01/22/a45/consolide/20211217/fr";
    private const string Xml = Expression + "/xml";
    private const string Pdf = Expression + "/pdf";
    private const string ActXml = "http://data.legilux.public.lu/eli/etat/leg/loi/2021/01/22/a45/jo/fr/xml";
    private static readonly string CcBy = VerifiedLuxembourgSourceProfile.AdmittingLicence;
    private static readonly string Scl = VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl;

    [TestMethod]
    public void TheReadingProfileIsItsPinnedTextAndDigest()
    {
        var text = Encoding.UTF8.GetString(LuxembourgInFileRightsReader.CopyReadingV2ProfileBytes());
        StringAssert.StartsWith(text, "lex-lu-in-file-rights-reading/2\n");
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), LuxembourgInFileRightsReader.ReadingV2ProfileSha256);
        Assert.AreEqual(PinnedReadingV2ProfileSha256, LuxembourgInFileRightsReader.ReadingV2ProfileSha256);
    }

    /// <summary>Recomputed in Python from the profile text (<c>hashlib.sha256(text.encode()).hexdigest()</c>).</summary>
    internal const string PinnedReadingV2ProfileSha256 = "eb3b6d96bdd719a810c1b976d6affccbccbdc72c4947bf407e9c44d81bd84bc7";

    [TestMethod]
    public void TwinBlocksBindTheOwnBlock()
    {
        var reading = Read(File(Identification(Frbr(Xml), Block(Xml, CcBy), Block(Pdf, CcBy))));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, reading.Status);
        CollectionAssert.AreEqual(new[] { CcBy }, reading.OwnBlockLicenceIris.ToArray());
        CollectionAssert.AreEqual(new[] { CcBy }, reading.DeclaredLicenceIris.ToArray());
        CollectionAssert.AreEqual(new[] { Xml, Pdf }, reading.NamedIdentities.Select(static named => named.Identity).ToArray());
        CollectionAssert.AreEqual(new[] { Xml }, reading.FrbrManifestationIdentities.ToArray());
    }

    [TestMethod]
    public void AStaleJoIdentityIsUnboundWithItsNamedIdentity()
    {
        var reading = Read(File(Identification(Frbr(Xml), Block(ActXml, CcBy))));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Unbound, reading.Status);
        Assert.AreEqual(0, reading.OwnBlockLicenceIris.Count);
        Assert.AreEqual(ActXml, reading.NamedIdentities.Single().Identity);
        CollectionAssert.AreEqual(new[] { CcBy }, reading.NamedIdentities.Single().LicenceIris.ToArray());
    }

    [TestMethod]
    public void AnEmptyTemplateIsSilent()
    {
        var reading = Read(File(Identification(Frbr(Xml),
            "<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\"></scl:jolux><scl:jolux scl:name=\"userFormat\"></scl:jolux></scl:JOLUXManifestation>")));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Silent, reading.Status);
        Assert.AreEqual(0, reading.NamedIdentities.Count, "an empty uriThis names nothing");
    }

    [TestMethod]
    public void AnOwnBlockWithoutALicenceIsOwnBlockEmptyEvenBesideACcBySibling()
    {
        var reading = Read(File(Identification(Frbr(Xml), Block(Xml, null), Block(Pdf, CcBy))));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.OwnBlockEmpty, reading.Status);
        CollectionAssert.AreEqual(new[] { CcBy }, reading.DeclaredLicenceIris.ToArray());
    }

    [TestMethod]
    public void ALeadingPostitIsTolerated()
    {
        var reading = Read(File(Identification(Frbr(Xml), Block(Xml, CcBy)), postit: true));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, reading.Status);
    }

    [TestMethod]
    public void AFrbrMismatchIsRecordedAndIgnored()
    {
        var reading = Read(File(Identification(Frbr(ActXml), Block(Xml, CcBy))));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, reading.Status);
        CollectionAssert.AreEqual(new[] { ActXml }, reading.FrbrManifestationIdentities.ToArray());
    }

    [TestMethod]
    public void SclInASiblingOrAComponentBlockIsDeclared()
    {
        var sibling = Read(File(Identification(Frbr(Xml), Block(Xml, CcBy), Block(Pdf, Scl))));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, sibling.Status);
        CollectionAssert.AreEqual(new[] { CcBy, Scl }.Order(StringComparer.Ordinal).ToArray(), sibling.DeclaredLicenceIris.ToArray());

        var component = Read(File(Identification(Frbr(Xml), Block(Xml, CcBy)),
            attachment: $"<attachments><attachment><doc name=\"annexe\"><meta><identification source=\"#s\">{Block(Expression + "/annexe/xml", Scl)}</identification></meta></doc></attachment></attachments>"));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, component.Status, "a component block never binds");
        CollectionAssert.AreEqual(new[] { CcBy }, component.OwnBlockLicenceIris.ToArray());
        CollectionAssert.Contains(component.DeclaredLicenceIris.ToArray(), Scl);
    }

    [TestMethod]
    public async Task APdfIsNotApplicable()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var bytes = "%PDF-1.4 synthetic"u8.ToArray();
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var reading = await LuxembourgInFileRightsReader.ReadV2Async(
            store, BodyRef(receipt.Reference.ContentSha256), Pdf, LuxembourgUserFormatToken.Pdf, CancellationToken.None);
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.NotApplicable, reading.Status);
        Assert.IsFalse(reading.IsRejected);
    }

    [TestMethod]
    public void MalformedXmlAndMarkupInsideALicenceValueAreRejected()
    {
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.MalformedXml,
            Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(File(Identification(Frbr(Xml), Block(Xml, CcBy)))) + "<trailing")).Status);
        var markup = Read(File(Identification(Frbr(Xml),
            $"<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\">{Xml}</scl:jolux><scl:jolux scl:name=\"license\"><b>{CcBy}</b></scl:jolux></scl:JOLUXManifestation>")));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.InvalidLicenceIri, markup.Status);
        Assert.IsTrue(markup.IsRejected);
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.InvalidLicenceIri,
            Read(File(Identification(Frbr(Xml), Block(Xml, " " + CcBy)))).Status, "a value is never trimmed");
    }

    [TestMethod]
    public void LicenceLikeBodyTextIsNotADeclaration()
    {
        var reading = Read(File(Identification(Frbr(Xml)), body: $"<p>license {CcBy} licenceSCL</p>"));
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Silent, reading.Status);
    }

    [TestMethod]
    public void TwoBlocksNamingTheFetchedFileAreAnAmbiguousBinding()
    {
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.AmbiguousBinding,
            Read(File(Identification(Frbr(Xml), Block(Xml, CcBy), Block(Xml, CcBy)))).Status);
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.AmbiguousBinding,
            Read(File(Identification(Frbr(Xml),
                $"<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\">{Xml}</scl:jolux><scl:jolux scl:name=\"uriThis\">{Pdf}</scl:jolux>"
                + $"<scl:jolux scl:name=\"license\">{CcBy}</scl:jolux></scl:JOLUXManifestation>"))).Status,
            "a block naming the fetched file and another identity");
    }

    [TestMethod]
    public void ARootThatIsNotOneAknDocumentIsRejected()
    {
        var two = Encoding.UTF8.GetString(File(Identification(Frbr(Xml), Block(Xml, CcBy))))
            .Replace("</act>\n", "</act>\n<act name=\"second\"/>\n", StringComparison.Ordinal);
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.RootRejected, Read(Encoding.UTF8.GetBytes(two)).Status);
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.RootRejected,
            Read("<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"/>"u8.ToArray()).Status);
    }

    /// <summary>Reading /1, the acquisition's, is unchanged: it still refuses the twin-block file that /2 binds.</summary>
    [TestMethod]
    public async Task ReadingOneStillRefusesTheTwinBlockFile()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync(File(Identification(Frbr(Xml), Block(Xml, CcBy), Block(Pdf, CcBy))),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var bodyRef = BodyRef(receipt.Reference.ContentSha256);
        var one = await LuxembourgInFileRightsReader.ReadAsync(
            store, bodyRef, Xml, bodyRef, LuxembourgUserFormatToken.XmlAkomaNtoso, CancellationToken.None);
        Assert.AreEqual(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch, one.Status);
        var two = await LuxembourgInFileRightsReader.ReadV2Async(store, bodyRef, Xml, LuxembourgUserFormatToken.XmlAkomaNtoso, CancellationToken.None);
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, two.Status);
    }

    /// <summary>
    /// The licence-page index reads a retained SPARQL assertion page (the acquisition's row shape: subject, predicate, object,
    /// object kind) for its <c>jolux:license</c> rows with an IRI object, and nothing else.
    /// </summary>
    [TestMethod]
    public void TheLicencePageIndexReadsTheLicenceRowsOfASparqlPage()
    {
        const string License = "http://data.legilux.public.lu/resource/ontology/jolux#license";
        static string Row(string subject, string predicate, string value, string kind) =>
            $"{{\"subject\":{{\"type\":\"uri\",\"value\":\"{subject}\"}},\"predicate\":{{\"type\":\"uri\",\"value\":\"{predicate}\"}},"
            + $"\"object\":{{\"type\":\"{(kind == "iri" ? "uri" : "literal")}\",\"value\":\"{value}\"}},"
            + $"\"object_kind\":{{\"type\":\"literal\",\"value\":\"{kind}\"}},\"datatype_iri\":{{\"type\":\"literal\",\"value\":\"\"}}}}";
        var page = "{\"head\":{\"link\":[],\"vars\":[\"subject\",\"predicate\",\"object\",\"object_kind\",\"datatype_iri\"]},"
            + "\"results\":{\"bindings\":["
            + string.Join(",",
                Row(Xml, License, CcBy, "iri"),
                Row(Xml, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", "http://data.legilux.public.lu/resource/ontology/jolux#Manifestation", "iri"),
                Row(Pdf, License, Scl, "iri"),
                Row(ActXml, License, "not an IRI", "literal"))
            + "]}}";
        CollectionAssert.AreEqual(
            new[] { (Xml, CcBy), (Pdf, Scl) },
            LuxembourgSparqlLicencePageIndex.LicenceRowsOf(Encoding.UTF8.GetBytes(page)).ToArray());
    }

    // ---- Synthetic files in the publisher's shape. ----

    private static LuxembourgInFileRightsReadingV2 Read(byte[] bytes) =>
        LuxembourgInFileRightsReader.ReadV2(bytes, BodyRef(Convert.ToHexStringLower(SHA256.HashData(bytes))), Xml);

    private static SourceArtifactRef BodyRef(string sha256) => new(
        ContentDerivedIdentity.DeriveUuidUrn("lex-lu-in-file-body-digest/1", Encoding.UTF8.GetBytes(sha256)), sha256);

    private static string Frbr(string manifestation) =>
        $"<FRBRManifestation><FRBRthis value=\"{manifestation}\"/></FRBRManifestation>";

    private static string Block(string uriThis, string? licence) =>
        LuxembourgRightsReleaseTwoTests.Block(uriThis, licence);

    private static string Identification(params string[] parts) =>
        "<identification source=\"#lex-synthetic\">" + string.Concat(parts) + "</identification>";

    private static byte[] File(string identification, bool postit = false, string body = "<p>Texte synthétique.</p>", string attachment = "") =>
        Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + $"<akomaNtoso xmlns=\"{Akn}\" xmlns:scl=\"http://www.scl.lu\">\n"
            + (postit ? "<scl:postit>Note synthétique.</scl:postit>\n<!-- a comment -->\n" : "")
            + $"<act name=\"consolidation\">\n<meta>\n{identification}\n</meta>\n<body>{body}</body>\n{attachment}</act>\n</akomaNtoso>\n");
}
