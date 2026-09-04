using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

/// <summary>
/// D1-06c-LU-2 item 2: the manifestation selection ladder ruled by
/// lex-event-20260904T174533266Z-bcf05c64ac1b43a3a4f8acf75196a6d5 and corrected by
/// lex-event-20260904T180020924Z-ca9982dc058b4d539f2e4a61662af959. Every test below says whether it
/// is grounded in an observation or is a shape test for an arm nobody has found an instance of.
/// </summary>
[TestClass]
public sealed class LuxembourgManifestationSelectionTests
{
    private const string Filestore =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/";

    private static LuxembourgManifestationCandidate Candidate(
        LuxembourgUserFormatToken token,
        LuxembourgLegalValue legalValue,
        string? uriText = null) =>
        new(token, legalValue, LuxembourgFileUri.RequireValid(
            uriText ?? Filestore + TokenSegment(token) + "/x"));

    private static string TokenSegment(LuxembourgUserFormatToken token) => token switch
    {
        LuxembourgUserFormatToken.XmlAkomaNtoso => "xml-akomantoso",
        LuxembourgUserFormatToken.Xml => "xml",
        LuxembourgUserFormatToken.PdfA => "pdfa",
        LuxembourgUserFormatToken.Pdf => "pdf",
        _ => throw new AssertFailedException("unknown token"),
    };

