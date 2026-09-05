using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-06c-EU (SCOPE_RULING lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef), items 2
/// and 3: the typed EU document-fetch address and its publisher-neutral manifest projection.
/// </summary>
[TestClass]
public sealed class EuDocumentFetchAddressTests
{
    [TestMethod]
    public void TryCreateMintsTheExactProvenUrlShapeForEveryClosedMediaTypeAndLanguage()
    {
        var address = EuDocumentFetchAddress.TryCreate(
            "celex",
            "32016R0679",
            EuManifestationMediaType.XhtmlXml,
            EuDocumentLanguage.Eng,
            out var refusal);
        Assert.IsNotNull(address);
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);
        Assert.AreEqual(
            "https://publications.europa.eu/resource/celex/32016R0679",
            address.ResourceUri);
        Assert.AreEqual("application/xhtml+xml", address.Accept);
        Assert.AreEqual("eng", address.AcceptLanguage);
        Assert.AreEqual("celex", address.PsName);
        Assert.AreEqual("32016R0679", address.PsId);

        var mediaTypes = new Dictionary<EuManifestationMediaType, string>
        {
            [EuManifestationMediaType.XhtmlXml] = "application/xhtml+xml",
            [EuManifestationMediaType.ZipMtypeFmx4] = "application/zip;mtype=fmx4",
            [EuManifestationMediaType.PdfTypePdfa2a] = "application/pdf;type=pdfa2a",
            [EuManifestationMediaType.RdfXml] = "application/rdf+xml",
            [EuManifestationMediaType.RdfXmlNoticeTree] = "application/rdf+xml;notice=tree",
            [EuManifestationMediaType.XmlNoticeBranch] = "application/xml;notice=branch",
            [EuManifestationMediaType.XmlNoticeObject] = "application/xml;notice=object",
            [EuManifestationMediaType.XmlNoticeIdentifier] = "application/xml;notice=identifier",
            // D1-05d's ninth member. The bare single token, never the multi-value preference list
            // v2 sent: observed live on 2026-09-04 serving 200 on 32003L0088, 31995L0046 and
            // 32004R0139.
            [EuManifestationMediaType.TextHtml] = "text/html",
            // D1-05d's tenth member, admitted by RULING
            // lex-event-20260904T185339315Z-87d1510eccdc42a5947c41d2d8580744. The bare token,
            // observed serving 200 live on 2026-09-04 on four works, the largest being 32006L0112 at
            // 486,142 bytes whose retained body carries the %PDF-1.4 magic.
            [EuManifestationMediaType.ApplicationPdf] = "application/pdf",
        };
        Assert.AreEqual(10, Enum.GetValues<EuManifestationMediaType>().Length);
        foreach (var (mediaType, expectedAccept) in mediaTypes)
        {
            var minted = EuDocumentFetchAddress.TryCreate(
                "celex", "32016R0679", mediaType, EuDocumentLanguage.Eng, out var mediaRefusal);
            Assert.IsNotNull(minted, mediaType.ToString());
            Assert.AreEqual(EuDocumentFetchAddressRefusal.None, mediaRefusal);
            Assert.AreEqual(expectedAccept, minted.Accept, mediaType.ToString());
        }

