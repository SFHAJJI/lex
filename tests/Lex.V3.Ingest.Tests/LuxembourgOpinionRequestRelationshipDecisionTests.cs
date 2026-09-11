namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The authorization gate and the cross-check rule, exercised without a publisher.
/// </summary>
/// <remarks>
/// <para>
/// THESE EXIST BECAUSE THE GATE WAS UNTESTABLE. The rule keeping the two conditionally authorized
/// requests unspent lived as an <c>if</c> inside a live test that is skipped by default. A reviewer
/// mutated it to <c>if (true)</c> and the whole suite stayed green: 744 total, 0 failed. Nothing in
/// CI could see a regression that spends requests an owner authorized only against a positive
/// identification.
/// </para>
/// <para>
/// So the decision moved out of the sending path, and the gate stopped being a branch: the canary
/// asks about <see cref="LuxembourgRelationshipDecision.CrossCheckTargets"/>, which is empty unless
/// exactly one candidate holds the class. There is no boolean left to invert, and the emptiness is
/// pinned here directly.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestRelationshipDecisionTests
{
    private const string SaceA = "http://data.legilux.public.lu/eli/dl/pl/2000/119/evenement/sace/1";
    private const string ScacA = "http://data.legilux.public.lu/eli/dl/pl/2000/119/evenement/scac/1";
    private const string SaceB = "http://data.legilux.public.lu/eli/dl/pc/2002/215/evenement/sace/1";
    private const string ScacB = "http://data.legilux.public.lu/eli/dl/pc/2002/215/evenement/scac/1";

    /// <summary>The measured first pair identifies hasOpinion and authorizes exactly two more.</summary>
    [TestMethod]
    public void ACleanSplitIdentifiesThePredicateAndAuthorizesTheCrossCheck()
    {
        var decision = First(sace: 1, scac: 0);

        Assert.AreEqual(
            LuxembourgOpinionRequestRelationship.HasOpinionPredicateIri, decision.IdentifiedPredicate);
        StringAssert.StartsWith(decision.Verdict, "IDENTIFIED");
        CollectionAssert.AreEqual(
            new[] { SaceB, ScacB }, decision.CrossCheckTargets.ToArray(),
            "the two authorized requests are named, in order, and nothing else is.");
    }

    /// <summary>The other way round is equally an identification, of the other predicate.</summary>
    [TestMethod]
    public void TheReverseSplitIdentifiesTheOtherPredicate()
    {
        var decision = First(sace: 0, scac: 1);

        Assert.AreEqual(
            LuxembourgOpinionRequestRelationship.DraftHasTaskPredicateIri, decision.IdentifiedPredicate);
        Assert.HasCount(2, decision.CrossCheckTargets);
    }

    /// <summary>
    /// EVERY ambiguous first pair authorizes NOTHING. This is the gate.
    /// </summary>
    /// <remarks>
    /// The cases are the four the owner named plus the two the range itself can produce: both
    /// candidates holding the class, neither holding it, a count a single-resource range cannot
    /// produce, and a request that did not complete. A change that let any of these hand back a
    /// target spends a request the owner did not authorize.
    /// </remarks>
    [TestMethod]
    [DataRow(1L, 1L, "both hold the class, so nothing is discriminated")]
    [DataRow(0L, 0L, "neither holds the class")]
    [DataRow(2L, 0L, "a single-resource range cannot answer 2")]
    [DataRow(1L, 7L, "nor 7")]
    [DataRow(-1L, 0L, "nor a negative")]
    public void NoAmbiguousFirstPairAuthorizesAnything(long sace, long scac, string because)
    {
        var decision = First(sace, scac);

        Assert.IsEmpty(
            decision.CrossCheckTargets,
            $"{because}: the two conditionally authorized requests must stay unspent.");
        Assert.IsNull(decision.IdentifiedPredicate, because);
        StringAssert.StartsWith(decision.Verdict, "AMBIGUOUS", because);
    }

    /// <summary>A request that did not complete is ambiguity, not a silent zero.</summary>
    [TestMethod]
    public void AFailedMembershipCheckAuthorizesNothing()
    {
        var decision = LuxembourgOpinionRequestRelationship.DecideFirstPair(
            new LuxembourgMembershipAnswer("A/sace", SaceA, null, "HttpRequestException: reset"),
            new LuxembourgMembershipAnswer("A/scac", ScacA, 0, null),
            SaceB, ScacB);

        Assert.IsEmpty(decision.CrossCheckTargets, "a failure cannot authorize further traffic.");
        Assert.IsNull(decision.IdentifiedPredicate);
    }

    /// <summary>An agreeing independent draft confirms, and says so.</summary>
    [TestMethod]
    public void AnAgreeingCrossCheckConfirmsTheIdentification()
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 1, scac: 0), Cross(sace: 1, scac: 0));

        StringAssert.StartsWith(concluded.Verdict, "CONFIRMED");
        Assert.AreEqual(
            LuxembourgOpinionRequestRelationship.HasOpinionPredicateIri, concluded.IdentifiedPredicate);
    }

    /// <summary>
    /// A reversing independent draft RETRACTS the identification rather than standing beside it.
    /// </summary>
    /// <remarks>
    /// The defect this pins: both cross-check answers were discarded, so the retained index kept the
    /// first pair's identification with a bare <c>crossCheckRun = true</c> next to it. A packet
    /// presented as an independently replicated relationship would have read identically had draft B
    /// said the opposite.
    /// </remarks>
    [TestMethod]
    public void AReversingCrossCheckRetractsTheIdentification()
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 1, scac: 0), Cross(sace: 0, scac: 1));

        StringAssert.StartsWith(concluded.Verdict, "CONTRADICTED");
        Assert.IsNull(
            concluded.IdentifiedPredicate,
            "a contradicted relationship identifies nothing; the first pair does not survive it.");
    }

    /// <summary>An independent draft that discriminates nothing is ambiguity, not confirmation.</summary>
    [TestMethod]
    [DataRow(1L, 1L)]
    [DataRow(0L, 0L)]
    [DataRow(3L, 0L)]
    public void ACrossCheckThatDiscriminatesNothingDoesNotConfirm(long sace, long scac)
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 1, scac: 0), Cross(sace, scac));

        StringAssert.StartsWith(concluded.Verdict, "AMBIGUOUS");
        Assert.IsNull(concluded.IdentifiedPredicate);
    }

    /// <summary>A failed cross-check does not leave the identification standing.</summary>
    [TestMethod]
    public void AFailedCrossCheckDoesNotConfirm()
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 1, scac: 0),
            [
                new LuxembourgMembershipAnswer("B/sace", SaceB, null, "TaskCanceledException: timeout"),
                new LuxembourgMembershipAnswer("B/scac", ScacB, 0, null),
            ]);

        StringAssert.StartsWith(concluded.Verdict, "AMBIGUOUS");
        Assert.IsNull(concluded.IdentifiedPredicate);
    }

    /// <summary>
    /// Cross-check answers arriving for an UNIDENTIFIED first pair are reported as a bypassed gate.
    /// </summary>
    /// <remarks>
    /// The backstop for the exact mutation that found this: if the sending path is ever changed to
    /// ask the second pair unconditionally, the conclusion refuses rather than quietly folding two
    /// unauthorized answers into a result.
    /// </remarks>
    [TestMethod]
    public void CrossCheckAnswersForAnUnidentifiedFirstPairAreRefused()
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 0, scac: 0), Cross(sace: 1, scac: 0));

        StringAssert.Contains(concluded.Verdict, "gate was bypassed");
        Assert.IsNull(concluded.IdentifiedPredicate);
    }

    /// <summary>A short cross-check is not a confirmation either.</summary>
    [TestMethod]
    public void AnIncompleteCrossCheckDoesNotConfirm()
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 1, scac: 0),
            [new LuxembourgMembershipAnswer("B/sace", SaceB, 1, null)]);

        StringAssert.StartsWith(concluded.Verdict, "AMBIGUOUS");
        Assert.IsNull(concluded.IdentifiedPredicate);
    }

    /// <summary>The retained run's own answers, replayed: 1/0 then 1/0 is a confirmation.</summary>
    /// <remarks>
    /// The measurement under
    /// <c>artifacts/e8-opinion-relationship-c2d099f7d72d4703bf37b3695c8a9ac7</c> is what this rule
    /// has to reproduce, so the rule is checked against it rather than only against invented cases.
    /// </remarks>
    [TestMethod]
    public void TheRetainedRunsOwnAnswersConcludeConfirmed()
    {
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(
            First(sace: 1, scac: 0), Cross(sace: 1, scac: 0));

        StringAssert.StartsWith(concluded.Verdict, "CONFIRMED");
        Assert.AreEqual(
            LuxembourgOpinionRequestRelationship.HasOpinionPredicateIri, concluded.IdentifiedPredicate,
            "hasOpinion reaches the resource carrying jolux:OpinionRequest.");
    }

    private static LuxembourgRelationshipDecision First(long sace, long scac) =>
        LuxembourgOpinionRequestRelationship.DecideFirstPair(
            new LuxembourgMembershipAnswer("A/sace", SaceA, sace, null),
            new LuxembourgMembershipAnswer("A/scac", ScacA, scac, null),
            SaceB, ScacB);

    private static LuxembourgMembershipAnswer[] Cross(long sace, long scac) =>
    [
        new LuxembourgMembershipAnswer("B/sace", SaceB, sace, null),
        new LuxembourgMembershipAnswer("B/scac", ScacB, scac, null),
    ];
}
