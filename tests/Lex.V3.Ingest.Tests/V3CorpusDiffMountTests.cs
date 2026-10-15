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
/// The fourth R6 temporal operation, driven through the real handler on a verified mount: two dates
/// of one Luxembourg work resolved to their states as <c>as_of</c> resolves them, served in full, and
/// compared article by article by publisher-minted id and wording digest; the same-state note; the
/// non-overridable <c>profiles_differ</c>; each bound's refusals; and the shared refusals.
/// </summary>
[TestClass]
public sealed class V3CorpusDiffMountTests
{
    private const string DiffRawTarget = "/api/v3/diff";
    private const string AsOfRawTarget = "/api/v3/as_of";

    [TestMethod]
    public async Task OneStateCoveringBothDatesIsTheSameVersionOnBothDates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, Shift(fixture.ApplicabilityDate, 100));

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("diff", envelope.OperationId);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        Assert.AreEqual("diff", envelope.Result!.ObjectType);
        var value = envelope.Result.Value;
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("requested_date_from").GetString());
        Assert.AreEqual(Shift(fixture.ApplicabilityDate, 100), value.GetProperty("requested_date_to").GetString());
        Assert.AreEqual(fixture.WorkKey, value.GetProperty("work_key").GetString());
        Assert.AreEqual(0, value.GetProperty("languages_not_compared").GetArrayLength());
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "consecutive text merged");
        var comparison = value.GetProperty("comparisons").EnumerateArray().Single();
        Assert.AreEqual("fra", comparison.GetProperty("language").GetString());
        Assert.IsTrue(comparison.GetProperty("same_state").GetBoolean());
        // The fixture's act is held only as published (its one state is a /jo expression), so the one state is said as what
        // it is: the one text held, the act as published, with its publisher applicability start, never "applied" (R21) and
        // never one version covering both dates (the review of #964). The index holds no fact about later versions.
        Assert.AreEqual(
            $"Both dates select the one text held, the act as published, applicable from {fixture.ApplicabilityDate} (publisher); later versions are not held",
            comparison.GetProperty("note").GetString());
        Assert.DoesNotContain("applied", comparison.GetProperty("note").GetString()!);
        Assert.AreEqual(JsonValueKind.Null, comparison.GetProperty("articles").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, comparison.GetProperty("counts").ValueKind);
        var between = value.GetProperty("later_version_facts_between_dates");
        Assert.AreEqual(fixture.ApplicabilityDate, between.GetProperty("after").GetString());
        Assert.AreEqual(Shift(fixture.ApplicabilityDate, 100), between.GetProperty("on_or_before").GetString());
        Assert.AreEqual(0, between.GetProperty("consolidations").GetArrayLength());
        Assert.AreEqual(0, between.GetProperty("end_of_force").GetArrayLength());
        // Both bounds are served in full, as the state as_of serves on that date.
        foreach (var side in new[] { "from", "to" })
        {
            var state = comparison.GetProperty(side);
            Assert.AreEqual(fixture.StateSha256, state.GetProperty("state_sha256").GetString(), side);
            Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString(), side);
            Assert.IsTrue(state.TryGetProperty("articles", out _), side);
        }
        var asOf = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate);
        Assert.AreEqual(asOf.Result!.Value.GetProperty("states").EnumerateArray().Single().GetRawText(),
            comparison.GetProperty("from").GetRawText(), "The from side is the as_of state on that date, byte for byte.");
        // The comparison carries as_published as as_of does, from the same builder: the same member, byte for byte.
        Assert.AreEqual(asOf.Result.Value.GetProperty("as_published").GetRawText(), value.GetProperty("as_published").GetRawText());
    }

    /// <summary>
    /// The review of #964: every held Luxembourg act is held as its one /jo text, so any two dates on or after its date select
    /// that one state, while the publisher's own record can name a consolidated version, or an end-of-force date, between
    /// them (rgd-2020-04-08-a265 compared on 2020-04-10 and 2020-05-20, where /consolide/20200511 applies from 2020-05-11;
    /// in derive-c's index 376 of the 388 named consolidated versions, in 149 acts, start after the held state's date). The
    /// answer said "Both dates fall within the same version", which those facts contradict. It now says that both dates
    /// select the one text held, the act as published, and names the consolidated versions whose applicability date falls
    /// after the earlier date and on or before the later one, and the end-of-force dates that do, from the facts alone.
    /// </summary>
    [TestMethod]
    public async Task TheOneTextHeldOfAnActHeldOnlyAsPublishedNamesTheConsolidatedVersionsAndTheEndOfForceDateBetweenTheDates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var held = fixture.ApplicabilityDate;
        var work = PublisherWorkIriOf(fixture);
        const string xsdDate = "http://www.w3.org/2001/XMLSchema#date";
        // The straddling version, as rgd-2020-04-08-a265's: applicable from a date between the two dates, with its end.
        await fixture.AddWorkFactAsync($"{work}/consolide/straddling", "dateApplicability", "consolidation_applicability", "literal", Shift(held, 33), xsdDate);
        await fixture.AddWorkFactAsync($"{work}/consolide/straddling", "dateEndApplicability", "consolidation_applicability", "literal", Shift(held, 77), xsdDate);
        // On the later date itself: named (on or before it).
        await fixture.AddWorkFactAsync($"{work}/consolide/onthelater", "dateApplicability", "consolidation_applicability", "literal", Shift(held, 42), xsdDate);
        // On the earlier date itself, and after the held state's date: not named (it is not after the earlier date).
        await fixture.AddWorkFactAsync($"{work}/consolide/ontheearlier", "dateApplicability", "consolidation_applicability", "literal", Shift(held, 2), xsdDate);
        // After the later date, and with no single date: not named.
        await fixture.AddWorkFactAsync($"{work}/consolide/afterwards", "dateApplicability", "consolidation_applicability", "literal", Shift(held, 200), xsdDate);
        await fixture.AddWorkFactAsync($"{work}/consolide/undated", "dateEndApplicability", "consolidation_applicability", "literal", Shift(held, 20), xsdDate);
        // The publisher's end-of-force date, between the dates.
        await fixture.AddWorkFactAsync(work + "/jo", "dateNoLongerInForce", "act_force", "literal", Shift(held, 40), xsdDate);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        foreach (var (dateFrom, dateTo) in new[] { (Shift(held, 2), Shift(held, 42)), (Shift(held, 42), Shift(held, 2)) })
        {
            var envelope = await DiffAsync(mount, identifier, dateFrom, dateTo);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
            var value = envelope.Result!.Value;
            var comparison = value.GetProperty("comparisons").EnumerateArray().Single();
            Assert.IsTrue(comparison.GetProperty("same_state").GetBoolean(), "both dates select the one state held");
            var note = comparison.GetProperty("note").GetString()!;
            Assert.AreEqual(
                $"Both dates select the one text held, the act as published, applicable from {held} (publisher); later versions are not held; " +
                $"the publisher's record names 2 consolidated versions of it whose applicability dates fall after {Shift(held, 2)} and on or before " +
                $"{Shift(held, 42)}, applicable from {Shift(held, 33)} to {Shift(held, 77)} and from {Shift(held, 42)} (publisher); the publisher's " +
                $"record gives {Shift(held, 40)} as the act's end-of-force date (dateNoLongerInForce), which falls after {Shift(held, 2)} and on or " +
                $"before {Shift(held, 42)}",
                note, $"{dateFrom} against {dateTo}");
            Assert.DoesNotContain("same version", note, "never one version covering both dates");
            Assert.DoesNotContain("changed", note, "facts only: nothing is said of whether the text changed");
            foreach (var forbidden in new[] { " in force", "en vigueur", "applied" })
            {
                Assert.DoesNotContain(forbidden, note, $"the note says {forbidden} (R21)");
            }

            var between = value.GetProperty("later_version_facts_between_dates");
            Assert.AreEqual(Shift(held, 2), between.GetProperty("after").GetString());
            Assert.AreEqual(Shift(held, 42), between.GetProperty("on_or_before").GetString());
            CollectionAssert.AreEqual(
                new (string, string?, string?)[]
                {
                    ($"{work}/consolide/straddling", Shift(held, 33), Shift(held, 77)),
                    ($"{work}/consolide/onthelater", Shift(held, 42), null),
                },
                between.GetProperty("consolidations").EnumerateArray().Select(static row => ((string, string?, string?))(
                    row.GetProperty("legal_resource_iri").GetString()!,
                    row.GetProperty("applicability_date").GetString(),
                    row.GetProperty("end_applicability_date").GetString())).ToArray());
            var end = between.GetProperty("end_of_force").EnumerateArray().Single();
            Assert.AreEqual(work + "/jo", end.GetProperty("subject_iri").GetString());
            Assert.AreEqual(Shift(held, 40), end.GetProperty("value").GetString());
            // as_published lists all five consolidated versions, the one each answer about this act's states lists.
            Assert.AreEqual(5, value.GetProperty("as_published").GetProperty("later_version_facts").GetProperty("consolidations").GetArrayLength());
        }

        // Dates that straddle nothing the publisher dates name nothing, and say only what is held.
        var quiet = await DiffAsync(mount, identifier, Shift(held, 100), Shift(held, 150));
        Assert.AreEqual(
            $"Both dates select the one text held, the act as published, applicable from {held} (publisher); later versions are not held",
            quiet.Result!.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("note").GetString());
    }

    [TestMethod]
    public async Task ASameStateOfAnActHeldAsPublishedWithAnotherStateInItsLanguageIsTheSameTextHeldAndAConsolidatedStateKeepsTheSameVersion()
    {
        // Two states of the act as published in one language (no work in the population has two; the fixture can): both
        // dates before the later one select the earlier, which is then "the same text held", not "the one text held".
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, Shift(fixture.ApplicabilityDate, 100));
            Assert.AreEqual(
                $"Both dates select the same text held, the act as published, applicable from {fixture.ApplicabilityDate} (publisher); no consolidated version is held",
                envelope.Result!.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("note").GetString());
        }

        // A work whose consolidated state is held is not held only as published: the one state on both dates keeps the
        // publisher's clock for one version, and the answer carries neither member.
        var consolidated = await MountedFixture.CreateAsync();
        await using var cleanupConsolidated = consolidated;
        await consolidated.AddStateAsync(Shift(consolidated.ApplicabilityDate, 400), "fr", legalResourceLeaf: "consolide/20250301");
        using var consolidatedMount = await V3CorpusMount.OpenAsync(consolidated.Directory, CancellationToken.None);
        Assert.IsNotNull(consolidatedMount);
        var answer = (await DiffAsync(consolidatedMount, $"/lu-legilux/{consolidated.WorkKey}", consolidated.ApplicabilityDate, Shift(consolidated.ApplicabilityDate, 100))).Result!.Value;
        Assert.AreEqual(
            $"Both dates fall within the same version, applicable from {consolidated.ApplicabilityDate} (publisher)",
            answer.GetProperty("comparisons").EnumerateArray().Single().GetProperty("note").GetString());
        Assert.AreEqual(JsonValueKind.Null, answer.GetProperty("as_published").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, answer.GetProperty("later_version_facts_between_dates").ValueKind);
    }

    private static string PublisherWorkIriOf(MountedFixture fixture)
    {
        using var connection = Lex.V3.Ingest.Luxembourg.LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT publisher_work_iri FROM states WHERE work_key=$work LIMIT 1";
        command.Parameters.AddWithValue("$work", fixture.WorkKey);
        return (string)command.ExecuteScalar()!;
    }

    [TestMethod]
    public async Task TwoStatesAreComparedArticleByArticleByPublisherIdAndWordingDigest()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var own = fixture.ArticlesOfOwnState();
        Assert.IsGreaterThan(2, own.Count, "The fixture needs an article to change, one to rename and one to keep.");
        var changedId = own[0].PublisherId;
        var renamedId = own[1].PublisherId;
        var keptId = own[2].PublisherId;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        await fixture.RewriteArticleTextAsync(later.ExpressionIri, changedId, own[0].Text + " amended");
        await fixture.RenameArticleIdAsync(later.ExpressionIri, renamedId, renamedId + "-new");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(fixture.ApplicabilityDate, 10), Shift(laterDate, 10));

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        var body = envelope.Result!.Value;
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", body.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("requested_language").ValueKind);
        Assert.AreEqual("lu-legilux", body.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.CorpusSha256, body.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName)))),
            body.GetProperty("index_sha256").GetString(),
            "The digest of the index as mounted, which the added state changed.");
        var comparison = body.GetProperty("comparisons").EnumerateArray().Single();
        Assert.IsFalse(comparison.GetProperty("same_state").GetBoolean());
        StringAssert.Contains(comparison.GetProperty("note").GetString(), "nothing about legal effect is asserted");
        Assert.AreEqual(fixture.StateSha256, comparison.GetProperty("from").GetProperty("state_sha256").GetString());
        Assert.AreEqual(later.StateSha256, comparison.GetProperty("to").GetProperty("state_sha256").GetString());
        Assert.AreEqual(laterDate, comparison.GetProperty("from").GetProperty("next_applicability_date").GetString());
        var byId = comparison.GetProperty("articles").EnumerateArray()
            .ToDictionary(static row => row.GetProperty("publisher_id").GetString()!, static row => row);
        Assert.AreEqual("changed", byId[changedId].GetProperty("status").GetString());
        Assert.AreNotEqual(
            byId[changedId].GetProperty("from").EnumerateArray().Single().GetProperty("wording_sha256").GetString(),
            byId[changedId].GetProperty("to").EnumerateArray().Single().GetProperty("wording_sha256").GetString());
        Assert.AreEqual("removed", byId[renamedId].GetProperty("status").GetString());
        Assert.AreEqual(0, byId[renamedId].GetProperty("to").GetArrayLength());
        Assert.AreEqual("added", byId[renamedId + "-new"].GetProperty("status").GetString());
        Assert.AreEqual(0, byId[renamedId + "-new"].GetProperty("from").GetArrayLength());
        Assert.AreEqual("unchanged", byId[keptId].GetProperty("status").GetString());
        Assert.AreEqual(
            byId[keptId].GetProperty("from").EnumerateArray().Single().GetProperty("wording_sha256").GetString(),
            byId[keptId].GetProperty("to").EnumerateArray().Single().GetProperty("wording_sha256").GetString());
        Assert.AreNotEqual(
            byId[keptId].GetProperty("from").EnumerateArray().Single().GetProperty("article_identity_sha256").GetString(),
            byId[keptId].GetProperty("to").EnumerateArray().Single().GetProperty("article_identity_sha256").GetString(),
            "Identities differ between states; the digest, not the identity, decides.");
        var counts = comparison.GetProperty("counts");
        Assert.AreEqual(1, counts.GetProperty("changed").GetInt32());
        Assert.AreEqual(1, counts.GetProperty("added").GetInt32());
        Assert.AreEqual(1, counts.GetProperty("removed").GetInt32());
        Assert.AreEqual(own.Count - 2, counts.GetProperty("unchanged").GetInt32());
        // Rows are in publisher-id order.
        var ids = comparison.GetProperty("articles").EnumerateArray().Select(static row => row.GetProperty("publisher_id").GetString()!).ToArray();
        CollectionAssert.AreEqual(ids.Order(StringComparer.Ordinal).ToArray(), ids);

        // The bounds swapped: added and removed swap, changed stays changed.
        var reversed = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(laterDate, 10), Shift(fixture.ApplicabilityDate, 10));
        var reversedById = reversed.Result!.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("articles").EnumerateArray()
            .ToDictionary(static row => row.GetProperty("publisher_id").GetString()!, static row => row.GetProperty("status").GetString());
        Assert.AreEqual("added", reversedById[renamedId]);
        Assert.AreEqual("removed", reversedById[renamedId + "-new"]);
        Assert.AreEqual("changed", reversedById[changedId]);
    }

    [TestMethod]
    public async Task StatesWithDifferentRuleProfilesRefuseProfilesDifferAndNothingOverridesIt()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        var extraProfile = new string('a', 64);
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, extraProfile);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("profiles_differ", envelope.Refusal!.Code);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        var payload = envelope.Refusal.HelpfulPayload;
        var left = payload.GetProperty("left_profile").EnumerateArray().Select(static v => v.GetString()).ToArray();
        var right = payload.GetProperty("right_profile").EnumerateArray().Select(static v => v.GetString()).ToArray();
        CollectionAssert.DoesNotContain(left, extraProfile);
        CollectionAssert.Contains(right, extraProfile);
        Assert.AreEqual(fixture.Permalink, payload.GetProperty("left").GetString());
        // The added profile re-derived the later state's digest, so `right` is that state's permalink as mounted.
        var rightPermalink = payload.GetProperty("right").GetString()!;
        var laterCoordinate = fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + laterDate) + "--";
        StringAssert.StartsWith(rightPermalink, laterCoordinate);
        Assert.AreEqual(64, rightPermalink.Length - laterCoordinate.Length, "The permalink ends in the state digest as mounted.");
        Assert.AreNotEqual(fixture.Permalink, rightPermalink);
        // The same two states, on their own dates, still refuse: there is no way to ask for the diff anyway.
        var direct = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(fixture.ApplicabilityDate, 1), Shift(laterDate, 1));
        Assert.AreEqual("profiles_differ", direct.Refusal!.Code);
        // One state against itself never reaches the profile check.
        var same = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", laterDate, Shift(laterDate, 5));
        Assert.IsTrue(same.Result!.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("same_state").GetBoolean());
    }

    [TestMethod]
    public async Task EachBoundRefusesAsAsOfDoesAndNamesTheBound()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var beforeHistory = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(fixture.ApplicabilityDate, -1), laterDate);
        Assert.AreEqual("no_version_for_date", beforeHistory.Refusal!.Code);
        Assert.AreEqual(
            "asserts_absence_of_law,bound,history_begins,nearest_earlier,nearest_later,requested_date,what_would_answer",
            string.Join(",", beforeHistory.Refusal.HelpfulPayload.EnumerateObject().Select(static property => property.Name)),
            "diff names the bound: exactly these properties, as served in the envelope's canonical order.");
        Assert.AreEqual(PublisherId.LuLegilux, beforeHistory.Context.Publisher);
        Assert.AreEqual("lu", beforeHistory.Context.Jurisdiction);
        Assert.AreEqual("from", beforeHistory.Refusal.HelpfulPayload.GetProperty("bound").GetString());
        Assert.AreEqual(Shift(fixture.ApplicabilityDate, -1), beforeHistory.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, beforeHistory.Refusal.HelpfulPayload.GetProperty("history_begins").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, beforeHistory.Refusal.HelpfulPayload.GetProperty("nearest_later").GetString());

        var toBeforeHistory = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", laterDate, Shift(fixture.ApplicabilityDate, -1));
        Assert.AreEqual("no_version_for_date", toBeforeHistory.Refusal!.Code);
        Assert.AreEqual("to", toBeforeHistory.Refusal.HelpfulPayload.GetProperty("bound").GetString());

        var twin = await fixture.AddStateAsync(laterDate, "twin");
        using var mountWithTwin = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mountWithTwin);
        var ambiguous = await DiffAsync(mountWithTwin, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate);
        Assert.AreEqual("ambiguous_version", ambiguous.Refusal!.Code);
        Assert.AreEqual(
            "bound,candidates,requested_date",
            string.Join(",", ambiguous.Refusal.HelpfulPayload.EnumerateObject().Select(static property => property.Name)),
            "diff names the bound: exactly these properties, as served in the envelope's canonical order.");
        Assert.AreEqual(PublisherId.LuLegilux, ambiguous.Context.Publisher);
        Assert.AreEqual("to", ambiguous.Refusal.HelpfulPayload.GetProperty("bound").GetString());
        Assert.AreEqual(laterDate, ambiguous.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
        CollectionAssert.Contains(
            ambiguous.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray(),
            fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + laterDate) + "--" + twin.StateSha256);

        // A twin on date_from names bound from, and its candidates are the two states on that date.
        var twinFrom = await fixture.AddStateAsync(fixture.ApplicabilityDate, "twin-from");
        using var mountWithTwoTwins = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mountWithTwoTwins);
        var ambiguousFrom = await DiffAsync(mountWithTwoTwins, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate);
        Assert.AreEqual("ambiguous_version", ambiguousFrom.Refusal!.Code);
        Assert.AreEqual("from", ambiguousFrom.Refusal.HelpfulPayload.GetProperty("bound").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, ambiguousFrom.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
        CollectionAssert.AreEquivalent(
            new[] { fixture.Permalink, fixture.StableCoordinate + "--" + twinFrom.StateSha256 },
            ambiguousFrom.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
    }

    [TestMethod]
    public async Task LanguagesAreComparedApartAndOneAbsentAtABoundIsListedNotCompared()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, text) = fixture.ArticlesOfOwnState()[0];
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        // The German text begins only at the later date; the French text is amended there.
        var german = await fixture.AddSecondLanguageStateAsync(laterDate);
        var later = await fixture.AddStateAsync(laterDate, "later");
        await fixture.RewriteArticleTextAsync(later.ExpressionIri, anchor, text + " amended");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var all = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate);
        Assert.AreEqual(V3Verdicts.Answer, all.Verdict);
        var comparison = all.Result!.Value.GetProperty("comparisons").EnumerateArray().Single();
        Assert.AreEqual("fra", comparison.GetProperty("language").GetString());
        Assert.AreEqual(1, comparison.GetProperty("counts").GetProperty("changed").GetInt32());
        var skipped = all.Result.Value.GetProperty("languages_not_compared").EnumerateArray().Single();
        Assert.AreEqual("deu", skipped.GetProperty("language").GetString());
        Assert.AreEqual("from", skipped.GetProperty("bound").GetString());
        Assert.AreEqual("no state at or before the date", skipped.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            all.Result.Value.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());

        var germanOnly = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate, "deu");
        Assert.AreEqual("no_version_for_date", germanOnly.Refusal!.Code);
        Assert.AreEqual("from", germanOnly.Refusal.HelpfulPayload.GetProperty("bound").GetString());
        Assert.AreEqual(laterDate, germanOnly.Refusal.HelpfulPayload.GetProperty("history_begins").GetString(),
            "The German history begins where the first German state is.");

        var germanLater = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", laterDate, Shift(laterDate, 30), "deu");
        var germanComparison = germanLater.Result!.Value.GetProperty("comparisons").EnumerateArray().Single();
        Assert.IsTrue(germanComparison.GetProperty("same_state").GetBoolean());
        Assert.AreEqual(german.StateSha256, germanComparison.GetProperty("from").GetProperty("state_sha256").GetString());

        var english = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate, "eng");
        Assert.AreEqual("language_not_available", english.Refusal!.Code);
    }

    [TestMethod]
    public async Task AnAmbiguityOrAProfileMismatchInOneLanguageRefusesTheWholeDiff()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        // German covers both bounds with one state; French has two states on the later date.
        var german = await fixture.AddSecondLanguageStateAtSameDateAsync();
        var later = await fixture.AddStateAsync(laterDate, "later");
        var twin = await fixture.AddStateAsync(laterDate, "twin");
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate);
            Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, "German compares cleanly, but French is ambiguous: the whole diff refuses, as as_of does.");
            Assert.AreEqual("ambiguous_version", envelope.Refusal!.Code);
            Assert.AreEqual("to", envelope.Refusal.HelpfulPayload.GetProperty("bound").GetString());
            CollectionAssert.AreEquivalent(
                new[] { later.StateSha256, twin.StateSha256 }.Select(digest => fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + laterDate) + "--" + digest).ToArray(),
                envelope.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
            // German alone still answers.
            var germanOnly = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, laterDate, "deu");
            Assert.IsTrue(germanOnly.Result!.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("same_state").GetBoolean());
            Assert.AreEqual(german.StateSha256, germanOnly.Result.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("from").GetProperty("state_sha256").GetString());
        }

        // The same shape with a profile mismatch instead: German clean, French with a changed profile set.
        var profiles = await MountedFixture.CreateAsync();
        await using var cleanupProfiles = profiles;
        await profiles.AddSecondLanguageStateAtSameDateAsync();
        var frenchLater = await profiles.AddStateAsync(laterDate, "later");
        await profiles.AddRuleProfileToStateAsync(frenchLater.ExpressionIri, new string('b', 64));
        using var mountProfiles = await V3CorpusMount.OpenAsync(profiles.Directory, CancellationToken.None);
        Assert.IsNotNull(mountProfiles);
        var mismatch = await DiffAsync(mountProfiles, $"/lu-legilux/{profiles.WorkKey}", profiles.ApplicabilityDate, laterDate);
        Assert.AreEqual("profiles_differ", mismatch.Refusal!.Code, "German compares cleanly, but the French pair differs in profiles: the whole diff refuses.");
        Assert.AreEqual("fra", mismatch.Refusal.HelpfulPayload.GetProperty("language").GetString());
    }

    [TestMethod]
    public async Task AMissingBoundDoesNotMaskAnAmbiguityOnTheOtherBound()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var earlier = Shift(fixture.ApplicabilityDate, -50);
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        // German covers both bounds with one state dated before date_from; the French history begins after
        // date_from, and French has two states on date_to.
        await fixture.AddSecondLanguageStateAsync(earlier);
        var later = await fixture.AddStateAsync(laterDate, "later");
        var twin = await fixture.AddStateAsync(laterDate, "twin");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(fixture.ApplicabilityDate, -10), laterDate);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict,
            "French has no state at date_from, but its twin on date_to is an ambiguity, and an ambiguity refuses the whole diff.");
        Assert.AreEqual("ambiguous_version", envelope.Refusal!.Code);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("to", envelope.Refusal.HelpfulPayload.GetProperty("bound").GetString());
        Assert.AreEqual(laterDate, envelope.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
        CollectionAssert.AreEquivalent(
            new[] { later.StateSha256, twin.StateSha256 }.Select(digest => fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + laterDate) + "--" + digest).ToArray(),
            envelope.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray());
        // Without the twin on date_to, the same request answers for German and lists French as not compared.
        var germanOnly = await DiffAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(fixture.ApplicabilityDate, -10), Shift(fixture.ApplicabilityDate, -5));
        Assert.AreEqual(V3Verdicts.Answer, germanOnly.Verdict);
        Assert.AreEqual("deu", germanOnly.Result!.Value.GetProperty("comparisons").EnumerateArray().Single().GetProperty("language").GetString());
        Assert.AreEqual("fra", germanOnly.Result.Value.GetProperty("languages_not_compared").EnumerateArray().Single().GetProperty("language").GetString());
    }

    [TestMethod]
    public async Task WhenTwoLanguagesRefuseDifferentlyTheFirstInLanguageOrderIsTheAnswer()
    {
        var laterDate = "";
        // German (first in language order) is ambiguous on date_to; French differs in profiles.
        var ambiguityFirst = await MountedFixture.CreateAsync();
        await using (ambiguityFirst)
        {
            laterDate = Shift(ambiguityFirst.ApplicabilityDate, 400);
            var german = await ambiguityFirst.AddSecondLanguageStateAtSameDateAsync();
            var germanLater = await ambiguityFirst.AddStateAsync(laterDate, "de-later", german.ExpressionIri);
            var germanTwin = await ambiguityFirst.AddStateAsync(laterDate, "de-twin", german.ExpressionIri);
            var frenchLater = await ambiguityFirst.AddStateAsync(laterDate, "later");
            await ambiguityFirst.AddRuleProfileToStateAsync(frenchLater.ExpressionIri, new string('c', 64));
            using var mount = await V3CorpusMount.OpenAsync(ambiguityFirst.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{ambiguityFirst.WorkKey}", ambiguityFirst.ApplicabilityDate, laterDate);
            Assert.AreEqual("ambiguous_version", envelope.Refusal!.Code, "deu comes before fra, and deu is the ambiguous one.");
            Assert.AreEqual("to", envelope.Refusal.HelpfulPayload.GetProperty("bound").GetString());
            CollectionAssert.AreEquivalent(
                new[] { germanLater.StateSha256, germanTwin.StateSha256 },
                envelope.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()!.Split("--")[^1]).ToArray());
            // French alone is the profile mismatch.
            var french = await DiffAsync(mount, $"/lu-legilux/{ambiguityFirst.WorkKey}", ambiguityFirst.ApplicabilityDate, laterDate, "fra");
            Assert.AreEqual("profiles_differ", french.Refusal!.Code);
        }

        // The other way round: German differs in profiles, French is ambiguous on date_to.
        var mismatchFirst = await MountedFixture.CreateAsync();
        await using (mismatchFirst)
        {
            laterDate = Shift(mismatchFirst.ApplicabilityDate, 400);
            var german = await mismatchFirst.AddSecondLanguageStateAtSameDateAsync();
            var germanLater = await mismatchFirst.AddStateAsync(laterDate, "de-later", german.ExpressionIri);
            await mismatchFirst.AddRuleProfileToStateAsync(germanLater.ExpressionIri, new string('d', 64));
            await mismatchFirst.AddStateAsync(laterDate, "later");
            await mismatchFirst.AddStateAsync(laterDate, "twin");
            using var mount = await V3CorpusMount.OpenAsync(mismatchFirst.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{mismatchFirst.WorkKey}", mismatchFirst.ApplicabilityDate, laterDate);
            Assert.AreEqual("profiles_differ", envelope.Refusal!.Code, "deu comes before fra, and deu is the profile mismatch.");
            Assert.AreEqual("deu", envelope.Refusal.HelpfulPayload.GetProperty("language").GetString());
            var french = await DiffAsync(mount, $"/lu-legilux/{mismatchFirst.WorkKey}", mismatchFirst.ApplicabilityDate, laterDate, "fra");
            Assert.AreEqual("ambiguous_version", french.Refusal!.Code);
        }
    }

    [TestMethod]
    public async Task WithinOneRefusalTheFirstLanguageInOrderIsTheOneNamed()
    {
        // Two ambiguous languages: the payload names the first language's bound and candidates, in ordinal order.
        var twins = await MountedFixture.CreateAsync();
        await using (twins)
        {
            var laterDate = Shift(twins.ApplicabilityDate, 400);
            var german = await twins.AddSecondLanguageStateAtSameDateAsync();
            var germanLater = await twins.AddStateAsync(laterDate, "de-later", german.ExpressionIri);
            var germanTwin = await twins.AddStateAsync(laterDate, "de-twin", german.ExpressionIri);
            await twins.AddStateAsync(laterDate, "later");
            await twins.AddStateAsync(laterDate, "twin");
            using var mount = await V3CorpusMount.OpenAsync(twins.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{twins.WorkKey}", twins.ApplicabilityDate, laterDate);
            Assert.AreEqual("ambiguous_version", envelope.Refusal!.Code);
            var coordinate = twins.StableCoordinate.Replace("/" + twins.ApplicabilityDate, "/" + laterDate) + "--";
            CollectionAssert.AreEqual(
                new[] { coordinate + germanLater.StateSha256, coordinate + germanTwin.StateSha256 }.Order(StringComparer.Ordinal).ToArray(),
                envelope.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray(),
                "Both languages are ambiguous; deu comes first, and its candidates are served in ordinal order.");
        }

        // Two languages whose pairs differ in profiles: the payload names deu.
        var mismatches = await MountedFixture.CreateAsync();
        await using (mismatches)
        {
            var laterDate = Shift(mismatches.ApplicabilityDate, 400);
            var german = await mismatches.AddSecondLanguageStateAtSameDateAsync();
            var germanLater = await mismatches.AddStateAsync(laterDate, "de-later", german.ExpressionIri);
            await mismatches.AddRuleProfileToStateAsync(germanLater.ExpressionIri, new string('e', 64));
            var frenchLater = await mismatches.AddStateAsync(laterDate, "later");
            await mismatches.AddRuleProfileToStateAsync(frenchLater.ExpressionIri, new string('f', 64));
            using var mount = await V3CorpusMount.OpenAsync(mismatches.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{mismatches.WorkKey}", mismatches.ApplicabilityDate, laterDate);
            Assert.AreEqual("profiles_differ", envelope.Refusal!.Code);
            Assert.AreEqual("deu", envelope.Refusal.HelpfulPayload.GetProperty("language").GetString());
            CollectionAssert.Contains(
                envelope.Refusal.HelpfulPayload.GetProperty("right_profile").EnumerateArray().Select(static v => v.GetString()).ToArray(),
                new string('e', 64));
        }

        // Reversed dates: deu has no state at date_from, fra has none at date_to; nothing is compared, and
        // the refusal names the first language's missing bound.
        var reversed = await MountedFixture.CreateAsync();
        await using (reversed)
        {
            var germanBegins = Shift(reversed.ApplicabilityDate, 400);
            await reversed.AddSecondLanguageStateAsync(germanBegins);
            using var mount = await V3CorpusMount.OpenAsync(reversed.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var dateFrom = Shift(reversed.ApplicabilityDate, 100);
            var dateTo = Shift(reversed.ApplicabilityDate, -10);
            var envelope = await DiffAsync(mount, $"/lu-legilux/{reversed.WorkKey}", dateFrom, dateTo);
            Assert.AreEqual("no_version_for_date", envelope.Refusal!.Code);
            Assert.AreEqual("from", envelope.Refusal.HelpfulPayload.GetProperty("bound").GetString(), "deu comes first and is missing at date_from.");
            Assert.AreEqual(dateFrom, envelope.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
            Assert.AreEqual(germanBegins, envelope.Refusal.HelpfulPayload.GetProperty("history_begins").GetString(),
                "The refusal is about German, so the history it names is German's, not the French one that begins earlier.");
            Assert.AreEqual(germanBegins, envelope.Refusal.HelpfulPayload.GetProperty("nearest_later").GetString());

            // date_from on the very date of the French state: French resolves there and fails at date_to,
            // German has nothing yet. A payload built from every language's dates would say the history
            // begins on the date it refuses.
            var onFrenchDate = await DiffAsync(mount, $"/lu-legilux/{reversed.WorkKey}", reversed.ApplicabilityDate, dateTo);
            Assert.AreEqual("no_version_for_date", onFrenchDate.Refusal!.Code);
            Assert.AreEqual("from", onFrenchDate.Refusal.HelpfulPayload.GetProperty("bound").GetString());
            Assert.AreEqual(reversed.ApplicabilityDate, onFrenchDate.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
            Assert.AreEqual(germanBegins, onFrenchDate.Refusal.HelpfulPayload.GetProperty("history_begins").GetString());
            Assert.AreEqual(germanBegins, onFrenchDate.Refusal.HelpfulPayload.GetProperty("nearest_later").GetString());
        }
    }

    [TestMethod]
    public async Task EuIdentifiersRefuseTheModeAndAMissingMountRefusesTheCorpus()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var envelope = await DiffAsync(mount, "32016R0679", "2018-05-25", "2024-01-01");
            Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code);
            Assert.AreEqual("r6_diff", envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
            Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher);
            var unknown = await DiffAsync(mount, "/lu-legilux/no-such-work", "2018-05-25", "2024-01-01");
            Assert.AreEqual("identifier_unknown", unknown.Refusal!.Code);
        }

        var context = Body(DiffRawTarget,
            "{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\"}}");
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var unmounted = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
        Assert.AreEqual("diff", unmounted.OperationId);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_from\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_to\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_from\":\"2024-02-30\",\"date_to\":\"2025-01-01\"}}")]
    [DataRow("{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_from\":\"2024-01-01\",\"date_to\":\"2025-1-1\"}}")]
    [DataRow("{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"diff\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date_from\":\"2024-01-01\",\"date_to\":\"2025-01-01\",\"anchor\":\"art_1\"}}")]
    public async Task UnusableDiffRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        AssertTransportProblem(await PostAsync(mount, DiffRawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndAQueryStringIsDrift()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var diffBody = JsonSerializer.Serialize(new { operation_id = "diff", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate } });
        var asOfBody = JsonSerializer.Serialize(new { operation_id = "as_of", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate } });

        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, DiffRawTarget, diffBody)).Response.StatusCode);
        AssertTransportProblem(await PostAsync(mount, AsOfRawTarget, diffBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, DiffRawTarget, asOfBody), "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(await PostAsync(mount, DiffRawTarget + "?x=1", diffBody), "unknown_route", StatusCodes.Status404NotFound);
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> DiffAsync(V3CorpusMount mount, string identifier, string dateFrom, string dateTo, string? language = null)
    {
        var parameters = new Dictionary<string, string> { ["identifier"] = identifier, ["date_from"] = dateFrom, ["date_to"] = dateTo };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var context = await PostAsync(mount, DiffRawTarget, JsonSerializer.Serialize(new { operation_id = "diff", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<V3Envelope> AsOfAsync(V3CorpusMount mount, string identifier, string date)
    {
        var context = await PostAsync(mount, AsOfRawTarget, JsonSerializer.Serialize(new { operation_id = "as_of", parameters = new { identifier, date } }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
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
        context.TraceIdentifier = "mounted-corpus-diff";
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
