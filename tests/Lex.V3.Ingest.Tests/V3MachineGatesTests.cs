using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Two of the launch contract's machine gates, run against the real handler on a mounted corpus: the temporal
/// case set at 100 percent (a dated request never receives a different date silently: <c>as_of</c> selects the one
/// state that applies, or refuses <c>no_version_for_date</c> or <c>ambiguous_version</c> rather than choose) and the
/// refusal case set at 100 percent (every absence is typed: each request that must be refused is refused with its
/// registry code), each with the shuffled control that proves the harness would notice if it were wrong.
/// </summary>
/// <remarks>
/// <para>
/// The harness is the one in <c>Lex.V3.Contracts.Evaluation</c> (<see cref="TemporalEvaluation"/>,
/// <see cref="VerdictEvaluation"/>, <see cref="ShuffledControls"/>), which until now nothing outside its own tests
/// called. The arms are the served operations, asked through <see cref="V3ApiHandler"/> as a client asks them; a
/// refusal is the key <c>refusal:{code}</c>, a selected state its digest. The cases are the fixture's, so the gates
/// prove the path, not a corpus: the first real mount is still blocked.
/// </para>
/// <para>
/// The date control shifts every case forward by an interval the corpus holds and requires every expectation to
/// break. A case at or after the latest held state can never break under a forward shift (the same state still
/// applies), which is the control's own design, so it runs over the cases before that state; the gate itself runs
/// over all of them.
/// </para>
/// </remarks>
[TestClass]
public sealed class V3MachineGatesTests
{
    private const int Spacing = 400;
    private const ulong Seed = 20260930;

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task TheTemporalCaseSetPassesAtOneHundredPercentAndTheDateShiftControlCatchesAShiftedSet()
    {
        // One work in three held dates: the fixture's own state, two states on one later date (ambiguous), and one
        // latest state held singly, so every region a date can fall in is represented.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var first = fixture.ApplicabilityDate;
        var twinDate = Shift(first, Spacing);
        var latestDate = Shift(first, 2 * Spacing);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        var latest = await fixture.AddStateAsync(latestDate, "latest");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        const string NoVersion = "refusal:no_version_for_date";
        const string Ambiguous = "refusal:ambiguous_version";
        var cases = new[]
        {
            new TemporalCase("before-history", fixture.WorkKey, Day(Shift(first, -1)), NoVersion),
            new TemporalCase("first-day", fixture.WorkKey, Day(first), fixture.StateSha256),
            new TemporalCase("inside-first", fixture.WorkKey, Day(Shift(first, 100)), fixture.StateSha256),
            new TemporalCase("last-day-of-first", fixture.WorkKey, Day(Shift(twinDate, -1)), fixture.StateSha256),
            new TemporalCase("twin-day", fixture.WorkKey, Day(twinDate), Ambiguous),
            new TemporalCase("inside-twins", fixture.WorkKey, Day(Shift(twinDate, 100)), Ambiguous),
            new TemporalCase("latest-day", fixture.WorkKey, Day(latestDate), latest.StateSha256),
            new TemporalCase("after-latest", fixture.WorkKey, Day(Shift(latestDate, 1000)), latest.StateSha256),
        };

        var arm = TemporalArm(mount);
        var report = TemporalEvaluation.Evaluate(cases, arm, floor: cases.Length);
        Assert.AreEqual(1.0, report.Exactness.Value, "every dated request selects exactly its state or refuses as it must.");
        Assert.AreEqual(GateVerdict.Pass, report.Gate.Verdict);
        Assert.AreEqual(EvaluationGateNames.TemporalExactness, report.Gate.Name);

        var beforeLatest = cases.Where(value => value.AsOf < Day(latestDate)).ToArray();
        var control = ShuffledControls.DateShuffle(
            beforeLatest, arm, (set, run) => TemporalEvaluation.Evaluate(set, run, floor: set.Count), [Spacing], Seed);
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, control.Verdict, control.Reason);
    }

    [TestMethod]
    public async Task TheRefusalCaseSetPassesAtOneHundredPercentAndTheVerdictShuffleControlCatchesAShuffledSet()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var twinDate = Shift(fixture.ApplicabilityDate, Spacing);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";

        // Each case is a request a client can send and the one code the registry says answers it; "answer" where the
        // request must be answered, so the set also holds that a refusal is not the default.
        var requests = new Dictionary<string, (string Operation, object Parameters, string Gold)>(StringComparer.Ordinal)
        {
            ["unknown-work"] = ("as_of", new { identifier = "/lu-legilux/no-such-work", date = fixture.ApplicabilityDate }, "identifier_unknown"),
            ["language-not-held"] = ("dossier", new { identifier = work, language = "eng" }, "language_not_available"),
            ["before-history"] = ("as_of", new { identifier = work, date = Shift(fixture.ApplicabilityDate, -1), language = "fra" }, "no_version_for_date"),
            ["twin-states"] = ("as_of", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version"),
            ["mode-not-held"] = ("search", new { query = "loyer", language = "fra", mode = "bm25" }, "retrieval_mode_unavailable"),
            ["foreign-cursor"] = ("events", new { after = new string('0', 64) + ":1" }, "snapshot_unknown"),
            ["format-not-held"] = ("manifestation", new { identifier = work, format = "docx" }, "format_not_available"),
            ["eu-work-no-eu-index"] = ("search", new { query = "x", language = "eng", identifier = "32016R0679" }, "no_corpus_mounted"),
            ["a-held-state"] = ("as_of", new { identifier = work, date = fixture.ApplicabilityDate, language = "fra" }, "answer"),
            ["coverage"] = ("coverage", new { }, "answer"),
        };
        var cases = requests.Select(static pair => new VerdictCase(pair.Key, pair.Value.Gold)).ToArray();
        VerdictArm arm = caseId =>
        {
            var (operation, parameters, _) = requests[caseId];
            var envelope = EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters).GetAwaiter().GetResult();
            return envelope.Refusal?.Code ?? "answer";
        };

        var report = VerdictEvaluation.Evaluate(cases, arm, floor: cases.Length);
        Assert.AreEqual(1.0, report.ExactMatch.Value, "every request is refused with its own code, or answered where it must be.");
        Assert.AreEqual(GateVerdict.Pass, report.Gate.Verdict);
        CollectionAssert.IsSubsetOf(
            cases.Select(static value => value.GoldVerdict).Where(static gold => gold != "answer").ToArray(),
            V3OperationRegistry.Reviewed.RefusalCodes.ToArray(),
            "every gold refusal is a registry code.");

        var control = ShuffledControls.VerdictShuffle(cases, arm, (set, run) => VerdictEvaluation.Evaluate(set, run, floor: set.Count), Seed);
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, control.Verdict, control.Reason);
    }

    private static TemporalArm TemporalArm(V3CorpusMount mount) => (workKey, asOf) =>
    {
        var envelope = EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new
        {
            identifier = $"/lu-legilux/{workKey}",
            date = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            language = "fra",
        }).GetAwaiter().GetResult();
        if (envelope.Refusal is { } refusal)
        {
            return "refusal:" + refusal.Code;
        }

        var states = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        return states.Length == 1 ? states[0].GetProperty("state_sha256").GetString() : null;
    };
}
