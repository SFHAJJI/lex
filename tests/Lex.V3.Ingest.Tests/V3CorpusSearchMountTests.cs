using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>search</c> driven through the real handler on a verified mount: the two lexical lanes the
/// mounted index can serve, resolver first. Relaxed never outranks strict and an article matching
/// both is served once, as strict; the answer says it is an order and not a rank, that matching is
/// byte-exact, that a page is the first hits and not the best, and what a hit counts; a mode the
/// index cannot serve is refused and never ignored; a named work on a date refuses as
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
    private const string Neither = "Le present article ne dit rien du depot.";

    /// <summary>
    /// Rewrites the first three articles of the fixture's own state: one matching the phrase (and so
    /// every term), one matching every term and not the phrase, one matching neither.
    /// </summary>
    private static async Task<(string Both, string Terms, string None)> WriteTextsAsync(MountedFixture fixture)
    {
        var articles = fixture.ArticlesOfOwnState();
        Assert.IsGreaterThanOrEqualTo(3, articles.Count, "The fixture's state holds too few articles for this test.");
        await fixture.RewriteArticleTextAsync(fixture.ExpressionIri, articles[0].PublisherId, BothLanes);
        await fixture.RewriteArticleTextAsync(fixture.ExpressionIri, articles[1].PublisherId, TermsOnly);
        await fixture.RewriteArticleTextAsync(fixture.ExpressionIri, articles[2].PublisherId, Neither);
        foreach (var other in articles.Skip(3))
        {
            await fixture.RewriteArticleTextAsync(fixture.ExpressionIri, other.PublisherId, Neither);
        }

        return (articles[0].PublisherId, articles[1].PublisherId, articles[2].PublisherId);
    }

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
        CollectionAssert.AreEqual(new[] { "exact_phrase" },
            hits[0].GetProperty("match_reasons").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(termsOnly, hits[1].GetProperty("publisher_id").GetString());
        Assert.AreEqual("relaxed", hits[1].GetProperty("lane").GetString());
        CollectionAssert.AreEqual(new[] { "all_terms" },
            hits[1].GetProperty("match_reasons").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(1, hits.Count(hit => hit.GetProperty("publisher_id").GetString() == both), "Served once, never again as relaxed.");

        // What the answer says about itself: an order and not a rank, byte-exact, and what a hit is.
        Assert.AreEqual(V3CorpusMount.SearchRanking, body.GetProperty("ranking").GetString());
        StringAssert.Contains(body.GetProperty("ranking").GetString(), "not the best hits");
        Assert.AreEqual(V3CorpusMount.SearchMatching, body.GetProperty("matching").GetString());
        Assert.AreEqual("the first hits in the stated order, not the best hits", body.GetProperty("page_is").GetString());
        Assert.AreEqual(V3CorpusMount.SearchHitUnit, body.GetProperty("hit_unit").GetString());
        CollectionAssert.AreEqual(new[] { "strict", "relaxed" },
            body.GetProperty("modes_held").EnumerateArray().Select(static v => v.GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { "bm25", "semantic" },
            body.GetProperty("modes_not_held").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.IsFalse(body.GetRawText().Contains("\"score\"", StringComparison.Ordinal), "No score is served.");
        Assert.IsFalse(body.GetRawText().Contains("snippet", StringComparison.Ordinal), "No snippet is served.");
        Assert.IsFalse(body.GetRawText().Contains(BothLanes, StringComparison.Ordinal), "No article text is served.");
        CollectionAssert.AreEqual(new[] { "garantie", "locative" },
            body.GetProperty("terms").EnumerateArray().Select(static v => v.GetString()).ToArray());

        var population = body.GetProperty("population");
        Assert.IsTrue(population.GetProperty("searchable_text_measured").GetBoolean());
        Assert.AreEqual(1, population.GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(1, population.GetProperty("relaxed_hits").GetInt32());
        Assert.AreEqual(2, population.GetProperty("distinct_publisher_articles").GetInt32());
        Assert.AreEqual(1, population.GetProperty("works_with_hits").GetInt32());
        Assert.IsFalse(body.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("continue_after").ValueKind);

        // A hit names its state, and its pointer reaches that state and no other.
        Assert.AreEqual(fixture.StateSha256, hits[0].GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.WorkKey, hits[0].GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, hits[0].GetProperty("applicability_date").GetString());
        var resolved = await PostOkAsync(mount, "/api/v3/resolve", "resolve", hits[0].GetProperty("resolve"));
        Assert.AreEqual(V3Verdicts.Answer, resolved.Verdict);
        StringAssert.Contains(resolved.Result!.Value.GetRawText(), fixture.StateSha256);

        // Each lane can be asked for alone; the article matching both is strict and so is not relaxed.
        var strictOnly = (await SearchAsync(mount, Phrase, mode: "strict")).Result!.Value;
        Assert.AreEqual(both, strictOnly.GetProperty("hits").EnumerateArray().Single().GetProperty("publisher_id").GetString());
        var relaxedOnly = (await SearchAsync(mount, Phrase, mode: "relaxed")).Result!.Value;
        Assert.AreEqual(termsOnly, relaxedOnly.GetProperty("hits").EnumerateArray().Single().GetProperty("publisher_id").GetString());
        Assert.AreEqual(0, relaxedOnly.GetProperty("population").GetProperty("strict_hits").GetInt32());
    }

    [TestMethod]
    public async Task MatchingIsByteExactAndNoHitIsAnAnswer()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await WriteTextsAsync(fixture);
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

        // One term has no relaxed lane to add: all of its hits are the phrase.
        var oneTerm = (await SearchAsync(mount, "locative")).Result!.Value;
        Assert.AreEqual(2, oneTerm.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(0, oneTerm.GetProperty("population").GetProperty("relaxed_hits").GetInt32());

        // A term repeated in the query is one term.
        var repeated = (await SearchAsync(mount, "locative  garantie locative")).Result!.Value;
        CollectionAssert.AreEqual(new[] { "locative", "garantie" },
            repeated.GetProperty("terms").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(0, repeated.GetProperty("population").GetProperty("strict_hits").GetInt32());
        Assert.AreEqual(2, repeated.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
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

        // With a date, only the state as_of selects contributes.
        var onFirst = (await SearchAsync(mount, Phrase, mode: "strict", date: Shift(laterDate, -1))).Result!.Value;
        Assert.AreEqual(fixture.StateSha256, onFirst.GetProperty("hits").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        var onLater = (await SearchAsync(mount, Phrase, mode: "strict", date: laterDate)).Result!.Value;
        Assert.AreEqual(later.StateSha256, onLater.GetProperty("hits").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        var before = (await SearchAsync(mount, Phrase, date: Shift(fixture.ApplicabilityDate, -1))).Result!.Value;
        Assert.AreEqual(0, before.GetProperty("hits").GetArrayLength(), "Across works, nothing applicable is an answer with no hits.");
        Assert.AreEqual(0, before.GetProperty("ambiguous_works").GetArrayLength());
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
        var ambiguous = body.GetProperty("ambiguous_works").EnumerateArray().Single();
        Assert.AreEqual(fixture.WorkKey, ambiguous.GetProperty("work_key").GetString());
        Assert.AreEqual("ambiguous_version", ambiguous.GetProperty("reason").GetString());
        var candidates = ambiguous.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()!).ToArray();
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
        // as a search that ran.
        var unmeasured = (await SearchAsync(mount, Phrase, language: "deu")).Result!.Value;
        Assert.IsFalse(unmeasured.GetProperty("population").GetProperty("searchable_text_measured").GetBoolean());
        Assert.AreEqual(0, unmeasured.GetProperty("hits").GetArrayLength());

        var germanSearch = await SearchAsync(mount, Phrase, identifier: identifier, date: asked, language: "deu");
        Assert.AreEqual("ambiguous_version", germanSearch.Refusal!.Code);
        var asOfGerman = await AsOfAsync(mount, identifier, asked, "deu");
        Assert.AreEqual(asOfGerman.Refusal!.HelpfulPayload.GetRawText(), germanSearch.Refusal.HelpfulPayload.GetRawText());
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
        var all = whole.GetProperty("hits").EnumerateArray().Select(Key).ToArray();
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
            var keys = page.GetProperty("hits").EnumerateArray().Select(Key).ToArray();
            Assert.IsLessThanOrEqualTo(2, keys.Length);
            pages.Add(keys);
            // The population is the scope asked for, not the page.
            Assert.AreEqual(3, page.GetProperty("population").GetProperty("strict_hits").GetInt32());
            Assert.AreEqual(3, page.GetProperty("population").GetProperty("relaxed_hits").GetInt32());
            after = page.GetProperty("truncated").GetBoolean() ? page.GetProperty("continue_after").GetString() : null;
            Assert.AreEqual(page.GetProperty("truncated").GetBoolean(), page.GetProperty("continue_after").ValueKind == JsonValueKind.String);
        }
        while (after is not null);

        Assert.AreEqual(3, pages.Count);
        CollectionAssert.AreEqual(all, pages.SelectMany(static page => page).ToArray(), "Neither repeated nor skipped, in the same order.");

        // A limit that exactly fills is not truncated, and a cursor naming no hit is a schema rejection.
        var exact = (await SearchAsync(mount, Phrase, limit: 6)).Result!.Value;
        Assert.IsFalse(exact.GetProperty("truncated").GetBoolean());
        AssertTransportProblem(
            await PostAsync(mount, SearchRawTarget, JsonSerializer.Serialize(new
            {
                operation_id = "search",
                parameters = new { query = Phrase, language = "fra", after = "strict.nothing.nothing" },
            })),
            "request_schema_invalid", StatusCodes.Status400BadRequest);
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
                CollectionAssert.AreEqual(new[] { "strict", "relaxed" },
                    refused.Refusal.HelpfulPayload.GetProperty("available_modes").EnumerateArray().Select(static v => v.GetString()).ToArray());
                Assert.AreEqual(PublisherId.LuLegilux, refused.Context.Publisher);
            }

            var english = await SearchAsync(mount, Phrase, language: "eng");
            Assert.AreEqual("language_not_available", english.Refusal!.Code);
            CollectionAssert.AreEqual(new[] { "fra" },
                english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
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

    private static string Key(JsonElement hit) =>
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
