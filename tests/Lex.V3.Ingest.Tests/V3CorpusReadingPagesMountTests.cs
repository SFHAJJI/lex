using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Reading by article and by page (the plan's item 3.7; the specification of record's R73 and R107): <c>evidence_bundle</c>
/// answers a version one bounded page at a time, with the index of its pages and, when asked, its contents, on both
/// publishers, at a permalink and by the work and a date. The fixture act (49 articles) fits one page, so it is answered whole
/// as before; a synthetic act of 130 articles (<see cref="SyntheticLongAct"/>) is three pages, and the retained GDPR wording
/// several, by its bytes. An article the version does not hold is <c>anchor_not_in_version</c>; a page past the last, or a
/// page and an article together, is a malformed request.
/// </summary>
[TestClass]
public sealed class V3CorpusReadingPagesMountTests
{
    private const string Route = "/api/v3/evidence_bundle";

    private static JsonElement StateOf(V3Envelope envelope)
    {
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        return envelope.Result!.Value.GetProperty("states").EnumerateArray().Single();
    }

    private static string[] IdsOf(JsonElement version, string member = "articles") =>
        version.GetProperty(member).EnumerateArray().Select(static article => article.GetProperty("publisher_id").GetString()!).ToArray();

    private static int[] PositionsOf(JsonElement version, string member = "articles") =>
        version.GetProperty(member).EnumerateArray().Select(static article => article.GetProperty("position").GetInt32()).ToArray();

    [TestMethod]
    public async Task AStateThatFitsOnePageIsAnsweredWholeInTheOrderOfItsIdsWithItsPageIndex()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" });
        var bundle = envelope.Result!.Value;
        Assert.AreEqual(JsonValueKind.Null, bundle.GetProperty("requested_page").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, bundle.GetProperty("requested_article").ValueKind);
        StringAssert.Contains(bundle.GetProperty("page_rule").GetString(), "at most 50 of them and at most 98,304 bytes");
        var state = StateOf(envelope);
        var ids = IdsOf(state);
        Assert.HasCount(49, ids, "the fixture state's 49 quoted articles, every one on its one page");
        Assert.AreEqual("art_1er", ids[0], "in the order of the publisher's ids: art_1er first");
        CollectionAssert.AreEqual(
            ids.Order(Comparer<string>.Create(V3CorpusMount.CompareLuxembourgArticleIds)).ToArray(), ids, "the ids in the reading's order");
        CollectionAssert.AreEqual(Enumerable.Range(1, 49).ToArray(), PositionsOf(state), "each article's position in the state");
        CollectionAssert.AreEqual(new[] { "art_1er", "art_2", "art_3", "art_4", "art_5", "art_7", "art_8", "art_9", "art_10", "art_11" }, ids[..10]);

        var page = state.GetProperty("page");
        Assert.AreEqual(1, page.GetProperty("number").GetInt32());
        Assert.AreEqual(1, page.GetProperty("pages").GetInt32());
        Assert.AreEqual(1, page.GetProperty("first_position").GetInt32());
        Assert.AreEqual(49, page.GetProperty("last_position").GetInt32());
        Assert.AreEqual(49, page.GetProperty("articles_in_version").GetInt32());
        Assert.AreEqual(49, page.GetProperty("quoted_in_version").GetInt32());
        Assert.AreEqual(0, page.GetProperty("without_text_in_version").GetInt32());
        Assert.AreEqual(state.GetProperty("validity_conflict_count").GetInt32(), page.GetProperty("validity_conflicts_in_version").GetInt32());
        var index = state.GetProperty("pages").EnumerateArray().Single();
        Assert.AreEqual("art_1er", index.GetProperty("first_publisher_id").GetString());
        Assert.AreEqual(ids[^1], index.GetProperty("last_publisher_id").GetString());
        Assert.AreEqual(49, index.GetProperty("articles").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, state.GetProperty("contents").ValueKind, "no contents unless asked");

