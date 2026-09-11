using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The five states a referral date can be in, and what each one costs to reach.
/// </summary>
/// <remarks>
/// This is the composite the architectural disposition asked for, tested before either producer
/// exists, because the partitions are the part worth getting wrong cheaply. Its inputs are the four
/// requirements the disposition names; wiring them to real citations is a later slice and does not
/// change what these cases mean.
/// </remarks>
[TestClass]
public sealed class LuxembourgReferralDateResolutionTests
{
    private const string Draft = "http://data.legilux.public.lu/eli/dl/pl/2000/119";
    private const string Sace = "http://data.legilux.public.lu/eli/dl/pl/2000/119/evenement/sace/1";
    private const string Avce = "http://data.legilux.public.lu/eli/dl/pl/2000/119/evenement/avce/1";

    /// <summary>A complete traversal over a proven request that holds a date.</summary>
    [TestMethod]
    public void AProvenRequestHoldingADateIsAFact()
    {
        var step = Complete(["2000-06-14"]);

        Assert.AreEqual(LuxembourgReferralDateState.Fact, step.State);
        Assert.AreEqual(LuxembourgReferralEvidenceRefusal.None, step.Refusal);
        Assert.AreEqual(Sace, step.TargetIri);
        CollectionAssert.AreEqual(new[] { "2000-06-14" }, step.ReferralDateValues.ToArray());
    }

    /// <summary>
    /// Every distinct date survives; none is chosen.
    /// </summary>
    /// <remarks>
    /// A multi-valued property delivers a row per value. Picking one would be this family inventing
    /// a precedence the publisher never stated, and the draft graph already measured a subject
    /// carrying nine transposition targets, so multi-value is this corpus's normal case rather than
    /// a hypothetical.
    /// </remarks>
    [TestMethod]
    public void EveryDistinctDateSurvivesAndNoneIsChosen()
    {
        var step = Complete(["2000-06-14", "2000-09-01", "2001-03-22"]);

        Assert.AreEqual(LuxembourgReferralDateState.Fact, step.State);
        CollectionAssert.AreEqual(
            new[] { "2000-06-14", "2000-09-01", "2001-03-22" }, step.ReferralDateValues.ToArray(),
            "delivered order, every value, no selection.");
    }

    /// <summary>
    /// Silence from a proven, complete delivery is the one absence that may be derived.
    /// </summary>
    [TestMethod]
    public void AProvenRequestWithACompleteGraphAndNoDateIsADerivedAbsence()
    {
        var step = Complete([]);

        Assert.AreEqual(LuxembourgReferralDateState.DerivedAbsence, step.State);
        Assert.AreEqual(LuxembourgReferralEvidenceRefusal.None, step.Refusal);
        Assert.IsEmpty(step.ReferralDateValues);
    }

    /// <summary>A draft that reached nothing was never asked, so it is a gap.</summary>
    [TestMethod]
    public void ADraftThatReachedNothingIsADraftSideGapAndNotAnAbsence()
    {
        var step = LuxembourgReferralDateStep.DraftReachedNothing(Draft);

        Assert.AreEqual(LuxembourgReferralDateState.DraftSideGap, step.State);
        Assert.IsNull(step.TargetIri, "there is no target to name.");
        Assert.AreNotEqual(
            LuxembourgReferralDateState.DerivedAbsence, step.State,
            "1 of the 650 retained drafts is in this position; calling it an absence is a false one.");
    }

    /// <summary>
    /// AN EDGE TO ANOTHER OPINION CLASS IS ITS OWN STATE, and this is the partition most easily lost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// I did not have this state in my own design. It was added by the architectural disposition,
    /// and without it every <c>avce</c>, <c>avis</c> and <c>disp</c> edge would have been folded
    /// into "the class was never delivered" - which in the retained sample is roughly four edges in
    /// five, so the common case rather than the corner.
    /// </para>
    /// <para>
    /// It is not an absence: the draft reached something. It is not a contradiction:
    /// <c>hasOpinion</c> is supposed to reach several opinion classes. It is a role this run did not
    /// prove, and the target is named so a reader can see WHICH edge was unresolved.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnEdgeToAnotherOpinionClassIsATargetRoleGapCarryingItsTarget()
    {
        var step = LuxembourgReferralDateStep.Resolve(
            Draft, Avce,
            edgeRowKeyVerified: true,
            edgeBoundToDraftCitation: true,
            targetIsBareIri: true,
            targetInProvenInventory: false,
            requestGraphCitationComplete: true,
            deliveredTypeRowPresent: true,
            referralDateValues: []);

        Assert.AreEqual(LuxembourgReferralDateState.TargetRoleGap, step.State);
        Assert.AreEqual(
            LuxembourgReferralEvidenceRefusal.None, step.Refusal,
            "reaching another opinion class is a fact about the graph, not an evidence failure.");
        Assert.AreEqual(Avce, step.TargetIri, "the unresolved edge must be nameable.");
        Assert.AreNotEqual(
            LuxembourgReferralDateState.DraftSideGap, step.State,
            "the draft DID reach something; saying otherwise hides which edge was unresolved.");
    }

