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
/// <c>articles_not_admitted</c>, the count of a state's document's articles that the corpus recorded and the state does
/// not hold, on every answer that describes a state: <c>as_of</c>, <c>timeline</c>, <c>dossier</c>'s rows, both states
/// of <c>diff</c> and the state a hash-pinned <c>resolve</c> serves. Each count is held against the corpus's own record
/// of the document, read from the member's row by SQLite's JSON functions and sharing nothing with the reader; two
/// states of two documents each carry their own; nought is a number and never an absence; and the one sentence they
/// carry is pinned in its words.
/// </summary>
[TestClass]
public sealed class V3CorpusArticlesNotAdmittedTests
{
    private const string AknDomain = "luxembourg_akn_legal_content";

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The document's legal-content outcomes under the token given and in all, from the member's row.</summary>
    private static (long Under, long All) ReadDocumentGround(MountedFixture fixture, string objectRef, string token)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(SUM(json_extract(j.value,'$.disposition')=$token),0),COUNT(*) " +
            "FROM members m,json_each(m.stage3_outcomes_json) j " +
            "WHERE m.object_ref_sha256=$ref AND json_extract(j.value,'$.domain')='luxembourg_akn_legal_content'";
        command.Parameters.AddWithValue("$ref", objectRef);
        command.Parameters.AddWithValue("$token", token);
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static string AcquiredMemberRef(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.object_ref_sha256 FROM members m WHERE m.object_ref_sha256=" +
            "(SELECT a.object_ref_sha256 FROM articles a WHERE a.expression_iri=$expression LIMIT 1)";
        command.Parameters.AddWithValue("$expression", fixture.ExpressionIri);
        return (string)command.ExecuteScalar()!;
    }

