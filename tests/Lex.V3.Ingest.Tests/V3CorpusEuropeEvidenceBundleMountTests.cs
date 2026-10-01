using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// EU <c>evidence_bundle</c> over the original wording the EU index holds (the owner's proxy, 2026-10-01 14:30 UTC: serve
/// EU text now). On the retained GDPR fixture, the bundle quotes every held article of the wording on its wording date. It
/// carries the acknowledgement and authenticity statement Decision 95 requires. Each article's text is the index's own,
/// its digest is the text's, and its permalink verifies. Any other date is refused, and the original wording is never
/// served as a later one.
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeEvidenceBundleMountTests
{
    private const string Route = "/api/v3/evidence_bundle";

    /// <summary>The pinned wording of the GDPR's English expression, as EU dossier names it.</summary>
    private static async Task<(string WordingDate, string Permalink, string ExpressionId)> PinnedWordingAsync(V3CorpusMount mount, string language)
    {
        var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = "32016R0679", language });
        Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
        var expression = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().Single();
        var pinned = expression.GetProperty("pinned_wording");
        return (pinned.GetProperty("wording_date").GetString()!, pinned.GetProperty("permalink").GetString()!,
            expression.GetProperty("publisher_expression_id").GetString()!);
    }

    [TestMethod]
    public async Task TheOriginalWordingIsQuotedOnItsWordingDateWithTheAcknowledgementAndEveryArticlePermalinkVerifies()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var (wordingDate, permalink, expressionId) = await PinnedWordingAsync(mount, "eng");

        var envelope = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date = wordingDate, language = "eng" });
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher);
        Assert.AreEqual("evidence_bundle", envelope.Result!.ObjectType);
        var bundle = envelope.Result.Value;
        Assert.AreEqual("© European Union, https://eur-lex.europa.eu", bundle.GetProperty("acknowledgement").GetString());
        StringAssert.Contains(bundle.GetProperty("authenticity").GetString(), "Regulation (EU) No 216/2013, Article 1(2)");
        Assert.IsFalse(bundle.GetProperty("consolidations_held").GetBoolean());

        var wording = bundle.GetProperty("wordings").EnumerateArray().Single();
        Assert.AreEqual(permalink, wording.GetProperty("permalink").GetString(), "the wording EU dossier pins");
        Assert.AreEqual(wordingDate, wording.GetProperty("wording_date").GetString());
        Assert.IsTrue(wording.GetProperty("sources").EnumerateArray().All(static source => source.GetProperty("outcome").GetString() == "acquired"));

        // Every article with text the index holds of the expression is quoted, in its order, with the index's own text.
        var held = ArticleTexts(fixture.Directory, expressionId);
        var articles = wording.GetProperty("articles").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(
            held.Where(static row => row.Text.Length > 0).Select(static row => row.Identity).ToArray(),
            articles.Select(static article => article.GetProperty("article_identity_sha256").GetString()).ToArray());
        foreach (var article in articles)
        {
            var identity = article.GetProperty("article_identity_sha256").GetString()!;
            var text = article.GetProperty("text").GetString()!;
            Assert.AreEqual(held.Single(row => row.Identity == identity).Text, text, $"{identity}: the index's own text");
            var bytes = Encoding.UTF8.GetBytes(text);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), article.GetProperty("text_sha256").GetString(), $"{identity}: the digest of the text served");
            Assert.AreEqual(bytes.Length, article.GetProperty("text_byte_length").GetInt32());
            StringAssert.Matches(article.GetProperty("body_sha256").GetString(), new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$"), identity);
            Assert.IsFalse(string.IsNullOrEmpty(article.GetProperty("official_source").GetString()), identity);

            // The launch contract's first promise: the permalink a quote carries verifies.
            var articlePermalink = article.GetProperty("article_permalink").GetString()!;
            StringAssert.StartsWith(articlePermalink, permalink + "#");
            var verified = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = articlePermalink, language = "eng" });
            Assert.AreEqual("digest_matches", verified.Result?.Value.GetProperty("verdict").GetString(), $"{articlePermalink}: {verified.Refusal?.Code}");
        }
    }

    [TestMethod]
    public async Task ADateOtherThanTheWordingDateIsRefusedAndTheOriginalWordingIsNeverServedAsALaterOne()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var (wordingDate, _, _) = await PinnedWordingAsync(mount, "eng");
        var day = DateOnly.ParseExact(wordingDate, "yyyy-MM-dd");

        var before = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date = day.AddDays(-1).ToString("yyyy-MM-dd"), language = "eng" });
        Assert.AreEqual("no_version_for_date", before.Refusal?.Code);
        Assert.AreEqual(wordingDate, before.Refusal!.HelpfulPayload.GetProperty("history_begins").GetString());
        Assert.AreEqual(JsonValueKind.Null, before.Refusal.HelpfulPayload.GetProperty("nearest_earlier").ValueKind);
        Assert.AreEqual(wordingDate, before.Refusal.HelpfulPayload.GetProperty("nearest_later").GetString());

        // A later date is refused too: with no consolidation held, the original wording is not the wording of that date.
        var after = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date = day.AddYears(5).ToString("yyyy-MM-dd"), language = "eng" });
        Assert.AreEqual("no_version_for_date", after.Refusal?.Code);
        Assert.AreEqual(wordingDate, after.Refusal!.HelpfulPayload.GetProperty("nearest_earlier").GetString());
        Assert.AreEqual(JsonValueKind.Null, after.Refusal.HelpfulPayload.GetProperty("nearest_later").ValueKind);
        Assert.IsFalse(after.Refusal.HelpfulPayload.GetProperty("asserts_absence_of_law").GetBoolean());

        var german = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date = wordingDate, language = "deu" });
        Assert.AreEqual("language_not_available", german.Refusal?.Code);
        CollectionAssert.AreEqual(new[] { "eng" }, german.Refusal!.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static value => value.GetString()).ToArray());

        var unknown = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32099R9999", date = wordingDate });
        Assert.AreEqual("identifier_unknown", unknown.Refusal?.Code, "an EU-shaped identifier the EU index does not hold");
        Assert.AreEqual(PublisherId.EuEurLex, unknown.Context.Publisher);
    }

    [TestMethod]
    public async Task WithNoLanguageEveryHeldExpressionOfTheWordingDateIsQuotedEachUnderItsOwnPermalink()
    {
        var fixture = await EuropeMountedFixture.CreateAsync(acquireFrenchExpression: true);
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var english = await PinnedWordingAsync(mount, "eng");
        var french = await PinnedWordingAsync(mount, "fra");
        Assert.AreEqual(english.WordingDate, french.WordingDate, "the fixture's two expressions share the act date");

        var bundle = (await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date = english.WordingDate })).Result!.Value;
        CollectionAssert.AreEqual(new[] { "eng", "fra" }, bundle.GetProperty("served_languages").EnumerateArray().Select(static value => value.GetString()).ToArray());
        var wordings = bundle.GetProperty("wordings").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { english.Permalink, french.Permalink }, wordings.Select(static wording => wording.GetProperty("permalink").GetString()).ToArray());
        foreach (var wording in wordings)
        {
            var language = wording.GetProperty("language").GetString();
            Assert.IsTrue(wording.GetProperty("articles").EnumerateArray().All(article => article.GetProperty("language").GetString() == language), $"{language}: its own articles");
            Assert.IsTrue(wording.GetProperty("articles").GetArrayLength() > 0, $"{language}: text is quoted");
        }
    }

    /// <summary>Every article of an expression as the EU index stores it, in the publisher's article id and identity order.</summary>
    private static (string Identity, string Text)[] ArticleTexts(string directory, string expressionId)
    {
        using var connection = EuropeIndexBuilder.Open(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256,searchable_text FROM articles WHERE publisher_expression_id=$expression " +
            "ORDER BY publisher_identifier,article_identity_sha256";
        command.Parameters.AddWithValue("$expression", expressionId);
        using var reader = command.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
        return [.. rows];
    }
}
