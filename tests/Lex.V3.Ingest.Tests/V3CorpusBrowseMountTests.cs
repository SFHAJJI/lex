using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>browse</c> on a mounted corpus: the held works as one ordered, paged list with the publisher's document
/// types read from the fact table; a type filters by the publisher's typeDocument, a language by the states'
/// language, and the page walks the whole list once.
/// </summary>
[TestClass]
public sealed class V3CorpusBrowseMountTests
{
    private static string RawTarget => V3RestRouteBinding.Browse.RawTarget;

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public void TheBrowseRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/browse", RawTarget);
        Assert.AreEqual("browse", V3RestRouteBinding.Browse.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Browse));
    }

    [TestMethod]
    public async Task TheListIsEveryHeldWorkInKeyOrderWithItsIdentifiersDatesAndThePublishersTypes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "other", workLeaf: "n9");
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 800), "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var ground = ReadFacts(fixture);

        var envelope = await EnvelopeAsync(mount, RawTarget, "browse", new { });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        Assert.AreEqual("work_record", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        var works = body.GetProperty("works").EnumerateArray().ToArray();
        Assert.AreEqual(2, works.Length, "the fixture's act and the other work.");
        var keys = works.Select(static work => work.GetProperty("work_key").GetString()!).ToArray();
        CollectionAssert.AreEqual(keys.Order(StringComparer.Ordinal).ToArray(), keys, "work keys in ordinal order.");
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("next_after").ValueKind);

        var own = works.Single(work => work.GetProperty("work_key").GetString() == fixture.WorkKey);
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", own.GetProperty("identifier").GetString());
        Assert.AreEqual(2, own.GetProperty("state_count").GetInt64(), "the act's own state and the later one.");
        Assert.AreEqual(fixture.ApplicabilityDate, own.GetProperty("history_begins").GetString());
        Assert.AreEqual(Shift(fixture.ApplicabilityDate, 800), own.GetProperty("latest_applicability_date").GetString());
        CollectionAssert.AreEqual(new[] { "fra" }, own.GetProperty("languages").EnumerateArray().Select(static value => value.GetString()).ToArray());
        var ownIris = own.GetProperty("publisher_work_iris").EnumerateArray().Select(static value => value.GetString()!)
            .Concat(own.GetProperty("publisher_legal_resource_iris").EnumerateArray().Select(static value => value.GetString()!)).ToArray();
        CollectionAssert.AreEqual(
            ground.Where(row => ownIris.Contains(row.Subject, StringComparer.Ordinal) && row.Predicate == "typeDocument").Select(static row => row.Value).ToArray(),
            own.GetProperty("document_types").EnumerateArray().Select(static fact => fact.GetProperty("value").GetString()).ToArray(),
            "the document types are the publisher's typeDocument facts on the work's own IRIs.");
        StringAssert.EndsWith(own.GetProperty("document_types").EnumerateArray().Single().GetProperty("value").GetString(), "/LOI");

        var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = $"/lu-legilux/{fixture.WorkKey}" });
        Assert.AreEqual(dossier.Result!.Value.GetProperty("state_count").GetInt32(), (int)own.GetProperty("state_count").GetInt64(), "browse counts what dossier counts.");
        CollectionAssert.AreEqual(
            new[] { "publisher_universe", "titles", "legal_status" },
            body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());
    }

    [TestMethod]
    public async Task ATypeFiltersByThePublishersTypeDocumentAsATokenOrAnIriAndAPageWalksTheListOnce()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "other", workLeaf: "n9");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var typeIri = ReadFacts(fixture).Single(static row => row.Predicate == "typeDocument").Value;
        var token = typeIri[(typeIri.LastIndexOf('/') + 1)..];

        // The type is asserted on the fixture's own act only (the other work's states were added by hand).
        var byToken = (await EnvelopeAsync(mount, RawTarget, "browse", new { type = token })).Result!.Value;
        CollectionAssert.AreEqual(new[] { fixture.WorkKey }, byToken.GetProperty("works").EnumerateArray().Select(static work => work.GetProperty("work_key").GetString()).ToArray());
        Assert.AreEqual(token, byToken.GetProperty("requested_type").GetString());
        var byIri = (await EnvelopeAsync(mount, RawTarget, "browse", new { type = typeIri })).Result!.Value;
        CollectionAssert.AreEqual(new[] { fixture.WorkKey }, byIri.GetProperty("works").EnumerateArray().Select(static work => work.GetProperty("work_key").GetString()).ToArray());
        var none = (await EnvelopeAsync(mount, RawTarget, "browse", new { type = "RGD" })).Result!.Value;
        Assert.AreEqual(0, none.GetProperty("works").GetArrayLength(), "a type no work carries lists nothing and refuses nothing.");

        // Pages of one walk the whole list once, in key order.
        var walked = new List<string>();
        string? after = null;
        for (var page = 0; page < 5; page++)
        {
            var body = (await EnvelopeAsync(mount, RawTarget, "browse", after is null ? new { limit = 1 } : new { limit = 1, after })).Result!.Value;
            walked.AddRange(body.GetProperty("works").EnumerateArray().Select(static work => work.GetProperty("work_key").GetString()!));
            if (body.GetProperty("next_after").ValueKind == JsonValueKind.Null)
            {
                break;
            }

            after = body.GetProperty("next_after").GetString();
        }

        var all = (await EnvelopeAsync(mount, RawTarget, "browse", new { })).Result!.Value.GetProperty("works").EnumerateArray().Select(static work => work.GetProperty("work_key").GetString()!).ToArray();
        CollectionAssert.AreEqual(all, walked.ToArray());
        Assert.AreEqual(2, walked.Count);

        var pattern = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "browse", parameters = new { type = "%" } }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, pattern.Response.StatusCode, "a type that is neither an IRI nor a token is a request-schema failure, never a pattern.");
        var tooMany = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "browse", parameters = new { limit = V3CorpusMount.BrowseMaxRows + 1 } }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, tooMany.Response.StatusCode);
    }

    [TestMethod]
    public async Task ALanguageFiltersToWorksWithAStateInItAndOneNotHeldIsRefused()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "other", workLeaf: "n9");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var german = (await EnvelopeAsync(mount, RawTarget, "browse", new { language = "deu" })).Result!.Value;
        CollectionAssert.AreEqual(new[] { fixture.WorkKey }, german.GetProperty("works").EnumerateArray().Select(static work => work.GetProperty("work_key").GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "deu", "fra" },
            german.GetProperty("works").EnumerateArray().Single().GetProperty("languages").EnumerateArray().Select(static value => value.GetString()).ToArray(),
            "a work's row names every language it has, even when the filter selected it by one.");

        var refused = await EnvelopeAsync(mount, RawTarget, "browse", new { language = "eng" });
        Assert.AreEqual("language_not_available", refused.Refusal?.Code);
        CollectionAssert.AreEqual(
            new[] { "deu", "fra" },
            refused.Refusal!.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task WithNoLuxembourgIndexTheAnswerIsNoCorpusMounted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await EnvelopeAsync(mount, RawTarget, "browse", new { });

        Assert.AreEqual("no_corpus_mounted", envelope.Refusal?.Code);
        Assert.AreEqual("lu", envelope.Refusal!.HelpfulPayload.GetProperty("required_corpus").GetString());
    }
}
