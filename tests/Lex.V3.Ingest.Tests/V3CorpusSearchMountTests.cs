using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>search</c> driven through the real handler on a verified mount: the two lexical lanes the
/// mounted index can serve, resolver first. The lanes are sets and the relaxed set contains the strict
/// set: with no mode an article matching both is served once, as strict, and relaxed never outranks
/// strict; with a mode the answer is that lane's whole set and the other lane is null, not zero. The
/// answer says it is an order and not a rank, that matching is byte-exact, that a page is the first
/// hits and not the best, and what a hit counts; a mode the index cannot serve is refused and never
/// ignored; the query is bounded before any data is read; a named work on a date refuses as
/// <c>as_of</c> refuses, and across works an ambiguous work is listed and hides nothing.
/// </summary>
[TestClass]
public sealed class V3CorpusSearchMountTests
{
    private const string SearchRawTarget = "/api/v3/search";
    private const string AsOfRawTarget = "/api/v3/as_of";

    private const string Phrase = "garantie locative";
    private const string BothLanes = "La garantie locative ne peut exceder trois mois de loyer.";
    private const string TermsOnly = "Le bailleur restitue toute somme locative tenue en garantie.";
    private const string OnlyGarantie = "La garantie est restituee au locataire sortant.";
    private const string OnlyLocative = "Le loyer locative est du chaque mois.";
    private const string Neither = "Le present article ne dit rien du depot.";

    /// <summary>
    /// Sets the text of the articles of one expression, in the order <see cref="MountedFixture.ArticlesOfOwnState"/>
    /// lists them (the same publisher ids in every copy of the state); articles past the texts given hold
    /// text that matches nothing. Returns the publisher ids in that order.
    /// </summary>
    private static async Task<string[]> SetTextsAsync(MountedFixture fixture, string expressionIri, params string[] texts)
    {
        var articles = fixture.ArticlesOfOwnState();
        Assert.IsGreaterThanOrEqualTo(texts.Length, articles.Count, "The fixture's state holds too few articles for this test.");
        for (var index = 0; index < articles.Count; index++)
        {
            await fixture.RewriteArticleTextAsync(expressionIri, articles[index].PublisherId, index < texts.Length ? texts[index] : Neither);
        }

        return articles.Select(static article => article.PublisherId).ToArray();
    }

