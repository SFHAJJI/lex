using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class LuxembourgManifestationSelectionTests
{
    private const string XmlUri =
        "https://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/x.xml";
    private const string PdfUri =
        "https://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/pdfa/x.pdf";

    [TestMethod]
    public void XmlWinsWhenBothAreListed()
    {
        var selection = LuxembourgManifestationSelection.Select(XmlUri, PdfUri);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgManifestationFormat.Xml, selection.Format);
        Assert.AreEqual(XmlUri, selection.FileUri);
    }

    [TestMethod]
    public void PdfAIsSelectedWhenXmlIsNotListed()
    {
        var selection = LuxembourgManifestationSelection.Select(null, PdfUri);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgManifestationFormat.PdfA, selection.Format);
        Assert.AreEqual(PdfUri, selection.FileUri);
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
    public void AnEmptyStringIsTreatedAsNotListed()
    {
        var selection = LuxembourgManifestationSelection.Select(string.Empty, PdfUri);

        Assert.AreEqual(LuxembourgManifestationFormat.PdfA, selection.Format);
    }

    [TestMethod]
    public void XmlAloneIsSelectedWithoutRequiringAPdfCandidate()
    {
        var selection = LuxembourgManifestationSelection.Select(XmlUri, null);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgManifestationFormat.Xml, selection.Format);
        Assert.AreEqual(XmlUri, selection.FileUri);
    }
}
