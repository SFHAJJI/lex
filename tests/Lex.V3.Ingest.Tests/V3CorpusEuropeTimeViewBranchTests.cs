using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU time view's selection branches (review of #909), each on a mount derived from the generalised consolidated fixture:
/// works sharing a date with one text (the publisher's designated work answers, the others disclosed), with two texts
/// (ambiguous, none chosen, not even by verify), the latest wording with no text in a language (text_not_available there),
/// and versions with no usable date (disclosed when their held text is the same, ambiguous when it differs).
/// </summary>
public sealed partial class V3FirstMountBuildTests
{
    private const string WorkB = "http://publications.europa.eu/resource/cellar/bbbbbbbb-0000-0000-0000-00000000000b";
    private const string WorkC = "http://publications.europa.eu/resource/cellar/cccccccc-0000-0000-0000-00000000000c";
    private const string WorkD = "http://publications.europa.eu/resource/cellar/dddddddd-0000-0000-0000-00000000000d";
    private const string Designated = "02016R0679-20240101";

    private static byte[] EnglishPackage => EuFirstMountAcquisitionTests.GdprEnglishPackage();
    private static byte[] FrenchPackage => EuFirstMountAcquisitionTests.FrenchOf(EnglishPackage);
    private static byte[] OtherEnglishPackage => EuFirstMountAcquisitionTests.RewrittenPackage(EnglishPackage, "natural persons", "natural humans");

