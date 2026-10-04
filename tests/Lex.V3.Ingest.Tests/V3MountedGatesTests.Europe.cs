using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The temporal set's EU arms over a mount, derived from its EU index's states table (schema 5): every work the publisher's
/// census discovered for a seed act, the original and each consolidated version, with the publisher's consolidation date or a
/// typed reason it has none. The EU time view's stated rule (<c>EuropeSelectionRule</c>) is restated here from the tables, so the
/// cases follow from the mount's data and not from the API's code. Per language: a wording's date is the original act's one
/// Formex date (the same in every language it is held in) or the version's consolidation date, and a version with neither is
/// undated. A date whose works hold no text in the language is refused <c>text_not_available</c>; one whose held texts differ is
/// <c>ambiguous_version</c>, never a choice; one whose held texts agree is answered by the publisher's designation (the original,
/// else the work whose CELEX names that version, else the first work); and an undated version holding a text that differs from
/// the answer makes the date ambiguous. A wording is keyed by its kind and hash-pinned permalink, its digest recomputed by the
/// stated rule. A request with no language is ambiguous if any language is, else answered by every language that answers, else
/// <c>text_not_available</c> if a language's wording of the date holds no text, else not yet answered.
/// </summary>
/// <remarks>
/// The cases and the date control are the Luxembourg set's, over a seeded sample of the acts. EU <c>in_force_on</c> has no arm: it
/// is a typed refusal the EU capability manifest states, the same on every date, so no date shift could break it; the refusal
/// set asks it. A mount with no EU index, or whose EU index has no states table, has no EU temporal case, and its one EU arm is
/// not measured (<c>no_measurable_query</c>).
/// </remarks>
public sealed partial class V3MountedGatesTests
{
    private const string TextNotAvailable = "refusal:text_not_available";

    /// <summary>One work of an act's census as the states table holds it: the act, the work and its CELEX, and what dates it.</summary>
    private sealed record EuropeCensusWork(string Act, string Work, string? Celex, bool Original, string? ConsolidationDate);

    /// <summary>
    /// One EU act's timelines in every language it holds a dated wording in, and what a request with no language must select (the
    /// time view's order): the ambiguity if any language is ambiguous; else the sorted wordings of the languages that answer; else
    /// <c>text_not_available</c> if a language's wording of the date holds no text; else nothing yet.
    /// </summary>
    internal sealed record EuropeActTimeline(string WorkKey, IReadOnlyList<Timeline> Languages)
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

            var answers = each.Where(static value => !value.StartsWith("refusal:", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            if (answers.Length > 0)
            {
                return string.Join('+', answers);
            }

            return each.Contains(TextNotAvailable, StringComparer.Ordinal) ? TextNotAvailable : NoVersion;
        }
    }

