using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU time view (V3CorpusMount.EuropeTime.cs) over a derived mount whose EU census holds the GDPR's original wording and one
/// consolidated version dated 2024-01-01 (the data lane's consolidated fixture), each in English and French: the timeline lists
/// both wordings, as_of answers the wording of the date, evidence_bundle quotes it, verify confirms its permalinks, and the original
/// wording keeps the permalink EU dossier pins.
/// </summary>
public sealed partial class V3FirstMountBuildTests
{
    private const string GdprSeed = "32016R0679";
    internal const string ConsolidationDate = "2024-01-01";

    private static Task<(string Root, string Mount)> ConsolidatedMountAsync(bool missingStateCelex = false) =>
        MountOfAsync(store => EuFirstMountAcquisitionTests.AcquireConsolidatedAsync(store, missingStateCelex));

    /// <summary>A mount derived from the generalised consolidated fixture: the directory to delete, and the mount's.</summary>
    internal static Task<(string Root, string Mount)> ConsolidatedWorksMountAsync(params EuFirstMountAcquisitionTests.ConsolidatedWorkSpec[] works) =>
        MountOfAsync(store => EuFirstMountAcquisitionTests.AcquireConsolidatedWorksAsync(store, works));

    private static async Task<(string Root, string Mount)> MountOfAsync(
        Func<FileSystemCustodyStore, Task<Lex.V3.Ingest.Europe.EuFirstMountAcquisitionResult>> acquireEurope)
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-eu-time-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(Path.Combine(root, "custody"));
        var (_, luxembourg) = await AcquireAsync(store, CheckoutRoot());
        var europe = await acquireEurope(store);
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(luxembourg.Delivered, luxembourg.Detail);
        var time = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(time)).RunAsync(europe, luxembourg, CancellationToken.None);
        Assert.IsTrue(build.Delivered, build.Detail);
        var mount = Path.Combine(root, "mount");
        await V3CorpusMountWriter.WriteAsync(build, mount, null, CancellationToken.None, time);
        return (root, mount);
    }

    /// <summary>The original wording EU dossier pins in a language: its Formex act date and its permalink.</summary>
    private static async Task<(string Date, string Permalink)> OriginalPinAsync(V3CorpusMount mount, string language)
    {
        var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = GdprSeed, language });
        Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
        var pinned = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().Single().GetProperty("pinned_wording");
        return (pinned.GetProperty("wording_date").GetString()!, pinned.GetProperty("permalink").GetString()!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TheEuTimelineListsTheOriginalWordingAndTheConsolidatedVersionInEachLanguage(bool missingStateCelex)
    {
        var (root, directory) = await ConsolidatedMountAsync(missingStateCelex);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, _) = await OriginalPinAsync(mount, "eng");
            Assert.IsTrue(string.CompareOrdinal(originalDate, ConsolidationDate) < 0, "the fixture's original wording precedes its consolidation");

            // The act's CELEX and the consolidated version's work name the same act.
            foreach (var identifier in new[] { GdprSeed, EuFirstMountAcquisitionTests.ConsolidatedWork })
            {
                var envelope = await EnvelopeAsync(mount, "/api/v3/timeline", "timeline", new { identifier });
                Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher);
                Assert.AreEqual("timeline", envelope.Result!.ObjectType);
                var value = envelope.Result.Value;
                Assert.AreEqual(GdprSeed, value.GetProperty("seed_celex").GetString());
                var languages = value.GetProperty("languages").EnumerateArray().ToArray();
                CollectionAssert.AreEqual(new[] { "eng", "fra" }, languages.Select(static language => language.GetProperty("language").GetString()).ToArray());
                foreach (var language in languages)
                {
                    var code = language.GetProperty("language").GetString();
                    var wordings = language.GetProperty("wordings").EnumerateArray().ToArray();
                    CollectionAssert.AreEqual(new[] { originalDate, ConsolidationDate }, wordings.Select(static wording => wording.GetProperty("wording_date").GetString()).ToArray(), code);
                    CollectionAssert.AreEqual(new[] { "original_wording", "consolidated_version" }, wordings.Select(static wording => wording.GetProperty("kind").GetString()).ToArray(), code);
                    Assert.IsTrue(wordings.All(static wording => wording.GetProperty("text_held").GetBoolean() && wording.GetProperty("basis").GetString() == "single_work"), code);
                    Assert.AreEqual(ConsolidationDate, wordings[0].GetProperty("next_date").GetString(), "the original wording holds until the consolidation");
                    Assert.AreEqual(JsonValueKind.Null, wordings[1].GetProperty("next_date").ValueKind, "no wording is held after the latest consolidation");
                    Assert.AreEqual(missingStateCelex ? null : "02016R0679-20240101", wordings[1].GetProperty("celex").GetString());
                    foreach (var wording in wordings)
                    {
                        StringAssert.StartsWith(wording.GetProperty("permalink").GetString(), $"/eu-eurlex/{GdprSeed}/{code}/{wording.GetProperty("wording_date").GetString()}--");
                    }

                    Assert.AreEqual(0, language.GetProperty("unplaced_versions").GetArrayLength());
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuAsOfAnswersTheWordingOfTheDateAndNeverADateBeforeTheHistory()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, originalPermalink) = await OriginalPinAsync(mount, "eng");

            // Between the two wordings: the original's, which holds until the consolidation, under the permalink EU dossier pins.
            var between = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2020-06-01", language = "eng" });
            Assert.IsNull(between.Refusal, between.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, between.Context.Publisher);
            Assert.AreEqual("version_state", between.Result!.ObjectType);
            var original = between.Result.Value.GetProperty("states").EnumerateArray().Single().GetProperty("wording");
            Assert.AreEqual(originalDate, original.GetProperty("wording_date").GetString());
            Assert.AreEqual("original_wording", original.GetProperty("kind").GetString());
            Assert.AreEqual(ConsolidationDate, original.GetProperty("next_date").GetString());
            Assert.AreEqual(originalPermalink, original.GetProperty("permalink").GetString(), "the original wording keeps its pin");

            // After the consolidation: the consolidated version, with no next date.
            var later = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.IsNull(later.Refusal, later.Refusal?.Code);
            var consolidated = later.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("wording");
            Assert.AreEqual(ConsolidationDate, consolidated.GetProperty("wording_date").GetString());
            Assert.AreEqual("consolidated_version", consolidated.GetProperty("kind").GetString());
            Assert.AreEqual("02016R0679-20240101", consolidated.GetProperty("celex").GetString());
            Assert.AreEqual(EuFirstMountAcquisitionTests.ConsolidatedWork, consolidated.GetProperty("publisher_work_id").GetString());
            Assert.AreEqual(JsonValueKind.Null, consolidated.GetProperty("next_date").ValueKind);
            Assert.IsTrue(later.Result.Value.GetProperty("not_held").EnumerateArray()
                .Any(static row => row.GetProperty("item").GetString() == "unconsolidated_amendments"), "a date after the latest consolidation says what it cannot see");

            // With no language, each held language answers.
            var both = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2025-03-01" });
            CollectionAssert.AreEqual(new[] { "eng", "fra" }, both.Result!.Value.GetProperty("states").EnumerateArray()
                .Select(static state => state.GetProperty("language").GetString()).ToArray());

            // Before the history: no_version_for_date, naming where it begins, with EU context.
            var before = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2016-01-01", language = "eng" });
            Assert.AreEqual("no_version_for_date", before.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, before.Context.Publisher);
            Assert.AreEqual(originalDate, before.Refusal!.HelpfulPayload.GetProperty("history_begins").GetString());
            Assert.AreEqual(JsonValueKind.Null, before.Refusal.HelpfulPayload.GetProperty("nearest_earlier").ValueKind);
            Assert.AreEqual(originalDate, before.Refusal.HelpfulPayload.GetProperty("nearest_later").GetString());

            var german = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2025-03-01", language = "deu" });
            Assert.AreEqual("language_not_available", german.Refusal?.Code);
            CollectionAssert.AreEqual(new[] { "eng", "fra" }, german.Refusal!.HelpfulPayload.GetProperty("available_languages").EnumerateArray()
                .Select(static value => value.GetString()).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuEvidenceBundleQuotesTheConsolidatedWordingOfALaterDateAndEveryPermalinkVerifies()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.IsNull(bundle.Refusal, bundle.Refusal?.Code);
            var value = bundle.Result!.Value;
            Assert.AreEqual("© European Union, https://eur-lex.europa.eu", value.GetProperty("acknowledgement").GetString());
            StringAssert.Contains(value.GetProperty("authenticity").GetString(), "Regulation (EU) No 216/2013, Article 1(2)");
            Assert.IsTrue(value.GetProperty("consolidations_held").GetBoolean());
            var wording = value.GetProperty("wordings").EnumerateArray().Single();
            Assert.AreEqual("consolidated_version", wording.GetProperty("kind").GetString());
            Assert.AreEqual(ConsolidationDate, wording.GetProperty("wording_date").GetString());
            Assert.AreEqual(EuFirstMountAcquisitionTests.ConsolidatedWork, wording.GetProperty("publisher_work_id").GetString());
            Assert.IsTrue(wording.GetProperty("sources").EnumerateArray().All(static source => source.GetProperty("outcome").GetString() == "acquired"));
            var articles = wording.GetProperty("articles").EnumerateArray().ToArray();
            Assert.IsNotEmpty(articles);
            foreach (var article in articles.Take(5))
            {
                var verified = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = article.GetProperty("article_permalink").GetString(), language = "eng" });
                Assert.AreEqual("digest_matches", verified.Result?.Value.GetProperty("verdict").GetString(), verified.Refusal?.Code);
                Assert.AreEqual("consolidated_version", verified.Result!.Value.GetProperty("kind").GetString());
            }

            // The original wording's pin still verifies, now through the time view.
            var (_, originalPermalink) = await OriginalPinAsync(mount, "eng");
            var original = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = originalPermalink });
            Assert.AreEqual("digest_matches", original.Result?.Value.GetProperty("verdict").GetString(), original.Refusal?.Code);
            Assert.AreEqual("original_wording", original.Result!.Value.GetProperty("kind").GetString());

            // A digest the wording does not have names the current pin.
            var permalink = wording.GetProperty("permalink").GetString()!;
            var mismatch = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = permalink[..^64] + new string('0', 64) });
            Assert.AreEqual("pinned_digest_mismatch", mismatch.Refusal?.Code);
            Assert.AreEqual(permalink, mismatch.Refusal!.HelpfulPayload.GetProperty("current_hash_pinned_url").GetString());

            // Before the history, the bundle refuses as as_of does.
            var before = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date = "2016-01-01" });
            Assert.AreEqual("no_version_for_date", before.Refusal?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AConsolidatedVersionsIdentifierNamesItsActsDossierWhichListsBothWordings()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = "02016R0679-20240101" });
            Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
            var value = dossier.Result!.Value;
            Assert.IsTrue(value.GetProperty("consolidations_held").GetBoolean());
            var timeline = value.GetProperty("wording_timeline");
            Assert.AreEqual(GdprSeed, timeline.GetProperty("seed_celex").GetString());
            Assert.IsTrue(timeline.GetProperty("languages").EnumerateArray().All(static language =>
                language.GetProperty("wordings").EnumerateArray().Select(static wording => wording.GetProperty("wording_date").GetString()).Last() == ConsolidationDate));
            var laterWordings = value.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "later_wordings");
            StringAssert.StartsWith(laterWordings.GetProperty("reason").GetString(), "consolidated versions of this act are held");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
