using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.TestSupport;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Exact references first (the plan's item 4.2; R59; R90's resolver-first order), driven through the real handler on a verified
/// mount: a search that names no work reads its query as a reference before any word is searched. A reference the index holds
/// opens its act (Reading at the article and date the fixed table and the date select, else the dossier) and searches no word;
/// an act the index does not hold is refused <c>identifier_unknown</c> with no list; several acts are candidates, never a guess;
/// every refusal says what was recognised; a query that is no reference is searched as before.
/// </summary>
[TestClass]
public sealed class V3CorpusReferenceMountTests
{
    private const string SearchTarget = "/api/v3/search";

    /// <summary>The fixture's act: the loi du 10 août 1991 (loi-1991-08-10-n3), held as one French state of 2024-02-01.</summary>
    private const string WorkIri = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";

    /// <summary>Today in Luxembourg at the fixture's answer instant (2026-09-18T07:00Z).</summary>
    private const string Today = "2026-09-18";

    internal static Task<V3Envelope> SearchAsync(V3CorpusMount mount, string query, string language = "fra", string? date = null, string? identifier = null)
    {
        var parameters = new Dictionary<string, object> { ["query"] = query, ["language"] = language };
        if (date is not null)
        {
            parameters["date"] = date;
        }

        if (identifier is not null)
        {
            parameters["identifier"] = identifier;
        }

        return EnvelopeAsync(mount, SearchTarget, "search", parameters);
    }

    /// <summary>The reference an answer opens: its <c>reference</c> member, held to the phase's rules for an opened act.</summary>
    internal static JsonElement Opened(V3Envelope envelope, string query)
    {
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{query}: {envelope.Refusal?.Code} {envelope.Refusal?.HelpfulPayload.GetRawText()}");
        var body = envelope.Result!.Value;
        var reference = body.GetProperty("reference");
        Assert.AreEqual("exact_reference", reference.GetProperty("phase").GetString(), query);
        Assert.AreEqual("opens", reference.GetProperty("outcome").GetString(), query);
        Assert.IsFalse(body.GetProperty("lanes_run").GetBoolean(), $"{query}: no word is searched");
        Assert.AreEqual(0, body.GetProperty("hits").GetArrayLength(), $"{query}: no list");
        Assert.IsFalse(body.TryGetProperty("population", out _), $"{query}: no lane ran, so none is counted");
        return reference;
    }

    internal static JsonElement Refused(V3Envelope envelope, string code, string query)
    {
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, $"{query}: {envelope.Result?.Value.GetRawText()}");
        Assert.AreEqual(code, envelope.Refusal!.Code, $"{query}: {envelope.Refusal.HelpfulPayload.GetRawText()}");
        Assert.IsNull(envelope.Result, $"{query}: a refusal carries no list");
        return envelope.Refusal.HelpfulPayload;
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(static value => value.GetString()!).ToArray();

    /// <summary>The readable address (R67) of one of the fixture act's articles in its French text.</summary>
    private static string Readable(MountedFixture fixture, string articleId) => $"/lu-legilux/{fixture.WorkKey}/fra/{articleId}";

    private static async Task<(MountedFixture Fixture, V3CorpusMount Mount)> MountAsync(Func<MountedFixture, Task>? prepare = null)
    {
        var fixture = await MountedFixture.CreateAsync();
        if (prepare is not null)
        {
            await prepare(fixture);
        }

        var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        return (fixture, mount);
    }

