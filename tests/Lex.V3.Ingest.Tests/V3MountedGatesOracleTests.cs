using System.Globalization;
using Lex.V3.Contracts.Evaluation;
using static Lex.V3.Ingest.Tests.LuxembourgIndexCatalogueStatesTests;
using static Lex.V3.Ingest.Tests.V3CorpusIntervalMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The mounted gates' interval oracle (<see cref="V3MountedGatesTests.IntervalOracle"/>, the data design's §3.5) against the
/// reader, on index 9 built from the interval fixture and the review of #987's works: every class the design names (a hole, a
/// closed end, an unclear end day, an invalid interval, a state without text covering a date, withheld or not, a same-date
/// pair of one wording and one of two, a date before the first held state that a state without text covers, a translation),
/// each day the oracle derives answered as it says, in each language and with none; and a gate held to an oracle that bridges
/// a hole fails, so neither side can serve through one unseen. The R1 gates of 2026-10-26: run 15's first mount with codes
/// and multi-state works failed its temporal gate on the oracle's index 8 rule.
/// </summary>
[TestClass]
public sealed class V3MountedGatesOracleTests
{
    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string DigestOf(V3MountedGatesTests.IntervalOracle oracle, string work, string resource, string language = "fra") =>
        oracle.RowsOf(work).Single(row => row.LegalResource == W(resource) && row.Language == language).Digest;

    [TestMethod]
    public async Task TheIntervalOracleNamesEachClassAsTheSpecificationsRulesSay()
    {
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        using var oracle = V3MountedGatesTests.IntervalOracle.Open(fixture.Directory, harness.MemberOutcomes)!;
        Assert.IsNotNull(oracle, "index 9");
        var asOf = V3MountedGatesTests.IntervalAsOfArm(fixture.Mount, "fra");
        var inForceOn = V3MountedGatesTests.IntervalInForceOnArm(fixture.Mount, "fra");

        // Each class: what the oracle says, and that the reader answers it, through as_of and in_force_on.
        var withheld = DigestOf(oracle, "loi-1798-11-03-n1", "loi/1798/11/03/n1/consolide/20200101");
        var pdfAct = DigestOf(oracle, "loi-2002-02-02-n1", "loi/2002/02/02/n1/jo");
        var act2018 = DigestOf(oracle, "loi-2018-09-14-a883", "loi/2018/09/14/a883/jo");
        foreach (var (work, date, expectedAsOf, expectedInForceOn, what) in new[]
                 {
                     ("loi-1798-11-03-n1", "2021-07-01", "refusal:no_version_for_date:catalogue_hole", "refusal:no_version_for_date:catalogue_hole", "a hole"),
                     ("loi-2005-01-01-n1", "2016-01-01", "refusal:no_version_for_date:after_closed_end", "refusal:no_version_for_date:after_closed_end", "after a closed end"),
                     ("loi-2007-07-07-n1", "2022-06-01", DigestOf(oracle, "loi-2007-07-07-n1", "loi/2007/07/07/n1/consolide/20200101"), DigestOf(oracle, "loi-2007-07-07-n1", "loi/2007/07/07/n1/consolide/20200101"), "a nested state's end, the outer covering"),
                     ("loi-1996-11-07-n1", "2022-12-31", "refusal:no_version_for_date:publisher_end_convention_unclear", "refusal:no_version_for_date:publisher_end_convention_unclear", "an unclear end day"),
                     ("rgd-2007-06-07-n2", "2023-08-01", "refusal:no_version_for_date:publisher_interval_invalid", "refusal:no_version_for_date:publisher_interval_invalid", "an invalid interval"),
                     ("loi-1798-11-03-n1", "2020-06-01", "refusal:text_withheld", withheld, "a withheld state covers"),
                     ("loi-2002-02-02-n1", "2005-01-01", "refusal:text_not_available", pdfAct, "before the first held state, a PDF act covers"),
                     ("loi-2018-09-14-a883", "2018-09-14", act2018, act2018, "a same-date pair of one wording, the act designated"),
                     ("loi-2021-05-05-n1", "2021-06-01", "refusal:ambiguous_version", "refusal:ambiguous_version", "a same-date pair of two wordings"),
                     ("code-civil", "2016-08-31", "refusal:no_version_for_date:before_history", "refusal:no_version_for_date:before_history", "before the history"),
                 })
        {
            Assert.AreEqual(expectedAsOf, oracle.AsOf(work, "fra", Day(date)), $"oracle as_of: {what}");
            Assert.AreEqual(expectedInForceOn, oracle.InForceOn(work, "fra", Day(date)), $"oracle in_force_on: {what}");
            Assert.AreEqual(expectedAsOf, asOf(work, Day(date)), $"reader as_of: {what}");
            Assert.AreEqual(expectedInForceOn, inForceOn(work, Day(date)), $"reader in_force_on: {what}");
        }

        // A translation held alone, of a French state not acquired: its own language serves it; French refuses for the text.
        var english = DigestOf(oracle, "loi-2017-07-20-a674", "loi/2017/07/20/a674/jo", "eng");
        Assert.AreEqual(english, oracle.AsOf("loi-2017-07-20-a674", "eng", Day("2018-01-01")));
        Assert.AreEqual(english, oracle.AsOf("loi-2017-07-20-a674", null, Day("2018-01-01")), "with no language, the language that holds text");
        Assert.AreEqual(english, V3MountedGatesTests.IntervalAsOfArm(fixture.Mount, null)("loi-2017-07-20-a674", Day("2018-01-01")));
    }