        // Asked with its contents, and by the article it holds: the same page, and every article's position, id and page.
        var withContents = StateOf(await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, article = "art_15", contents = true }));
        CollectionAssert.AreEqual(ids, IdsOf(withContents), "the page that holds art_15 at the permalink: the one page");
        var contents = withContents.GetProperty("contents").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(ids, contents.Select(static row => row.GetProperty("publisher_id").GetString()!).ToArray());
        Assert.IsTrue(contents.All(static row => row.GetProperty("page").GetInt32() == 1 && row.GetProperty("text_held").GetBoolean()));
        Assert.IsFalse(contents.Any(static row => row.TryGetProperty("heading", out _)), "a Luxembourg article has no heading of its own here");
        Assert.IsFalse(contents.Any(static row => row.TryGetProperty("text", out _)), "and the contents carry no text");
    }

    [TestMethod]
    public async Task ALongStateIsReadPageByPageAndEveryArticleIsOnExactlyOnePage()
    {
        var fixture = await MountedFixture.CreateAsync(await SyntheticLongAct.DocumentAsync(130, textBytes: 1200));
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var asked = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };

        var first = StateOf(await EnvelopeAsync(mount, Route, "evidence_bundle", asked));
        var page = first.GetProperty("page");
        Assert.AreEqual(3, page.GetProperty("pages").GetInt32(), "130 articles of about 1,200 bytes: 50 a page");
        Assert.AreEqual(130, page.GetProperty("articles_in_version").GetInt32());
        CollectionAssert.AreEqual(new[] { 50, 50, 30 }, first.GetProperty("pages").EnumerateArray().Select(static row => row.GetProperty("articles").GetInt32()).ToArray());
        CollectionAssert.AreEqual(new[] { "art_1er", "art_51", "art_101" },
            first.GetProperty("pages").EnumerateArray().Select(static row => row.GetProperty("first_publisher_id").GetString()).ToArray(),
            "the ids' numbers compared as numbers, never as text (art_10 after art_9)");
        Assert.AreEqual("art_10", IdsOf(first)[9]);

        var all = new List<string>();
        var positions = new List<int>();
        for (var number = 1; number <= 3; number++)
        {
            var state = StateOf(await EnvelopeAsync(mount, Route, "evidence_bundle", new { asked.identifier, asked.date, asked.language, page = number }));
            Assert.AreEqual(number, state.GetProperty("page").GetProperty("number").GetInt32());
            Assert.AreEqual(first.GetProperty("state_sha256").GetString(), state.GetProperty("state_sha256").GetString(), "every page is of the one state");
            all.AddRange(IdsOf(state));
            positions.AddRange(PositionsOf(state));
        }

        CollectionAssert.AreEqual(Enumerable.Range(1, 130).ToArray(), positions.ToArray(), "every article once, in order, across the pages");
        Assert.AreEqual(130, all.Distinct(StringComparer.Ordinal).Count());

        // By an article: the page that holds it; at the permalink too, with the contents.
        var permalink = first.GetProperty("permalink").GetString()!;
        var holding = StateOf(await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = permalink, date = fixture.ApplicabilityDate, article = "art_77", contents = true }));
        Assert.AreEqual(2, holding.GetProperty("page").GetProperty("number").GetInt32());
        CollectionAssert.Contains(IdsOf(holding), "art_77");
        var contents = holding.GetProperty("contents").EnumerateArray().ToArray();
        Assert.HasCount(130, contents);
        Assert.AreEqual(2, contents.Single(static row => row.GetProperty("publisher_id").GetString() == "art_77").GetProperty("page").GetInt32());

        // Bounded: no page of the act answers more than its share, whatever the act's length.
        var pageBytes = Encoding.UTF8.GetByteCount(holding.GetRawText());
        Assert.IsLessThan(200_000, pageBytes, "a page of 50 articles with the contents of 130");
    }

    [TestMethod]
    public async Task AnArticleTheVersionDoesNotHoldIsRefusedWithItsNeighboursAndAPagePastTheLastIsMalformed()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { $"/lu-legilux/{fixture.WorkKey}", fixture.Permalink })
        {
            var refused = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier, date = fixture.ApplicabilityDate, language = "fra", article = "art_15-9" });
            Assert.AreEqual("anchor_not_in_version", refused.Refusal?.Code, identifier);
            var payload = refused.Refusal!.HelpfulPayload;
            Assert.AreEqual("art_15-9", payload.GetProperty("requested_anchor").GetString());
            CollectionAssert.Contains(payload.GetProperty("nearest_anchors").EnumerateArray().Select(static id => id.GetString()).ToArray(), "art_15");
            Assert.IsTrue(payload.GetProperty("do_not_fall_back_to_full_text_search").GetBoolean());
            Assert.AreEqual(fixture.Permalink, payload.GetProperty("permalink").GetString());
        }

        foreach (var (parameters, why) in new (object Parameters, string Why)[]
                 {
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, page = 2 }, "a page past the state's one page"),
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, page = 1, article = "art_15" }, "a page and an article together"),
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, page = 0 }, "page 0"),
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, page = "1" }, "a page written as text"),
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, contents = "yes" }, "contents that is not true or false"),
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, article = " " }, "a blank article"),
                 })
        {
            var context = await PostAsync(mount, Route, JsonSerializer.Serialize(new { operation_id = "evidence_bundle", parameters }));
            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode, why);
            using var problem = JsonDocument.Parse(ResponseBytes(context));
            Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString(), why);
        }
    }

    [TestMethod]
    public async Task AnEuWordingIsReadPageByPageByItsBytesAtItsPermalinkAndByItsCelex()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = "32016R0679", language = "eng" });
        var pinned = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().Single().GetProperty("pinned_wording");
        var date = pinned.GetProperty("wording_date").GetString()!;
        var permalink = pinned.GetProperty("permalink").GetString()!;

        var first = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date, language = "eng" });
        Assert.IsNull(first.Refusal, first.Refusal?.Code);
        StringAssert.Contains(first.Result!.Value.GetProperty("page_rule").GetString(), "provision ids");
        var wording = first.Result.Value.GetProperty("wordings").EnumerateArray().Single();
        var page = wording.GetProperty("page");
        var pages = page.GetProperty("pages").GetInt32();
        Assert.IsGreaterThan(1, pages, "the GDPR's English wording passes one page's bytes");
        var index = wording.GetProperty("pages").EnumerateArray().ToArray();
        Assert.HasCount(pages, index);
        Assert.IsTrue(index.All(static row => row.TryGetProperty("first_heading", out _) && row.TryGetProperty("last_heading", out _)), "an EU page names its headings");

        var all = new List<int>();
        var largest = 0L;
        for (var number = 1; number <= pages; number++)
        {
            var served = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = permalink, date, page = number });
            Assert.IsNull(served.Refusal, served.Refusal?.Code);
            var at = served.Result!.Value.GetProperty("wordings").EnumerateArray().Single();
            Assert.AreEqual(permalink, at.GetProperty("permalink").GetString());
            all.AddRange(PositionsOf(at));
            all.AddRange(PositionsOf(at, "articles_without_text"));
            var text = at.GetProperty("articles").EnumerateArray().Sum(static article => (long)article.GetProperty("text_byte_length").GetInt32());
            Assert.IsTrue(text <= V3CorpusMount.ReadingPageBytes || at.GetProperty("articles").GetArrayLength() == 1, $"page {number} holds {text} bytes of text");
            largest = Math.Max(largest, text);
        }

        all.Sort();
        CollectionAssert.AreEqual(Enumerable.Range(1, page.GetProperty("articles_in_version").GetInt32()).ToArray(), all.ToArray(), "every provision on exactly one page");

        var lastId = index[^1].GetProperty("last_publisher_id").GetString()!;
        var byArticle = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = "32016R0679", date, language = "eng", article = lastId, contents = true });
        var atLast = byArticle.Result!.Value.GetProperty("wordings").EnumerateArray().Single();
        Assert.AreEqual(pages, atLast.GetProperty("page").GetProperty("number").GetInt32(), "the page that holds the last provision is the last page");
        Assert.IsTrue(atLast.GetProperty("contents").EnumerateArray().All(static row => row.TryGetProperty("heading", out _)), "an EU wording's contents name each heading");

        var missing = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = permalink, date, article = "no-such-provision" });
        Assert.AreEqual("anchor_not_in_version", missing.Refusal?.Code);
        Assert.AreEqual(PublisherId.EuEurLex, missing.Context.Publisher);
    }

    [TestMethod]
    public void TheReadingOrderComparesNumbersAsNumbersAndKnowsTheLatinAdverbsAndRomanNumerals()
    {
        var order = Comparer<string>.Create(V3CorpusMount.CompareLuxembourgArticleIds);
        foreach (var expected in new[]
                 {
                     new[] { "art_1er", "art_1bis", "art_2", "art_2-1", "art_2bis", "art_2ter", "art_2quater", "art_9", "art_10", "art_10-2", "art_10-10", "art_100" },
                     new[] { "art_i", "art_ii", "art_iii", "art_iv", "art_v", "art_ix", "art_x", "art_xxvii" },
                     new[] { "art_l_121-1", "art_l_121-6", "art_l_121-10", "art_r_11-1" },
                     new[] { "art_40", "art_40bis", "art_40quinquies", "art_40decies", "art_41" },
                 })
        {
            CollectionAssert.AreEqual(expected, expected.Reverse().Order(order).ToArray(), string.Join(" ", expected));
        }

        Assert.AreEqual(0, V3CorpusMount.CompareLuxembourgArticleIds("art_15", "art_15"));
        Assert.IsLessThan(0, V3CorpusMount.CompareLuxembourgArticleIds("art_05", "art_5"), "a tie in value broken by the text, so the order is total");
    }

    [TestMethod]
    public void APageClosesAtFiftyArticlesOrItsBytesAndAnArticleThatPassesTheBoundAloneIsAPageOfItsOwn()
    {
        static V3CorpusMount.ReadingEntry Entry(int number, long weight) => new($"art_{number}", null, weight > 0, weight);
        var none = new V3CorpusMount.ReadingPageAsked(null, null, false);

        var counted = V3CorpusMount.CutReadingPages(Enumerable.Range(1, 101).Select(number => Entry(number, 10)).ToArray(), none, "test")!;
        CollectionAssert.AreEqual(new[] { (0, 49), (50, 99), (100, 100) }, counted.Bounds.ToArray(), "50, 50 and 1");

        var weighed = V3CorpusMount.CutReadingPages(
            [Entry(1, 50_000), Entry(2, 40_000), Entry(3, 10_000), Entry(4, 100_000), Entry(5, 0), Entry(6, 98_304)], none, "test")!;
        CollectionAssert.AreEqual(new[] { (0, 1), (2, 2), (3, 3), (4, 5) }, weighed.Bounds.ToArray(),
            "90,000 bytes, then 10,000 (adding 100,000 would pass the bound), then 100,000 alone, then a text-less article with 98,304");

        var byArticle = V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(null, "art_4", false), "test")!;
        Assert.AreEqual(3, byArticle.Served);
        Assert.IsNull(V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(null, "art_7", false), "test"));
        Assert.AreEqual(4, V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(4, null, false), "test")!.Served);
        Assert.ThrowsExactly<V3TransportFailureException>(() => V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(5, null, false), "test"));
    }

    /// <summary>
    /// The speed check of the stand-in for the codes (R73, R107): a state of 3,000 synthetic articles, read through the real
    /// handler. Its first page and the page that holds its last article are each answered within the page bound, in time.
    /// The probe prints what it measured (Release 1's codes are not in the population mount, so this is their stand-in).
    /// </summary>
    [TestMethod]
    public async Task AStateOfThreeThousandArticlesIsAnsweredOnePageAtATimeWithinItsBoundAndInTime()
    {
        var fixture = await MountedFixture.CreateAsync(await SyntheticLongAct.DocumentAsync(3000, textBytes: 340, lean: true));
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var asked = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };

        // Warm once (the index's first read of the state), then measured.
        _ = await EnvelopeAsync(mount, Route, "evidence_bundle", asked);
        var clock = Stopwatch.StartNew();
        var first = await PostAsync(mount, Route, JsonSerializer.Serialize(new { operation_id = "evidence_bundle", parameters = asked }));
        var firstMs = clock.ElapsedMilliseconds;
        var firstBytes = ResponseBytes(first).Length;
        clock.Restart();
        var last = await PostAsync(mount, Route, JsonSerializer.Serialize(new { operation_id = "evidence_bundle", parameters = new { asked.identifier, asked.date, asked.language, article = "art_3000", contents = true } }));
        var lastMs = clock.ElapsedMilliseconds;
        var lastBytes = ResponseBytes(last).Length;

        using var firstDocument = JsonDocument.Parse(ResponseBytes(first));
        var state = firstDocument.RootElement.GetProperty("result").GetProperty("value").GetProperty("states")[0];
        Assert.AreEqual(3000, state.GetProperty("page").GetProperty("articles_in_version").GetInt32());
        Assert.AreEqual(60, state.GetProperty("page").GetProperty("pages").GetInt32());
        Console.WriteLine($"3,000 synthetic articles: page 1 {firstBytes:N0} bytes in {firstMs} ms; the last page with the contents {lastBytes:N0} bytes in {lastMs} ms");
        Assert.IsLessThan(150_000, firstBytes, "one page of 50 articles, a small fraction of the whole state's answer");
        Assert.IsLessThan(600_000, lastBytes, "a page and the contents of 3,000 articles");
        Assert.IsLessThan(5_000L, firstMs, "a page is answered in time on any runner");
    }
}
