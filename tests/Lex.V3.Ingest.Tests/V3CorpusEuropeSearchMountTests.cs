using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// EU <c>search</c> in one work named by identifier: the Luxembourg search's two lanes over the one wording
/// the EU index holds of the work's expression, with EU context, the Formex act date named as
/// <c>wording_date</c> and never as an applicability date, no text served, and every refusal typed.
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeSearchMountTests
{
    private const string Celex = "32016R0679";
    private const string Span = "It shall apply from 25 May 2018.";

    /// <summary>A phrase written into the Luxembourg fixture's law for the scan (the search suite's own phrase).</summary>
    private const string LuxembourgPhrase = "garantie locative";

    private static string RawTarget => V3RestRouteBinding.Search.RawTarget;

    [TestMethod]
    public async Task AnEuWorkIsSearchedInItsHeldWordingByCelexWorkOrExpressionWithEuContext()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { Celex, fixture.PublisherWorkId, fixture.PublisherExpressionId })
        {
            var envelope = await EnvelopeAsync(mount, RawTarget, "search", new { query = Span, language = "eng", identifier });
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{identifier}: {envelope.Refusal?.Code}");
            Assert.AreEqual("quote", envelope.Result!.ObjectType);
            Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, identifier);
            Assert.AreEqual("eu", envelope.Context.Jurisdiction);
            var body = envelope.Result.Value;
            Assert.AreEqual("eu-eurlex", body.GetProperty("publisher").GetString());
            Assert.AreEqual(fixture.IndexSha256, body.GetProperty("index_sha256").GetString());
            Assert.IsFalse(body.GetProperty("consolidations_held").GetBoolean());
            Assert.IsFalse(body.GetProperty("text_served").GetBoolean());
            StringAssert.Contains(body.GetProperty("date_semantics").GetString(), "never merged with a Luxembourg applicability date");
            Assert.AreEqual("not_run_identifier_given", body.GetProperty("work_resolution").GetProperty("outcome").GetString());
            var hits = body.GetProperty("hits").EnumerateArray().ToArray();
            Assert.HasCount(1, hits, identifier);
            var hit = hits[0];
            Assert.AreEqual("strict", hit.GetProperty("lane").GetString());
            Assert.AreEqual(Celex, hit.GetProperty("celex").GetString());
            Assert.AreEqual(fixture.PublisherExpressionId, hit.GetProperty("publisher_expression_id").GetString());
            Assert.AreEqual("2016-04-27", hit.GetProperty("wording_date").GetString(), "the Formex act date of the GDPR's original wording.");
            Assert.AreEqual("eng", hit.GetProperty("language").GetString());
            Assert.IsFalse(ContainsProperty(body, "applicability_date"), "an EU answer never carries a Luxembourg applicability date.");
            Assert.IsFalse(ContainsProperty(body, "text"), "no article text is served.");

            var resolved = await EnvelopeAsync(mount, V3RestRouteBinding.Resolve.RawTarget, "resolve",
                new { identifier = hit.GetProperty("resolve").GetProperty("identifier").GetString() });
            Assert.IsNull(resolved.Refusal, $"the hit's provision coordinate resolves: {resolved.Refusal?.Code}");
            Assert.AreEqual(PublisherId.EuEurLex, resolved.Context.Publisher);
        }
    }

    [TestMethod]
    public async Task TheLanesPageOnceStrictBeforeRelaxedAndFootnoteTextIsNotMatched()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        const string query = "personal data";

        var whole = (await EnvelopeAsync(mount, RawTarget, "search", new { query, language = "eng", identifier = Celex })).Result!.Value;
        var all = whole.GetProperty("hits").EnumerateArray().ToArray();
        var strictCount = whole.GetProperty("population").GetProperty("strict_hits").GetInt32();
        var relaxedCount = whole.GetProperty("population").GetProperty("relaxed_hits").GetInt32();
        Assert.IsGreaterThan(0, strictCount);
        Assert.AreEqual(strictCount + relaxedCount, all.Length, "the default answer is every strict hit, then the relaxed hits strict did not serve.");
        var lanes = all.Select(static hit => hit.GetProperty("lane").GetString()).ToArray();
        CollectionAssert.AreEqual(lanes.OrderBy(static lane => lane == "strict" ? 0 : 1).ToArray(), lanes, "relaxed never outranks strict.");
        Assert.AreEqual(
            all.Length,
            all.Select(static hit => hit.GetProperty("article_identity_sha256").GetString()).Distinct().Count(),
            "each article once.");

        var walked = new List<string>();
        string? after = null;
        do
        {
            object parameters = after is null
                ? new { query, language = "eng", identifier = Celex, limit = 7 }
                : new { query, language = "eng", identifier = Celex, limit = 7, after };
            var page = (await EnvelopeAsync(mount, RawTarget, "search", parameters)).Result!.Value;
            walked.AddRange(page.GetProperty("hits").EnumerateArray().Select(static hit => hit.GetProperty("article_identity_sha256").GetString()!));
            after = page.GetProperty("truncated").GetBoolean() ? page.GetProperty("continue_after").GetString() : null;
        }
        while (after is not null);
        CollectionAssert.AreEqual(all.Select(static hit => hit.GetProperty("article_identity_sha256").GetString()).ToArray(), walked.ToArray());

        var strictOnly = (await EnvelopeAsync(mount, RawTarget, "search", new { query, language = "eng", identifier = Celex, mode = "strict" })).Result!.Value;
        Assert.AreEqual(JsonValueKind.Null, strictOnly.GetProperty("population").GetProperty("relaxed_hits").ValueKind, "a lane not asked for is not counted.");
        var relaxedOnly = (await EnvelopeAsync(mount, RawTarget, "search", new { query, language = "eng", identifier = Celex, mode = "relaxed" })).Result!.Value;
        Assert.AreEqual(JsonValueKind.Null, relaxedOnly.GetProperty("population").GetProperty("strict_hits").ValueKind);
        Assert.AreEqual(all.Length, relaxedOnly.GetProperty("hits").GetArrayLength(), "the relaxed set contains the strict set.");

        var badCursor = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new
        {
            operation_id = "search",
            parameters = new { query, language = "eng", identifier = Celex, after = "strict." + new string('0', 64) },
        }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, badCursor.Response.StatusCode);

        var footnote = (await EnvelopeAsync(mount, RawTarget, "search", new
        {
            query = "laying down a procedure for the provision of information in the field of technical regulations",
            language = "eng",
            identifier = Celex,
        })).Result!.Value;
        Assert.AreEqual(0, footnote.GetProperty("hits").GetArrayLength(), "footnote text is not article wording.");
    }

    [TestMethod]
    public async Task EveryEuRefusalIsTypedWithEuContextAndADateIsNeverAFilter()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var date in new[] { "2010-01-01", "2016-04-27", "2018-05-25" })
        {
            var dated = await EnvelopeAsync(mount, RawTarget, "search", new { query = Span, language = "eng", identifier = Celex, date });
            Assert.AreEqual("retrieval_mode_unavailable", dated.Refusal?.Code, date);
            Assert.AreEqual("r6_as_of", dated.Refusal!.HelpfulPayload.GetProperty("requested_mode").GetString());
            Assert.AreEqual(PublisherId.EuEurLex, dated.Context.Publisher);
        }

        var french = await EnvelopeAsync(mount, RawTarget, "search", new { query = Span, language = "fra", identifier = Celex });
        Assert.AreEqual("language_not_available", french.Refusal?.Code);
        CollectionAssert.AreEqual(new[] { "eng" }, french.Refusal!.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static value => value.GetString()).ToArray());
        Assert.AreEqual(PublisherId.EuEurLex, french.Context.Publisher);

        var unknown = await EnvelopeAsync(mount, RawTarget, "search", new { query = Span, language = "eng", identifier = "32099R9999" });
        Assert.AreEqual("identifier_unknown", unknown.Refusal?.Code);
        Assert.AreEqual(PublisherId.EuEurLex, unknown.Context.Publisher);

        var ranked = await EnvelopeAsync(mount, RawTarget, "search", new { query = Span, language = "eng", identifier = Celex, mode = "bm25" });
        Assert.AreEqual("retrieval_mode_unavailable", ranked.Refusal?.Code);
        Assert.AreEqual("bm25", ranked.Refusal!.HelpfulPayload.GetProperty("requested_mode").GetString());

        var second = await fixture.AddSecondExpressionWithSamePublisherProvisionIdentifierAsync(fixture.PublisherWorkId);
        using var twoExpressions = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(twoExpressions);
        var ambiguous = await EnvelopeAsync(twoExpressions, RawTarget, "search", new { query = Span, language = "eng", identifier = fixture.PublisherWorkId });
        Assert.AreEqual("ambiguous_identifier", ambiguous.Refusal?.Code, "two English expressions of one work: none is picked.");
        CollectionAssert.AreEquivalent(
            new[] { fixture.PublisherExpressionId, second },
            ambiguous.Refusal!.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task AnIdentifierBothIndexesHoldIsAmbiguousAndLuxembourgSearchIsUnchanged()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // The publisher's work IRI names the Luxembourg work, and the EU index holds an article under it too.
        await fixture.BindPublisherWorkIdentifierAsync();
        // One article of the Luxembourg state holds the phrase searched below, as the search suite does it.
        await fixture.RewriteArticleTextAsync(
            fixture.ExpressionIri, fixture.ArticlesOfOwnState()[0].PublisherId, "La " + LuxembourgPhrase + " ne peut exceder trois mois de loyer.");
        var europeExpression = await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var collision = await EnvelopeAsync(mount, RawTarget, "search", new { query = "collision", language = "eng", identifier = fixture.PublisherWid });
        Assert.AreEqual("ambiguous_identifier", collision.Refusal?.Code, "never silently one publisher's work.");
        var candidates = collision.Refusal!.HelpfulPayload.GetProperty("candidates").EnumerateArray().Select(static value => value.GetString()).ToArray();
        CollectionAssert.Contains(candidates, europeExpression);
        CollectionAssert.Contains(candidates, fixture.ExpressionIri);

        // A phrase written into the fixture's law above, so the scan below runs over real Luxembourg hits: an answer with
        // no hit would carry no hit fields and pass whatever they said (review of #761).
        var luxembourg = await EnvelopeAsync(mount, RawTarget, "search", new { query = LuxembourgPhrase, language = "fra", identifier = $"/lu-legilux/{fixture.WorkKey}" });
        Assert.IsNull(luxembourg.Refusal, luxembourg.Refusal?.Code);
        Assert.AreEqual("lu-legilux", luxembourg.Result!.Value.GetProperty("publisher").GetString());
        var luxembourgHits = luxembourg.Result.Value.GetProperty("hits").EnumerateArray().ToArray();
        Assert.IsNotEmpty(luxembourgHits, "the scan needs Luxembourg hits to mean anything.");
        Assert.IsTrue(luxembourgHits.All(static hit => hit.TryGetProperty("applicability_date", out _)), "a Luxembourg hit names its publisher applicability date.");
        Assert.IsFalse(ContainsProperty(luxembourg.Result.Value, "wording_date"), "a Luxembourg answer never carries an EU wording date.");
        var unscoped = await EnvelopeAsync(mount, RawTarget, "search", new { query = LuxembourgPhrase, language = "fra" });
        Assert.AreEqual("lu-legilux", unscoped.Result!.Value.GetProperty("publisher").GetString(), "a search that names no work is the Luxembourg search.");
    }

    [TestMethod]
    public async Task OnACombinedMountEachPublishersWorkIsSearchedByItsOwnIndexAndMcpAnswersAsRestDoes()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddLuxembourgMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var rest = await EnvelopeAsync(mount, RawTarget, "search", new { query = Span, language = "eng", identifier = Celex });
        Assert.AreEqual("eu-eurlex", rest.Result!.Value.GetProperty("publisher").GetString());
        var mcp = await PostAsync(mount, V3ApiHandler.McpRawTarget, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "search", arguments = new { query = Span, language = "eng", identifier = Celex } },
        }));
        using var rpc = JsonDocument.Parse(ResponseBytes(mcp));
        var structured = rpc.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.AreEqual(rest.Verdict, structured.GetProperty("verdict").GetString());
        Assert.IsTrue(JsonElement.DeepEquals(rest.Result.Value, structured.GetProperty("result").GetProperty("value")));

        var luxembourg = await EnvelopeAsync(mount, RawTarget, "search", new { query = "Regulation", language = "fra" });
        Assert.AreEqual(PublisherId.LuLegilux, luxembourg.Context.Publisher, "an unscoped search stays the Luxembourg search.");
    }

    private static bool ContainsProperty(JsonElement element, string name) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(property =>
            string.Equals(property.Name, name, StringComparison.Ordinal) || ContainsProperty(property.Value, name)),
        JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsProperty(item, name)),
        _ => false,
    };
}
