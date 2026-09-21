using Lex.V3.Contracts;
using Lex.V3.Contracts.Evaluation;

namespace Lex.V3.Tests.Evaluation;

/// <summary>
/// S4-A11, first slice: metrics that are nullable, and judged at the provision. Every expected value here is computed
/// by hand or by an independent script from the definition (gain over log2 of rank plus one), never by the code under
/// test, and the empty-denominator defect the V2 harness had (a stratum with nothing to measure scoring 1.0) is the
/// first thing pinned.
/// </summary>
[TestClass]
public sealed class RetrievalMetricsTests
{
    private const double Tolerance = 1e-12;

    private static readonly (string Work, string Anchor) A = ("work-1", "art-1");
    private static readonly (string Work, string Anchor) B = ("work-1", "art-2");
    private static readonly (string Work, string Anchor) C = ("work-2", "art-9");
    private static readonly (string Work, string Anchor) X = ("work-3", "art-7");

    private static QueryJudgments Judged(params ((string Work, string Anchor) Item, int Grade)[] entries) =>
        new("query-1", entries.Select(entry => new JudgedAnchor(entry.Item.Work, entry.Item.Anchor, entry.Grade)).ToArray());

    private static RankedAnchor[] Ranked(params (string Work, string Anchor)[] items) =>
        items.Select(item => new RankedAnchor(item.Work, item.Anchor)).ToArray();

    private static void AssertMeasured(double expected, MetricResult actual)
    {
        Assert.IsTrue(actual.IsMeasured, $"expected {expected}, got not_measured ({actual.Reason})");
        Assert.AreEqual(expected, actual.Value!.Value, Tolerance);
        Assert.IsNull(actual.Reason);
    }

    // ---- nDCG ----

    [TestMethod]
    public void ARankingThatReproducesTheIdealOrderScoresOne()
    {
        AssertMeasured(1.0, RetrievalMetrics.NdcgAtK(Judged((A, 3), (B, 1)), Ranked(A, B), 10));
    }

    [TestMethod]
    public void AnUnjudgedHitAheadOfTheJudgedOnesCostsExactlyItsPositionDiscount()
    {
        // DCG = 0/log2(2) + 3/log2(3) + 1/log2(4) = 2.3927892607143724; ideal = 3 + 1/log2(3) = 3.6309297535714578.
        AssertMeasured(0.6590018048024133, RetrievalMetrics.NdcgAtK(Judged((A, 3), (B, 1)), Ranked(X, A, B), 10));
    }

    [TestMethod]
    public void TheHigherGradeBelongsFirst()
    {
        // DCG = 1/log2(2) + 3/log2(3) = 2.8927892607143724 over the same ideal.
        AssertMeasured(0.7967075809905066, RetrievalMetrics.NdcgAtK(Judged((A, 3), (B, 1)), Ranked(B, A), 10));
    }

    [TestMethod]
    public void ARepeatedAnchorEarnsItsGainOnce()
    {
        // DCG = 3/log2(2) + 0 + 1/log2(4) = 3.5, so a second sight of A adds nothing.
        AssertMeasured(0.9639404333166532, RetrievalMetrics.NdcgAtK(Judged((A, 3), (B, 1)), Ranked(A, A, B), 10));
    }

    [TestMethod]
    public void HitsBeyondKEarnNothingAndAHitInsideItEarnsItsDiscount()
    {
        var judgments = Judged((A, 3));

        AssertMeasured(0.0, RetrievalMetrics.NdcgAtK(judgments, Ranked(X, X, A), 2));
        AssertMeasured(0.5, RetrievalMetrics.NdcgAtK(judgments, Ranked(X, X, A), 3));
    }

    [TestMethod]
    public void TheIdealIsTruncatedAtKToo()
    {
        // Three judged anchors, k = 2: ideal = 3 + 3/log2(3); ranking [C(1), A(3)] gives 1 + 3/log2(3).
        var judgments = Judged((A, 3), (B, 3), (C, 1));

        AssertMeasured(0.5912352048230277, RetrievalMetrics.NdcgAtK(judgments, Ranked(C, A, B), 2));
    }

