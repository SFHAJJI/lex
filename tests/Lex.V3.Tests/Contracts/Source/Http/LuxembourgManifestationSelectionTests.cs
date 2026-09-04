using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class LuxembourgManifestationSelectionTests
{
    private const string XmlUriText =
        "https://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/x.xml";
    private const string PdfUriText =
        "https://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/pdfa/x.pdf";

    private static LuxembourgFileUri XmlUri => LuxembourgFileUri.RequireValid(XmlUriText);

    private static LuxembourgFileUri PdfUri => LuxembourgFileUri.RequireValid(PdfUriText);

    [TestMethod]
    public void XmlWinsWhenBothAreListed()
    {
        var selection = LuxembourgManifestationSelection.Select(XmlUri, PdfUri);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgManifestationFormat.Xml, selection.Format);
        Assert.AreEqual(XmlUriText, selection.FileUri);
    }

    [TestMethod]
    public void PdfAIsSelectedWhenXmlIsNotListed()
    {
        var selection = LuxembourgManifestationSelection.Select(null, PdfUri);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgManifestationFormat.PdfA, selection.Format);
        Assert.AreEqual(PdfUriText, selection.FileUri);
    }

    [TestMethod]
    public void NeitherListedIsATypedAbsenceCarryingNoFormat()
    {
        var selection = LuxembourgManifestationSelection.Select(null, null);

        Assert.AreEqual(
            LuxembourgManifestationSelectionOutcome.NoManifestationAvailable,
            selection.Outcome);
        Assert.IsNull(selection.Format);
        Assert.IsNull(selection.FileUri);
    }

    [TestMethod]
    public void XmlAloneIsSelectedWithoutRequiringAPdfCandidate()
    {
        var selection = LuxembourgManifestationSelection.Select(XmlUri, null);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgManifestationFormat.Xml, selection.Format);
        Assert.AreEqual(XmlUriText, selection.FileUri);
    }
}