    [TestMethod]
    public async Task EveryDerivedDayOfTheIntervalFixtureIsAnsweredAsTheOracleSaysInEachLanguageAndWithNone()
    {
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        using var oracle = V3MountedGatesTests.IntervalOracle.Open(fixture.Directory, harness.MemberOutcomes)!;
        var timelines = V3MountedGatesTests.Timelines(fixture.Directory);
        Assert.IsTrue(timelines.All(timeline => oracle.RowsOf(timeline.WorkKey).Any(row => row.Held)), "the timelines name the states whose text is held");
        var sets = V3MountedGatesTests.RunIntervalGate(fixture.Mount, timelines, oracle);
        CollectionAssert.AreEquivalent(
            new[] { "as_of in eng", "in_force_on in eng", "as_of in fra", "in_force_on in fra", "as_of with no language", "in_force_on with no language" },
            sets.Select(static set => set.Arm).ToArray());
        foreach (var set in sets)
        {
            Assert.IsTrue(set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"{set.Arm}: every derived day answered as the oracle says ({set.CaseCount} cases)");
            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, $"{set.Arm}: {set.Control.Reason}");
            // English holds one translation (3 days); French and no language hold every class above.
            Assert.IsGreaterThan(set.Arm.EndsWith(" in eng", StringComparison.Ordinal) ? 2 : 100, set.CaseCount, set.Arm);
        }
    }

    [TestMethod]
    public async Task AGateHeldToAnOracleThatBridgesAHoleFailsAndSoDoesOneThatServesATextlessState()
    {
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        using var oracle = V3MountedGatesTests.IntervalOracle.Open(fixture.Directory, harness.MemberOutcomes)!;
        var arm = V3MountedGatesTests.IntervalAsOfArm(fixture.Mount, "fra");

        // The rule of an index of schema 8, "the latest held state on or before the day", bridges the 1798 act's hole and its
        // withheld consolidation with the act; the reader does not, and the gate says the oracle is wrong.
        var act = DigestOf(oracle, "loi-1798-11-03-n1", "loi/1798/11/03/n1/jo");
        foreach (var day in new[] { "2021-07-01", "2020-06-01" })
        {
            var bridged = new TemporalCase($"loi-1798-11-03-n1/fra/{day}", "loi-1798-11-03-n1", Day(day), act);
            var report = TemporalEvaluation.Evaluate([bridged], arm, floor: 1);
            Assert.AreEqual(GateVerdict.Fail, report.Gate.Verdict, $"{day}: a bridged expectation is caught");
            var right = new TemporalCase(bridged.CaseId, bridged.WorkKey, bridged.AsOf, oracle.AsOf("loi-1798-11-03-n1", "fra", Day(day)));
            Assert.AreEqual(GateVerdict.Pass, TemporalEvaluation.Evaluate([right], arm, floor: 1).Gate.Verdict, $"{day}: the interval oracle's is answered");
        }

        // And the index 8 oracle's own expectation (Timeline.Expected, over the held states' dates) fails the gate where a work
        // has an unclear end day between two held states or a hole after a nested one, as run 15's gate failed: its last day of
        // the 2020 consolidation (2022-12-31) and of the 2021 one (2024-12-31) are no state's.
        foreach (var work in new[] { "loi-1996-11-07-n1", "loi-2004-04-04-n1" })
        {
            var cases = V3MountedGatesTests.Timelines(fixture.Directory).Where(timeline => timeline.WorkKey == work).SelectMany(V3MountedGatesTests.Cases).ToArray();
            Assert.AreEqual(GateVerdict.Fail, TemporalEvaluation.Evaluate(cases, Lex.V3.Ingest.Tests.V3MachineGatesTests.AsOfArm(fixture.Mount, "fra"), floor: cases.Length).Gate.Verdict, work);
        }
    }

    [TestMethod]
    public async Task TheRefusalSetOverIndexNineAnchorsOnStatesTheRulesServeAloneAndIsAnsweredCodeForCode()
    {
        // On index 9 the refusal set's anchors are what the rules serve (the R1 gates, 2026-10-26): its held anchor a state the
        // rules serve alone on its date, its date before the history before the catalogue's first state, its ambiguity one
        // the oracle says is one (the act and consolidation of one wording are served once, so they are no twin).
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        var timelines = V3MountedGatesTests.Timelines(fixture.Directory);
        var (set, derived) = V3MountedGatesTests.RunRefusalGate(fixture.Mount, fixture.Directory, timelines, harness.MemberOutcomes);
        var report = string.Join("; ", derived.Requests.Select(static pair => $"{pair.Key}={pair.Value.Gold}"));
        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict, report);
        Assert.AreEqual("ambiguous_version", derived.Requests["twin-states"].Gold, "the 2021 pair of two wordings");
        StringAssert.Contains(System.Text.Json.JsonSerializer.Serialize(derived.Requests["twin-states"].Parameters), "loi-2021-05-05-n1");

        // A date a state without text covers is as_of's refusal with the state's reason, so index 9 produces both codes from its
        // catalogue (derive-r1 holds 609 states withheld by their rights and 13 without text for another reason, which the
        // release's set named as not produced, with a reason that was not true there).
        Assert.AreEqual("text_withheld", derived.Requests["state-text-withheld"].Gold);
        Assert.AreEqual("text_not_available", derived.Requests["state-text-not-available"].Gold);
        using var oracle = V3MountedGatesTests.IntervalOracle.Open(fixture.Directory, harness.MemberOutcomes)!;
        foreach (var id in new[] { "state-text-withheld", "state-text-not-available" })
        {
            var parameters = System.Text.Json.JsonSerializer.SerializeToElement(derived.Requests[id].Parameters);
            var work = parameters.GetProperty("identifier").GetString()!["/lu-legilux/".Length..];
            Assert.AreEqual("refusal:" + derived.Requests[id].Gold, oracle.AsOf(work, parameters.GetProperty("language").GetString(), Day(parameters.GetProperty("date").GetString()!)), id);
        }

        Assert.IsFalse(derived.NotProduced.ContainsKey("text_withheld") || derived.NotProduced.ContainsKey("text_not_available"), report);
    }

    [TestMethod]
    public async Task TheTemporalSampleIsStratifiedByTheClassesTheOracleReadsInTheCatalogue()
    {
        // The data design's §3.5: a seeded sample may miss every hole of a mount; each class the oracle reads adds works, every
        // code, overlap, unclear end day and invalid interval among them.
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        using var oracle = V3MountedGatesTests.IntervalOracle.Open(fixture.Directory, harness.MemberOutcomes)!;
        foreach (var (work, kind) in new[]
                 {
                     ("code-civil", "code"), ("loi-1798-11-03-n1", "hole"), ("rgd-2007-06-07-n2", "overlap"), ("rgd-2007-06-07-n2", "invalid"),
                     ("loi-1996-11-07-n1", "unclear_end"), ("loi-2018-09-14-a883", "same_date"), ("loi-2017-07-20-a674", "translation"),
                     ("loi-2003-03-03-n1", "no_expression"), ("loi-2002-02-02-n1", "textless_before_held"), ("loi-1798-11-03-n1", "textless_after_held"),
                     ("loi-2004-04-04-n1", "hole"), ("loi-2004-04-04-n1", "overlap"),
                     ("loi-2001-01-01-n1", "multi_state"), ("loi-2008-08-08-n1", "multi_state"), ("loi-2010-03-03-n1", "single_act"),
                     ("loi-1798-11-03-n1", "withheld_after_act"), ("loi-2001-01-01-n1", "withheld_after_act"),
                 })
        {
            CollectionAssert.Contains(oracle.Classes(work).ToArray(), kind, work);
        }

        CollectionAssert.DoesNotContain(oracle.Classes("loi-2010-03-03-n1").ToArray(), "hole", "a single act holds none");
        CollectionAssert.DoesNotContain(oracle.Classes("loi-2002-02-02-n1").ToArray(), "withheld_after_act", "its act holds no text, and its later consolidation is held");
        CollectionAssert.DoesNotContain(oracle.Classes("loi-2005-01-01-n1").ToArray(), "multi_state", "an act and one consolidation are two states");
        var population = V3MountedGatesTests.Timelines(fixture.Directory);
        var stratified = V3MountedGatesTests.Stratified([], population, oracle);
        var chosen = stratified.Select(static value => value.WorkKey).ToHashSet(StringComparer.Ordinal);
        var works = population.Select(static value => value.WorkKey).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var (kind, quota) in V3MountedGatesTests.StratumWorks)
        {
            var holding = works.Where(work => oracle.Classes(work).Contains(kind)).ToArray();
            Assert.IsNotEmpty(holding, $"the fixture holds a work with {kind}");
            Assert.IsTrue(holding.Count(chosen.Contains) >= Math.Min(quota, holding.Length), $"the stratified sample holds {kind}'s quota of {quota}, or every work of it");
        }

        Assert.IsFalse(chosen.Contains("loi-2005-01-01-n1"), "a work of no class is not added");
    }

    [TestMethod]
    public async Task TheOracleDatesEachStateByTheCatalogueSoAnIndexThatMisdatesOneFailsTheGate()
    {
        // The data design's §3.5: the oracle reads the catalogue's own dates, never the row's. A row whose date is moved leaves the
        // oracle's answers as they were; a catalogue that dates a state otherwise than the index under test does fails the gate.
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        const string Work = "loi-2008-08-08-n1";
        var resource = W("loi/2008/08/08/n1/consolide/20120101");
        using var oracle = V3MountedGatesTests.IntervalOracle.Open(fixture.Directory, harness.MemberOutcomes)!;
        var days = oracle.Boundaries(Work, "fra").SelectMany(static day => new[] { day.AddDays(-1), day, day.AddDays(1) }).Append(Day("2011-06-01")).Distinct().Order().ToArray();
        var truth = days.Select(day => oracle.AsOf(Work, "fra", day)).ToArray();

        var copy = Path.Combine(Path.GetTempPath(), "lex-gates-oracle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        try
        {
            var index = Path.Combine(copy, Lex.V3.Api.V3CorpusMount.IndexFileName);
            void Mutate(string sql)
            {
                File.Copy(Path.Combine(fixture.Directory, Lex.V3.Api.V3CorpusMount.IndexFileName), index, overwrite: true);
                using var connection = Lex.V3.Ingest.Luxembourg.LuxembourgIndexBuilder.Open(index, Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite);
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$resource", resource);
                Assert.AreEqual(1, command.ExecuteNonQuery(), sql);
            }

            // The row's own date moved: the oracle does not read it.
            Mutate("UPDATE states SET applicability_date = '2011-06-01' WHERE publisher_legal_resource_iri = $resource");
            using (var rowMoved = V3MountedGatesTests.IntervalOracle.Open(copy, harness.MemberOutcomes)!)
            {
                Assert.AreEqual(Day("2012-01-01"), rowMoved.RowsOf(Work).Single(row => row.LegalResource == resource).From);
                CollectionAssert.AreEqual(truth, days.Select(day => rowMoved.AsOf(Work, "fra", day)).ToArray());
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            // The catalogue's date moved: the index under test (the mount's) now misdates the state against it, and the gate fails.
            Mutate("UPDATE work_facts SET object_value = '2011-06-01' WHERE subject_iri = $resource AND predicate = 'dateApplicability'");
            using var catalogueMoved = V3MountedGatesTests.IntervalOracle.Open(copy, harness.MemberOutcomes)!;
            Assert.AreEqual(Day("2011-06-01"), catalogueMoved.RowsOf(Work).Single(row => row.LegalResource == resource).From);
            var timelines = V3MountedGatesTests.Timelines(fixture.Directory).Where(static timeline => timeline.WorkKey == Work).ToArray();
            var sets = V3MountedGatesTests.RunIntervalGate(fixture.Mount, timelines, catalogueMoved);
            Assert.AreEqual(GateVerdict.Fail, sets.Single(static set => set.Arm == "as_of in fra").Gates.Single().Verdict, "the misdated state is caught");
            var held = V3MountedGatesTests.RunIntervalGate(fixture.Mount, timelines, oracle);
            Assert.IsTrue(held.All(static set => set.Gates.All(static gate => gate.Verdict == GateVerdict.Pass)), "the catalogue's own dates are answered");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(copy, recursive: true);
        }
    }
}
