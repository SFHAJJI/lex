using System.Text.Json.Nodes;
using Lex.V3.Contracts.Evaluation;

namespace Lex.V3.Tests.Evaluation;

/// <summary>
/// The evaluation card: the bounds it prints beside a rate, which rows carry them, the strata its retrieval rows count,
/// and the statistical rows it marks "not yet labelled".
/// </summary>
[TestClass]
public sealed class EvaluationCardTests
{
    private static readonly ControlResult Caught = new(ShuffledControlNames.VerdictShuffle, ControlVerdict.CaughtTheShuffle, "fell", 7);

    [TestMethod]
    public void TheWilsonIntervalIsTheScoreIntervalAtNinetyFivePercent()
    {
        Assert.AreEqual((0.6756, 1.0), EvaluationCard.Wilson95(8, 8));
        Assert.AreEqual((0.0, 0.2775), EvaluationCard.Wilson95(0, 10));
        Assert.AreEqual((0.2366, 0.7634), EvaluationCard.Wilson95(5, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EvaluationCard.Wilson95(1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EvaluationCard.Wilson95(3, 2));
    }

    [TestMethod]
    public void TheRuleOfThreeBoundsTheFailureRateOfACleanSampleAndNeverExceedsOne()
    {
        Assert.AreEqual(0.03, EvaluationCard.RuleOfThreeUpper95(100));
        Assert.AreEqual(0.375, EvaluationCard.RuleOfThreeUpper95(8));
        Assert.AreEqual(1.0, EvaluationCard.RuleOfThreeUpper95(2));
    }

    [TestMethod]
    public void ARateCarriesItsIntervalAndAPerfectRateItsRuleOfThreeWhileNdcgAndAnUnmeasuredGateCarryNeither()
    {
        var perfect = new EvaluationCardGateRow(EvaluationGateNames.TemporalExactness, GateVerdict.Pass, 1.0, null, 1.0, 8);
        Assert.AreEqual((0.6756, 1.0), perfect.Wilson95);
        Assert.AreEqual(0.375, perfect.RuleOfThreeFailureUpper95);

        var failing = new EvaluationCardGateRow(EvaluationGateNames.VerdictExactMatch, GateVerdict.Fail, 0.5, null, 1.0, 10);
        Assert.AreEqual((0.2366, 0.7634), failing.Wilson95);
        Assert.IsNull(failing.RuleOfThreeFailureUpper95, "the rule of three bounds a sample with no failure");

        var ndcg = new EvaluationCardGateRow(EvaluationGateNames.AnchorNdcgAt10, GateVerdict.Pass, 1.0, null, 1.0, 9);
        Assert.IsFalse(ndcg.IsRate);
        Assert.IsNull(ndcg.Wilson95, "a graded mean is not a share of cases");
        Assert.IsNull(ndcg.RuleOfThreeFailureUpper95);

        var unmeasured = new EvaluationCardGateRow(
            EvaluationGateNames.NoHitAccuracy, GateVerdict.NotMeasured, null, NotMeasuredReason.StratumBelowFloor, 1.0, 1);
        Assert.IsNull(unmeasured.Wilson95);
        Assert.IsNull(unmeasured.RuleOfThreeFailureUpper95);
        var node = Gate(EvaluationCard.Render("t", [new EvaluationCardSet("s", "a", "d", 1, [unmeasured], Caught)]), 0, 0);
        Assert.AreEqual("not_measured", (string?)node["verdict"]);
        Assert.AreEqual("stratum_below_floor", (string?)node["not_measured_reason"]);
        Assert.IsNull(node["value"]);
        Assert.IsFalse(node.ContainsKey("wilson_95"));
    }

    [TestMethod]
    public void EachRetrievalRowCountsTheStratumTheHarnessMeasuresItOver()
    {
        EvaluationCase Case(string id, EvaluationCaseKind kind, params (string Anchor, int Grade)[] anchors) =>
            new(id, "lu", kind, new QueryJudgments(id, anchors.Select(anchor => new JudgedAnchor("w", anchor.Anchor, anchor.Grade)).ToArray()));
        var cases = new[]
        {
            Case("r1", EvaluationCaseKind.Retrieval, ("a1", 3)),
            Case("r2", EvaluationCaseKind.Retrieval, ("a2", 3), ("a3", 1)),
            Case("wrong-only", EvaluationCaseKind.Retrieval, ("a4", 0)),
            Case("none", EvaluationCaseKind.Retrieval),
            Case("x1", EvaluationCaseKind.ExactIdentifier, ("a5", 3)),
        };
        RetrievalArm arm = caseId => caseId switch
        {
            "r1" => [new RankedAnchor("w", "a1")],
            "r2" => [new RankedAnchor("w", "a2"), new RankedAnchor("w", "a3")],
            "x1" => [new RankedAnchor("w", "a5")],
            _ => [],
        };
        var report = RetrievalEvaluation.Evaluate(cases, arm, floor: 1, ndcgThreshold: 0.9);
        var set = EvaluationCard.Retrieval("arm", cases, report, 0.9, Caught);

        var n = set.Gates.ToDictionary(static row => row.Gate, static row => row.N);
        Assert.AreEqual(3, n[EvaluationGateNames.AnchorNdcgAt10], "the cases that judge an anchor relevant, the exact one included");
        Assert.AreEqual(2, n[EvaluationGateNames.NoHitAccuracy], "a case judging only a wrong provision is a no-hit case");
        Assert.AreEqual(1, n[EvaluationGateNames.ResolverExactness]);
        Assert.AreEqual(0.9, set.Gates.Single(static row => row.Gate == EvaluationGateNames.AnchorNdcgAt10).Threshold);
        Assert.AreEqual(5, set.CaseCount);
    }

    [TestMethod]
    public void TheCasesDigestNamesExactlyTheCasesMeasured()
    {
        VerdictReport Report(IReadOnlyList<VerdictCase> cases) => VerdictEvaluation.Evaluate(cases, static _ => "answer", floor: 1);
        VerdictCase[] one = [new("a", "answer"), new("b", "identifier_unknown")];
        VerdictCase[] same = [new("a", "answer"), new("b", "identifier_unknown")];
        VerdictCase[] other = [new("a", "answer"), new("b", "snapshot_unknown")];

        var digest = EvaluationCard.Verdict("refusal", "arm", one, Report(one), Caught).CasesSha256;
        Assert.AreEqual(64, digest.Length);
        Assert.AreEqual(digest, EvaluationCard.Verdict("refusal", "arm", same, Report(same), Caught).CasesSha256);
        Assert.AreNotEqual(digest, EvaluationCard.Verdict("refusal", "arm", other, Report(other), Caught).CasesSha256);
    }

    [TestMethod]
    public void EveryStatisticalRowIsNotYetLabelledAndTheControlsArePrintedLikeAnyOtherNumber()
    {
        var gate = new EvaluationCardGateRow(EvaluationGateNames.VerdictExactMatch, GateVerdict.Pass, 1.0, null, 1.0, 4);
        var card = EvaluationCard.Render("the fixture", [new EvaluationCardSet("refusal", "served", "d", 4, [gate], Caught)]);

        Assert.AreEqual(EvaluationCard.Schema, (string?)card["schema"]);
        var rows = card["statistical_rows"]!.AsArray();
        CollectionAssert.AreEqual(
            new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8" },
            rows.Select(static row => (string?)row!["dataset"]).ToArray());
        Assert.IsTrue(rows.All(static row => (string?)row!["status"] == EvaluationCard.NotYetLabelled));

        var control = card["shuffled_controls"]!.AsArray().Single()!;
        Assert.AreEqual("verdict_shuffle", (string?)control["control"]);
        Assert.AreEqual("caught_the_shuffle", (string?)control["verdict"]);
        Assert.AreEqual(7UL, (ulong?)control["seed"]);

        var node = Gate(card, 0, 0);
        Assert.AreEqual(0.75, (double?)node["rule_of_three_failure_upper_95"], "a pass on four cases bounds the failure rate only at 3 / 4");
        CollectionAssert.AreEqual(new double?[] { 0.5101, 1.0 }, node["wilson_95"]!.AsArray().Select(static value => (double?)value).ToArray());
    }

    private static JsonObject Gate(JsonObject card, int set, int gate) =>
        card["machine_gates"]!.AsArray()[set]!["gates"]!.AsArray()[gate]!.AsObject();
}