    /// <summary>
    /// The timelines of a mount's EU index, one per census act and language that holds a dated wording, keyed by the act's seed
    /// CELEX (the identifier EU <c>as_of</c> answers). Each date selects the wording's key; <c>text_not_available</c> where no text
    /// of it is held; or two entries or more where it is ambiguous (each held text's key where they differ, or the answer's key and
    /// each undated expression whose text differs from it). Empty when the mount holds no EU index or its index has no states table.
    /// </summary>
    internal static IReadOnlyList<Timeline> EuropeTimelines(string mountDirectory)
    {
        var path = Path.Combine(mountDirectory, V3CorpusMount.EuropeIndexFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        using var connection = EuropeIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
        if (!HasStatesTable(connection))
        {
            return [];
        }

        var census = new List<EuropeCensusWork>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT seed_celex, publisher_work_iri, publisher_work_celex, date_status, publisher_consolidation_date FROM states ORDER BY seed_celex, publisher_work_iri";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                census.Add(new EuropeCensusWork(
                    reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3) == (int)EuropeIndexStateDateStatus.OriginalWording, reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        // The expressions each work holds text of, by language: what the time view calls held.
        var expressions = EuropeRows(connection, "SELECT DISTINCT publisher_work_id, publisher_expression_id, language FROM articles")
            .Select(static row => (Work: row[0]!, Expression: row[1]!, Language: row[2]!))
            .ToArray();
        string[] HeldOf(EuropeCensusWork work, string language) => expressions
            .Where(expression => expression.Work == work.Work && expression.Language == language)
            .Select(static expression => expression.Expression)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // An expression's held text, article by article in the publisher's order: what "the same text" compares, read only where
        // two texts are compared.
        var texts = new Dictionary<string, (string Provision, string Text)[]>(StringComparer.Ordinal);
        bool SameText(string expression, string other) => TextOf(expression).SequenceEqual(TextOf(other));
        (string Provision, string Text)[] TextOf(string expression)
        {
            if (!texts.TryGetValue(expression, out var text))
            {
                texts[expression] = text = EuropeRows(connection,
                        "SELECT publisher_identifier, searchable_text FROM articles WHERE publisher_expression_id = $expression ORDER BY publisher_identifier, article_identity_sha256",
                        ("$expression", expression))
                    .Select(static row => (row[0]!, row[1]!))
                    .ToArray();
            }

            return text;
        }

        string KeyOf(string act, EuropeCensusWork work, string expression, string language, string date)
        {
            var identities = EuropeRows(connection,
                    "SELECT article_identity_sha256 FROM articles WHERE publisher_expression_id = $expression ORDER BY publisher_identifier, article_identity_sha256",
                    ("$expression", expression))
                .Select(static row => row[0]!);
            return $"{(work.Original ? "original_wording" : "consolidated_version")}:/eu-eurlex/{act}/{language}/{date}--" +
                V3EuropePermalinkTests.WordingSha256ByTheStatedRule(act, work.Work, expression, language, date, identities);
        }

        var timelines = new List<Timeline>();
        foreach (var act in census.GroupBy(static work => work.Act, StringComparer.Ordinal).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            // The original wording is dated by the one Formex date its held text bears, in every language; with none or several, it
            // is not placed in time.
            var root = act.FirstOrDefault(static work => work.Original);
            string[] rootDates = root is null
                ? []
                : EuropeRows(connection,
                        "SELECT DISTINCT wording_date FROM articles WHERE publisher_expression_id IN (SELECT publisher_expression_id FROM articles WHERE publisher_work_id = $work)",
                        ("$work", root.Work))
                    .Select(static row => row[0]!)
                    .ToArray();
            string? originalDate = rootDates.Length == 1 ? rootDates[0] : null;
            var languages = act
                .SelectMany(work => expressions.Where(expression => expression.Work == work.Work).Select(static expression => expression.Language))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (var language in languages)
            {
                var dated = new SortedDictionary<string, List<EuropeCensusWork>>(StringComparer.Ordinal);
                var undated = new List<string>();
                foreach (var work in act)
                {
                    var day = work.Original ? originalDate : work.ConsolidationDate;
                    if (day is null)
                    {
                        // Each expression a version with no usable date holds text of can make a date ambiguous.
                        undated.AddRange(HeldOf(work, language));
                    }
                    else if (dated.TryGetValue(day, out var sharing))
                    {
                        sharing.Add(work);
                    }
                    else
                    {
                        dated[day] = [work];
                    }
                }

                var entries = new List<(DateOnly Date, IReadOnlyList<string> States)>();
                foreach (var (date, works) in dated)
                {
                    var candidates = works.OrderBy(static work => work.Work, StringComparer.Ordinal)
                        .SelectMany(work => HeldOf(work, language).Select(expression => (Work: work, Expression: expression)))
                        .ToArray();
                    IReadOnlyList<string> selects;
                    if (candidates.Length == 0)
                    {
                        selects = [TextNotAvailable];
                    }
                    else if (candidates.Skip(1).Any(candidate => !SameText(candidate.Expression, candidates[0].Expression)))
                    {
                        selects = candidates.Select(candidate => KeyOf(act.Key, candidate.Work, candidate.Expression, language, date)).ToArray();
                    }
                    else
                    {
                        var designated = candidates.FirstOrDefault(candidate =>
                            candidate.Work.Original || candidate.Work.Celex == EuropeConsolidatedCelex(act.Key, date));
                        var answer = designated.Work is null ? candidates[0] : designated;
                        selects = [KeyOf(act.Key, answer.Work, answer.Expression, language, date), .. undated.Where(expression => !SameText(expression, answer.Expression))];
                    }

                    entries.Add((Day(date), selects));
                }

                if (entries.Count > 0)
                {
                    timelines.Add(new Timeline(act.Key, language, entries));
                }
            }
        }

        return timelines;
    }

