using System.Globalization;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.LuxembourgIndexBuilderTests;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
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
/// holds, and with no language for every work. A request with no language selects in every language the work
/// holds (the mount's rule): <c>ambiguous_version</c> if any language's date holds two states,
/// <c>no_version_for_date</c> if no language holds a state yet, and otherwise the state of each language that
/// holds one. So a work's timelines together say what it must select, the key being the sorted set of those
/// states (review of #838: the first version left multilingual works out of the no-language arms).
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

    /// <summary>
    /// One work's timelines in every language it is held in, and what a request with no language must select:
    /// the ambiguity if any language is ambiguous, nothing before every language's first date, and otherwise
    /// the sorted states of the languages that hold one.
    /// </summary>
    internal sealed record WorkTimeline(string WorkKey, IReadOnlyList<Timeline> Languages)
    {
        public IReadOnlyList<DateOnly> Dates =>
            Languages.SelectMany(static language => language.Dates.Select(static date => date.Date)).Distinct().Order().ToArray();

        public string Expected(DateOnly day)
        {
            var each = Languages.Select(language => language.Expected(day)).ToArray();
            if (each.Contains(Ambiguous, StringComparer.Ordinal))
            {
                return Ambiguous;
            }

            var states = each.Where(static value => value != NoVersion).Order(StringComparer.Ordinal).ToArray();
            return states.Length == 0 ? NoVersion : string.Join('+', states);
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
    internal static IReadOnlyList<TemporalCase> Cases(Timeline timeline) =>
        Cases($"{timeline.WorkKey}/{timeline.Language}", timeline.WorkKey, timeline.Dates.Select(static date => date.Date).ToArray(), timeline.Expected);

    /// <summary>The cases of a request with no language, over the dates of every language the work is held in.</summary>
    internal static IReadOnlyList<TemporalCase> Cases(WorkTimeline work) =>
        Cases($"{work.WorkKey}/any", work.WorkKey, work.Dates, work.Expected);

    private static IReadOnlyList<TemporalCase> Cases(string id, string workKey, IReadOnlyList<DateOnly> dates, Func<DateOnly, string> expected)
    {
        var days = new List<(string Label, DateOnly Day)> { ("before-first", dates[0].AddDays(-1)) };
        for (var at = 0; at < dates.Count; at++)
        {
            var date = dates[at];
            days.Add(($"on-{date:yyyy-MM-dd}", date));
            if (at + 1 < dates.Count)
            {
                var next = dates[at + 1];
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

        days.Add(("after-latest", dates[^1].AddDays(AfterLatestDays)));
        return days.Select(value => new TemporalCase($"{id}/{value.Label}", workKey, value.Day, expected(value.Day))).ToArray();
    }

    /// <summary>
    /// <c>as_of</c> asked with no language, keyed as a work timeline keys it: a refusal by its code, an answer by the
    /// sorted states it serves (one per language that holds one).
    /// </summary>
    internal static TemporalArm DefaultAsOfArm(V3CorpusMount mount) => (workKey, asOf) =>
    {
        var envelope = EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = $"/lu-legilux/{workKey}", date = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
        return envelope.Refusal is { } refusal
            ? "refusal:" + refusal.Code
            : string.Join('+', envelope.Result!.Value.GetProperty("states").EnumerateArray()
                .Select(static state => state.GetProperty("state_sha256").GetString()!).Order(StringComparer.Ordinal));
    };

    /// <summary><c>in_force_on</c> for one work with no language, keyed the same way; a row without its one state is a defect (null).</summary>
    internal static TemporalArm DefaultInForceOnArm(V3CorpusMount mount) => (workKey, asOf) =>
    {
        var envelope = EnvelopeAsync(mount, "/api/v3/in_force_on", "in_force_on", new { identifier = $"/lu-legilux/{workKey}", date = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
        if (envelope.Refusal is { } refusal)
        {
            return "refusal:" + refusal.Code;
        }

        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        return rows.Any(static row => row.GetProperty("state").ValueKind != System.Text.Json.JsonValueKind.Object)
            ? null
            : string.Join('+', rows.Select(static row => row.GetProperty("state").GetProperty("state_sha256").GetString()!).Order(StringComparer.Ordinal));
    };

    /// <summary>The date control's one interval: the median gap between a timeline's held dates, or a year when none has two.</summary>
    internal static int ShiftDays(IReadOnlyList<Timeline> timelines)
    {
        var gaps = timelines.SelectMany(static timeline => timeline.Dates.Zip(timeline.Dates.Skip(1), static (a, b) => b.Date.DayNumber - a.Date.DayNumber))
            .Order().ToArray();
        return gaps.Length == 0 ? 365 : Math.Max(1, gaps[gaps.Length / 2]);
    }

    /// <summary>
    /// The temporal gate over a mount: one card set per arm, each over the cases of its language, or of every
    /// work with no language, with its date control.
    /// </summary>
    internal static EvaluationCardSet[] RunTemporalGate(V3CorpusMount mount, string mountDirectory) =>
        RunTemporalGate(mount, Sample(Timelines(mountDirectory), WorkSample, Seed));

    internal static EvaluationCardSet[] RunTemporalGate(V3CorpusMount mount, IReadOnlyList<Timeline> timelines)
    {
        var shift = ShiftDays(timelines);
        var arms = new List<(string Name, TemporalArm Arm, IReadOnlyList<TemporalCase> Cases, Func<string, DateOnly, string> ExpectedAt)>();
        foreach (var language in timelines.Select(static value => value.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var ofLanguage = timelines.Where(value => value.Language == language).ToDictionary(static value => value.WorkKey, StringComparer.Ordinal);
            var cases = ofLanguage.Values.SelectMany(Cases).ToArray();
            string ExpectedAt(string work, DateOnly day) => ofLanguage[work].Expected(day);
            arms.Add(($"as_of in {language}", V3MachineGatesTests.AsOfArm(mount, language), cases, ExpectedAt));
            arms.Add(($"in_force_on in {language}", V3MachineGatesTests.InForceOnArm(mount, language), cases, ExpectedAt));
        }

        var works = timelines.GroupBy(static value => value.WorkKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => new WorkTimeline(group.Key, group.ToArray()), StringComparer.Ordinal);
        var workCases = works.Values.SelectMany(Cases).ToArray();
        string WorkExpectedAt(string work, DateOnly day) => works[work].Expected(day);
        arms.Add(("as_of with no language", DefaultAsOfArm(mount), workCases, WorkExpectedAt));
        arms.Add(("in_force_on with no language", DefaultInForceOnArm(mount), workCases, WorkExpectedAt));

        var sets = new List<EvaluationCardSet>();
        foreach (var (name, arm, cases, expectedAt) in arms)
        {
            var report = TemporalEvaluation.Evaluate(cases, arm, floor: Math.Max(1, cases.Count));
            var controlCases = cases.Where(value => expectedAt(value.WorkKey, value.AsOf.AddDays(shift)) != value.ExpectedStateKey).ToArray();
            var control = cases.Count == 0
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
    public async Task AMultilingualWorkIsHeldToEveryLanguageOnARequestWithNoLanguage()
    {
        // The fixture's French state, and a German one 200 days later: with no language, the work must select
        // French alone until the German date and both from it (review of #838: the first version left such a
        // work out of the no-language arms).
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var first = Day(fixture.ApplicabilityDate);
        var german = await fixture.AddSecondLanguageStateAsync(first.AddDays(200).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var timelines = Timelines(fixture.Directory);
        CollectionAssert.AreEqual(new[] { "deu", "fra" }, timelines.Select(static value => value.Language).ToArray());
        var work = new WorkTimeline(fixture.WorkKey, timelines);
        Assert.AreEqual(fixture.StateSha256, work.Expected(first.AddDays(100)), "French alone before the German date");
        Assert.AreEqual(string.Join('+', new[] { fixture.StateSha256, german.StateSha256 }.Order(StringComparer.Ordinal)), work.Expected(first.AddDays(300)), "both from it");

        var sets = RunTemporalGate(mount, timelines);
        CollectionAssert.AreEqual(
            new[] { "as_of in deu", "in_force_on in deu", "as_of in fra", "in_force_on in fra", "as_of with no language", "in_force_on with no language" },
            sets.Select(static set => set.Arm).ToArray());
        foreach (var set in sets)
        {
            Assert.IsTrue(set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"{set.Arm}: {set.CaseCount} cases");
            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{set.Arm}: {set.Control.Reason}");
        }

        Assert.AreEqual(6, sets.Single(static set => set.Arm == "as_of with no language").CaseCount, "the no-language arm holds the multilingual work: both dates, inside, last day, before and after");
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