        var languages = new Dictionary<EuDocumentLanguage, string>
        {
            [EuDocumentLanguage.Eng] = "eng",
            [EuDocumentLanguage.Fra] = "fra",
        };
        Assert.AreEqual(2, Enum.GetValues<EuDocumentLanguage>().Length);
        foreach (var (language, expectedToken) in languages)
        {
            var minted = EuDocumentFetchAddress.TryCreate(
                "celex", "32016R0679", EuManifestationMediaType.XhtmlXml, language, out var langRefusal);
            Assert.IsNotNull(minted, language.ToString());
            Assert.AreEqual(EuDocumentFetchAddressRefusal.None, langRefusal);
            Assert.AreEqual(expectedToken, minted.AcceptLanguage, language.ToString());
        }
    }

    /// <summary>
    /// The closed Accept vocabulary this route may ever ask for, pinned member by member.
    /// </summary>
    /// <remarks>
    /// Added by D1-05d. This vocabulary had NO construction-surface pin before, which is why adding
    /// a ninth and a tenth member broke no gate: a closed set whose whole point is that widening it
    /// is a reviewed act was widened twice with nothing to say so. Every marker below is transcribed
    /// from ConstructionSurface.Of's own printed output, never hand-derived.
    /// </remarks>
    [TestMethod]
    public void TheMediaTypeVocabularyHasExactlyTenMembers()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.";
        const string T = N + "EuManifestationMediaType";
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + T + "::ApplicationPdf -> " + T,
                "field public static " + T + "::PdfTypePdfa2a -> " + T,
                "field public static " + T + "::RdfXml -> " + T,
                "field public static " + T + "::RdfXmlNoticeTree -> " + T,
                "field public static " + T + "::TextHtml -> " + T,
                "field public static " + T + "::XhtmlXml -> " + T,
                "field public static " + T + "::XmlNoticeBranch -> " + T,
                "field public static " + T + "::XmlNoticeIdentifier -> " + T,
                "field public static " + T + "::XmlNoticeObject -> " + T,
                "field public static " + T + "::ZipMtypeFmx4 -> " + T,
            },
            ConstructionSurface.Of(typeof(EuManifestationMediaType)).ToArray());
    }

    [TestMethod]
    public void TryCreateMintsTheCellarPsNameShapeEuWemiIdentityBoundaryAlreadyProves()
    {
        // EuWemiIdentityBoundary's own CellarOrigins constant already proves every Cellar WEMI
        // object's PublisherUri is exactly "https://publications.europa.eu/resource/cellar/{key}",
        // so ps-name=cellar with that same key is this route's real, proven address for such an
        // object -- not an invented shape.
        var address = EuDocumentFetchAddress.TryCreate(
            "cellar",
            "3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
            EuManifestationMediaType.RdfXml,
            EuDocumentLanguage.Eng,
            out var refusal);
        Assert.IsNotNull(address);
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);
        Assert.AreEqual(
            "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
            address.ResourceUri);
    }

    [TestMethod]
    public void TryCreateRefusesAnInadmissiblePsNameOrPsIdShape()
    {
        Assert.IsNull(EuDocumentFetchAddress.TryCreate(
            "CELEX", "32016R0679", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng,
            out var upperCaseRefusal));
        Assert.AreEqual(EuDocumentFetchAddressRefusal.PsNameShapeInvalid, upperCaseRefusal);

        Assert.IsNull(EuDocumentFetchAddress.TryCreate(
            string.Empty, "32016R0679", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng,
            out var emptyNameRefusal));
        Assert.AreEqual(EuDocumentFetchAddressRefusal.PsNameShapeInvalid, emptyNameRefusal);

        Assert.IsNull(EuDocumentFetchAddress.TryCreate(
            "celex", "32016R0679/extra", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng,
            out var slashRefusal));
        Assert.AreEqual(EuDocumentFetchAddressRefusal.PsIdShapeInvalid, slashRefusal);

        Assert.IsNull(EuDocumentFetchAddress.TryCreate(
            "celex", string.Empty, EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng,
            out var emptyIdRefusal));
        Assert.AreEqual(EuDocumentFetchAddressRefusal.PsIdShapeInvalid, emptyIdRefusal);

        // Fold-in: this type's own doc comments claim the minted resource URI carries "no query, no
        // fragment" (see the remarks on IsAdmittedResourceUri). Before this fix, IsAdmittedPsIdShape's
        // character class (> ' ' and <= '~' and not '/') admitted both '?' and '#' -- printable ASCII
        // neither excluded -- so a candidate carrying either was minted into a resourceUri that
        // silently carried a query or a fragment, contradicting the type's own claim.
        Assert.IsNull(EuDocumentFetchAddress.TryCreate(
            "celex", "32016R0679?x=1", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng,
            out var queryRefusal));
        Assert.AreEqual(EuDocumentFetchAddressRefusal.PsIdShapeInvalid, queryRefusal);

        Assert.IsNull(EuDocumentFetchAddress.TryCreate(
            "celex", "32016R0679#frag", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng,
            out var fragmentRefusal));
        Assert.AreEqual(EuDocumentFetchAddressRefusal.PsIdShapeInvalid, fragmentRefusal);
    }

    [TestMethod]
    public void IsAdmittedResourceUriAdmitsOnlyTheExactTwoSegmentShapeOnTheAdmittedHost()
    {
        Assert.IsTrue(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/resource/celex/32016R0679"));
        Assert.IsTrue(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1"));

        // Wrong host, malformed path, missing segment, extra segment, wrong scheme.
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://eur-lex.europa.eu/resource/celex/32016R0679"));
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/webapi/rdf/sparql"));
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/resource/celex"));
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "https://publications.europa.eu/resource/celex/32016R0679/extra"));
        Assert.IsFalse(EuDocumentFetchAddress.IsAdmittedResourceUri(
            "http://publications.europa.eu/resource/celex/32016R0679"));
    }

    [TestMethod]
    public void ToManifestFetchAddressCopiesFieldsVerbatimIntoThePublisherNeutralProjection()
    {
        var address = EuDocumentFetchAddress.TryCreate(
            "celex", "32016R0679", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng, out _)!;
        var projected = address.ToManifestFetchAddress();
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, projected.Status);
        Assert.AreEqual(EuDocumentFetchAddress.AdmittedHost, projected.Host);
        Assert.AreEqual("celex/32016R0679", projected.ResourcePath);
        Assert.AreEqual("application/xhtml+xml", projected.AcceptMediaType);
        Assert.AreEqual("eng", projected.AcceptLanguage);
        Assert.IsNull(projected.NotMintedReason);
    }

    [TestMethod]
    public void ScopeManifestFetchAddressAdmitsExactlyTheMintedOrNotMintedShapes()
    {
        var minted = ScopeManifestFetchAddress.Minted("publications.europa.eu", "celex/1", "a/b", "eng");
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, minted.Status);

        var notMinted = ScopeManifestFetchAddress.NotMinted(
            ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet);
        Assert.AreEqual(ScopeManifestFetchAddressStatus.NotMinted, notMinted.Status);
        Assert.IsNull(notMinted.Host);
        Assert.IsNull(notMinted.ResourcePath);
        Assert.IsNull(notMinted.AcceptMediaType);
        Assert.IsNull(notMinted.AcceptLanguage);
        Assert.AreEqual(
            ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet,
            notMinted.NotMintedReason);

        // A minted status with a missing field, or a not-minted status carrying a stray field, is
        // not one of the two admitted shapes.
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeManifestFetchAddress(
            ScopeManifestFetchAddressStatus.Minted,
            "publications.europa.eu",
            "celex/1",
            "a/b",
            null,
            null));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeManifestFetchAddress(
            ScopeManifestFetchAddressStatus.NotMinted,
            "publications.europa.eu",
            null,
            null,
            null,
            ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeManifestFetchAddress(
            ScopeManifestFetchAddressStatus.Minted,
            "publications.europa.eu",
            "celex/1",
            "a/b",
            "eng",
            ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeManifestFetchAddress(
            ScopeManifestFetchAddressStatus.NotMinted,
            null,
            null,
            null,
            null,
            null));
    }
}