    /// <summary>
    /// GROUNDED. Both tokens are real and both were read: the retained xml-akomantoso instance
    /// (9e43a99e.., 19,986 bytes) and the plain-xml Civil Code (71695f37.., 5,531,380 bytes, an
    /// akomaNtoso root in the AKN 3.0 CSD13 namespace). The ruling admits both as wording
    /// candidates, xml-akomantoso first.
    /// </summary>
    [TestMethod]
    public void XmlAkomaNtosoPrecedesPlainXmlWhenBothAreListed()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel),
            Candidate(LuxembourgUserFormatToken.XmlAkomaNtoso, LuxembourgLegalValue.Officiel),
        ]);

        Assert.AreEqual(LuxembourgManifestationSelectionOutcome.Selected, selection.Outcome);
        Assert.AreEqual(LuxembourgUserFormatToken.XmlAkomaNtoso, selection.Token);
        StringAssert.Contains(selection.FileUri, "/xml-akomantoso/");
    }

    /// <summary>
    /// GROUNDED. 250 of the 1,000 probed expressions carry xml-akomantoso and 586 carry pdf; the
    /// PDF-only act rgd 1977/11/16/n3 was fetched whole (13a73ea1.., 124,932 bytes), so both arms
    /// of "XML over PDF" name real, observed manifestation kinds.
    /// </summary>
    [TestMethod]
    public void EitherXmlTokenPrecedesEitherPdfTokenAtEqualLegalValue()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel),
            Candidate(LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel),
        ]);

        Assert.AreEqual(LuxembourgUserFormatToken.Xml, selection.Token);
    }

    /// <summary>
    /// GROUNDED in the store's own counts: pdfa is the only token whose legalValue splits (80,291
    /// definitif against 28,251 officiel), which is exactly why the ladder ranks the publisher's
    /// own marker above the token rather than the other way round.
    /// </summary>
    [TestMethod]
    public void AnOfficielManifestationOutranksADefinitifOneWhateverTheTokenOrder()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.PdfA, LuxembourgLegalValue.Definitif),
            Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel),
        ]);

        Assert.AreEqual(LuxembourgUserFormatToken.Pdf, selection.Token);
        Assert.AreEqual(LuxembourgLegalValue.Officiel, selection.Selected?.LegalValue);
    }

    /// <summary>
    /// SHAPE TEST, NOT GROUNDED IN OBSERVATION. A direct query for an expression offering both pdfa
    /// and pdf, admin paths excluded, returned ZERO rows, and the separate 1,000-expression sample
    /// contained no such format set either
    /// (lex-event-20260904T180020924Z-ca9982dc058b4d539f2e4a61662af959). The pdfa-before-pdf step is
    /// kept because the ruling states it and a total order needs it, not because anyone has seen a
    /// case it decides. The candidates below are constructed, and nothing here is called observed.
    /// </summary>
    [TestMethod]
    public void PdfAPrecedesPdfAtEqualLegalValue_ShapeTestNotGroundedInObservation()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel),
            Candidate(LuxembourgUserFormatToken.PdfA, LuxembourgLegalValue.Officiel),
        ]);

        Assert.AreEqual(LuxembourgUserFormatToken.PdfA, selection.Token);
    }

    /// <summary>
    /// SHAPE TEST, NOT GROUNDED IN OBSERVATION. Zero of the 1,000 probed expressions offered
    /// neither an xml nor a pdf token
    /// (lex-event-20260904T174227089Z-8f2c03f33d1c4e95b397323c992bbfce), so this arm is a
    /// closed-world necessity of the selection function. The ruling kept it in code with its
    /// acquisition-level fixture explicitly out of scope; no synthetic response anywhere is called
    /// observed.
    /// </summary>
    [TestMethod]
    public void NoCandidateIsATypedAbsenceCarryingNoTokenAndNoUri_ShapeTestNotGroundedInObservation()
    {
        var selection = LuxembourgManifestationSelection.Select([]);

        Assert.AreEqual(
            LuxembourgManifestationSelectionOutcome.NoManifestationAvailable,
            selection.Outcome);
        Assert.IsNull(selection.Selected);
        Assert.IsNull(selection.Token);
        Assert.IsNull(selection.FileUri);
    }

    /// <summary>
    /// The order a caller happens to enumerate candidates in must not decide the answer. Both
    /// permutations of the same two-candidate set are asserted to give the identical winner, which
    /// is what makes the ladder a total order rather than a first-match scan.
    /// </summary>
    [TestMethod]
    public void SelectionDoesNotDependOnEnumerationOrder()
    {
        var xml = Candidate(LuxembourgUserFormatToken.XmlAkomaNtoso, LuxembourgLegalValue.Officiel);
        var pdf = Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel);

        Assert.AreEqual(
            LuxembourgManifestationSelection.Select([xml, pdf]).FileUri,
            LuxembourgManifestationSelection.Select([pdf, xml]).FileUri);
    }

    /// <summary>
    /// Two candidates identical in legal value and token still resolve to exactly one answer, by
    /// the store URI's own ordinal order. Without this the ladder would be a partial order and two
    /// runs over the same data could fetch different files.
    /// </summary>
    [TestMethod]
    public void AnExactTieIsBrokenDeterministicallyByTheStoreFileUri()
    {
        var first = Candidate(
            LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel, Filestore + "pdf/a");
        var second = Candidate(
            LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel, Filestore + "pdf/b");

        Assert.AreEqual(
            first.FileUri.Value.AbsoluteUri,
            LuxembourgManifestationSelection.Select([second, first]).FileUri);
        Assert.AreEqual(
            first.FileUri.Value.AbsoluteUri,
            LuxembourgManifestationSelection.Select([first, second]).FileUri);
    }

    /// <summary>
    /// The four exact userFormat tokens are what the record names, never a normalised category:
    /// the two XML tokens must stay distinguishable from each other and the two PDF tokens from
    /// each other, which is the whole point of ruling item 4.
    /// </summary>
    [TestMethod]
    public void TheSelectedCandidateNamesTheExactTokenRatherThanACategory()
    {
        foreach (var token in Enum.GetValues<LuxembourgUserFormatToken>())
        {
            var selection = LuxembourgManifestationSelection.Select(
                [Candidate(token, LuxembourgLegalValue.Officiel)]);

            Assert.AreEqual(token, selection.Token);
        }

        Assert.AreEqual(4, Enum.GetValues<LuxembourgUserFormatToken>().Length);
    }

    /// <summary>
    /// The store's real non-wording tokens parse to null rather than to a nearby member. This is
    /// the substring bug the canary caught, pinned: "xml" is a substring of "xml-akomantoso" and
    /// "pdf" of "pdfa", and a first probe pass that classified by substring produced a frequency
    /// table that contradicted itself.
    /// </summary>
    [TestMethod]
    public void AuthorityTokensParseByLastSegmentRatherThanBySubstring()
    {
        const string prefix = "http://data.legilux.public.lu/resource/authority/user-format/";

        Assert.AreEqual(
            LuxembourgUserFormatToken.XmlAkomaNtoso,
            LuxembourgAuthorityIri.TryParseUserFormat(prefix + "xml-akomantoso"));
        Assert.AreEqual(
            LuxembourgUserFormatToken.Xml,
            LuxembourgAuthorityIri.TryParseUserFormat(prefix + "xml"));
        Assert.AreEqual(
            LuxembourgUserFormatToken.PdfA,
            LuxembourgAuthorityIri.TryParseUserFormat(prefix + "pdfa"));
        Assert.AreEqual(
            LuxembourgUserFormatToken.Pdf,
            LuxembourgAuthorityIri.TryParseUserFormat(prefix + "pdf"));

        // Real store tokens this route does not select, and one that is not this authority at all.
        Assert.IsNull(LuxembourgAuthorityIri.TryParseUserFormat(prefix + "html"));
        Assert.IsNull(LuxembourgAuthorityIri.TryParseUserFormat(prefix + "docx"));
        Assert.IsNull(LuxembourgAuthorityIri.TryParseUserFormat(prefix + "svg"));
        Assert.IsNull(LuxembourgAuthorityIri.TryParseUserFormat(prefix + "pdf/extra"));
        Assert.IsNull(LuxembourgAuthorityIri.TryParseUserFormat(
            "http://data.legilux.public.lu/resource/authority/statut-version/officiel"));
    }

    /// <summary>The two legalValue markers the store actually carries, and nothing else.</summary>
    [TestMethod]
    public void LegalValueParsesTheTwoMarkersTheStoreCarries()
    {
        const string prefix = "http://data.legilux.public.lu/resource/authority/statut-version/";

        Assert.AreEqual(
            LuxembourgLegalValue.Officiel,
            LuxembourgAuthorityIri.TryParseLegalValue(prefix + "officiel"));
        Assert.AreEqual(
            LuxembourgLegalValue.Definitif,
            LuxembourgAuthorityIri.TryParseLegalValue(prefix + "definitif"));
        Assert.IsNull(LuxembourgAuthorityIri.TryParseLegalValue(prefix + "provisoire"));
        Assert.AreEqual(2, Enum.GetValues<LuxembourgLegalValue>().Length);
    }
}
