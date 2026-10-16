using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>evidence_bundle</c> and <c>as_of</c> at a hash-pinned permalink (the specification of record's R64, the plan's item 3.2),
/// driven through the real handler on a verified mount: the identifier is the permalink the reading page stands at, and the
/// answer is the version it pins, never another version in its place. A digest the coordinate no longer carries in the
/// permalink's language is <c>pinned_digest_mismatch</c> naming the current state, with the payload <c>verify</c> gives the
/// same permalink; a withheld text is <c>text_withheld</c> with the state's digest beside the body's; the date asked must be
/// the permalink's own; and a permalink pins one of two states of its language at one date without the date being ambiguous.
/// </summary>
[TestClass]
public sealed class V3CorpusPinnedPermalinkMountTests
{
    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task AStatesPermalinkAnswersTheStateItPinsAsTheWorkAtItsDateAnswersIt()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var pinned = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, language = "fra" });
        var byWork = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" });
        Assert.IsNull(pinned.Refusal, pinned.Refusal?.Code);
        Assert.AreEqual(fixture.Permalink, pinned.Result!.Value.GetProperty("requested_identifier").GetString(), "the identifier asked is the permalink");
        var state = pinned.Result.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.StateSha256, state.GetProperty("state_sha256").GetString());
        Assert.AreEqual(
            JsonSerializer.Serialize(byWork.Result!.Value.GetProperty("states")),
            JsonSerializer.Serialize(pinned.Result.Value.GetProperty("states")),
            "the state, its text and digests are those the work at that date and language answers");
        StringAssert.Contains(pinned.Result.Value.GetProperty("scope").GetString(), "hash-pinned permalink");

        // Asked with no language, the answer is still in the permalink's: it names one.
        var unasked = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate });
        Assert.AreEqual("fra", unasked.Result!.Value.GetProperty("requested_language").GetString());

        // as_of answers it the same way, the state's row as as_of serves it.
        var asOf = await EnvelopeAsync(mount, "as_of", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate });
        Assert.IsNull(asOf.Refusal, asOf.Refusal?.Code);
        Assert.AreEqual(fixture.Permalink, asOf.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("permalink").GetString());
    }

    [TestMethod]
    public async Task AnEarlierStatesPermalinkAnswersThatStateWithTheDateTheNewerOneAppliesFrom()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var operation in new[] { "evidence_bundle", "as_of" })
        {
            var earlier = await EnvelopeAsync(mount, operation, new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, language = "fra" });
            var state = earlier.Result!.Value.GetProperty("states").EnumerateArray().Single();
            Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString(), operation);
            Assert.AreEqual(laterDate, state.GetProperty("next_applicability_date").GetString(), $"{operation}: the newer version's date, which the page's banner says");

            var latest = await EnvelopeAsync(mount, operation, new { identifier = MountedFixture.PermalinkOf(later), date = laterDate, language = "fra" });
            var latestState = latest.Result!.Value.GetProperty("states").EnumerateArray().Single();
            Assert.AreEqual(later.StateSha256, latestState.GetProperty("state_sha256").GetString(), operation);
            Assert.AreEqual(JsonValueKind.Null, latestState.GetProperty("next_applicability_date").ValueKind, $"{operation}: the latest held has no next date");
        }
    }

    [TestMethod]
    public async Task ADigestTheCoordinateNoLongerCarriesIsRefusedWithTheCurrentLinkAsVerifyRefusesIt()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var stale = fixture.PermalinkOf(new string('0', 64));
        var verify = await EnvelopeAsync(mount, "verify", new { identifier = stale, language = "fra" });
        Assert.AreEqual("pinned_digest_mismatch", verify.Refusal!.Code);

        foreach (var operation in new[] { "evidence_bundle", "as_of" })
        {
            var envelope = await EnvelopeAsync(mount, operation, new { identifier = stale, date = fixture.ApplicabilityDate, language = "fra" });
            Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, operation);
            Assert.AreEqual("pinned_digest_mismatch", envelope.Refusal!.Code, operation);
            var payload = envelope.Refusal.HelpfulPayload;
            Assert.AreEqual(fixture.Permalink, payload.GetProperty("current_hash_pinned_url").GetString(), $"{operation}: the current link");
            Assert.AreEqual(fixture.StateSha256, payload.GetProperty("current_digest").GetString(), operation);
            Assert.AreEqual(new string('0', 64), payload.GetProperty("requested_digest").GetString(), operation);
            Assert.AreEqual(
                JsonSerializer.Serialize(verify.Refusal.HelpfulPayload),
                JsonSerializer.Serialize(payload),
                $"{operation} refuses the permalink with the payload verify gives it");
        }
    }

    [TestMethod]
    public async Task APermalinkPinsOneOfTwoStatesOfItsLanguageAtOneDateAndADigestNeitherCarriesIsAmbiguous()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twinDate = Shift(fixture.ApplicabilityDate, 800);
        var twinA = await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // The work at that date is ambiguous; the permalink of one of the two is not: it pins that one.
        var byWork = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = twinDate, language = "fra" });
        Assert.AreEqual("ambiguous_version", byWork.Refusal!.Code);
        var pinned = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = MountedFixture.PermalinkOf(twinA), date = twinDate, language = "fra" });
        Assert.IsNull(pinned.Refusal, pinned.Refusal?.Code);
        Assert.AreEqual(twinA.StateSha256, pinned.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state_sha256").GetString());

        var neither = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}/{twinDate}--{new string('0', 64)}/fra", date = twinDate });
        var verify = await EnvelopeAsync(mount, "verify", new { identifier = $"/lu-legilux/{fixture.WorkKey}/{twinDate}--{new string('0', 64)}/fra" });
        Assert.AreEqual("ambiguous_identifier", neither.Refusal!.Code);
        Assert.AreEqual(JsonSerializer.Serialize(verify.Refusal!.HelpfulPayload), JsonSerializer.Serialize(neither.Refusal.HelpfulPayload));
    }

    [TestMethod]
    public async Task ALanguageAnotherDateACoordinateWithNoStateAndAnArticlesPermalinkAreRefused()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // Another language asked beside the permalink, or a permalink naming a language the coordinate does not hold.
        var asked = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, language = "deu" });
        Assert.AreEqual("language_not_available", asked.Refusal!.Code);
        CollectionAssert.AreEqual(new[] { "fra" }, asked.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static value => value.GetString()).ToArray());
        var named = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = fixture.PermalinkOf(fixture.StateSha256, "deu"), date = fixture.ApplicabilityDate });
        Assert.AreEqual("language_not_available", named.Refusal!.Code);

        // A coordinate holding no state: the permalink names nothing this index holds.
        var elsewhere = $"/lu-legilux/{fixture.WorkKey}/1999-01-01--{fixture.StateSha256}/fra";
        var nowhere = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = elsewhere, date = "1999-01-01" });
        Assert.AreEqual("identifier_unknown", nowhere.Refusal!.Code);

        // An article's permalink names an article, not a state: it is not an identifier these operations take, as resolve.
        var article = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = fixture.Permalink + "/art_15", date = fixture.ApplicabilityDate });
        Assert.AreEqual("identifier_unknown", article.Refusal!.Code);

        // A date other than the permalink's own asks for another version than the one it pins: a malformed request.
        foreach (var operation in new[] { "evidence_bundle", "as_of" })
        {
            var context = await PostAsync(mount, operation, new { identifier = fixture.Permalink, date = Shift(fixture.ApplicabilityDate, 1), language = "fra" });
            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode, operation);
            using var problem = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
            Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString(), operation);
        }
    }

    [TestMethod]
    public async Task AWithheldStatesPermalinkIsRefusedTextWithheldWithTheStatesDigestAndTheBodys()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var texts = fixture.ArticlesOfOwnState().Select(static article => article.Text).Where(static text => text.Length > 0).ToArray();
        await fixture.SetMemberRightsDispositionAsync("non_admitting_licence_scl");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, "evidence_bundle", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, language = "fra" });
        var wire = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        var envelope = V3EnvelopeJson.ParseAndVerify(((MemoryStream)context.Response.Body).ToArray(), V3OperationRegistry.Reviewed);
        Assert.AreEqual("text_withheld", envelope.Refusal!.Code);
        var payload = envelope.Refusal.HelpfulPayload;
        Assert.AreEqual(fixture.StateSha256, payload.GetProperty("state_sha256").GetString(), "the state's digest, which the permalink pins");
        Assert.AreEqual(fixture.Permalink, payload.GetProperty("permalink").GetString());
        Assert.AreEqual(64, payload.GetProperty("content_sha256").GetString()!.Length, "and the withholding body's digest");
        foreach (var text in texts)
        {
            Assert.IsFalse(wire.Contains(text, StringComparison.Ordinal), "no article text reaches the wire at a withheld permalink");
        }
    }

    [TestMethod]
    public async Task AnEuWordingsPermalinkAnswersTheWordingItPinsAndADigestItDoesNotCarryIsRefusedAsVerifyRefusesIt()
    {
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = europe;
        using var mount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var dossier = await EnvelopeAsync(mount, "dossier", new { identifier = "32016R0679" });
        var pin = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().Single().GetProperty("pinned_wording");
        var permalink = pin.GetProperty("permalink").GetString()!;
        var date = pin.GetProperty("wording_date").GetString()!;

        var pinned = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = permalink, date });
        Assert.IsNull(pinned.Refusal, pinned.Refusal?.Code);
        Assert.AreEqual(permalink, pinned.Result!.Value.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(permalink, pinned.Result.Value.GetProperty("wordings").EnumerateArray().Single().GetProperty("permalink").GetString());

        var stale = permalink.Replace(pin.GetProperty("wording_sha256").GetString()!, new string('0', 64), StringComparison.Ordinal);
        var refused = await EnvelopeAsync(mount, "evidence_bundle", new { identifier = stale, date });
        var verify = await EnvelopeAsync(mount, "verify", new { identifier = stale });
        Assert.AreEqual("pinned_digest_mismatch", refused.Refusal!.Code);
        Assert.AreEqual(permalink, refused.Refusal.HelpfulPayload.GetProperty("current_hash_pinned_url").GetString(), "the current link");
        Assert.AreEqual(JsonSerializer.Serialize(verify.Refusal!.HelpfulPayload), JsonSerializer.Serialize(refused.Refusal.HelpfulPayload));

        // as_of answers it alike, through the time view the EU census serves.
        var asOf = await EnvelopeAsync(mount, "as_of", new { identifier = permalink, date });
        Assert.IsNull(asOf.Refusal, asOf.Refusal?.Code);
        Assert.AreEqual(permalink, asOf.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("wording").GetProperty("permalink").GetString());
        var asOfRefused = await EnvelopeAsync(mount, "as_of", new { identifier = stale, date });
        Assert.AreEqual(JsonSerializer.Serialize(verify.Refusal.HelpfulPayload), JsonSerializer.Serialize(asOfRefused.Refusal!.HelpfulPayload));
    }

    private static async Task<V3Envelope> EnvelopeAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var context = await PostAsync(mount, operation, parameters);
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(bytes));
        return V3EnvelopeJson.ParseAndVerify(bytes, V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-pinned-permalink";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }
}