    [TestMethod]
    public async Task AnIdentifierOrATypeAndDateOfAHeldActOpensItsDossierAndSearchesNoWord()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var (query, kind, form) in new[]
                 {
                     ($"/lu-legilux/{fixture.WorkKey}", "permalink", "permalink"),
                     ($"https://law.soufien.lu/fr/lu-legilux/{fixture.WorkKey}", "permalink", "permalink"),
                     (WorkIri, "eli", "eli"),
                     ("eli/etat/leg/loi/1991/08/10/n3", "eli", "eli"),
                     ("https://legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3", "eli", "eli"),
                     ("loi du 10 août 1991", "citation", "type_and_date"),
                     ("Loi modifiée du 10 août 1991", "citation", "type_and_date"),
                 })
        {
            var envelope = await SearchAsync(mount, query, date: Today);
            var reference = Opened(envelope, query);
            Assert.AreEqual(query, envelope.Result!.Value.GetProperty("requested_query").GetString(), "the query is echoed as typed");
            Assert.AreEqual(Today, envelope.Result.Value.GetProperty("requested_date").GetString());
            Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, query);
            Assert.AreEqual(kind, reference.GetProperty("kind").GetString(), query);
            var opens = reference.GetProperty("opens");
            Assert.AreEqual("dossier", opens.GetProperty("page").GetString(), query);
            Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", opens.GetProperty("address").GetString(), query);
            Assert.AreEqual(fixture.WorkKey, opens.GetProperty("work").GetString(), query);
            var recognised = reference.GetProperty("recognised");
            Assert.AreEqual(form, recognised.GetProperty("act").GetProperty("form").GetString(), query);
            Assert.AreEqual(JsonValueKind.Null, recognised.GetProperty("article").ValueKind, query);
            Assert.AreEqual(JsonValueKind.Null, recognised.GetProperty("date").ValueKind, $"{query}: a dossier is opened at no date");
        }
    }

    [TestMethod]
    public async Task ACitationOfAnArticleAtADateOpensReadingAtThatArticleInTheVersionTheDateSelects()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        var held = fixture.ArticlesOfOwnState().Select(static article => article.PublisherId).ToArray();
        CollectionAssert.Contains(held, "art_1er");
        CollectionAssert.Contains(held, "art_5");

        var dated = Opened(await SearchAsync(mount, "article 5 de la loi du 10 août 1991, 15 mars 2024"), "dated");
        var opens = dated.GetProperty("opens");
        Assert.AreEqual("reading", opens.GetProperty("page").GetString());
        Assert.AreEqual("permalink", opens.GetProperty("address_kind").GetString(), "a date given opens the version it selects, pinned");
        Assert.AreEqual(fixture.Permalink + "/art_5", opens.GetProperty("address").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, opens.GetProperty("state_date").GetString());
        Assert.AreEqual("art_5", opens.GetProperty("article_id").GetString());
        Assert.AreEqual("fra", opens.GetProperty("language").GetString());
        var recognised = dated.GetProperty("recognised");
        Assert.AreEqual("5", recognised.GetProperty("article").GetProperty("number").GetString());
        Assert.AreEqual("5", recognised.GetProperty("article").GetProperty("as_typed").GetString());
        Assert.AreEqual("number", recognised.GetProperty("article").GetProperty("matched_by").GetString());
        Assert.AreEqual("15 mars 2024", recognised.GetProperty("date").GetProperty("as_typed").GetString());
        Assert.AreEqual("2024-03-15", recognised.GetProperty("date").GetProperty("date").GetString());
        Assert.AreEqual("citation", recognised.GetProperty("date").GetProperty("source").GetString());
        Assert.AreEqual("type_and_date", recognised.GetProperty("act").GetProperty("form").GetString());
        Assert.AreEqual(fixture.WorkKey, recognised.GetProperty("act").GetProperty("work").GetString());
        StringAssert.Contains(recognised.GetProperty("article_rule").GetString(), "42bis, 42ter and 42-1 are four numbers");

        // 1 = 1er = premier. With no date given, the act's readable address opens today's version (R67); with one, the
        // permalink of the version the date selects (R64).
        foreach (var (query, address) in new[]
                 {
                     ("article 1er de la loi du 10 août 1991", Readable(fixture, "art_1er")),
                     ("article premier de la loi du 10 août 1991", Readable(fixture, "art_1er")),
                     ("loi du 10 août 1991, art. 1", Readable(fixture, "art_1er")),
                     ("art. 1er de la loi modifiée du 10 août 1991 le 1er mars 2025", fixture.Permalink + "/art_1er"),
                 })
        {
            Assert.AreEqual(address, Opened(await SearchAsync(mount, query), query).GetProperty("opens").GetProperty("address").GetString(), query);
        }

        var readable = Opened(await SearchAsync(mount, "article 5 de la loi du 10 août 1991", date: Today), "the box's today").GetProperty("opens");
        Assert.AreEqual("readable", readable.GetProperty("address_kind").GetString(), "the home page's box sends today, which is no date given");
        Assert.AreEqual(Readable(fixture, "art_5"), readable.GetProperty("address").GetString());
        Assert.AreEqual(fixture.Permalink + "/art_5",
            Opened(await SearchAsync(mount, "article 5 de la loi du 10 août 1991", date: "2024-06-01"), "another day")
                .GetProperty("opens").GetProperty("address").GetString(), "a search date that is not today is a date given");

        // The date: the citation's, else the search's, else today in Luxembourg (R13).
        var searchDated = Opened(await SearchAsync(mount, "article 5 de la loi du 10 août 1991", date: "2024-06-01"), "search date")
            .GetProperty("recognised").GetProperty("date");
        Assert.AreEqual("2024-06-01", searchDated.GetProperty("date").GetString());
        Assert.AreEqual("search_date", searchDated.GetProperty("source").GetString());
        Assert.AreEqual(JsonValueKind.Null, searchDated.GetProperty("as_typed").ValueKind);
        var today = Opened(await SearchAsync(mount, "article 5 de la loi du 10 août 1991"), "today").GetProperty("recognised").GetProperty("date");
        Assert.AreEqual(Today, today.GetProperty("date").GetString());
        Assert.AreEqual("today_in_luxembourg", today.GetProperty("source").GetString());
        var cited = Opened(await SearchAsync(mount, "article 5 de la loi du 10 août 1991, 15 mars 2025", date: "2024-06-01"), "both")
            .GetProperty("recognised").GetProperty("date");
        Assert.AreEqual("2025-03-15", cited.GetProperty("date").GetString(), "the citation's own date wins over the search's");
    }

    [TestMethod]
    public async Task ADateBeforeTheHeldHistoryIsNoVersionForDateAndSaysWhatWasRecognised()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        const string query = "article 5 de la loi du 10 août 1991 au 1er janvier 2020";
        var payload = Refused(await SearchAsync(mount, query), "no_version_for_date", query);
        Assert.AreEqual("2020-01-01", payload.GetProperty("requested_date").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, payload.GetProperty("history_begins").GetString());
        Assert.AreEqual("citation", payload.GetProperty("recognised_kind").GetString());
        Assert.AreEqual("loi du 10 août 1991", payload.GetProperty("recognised_act").GetString());
        Assert.AreEqual("5", payload.GetProperty("recognised_article").GetString());
        Assert.AreEqual("2020-01-01", payload.GetProperty("recognised_date").GetString());
        Assert.AreEqual("citation", payload.GetProperty("recognised_date_source").GetString());
        Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean());

        // The review of #974, F1: an earlier date after a comma or "le" that the act's title does not carry is the date asked, as
        // after "au": never words that name no act.
        foreach (var earlier in new[] { "article 5 de la loi du 10 août 1991, 1er janvier 1990", "article 5 de la loi du 10 août 1991, le 1er janvier 1990",
                     "loi du 10 août 1991, 1990-01-01", "article 2 de la loi du 10 août 1991, 1990-1-1" })
        {
            var asked = Refused(await SearchAsync(mount, earlier), "no_version_for_date", earlier);
            Assert.AreEqual(fixture.ApplicabilityDate, asked.GetProperty("history_begins").GetString(), earlier);
            StringAssert.StartsWith(asked.GetProperty("recognised_date").GetString(), "1990-01-01", earlier);
        }
    }

    [TestMethod]
    public async Task ATitlesOwnEarlierDateIsReadAsTheTitlesAndAPartOfAnArticleIsKept()
    {
        const string title = "Loi du 10 août 1991 portant approbation de la Convention, signée à Genève, le 1er janvier 1990";
        var (fixture, mount) = await MountAsync(fixture =>
            fixture.AddTitleRowsAsync((null, fixture.ExpressionIri, "fra", "title", title, new string('c', 64))));
        await using var cleanup = fixture;
        using var mounted = mount;

        // The title's own date: the title reading names the act, so its dossier opens.
        var titled = Opened(await SearchAsync(mount, title), "title");
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", titled.GetProperty("opens").GetProperty("address").GetString());

        // "la loi du …", "art. 5, loi du …", and a part of the article, kept as typed and said with the article opened.
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}",
            Opened(await SearchAsync(mount, "la loi du 10 août 1991"), "la loi").GetProperty("opens").GetProperty("address").GetString());
        Assert.AreEqual(Readable(fixture, "art_5"),
            Opened(await SearchAsync(mount, "art. 5, loi du 10 août 1991"), "comma").GetProperty("opens").GetProperty("address").GetString());
        foreach (var (query, part) in new[] { ("loi du 10 août 1991 art. 5 al. 3", "al. 3"), ("article 5, alinéa 2, de la loi du 10 août 1991", "alinéa 2") })
        {
            var reference = Opened(await SearchAsync(mount, query), query);
            Assert.AreEqual(Readable(fixture, "art_5"), reference.GetProperty("opens").GetProperty("address").GetString(), query);
            Assert.AreEqual(part, reference.GetProperty("recognised").GetProperty("article").GetProperty("part").GetString(), query);
        }

        const string partRefused = "loi du 10 août 1991 art. 99 al. 3";
        Assert.AreEqual("99, al. 3", Refused(await SearchAsync(mount, partRefused), "anchor_not_in_version", partRefused)
            .GetProperty("recognised_article").GetString(), "a refusal says the part with the article");

        // Words after a type and date that name no act of the day (a negation, another act) list the day's acts, here one:
        // never a guess, never an absence.
        foreach (var query in new[] { "loi du 10 août 1991 ne s'applique pas", "loi du 10 août 1991 relative aux chiens", "loi du 10 août 1991 et loi du 3 février 2018" })
        {
            var listed = Refused(await SearchAsync(mount, query), "ambiguous_identifier", query);
            CollectionAssert.AreEqual(new[] { $"/lu-legilux/{fixture.WorkKey}" }, Strings(listed.GetProperty("candidates")), query);
            CollectionAssert.AreEqual(new[] { true }, listed.GetProperty("candidate_held").EnumerateArray().Select(static held => held.GetBoolean()).ToArray(), query);
        }
    }

    [TestMethod]
    public async Task ADayOrAnIssueTheCatalogueNamesSeveralActsOnListsEachHeldOrNotAndOpensNone()
    {
        // The review of #974, F2: the catalogue names a second act of the day, which the index holds no version of; the day and the
        // Mémorial issue that publishes both name two acts, so none is opened on its own.
        const string other = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n5";
        const string issue = "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58";
        var (fixture, mount) = await MountAsync(async fixture =>
        {
            await fixture.AddTitleRowsAsync((null, fixture.ExpressionIri, "fra", "title", "Loi du 10 août 1991 sur la profession d'avocat", new string('c', 64)));
            await fixture.AddWorkFactAsync(other + "/jo/fr", "title", "expression_language_or_title", "literal", "Loi du 10 août 1991 portant approbation de la Convention de La Haye");
            await fixture.AddWorkFactAsync(other + "/jo", "isPartOf", "wemi_structural", "iri", issue);
            await fixture.AddWorkFactAsync(WorkIri + "/jo", "isPartOf", "wemi_structural", "iri", issue);
        });
        await using var cleanup = fixture;
        using var mounted = mount;

        foreach (var query in new[] { "loi du 10 août 1991", "Mém. A n° 58 de 1991", "article 5 de la loi du 10 août 1991, 15 mars 2024", "loi du 10 août 1991 relative aux chiens" })
        {
            var payload = Refused(await SearchAsync(mount, query), "ambiguous_identifier", query);
            CollectionAssert.AreEqual(new[] { $"/lu-legilux/{fixture.WorkKey}", other }, Strings(payload.GetProperty("candidates")), query);
            CollectionAssert.AreEqual(new[] { true, false }, payload.GetProperty("candidate_held").EnumerateArray().Select(static held => held.GetBoolean()).ToArray(), query);
            CollectionAssert.AreEqual(new[] { "Loi du 10 août 1991 sur la profession d'avocat", "Loi du 10 août 1991 portant approbation de la Convention de La Haye" },
                Strings(payload.GetProperty("candidate_titles")), query);
            CollectionAssert.AreEqual(new[] { "fra", "fra" }, Strings(payload.GetProperty("candidate_title_languages")), $"{query}: each title's language");
        }

        // Title words choose among the day's acts: the held one opens; the other is named, held by the catalogue and not here.
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}",
            Opened(await SearchAsync(mount, "loi du 10 août 1991 sur la profession d'avocat"), "held").GetProperty("opens").GetProperty("address").GetString());
        const string notHeld = "loi du 10 août 1991 portant approbation de la Convention de La Haye";
        var unknown = Refused(await SearchAsync(mount, notHeld), "identifier_unknown", notHeld);
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, unknown.GetProperty("recognised_held_act").ValueKind, "an act this site does not hold");
        CollectionAssert.AreEqual(new[] { other }, Strings(unknown.GetProperty("recognised_catalogue_works")));
        StringAssert.Contains(unknown.GetProperty("what_would_answer_detail").GetString(), "the publisher's catalogue names this one");
    }

    [TestMethod]
    public async Task AnArticleTheVersionDoesNotHoldIsAnchorNotInVersionAndFortyTwoIsNeverFortyTwoTer()
    {
        var (fixture, mount) = await MountAsync(static fixture =>
        {
            // One plain article the index holds, other than those the test cites, numbered 42ter.
            var spare = fixture.ArticlesOfOwnState().Select(static article => article.PublisherId)
                .First(static id => id is "art_10" or "art_11" or "art_12" or "art_13" or "art_14");
            return fixture.RenameArticleIdAsync(fixture.ExpressionIri, spare, "art_42ter");
        });
        await using var cleanup = fixture;
        using var mounted = mount;

        const string ninetyNine = "article 99 de la loi du 10 août 1991";
        var missing = Refused(await SearchAsync(mount, ninetyNine), "anchor_not_in_version", ninetyNine);
        Assert.AreEqual("99", missing.GetProperty("requested_anchor").GetString());
        Assert.IsTrue(missing.GetProperty("do_not_fall_back_to_full_text_search").GetBoolean(), "no word is searched instead");
        Assert.IsGreaterThan(0, missing.GetProperty("nearest_anchors").GetArrayLength());
        Assert.AreEqual("99", missing.GetProperty("recognised_article").GetString());
        // The act and the version the date selects are held; an article of it is what is not.
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", missing.GetProperty("recognised_held_act").GetString());
        Assert.AreEqual(fixture.Permalink, missing.GetProperty("recognised_held_version").GetString());

        // The act holds Art. 42 and, here, an Art. 42ter beside it: each opens its own, and 42bis neither.
        CollectionAssert.Contains(fixture.ArticlesOfOwnState().Select(static article => article.PublisherId).ToArray(), "art_42");
        Assert.AreEqual(Readable(fixture, "art_42"),
            Opened(await SearchAsync(mount, "article 42 de la loi du 10 août 1991"), "42").GetProperty("opens").GetProperty("address").GetString());
        Assert.AreEqual(Readable(fixture, "art_42ter"),
            Opened(await SearchAsync(mount, "article 42ter de la loi du 10 août 1991"), "42ter").GetProperty("opens").GetProperty("address").GetString());
        Assert.AreEqual(fixture.Permalink + "/art_42ter",
            Opened(await SearchAsync(mount, "loi du 10 août 1991, art. 42 ter, 15 mars 2024"), "42 ter").GetProperty("opens").GetProperty("address").GetString());
        const string fortyTwoBis = "article 42bis de la loi du 10 août 1991";
        var never = Refused(await SearchAsync(mount, fortyTwoBis), "anchor_not_in_version", fortyTwoBis);
        CollectionAssert.IsSubsetOf(new[] { "art_42", "art_42ter" }, Strings(never.GetProperty("nearest_anchors")), "named as neighbours, never opened");
        Assert.AreEqual("42bis", never.GetProperty("recognised_article").GetString());
    }

    [TestMethod]
    public async Task ADateThatNamesNoCalendarDayOpensNothingAndSearchesNoWord()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var query in new[] { "article 5 de la loi du 10 août 1991, 31 février 2024", "loi du 31 février 1991", "Mémorial A n° 58 du 30 février 1991" })
        {
            var envelope = await SearchAsync(mount, query);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, query);
            var body = envelope.Result!.Value;
            var reference = body.GetProperty("reference");
            Assert.AreEqual("not_opened", reference.GetProperty("outcome").GetString(), query);
            Assert.AreEqual("date_not_a_calendar_date", reference.GetProperty("reason").GetString(), query);
            Assert.AreEqual(JsonValueKind.Null, reference.GetProperty("opens").ValueKind, query);
            Assert.IsFalse(body.GetProperty("lanes_run").GetBoolean(), $"{query}: never searched as words with its date dropped");
            Assert.AreEqual(0, body.GetProperty("hits").GetArrayLength(), query);
        }
    }

    [TestMethod]
    public async Task AnActTheIndexDoesNotHoldIsRefusedWithNoListAndIsNeverSearchedAsWords()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var (query, act, article, date) in new (string, string, string?, string?)[]
                 {
                     ("loi du 11 août 1991", "loi du 11 août 1991", null, null),
                     ("article 5 de la loi du 11 août 1991", "loi du 11 août 1991", "5", null),
                     ("http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/11/n1", "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/11/n1", null, null),
                     ("Mém. A n° 999 de 1991", "Mém. A n° 999 de 1991", null, null),
                     ("1991A99999", "1991A99999", null, null),
                     ("/lu-legilux/loi-1999-01-01-n1", "/lu-legilux/loi-1999-01-01-n1", null, null),
                 })
        {
            var envelope = await SearchAsync(mount, query, date: Today);
            var payload = Refused(envelope, "identifier_unknown", query);
            Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, query);
            Assert.AreEqual(query, payload.GetProperty("requested_identifier").GetString(), query);
            Assert.AreEqual(act, payload.GetProperty("recognised_act").GetString(), query);
            Assert.AreEqual(article, payload.GetProperty("recognised_article").GetString(), query);
            Assert.AreEqual(date, payload.GetProperty("recognised_date").GetString(), query);
            Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean(), query);
            StringAssert.Contains(payload.GetProperty("population_disclosure").GetString(), "Luxembourg index holds 1 Luxembourg work", query);
            StringAssert.Contains(payload.GetProperty("what_would_answer_detail").GetString(), "no word was searched", query);
        }
    }

    [TestMethod]
    public async Task ATitleTheIndexHoldsNoActUnderIsNoReferenceAndIsSearchedAsWordsWithEveryToken()
    {
        // The review of #974, F1: a title is not a strict locator, so a title the index holds no act under says no absence; the
        // query is searched as words, as typed, so no protected token is dropped (R90).
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var (query, tokens) in new (string, string[])[]
                 {
                     ("Code du travail, article L. 121-6, 15 mars 2021", ["L.", "121-6,", "2021"]),
                     ("règlement intérieur article 12", ["12"]),
                     ("convention collective article 5", ["5"]),
                     ("loi sur le bail commercial, article 3", ["3"]),
                     ("article 1er de la loi relative aux oeuvres orphelines", ["1er"]),
                     ("règlement grand-ducal relatif aux vacances scolaires, article 2", ["2"]),
                 })
        {
            var envelope = await SearchAsync(mount, query);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{query}: {envelope.Refusal?.Code}");
            var body = envelope.Result!.Value;
            var reference = body.GetProperty("reference");
            Assert.AreEqual("no_reference", reference.GetProperty("outcome").GetString(), query);
            Assert.AreEqual("title_not_held", reference.GetProperty("reason").GetString(), query);
            Assert.IsTrue(body.GetProperty("lanes_run").GetBoolean(), query);
            Assert.AreEqual(query, body.GetProperty("requested_query").GetString(), $"{query}: searched as typed");
            CollectionAssert.IsSubsetOf(tokens, Strings(body.GetProperty("terms")), $"{query}: every protected token is a term");
        }
    }

    [TestMethod]
    public async Task ACodeShapedCitationOpensTheArticleTheFixedTableReadsInTheVersionTheDateSelects()
    {
        // Codes are not held yet (Release 1): this act is made code-shaped, its title "Code du travail" and its articles numbered
        // as a code numbers them, in two versions.
        string? later = null;
        var (fixture, mount) = await MountAsync(async fixture =>
        {
            await fixture.AddTitleRowsAsync((null, fixture.ExpressionIri, "fra", "title", "Code du travail", new string('c', 64)));
            var held = fixture.ArticlesOfOwnState().Select(static article => article.PublisherId).ToHashSet(StringComparer.Ordinal);
            var plain = new[] { "art_2", "art_3", "art_4", "art_6", "art_7", "art_8", "art_9", "art_10" }.Where(held.Contains).Take(4).ToArray();
            Assert.HasCount(4, plain, "four plain articles to number as a code numbers them");
            await fixture.RenameArticleIdAsync(fixture.ExpressionIri, plain[0], "art_l_121-6");
            await fixture.RenameArticleIdAsync(fixture.ExpressionIri, plain[1], "art_L.121-6-1");
            await fixture.RenameArticleIdAsync(fixture.ExpressionIri, plain[2], "art_r_121-6");
            await fixture.RenameArticleIdAsync(fixture.ExpressionIri, plain[3], "art_l_121-60");
            later = MountedFixture.PermalinkOf(await fixture.AddStateAsync("2025-01-01", "fr-20250101"));
        });
        await using var cleanup = fixture;
        using var mounted = mount;
        Assert.IsNotNull(later);

        string Address(string query, V3Envelope envelope) =>
            Opened(envelope, query).GetProperty("opens").GetProperty("address").GetString()!;

        foreach (var query in new[] { "Code du travail, article L. 121-6, 15 mars 2024", "Code du travail, art. L.121-6, 15/03/2024",
                     "Code du travail, article L 121-6, 2024-03-15", "art. L. 121-6 du Code du travail au 15 mars 2024" })
        {
            Assert.AreEqual(fixture.Permalink + "/art_l_121-6", Address(query, await SearchAsync(mount, query)), query);
        }

        Assert.AreEqual(later + "/art_l_121-6",
            Address("2025", await SearchAsync(mount, "Code du travail, article L. 121-6, 15 March 2025")));
        var undated = Opened(await SearchAsync(mount, "Code du travail, article L. 121-6"), "undated");
        Assert.AreEqual(Readable(fixture, "art_l_121-6"), undated.GetProperty("opens").GetProperty("address").GetString(), "today's version, by its readable address");
        Assert.AreEqual("2025-01-01", undated.GetProperty("opens").GetProperty("state_date").GetString(), "the article is read in today's version");
        Assert.AreEqual("Code du travail", undated.GetProperty("recognised").GetProperty("act").GetProperty("title").GetString());
        Assert.AreEqual("L. 121-6", undated.GetProperty("recognised").GetProperty("article").GetProperty("number").GetString());
        Assert.AreEqual(fixture.Permalink + "/art_L.121-6-1",
            Address("6-1", await SearchAsync(mount, "Code du travail, article L. 121-6-1, 15 mars 2024")));
        Assert.AreEqual(fixture.Permalink + "/art_r_121-6",
            Address("R", await SearchAsync(mount, "Code du travail, article R. 121-6, 15 mars 2024")));

        const string partial = "Code du travail, article L. 121, 15 mars 2024";
        var nearest = Strings(Refused(await SearchAsync(mount, partial), "anchor_not_in_version", partial).GetProperty("nearest_anchors"));
        CollectionAssert.IsSubsetOf(new[] { "art_l_121-6", "art_l_121-60" }, nearest, "the ids that begin as L. 121 does, none opened");
        const string before = "Code du travail, article L. 121-6, 1er janvier 2020";
        Refused(await SearchAsync(mount, before), "no_version_for_date", before);
    }

    [TestMethod]
    public async Task SeveralActsOfOneDayAreAmbiguousWithEveryCandidateAndNoneIsPicked()
    {
        var (fixture, mount) = await MountAsync(fixture => fixture.AddStateAsync(fixture.ApplicabilityDate, "fr", workLeaf: "n4"));
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var query in new[] { "loi du 10 août 1991", "article 5 de la loi du 10 août 1991, 15 mars 2024" })
        {
            var payload = Refused(await SearchAsync(mount, query), "ambiguous_identifier", query);
            Assert.AreEqual(query, payload.GetProperty("requested_identifier").GetString());
            CollectionAssert.AreEqual(new[] { "/lu-legilux/loi-1991-08-10-n3", "/lu-legilux/loi-1991-08-10-n4" },
                Strings(payload.GetProperty("candidates")), query);
            Assert.AreEqual(2, payload.GetProperty("candidate_titles").GetArrayLength(), query);
            CollectionAssert.AreEqual(new[] { true, true }, payload.GetProperty("candidate_held").EnumerateArray().Select(static held => held.GetBoolean()).ToArray(), query);
        }

        // Its own identifier still names one of them.
        Assert.AreEqual("/lu-legilux/loi-1991-08-10-n4",
            Opened(await SearchAsync(mount, "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n4"), "n4")
                .GetProperty("opens").GetProperty("address").GetString());
    }

    [TestMethod]
    public async Task AMemorialCitationAndAHistoricalIdentifierOpenTheActTheCatalogueNamesInThem()
    {
        const string resource = WorkIri + "/jo";
        var (fixture, mount) = await MountAsync(async fixture =>
        {
            await fixture.AddWorkFactAsync(resource, "isPartOf", "wemi_structural", "iri", "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58");
            await fixture.AddWorkFactAsync(resource, "publicationDate", "descriptive_date", "literal", "1991-08-27");
            await fixture.AddWorkFactAsync(resource, "historicalLegalId", "act_identity", "literal", "1991A10980");
        });
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var (query, kind) in new[]
                 {
                     ("Mém. A n° 58 de 1991", "memorial"), ("Mémorial A n° 58 du 27 août 1991", "memorial"), ("JO A 58/1991", "memorial"),
                     ("1991A10980", "historical_legal_id"), ("1991a10980", "historical_legal_id"),
                 })
        {
            var reference = Opened(await SearchAsync(mount, query), query);
            Assert.AreEqual(kind, reference.GetProperty("kind").GetString(), query);
            Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", reference.GetProperty("opens").GetProperty("address").GetString(), query);
        }

        const string otherDay = "Mémorial A n° 58 du 28 août 1991";
        Refused(await SearchAsync(mount, otherDay), "identifier_unknown", otherDay);
        Assert.AreEqual(fixture.Permalink + "/art_5",
            Opened(await SearchAsync(mount, "Mém. A n° 58 de 1991, article 5, 15 mars 2024"), "memorial article").GetProperty("opens").GetProperty("address").GetString());
    }

    [TestMethod]
    public async Task APermalinkOrAnEliOfAVersionOpensReadingAtTheVersionItNames()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var (query, address) in new[]
                 {
                     (fixture.Permalink + "/art_5", fixture.Permalink + "/art_5"),
                     ("https://law.soufien.lu/fr" + fixture.Permalink, fixture.Permalink),
                     ("/en" + fixture.Permalink + "/art_1er", fixture.Permalink + "/art_1er"),
                     (fixture.PermalinkOf(new string('0', 64)), fixture.PermalinkOf(new string('0', 64))),
                     ($"/lu-legilux/{fixture.WorkKey}/{fixture.ApplicabilityDate}", fixture.Permalink),
                     (WorkIri + "/jo", fixture.Permalink),
                     (WorkIri + "/jo/fr", fixture.Permalink),
                     (WorkIri + "/jo/fr, article 5", fixture.Permalink + "/art_5"),
                     (Readable(fixture, "art_5"), Readable(fixture, "art_5")),
                     ("/fr" + Readable(fixture, "art_1er"), Readable(fixture, "art_1er")),
                     ($"/lu-legilux/{fixture.WorkKey}/fra", $"/lu-legilux/{fixture.WorkKey}/fra"),
                 })
        {
            var reference = Opened(await SearchAsync(mount, query), query);
            Assert.AreEqual("reading", reference.GetProperty("opens").GetProperty("page").GetString(), query);
            Assert.AreEqual(address, reference.GetProperty("opens").GetProperty("address").GetString(), query);
        }

        const string unheld = "/lu-legilux/loi-1991-08-10-n3/2020-01-01--" + "0000000000000000000000000000000000000000000000000000000000000000/fra";
        // The act is held and the version is not: the refusal says which act this site holds (the French review of #974's repair).
        var unheldVersion = Refused(await SearchAsync(mount, unheld), "identifier_unknown", unheld);
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", unheldVersion.GetProperty("recognised_held_act").GetString());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, unheldVersion.GetProperty("recognised_held_version").ValueKind, "the version is what is not held");
        const string german = WorkIri + "/jo/de";
        Refused(await SearchAsync(mount, german), "language_not_available", german);
    }

    [TestMethod]
    public async Task AQueryThatIsNoReferenceIsSearchedAsBeforeAndTheAnswerSaysThePhaseReadNone()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var query in new[] { "garantie locative", "Que disait l'art. L. 121-6 le 15 mars 2021?", "article 5", "Code du travail" })
        {
            var envelope = await SearchAsync(mount, query);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, query);
            var body = envelope.Result!.Value;
            Assert.AreEqual("no_reference", body.GetProperty("reference").GetProperty("outcome").GetString(), query);
            Assert.IsTrue(body.GetProperty("lanes_run").GetBoolean(), query);
            Assert.AreEqual(query, body.GetProperty("requested_query").GetString(), query);
            Assert.IsTrue(body.TryGetProperty("population", out _), query);
        }

        // With a work named, the phase is not run: the query is searched as words within it.
        var named = (await SearchAsync(mount, "loi du 10 août 1991", identifier: $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;
        Assert.AreEqual("not_run_identifier_given", named.GetProperty("reference").GetProperty("outcome").GetString());
        Assert.IsTrue(named.GetProperty("lanes_run").GetBoolean());
    }

    [TestMethod]
    public async Task NoProtectedTokenOfTheGoldSetIsDroppedByTheSearch()
    {
        // R90's protected tokens through the whole search: a reference keeps each token in what it recognised (opened or refused);
        // a query that is no reference is searched as typed, the strict lane matching the whole query.
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        foreach (var (query, act, article, date) in ProtectedTokenGoldSet.Cases)
        {
            var envelope = await SearchAsync(mount, query);
            if (act is null)
            {
                Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, query);
                var body = envelope.Result!.Value;
                Assert.IsTrue(body.GetProperty("lanes_run").GetBoolean(), query);
                Assert.AreEqual(query, body.GetProperty("requested_query").GetString(), $"{query}: searched as typed");
                continue;
            }

            if (envelope.Result is { } words && words.Value.GetProperty("lanes_run").GetBoolean())
            {
                // A citation by a title the index holds no act under is searched as words, as typed (F1 of #974's review).
                Assert.AreEqual("title_not_held", words.Value.GetProperty("reference").GetProperty("reason").GetString(), query);
                Assert.AreEqual(query, words.Value.GetProperty("requested_query").GetString(), query);
                continue;
            }

            if (envelope.Refusal is { } refusal)
            {
                var payload = refusal.HelpfulPayload;
                Assert.IsTrue(payload.TryGetProperty("recognised_act", out _), $"{query}: {refusal.Code}");
                if (article is not null)
                {
                    // The number is said as the act's language prints it (1er, L. 121-6); its key is the gold set's.
                    Assert.AreEqual(article, ArticleNumbers.FromCitation(payload.GetProperty("recognised_article").GetString()!)?.Key, query);
                }

                if (date is not null)
                {
                    Assert.AreEqual(date, payload.GetProperty("recognised_date").GetString(), query);
                }

                continue;
            }

            var recognised = envelope.Result!.Value.GetProperty("reference").GetProperty("recognised");
            Assert.AreEqual(act, recognised.GetProperty("act").GetProperty("as_typed").GetString(), query);
            if (article is not null)
            {
                Assert.AreEqual(article, recognised.GetProperty("article").GetProperty("key").GetString(), query);
            }

            if (date is not null)
            {
                Assert.AreEqual(date, recognised.GetProperty("date").GetProperty("date").GetString(), query);
            }
        }
    }
}
