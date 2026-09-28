using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>status_on</c> on a mounted corpus: the publisher's force assertions about a work, verbatim, beside the
/// state <c>as_of</c> selects for the date, with one fixed reading of the publisher's dates. The real act's
/// envelope asserts no force fact, so the absence is typed first; then the facts are asserted through the
/// fixture as the builder would have written them and the reading is held on every side of the dates.
/// </summary>
[TestClass]
public sealed class V3CorpusStatusOnMountTests
{
    private static string RawTarget => V3RestRouteBinding.StatusOn.RawTarget;
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public void TheStatusOnRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/status_on", RawTarget);
        Assert.AreEqual("status_on", V3RestRouteBinding.StatusOn.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.StatusOn));
    }

    [TestMethod]
    public async Task WithNoForceFactAssertedTheAbsenceIsTypedAndTheStateIsAsOfs()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };
        Assert.IsFalse(ReadFacts(fixture).Any(static row => row.FactKind == "act_force"), "the real act's envelope asserts no force fact; this test relies on it.");

        var envelope = await EnvelopeAsync(mount, RawTarget, "status_on", parameters);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        Assert.AreEqual("version_state", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.IsFalse(body.GetProperty("force_facts_held").GetBoolean());
        Assert.AreEqual(0, body.GetProperty("in_force_status").GetArrayLength());
        Assert.AreEqual(0, body.GetProperty("entry_into_force").GetArrayLength());
        Assert.AreEqual(0, body.GetProperty("no_longer_in_force").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("asserted_in_force_on_date").ValueKind, "no fact is not 'not in force'.");
        Assert.AreEqual("no dated force fact asserted", body.GetProperty("reading_basis").GetString());
        CollectionAssert.AreEqual(new[] { "new_official_observation" }, body.GetProperty("what_would_answer").EnumerateArray().Select(static value => value.GetString()).ToArray());
        Assert.IsFalse(body.GetProperty("asserts_absence_of_law").GetBoolean());

        var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", parameters);
        var state = body.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual(
            asOf.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state_sha256").GetString(),
            state.GetProperty("state_sha256").GetString(),
            "the state beside the facts is the one as_of selects for the same request.");
        Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString());
        CollectionAssert.AreEqual(
            new[] { "repeal_and_amendment_events", "article_level_force", "status_at_publication" },
            body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());
    }

    [TestMethod]
    public async Task TheFactsAreServedVerbatimAndTheReadingFollowsTheRuleOnEverySideOfTheDates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var entry = Shift(fixture.ApplicabilityDate, -30);
        var end = Shift(fixture.ApplicabilityDate, 365);
        var work = fixture.ExpressionIri[..fixture.ExpressionIri.LastIndexOf('/')];
        Assert.IsTrue(work.EndsWith("/jo", StringComparison.Ordinal), "the fixture's expression sits under the act's /jo resource: " + work);
        await fixture.AddWorkFactAsync(work, "inForceStatus", "act_force", "iri", "http://data.legilux.public.lu/resource/authority/legal-status/inForce", evidenceSha256: new string('1', 64));
        await fixture.AddWorkFactAsync(work, "dateEntryInForce", "act_force", "literal", entry, XsdDate, evidenceSha256: new string('2', 64));
        await fixture.AddWorkFactAsync(work, "dateNoLongerInForce", "act_force", "literal", end, XsdDate, evidenceSha256: new string('3', 64));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var onDate = (await EnvelopeAsync(mount, RawTarget, "status_on", new { identifier, date = fixture.ApplicabilityDate, language = "fra" })).Result!.Value;
        Assert.IsTrue(onDate.GetProperty("force_facts_held").GetBoolean());
        CollectionAssert.Contains(onDate.GetProperty("subjects").EnumerateArray().Select(static value => value.GetString()).ToArray(), work);
        var status = onDate.GetProperty("in_force_status").EnumerateArray().Single();
        Assert.AreEqual(work, status.GetProperty("subject_iri").GetString());
        Assert.AreEqual("iri", status.GetProperty("object_kind").GetString());
        StringAssert.EndsWith(status.GetProperty("value").GetString(), "/inForce");
        Assert.AreEqual(new string('1', 64), status.GetProperty("evidence_sha256").GetString());
        var entryFact = onDate.GetProperty("entry_into_force").EnumerateArray().Single();
        Assert.AreEqual(entry, entryFact.GetProperty("value").GetString());
        Assert.AreEqual(XsdDate, entryFact.GetProperty("datatype_iri").GetString());
        Assert.AreEqual(JsonValueKind.Null, entryFact.GetProperty("language_tag").ValueKind);
        Assert.AreEqual(end, onDate.GetProperty("no_longer_in_force").EnumerateArray().Single().GetProperty("value").GetString());
        Assert.IsTrue(onDate.GetProperty("asserted_in_force_on_date").GetBoolean(), "entry on or before the date, end after it.");
        Assert.AreEqual(JsonValueKind.Null, onDate.GetProperty("what_would_answer").ValueKind, "facts are held; there is no absence to route out of.");

        // On the end date itself the publisher's dateNoLongerInForce is on or before the requested date: false.
        var onEnd = (await EnvelopeAsync(mount, RawTarget, "status_on", new { identifier, date = end, language = "fra" })).Result!.Value;
        Assert.IsFalse(onEnd.GetProperty("asserted_in_force_on_date").GetBoolean());
        StringAssert.Contains(onEnd.GetProperty("reading_basis").GetString(), "dateNoLongerInForce");

    }

    [TestMethod]
    public async Task AnEntryIntoForceAfterTheDateReadsFalseAndAnEndAloneReadsNull()
    {
        // The two sides the first fixture cannot reach: the publisher's entry date after the requested date, and an
        // end date alone (after the requested date), which says nothing about the work having been in force.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var work = fixture.ExpressionIri[..fixture.ExpressionIri.LastIndexOf('/')];
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        await fixture.AddWorkFactAsync(work, "dateNoLongerInForce", "act_force", "literal", Shift(fixture.ApplicabilityDate, 365), XsdDate, evidenceSha256: new string('3', 64));
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var endAlone = (await EnvelopeAsync(mount, RawTarget, "status_on", new { identifier, date = fixture.ApplicabilityDate, language = "fra" })).Result!.Value;
            Assert.IsTrue(endAlone.GetProperty("force_facts_held").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, endAlone.GetProperty("asserted_in_force_on_date").ValueKind);
            StringAssert.Contains(endAlone.GetProperty("reading_basis").GetString(), "no dateEntryInForce asserted");
        }

        await fixture.AddWorkFactAsync(work, "dateEntryInForce", "act_force", "literal", Shift(fixture.ApplicabilityDate, 10), XsdDate, evidenceSha256: new string('2', 64));
        using var remounted = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(remounted);
        var entryAfter = (await EnvelopeAsync(remounted, RawTarget, "status_on", new { identifier, date = fixture.ApplicabilityDate, language = "fra" })).Result!.Value;
        Assert.IsFalse(entryAfter.GetProperty("asserted_in_force_on_date").GetBoolean(), "the only entry date is after the requested date.");
        Assert.AreEqual("every dateEntryInForce is after the requested date", entryAfter.GetProperty("reading_basis").GetString());
    }

    [TestMethod]
    [DataRow("1991-08", "http://www.w3.org/2001/XMLSchema#gYearMonth")]
    [DataRow("2024-01-22garbage", "http://www.w3.org/2001/XMLSchema#date")]
    [DataRow("2024-01-22T00:00:00", "http://www.w3.org/2001/XMLSchema#dateTime")]
    [DataRow("2024-01-22+02:00", "http://www.w3.org/2001/XMLSchema#date")]
    public async Task ADatedFactThatIsNotExactlyACivilDateIsServedVerbatimAndNotRead(string lexical, string datatype)
    {
        // A value that merely begins with a date is not a date: the whole lexical value is parsed, so a dateTime,
        // a timezone-bearing date or a suffix leaves the reading null and the value on the wire as written.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var work = fixture.ExpressionIri[..fixture.ExpressionIri.LastIndexOf('/')];
        await fixture.AddWorkFactAsync(work, "dateEntryInForce", "act_force", "literal", lexical, datatype);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await EnvelopeAsync(mount, RawTarget, "status_on", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" })).Result!.Value;

        Assert.AreEqual(lexical, body.GetProperty("entry_into_force").EnumerateArray().Single().GetProperty("value").GetString());
        Assert.AreEqual(JsonValueKind.Null, body.GetProperty("asserted_in_force_on_date").ValueKind, lexical);
        StringAssert.Contains(body.GetProperty("reading_basis").GetString(), "not a civil date");
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
            var statusOn = await EnvelopeAsync(mount, RawTarget, "status_on", parameters);
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", parameters);
            Assert.AreEqual(expected, statusOn.Refusal?.Code, JsonSerializer.Serialize(parameters));
            Assert.AreEqual(
                JsonSerializer.Serialize(asOf.Refusal!.HelpfulPayload),
                JsonSerializer.Serialize(statusOn.Refusal!.HelpfulPayload),
                expected + ": status_on refuses with as_of's payload.");
        }

        var noDate = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "status_on", parameters = new { identifier = work } }));
        Assert.AreEqual(StatusCodes.Status400BadRequest, noDate.Response.StatusCode);
    }
}
