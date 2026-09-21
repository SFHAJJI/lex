using Lex.V3.Contracts;
using Lex.V3.Contracts.Evaluation;

namespace Lex.V3.Tests.Evaluation;

/// <summary>
/// The three harness evaluations, before any control is run against them: each stratum is measured over the cases that
/// belong to it, a stratum with none is not measured and never a perfect score, and what a case is judged against is its own.
/// </summary>
[TestClass]
public sealed class HarnessEvaluationsTests
{
    private static QueryJudgments Judged(string caseId, params (string Work, string Anchor, int Grade)[] anchors) =>
        new(caseId, anchors.Select(anchor => new JudgedAnchor(anchor.Work, anchor.Anchor, anchor.Grade)).ToArray());

    private static EvaluationCase Retrieval(string caseId, params (string Work, string Anchor, int Grade)[] anchors) =>
        new(caseId, "lu", EvaluationCaseKind.Retrieval, Judged(caseId, anchors));

    private static EvaluationCase Exact(string caseId, params (string Work, string Anchor, int Grade)[] anchors) =>
        new(caseId, "lu", EvaluationCaseKind.ExactIdentifier, Judged(caseId, anchors));

    private static RetrievalArm Returns(params (string CaseId, (string Work, string Anchor)[] Ranking)[] answers) =>
        caseId => answers.Single(answer => answer.CaseId == caseId).Ranking.Select(item => new RankedAnchor(item.Work, item.Anchor)).ToArray();

    private static GateResult Gate(RetrievalReport report, string name) => report.Gates.Single(gate => gate.Name == name);

    // ---- retrieval ----

    [TestMethod]
    public void ANoHitStratumThatIsEmptyIsNotMeasuredAndItsGateIsNotMeasuredEither()
    {
        var cases = new[] { Retrieval("q1", ("w1", "a1", 3)), Exact("e1", ("w2", "a1", 3)) };
        var arm = Returns(("q1", [("w1", "a1")]), ("e1", [("w2", "a1")]));

        var report = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.9);