    /// <summary>The CELEX the publisher gives a consolidated version of <paramref name="act"/> dated <paramref name="date"/>.</summary>
    internal static string EuropeConsolidatedCelex(string act, string date) =>
        "0" + act[1..] + "-" + date.Replace("-", string.Empty, StringComparison.Ordinal);

    /// <summary>Whether an EU index has a states table (schema 5), the census the EU time view answers from.</summary>
    private static bool HasStatesTable(SqliteConnection connection) =>
        EuropeRows(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'states'").Count > 0;

    /// <summary>The rows of a query over an EU index, its parameters bound by name, each column read as text or null.</summary>
    private static List<string?[]> EuropeRows(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<string?[]>();
        while (reader.Read())
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? null : reader.GetString(at)).ToArray());
        }

        return rows;
    }

    /// <summary>The cases of a request with no language, over the dates of every language the act holds a dated wording in.</summary>
    internal static IReadOnlyList<TemporalCase> Cases(EuropeActTimeline act) =>
        Cases($"{act.WorkKey}/any", act.WorkKey, act.Dates, act.Expected);

    /// <summary>The EU temporal gate over a mount, over a seeded sample of its census acts.</summary>
    internal static EvaluationCardSet[] RunEuropeTemporalGate(V3CorpusMount mount, string mountDirectory) =>
        RunEuropeTemporalGate(mount, Sample(EuropeTimelines(mountDirectory), WorkSample, Seed));

    /// <summary>
    /// The EU temporal gate: one card set per language, EU <c>as_of</c> over that language's cases, and one with no language over
    /// every act's, each with its date control. With no EU timeline, the arm with no language is the only one, and is not measured.
    /// </summary>
    internal static EvaluationCardSet[] RunEuropeTemporalGate(V3CorpusMount mount, IReadOnlyList<Timeline> timelines)
    {
        var shift = ShiftDays(timelines);
        var arms = new List<(string Name, TemporalArm Arm, IReadOnlyList<TemporalCase> Cases, Func<string, DateOnly, string> ExpectedAt)>();
        foreach (var language in timelines.Select(static value => value.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var ofLanguage = timelines.Where(value => value.Language == language).ToDictionary(static value => value.WorkKey, StringComparer.Ordinal);
            string ExpectedAt(string act, DateOnly day) => ofLanguage[act].Expected(day);
            arms.Add(($"EU as_of in {language}", V3MachineGatesTests.EuropeAsOfArm(mount, language), ofLanguage.Values.SelectMany(Cases).ToArray(), ExpectedAt));
        }

        var acts = timelines.GroupBy(static value => value.WorkKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => new EuropeActTimeline(group.Key, group.ToArray()), StringComparer.Ordinal);
        string ActExpectedAt(string act, DateOnly day) => acts[act].Expected(day);
        arms.Add(("EU as_of with no language", V3MachineGatesTests.EuropeAsOfArm(mount, null), acts.Values.SelectMany(Cases).ToArray(), ActExpectedAt));
        return TemporalSets(arms, shift, "there is no temporal case to shift: the mount's EU index holds no dated wording of a census act for this arm");
    }

    [TestMethod]
    public void AnEuTimelineSaysWhatEveryDayMustSelect()
    {
        // English: the original wording, two different texts on one later date, then a consolidation. French: the original
        // wording, and the consolidation's date with no French text held.
        var english = new Timeline("act", "eng", [
            (Day("2016-04-27"), ["o-eng"]),
            (Day("2017-06-01"), ["b-eng", "c-eng"]),
            (Day("2018-07-06"), ["e-eng"]),
        ]);
        var french = new Timeline("act", "fra", [
            (Day("2016-04-27"), ["o-fra"]),
            (Day("2018-07-06"), [TextNotAvailable]),
        ]);
        Assert.AreEqual(TextNotAvailable, french.Expected(Day("2020-01-01")), "a date whose wording holds no text in the language");

        var act = new EuropeActTimeline("act", [english, french]);
        Assert.AreEqual(NoVersion, act.Expected(Day("2016-04-26")), "before every language's first wording");
        Assert.AreEqual("o-eng+o-fra", act.Expected(Day("2016-04-27")), "every language that answers, sorted");
        Assert.AreEqual(Ambiguous, act.Expected(Day("2017-06-01")), "one ambiguous language makes the request ambiguous");
        Assert.AreEqual("e-eng", act.Expected(Day("2018-07-06")), "a language whose wording holds no text is left out of an answer");
        Assert.AreEqual(TextNotAvailable, new EuropeActTimeline("act", [french]).Expected(Day("2020-01-01")), "and refuses when no language answers");

        var cases = Cases(act);
        CollectionAssert.AreEqual(
            new[] { NoVersion, "o-eng+o-fra", "o-eng+o-fra", "o-eng+o-fra", Ambiguous, Ambiguous, Ambiguous, "e-eng", "e-eng" },
            cases.Select(static value => value.ExpectedStateKey).ToArray(),
            "before the first date, each date of either language with a day inside and its last day, and long after the latest");
        Assert.HasCount(9, cases.Select(static value => value.CaseId).Distinct(StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task TheEuTemporalGateDerivedFromTheStatesTableHoldsTheMachineGatesCasesAndPasses()
    {
        // The machine gates' EU fixture mount: the original wording, two versions of one later date whose texts differ, and a later
        // consolidation. The timelines are derived from the states table the build writes, not written by the test, and on every day
        // the machine gates' hand-written cases ask, they select what those cases expect.
        var (root, directory) = await V3MachineGatesTests.EuropeTemporalMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var timelines = EuropeTimelines(directory);
            CollectionAssert.AreEqual(new[] { "eng", "fra" }, timelines.Select(static value => value.Language).ToArray());
            foreach (var timeline in timelines)
            {
                Assert.AreEqual(EuFirstMountAcquisitionTests.ConsolidatedSeed, timeline.WorkKey);
                Assert.HasCount(3, timeline.Dates, $"{timeline.Language}: the original wording's date, the two versions' and the latest");
                foreach (var held in V3MachineGatesTests.EuropeTemporalCases(directory, timeline.Language))
                {
                    Assert.AreEqual(held.ExpectedStateKey, timeline.Expected(held.AsOf), $"{timeline.Language}, {held.CaseId}");
                }
            }

            var act = new EuropeActTimeline(EuFirstMountAcquisitionTests.ConsolidatedSeed, timelines);
            foreach (var held in V3MachineGatesTests.EuropeTemporalCases(directory, null))
            {
                Assert.AreEqual(held.ExpectedStateKey, act.Expected(held.AsOf), $"with no language, {held.CaseId}");
            }

            var sets = RunEuropeTemporalGate(mount, directory);
            CollectionAssert.AreEqual(
                new[] { "EU as_of in eng", "EU as_of in fra", "EU as_of with no language" }, sets.Select(static set => set.Arm).ToArray());
            foreach (var set in sets)
            {
                Assert.IsTrue(set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"{set.Arm}: every derived case selects exactly its wording or refuses as it must");
                Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{set.Arm}: {set.Control.Reason}");
                Assert.AreEqual(9, set.CaseCount, "before the first date, three dates each with a day inside and its last day but the latest, and long after it");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("one text held by two works of one date", "consolidated_version:")]
    [DataRow("no English text of the latest wording", TextNotAvailable)]
    [DataRow("an undated version holding another English text", Ambiguous)]
    public async Task TheEuTemporalGatePassesOnEachSelectionBranch(string branch, string englishOnTheLatestDate)
    {
        // The time view's selection branches (V3CorpusEuropeTimeViewBranchTests' mounts), each derived from the states table: the
        // work whose CELEX names the version answers for a text two works hold; a language that holds no text of the latest wording
        // refuses text_not_available there while the other answers; an undated version whose English text differs makes every
        // English date ambiguous, and French, which holds no text of it, still answers.
        var (root, directory) = await (branch switch
        {
            "one text held by two works of one date" => V3FirstMountBuildTests.ConsolidatedWorksMountAsync(
                new(V3FirstMountBuildTests.WorkB, V3FirstMountBuildTests.ConsolidationDate, V3FirstMountBuildTests.Designated, V3FirstMountBuildTests.EnglishPackage, V3FirstMountBuildTests.FrenchPackage),
                new(V3FirstMountBuildTests.WorkC, V3FirstMountBuildTests.ConsolidationDate, null, V3FirstMountBuildTests.EnglishPackage, V3FirstMountBuildTests.FrenchPackage),
                new(V3FirstMountBuildTests.WorkD, V3FirstMountBuildTests.ConsolidationDate, null, null, null)),
            "no English text of the latest wording" => V3FirstMountBuildTests.ConsolidatedWorksMountAsync(
                new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(
                    V3FirstMountBuildTests.WorkB, V3FirstMountBuildTests.ConsolidationDate, V3FirstMountBuildTests.Designated, null, V3FirstMountBuildTests.FrenchPackage)),
            "an undated version holding another English text" => V3FirstMountBuildTests.ConsolidatedWorksMountAsync(
                new(V3FirstMountBuildTests.WorkB, V3FirstMountBuildTests.ConsolidationDate, V3FirstMountBuildTests.Designated, V3FirstMountBuildTests.EnglishPackage, V3FirstMountBuildTests.FrenchPackage),
                new(V3FirstMountBuildTests.WorkC, null, null, V3FirstMountBuildTests.OtherEnglishPackage, null)),
            _ => throw new ArgumentOutOfRangeException(nameof(branch), branch, "no such branch"),
        });
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var english = EuropeTimelines(directory).Single(static timeline => timeline.Language == "eng");
            StringAssert.StartsWith(english.Expected(english.Dates[^1].Date), englishOnTheLatestDate, branch);

            var sets = RunEuropeTemporalGate(mount, directory);
            CollectionAssert.AreEqual(
                new[] { "EU as_of in eng", "EU as_of in fra", "EU as_of with no language" }, sets.Select(static set => set.Arm).ToArray());
            foreach (var set in sets)
            {
                Assert.IsTrue(set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"{branch}, {set.Arm}: {set.CaseCount} cases");
                Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{branch}, {set.Arm}: {set.Control.Reason}");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AnEuIndexWhoseCensusHoldsTheOriginalWordingAloneGivesItsOwnTemporalCases()
    {
        // The GDPR mounted alone in its EU index: its census holds the act's original wording and nothing else, so the EU set
        // asks it before its Formex date, on it and long after it, in English and with no language, and every answer is the one
        // the states table says.
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var english = EuropeTimelines(fixture.Directory).Single();
        Assert.AreEqual("32016R0679", english.WorkKey);
        Assert.AreEqual("eng", english.Language);
        StringAssert.StartsWith(english.Expected(Day("2016-04-27")), "original_wording:/eu-eurlex/32016R0679/eng/2016-04-27--");
        Assert.AreEqual(NoVersion, english.Expected(Day("2016-04-26")));

        var sets = RunEuropeTemporalGate(mount, fixture.Directory);
        CollectionAssert.AreEqual(new[] { "EU as_of in eng", "EU as_of with no language" }, sets.Select(static set => set.Arm).ToArray());
        foreach (var set in sets)
        {
            Assert.IsTrue(set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"{set.Arm}: {set.CaseCount} cases");
            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{set.Arm}: {set.Control.Reason}");
        }
    }

    [TestMethod]
    public async Task AMountWithNoEuIndexHasItsEuTemporalGateNotMeasuredAndSaysWhy()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        Assert.IsEmpty(EuropeTimelines(fixture.Directory));

        var set = RunEuropeTemporalGate(mount, fixture.Directory).Single();
        Assert.AreEqual("EU as_of with no language", set.Arm);
        Assert.AreEqual(0, set.CaseCount);
        Assert.AreEqual(GateVerdict.NotMeasured, set.Gates.Single().Verdict, "an empty stratum is reported, never scored");
        Assert.AreEqual(NotMeasuredReason.NoMeasurableQuery, set.Gates.Single().Reason);
        Assert.AreEqual(ControlVerdict.NotApplicable, set.Control.Verdict);
        StringAssert.Contains(set.Control.Reason, "no temporal case to shift");
    }

    [TestMethod]
    public async Task AnEuWorkWithNoCelexNeitherBreaksTheRefusalAndRetrievalSetsNorEntersThem()
    {
        // The machine gates' EU fixture mount: a consolidated version the publisher gives no CELEX holds text, so its articles' CELEX
        // is NULL (schema 5), and the first version of both sets threw reading it before any card was written. Nor are the
        // consolidated versions with a CELEX asked: EU search and the original-wording verify answer an act's original wording by
        // its CELEX, and the time view answers the versions. The Luxembourg half of both sets is the fixture tests'; this is the EU
        // half alone, and every request it derives is answered as derived.
        var (root, directory) = await V3MachineGatesTests.EuropeTemporalMountAsync();
        try
        {
            using (var connection = EuropeIndexBuilder.Open(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName), SqliteOpenMode.ReadOnly))
            {
                Assert.IsNotNull(
                    Scalar(connection, "SELECT article_identity_sha256 FROM articles WHERE publisher_work_celex IS NULL LIMIT 1"),
                    "the fixture holds the text of a work with no CELEX");
            }

            var refusals = RefusalCases(directory, Array.Empty<Timeline>());
            var searched = System.Text.Json.JsonSerializer.SerializeToElement(refusals.Requests["eu-search-held"].Parameters);
            Assert.AreEqual(EuFirstMountAcquisitionTests.ConsolidatedSeed, searched.GetProperty("identifier").GetString(), "the act's original wording is searched");
            Assert.AreEqual("retrieval_mode_unavailable", refusals.Requests["eu-in-force-on"].Gold);
            var retrieval = RetrievalCases(directory, Array.Empty<Timeline>());
            Assert.IsTrue(retrieval.Keys.Any(static key => key.StartsWith($"eu-word-{EuFirstMountAcquisitionTests.ConsolidatedSeed}-", StringComparison.Ordinal)), string.Join(", ", retrieval.Keys));
            Assert.IsFalse(retrieval.Keys.Any(static key => key.Contains("-02016R0679-", StringComparison.Ordinal)), "no consolidated version is searched by its CELEX");

            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var (refusal, _) = RunRefusalGate(mount, directory, Array.Empty<Timeline>());
            Assert.AreEqual(GateVerdict.Pass, refusal.Gates.Single().Verdict, "every EU request is refused with its own code, or answered where it must be");
            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, refusal.Control.Verdict, refusal.Control.Reason);
            foreach (var gate in RunRetrievalGate(mount, directory, Array.Empty<Timeline>()).Gates)
            {
                Assert.AreEqual(GateVerdict.Pass, gate.Verdict, $"{gate.Gate}: {gate.Value} over {gate.N}");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
