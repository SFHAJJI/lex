using System.Globalization;
using System.Security.Cryptography;
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
/// The fifth R6 temporal operation, driven through the real handler on a verified mount: the change
/// radar over a closed window. Every publisher-dated state in the window is a row with the baseline
/// it replaced and <c>wording_changed</c> from the article-level comparison <c>diff</c> makes; a row
/// that cannot be compared honestly says why and the radar still answers; rows are never cut within
/// a date; the population is the scope asked for; nothing is derived and the fixed caveat says so.
/// </summary>
[TestClass]
public sealed class V3CorpusChangesInPeriodMountTests
{
    private const string RadarRawTarget = "/api/v3/changes_in_period";
    private const string DiffRawTarget = "/api/v3/diff";

    [TestMethod]
    public async Task AWindowListsEachStateWithItsBaselineAndWhetherTheWordingChanged()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var own = fixture.ArticlesOfOwnState();
        Assert.IsGreaterThan(2, own.Count, "The fixture needs an article to change, one to rename and one to keep.");
        var amendedDate = Shift(fixture.ApplicabilityDate, 400);
        var copiedDate = Shift(fixture.ApplicabilityDate, 800);
        var amended = await fixture.AddStateAsync(amendedDate, "amended");
        await fixture.RewriteArticleTextAsync(amended.ExpressionIri, own[0].PublisherId, own[0].Text + " amended");
        await fixture.RenameArticleIdAsync(amended.ExpressionIri, own[1].PublisherId, own[1].PublisherId + "-new");
        // A later state that copies the fixture's own articles: against the amended baseline it changes back.
        var copied = await fixture.AddStateAsync(copiedDate, "copied");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await RadarAsync(mount, Shift(fixture.ApplicabilityDate, 1), Shift(copiedDate, 30));

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("change_list", envelope.Result!.ObjectType);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        var body = envelope.Result.Value;
        Assert.AreEqual(
            "a version row does not by itself assert a wording change, legal effect, or entry into force",
            body.GetProperty("caveat").GetString());
        Assert.AreEqual("lu-legilux", body.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.CorpusSha256, body.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName)))),
            body.GetProperty("index_sha256").GetString());
        StringAssert.Contains(body.GetProperty("wording_rule").GetString(), "stored token stream");

        var rows = body.GetProperty("changes").EnumerateArray().ToArray();
        Assert.AreEqual(2, rows.Length, "The fixture's own state is dated before the window, so it is a baseline and not a row.");
        var first = rows[0];
        Assert.AreEqual(fixture.WorkKey, first.GetProperty("work_key").GetString());
        Assert.AreEqual(amended.StateSha256, first.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(amendedDate, first.GetProperty("state").GetProperty("applicability_date").GetString());
        Assert.AreEqual(copiedDate, first.GetProperty("state").GetProperty("next_applicability_date").GetString());
        Assert.AreEqual(own.Count, first.GetProperty("state").GetProperty("article_count").GetInt32());
        Assert.AreEqual(fixture.StateSha256, first.GetProperty("baseline").GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.Permalink, first.GetProperty("baseline").GetProperty("permalink").GetString());
        Assert.IsTrue(first.GetProperty("wording_changed").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, first.GetProperty("reason").ValueKind);
        var counts = first.GetProperty("counts");
        Assert.AreEqual(1, counts.GetProperty("changed").GetInt32());
        Assert.AreEqual(1, counts.GetProperty("added").GetInt32());
        Assert.AreEqual(1, counts.GetProperty("removed").GetInt32());
        Assert.AreEqual(own.Count - 2, counts.GetProperty("unchanged").GetInt32());

        // The baseline of the second row is the amended state, not the fixture's own.
        var second = rows[1];
        Assert.AreEqual(copied.StateSha256, second.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(amended.StateSha256, second.GetProperty("baseline").GetProperty("state_sha256").GetString());
        Assert.IsTrue(second.GetProperty("wording_changed").GetBoolean());

        // The diff parameters a row carries ask diff for exactly that pair, and diff agrees with the counts.
        var link = first.GetProperty("diff");
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", link.GetProperty("identifier").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, link.GetProperty("date_from").GetString());
        Assert.AreEqual(amendedDate, link.GetProperty("date_to").GetString());
        var diffContext = await PostAsync(mount, DiffRawTarget, JsonSerializer.Serialize(new { operation_id = "diff", parameters = link }));
        Assert.AreEqual(StatusCodes.Status200OK, diffContext.Response.StatusCode);
        var comparison = V3EnvelopeJson.ParseAndVerify(ResponseBytes(diffContext), V3OperationRegistry.Reviewed)
            .Result!.Value.GetProperty("comparisons").EnumerateArray().Single();
        Assert.AreEqual(fixture.StateSha256, comparison.GetProperty("from").GetProperty("state_sha256").GetString());
        Assert.AreEqual(amended.StateSha256, comparison.GetProperty("to").GetProperty("state_sha256").GetString());
        Assert.AreEqual(counts.GetRawText(), comparison.GetProperty("counts").GetRawText());
        Assert.AreEqual(amendedDate, first.GetProperty("baseline").GetProperty("next_applicability_date").GetString(),
            "A baseline says the date it was replaced on, which is the row's.");

        // The row's resolve parameters reach the row's own state, in full.
        var resolved = await ResolveAsync(mount, first.GetProperty("resolve"));
        Assert.AreEqual(V3Verdicts.Answer, resolved.Verdict);
        Assert.AreEqual(amended.StateSha256, resolved.Result!.Value.GetProperty("state_sha256").GetString());
        Assert.AreEqual(own.Count, resolved.Result.Value.GetProperty("article_identities").GetArrayLength());
    }

    [TestMethod]
    public async Task AStateWhoseArticlesAreCopiedUnchangedIsARowThatSaysTheWordingDidNotChange()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "copied");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var row = (await RadarAsync(mount, laterDate, laterDate)).Result!.Value.GetProperty("changes").EnumerateArray().Single();

        Assert.IsFalse(row.GetProperty("wording_changed").GetBoolean(),
            "A version row is not a wording change: the copy has new identities and the same wording digests.");
        Assert.AreEqual(0, row.GetProperty("counts").GetProperty("changed").GetInt32());
        Assert.AreEqual(0, row.GetProperty("counts").GetProperty("added").GetInt32());
        Assert.AreEqual(0, row.GetProperty("counts").GetProperty("removed").GetInt32());
    }

    [TestMethod]
    public async Task TheWindowIsClosedAtBothEndsAndReadsTheSameWhicheverDateComesFirst()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var onBothEnds = await RadarAsync(mount, fixture.ApplicabilityDate, laterDate);
        Assert.AreEqual(2, onBothEnds.Result!.Value.GetProperty("changes").GetArrayLength(), "A state dated on either end is in the window.");
        Assert.AreEqual(0, (await RadarAsync(mount, Shift(fixture.ApplicabilityDate, 1), Shift(laterDate, -1)))
            .Result!.Value.GetProperty("changes").GetArrayLength());

        var reversed = await RadarAsync(mount, laterDate, fixture.ApplicabilityDate);
        var value = reversed.Result!.Value;
        Assert.AreEqual(laterDate, value.GetProperty("requested_date_from").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("window_from").GetString());
        Assert.AreEqual(laterDate, value.GetProperty("window_to").GetString());
        Assert.AreEqual(
            onBothEnds.Result.Value.GetProperty("changes").GetRawText(),
            value.GetProperty("changes").GetRawText(),
            "A window is the closed interval between two dates, whichever is given first.");
    }

    [TestMethod]
    public async Task TheFirstHeldStateHasNoBaselineAndSaysSo()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var row = (await RadarAsync(mount, fixture.ApplicabilityDate, fixture.ApplicabilityDate))
            .Result!.Value.GetProperty("changes").EnumerateArray().Single();

        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("wording_changed").ValueKind);
        Assert.AreEqual("first_held_state", row.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("baseline").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("diff").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("counts").ValueKind);
        Assert.AreEqual(fixture.StateSha256, row.GetProperty("state").GetProperty("state_sha256").GetString());
    }

    [TestMethod]
    public async Task TwinsAreEachARowNoneIsComparedAndALaterStateCannotTakeThemAsItsBaseline()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twinDate = Shift(fixture.ApplicabilityDate, 400);
        var afterDate = Shift(fixture.ApplicabilityDate, 800);
        var later = await fixture.AddStateAsync(twinDate, "later");
        var twin = await fixture.AddStateAsync(twinDate, "twin");
        var after = await fixture.AddStateAsync(afterDate, "after");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await RadarAsync(mount, twinDate, afterDate);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, "One ambiguous date does not silence the radar.");
        var rows = envelope.Result!.Value.GetProperty("changes").EnumerateArray().ToArray();
        Assert.AreEqual(3, rows.Length);
        var coordinate = fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + twinDate) + "--";
        var twins = new[] { coordinate + later.StateSha256, coordinate + twin.StateSha256 }.Order(StringComparer.Ordinal).ToArray();
        foreach (var row in rows.Take(2))
        {
            Assert.AreEqual(JsonValueKind.Null, row.GetProperty("wording_changed").ValueKind);
            Assert.AreEqual("ambiguous_version", row.GetProperty("reason").GetString());
            CollectionAssert.AreEqual(twins, row.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
            Assert.AreEqual(JsonValueKind.Null, row.GetProperty("diff").ValueKind);
            // The state before the twins' date is still named: the row says what it would have replaced.
            Assert.AreEqual(fixture.StateSha256, row.GetProperty("baseline").GetProperty("state_sha256").GetString());
        }

        CollectionAssert.AreEquivalent(
            new[] { later.StateSha256, twin.StateSha256 },
            rows.Take(2).Select(static row => row.GetProperty("state").GetProperty("state_sha256").GetString()).ToArray());
        // An ambiguous row still hands the reader a pointer that resolves: each twin's own permalink
        // reaches that twin and not the other, where as_of on the date refuses.
        foreach (var row in rows.Take(2))
        {
            var reached = await ResolveAsync(mount, row.GetProperty("resolve"));
            Assert.AreEqual(V3Verdicts.Answer, reached.Verdict);
            Assert.AreEqual(
                row.GetProperty("state").GetProperty("state_sha256").GetString(),
                reached.Result!.Value.GetProperty("state_sha256").GetString());
            Assert.IsFalse(row.TryGetProperty("as_of", out _), "No pointer that would be refused is offered.");
        }

        // The state after the twins: its baseline date has two states, so none is chosen.
        var last = rows[2];
        Assert.AreEqual(after.StateSha256, last.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(JsonValueKind.Null, last.GetProperty("wording_changed").ValueKind);
        Assert.AreEqual("ambiguous_baseline", last.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, last.GetProperty("baseline").ValueKind);
        CollectionAssert.AreEqual(twins, last.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
    }

    [TestMethod]
    public async Task ATwinOnTheEarliestDateHeldIsAmbiguousBeforeItIsFirst()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twin = await fixture.AddStateAsync(fixture.ApplicabilityDate, "twin-first");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var rows = (await RadarAsync(mount, fixture.ApplicabilityDate, fixture.ApplicabilityDate))
            .Result!.Value.GetProperty("changes").EnumerateArray().ToArray();

        Assert.AreEqual(2, rows.Length);
        var twins = new[] { fixture.Permalink, fixture.StableCoordinate + "--" + twin.StateSha256 }.Order(StringComparer.Ordinal).ToArray();
        foreach (var row in rows)
        {
            Assert.AreEqual("ambiguous_version", row.GetProperty("reason").GetString(),
                "Nothing precedes these states, but what decides the date is that it has two: as_of refuses it.");
            CollectionAssert.AreEqual(twins, row.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
            Assert.AreEqual(JsonValueKind.Null, row.GetProperty("baseline").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, row.GetProperty("wording_changed").ValueKind);
        }

        // as_of on that date refuses exactly what the rows predict.
        var asOf = await PostAsync(mount, "/api/v3/as_of", JsonSerializer.Serialize(new
        {
            operation_id = "as_of",
            parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate },
        }));
        Assert.AreEqual("ambiguous_version",
            V3EnvelopeJson.ParseAndVerify(ResponseBytes(asOf), V3OperationRegistry.Reviewed).Refusal!.Code);
    }

    [TestMethod]
    public async Task StatesWithDifferentRuleProfilesAreNeverComparedAndTheRowSaysSo()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        var extraProfile = new string('a', 64);
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, extraProfile);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var row = (await RadarAsync(mount, laterDate, laterDate)).Result!.Value.GetProperty("changes").EnumerateArray().Single();

        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("wording_changed").ValueKind);
        Assert.AreEqual("profiles_differ", row.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("counts").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("diff").ValueKind);
        Assert.AreEqual(fixture.StateSha256, row.GetProperty("baseline").GetProperty("state_sha256").GetString());
        CollectionAssert.Contains(
            row.GetProperty("state").GetProperty("rule_profile_sha256s").EnumerateArray().Select(static v => v.GetString()).ToArray(),
            extraProfile);
        CollectionAssert.DoesNotContain(
            row.GetProperty("baseline").GetProperty("rule_profile_sha256s").EnumerateArray().Select(static v => v.GetString()).ToArray(),
            extraProfile);
    }

    [TestMethod]
    public async Task RowsAreNeverCutWithinADateAndTheNextRequestNeitherRepeatsNorSkipsARow()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var sharedDate = Shift(fixture.ApplicabilityDate, 400);
        var lastDate = Shift(fixture.ApplicabilityDate, 800);
        // The fixture's own date holds one row; the shared date holds two (a French state and a German one).
        await fixture.AddSecondLanguageStateAsync(sharedDate);
        await fixture.AddStateAsync(sharedDate, "later");
        await fixture.AddStateAsync(lastDate, "last");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var everything = (await RadarAsync(mount, fixture.ApplicabilityDate, lastDate)).Result!.Value;
        Assert.AreEqual(4, everything.GetProperty("changes").GetArrayLength());
        Assert.IsFalse(everything.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, everything.GetProperty("continue_from").ValueKind);
        Assert.AreEqual(200, everything.GetProperty("limit").GetInt32());

        // A limit of two falls between the two rows of the shared date: the date is not cut.
        var firstPage = (await RadarAsync(mount, fixture.ApplicabilityDate, lastDate, limit: 2)).Result!.Value;
        Assert.AreEqual(1, firstPage.GetProperty("changes").GetArrayLength(), "Only the first date fits whole under a limit of two.");
        Assert.IsTrue(firstPage.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(sharedDate, firstPage.GetProperty("continue_from").GetString());
        Assert.IsFalse(firstPage.GetProperty("whole_date_over_limit").GetBoolean());
        Assert.AreEqual(4, firstPage.GetProperty("population").GetProperty("versions_in_window").GetInt32(),
            "The population counts the window, not the rows served.");

        // Continuing from the date named serves the rest, with no row repeated and none skipped.
        var pages = new List<string>(firstPage.GetProperty("changes").EnumerateArray().Select(StateDigest));
        var from = firstPage.GetProperty("continue_from").GetString();
        while (from is not null)
        {
            var page = (await RadarAsync(mount, from, lastDate, limit: 2)).Result!.Value;
            pages.AddRange(page.GetProperty("changes").EnumerateArray().Select(StateDigest));
            from = page.GetProperty("truncated").GetBoolean() ? page.GetProperty("continue_from").GetString() : null;
        }

        CollectionAssert.AreEqual(
            everything.GetProperty("changes").EnumerateArray().Select(StateDigest).ToArray(),
            pages.ToArray());

        // Dates that fill the limit exactly are served, not deferred: 1 + 2 rows under a limit of three,
        // with a date still to come.
        var exactFill = (await RadarAsync(mount, fixture.ApplicabilityDate, lastDate, limit: 3)).Result!.Value;
        Assert.AreEqual(3, exactFill.GetProperty("changes").GetArrayLength());
        Assert.IsTrue(exactFill.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(lastDate, exactFill.GetProperty("continue_from").GetString());
        Assert.IsFalse(exactFill.GetProperty("whole_date_over_limit").GetBoolean());

        // One date alone holding more rows than the limit is served whole, and the answer says so.
        var overLimit = (await RadarAsync(mount, sharedDate, sharedDate, limit: 1)).Result!.Value;
        Assert.AreEqual(2, overLimit.GetProperty("changes").GetArrayLength());
        Assert.IsTrue(overLimit.GetProperty("whole_date_over_limit").GetBoolean());
        Assert.IsFalse(overLimit.GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    public async Task ThePopulationIsTheScopeAskedForAndAnEmptyWindowSaysWhetherItOverlapsWhatIsHeld()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var germanDate = Shift(fixture.ApplicabilityDate, 400);
        var german = await fixture.AddSecondLanguageStateAsync(germanDate);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var whole = (await RadarAsync(mount, fixture.ApplicabilityDate, germanDate)).Result!.Value.GetProperty("population");
        Assert.AreEqual(1, whole.GetProperty("works_held").GetInt64());
        Assert.AreEqual(fixture.ApplicabilityDate, whole.GetProperty("first_date_held").GetString());
        Assert.AreEqual(germanDate, whole.GetProperty("last_date_held").GetString());
        Assert.AreEqual(2, whole.GetProperty("versions_in_window").GetInt32());
        Assert.AreEqual(1, whole.GetProperty("works_in_window").GetInt32());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            whole.GetProperty("languages_held").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(JsonValueKind.Null, whole.GetProperty("scope").GetProperty("language").ValueKind);

        // With a language, the dates held and the versions are that language's.
        var germanOnly = (await RadarAsync(mount, fixture.ApplicabilityDate, germanDate, language: "deu")).Result!.Value;
        var germanPopulation = germanOnly.GetProperty("population");
        Assert.AreEqual("deu", germanPopulation.GetProperty("scope").GetProperty("language").GetString());
        Assert.AreEqual(germanDate, germanPopulation.GetProperty("first_date_held").GetString());
        Assert.AreEqual(1, germanPopulation.GetProperty("versions_in_window").GetInt32());
        var germanRow = germanOnly.GetProperty("changes").EnumerateArray().Single();
        Assert.AreEqual(german.StateSha256, germanRow.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual("first_held_state", germanRow.GetProperty("reason").GetString(),
            "A baseline is a state of the same language: the French state is not the German text's predecessor.");

        // With an identifier the scope is that work, through the shared locator.
        var oneWork = (await RadarAsync(mount, fixture.ApplicabilityDate, germanDate, identifier: $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", oneWork.GetProperty("population").GetProperty("scope").GetProperty("identifier").GetString());
        Assert.AreEqual(2, oneWork.GetProperty("changes").GetArrayLength());

        // An empty window inside what is held, and one wholly outside it, are told apart.
        var inside = (await RadarAsync(mount, Shift(fixture.ApplicabilityDate, 1), Shift(germanDate, -1))).Result!.Value;
        Assert.AreEqual(0, inside.GetProperty("changes").GetArrayLength());
        Assert.IsTrue(inside.GetProperty("population").GetProperty("window_overlaps_what_is_held").GetBoolean());
        // A window that only touches the last date held, or the first, overlaps what is held.
        var touchingLast = (await RadarAsync(mount, germanDate, Shift(germanDate, 20))).Result!.Value;
        Assert.IsTrue(touchingLast.GetProperty("population").GetProperty("window_overlaps_what_is_held").GetBoolean());
        Assert.AreEqual(1, touchingLast.GetProperty("changes").GetArrayLength());
        var touchingFirst = (await RadarAsync(mount, Shift(fixture.ApplicabilityDate, -20), fixture.ApplicabilityDate)).Result!.Value;
        Assert.IsTrue(touchingFirst.GetProperty("population").GetProperty("window_overlaps_what_is_held").GetBoolean());
        Assert.AreEqual(1, touchingFirst.GetProperty("changes").GetArrayLength());
        var before = (await RadarAsync(mount, Shift(fixture.ApplicabilityDate, -20), Shift(fixture.ApplicabilityDate, -1))).Result!.Value;
        Assert.IsFalse(before.GetProperty("population").GetProperty("window_overlaps_what_is_held").GetBoolean());
        var outside = (await RadarAsync(mount, Shift(germanDate, 10), Shift(germanDate, 20))).Result!.Value;
        Assert.AreEqual(0, outside.GetProperty("changes").GetArrayLength());
        Assert.IsFalse(outside.GetProperty("population").GetProperty("window_overlaps_what_is_held").GetBoolean());
        Assert.AreEqual(V3Verdicts.Answer, (await RadarAsync(mount, Shift(germanDate, 10), Shift(germanDate, 20))).Verdict,
            "Nothing in a window is an answer, not a refusal.");
    }

    [TestMethod]
    public async Task TheIdentifierLanguageAndMountFamiliesRefuseAsTheOtherTemporalOperationsDo()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var eu = await RadarAsync(mount, "2018-05-25", "2024-01-01", identifier: "32016R0679");
            Assert.AreEqual("retrieval_mode_unavailable", eu.Refusal!.Code);
            Assert.AreEqual("r6_changes_in_period", eu.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
            Assert.AreEqual(PublisherId.EuEurLex, eu.Context.Publisher);
            var unknown = await RadarAsync(mount, "2018-05-25", "2024-01-01", identifier: "/lu-legilux/no-such-work");
            Assert.AreEqual("identifier_unknown", unknown.Refusal!.Code);

            var english = await RadarAsync(mount, "2018-05-25", "2024-01-01", language: "eng");
            Assert.AreEqual("language_not_available", english.Refusal!.Code);
            Assert.AreEqual(PublisherId.LuLegilux, english.Context.Publisher);
            CollectionAssert.AreEqual(new[] { "fra" },
                english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
            var englishOfOneWork = await RadarAsync(mount, "2018-05-25", "2024-01-01", identifier: $"/lu-legilux/{fixture.WorkKey}", language: "eng");
            Assert.AreEqual("language_not_available", englishOfOneWork.Refusal!.Code);
        }

        var context = Body(RadarRawTarget,
            "{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\"}}");
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var unmounted = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
        Assert.AreEqual("changes_in_period", unmounted.OperationId);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_to\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-02-30\",\"date_to\":\"2025-01-01\"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-1-1\"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"identifier\":\" \"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"limit\":0}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"limit\":201}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"limit\":1.5}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"limit\":\"2\"}}")]
    [DataRow("{\"operation_id\":\"changes_in_period\",\"parameters\":{\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"anchor\":\"art_1\"}}")]
    public async Task UnusableRadarRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        AssertTransportProblem(await PostAsync(mount, RadarRawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndAQueryStringIsDrift()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var radarBody = JsonSerializer.Serialize(new { operation_id = "changes_in_period", parameters = new { date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate } });
        var diffBody = JsonSerializer.Serialize(new { operation_id = "diff", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate } });

        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, RadarRawTarget, radarBody)).Response.StatusCode);
        AssertTransportProblem(await PostAsync(mount, DiffRawTarget, radarBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, RadarRawTarget, diffBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, RadarRawTarget + "?x=1", radarBody), "unknown_route", StatusCodes.Status404NotFound);
    }

    private static async Task<V3Envelope> ResolveAsync(V3CorpusMount mount, JsonElement parameters)
    {
        var context = await PostAsync(mount, "/api/v3/resolve", JsonSerializer.Serialize(new { operation_id = "resolve", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static string StateDigest(JsonElement row) =>
        row.GetProperty("state").GetProperty("state_sha256").GetString()!;

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> RadarAsync(
        V3CorpusMount mount, string dateFrom, string dateTo, string? identifier = null, string? language = null, int? limit = null)
    {
        var parameters = new Dictionary<string, object> { ["date_from"] = dateFrom, ["date_to"] = dateTo };
        if (identifier is not null)
        {
            parameters["identifier"] = identifier;
        }

        if (language is not null)
        {
            parameters["language"] = language;
        }

        if (limit is not null)
        {
            parameters["limit"] = limit.Value;
        }

        var context = await PostAsync(mount, RadarRawTarget, JsonSerializer.Serialize(new { operation_id = "changes_in_period", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var context = Body(rawTarget, body);
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static DefaultHttpContext Body(string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-changes-in-period";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
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
