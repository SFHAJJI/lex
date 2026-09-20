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
/// The sixth R6 temporal operation, driven through the real handler on a verified mount:
/// <c>as_of</c> across the mounted works. For every work and language the state on the greatest
/// publisher date at or before the requested date, by the one selection rule; the derivation and
/// the fixed caveat in every answer, since the index holds no repeal and the operation never says a
/// work was in force; an ambiguous work as a row that says so; rows cut only between works.
/// </summary>
[TestClass]
public sealed class V3CorpusInForceOnMountTests
{
    private const string SelectionRawTarget = "/api/v3/in_force_on";
    private const string AsOfRawTarget = "/api/v3/as_of";

    [TestMethod]
    public async Task EachWorkAndLanguageAnswersWithTheStateAsOfSelectsAndSaysHowItWasDerived()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var between = await SelectionAsync(mount, Shift(fixture.ApplicabilityDate, 100));

        Assert.AreEqual(V3Verdicts.Answer, between.Verdict);
        Assert.AreEqual("version_state", between.Result!.ObjectType);
        Assert.AreEqual(PublisherId.LuLegilux, between.Context.Publisher);
        Assert.AreEqual("lu", between.Context.Jurisdiction);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, between.Context.TimelineSemantics);
        var body = between.Result.Value;
        Assert.AreEqual(
            "a row is the text the publisher dates as applicable on the requested date; nothing is said about " +
            "legal status, repeal or commencement",
            body.GetProperty("caveat").GetString());
        // The operation id is the registry's; no served string speaks of legal force, and no status is served.
        Assert.IsFalse(body.GetRawText().Contains("force", StringComparison.OrdinalIgnoreCase), body.GetRawText());
        Assert.IsFalse(body.TryGetProperty("status", out _));
        var derivation = body.GetProperty("derivation");
        Assert.AreEqual("versioned works only", derivation.GetProperty("basis").GetString());
        StringAssert.Contains(derivation.GetProperty("rule").GetString(), "greatest publisher applicability date at or before");
        StringAssert.Contains(derivation.GetProperty("interval_until").GetString(), "no end date is published or invented");
        CollectionAssert.AreEqual(
            new[] { "repeal", "end of validity", "commencement" },
            derivation.GetProperty("not_consulted").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual("lu-legilux", body.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.CorpusSha256, body.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName)))),
            body.GetProperty("index_sha256").GetString());

        var row = body.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.WorkKey, row.GetProperty("work_key").GetString());
        Assert.AreEqual("fra", row.GetProperty("language").GetString());
        Assert.AreEqual(fixture.StateSha256, row.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, row.GetProperty("interval").GetProperty("from").GetString());
        Assert.AreEqual(laterDate, row.GetProperty("interval").GetProperty("until").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("reason").ValueKind);

        // The row is the state as_of selects for that work, date and language, and its pointer reaches it.
        var asOf = await PostAsync(mount, AsOfRawTarget, JsonSerializer.Serialize(new
        {
            operation_id = "as_of",
            parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = Shift(fixture.ApplicabilityDate, 100) },
        }));
        var asOfState = V3EnvelopeJson.ParseAndVerify(ResponseBytes(asOf), V3OperationRegistry.Reviewed)
            .Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(asOfState.GetProperty("state_sha256").GetString(), row.GetProperty("state").GetProperty("state_sha256").GetString());
        var resolved = await ResolveAsync(mount, row.GetProperty("resolve"));
        Assert.AreEqual(fixture.StateSha256, resolved.Result!.Value.GetProperty("state_sha256").GetString());

        // On the later state's own date it is the later state, and nothing bounds it: no end is invented.
        var onLater = (await SelectionAsync(mount, laterDate)).Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(later.StateSha256, onLater.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(JsonValueKind.Null, onLater.GetProperty("interval").GetProperty("until").ValueKind);
        // The day before it, the earlier state still applies.
        var dayBefore = (await SelectionAsync(mount, Shift(laterDate, -1))).Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.StateSha256, dayBefore.GetProperty("state").GetProperty("state_sha256").GetString());
    }

    [TestMethod]
    public async Task ADateBeforeEverythingHeldIsAnAnswerWithNoRowsThatSaysSo()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var before = await SelectionAsync(mount, Shift(fixture.ApplicabilityDate, -1));

        Assert.AreEqual(V3Verdicts.Answer, before.Verdict, "Nothing applicable is an answer, not a refusal.");
        var body = before.Result!.Value;
        Assert.AreEqual(0, body.GetProperty("states").GetArrayLength());
        var population = body.GetProperty("population");
        Assert.IsTrue(population.GetProperty("date_is_before_everything_held").GetBoolean());
        Assert.AreEqual(1, population.GetProperty("works_held").GetInt64());
        Assert.AreEqual(0, population.GetProperty("works_with_a_state_on_date").GetInt64());
        Assert.AreEqual(1, population.GetProperty("works_beginning_later").GetInt64());
        Assert.AreEqual(fixture.ApplicabilityDate, population.GetProperty("first_date_held").GetString());

        // On the first date held the work answers, and the population says so.
        var onFirst = (await SelectionAsync(mount, fixture.ApplicabilityDate)).Result!.Value;
        Assert.AreEqual(1, onFirst.GetProperty("states").GetArrayLength());
        Assert.IsFalse(onFirst.GetProperty("population").GetProperty("date_is_before_everything_held").GetBoolean());
        Assert.AreEqual(1, onFirst.GetProperty("population").GetProperty("works_with_a_state_on_date").GetInt64());

        // With the work named the question is about that work, and as_of's refusal is kept: one work
        // and one date must not have an answer in one operation and none in the other.
        var named = await SelectionAsync(mount, Shift(fixture.ApplicabilityDate, -1), identifier: $"/lu-legilux/{fixture.WorkKey}");
        Assert.AreEqual(V3Verdicts.Refuse, named.Verdict);
        Assert.AreEqual("no_version_for_date", named.Refusal!.Code);
        AssertSaysNothingOfForce(named.Refusal.HelpfulPayload);
        Assert.AreEqual(PublisherId.LuLegilux, named.Context.Publisher);
        var asOf = await PostAsync(mount, AsOfRawTarget, JsonSerializer.Serialize(new
        {
            operation_id = "as_of",
            parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = Shift(fixture.ApplicabilityDate, -1) },
        }));
        var asOfRefusal = V3EnvelopeJson.ParseAndVerify(ResponseBytes(asOf), V3OperationRegistry.Reviewed).Refusal!;
        Assert.AreEqual(asOfRefusal.Code, named.Refusal.Code);
        Assert.AreEqual(asOfRefusal.HelpfulPayload.GetRawText(), named.Refusal.HelpfulPayload.GetRawText(),
            "The two operations give the same refusal payload for the same work and date.");
        Assert.AreEqual(fixture.ApplicabilityDate, named.Refusal.HelpfulPayload.GetProperty("history_begins").GetString());

        // The boundary itself: on the first date the named work holds, the day the text began to
        // apply, it answers with that state, as as_of does; only the day before refuses.
        var namedOnFirst = await SelectionAsync(mount, fixture.ApplicabilityDate, identifier: $"/lu-legilux/{fixture.WorkKey}");
        Assert.AreEqual(V3Verdicts.Answer, namedOnFirst.Verdict);
        var firstRow = namedOnFirst.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.StateSha256, firstRow.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, firstRow.GetProperty("interval").GetProperty("from").GetString());
        var asOfOnFirst = await PostAsync(mount, AsOfRawTarget, JsonSerializer.Serialize(new
        {
            operation_id = "as_of",
            parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate },
        }));
        Assert.AreEqual(V3Verdicts.Answer,
            V3EnvelopeJson.ParseAndVerify(ResponseBytes(asOfOnFirst), V3OperationRegistry.Reviewed).Verdict);
    }

    [TestMethod]
    public async Task AWorkWithTwoStatesOnTheSelectedDateIsARowThatNamesNoneAndTheOthersStillAnswer()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twinDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(twinDate, "later");
        var twin = await fixture.AddStateAsync(twinDate, "twin");
        var other = await fixture.AddStateAsync(fixture.ApplicabilityDate, "other-work", workLeaf: "n4");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await SelectionAsync(mount, Shift(twinDate, 10));

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, "One ambiguous work does not silence the others.");
        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.AreEqual(2, rows.Length);
        var ambiguous = rows.Single(row => row.GetProperty("work_key").GetString() == fixture.WorkKey);
        Assert.AreEqual("ambiguous_version", ambiguous.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, ambiguous.GetProperty("state").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, ambiguous.GetProperty("resolve").ValueKind);
        var coordinate = fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + twinDate) + "--";
        CollectionAssert.AreEqual(
            new[] { coordinate + later.StateSha256, coordinate + twin.StateSha256 }.Order(StringComparer.Ordinal).ToArray(),
            ambiguous.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(twinDate, ambiguous.GetProperty("interval").GetProperty("from").GetString());

        var clean = rows.Single(row => row.GetProperty("work_key").GetString() == other.WorkKey);
        Assert.AreEqual(other.StateSha256, clean.GetProperty("state").GetProperty("state_sha256").GetString());
        Assert.AreEqual(JsonValueKind.Null, clean.GetProperty("reason").ValueKind);

        // With the work named the question is as_of's, and so is the refusal, byte for byte: one work
        // and one date must not refuse in one operation and answer in the other. The other work being
        // held changes nothing, and neither does naming the clean work.
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var named = await SelectionAsync(mount, Shift(twinDate, 10), identifier: identifier);
        Assert.AreEqual(V3Verdicts.Refuse, named.Verdict);
        Assert.AreEqual("ambiguous_version", named.Refusal!.Code);
        AssertSaysNothingOfForce(named.Refusal.HelpfulPayload);
        Assert.AreEqual(PublisherId.LuLegilux, named.Context.Publisher);
        var asOf = await PostAsync(mount, AsOfRawTarget, JsonSerializer.Serialize(new
        {
            operation_id = "as_of",
            parameters = new { identifier, date = Shift(twinDate, 10) },
        }));
        var asOfRefusal = V3EnvelopeJson.ParseAndVerify(ResponseBytes(asOf), V3OperationRegistry.Reviewed).Refusal!;
        Assert.AreEqual("ambiguous_version", asOfRefusal.Code);
        Assert.AreEqual(asOfRefusal.HelpfulPayload.GetRawText(), named.Refusal.HelpfulPayload.GetRawText(),
            "The two operations give the same refusal payload for the same work and date.");
        CollectionAssert.AreEqual(
            new[] { coordinate + later.StateSha256, coordinate + twin.StateSha256 }.Order(StringComparer.Ordinal).ToArray(),
            named.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
        var namedClean = await SelectionAsync(mount, Shift(twinDate, 10), identifier: $"/lu-legilux/{other.WorkKey}");
        Assert.AreEqual(V3Verdicts.Answer, namedClean.Verdict);
        var namedBefore = await SelectionAsync(mount, Shift(twinDate, -1), identifier: identifier);
        Assert.AreEqual(V3Verdicts.Answer, namedBefore.Verdict, "Before the twins' date the named work is not ambiguous.");
        Assert.AreEqual(fixture.StateSha256,
            namedBefore.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state").GetProperty("state_sha256").GetString());

        // Before the twins' date the first work is not ambiguous.
        var earlier = (await SelectionAsync(mount, Shift(twinDate, -1))).Result!.Value.GetProperty("states").EnumerateArray()
            .Single(row => row.GetProperty("work_key").GetString() == fixture.WorkKey);
        Assert.AreEqual(fixture.StateSha256, earlier.GetProperty("state").GetProperty("state_sha256").GetString());
    }

    [TestMethod]
    public async Task RowsAreCutOnlyBetweenWorksAndTheNextRequestNeitherRepeatsNorSkipsAWork()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // Three works in key order: the fixture's (n3) with a German state too, then n4 and n5.
        await fixture.AddSecondLanguageStateAtSameDateAsync();
        await fixture.AddStateAsync(fixture.ApplicabilityDate, "work-four", workLeaf: "n4");
        await fixture.AddStateAsync(fixture.ApplicabilityDate, "work-five", workLeaf: "n5");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var date = Shift(fixture.ApplicabilityDate, 10);

        var everything = (await SelectionAsync(mount, date)).Result!.Value;
        var all = everything.GetProperty("states").EnumerateArray().Select(RowKey).ToArray();
        Assert.AreEqual(4, all.Length, "Two languages of the first work, then one row each for the other two.");
        CollectionAssert.AreEqual(all.Order(StringComparer.Ordinal).ToArray(), all, "Rows are in work key and language order.");
        Assert.IsFalse(everything.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, everything.GetProperty("continue_after").ValueKind);
        Assert.AreEqual(200, everything.GetProperty("limit").GetInt32());
        Assert.AreEqual(3, everything.GetProperty("population").GetProperty("works_with_a_state_on_date").GetInt64());

        // A limit of three: the first work's two rows and the second work's one fill it exactly, with a work still to come.
        var exactFill = (await SelectionAsync(mount, date, limit: 3)).Result!.Value;
        Assert.AreEqual(3, exactFill.GetProperty("states").GetArrayLength());
        Assert.IsTrue(exactFill.GetProperty("truncated").GetBoolean());
        Assert.IsFalse(exactFill.GetProperty("whole_work_over_limit").GetBoolean());
        Assert.AreEqual(3, exactFill.GetProperty("population").GetProperty("works_with_a_state_on_date").GetInt64(),
            "The population counts the date, not the rows served.");

        // A limit of one falls inside the first work: the work is served whole and says so.
        var firstPage = (await SelectionAsync(mount, date, limit: 1)).Result!.Value;
        Assert.AreEqual(2, firstPage.GetProperty("states").GetArrayLength(), "A work's languages are never separated.");
        Assert.IsTrue(firstPage.GetProperty("whole_work_over_limit").GetBoolean());
        Assert.IsTrue(firstPage.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(fixture.WorkKey, firstPage.GetProperty("continue_after").GetString());

        // Paging from the cursor serves the rest, with no work repeated and none skipped.
        var pages = new List<string>(firstPage.GetProperty("states").EnumerateArray().Select(RowKey));
        var after = firstPage.GetProperty("continue_after").GetString();
        while (after is not null)
        {
            // A cursor that does not advance would page forever; a wrong cursor must fail, not hang.
            Assert.IsLessThanOrEqualTo(all.Length, pages.Count, "The cursor repeated rows instead of advancing.");
            var page = (await SelectionAsync(mount, date, limit: 1, afterWorkKey: after)).Result!.Value;
            pages.AddRange(page.GetProperty("states").EnumerateArray().Select(RowKey));
            after = page.GetProperty("truncated").GetBoolean() ? page.GetProperty("continue_after").GetString() : null;
        }

        CollectionAssert.AreEqual(all, pages.ToArray());
    }

    [TestMethod]
    public async Task ANamedWorksTwinsAreJudgedInTheLanguageAskedForAndEveryAmbiguousLanguageIsNamed()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        var twinDate = Shift(fixture.ApplicabilityDate, 400);
        var asked = Shift(twinDate, 10);
        var germanLater = await fixture.AddStateAsync(twinDate, "de-later", german.ExpressionIri);
        var germanTwin = await fixture.AddStateAsync(twinDate, "de-twin", german.ExpressionIri);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        async Task<V3Envelope> AsOfAsync(V3CorpusMount mount, string? language)
        {
            var parameters = new Dictionary<string, object> { ["identifier"] = identifier, ["date"] = asked };
            if (language is not null)
            {
                parameters["language"] = language;
            }

            var context = await PostAsync(mount, AsOfRawTarget, JsonSerializer.Serialize(new { operation_id = "as_of", parameters }));
            return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        }

        static string[] Candidates(V3Envelope refused) =>
            refused.Refusal!.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()!).ToArray();

        // German has twins on the date and French has one state. The language asked for is the
        // question: French answers, German refuses, and as_of agrees on both.
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var french = await SelectionAsync(mount, asked, identifier: identifier, language: "fra");
            Assert.AreEqual(V3Verdicts.Answer, french.Verdict, "Another language's twins do not refuse the language asked for.");
            Assert.AreEqual(fixture.StateSha256,
                french.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state").GetProperty("state_sha256").GetString());
            Assert.AreEqual(V3Verdicts.Answer, (await AsOfAsync(mount, "fra")).Verdict);

            var germanOnly = await SelectionAsync(mount, asked, identifier: identifier, language: "deu");
            Assert.AreEqual("ambiguous_version", germanOnly.Refusal!.Code);
            var asOfGerman = await AsOfAsync(mount, "deu");
            Assert.AreEqual(asOfGerman.Refusal!.HelpfulPayload.GetRawText(), germanOnly.Refusal.HelpfulPayload.GetRawText());
            Assert.AreEqual(2, Candidates(germanOnly).Length);
            Assert.IsTrue(Candidates(germanOnly).All(candidate =>
                candidate.EndsWith(germanLater.StateSha256, StringComparison.Ordinal) ||
                candidate.EndsWith(germanTwin.StateSha256, StringComparison.Ordinal)));
        }

        // Now French has twins too. With no language asked for, every ambiguous language's candidates
        // are named, all four in ordinal order, as as_of names them.
        var frenchLater = await fixture.AddStateAsync(twinDate, "later");
        var frenchTwin = await fixture.AddStateAsync(twinDate, "twin");
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var both = await SelectionAsync(mount, asked, identifier: identifier);
            Assert.AreEqual("ambiguous_version", both.Refusal!.Code);
            var candidates = Candidates(both);
            Assert.AreEqual(4, candidates.Length);
            CollectionAssert.AreEqual(candidates.Order(StringComparer.Ordinal).ToArray(), candidates);
            foreach (var state in new[] { germanLater, germanTwin, frenchLater, frenchTwin })
            {
                Assert.AreEqual(1, candidates.Count(candidate => candidate.EndsWith(state.StateSha256, StringComparison.Ordinal)));
            }

            var asOfBoth = await AsOfAsync(mount, null);
            Assert.AreEqual("ambiguous_version", asOfBoth.Refusal!.Code);
            Assert.AreEqual(asOfBoth.Refusal.HelpfulPayload.GetRawText(), both.Refusal.HelpfulPayload.GetRawText(),
                "The two operations give the same refusal payload for the same work and date.");
        }
    }

    [TestMethod]
    public async Task WithAWorkNamedTheCursorThatNamesThatWorkServesNothingMore()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        // The cursor is the last work key served. A reader who pages a named work with the cursor
        // they were given must not read its rows twice.
        var after = await SelectionAsync(mount, fixture.ApplicabilityDate, identifier: identifier, afterWorkKey: fixture.WorkKey);
        Assert.AreEqual(V3Verdicts.Answer, after.Verdict);
        Assert.AreEqual(0, after.Result!.Value.GetProperty("states").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, after.Result.Value.GetProperty("continue_after").ValueKind);
        Assert.AreEqual(1, after.Result.Value.GetProperty("population").GetProperty("works_with_a_state_on_date").GetInt64(),
            "The population is the scope asked for, not the page.");

        // A cursor that sorts before the work serves it; one that sorts after serves nothing.
        var before = await SelectionAsync(mount, fixture.ApplicabilityDate, identifier: identifier, afterWorkKey: fixture.WorkKey[..^1]);
        Assert.AreEqual(fixture.StateSha256,
            before.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state").GetProperty("state_sha256").GetString());
        var beyond = await SelectionAsync(mount, fixture.ApplicabilityDate, identifier: identifier, afterWorkKey: fixture.WorkKey + "0");
        Assert.AreEqual(0, beyond.Result!.Value.GetProperty("states").GetArrayLength());
    }

    [TestMethod]
    public async Task ThePopulationIsTheScopeAskedForAndALanguageSelectsItsOwnStates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var germanDate = Shift(fixture.ApplicabilityDate, 400);
        var german = await fixture.AddSecondLanguageStateAsync(germanDate);
        await fixture.AddStateAsync(fixture.ApplicabilityDate, "other-work", workLeaf: "n4");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var between = Shift(fixture.ApplicabilityDate, 100);

        var whole = (await SelectionAsync(mount, between)).Result!.Value;
        Assert.AreEqual(2, whole.GetProperty("states").GetArrayLength(), "Both works in French; German has not begun.");
        Assert.AreEqual(2, whole.GetProperty("population").GetProperty("works_held").GetInt64());
        Assert.AreEqual(JsonValueKind.Null, whole.GetProperty("population").GetProperty("works_holding_the_language").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, whole.GetProperty("population").GetProperty("works_without_the_language").ValueKind);
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            whole.GetProperty("population").GetProperty("languages_held").EnumerateArray().Select(static v => v.GetString()).ToArray());

        // In German the scope is German: one work holds it, and on this date none has begun.
        var germanEarly = (await SelectionAsync(mount, between, language: "deu")).Result!.Value;
        Assert.AreEqual(0, germanEarly.GetProperty("states").GetArrayLength());
        var germanPopulation = germanEarly.GetProperty("population");
        Assert.AreEqual("deu", germanPopulation.GetProperty("scope").GetProperty("language").GetString());
        // Two kinds of absence, counted apart: one work holds German and begins later; the other holds no German.
        Assert.AreEqual(2, germanPopulation.GetProperty("works_held").GetInt64());
        Assert.AreEqual(1, germanPopulation.GetProperty("works_holding_the_language").GetInt64());
        Assert.AreEqual(1, germanPopulation.GetProperty("works_without_the_language").GetInt64());
        Assert.AreEqual(1, germanPopulation.GetProperty("works_beginning_later").GetInt64());
        Assert.AreEqual(0, germanPopulation.GetProperty("works_with_a_state_on_date").GetInt64());
        Assert.IsTrue(germanPopulation.GetProperty("date_is_before_everything_held").GetBoolean());
        var germanLater = (await SelectionAsync(mount, germanDate, language: "deu")).Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(german.StateSha256, germanLater.GetProperty("state").GetProperty("state_sha256").GetString());

        // With the work named, the scope is that work and both its languages answer once German has begun.
        var oneWork = (await SelectionAsync(mount, germanDate, identifier: $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", oneWork.GetProperty("population").GetProperty("scope").GetProperty("identifier").GetString());
        Assert.AreEqual(1, oneWork.GetProperty("population").GetProperty("works_held").GetInt64());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            oneWork.GetProperty("states").EnumerateArray().Select(static row => row.GetProperty("language").GetString()).ToArray());
    }

    /// <summary>
    /// A refusal payload says nothing of legal force. The mode tag is the operation's own name, fixed
    /// by the contract, and is set aside before looking; the identifier asked for is the reader's.
    /// </summary>
    private static void AssertSaysNothingOfForce(JsonElement payload)
    {
        var served = payload.GetRawText().Replace("\"r6_in_force_on\"", string.Empty, StringComparison.Ordinal);
        Assert.IsFalse(served.Contains("force", StringComparison.OrdinalIgnoreCase), served);
    }

    [TestMethod]
    public async Task TheIdentifierLanguageAndMountFamiliesRefuseAsTheOtherTemporalOperationsDo()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var eu = await SelectionAsync(mount, "2020-04-15", identifier: "32016R0679");
            Assert.AreEqual("retrieval_mode_unavailable", eu.Refusal!.Code);
            Assert.AreEqual("r6_in_force_on", eu.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
            // The mode tag mirrors the operation id, as every R6 tag does; it is the one place a
            // refusal payload says "force", and nothing else in any refusal here does.
            AssertSaysNothingOfForce(eu.Refusal.HelpfulPayload);
            Assert.AreEqual(PublisherId.EuEurLex, eu.Context.Publisher);
            var unknown = await SelectionAsync(mount, "2020-04-15", identifier: "/lu-legilux/no-such-work");
            Assert.AreEqual("identifier_unknown", unknown.Refusal!.Code);
            AssertSaysNothingOfForce(unknown.Refusal.HelpfulPayload);

            var english = await SelectionAsync(mount, "2020-04-15", language: "eng");
            Assert.AreEqual("language_not_available", english.Refusal!.Code);
            AssertSaysNothingOfForce(english.Refusal.HelpfulPayload);
            Assert.AreEqual(PublisherId.LuLegilux, english.Context.Publisher);
            CollectionAssert.AreEqual(new[] { "fra" },
                english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
            var englishOfOneWork = await SelectionAsync(mount, "2020-04-15", identifier: $"/lu-legilux/{fixture.WorkKey}", language: "eng");
            Assert.AreEqual("language_not_available", englishOfOneWork.Refusal!.Code);
        }

        var context = Body(SelectionRawTarget, "{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2020-04-15\"}}");
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var unmounted = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
        Assert.AreEqual("in_force_on", unmounted.OperationId);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-02-30\"}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2025-1-1\"}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"identifier\":\" \"}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"after_work_key\":\"\"}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"limit\":0}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"limit\":201}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"limit\":1.5}}")]
    [DataRow("{\"operation_id\":\"in_force_on\",\"parameters\":{\"date\":\"2024-01-01\",\"date_to\":\"2025-01-01\"}}")]
    public async Task UnusableSelectionRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        AssertTransportProblem(await PostAsync(mount, SelectionRawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndAQueryStringIsDrift()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var selectionBody = JsonSerializer.Serialize(new { operation_id = "in_force_on", parameters = new { date = fixture.ApplicabilityDate } });
        var asOfBody = JsonSerializer.Serialize(new { operation_id = "as_of", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate } });

        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, SelectionRawTarget, selectionBody)).Response.StatusCode);
        AssertTransportProblem(await PostAsync(mount, AsOfRawTarget, selectionBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, SelectionRawTarget, asOfBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, SelectionRawTarget + "?x=1", selectionBody), "unknown_route", StatusCodes.Status404NotFound);
    }

    private static string RowKey(JsonElement row) =>
        row.GetProperty("work_key").GetString() + "|" + row.GetProperty("language").GetString();

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> SelectionAsync(
        V3CorpusMount mount, string date, string? identifier = null, string? language = null, int? limit = null, string? afterWorkKey = null)
    {
        var parameters = new Dictionary<string, object> { ["date"] = date };
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

        if (afterWorkKey is not null)
        {
            parameters["after_work_key"] = afterWorkKey;
        }

        var context = await PostAsync(mount, SelectionRawTarget, JsonSerializer.Serialize(new { operation_id = "in_force_on", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<V3Envelope> ResolveAsync(V3CorpusMount mount, JsonElement parameters)
    {
        var context = await PostAsync(mount, "/api/v3/resolve", JsonSerializer.Serialize(new { operation_id = "resolve", parameters }));
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
        context.TraceIdentifier = "mounted-corpus-in-force-on";
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
