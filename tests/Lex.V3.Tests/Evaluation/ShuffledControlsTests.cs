using Lex.V3.Contracts;
using Lex.V3.Contracts.Evaluation;

namespace Lex.V3.Tests.Evaluation;

/// <summary>
/// S4-A11, second slice: the three shuffled controls, proven on purpose-built cases. Each dataset is built so that an
/// oracle arm passes every gate, and each control must then catch the shuffle for every seed tried, not for a lucky one.
/// A control that is missed blocks the harness, and the harnesses that must be missed here are handed to the controls as
/// defective evaluations: one that always passes, one that reads no gate, one that ignores the dates.
/// </summary>
[TestClass]
public sealed class ShuffledControlsTests
{
    private const int Floor = 3;
    private const double NdcgThreshold = 0.95;

    // ---- the purpose-built retrieval cases: six distinct queries, three exact identifiers, three no-hit ----

    private static EvaluationCase[] RetrievalCases()
    {
        var cases = new List<EvaluationCase>();
        for (var index = 1; index <= 6; index++)
        {
            var id = $"r{index}";
            cases.Add(new EvaluationCase(id, "lu", EvaluationCaseKind.Retrieval, new QueryJudgments(id,
                [new JudgedAnchor($"work-{index}", "art-1", 3), new JudgedAnchor($"work-{index}", "art-2", 1)])));
        }

        for (var index = 1; index <= 3; index++)
        {
            var id = $"e{index}";
            cases.Add(new EvaluationCase(id, "lu", EvaluationCaseKind.ExactIdentifier, new QueryJudgments(id,
                [new JudgedAnchor($"exact-{index}", "art-9", 3)])));
        }

        for (var index = 1; index <= 3; index++)
        {
            var id = $"n{index}";
            cases.Add(new EvaluationCase(id, "lu", EvaluationCaseKind.Retrieval, new QueryJudgments(id, [])));
        }

        return [.. cases];
    }

    private static RetrievalArm Oracle(IReadOnlyList<EvaluationCase> cases) => caseId =>
        cases.Single(value => value.CaseId == caseId).Judgments.Anchors
            .Where(anchor => anchor.Grade > 0)
            .OrderByDescending(anchor => anchor.Grade).ThenBy(anchor => anchor.AnchorId, StringComparer.Ordinal)
            .Select(anchor => new RankedAnchor(anchor.WorkKey, anchor.AnchorId))
            .ToArray();

    private static RetrievalReport RealRetrieval(IReadOnlyList<EvaluationCase> cases, RetrievalArm arm) =>
        RetrievalEvaluation.Evaluate(cases, arm, Floor, NdcgThreshold);

    private static RetrievalReport PassEverything(IReadOnlyList<EvaluationCase> cases, RetrievalArm arm) =>
        new(MetricResult.Measured(1.0), MetricResult.Measured(1.0), MetricResult.Measured(1.0),
        [
            EvaluationGates.AtLeast(EvaluationGateNames.AnchorNdcgAt10, MetricResult.Measured(1.0), NdcgThreshold),
            EvaluationGates.AtLeast(EvaluationGateNames.NoHitAccuracy, MetricResult.Measured(1.0), 1.0),
            EvaluationGates.AtLeast(EvaluationGateNames.ResolverExactness, MetricResult.Measured(1.0), 1.0),
        ]);

    [TestMethod]
    public void TheOracleArmPassesEveryGateOnThePurposeBuiltRetrievalCases()
    {
        var cases = RetrievalCases();

        var report = RealRetrieval(cases, Oracle(cases));

        Assert.AreEqual(MetricResult.Measured(1.0), report.AnchorNdcgAt10);
        Assert.AreEqual(MetricResult.Measured(1.0), report.NoHitAccuracy);
        Assert.AreEqual(MetricResult.Measured(1.0), report.ResolverExactness);
        Assert.IsTrue(EvaluationGates.ReleasePasses(report.Gates));
    }

    // ---- control 1: the qrels shuffle ----