    private static async Task<JsonElement> AnswerAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-not-admitted";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, Encoding.UTF8.GetString(ResponseBytes(context)));
        return envelope.Result!.Value;
    }

    private static string Body(string operation, object parameters) =>
        JsonSerializer.Serialize(new { operation_id = operation, parameters });

    private const string PinnedNote =
        "articles_not_admitted counts the articles the corpus recorded for the publisher's document for this state that this state does not hold; " +
        "the number of articles in this state plus articles_not_admitted is the number of articles the corpus recorded for that document, which provenance counts by disposition token; " +
        "which articles they are is not held. " +
        "An article is not admitted whole when the reviewed profile cannot represent every element in it: " +
        "that can be an article the publisher struck out, and it can equally be an article whose text is complete " +
        "but which carries a mark the profile does not accept, such as an empty placeholder where a list item was removed";

    [TestMethod]
    public void TheCountAndTheSentenceRestOnUnsupportedContentShapeBeingTheOnlyNonHeldDispositionAStatesDocumentCarries()
    {
        // Each of the four others ends a document's run in the legal-content stage before its articles are read
        // (RunAsync: the inventory check, then the retained bytes, the XML and the coordinates), so a document that
        // produced one admitted article, which is what a state needs, never carries them. The three sets below
        // partition the enum: the commit that adds a disposition fails here, in front of whoever adds it, naming the
        // sentence whose arithmetic depends on the answer.
        var held = new[]
        {
            LuxembourgAknLegalContentDisposition.Admitted,
            LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence,
        };
        var endsADocumentsRun = new[]
        {
            LuxembourgAknLegalContentDisposition.UpstreamNotInventoried,
            LuxembourgAknLegalContentDisposition.RetainedBytesUnavailable,
            LuxembourgAknLegalContentDisposition.XmlRejected,
            LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch,
        };
        var carriedByADocumentWithAState = new[] { LuxembourgAknLegalContentDisposition.UnsupportedContentShape };

        CollectionAssert.AreEquivalent(
            Enum.GetValues<LuxembourgAknLegalContentDisposition>(),
            held.Concat(endsADocumentsRun).Concat(carriedByADocumentWithAState).ToArray(),
            "A disposition was added to or removed from LuxembourgAknLegalContentDisposition. articles_not_admitted counts " +
            "akn_unsupported_content_shape alone, and V3CorpusMount.ArticlesNotAdmittedNote says the articles in a state plus " +
            "articles_not_admitted are the articles the corpus recorded for that document: that holds only while the one " +
            "non-held disposition a document with a state can carry is UnsupportedContentShape. Decide where the new " +
            "disposition belongs, then update this test, LuxembourgIndexReader.ResolveArticlesNotAdmitted and the note.");
    }

    [TestMethod]
    public async Task EveryAnswerThatServesTheRealActsStateSaysFiveOfItsDocumentsArticlesWereNotAdmitted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var member = AcquiredMemberRef(fixture);
        var (unsupported, all) = ReadDocumentGround(fixture, member, "akn_unsupported_content_shape");
        // The committed real document: 54 top-level articles, 49 admitted and five the reviewed profile cannot represent.
        Assert.AreEqual(5, unsupported);
        Assert.AreEqual(54, all);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var date = fixture.ApplicabilityDate;

        var asOf = await AnswerAsync(mount, V3RestRouteBinding.AsOf.RawTarget,
            Body("as_of", new { identifier, date, language = "fra" }));
        var timeline = await AnswerAsync(mount, V3RestRouteBinding.Timeline.RawTarget,
            Body("timeline", new { identifier, language = "fra" }));
        var dossier = await AnswerAsync(mount, V3RestRouteBinding.Dossier.RawTarget,
            Body("dossier", new { identifier, language = "fra" }));
        var diff = await AnswerAsync(mount, V3RestRouteBinding.Diff.RawTarget,
            Body("diff", new { identifier, date_from = date, date_to = date, language = "fra" }));
        var pinned = await AnswerAsync(mount, V3ResolveRestRoute.RawTarget,
            Body("resolve", new { identifier = "https://law.soufien.lu" + fixture.Permalink }));

        var asOfState = asOf.GetProperty("states").EnumerateArray().Single();
        var timelineState = timeline.GetProperty("states").EnumerateArray().Single();
        var dossierState = dossier.GetProperty("states").EnumerateArray().Single();
        var comparison = diff.GetProperty("comparisons").EnumerateArray().Single();
        foreach (var (name, row) in new (string, JsonElement)[]
                 {
                     ("as_of", asOfState), ("timeline", timelineState), ("dossier", dossierState),
                     ("diff from", comparison.GetProperty("from")), ("diff to", comparison.GetProperty("to")),
                     ("resolve, pinned", pinned),
                 })
        {
            Assert.AreEqual(JsonValueKind.Number, row.GetProperty("articles_not_admitted").ValueKind, name);
            Assert.AreEqual(unsupported, row.GetProperty("articles_not_admitted").GetInt64(), name);
        }

        // The state's articles plus the count are the document's articles: 49 held and five not, 54 recorded.
        Assert.AreEqual(49, asOfState.GetProperty("article_identities").GetArrayLength());
        Assert.AreEqual(49, dossierState.GetProperty("article_count").GetInt32());
        Assert.AreEqual(all, asOfState.GetProperty("article_identities").GetArrayLength() + asOfState.GetProperty("articles_not_admitted").GetInt64());
        Assert.AreEqual(all, pinned.GetProperty("article_identities").GetArrayLength() + pinned.GetProperty("articles_not_admitted").GetInt64());

        // The one sentence, in the platform's words, on every answer that carries the field: not computed from the
        // constant, so a reworded sentence fails here.
        foreach (var (name, body) in new (string, JsonElement)[]
                 {
                     ("as_of", asOf), ("timeline", timeline), ("dossier", dossier), ("diff", diff), ("resolve, pinned", pinned),
                 })
        {
            Assert.AreEqual(PinnedNote, body.GetProperty("articles_not_admitted_note").GetString(), name);
        }
    }

    [TestMethod]
    public async Task TwoStatesOfTwoDocumentsEachCarryTheirOwnDocumentsCount()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        // The later state is made of a second document, whose outcomes differ: forty-nine admitted and three not.
        var second = new string('7', 64);
        await fixture.AddIndexOnlyMembersAsync((second, "acquired", MountedFixture.OutcomesJson(
            AknDomain, ("akn_admitted", 49), ("akn_unsupported_content_shape", 3))));
        await fixture.PointExpressionAtMemberAsync(later.ExpressionIri, second);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        Assert.AreEqual(5, ReadDocumentGround(fixture, AcquiredMemberRef(fixture), "akn_unsupported_content_shape").Under);
        Assert.AreEqual(3, ReadDocumentGround(fixture, second, "akn_unsupported_content_shape").Under);

        var timeline = await AnswerAsync(mount, V3RestRouteBinding.Timeline.RawTarget, Body("timeline", new { identifier }));
        var dossier = await AnswerAsync(mount, V3RestRouteBinding.Dossier.RawTarget, Body("dossier", new { identifier }));

        // Each state its own document's count, in the reader's order (date), on both answers.
        foreach (var (name, body) in new (string, JsonElement)[] { ("timeline", timeline), ("dossier", dossier) })
        {
            var rows = body.GetProperty("states").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(
                new[] { fixture.ApplicabilityDate, laterDate },
                rows.Select(static row => row.GetProperty("applicability_date").GetString()).ToArray(), name);
            CollectionAssert.AreEqual(
                new long[] { 5, 3 }, rows.Select(static row => row.GetProperty("articles_not_admitted").GetInt64()).ToArray(), name);
        }

        var onFirst = await AnswerAsync(mount, V3RestRouteBinding.AsOf.RawTarget,
            Body("as_of", new { identifier, date = fixture.ApplicabilityDate }));
        var onLater = await AnswerAsync(mount, V3RestRouteBinding.AsOf.RawTarget,
            Body("as_of", new { identifier, date = laterDate }));
        Assert.AreEqual(5, onFirst.GetProperty("states").EnumerateArray().Single().GetProperty("articles_not_admitted").GetInt64());
        Assert.AreEqual(3, onLater.GetProperty("states").EnumerateArray().Single().GetProperty("articles_not_admitted").GetInt64());

        // A comparison of the two states carries both: the earlier document's and the later one's.
        var diff = await AnswerAsync(mount, V3RestRouteBinding.Diff.RawTarget,
            Body("diff", new { identifier, date_from = fixture.ApplicabilityDate, date_to = laterDate }));
        var comparison = diff.GetProperty("comparisons").EnumerateArray().Single();
        Assert.AreEqual(5, comparison.GetProperty("from").GetProperty("articles_not_admitted").GetInt64());
        Assert.AreEqual(3, comparison.GetProperty("to").GetProperty("articles_not_admitted").GetInt64());
    }

    [TestMethod]
    public async Task ADocumentWithNoArticleTheProfileRefusedSaysZeroAndNeverNothing()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.SetMemberOutcomesAsync(MountedFixture.OutcomesJson(AknDomain, ("akn_admitted", 49)));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var asOf = await AnswerAsync(mount, V3RestRouteBinding.AsOf.RawTarget,
            Body("as_of", new { identifier, date = fixture.ApplicabilityDate }));

        var count = asOf.GetProperty("states").EnumerateArray().Single().GetProperty("articles_not_admitted");
        Assert.AreEqual(JsonValueKind.Number, count.ValueKind);
        Assert.AreEqual(0, count.GetInt64());
    }

    [TestMethod]
    public async Task AStateTheIndexResolvedThatHasNoSourceDocumentRowIsABrokenIndexAndNotAZero()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var manifest = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        using var reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), manifest, CancellationToken.None);

        Assert.IsEmpty(reader.ResolveArticlesNotAdmitted([]));
        var counted = reader.ResolveArticlesNotAdmitted([fixture.StateSha256, fixture.StateSha256]);
        Assert.AreEqual(1, counted.Count);
        Assert.AreEqual(5, counted[fixture.StateSha256]);
        Assert.ThrowsExactly<InvalidDataException>(() => reader.ResolveArticlesNotAdmitted([new string('e', 64)]));
    }
}
