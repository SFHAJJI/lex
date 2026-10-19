using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU act's force dates in its answers (the plan's item 2.1; R79's E1, R21): over an EU index /6, dossier, timeline, as_of
/// and evidence_bundle each say the act's dates by role, with the publisher's code and qualifier word for word and the
/// statement each was read from, beside the wordings and never as one; a wording is chosen exactly as before; and the not-held
/// row says what of the dates is still not held. Over an index /5 the answers are unchanged (<see cref="V3CorpusEuropeSchema5MountTests"/>).
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeForceDatesMountTests
{
    private const string Celex = "32016R0679";

    private static readonly (string Operation, object Parameters)[] Answers =
    [
        ("dossier", new { identifier = Celex }),
        ("timeline", new { identifier = Celex }),
        ("as_of", new { identifier = Celex, date = "2018-06-01" }),
        ("evidence_bundle", new { identifier = Celex, date = "2018-06-01", language = "eng" }),
    ];

    [TestMethod]
    public async Task EveryEuAnswerOfTheActSaysItsForceDatesByRoleBesideItsWordingsAndChoosesNothingByThem()
    {
        var fixture = await EuropeMountedFixture.CreateAsync(withForceDates: true);
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        string? first = null;
        foreach (var (operation, parameters) in Answers)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{operation}: {envelope.Refusal?.Code}");
            var body = envelope.Result!.Value;
            var forceDates = body.GetProperty("force_dates");
            Assert.AreEqual(fixture.PublisherWorkId, forceDates.GetProperty("publisher_work_id").GetString(), "the dates are the act's original work's");
            Assert.AreEqual(Celex, forceDates.GetProperty("seed_celex").GetString());
            var dates = forceDates.GetProperty("dates").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(new[] { "entry_into_force", "application", "deadline" }, dates.Select(static row => row.GetProperty("role").GetString()).ToArray());
            CollectionAssert.AreEqual(new[] { "2016-05-24", "2018-05-25", "2020-05-25" }, dates.Select(static row => row.GetProperty("date").GetString()).ToArray());
            CollectionAssert.AreEqual(new[] { "EV", "MA", null }, dates.Select(static row => row.GetProperty("publisher_code").GetString()).ToArray());
            CollectionAssert.AreEqual(new[] { "DATPUB +20 V ART 99", "V ART 99", "AU+TARD V ART 97" },
                dates.Select(static row => row.GetProperty("publisher_note_codes").GetString()).ToArray(), "the qualifier word for word, in the publisher's codes");
            CollectionAssert.AreEqual(EuForceDatesFixture.Gdpr().Select(static statement => statement.Note).ToArray(),
                dates.Select(static row => row.GetProperty("publisher_note").GetString()).ToArray(), "and the note exactly as served");
            CollectionAssert.AreEqual(EuForceDatesFixture.Gdpr().Select(static statement => statement.Axiom).ToArray(),
                dates.Select(static row => row.GetProperty("statement").GetString()).ToArray(), "each date names the statement it was read from");
            Assert.IsTrue(dates.All(static row => !row.GetProperty("open_ended").GetBoolean()));
            CollectionAssert.AreEqual(new[] { "end_of_validity" }, forceDates.GetProperty("roles_not_held").EnumerateArray().Select(static role => role.GetString()).ToArray(),
                "the GDPR's statements in run 16 carry no end of validity, so none is held, and none is said");
            Assert.AreEqual(0, forceDates.GetProperty("conflicts").GetArrayLength());
            Assert.AreEqual(V3CorpusMount.EuropeForceDateRoleRule, forceDates.GetProperty("role_rule").GetString());
            var notHeld = body.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "force_dates");
            Assert.AreEqual(V3CorpusMount.EuropeForceDatesNotHeldWhenHeld, notHeld.GetProperty("reason").GetString());

            var said = forceDates.GetRawText();
            first ??= said;
            Assert.AreEqual(first, said, $"{operation}: one act, one set of dates, in every answer");
        }

        // The dates choose no wording: the date after the application date answers the act's only wording, as before.
        var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = Celex, date = "2018-06-01" });
        Assert.AreEqual("2016-04-27", asOf.Result!.Value.GetProperty("states")[0].GetProperty("wording").GetProperty("wording_date").GetString());
        var before = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = Celex, date = "2016-01-01" });
        Assert.AreEqual("no_version_for_date", before.Refusal?.Code, "a refusal says no force date");
        Assert.DoesNotContain("force_dates", before.Refusal!.HelpfulPayload.GetRawText());
    }

    [TestMethod]
    public async Task AnActWhoseStatementsCarryNoDateSaysEveryNamedRoleIsNotHeld()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (operation, parameters) in Answers)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters);
            var forceDates = envelope.Result!.Value.GetProperty("force_dates");
            Assert.AreEqual(0, forceDates.GetProperty("dates").GetArrayLength(), operation);
            CollectionAssert.AreEqual(new[] { "entry_into_force", "application", "end_of_validity", "deadline" },
                forceDates.GetProperty("roles_not_held").EnumerateArray().Select(static role => role.GetString()).ToArray(), operation);
            StringAssert.Contains(forceDates.GetProperty("not_held_rule").GetString(), "no statement that the act has no such date");
        }
    }

    [TestMethod]
    public void TheNoteCodesAreTheNoteWithEachCarrierWrittenAsItsCodeAndNothingElseChanged()
    {
        Assert.AreEqual("DATPUB +20 V ART 99", V3CorpusMount.EuropeNoteCodes(EuForceDatesFixture.Gdpr()[0].Note));
        Assert.AreEqual("FRA_SXB", V3CorpusMount.EuropeNoteCodes("{FRA_SXB|http://publications.europa.eu/resource/authority/place/FRA_SXB}"));
        Assert.AreEqual("Strasbourg", V3CorpusMount.EuropeNoteCodes("Strasbourg"));
        Assert.AreEqual("{not a carrier} MA/PART 55 L 4", V3CorpusMount.EuropeNoteCodes("{not a carrier} {MA/PART|http://publications.europa.eu/resource/authority/fd_335/MA%2FPART} 55 L 4"));
        Assert.IsNull(V3CorpusMount.EuropeNoteCodes(null));
    }
}