    private static async Task<(string Both, string Terms, string None)> WriteTextsAsync(MountedFixture fixture)
    {
        var ids = await SetTextsAsync(fixture, fixture.ExpressionIri, BothLanes, TermsOnly, Neither);
        return (ids[0], ids[1], ids[2]);
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(static v => v.GetString()!).ToArray();

    [TestMethod]
    public async Task RelaxedNeverOutranksStrictAndAnArticleMatchingBothIsServedOnceAsStrict()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (both, termsOnly, _) = await WriteTextsAsync(fixture);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await SearchAsync(mount, Phrase);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("search", envelope.OperationId);
        Assert.AreEqual("quote", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        var hits = body.GetProperty("hits").EnumerateArray().ToArray();
        Assert.AreEqual(2, hits.Length, body.GetRawText());

        // The article matching the phrase matches every term too. It is a strict hit and nothing else.
        Assert.AreEqual(both, hits[0].GetProperty("publisher_id").GetString());
        Assert.AreEqual("strict", hits[0].GetProperty("lane").GetString());
        CollectionAssert.AreEqual(new[] { "exact_phrase" }, Strings(hits[0].GetProperty("match_reasons")));
        Assert.AreEqual(termsOnly, hits[1].GetProperty("publisher_id").GetString());
        Assert.AreEqual("relaxed", hits[1].GetProperty("lane").GetString());
        CollectionAssert.AreEqual(new[] { "all_terms" }, Strings(hits[1].GetProperty("match_reasons")));
        Assert.AreEqual(1, hits.Count(hit => hit.GetProperty("publisher_id").GetString() == both), "Served once, never again as relaxed.");

        // What the answer says about itself, held against wording written here and not against the
        // constants that produce it: an order and not a rank, byte-exact, what a hit is, what a lane is.
        var ranking = body.GetProperty("ranking").GetString();
        StringAssert.StartsWith(ranking, "none;");
        StringAssert.Contains(ranking, "strict lane before relaxed lane");
        StringAssert.Contains(ranking, "work key, publisher date, article identity");
        StringAssert.Contains(ranking, "no BM25 ranker");
        StringAssert.Contains(ranking, "not the best hits");
        var matching = body.GetProperty("matching").GetString();
        StringAssert.Contains(matching, "byte-exact substring");
        StringAssert.Contains(matching, "the article's searchable text");
        StringAssert.Contains(matching, "text and reference tokens");
        StringAssert.Contains(matching, "modification markers and note references are not searched");
        StringAssert.Contains(matching, "case and diacritics are significant");
        StringAssert.Contains(matching, "nothing is folded, stemmed or expanded");
        StringAssert.Contains(body.GetProperty("hit_unit").GetString(), "one article of one held state, not a provision");
        var lanes = body.GetProperty("lanes").GetString();
        StringAssert.Contains(lanes, "the relaxed set contains the strict set");
        StringAssert.Contains(lanes, "each article once");
        StringAssert.Contains(lanes, "null in the population, not zero");
        Assert.AreEqual("the first hits in the stated order, not the best hits", body.GetProperty("page_is").GetString());
        CollectionAssert.AreEqual(new[] { "strict", "relaxed" }, Strings(body.GetProperty("modes_held")));
        CollectionAssert.AreEqual(new[] { "bm25", "semantic" }, Strings(body.GetProperty("modes_not_held")));
        Assert.IsFalse(body.GetRawText().Contains("\"score\"", StringComparison.Ordinal), "No score is served.");
        Assert.IsFalse(body.GetRawText().Contains("snippet", StringComparison.Ordinal), "No snippet is served.");
        Assert.IsFalse(body.GetRawText().Contains(BothLanes, StringComparison.Ordinal), "No article text is served.");
        CollectionAssert.AreEqual(new[] { "garantie", "locative" }, Strings(body.GetProperty("terms")));

        // The echoes a caller checks its own request against.
        Assert.AreEqual(Phrase, body.GetProperty("requested_query").GetString());
        Assert.AreEqual("fra", body.GetProperty("requested_language").GetString());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("requested_after").ValueKind);

        var population = body.GetProperty("population");
        Assert.IsTrue(body.GetProperty("searchable_text_held_for_language").GetBoolean());
        CollectionAssert.AreEqual(new[] { "fra" }, Strings(body.GetProperty("searchable_languages")));
        Assert.AreEqual(1, population.GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(1, population.GetProperty("relaxed_hits").GetInt32());
        Assert.AreEqual(2, population.GetProperty("distinct_publisher_articles").GetInt32());
        Assert.AreEqual(1, population.GetProperty("works_with_hits").GetInt32());
        Assert.AreEqual(200, body.GetProperty("limit").GetInt32(), "The default limit is the ceiling.");
        Assert.IsFalse(body.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("continue_after").ValueKind);

        // A hit names its state, and its pointer reaches that state and no other.
        Assert.AreEqual(fixture.StateSha256, hits[0].GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.WorkKey, hits[0].GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, hits[0].GetProperty("applicability_date").GetString());
        var resolved = await PostOkAsync(mount, "/api/v3/resolve", "resolve", hits[0].GetProperty("resolve"));
        Assert.AreEqual(V3Verdicts.Answer, resolved.Verdict);
        StringAssert.Contains(resolved.Result!.Value.GetRawText(), fixture.StateSha256);

        // The lanes are sets. The strict lane alone is the phrase set; the relaxed lane alone is every
        // article holding every term, which contains the phrase set, so the article matching both is in
        // it too. The lane not asked for is not counted: null, never zero.
        var strictOnly = (await SearchAsync(mount, Phrase, mode: "strict")).Result!.Value;
        Assert.AreEqual(both, strictOnly.GetProperty("hits").EnumerateArray().Single().GetProperty("publisher_id").GetString());
        Assert.AreEqual(1, strictOnly.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, strictOnly.GetProperty("population").GetProperty("relaxed_hits").ValueKind);

        var relaxedOnly = (await SearchAsync(mount, Phrase, mode: "relaxed")).Result!.Value;
        var relaxedHits = relaxedOnly.GetProperty("hits").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(new[] { both, termsOnly }, relaxedHits.Select(static hit => hit.GetProperty("publisher_id").GetString()).ToArray());
        Assert.IsTrue(relaxedHits.All(static hit => hit.GetProperty("lane").GetString() == "relaxed"));
        Assert.IsTrue(relaxedHits.All(static hit => Strings(hit.GetProperty("match_reasons")).SequenceEqual(new[] { "all_terms" })));
        Assert.AreEqual(2, relaxedOnly.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, relaxedOnly.GetProperty("population").GetProperty("strict_hits").ValueKind,
            "A count of a lane that was never scanned is not zero.");
        Assert.IsTrue(relaxedOnly.GetProperty("searchable_text_held_for_language").GetBoolean());
    }

    [TestMethod]
    public async Task MatchingIsByteExactAndNoHitIsAnAnswer()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (both, termsOnly, _) = await WriteTextsAsync(fixture);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // Case is significant and nothing is folded: the capitalised phrase is in no article.
        var capitalised = await SearchAsync(mount, "Garantie Locative");
        Assert.AreEqual(V3Verdicts.Answer, capitalised.Verdict, "No hit is an answer, never a refusal.");
        var body = capitalised.Result!.Value;
        Assert.AreEqual(0, body.GetProperty("hits").GetArrayLength());
        Assert.AreEqual(0, body.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(0, body.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
        // The fixture holds no work titles here, and the resolver says that rather than "no match".
        Assert.AreEqual("no_titles_held", body.GetProperty("work_resolution").GetProperty("outcome").GetString());

        // A query that is one word has the strict set as its relaxed set: with no mode the second scan
        // adds nothing, and with the relaxed mode the word is found, not hidden behind the strict lane.
        var oneTerm = (await SearchAsync(mount, "locative")).Result!.Value;
        Assert.AreEqual(2, oneTerm.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(0, oneTerm.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
        var oneTermRelaxed = (await SearchAsync(mount, "locative", mode: "relaxed")).Result!.Value;
        CollectionAssert.AreEquivalent(new[] { both, termsOnly },
            oneTermRelaxed.GetProperty("hits").EnumerateArray().Select(static hit => hit.GetProperty("publisher_id").GetString()).ToArray());

        // One word with a space after it is not the word as typed. Strict finds the articles with the
        // space; the relaxed lane still runs and adds the article that has the word and no space after it.
        var trailingSpace = (await SearchAsync(mount, "garantie ")).Result!.Value.GetProperty("hits").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { both, termsOnly }, trailingSpace.Select(static hit => hit.GetProperty("publisher_id").GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { "strict", "relaxed" }, trailingSpace.Select(static hit => hit.GetProperty("lane").GetString()).ToArray());

        // A term repeated in the query is one term.
        var repeated = (await SearchAsync(mount, "locative  garantie locative")).Result!.Value;
        CollectionAssert.AreEqual(new[] { "locative", "garantie" }, Strings(repeated.GetProperty("terms")));
        Assert.AreEqual(0, repeated.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(2, repeated.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
    }

    [TestMethod]
    public async Task DiacriticsAreSignificantInBothDirections()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var ids = await SetTextsAsync(fixture, fixture.ExpressionIri, "Le montant égal est dû.", "Le montant egal est du.", Neither);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        static string[] PublisherIds(JsonElement body) =>
            body.GetProperty("hits").EnumerateArray().Select(static hit => hit.GetProperty("publisher_id").GetString()!).ToArray();

        // The accented spelling finds the accented text only, and the plain spelling the plain text only.
        CollectionAssert.AreEqual(new[] { ids[0] }, PublisherIds((await SearchAsync(mount, "égal")).Result!.Value));
        CollectionAssert.AreEqual(new[] { ids[1] }, PublisherIds((await SearchAsync(mount, "egal")).Result!.Value));
        CollectionAssert.AreEqual(new[] { ids[0] }, PublisherIds((await SearchAsync(mount, "dû")).Result!.Value));
        CollectionAssert.AreEqual(Array.Empty<string>(), PublisherIds((await SearchAsync(mount, "Égal")).Result!.Value));
    }

    [TestMethod]
    public async Task TheResolverRunsFirstAndAPluralTitleMatchPicksNone()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddWorkTitleAsync();
        using (var oneTitle = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(oneTitle);
            var byTitle = (await SearchAsync(oneTitle, fixture.WorkTitle.ToUpperInvariant() + "...")).Result!.Value.GetProperty("work_resolution");
            Assert.AreEqual("r1_work_discovery", byTitle.GetProperty("retrieval_lane").GetString());
            Assert.AreEqual("one_work", byTitle.GetProperty("outcome").GetString());
            Assert.AreEqual("exact_normalized_title", byTitle.GetProperty("work").GetProperty("match_reason").GetString());
            Assert.AreEqual(fixture.PublisherWid, byTitle.GetProperty("work").GetProperty("work_identifier").GetString());
            Assert.AreEqual(JsonValueKind.Null, byTitle.GetProperty("candidates").ValueKind);

            var noMatch = (await SearchAsync(oneTitle, "ordinary unknown words")).Result!.Value.GetProperty("work_resolution");
            Assert.AreEqual("no_title_match", noMatch.GetProperty("outcome").GetString());
            Assert.AreEqual(JsonValueKind.Null, noMatch.GetProperty("work").ValueKind);
        }

        await fixture.AddTwoWorkTitlesAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var plural = (await SearchAsync(mount, "Reglement sur l'epreuve")).Result!.Value.GetProperty("work_resolution");
        Assert.AreEqual("several_candidates", plural.GetProperty("outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, plural.GetProperty("work").ValueKind, "Several candidates are listed and none is picked.");
        Assert.AreEqual(2, plural.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public async Task WithAWorkNamedTheTitleLadderIsNotRunAndNoOtherWorksCardSitsAboveItsHits()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddTwoWorkTitlesAsync();
        // The second work's exact title, written into an article of the first: the ordinary search of a
        // lawyer looking for where one law is cited inside another.
        var otherTitle = fixture.WorkTitle + " alpha";
        await SetTextsAsync(fixture, fixture.ExpressionIri, "Voir " + otherTitle + " a l'article 3.", Neither, Neither);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        // Without a work named the ladder resolves the title, and the card is the other work's.
        var open = (await SearchAsync(mount, otherTitle)).Result!.Value.GetProperty("work_resolution");
        Assert.AreEqual("one_work", open.GetProperty("outcome").GetString());
        Assert.AreNotEqual(fixture.PublisherWid, open.GetProperty("work").GetProperty("work_identifier").GetString());

        // With the first work named, the same query is a search inside it: the ladder is not run, no card
        // for the other work sits above the hits, and the hits are the named work's.
        var named = (await SearchAsync(mount, otherTitle, identifier: identifier)).Result!.Value;
        var resolution = named.GetProperty("work_resolution");
        Assert.AreEqual("r1_work_discovery", resolution.GetProperty("retrieval_lane").GetString());
        Assert.AreEqual("not_run_identifier_given", resolution.GetProperty("outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, resolution.GetProperty("work").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, resolution.GetProperty("candidates").ValueKind);
        var hits = named.GetProperty("hits").EnumerateArray().ToArray();
        Assert.IsTrue(hits.Length > 0);
        Assert.IsTrue(hits.All(hit => hit.GetProperty("work_key").GetString() == fixture.WorkKey));
        // How a reader leaves the platform for the official document: the publisher's work IRI of the
        // hit's state, as the index holds it, and the publisher's own work id of the article.
        foreach (var hit in hits)
        {
            var workIri = PublisherWorkIriOf(fixture, hit.GetProperty("state_sha256").GetString()!);
            Assert.IsFalse(string.IsNullOrEmpty(workIri));
            Assert.AreEqual(workIri, hit.GetProperty("publisher_work_iri").GetString());
            Assert.AreEqual(fixture.PublisherWid, hit.GetProperty("publisher_wid").GetString());
        }

        // A title that would be several candidates is no way around it: no candidates are listed either.
        var plural = (await SearchAsync(mount, "Reglement sur l'epreuve", identifier: identifier)).Result!.Value.GetProperty("work_resolution");
        Assert.AreEqual("not_run_identifier_given", plural.GetProperty("outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, plural.GetProperty("candidates").ValueKind);
    }

    [TestMethod]
    public async Task WithoutADateEveryHeldStateIsSearchedAndTheAnswerSaysWhatAHitCounts()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (both, _, _) = await WriteTextsAsync(fixture);
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // The unchanged article is a hit in each state that holds it, and the count says it is one.
        var everyState = (await SearchAsync(mount, Phrase, mode: "strict")).Result!.Value;
        var hits = everyState.GetProperty("hits").EnumerateArray().ToArray();
        Assert.AreEqual(2, hits.Length);
        CollectionAssert.AreEqual(new[] { fixture.ApplicabilityDate, laterDate },
            hits.Select(static hit => hit.GetProperty("applicability_date").GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { fixture.StateSha256, later.StateSha256 },
            hits.Select(static hit => hit.GetProperty("state_sha256").GetString()).ToArray());
        Assert.IsTrue(hits.All(hit => hit.GetProperty("publisher_id").GetString() == both));
        Assert.AreEqual(2, everyState.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(1, everyState.GetProperty("population").GetProperty("distinct_publisher_articles").GetInt32());
        // Two dated states, one language: the searchable languages are listed once each, not once per date.
        CollectionAssert.AreEqual(new[] { "fra" }, Strings(everyState.GetProperty("searchable_languages")));
        // Each hit's pointer names its own state, on a fixture where two states hold the same article.
        foreach (var hit in hits)
        {
            StringAssert.EndsWith(hit.GetProperty("resolve").GetProperty("identifier").GetString(), hit.GetProperty("state_sha256").GetString());
        }

        // With a date, only the state as_of selects contributes, and the population counts that, not the whole.
        var onFirst = (await SearchAsync(mount, Phrase, mode: "strict", date: Shift(laterDate, -1))).Result!.Value;
        Assert.AreEqual(fixture.StateSha256, onFirst.GetProperty("hits").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        Assert.AreEqual(1, onFirst.GetProperty("population").GetProperty("strict_hits").GetInt32());
        var onLater = (await SearchAsync(mount, Phrase, mode: "strict", date: laterDate)).Result!.Value;
        Assert.AreEqual(later.StateSha256, onLater.GetProperty("hits").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        var before = (await SearchAsync(mount, Phrase, date: Shift(fixture.ApplicabilityDate, -1))).Result!.Value;
        Assert.AreEqual(0, before.GetProperty("hits").GetArrayLength(), "Across works, nothing applicable is an answer with no hits.");
        Assert.AreEqual(0, before.GetProperty("ambiguous_works").GetArrayLength());
        Assert.AreEqual(0, before.GetProperty("population").GetProperty("strict_hits").GetInt32());
    }

    [TestMethod]
    public async Task TheHitOrderIsLaneWorkKeyDateAndArticleAndTheAllTermsSetNeedsEveryTerm()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // Two works of two states each, their dates interleaved, so that ordering by date first, by work
        // key descending, or by article before date each gives a different sequence from the stated one
        // whichever of the two work keys sorts first.
        var d1 = fixture.ApplicabilityDate;
        var a2 = await fixture.AddStateAsync(Shift(d1, 400), "a-later");
        var b1 = await fixture.AddStateAsync(d1, "b-first", workLeaf: "n4");
        var b2 = await fixture.AddStateAsync(Shift(d1, 200), "b-later", b1.ExpressionIri);
        // Each state's three articles: a phrase match, a match of both terms without the phrase, an
        // article that holds one term only (which no lane may serve), and every other article matching nothing.
        await SetTextsAsync(fixture, fixture.ExpressionIri, BothLanes, TermsOnly, OnlyGarantie);
        await SetTextsAsync(fixture, a2.ExpressionIri, OnlyLocative, BothLanes, TermsOnly);
        await SetTextsAsync(fixture, b1.ExpressionIri, BothLanes, OnlyLocative, TermsOnly);
        await SetTextsAsync(fixture, b2.ExpressionIri, TermsOnly, OnlyGarantie, BothLanes);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        static (string Lane, string Work, string Date, string Article) OrderKey(JsonElement hit) => (
            hit.GetProperty("lane").GetString()!,
            hit.GetProperty("work_key").GetString()!,
            hit.GetProperty("applicability_date").GetString()!,
            hit.GetProperty("article_identity_sha256").GetString()!);

        static IEnumerable<(string Lane, string Work, string Date, string Article)> InStatedOrder(
            IEnumerable<(string Lane, string Work, string Date, string Article)> keys) =>
            keys.OrderBy(static key => key.Lane == "strict" ? 0 : 1)
                .ThenBy(static key => key.Work, StringComparer.Ordinal)
                .ThenBy(static key => key.Date, StringComparer.Ordinal)
                .ThenBy(static key => key.Article, StringComparer.Ordinal);

        var whole = (await SearchAsync(mount, Phrase)).Result!.Value;
        var served = whole.GetProperty("hits").EnumerateArray().ToArray();

        // Four phrase matches then four both-terms matches; the one-term articles are in neither.
        CollectionAssert.AreEqual(
            new[] { "strict", "strict", "strict", "strict", "relaxed", "relaxed", "relaxed", "relaxed" },
            served.Select(static hit => hit.GetProperty("lane").GetString()).ToArray());
        var byState = served.ToLookup(static hit => hit.GetProperty("state_sha256").GetString()!);
        Assert.AreEqual(4, byState.Count, "Every state has hits.");
        Assert.IsTrue(byState.All(static group => group.Count() == 2), "Two hits in each state: one per lane, never the one-term article.");
        Assert.AreEqual(2, served.Select(static hit => hit.GetProperty("work_key").GetString()).Distinct().Count());
        // Three dates, not four: the two works both begin on the first date, which is what interleaves them.
        Assert.AreEqual(3, served.Select(static hit => hit.GetProperty("applicability_date").GetString()).Distinct().Count());

        // The order is the stated one: lane, work key, publisher date, article identity.
        var keys = served.Select(OrderKey).ToArray();
        CollectionAssert.AreEqual(InStatedOrder(keys).ToArray(), keys, "Served in the order the answer says.");
        // The interleaving is real: the same hits by date first are a different sequence.
        CollectionAssert.AreNotEqual(
            keys.Where(static key => key.Lane == "strict").OrderBy(static key => key.Date, StringComparer.Ordinal).ThenBy(static key => key.Work, StringComparer.Ordinal).ToArray(),
            keys.Where(static key => key.Lane == "strict").ToArray());

        // Each hit's pointer names its own state, on a fixture with four states.
        foreach (var hit in served)
        {
            StringAssert.EndsWith(hit.GetProperty("resolve").GetProperty("identifier").GetString(), hit.GetProperty("state_sha256").GetString());
        }

        // What a hit counts: eight article-state hits, five provisions (by work and the publisher's own
        // article id: three in the first work, two in the second), never the eight article identities.
        var population = whole.GetProperty("population");
        Assert.AreEqual(4, population.GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(4, population.GetProperty("relaxed_hits").GetInt32());
        Assert.AreEqual(5, population.GetProperty("distinct_publisher_articles").GetInt32());
        Assert.AreEqual(2, population.GetProperty("works_with_hits").GetInt32());

        // The lanes alone: the phrase set, and the whole all-terms set in the same stated order.
        var strictKeys = (await SearchAsync(mount, Phrase, mode: "strict")).Result!.Value.GetProperty("hits").EnumerateArray().Select(OrderKey).ToArray();
        Assert.AreEqual(4, strictKeys.Length);
        CollectionAssert.AreEqual(InStatedOrder(strictKeys).ToArray(), strictKeys);
        var relaxedKeys = (await SearchAsync(mount, Phrase, mode: "relaxed")).Result!.Value.GetProperty("hits").EnumerateArray().Select(OrderKey).ToArray();
        Assert.AreEqual(8, relaxedKeys.Length, "Every article holding both terms, the phrase matches included, and no article holding one.");
        Assert.IsTrue(relaxedKeys.All(static key => key.Lane == "relaxed"));
        CollectionAssert.AreEqual(InStatedOrder(relaxedKeys).ToArray(), relaxedKeys);
    }

    [TestMethod]
    public async Task AcrossWorksAnAmbiguousWorkIsListedAndHidesNothingAndANamedWorkRefusesAsAsOfRefuses()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await WriteTextsAsync(fixture);
        var twinDate = Shift(fixture.ApplicabilityDate, 400);
        var asked = Shift(twinDate, 10);
        var later = await fixture.AddStateAsync(twinDate, "later");
        var twin = await fixture.AddStateAsync(twinDate, "twin");
        var other = await fixture.AddStateAsync(fixture.ApplicabilityDate, "other-work", workLeaf: "n4");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        // The question about many works: the ambiguous work contributes no hits, names both
        // candidates and picks neither, and the other work's hits are still served.
        var across = await SearchAsync(mount, Phrase, mode: "strict", date: asked);
        Assert.AreEqual(V3Verdicts.Answer, across.Verdict, "One ambiguous work does not silence the others.");
        var body = across.Result!.Value;
        var hit = body.GetProperty("hits").EnumerateArray().Single();
        Assert.AreEqual(other.WorkKey, hit.GetProperty("work_key").GetString());
        Assert.AreEqual(other.StateSha256, hit.GetProperty("state_sha256").GetString());
        Assert.AreEqual(1, body.GetProperty("population").GetProperty("strict_hits").GetInt32(), "The population counts what applies on the date.");
        var ambiguous = body.GetProperty("ambiguous_works").EnumerateArray().Single();
        Assert.AreEqual(fixture.WorkKey, ambiguous.GetProperty("work_key").GetString());
        Assert.AreEqual("ambiguous_version", ambiguous.GetProperty("reason").GetString());
        var candidates = Strings(ambiguous.GetProperty("candidates"));
        Assert.AreEqual(2, candidates.Length);
        CollectionAssert.AreEqual(candidates.Order(StringComparer.Ordinal).ToArray(), candidates);
        Assert.AreEqual(1, candidates.Count(candidate => candidate.EndsWith(later.StateSha256, StringComparison.Ordinal)));
        Assert.AreEqual(1, candidates.Count(candidate => candidate.EndsWith(twin.StateSha256, StringComparison.Ordinal)));

        // One named work on one date is as_of's question, and so are its refusals, byte for byte.
        var named = await SearchAsync(mount, Phrase, identifier: identifier, date: asked);
        Assert.AreEqual(V3Verdicts.Refuse, named.Verdict);
        Assert.AreEqual("ambiguous_version", named.Refusal!.Code);
        var asOfTwins = await AsOfAsync(mount, identifier, asked);
        Assert.AreEqual("ambiguous_version", asOfTwins.Refusal!.Code);
        Assert.AreEqual(asOfTwins.Refusal.HelpfulPayload.GetRawText(), named.Refusal.HelpfulPayload.GetRawText());

        var early = Shift(fixture.ApplicabilityDate, -1);
        var namedBefore = await SearchAsync(mount, Phrase, identifier: identifier, date: early);
        Assert.AreEqual("no_version_for_date", namedBefore.Refusal!.Code);
        var asOfBefore = await AsOfAsync(mount, identifier, early);
        Assert.AreEqual(asOfBefore.Refusal!.HelpfulPayload.GetRawText(), namedBefore.Refusal.HelpfulPayload.GetRawText());

        // The boundary: on the first date the named work holds, it answers with that state's hits.
        var namedOnFirst = await SearchAsync(mount, Phrase, identifier: identifier, date: fixture.ApplicabilityDate, mode: "strict");
        Assert.AreEqual(V3Verdicts.Answer, namedOnFirst.Verdict);
        Assert.AreEqual(fixture.StateSha256,
            namedOnFirst.Result!.Value.GetProperty("hits").EnumerateArray().Single().GetProperty("state_sha256").GetString());

        // With a work named and no date, only that work's states are searched.
        var namedNoDate = (await SearchAsync(mount, Phrase, identifier: identifier, mode: "strict")).Result!.Value;
        Assert.IsTrue(namedNoDate.GetProperty("hits").EnumerateArray()
            .All(row => row.GetProperty("work_key").GetString() == fixture.WorkKey));
        Assert.AreEqual(3, namedNoDate.GetProperty("hits").GetArrayLength());
    }

    [TestMethod]
    public async Task ANamedWorksTwinsAreJudgedInTheLanguageSearched()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await WriteTextsAsync(fixture);
        foreach (var article in fixture.ArticlesOfOwnState())
        {
            await fixture.RewriteArticleTextAsync(german.ExpressionIri, article.PublisherId, string.Empty);
        }

        var twinDate = Shift(fixture.ApplicabilityDate, 400);
        var asked = Shift(twinDate, 10);
        await fixture.AddStateAsync(twinDate, "de-later", german.ExpressionIri);
        await fixture.AddStateAsync(twinDate, "de-twin", german.ExpressionIri);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        // German has twins on the date and French has one state: French answers, German refuses.
        var french = await SearchAsync(mount, Phrase, identifier: identifier, date: asked, mode: "strict");
        Assert.AreEqual(V3Verdicts.Answer, french.Verdict, "Another language's twins do not refuse the language searched.");
        Assert.AreEqual(fixture.StateSha256,
            french.Result!.Value.GetProperty("hits").EnumerateArray().Single().GetProperty("state_sha256").GetString());

        // German is held as states and its articles carry no text here, so the capability manifest
        // measured no searchable German text. That is an answer that says so, not zero hits passed off
        // as a search that ran, in either lane and whichever lane is scanned first.
        foreach (var mode in new string?[] { null, "strict", "relaxed" })
        {
            var unmeasured = (await SearchAsync(mount, Phrase, language: "deu", mode: mode)).Result!.Value;
            Assert.IsFalse(unmeasured.GetProperty("searchable_text_held_for_language").GetBoolean(), $"mode {mode}");
            Assert.AreEqual(0, unmeasured.GetProperty("hits").GetArrayLength());
            // The way out: German is held as states and is not searchable, and the answer names the
            // language that is, so a caller who asked in German learns that French is.
            CollectionAssert.AreEqual(new[] { "fra" }, Strings(unmeasured.GetProperty("searchable_languages")), $"mode {mode}");
        }

        var germanSearch = await SearchAsync(mount, Phrase, identifier: identifier, date: asked, language: "deu");
        Assert.AreEqual("ambiguous_version", germanSearch.Refusal!.Code);
        var asOfGerman = await AsOfAsync(mount, identifier, asked, "deu");
        Assert.AreEqual(asOfGerman.Refusal!.HelpfulPayload.GetRawText(), germanSearch.Refusal.HelpfulPayload.GetRawText());
    }

    [TestMethod]
    public async Task TheSearchIsInTheLanguageAskedForAndAGermanArticleIsNeverAFrenchHit()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // The German state copies the French text while the index holds the one state alone.
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await WriteTextsAsync(fixture);
        // Each German article holds the French phrase too, so a search that forgot the language would
        // return it, and a German word that no French article holds.
        var germanTexts = new[]
        {
            "Die garantie locative ist die Mietkaution eins.",
            "Die garantie locative ist die Mietkaution zwei.",
            "Die garantie locative ist die Mietkaution drei.",
        };
        await SetTextsAsync(fixture, german.ExpressionIri, germanTexts);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var mode in new string?[] { null, "strict", "relaxed" })
        {
            var french = (await SearchAsync(mount, Phrase, mode: mode)).Result!.Value;
            Assert.IsTrue(french.GetProperty("hits").EnumerateArray().All(hit => hit.GetProperty("state_sha256").GetString() == fixture.StateSha256),
                $"A French search serves French states only, mode {mode}.");
            Assert.IsTrue(french.GetProperty("hits").GetArrayLength() > 0);
        }

        var germanWord = (await SearchAsync(mount, "Mietkaution", language: "deu")).Result!.Value;
        var germanHits = germanWord.GetProperty("hits").EnumerateArray().ToArray();
        Assert.AreEqual(3, germanHits.Length);
        Assert.IsTrue(germanHits.All(hit => hit.GetProperty("state_sha256").GetString() == german.StateSha256));
        Assert.IsTrue(germanHits.All(hit => hit.GetProperty("language").GetString() == "deu"));
        Assert.IsTrue(germanWord.GetProperty("searchable_text_held_for_language").GetBoolean());
        CollectionAssert.AreEqual(new[] { "deu", "fra" }, Strings(germanWord.GetProperty("searchable_languages")));

        var frenchOfGerman = (await SearchAsync(mount, "Mietkaution")).Result!.Value;
        Assert.AreEqual(0, frenchOfGerman.GetProperty("hits").GetArrayLength(), "A German word is in no French article.");
        var germanOfFrench = (await SearchAsync(mount, "trois mois de loyer", language: "deu")).Result!.Value;
        Assert.AreEqual(0, germanOfFrench.GetProperty("hits").GetArrayLength(), "A French phrase that only French articles hold is in no German hit.");
    }

    [TestMethod]
    public async Task ALanguageThatHoldsATitleAndNoSearchableTextIsNotOneASearchCanBeAskedIn()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // German is held as a state and as a title, and its articles carry no text: the index measured
        // a title cell for German and no search cell, so listing every language with any cell would
        // send a caller to a language that answers nothing.
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await WriteTextsAsync(fixture);
        foreach (var article in fixture.ArticlesOfOwnState())
        {
            await fixture.RewriteArticleTextAsync(german.ExpressionIri, article.PublisherId, string.Empty);
        }

        await fixture.AddWorkTitleInLanguageAsync(german.ExpressionIri, "deu", "Gesetz ueber die Mietkaution");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var inGerman = (await SearchAsync(mount, Phrase, language: "deu")).Result!.Value;
        Assert.IsFalse(inGerman.GetProperty("searchable_text_held_for_language").GetBoolean());
        CollectionAssert.AreEqual(new[] { "fra" }, Strings(inGerman.GetProperty("searchable_languages")));
        var inFrench = (await SearchAsync(mount, Phrase)).Result!.Value;
        Assert.IsTrue(inFrench.GetProperty("searchable_text_held_for_language").GetBoolean());
        CollectionAssert.AreEqual(new[] { "fra" }, Strings(inFrench.GetProperty("searchable_languages")));
    }

    [TestMethod]
    public async Task ThePageIsBoundedAndTheCursorNeitherRepeatsNorSkipsAHitAcrossTheTwoLanes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await WriteTextsAsync(fixture);
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        await fixture.AddStateAsync(fixture.ApplicabilityDate, "other-work", workLeaf: "n4");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var whole = (await SearchAsync(mount, Phrase)).Result!.Value;
        var all = whole.GetProperty("hits").EnumerateArray().Select(CursorKey).ToArray();
        Assert.AreEqual(6, all.Length, "Three states, each with one strict and one relaxed hit.");
        CollectionAssert.AreEqual(new[] { "strict", "strict", "strict", "relaxed", "relaxed", "relaxed" },
            whole.GetProperty("hits").EnumerateArray().Select(static hit => hit.GetProperty("lane").GetString()).ToArray());

        var pages = new List<string[]>();
        string? after = null;
        do
        {
            // The loop is bounded: a cursor that repeats a hit must fail here and not run forever.
            Assert.IsLessThanOrEqualTo(all.Length, pages.Count, "The cursor did not advance.");
            var page = (await SearchAsync(mount, Phrase, limit: 2, after: after)).Result!.Value;
            var keys = page.GetProperty("hits").EnumerateArray().Select(CursorKey).ToArray();
            Assert.IsLessThanOrEqualTo(2, keys.Length);
            pages.Add(keys);
            // The population is the scope asked for, not the page.
            Assert.AreEqual(3, page.GetProperty("population").GetProperty("strict_hits").GetInt32());
            Assert.AreEqual(3, page.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
            // The page echoes the cursor it was asked from, which is how a paging client checks its own.
            if (after is null)
            {
                Assert.AreEqual(JsonValueKind.Null, page.GetProperty("requested_after").ValueKind);
            }
            else
            {
                Assert.AreEqual(after, page.GetProperty("requested_after").GetString());
            }

            after = page.GetProperty("truncated").GetBoolean() ? page.GetProperty("continue_after").GetString() : null;
            Assert.AreEqual(page.GetProperty("truncated").GetBoolean(), page.GetProperty("continue_after").ValueKind == JsonValueKind.String);
        }
        while (after is not null);

        Assert.AreEqual(3, pages.Count);
        CollectionAssert.AreEqual(all, pages.SelectMany(static page => page).ToArray(), "Neither repeated nor skipped, in the same order.");

        // A limit that exactly fills is not truncated; the ceiling is accepted and echoed; one hit is a page.
        var exact = (await SearchAsync(mount, Phrase, limit: 6)).Result!.Value;
        Assert.IsFalse(exact.GetProperty("truncated").GetBoolean());
        var ceiling = (await SearchAsync(mount, Phrase, limit: 200)).Result!.Value;
        Assert.AreEqual(200, ceiling.GetProperty("limit").GetInt32());
        var one = (await SearchAsync(mount, Phrase, limit: 1)).Result!.Value;
        Assert.AreEqual(1, one.GetProperty("hits").GetArrayLength());
        Assert.IsTrue(one.GetProperty("truncated").GetBoolean());
        // A cursor naming no hit is a schema rejection.
        AssertTransportProblem(
            await PostAsync(mount, SearchRawTarget, JsonSerializer.Serialize(new
            {
                operation_id = "search",
                parameters = new { query = Phrase, language = "fra", after = "strict.nothing.nothing" },
            })),
            "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task TheQueryIsBoundedAsShapeBeforeAnyDataIsReadAndCountsCharactersAsCodePoints()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await WriteTextsAsync(fixture);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        async Task<DefaultHttpContext> Post(string query, string? identifier = null)
        {
            var parameters = new Dictionary<string, object> { ["query"] = query, ["language"] = "fra" };
            if (identifier is not null)
            {
                parameters["identifier"] = identifier;
            }

            return await PostAsync(mount, SearchRawTarget, JsonSerializer.Serialize(new { operation_id = "search", parameters }));
        }

        // The ceiling on characters is 512 code points: 512 answer, 513 are the request document's rejection.
        Assert.AreEqual(StatusCodes.Status200OK, (await Post(new string('a', 512))).Response.StatusCode);
        AssertTransportProblem(await Post(new string('a', 513)), "request_schema_invalid", StatusCodes.Status400BadRequest);
        // Characters outside the basic plane are two UTF-16 units and one code point: 512 of them is within the ceiling.
        var astral = string.Concat(Enumerable.Repeat("\U0001F4DC", 512));
        Assert.AreEqual(1024, astral.Length);
        Assert.AreEqual(StatusCodes.Status200OK, (await Post(astral)).Response.StatusCode, "512 code points, 1,024 UTF-16 units.");
        AssertTransportProblem(await Post(astral + "\U0001F4DC"), "request_schema_invalid", StatusCodes.Status400BadRequest);

        // Each distinct term is one clause of an AND chain: 32 distinct terms answer and 33 are rejected,
        // and a repeated term is not a new one.
        static string Terms(int count) => string.Join(' ', Enumerable.Range(0, count).Select(index => "t" + index.ToString(CultureInfo.InvariantCulture)));
        Assert.AreEqual(StatusCodes.Status200OK, (await Post(Terms(32))).Response.StatusCode);
        Assert.AreEqual(StatusCodes.Status200OK, (await Post(Terms(32) + " " + Terms(32))).Response.StatusCode, "Sixty-four terms, thirty-two of them distinct.");
        AssertTransportProblem(await Post(Terms(33)), "request_schema_invalid", StatusCodes.Status400BadRequest);

        // It is a shape check: it is judged before the work is looked up, so an unknown work does not hide it.
        AssertTransportProblem(await Post(Terms(33), "/lu-legilux/no-such-work"), "request_schema_invalid", StatusCodes.Status400BadRequest);

        // A query of over a thousand distinct terms once reached SQLite's expression-depth limit and came
        // back as a server error. It is a rejection now, and nothing reaches the index.
        AssertTransportProblem(await Post(Terms(1200)), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task AModeTheIndexCannotServeIsRefusedAndTheOtherFamiliesRefuseAsElsewhere()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            foreach (var mode in new[] { "bm25", "semantic", "relevance", "Strict" })
            {
                var refused = await SearchAsync(mount, Phrase, mode: mode);
                Assert.AreEqual(V3Verdicts.Refuse, refused.Verdict, mode);
                Assert.AreEqual("retrieval_mode_unavailable", refused.Refusal!.Code);
                Assert.AreEqual(mode, refused.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
                CollectionAssert.AreEqual(new[] { "strict", "relaxed" }, Strings(refused.Refusal.HelpfulPayload.GetProperty("available_modes")));
                Assert.AreEqual(PublisherId.LuLegilux, refused.Context.Publisher);
            }

            var english = await SearchAsync(mount, Phrase, language: "eng");
            Assert.AreEqual("language_not_available", english.Refusal!.Code);
            CollectionAssert.AreEqual(new[] { "fra" }, Strings(english.Refusal.HelpfulPayload.GetProperty("available_languages")));
            var englishOfOneWork = await SearchAsync(mount, Phrase, identifier: $"/lu-legilux/{fixture.WorkKey}", language: "eng");
            Assert.AreEqual("language_not_available", englishOfOneWork.Refusal!.Code);
            var unknown = await SearchAsync(mount, Phrase, identifier: "/lu-legilux/no-such-work");
            Assert.AreEqual("identifier_unknown", unknown.Refusal!.Code);
            var eu = await SearchAsync(mount, Phrase, identifier: "32016R0679");
            Assert.AreEqual("retrieval_mode_unavailable", eu.Refusal!.Code);
            Assert.AreEqual("r2_provision_discovery", eu.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
            Assert.AreEqual(PublisherId.EuEurLex, eu.Context.Publisher);
        }

        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var europeOnly = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(europeOnly);
        var unmounted = await SearchAsync(europeOnly, Phrase);
        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
        Assert.AreEqual("lu", unmounted.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\"}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"language\":\"fra\"}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\" \",\"language\":\"fra\"}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\",\"language\":\"fra\",\"date\":\"2024-02-30\"}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\",\"language\":\"fra\",\"limit\":0}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\",\"language\":\"fra\",\"limit\":201}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\",\"language\":\"fra\",\"mode\":\"\"}}")]
    // A parameter that implies ranking is not a declared one: it is rejected, never ignored.
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\",\"language\":\"fra\",\"sort\":\"relevance\"}}")]
    [DataRow("{\"operation_id\":\"search\",\"parameters\":{\"query\":\"bail\",\"language\":\"fra\",\"rank\":\"bm25\"}}")]
    public async Task UnusableSearchRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        AssertTransportProblem(await PostAsync(mount, SearchRawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public void ThePublishedBoundsAreTheEnforcedBounds()
    {
        // The request document is rendered by the exporter and the mount holds the same numbers again for a
        // caller that reaches it without the document. Nothing but this test says they are one number: the
        // rendered-file check ties the file to the exporter, and the exporter and the mount are separate.
        using var document = JsonDocument.Parse(V3PlatformSchemaExporter.ExportRequestUtf8("search"));
        var properties = document.RootElement.GetProperty("properties").GetProperty("parameters").GetProperty("properties");
        Assert.AreEqual(V3CorpusMount.SearchMaxQueryCharacters, properties.GetProperty("query").GetProperty("maxLength").GetInt32());
        Assert.AreEqual(1, properties.GetProperty("limit").GetProperty("minimum").GetInt32());
        Assert.AreEqual(V3CorpusMount.SearchMaxHits, properties.GetProperty("limit").GetProperty("maximum").GetInt32());
    }

    [TestMethod]
    public async Task AScanDoesNotHoldTheReadersGateSoOneQueryCannotStallTheMount()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await WriteTextsAsync(fixture);
        var manifest = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        using var reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), manifest, CancellationToken.None);
        var gate = typeof(LuxembourgIndexReader).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader)!;

        Task<IReadOnlyList<LuxembourgIndexResolvedState>> control;
        Task<IReadOnlyList<LuxembourgIndexSearchHit>?> scan;
        lock (gate)
        {
            // The control: an operation that needs the gate waits for it, so this test can see a scan that holds it.
            control = Task.Run(() => reader.ResolveWorkStates(fixture.WorkKey));
            Assert.IsFalse(control.Wait(TimeSpan.FromMilliseconds(500)), "the control operation was not held by the gate, so this test cannot see the scan holding it");

            // The scan is not held by it.
            scan = Task.Run(() => reader.SearchStateArticles("fra", ["garantie"], null));
            Assert.IsTrue(scan.Wait(TimeSpan.FromSeconds(15)), "a search scan waited for the reader's gate: one query could stall every other operation on the mount");
        }

        Assert.IsTrue(scan.Result!.Count > 0);
        Assert.IsTrue(control.Wait(TimeSpan.FromSeconds(15)), "the control never completed once the gate was released");
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchema()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var searchBody = JsonSerializer.Serialize(new { operation_id = "search", parameters = new { query = Phrase, language = "fra" } });

        AssertTransportProblem(await PostAsync(mount, AsOfRawTarget, searchBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, SearchRawTarget + "?x=1", searchBody), "unknown_route", StatusCodes.Status404NotFound);
    }

    private static string PublisherWorkIriOf(MountedFixture fixture, string stateSha256)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT publisher_work_iri FROM states WHERE state_sha256=$state";
        command.Parameters.AddWithValue("$state", stateSha256);
        return (string)command.ExecuteScalar()!;
    }

    private static string CursorKey(JsonElement hit) =>
        $"{hit.GetProperty("lane").GetString()}.{hit.GetProperty("state_sha256").GetString()}.{hit.GetProperty("article_identity_sha256").GetString()}";

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> SearchAsync(
        V3CorpusMount mount, string query, string language = "fra", string? identifier = null, string? date = null,
        string? mode = null, int? limit = null, string? after = null)
    {
        var parameters = new Dictionary<string, object> { ["query"] = query, ["language"] = language };
        if (identifier is not null)
        {
            parameters["identifier"] = identifier;
        }

        if (date is not null)
        {
            parameters["date"] = date;
        }

        if (mode is not null)
        {
            parameters["mode"] = mode;
        }

        if (limit is not null)
        {
            parameters["limit"] = limit.Value;
        }

        if (after is not null)
        {
            parameters["after"] = after;
        }

        using var document = JsonSerializer.SerializeToDocument(parameters);
        return await PostOkAsync(mount, SearchRawTarget, "search", document.RootElement);
    }

    private static async Task<V3Envelope> AsOfAsync(V3CorpusMount mount, string identifier, string date, string? language = "fra")
    {
        var parameters = new Dictionary<string, object> { ["identifier"] = identifier, ["date"] = date };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        using var document = JsonSerializer.SerializeToDocument(parameters);
        return await PostOkAsync(mount, AsOfRawTarget, "as_of", document.RootElement);
    }

    private static async Task<V3Envelope> PostOkAsync(V3CorpusMount mount, string rawTarget, string operationId, JsonElement parameters)
    {
        var context = await PostAsync(mount, rawTarget, JsonSerializer.Serialize(new { operation_id = operationId, parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-search";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static void AssertTransportProblem(DefaultHttpContext context, string code, int status)
    {
        Assert.AreEqual(status, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
    }
}