    private static Task<V3Envelope> EuAsOfAsync(V3CorpusMount mount, string date, string? language = "eng") =>
        language is null
            ? EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date })
            : EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date, language });

    [TestMethod]
    public async Task WorksSharingADateWithOneTextAnswerWithThePublishersDesignatedWorkAndDiscloseTheOthers()
    {
        var (root, directory) = await ConsolidatedWorksMountAsync(
            new(WorkB, ConsolidationDate, Designated, EnglishPackage, FrenchPackage),
            new(WorkC, ConsolidationDate, null, EnglishPackage, FrenchPackage),
            new(WorkD, ConsolidationDate, null, null, null));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var answer = await EuAsOfAsync(mount, "2025-03-01");
            Assert.IsNull(answer.Refusal, answer.Refusal?.Code);
            var wording = answer.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("wording");
            Assert.AreEqual(WorkB, wording.GetProperty("publisher_work_id").GetString(), "the work whose CELEX names the version represents the date");
            Assert.AreEqual("identical_held_text", wording.GetProperty("basis").GetString());
            var others = wording.GetProperty("same_date_works").EnumerateArray()
                .ToDictionary(static work => work.GetProperty("publisher_work_id").GetString()!, StringComparer.Ordinal);
            CollectionAssert.AreEquivalent(new[] { WorkC, WorkD }, others.Keys.ToArray());
            Assert.IsTrue(others[WorkC].GetProperty("text_held").GetBoolean());
            Assert.IsTrue(others[WorkC].GetProperty("same_text").GetBoolean());
            Assert.IsFalse(others[WorkD].GetProperty("text_held").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, others[WorkD].GetProperty("same_text").ValueKind, "a text not held is never said to be the same");

            // The bundle quotes the same wording and discloses the same works beside it, under the time view's own statements.
            var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.IsNull(bundle.Refusal, bundle.Refusal?.Code);
            var quoted = bundle.Result!.Value.GetProperty("wordings").EnumerateArray().Single();
            Assert.AreEqual(WorkB, quoted.GetProperty("publisher_work_id").GetString());
            Assert.AreEqual(2, quoted.GetProperty("same_date_works").GetArrayLength());
            Assert.AreEqual(V3CorpusMount.EuropeTimeEvidenceBundleScope, bundle.Result.Value.GetProperty("scope").GetString());
            Assert.AreEqual(V3CorpusMount.EuropeStateDigestRule, bundle.Result.Value.GetProperty("digest_rule").GetString());
            Assert.IsTrue(bundle.Result.Value.GetProperty("not_held").EnumerateArray()
                .Any(static row => row.GetProperty("item").GetString() == "markup_and_notes"));

            var timeline = await EnvelopeAsync(mount, "/api/v3/timeline", "timeline", new { identifier = GdprSeed, language = "eng" });
            var dated = timeline.Result!.Value.GetProperty("languages").EnumerateArray().Single().GetProperty("wordings").EnumerateArray().Last();
            Assert.AreEqual(ConsolidationDate, dated.GetProperty("wording_date").GetString());
            Assert.AreEqual("identical_held_text", dated.GetProperty("basis").GetString());
            Assert.IsTrue(dated.GetProperty("text_held").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DifferentHeldTextsOnOneDateAreRefusedAmbiguousAndNoneIsChosenNotEvenByVerify()
    {
        var (root, directory) = await ConsolidatedWorksMountAsync(
            new(WorkB, ConsolidationDate, Designated, EnglishPackage, FrenchPackage),
            new(WorkC, ConsolidationDate, null, OtherEnglishPackage, EuFirstMountAcquisitionTests.FrenchOf(OtherEnglishPackage)));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var refused = await EuAsOfAsync(mount, "2025-03-01");
            Assert.AreEqual("ambiguous_version", refused.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, refused.Context.Publisher);
            var payload = refused.Refusal!.HelpfulPayload;
            CollectionAssert.AreEquivalent(new[] { "requested_date", "candidates" },
                payload.EnumerateObject().Select(static field => field.Name).ToArray(), "the registry's fields and no other");
            var candidates = payload.GetProperty("candidates").EnumerateArray().Select(static value => value.GetString()!).ToArray();
            Assert.HasCount(2, candidates);
            Assert.IsTrue(candidates.All(static candidate => candidate.StartsWith($"/eu-eurlex/{GdprSeed}/eng/{ConsolidationDate}--", StringComparison.Ordinal)));

            // The original wording, before the consolidation, is not touched by the ambiguity.
            Assert.IsNull((await EuAsOfAsync(mount, "2020-06-01")).Refusal);
            var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.AreEqual("ambiguous_version", bundle.Refusal?.Code);

            var timeline = await EnvelopeAsync(mount, "/api/v3/timeline", "timeline", new { identifier = GdprSeed, language = "eng" });
            var dated = timeline.Result!.Value.GetProperty("languages").EnumerateArray().Single().GetProperty("wordings").EnumerateArray().Last();
            Assert.AreEqual("different_texts", dated.GetProperty("basis").GetString());
            Assert.IsTrue(dated.GetProperty("text_held").GetBoolean(), "both texts are held; the date is ambiguous, not unheld");

            // Each held wording's own pin verifies; a digest neither holds names no current pin, since none is chosen.
            foreach (var candidate in candidates)
            {
                var verified = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = candidate });
                Assert.AreEqual("digest_matches", verified.Result?.Value.GetProperty("verdict").GetString(), verified.Refusal?.Code);
            }

            var tampered = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = candidates[0][..^64] + new string('0', 64) });
            Assert.AreEqual("ambiguous_version", tampered.Refusal?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheLatestWordingWithNoTextInALanguageIsTextNotAvailableThereWhileTheOtherLanguageAnswers()
    {
        var (root, directory) = await ConsolidatedWorksMountAsync(new(WorkB, ConsolidationDate, Designated, null, FrenchPackage));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var english = await EuAsOfAsync(mount, "2025-03-01");
            Assert.AreEqual("text_not_available", english.Refusal?.Code);
            var payload = english.Refusal!.HelpfulPayload;
            CollectionAssert.AreEquivalent(
                new[] { "official_identity", "official_source", "retained_transport_evidence", "language", "what_would_answer", "asserts_absence_of_law" },
                payload.EnumerateObject().Select(static field => field.Name).ToArray(),
                "the fields Luxembourg's refusal carries, which the reading screen's card admits");
            Assert.AreEqual(WorkB, payload.GetProperty("official_identity").GetString());
            Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean());

            Assert.IsNull((await EuAsOfAsync(mount, "2025-03-01", "fra")).Refusal);
            var either = await EuAsOfAsync(mount, "2025-03-01", language: null);
            Assert.IsNull(either.Refusal, either.Refusal?.Code);
            CollectionAssert.AreEqual(new[] { "fra" }, either.Result!.Value.GetProperty("states").EnumerateArray()
                .Select(static state => state.GetProperty("language").GetString()).ToArray());
            CollectionAssert.AreEqual(new[] { "eng" }, either.Result.Value.GetProperty("languages_not_answering").EnumerateArray()
                .Select(static value => value.GetString()).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AVersionWithNoUsableDateIsDisclosedWhenItsHeldTextIsTheSameAndMakesTheDatesAmbiguousWhenItDiffers()
    {
        var (sameRoot, sameDirectory) = await ConsolidatedWorksMountAsync(
            new(WorkB, ConsolidationDate, Designated, EnglishPackage, FrenchPackage),
            new(WorkC, null, null, EnglishPackage, FrenchPackage));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(sameDirectory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var answer = await EuAsOfAsync(mount, "2025-03-01");
            Assert.IsNull(answer.Refusal, answer.Refusal?.Code);
            var unplaced = answer.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("unplaced_versions").EnumerateArray().Single();
            Assert.AreEqual(WorkC, unplaced.GetProperty("publisher_work_id").GetString());
            Assert.AreEqual("publisher_date_absent", unplaced.GetProperty("date_status").GetString());
            Assert.IsTrue(unplaced.GetProperty("same_text").GetBoolean());
        }
        finally
        {
            Directory.Delete(sameRoot, recursive: true);
        }

        var (otherRoot, otherDirectory) = await ConsolidatedWorksMountAsync(
            new(WorkB, ConsolidationDate, Designated, EnglishPackage, FrenchPackage),
            new(WorkC, null, null, OtherEnglishPackage, null));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(otherDirectory, CancellationToken.None);
            Assert.IsNotNull(mount);
            foreach (var date in new[] { "2025-03-01", "2020-06-01" })
            {
                var refused = await EuAsOfAsync(mount, date);
                Assert.AreEqual("ambiguous_version", refused.Refusal?.Code, $"{date}: an undated version holding a different text could be the wording of any date");
                CollectionAssert.Contains(refused.Refusal!.HelpfulPayload.GetProperty("candidates").EnumerateArray()
                    .Select(static value => value.GetString()).ToArray(), WorkC + ".0001", "an undated version is named by its expression, having no date to pin");
            }

            // French holds no text of the undated version, so French is not ambiguous.
            Assert.IsNull((await EuAsOfAsync(mount, "2025-03-01", "fra")).Refusal);
        }
        finally
        {
            Directory.Delete(otherRoot, recursive: true);
        }
    }
}