    [TestMethod]
    public void TheQrelsShuffleIsCaughtOnThePurposeBuiltCasesForEverySeedTried()
    {
        var cases = RetrievalCases();

        for (ulong seed = 1; seed <= 40; seed++)
        {
            var result = ShuffledControls.QrelsShuffle(cases, Oracle(cases), RealRetrieval, seed);

            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, result.Verdict, $"seed {seed}: {result.Reason}");
            Assert.AreEqual(ShuffledControlNames.QrelsShuffle, result.Name);
            Assert.AreEqual(seed, result.Seed);
            Assert.IsFalse(result.BlocksTheHarness);
        }
    }

    [TestMethod]
    public void TheQrelsShuffleCollapsesTheNdcgAndStopsBothInvariantGatesOnTheShuffledCases()
    {
        // The control's own claim, read off the shuffled cases: nothing in the reasons is taken on trust.
        var cases = RetrievalCases();
        var result = ShuffledControls.QrelsShuffle(cases, Oracle(cases), RealRetrieval, 1);

        StringAssert.Contains(result.Reason, "anchor nDCG@10 fell to 0 ");
        StringAssert.Contains(result.Reason, "both invariant gates stopped passing");
    }

    /// <summary>The document's ceiling, 0.15, read off the boundary: 0.1499 collapsed, 0.15 did not.</summary>
    [TestMethod]
    public void TheQrelsShuffleCollapseCeilingIsExactlyOneHundredFiftyThousandths()
    {
        var cases = RetrievalCases();
        RetrievalEvaluator AfterTheShuffleReports(double ndcg) => (value, arm) =>
        {
            var real = RealRetrieval(value, arm);
            return ReferenceEquals(value, cases) ? real : real with { AnchorNdcgAt10 = MetricResult.Measured(ndcg) };
        };

        Assert.AreEqual(ControlVerdict.CaughtTheShuffle,
            ShuffledControls.QrelsShuffle(cases, Oracle(cases), AfterTheShuffleReports(0.1499), 1).Verdict);
        Assert.AreEqual(ControlVerdict.MissedTheShuffle,
            ShuffledControls.QrelsShuffle(cases, Oracle(cases), AfterTheShuffleReports(0.15), 1).Verdict);
    }

    [TestMethod]
    public void TheQrelsShuffleIsRepeatableFromItsSeed()
    {
        var cases = RetrievalCases();

        Assert.AreEqual(
            ShuffledControls.QrelsShuffle(cases, Oracle(cases), RealRetrieval, 17),
            ShuffledControls.QrelsShuffle(cases, Oracle(cases), RealRetrieval, 17));
    }

    [TestMethod]
    public void AnArmThatFailsTheUnshuffledCasesLeavesTheQrelsShuffleNotApplicable()
    {
        var result = ShuffledControls.QrelsShuffle(RetrievalCases(), _ => [], RealRetrieval, 1);

        Assert.AreEqual(ControlVerdict.NotApplicable, result.Verdict);
        StringAssert.Contains(result.Reason, "the reference arm does not pass the unshuffled cases");
        Assert.IsTrue(result.BlocksTheHarness, "a control that proved nothing is no proof");
    }

    [TestMethod]
    public void AShuffleThatMovesNothingLeavesTheQrelsShuffleNotApplicable()
    {
        // One case to a collection: the shuffle has nowhere to move a judgment to. Every stratum is still measured.
        var cases = new[]
        {
            new EvaluationCase("r1", "c1", EvaluationCaseKind.Retrieval, new QueryJudgments("r1", [new JudgedAnchor("w1", "a1", 3)])),
            new EvaluationCase("e1", "c2", EvaluationCaseKind.ExactIdentifier, new QueryJudgments("e1", [new JudgedAnchor("w2", "a1", 3)])),
            new EvaluationCase("n1", "c3", EvaluationCaseKind.Retrieval, new QueryJudgments("n1", [])),
        };
        RetrievalReport OneEach(IReadOnlyList<EvaluationCase> value, RetrievalArm arm) =>
            RetrievalEvaluation.Evaluate(value, arm, floor: 1, NdcgThreshold);

        var result = ShuffledControls.QrelsShuffle(cases, Oracle(cases), OneEach, 1);

        Assert.AreEqual(ControlVerdict.NotApplicable, result.Verdict);
        StringAssert.Contains(result.Reason, "changes no case's judgments");
    }

    [TestMethod]
    public void AHarnessThatPassesEverythingMissesTheQrelsShuffleAndIsBlocked()
    {
        var cases = RetrievalCases();

        var result = ShuffledControls.QrelsShuffle(cases, Oracle(cases), PassEverything, 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "anchor nDCG@10 is 1 after the shuffle");
        Assert.IsTrue(result.BlocksTheHarness);
    }

    [TestMethod]
    public void AHarnessWhoseInvariantGatesReadNothingMissesTheQrelsShuffleEvenWhenItsNdcgCollapses()
    {
        var cases = RetrievalCases();
        RetrievalReport GatesAlwaysPass(IReadOnlyList<EvaluationCase> value, RetrievalArm arm)
        {
            var real = RealRetrieval(value, arm);
            return real with
            {
                Gates =
                [
                    real.Gates[0],
                    EvaluationGates.AtLeast(EvaluationGateNames.NoHitAccuracy, MetricResult.Measured(1.0), 1.0),
                    EvaluationGates.AtLeast(EvaluationGateNames.ResolverExactness, MetricResult.Measured(1.0), 1.0),
                ],
            };
        }

        var result = ShuffledControls.QrelsShuffle(cases, Oracle(cases), GatesAlwaysPass, 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "invariant gate 'no_hit_accuracy' still passes after the shuffle");
    }

    [TestMethod]
    public void AHarnessThatReportsNoInvariantGateMissesTheQrelsShuffleBecauseNothingCouldFire()
    {
        var cases = RetrievalCases();
        RetrievalReport OnlyNdcg(IReadOnlyList<EvaluationCase> value, RetrievalArm arm)
        {
            var real = RealRetrieval(value, arm);
            return real with { Gates = [real.Gates[0]] };
        }

        var result = ShuffledControls.QrelsShuffle(cases, Oracle(cases), OnlyNdcg, 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "the harness reports no gate 'no_hit_accuracy'");
    }

    [TestMethod]
    public void TheQrelsShuffleMovesJudgmentsOnlyWithinACollection()
    {
        var original = new List<EvaluationCase>();
        foreach (var collection in new[] { "alpha", "beta" })
        {
            for (var index = 1; index <= 3; index++)
            {
                var id = $"{collection}-r{index}";
                original.Add(new EvaluationCase(id, collection, EvaluationCaseKind.Retrieval, new QueryJudgments(id,
                    [new JudgedAnchor($"{collection}-work-{index}", "art-1", 3)])));
            }

            for (var index = 1; index <= 2; index++)
            {
                var id = $"{collection}-e{index}";
                original.Add(new EvaluationCase(id, collection, EvaluationCaseKind.ExactIdentifier, new QueryJudgments(id,
                    [new JudgedAnchor($"{collection}-exact-{index}", "art-9", 3)])));
                var none = $"{collection}-n{index}";
                original.Add(new EvaluationCase(none, collection, EvaluationCaseKind.Retrieval, new QueryJudgments(none, [])));
            }
        }

        IReadOnlyList<EvaluationCase>? shuffled = null;
        RetrievalReport Capture(IReadOnlyList<EvaluationCase> value, RetrievalArm arm)
        {
            if (!ReferenceEquals(value, original))
            {
                shuffled = value;
            }

            return RealRetrieval(value, arm);
        }

        var result = ShuffledControls.QrelsShuffle(original, Oracle(original), Capture, 5);

        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, result.Verdict, result.Reason);
        Assert.IsNotNull(shuffled);
        Assert.IsTrue(
            shuffled.All(value => value.Judgments.Anchors.All(anchor => anchor.WorkKey.StartsWith(value.Collection + "-", StringComparison.Ordinal))),
            "a judgment is never moved into another collection");
        Assert.IsTrue(
            shuffled.Zip(original).Any(pair => pair.First.Judgments.Anchors.Count != pair.Second.Judgments.Anchors.Count ||
                pair.First.Judgments.Anchors.Any(anchor => pair.Second.Judgments.GradeOf(new RankedAnchor(anchor.WorkKey, anchor.AnchorId)) == 0)),
            "and the judgments did move");
        Assert.HasCount(original.Count, shuffled);
    }

    // ---- control 2: the verdict shuffle ----

    private static readonly string[] Verdicts = ["answer", "awe", "point", "clarify", "refuse", "split"];

    private static VerdictCase[] VerdictCases(IReadOnlyList<string> gold) =>
        gold.Select((verdict, index) => new VerdictCase($"v{index + 1:00}", verdict)).ToArray();

    private static VerdictArm GoldOf(IReadOnlyList<VerdictCase> cases) => caseId => cases.Single(value => value.CaseId == caseId).GoldVerdict;

    private static VerdictReport RealVerdicts(IReadOnlyList<VerdictCase> cases, VerdictArm arm) =>
        VerdictEvaluation.Evaluate(cases, arm, floor: 6);

    [TestMethod]
    public void TheVerdictShuffleIsCaughtOnFourCasesOfEachOfSixVerdictsForEverySeedTried()
    {
        var cases = VerdictCases(Enumerable.Range(0, 24).Select(index => Verdicts[index % 6]).ToArray());

        for (ulong seed = 1; seed <= 40; seed++)
        {
            var result = ShuffledControls.VerdictShuffle(cases, GoldOf(cases), RealVerdicts, seed);

            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, result.Verdict, $"seed {seed}: {result.Reason}");
            StringAssert.Contains(result.Reason, "exact match fell to 0, within the base-rate ceiling 0.1667");
        }
    }

    [TestMethod]
    public void WhereOneVerdictIsMostOfTheCasesTheCeilingIsItsShareAndTwoCasesMustStillMatch()
    {
        // Five of eight are "answer": at least 2 x 5 - 8 = 2 cases must keep a matching verdict, so exact match is 0.25
        // against a base-rate ceiling of 0.625.
        var cases = VerdictCases(["answer", "answer", "answer", "answer", "answer", "refuse", "refuse", "refuse"]);

        var result = ShuffledControls.VerdictShuffle(cases, GoldOf(cases), (value, arm) => VerdictEvaluation.Evaluate(value, arm, floor: 8), 3);

        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "exact match fell to 0.25, within the base-rate ceiling 0.625");
    }

    [TestMethod]
    public void CasesThatCarryOneVerdictLeaveTheVerdictShuffleNotApplicableAndAWrongArmDoesToo()
    {
        var one = VerdictCases(Enumerable.Repeat("answer", 6).ToArray());
        var mixed = VerdictCases(Enumerable.Range(0, 24).Select(index => Verdicts[index % 6]).ToArray());

        var same = ShuffledControls.VerdictShuffle(one, GoldOf(one), RealVerdicts, 1);
        var wrong = ShuffledControls.VerdictShuffle(mixed, _ => "answer", RealVerdicts, 1);

        Assert.AreEqual(ControlVerdict.NotApplicable, same.Verdict);
        StringAssert.Contains(same.Reason, "the cases carry one verdict");
        Assert.AreEqual(ControlVerdict.NotApplicable, wrong.Verdict);
        StringAssert.Contains(wrong.Reason, "the reference arm does not pass the unshuffled cases");
    }

    [TestMethod]
    public void AComparerThatIsAlwaysTrueMissesTheVerdictShuffleAndIsBlocked()
    {
        var cases = VerdictCases(Enumerable.Range(0, 24).Select(index => Verdicts[index % 6]).ToArray());
        VerdictReport AlwaysTrue(IReadOnlyList<VerdictCase> value, VerdictArm arm) =>
            new(MetricResult.Measured(1.0), EvaluationGates.AtLeast(EvaluationGateNames.VerdictExactMatch, MetricResult.Measured(1.0), 1.0));

        var result = ShuffledControls.VerdictShuffle(cases, GoldOf(cases), AlwaysTrue, 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "exact match is 1 after the shuffle and must not exceed the base-rate ceiling 0.1667");
        Assert.IsTrue(result.BlocksTheHarness);
    }

    [TestMethod]
    public void AnExactMatchExactlyAtTheBaseRateCeilingIsCaughtAndOneStepAboveItIsMissed()
    {
        var cases = VerdictCases(Enumerable.Range(0, 24).Select(index => Verdicts[index % 6]).ToArray());
        VerdictEvaluator After(double exact) => (value, arm) =>
        {
            var real = RealVerdicts(value, arm);
            return ReferenceEquals(value, cases) ? real : real with { ExactMatch = MetricResult.Measured(exact) };
        };

        Assert.AreEqual(ControlVerdict.CaughtTheShuffle,
            ShuffledControls.VerdictShuffle(cases, GoldOf(cases), After(4.0 / 24.0), 1).Verdict, "the ceiling is four of twenty-four");
        Assert.AreEqual(ControlVerdict.MissedTheShuffle,
            ShuffledControls.VerdictShuffle(cases, GoldOf(cases), After((4.0 / 24.0) + 0.001), 1).Verdict);
    }

    // ---- control 3: the date shuffle ----

    private static readonly DateOnly First = new(2024, 1, 1);

    // Six states of thirty days each, the last open-ended; twelve cases inside the first four states.
    private static TemporalCase[] TemporalCases() =>
    [
        .. Enumerable.Range(1, 4).SelectMany(state => new[] { 5, 15, 25 }.Select(offset =>
            new TemporalCase($"t{state}-{offset}", "w1", First.AddDays(((state - 1) * 30) + offset), $"s{state}"))),
    ];

    private static string? StateOn(string work, DateOnly asOf) =>
        asOf < First ? null : $"s{Math.Min(((asOf.DayNumber - First.DayNumber) / 30) + 1, 6)}";

    private static TemporalReport RealDates(IReadOnlyList<TemporalCase> cases, TemporalArm arm) =>
        TemporalEvaluation.Evaluate(cases, arm, floor: 3);

    [TestMethod]
    public void TheDateShuffleIsCaughtWhenEveryHeldIntervalLeavesTheStateForEverySeedTried()
    {
        var cases = TemporalCases();

        for (ulong seed = 1; seed <= 40; seed++)
        {
            var result = ShuffledControls.DateShuffle(cases, StateOn, RealDates, [30, 60, 90], seed);

            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, result.Verdict, $"seed {seed}: {result.Reason}");
            Assert.AreEqual("every expectation broke after the shift", result.Reason);
        }
    }

    [TestMethod]
    public void AShiftShorterThanAStateLeavesExpectationsStandingAndTheDateShuffleReportsIt()
    {
        var result = ShuffledControls.DateShuffle(TemporalCases(), StateOn, RealDates, [1], 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "of the expectations still hold after the shift and every one must break");
    }

    [TestMethod]
    public void OneExpectationThatStillHoldsMissesTheDateShuffleHoweverManyBroke()
    {
        var cases = TemporalCases();
        TemporalEvaluator OneSurvives = (value, arm) =>
        {
            var real = RealDates(value, arm);
            return ReferenceEquals(value, cases) ? real : real with { Exactness = MetricResult.Measured(1.0 / 12.0) };
        };

        var result = ShuffledControls.DateShuffle(cases, StateOn, OneSurvives, [30, 60, 90], 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        StringAssert.Contains(result.Reason, "0.0833 of the expectations still hold");
    }

    [TestMethod]
    public void AHarnessThatIgnoresTheDatesMissesTheDateShuffle()
    {
        TemporalReport IgnoresDates(IReadOnlyList<TemporalCase> value, TemporalArm arm) =>
            new(MetricResult.Measured(1.0), EvaluationGates.AtLeast(EvaluationGateNames.TemporalExactness, MetricResult.Measured(1.0), 1.0));

        var result = ShuffledControls.DateShuffle(TemporalCases(), StateOn, IgnoresDates, [30], 1);

        Assert.AreEqual(ControlVerdict.MissedTheShuffle, result.Verdict);
        Assert.IsTrue(result.BlocksTheHarness);
    }

    [TestMethod]
    public void AnArmThatFailsTheUnshiftedCasesLeavesTheDateShuffleNotApplicableAndBadIntervalsAreRefused()
    {
        var cases = TemporalCases();

        var result = ShuffledControls.DateShuffle(cases, (_, _) => null, RealDates, [30], 1);

        Assert.AreEqual(ControlVerdict.NotApplicable, result.Verdict);
        StringAssert.Contains(result.Reason, "the reference arm does not pass the unshifted cases");
        Assert.ThrowsExactly<ArgumentException>(() => ShuffledControls.DateShuffle(cases, StateOn, RealDates, [], 1));
        Assert.ThrowsExactly<ArgumentException>(() => ShuffledControls.DateShuffle(cases, StateOn, RealDates, [30, 0], 1));
        Assert.ThrowsExactly<ArgumentException>(() => ShuffledControls.DateShuffle(cases, StateOn, RealDates, [-30], 1));
    }

    [TestMethod]
    public void TheDateShiftDrawsItsIntervalFromTheSeedAndNotFromTheCase()
    {
        // The shift is the reference generator's draw over the intervals, in case-id order: 30 then 60 then 30 with seed 1.
        var cases = new[]
        {
            new TemporalCase("a", "w1", First.AddDays(5), "s1"),
            new TemporalCase("b", "w1", First.AddDays(5), "s1"),
            new TemporalCase("c", "w1", First.AddDays(5), "s1"),
        };
        var seen = new List<DateOnly>();
        TemporalReport Recording(IReadOnlyList<TemporalCase> value, TemporalArm arm)
        {
            if (value.Any(one => one.AsOf != First.AddDays(5)))
            {
                seen.AddRange(value.Select(one => one.AsOf));
            }

            return TemporalEvaluation.Evaluate(value, arm, floor: 1);
        }

        var random = new SplitMix64(1);
        var expected = Enumerable.Range(0, 3).Select(_ => First.AddDays(5 + new[] { 30, 60, 90 }[random.NextBelow(3)])).ToArray();

        ShuffledControls.DateShuffle(cases, StateOn, Recording, [30, 60, 90], 1);

        CollectionAssert.AreEqual(expected, seen);
    }

    // ---- the suite ----

    private static ControlResult Named(string name, ControlVerdict verdict) => new(name, verdict, "because", 1);

    [TestMethod]
    public void TheHarnessIsBlockedUnlessEachOfTheThreeControlsWasRunOnceAndCaught()
    {
        var caught = new[]
        {
            Named(ShuffledControlNames.QrelsShuffle, ControlVerdict.CaughtTheShuffle),
            Named(ShuffledControlNames.VerdictShuffle, ControlVerdict.CaughtTheShuffle),
            Named(ShuffledControlNames.DateShuffle, ControlVerdict.CaughtTheShuffle),
        };

        Assert.IsFalse(new ControlSuiteResult(caught).HarnessBlocked);
        Assert.IsTrue(new ControlSuiteResult([]).HarnessBlocked, "no control run is not a proof");
        Assert.IsTrue(new ControlSuiteResult(caught[..2]).HarnessBlocked, "a control that was never run");
        Assert.IsTrue(new ControlSuiteResult([caught[0], caught[0], caught[1]]).HarnessBlocked, "a repeated control is not the missing one");
        Assert.IsTrue(new ControlSuiteResult([.. caught, caught[0]]).HarnessBlocked);
        Assert.IsTrue(new ControlSuiteResult([caught[0], caught[1], Named(ShuffledControlNames.DateShuffle, ControlVerdict.MissedTheShuffle)]).HarnessBlocked);
        Assert.IsTrue(new ControlSuiteResult([caught[0], caught[1], Named(ShuffledControlNames.DateShuffle, ControlVerdict.NotApplicable)]).HarnessBlocked);
    }

    [TestMethod]
    public void TheThreeControlsRunTogetherOnThePurposeBuiltCasesAndTheHarnessStands()
    {
        var retrieval = RetrievalCases();
        var verdicts = VerdictCases(Enumerable.Range(0, 24).Select(index => Verdicts[index % 6]).ToArray());
        var dates = TemporalCases();

        var suite = new ControlSuiteResult(
        [
            ShuffledControls.QrelsShuffle(retrieval, Oracle(retrieval), RealRetrieval, 2),
            ShuffledControls.VerdictShuffle(verdicts, GoldOf(verdicts), RealVerdicts, 2),
            ShuffledControls.DateShuffle(dates, StateOn, RealDates, [30, 60, 90], 2),
        ]);

        Assert.IsFalse(suite.HarnessBlocked, string.Join("; ", suite.Results.Select(result => $"{result.Name} {result.Verdict}: {result.Reason}")));
        CollectionAssert.AreEqual(
            new[] { "qrels_shuffle", "verdict_shuffle", "date_shuffle" }, suite.Results.Select(result => result.Name).ToArray());
    }

    [TestMethod]
    public void ControlResultsAreClosedAndSerialiseTheirVerdictsByName()
    {
        Assert.AreEqual("\"caught_the_shuffle\"", ContractJson.Serialize(ControlVerdict.CaughtTheShuffle));
        Assert.AreEqual("\"missed_the_shuffle\"", ContractJson.Serialize(ControlVerdict.MissedTheShuffle));
        Assert.AreEqual("\"not_applicable\"", ContractJson.Serialize(ControlVerdict.NotApplicable));
        Assert.HasCount(3, Enum.GetValues<ControlVerdict>());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ControlResult("qrels_shuffle", (ControlVerdict)0, "r", 1));
        Assert.ThrowsExactly<ArgumentException>(() => new ControlResult(" ", ControlVerdict.CaughtTheShuffle, "r", 1));
        Assert.ThrowsExactly<ArgumentException>(() => new ControlResult("qrels_shuffle", ControlVerdict.CaughtTheShuffle, "", 1));
    }
}