        Assert.AreEqual(MetricResult.NotMeasured(NotMeasuredReason.NoMeasurableQuery), report.NoHitAccuracy);
        Assert.AreEqual(GateVerdict.NotMeasured, Gate(report, EvaluationGateNames.NoHitAccuracy).Verdict);
        Assert.IsFalse(report.Releases, "a stratum nobody measured must not read as a pass");
    }

    [TestMethod]
    public void AReleaseNeedsTheThreeRetrievalGatesTheVerdictGateAndTheTemporalGateAndNoOtherName()
    {
        CollectionAssert.AreEqual(
            new[] { "anchor_ndcg_at_10", "no_hit_accuracy", "resolver_exactness" },
            new RetrievalReport(MetricResult.Measured(1.0), MetricResult.Measured(1.0), MetricResult.Measured(1.0), []).RequiredGates.ToArray());
        CollectionAssert.AreEqual(new[] { "verdict_exact_match" }, new VerdictReport(MetricResult.Measured(1.0), new GateResult("g", GateVerdict.Pass, null)).RequiredGates.ToArray());
        CollectionAssert.AreEqual(new[] { "temporal_exactness" }, new TemporalReport(MetricResult.Measured(1.0), new GateResult("g", GateVerdict.Pass, null)).RequiredGates.ToArray());
    }

    [TestMethod]
    public void AHarnessThatDropsOrRenamesAGateDoesNotReleaseHoweverWellTheRestPass()
    {
        var cases = new[] { Retrieval("q1", ("w1", "a1", 3)), Retrieval("n1"), Exact("e1", ("w2", "a1", 3)) };
        var arm = Returns(("q1", [("w1", "a1")]), ("n1", []), ("e1", [("w2", "a1")]));
        var whole = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.9);
        Assert.IsTrue(whole.Releases, "the control case: every gate reported and passing releases");

        foreach (var dropped in new[] { EvaluationGateNames.AnchorNdcgAt10, EvaluationGateNames.NoHitAccuracy, EvaluationGateNames.ResolverExactness })
        {
            var smaller = whole with { Gates = whole.Gates.Where(gate => gate.Name != dropped).ToArray() };
            Assert.IsTrue(smaller.Gates.All(static gate => gate.Verdict == GateVerdict.Pass), $"without {dropped} every gate left passes");
            Assert.IsFalse(smaller.Releases, $"a report without {dropped} must not release");
        }

        var renamed = whole with { Gates = whole.Gates.Select(gate => gate.Name == EvaluationGateNames.ResolverExactness ? new GateResult("resolver", gate.Verdict, gate.Reason) : gate).ToArray() };
        Assert.IsFalse(renamed.Releases, "a gate under another name is not the required one");

        var verdict = new VerdictReport(MetricResult.Measured(1.0), new GateResult(EvaluationGateNames.VerdictExactMatch, GateVerdict.Pass, null));
        Assert.IsTrue(verdict.Releases);
        Assert.IsFalse((verdict with { Gate = new GateResult("some_other_gate", GateVerdict.Pass, null) }).Releases);
        var temporal = new TemporalReport(MetricResult.Measured(1.0), new GateResult(EvaluationGateNames.TemporalExactness, GateVerdict.Pass, null));
        Assert.IsTrue(temporal.Releases);
        Assert.IsFalse((temporal with { Gate = new GateResult(EvaluationGateNames.VerdictExactMatch, GateVerdict.Pass, null) }).Releases, "the verdict gate is not the temporal one");
    }

    [TestMethod]
    public void ANoHitCaseIsRightWhenNothingIsReturnedAndWrongWhenAnythingIs()
    {
        var cases = new[] { Retrieval("n1"), Retrieval("n2") };

        var silent = RetrievalEvaluation.Evaluate(cases, Returns(("n1", []), ("n2", [])), floor: 1, ndcgThreshold: 0.9);
        var oneHit = RetrievalEvaluation.Evaluate(cases, Returns(("n1", []), ("n2", [("w", "a")])), floor: 1, ndcgThreshold: 0.9);

        Assert.AreEqual(MetricResult.Measured(1.0), silent.NoHitAccuracy);
        Assert.AreEqual(MetricResult.Measured(0.5), oneHit.NoHitAccuracy);
        Assert.AreEqual(GateVerdict.Fail, Gate(oneHit, EvaluationGateNames.NoHitAccuracy).Verdict, "an invariant gates at 1.0");
    }

    [TestMethod]
    public void AnExactIdentifierIsResolvedOnlyWhenItsFirstResultIsItsOneSupportingAnchor()
    {
        var cases = new[]
        {
            Exact("right", ("w1", "a1", 3)),
            Exact("wrong-first", ("w2", "a1", 3)),
            Exact("empty", ("w3", "a1", 3)),
            Exact("second", ("w4", "a1", 3)),
        };
        var arm = Returns(
            ("right", [("w1", "a1")]),
            ("wrong-first", [("w2", "a9")]),
            ("empty", []),
            ("second", [("w9", "a9"), ("w4", "a1")]));

        var report = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.9);

        Assert.AreEqual(MetricResult.Measured(0.25), report.ResolverExactness, "only the first result counts");
        Assert.AreEqual(GateVerdict.Fail, Gate(report, EvaluationGateNames.ResolverExactness).Verdict);
    }

    [TestMethod]
    public void AnExactIdentifierCaseWithoutExactlyOneSupportingAnchorCountsAsNotResolvedAndNeverAsSkipped()
    {
        var cases = new[]
        {
            Exact("two", ("w1", "a1", 3), ("w1", "a2", 3)),
            Exact("none", ("w2", "a1", 1)),
            Exact("good", ("w3", "a1", 3)),
        };
        var arm = Returns(("two", [("w1", "a1")]), ("none", [("w2", "a1")]), ("good", [("w3", "a1")]));

        var report = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.9);

        Assert.AreEqual(1.0 / 3.0, report.ResolverExactness.Value!.Value, 1e-12);
    }

    [TestMethod]
    public void AStratumBelowItsFloorIsNotMeasuredWhateverItScored()
    {
        var cases = new[] { Exact("e1", ("w1", "a1", 3)), Exact("e2", ("w2", "a1", 3)) };
        var arm = Returns(("e1", [("w1", "a1")]), ("e2", [("w2", "a1")]));

        var report = RetrievalEvaluation.Evaluate(cases, arm, floor: 3, ndcgThreshold: 0.9);

        Assert.AreEqual(MetricResult.NotMeasured(NotMeasuredReason.StratumBelowFloor), report.ResolverExactness);
        Assert.AreEqual(MetricResult.NotMeasured(NotMeasuredReason.StratumBelowFloor), report.AnchorNdcgAt10);
    }

    [TestMethod]
    public void TheNdcgGateReadsTheDeclaredThresholdAndTheCutoffIsTen()
    {
        var cases = new[] { Retrieval("q1", ("w1", "a1", 3), ("w1", "a2", 1)) };
        var arm = Returns(("q1", [("w1", "a2"), ("w1", "a1")]));

        var strict = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.9);
        var lenient = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.7);

        Assert.AreEqual(0.7967075809905066, strict.AnchorNdcgAt10.Value!.Value, 1e-12);
        Assert.AreEqual(GateVerdict.Fail, Gate(strict, EvaluationGateNames.AnchorNdcgAt10).Verdict);
        Assert.AreEqual(GateVerdict.Pass, Gate(lenient, EvaluationGateNames.AnchorNdcgAt10).Verdict);

        var eleven = Enumerable.Range(1, 11).Select(index => (("w", $"a{index}"))).ToArray();
        var late = new[] { Retrieval("q2", ("w", "a11", 3)) };
        var lateReport = RetrievalEvaluation.Evaluate(late, Returns(("q2", eleven)), floor: 1, ndcgThreshold: 0.1);
        Assert.AreEqual(MetricResult.Measured(0.0), lateReport.AnchorNdcgAt10, "the eleventh result is beyond the cutoff");
    }

    [TestMethod]
    public void ARunReportsExactlyTheThreeNamedGatesAndRefusesRepeatedCaseIdentities()
    {
        var cases = new[] { Retrieval("q1", ("w1", "a1", 3)) };
        var report = RetrievalEvaluation.Evaluate(cases, Returns(("q1", [("w1", "a1")])), floor: 1, ndcgThreshold: 0.9);

        CollectionAssert.AreEqual(
            new[] { "anchor_ndcg_at_10", "no_hit_accuracy", "resolver_exactness" }, report.Gates.Select(gate => gate.Name).ToArray());

        Assert.ThrowsExactly<ArgumentException>(() =>
            RetrievalEvaluation.Evaluate([cases[0], cases[0]], Returns(("q1", [])), floor: 1, ndcgThreshold: 0.9));
    }

    [TestMethod]
    public void ACaseIsJudgedUnderItsOwnIdentityAndItsKindIsClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new EvaluationCase("q1", "lu", EvaluationCaseKind.Retrieval, Judged("someone-else", ("w", "a", 3))));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new EvaluationCase("q1", "lu", (EvaluationCaseKind)0, Judged("q1")));
        Assert.ThrowsExactly<ArgumentException>(() => new EvaluationCase("q1", " ", EvaluationCaseKind.Retrieval, Judged("q1")));
        Assert.ThrowsExactly<ArgumentNullException>(() => new EvaluationCase("q1", "lu", EvaluationCaseKind.Retrieval, null!));

        Assert.AreEqual("\"retrieval\"", ContractJson.Serialize(EvaluationCaseKind.Retrieval));
        Assert.AreEqual("\"exact_identifier\"", ContractJson.Serialize(EvaluationCaseKind.ExactIdentifier));
    }

    // ---- verdicts ----

    [TestMethod]
    public void AVerdictMustMatchTheGoldTokenExactlyAndAnEmptyRunIsNotMeasured()
    {
        var cases = new[] { new VerdictCase("c1", "refuse"), new VerdictCase("c2", "answer"), new VerdictCase("c3", "split") };
        var emitted = new Dictionary<string, string> { ["c1"] = "refuse", ["c2"] = "Answer", ["c3"] = "split" };

        var report = VerdictEvaluation.Evaluate(cases, caseId => emitted[caseId], floor: 1);

        Assert.AreEqual(2.0 / 3.0, report.ExactMatch.Value!.Value, 1e-12, "a different case is a different verdict");
        Assert.AreEqual(GateVerdict.Fail, report.Gate.Verdict);
        Assert.AreEqual("verdict_exact_match", report.Gate.Name);

        var none = VerdictEvaluation.Evaluate([], _ => "x", floor: 1);
        Assert.AreEqual(MetricResult.NotMeasured(NotMeasuredReason.NoMeasurableQuery), none.ExactMatch);
        Assert.AreEqual(GateVerdict.NotMeasured, none.Gate.Verdict);

        var perfect = VerdictEvaluation.Evaluate(cases, caseId => cases.Single(value => value.CaseId == caseId).GoldVerdict, floor: 3);
        Assert.AreEqual(GateVerdict.Pass, perfect.Gate.Verdict);
        Assert.AreEqual(
            MetricResult.NotMeasured(NotMeasuredReason.StratumBelowFloor),
            VerdictEvaluation.Evaluate(cases, _ => "refuse", floor: 4).ExactMatch);
    }

    // ---- dates ----

    [TestMethod]
    public void ADateSelectsTheExpectedStateOnlyWhenTheStateKeyIsExactlyTheExpectedOne()
    {
        var day = new DateOnly(2024, 3, 1);
        var cases = new[]
        {
            new TemporalCase("t1", "w", day, "s1"),
            new TemporalCase("t2", "w", day.AddDays(40), "s2"),
            new TemporalCase("t3", "w", day.AddDays(80), "s3"),
        };
        string? Arm(string work, DateOnly asOf) => (asOf.DayNumber - day.DayNumber) switch
        {
            < 30 => "s1",
            < 60 => "S2",
            _ => null,
        };

        var report = TemporalEvaluation.Evaluate(cases, Arm, floor: 1);

        Assert.AreEqual(1.0 / 3.0, report.Exactness.Value!.Value, 1e-12, "a different case, and no state at all, are both wrong");
        Assert.AreEqual(GateVerdict.Fail, report.Gate.Verdict);
        Assert.AreEqual("temporal_exactness", report.Gate.Name);
        Assert.AreEqual(MetricResult.NotMeasured(NotMeasuredReason.NoMeasurableQuery), TemporalEvaluation.Evaluate([], Arm, floor: 1).Exactness);
        Assert.ThrowsExactly<ArgumentException>(() => TemporalEvaluation.Evaluate([cases[0], cases[0]], Arm, floor: 1));
    }
}
