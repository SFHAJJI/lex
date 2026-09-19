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
/// The second R6 temporal operation, driven through the real handler on a verified mount: the
/// inventory of every publisher-dated Luxembourg state of one work, per language, in one vocabulary
/// with <c>as_of</c>, with every refusal the closed registry names for it and the route binding that
/// keeps another operation's body out.
/// </summary>
[TestClass]
public sealed class V3CorpusTimelineMountTests
{
    private const string TimelineRawTarget = "/api/v3/timeline";
    private const string AsOfRawTarget = "/api/v3/as_of";

    [TestMethod]
    public async Task OneStateIsOneRowWithHistoryBeginsAndNoNextDate()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}");

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("timeline", envelope.OperationId);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
        Assert.AreEqual("timeline", envelope.Result!.ObjectType);
        var value = envelope.Result.Value;
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", value.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(JsonValueKind.Null, value.GetProperty("requested_language").ValueKind);
        Assert.AreEqual("lu-legilux", value.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.WorkKey, value.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("history_begins").GetString());
        Assert.AreEqual(1, value.GetProperty("state_count").GetInt32());
        Assert.AreEqual(fixture.CorpusSha256, value.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(fixture.IndexSha256, value.GetProperty("index_sha256").GetString());
        CollectionAssert.AreEqual(new[] { "fra" },
            value.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
        var row = value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("fra", row.GetProperty("language").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, row.GetProperty("applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("next_applicability_date").ValueKind,
            "The last publisher-dated state has no next date; no end is invented.");
        Assert.AreEqual(fixture.StateSha256, row.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.ExpressionIri, row.GetProperty("expression_iri").GetString());
        Assert.AreEqual(fixture.StableCoordinate, row.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.Permalink, row.GetProperty("permalink").GetString());
        var identities = row.GetProperty("article_identities").EnumerateArray().Select(static v => v.GetString()).ToArray();
        Assert.IsGreaterThan(0, identities.Length);
        CollectionAssert.AreEqual(identities,
            row.GetProperty("articles").EnumerateArray()
                .Select(static article => article.GetProperty("article_identity_sha256").GetString()).ToArray(),
            "Every article of the state carries its publisher date, in the state's order.");
        Assert.AreEqual(JsonValueKind.Number, row.GetProperty("validity_conflict_count").ValueKind);
        StringAssert.Contains(row.GetProperty("validity_conflict_rule").GetString(),
            "differs from the state's applicability_date");
        // No end date, no "in force", no gap or overlap label anywhere on a row.
        foreach (var forbidden in new[] { "valid_to", "end_date", "in_force", "gap", "overlap" })
        {
            Assert.IsFalse(row.TryGetProperty(forbidden, out _), forbidden);
        }
    }

    [TestMethod]
    public async Task StatesAreListedInDateOrderAndEachNextDateIsScopedToItsOwnLanguage()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        // The German text begins 400 days after the French one, on the same date as the second French
        // state, so the two languages' histories start apart and one date carries two rows.
        var german = await fixture.AddSecondLanguageStateAsync(laterDate);
        var later = await fixture.AddStateAsync(laterDate, "later");
        var latestDate = Shift(fixture.ApplicabilityDate, 800);
        var latest = await fixture.AddStateAsync(latestDate, "latest");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var all = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}");

        Assert.AreEqual(V3Verdicts.Answer, all.Verdict);
        var value = all.Result!.Value;
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("history_begins").GetString());
        Assert.AreEqual(4, value.GetProperty("state_count").GetInt32());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            value.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
        var rows = value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(4, rows);
        // Reader order: date, then language: on the shared date the German row sorts before the French.
        CollectionAssert.AreEqual(
            new[] { fixture.StateSha256, german.StateSha256, later.StateSha256, latest.StateSha256 },
            rows.Select(static row => row.GetProperty("state_sha256").GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[] { fixture.ApplicabilityDate, laterDate, laterDate, latestDate },
            rows.Select(static row => row.GetProperty("applicability_date").GetString()).ToArray());
        CollectionAssert.AreEqual(new[] { "fra", "deu", "fra", "fra" },
            rows.Select(static row => row.GetProperty("language").GetString()).ToArray());
        // Each French row is bounded by the next French date; the German row by nothing, because no
        // German state follows it, however many French ones do.
        Assert.AreEqual(laterDate, rows[0].GetProperty("next_applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, rows[1].GetProperty("next_applicability_date").ValueKind,
            "A French change date must never be reported as a change of the German text.");
        Assert.AreEqual(latestDate, rows[2].GetProperty("next_applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, rows[3].GetProperty("next_applicability_date").ValueKind);

        // Filtered to one language: only its rows, its own history start (not the work's), and the
        // work-wide language list.
        var germanOnly = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "deu");
        Assert.AreEqual(V3Verdicts.Answer, germanOnly.Verdict);
        Assert.AreEqual("deu", germanOnly.Result!.Value.GetProperty("requested_language").GetString());
        Assert.AreEqual(laterDate, germanOnly.Result.Value.GetProperty("history_begins").GetString(),
            "The German history begins where the first German state is, not where the work's first state is.");
        Assert.AreEqual(1, germanOnly.Result.Value.GetProperty("state_count").GetInt32());
        Assert.AreEqual(german.StateSha256,
            germanOnly.Result.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            germanOnly.Result.Value.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());

        var frenchOnly = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "fra");
        Assert.AreEqual(fixture.ApplicabilityDate, frenchOnly.Result!.Value.GetProperty("history_begins").GetString());
        CollectionAssert.AreEqual(
            new[] { fixture.StateSha256, later.StateSha256, latest.StateSha256 },
            frenchOnly.Result.Value.GetProperty("states").EnumerateArray()
                .Select(static row => row.GetProperty("state_sha256").GetString()).ToArray());

        var english = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "eng");
        Assert.AreEqual(V3Verdicts.Refuse, english.Verdict);
        Assert.AreEqual("language_not_available", english.Refusal!.Code);
        Assert.AreEqual(PublisherId.LuLegilux, english.Context.Publisher);
        Assert.AreEqual("eng", english.Refusal.HelpfulPayload.GetProperty("requested_language").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray()
                .Select(static v => v.GetString()).ToArray());
    }

    [TestMethod]
    public async Task TwoStatesOnOneDateAndLanguageAreBothListedNotRefused()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twin = await fixture.AddStateAsync(fixture.ApplicabilityDate, "twin");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}");

        // An inventory selects nothing, so it refuses nothing a selection refuses: the reader sees
        // both rows, in the reader's order (same date and language, so by expression IRI), and as_of
        // on the same date still refuses ambiguous_version.
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(2, rows);
        CollectionAssert.AreEquivalent(
            new[] { fixture.StateSha256, twin.StateSha256 },
            rows.Select(static row => row.GetProperty("state_sha256").GetString()).ToArray());
        CollectionAssert.AreEqual(
            rows.Select(static row => row.GetProperty("expression_iri").GetString()).Order(StringComparer.Ordinal).ToArray(),
            rows.Select(static row => row.GetProperty("expression_iri").GetString()).ToArray());
        Assert.IsTrue(rows.All(row => row.GetProperty("applicability_date").GetString() == fixture.ApplicabilityDate));
        Assert.IsTrue(rows.All(row => row.GetProperty("next_applicability_date").ValueKind == JsonValueKind.Null));
        Assert.AreEqual(2, envelope.Result.Value.GetProperty("state_count").GetInt32());

        var selection = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate);
        Assert.AreEqual("ambiguous_version", selection.Refusal!.Code);
    }

    [TestMethod]
    public async Task EveryRowIsByteIdenticalToTheAsOfStateOnItsOwnDate()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAtSameDateAsync();
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        await fixture.NullOneArticleDateAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var timeline = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}");
        var rows = timeline.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(3, rows);

        // One vocabulary for a dated state: asked for on its own date and in its own language, as_of
        // serves the very same row, article dates and conflict flags included.
        foreach (var row in rows)
        {
            var date = row.GetProperty("applicability_date").GetString()!;
            var language = row.GetProperty("language").GetString();
            var selected = await AsOfAsync(mount, $"/lu-legilux/{fixture.WorkKey}", date, language);
            Assert.AreEqual(V3Verdicts.Answer, selected.Verdict, $"{date} {language}");
            var state = selected.Result!.Value.GetProperty("states").EnumerateArray().Single();
            Assert.AreEqual(state.GetRawText(), row.GetRawText(), $"{date} {language}");
        }
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

        var byIri = await TimelineAsync(mount, publisherWorkIri);
        var byCoordinate = await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}");
        var byOrigin = await TimelineAsync(mount, $"https://law.soufien.lu/lu-legilux/{fixture.WorkKey}");

        foreach (var envelope in new[] { byIri, byCoordinate, byOrigin })
        {
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
            Assert.AreEqual(fixture.StateSha256,
                envelope.Result!.Value.GetProperty("states").EnumerateArray().Single()
                    .GetProperty("state_sha256").GetString());
        }

        foreach (var foreign in new[]
                 {
                     $"https://any-host.example/lu-legilux/{fixture.WorkKey}",
                     $"http://law.soufien.lu/lu-legilux/{fixture.WorkKey}",
                     $"https://law.soufien.lu:8443/lu-legilux/{fixture.WorkKey}",
                     $"https://user@law.soufien.lu/lu-legilux/{fixture.WorkKey}",
                     $"https://law.soufien.lu/lu-legilux/{fixture.WorkKey}?x=1",
                 })
        {
            var envelope = await TimelineAsync(mount, foreign);
            Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, foreign);
            Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, foreign);
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
            var envelope = await TimelineAsync(mount, identifier);
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
                var envelope = await TimelineAsync(mount, identifier);
                Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, identifier);
                Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code, identifier);
                Assert.AreEqual("r6_timeline", envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
                CollectionAssert.AreEqual(new[] { "r0_exact_coordinate" },
                    envelope.Refusal.HelpfulPayload.GetProperty("available_modes").EnumerateArray()
                        .Select(static v => v.GetString()).ToArray());
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
            foreach (var luxembourgIdentifier in new[]
                     {
                         $"/lu-legilux/{luxembourg.WorkKey}",
                         luxembourg.Permalink,
                         "eli/etat/leg/loi/2004/07/30/n1/jo",
                     })
            {
                var onEuropeOnly = await TimelineAsync(europeMount, luxembourgIdentifier);
                Assert.AreEqual(V3Verdicts.Refuse, onEuropeOnly.Verdict, luxembourgIdentifier);
                Assert.AreEqual("no_corpus_mounted", onEuropeOnly.Refusal!.Code, luxembourgIdentifier);
                Assert.AreEqual("lu", onEuropeOnly.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
                Assert.AreEqual(PublisherId.LuLegilux, onEuropeOnly.Context.Publisher, luxembourgIdentifier);
                Assert.AreEqual(TimelineSemantics.PublisherApplicability,
                    onEuropeOnly.Context.TimelineSemantics, luxembourgIdentifier);
            }

            var euOnEuropeOnly = await TimelineAsync(europeMount, "32016R0679");
            Assert.AreEqual("retrieval_mode_unavailable", euOnEuropeOnly.Refusal!.Code);
            Assert.AreEqual("r6_timeline", euOnEuropeOnly.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
            Assert.AreEqual(PublisherId.EuEurLex, euOnEuropeOnly.Context.Publisher);
        }

        _ = await luxembourg.AddEuropeCollisionAsync();
        using var combined = await V3CorpusMount.OpenAsync(luxembourg.Directory, CancellationToken.None);
        Assert.IsNotNull(combined);
        var luxembourgOnCombined = await TimelineAsync(combined, $"/lu-legilux/{luxembourg.WorkKey}");
        Assert.AreEqual(V3Verdicts.Answer, luxembourgOnCombined.Verdict);
        Assert.AreEqual(PublisherId.LuLegilux, luxembourgOnCombined.Context.Publisher);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, luxembourgOnCombined.Context.TimelineSemantics);
        var euOnCombined = await TimelineAsync(combined, "32016R0679");
        Assert.AreEqual(V3Verdicts.Refuse, euOnCombined.Verdict);
        Assert.AreEqual("retrieval_mode_unavailable", euOnCombined.Refusal!.Code);
        Assert.AreEqual(PublisherId.EuEurLex, euOnCombined.Context.Publisher);
        Assert.AreEqual(TimelineSemantics.OfficialConsolidationState, euOnCombined.Context.TimelineSemantics);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"timeline\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\" \"}}")]
    [DataRow("{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-01-01\"}}")]
    [DataRow("{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"extra\":1}}")]
    [DataRow("{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":1}}")]
    public async Task UnusableTimelineRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, TimelineRawTarget, body);

        AssertTransportProblem(context, "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndAQueryStringIsDrift()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var timelineBody = "{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\"/lu-legilux/"
            + fixture.WorkKey + "\"}}";
        var asOfBody = "{\"operation_id\":\"as_of\",\"parameters\":{\"identifier\":\"/lu-legilux/"
            + fixture.WorkKey + "\",\"date\":\"" + fixture.ApplicabilityDate + "\"}}";
        var resolveBody = "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":"
            + JsonSerializer.Serialize(fixture.ExpressionIri) + "}}";

        // The body is valid for its own route.
        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, TimelineRawTarget, timelineBody)).Response.StatusCode);

        // Posted to another route, or another operation's body posted here, the route's own document
        // refuses before any operation runs. A resolve body is a subset of a timeline body in shape;
        // the operation_id constant is what keeps it out.
        foreach (var (rawTarget, body) in new[]
                 {
                     (V3ResolveRestRoute.RawTarget, timelineBody),
                     (AsOfRawTarget, timelineBody),
                     (TimelineRawTarget, asOfBody),
                     (TimelineRawTarget, resolveBody),
                 })
        {
            AssertTransportProblem(
                await PostAsync(mount, rawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest,
                rawTarget + " " + body);
        }

        AssertTransportProblem(
            await PostAsync(mount, TimelineRawTarget, "{\"operation_id\":\"unknown\",\"parameters\":{}}"),
            "unknown_operation", StatusCodes.Status400BadRequest);

        // A query string on the route is transport drift, refused below the envelope; nothing runs.
        AssertTransportProblem(
            await PostAsync(mount, TimelineRawTarget + "?x=1", timelineBody),
            "unknown_route", StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task TimelineWithoutAMountRefusesNoCorpusMountedThroughTheSameRoute()
    {
        var context = Body(TimelineRawTarget,
            "{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\"/lu-legilux/w\"}}");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        Assert.AreEqual("timeline", envelope.OperationId);
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> TimelineAsync(V3CorpusMount mount, string identifier, string? language = null)
    {
        var parameters = new Dictionary<string, string> { ["identifier"] = identifier };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var body = JsonSerializer.Serialize(new { operation_id = "timeline", parameters });
        var context = await PostAsync(mount, TimelineRawTarget, body);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode,
            Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

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
        context.TraceIdentifier = "mounted-corpus-timeline";
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