    [TestMethod]
    public void NothingRetrievedForAJudgedQueryScoresZeroAndIsNotNull()
    {
        AssertMeasured(0.0, RetrievalMetrics.NdcgAtK(Judged((A, 3)), [], 10));
    }

    [TestMethod]
    public void AGradeOnlyJudgmentIsStillMeasurable()
    {
        AssertMeasured(1.0, RetrievalMetrics.NdcgAtK(Judged((B, 1)), Ranked(B), 10));
    }

    /// <summary>
    /// The V2 defect: <c>rankingCount == 0 ? 1 : ...</c>. A query with nothing judged relevant has no ideal to compare
    /// with, so its nDCG is undefined, and it is not 1.0 whether or not the ranking is empty.
    /// </summary>
    [TestMethod]
    public void AQueryWithNothingJudgedRelevantIsNotMeasuredAndNeverScoresOne()
    {
        var expected = MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment);

        Assert.AreEqual(expected, RetrievalMetrics.NdcgAtK(Judged(), [], 10));
        Assert.AreEqual(expected, RetrievalMetrics.NdcgAtK(Judged(), Ranked(A, B), 10));
        Assert.AreEqual(expected, RetrievalMetrics.NdcgAtK(Judged((A, 0)), [], 10));
        Assert.AreEqual(expected, RetrievalMetrics.NdcgAtK(Judged((A, 0)), Ranked(A), 10));
    }

    /// <summary>
    /// Provision-level relevance, the reason qrels moved from works to (work, anchor) pairs: a wrong article inside the
    /// right work earned full credit in V2 and earns nothing here, and an anchor id means nothing outside its work.
    /// </summary>
    [TestMethod]
    public void AWrongArticleInTheRightWorkEarnsNothingAndAnAnchorIdBelongsToItsWork()
    {
        var judgments = Judged((A, 3));

        foreach (var ranking in new[] { Ranked(("work-1", "art-99")), Ranked(("work-2", "art-1")) })
        {
            AssertMeasured(0.0, RetrievalMetrics.NdcgAtK(judgments, ranking, 10));
            AssertMeasured(0.0, RetrievalMetrics.RecallAtK(judgments, ranking, 10));
            AssertMeasured(0.0, RetrievalMetrics.ReciprocalRank(judgments, ranking));
        }
    }

    [TestMethod]
    public void TwoWorksMayEachJudgeTheSameAnchorIdAndBothAreEarned()
    {
        var judgments = Judged((("work-1", "art-1"), 3), (("work-2", "art-1"), 3));
        var ranking = Ranked(("work-1", "art-1"), ("work-2", "art-1"));

        AssertMeasured(1.0, RetrievalMetrics.NdcgAtK(judgments, ranking, 10));
        AssertMeasured(1.0, RetrievalMetrics.RecallAtK(judgments, ranking, 10));
    }

    // ---- recall ----

    [TestMethod]
    public void RecallCountsOnlyTheSupportingAnchorsInsideK()
    {
        // C is grade 1, so finding it is context and not a win; B sits at rank 4, beyond k = 3.
        var judgments = Judged((A, 3), (B, 3), (C, 1));

        AssertMeasured(0.5, RetrievalMetrics.RecallAtK(judgments, Ranked(A, C, X, B), 3));
        AssertMeasured(1.0, RetrievalMetrics.RecallAtK(judgments, Ranked(A, C, X, B), 4));
    }

    [TestMethod]
    public void ARepeatedSupportingAnchorIsFoundOnce()
    {
        AssertMeasured(0.5, RetrievalMetrics.RecallAtK(Judged((A, 3), (B, 3)), Ranked(A, A), 2));
    }

    [TestMethod]
    public void RecallOfNothingRetrievedIsZeroAndRecallWithNoSupportingAnchorIsNotMeasured()
    {
        AssertMeasured(0.0, RetrievalMetrics.RecallAtK(Judged((A, 3)), [], 10));

        var none = MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment);
        Assert.AreEqual(none, RetrievalMetrics.RecallAtK(Judged((C, 1)), Ranked(C), 10));
        Assert.AreEqual(none, RetrievalMetrics.RecallAtK(Judged(), [], 10));
    }

    // ---- reciprocal rank ----

    [TestMethod]
    public void TheReciprocalRankIsOneOverTheRankOfTheFirstSupportingAnchor()
    {
        var judgments = Judged((A, 3));

        AssertMeasured(1.0, RetrievalMetrics.ReciprocalRank(judgments, Ranked(A)));
        AssertMeasured(0.5, RetrievalMetrics.ReciprocalRank(judgments, Ranked(X, A)));
        AssertMeasured(1.0 / 3.0, RetrievalMetrics.ReciprocalRank(judgments, Ranked(X, X, A)));
        AssertMeasured(0.0, RetrievalMetrics.ReciprocalRank(judgments, Ranked(X, C)));
        AssertMeasured(0.0, RetrievalMetrics.ReciprocalRank(judgments, []));
    }

    [TestMethod]
    public void TheReciprocalRankIsNotMeasuredWhereNothingOfTheGradeIsJudgedAndMeasurableWhenTheGradeIsLowered()
    {
        Assert.AreEqual(
            MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment),
            RetrievalMetrics.ReciprocalRank(Judged((B, 1)), Ranked(B)));
        AssertMeasured(1.0, RetrievalMetrics.ReciprocalRank(Judged((B, 1)), Ranked(B), minimumGrade: 1));
    }

    // ---- aggregation ----

    [TestMethod]
    public void AStratumMeanIsOverTheMeasuredQueriesOnlyAndANotMeasuredOneIsNeitherZeroNorOne()
    {
        var queries = new[]
        {
            MetricResult.Measured(1.0),
            MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment),
            MetricResult.Measured(0.5),
        };

        AssertMeasured(0.75, RetrievalMetrics.Mean(queries, floor: 2));
        Assert.AreEqual(
            MetricResult.NotMeasured(NotMeasuredReason.StratumBelowFloor), RetrievalMetrics.Mean(queries, floor: 3));
    }

    /// <summary>The same V2 defect at the stratum: nothing to average is not a perfect score.</summary>
    [TestMethod]
    public void AStratumWithNothingMeasurableIsNotMeasuredAndNeverScoresOne()
    {
        var none = MetricResult.NotMeasured(NotMeasuredReason.NoMeasurableQuery);

        Assert.AreEqual(none, RetrievalMetrics.Mean([], floor: 1));
        Assert.AreEqual(
            none,
            RetrievalMetrics.Mean(
                [MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment), MetricResult.NotMeasured(NotMeasuredReason.NoRelevantJudgment)],
                floor: 1));
    }

    // ---- gates ----

    [TestMethod]
    public void AGatePassesAtItsThresholdFailsBelowItAndIsNotMeasuredWhereItsMetricIs()
    {
        Assert.AreEqual(GateVerdict.Pass, EvaluationGates.AtLeast("g", MetricResult.Measured(0.95), 0.95).Verdict);
        Assert.AreEqual(GateVerdict.Fail, EvaluationGates.AtLeast("g", MetricResult.Measured(0.9499), 0.95).Verdict);

        var unmeasured = EvaluationGates.AtLeast(
            "anchor-ndcg", MetricResult.NotMeasured(NotMeasuredReason.StratumBelowFloor), 0.95);
        Assert.AreEqual(GateVerdict.NotMeasured, unmeasured.Verdict);
        Assert.AreEqual(NotMeasuredReason.StratumBelowFloor, unmeasured.Reason);
        Assert.AreEqual("anchor-ndcg", unmeasured.Name);
        Assert.IsNull(EvaluationGates.AtLeast("g", MetricResult.Measured(1.0), 0.5).Reason);
    }

    [TestMethod]
    public void AReleaseGateSetPassesOnlyWhenItIsNotEmptyAndEveryGatePasses()
    {
        var pass = EvaluationGates.AtLeast("a", MetricResult.Measured(1.0), 0.5);
        var fail = EvaluationGates.AtLeast("b", MetricResult.Measured(0.1), 0.5);
        var unmeasured = EvaluationGates.AtLeast("c", MetricResult.NotMeasured(NotMeasuredReason.NoMeasurableQuery), 0.5);

        Assert.IsTrue(EvaluationGates.ReleasePasses([pass]));
        Assert.IsTrue(EvaluationGates.ReleasePasses([pass, pass]));
        Assert.IsFalse(EvaluationGates.ReleasePasses([]), "a gate set that would pass on nothing is a blind spot");
        Assert.IsFalse(EvaluationGates.ReleasePasses([pass, fail]));
        Assert.IsFalse(EvaluationGates.ReleasePasses([pass, unmeasured]), "a null the gate reads blocks like a failure");
    }

    // ---- what the types refuse ----

    [TestMethod]
    public void JudgmentsRefuseGradesOutsideTheClosedSetDuplicatesAndBlankIdentities()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JudgedAnchor("w", "a", 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JudgedAnchor("w", "a", -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JudgedAnchor("w", "a", 4));
        Assert.ThrowsExactly<ArgumentException>(() => new JudgedAnchor("", "a", 3));
        Assert.ThrowsExactly<ArgumentException>(() => new JudgedAnchor("w", " ", 3));
        Assert.ThrowsExactly<ArgumentException>(() => new RankedAnchor("", "a"));
        Assert.ThrowsExactly<ArgumentException>(() => new QueryJudgments("", []));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new QueryJudgments("q", [new JudgedAnchor("w", "a", 3), new JudgedAnchor("w", "a", 1)]));

        foreach (var grade in new[] { 0, 1, 3 })
        {
            Assert.AreEqual(grade, new JudgedAnchor("w", "a", grade).Grade);
        }
    }

    [TestMethod]
    public void MetricsRefuseAnUnusableCutoffFloorOrGradeAndResultsRefuseValuesOutsideZeroToOne()
    {
        var judgments = Judged((A, 3));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetrievalMetrics.NdcgAtK(judgments, [], 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetrievalMetrics.RecallAtK(judgments, [], 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetrievalMetrics.ReciprocalRank(judgments, [], minimumGrade: 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetrievalMetrics.Mean([], floor: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MetricResult.Measured(1.0000001));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MetricResult.Measured(-0.0000001));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MetricResult.Measured(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MetricResult.NotMeasured((NotMeasuredReason)0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EvaluationGates.AtLeast("g", MetricResult.Measured(0.5), 1.5));
        Assert.ThrowsExactly<ArgumentException>(() => EvaluationGates.AtLeast("", MetricResult.Measured(0.5), 0.5));
        Assert.ThrowsExactly<ArgumentException>(() => new GateResult("g", GateVerdict.Pass, NotMeasuredReason.NoMeasurableQuery));
        Assert.ThrowsExactly<ArgumentException>(() => new GateResult("g", GateVerdict.Fail, NotMeasuredReason.NoMeasurableQuery));
        Assert.ThrowsExactly<ArgumentException>(() => new GateResult("g", GateVerdict.NotMeasured, null));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GateResult("g", (GateVerdict)0, null));
    }

    // ---- the closed vocabulary ----

    [TestMethod]
    public void TheVerdictAndReasonVocabulariesAreTheClosedNamedOnes()
    {
        Assert.AreEqual("\"pass\"", ContractJson.Serialize(GateVerdict.Pass));
        Assert.AreEqual("\"fail\"", ContractJson.Serialize(GateVerdict.Fail));
        Assert.AreEqual("\"not_measured\"", ContractJson.Serialize(GateVerdict.NotMeasured));

        Assert.AreEqual("\"no_relevant_judgment\"", ContractJson.Serialize(NotMeasuredReason.NoRelevantJudgment));
        Assert.AreEqual("\"no_measurable_query\"", ContractJson.Serialize(NotMeasuredReason.NoMeasurableQuery));
        Assert.AreEqual("\"stratum_below_floor\"", ContractJson.Serialize(NotMeasuredReason.StratumBelowFloor));

        Assert.HasCount(3, Enum.GetValues<GateVerdict>());
        Assert.HasCount(3, Enum.GetValues<NotMeasuredReason>());
    }
}
