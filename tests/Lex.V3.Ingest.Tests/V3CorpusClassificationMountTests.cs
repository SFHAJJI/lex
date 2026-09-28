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
/// <c>classification</c> on a mounted corpus: the publisher's typed facts about a work, grouped by
/// predicate and served verbatim from the index's fact table. The groups are held against the rows read
/// by code that shares nothing with the reader; the request and the refusals are <c>dossier</c>'s.
/// </summary>
[TestClass]
public sealed class V3CorpusClassificationMountTests
{
    private static string RawTarget => V3RestRouteBinding.Classification.RawTarget;

    internal sealed record FactRow(
        string Subject, string Predicate, string FactKind, string ObjectKind, string Value, string Datatype, string LanguageTag, string Evidence);

    /// <summary>Every fact row of the mounted index, in the table's order, read by SQL.</summary>
    internal static FactRow[] ReadFacts(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts ORDER BY subject_iri,predicate,object_kind,object_value,datatype_iri,language_tag,evidence_sha256";
        using var reader = command.ExecuteReader();
        var rows = new List<FactRow>();
        while (reader.Read())
        {
            rows.Add(new FactRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }

        return rows.ToArray();
    }

    private static string[] Flat(JsonElement group) =>
        group.EnumerateArray()
            .Select(static fact => fact.GetProperty("subject_iri").GetString() + "|" + fact.GetProperty("object_kind").GetString() + "|" +
                                   fact.GetProperty("value").GetString() + "|" + fact.GetProperty("evidence_sha256").GetString())
            .ToArray();

    private static string[] Flat(IEnumerable<FactRow> rows) =>
        rows.Select(static row => row.Subject + "|" + row.ObjectKind + "|" + row.Value + "|" + row.Evidence).ToArray();

    [TestMethod]
    public void TheClassificationRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/classification", RawTarget);
        Assert.AreEqual("classification", V3RestRouteBinding.Classification.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Classification));
    }

    [TestMethod]
    public async Task TheGroupsAreThePublishersFactsOnTheWorksOwnIrisAndExpressionsVerbatim()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var ground = ReadFacts(fixture);
        Assert.IsNotEmpty(ground, "the real act's envelope carries typed assertions; the fact table must hold them.");

        var envelope = await EnvelopeAsync(mount, RawTarget, "classification", new { identifier, language = "fra" });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        Assert.AreEqual("classification", envelope.OperationId);
        Assert.AreEqual("classification", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.AreEqual(identifier, body.GetProperty("requested_identifier").GetString());
        Assert.AreEqual("fra", body.GetProperty("requested_language").GetString());
        Assert.AreEqual(fixture.WorkKey, body.GetProperty("work_key").GetString());
        var subjects = body.GetProperty("subjects").EnumerateArray().Select(static value => value.GetString()!).ToArray();
        CollectionAssert.Contains(subjects, fixture.ExpressionIri);
        CollectionAssert.Contains(subjects, body.GetProperty("publisher_work_iri").GetString());
        CollectionAssert.Contains(subjects, body.GetProperty("publisher_legal_resource_iri").GetString());
        CollectionAssert.AreEqual(subjects.Order(StringComparer.Ordinal).ToArray(), subjects, "subjects are in ordinal order.");

        var relevant = ground.Where(row => subjects.Contains(row.Subject, StringComparer.Ordinal)).ToArray();
        var groups = new (string Group, string Predicate)[]
        {
            ("document_types", "typeDocument"), ("resource_types", "rdf:type"), ("legal_values", "legalValue"),
            ("responsible_bodies", "responsibilityOf"), ("historical_identifiers", "historicalLegalId"),
            ("publication_dates", "publicationDate"), ("document_dates", "dateDocument"),
        };
        foreach (var (group, predicate) in groups)
        {
            CollectionAssert.AreEqual(
                Flat(relevant.Where(row => row.Predicate == predicate)),
                Flat(body.GetProperty(group)),
                group + ": the group is exactly the rows of its predicate on the work's subjects, in the table's order.");
        }

        var documentType = body.GetProperty("document_types").EnumerateArray().Single();
        StringAssert.EndsWith(documentType.GetProperty("value").GetString(), "/LOI", "the real act is a law, as the publisher typed it.");
        Assert.AreEqual("iri", documentType.GetProperty("object_kind").GetString());
        Assert.AreEqual(JsonValueKind.Null, documentType.GetProperty("datatype_iri").ValueKind, "an IRI object has no datatype.");
        Assert.IsGreaterThan(0, body.GetProperty("resource_types").GetArrayLength());
        Assert.AreEqual(relevant.Length, body.GetProperty("fact_count").GetInt32());
        CollectionAssert.AreEqual(
            relevant.Select(static row => row.Predicate).Where(predicate => !groups.Any(group => group.Predicate == predicate))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            body.GetProperty("other_predicates_held").EnumerateArray().Select(static value => value.GetString()).ToArray(),
            "the predicates held on these subjects and not grouped are named, so nothing held is silently absent.");
        CollectionAssert.AreEqual(
            new[] { "subject_classification", "legal_status", "labels" },
            body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());
        foreach (var digest in new[] { "corpus_sha256", "index_sha256", "registry_sha256" })
        {
            Assert.AreEqual(64, body.GetProperty("verified_by").GetProperty(digest).GetString()!.Length, digest);
        }
    }

    [TestMethod]
    public async Task ItRefusesAsDossierRefuses()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (parameters, expected) in new (object, string)[]
                 {
                     (new { identifier = "/lu-legilux/no-such-work", language = "fra" }, "identifier_unknown"),
                     (new { identifier = $"/lu-legilux/{fixture.WorkKey}", language = "eng" }, "language_not_available"),
                 })
        {
            var classification = await EnvelopeAsync(mount, RawTarget, "classification", parameters);
            var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", parameters);
            Assert.AreEqual(expected, classification.Refusal?.Code, JsonSerializer.Serialize(parameters));
            Assert.AreEqual(dossier.Refusal!.Code, classification.Refusal!.Code);
            Assert.AreEqual(
                JsonSerializer.Serialize(dossier.Refusal.HelpfulPayload),
                JsonSerializer.Serialize(classification.Refusal.HelpfulPayload),
                expected + ": classification refuses with dossier's payload.");
        }
    }

    [TestMethod]
    public async Task ARequestWithoutAnIdentifierIsARequestSchemaFailure()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "classification", parameters = new { language = "fra" } }));

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
    }

    internal static async Task<V3Envelope> EnvelopeAsync(V3CorpusMount mount, string rawTarget, string operation, object parameters)
    {
        var context = await PostAsync(mount, rawTarget, JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    internal static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-classification";
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
