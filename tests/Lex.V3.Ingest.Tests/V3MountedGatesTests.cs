using System.Globalization;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.LuxembourgIndexBuilderTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The machine gates over a mounted corpus whose states no test built (ruling 2: the launch card carries
/// machine gates run over the real mounted corpus, not fixture-only scores). The cases are derived from
/// the mount's own Luxembourg index, so the gates run over whatever mount they are given: the fixture on
/// every run, and the mount <c>V3_EVALUATE_MOUNT</c> names when it names one (the release rehearsal's).
/// </summary>
/// <remarks>
/// <para>
/// Temporal: each work's held states in one language, grouped by applicability date, are a timeline, and the
/// timeline says what a dated request must select on any day: nothing before the first date
/// (<c>no_version_for_date</c>), the one state of the latest date on or before the day, or
/// <c>ambiguous_version</c> where two states share that date. The cases are the day before the first date,
/// each date, a day inside and the last day of each window, and a day long after the latest date, for a
/// seeded sample of works. They run through <c>as_of</c> and <c>in_force_on</c> in each language the sample
/// holds, and with no language for the works held in one language only (with several, the default request
/// serves every language, so it selects no single state).
/// </para>
/// <para>
/// The date control shifts every case forward by one interval, the median gap between held dates, and must
/// break every expectation. It runs over the cases whose expected selection the timeline says the shift
/// changes, which always includes each day before a first date; a case the shift cannot change is outside
/// the control by its design, as in the fixture's set. A mount with no Luxembourg state has no temporal
/// case, and its gate is not measured (<c>no_measurable_query</c>): an empty stratum is reported, never
/// scored.
/// </para>
/// </remarks>
[TestClass]
public sealed class V3MountedGatesTests
{
    private const string MountVariable = "V3_EVALUATE_MOUNT";
    private const string CardOutVariable = "V3_EVALUATION_CARD_OUT";
    private const ulong Seed = 20260930;
    private const int WorkSample = 40;
    private const int AfterLatestDays = 1000;
    private const string NoVersion = "refusal:no_version_for_date";
    private const string Ambiguous = "refusal:ambiguous_version";

    /// <summary>One work's held states in one language, by applicability date: what a dated request must select.</summary>
    internal sealed record Timeline(string WorkKey, string Language, IReadOnlyList<(DateOnly Date, IReadOnlyList<string> States)> Dates)
    {
        public string Expected(DateOnly day)
        {
            var held = Dates.LastOrDefault(value => value.Date <= day);
            return held.States is null ? NoVersion : held.States.Count == 1 ? held.States[0] : Ambiguous;
        }
    }

    /// <summary>The timelines of a mount's Luxembourg index, per work and language.</summary>
    internal static IReadOnlyList<Timeline> Timelines(string mountDirectory)
    {
        var index = Path.Combine(mountDirectory, V3CorpusMount.IndexFileName);
        if (!File.Exists(index))
        {
            return [];
        }

        using var connection = LuxembourgIndexBuilder.Open(index, SqliteOpenMode.ReadOnly);
        return ReadStates(connection)
            .GroupBy(static row => (row.WorkKey, row.Language))
            .OrderBy(static group => group.Key.WorkKey, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Language, StringComparer.Ordinal)
            .Select(static group => new Timeline(group.Key.WorkKey, group.Key.Language, group
                .GroupBy(static row => Day(row.ApplicabilityDate))
                .OrderBy(static date => date.Key)
                .Select(static date => (date.Key, (IReadOnlyList<string>)date.Select(static row => row.StateSha256).Order(StringComparer.Ordinal).ToArray()))
                .ToArray()))
            .ToArray();
    }

    /// <summary>A seeded sample of the works, all of each chosen work's languages kept together.</summary>
    internal static IReadOnlyList<Timeline> Sample(IReadOnlyList<Timeline> timelines, int works, ulong seed)
    {
        var keys = timelines.Select(static value => value.WorkKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var random = new SplitMix64(seed);
        for (var at = keys.Length - 1; at > 0; at--)
        {
            var other = random.NextBelow(at + 1);
            (keys[at], keys[other]) = (keys[other], keys[at]);
        }

        var chosen = keys.Take(works).ToHashSet(StringComparer.Ordinal);
        return timelines.Where(value => chosen.Contains(value.WorkKey)).ToArray();
    }

    /// <summary>The temporal cases a timeline gives, each with what the timeline says it must select.</summary>
    internal static IReadOnlyList<TemporalCase> Cases(Timeline timeline)
    {
        var days = new List<(string Label, DateOnly Day)> { ("before-first", timeline.Dates[0].Date.AddDays(-1)) };
        for (var at = 0; at < timeline.Dates.Count; at++)
        {
            var date = timeline.Dates[at].Date;
            days.Add(($"on-{date:yyyy-MM-dd}", date));
            if (at + 1 < timeline.Dates.Count)
            {
                var next = timeline.Dates[at + 1].Date;
                var gap = next.DayNumber - date.DayNumber;
                if (gap > 2)
                {
                    days.Add(($"inside-{date:yyyy-MM-dd}", date.AddDays(gap / 2)));
                }

                if (gap > 1)
                {
                    days.Add(($"last-day-{date:yyyy-MM-dd}", next.AddDays(-1)));
                }
            }
        }

        days.Add(("after-latest", timeline.Dates[^1].Date.AddDays(AfterLatestDays)));
        return days.Select(value => new TemporalCase(
            $"{timeline.WorkKey}/{timeline.Language}/{value.Label}", timeline.WorkKey, value.Day, timeline.Expected(value.Day))).ToArray();
    }

    /// <summary>The date control's one interval: the median gap between a timeline's held dates, or a year when none has two.</summary>
    internal static int ShiftDays(IReadOnlyList<Timeline> timelines)
    {
        var gaps = timelines.SelectMany(static timeline => timeline.Dates.Zip(timeline.Dates.Skip(1), static (a, b) => b.Date.DayNumber - a.Date.DayNumber))
            .Order().ToArray();
        return gaps.Length == 0 ? 365 : Math.Max(1, gaps[gaps.Length / 2]);
    }

    /// <summary>
    /// The temporal gate over a mount: one card set per arm, each over the cases of its language (or of the
    /// single-language works, with no language), with its date control.
    /// </summary>
    internal static EvaluationCardSet[] RunTemporalGate(V3CorpusMount mount, string mountDirectory) =>
        RunTemporalGate(mount, Sample(Timelines(mountDirectory), WorkSample, Seed));

    internal static EvaluationCardSet[] RunTemporalGate(V3CorpusMount mount, IReadOnlyList<Timeline> timelines)
    {
        var shift = ShiftDays(timelines);
        var languagesOfWork = timelines.GroupBy(static value => value.WorkKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var arms = new List<(string Name, TemporalArm Arm, IReadOnlyList<Timeline> Timelines)>();
        foreach (var language in timelines.Select(static value => value.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var ofLanguage = timelines.Where(value => value.Language == language).ToArray();
            arms.Add(($"as_of in {language}", V3MachineGatesTests.AsOfArm(mount, language), ofLanguage));
            arms.Add(($"in_force_on in {language}", V3MachineGatesTests.InForceOnArm(mount, language), ofLanguage));
        }

        var single = timelines.Where(value => languagesOfWork[value.WorkKey] == 1).ToArray();
        arms.Add(("as_of with no language", V3MachineGatesTests.AsOfArm(mount, null), single));
        arms.Add(("in_force_on with no language", V3MachineGatesTests.InForceOnArm(mount, null), single));

        var sets = new List<EvaluationCardSet>();
        foreach (var (name, arm, armTimelines) in arms)
        {
            var cases = armTimelines.SelectMany(Cases).ToArray();
            var report = TemporalEvaluation.Evaluate(cases, arm, floor: Math.Max(1, cases.Length));
            var byCase = armTimelines.ToDictionary(static value => value.WorkKey, StringComparer.Ordinal);
            var controlCases = cases.Where(value => byCase[value.WorkKey].Expected(value.AsOf.AddDays(shift)) != value.ExpectedStateKey).ToArray();
            var control = cases.Length == 0
                ? new ControlResult(ShuffledControlNames.DateShuffle, ControlVerdict.NotApplicable, "there is no temporal case to shift: the mount holds no Luxembourg state for this arm", Seed)
                : ShuffledControls.DateShuffle(
                    controlCases, arm, (set, run) => TemporalEvaluation.Evaluate(set, run, floor: Math.Max(1, set.Count)), [shift], Seed);
            sets.Add(EvaluationCard.Temporal(
                name, cases, report, controlCases, control,
                $"the control shifts every case forward by {shift} days, the median gap between held dates; a case the shift cannot change (its timeline selects the same after it) is outside the control by its design"));
        }

        return [.. sets];
    }

    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public void ATimelineSaysWhatEveryDayMustSelect()
    {
        var timeline = new Timeline("w", "fra", [
            (Day("2020-01-01"), ["a"]),
            (Day("2021-01-01"), ["b", "c"]),
            (Day("2022-06-01"), ["d"]),
        ]);
        Assert.AreEqual(NoVersion, timeline.Expected(Day("2019-12-31")));
        Assert.AreEqual("a", timeline.Expected(Day("2020-01-01")));
        Assert.AreEqual("a", timeline.Expected(Day("2020-12-31")));
        Assert.AreEqual(Ambiguous, timeline.Expected(Day("2021-01-01")));
        Assert.AreEqual(Ambiguous, timeline.Expected(Day("2022-05-31")));
        Assert.AreEqual("d", timeline.Expected(Day("2030-01-01")));

        var cases = Cases(timeline);
        CollectionAssert.AreEqual(
            new[] { NoVersion, "a", "a", "a", Ambiguous, Ambiguous, Ambiguous, "d", "d" },
            cases.Select(static value => value.ExpectedStateKey).ToArray(),
            "before the first date, each date with a day inside and its last day, and long after the latest");
        Assert.AreEqual(9, cases.Select(static value => value.CaseId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(516, ShiftDays([timeline]), "the median gap between held dates (366 and 516 days: the upper middle of an even count)");
    }

    [TestMethod]
    public async Task TheTemporalGateDerivedFromTheMountPassesOnTheFixtureAndItsDateControlCatchesTheShift()
    {
        // The fixture's own state, two states on one later date and a latest state: the cases are derived from
        // the index the fixture writes, not written by the test.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var first = Day(fixture.ApplicabilityDate);
        await fixture.AddStateAsync(first.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "twin-a");
        await fixture.AddStateAsync(first.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "twin-b");
        await fixture.AddStateAsync(first.AddDays(800).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "latest");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var timelines = Timelines(fixture.Directory);
        Assert.AreEqual(1, timelines.Count, "one work in one language");
        Assert.AreEqual(3, timelines[0].Dates.Count);
        var sets = RunTemporalGate(mount, fixture.Directory);
        Assert.AreEqual(4, sets.Length, "as_of and in_force_on, in fra and with no language");
        foreach (var set in sets)
        {
            Assert.IsTrue(set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"{set.Arm}: every derived case selects exactly its state or refuses as it must");
            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{set.Arm}: {set.Control.Reason}");
            Assert.AreEqual(9, set.CaseCount);
        }
    }

    [TestMethod]
    public async Task AMountWithNoLuxembourgStateHasItsTemporalGateNotMeasuredAndSaysWhy()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var sets = RunTemporalGate(mount, Array.Empty<Timeline>());
        CollectionAssert.AreEqual(new[] { "as_of with no language", "in_force_on with no language" }, sets.Select(static set => set.Arm).ToArray());
        foreach (var set in sets)
        {
            Assert.AreEqual(0, set.CaseCount);
            Assert.AreEqual(GateVerdict.NotMeasured, set.Gates.Single().Verdict, "an empty stratum is reported, never scored");
            Assert.AreEqual(NotMeasuredReason.NoMeasurableQuery, set.Gates.Single().Reason);
            Assert.AreEqual(ControlVerdict.NotApplicable, set.Control.Verdict);
            StringAssert.Contains(set.Control.Reason, "no temporal case to shift");
        }
    }

    [TestMethod]
    public async Task TheTemporalGateOverTheMountTheReleaseNames()
    {
        var directory = Environment.GetEnvironmentVariable(MountVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive($"{MountVariable} names no mount, so the gates ran over the fixture only.");
        }

        using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
        Assert.IsNotNull(mount, $"{directory} does not mount.");
        var sets = RunTemporalGate(mount, directory);
        var output = Environment.GetEnvironmentVariable(CardOutVariable);
        if (!string.IsNullOrWhiteSpace(output))
        {
            var corpus = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName))));
            var target = $"the mount whose corpus file is sha256:{corpus}, asked through the real handler; the temporal cases are derived from its own Luxembourg index";
            await File.WriteAllTextAsync(output, EvaluationCard.ToText(EvaluationCard.Render(target, sets)));
        }

        foreach (var set in sets)
        {
            var gate = set.Gates.Single();
            Assert.AreNotEqual(GateVerdict.Fail, gate.Verdict, $"{set.Arm}: a derived case selected another state than its timeline says");
            if (gate.Verdict == GateVerdict.Pass)
            {
                Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{set.Arm}: {set.Control.Reason}");
            }
        }
    }
}