    /// <summary>A referral date asserted of the draft itself is drift, never a fact.</summary>
    [TestMethod]
    public void ADirectTripleOnTheDraftIsDrift()
    {
        var step = LuxembourgReferralDateStep.DirectTripleIsDrift(Draft);

        Assert.AreEqual(LuxembourgReferralDateState.Drift, step.State);
        Assert.IsEmpty(
            step.ReferralDateValues,
            "drift is retained as evidence of what arrived, and asserts nothing as a fact.");
    }

    /// <summary>
    /// Each of the four requirements refuses, with its own reason, and none of them reads as absence.
    /// </summary>
    /// <remarks>
    /// A custody or binding failure must never be reportable as a statement about what the publisher
    /// holds. Every one of these lands on a gap carrying a refusal, so "no fact" and "no evidence"
    /// stay distinguishable at the point a reader looks.
    /// </remarks>
    [TestMethod]
    [DataRow(false, true, true, true, true, LuxembourgReferralEvidenceRefusal.EdgeRowNotKeyVerified)]
    [DataRow(true, false, true, true, true, LuxembourgReferralEvidenceRefusal.EdgeNotBoundToItsDraftCitation)]
    [DataRow(true, true, false, true, true, LuxembourgReferralEvidenceRefusal.TargetNotABareIri)]
    [DataRow(true, true, true, false, true, LuxembourgReferralEvidenceRefusal.RequestGraphCitationIncomplete)]
    [DataRow(true, true, true, true, false, LuxembourgReferralEvidenceRefusal.DeliveredTypeRowMissing)]
    public void EveryUnmetRequirementRefusesWithItsOwnReason(
        bool keyVerified,
        bool boundToCitation,
        bool bareIri,
        bool citationComplete,
        bool typeRowPresent,
        LuxembourgReferralEvidenceRefusal expected)
    {
        var step = LuxembourgReferralDateStep.Resolve(
            Draft, Sace,
            edgeRowKeyVerified: keyVerified,
            edgeBoundToDraftCitation: boundToCitation,
            targetIsBareIri: bareIri,
            targetInProvenInventory: true,
            requestGraphCitationComplete: citationComplete,
            deliveredTypeRowPresent: typeRowPresent,
            referralDateValues: ["2000-06-14"]);

        Assert.AreEqual(expected, step.Refusal);
        Assert.AreEqual(
            LuxembourgReferralDateState.TargetRoleGap, step.State,
            "an evidence failure is an unproven role, never an absence.");
        Assert.IsEmpty(
            step.ReferralDateValues,
            "a date delivered under insufficient evidence is not carried as a fact.");
    }

    /// <summary>
    /// The delivered type row is required even though the query filtered on the class.
    /// </summary>
    /// <remarks>
    /// The filter says what was asked; the row says what the publisher answered. A broad-predicate
    /// graph over a mandatory-class subject necessarily returns that subject's own type triple, so
    /// requiring it costs nothing and closes the gap between the two.
    /// </remarks>
    [TestMethod]
    public void TheClassFilterAloneIsNotTypeEvidence()
    {
        var step = LuxembourgReferralDateStep.Resolve(
            Draft, Sace,
            edgeRowKeyVerified: true,
            edgeBoundToDraftCitation: true,
            targetIsBareIri: true,
            targetInProvenInventory: true,
            requestGraphCitationComplete: true,
            deliveredTypeRowPresent: false,
            referralDateValues: []);

        Assert.AreEqual(LuxembourgReferralEvidenceRefusal.DeliveredTypeRowMissing, step.Refusal);
        Assert.AreNotEqual(
            LuxembourgReferralDateState.DerivedAbsence, step.State,
            "without the delivered type row this is not a proven request, so its silence proves "
                + "nothing.");
    }

    /// <summary>Duplicate dates fail closed rather than being deduplicated here.</summary>
    [TestMethod]
    public void DuplicateDeliveredDatesFailClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Complete(["2000-06-14", "2000-06-14"]));
    }

    /// <summary>An empty lexical value is not a date this publisher sent.</summary>
    [TestMethod]
    public void AnEmptyDeliveredDateIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Complete(["2000-06-14", ""]));
    }

    /// <summary>Every state is reachable, so none of them is decorative.</summary>
    [TestMethod]
    public void EveryDeclaredStateIsReachable()
    {
        var reached = new[]
        {
            Complete(["2000-06-14"]).State,
            Complete([]).State,
            LuxembourgReferralDateStep.DraftReachedNothing(Draft).State,
            LuxembourgReferralDateStep.TargetRoleUnproven(Draft, Avce).State,
            LuxembourgReferralDateStep.DirectTripleIsDrift(Draft).State,
        };

        CollectionAssert.AreEquivalent(
            Enum.GetValues<LuxembourgReferralDateState>(), reached,
            "a state nothing can produce is a state nobody maintains.");
    }

    private static LuxembourgReferralDateStep Complete(string[] dates) =>
        LuxembourgReferralDateStep.Resolve(
            Draft, Sace,
            edgeRowKeyVerified: true,
            edgeBoundToDraftCitation: true,
            targetIsBareIri: true,
            targetInProvenInventory: true,
            requestGraphCitationComplete: true,
            deliveredTypeRowPresent: true,
            referralDateValues: dates);
}
