using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

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
    /// GROUNDED. Both kinds are real and both were fetched whole: the retained xml-akomantoso
    /// instance (9e43a99e.., 19,986 bytes) and the PDF-only act rgd 1977/11/16/n3 (13a73ea1..,
    /// 124,932 bytes). Store-wide totals, measured with an OPTIONAL so manifestations without a
    /// legalValue are counted rather than dropped: 63,862 xml-akomantoso, 34,569 xml, 107,485 pdfa,
    /// 153,648 pdf.
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
    /// GROUNDED, and this is the case the whole amendment turns on. Legal value NEVER outranks
    /// format: an officiel PDF does not beat an unmarked Akoma Ntoso XML. The ladder ranked legal
    /// value first until RULING lex-event-20260904T194018108Z-62079c93ce9d405ca1fb326cfea41bd9,
    /// on a census that joined on jolux:legalValue and so dropped every manifestation without one.
    /// With the absent rows counted, 99.5 percent of plain xml and 42 percent of xml-akomantoso
    /// carry no marker, so legal-value-first inverted D49 in tens of thousands of expressions
    /// rather than refining it in none.
    /// </summary>
    [TestMethod]
    public void LegalValueNeverOutranksFormat()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel),
            Candidate(LuxembourgUserFormatToken.XmlAkomaNtoso, LuxembourgLegalValue.Unstated),
        ]);

        Assert.AreEqual(LuxembourgUserFormatToken.XmlAkomaNtoso, selection.Token);
        Assert.AreEqual(LuxembourgLegalValue.Unstated, selection.Selected?.LegalValue);
    }

    /// <summary>
    /// GROUNDED IN ONE NAMED ACT rather than in a rate. loi 2021/09/09/a676/jo/fr, retained at
    /// digest f36772f7377f0d30f827a74594165219b42374e4a420fc9f39cd890d059d5efe (PROBE_RESULT
    /// lex-event-20260904T194555823Z-c9118e0eeb5a4433bc7488ae768ea6ea), lists exactly four
    /// manifestations: pdfa marked DEFINITIF, and html, xml-akomantoso and docx with legalValue
    /// ABSENT, every one carrying a file. Under the previous ladder its XML was dropped for having
    /// no marker and the definitif PDF/A was the only survivor, so a 2021 Luxembourg law would have
    /// been held as a PDF/A instead of its Akoma Ntoso XML. This asserts the amended answer on that
    /// exact candidate set. html and docx are not admitted tokens and so are not candidates at all.
    /// </summary>
    [TestMethod]
    public void TheRealLoi2021SelectsItsUnmarkedAkomaNtosoXmlOverItsDefinitifPdfA()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.PdfA, LuxembourgLegalValue.Definitif),
            Candidate(LuxembourgUserFormatToken.XmlAkomaNtoso, LuxembourgLegalValue.Unstated),
        ]);

        Assert.AreEqual(LuxembourgUserFormatToken.XmlAkomaNtoso, selection.Token);
        Assert.AreEqual(
            LuxembourgLegalValue.Unstated,
            selection.Selected?.LegalValue,
            "and it is carried as Unstated, never rewritten to officiel or to definitif.");
    }

    /// <summary>
    /// Legal value still decides, but only WITHIN one token: the ruling's own example of an
    /// officiel pdf over an unmarked pdf. Without this the property would order nothing at all.
    /// </summary>
    [TestMethod]
    public void LegalValueOrdersWithinOneToken()
    {
        var selection = LuxembourgManifestationSelection.Select(
        [
            Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Unstated, Filestore + "pdf/a"),
            Candidate(LuxembourgUserFormatToken.Pdf, LuxembourgLegalValue.Officiel, Filestore + "pdf/z"),
        ]);

        Assert.AreEqual(LuxembourgLegalValue.Officiel, selection.Selected?.LegalValue);
        StringAssert.EndsWith(
            selection.FileUri,
            "pdf/z",
            "and it wins on its marker, not on the URI tie-break, which would have chosen 'a'.");
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
    /// The typed absence, REACHABLE ONLY WHEN NO ADMITTED TOKEN IS LISTED. Its earlier label,
    /// "shape test not grounded in observation", was accurate about the token sample (zero of 1,000
    /// probed expressions offered neither an xml nor a pdf token) and misleading about the arm: the
    /// arm was also reachable for a second, wrong reason, because a manifestation without a
    /// legalValue was dropped from candidacy, so an expression whose files were all unmarked
    /// reported an absence that was not one. That drop is gone, and this outcome now means exactly
    /// what its name says.
    /// </summary>
    [TestMethod]
    public void NoAdmittedTokenListedIsATypedAbsenceCarryingNoTokenAndNoUri()
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

    /// <summary>
    /// RULING lex-event-20260904T194556163Z-dd9191017eaf4c3b83ea04862933006f item three: there is
    /// ONE closed userFormat vocabulary in this tree, <c>LuxembourgScopeResolver</c>'s own three
    /// sets, and the route's admitted-token table is checked against it here rather than
    /// transcribed from a probe beside it. That is exactly how <c>doc</c> stayed invisible: it was
    /// in NeverFormats all along, and absent from the probe the route's table was written from,
    /// because the probe joined on jolux:legalValue and all four doc manifestations lack one.
    /// <para>
    /// Every token the resolver knows must be classified by the route: either selected (the four
    /// wording candidates, in ladder order) or listed and never selected. Adding a token to the
    /// resolver without teaching the route about it fails here.
    /// </para>
    /// </summary>
    [TestMethod]
    public void TheRoutesAdmittedTokensArePartitionedFromTheResolversOwnVocabulary()
    {
        const string prefix = "http://data.legilux.public.lu/resource/authority/user-format/";
        var known = LuxembourgScopeResolver.KnownUserFormatIris;

        CollectionAssert.AreEqual(
            new[]
            {
                prefix + "doc",
                prefix + "docx",
                prefix + "html",
                prefix + "pdf",
                prefix + "pdfa",
                prefix + "svg",
                prefix + "xml",
                prefix + "xml-akomantoso",
            },
            known.ToArray(),
            "the resolver's own vocabulary is the closed list; this is it, sorted.");

        var selected = known
            .Where(static iri => LuxembourgAuthorityIri.TryParseUserFormat(iri) is not null)
            .ToArray();
        var listedNotSelected = known
            .Where(static iri => LuxembourgAuthorityIri.TryParseUserFormat(iri) is null)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { prefix + "pdf", prefix + "pdfa", prefix + "xml", prefix + "xml-akomantoso" },
            selected,
            "the four wording candidates the ladder ranks.");
        CollectionAssert.AreEqual(
            new[] { prefix + "doc", prefix + "docx", prefix + "html", prefix + "svg" },
            listedNotSelected,
            "doc, docx and svg are listed and never selected; html is listed and deferred for LU "
            + "exactly as it is for EU, until an LU html manifestation is actually probed.");
        Assert.AreEqual(known.Count, selected.Length + listedNotSelected.Length);
    }

    /// <summary>
    /// The seven tokens the OPTIONAL census actually observed in the store are all known to the
    /// resolver. svg is the eighth token the resolver refuses and the census did not observe, which
    /// is a live divergence rather than a defect: refusing a token nobody has published costs
    /// nothing, and it is recorded here so the next reader does not have to rediscover the
    /// asymmetry.
    /// </summary>
    [TestMethod]
    public void EveryCensusedStoreTokenIsKnownToTheResolver()
    {
        const string prefix = "http://data.legilux.public.lu/resource/authority/user-format/";
        string[] censused =
            ["doc", "docx", "html", "pdf", "pdfa", "xml", "xml-akomantoso"];

        foreach (var token in censused)
        {
            Assert.Contains(
                prefix + token,
                LuxembourgScopeResolver.KnownUserFormatIris,
                $"the census observed '{token}' and the resolver must know it.");
        }

        Assert.AreEqual(7, censused.Length);
        Assert.AreEqual(8, LuxembourgScopeResolver.KnownUserFormatIris.Count);
    }

    /// <summary>The three legalValue states, two markers the store carries and the typed absence.</summary>
    [TestMethod]
    public void LegalValueParsesTheTwoMarkersTheStoreCarriesAndNamesTheAbsence()
    {
        const string prefix = "http://data.legilux.public.lu/resource/authority/statut-version/";

        Assert.AreEqual(
            LuxembourgLegalValue.Officiel,
            LuxembourgAuthorityIri.TryParseLegalValue(prefix + "officiel"));
        Assert.AreEqual(
            LuxembourgLegalValue.Definitif,
            LuxembourgAuthorityIri.TryParseLegalValue(prefix + "definitif"));
        Assert.IsNull(LuxembourgAuthorityIri.TryParseLegalValue(prefix + "provisoire"));

        // Unstated is a state the minting assigns for an absent property, never a value the store
        // publishes, so the IRI parser must not produce it from any IRI at all.
        Assert.AreEqual(3, Enum.GetValues<LuxembourgLegalValue>().Length);
        Assert.IsNull(LuxembourgAuthorityIri.TryParseLegalValue(prefix + "unstated"));
    }
}
