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
/// <c>citation</c> driven through the real handler on a verified mount that holds the real 1991 act: the
/// references the publisher wrote in a state's articles, read from the index's edge table (lane R4). It takes
/// <c>as_of</c>'s request and refuses as <c>as_of</c> refuses, which is held byte for byte; every edge is held against a
/// ground truth read from the table's rows by SQL that shares nothing with the reader; a target is a held work only
/// by exact equality with an IRI a state carries; and nothing is assessed.
/// </summary>
[TestClass]
public sealed class V3CorpusCitationMountTests
{
    private static string RawTarget => V3RestRouteBinding.Citation.RawTarget;

    private sealed record Ground(
        string PublisherId, string FromRef, int Ordinal, bool InNote, string? Label, string? Href, string Kind, string? ToRef);

    /// <summary>Every edge of the table, joined to its article for the publisher id, in the order the answer states.</summary>
    private static Ground[] ReadEdges(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT a.publisher_id,r.from_ref,r.ordinal,r.in_note,r.label,r.href,r.to_kind,r.to_ref " +
            "FROM relations r JOIN articles a ON a.article_identity_sha256=r.from_ref " +
            "ORDER BY a.publisher_id,r.from_ref,r.ordinal";
        using var reader = command.ExecuteReader();
        var rows = new List<Ground>();
        while (reader.Read())
        {
            rows.Add(new Ground(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows.ToArray();
    }

    private static string[] PublisherIds(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT publisher_id FROM articles ORDER BY publisher_id";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids.ToArray();
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public void TheCitationRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/citation", RawTarget);
        Assert.AreEqual("citation", V3RestRouteBinding.Citation.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Citation));
    }

    [TestMethod]
    public async Task TheRealActsEdgesAreTheTablesRowsInTheirStatedOrderAndNothingIsAssessed()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var envelope = await CiteAsync(mount, new { identifier, date = fixture.ApplicabilityDate, language = "fra" });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("citation", envelope.OperationId);
        Assert.AreEqual("relation_edge", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        // The two fixed statements the product spec requires, as JSON false and not as text.
        Assert.AreEqual(JsonValueKind.False, body.GetProperty("relationship_type_assessed").ValueKind);
        Assert.AreEqual(JsonValueKind.False, body.GetProperty("current_legal_effect_assessed").ValueKind);

        var ground = ReadEdges(fixture);
        // The 49 admitted articles of the real act carry 68 references (52 in running text and 16 in notes), counted
        // from the retained XML by code that shares nothing with the profile, the grammar or this operation.
        Assert.HasCount(68, ground);
        Assert.AreEqual(68, body.GetProperty("edge_count").GetInt32());
        var edges = body.GetProperty("edges").EnumerateArray().ToArray();
        Assert.HasCount(68, edges);
        Assert.IsFalse(body.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("continue_after").ValueKind);
        for (var index = 0; index < ground.Length; index++)
        {
            var row = ground[index];
            var edge = edges[index];
            var label = $"edge {index} ({row.PublisherId} #{row.Ordinal})";
            Assert.AreEqual("fra", edge.GetProperty("language").GetString(), label);
            Assert.AreEqual(fixture.StateSha256, edge.GetProperty("state_sha256").GetString(), label);
            Assert.AreEqual(row.FromRef, edge.GetProperty("article_identity_sha256").GetString(), label);
            Assert.AreEqual(row.PublisherId, edge.GetProperty("article_publisher_id").GetString(), label);
            Assert.AreEqual(row.Ordinal, edge.GetProperty("ordinal").GetInt32(), label);
            Assert.AreEqual(row.InNote, edge.GetProperty("in_note").GetBoolean(), label);
            Assert.AreEqual(row.Label, Text(edge.GetProperty("label")), label);
            Assert.AreEqual(row.Href, Text(edge.GetProperty("href")), label);
            Assert.AreEqual(row.Kind, edge.GetProperty("target_kind").GetString(), label);
            Assert.AreEqual(row.ToRef, Text(edge.GetProperty("target_iri")), label);
        }

        var state = body.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.StateSha256, state.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.StableCoordinate, state.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(68, state.GetProperty("edges_in_scope").GetInt32());
        Assert.AreEqual(identifier, body.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, body.GetProperty("requested_date").GetString());
        Assert.AreEqual("fra", body.GetProperty("requested_language").GetString());
        Assert.AreEqual("lu-legilux", body.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.WorkKey, body.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.CorpusSha256, body.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName)))),
            body.GetProperty("index_sha256").GetString());
    }

    [TestMethod]
    public async Task EveryEdgeOfTheRealActNamesAWorkThisIndexDoesNotHoldAndTheActsOwnSelfReferencesAreNotAmongThem()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await CiteAsync(mount, new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate }))
            .Result!.Value;

        var edges = body.GetProperty("edges").EnumerateArray().ToArray();
        // The mount holds one work, so every reference to another act, a code or an EU act is a work this index does not
        // hold, and the answer says exactly that: not_held is "this index has no work with exactly that IRI".
        Assert.IsTrue(edges.All(static edge => edge.GetProperty("resolution").GetString() == "not_held"));
        Assert.IsTrue(edges.All(static edge => edge.GetProperty("target_work_key").ValueKind == JsonValueKind.Null));
        // 52 references in running text (48 to Legilux ELIs and 4 to data.europa.eu) and 16 in notes (all Legilux ELIs),
        // counted from the retained XML by code that shares nothing with the profile or the grammar.
        Assert.AreEqual(64, edges.Count(static edge => edge.GetProperty("target_kind").GetString() == "legilux_eli"));
        Assert.AreEqual(4, edges.Count(static edge => edge.GetProperty("target_kind").GetString() == "other_uri"));
        Assert.AreEqual(0, edges.Count(static edge => edge.GetProperty("target_kind").GetString() == "unparsed"),
            "the real 1991 act writes no reference the grammar cannot read (the 1984 act writes one, ???)");
        // The act cites itself six times in its own text, in the form the publisher writes (/eli/.../n3/jo), and all six
        // are in art_43, which the reviewed profile does not admit: they are not held here, so they are not edges here.
        Assert.IsFalse(edges.Any(static edge => Text(edge.GetProperty("href")) == "/eli/etat/leg/loi/1991/08/10/n3/jo"));
    }

    [TestMethod]
    public async Task AReferenceTheGrammarCannotReadIsAnEdgeWithNoTargetAndAWorkIsHeldOnlyByExactEquality()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        const string work = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";
        // A hand-authored stream: what each reference must come out as is written here from the grammar's rules and not
        // from the code that applies them.
        var tokens = JsonSerializer.Serialize(new object[]
        {
            new { kind = "text", text = "avant ", target = (string?)null, marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "placeholder", target = "???", marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "the act, /jo form", target = "/eli/etat/leg/loi/1991/08/10/n3/jo", marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "the act, work form", target = work, marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "the act, https", target = "https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo", marker = (string?)null, note_body = (object?)null },
            new { kind = "reference", text = "the act, slash", target = "/eli/etat/leg/loi/1991/08/10/n3/jo/", marker = (string?)null, note_body = (object?)null },
            new
            {
                kind = "note_reference", text = (string?)null, target = (string?)null, marker = "1",
                note_body = new object[]
                {
                    new { kind = "reference", text = "in a note", target = "http://example.org/x", marker = (string?)null },
                },
            },
            new { kind = "reference", text = "no value", target = (string?)null, marker = (string?)null, note_body = (object?)null },
        });
        await fixture.SetArticleTokensAsync(fixture.ExpressionIri, "art_1er", "avant", tokens);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await CiteAsync(mount, new
        {
            identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, anchor = "art_1er",
        })).Result!.Value;

        var edges = body.GetProperty("edges").EnumerateArray().ToArray();
        var expected = new (string? Href, bool InNote, string Kind, string? Target, string Resolution, string? WorkKey)[]
        {
            ("???", false, "unparsed", null, "unparsed", null),
            ("/eli/etat/leg/loi/1991/08/10/n3/jo", false, "legilux_eli", work + "/jo", "held_work", fixture.WorkKey),
            (work, false, "legilux_eli", null, "PLACEHOLDER", null),
            ("https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo", false, "other_uri",
                "https://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo", "not_held", null),
            ("/eli/etat/leg/loi/1991/08/10/n3/jo/", false, "legilux_eli", work + "/jo/", "not_held", null),
            ("http://example.org/x", true, "other_uri", "http://example.org/x", "not_held", null),
            (null, false, "unparsed", null, "unparsed", null),
        };
        Assert.HasCount(expected.Length, edges);
        for (var index = 0; index < expected.Length; index++)
        {
            var edge = edges[index];
            var (href, inNote, kind, target, resolution, workKey) = expected[index];
            var label = $"edge {index} ({href ?? "no value"})";
            Assert.AreEqual(index, edge.GetProperty("ordinal").GetInt32(), label);
            Assert.AreEqual(href, Text(edge.GetProperty("href")), label);
            Assert.AreEqual(inNote, edge.GetProperty("in_note").GetBoolean(), label);
            Assert.AreEqual(kind, edge.GetProperty("target_kind").GetString(), label);
            if (href == work)
            {
                // The work IRI itself, exactly: held by the second exact string a state carries, with the key.
                Assert.AreEqual(work, Text(edge.GetProperty("target_iri")), label);
                Assert.AreEqual("held_work", edge.GetProperty("resolution").GetString(), label);
                Assert.AreEqual(fixture.WorkKey, Text(edge.GetProperty("target_work_key")), label);
                continue;
            }

            Assert.AreEqual(target, Text(edge.GetProperty("target_iri")), label);
            Assert.AreEqual(resolution, edge.GetProperty("resolution").GetString(), label);
            Assert.AreEqual(workKey, Text(edge.GetProperty("target_work_key")), label);
        }

        // The placeholder is never a work and nothing in the answer names one for it.
        var placeholder = edges[0];
        Assert.AreEqual("???", Text(placeholder.GetProperty("href")));
        Assert.AreEqual(JsonValueKind.Null, placeholder.GetProperty("target_iri").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, placeholder.GetProperty("target_work_key").ValueKind);
    }

    [TestMethod]
    public async Task AnAnchorNarrowsToOneArticleAndAnAnchorTheStateDoesNotHoldRefusesWithItsNeighbours()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var ground = ReadEdges(fixture);
        var withEdges = ground.First().PublisherId;
        var without = PublisherIds(fixture).First(id => ground.All(row => row.PublisherId != id));

        var narrowed = (await CiteAsync(mount, new { identifier, date = fixture.ApplicabilityDate, anchor = withEdges })).Result!.Value;
        var expected = ground.Where(row => row.PublisherId == withEdges).ToArray();
        var edges = narrowed.GetProperty("edges").EnumerateArray().ToArray();
        Assert.HasCount(expected.Length, edges);
        Assert.IsTrue(edges.All(edge => edge.GetProperty("article_publisher_id").GetString() == withEdges));
        CollectionAssert.AreEqual(
            expected.Select(static row => row.Ordinal).ToArray(), edges.Select(static edge => edge.GetProperty("ordinal").GetInt32()).ToArray());
        Assert.AreEqual(expected.Length, narrowed.GetProperty("edge_count").GetInt32());
        Assert.AreEqual(withEdges, narrowed.GetProperty("requested_anchor").GetString());

        // An article the state holds and that wrote no reference is an empty answer, never a refusal.
        var empty = (await CiteAsync(mount, new { identifier, date = fixture.ApplicabilityDate, anchor = without })).Result!.Value;
        Assert.AreEqual(0, empty.GetProperty("edge_count").GetInt32());
        Assert.AreEqual(0, empty.GetProperty("edges").GetArrayLength());
        Assert.AreEqual(0, empty.GetProperty("absent_in_states").GetArrayLength());

        // An article the state does not hold is the closed refusal, with the neighbourhood it always names.
        var refused = await CiteAsync(mount, new { identifier, date = fixture.ApplicabilityDate, anchor = "art_that_is_not_here" });
        Assert.AreEqual(V3Verdicts.Refuse, refused.Verdict);
        Assert.AreEqual("anchor_not_in_version", refused.Refusal!.Code);
        var payload = refused.Refusal.HelpfulPayload;
        Assert.AreEqual("art_that_is_not_here", payload.GetProperty("requested_anchor").GetString());
        Assert.IsTrue(payload.GetProperty("do_not_fall_back_to_full_text_search").GetBoolean());
        var nearest = payload.GetProperty("nearest_anchors").EnumerateArray().Select(static value => value.GetString()!).ToArray();
        Assert.IsNotEmpty(nearest);
        Assert.IsTrue(nearest.All(id => PublisherIds(fixture).Contains(id)));
    }

    [TestMethod]
    public async Task PagesAreCutInTheStatedOrderAndTheNextRequestNeitherRepeatsNorSkipsAnEdge()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var ground = ReadEdges(fixture);

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var request = cursor is null
                ? (object)new { identifier, date = fixture.ApplicabilityDate, limit = 10 }
                : new { identifier, date = fixture.ApplicabilityDate, limit = 10, after = cursor };
            var body = (await CiteAsync(mount, request)).Result!.Value;
            var page = body.GetProperty("edges").EnumerateArray().ToArray();
            Assert.IsLessThanOrEqualTo(10, page.Length);
            seen.AddRange(page.Select(static edge =>
                $"{edge.GetProperty("article_identity_sha256").GetString()}.{edge.GetProperty("ordinal").GetInt32()}"));
            Assert.AreEqual(10, body.GetProperty("limit").GetInt32());
            var truncated = body.GetProperty("truncated").GetBoolean();
            var next = body.GetProperty("continue_after");
            Assert.AreEqual(truncated, next.ValueKind == JsonValueKind.String);
            if (truncated)
            {
                Assert.AreEqual(
                    $"{fixture.StateSha256}.{page[^1].GetProperty("article_identity_sha256").GetString()}.{page[^1].GetProperty("ordinal").GetInt32()}",
                    next.GetString());
            }

            cursor = truncated ? next.GetString() : null;
            pages++;
        }
        while (cursor is not null);

        Assert.AreEqual(7, pages, "68 edges in pages of ten");
        CollectionAssert.AreEqual(ground.Select(static row => $"{row.FromRef}.{row.Ordinal}").ToArray(), seen.ToArray());

        // A limit equal to the number of edges is not a truncated page; one below it is.
        var exact = (await CiteAsync(mount, new { identifier, date = fixture.ApplicabilityDate, limit = 68 })).Result!.Value;
        Assert.IsFalse(exact.GetProperty("truncated").GetBoolean());
        var short1 = (await CiteAsync(mount, new { identifier, date = fixture.ApplicabilityDate, limit = 67 })).Result!.Value;
        Assert.IsTrue(short1.GetProperty("truncated").GetBoolean());

        // A cursor that names no edge of this query is a request below the envelope, never a silent restart.
        var context = await PostAsync(mount, RawTarget,
            JsonSerializer.Serialize(new { operation_id = "citation", parameters = new { identifier, date = fixture.ApplicabilityDate, after = "not.a.cursor" } }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task TheLanguageAndTheDateSelectAndRefuseExactlyAsAsOfDoes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        var twinDate = Shift(fixture.ApplicabilityDate, 800);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";

        // No language asked: every served language's state on the date, as as_of serves them, each state's edges together.
        var both = (await CiteAsync(mount, new { identifier = work, date = fixture.ApplicabilityDate })).Result!.Value;
        var asOfBoth = (await AsOfAsync(mount, work, fixture.ApplicabilityDate, null)).Result!.Value;
        CollectionAssert.AreEqual(
            asOfBoth.GetProperty("states").EnumerateArray().Select(static s => s.GetProperty("state_sha256").GetString()).ToArray(),
            both.GetProperty("states").EnumerateArray().Select(static s => s.GetProperty("state_sha256").GetString()).ToArray());
        var states = both.GetProperty("states").EnumerateArray().ToArray();
        Assert.AreEqual(2, states.Length);
        Assert.IsTrue(states.All(static state => state.GetProperty("edges_in_scope").GetInt32() == 68));
        Assert.AreEqual(136, both.GetProperty("edge_count").GetInt32());
        var byState = both.GetProperty("edges").EnumerateArray().Select(static edge => edge.GetProperty("state_sha256").GetString()).ToArray();
        CollectionAssert.AreEqual(
            states.SelectMany(static state => Enumerable.Repeat(state.GetProperty("state_sha256").GetString(), 68)).ToArray(),
            byState, "the edges of one state, then the next, in the order as_of lists the states");
        var french = (await CiteAsync(mount, new { identifier = work, date = fixture.ApplicabilityDate, language = "fra" })).Result!.Value;
        Assert.AreEqual(1, french.GetProperty("states").GetArrayLength());
        Assert.AreEqual(68, french.GetProperty("edge_count").GetInt32());

        // The date selects the state in force on it, not the one dated on it.
        var between = (await CiteAsync(mount, new { identifier = work, date = Shift(fixture.ApplicabilityDate, 100), language = "fra" })).Result!.Value;
        Assert.AreEqual(fixture.StateSha256, between.GetProperty("states")[0].GetProperty("state_sha256").GetString());

        // Every refusal is as_of's, byte for byte, through the shared builders.
        foreach (var (label, identifier, date, language) in new (string, string, string, string?)[]
                 {
                     ("two states on the date", work, twinDate, "fra"),
                     ("a date before the history", work, "1900-01-01", "fra"),
                     ("a language not held", work, fixture.ApplicabilityDate, "eng"),
                     ("an identifier no work has", "/lu-legilux/no-such-work", fixture.ApplicabilityDate, "fra"),
                     ("a European identifier", "32016R0679", fixture.ApplicabilityDate, "fra"),
                 })
        {
            var parameters = language is null
                ? (object)new { identifier, date }
                : new { identifier, date, language };
            var citation = await CiteAsync(mount, parameters);
            var asOf = await AsOfAsync(mount, identifier, date, language);
            Assert.AreEqual(V3Verdicts.Refuse, citation.Verdict, label);
            Assert.AreEqual(asOf.Refusal!.Code, citation.Refusal!.Code, label);
            // The one thing that may differ is the mode a European identifier is refused for: it names the lane.
            if (citation.Refusal.Code != "retrieval_mode_unavailable")
            {
                Assert.AreEqual(asOf.Refusal.HelpfulPayload.GetRawText(), citation.Refusal.HelpfulPayload.GetRawText(), label);
            }
            else
            {
                Assert.AreEqual("r4_citation", citation.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString(), label);
            }
        }
    }

    [TestMethod]
    public async Task TheAnswerHasExactlyTheseProperties_AndSaysWhatItIsAndWhatIsNotHeld()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await CiteAsync(mount, new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, limit = 5 }))
            .Result!.Value;

        // A property of any name, at any depth the answer has, fails here until it is declared.
        CollectionAssert.AreEqual(
            new[]
            {
                "absent_in_states", "available_languages", "corpus_sha256", "current_legal_effect_assessed", "edge_count", "edge_order",
                "edges", "index_sha256", "limit", "not_held", "page_is", "publisher", "relationship_type_assessed", "requested_after",
                "requested_anchor", "requested_date", "requested_identifier", "requested_language", "scope", "states", "target_note",
                "truncated", "continue_after", "work_key",
            }.Order(StringComparer.Ordinal).ToArray(),
            body.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[] { "applicability_date", "edges_in_scope", "language", "permalink", "stable_coordinate", "state_sha256" },
            body.GetProperty("states")[0].EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "article_identity_sha256", "article_publisher_id", "href", "in_note", "label", "language", "ordinal", "resolution",
                "state_sha256", "target_iri", "target_kind", "target_work_key",
            },
            body.GetProperty("edges")[0].EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray());

        Assert.AreEqual(
            "the references the publisher wrote in the text of the selected state's articles, and in the footnote bodies its notes carry (in_note), " +
            "read from the index's edge table of forward edges (lane R4); an edge records that a reference was written where it says, " +
            "and this answer assesses neither what relationship the reference states nor whether it has any legal effect",
            body.GetProperty("scope").GetString());
        Assert.AreEqual(
            "href is the value the publisher wrote, verbatim; target_kind and target_iri come from a fixed reading of it that resolves nothing: " +
            "legilux_eli is a value that begins /eli/ or http://data.legilux.public.lu/eli/ and its target_iri is the absolute form (the one change is putting the host " +
            "in front of a relative value; no trailing slash is trimmed and no scheme or case is changed), other_uri is any other absolute http or https value and its " +
            "target_iri is the value as written, unparsed is anything else and has no target_iri; resolution is held_work only when target_iri is exactly the publisher " +
            "legal-resource IRI (the form the publisher writes in running text, ending /jo) or the publisher work IRI of a work this index holds, and then target_work_key " +
            "names it, not_held when it is not (that says this index holds no work with exactly that IRI, not that no such work exists or that it is not held under another " +
            "spelling), and unparsed when there is no target_iri; no edge is dropped or upgraded, " +
            "and a note's reference is the publisher's own reference to the act named there, not a statement of how that act relates to the article",
            body.GetProperty("target_note").GetString());
        Assert.AreEqual(
            "by the citing article's publisher id, then its identity, then the order the references occur in it (a footnote body's at its note reference), " +
            "for each language in turn; the index holds no position of an article in its document, so that order is not one",
            body.GetProperty("edge_order").GetString());
        Assert.AreEqual(
            "the edges are in the stated order and a truncated page is the first limit of them from the cursor, not the most relevant",
            body.GetProperty("page_is").GetString());
        var notHeld = body.GetProperty("not_held").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(
            new[] { "relationship_type", "current_legal_effect", "structured_relations", "references_to_this_work" },
            notHeld.Select(static row => row.GetProperty("item").GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "no type of relationship is assessed or held for a reference: relationship_type_assessed is false",
                "no legal effect of a reference is assessed or held: current_legal_effect_assessed is false",
                "the publisher's structured relation records (modifies, repeals, based on, transposes) are not held by this index, so these edges are only the references written in the text",
                "which texts refer to this work is not answered here: it is the inverse of these edges and its own operation",
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

        var unmounted = await CiteAsync(europeOnly, new { identifier = "/lu-legilux/any-work", date = "2024-01-01" });

        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"date\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-02-30\"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"language\":\" \"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"anchor\":\" \"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"after\":\"\"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"limit\":0}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"limit\":201}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"limit\":\"5\"}}")]
    // Undeclared: rejected, never ignored (a sort, a relationship filter or a signature are other questions).
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"relationship\":\"amends\"}}")]
    [DataRow("{\"operation_id\":\"citation\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"in_note\":true}}")]
    public async Task UnusableCitationRequestsAreBelowEnvelopeSchemaRejections(string body)
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

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static async Task<V3Envelope> CiteAsync(V3CorpusMount mount, object parameters)
    {
        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "citation", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<V3Envelope> AsOfAsync(V3CorpusMount mount, string identifier, string date, string? language)
    {
        var parameters = new Dictionary<string, object> { ["identifier"] = identifier, ["date"] = date };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var context = await PostAsync(mount, "/api/v3/as_of", JsonSerializer.Serialize(new { operation_id = "as_of", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-citation";
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
