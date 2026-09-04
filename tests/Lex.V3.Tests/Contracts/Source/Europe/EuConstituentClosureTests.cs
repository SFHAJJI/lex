using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E4: the constituent closure over
/// <c>act_consolidated_based_on_resource_legal</c> and
/// <c>act_consolidated_consolidates_resource_legal</c>, with Candidate 5 R4's four typed refusals.
/// </summary>
/// <remarks>
/// Every identity here is built from CELEX numbers and is a shape fixture, not a transcription: no
/// retained probe in this slice reads a real consolidation chain. What is tested is the closure
/// rule, which is R4's, and the walk direction, which is v2's proven consolidation query shape.
/// Contract only.
/// </remarks>
[TestClass]
public sealed class EuConstituentClosureTests
{
    private static OfficialIdentitySet Act(string celex) =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, celex)]);

    private static OfficialIdentitySet ActWithEli(string celex, string eli) =>
        new(PublisherId.EuEurLex,
        [
            new OfficialIdentifier(FactsIdentifierFamily.Celex, celex),
            new OfficialIdentifier(FactsIdentifierFamily.Eli, eli),
        ]);

    private static readonly OfficialIdentitySet Root = Act("32016R0679");
    private static readonly OfficialIdentitySet First = Act("02016R0679-20160504");
    private static readonly OfficialIdentitySet Second = Act("02016R0679-20180525");

    private static EuConstituentStep Step(
        OfficialIdentitySet act,
        OfficialIdentitySet basedOn,
        OfficialIdentitySet consolidates,
        EuRelationTargetState state = EuRelationTargetState.Held) =>
        EuConstituentStep.Create(act, basedOn, consolidates, state);

    // --- The validated closure ------------------------------------------------------------------

    /// <summary>A chain whose steps close transitively under one root validates end to end.</summary>
    [TestMethod]
    public void AChainClosingTransitivelyUnderOneRootValidates()
    {
        var closure = EuConstituentClosure.Validate(
            Root,
            [Step(First, Root, Root), Step(Second, Root, First)]);

        Assert.IsTrue(closure.IsValidated);
        Assert.AreEqual(EuConstituentClosureRefusal.None, closure.Refusal);
        Assert.IsNull(closure.RefusedDetail);
        Assert.HasCount(2, closure.Chain);
        Assert.AreSame(Root, closure.Root);
    }

    /// <summary>An empty chain is a validated closure of no steps, not a refusal.</summary>
    [TestMethod]
    public void AnEmptyChainValidatesAsAClosureOfNoSteps()
    {
        var closure = EuConstituentClosure.Validate(Root, []);

        Assert.IsTrue(closure.IsValidated);
        Assert.IsEmpty(closure.Chain);
    }

    // --- R4's four refusals ---------------------------------------------------------------------

    /// <summary>R4 case one: a member that did not resolve blocks the whole closure.</summary>
    [TestMethod]
    public void AnUnresolvedMemberBlocks()
    {
        var closure = EuConstituentClosure.Validate(
            Root,
            [Step(First, Root, Root), Step(Second, Root, First, EuRelationTargetState.Unresolved)]);

        Assert.AreEqual(EuConstituentClosureRefusal.UnresolvedMember, closure.Refusal);
        StringAssert.Contains(closure.RefusedDetail!, "step 1");
    }

    /// <summary>R4 case two: a chain revisiting an act is cyclic and blocks.</summary>
    [TestMethod]
    public void ACyclicChainBlocks()
    {
        var closure = EuConstituentClosure.Validate(
            Root,
            [Step(First, Root, Root), Step(First, Root, First)]);

        Assert.AreEqual(EuConstituentClosureRefusal.CyclicChain, closure.Refusal);
        StringAssert.Contains(closure.RefusedDetail!, "step 1");
    }

    /// <summary>A step whose act is the root itself is also a revisit.</summary>
    [TestMethod]
    public void AStepReturningToTheRootIsCyclic()
    {
        var closure = EuConstituentClosure.Validate(Root, [Step(Root, Root, Root)]);

        Assert.AreEqual(EuConstituentClosureRefusal.CyclicChain, closure.Refusal);
    }

    /// <summary>
    /// R4 case three: a member based on an act sharing no identifier with the root is under a
    /// different root, and blocks.
    /// </summary>
    [TestMethod]
    public void ACrossRootMemberBlocks()
    {
        var closure = EuConstituentClosure.Validate(
            Root,
            [Step(First, Act("31995L0046"), Root)]);

        Assert.AreEqual(EuConstituentClosureRefusal.CrossRootMember, closure.Refusal);
        StringAssert.Contains(closure.RefusedDetail!, "sharing no identifier with the root");
    }

    /// <summary>
    /// R4 case four, first shape: a member overlapping the root's identity without matching it is
    /// evidence disagreeing with itself, and is separated from the cross-root case rather than
    /// smoothed over by a looser comparison.
    /// </summary>
    [TestMethod]
    public void AMemberOverlappingTheRootWithoutMatchingItIsAnUnexplainedMismatch()
    {
        var overlapping = ActWithEli("32016R0679", "http://data.europa.eu/eli/reg/2016/679/oj");
        var closure = EuConstituentClosure.Validate(Root, [Step(First, overlapping, Root)]);

        Assert.AreEqual(EuConstituentClosureRefusal.UnexplainedMismatch, closure.Refusal);
        StringAssert.Contains(closure.RefusedDetail!, "overlapping the root without matching it");
    }

    /// <summary>
    /// R4 case four, second shape: a step consolidating something other than the step before it
    /// leaves a hole in the chain, and blocks.
    /// </summary>
    [TestMethod]
    public void AStepConsolidatingSomethingOtherThanItsPredecessorIsAnUnexplainedMismatch()
    {
        var closure = EuConstituentClosure.Validate(
            Root,
            [Step(First, Root, Root), Step(Second, Root, Root)]);

        Assert.AreEqual(EuConstituentClosureRefusal.UnexplainedMismatch, closure.Refusal);
        StringAssert.Contains(closure.RefusedDetail!, "step 1");
    }

    /// <summary>Every declared refusal except <c>None</c> is reachable, so none is decorative.</summary>
    [TestMethod]
    public void EveryDeclaredRefusalIsReachable()
    {
        var reached = new[]
        {
            EuConstituentClosure.Validate(Root, [Step(First, Root, Root, EuRelationTargetState.Unresolved)]).Refusal,
            EuConstituentClosure.Validate(Root, [Step(First, Root, Root), Step(First, Root, First)]).Refusal,
            EuConstituentClosure.Validate(Root, [Step(First, Act("31995L0046"), Root)]).Refusal,
            EuConstituentClosure.Validate(Root, [Step(First, Root, Root), Step(Second, Root, Root)]).Refusal,
            EuConstituentClosure.Validate(Root, []).Refusal,
        };

        CollectionAssert.AreEquivalent(Enum.GetValues<EuConstituentClosureRefusal>(), reached.Distinct().ToArray());
    }

    // --- Blocking is structural, not advisory ---------------------------------------------------

    /// <summary>
    /// A refused closure has no chain to read. This is the one behaviour the type exists for: a
    /// partial consolidation history looks exactly like a complete one to every caller downstream.
    /// </summary>
    [TestMethod]
    public void ARefusedClosureRefusesToHandBackThePartOfTheChainThatDidValidate()
    {
        // The first step is valid; the second is not. A type reporting partial answers would hand
        // back the first step here.
        var closure = EuConstituentClosure.Validate(
            Root,
            [Step(First, Root, Root), Step(Second, Root, First, EuRelationTargetState.Unresolved)]);

        Assert.IsFalse(closure.IsValidated);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => _ = closure.Chain);
        StringAssert.Contains(error.Message, "partial or otherwise");
        StringAssert.Contains(error.Message, "UnresolvedMember");
    }

    /// <summary>
    /// The only way to mint a closure is <see cref="EuConstituentClosure.Validate"/>. Pinned
    /// structurally, because a second producer could hand out a closure whose chain was never
    /// checked, and visibility alone does not prevent one being added.
    /// </summary>
    [TestMethod]
    public void ValidateIsTheOnlyDoorThatMintsAClosure()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.EuConstituentClosure";
        const string Facts = "Lex.V3.Contracts.Facts.OfficialIdentitySet";

        CollectionAssert.AreEqual(
            new[]
            {
                // The two nullable parameters are the refusal path: a refused closure is built with
                // no chain and a detail string, and a validated one with a chain and no detail.
                "constructor private instance " + N + "::.ctor(" + Facts
                    + ", System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Europe."
                    + "EuConstituentStep>?, Lex.V3.Contracts.Source.Europe."
                    + "EuConstituentClosureRefusal, System.String?) -> " + N,
                "method private static " + N + "::Refuse(" + Facts
                    + ", Lex.V3.Contracts.Source.Europe.EuConstituentClosureRefusal, System.String) -> " + N,
                "method public static " + N + "::Validate(" + Facts
                    + ", System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Europe."
                    + "EuConstituentStep>) -> " + N,
            },
            ConstructionSurface.Of(typeof(EuConstituentClosure)).ToArray());

        Assert.IsEmpty(
            ConstructionSurface.ProducersIn(
                typeof(EuConstituentClosure).Assembly,
                typeof(EuConstituentClosure),
                includeNonPublic: true));
    }
}
