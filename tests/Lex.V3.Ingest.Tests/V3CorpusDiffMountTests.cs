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
        Assert.AreEqual("the same version applied on both dates", comparison.GetProperty("note").GetString());
        Assert.AreEqual(JsonValueKind.Null, comparison.GetProperty("articles").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, comparison.GetProperty("counts").ValueKind);
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
        var comparison = envelope.Result!.Value.GetProperty("comparisons").EnumerateArray().Single();
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
        Assert.AreEqual("to", ambiguous.Refusal.HelpfulPayload.GetProperty("bound").GetString());
        Assert.AreEqual(laterDate, ambiguous.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
        CollectionAssert.Contains(
            ambiguous.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static v => v.GetString()).ToArray(),
            fixture.StableCoordinate.Replace("/" + fixture.ApplicabilityDate, "/" + laterDate) + "--" + twin.StateSha256);
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
