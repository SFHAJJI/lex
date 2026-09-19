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
/// The third R6 temporal operation, driven through the real handler on a verified mount: the lineage
/// of one publisher-minted article id through the publisher-dated states of a Luxembourg work, with
/// the byte-equality wording rule, the absent states, the anchor refusal and the shared refusals.
/// </summary>
[TestClass]
public sealed class V3CorpusArticleHistoryMountTests
{
    private const string HistoryRawTarget = "/api/v3/article_history";
    private const string TimelineRawTarget = "/api/v3/timeline";
    private const string AsOfRawTarget = "/api/v3/as_of";

    [TestMethod]
    public async Task AnchorCarriedByEveryStateWithUnchangedTextIsOneWording()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (identity, anchor, text) = fixture.ArticlesOfOwnState()[0];
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        // The copied state's article has its own identity (the fixture derives it from the source
        // identity); the publisher dates it a week after the state, so the row carries a conflict.
        var laterIdentity = Sha256("later:" + identity);
        var laterArticleDate = Shift(laterDate, 7);
        await fixture.SetArticleDateAsync(laterIdentity, laterArticleDate);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("article_history", envelope.OperationId);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        Assert.AreEqual("provision_history", envelope.Result!.ObjectType);
        var value = envelope.Result.Value;
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", value.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(anchor, value.GetProperty("requested_anchor").GetString());
        Assert.AreEqual(JsonValueKind.Null, value.GetProperty("requested_language").ValueKind);
        Assert.AreEqual("lu-legilux", value.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.WorkKey, value.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("history_begins").GetString());
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName)))),
            value.GetProperty("index_sha256").GetString(),
            "The digest of the index as mounted, which the added state changed.");
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "SHA-256 of the retained article text");
        StringAssert.Contains(value.GetProperty("validity_conflict_rule").GetString(), "differs from the state's applicability_date");
        Assert.AreEqual(0, value.GetProperty("absent_in_states").GetArrayLength());
        var distinct = value.GetProperty("distinct_wordings").EnumerateArray().Single();
        Assert.AreEqual("fra", distinct.GetProperty("language").GetString());
        Assert.AreEqual(1, distinct.GetProperty("count").GetInt32());

        var rows = value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(2, rows);
        Assert.AreEqual(fixture.ApplicabilityDate, rows[0].GetProperty("applicability_date").GetString());
        Assert.AreEqual(laterDate, rows[0].GetProperty("next_applicability_date").GetString());
        Assert.AreEqual(fixture.StateSha256, rows[0].GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.Permalink, rows[0].GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.StableCoordinate, rows[0].GetProperty("stable_coordinate").GetString());
        Assert.IsFalse(rows[0].GetProperty("wording_changed").GetBoolean(), "The first row has nothing before it.");
        Assert.AreEqual(laterDate, rows[1].GetProperty("applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, rows[1].GetProperty("next_applicability_date").ValueKind);
        Assert.AreEqual(later.StateSha256, rows[1].GetProperty("state_sha256").GetString());
        Assert.IsFalse(rows[1].GetProperty("wording_changed").GetBoolean(), "A copied text is the same wording.");

        var first = rows[0].GetProperty("articles").EnumerateArray().Single();
        Assert.AreEqual(identity, first.GetProperty("article_identity_sha256").GetString());
        Assert.AreEqual(anchor, first.GetProperty("publisher_id").GetString());
        Assert.AreEqual(Sha256(text), first.GetProperty("text_sha256").GetString());
        Assert.IsTrue(first.GetProperty("validity_conflict").ValueKind is JsonValueKind.True or JsonValueKind.False,
            "validity_conflict is a boolean");
        var second = rows[1].GetProperty("articles").EnumerateArray().Single();
        Assert.AreEqual(Sha256(text), second.GetProperty("text_sha256").GetString());
        Assert.AreEqual(laterIdentity, second.GetProperty("article_identity_sha256").GetString(),
            "The copied state's own bound article; the text digest, not the identity, decides the wording.");
        Assert.AreEqual(laterArticleDate, second.GetProperty("article_valid_from").GetString());
        Assert.IsTrue(second.GetProperty("validity_conflict").GetBoolean(),
            "The publisher's article date differs from the state date: a conflict, stated as such.");
        Assert.IsFalse(first.GetProperty("validity_conflict").GetBoolean() &&
                       first.GetProperty("article_valid_from").GetString() == fixture.ApplicabilityDate,
            "An article dated on its state is never a conflict.");
        foreach (var forbidden in new[] { "valid_to", "end_date", "in_force", "repealed", "diff" })
        {
            Assert.IsFalse(rows[1].TryGetProperty(forbidden, out _), forbidden);
        }
    }

    [TestMethod]
    public async Task AChangedTextIsANewWordingAndAnUnchangedOneIsNot()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, text) = fixture.ArticlesOfOwnState()[0];
        var secondDate = Shift(fixture.ApplicabilityDate, 400);
        var second = await fixture.AddStateAsync(secondDate, "second");
        var thirdDate = Shift(fixture.ApplicabilityDate, 800);
        await fixture.AddStateAsync(thirdDate, "third");
        var fourthDate = Shift(fixture.ApplicabilityDate, 1200);
        var fourth = await fixture.AddStateAsync(fourthDate, "fourth");
        // The second state rewrites the article; the third copies the original text again; the fourth
        // differs from the original by one apostrophe only.
        await fixture.RewriteArticleTextAsync(second.ExpressionIri, anchor, text + " amended");
        await fixture.RewriteArticleTextAsync(fourth.ExpressionIri, anchor, text + "’");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(4, rows);
        CollectionAssert.AreEqual(new[] { false, true, true, true },
            rows.Select(static row => row.GetProperty("wording_changed").GetBoolean()).ToArray(),
            "Amended, back to the original, and a one-character punctuation change are each a change against the previous state.");
        CollectionAssert.AreEqual(
            new[] { Sha256(text), Sha256(text + " amended"), Sha256(text), Sha256(text + "’") },
            rows.Select(static row => row.GetProperty("articles").EnumerateArray().Single().GetProperty("text_sha256").GetString()).ToArray());
        var distinct = envelope.Result.Value.GetProperty("distinct_wordings").EnumerateArray().Single();
        Assert.AreEqual(4, distinct.GetProperty("count").GetInt32(),
            "Returning to an earlier text is counted as a change of wording, not a return; the rule says consecutive states.");
    }

    [TestMethod]
    public async Task AStateWithoutTheAnchorIsListedAsAbsentAndTheLineageBeginsWhereTheAnchorDoes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, _) = fixture.ArticlesOfOwnState()[0];
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        // The first state knew the article under another id; only the later state carries the anchor.
        await fixture.RenameArticleIdAsync(fixture.ExpressionIri, anchor, anchor + "-former");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        var value = envelope.Result!.Value;
        Assert.AreEqual(laterDate, value.GetProperty("history_begins").GetString(),
            "The lineage begins where the anchor first appears, not where the work does.");
        var rows = value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(1, rows);
        Assert.AreEqual(later.StateSha256, rows[0].GetProperty("state_sha256").GetString());
        var absent = value.GetProperty("absent_in_states").EnumerateArray().Single();
        Assert.AreEqual(fixture.ApplicabilityDate, absent.GetProperty("applicability_date").GetString());
        Assert.AreEqual(fixture.StateSha256, absent.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.Permalink, absent.GetProperty("permalink").GetString());
        Assert.AreEqual("fra", absent.GetProperty("language").GetString());

        // The former id has the mirror lineage: present first, absent later.
        var former = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor + "-former");
        Assert.AreEqual(fixture.ApplicabilityDate, former.Result!.Value.GetProperty("history_begins").GetString());
        Assert.AreEqual(laterDate,
            former.Result.Value.GetProperty("absent_in_states").EnumerateArray().Single().GetProperty("applicability_date").GetString());
    }

    [TestMethod]
    public async Task AnAnchorInNoStateRefusesAnchorNotInVersionWithTheNearestIdsOfTheLatestState()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var ids = fixture.ArticlesOfOwnState().Select(static article => article.PublisherId).Distinct().Order(StringComparer.Ordinal).ToArray();
        var anchor = ids[0];
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // Shares its whole spelling with one real id and more: that id has the longest common prefix.
        var near = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor + "-bis");
        Assert.AreEqual(V3Verdicts.Refuse, near.Verdict);
        Assert.AreEqual("anchor_not_in_version", near.Refusal!.Code);
        Assert.AreEqual(PublisherId.LuLegilux, near.Context.Publisher);
        Assert.AreEqual("lu", near.Context.Jurisdiction);
        var payload = near.Refusal.HelpfulPayload;
        Assert.AreEqual(anchor + "-bis", payload.GetProperty("requested_anchor").GetString());
        Assert.IsTrue(payload.GetProperty("do_not_fall_back_to_full_text_search").GetBoolean());
        var nearest = payload.GetProperty("nearest_anchors").EnumerateArray().Select(static v => v.GetString()).ToArray();
        CollectionAssert.Contains(nearest, anchor);
        Assert.IsTrue(nearest.All(id => id!.StartsWith(anchor, StringComparison.Ordinal)),
            "Only ids sharing the longest common prefix are offered: " + string.Join(",", nearest));
        CollectionAssert.AreEqual(nearest.Order(StringComparer.Ordinal).ToArray(), nearest);

        // Shares nothing: the neighbourhood is the state's first ids in ordinal order, because the
        // reviewed refusal contract always names one; it stays a refusal, never a search.
        var far = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "§zzz");
        Assert.AreEqual("anchor_not_in_version", far.Refusal!.Code);
        CollectionAssert.AreEqual(ids.Take(10).ToArray(),
            far.Refusal.HelpfulPayload.GetProperty("nearest_anchors").EnumerateArray().Select(static v => v.GetString()).ToArray());
    }

    [TestMethod]
    public async Task LanguagesAreLineagesOfTheirOwnAndAMissingLanguageRefuses()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, text) = fixture.ArticlesOfOwnState()[0];
        var german = await fixture.AddSecondLanguageStateAtSameDateAsync();
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        // The German text differs from the French, as a translation does, so a comparison that reaches
        // across languages would report the first French row as a change against the German one.
        await fixture.RewriteArticleTextAsync(german.ExpressionIri, anchor, text + " (deutsch)");
        await fixture.RewriteArticleTextAsync(later.ExpressionIri, anchor, text + " amended");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var all = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);
        Assert.AreEqual(V3Verdicts.Answer, all.Verdict);
        var rows = all.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(3, rows);
        CollectionAssert.AreEqual(new[] { "deu", "fra", "fra" },
            rows.Select(static row => row.GetProperty("language").GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { german.StateSha256, fixture.StateSha256, later.StateSha256 },
            rows.Select(static row => row.GetProperty("state_sha256").GetString()).ToArray());
        // The French amendment is a change of the French lineage only; the German row has no
        // predecessor and no successor of its own, and the first French row is not compared with it.
        CollectionAssert.AreEqual(new[] { false, false, true },
            rows.Select(static row => row.GetProperty("wording_changed").GetBoolean()).ToArray());
        CollectionAssert.AreEqual(new[] { Sha256(text + " (deutsch)"), Sha256(text), Sha256(text + " amended") },
            rows.Select(static row => row.GetProperty("articles").EnumerateArray().Single().GetProperty("text_sha256").GetString()).ToArray());
        Assert.AreEqual(JsonValueKind.Null, rows[0].GetProperty("next_applicability_date").ValueKind);
        Assert.AreEqual(laterDate, rows[1].GetProperty("next_applicability_date").GetString());
        var distinct = all.Result.Value.GetProperty("distinct_wordings").EnumerateArray()
            .ToDictionary(static d => d.GetProperty("language").GetString()!, static d => d.GetProperty("count").GetInt32());
        Assert.AreEqual(1, distinct["deu"]);
        Assert.AreEqual(2, distinct["fra"]);
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            all.Result.Value.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());

        var germanOnly = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor, "deu");
        Assert.AreEqual("deu", germanOnly.Result!.Value.GetProperty("requested_language").GetString());
        Assert.AreEqual(german.StateSha256,
            germanOnly.Result.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        Assert.AreEqual(0, germanOnly.Result.Value.GetProperty("absent_in_states").GetArrayLength());

        var english = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor, "eng");
        Assert.AreEqual(V3Verdicts.Refuse, english.Verdict);
        Assert.AreEqual("language_not_available", english.Refusal!.Code);
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
    }

    [TestMethod]
    public async Task AnIdentifierWithNoPublisherShapeIsUnknownWithLuxembourgContextOnEveryMountAndOperation()
    {
        var luxembourg = await MountedFixture.CreateAsync();
        await using var cleanupLuxembourg = luxembourg;
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanupEurope = europe;
        using var luxembourgMount = await V3CorpusMount.OpenAsync(luxembourg.Directory, CancellationToken.None);
        using var europeMount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        _ = await luxembourg.AddEuropeCollisionAsync();
        using var combined = await V3CorpusMount.OpenAsync(luxembourg.Directory, CancellationToken.None);
        Assert.IsNotNull(luxembourgMount);
        Assert.IsNotNull(europeMount);
        Assert.IsNotNull(combined);

        // Neither Luxembourg-shaped, nor EU-shaped, nor an ELI. The mount must not decide.
        foreach (var identifier in new[] { "foo", "art_1", "loi 2004", "urn:x:1" })
        {
            foreach (var (mount, name) in new[] { (luxembourgMount, "lu"), (europeMount, "eu-only"), (combined, "combined") })
            {
                var bodies = new[]
                {
                    (AsOfRawTarget, JsonSerializer.Serialize(new { operation_id = "as_of", parameters = new { identifier, date = "2024-01-01" } })),
                    (TimelineRawTarget, JsonSerializer.Serialize(new { operation_id = "timeline", parameters = new { identifier } })),
                    (HistoryRawTarget, JsonSerializer.Serialize(new { operation_id = "article_history", parameters = new { identifier, anchor = "art_1" } })),
                };
                foreach (var (rawTarget, body) in bodies)
                {
                    var context = await PostAsync(mount, rawTarget, body);
                    Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, $"{identifier} {name} {rawTarget}");
                    var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
                    Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, $"{identifier} {name} {rawTarget}");
                    Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, $"{identifier} {name} {rawTarget}");
                    Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, $"{identifier} {name} {rawTarget}");
                    Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics, $"{identifier} {name} {rawTarget}");
                    StringAssert.Contains(envelope.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(), "no publisher shape");
                }
            }
        }
    }

    [TestMethod]
    public async Task EuIdentifiersRefuseTheModeAndLuxembourgIdentifiersOnAnEuOnlyMountRefuseTheCorpus()
    {
        var luxembourg = await MountedFixture.CreateAsync();
        await using var cleanupLuxembourg = luxembourg;
        using (var mount = await V3CorpusMount.OpenAsync(luxembourg.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            foreach (var identifier in new[]
                     {
                         "32016R0679",
                         "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
                         "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679",
                     })
            {
                var envelope = await HistoryAsync(mount, identifier, "art_1");
                Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code, identifier);
                Assert.AreEqual("r6_article_history", envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, identifier);
                Assert.AreEqual(TimelineSemantics.OfficialConsolidationState, envelope.Context.TimelineSemantics, identifier);
            }

            foreach (var identifier in new[] { "/lu-legilux/no-such-work", luxembourg.Permalink })
            {
                var envelope = await HistoryAsync(mount, identifier, "art_1");
                Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, identifier);
                Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, identifier);
                StringAssert.Contains(envelope.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(), "present in the mounted index");
            }
        }

        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanupEurope = europe;
        using var europeMount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(europeMount);
        var onEuropeOnly = await HistoryAsync(europeMount, $"/lu-legilux/{luxembourg.WorkKey}", "art_1");
        Assert.AreEqual("no_corpus_mounted", onEuropeOnly.Refusal!.Code);
        Assert.AreEqual("lu", onEuropeOnly.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
        Assert.AreEqual(PublisherId.LuLegilux, onEuropeOnly.Context.Publisher);
        var euOnEuropeOnly = await HistoryAsync(europeMount, "32016R0679", "art_1");
        Assert.AreEqual("retrieval_mode_unavailable", euOnEuropeOnly.Refusal!.Code);
        Assert.AreEqual("r6_article_history", euOnEuropeOnly.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
        Assert.AreEqual(PublisherId.EuEurLex, euOnEuropeOnly.Context.Publisher);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"article_history\",\"parameters\":{\"identifier\":\"/lu-legilux/w\"}}")]
    [DataRow("{\"operation_id\":\"article_history\",\"parameters\":{\"anchor\":\"art_1\"}}")]
    [DataRow("{\"operation_id\":\"article_history\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"anchor\":\" \"}}")]
    [DataRow("{\"operation_id\":\"article_history\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"anchor\":\"art_1\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"article_history\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"anchor\":\"art_1\",\"date\":\"2026-01-01\"}}")]
    [DataRow("{\"operation_id\":\"article_history\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"anchor\":1}}")]
    public async Task UnusableHistoryRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        AssertTransportProblem(await PostAsync(mount, HistoryRawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndAQueryStringIsDrift()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var (_, anchor, _) = fixture.ArticlesOfOwnState()[0];
        var historyBody = JsonSerializer.Serialize(new { operation_id = "article_history", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", anchor } });
        var timelineBody = JsonSerializer.Serialize(new { operation_id = "timeline", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}" } });

        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, HistoryRawTarget, historyBody)).Response.StatusCode);
        foreach (var (rawTarget, body) in new[]
                 {
                     (TimelineRawTarget, historyBody),
                     (AsOfRawTarget, historyBody),
                     (V3ResolveRestRoute.RawTarget, historyBody),
                     (HistoryRawTarget, timelineBody),
                 })
        {
            AssertTransportProblem(await PostAsync(mount, rawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest, rawTarget);
        }
        AssertTransportProblem(await PostAsync(mount, HistoryRawTarget + "?x=1", historyBody), "unknown_route", StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task HistoryWithoutAMountRefusesNoCorpusMountedThroughTheSameRoute()
    {
        var context = Body(HistoryRawTarget,
            "{\"operation_id\":\"article_history\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"anchor\":\"art_1\"}}");
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        Assert.AreEqual("article_history", envelope.OperationId);
    }

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> HistoryAsync(V3CorpusMount mount, string identifier, string anchor, string? language = null)
    {
        var parameters = new Dictionary<string, string> { ["identifier"] = identifier, ["anchor"] = anchor };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var context = await PostAsync(mount, HistoryRawTarget, JsonSerializer.Serialize(new { operation_id = "article_history", parameters }));
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
        context.TraceIdentifier = "mounted-corpus-article-history";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertTransportProblem(DefaultHttpContext context, string code, int status, string? because = null)
    {
        Assert.AreEqual(status, context.Response.StatusCode, because);
        Assert.AreEqual("application/problem+json", context.Response.ContentType, because);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString(), because);
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
    }
}
