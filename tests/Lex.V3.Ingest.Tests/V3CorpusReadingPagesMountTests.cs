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
/// several, by its bytes. An article the version does not hold is <c>anchor_not_in_version</c>, and so is a page past the
/// version's last, naming the version's pages (the review of #978, F2); a page and an article together is a malformed request.
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
        AssertPageNotInVersion(await EnvelopeAsync(mount, Route, "evidence_bundle", new { asked.identifier, asked.date, asked.language, page = 4 }),
            4, 3, "fra", "the page after the last of three");

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

    /// <summary>
    /// The refusal of a page past a version's last (the review of #978, F2): an anchor the version does not hold, <c>page=N</c>,
    /// with its first and last pages as the nearest anchors, and the page asked, the version's page count and its language.
    /// </summary>
    private static void AssertPageNotInVersion(V3Envelope refused, int asked, int pages, string language, string why)
    {
        Assert.AreEqual("anchor_not_in_version", refused.Refusal?.Code, why);
        var payload = refused.Refusal!.HelpfulPayload;
        Assert.AreEqual($"page={asked}", payload.GetProperty("requested_anchor").GetString(), why);
        CollectionAssert.AreEqual(pages == 1 ? new[] { "page=1" } : new[] { "page=1", $"page={pages}" },
            payload.GetProperty("nearest_anchors").EnumerateArray().Select(static anchor => anchor.GetString()).ToArray(), why);
        Assert.IsTrue(payload.GetProperty("do_not_fall_back_to_full_text_search").GetBoolean(), why);
        Assert.AreEqual(asked, payload.GetProperty("requested_page").GetInt32(), why);
        Assert.AreEqual(pages, payload.GetProperty("pages").GetInt32(), why);
        Assert.AreEqual(language, payload.GetProperty("language").GetString(), why);
        Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean(), why);
        Assert.AreEqual(V3OperationRegistry.Reviewed.MandatoryPayloadFields("anchor_not_in_version").Count + 5, payload.EnumerateObject().Count(),
            "the anchor's mandatory fields, the page asked, the version's pages and language, what would answer, and no claim of absence");
    }

    [TestMethod]
    public async Task AnArticleTheVersionDoesNotHoldIsRefusedWithItsNeighboursAndAPagePastTheLastNamesTheVersionsPages()
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
            Assert.AreEqual(V3OperationRegistry.Reviewed.MandatoryPayloadFields("anchor_not_in_version").Count + 2, payload.EnumerateObject().Count(),
                "the payload every operation sends for an anchor: its mandatory fields, what would answer, and no claim of absence");
        }

        // A page past the state's one page: refused as an anchor the state does not hold, at its permalink and by the work, with a
        // language asked or none (each state served is cut on its own, and the refusal names the state's language).
        AssertPageNotInVersion(await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, page = 2 }),
            2, 1, "fra", "at the permalink");
        AssertPageNotInVersion(await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra", page = 100_000 }),
            100_000, 1, "fra", "by the work, in French");
        AssertPageNotInVersion(await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, page = 3 }),
            3, 1, "fra", "by the work, with no language asked");

        foreach (var (parameters, why) in new (object Parameters, string Why)[]
                 {
                     (new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, page = 100_001 }, "a page past the schema's greatest"),
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

        // A page past the wording's last, at its permalink and by its CELEX: the wording's pages and its language.
        foreach (var (identifier, why) in new[] { (permalink, "at the permalink"), ("32016R0679", "by the CELEX") })
        {
            var past = await EnvelopeAsync(mount, Route, "evidence_bundle", new { identifier, date, language = "eng", page = pages + 1 });
            AssertPageNotInVersion(past, pages + 1, pages, "eng", why);
            Assert.AreEqual(PublisherId.EuEurLex, past.Context.Publisher, why);
        }
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
                     // The review of #978 (F1): "Ier" written as a roman numeral with er after it, and art written right before
                     // its number, come first and in their place, as derive-c's states write them.
                     new[] { "art_ier", "art_ii", "art_iibis", "art_iii", "art_iv", "art_v", "art_viii" },
                     new[] { "art1er", "art_2", "art_3", "art12", "art_13", "art_32" },
                     new[] { "art_premier", "art_2", "art_3" },
                     new[] { "article_1er", "article_2", "article12" },
                     new[] { "art_9", "art_99999999999999999999", "art_100000000000000000000" },
                 })
        {
            CollectionAssert.AreEqual(expected, expected.Reverse().Order(order).ToArray(), string.Join(" ", expected));
        }

        Assert.AreEqual(0, V3CorpusMount.CompareLuxembourgArticleIds("art_15", "art_15"));
        Assert.IsLessThan(0, V3CorpusMount.CompareLuxembourgArticleIds("art_05", "art_5"), "a tie in value broken by the text, so the order is total");
        Assert.AreNotEqual(0, V3CorpusMount.CompareLuxembourgArticleIds("art_1er", "art1er"), "one value, one adverb: the text decides, so the order is total");
        Assert.IsGreaterThan(0, V3CorpusMount.CompareLuxembourgArticleIds("art_l_121-6", "art_l"), "art_l alone is the roman 50; art_l_121-6 is a code's L. article");
    }

    /// <summary>
    /// The reading order over every state a Luxembourg index holds (the review of #978, F1), when
    /// <c>V3_READING_ORDER_INDEX</c> names a <c>luxembourg-index.sqlite3</c>, read in place and read-only: in each state, the
    /// first article (an id that is 1, 1er, Ier, I or premier after art or article) comes first wherever one is held, and the ids
    /// of the plain shapes (art or article, a number or a roman numeral, and er or a Latin adverb) come in the order of their
    /// numbers. Opt-in, as the probes over a run's population are; it prints what it read.
    /// </summary>
    [TestMethod]
    public void TheReadingOrderPutsTheFirstArticleFirstInEveryStateTheNamedIndexHolds()
    {
        var path = Environment.GetEnvironmentVariable("V3_READING_ORDER_INDEX");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive("V3_READING_ORDER_INDEX names no Luxembourg index, so no population's order is read.");
        }

        var first = new System.Text.RegularExpressions.Regex("^(?:art|article)_?(?:1|1er|ier|i|premier)$");
        var plain = new System.Text.RegularExpressions.Regex("^(?:art|article)_?(?<number>[0-9]+|[ivxlcdm]+)(?<suffix>er|bis|ter|quater|quinquies|sexies|septies|octies|nonies|decies)?$");
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source=file:{path.Replace('\\', '/')}?immutable=1;Mode=ReadOnly");
        using var _ = connection;
        connection.Open();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT article_identity_sha256, publisher_id FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var order = Comparer<string>.Create(V3CorpusMount.CompareLuxembourgArticleIds);
        int states = 0, withFirst = 0, plainPairs = 0;
        var failures = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT work_key, applicability_date, language, article_identities_json FROM states";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                states++;
                var held = JsonSerializer.Deserialize<string[]>(reader.GetString(3))!.Select(identity => ids[identity]).Distinct(StringComparer.Ordinal).Order(order).ToArray();
                var name = $"{reader.GetString(0)} {reader.GetString(1)} {reader.GetString(2)}";
                if (held.Any(id => first.IsMatch(id)))
                {
                    withFirst++;
                    if (!first.IsMatch(held[0]))
                    {
                        failures.Add($"{name}: {held[0]} before its first article");
                    }
                }

                var values = held.Select(id => plain.Match(id)).Where(static match => match.Success)
                    .Select(static match => (Value: NumberOf(match.Groups["number"].Value), Text: match.Value)).ToArray();
                for (var index = 1; index < values.Length; index++)
                {
                    plainPairs++;
                    if (values[index].Value < values[index - 1].Value)
                    {
                        failures.Add($"{name}: {values[index - 1].Text} before {values[index].Text}");
                    }
                }
            }
        }

        Console.WriteLine($"{states} states, {withFirst} with a first article, {plainPairs} pairs of plain ids in order");
        // Where the run asks for it (V3_READING_ORDER_REPORT), what it read, written down.
        if (Environment.GetEnvironmentVariable("V3_READING_ORDER_REPORT") is { Length: > 0 } report)
            File.WriteAllText(report,
            $"{states} states, {withFirst} with a first article, {plainPairs} pairs of plain ids, {failures.Count} out of order{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(50))}{Environment.NewLine}");
        Assert.IsEmpty(failures, string.Join("; ", failures.Take(20)));

        static long NumberOf(string text) => char.IsAsciiDigit(text[0])
            ? long.Parse(text.Length > 18 ? text[..18] : text, System.Globalization.CultureInfo.InvariantCulture)
            : RomanOf(text);

        static long RomanOf(string text)
        {
            long value = 0;
            for (var index = 0; index < text.Length; index++)
            {
                long Digit(char character) => character switch { 'i' => 1, 'v' => 5, 'x' => 10, 'l' => 50, 'c' => 100, 'd' => 500, _ => 1000 };
                var current = Digit(text[index]);
                value += index + 1 < text.Length && current < Digit(text[index + 1]) ? -current : current;
            }

            return value;
        }
    }

    [TestMethod]
    public void APageClosesAtFiftyArticlesOrItsBytesAndAnArticleThatPassesTheBoundAloneIsAPageOfItsOwn()
    {
        static V3CorpusMount.ReadingEntry Entry(int number, long weight) => new($"art_{number}", null, weight > 0, weight);
        var none = new V3CorpusMount.ReadingPageAsked(null, null, false);

        var counted = V3CorpusMount.CutReadingPages(Enumerable.Range(1, 101).Select(number => Entry(number, 10)).ToArray(), none)!;
        CollectionAssert.AreEqual(new[] { (0, 49), (50, 99), (100, 100) }, counted.Bounds.ToArray(), "50, 50 and 1");

        var weighed = V3CorpusMount.CutReadingPages(
            [Entry(1, 50_000), Entry(2, 40_000), Entry(3, 10_000), Entry(4, 100_000), Entry(5, 0), Entry(6, 98_304)], none)!;
        CollectionAssert.AreEqual(new[] { (0, 1), (2, 2), (3, 3), (4, 5) }, weighed.Bounds.ToArray(),
            "90,000 bytes, then 10,000 (adding 100,000 would pass the bound), then 100,000 alone, then a text-less article with 98,304");

        var byArticle = V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(null, "art_4", false))!;
        Assert.AreEqual(3, byArticle.Served);
        Assert.IsNull(V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(null, "art_7", false)));
        Assert.AreEqual(4, V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(4, null, false))!.Served);
        Assert.IsFalse(V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(4, null, false))!.PastLast);
        var past = V3CorpusMount.CutReadingPages(weighed.Entries, new V3CorpusMount.ReadingPageAsked(5, null, false))!;
        Assert.IsTrue(past.PastLast, "a page past the last is cut and not served, for its refusal to name the pages");
        Assert.AreEqual(4, past.Count);
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

    /// <summary>
    /// The stand-in for the codes written where the speed check asks (<c>web/scripts/reading-weight.mjs</c>), when
    /// <c>V3_WRITE_CODE_STAND_IN_MOUNT</c> names a directory: the state of 3,000 synthetic articles above, as a mount a local
    /// API serves, so its answers and its pages are measured as the population mount's largest versions are. Opt-in, as the
    /// journey mounts are; otherwise inconclusive and nothing is written.
    /// </summary>
    [TestMethod]
    public async Task TheStandInForTheCodesIsWrittenWhereTheSpeedCheckAsks()
    {
        var target = Environment.GetEnvironmentVariable("V3_WRITE_CODE_STAND_IN_MOUNT");
        if (string.IsNullOrWhiteSpace(target))
        {
            Assert.Inconclusive("V3_WRITE_CODE_STAND_IN_MOUNT names no directory, so no stand-in mount is written.");
        }

        var fixture = await MountedFixture.CreateAsync(await SyntheticLongAct.DocumentAsync(3000, textBytes: 340, lean: true));
        await using var cleanup = fixture;
        Directory.CreateDirectory(target);
        foreach (var name in new[] { V3CorpusMount.IndexFileName, V3CorpusMount.CapabilityManifestFileName, V3CorpusMount.CorpusFileName })
        {
            File.Copy(Path.Combine(fixture.Directory, name), Path.Combine(target, name), overwrite: true);
        }

        using (var mount = await V3CorpusMount.OpenAsync(target, CancellationToken.None))
        {
            Assert.IsNotNull(mount, "the stand-in mounts through the API's own verifier.");
        }

        await File.WriteAllTextAsync(Path.Combine(target, "stand-in.json"), JsonSerializer.Serialize(new
        {
            note = "the fixture act with 3,000 articles of synthetic text (THE MOUNT IS A FIXTURE), the stand-in for the codes",
            permalink = fixture.Permalink,
            date = fixture.ApplicabilityDate,
        }) + "\n");
    }
}
