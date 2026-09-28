using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>evidence_bundle</c> driven through the real handler on a verified mount: what a quote needs. It
/// takes <c>as_of</c>'s request, selects by <c>as_of</c>'s rule and refuses as <c>as_of</c> refuses;
/// it enforces the rights rule per selected state before any text is read, so a member the index
/// records under any rights disposition but the admitting one withholds the whole bundle and no text
/// of it reaches the wire; every article's text is the publisher's searchable wording with the digest
/// of exactly those bytes; the notes travel beside the text and outside the wording digest; and the
/// state's sources and body digests are the ones <c>provenance</c> names for the same request.
/// </summary>
[TestClass]
public sealed class V3CorpusEvidenceBundleMountTests
{
    private static string RawTarget => V3RestRouteBinding.EvidenceBundle.RawTarget;

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Token(string kind, string text, string? target = null, string? marker = null, object? noteBody = null) =>
        JsonSerializer.Serialize(new { kind, text, target, marker, note_body = noteBody });

    /// <summary>
    /// The notes of every article as the rows hold them, read here from the token stream by code that shares
    /// nothing with the mount: one (marker, body text) per note_reference token, the body text being its
    /// text and reference tokens' texts in order.
    /// </summary>
    private static Dictionary<string, (string? Marker, string Text)[]> ReadNotes(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT article_identity_sha256, tokens_json FROM articles";
        using var reader = command.ExecuteReader();
        var notes = new Dictionary<string, (string?, string)[]>(StringComparer.Ordinal);
        while (reader.Read())
        {
            using var tokens = JsonDocument.Parse(reader.GetString(1));
            notes[reader.GetString(0)] = tokens.RootElement.EnumerateArray()
                .Where(static token => token.GetProperty("kind").GetString() == "note_reference")
                .Select(static token => (
                    token.GetProperty("marker").ValueKind == JsonValueKind.String ? token.GetProperty("marker").GetString() : null,
                    string.Concat(token.GetProperty("note_body").EnumerateArray()
                        .Where(static nested => nested.GetProperty("kind").GetString() is "text" or "reference")
                        .Select(static nested => nested.GetProperty("text").GetString()))))
                .ToArray();
        }

        return notes;
    }

