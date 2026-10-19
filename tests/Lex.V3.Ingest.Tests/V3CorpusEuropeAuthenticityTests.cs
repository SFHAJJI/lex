using System.Text.Json;
using Lex.V3.Api;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The authenticity statement every EU text carries (Decision 95), chosen by the act's own date: the owner's decision of
/// 2026-10-18 on PR #975's question (item O4, "yes yes go with what you recommend"), to the specification of record's R20 (the
/// electronic Official Journal is authentic since 1 July 2013, the printed edition before). The EU index holds no Official
/// Journal publication date, so the act's date chooses: the electronic edition's sentence for an act dated on or after
/// 1 July 2013, which was published on or after it; otherwise the dated sentence, which says which edition is authentic on
/// either side of that day, so it also holds for an act adopted in June 2013 and published in July. Every wording of an act,
/// a consolidation's too, takes the act's sentence, on both EU paths (the time view's and the original wording's).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class V3CorpusEuropeAuthenticityTests
{
    /// <summary>The owner's words, verbatim, for an act published on or after 1 July 2013.</summary>
    private const string ElectronicSentence =
        "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.";

    /// <summary>The owner's words, verbatim, for an act published before 1 July 2013.</summary>
    private const string DatedSentence =
        "For an act published before 1 July 2013, the printed Official Journal of the European Union is authentic; the electronic edition is authentic since that date (Regulation (EU) No 216/2013). This text is a reproduction read from the Publications Office's Formex package, not the authentic edition.";

    private const string Electronic = V3CorpusMount.EuropeElectronicEdition;
    private const string Dated = V3CorpusMount.EuropeEditionByPublicationDate;

    [TestMethod]
    public void TheTwoSentencesAreTheOwnersWordForWord()
    {
        Assert.HasCount(2, V3CorpusMount.EuropeTextAuthenticity);
        Assert.AreEqual(ElectronicSentence, V3CorpusMount.EuropeTextAuthenticity[Electronic]);
        Assert.AreEqual(DatedSentence, V3CorpusMount.EuropeTextAuthenticity[Dated]);
    }

    [TestMethod]
    [DataRow("2013-06-30", Dated, DisplayName = "the day before 1 July 2013")]
    [DataRow("2013-07-01", Electronic, DisplayName = "1 July 2013")]
    [DataRow("2013-07-02", Electronic, DisplayName = "the day after")]
    [DataRow("2013-06-26", Dated, DisplayName = "June 2013, published on either side of the day")]
    [DataRow("2013-01-01", Dated, DisplayName = "earlier in 2013")]
    [DataRow("1992-02-07", Dated, DisplayName = "long before")]
    [DataRow("2016-04-27", Electronic, DisplayName = "the GDPR's")]
    [DataRow("2024-10-23", Electronic, DisplayName = "the population's latest")]
    public void TheActsDateChoosesTheSentence(string date, string basis)
    {
        var chosen = V3CorpusMount.EuropeAuthenticityOf([date]);
        Assert.AreEqual(basis, chosen.Basis);
        Assert.AreEqual(V3CorpusMount.EuropeTextAuthenticity[basis], chosen.Sentence);
        CollectionAssert.AreEqual(new[] { date }, chosen.ActDates.ToArray());
    }

    [TestMethod]
    public void WithNoDateHeldOrAnEarlierOneAmongSeveralTheDatedSentenceIsSaid()
    {
        // No date of the act held: Lex cannot say it was published on or after 1 July 2013.
        var none = V3CorpusMount.EuropeAuthenticityOf([]);
        Assert.AreEqual(Dated, none.Basis);
        Assert.IsEmpty(none.ActDates);

        // Two dates on either side of the day (a Formex date and a document date that disagree): the earlier decides.
        var both = V3CorpusMount.EuropeAuthenticityOf(["2013-07-02", "2013-06-28", "2013-07-02"]);
        Assert.AreEqual(Dated, both.Basis);
        CollectionAssert.AreEqual(new[] { "2013-06-28", "2013-07-02" }, both.ActDates.ToArray(), "once each, in order");

        // Two dates after it (derive-c's 32022L2523: its Formex date 2022-12-14, its document date 2022-12-15).
        Assert.AreEqual(Electronic, V3CorpusMount.EuropeAuthenticityOf(["2022-12-15", "2022-12-14"]).Basis);

        // A date that is not one civil date is a damaged index, never a choice.
        Assert.ThrowsExactly<InvalidDataException>(() => V3CorpusMount.EuropeAuthenticityOf(["20130626"]));
        Assert.ThrowsExactly<InvalidDataException>(() => V3CorpusMount.EuropeAuthenticityOf(["2013-02-30"]));
    }

    /// <summary>
    /// The population's EU acts with text held whose own date falls before 1 July 2013 (derive-c's EU index, read read-only: the
    /// publisher's work_date_document of each seed act's work, which the census holds for all 82 acts; 70 acts have a wording's
    /// text held, and these 16 of them are dated before the day). Each was published in the Official Journal before 1 July 2013
    /// too (the two of June 2013, CRD IV and the CRR, in OJ L 176 of 27 June 2013), so the dated sentence names the edition that
    /// is authentic for each; under the undated sentence each would have named the wrong one (the review of #975, F2).
    /// </summary>
    [TestMethod]
    [DataRow("32003L0087", "2003-10-13")]
    [DataRow("32003R0001", "2002-12-16")]
    [DataRow("32004L0048", "2004-04-29")]
    [DataRow("32005L0029", "2005-05-11")]
    [DataRow("32006L0054", "2006-07-05")]
    [DataRow("32006L0112", "2006-11-28")]
    [DataRow("32006L0116", "2006-12-12")]
    [DataRow("32007L0036", "2007-07-11")]
    [DataRow("32007R0864", "2007-07-11")]
    [DataRow("32008R0593", "2008-06-17")]
    [DataRow("32011L0016", "2011-02-15")]
    [DataRow("32011L0083", "2011-10-25")]
    [DataRow("32012R1215", "2012-12-12")]
    [DataRow("32012R1257", "2012-12-17")]
    [DataRow("32013L0036", "2013-06-26")]
    [DataRow("32013R0575", "2013-06-26")]
    public void ThePopulationsActsDatedBefore1July2013SayTheDatedSentence(string celex, string date)
    {
        Assert.AreEqual(Dated, V3CorpusMount.EuropeAuthenticityOf([date]).Basis, celex);
    }

    /// <summary>
    /// Over a derived mount whose original wording is dated <paramref name="formexDate"/> (a fixture's date in the GDPR's package)
    /// and which holds a consolidation of 2024-01-01 in both languages: the original wording at its date and the consolidation at
    /// a later date (the time view), and the original reached by an article identity (the original-wording path, which serves
    /// original legal text only): each says the act's sentence, with the act's date, never the consolidation's. The day before
    /// and the day itself; an act dated later is the GDPR's, in the time view's and the evidence bundle's own tests.
    /// </summary>
    [TestMethod]
    [DataRow("20130630", "2013-06-30", Dated, DisplayName = "an act dated the day before 1 July 2013")]
    [DataRow("20130701", "2013-07-01", Electronic, DisplayName = "an act dated 1 July 2013")]
    public async Task EveryWordingOfAnActSaysTheSentenceItsOwnDateChooses(string formexDate, string actDate, string basis)
    {
        var consolidation = new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(
            V3CorpusEuropeTimeViewBranchTests.WorkB, V3CorpusEuropeTimeViewTests.ConsolidationDate, V3CorpusEuropeTimeViewBranchTests.Designated,
            V3CorpusEuropeTimeViewBranchTests.EnglishPackage, V3CorpusEuropeTimeViewBranchTests.FrenchPackage);
        var (root, directory) = await V3CorpusEuropeTimeViewTests.OriginalDatedMountAsync(formexDate, consolidation);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, _) = await V3CorpusEuropeTimeViewTests.OriginalPinAsync(mount, "eng");
            Assert.AreEqual(actDate, originalDate, "the index holds the fixture's date as the original wording's");

            void SaysTheActsSentence(JsonElement value, string what)
            {
                Assert.AreEqual(V3CorpusMount.EuropeTextAuthenticity[basis], value.GetProperty("authenticity").GetString(), what);
                Assert.AreEqual(basis, value.GetProperty("authenticity_basis").GetString(), what);
                CollectionAssert.AreEqual(new[] { actDate }, value.GetProperty("authenticity_act_dates").EnumerateArray().Select(static date => date.GetString()).ToArray(), what);
                Assert.AreEqual(V3CorpusMount.EuropeAuthenticityRule, value.GetProperty("authenticity_rule").GetString(), what);
                Assert.AreEqual("© European Union, https://eur-lex.europa.eu", value.GetProperty("acknowledgement").GetString(), what);
            }

            async Task<JsonElement> BundleAsync(object parameters, string what)
            {
                var envelope = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", parameters);
                Assert.IsNull(envelope.Refusal, $"{what}: {envelope.Refusal?.Code}");
                return envelope.Result!.Value;
            }

            // The time view: the original wording on its date, in both languages, and the consolidation of 2024 at a later date.
            var original = await BundleAsync(new { identifier = V3CorpusEuropeTimeViewTests.GdprSeed, date = originalDate }, "the original wording");
            CollectionAssert.AreEqual(new[] { "eng", "fra" }, original.GetProperty("served_languages").EnumerateArray().Select(static language => language.GetString()).ToArray());
            Assert.AreEqual(V3CorpusMount.EuropeTimeEvidenceBundleScope, original.GetProperty("scope").GetString());
            SaysTheActsSentence(original, "the original wording");
            var consolidated = await BundleAsync(new { identifier = V3CorpusEuropeTimeViewTests.GdprSeed, date = "2025-03-01", language = "eng" }, "the consolidation");
            Assert.AreEqual("consolidated_version", consolidated.GetProperty("wordings").EnumerateArray().Single().GetProperty("kind").GetString());
            SaysTheActsSentence(consolidated, "the consolidation of 2024, a wording of the act");

            // The original-wording path, reached by an article identity: the original's, and the consolidation's, whose package
            // gives its own text the GDPR's Formex date, 2016-04-27; the act's census still chooses.
            var originalArticle = original.GetProperty("wordings").EnumerateArray().First().GetProperty("articles").EnumerateArray().First();
            var byOriginalArticle = await BundleAsync(new { identifier = originalArticle.GetProperty("article_identity_sha256").GetString(), date = originalDate, language = "eng" }, "the original by an article");
            Assert.AreEqual(V3CorpusMount.EuropeEvidenceBundleScope, byOriginalArticle.GetProperty("scope").GetString(), "an article identity reaches the original-wording path");
            SaysTheActsSentence(byOriginalArticle, "the original by an article");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
