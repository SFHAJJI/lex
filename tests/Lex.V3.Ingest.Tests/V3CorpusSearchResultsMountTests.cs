using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What a Luxembourg <c>search</c> answer carries so its results read by act (the plan's item 4.3; R59, R21, R11) and so a
/// search with no hit can say what it searched (item 4.5), driven through the real handler on a verified mount: each hit's
/// extract is the bytes its lane matched, word for word, with every whole match marked and nothing else; the page's acts are
/// named once each, with the title the index holds and the as-published label; a version's hits come in the reading's order
/// of the publisher's article ids; the title ladder's card names the work it links to; and the population the lanes matched
/// against is counted from the index, with a date, a named work and the rights rule each narrowing it as they narrow the
/// hits.
/// </summary>
[TestClass]
public sealed class V3CorpusSearchResultsMountTests
{
    private const string Phrase = "garantie locative";
    private const string Filler = "Le present article ne dit rien du depot.";

    private static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>A text of <paramref name="words"/> words before and after the phrase, accented so bytes and characters differ.</summary>
    private static string Around(int words, string middle) =>
        string.Join(' ', Enumerable.Range(0, words).Select(static index => $"élément{index}")) + " " + middle + " " +
        string.Join(' ', Enumerable.Range(0, words).Select(static index => $"après{index}"));

    private static (string Text, bool Matched)[] Parts(JsonElement extract) =>
        extract.GetProperty("parts").EnumerateArray()
            .Select(static part => (part.GetProperty("text").GetString()!, part.GetProperty("matched").GetBoolean()))
            .ToArray();

    [TestMethod]
    public async Task EachHitQuotesTheBytesItsLaneMatchedTheActsAreNamedOnceAndAVersionsHitsComeInTheReadingsOrder()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var articles = fixture.ArticlesOfOwnState();
        Assert.IsGreaterThanOrEqualTo(12, articles.Count, "the fixture's state holds enough articles for this test");

        // Twelve articles carry the phrase: their ids run past 9, so the reading's order (art_2 before art_10) differs from
        // the ordinal one and from the order of their identities.
        var long1 = Around(60, "La " + Phrase + " ne peut exceder trois mois de loyer; la " + Phrase + " est restituee.");
        var texts = articles.ToDictionary(static article => article.PublisherId, static _ => Filler, StringComparer.Ordinal);
        var carrying = articles.Select(static article => article.PublisherId)
            .OrderBy(static id => id, Comparer<string>.Create(V3CorpusMount.CompareLuxembourgArticleIds))
            .Take(12)
            .ToArray();
        foreach (var id in carrying)
        {
            texts[id] = "Art. " + id + ". La " + Phrase + " ne peut exceder trois mois.";
        }

        texts[carrying[0]] = long1;
        // One article only the relaxed lane matches: both terms, apart and out of order.
        var termsOnly = articles.Select(static article => article.PublisherId).Except(carrying, StringComparer.Ordinal).First();
        texts[termsOnly] = "Le bailleur restitue toute somme locative tenue en garantie.";
        await fixture.RewriteArticleTextsAsync(fixture.ExpressionIri, texts);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase)).Result!.Value;
        var hits = body.GetProperty("hits").EnumerateArray().ToArray();
        var strict = hits.Where(static hit => hit.GetProperty("lane").GetString() == "strict").ToArray();
        Assert.AreEqual(12, strict.Length, body.GetRawText());

        // A version's hits in the reading's order of the publisher's article ids, which the ranking sentence says.
        CollectionAssert.AreEqual(carrying, strict.Select(static hit => hit.GetProperty("publisher_id").GetString()).ToArray());
        CollectionAssert.AreNotEqual(
            carrying.Order(StringComparer.Ordinal).ToArray(), carrying, "the test's ids would not tell the reading's order from the ordinal one");
        StringAssert.Contains(body.GetProperty("ranking").GetString(), "publisher article id (in the reading's order)");

        // Each extract is a run of the bytes the lane matched, at the byte offsets it names in the text whose digest it names.
        foreach (var hit in hits)
        {
            var id = hit.GetProperty("publisher_id").GetString()!;
            var text = texts[id];
            var extract = hit.GetProperty("extract");
            var bytes = Encoding.UTF8.GetBytes(text);
            Assert.AreEqual(Sha256Hex(text), extract.GetProperty("text_sha256").GetString(), id);
            Assert.AreEqual(bytes.Length, extract.GetProperty("text_byte_length").GetInt32(), id);
            var start = extract.GetProperty("byte_start").GetInt32();
            var end = extract.GetProperty("byte_end").GetInt32();
            var parts = Parts(extract);
            Assert.AreEqual(Encoding.UTF8.GetString(bytes, start, end - start), string.Concat(parts.Select(static part => part.Text)), id);
            var lane = hit.GetProperty("lane").GetString();
            foreach (var part in parts.Where(static part => part.Matched))
            {
                if (lane == "strict")
                {
                    Assert.AreEqual(Phrase, part.Text, id);
                }
                else
                {
                    Assert.IsTrue(part.Text is "garantie" or "locative", $"{id}: {part.Text}");
                }
            }
        }

        // The long text: cut at whitespace, at most 80 characters before the first match and 200 after it, both matches in
        // reach marked, an elision on both sides.
        var longExtract = strict[0].GetProperty("extract");
        var longParts = Parts(longExtract);
        var run = string.Concat(longParts.Select(static part => part.Text));
        Assert.AreEqual(2, longParts.Count(static part => part.Matched), run);
        var firstMatch = long1.IndexOf(Phrase, StringComparison.Ordinal);
        var runStart = long1.IndexOf(run, StringComparison.Ordinal);
        Assert.IsTrue(runStart > 0 && firstMatch - runStart <= V3CorpusMount.SearchExtractBefore, $"{runStart}: {run}");
        Assert.IsTrue(char.IsWhiteSpace(long1[runStart - 1]), "the run begins at a word");
        var runEnd = runStart + run.Length;
        Assert.IsTrue(runEnd < long1.Length && runEnd - (firstMatch + Phrase.Length) <= V3CorpusMount.SearchExtractAfter);
        Assert.IsTrue(char.IsWhiteSpace(long1[runEnd]), "the run ends at a word");
        Assert.IsGreaterThan(0, longExtract.GetProperty("byte_start").GetInt32());
        Assert.AreNotEqual(runStart, longExtract.GetProperty("byte_start").GetInt32(), "byte offsets, not character offsets, in an accented text");
        StringAssert.Contains(body.GetProperty("extract_rule").GetString(), "word for word");

        // The relaxed hit marks each term where it stands.
        var relaxed = hits.Single(static hit => hit.GetProperty("lane").GetString() == "relaxed");
        CollectionAssert.AreEqual(
            new[] { "locative", "garantie" },
            Parts(relaxed.GetProperty("extract")).Where(static part => part.Matched).Select(static part => part.Text).ToArray());

        // The page's one act, named once, with its hits over the whole result, as held only as published, and no title held.
        var acts = body.GetProperty("acts").EnumerateArray().ToArray();
        Assert.AreEqual(1, acts.Length, body.GetProperty("acts").GetRawText());
        Assert.AreEqual(fixture.WorkKey, acts[0].GetProperty("work_key").GetString());
        Assert.AreEqual("/lu-legilux/" + fixture.WorkKey, acts[0].GetProperty("dossier").GetString());
        Assert.AreEqual(13, acts[0].GetProperty("hits").GetInt32());
        Assert.AreEqual("as published; may have been amended since; no consolidated text", acts[0].GetProperty("as_published").GetString());
        Assert.AreEqual(JsonValueKind.Null, acts[0].GetProperty("title").ValueKind, "the fixture's index holds no title");
        Assert.AreEqual(JsonValueKind.Null, acts[0].GetProperty("title_language").ValueKind);

        // A page of one hit still counts the act's hits over the whole result.
        var first = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase, limit: 1)).Result!.Value;
        Assert.AreEqual(13, first.GetProperty("acts")[0].GetProperty("hits").GetInt32());

        // What the lanes matched against: the one state, its work and its articles, every one with text here; the build the
        // mounted log records.
        var searched = body.GetProperty("searched");
        Assert.AreEqual("lu-legilux", searched.GetProperty("publisher").GetString());
        Assert.AreEqual("fra", searched.GetProperty("language").GetString());
        Assert.AreEqual(1, searched.GetProperty("works").GetInt32());
        Assert.AreEqual(1, searched.GetProperty("states").GetInt32());
        Assert.AreEqual(articles.Count, searched.GetProperty("articles_with_searchable_text").GetInt32());
        Assert.AreEqual(0, searched.GetProperty("states_withheld_by_rights").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, searched.GetProperty("date").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, searched.GetProperty("works_with_several_states_on_date").ValueKind);
        Assert.AreEqual(JsonValueKind.String, searched.GetProperty("index_built_at").ValueKind);
        StringAssert.Contains(searched.GetProperty("rule").GetString(), "nothing here counts what the publisher holds");

        // With the work named, the population is that work's; with a date before its one state, no work is searched.
        var named = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase, identifier: "/lu-legilux/" + fixture.WorkKey)).Result!.Value;
        Assert.AreEqual(fixture.WorkKey, named.GetProperty("searched").GetProperty("identifier_work_key").GetString());
        Assert.AreEqual(1, named.GetProperty("searched").GetProperty("works").GetInt32());
        var early = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase, date: "2000-01-01")).Result!.Value;
        Assert.AreEqual(0, early.GetProperty("hits").GetArrayLength());
        Assert.AreEqual(0, early.GetProperty("searched").GetProperty("works").GetInt32());
        Assert.AreEqual(0, early.GetProperty("searched").GetProperty("articles_with_searchable_text").GetInt32());
        Assert.AreEqual(0, early.GetProperty("searched").GetProperty("works_with_several_states_on_date").GetInt32());
        var onDate = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase, date: fixture.ApplicabilityDate, mode: "strict")).Result!.Value;
        Assert.AreEqual(12, onDate.GetProperty("hits").GetArrayLength());
        Assert.AreEqual(1, onDate.GetProperty("searched").GetProperty("works").GetInt32());

        // A phrase no article carries is a whole answer with no act, and the same population.
        var none = (await V3CorpusSearchMountTests.SearchAsync(mount, "zéphyr", mode: "strict")).Result!.Value;
        Assert.AreEqual(0, none.GetProperty("acts").GetArrayLength());
        Assert.AreEqual(1, none.GetProperty("searched").GetProperty("works").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, none.GetProperty("population").GetProperty("relaxed_hits").ValueKind, "the strict lane alone counts no relaxed lane");
    }

    [TestMethod]
    public async Task TheRightsRuleNarrowsThePopulationAsItNarrowsTheHitsAndATitleCardNamesTheWorkItLinksTo()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var id = fixture.ArticlesOfOwnState()[0].PublisherId;
        await fixture.RewriteArticleTextAsync(fixture.ExpressionIri, id, "La " + Phrase + " est due.");
        await fixture.AddWorkTitleAsync();
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var byTitle = (await V3CorpusSearchMountTests.SearchAsync(mount, fixture.WorkTitle)).Result!.Value;
            var card = byTitle.GetProperty("work_resolution").GetProperty("work");
            Assert.AreEqual("exact_normalized_title", card.GetProperty("match_reason").GetString());
            Assert.AreEqual(fixture.WorkKey, card.GetProperty("work_key").GetString(), "the card names the work the index holds its expression under");

            var withTitle = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase)).Result!.Value;
            var act = withTitle.GetProperty("acts")[0];
            Assert.AreEqual(fixture.WorkTitle, act.GetProperty("title").GetString());
            Assert.AreEqual("fra", act.GetProperty("title_language").GetString());
            Assert.AreEqual("title", act.GetProperty("title_kind").GetString());
        }

        // A licence that does not admit the text keeps it out of matching and out of the population, and is counted apart.
        await fixture.SetMemberRightsDispositionAsync("typed_quarantine_in_file_reading_rejected");
        using var withheld = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(withheld);
        var body = (await V3CorpusSearchMountTests.SearchAsync(withheld, Phrase)).Result!.Value;
        Assert.AreEqual(0, body.GetProperty("hits").GetArrayLength());
        var searched = body.GetProperty("searched");
        Assert.AreEqual(0, searched.GetProperty("works").GetInt32());
        Assert.AreEqual(0, searched.GetProperty("states").GetInt32());
        Assert.AreEqual(0, searched.GetProperty("articles_with_searchable_text").GetInt32());
        Assert.AreEqual(1, searched.GetProperty("states_withheld_by_rights").GetInt32());
    }

    [TestMethod]
    public void AnExtractIsAWholeWordRunAroundTheFirstMatchWithEveryWholeMatchMarked()
    {
        static string Joined(V3CorpusMount.SearchExtract extract) => string.Concat(extract.Parts.Select(static part => part.Text));
        static string[] Marked(V3CorpusMount.SearchExtract extract) =>
            extract.Parts.Where(static part => part.Matched).Select(static part => part.Text).ToArray();

        // A short text is its own extract, every occurrence marked, nothing between two touching matches.
        var whole = V3CorpusMount.ExtractOf("le bail et le bail", ["bail"])!;
        Assert.AreEqual("le bail et le bail", Joined(whole));
        CollectionAssert.AreEqual(new[] { "bail", "bail" }, Marked(whole));
        Assert.AreEqual(0, whole.ByteStart);
        CollectionAssert.AreEqual(new[] { "aa", "aa" }, Marked(V3CorpusMount.ExtractOf("aaaa", ["aa"])!), "a needle's occurrences do not overlap");

        // A match inside a word is the bytes that matched: congé in congés.
        var inWord = V3CorpusMount.ExtractOf("Les congés payés", ["congé"])!;
        CollectionAssert.AreEqual(new[] { "Les ", "congé", "s payés" }, inWord.Parts.Select(static part => part.Text).ToArray());

        // Overlapping terms of a relaxed hit are one marked run; the run is no single term.
        CollectionAssert.AreEqual(new[] { "abcd" }, Marked(V3CorpusMount.ExtractOf("x abcd y", ["abc", "bcd"])!));

        // A long text is cut at whitespace on both sides, within the bounds.
        var words = string.Join(' ', Enumerable.Range(0, 200).Select(static index => $"mot{index:000}"));
        var text = words + " CIBLE " + words;
        var cut = V3CorpusMount.ExtractOf(text, ["CIBLE"])!;
        var at = text.IndexOf("CIBLE", StringComparison.Ordinal);
        Assert.IsTrue(at - cut.Start <= V3CorpusMount.SearchExtractBefore && cut.End - (at + 5) <= V3CorpusMount.SearchExtractAfter);
        Assert.IsTrue(char.IsWhiteSpace(text[cut.Start - 1]) && char.IsWhiteSpace(text[cut.End]));
        Assert.IsFalse(char.IsWhiteSpace(Joined(cut)[0]) || char.IsWhiteSpace(Joined(cut)[^1]));
        Assert.AreEqual(Joined(cut), text[cut.Start..cut.End]);

        // A word that alone passes the bound is cut at the bound; a match the run cuts is not marked.
        var unbroken = new string('x', 300) + "CIBLE" + new string('y', 300);
        var bounded = V3CorpusMount.ExtractOf(unbroken, ["CIBLE"])!;
        Assert.AreEqual(300 - V3CorpusMount.SearchExtractBefore, bounded.Start);
        Assert.AreEqual(305 + V3CorpusMount.SearchExtractAfter, bounded.End);
        var cutMatch = V3CorpusMount.ExtractOf("CIBLE X " + new string('z', 192) + " CIBLE X fin", ["CIBLE X"])!;
        CollectionAssert.AreEqual(new[] { "CIBLE X" }, Marked(cutMatch));
        StringAssert.EndsWith(Joined(cutMatch), " CIBLE", "the second match is cut by the run's end, and left unmarked");

        // Byte offsets count UTF-8 bytes, and a surrogate pair is never split.
        var accented = new string('é', 101) + "CIBLE";
        var bytes = V3CorpusMount.ExtractOf(accented, ["CIBLE"])!;
        Assert.AreEqual(Encoding.UTF8.GetByteCount(accented[..bytes.Start]), bytes.ByteStart);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(accented), bytes.ByteEnd);
        // The bound falls on the second half of a pair: "a", then pairs from index 1, so index 42 is a low surrogate.
        var astral = "a" + string.Concat(Enumerable.Repeat("\U0001D400", 60)) + "b" + "CIBLE";
        Assert.IsTrue(char.IsLowSurrogate(astral[astral.IndexOf("CIBLE", StringComparison.Ordinal) - V3CorpusMount.SearchExtractBefore]));
        var pair = V3CorpusMount.ExtractOf(astral, ["CIBLE"])!;
        Assert.IsFalse(char.IsLowSurrogate(astral[pair.Start]), "the run does not begin inside a surrogate pair");

        // No needle in the text is no extract.
        Assert.IsNull(V3CorpusMount.ExtractOf("rien", ["bail"]));
    }

    /// <summary>
    /// An extract is never cut at a no-break space (the review of #982, F3): the publisher sets "6 septembre" with U+00A0, and a
    /// bound falling on it would print "septembre 2021" without its day, or "le 6" without its month.
    /// </summary>
    [TestMethod]
    public void AnExtractIsNeverCutAtANoBreakSpace()
    {
        static string Joined(V3CorpusMount.SearchExtract extract) => string.Concat(extract.Parts.Select(static part => part.Text));

        // The bound before the match falls on the no-break space between the day and the month.
        const string head = "début le 6\u00A0septembre 2021 a";
        var noBreak = head.IndexOf('\u00A0', StringComparison.Ordinal);
        var filler = " " + new string('y', noBreak + V3CorpusMount.SearchExtractBefore - head.Length - 2) + " ";
        var before = head + filler + "CIBLE";
        Assert.AreEqual(noBreak + V3CorpusMount.SearchExtractBefore, before.IndexOf("CIBLE", StringComparison.Ordinal), "the test sets the bound on the no-break space");
        var cutBefore = Joined(V3CorpusMount.ExtractOf(before, ["CIBLE"])!);
        Assert.IsFalse(cutBefore.StartsWith("septembre", StringComparison.Ordinal), cutBefore);

        // The bound after the match falls on it too.
        var after = "CIBLE " + new string('z', 194) + " le 6\u00A0septembre fin";
        Assert.AreEqual('\u00A0', after[5 + V3CorpusMount.SearchExtractAfter], "the test sets the bound on the no-break space");
        var cutAfter = Joined(V3CorpusMount.ExtractOf(after, ["CIBLE"])!);
        Assert.IsFalse(cutAfter.EndsWith('6'), cutAfter);

        // A no-break space is no break; an ordinary space, a tab and a line break are.
        foreach (var space in new[] { '\u00A0', '\u202F', '\u2007' })
        {
            Assert.IsFalse(V3CorpusMount.IsExtractBreak(space), $"U+{(int)space:X4}");
        }

        foreach (var space in new[] { ' ', '\t', '\n' })
        {
            Assert.IsTrue(V3CorpusMount.IsExtractBreak(space), $"U+{(int)space:X4}");
        }
    }

    /// <summary>
    /// Two versions of one act on one date keep their hits apart (the review of #982, F1): an undated search serves both, and
    /// the hits of each come together, so a page says each version's date once above its own passages. Two acts on one page
    /// are each named by their own title (F2), and a title-ladder card names the work it links to, or none for an act this
    /// site holds no version of.
    /// </summary>
    [TestMethod]
    public async Task TwoVersionsOnOneDateKeepTheirHitsApartAndEachActAndCardNamesItsOwnWork()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var articles = fixture.ArticlesOfOwnState();
        var carrying = articles.Select(static article => article.PublisherId)
            .OrderBy(static id => id, Comparer<string>.Create(V3CorpusMount.CompareLuxembourgArticleIds))
            .Take(4)
            .ToArray();
        await fixture.RewriteArticleTextsAsync(fixture.ExpressionIri, carrying.ToDictionary(
            static id => id, id => "Art. " + id + ". La " + Phrase + " ne peut exceder trois mois.", StringComparer.Ordinal));

        // Two later states of the work on one date (each a copy of the first, with its own article identities), and a
        // sibling work's state, so the page holds two acts.
        const string twinDate = "2025-03-01";
        var later = await fixture.AddStateAsync(twinDate, "later");
        var twin = await fixture.AddStateAsync(twinDate, "twin");
        var sibling = await fixture.AddStateAsync(fixture.ApplicabilityDate, "fr", workLeaf: "n77");
        await fixture.AddTitleRowsAsync(
            (null, fixture.ExpressionIri, "fra", "title", "Règlement sur la profession d'avocat.", new string('a', 64)),
            ("sibling-work-identifier", sibling.ExpressionIri, "fra", "title", "Règlement sur les huissiers de justice.", new string('b', 64)),
            ("unheld-work-identifier", "http://data.legilux.public.lu/eli/etat/leg/loi/1900/01/01/n1/jo/fr", "fra", "title",
                "Règlement sur les notaires.", new string('c', 64)));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await V3CorpusSearchMountTests.SearchAsync(mount, Phrase, mode: "strict")).Result!.Value;
        var hits = body.GetProperty("hits").EnumerateArray().ToArray();
        Assert.AreEqual(16, hits.Length, "four articles in each of the work's three states and the sibling's one");

        // Within a work and a date, each state's hits form one run.
        foreach (var group in hits.GroupBy(static hit => (hit.GetProperty("work_key").GetString(), hit.GetProperty("applicability_date").GetString())))
        {
            var states = group.Select(static hit => hit.GetProperty("state_sha256").GetString()!).ToArray();
            var runs = states.Where((state, index) => index == 0 || state != states[index - 1]).ToArray();
            CollectionAssert.AllItemsAreUnique(runs, $"{group.Key}: the states' hits interleave: {string.Join(", ", states)}");
        }

        var onTwinDate = hits.Where(hit => hit.GetProperty("applicability_date").GetString() == twinDate)
            .Select(static hit => hit.GetProperty("state_sha256").GetString()).Distinct().ToArray();
        CollectionAssert.AreEquivalent(new[] { later.StateSha256, twin.StateSha256 }, onTwinDate);
        StringAssert.Contains(body.GetProperty("ranking").GetString(), "state digest, publisher article id (in the reading's order) and article identity");

        // Each act once, each named by its own title.
        var acts = body.GetProperty("acts").EnumerateArray().ToDictionary(
            static act => act.GetProperty("work_key").GetString()!, static act => act.GetProperty("title").GetString());
        Assert.AreEqual(2, acts.Count);
        Assert.AreEqual("Règlement sur la profession d'avocat.", acts[fixture.WorkKey]);
        Assert.AreEqual("Règlement sur les huissiers de justice.", acts[sibling.WorkKey]);

        // The ladder: the phrase begins three titles; two acts are held, each card naming its own work, and one is not.
        var ladder = (await V3CorpusSearchMountTests.SearchAsync(mount, "Règlement sur")).Result!.Value.GetProperty("work_resolution");
        if (ladder.ValueKind != JsonValueKind.Object)
        {
            Assert.Inconclusive("the phase read the phrase as a reference");
        }

        Assert.AreEqual("several_candidates", ladder.GetProperty("outcome").GetString(), ladder.GetRawText());
        var keys = ladder.GetProperty("candidates").EnumerateArray().ToDictionary(
            static card => card.GetProperty("matched_title").GetString()!,
            static card => card.GetProperty("work_key").ValueKind == JsonValueKind.Null ? null : card.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.WorkKey, keys["Règlement sur la profession d'avocat."]);
        Assert.AreEqual(sibling.WorkKey, keys["Règlement sur les huissiers de justice."]);
        Assert.IsNull(keys["Règlement sur les notaires."], "a title of a work this index holds no state of links nowhere");
    }
}
