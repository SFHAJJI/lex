using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusEuropeTimeViewBranchTests;
using static Lex.V3.Ingest.Tests.V3CorpusEuropeTimeViewTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// EU <c>search</c> over an act's time view (the audit of 2026-10-27, gap 5): on a mount whose EU census holds the GDPR's
/// original wording and one consolidated version dated 2024-01-01 whose text says "natural humans" where the original says
/// "natural persons", an undated search reads both wordings, serves an article's text once under the latest wording that holds
/// it, and dates each hit by its wording's role; a dated search reads the one wording the date selects, with as_of's refusals;
/// matching folds capitals and accents; and every extract is the bytes evidence_bundle quotes at that article.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class V3CorpusEuropeTimeSearchTests
{
    private const string RawTarget = "/api/v3/search";

    private static Task<(string Root, string Mount)> ChangedConsolidationAsync(bool frenchHeld = true) =>
        ConsolidatedWorksMountAsync(new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, OtherEnglishPackage,
            frenchHeld ? EuFirstMountAcquisitionTests.FrenchOf(OtherEnglishPackage) : null));

    private static Task<V3Envelope> SearchAsync(V3CorpusMount mount, string query, string? date = null, string language = "eng") =>
        date is null
            ? EnvelopeAsync(mount, RawTarget, "search", new { query, language, identifier = GdprSeed, mode = "strict" })
            : EnvelopeAsync(mount, RawTarget, "search", new { query, language, identifier = GdprSeed, mode = "strict", date });

    private static JsonElement[] Hits(V3Envelope envelope)
    {
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        return envelope.Result!.Value.GetProperty("hits").EnumerateArray().ToArray();
    }

    [TestMethod]
    public async Task AnUndatedSearchReadsEveryWordingAndServesAnUnchangedArticleOnceUnderTheLatest()
    {
        var (root, directory) = await ChangedConsolidationAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, originalPermalink) = await OriginalPinAsync(mount, "eng");

            // The new wording is found in the consolidated version alone, dated by its consolidation date and holding on.
            var humans = await SearchAsync(mount, "natural humans");
            var value = humans.Result!.Value;
            Assert.AreEqual(PublisherId.EuEurLex, humans.Context.Publisher);
            Assert.AreEqual(V3CorpusMount.EuropeSearchScope, value.GetProperty("scope").GetString());
            Assert.AreEqual(GdprSeed, value.GetProperty("seed_celex").GetString());
            Assert.IsTrue(value.GetProperty("consolidations_held").GetBoolean());
            Assert.AreEqual(V3CorpusMount.EuropeSelectionRule, value.GetProperty("selection_rule").GetString());
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("pinned_wording").ValueKind, "two wordings are searched, so each hit pins its own");
            var wordings = value.GetProperty("wordings").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(new[] { originalDate, ConsolidationDate }, wordings.Select(static wording => wording.GetProperty("wording_date").GetString()).ToArray());
            CollectionAssert.AreEqual(new[] { "original_wording", "consolidated_version" }, wordings.Select(static wording => wording.GetProperty("kind").GetString()).ToArray());
            Assert.AreEqual(ConsolidationDate, wordings[0].GetProperty("next_date").GetString(), "the original holds until the consolidation");
            Assert.AreEqual(originalPermalink, wordings[0].GetProperty("permalink").GetString(), "the original's pin is the dossier's");
            Assert.AreEqual(2, value.GetProperty("searched").GetProperty("wordings").GetInt32());
            var humanHits = Hits(humans);
            Assert.IsNotEmpty(humanHits);
            foreach (var hit in humanHits)
            {
                Assert.AreEqual("consolidated_version", hit.GetProperty("kind").GetString());
                Assert.AreEqual(ConsolidationDate, hit.GetProperty("wording_date").GetString());
                Assert.AreEqual(Designated, hit.GetProperty("celex").GetString());
                Assert.AreEqual(JsonValueKind.Null, hit.GetProperty("next_date").ValueKind);
                StringAssert.StartsWith(hit.GetProperty("permalink").GetString(), wordings[1].GetProperty("permalink").GetString() + "/");
                Assert.AreEqual(0, hit.GetProperty("same_wording").GetArrayLength(), "the original never held this text");
            }

            // The old wording is found in the original alone.
            var persons = Hits(await SearchAsync(mount, "natural persons"));
            Assert.IsNotEmpty(persons);
            Assert.IsTrue(persons.All(static hit => hit.GetProperty("kind").GetString() == "original_wording" &&
                hit.GetProperty("next_date").GetString() == ConsolidationDate));

            // A phrase in articles the consolidation did not change: one hit each, under the consolidated version, the original
            // named as holding the same text; the fold says how many hits it folded.
            var data = await SearchAsync(mount, "personal data");
            var dataHits = Hits(data);
            var unchanged = dataHits.Where(static hit => hit.GetProperty("same_wording").GetArrayLength() == 1).ToArray();
            Assert.IsNotEmpty(unchanged);
            foreach (var hit in unchanged)
            {
                Assert.AreEqual("consolidated_version", hit.GetProperty("kind").GetString());
                var other = hit.GetProperty("same_wording")[0];
                Assert.AreEqual(originalDate, other.GetProperty("wording_date").GetString());
                Assert.AreEqual("original_wording", other.GetProperty("kind").GetString());
                StringAssert.StartsWith(other.GetProperty("permalink").GetString(), originalPermalink + "/");
            }

            var fold = data.Result!.Value.GetProperty("fold");
            Assert.AreEqual(V3CorpusMount.EuropeSearchFoldRule, fold.GetProperty("rule").GetString());
            Assert.AreEqual(dataHits.Length + unchanged.Length, fold.GetProperty("strict_hits_before_fold").GetInt32());
            Assert.AreEqual(dataHits.Length, data.Result.Value.GetProperty("population").GetProperty("strict_hits").GetInt32());

            // Capitals and accents never decide: the phrase in capitals finds the same hits.
            CollectionAssert.AreEqual(
                humanHits.Select(static hit => hit.GetProperty("article_identity_sha256").GetString()).ToArray(),
                Hits(await SearchAsync(mount, "NATURAL HUMANS")).Select(static hit => hit.GetProperty("article_identity_sha256").GetString()).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AnArticleIsShownOncePerTextNotOncePerArticle()
    {
        // The French review of this search (2026-10-27, its point 2): a 2024 consolidation changes the articles that say
        // "natural persons", and a 2025 one restores the original's text. An article holding both phrases then has two texts:
        // the original's, which 2025 holds again, shown once under 2025 with the original named; and 2024's, shown under 2024.
        var (root, directory) = await ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, OtherEnglishPackage, null),
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkC, "2025-01-01", null, EnglishPackage, null));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, _) = await OriginalPinAsync(mount, "eng");
            var hits = Hits(await SearchAsync(mount, "personal data"));
            var byArticle = hits.GroupBy(static hit => hit.GetProperty("publisher_id").GetString()!, StringComparer.Ordinal).ToArray();
            var changed = byArticle.Where(static group => group.Count() == 2).ToArray();
            Assert.IsNotEmpty(changed, "an article whose text 2024 changed is shown once per text");
            foreach (var group in changed)
            {
                CollectionAssert.AreEqual(new[] { ConsolidationDate, "2025-01-01" },
                    group.Select(static hit => hit.GetProperty("wording_date").GetString()).ToArray(), group.Key);
                var restored = group.Single(static hit => hit.GetProperty("wording_date").GetString() == "2025-01-01");
                CollectionAssert.AreEqual(new[] { originalDate },
                    restored.GetProperty("same_wording").EnumerateArray().Select(static other => other.GetProperty("wording_date").GetString()).ToArray());
                Assert.AreEqual(0, group.Single(static hit => hit.GetProperty("wording_date").GetString() == ConsolidationDate).GetProperty("same_wording").GetArrayLength());
            }

            Assert.IsTrue(byArticle.Where(static group => group.Count() == 1).All(static group =>
                group.Single().GetProperty("wording_date").GetString() == "2025-01-01" && group.Single().GetProperty("same_wording").GetArrayLength() == 2),
                "an article no wording changed is shown once, under the latest, the two others named");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ADatedSearchReadsTheWordingTheDateSelectsWithAsOfsRefusals()
    {
        var (root, directory) = await ChangedConsolidationAsync(frenchHeld: false);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, originalPermalink) = await OriginalPinAsync(mount, "eng");

            // Before the consolidation the original answers; from it, the consolidated version.
            var early = await SearchAsync(mount, "natural persons", date: "2020-06-01");
            Assert.IsNotEmpty(Hits(early));
            var pinned = early.Result!.Value.GetProperty("pinned_wording");
            Assert.AreEqual(originalDate, pinned.GetProperty("wording_date").GetString());
            Assert.AreEqual(ConsolidationDate, pinned.GetProperty("next_date").GetString());
            Assert.AreEqual(originalPermalink, pinned.GetProperty("permalink").GetString());
            Assert.AreEqual(JsonValueKind.Null, early.Result.Value.GetProperty("fold").ValueKind);
            Assert.IsEmpty(Hits(await SearchAsync(mount, "natural humans", date: "2020-06-01")), "the consolidation's words are not in the wording of 2020");
            Assert.IsNotEmpty(Hits(await SearchAsync(mount, "natural humans", date: "2025-03-01")));
            Assert.IsEmpty(Hits(await SearchAsync(mount, "natural persons", date: "2025-03-01")));

            // A date before the history; a date whose wording is held in no text in the language asked.
            var before = await SearchAsync(mount, "natural persons", date: "2010-01-01");
            Assert.AreEqual("no_version_for_date", before.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, before.Context.Publisher);
            var french = await SearchAsync(mount, "natural persons", date: "2025-03-01", language: "fra");
            Assert.AreEqual("text_not_available", french.Refusal?.Code, "the French text of the 2024 consolidation is not held");

            // Undated in French, the version whose French is not held is not searched, and the answer counts it.
            var undatedFrench = await SearchAsync(mount, "natural persons", language: "fra");
            Assert.IsNull(undatedFrench.Refusal, undatedFrench.Refusal?.Code);
            var searched = undatedFrench.Result!.Value.GetProperty("searched");
            Assert.AreEqual(1, searched.GetProperty("wordings").GetInt32());
            Assert.AreEqual(1, searched.GetProperty("not_searched").GetProperty("wordings_without_text").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EveryExtractIsTheBytesEvidenceBundleQuotesAtThatArticle()
    {
        var (root, directory) = await ChangedConsolidationAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            foreach (var (query, date) in new[] { ("Natural Humans", "2025-03-01"), ("natural persons", "2020-06-01") })
            {
                var answer = await SearchAsync(mount, query, date);
                var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date, language = "eng" });
                Assert.IsNull(bundle.Refusal, bundle.Refusal?.Code);
                var quoted = bundle.Result!.Value.GetProperty("wordings")[0].GetProperty("articles").EnumerateArray()
                    .ToDictionary(static article => article.GetProperty("article_identity_sha256").GetString()!, StringComparer.Ordinal);
                Assert.AreEqual(V3CorpusMount.EuropeTextAcknowledgement, answer.Result!.Value.GetProperty("acknowledgement").GetString());
                Assert.AreEqual(bundle.Result.Value.GetProperty("authenticity").GetString(), answer.Result.Value.GetProperty("authenticity").GetString());
                var hits = Hits(answer);
                Assert.IsNotEmpty(hits);
                foreach (var hit in hits)
                {
                    // The bundle cuts its articles into pages; an article of a later page is not in the first.
                    if (!quoted.TryGetValue(hit.GetProperty("article_identity_sha256").GetString()!, out var article))
                    {
                        continue;
                    }

                    var extract = hit.GetProperty("extract");
                    Assert.AreEqual(article.GetProperty("text_sha256").GetString(), extract.GetProperty("text_sha256").GetString());
                    var bytes = Encoding.UTF8.GetBytes(article.GetProperty("text").GetString()!);
                    var cut = Encoding.UTF8.GetString(bytes, extract.GetProperty("byte_start").GetInt32(),
                        extract.GetProperty("byte_end").GetInt32() - extract.GetProperty("byte_start").GetInt32());
                    Assert.AreEqual(cut, string.Concat(extract.GetProperty("parts").EnumerateArray().Select(static part => part.GetProperty("text").GetString())));
                    Assert.IsTrue(extract.GetProperty("parts").EnumerateArray().Any(part => part.GetProperty("matched").GetBoolean() &&
                        string.Equals(part.GetProperty("text").GetString(), query, StringComparison.OrdinalIgnoreCase)), query);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
