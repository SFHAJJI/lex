using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// EU <c>diff</c> over the EU time view (V3CorpusMount.EuropeDiff.cs): each bound is the wording EU <c>as_of</c> answers on its
/// date, and two different wordings are compared article by article by the publisher's article identifier and the SHA-256 of
/// each article's held text, the digest the EU evidence bundle states for the text it quotes. On mounts derived from the
/// consolidated fixtures: the original wording against a consolidated version whose text differs, one wording on both dates,
/// a language whose later wording is not held, an ambiguous bound, and the refusals at the edges of the history.
/// </summary>
public sealed partial class V3FirstMountBuildTests
{
    private static Task<V3Envelope> EuDiffAsync(V3CorpusMount mount, string from, string to, string? language = "eng") =>
        language is null
            ? EnvelopeAsync(mount, "/api/v3/diff", "diff", new { identifier = GdprSeed, date_from = from, date_to = to })
            : EnvelopeAsync(mount, "/api/v3/diff", "diff", new { identifier = GdprSeed, date_from = from, date_to = to, language });

    /// <summary>
    /// The articles the EU evidence bundle holds at a date, in a language: publisher id to the digests of their quoted texts, in
    /// the wording's order, and null for an article it holds without text.
    /// </summary>
    private static async Task<Dictionary<string, string?[]>> QuotedTextDigestsAsync(V3CorpusMount mount, string date, string language)
    {
        var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date, language });
        Assert.IsNull(bundle.Refusal, bundle.Refusal?.Code);
        var wording = bundle.Result!.Value.GetProperty("wordings").EnumerateArray().Single();
        var quoted = wording.GetProperty("articles").EnumerateArray().Select(static article =>
        {
            var text = article.GetProperty("text").GetString()!;
            var digest = article.GetProperty("text_sha256").GetString()!;
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), digest);
            return (Id: article.GetProperty("publisher_id").GetString()!, Digest: (string?)digest);
        });
        var withoutText = wording.GetProperty("articles_without_text").EnumerateArray()
            .Select(static article => (Id: article.GetProperty("publisher_id").GetString()!, Digest: (string?)null));
        return quoted.Concat(withoutText)
            .GroupBy(static article => article.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static article => article.Digest).ToArray(), StringComparer.Ordinal);
    }

    [TestMethod]
    public async Task EuDiffComparesTheOriginalWordingWithALaterConsolidationArticleByArticleByTheBundlesTextDigests()
    {
        var (root, directory) = await ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, OtherEnglishPackage,
                EuFirstMountAcquisitionTests.FrenchOf(OtherEnglishPackage)));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, originalPermalink) = await OriginalPinAsync(mount, "eng");

            var answer = await EuDiffAsync(mount, "2020-06-01", "2025-03-01");
            Assert.IsNull(answer.Refusal, answer.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, answer.Context.Publisher);
            Assert.AreEqual("diff", answer.Result!.ObjectType);
            var value = answer.Result.Value;
            Assert.AreEqual("eu-eurlex", value.GetProperty("publisher").GetString());
            Assert.AreEqual(GdprSeed, value.GetProperty("seed_celex").GetString());
            Assert.AreEqual(V3CorpusMount.EuropeStateDigestRule, value.GetProperty("digest_rule").GetString());
            Assert.AreEqual(0, value.GetProperty("languages_not_compared").GetArrayLength());

            // Each side is the wording as_of answers on its date: the original wording under the permalink dossier pins, then the
            // consolidated version.
            var comparison = value.GetProperty("comparisons").EnumerateArray().Single();
            Assert.AreEqual("eng", comparison.GetProperty("language").GetString());
            Assert.IsFalse(comparison.GetProperty("same_wording").GetBoolean());
            Assert.AreEqual(V3CorpusMount.EuropeComparisonNote, comparison.GetProperty("note").GetString());
            var from = comparison.GetProperty("from");
            var to = comparison.GetProperty("to");
            Assert.AreEqual(originalDate, from.GetProperty("wording_date").GetString());
            Assert.AreEqual(originalPermalink, from.GetProperty("permalink").GetString());
            Assert.AreEqual(ConsolidationDate, from.GetProperty("next_date").GetString());
            Assert.AreEqual(ConsolidationDate, to.GetProperty("wording_date").GetString());
            Assert.AreEqual("consolidated_version", to.GetProperty("kind").GetString());
            foreach (var (date, side) in new[] { ("2020-06-01", from), ("2025-03-01", to) })
            {
                var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date, language = "eng" });
                Assert.AreEqual(asOf.Result!.Value.GetProperty("states")[0].GetProperty("wording").GetProperty("permalink").GetString(),
                    side.GetProperty("permalink").GetString(), $"the {date} side is the wording as_of answers then");
            }

            // Every article of each side in exactly one row, in the identifiers' ordinal order, each side's digests the texts the
            // bundle quotes on that side's date; the rewritten phrase changes some articles and leaves the others unchanged.
            var before = await QuotedTextDigestsAsync(mount, "2020-06-01", "eng");
            var after = await QuotedTextDigestsAsync(mount, "2025-03-01", "eng");
            var rows = comparison.GetProperty("articles").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(before.Keys.Union(after.Keys).Order(StringComparer.Ordinal).ToArray(),
                rows.Select(static row => row.GetProperty("publisher_id").GetString()!).ToArray());
            var tally = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var id = row.GetProperty("publisher_id").GetString()!;
                string?[] Digests(string side) => row.GetProperty(side).EnumerateArray()
                    .Select(static entry => entry.GetProperty("wording_sha256").GetString()).ToArray();
                CollectionAssert.AreEqual(before.GetValueOrDefault(id, []), Digests("from"), id);
                CollectionAssert.AreEqual(after.GetValueOrDefault(id, []), Digests("to"), id);
                var status = row.GetProperty("status").GetString()!;
                var expected = !before.ContainsKey(id) ? "added" : !after.ContainsKey(id) ? "removed"
                    : before[id].Concat(after[id]).Any(static digest => digest is null) ? "text_not_held"
                    : before[id].SequenceEqual(after[id], StringComparer.Ordinal) ? "unchanged" : "changed";
                Assert.AreEqual(expected, status, id);
                tally[status] = tally.GetValueOrDefault(status) + 1;
            }

            Assert.IsGreaterThan(0, tally.GetValueOrDefault("changed"), "the rewritten phrase changes at least one article");
            Assert.IsGreaterThan(0, tally.GetValueOrDefault("unchanged"), "and leaves others as they were");
            var counts = comparison.GetProperty("counts");
            foreach (var status in new[] { "unchanged", "changed", "added", "removed", "text_not_held" })
            {
                Assert.AreEqual(tally.GetValueOrDefault(status), counts.GetProperty(status).GetInt32(), status);
            }

            // With no language, each held language is compared, in their order.
            var both = await EuDiffAsync(mount, "2020-06-01", "2025-03-01", language: null);
            CollectionAssert.AreEqual(new[] { "eng", "fra" }, both.Result!.Value.GetProperty("comparisons").EnumerateArray()
                .Select(static row => row.GetProperty("language").GetString()).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuDiffOfTwoDatesInOneWordingSaysSoAndComparesNothing()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var answer = await EuDiffAsync(mount, "2018-05-25", "2020-06-01");
            Assert.IsNull(answer.Refusal, answer.Refusal?.Code);
            var comparison = answer.Result!.Value.GetProperty("comparisons").EnumerateArray().Single();
            Assert.IsTrue(comparison.GetProperty("same_wording").GetBoolean());
            Assert.AreEqual(V3CorpusMount.EuropeSameWordingNote, comparison.GetProperty("note").GetString());
            Assert.AreEqual(JsonValueKind.Null, comparison.GetProperty("articles").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, comparison.GetProperty("counts").ValueKind);
            Assert.AreEqual(comparison.GetProperty("from").GetProperty("permalink").GetString(),
                comparison.GetProperty("to").GetProperty("permalink").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuDiffRefusesAtTheEdgesOfTheHistoryAsAsOfDoes()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (originalDate, _) = await OriginalPinAsync(mount, "eng");

            // A from bound before every wording: no_version_for_date for that bound's date, with EU context.
            var before = await EuDiffAsync(mount, "2016-01-01", "2025-03-01");
            Assert.AreEqual("no_version_for_date", before.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, before.Context.Publisher);
            Assert.AreEqual("2016-01-01", before.Refusal!.HelpfulPayload.GetProperty("requested_date").GetString());
            Assert.AreEqual(originalDate, before.Refusal.HelpfulPayload.GetProperty("history_begins").GetString());

            var german = await EuDiffAsync(mount, "2020-06-01", "2025-03-01", "deu");
            Assert.AreEqual("language_not_available", german.Refusal?.Code);

            // An EU-shaped identifier the census does not hold is unknown with EU context, as as_of answers it.
            var unknown = await EnvelopeAsync(mount, "/api/v3/diff", "diff", new { identifier = "32099R9999", date_from = "2020-06-01", date_to = "2025-03-01" });
            Assert.AreEqual("identifier_unknown", unknown.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, unknown.Context.Publisher);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuDiffListsALanguageWhoseLaterWordingIsNotHeldAsNotComparedAndComparesTheOther()
    {
        var (root, directory) = await ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, null, FrenchPackage));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var answer = await EuDiffAsync(mount, "2020-06-01", "2025-03-01", language: null);
            Assert.IsNull(answer.Refusal, answer.Refusal?.Code);
            var value = answer.Result!.Value;
            CollectionAssert.AreEqual(new[] { "fra" }, value.GetProperty("comparisons").EnumerateArray()
                .Select(static row => row.GetProperty("language").GetString()).ToArray());
            var notCompared = value.GetProperty("languages_not_compared").EnumerateArray().Single();
            Assert.AreEqual("eng", notCompared.GetProperty("language").GetString());
            Assert.AreEqual("to", notCompared.GetProperty("bound").GetString());
            Assert.AreEqual("the wording of that date is not held in this language", notCompared.GetProperty("reason").GetString());

            // Asked in English alone, nothing is compared: the refusal as_of gives English on that date.
            var english = await EuDiffAsync(mount, "2020-06-01", "2025-03-01");
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.AreEqual("text_not_available", english.Refusal?.Code);
            Assert.AreEqual(asOf.Refusal?.Code, english.Refusal?.Code);
            Assert.AreEqual(asOf.Refusal!.HelpfulPayload.GetRawText(), english.Refusal!.HelpfulPayload.GetRawText());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuDiffRefusesABoundWithTwoDifferentTextsEvenWhenTheOtherBoundHasNoWording()
    {
        var (root, directory) = await ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkB, ConsolidationDate, Designated, EnglishPackage, FrenchPackage),
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(WorkC, ConsolidationDate, null, OtherEnglishPackage,
                EuFirstMountAcquisitionTests.FrenchOf(OtherEnglishPackage)));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var ambiguous = await EuDiffAsync(mount, "2020-06-01", "2025-03-01");
            Assert.AreEqual("ambiguous_version", ambiguous.Refusal?.Code);
            Assert.AreEqual("2025-03-01", ambiguous.Refusal!.HelpfulPayload.GetProperty("requested_date").GetString());
            Assert.HasCount(2, ambiguous.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().ToArray());

            // A from bound before every wording does not mask the ambiguity at the to bound: both are selected first.
            var masked = await EuDiffAsync(mount, "2016-01-01", "2025-03-01");
            Assert.AreEqual("ambiguous_version", masked.Refusal?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
