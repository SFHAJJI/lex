using System.Globalization;
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
/// The first R6 temporal operation, driven through the real handler on a verified mount: the
/// publisher-dated Luxembourg state that applies on a date, per language, with every refusal the
/// closed registry names for it, and the route binding that keeps another operation's body out.
/// </summary>
[TestClass]
public sealed class V3CorpusAsOfMountTests
{
    private const string AsOfRawTarget = "/api/v3/as_of";

    [TestMethod]
    public async Task AsOfOnTheStateDateServesThatStateWithNoNextDate()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
        Assert.AreEqual("version_state", envelope.Result!.ObjectType);
        var value = envelope.Result.Value;
        Assert.AreEqual(fixture.WorkKey, value.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.IndexSha256, value.GetProperty("index_sha256").GetString());
        Assert.AreEqual(JsonValueKind.Null, value.GetProperty("requested_language").ValueKind);
        var states = value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(1, states);
        Assert.AreEqual(fixture.ApplicabilityDate, states[0].GetProperty("applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, states[0].GetProperty("next_applicability_date").ValueKind);
        Assert.AreEqual(fixture.StateSha256, states[0].GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.ExpressionIri, states[0].GetProperty("expression_iri").GetString());
        Assert.AreEqual(fixture.StableCoordinate, states[0].GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.Permalink, states[0].GetProperty("permalink").GetString());
    }

    [TestMethod]
    public async Task AsOfBetweenTwoStatesSelectsTheEarlierAndBoundsItByTheLater()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var between = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(laterDate, -1));
        Assert.AreEqual(V3Verdicts.Answer, between.Verdict);
        var state = between.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.ApplicabilityDate, state.GetProperty("applicability_date").GetString());
        Assert.AreEqual(laterDate, state.GetProperty("next_applicability_date").GetString());
        Assert.AreEqual(fixture.StateSha256, state.GetProperty("state_sha256").GetString());

        var onLater = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", laterDate);
        Assert.AreEqual(V3Verdicts.Answer, onLater.Verdict);
        var laterState = onLater.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(laterDate, laterState.GetProperty("applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, laterState.GetProperty("next_applicability_date").ValueKind);
        Assert.AreEqual(later.StateSha256, laterState.GetProperty("state_sha256").GetString());

        var afterLater = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(laterDate, 3650));
        Assert.AreEqual(later.StateSha256,
            afterLater.Result!.Value.GetProperty("states").EnumerateArray().Single()
                .GetProperty("state_sha256").GetString(),
            "The last publisher-dated state applies to every later date; no end is invented.");
    }

    [TestMethod]
    public async Task DateBeforeHistoryRefusesNoVersionForDateWithTheHistoryBounds()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var before = Shift(fixture.ApplicabilityDate, -1);

        var envelope = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", before);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("no_version_for_date", envelope.Refusal!.Code);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        var payload = envelope.Refusal.HelpfulPayload;
        Assert.AreEqual(before, payload.GetProperty("requested_date").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, payload.GetProperty("history_begins").GetString());
        Assert.AreEqual(JsonValueKind.Null, payload.GetProperty("nearest_earlier").ValueKind);
        Assert.AreEqual(fixture.ApplicabilityDate, payload.GetProperty("nearest_later").GetString());
    }

    [TestMethod]
    public async Task TwoLanguagesAreServedTogetherAndEachNextDateIsScopedToItsOwnLanguage()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var german = await fixture.AddSecondLanguageStateAtSameDateAsync();
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var both = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", Shift(laterDate, -1));
        Assert.AreEqual(V3Verdicts.Answer, both.Verdict);
        var states = both.Result!.Value.GetProperty("states").EnumerateArray()
            .ToDictionary(state => state.GetProperty("language").GetString()!, state => state);
        Assert.HasCount(2, states);
        Assert.AreEqual(laterDate, states["fra"].GetProperty("next_applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, states["deu"].GetProperty("next_applicability_date").ValueKind,
            "A French change date must never be reported as a change of the German text.");
        Assert.AreEqual(german.StateSha256, states["deu"].GetProperty("state_sha256").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            both.Result.Value.GetProperty("available_languages").EnumerateArray()
                .Select(static value => value.GetString()).ToArray());

        var germanOnly = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", laterDate, "deu");
        Assert.AreEqual(V3Verdicts.Answer, germanOnly.Verdict);
        Assert.AreEqual("deu", germanOnly.Result!.Value.GetProperty("requested_language").GetString());
        Assert.AreEqual("deu",
            germanOnly.Result.Value.GetProperty("states").EnumerateArray().Single()
                .GetProperty("language").GetString());

        var english = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", laterDate, "eng");
        Assert.AreEqual(V3Verdicts.Refuse, english.Verdict);
        Assert.AreEqual("language_not_available", english.Refusal!.Code);
        Assert.AreEqual("eng", english.Refusal.HelpfulPayload.GetProperty("requested_language").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray()
                .Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task TwoStatesOnOneDateAndLanguageRefuseAmbiguousVersionWithBothPermalinks()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twin = await fixture.AddStateAsync(fixture.ApplicabilityDate, "twin");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_version", envelope.Refusal!.Code);
        Assert.AreEqual(fixture.ApplicabilityDate,
            envelope.Refusal.HelpfulPayload.GetProperty("requested_date").GetString());
        CollectionAssert.AreEquivalent(
            new[] { fixture.Permalink, fixture.StableCoordinate + "--" + twin.StateSha256 },
            envelope.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray()
                .Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task PublisherWorkIriAndStableWorkCoordinateNameTheSameWork()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        string publisherWorkIri;
        using (var connection = LuxembourgIndexBuilder.Open(
                   Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT publisher_work_iri FROM states WHERE work_key=$work LIMIT 1";
            command.Parameters.AddWithValue("$work", fixture.WorkKey);
            publisherWorkIri = (string)command.ExecuteScalar()!;
        }
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var byIri = await AsOfAsync(mount, publisherWorkIri, fixture.ApplicabilityDate);
        var byCoordinate = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate);
        var byOrigin = await AsOfAsync(
            mount, $"https://law.soufien.lu/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate);

        foreach (var envelope in new[] { byIri, byCoordinate, byOrigin })
        {
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
            Assert.AreEqual(fixture.StateSha256,
                envelope.Result!.Value.GetProperty("states").EnumerateArray().Single()
                    .GetProperty("state_sha256").GetString());
        }
    }

    [TestMethod]
    public async Task UnknownWorkAndHashPinnedPermalinkRefuseIdentifierUnknownWithLuxembourgContext()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { "/lu-legilux/no-such-work", fixture.Permalink, "eli/unknown" })
        {
            var envelope = await AsOfAsync(mount, identifier, fixture.ApplicabilityDate);
            Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, identifier);
            Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, identifier);
            Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, identifier);
            Assert.AreEqual(identifier,
                envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
            StringAssert.Contains(
                envelope.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(),
                "Luxembourg work identifier");
        }
    }

    [TestMethod]
    public async Task EuIdentifiersAndEuOnlyMountsRefuseTheModeWithEuContext()
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
                var envelope = await AsOfAsync(mount, identifier, "2024-01-01");
                Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, identifier);
                Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code, identifier);
                Assert.AreEqual("r6_as_of", envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, identifier);
                Assert.AreEqual("eu", envelope.Context.Jurisdiction, identifier);
                Assert.AreEqual(TimelineSemantics.OfficialConsolidationState,
                    envelope.Context.TimelineSemantics, identifier);
            }
        }

        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanupEurope = europe;
        using (var europeMount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(europeMount);
            var onEuropeOnly = await AsOfAsync(europeMount, $"/lu-legilux/{luxembourg.WorkKey}", "2024-01-01");
            Assert.AreEqual(V3Verdicts.Refuse, onEuropeOnly.Verdict);
            Assert.AreEqual("retrieval_mode_unavailable", onEuropeOnly.Refusal!.Code);
            Assert.AreEqual(PublisherId.EuEurLex, onEuropeOnly.Context.Publisher);
            CollectionAssert.AreEqual(new[] { "r0_exact_coordinate" },
                onEuropeOnly.Refusal.HelpfulPayload.GetProperty("available_modes").EnumerateArray()
                    .Select(static value => value.GetString()).ToArray());
        }

        // Combined mount, built as the resolve tests build it: the Luxembourg fixture with the EU index
        // added. The Luxembourg work answers with Luxembourg context and the EU act still refuses the
        // mode with EU context; neither publisher borrows the other's.
        _ = await luxembourg.AddEuropeCollisionAsync();
        using var combined = await V3CorpusMount.OpenAsync(luxembourg.Directory, CancellationToken.None);
        Assert.IsNotNull(combined);
        var luxembourgOnCombined = await AsOfAsync(
            combined, $"/lu-legilux/{luxembourg.WorkKey}", luxembourg.ApplicabilityDate);
        Assert.AreEqual(V3Verdicts.Answer, luxembourgOnCombined.Verdict);
        Assert.AreEqual(PublisherId.LuLegilux, luxembourgOnCombined.Context.Publisher);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, luxembourgOnCombined.Context.TimelineSemantics);
        var euOnCombined = await AsOfAsync(combined, "32016R0679", "2024-01-01");
        Assert.AreEqual(V3Verdicts.Refuse, euOnCombined.Verdict);
        Assert.AreEqual("retrieval_mode_unavailable", euOnCombined.Refusal!.Code);
        Assert.AreEqual(PublisherId.EuEurLex, euOnCombined.Context.Publisher);
        Assert.AreEqual(TimelineSemantics.OfficialConsolidationState, euOnCombined.Context.TimelineSemantics);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-02-30\"}}")]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-1-1\"}}")]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/w\"}}")]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"date\":\"2026-01-01\"}}")]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\" \",\"date\":\"2026-01-01\"}}")]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-01-01\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-01-01\",\"extra\":1}}")]
    public async Task UnusableAsOfRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, AsOfRawTarget, body);

        AssertTransportProblem(context, "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndNeverReachesTheMount()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var asOfBody = "{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/"
            + fixture.WorkKey + "\",\"date\":\"" + fixture.ApplicabilityDate + "\"}}";
        var resolveBody = "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":"
            + JsonSerializer.Serialize(fixture.ExpressionIri) + "}}";

        // Each body is valid for its own route: the same bytes answer 200 there.
        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, AsOfRawTarget, asOfBody)).Response.StatusCode);
        Assert.AreEqual(StatusCodes.Status200OK,
            (await PostAsync(mount, V3ResolveRestRoute.RawTarget, resolveBody)).Response.StatusCode);

        // Posted to the other route, the route's own document refuses them before any operation runs.
        AssertTransportProblem(
            await PostAsync(mount, V3ResolveRestRoute.RawTarget, asOfBody),
            "request_schema_invalid", StatusCodes.Status400BadRequest);
        AssertTransportProblem(
            await PostAsync(mount, AsOfRawTarget, resolveBody),
            "request_schema_invalid", StatusCodes.Status400BadRequest);

        // An operation the registry does not declare keeps its own closed code at either route.
        AssertTransportProblem(
            await PostAsync(mount, AsOfRawTarget, "{\"operation_id\":\"unknown\",\"parameters\":{}}"),
            "unknown_operation", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task AsOfWithoutAMountRefusesNoCorpusMountedThroughTheSameRoute()
    {
        var context = Body(AsOfRawTarget,
            "{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-01-01\"}}");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        Assert.AreEqual("as_of", envelope.OperationId);
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> AsOfAsync(
        V3CorpusMount mount, string identifier, string date, string? language = null)
    {
        var parameters = new Dictionary<string, string> { ["identifier"] = identifier, ["date"] = date };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var body = JsonSerializer.Serialize(new { operation_id = "as_of", parameters });
        var context = await PostAsync(mount, AsOfRawTarget, body);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode,
            Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var context = Body(rawTarget, body);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static DefaultHttpContext Body(string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-as-of";
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
