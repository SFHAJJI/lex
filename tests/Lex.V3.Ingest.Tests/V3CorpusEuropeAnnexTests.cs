using System.Text.Json;
using Lex.V3.Api;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// EU annexes as dossier and evidence_bundle disclose them under <c>annexes_not_served</c>: one row per annex disposition the
/// corpus recorded on the members an expression was read from, each served as text not available with the official source,
/// and one fixed reason for every annex disposition. The GDPR fixture holds no annex, and its dossier and bundle tests hold
/// the empty list.
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeAnnexTests
{
    private const string Expression = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000.0006";
    private const string OfficialSource = "https://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000.0006.02";

    private static string Identity(char digit) => new(digit, 64);

    [TestMethod]
    public void EveryEuAnnexDispositionHasOneFixedReasonAndNoOtherDispositionHasOne()
    {
        // The annex dispositions are the ones the corpus pairs with the annex domain.
        var annex = Enum.GetValues<LexCorpus6Stage3Disposition>().Where(static disposition =>
        {
            try
            {
                _ = new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, Identity('a'), disposition).Validate();
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }).ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                LexCorpus6Stage3Disposition.AnnexTextNotAvailable, LexCorpus6Stage3Disposition.AnnexMappingUnresolved,
                LexCorpus6Stage3Disposition.AnnexBodyContainsText, LexCorpus6Stage3Disposition.AnnexBodyContainsNoImage,
                LexCorpus6Stage3Disposition.AnnexMappedPageOutsideDocument,
            },
            annex);

        var reasons = annex.Select(V3CorpusMount.EuropeAnnexReason).ToArray();
        foreach (var reason in reasons)
        {
            StringAssert.Contains(reason, "publisher PDF", "each reason says what the corpus found against the publisher's PDF");
        }
        Assert.AreEqual(reasons.Length, reasons.Distinct(StringComparer.Ordinal).Count(), "one reason per disposition");
        foreach (var other in Enum.GetValues<LexCorpus6Stage3Disposition>().Except(annex))
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => V3CorpusMount.EuropeAnnexReason(other), other.ToString());
        }
    }

    [TestMethod]
    public void TheAnnexRowsGroupTheCorpusAnnexOutcomesByDispositionEachServedAsTextNotAvailableAndOfficiallyLinked()
    {
        var outcomes = new[]
        {
            new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, Identity('2'), LexCorpus6Stage3Disposition.AnnexTextNotAvailable),
            new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody, Identity('4'), LexCorpus6Stage3Disposition.FormexMainBodyAdmitted),
            new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, Identity('3'), LexCorpus6Stage3Disposition.AnnexBodyContainsText),
            new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, Identity('1'), LexCorpus6Stage3Disposition.AnnexTextNotAvailable),
            // The same annex read from a second member of the expression is one annex.
            new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, Identity('1'), LexCorpus6Stage3Disposition.AnnexTextNotAvailable),
        };

        var reads = 0;
        var rows = JsonSerializer.SerializeToElement(V3CorpusMount.EuropeAnnexRows(outcomes, Expression, () =>
        {
            reads++;
            return OfficialSource;
        })).EnumerateArray().ToArray();
        Assert.AreEqual(1, reads, "the official source is read once for the rows");

        Assert.HasCount(2, rows, "one row per annex disposition; the main-body outcome is no annex");
        var image = rows[0];
        Assert.AreEqual("annex_text_not_available", image.GetProperty("disposition").GetString());
        Assert.AreEqual(2, image.GetProperty("annexes").GetInt32());
        CollectionAssert.AreEqual(
            new[] { Identity('1'), Identity('2') },
            image.GetProperty("annex_identities_sha256").EnumerateArray().Select(static value => value.GetString()).ToArray());
        Assert.AreEqual("text_not_available", image.GetProperty("served_as").GetString(), "the launch contract's word for an image-only annex");
        Assert.AreEqual(Expression, image.GetProperty("official_identity").GetString());
        Assert.AreEqual(OfficialSource, image.GetProperty("official_source").GetString());
        Assert.AreEqual(V3CorpusMount.EuropeAnnexReason(LexCorpus6Stage3Disposition.AnnexTextNotAvailable), image.GetProperty("reason").GetString());
        StringAssert.Contains(image.GetProperty("reason").GetString(), "image-only");

        var text = rows[1];
        Assert.AreEqual("annex_body_contains_text", text.GetProperty("disposition").GetString());
        Assert.AreEqual(1, text.GetProperty("annexes").GetInt32());
        Assert.AreEqual("text_not_available", text.GetProperty("served_as").GetString(), "no annex text is served, whatever its body holds");
        Assert.AreEqual(OfficialSource, text.GetProperty("official_source").GetString());
        Assert.IsFalse(rows.Any(static row => row.GetRawText().Contains(Identity('4'), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WithNoAnnexOutcomeNoRowIsListedAndTheOfficialSourceIsNotRead()
    {
        var mainBodyOnly = new[]
        {
            new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody, Identity('4'), LexCorpus6Stage3Disposition.FormexMainBodyAdmitted),
        };

        var reads = 0;
        string Read()
        {
            reads++;
            return OfficialSource;
        }

        Assert.IsEmpty(V3CorpusMount.EuropeAnnexRows(mainBodyOnly, Expression, Read));
        Assert.IsEmpty(V3CorpusMount.EuropeAnnexRows([], Expression, Read));
        Assert.AreEqual(0, reads, "the official source is read only for a row");
    }
}
