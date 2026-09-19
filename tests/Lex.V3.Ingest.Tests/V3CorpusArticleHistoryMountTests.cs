using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The third R6 temporal operation, driven through the real handler on a verified mount: the lineage
/// of one publisher-minted article id through the publisher-dated states of a Luxembourg work, with
/// the byte-equality wording rule over the stored token stream, the absent states, the anchor refusal
/// and the shared refusals.
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
        // The first state's article loses its publisher date: served as null and never a conflict,
        // the rule #682 pinned for the other answers (reviewer B3 on #687).
        await fixture.SetArticleDateAsync(identity, null);
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
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "stored token stream");
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "with consecutive text merged into one entry");
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "paragraph and inline-formatting boundaries and whitespace-only nodes are not compared");
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "reference labels and targets included; note references, note bodies and modification markers excluded");
        StringAssert.Contains(value.GetProperty("wording_rule").GetString(), "[kind, text, target]");
        StringAssert.Contains(value.GetProperty("validity_conflict_rule").GetString(), "differs from the state's applicability_date");
        Assert.AreEqual(0, value.GetProperty("absent_in_states").GetArrayLength());
        var distinct = value.GetProperty("distinct_wordings").EnumerateArray().Single();
        Assert.AreEqual("fra", distinct.GetProperty("language").GetString());
        Assert.AreEqual(1, distinct.GetProperty("count").GetInt32());
        var runs = value.GetProperty("wording_runs").EnumerateArray().Single();
        Assert.AreEqual("fra", runs.GetProperty("language").GetString());
        Assert.AreEqual(1, runs.GetProperty("count").GetInt32());

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
        Assert.AreEqual(LuxembourgIndexReader.WordingSha256(fixture.ArticleTokensJson(fixture.ExpressionIri, anchor)), first.GetProperty("wording_sha256").GetString(),
            "The digest of the stored token stream, as the index holds it.");
        Assert.IsFalse(first.TryGetProperty("text_sha256", out _), "No field may read as a hash of the text alone.");
        Assert.IsTrue(first.GetProperty("validity_conflict").ValueKind is JsonValueKind.True or JsonValueKind.False,
            "validity_conflict is a boolean");
        var second = rows[1].GetProperty("articles").EnumerateArray().Single();
        Assert.AreEqual(LuxembourgIndexReader.WordingSha256(fixture.ArticleTokensJson(later.ExpressionIri, anchor)), second.GetProperty("wording_sha256").GetString());
        Assert.AreEqual(first.GetProperty("wording_sha256").GetString(), second.GetProperty("wording_sha256").GetString(),
            "A copied token stream is the same wording.");
        Assert.AreEqual(laterIdentity, second.GetProperty("article_identity_sha256").GetString(),
            "The copied state's own bound article; the text digest, not the identity, decides the wording.");
        Assert.AreEqual(laterArticleDate, second.GetProperty("article_valid_from").GetString());
        Assert.IsTrue(second.GetProperty("validity_conflict").GetBoolean(),
            "The publisher's article date differs from the state date: a conflict, stated as such.");
        Assert.AreEqual(JsonValueKind.Null, first.GetProperty("article_valid_from").ValueKind);
        Assert.IsFalse(first.GetProperty("validity_conflict").GetBoolean(), "A blank publisher date is never a conflict.");
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
        var third = await fixture.AddStateAsync(thirdDate, "third");
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
        var digests = rows.Select(static row => row.GetProperty("articles").EnumerateArray().Single().GetProperty("wording_sha256").GetString()).ToArray();
        CollectionAssert.AreEqual(
            new[] { fixture.ExpressionIri, second.ExpressionIri, third.ExpressionIri, fourth.ExpressionIri }
                .Select(expression => LuxembourgIndexReader.WordingSha256(fixture.ArticleTokensJson(expression, anchor))).ToArray(),
            digests);
        Assert.AreEqual(digests[0], digests[2], "The third state carries the original stream again.");
        Assert.AreEqual(3, digests.Distinct(StringComparer.Ordinal).Count());
        // Runs count the first state and every change (A, B, A, A' is four runs); distinct wordings
        // count distinct digests (A, B, A' is three). Neither is a claim about the law.
        Assert.AreEqual(4, envelope.Result.Value.GetProperty("wording_runs").EnumerateArray().Single().GetProperty("count").GetInt32());
        Assert.AreEqual(3, envelope.Result.Value.GetProperty("distinct_wordings").EnumerateArray().Single().GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task AReferenceRetargetedUnderTheSameLabelIsANewWordingAndTheSameStreamIsNot()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, _) = fixture.ArticlesOfOwnState()[0];
        var secondDate = Shift(fixture.ApplicabilityDate, 400);
        var second = await fixture.AddStateAsync(secondDate, "second");
        var thirdDate = Shift(fixture.ApplicabilityDate, 800);
        var third = await fixture.AddStateAsync(thirdDate, "third");
        // The same searchable text in all three states ("voir la loi"); the reference points at act n1
        // in the first two and at act n2 in the third. Only the token stream tells them apart.
        static string Stream(string target) => JsonSerializer.Serialize(new object[]
        {
            new { kind = "text", text = "voir ", target = (string?)null, marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "la loi", target, marker = (string?)null, note_body = (object?)null },
        });
        await fixture.SetArticleTokensAsync(fixture.ExpressionIri, anchor, "voir la loi", Stream("http://data.legilux.public.lu/eli/etat/leg/loi/2001/01/01/n1"));
        await fixture.SetArticleTokensAsync(second.ExpressionIri, anchor, "voir la loi", Stream("http://data.legilux.public.lu/eli/etat/leg/loi/2001/01/01/n1"));
        await fixture.SetArticleTokensAsync(third.ExpressionIri, anchor, "voir la loi", Stream("http://data.legilux.public.lu/eli/etat/leg/loi/2002/02/02/n2"));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(3, rows);
        CollectionAssert.AreEqual(new[] { false, false, true },
            rows.Select(static row => row.GetProperty("wording_changed").GetBoolean()).ToArray(),
            "A reference moved to another act under the same label is a change of wording; the same stream is not.");
        Assert.AreEqual(2, envelope.Result.Value.GetProperty("wording_runs").EnumerateArray().Single().GetProperty("count").GetInt32());
        Assert.AreEqual(2, envelope.Result.Value.GetProperty("distinct_wordings").EnumerateArray().Single().GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task ANoteOrAModificationMarkerIsNotAChangeOfWording()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, _) = fixture.ArticlesOfOwnState()[0];
        var second = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "second");
        var third = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 800), "third");
        // The same words and reference in all three states; the second adds a note reference with a
        // body, the third wraps the words in a modification span. The publisher's apparatus moved,
        // the article's words did not.
        var words = new object[]
        {
            new { kind = "text", text = "voir ", target = (string?)null, marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "la loi", target = "http://data.legilux.public.lu/eli/etat/leg/loi/2001/01/01/n1", marker = (string?)null, note_body = (object?)null },
        };
        var withNote = words.Concat(new object[]
        {
            new { kind = "note_reference", text = (string?)null, target = (string?)null, marker = "1", note_body = (object?)new[] { new { kind = "text", text = "Note du publisher.", target = (string?)null, marker = (string?)null } } },
        }).ToArray();
        var withMarkers = new object[]
        {
            new { kind = "modification_start", text = (string?)null, target = (string?)null, marker = "mod_1", note_body = (object?)null },
        }.Concat(words).Concat(new object[]
        {
            new { kind = "modification_end", text = (string?)null, target = (string?)null, marker = "mod_1", note_body = (object?)null },
        }).ToArray();
        await fixture.SetArticleTokensAsync(fixture.ExpressionIri, anchor, "voir la loi", JsonSerializer.Serialize(words));
        await fixture.SetArticleTokensAsync(second.ExpressionIri, anchor, "voir la loi", JsonSerializer.Serialize(withNote));
        await fixture.SetArticleTokensAsync(third.ExpressionIri, anchor, "voir la loi", JsonSerializer.Serialize(withMarkers));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);

        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(3, rows);
        CollectionAssert.AreEqual(new[] { false, false, false },
            rows.Select(static row => row.GetProperty("wording_changed").GetBoolean()).ToArray(),
            "Notes and modification markers are the publisher's apparatus, not the article's words.");
        Assert.AreEqual(1, rows.Select(static row => row.GetProperty("articles").EnumerateArray().Single().GetProperty("wording_sha256").GetString()).Distinct().Count());
        Assert.AreEqual(1, envelope.Result.Value.GetProperty("distinct_wordings").EnumerateArray().Single().GetProperty("count").GetInt32());
        Assert.AreEqual(1, envelope.Result.Value.GetProperty("wording_runs").EnumerateArray().Single().GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task ReturningToAnEarlierWordingIsARunButNotANewDistinctWording()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, anchor, text) = fixture.ArticlesOfOwnState()[0];
        var second = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "second");
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 800), "third");
        // A, B, A: the third state copies the fixture's original stream.
        await fixture.RewriteArticleTextAsync(second.ExpressionIri, anchor, text + " (B)");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", anchor);

        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { false, true, true },
            rows.Select(static row => row.GetProperty("wording_changed").GetBoolean()).ToArray());
        Assert.AreEqual(3, envelope.Result.Value.GetProperty("wording_runs").EnumerateArray().Single().GetProperty("count").GetInt32(), "A, B, A is three runs");
        Assert.AreEqual(2, envelope.Result.Value.GetProperty("distinct_wordings").EnumerateArray().Single().GetProperty("count").GetInt32(), "and two distinct wordings");
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
        // The latest state renames the anchor's id, so the neighbourhood must come from that state:
        // the old id is gone from it and the renamed one is there.
        var latest = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "latest");
        await fixture.RenameArticleIdAsync(latest.ExpressionIri, anchor, anchor + "-renamed");
        var latestIds = ids.Select(id => id == anchor ? anchor + "-renamed" : id).Order(StringComparer.Ordinal).ToArray();
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
        CollectionAssert.Contains(nearest, anchor + "-renamed");
        CollectionAssert.DoesNotContain(nearest, anchor, "The neighbourhood is the latest state's ids; the old id is not there any more.");
        Assert.IsTrue(nearest.All(id => id!.StartsWith(anchor, StringComparison.Ordinal)),
            "Only ids sharing the longest common prefix are offered: " + string.Join(",", nearest));
        CollectionAssert.AreEqual(nearest.Order(StringComparer.Ordinal).ToArray(), nearest);

        // Shares nothing: the neighbourhood is the state's first ids in ordinal order, because the
        // reviewed refusal contract always names one; it stays a refusal, never a search.
        var far = await HistoryAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "§zzz");
        Assert.AreEqual("anchor_not_in_version", far.Refusal!.Code);
        CollectionAssert.AreEqual(latestIds.Take(10).ToArray(),
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
        CollectionAssert.AreEqual(
            new[] { german.ExpressionIri, fixture.ExpressionIri, later.ExpressionIri }
                .Select(expression => LuxembourgIndexReader.WordingSha256(fixture.ArticleTokensJson(expression, anchor))).ToArray(),
            rows.Select(static row => row.GetProperty("articles").EnumerateArray().Single().GetProperty("wording_sha256").GetString()).ToArray());
        Assert.AreEqual(JsonValueKind.Null, rows[0].GetProperty("next_applicability_date").ValueKind);
        Assert.AreEqual(laterDate, rows[1].GetProperty("next_applicability_date").GetString());
        var distinct = all.Result.Value.GetProperty("distinct_wordings").EnumerateArray()
            .ToDictionary(static d => d.GetProperty("language").GetString()!, static d => d.GetProperty("count").GetInt32());
        Assert.AreEqual(1, distinct["deu"]);
        Assert.AreEqual(2, distinct["fra"]);
        var runs = all.Result.Value.GetProperty("wording_runs").EnumerateArray()
            .ToDictionary(static d => d.GetProperty("language").GetString()!, static d => d.GetProperty("count").GetInt32());
        Assert.AreEqual(1, runs["deu"]);
        Assert.AreEqual(2, runs["fra"]);
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
