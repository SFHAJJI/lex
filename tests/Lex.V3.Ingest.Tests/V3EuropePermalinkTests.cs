using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU permalink grammar (STATUS item 5): an EU search hit carries a hash-pinned provision permalink,
/// <c>/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}#{provision}</c>, in Luxembourg's family with the
/// language part of it, and <c>verify</c> checks it against the one wording the EU index holds. The digest is computed
/// by the API from what the index holds (it stores no wording digest), as its stated rule says; this test recomputes it
/// from the index on its own and requires the same value.
/// </summary>
[TestClass]
public sealed class V3EuropePermalinkTests
{
    private static async Task<(EuropeMountedFixture Fixture, V3CorpusMount Mount, JsonElement Answer)> SearchedAsync()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var word = OneWord(fixture.Directory);
        var envelope = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query = word, language = "eng", identifier = "32016R0679" });
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        return (fixture, mount, envelope.Result!.Value);
    }

    /// <summary>A word of the GDPR's first article, to search for.</summary>
    private static string OneWord(string directory)
    {
        using var connection = EuropeIndexBuilder.Open(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT searchable_text FROM articles ORDER BY publisher_identifier LIMIT 1";
        var text = (string)command.ExecuteScalar()!;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).First(static token => token.Length >= 6 && token.All(char.IsLetter));
    }

    /// <summary>
    /// The wording digest recomputed by the rule the answer states (<c>EuropeWordingDigestRule</c>), independently of the
    /// API's code: the domain, the CELEX, the work and expression IRIs, the language, the wording date, then every article
    /// identity in the publisher's article order, each field a 4-byte big-endian length and its UTF-8 bytes.
    /// </summary>
    internal static string WordingSha256ByTheStatedRule(
        string celex, string workId, string expressionId, string language, string wordingDate, IEnumerable<string> articleIdentities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        foreach (var field in new[] { "lex-v3-eu-wording/1", celex, workId, expressionId, language, wordingDate })
        {
            Append(field);
        }

        foreach (var identity in articleIdentities)
        {
            Append(identity);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task<V3Envelope> VerifyAsync(V3CorpusMount mount, string identifier, string? language = null) =>
        await EnvelopeAsync(mount, "/api/v3/verify", "verify", language is null ? new { identifier } : new { identifier, language });

    [TestMethod]
    public async Task AnEuHitCarriesAPinnedPermalinkThatVerifies()
    {
        var (fixture, mount, answer) = await SearchedAsync();
        await using var cleanup = fixture;
        using var disposeMount = mount;

        var wording = answer.GetProperty("pinned_wording");
        var permalink = wording.GetProperty("permalink").GetString()!;
        StringAssert.StartsWith(permalink, "/eu-eurlex/32016R0679/eng/");
        StringAssert.Matches(permalink, new System.Text.RegularExpressions.Regex(@"^/eu-eurlex/32016R0679/eng/\d{4}-\d{2}-\d{2}--[0-9a-f]{64}$"));
        var hit = answer.GetProperty("hits")[0];
        var hitPermalink = hit.GetProperty("permalink").GetString()!;
        Assert.AreEqual($"{permalink}#{Uri.EscapeDataString(hit.GetProperty("publisher_id").GetString()!)}", hitPermalink, "each hit's permalink is the wording's with its provision");

        var verified = await VerifyAsync(mount, hitPermalink, "eng");
        Assert.IsNull(verified.Refusal, verified.Refusal?.Code);
        var value = verified.Result!.Value;
        Assert.AreEqual("digest_matches", value.GetProperty("verdict").GetString());
        Assert.AreEqual("eu-eurlex", value.GetProperty("publisher").GetString());
        Assert.AreEqual(hit.GetProperty("publisher_id").GetString(), value.GetProperty("requested_anchor").GetString());
        Assert.AreEqual(wording.GetProperty("wording_sha256").GetString(), value.GetProperty("wording_sha256").GetString());
        Assert.AreEqual(hitPermalink, value.GetProperty("article_permalink").GetString());
        Assert.AreEqual(hit.GetProperty("resolve").GetProperty("identifier").GetString(), value.GetProperty("provision_coordinate").GetString(),
            "the provision coordinate is the one EU resolve answers");

        var absolute = await VerifyAsync(mount, "https://law.soufien.lu" + hitPermalink);
        Assert.AreEqual("digest_matches", absolute.Result!.Value.GetProperty("verdict").GetString(), "under the product's own origin too");
    }

    [TestMethod]
    public async Task TheDigestIsTheStatedRuleOverTheIndexsOwnRows()
    {
        // Recomputed here from the EU index, as EuropeWordingDigestRule states it: the domain, the CELEX, the work and
        // expression IRIs, the language, the wording date, then every article identity in the publisher's article order.
        var (fixture, mount, answer) = await SearchedAsync();
        await using var cleanup = fixture;
        using var disposeMount = mount;
        using var connection = EuropeIndexBuilder.Open(Path.Combine(fixture.Directory, V3CorpusMount.EuropeIndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT publisher_work_celex, publisher_work_id, publisher_expression_id, language, wording_date, article_identity_sha256 FROM articles " +
            "ORDER BY publisher_identifier, article_identity_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<string[]>();
        while (reader.Read())
        {
            rows.Add([reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)]);
        }

        Assert.AreEqual(1, rows.Select(static row => row[4]).Distinct().Count(), "the GDPR's wording holds one date");
        var digest = WordingSha256ByTheStatedRule(rows[0][0], rows[0][1], rows[0][2], rows[0][3], rows[0][4], rows.Select(static row => row[5]));
        Assert.AreEqual(digest, answer.GetProperty("pinned_wording").GetProperty("wording_sha256").GetString());
    }

    [TestMethod]
    public async Task APermalinkTheWordingDoesNotBearIsRefusedWithItsOwnCode()
    {
        var (fixture, mount, answer) = await SearchedAsync();
        await using var cleanup = fixture;
        using var disposeMount = mount;
        var permalink = answer.GetProperty("pinned_wording").GetProperty("permalink").GetString()!;
        var digest = permalink[^64..];
        var date = permalink.Split('/')[4][..10];

        var mismatch = await VerifyAsync(mount, permalink.Replace(digest, new string('0', 64), StringComparison.Ordinal));
        Assert.AreEqual("pinned_digest_mismatch", mismatch.Refusal?.Code);
        Assert.AreEqual(digest, mismatch.Refusal!.HelpfulPayload.GetProperty("current_digest").GetString(), "the refusal names the current digest");
        Assert.AreEqual(permalink, mismatch.Refusal.HelpfulPayload.GetProperty("current_hash_pinned_url").GetString());
        Assert.AreEqual(permalink[..^66], mismatch.Refusal.HelpfulPayload.GetProperty("stable_coordinate").GetString(), "the stable coordinate is the permalink without its digest");

        Assert.AreEqual("anchor_not_in_version", (await VerifyAsync(mount, permalink + "#999999")).Refusal?.Code, "a provision the wording does not hold");
        Assert.AreEqual("language_not_available", (await VerifyAsync(mount, permalink.Replace("/eng/", "/fra/", StringComparison.Ordinal))).Refusal?.Code,
            "a language the work is not held in");
        Assert.AreEqual("language_not_available", (await VerifyAsync(mount, permalink, "fra")).Refusal?.Code, "a requested language that is not the permalink's");
        Assert.AreEqual("identifier_unknown", (await VerifyAsync(mount, permalink.Replace("32016R0679", "32099R9999", StringComparison.Ordinal))).Refusal?.Code,
            "a work the index does not hold");
        Assert.AreEqual("identifier_unknown", (await VerifyAsync(mount, permalink.Replace(date, "1999-01-01", StringComparison.Ordinal))).Refusal?.Code,
            "a wording date the expression does not bear");

        // The review of #850's reproduction: an article identity of the work in the CELEX slot resolves the same work,
        // and must still be refused: the slot holds the CELEX and nothing else.
        var articleIdentity = answer.GetProperty("hits")[0].GetProperty("article_identity_sha256").GetString()!;
        var stolen = await VerifyAsync(mount, permalink.Replace("/32016R0679/", $"/{articleIdentity}/", StringComparison.Ordinal) + "#" + answer.GetProperty("hits")[0].GetProperty("publisher_id").GetString());
        Assert.AreEqual("identifier_unknown", stolen.Refusal?.Code, $"an article identity in the CELEX slot: {stolen.Result?.Value.GetProperty("verdict")}");
    }

    [TestMethod]
    public async Task AnEuPermalinkOnAMountWithoutTheEuIndexIsRefusedNoCorpusMounted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = await VerifyAsync(mount, $"/eu-eurlex/32016R0679/eng/2016-04-27--{new string('a', 64)}#001");
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal?.Code);
    }

    [TestMethod]
    public async Task EveryEuPermalinkASearchOfTheNamedMountEmitsVerifies()
    {
        // On the mount V3_EVALUATE_MOUNT names (the real bounded first mount holds the GDPR in English): every hit of a
        // search for "personal data" carries a pinned permalink, and each verifies as digest_matches.
        var directory = Environment.GetEnvironmentVariable("V3_EVALUATE_MOUNT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive("V3_EVALUATE_MOUNT names no mount.");
        }

        using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query = "personal data", language = "eng", identifier = "32016R0679" });
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        var hits = envelope.Result!.Value.GetProperty("hits").EnumerateArray().ToArray();
        Assert.IsNotEmpty(hits);
        foreach (var hit in hits)
        {
            var permalink = hit.GetProperty("permalink").GetString()!;
            var verified = await VerifyAsync(mount, permalink, "eng");
            Assert.AreEqual("digest_matches", verified.Result?.Value.GetProperty("verdict").GetString(), $"{permalink}: {verified.Refusal?.Code}");
        }

    }
}
