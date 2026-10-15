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
/// A Luxembourg act held only as first published says so in every answer that serves its states (the plan's item 0.6;
/// the specification of record's R76, R18 and R20), driven through the real handler on a verified mount: <c>as_of</c>,
/// <c>evidence_bundle</c>, <c>timeline</c> and <c>dossier</c> carry <c>as_published</c> (the label, the facts the index
/// holds about later versions, and an authenticity sentence whose case is the publisher's publication date of the act as
/// published), and the two with a not-held list carry the later-versions row; <c>diff</c> carries it too (the review of #964). The fixture's act is held as its Journal
/// officiel expression (<c>.../jo/fr</c>) and the index holds none of its facts, so it is the case the whole population is
/// in today, with the least said; the facts are then added one kind at a time, and none is ever invented.
/// </summary>
[TestClass]
public sealed class V3CorpusAsPublishedMountTests
{
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    private const string Label = "as published; may have been amended since; no consolidated text";

    [TestMethod]
    public async Task EveryAnswerThatServesTheStatesOfAnActHeldOnlyAsPublishedSaysSoInOneWayOverRestAndMcp()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var work = PublisherWorkIriOf(fixture);

        var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["as_of"] = await AnswerAsync(mount, "as_of", new { identifier, date = fixture.ApplicabilityDate }),
            ["evidence_bundle"] = await AnswerAsync(mount, "evidence_bundle", new { identifier, date = fixture.ApplicabilityDate }),
            ["timeline"] = await AnswerAsync(mount, "timeline", new { identifier }),
            ["dossier"] = await AnswerAsync(mount, "dossier", new { identifier }),
            // A comparison serves the act's states too (the review of #964).
            ["diff"] = await AnswerAsync(mount, "diff", new { identifier, date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
        };
        foreach (var (operation, answer) in answers)
        {
            var published = answer.GetProperty("as_published");
            Assert.AreEqual(Label, published.GetProperty("label").GetString(), operation);
            Assert.AreEqual(work + "/jo", published.GetProperty("journal_officiel_iri").GetString(), operation);
            StringAssert.Contains(published.GetProperty("rule").GetString(), "/jo", operation);
            var later = published.GetProperty("later_version_facts");
            // No later-version fact is held, so only that later versions are not held is said, and never that none exists.
            Assert.AreEqual("later versions of this act are not held", later.GetProperty("sentence").GetString(), operation);
            Assert.AreEqual(0, later.GetProperty("consolidations").GetArrayLength(), operation);
            Assert.AreEqual(0, later.GetProperty("end_of_force").GetArrayLength(), operation);
            var authenticity = published.GetProperty("authenticity");
            Assert.AreEqual("publication_date_not_stated", authenticity.GetProperty("basis").GetString(), operation);
            Assert.AreEqual(
                "This text was extracted and normalised by Lex, for information; it is not authentic. The publisher's record gives no single " +
                $"publication date for this act, so Lex does not say which edition is authentic. The act as published: {work}/jo.",
                authenticity.GetProperty("sentence").GetString(), operation);
            Assert.AreEqual(0, authenticity.GetProperty("publication_dates").GetArrayLength(), operation);
            // The file the text was read from, with what the publisher asserts on it: here nothing, served as nothing.
            var served = authenticity.GetProperty("served_manifestations").EnumerateArray().Single();
            Assert.AreEqual(fixture.ExpressionIri + "/xml", served.GetProperty("manifestation_iri").GetString(), operation);
            Assert.AreEqual(0, served.GetProperty("legal_values").GetArrayLength(), operation);
            foreach (var sentence in new[] { later.GetProperty("sentence").GetString()!, authenticity.GetProperty("sentence").GetString()! })
            {
                foreach (var forbidden in new[] { " in force", "en vigueur", "applied" })
                {
                    Assert.DoesNotContain(forbidden, sentence, $"{operation} says {forbidden} (R21)");
                }
            }
        }

        // The two answers with a not-held list end it with the later-versions row, in the same words.
        foreach (var operation in new[] { "evidence_bundle", "dossier" })
        {
            var row = answers[operation].GetProperty("not_held").EnumerateArray().Last();
            Assert.AreEqual("later_versions", row.GetProperty("item").GetString(), operation);
            Assert.AreEqual("later versions of this act are not held", row.GetProperty("reason").GetString(), operation);
        }

        // MCP answers through the same dispatch: the tool's envelope carries the same member, byte for byte.
        var bytes = await V3McpJsonRpc.HandleAsync(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = "dossier", arguments = new { identifier } },
            }),
            new V3PlatformHost(), mount, static () => ObservedAt, "mcp_as_published", CancellationToken.None);
        Assert.IsNotNull(bytes);
        using var mcp = JsonDocument.Parse(bytes);
        var structured = mcp.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.AreEqual(
            answers["dossier"].GetProperty("as_published").GetRawText(),
            structured.GetProperty("result").GetProperty("value").GetProperty("as_published").GetRawText());
    }

    [TestMethod]
    public async Task LaterVersionsAreTheConsolidatedVersionsThePublisherNamesAndItsEndOfForceDateAndNothingElse()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var work = PublisherWorkIriOf(fixture);
        // Given out of order: the answer's order is the dates', the undated last.
        await fixture.AddWorkFactAsync(work + "/consolide/20240601", "dateApplicability", "consolidation_applicability", "literal", "2024-06-01", XsdDate);
        await fixture.AddWorkFactAsync(work + "/consolide/20220101", "dateEndApplicability", "consolidation_applicability", "literal", "2024-06-01", XsdDate);
        await fixture.AddWorkFactAsync(work + "/consolide/20220101", "dateApplicability", "consolidation_applicability", "literal", "2022-01-01", XsdDate);
        // A consolidation the publisher dates twice has no single applicability date, and is listed as such.
        await fixture.AddWorkFactAsync(work + "/consolide/twice", "dateApplicability", "consolidation_applicability", "literal", "2023-01-01", XsdDate);
        await fixture.AddWorkFactAsync(work + "/consolide/twice", "dateApplicability", "consolidation_applicability", "literal", "2023-02-01", XsdDate);
        // Not consolidated versions of this work: an expression of one, and another work whose IRI this one's prefixes.
        await fixture.AddWorkFactAsync(work + "/consolide/20240601/fr", "language", "expression_language_or_title", "iri", "http://publications.europa.eu/resource/authority/language/FRA");
        await fixture.AddWorkFactAsync(work + "0/consolide/20990101", "dateApplicability", "consolidation_applicability", "literal", "2099-01-01", XsdDate);
        await fixture.AddWorkFactAsync(work + "/jo", "dateNoLongerInForce", "act_force", "literal", "2026-01-04", XsdDate);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var dossier = await AnswerAsync(mount, "dossier", new { identifier = $"/lu-legilux/{fixture.WorkKey}" });

        var later = dossier.GetProperty("as_published").GetProperty("later_version_facts");
        CollectionAssert.AreEqual(
            new (string, string?, string?)[]
            {
                (work + "/consolide/20220101", "2022-01-01", "2024-06-01"),
                (work + "/consolide/20240601", "2024-06-01", null),
                (work + "/consolide/twice", null, null),
            },
            later.GetProperty("consolidations").EnumerateArray().Select(static row => ((string, string?, string?))(
                row.GetProperty("legal_resource_iri").GetString()!,
                row.GetProperty("applicability_date").GetString(),
                row.GetProperty("end_applicability_date").GetString())).ToArray());
        var end = later.GetProperty("end_of_force").EnumerateArray().Single();
        Assert.AreEqual(work + "/jo", end.GetProperty("subject_iri").GetString());
        Assert.AreEqual("2026-01-04", end.GetProperty("value").GetString());
        const string expected =
            "later versions of this act are not held: the publisher's record names 3 consolidated versions of it, applicable from 2022-01-01 " +
            "to 2024-06-01 and from 2024-06-01 (publisher), and 1 with no single applicability date stated; this index holds none of their " +
            "text; the publisher's record gives 2026-01-04 as the act's end-of-force date (dateNoLongerInForce)";
        Assert.AreEqual(expected, later.GetProperty("sentence").GetString());
        Assert.AreEqual(expected, dossier.GetProperty("not_held").EnumerateArray().Last().GetProperty("reason").GetString());
    }

    [TestMethod]
    public void TheLaterVersionsSentenceSaysOnlyWhatItsFactsSay()
    {
        Assert.AreEqual("later versions of this act are not held", V3CorpusMount.LaterVersionsSentence([], []));
        Assert.AreEqual(
            "later versions of this act are not held: the publisher's record names 1 consolidated version of it, applicable from 2022-04-09 " +
            "(publisher); this index holds none of its text",
            V3CorpusMount.LaterVersionsSentence([("2022-04-09", null)], []));
        Assert.AreEqual(
            "later versions of this act are not held: the publisher's record names 2 consolidated versions of it, with no single applicability " +
            "date stated; this index holds none of their text",
            V3CorpusMount.LaterVersionsSentence([(null, null), (null, "2024-01-01")], []));
        Assert.AreEqual(
            "later versions of this act are not held; the publisher's record gives 2020-01-01 and 2021-01-01 as the act's end-of-force date " +
            "(dateNoLongerInForce)",
            V3CorpusMount.LaterVersionsSentence([], ["2020-01-01", "2021-01-01"]));
    }

    [TestMethod]
    public async Task TheAuthenticityCaseIsThePublishersPublicationDateOfTheActAsPublishedAndTheServedFilesMarkIsServedVerbatim()
    {
        foreach (var (dates, basis, sentence) in new (string[], string, string)[]
                 {
                     (["2017-01-01"], "electronic_journal_officiel",
                         "This text was extracted and normalised by Lex, for information; it is not authentic. Since 1 January 2017, only the " +
                         "Journal officiel du Grand-Duché de Luxembourg (Mémorial A) as published electronically is authentic (loi du 23 " +
                         "décembre 2016, art. 5). The act as published: {0}."),
                     (["2016-12-31"], "printed_memorial",
                         "This text was extracted and normalised by Lex, for information; it is not authentic. For an act published before 1 " +
                         "January 2017, the printed Mémorial is authentic, and the publisher's PDF is its digital reproduction. The act as " +
                         "published: {0}."),
                     // Two dates are no single date: which edition is authentic is not said.
                     (["1991-08-20", "1991-08-21"], "publication_date_not_stated",
                         "This text was extracted and normalised by Lex, for information; it is not authentic. The publisher's record gives no " +
                         "single publication date for this act, so Lex does not say which edition is authentic. The act as published: {0}."),
                 })
        {
            var fixture = await MountedFixture.CreateAsync();
            await using var cleanup = fixture;
            var work = PublisherWorkIriOf(fixture);
            foreach (var date in dates)
            {
                await fixture.AddWorkFactAsync(work + "/jo", "publicationDate", "descriptive_date", "literal", date, XsdDate);
            }
            const string officiel = "http://data.legilux.public.lu/resource/authority/statut-version/officiel";
            await fixture.AddWorkFactAsync(fixture.ExpressionIri + "/xml", "legalValue", "legal_value_assertion", "iri", officiel);
            using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);

            var bundle = await AnswerAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate });

            var authenticity = bundle.GetProperty("as_published").GetProperty("authenticity");
            Assert.AreEqual(basis, authenticity.GetProperty("basis").GetString(), string.Join(",", dates));
            Assert.AreEqual(string.Format(CultureInfo.InvariantCulture, sentence, work + "/jo"), authenticity.GetProperty("sentence").GetString(), basis);
            CollectionAssert.AreEqual(dates, authenticity.GetProperty("publication_dates").EnumerateArray().Select(static fact => fact.GetProperty("value").GetString()).ToArray());
            // A file the publisher marks officiel is named with its mark, and Lex's extraction of it is still not authentic.
            var served = authenticity.GetProperty("served_manifestations").EnumerateArray().Single();
            Assert.AreEqual(officiel, served.GetProperty("legal_values").EnumerateArray().Single().GetProperty("value").GetString());
            StringAssert.StartsWith(authenticity.GetProperty("sentence").GetString(), "This text was extracted and normalised by Lex, for information; it is not authentic.");
        }
    }

    [TestMethod]
    public async Task AWorkWhoseConsolidatedStateIsHeldIsNotLabelledAndListsNoLaterVersionsRow()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var later = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "fr", legalResourceLeaf: "consolide/20250301");
        StringAssert.Contains(later.ExpressionIri, "/consolide/20250301/fr");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        foreach (var (operation, parameters) in new (string, object)[]
                 {
                     ("dossier", new { identifier }),
                     ("timeline", new { identifier }),
                     // The state of the date asked is the act as published, and the work still holds a consolidated text.
                     ("as_of", new { identifier, date = fixture.ApplicabilityDate }),
                     ("evidence_bundle", new { identifier, date = fixture.ApplicabilityDate }),
                     ("diff", new { identifier, date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
                 })
        {
            var answer = await AnswerAsync(mount, operation, parameters);
            Assert.AreEqual(JsonValueKind.Null, answer.GetProperty("as_published").ValueKind, operation);
            if (answer.TryGetProperty("not_held", out var notHeld))
            {
                Assert.IsFalse(
                    notHeld.EnumerateArray().Any(static row => row.GetProperty("item").GetString() == "later_versions"),
                    $"{operation} lists later versions as not held for a work whose consolidated text is held");
            }
        }
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string PublisherWorkIriOf(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT publisher_work_iri FROM states WHERE work_key=$work LIMIT 1";
        command.Parameters.AddWithValue("$work", fixture.WorkKey);
        return (string)command.ExecuteScalar()!;
    }

    private static async Task<JsonElement> AnswerAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-as-published";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, operation + ": " + envelope.Refusal?.Code);
        return envelope.Result!.Value.Clone();
    }
}