    private static VerifiedLexCorpus6ManifestSet ReadCorpus(MountedFixture fixture) =>
        VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(
            File.ReadAllBytes(Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName)));

    [TestMethod]
    public void TheEvidenceBundleRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/evidence_bundle", RawTarget);
        Assert.AreEqual("evidence_bundle", V3RestRouteBinding.EvidenceBundle.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.EvidenceBundle));
    }

    [TestMethod]
    public async Task TheBundleCarriesEveryArticlesTextAndDigestsOfTheStateAsOfAndProvenanceServe()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var parameters = new { identifier, date = fixture.ApplicabilityDate, language = "fra" };
        var ground = fixture.ArticlesOfOwnState();
        var groundNotes = ReadNotes(fixture);
        Assert.IsGreaterThan(1, ground.Count, "The fixture's state must hold several articles for the list to mean something.");

        var envelope = await BundleAsync(mount, parameters);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("evidence_bundle", envelope.OperationId);
        Assert.AreEqual("evidence_bundle", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.AreEqual(identifier, body.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, body.GetProperty("requested_date").GetString());
        Assert.AreEqual("fra", body.GetProperty("requested_language").GetString());
        Assert.AreEqual("lu-legilux", body.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.WorkKey, body.GetProperty("work_key").GetString());
        Assert.AreEqual("agreed_same_run_cc_by", body.GetProperty("rights_disposition").GetString());
        StringAssert.Contains(body.GetProperty("rights_rule").GetString(), "before any text is read");
        CollectionAssert.AreEqual(
            new[] { "publisher_signature", "observation_time", "export_formats", "notes_in_wording_digest" },
            body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());

        var state = body.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(fixture.StateSha256, state.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.StableCoordinate, state.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual("fra", state.GetProperty("language").GetString());
        Assert.AreEqual(fixture.ExpressionIri, state.GetProperty("expression_iri").GetString());
        var officialSource = state.GetProperty("publisher_legal_resource_iri").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(officialSource));

        // Every article, its text the publisher's searchable wording and its digest the digest of exactly those bytes.
        var articles = state.GetProperty("articles").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(
            ground.Select(static article => article.Identity).ToArray(),
            articles.Select(static article => article.GetProperty("article_identity_sha256").GetString()).ToArray());
        foreach (var article in articles)
        {
            var identity = article.GetProperty("article_identity_sha256").GetString()!;
            var expected = ground.Single(candidate => candidate.Identity == identity);
            var text = article.GetProperty("text").GetString()!;
            Assert.AreEqual(expected.Text, text, identity);
            Assert.AreEqual(expected.PublisherId, article.GetProperty("publisher_id").GetString(), identity);
            Assert.AreEqual(Sha256Hex(text), article.GetProperty("text_sha256").GetString(), identity);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(text), article.GetProperty("text_byte_length").GetInt32(), identity);
            Assert.AreEqual(64, article.GetProperty("wording_sha256").GetString()!.Length, identity);
            Assert.AreEqual(fixture.Permalink + "#" + expected.PublisherId, article.GetProperty("article_permalink").GetString(), identity);
            Assert.AreEqual(officialSource, article.GetProperty("official_source").GetString(), identity);
            CollectionAssert.AreEqual(
                groundNotes[identity].Select(static note => note.Marker + "|" + note.Text).ToArray(),
                article.GetProperty("notes").EnumerateArray().Select(static note => note.GetProperty("marker").GetString() + "|" + note.GetProperty("text").GetString()).ToArray(),
                identity + ": the notes are the note_reference tokens' markers and bodies, in order.");
        }

        Assert.IsTrue(
            articles.Any(static article => article.GetProperty("notes").GetArrayLength() > 0),
            "The real act carries notes; a state with none would not exercise the rendering.");

        // The same state as_of serves for the same request, with the same article identities.
        var asOf = await OtherAsync(mount, "/api/v3/as_of", "as_of", parameters);
        var asOfState = asOf.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(asOfState.GetProperty("state_sha256").GetString(), state.GetProperty("state_sha256").GetString());
        CollectionAssert.AreEquivalent(
            asOfState.GetProperty("article_identities").EnumerateArray().Select(static value => value.GetString()).ToArray(),
            articles.Select(static article => article.GetProperty("article_identity_sha256").GetString()).ToArray());

        // The sources provenance names for the same request, and the body digests are the corpus manifest's.
        var provenance = await OtherAsync(mount, "/api/v3/provenance", "provenance", parameters);
        var provenanceState = provenance.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(
            JsonSerializer.Serialize(provenanceState.GetProperty("sources")),
            JsonSerializer.Serialize(state.GetProperty("sources")));
        Assert.AreEqual(
            JsonSerializer.Serialize(provenance.Result.Value.GetProperty("verified_by")),
            JsonSerializer.Serialize(body.GetProperty("verified_by")));
        var corpus = ReadCorpus(fixture);
        var expectedBodies = state.GetProperty("sources").EnumerateArray()
            .Select(source => corpus.Set.Members.Single(member => member.ObjectRefSha256 == source.GetProperty("object_ref_sha256").GetString()).BodySha256)
            .Where(static digest => digest is not null)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.IsNotEmpty(expectedBodies, "The fixture's members retain a body; a bundle with no body digest would name nothing to verify against.");
        CollectionAssert.AreEqual(
            expectedBodies,
            state.GetProperty("body_sha256s").EnumerateArray().Select(static value => value.GetString()).ToArray());
        foreach (var source in state.GetProperty("sources").EnumerateArray())
        {
            Assert.AreEqual("acquired", source.GetProperty("outcome").GetString());
            Assert.AreEqual("agreed_same_run_cc_by", source.GetProperty("rights_disposition").GetString());
        }
    }

    [TestMethod]
    public async Task NotesTravelBesideTheTextAndOutsideTheWordingDigest()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var (_, publisherId, _) = fixture.ArticlesOfOwnState()[0];
        const string searchable = "Le loyer est fixé par écrit.";
        var plain = "[" + string.Join(",", Token("text", "Le loyer est "), Token("reference", "fixé", "/eli/etat/leg/loi/2006/09/21/n1/jo"), Token("text", " par écrit.")) + "]";
        var noted = "[" + string.Join(",",
            Token("text", "Le loyer est "),
            Token("reference", "fixé", "/eli/etat/leg/loi/2006/09/21/n1/jo"),
            Token("note_reference", "1", marker: "(1)", noteBody: new object[]
            {
                new { kind = "text", text = "Modifié par la ", target = (string?)null, marker = (string?)null, note_body = (object?)null },
                new { kind = "reference", text = "loi du 21 septembre 2006", target = "/eli/etat/leg/loi/2006/09/21/n1/jo", marker = (string?)null, note_body = (object?)null },
                new { kind = "text", text = ".", target = (string?)null, marker = (string?)null, note_body = (object?)null },
            }),
            Token("text", " par écrit.")) + "]";
        var parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };

        await fixture.SetArticleTokensAsync(fixture.ExpressionIri, publisherId, searchable, plain);
        JsonElement before;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            before = ArticleOf(await BundleAsync(mount, parameters), publisherId);
            Assert.AreEqual(0, before.GetProperty("notes").GetArrayLength());
        }

        await fixture.SetArticleTokensAsync(fixture.ExpressionIri, publisherId, searchable, noted);
        using var noteMount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(noteMount);
        var after = ArticleOf(await BundleAsync(noteMount, parameters), publisherId);

        Assert.AreEqual(searchable, after.GetProperty("text").GetString(), "the note's body is not in the text; the text is the searchable wording.");
        Assert.AreEqual(before.GetProperty("text_sha256").GetString(), after.GetProperty("text_sha256").GetString());
        Assert.AreEqual(before.GetProperty("wording_sha256").GetString(), after.GetProperty("wording_sha256").GetString(), "a note added to the same wording leaves the wording digest as it was.");
        var note = after.GetProperty("notes").EnumerateArray().Single();
        Assert.AreEqual("(1)", note.GetProperty("marker").GetString());
        Assert.AreEqual("Modifié par la loi du 21 septembre 2006.", note.GetProperty("text").GetString());
    }

    [TestMethod]
    public async Task AMemberWhoseRightsChannelsDidNotAgreeWithholdsTheWholeBundleBeforeAnyTextIsRead()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var texts = fixture.ArticlesOfOwnState().Select(static article => article.Text).Where(static text => text.Length > 0).ToArray();
        Assert.IsNotEmpty(texts);
        await fixture.SetMemberRightsDispositionAsync("non_admitting_licence_scl");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };

        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "evidence_bundle", parameters }));

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var wire = Encoding.UTF8.GetString(ResponseBytes(context));
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("text_withheld", envelope.Refusal!.Code);
        var payload = envelope.Refusal.HelpfulPayload;
        var asOf = await OtherAsync(mount, "/api/v3/as_of", "as_of", parameters);
        var asOfState = asOf.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(asOfState.GetProperty("publisher_legal_resource_iri").GetString(), payload.GetProperty("official_identity").GetString());
        Assert.AreEqual(asOfState.GetProperty("publisher_work_iri").GetString(), payload.GetProperty("official_link").GetString());
        Assert.AreEqual(fixture.Permalink, payload.GetProperty("permalink").GetString());
        Assert.AreEqual("non_admitting_licence_scl", payload.GetProperty("rights_disposition").GetString());
        var corpus = ReadCorpus(fixture);
        Assert.IsTrue(
            corpus.Set.Members.Any(member => member.BodySha256 == payload.GetProperty("content_sha256").GetString()),
            "content_sha256 is the retained body digest of the withholding member.");
        foreach (var text in texts)
        {
            Assert.IsFalse(wire.Contains(text, StringComparison.Ordinal), "no article text reaches the wire under a withheld bundle.");
        }
    }

    [TestMethod]
    public async Task AStateWhoseArticlesHoldNoTextRefusesTextNotAvailable()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var articles = fixture.ArticlesOfOwnState();
        foreach (var (_, publisherId, _) in articles)
        {
            await fixture.SetArticleTokensAsync(fixture.ExpressionIri, publisherId, string.Empty, "[]");
        }

        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };

        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "evidence_bundle", parameters }));

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual("text_not_available", envelope.Refusal!.Code);
        var payload = envelope.Refusal.HelpfulPayload;
        var asOf = await OtherAsync(mount, "/api/v3/as_of", "as_of", parameters);
        var asOfState = asOf.Result!.Value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(asOfState.GetProperty("publisher_legal_resource_iri").GetString(), payload.GetProperty("official_identity").GetString());
        Assert.AreEqual(asOfState.GetProperty("publisher_work_iri").GetString(), payload.GetProperty("official_source").GetString());
        Assert.AreEqual(articles.Count, payload.GetProperty("articles_held").GetInt32());
        var corpus = ReadCorpus(fixture);
        var receipts = corpus.Set.Members.Select(static member => member.BodyReceiptSha256).Where(static value => value is not null).ToArray();
        Assert.IsNotEmpty(receipts);
        CollectionAssert.Contains(receipts, payload.GetProperty("retained_transport_evidence").GetString());
    }

    [TestMethod]
    public async Task ItRefusesExactlyAsAsOfRefuses()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twinDate = Shift(fixture.ApplicabilityDate, 800);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";

        foreach (var (parameters, expected) in new (object, string)[]
                 {
                     (new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version"),
                     (new { identifier = work, date = "1900-01-01", language = "fra" }, "no_version_for_date"),
                     (new { identifier = work, date = fixture.ApplicabilityDate, language = "eng" }, "language_not_available"),
                     (new { identifier = "/lu-legilux/no-such-work", date = fixture.ApplicabilityDate, language = "fra" }, "identifier_unknown"),
                 })
        {
            var bundle = await BundleAsync(mount, parameters);
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", parameters);
            Assert.AreEqual(expected, bundle.Refusal?.Code, JsonSerializer.Serialize(parameters));
            Assert.AreEqual(asOf.Refusal!.Code, bundle.Refusal!.Code, JsonSerializer.Serialize(parameters));
            Assert.AreEqual(
                JsonSerializer.Serialize(asOf.Refusal.HelpfulPayload),
                JsonSerializer.Serialize(bundle.Refusal.HelpfulPayload),
                expected + ": the bundle refuses with as_of's payload.");
        }
    }

    [TestMethod]
    public async Task ARequestWithoutADateIsARequestSchemaFailure()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new
        {
            operation_id = "evidence_bundle", parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}" },
        }));

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
    }

    private static JsonElement ArticleOf(V3Envelope envelope, string publisherId)
    {
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        return envelope.Result!.Value.GetProperty("states").EnumerateArray().Single()
            .GetProperty("articles").EnumerateArray()
            .Single(article => article.GetProperty("publisher_id").GetString() == publisherId);
    }

    private static Task<V3Envelope> BundleAsync(V3CorpusMount mount, object parameters) =>
        EnvelopeAsync(mount, RawTarget, "evidence_bundle", parameters);

    private static async Task<V3Envelope> OtherAsync(V3CorpusMount mount, string rawTarget, string operation, object parameters)
    {
        var envelope = await EnvelopeAsync(mount, rawTarget, operation, parameters);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, operation + ": " + envelope.Refusal?.Code);
        return envelope;
    }

    private static async Task<V3Envelope> EnvelopeAsync(V3CorpusMount mount, string rawTarget, string operation, object parameters)
    {
        var context = await PostAsync(mount, rawTarget, JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-evidence-bundle";
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
