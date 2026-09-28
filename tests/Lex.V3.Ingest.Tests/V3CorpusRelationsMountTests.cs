using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>relations</c> on a mounted corpus: the one edge table read in both directions. The outbound
/// edges are exactly what <c>citation</c> serves for the state at the date (or the latest state),
/// the inbound edges exactly what <c>cited_by</c> serves, in the stated order, paged by one cursor,
/// every edge <c>cites</c> asserted by the publisher's text, and nothing assessed.
/// </summary>
[TestClass]
public sealed class V3CorpusRelationsMountTests
{
    private static string RawTarget => V3RestRouteBinding.Relations.RawTarget;
    private const string Work = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Reference(string label, string? target) =>
        JsonSerializer.Serialize(new { kind = "reference", text = label, target, marker = (string?)null, note_body = (object?)null });

    /// <summary>The real act plus a citing state of another work (three references to this act, one to another) and a later self-citing state.</summary>
    private static async Task<(MountedFixture Fixture, LuxembourgIndexBuilder.StateRow Other, LuxembourgIndexBuilder.StateRow Later)> BuildAsync()
    {
        var fixture = await MountedFixture.CreateAsync();
        var other = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "citing", workLeaf: "n9");
        var later = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 800), "self");
        var otherTokens = "[" + string.Join(",",
            "{\"kind\":\"text\",\"text\":\"avant \",\"target\":null,\"marker\":null,\"note_body\":null}",
            Reference("another act", "/eli/etat/leg/loi/2004/07/09/n3/jo"),
            Reference("the act, /jo form", "/eli/etat/leg/loi/1991/08/10/n3/jo"),
            Reference("the act, work form", Work),
            Reference("the act, https", "https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo")) + "]";
        await fixture.SetArticleTokensAsync(other.ExpressionIri, "art_1er", "avant", otherTokens);
        await fixture.SetArticleTokensAsync(later.ExpressionIri, "art_1er", "avant", "[" + Reference("itself", Work + "/jo") + "]");
        return (fixture, other, later);
    }

    [TestMethod]
    public void TheRelationsRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/relations", RawTarget);
        Assert.AreEqual("relations", V3RestRouteBinding.Relations.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Relations));
    }

    [TestMethod]
    public async Task BothDirectionsAreCitationsEdgesThenCitedBysEdgesAndNothingIsAssessed()
    {
        var (fixture, other, later) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        // Without a date, the outbound edges are the latest state's: the self-citing state.
        var envelope = await RelationsAsync(mount, new { identifier });
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("relations", envelope.OperationId);
        Assert.AreEqual("relation_edge", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.AreEqual(JsonValueKind.False, body.GetProperty("relationship_type_assessed").ValueKind);
        Assert.AreEqual(JsonValueKind.False, body.GetProperty("current_legal_effect_assessed").ValueKind);
        Assert.AreEqual("both", body.GetProperty("requested_direction").GetString());
        Assert.AreEqual("cites", body.GetProperty("edge_type").GetString());
        Assert.AreEqual("publisher_text", body.GetProperty("asserted_by").GetString());
        Assert.AreEqual("akn_ref", body.GetProperty("source_predicate").GetString());

        var citation = await OtherAsync(mount, V3RestRouteBinding.Citation.RawTarget, "citation",
            new { identifier, date = later.ApplicabilityDate });
        var citedBy = await OtherAsync(mount, V3RestRouteBinding.CitedBy.RawTarget, "cited_by", new { identifier });
        var outboundExpected = citation.Result!.Value.GetProperty("edges").EnumerateArray()
            .Select(static edge => (edge.GetProperty("article_identity_sha256").GetString(), edge.GetProperty("ordinal").GetInt32(), edge.GetProperty("href").GetString()))
            .ToArray();
        var inboundExpected = citedBy.Result!.Value.GetProperty("edges").EnumerateArray()
            .Select(static edge => (edge.GetProperty("article_identity_sha256").GetString(), edge.GetProperty("ordinal").GetInt32(), edge.GetProperty("href").GetString()))
            .ToArray();
        var edges = body.GetProperty("edges").EnumerateArray().ToArray();
        var outbound = edges.Where(static edge => edge.GetProperty("direction").GetString() == "outbound").ToArray();
        var inbound = edges.Where(static edge => edge.GetProperty("direction").GetString() == "inbound").ToArray();
        CollectionAssert.AreEqual(
            outboundExpected,
            outbound.Select(static edge => (edge.GetProperty("article_identity_sha256").GetString(), edge.GetProperty("ordinal").GetInt32(), edge.GetProperty("href").GetString())).ToArray(),
            "the outbound edges are citation's edges for the latest state, in its order.");
        CollectionAssert.AreEqual(
            inboundExpected,
            inbound.Select(static edge => (edge.GetProperty("article_identity_sha256").GetString(), edge.GetProperty("ordinal").GetInt32(), edge.GetProperty("href").GetString())).ToArray(),
            "the inbound edges are cited_by's edges, in its order.");
        Assert.AreEqual(outboundExpected.Length + inboundExpected.Length, body.GetProperty("edge_count").GetInt32());
        Assert.AreEqual(outbound.Length, body.GetProperty("edge_counts").GetProperty("outbound").GetInt32());
        Assert.AreEqual(inbound.Length, body.GetProperty("edge_counts").GetProperty("inbound").GetInt32());
        Assert.IsTrue(edges.Take(outbound.Length).All(static edge => edge.GetProperty("direction").GetString() == "outbound"), "outbound edges come first.");
        Assert.IsTrue(edges.All(static edge => edge.GetProperty("edge_type").GetString() == "cites"));

        // The later state carries the act's references plus the one it makes to itself: that edge
        // resolves to this work and is a self reference; the others resolve as citation says.
        var selfOutbound = outbound.Where(static edge => edge.GetProperty("is_self_reference").ValueKind == JsonValueKind.True).ToArray();
        Assert.AreEqual(1, selfOutbound.Length);
        Assert.AreEqual("held_work", selfOutbound[0].GetProperty("resolution").GetString());
        Assert.AreEqual(fixture.WorkKey, selfOutbound[0].GetProperty("target_work_key").GetString());
        CollectionAssert.AreEqual(
            citation.Result.Value.GetProperty("edges").EnumerateArray().Select(static edge => edge.GetProperty("resolution").GetString()).ToArray(),
            outbound.Select(static edge => edge.GetProperty("resolution").GetString()).ToArray());
        // The inbound edges are the other work's references to this act (exact-IRI matches, as cited_by
        // reads them) and the later state's own.
        Assert.IsGreaterThan(0, inbound.Count(edge => edge.GetProperty("citing_work_key").GetString() == other.WorkKey));
        Assert.IsTrue(inbound.Where(edge => edge.GetProperty("citing_work_key").GetString() == other.WorkKey)
            .All(static edge => edge.GetProperty("is_self_reference").ValueKind == JsonValueKind.False));
        Assert.IsTrue(inbound.Where(edge => edge.GetProperty("citing_work_key").GetString() == fixture.WorkKey)
            .All(static edge => edge.GetProperty("is_self_reference").ValueKind == JsonValueKind.True));
        Assert.IsTrue(inbound.All(edge => edge.GetProperty("target_work_key").GetString() == fixture.WorkKey));

        var states = body.GetProperty("states").EnumerateArray().ToArray();
        Assert.AreEqual(1, states.Length);
        Assert.AreEqual(later.StateSha256, states[0].GetProperty("state_sha256").GetString());
        Assert.AreEqual(outbound.Length, states[0].GetProperty("outbound_edges").GetInt32(), "the state's count is the outbound edges served for it.");
        Assert.AreEqual(4, body.GetProperty("not_held").GetArrayLength());
    }

    [TestMethod]
    public async Task ADateSelectsTheOutboundStateAndADirectionNarrowsTheEdges()
    {
        var (fixture, _, later) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var atFirst = (await RelationsAsync(mount, new { identifier, date = fixture.ApplicabilityDate, direction = "outbound" })).Result!.Value;
        Assert.AreEqual(fixture.StateSha256, atFirst.GetProperty("states")[0].GetProperty("state_sha256").GetString());
        Assert.AreEqual(0, atFirst.GetProperty("edge_counts").GetProperty("inbound").GetInt32());
        var citation = await OtherAsync(mount, V3RestRouteBinding.Citation.RawTarget, "citation", new { identifier, date = fixture.ApplicabilityDate });
        Assert.AreEqual(citation.Result!.Value.GetProperty("edge_count").GetInt32(), atFirst.GetProperty("edge_count").GetInt32(),
            "outbound at the first state's date is citation at that date.");

        var citedBy = await OtherAsync(mount, V3RestRouteBinding.CitedBy.RawTarget, "cited_by", new { identifier });
        var inboundOnly = (await RelationsAsync(mount, new { identifier, direction = "inbound" })).Result!.Value;
        Assert.AreEqual(0, inboundOnly.GetProperty("edge_counts").GetProperty("outbound").GetInt32());
        Assert.AreEqual(citedBy.Result!.Value.GetProperty("edge_count").GetInt32(), inboundOnly.GetProperty("edge_counts").GetProperty("inbound").GetInt32(),
            "inbound only is cited_by.");
        Assert.IsTrue(inboundOnly.GetProperty("edges").EnumerateArray().All(static edge => edge.GetProperty("direction").GetString() == "inbound"));
        Assert.AreEqual(0, inboundOnly.GetProperty("states").GetArrayLength(),
            "inbound alone selects no outbound state, so none is named and no count is stated for it (review finding on this pull request).");

        var beforeAny = await RelationsAsync(mount, new { identifier, date = "1900-01-01" });
        Assert.AreEqual("no_version_for_date", beforeAny.Refusal!.Code);
    }

    [TestMethod]
    public async Task OneCursorPagesAcrossBothDirectionsAndAForeignCursorIsRefusedAtTheTransport()
    {
        var (fixture, _, _) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var whole = (await RelationsAsync(mount, new { identifier })).Result!.Value;
        var expected = whole.GetProperty("edges").EnumerateArray()
            .Select(static edge => edge.GetProperty("direction").GetString() + ":" + edge.GetProperty("article_identity_sha256").GetString() + ":" + edge.GetProperty("ordinal").GetInt32())
            .ToArray();
        Assert.IsGreaterThan(4, expected.Length);
        Assert.AreEqual(JsonValueKind.False, whole.GetProperty("truncated").ValueKind);

        var walked = new List<string>();
        string? after = null;
        var pageSize = expected.Length / 3 + 1;
        for (var page = 0; page < 10; page++)
        {
            var value = (after is null
                ? await RelationsAsync(mount, new { identifier, limit = pageSize })
                : await RelationsAsync(mount, new { identifier, limit = pageSize, after })).Result!.Value;
            walked.AddRange(value.GetProperty("edges").EnumerateArray()
                .Select(static edge => edge.GetProperty("direction").GetString() + ":" + edge.GetProperty("article_identity_sha256").GetString() + ":" + edge.GetProperty("ordinal").GetInt32()));
            if (value.GetProperty("truncated").ValueKind == JsonValueKind.False)
            {
                Assert.AreEqual(JsonValueKind.Null, value.GetProperty("continue_after").ValueKind);
                break;
            }

            after = value.GetProperty("continue_after").GetString();
            StringAssert.StartsWith(after, walked[^1][..walked[^1].IndexOf(':')] + ".", "the cursor names the direction of the last edge served.");
        }

        CollectionAssert.AreEqual(expected, walked.ToArray(), "pages walk the whole list once, outbound then inbound.");

        var foreign = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "relations", parameters = new { identifier, after = "not.a.cursor" } }));
        Assert.AreNotEqual(StatusCodes.Status200OK, foreign.Response.StatusCode);
        var badDirection = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "relations", parameters = new { identifier, direction = "sideways" } }));
        Assert.AreNotEqual(StatusCodes.Status200OK, badDirection.Response.StatusCode);
    }

    private static async Task<V3Envelope> RelationsAsync(V3CorpusMount mount, object parameters)
    {
        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "relations", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<V3Envelope> OtherAsync(V3CorpusMount mount, string rawTarget, string operation, object parameters)
    {
        var context = await PostAsync(mount, rawTarget, JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, operation);
        return envelope;
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-relations";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }
}
