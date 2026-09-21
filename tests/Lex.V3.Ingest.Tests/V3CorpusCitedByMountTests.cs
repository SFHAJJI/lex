using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>cited_by</c> driven through the real handler on a verified mount: the references written in any state the index
/// holds whose target is exactly a work's publisher legal-resource IRI or work IRI, read by their target from the table
/// <c>citation</c> reads by the citing article. The edges are held against expectations written by hand from the
/// grammar's rules and against a query of the table that shares nothing with the reader; and the two operations are
/// held against each other, because a fact with two derivations is the defect this design exists to remove.
/// </summary>
[TestClass]
public sealed class V3CorpusCitedByMountTests
{
    private static string RawTarget => V3RestRouteBinding.CitedBy.RawTarget;

    private const string Work = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Reference(string label, string? target) =>
        JsonSerializer.Serialize(new { kind = "reference", text = label, target, marker = (string?)null, note_body = (object?)null });

    /// <summary>
    /// The real act, one state of another work (n9) dated 400 days on that cites the act four ways and once in a footnote, and one
    /// later state of the act's own work that cites it once. What each reference is written from the grammar's rules, here.
    /// </summary>
    private static async Task<(MountedFixture Fixture, LuxembourgIndexBuilder.StateRow Other, LuxembourgIndexBuilder.StateRow Later)>
        BuildAsync()
    {
        var fixture = await MountedFixture.CreateAsync();
        var other = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "citing", workLeaf: "n9");
        var later = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 800), "self");
        var note = JsonSerializer.Serialize(new
        {
            kind = "note_reference", text = (string?)null, target = (string?)null, marker = "1",
            note_body = new object[] { new { kind = "reference", text = "in the note", target = "/eli/etat/leg/loi/1991/08/10/n3/jo", marker = (string?)null } },
        });
        var otherTokens = "[" + string.Join(",",
            "{\"kind\":\"text\",\"text\":\"avant \",\"target\":null,\"marker\":null,\"note_body\":null}",
            Reference("another act", "/eli/etat/leg/loi/2004/07/09/n3/jo"),
            Reference("the act, /jo form", "/eli/etat/leg/loi/1991/08/10/n3/jo"),
            Reference("the act, work form", Work),
            Reference("the act, https", "https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo"),
            note,
            Reference("the act, slash", "/eli/etat/leg/loi/1991/08/10/n3/jo/")) + "]";
        await fixture.SetArticleTokensAsync(other.ExpressionIri, "art_1er", "avant", otherTokens);
        await fixture.SetArticleTokensAsync(later.ExpressionIri, "art_1er", "avant",
            "[" + Reference("itself", Work + "/jo") + "]");
        return (fixture, other, later);
    }

    [TestMethod]
    public void TheCitedByRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/cited_by", RawTarget);
        Assert.AreEqual("cited_by", V3RestRouteBinding.CitedBy.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.CitedBy));
    }

    [TestMethod]
    public async Task TheEdgesThatNameAWorkExactlyAreTheOnesReturnedInDateOrderAndNothingIsAssessed()
    {
        var (fixture, other, later) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await CitedByAsync(mount, new { identifier = $"/lu-legilux/{fixture.WorkKey}" });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("cited_by", envelope.OperationId);
        Assert.AreEqual("relation_edge", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.AreEqual(JsonValueKind.False, body.GetProperty("relationship_type_assessed").ValueKind);
        Assert.AreEqual(JsonValueKind.False, body.GetProperty("current_legal_effect_assessed").ValueKind);
        // The answer says it is derived: the publisher wrote each reference in the citing text and asserted no inbound relation.
        Assert.AreEqual(JsonValueKind.True, body.GetProperty("derived").ValueKind);
        Assert.AreEqual(fixture.WorkKey, body.GetProperty("work_key").GetString());
        // The two exact strings the work is named by, in ordinal order.
        CollectionAssert.AreEqual(
            new[] { Work, Work + "/jo" },
            body.GetProperty("target_iris").EnumerateArray().Select(static value => value.GetString()).ToArray());

        // Four of the seven references in the citing state name the act exactly (the /jo form, the work form, and the /jo
        // form in a note) or are its own later state's; the https form and the trailing slash are not the act, and another act is not.
        var edges = body.GetProperty("edges").EnumerateArray().ToArray();
        Assert.HasCount(4, edges);
        Assert.AreEqual(4, body.GetProperty("edge_count").GetInt32());
        Assert.AreEqual(3, body.GetProperty("edge_counts").GetProperty("in_text").GetInt32());
        Assert.AreEqual(1, body.GetProperty("edge_counts").GetProperty("in_note").GetInt32());
        var expected = new (string State, string Work, string Date, int Ordinal, bool InNote, string Href, string Target, bool Self)[]
        {
            (other.StateSha256, other.WorkKey, other.ApplicabilityDate, 1, false, "/eli/etat/leg/loi/1991/08/10/n3/jo", Work + "/jo", false),
            (other.StateSha256, other.WorkKey, other.ApplicabilityDate, 2, false, Work, Work, false),
            (other.StateSha256, other.WorkKey, other.ApplicabilityDate, 4, true, "/eli/etat/leg/loi/1991/08/10/n3/jo", Work + "/jo", false),
            (later.StateSha256, later.WorkKey, later.ApplicabilityDate, 0, false, Work + "/jo", Work + "/jo", true),
        };
        for (var index = 0; index < expected.Length; index++)
        {
            var edge = edges[index];
            var (state, work, date, ordinal, inNote, href, target, self) = expected[index];
            var label = $"edge {index}";
            Assert.AreEqual(state, edge.GetProperty("citing_state_sha256").GetString(), label);
            Assert.AreEqual(work, edge.GetProperty("citing_work_key").GetString(), label);
            Assert.AreEqual(date, edge.GetProperty("citing_applicability_date").GetString(), label);
            Assert.AreEqual("fra", edge.GetProperty("citing_language").GetString(), label);
            Assert.AreEqual("art_1er", edge.GetProperty("article_publisher_id").GetString(), label);
            Assert.AreEqual(ordinal, edge.GetProperty("ordinal").GetInt32(), label);
            Assert.AreEqual(inNote, edge.GetProperty("in_note").GetBoolean(), label);
            Assert.AreEqual(href, edge.GetProperty("href").GetString(), label);
            Assert.AreEqual(target, edge.GetProperty("target_iri").GetString(), label);
            Assert.AreEqual(self, edge.GetProperty("is_self_reference").GetBoolean(), label);
        }

        // The citing state is named the way every other operation names a state.
        Assert.AreEqual(V3StateLocator(other), edges[0].GetProperty("citing_stable_coordinate").GetString());
        Assert.AreEqual(
            "/lu-legilux/" + other.WorkKey + "/" + other.ApplicabilityDate + "--" + other.StateSha256,
            edges[0].GetProperty("citing_permalink").GetString());

        // A table of the same rows read by SQL that shares nothing with the reader.
        Assert.AreEqual(4, CountEdgesNaming(fixture, Work, Work + "/jo"));
    }

    [TestMethod]
    public async Task CitedByAndCitationAnswerOneFactAndCannotDisagreeAboutAPairOfTexts()
    {
        var (fixture, other, later) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var inbound = (await CitedByAsync(mount, new { identifier = $"/lu-legilux/{fixture.WorkKey}" })).Result!.Value
            .GetProperty("edges").EnumerateArray()
            .Select(static edge => (
                State: edge.GetProperty("citing_state_sha256").GetString()!,
                Article: edge.GetProperty("article_identity_sha256").GetString()!,
                Ordinal: edge.GetProperty("ordinal").GetInt32()))
            .ToHashSet();

        // Every reference of every citing state that citation says is a held work named as the cited work, and no other.
        var forward = new HashSet<(string State, string Article, int Ordinal)>();
        foreach (var state in new[] { other, later })
        {
            var body = (await CiteAsync(mount, new
            {
                identifier = $"/lu-legilux/{state.WorkKey}", date = state.ApplicabilityDate, language = "fra",
            })).Result!.Value;
            foreach (var edge in body.GetProperty("edges").EnumerateArray())
            {
                if (edge.GetProperty("resolution").GetString() == "held_work" &&
                    edge.GetProperty("target_work_key").GetString() == fixture.WorkKey &&
                    edge.GetProperty("state_sha256").GetString() == state.StateSha256)
                {
                    forward.Add((state.StateSha256, edge.GetProperty("article_identity_sha256").GetString()!, edge.GetProperty("ordinal").GetInt32()));
                }
            }
        }

        Assert.IsNotEmpty(inbound);
        CollectionAssert.AreEquivalent(forward.ToArray(), inbound.ToArray(),
            "citation and cited_by disagree about which texts refer to the work");
    }

    [TestMethod]
    public async Task AWorkNoHeldTextCitesIsAnAnswerWithNoEdgesAndNotARefusal()
    {
        var (fixture, other, _) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await CitedByAsync(mount, new { identifier = $"/lu-legilux/{other.WorkKey}" })).Result!.Value;

        Assert.AreEqual(other.WorkKey, body.GetProperty("work_key").GetString());
        Assert.AreEqual(0, body.GetProperty("edge_count").GetInt32());
        Assert.AreEqual(0, body.GetProperty("edges").GetArrayLength());
        Assert.IsFalse(body.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("continue_after").ValueKind);
        // It still says what a count of zero does not mean.
        Assert.AreEqual("citing_texts_not_held", body.GetProperty("not_held")[2].GetProperty("item").GetString());
    }

    [TestMethod]
    public async Task PagesAreCutInTheStatedOrderAndTheNextRequestNeitherRepeatsNorSkipsAnEdge()
    {
        var (fixture, _, _) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var all = (await CitedByAsync(mount, new { identifier })).Result!.Value.GetProperty("edges").EnumerateArray()
            .Select(static edge => $"{edge.GetProperty("citing_state_sha256").GetString()}.{edge.GetProperty("article_identity_sha256").GetString()}.{edge.GetProperty("ordinal").GetInt32()}")
            .ToArray();

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var request = cursor is null ? (object)new { identifier, limit = 1 } : new { identifier, limit = 1, after = cursor };
            var body = (await CitedByAsync(mount, request)).Result!.Value;
            var page = body.GetProperty("edges").EnumerateArray().ToArray();
            Assert.HasCount(1, page);
            Assert.AreEqual(4, body.GetProperty("edge_count").GetInt32(), "the count is the query's and not the page's");
            var edge = page[0];
            var key = $"{edge.GetProperty("citing_state_sha256").GetString()}.{edge.GetProperty("article_identity_sha256").GetString()}.{edge.GetProperty("ordinal").GetInt32()}";
            seen.Add(key);
            var truncated = body.GetProperty("truncated").GetBoolean();
            Assert.AreEqual(truncated, body.GetProperty("continue_after").ValueKind == JsonValueKind.String);
            if (truncated)
            {
                Assert.AreEqual(key, body.GetProperty("continue_after").GetString());
            }

            cursor = truncated ? body.GetProperty("continue_after").GetString() : null;
            pages++;
        }
        while (cursor is not null);

        Assert.AreEqual(4, pages);
        CollectionAssert.AreEqual(all, seen.ToArray());

        var exact = (await CitedByAsync(mount, new { identifier, limit = 4 })).Result!.Value;
        Assert.IsFalse(exact.GetProperty("truncated").GetBoolean());
        var context = await PostAsync(mount, RawTarget,
            JsonSerializer.Serialize(new { operation_id = "cited_by", parameters = new { identifier, after = "not.a.cursor" } }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task EveryRefusalIsTheTimelinesThroughTheSharedBuilders()
    {
        var (fixture, _, _) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (label, identifier) in new[] { ("an identifier no work has", "/lu-legilux/no-such-work"), ("a European identifier", "32016R0679") })
        {
            var cited = await CitedByAsync(mount, new { identifier });
            var timeline = await TimelineAsync(mount, identifier);
            Assert.AreEqual(V3Verdicts.Refuse, cited.Verdict, label);
            Assert.AreEqual(timeline.Refusal!.Code, cited.Refusal!.Code, label);
            if (cited.Refusal.Code != "retrieval_mode_unavailable")
            {
                Assert.AreEqual(timeline.Refusal.HelpfulPayload.GetRawText(), cited.Refusal.HelpfulPayload.GetRawText(), label);
            }
            else
            {
                Assert.AreEqual("r4_cited_by", cited.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString(), label);
            }
        }
    }

    [TestMethod]
    public async Task TheAnswerHasExactlyTheseProperties_AndSaysWhatItIsAndWhatIsNotHeld()
    {
        var (fixture, _, _) = await BuildAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await CitedByAsync(mount, new { identifier = $"/lu-legilux/{fixture.WorkKey}", limit = 2 })).Result!.Value;

        CollectionAssert.AreEqual(
            new[]
            {
                "continue_after", "corpus_sha256", "current_legal_effect_assessed", "derived", "edge_count", "edge_counts", "edge_order",
                "edges", "index_sha256", "limit", "not_held", "page_is", "publisher", "relationship_type_assessed", "requested_after",
                "requested_identifier", "scope", "target_iris", "truncated", "work_key",
            }.Order(StringComparer.Ordinal).ToArray(),
            body.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[] { "in_note", "in_text" },
            body.GetProperty("edge_counts").EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "article_identity_sha256", "article_publisher_id", "citing_applicability_date", "citing_language", "citing_permalink",
                "citing_stable_coordinate", "citing_state_sha256", "citing_work_key", "href", "in_note", "is_self_reference", "label",
                "ordinal", "target_iri",
            },
            body.GetProperty("edges")[0].EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray());

        Assert.AreEqual(
            "the references, in the text of any state this index holds, whose target is exactly this work's publisher legal-resource IRI or its publisher work IRI: " +
            "the forward edges that citation serves, read by their target from the same table (lane R4), so the two operations cannot disagree about a pair of texts; " +
            "an edge records that a reference was written where it says, it is derived here (the publisher asserted the reference in the citing text, not an inbound " +
            "relation on this work), and this answer assesses neither what relationship the reference states nor whether it has any legal effect",
            body.GetProperty("scope").GetString());
        Assert.AreEqual(
            "by the citing state's publisher date, then its work key, language and expression, then the citing article's publisher id and identity, then the order the " +
            "references occur in it",
            body.GetProperty("edge_order").GetString());
        Assert.AreEqual(
            "the edges are in the stated order and a truncated page is the first limit of them from the cursor, not the most relevant",
            body.GetProperty("page_is").GetString());
        var notHeld = body.GetProperty("not_held").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(
            new[] { "relationship_type", "current_legal_effect", "citing_texts_not_held", "structured_relations" },
            notHeld.Select(static row => row.GetProperty("item").GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "no type of relationship is assessed or held for a reference: relationship_type_assessed is false",
                "no legal effect of a reference is assessed or held: current_legal_effect_assessed is false",
                "a text this index does not hold cannot be among the citing texts, so the count here is the references the held texts write and never a count of everything that cites this work",
                "the publisher's structured relation records (modifies, repeals, based on, transposes) are not held by this index, so these edges are only the references written in the text",
            },
            notHeld.Select(static row => row.GetProperty("reason").GetString()).ToArray());
    }

    [TestMethod]
    public async Task AMountWithoutTheLuxembourgIndexRefusesAsNoCorpusMounted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var europeOnly = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(europeOnly);

        var unmounted = await CitedByAsync(europeOnly, new { identifier = "/lu-legilux/any-work" });

        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\" \"}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"language\":\"fra\"}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"after\":\"\"}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"limit\":0}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"limit\":201}}")]
    [DataRow("{\"operation_id\":\"cited_by\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"in_note\":true}}")]
    public async Task UnusableCitedByRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, RawTarget, body);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
    }

    private static string V3StateLocator(LuxembourgIndexBuilder.StateRow state) =>
        "/lu-legilux/" + state.WorkKey + "/" + state.ApplicabilityDate;

    private static long CountEdgesNaming(MountedFixture fixture, params string[] targets)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM relations WHERE to_ref IN (" + string.Join(",", targets.Select(static (_, index) => "$t" + index)) + ")";
        for (var index = 0; index < targets.Length; index++)
        {
            command.Parameters.AddWithValue("$t" + index, targets[index]);
        }

        return (long)command.ExecuteScalar()!;
    }

    private static async Task<V3Envelope> CitedByAsync(V3CorpusMount mount, object parameters) =>
        await PostOkAsync(mount, RawTarget, "cited_by", parameters);

    private static async Task<V3Envelope> CiteAsync(V3CorpusMount mount, object parameters) =>
        await PostOkAsync(mount, V3RestRouteBinding.Citation.RawTarget, "citation", parameters);

    private static async Task<V3Envelope> TimelineAsync(V3CorpusMount mount, string identifier) =>
        await PostOkAsync(mount, V3RestRouteBinding.Timeline.RawTarget, "timeline", new { identifier });

    private static async Task<V3Envelope> PostOkAsync(V3CorpusMount mount, string rawTarget, string operation, object parameters)
    {
        var context = await PostAsync(mount, rawTarget, JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-cited-by";
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
