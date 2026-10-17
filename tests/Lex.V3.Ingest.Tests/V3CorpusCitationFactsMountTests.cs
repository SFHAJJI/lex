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
/// What the citation generator composes a Luxembourg article's citations from (the plan's item 3.3; the specification of
/// record's R65), driven through the real handler on a verified mount: <c>evidence_bundle</c> carries <c>citation_facts</c>,
/// the publisher's titles of the listed states' expressions and the act as published with its Mémorial issue and publication
/// date facts, verbatim and never composed. The fixture's act is held with none of these facts, which is said by empty lists;
/// they are then added, beside facts of other subjects and an expression the answer does not list, which it never serves.
/// </summary>
[TestClass]
public sealed class V3CorpusCitationFactsMountTests
{
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    [TestMethod]
    public async Task AnActWithNoTitleOrPublicationFactHeldSaysSoByEmptyListsAndNamesTheActAsPublished()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = PublisherWorkIriOf(fixture);

        var bundle = await AnswerAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate });

        var facts = bundle.GetProperty("citation_facts");
        Assert.AreEqual(0, facts.GetProperty("titles").GetArrayLength());
        Assert.AreEqual(work + "/jo", facts.GetProperty("journal_officiel_iri").GetString());
        Assert.AreEqual(0, facts.GetProperty("memorial_issues").GetArrayLength());
        Assert.AreEqual(0, facts.GetProperty("publication_dates").GetArrayLength());
        Assert.AreEqual(V3CorpusMount.CitationFactsRule, facts.GetProperty("rule").GetString());
        StringAssert.Contains(facts.GetProperty("rule").GetString(), "never that none exists");
        StringAssert.Contains(bundle.GetProperty("scope").GetString(), "citation_facts");
    }

    [TestMethod]
    public async Task TheTitlesTheMemorialIssueAndThePublicationDateAreThePublishersVerbatimForTheListedStatesOnly()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var work = PublisherWorkIriOf(fixture);
        var title = "Loi du 10 août 1991 sur la profession d’avocat.";
        await fixture.AddTitleRowsAsync(
            (null, fixture.ExpressionIri, "fra", "title", title, new string('1', 64)),
            (null, fixture.ExpressionIri, "fra", "title_short", "Loi sur la profession d’avocat", new string('2', 64)),
            // Another expression of the work, which no listed state is: never served.
            (null, work + "/consolide/20210822/fr", "fra", "title", "Version consolidée applicable au 22/08/2021 : " + title, new string('3', 64)));
        await fixture.AddWorkFactAsync(work + "/jo", "isPartOf", "wemi_structural", "iri", "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58", evidenceSha256: new string('4', 64));
        await fixture.AddWorkFactAsync(work + "/jo", "publicationDate", "descriptive_date", "literal", "1991-08-27", XsdDate, evidenceSha256: new string('5', 64));
        // Facts of other subjects: the work itself and an expression; neither is the act as published.
        await fixture.AddWorkFactAsync(work, "isPartOf", "wemi_structural", "iri", "http://data.legilux.public.lu/eli/etat/leg/memorial/1999/a1");
        await fixture.AddWorkFactAsync(fixture.ExpressionIri, "publicationDate", "descriptive_date", "literal", "1999-01-01", XsdDate);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { $"/lu-legilux/{fixture.WorkKey}", fixture.Permalink })
        {
            var bundle = await AnswerAsync(mount, "evidence_bundle", new { identifier, date = fixture.ApplicabilityDate });
            var facts = bundle.GetProperty("citation_facts");

            var group = facts.GetProperty("titles").EnumerateArray().Single();
            Assert.AreEqual("fra", group.GetProperty("language").GetString(), identifier);
            Assert.AreEqual(fixture.ExpressionIri, group.GetProperty("expression_iri").GetString(), identifier);
            var held = group.GetProperty("titles").EnumerateArray().Single();
            Assert.AreEqual(title, held.GetProperty("title").GetString(), identifier);
            Assert.AreEqual(new string('1', 64), held.GetProperty("evidence_sha256").GetString(), identifier);
            Assert.AreEqual("Loi sur la profession d’avocat", group.GetProperty("short_titles")[0].GetProperty("title").GetString(), identifier);

            var issue = facts.GetProperty("memorial_issues").EnumerateArray().Single();
            Assert.AreEqual(work + "/jo", issue.GetProperty("subject_iri").GetString(), identifier);
            Assert.AreEqual("iri", issue.GetProperty("object_kind").GetString(), identifier);
            Assert.AreEqual("http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58", issue.GetProperty("value").GetString(), identifier);
            Assert.AreEqual(new string('4', 64), issue.GetProperty("evidence_sha256").GetString(), identifier);

            var published = facts.GetProperty("publication_dates").EnumerateArray().Single();
            Assert.AreEqual("1991-08-27", published.GetProperty("value").GetString(), identifier);
            Assert.AreEqual(XsdDate, published.GetProperty("datatype_iri").GetString(), identifier);
        }

        // The dossier serves the same titles, read by the one helper: its group for the listed expression is the bundle's.
        var dossier = await AnswerAsync(mount, "dossier", new { identifier = $"/lu-legilux/{fixture.WorkKey}" });
        var bundleTitles = (await AnswerAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate }))
            .GetProperty("citation_facts").GetProperty("titles");
        Assert.AreEqual(bundleTitles.GetRawText(), dossier.GetProperty("titles").GetRawText());
    }

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
        context.TraceIdentifier = "mounted-corpus-citation-facts";
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
