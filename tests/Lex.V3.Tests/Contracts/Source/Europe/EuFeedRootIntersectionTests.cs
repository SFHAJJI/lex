using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// <c>eu_feed_root_intersection/1</c>: R3 line 411 and R7 line 739, the total classifier that
/// gives every feed entry exactly one of the four terminals, and R3's terminal-count equation.
///
/// This contract does not resolve a feed entry's Work roots or derive the pack itself; both are
/// declared inputs bound by artifact digest, because no bounded observation covers either
/// question. What it owns is the arithmetic once those inputs are supplied: the intersection, the
/// four-way classification, and the reconciliation that R3 requires against the canonical entry
/// count. Every refusal below is driven to its own branch and none stands for an observation this
/// repository has not taken.
/// </summary>
[TestClass]
public sealed class EuFeedRootIntersectionTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";

    private const string RootIn = "http://publications.europa.eu/resource/celex/32016R0679";
    private const string RootAlsoIn = "http://publications.europa.eu/resource/celex/32019R0452";
    private const string RootOut = "http://publications.europa.eu/resource/celex/99999999";

    [TestMethod]
    public void TheBindingHasExactlyOneConstructionPath()
    {
        // Like the plan (EuWatermarkWitnessPlanTests.ThePlanHasExactlyOneConstructionPath), this
        // type carries a private static readonly UTF8Encoding for its digest, which the compiler
        // gives a static constructor. ConstructionSurface pins every constructor the type
        // declares without distinguishing instance from static, so it is pinned rather than
        // filtered.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuFeedRootIntersection::.ctor("
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedFamilyProjection>, "
                + "System.Collections.Generic.HashSet<System.String>, "
                + "System.Collections.Generic.HashSet<" + N + "EuFeedFamilyProjection>, "
                + "System.String) -> " + N + "EuFeedRootIntersection",
                "constructor private static " + N + "EuFeedRootIntersection::.cctor() -> " + N
                + "EuFeedRootIntersection",
                "method public static " + N + "EuFeedRootIntersection::TryBind("
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedFamilyProjection>, out "
                + N + "EuFeedIntersectionRefusal&) -> " + N + "EuFeedRootIntersection",
            },
            ConstructionSurface.Of(typeof(EuFeedRootIntersection)).ToArray());
    }

    [TestMethod]
    public void TheEntrySetHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuFeedWatermarkEntrySet::.ctor("
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuWatermarkCursor>, "
                + "System.Collections.Generic.HashSet<" + N + "EuWatermarkCursor>) -> " + N
                + "EuFeedWatermarkEntrySet",
                "method public static " + N + "EuFeedWatermarkEntrySet::TryClose("
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuWatermarkTraversalStep>, "
                + "out " + N + "EuFeedEntrySetRefusal&) -> " + N + "EuFeedWatermarkEntrySet",
            },
            ConstructionSurface.Of(typeof(EuFeedWatermarkEntrySet)).ToArray());
    }

    [TestMethod]
    public void TheObservationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuFeedEntryObservation::.ctor(" + N
                + "EuWatermarkCursor, System.Boolean, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedFamilyProjection>) -> "
                + N + "EuFeedEntryObservation",
                "method public static " + N + "EuFeedEntryObservation::TryObserve(" + N
                + "EuWatermarkCursor, System.Boolean, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedFamilyProjection>, out "
                + N + "EuFeedObservationRefusal&) -> " + N + "EuFeedEntryObservation",
            },
            ConstructionSurface.Of(typeof(EuFeedEntryObservation)).ToArray());
    }

    [TestMethod]
    public void TheTerminationHasExactlyOneConstructionPath()
    {
        // No TryX here: a termination is never refused, because Classify is total. Both internal
        // factories are the door instead, and internal is a friend door in this assembly, which is
        // exactly why ConstructionSurface.Of is what pins it rather than a public/private count.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuFeedEntryTermination::.ctor(" + N
                + "EuFeedTerminal, " + N + "EuFeedEntryObservation, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedFamilyProjection>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedFamilyProjection>, " + N
                + "EuFeedUnresolvedCause, " + N + "EuFeedOutOfPackReason) -> " + N
                + "EuFeedEntryTermination",
                "method internal static " + N + "EuFeedEntryTermination::Resolved(" + N
                + "EuFeedTerminal, " + N + "EuFeedEntryObservation, System.String[], "
                + "System.String[], " + N + "EuFeedFamilyProjection[], " + N
                + "EuFeedFamilyProjection[]) -> " + N + "EuFeedEntryTermination",
                "method internal static " + N + "EuFeedEntryTermination::Unresolved(" + N
                + "EuFeedEntryObservation, " + N + "EuFeedUnresolvedCause) -> " + N
                + "EuFeedEntryTermination",
            },
            ConstructionSurface.Of(typeof(EuFeedEntryTermination)).ToArray());
    }

    [TestMethod]
    public void TheReconciliationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuFeedTerminalReconciliation::.ctor("
                + "System.Collections.Generic.IReadOnlyDictionary<" + N
                + "EuFeedTerminal, System.Int32>, "
                + "System.Collections.Generic.IReadOnlyDictionary<" + N
                + "EuFeedReconciliationConflict, System.Int32>, System.Int32) -> " + N
                + "EuFeedTerminalReconciliation",
                "method public static " + N + "EuFeedTerminalReconciliation::Of(" + N
                + "EuFeedRootIntersection, " + N + "EuFeedWatermarkEntrySet, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedEntryTermination>) -> "
                + N + "EuFeedTerminalReconciliation",
            },
            ConstructionSurface.Of(typeof(EuFeedTerminalReconciliation)).ToArray());
    }

    [TestMethod]
    public void TheTerminalVocabularyIsClosedAndSpelledForTheWire()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"eu_feed_positive_in_pack\"",
                "\"eu_feed_positive_out_of_pack\"",
                "\"eu_feed_positive_mixed_scope\"",
                "\"eu_feed_positive_unresolved_or_ambiguous\"",
            },
            Enum.GetValues<EuFeedTerminal>().Select(value => ContractJson.Serialize(value))
                .ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"identity_resolution_did_not_close\"",
                "\"watermark_membership_did_not_close\"",
                "\"family_projection_did_not_close\"",
                "\"partition_did_not_close\"",
            },
            Enum.GetValues<EuFeedUnresolvedCause>().Select(value => ContractJson.Serialize(value))
                .ToArray());

        CollectionAssert.AreEqual(
            new[] { "\"none\"", "\"not_a_member_of_the_discovered_pack_root_set\"" },
            Enum.GetValues<EuFeedOutOfPackReason>().Select(value => ContractJson.Serialize(value))
                .ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"pack_root_set_empty\"",
                "\"pack_root_blank\"",
                "\"pack_root_repeated\"",
                "\"discovered_family_row_outside_the_pack\"",
            },
            Enum.GetValues<EuFeedIntersectionRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"canonical_entry_repeated\"",
                "\"traversal_steps_do_not_share_one_plan\"",
            },
            Enum.GetValues<EuFeedEntrySetRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"resolved_work_root_blank\"",
                "\"resolved_work_root_repeated\"",
                "\"unresolved_observation_carries_resolution_output\"",
                "\"projection_key_blank\"",
            },
            Enum.GetValues<EuFeedObservationRefusal>()
                .Select(value => ContractJson.Serialize(value)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "\"unresolved_or_ambiguous_terminal\"",
                "\"projection_missing_from_its_discovered_family\"",
                "\"duplicate_terminal_accounting\"",
                "\"entry_without_a_terminal\"",
                "\"terminal_outside_the_canonical_entry_set\"",
            },
            Enum.GetValues<EuFeedReconciliationConflict>()
                .Select(value => ContractJson.Serialize(value)).ToArray());
    }

    // ---- EuFeedRootIntersection.TryBind refusals, each driven to its own branch. ----

    [TestMethod]
    public void AnEmptyPackRootSetIsRefused()
    {
        var binding = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [], [], out var refusal);

        Assert.IsNull(binding);
        Assert.AreEqual(EuFeedIntersectionRefusal.PackRootSetEmpty, refusal);
    }

    [TestMethod]
    public void ABlankPackRootIsRefused()
    {
        var binding = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [RootIn, "   "], [], out var refusal);

        Assert.IsNull(binding);
        Assert.AreEqual(EuFeedIntersectionRefusal.PackRootBlank, refusal);
    }

    [TestMethod]
    public void ARepeatedPackRootIsRefused()
    {
        var binding = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [RootIn, RootIn], [], out var refusal);

        Assert.IsNull(binding);
        Assert.AreEqual(EuFeedIntersectionRefusal.PackRootRepeated, refusal);
    }

    [TestMethod]
    public void ADiscoveredFamilyRowNamingARootOutsideThePackIsRefused()
    {
        var binding = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [RootIn],
            [new EuFeedFamilyProjection(RootOut, "family-a", "projected-key-1")],
            out var refusal);

        Assert.IsNull(binding);
        Assert.AreEqual(EuFeedIntersectionRefusal.DiscoveredFamilyRowOutsideThePack, refusal);
    }

    [TestMethod]
    public void ABindingThatDoesRefuseCarriesNoBindingIdentity()
    {
        // A binding is the only door that fixes P. Nothing about a refusal can produce one, so a
        // caller who ignores the null cannot go on to classify against a half-built pack.
        var binding = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [], [], out var refusal);

        Assert.IsNull(binding);
        Assert.AreNotEqual(EuFeedIntersectionRefusal.None, refusal);
    }

    [TestMethod]
    public void AWellFormedBindingSortsThePackAndRecordsAStableDigest()
    {
        var first = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [RootOut, RootIn],
            [new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1")],
            out var refusal);
        Assert.IsNotNull(first, $"refused as {refusal}");

        CollectionAssert.AreEqual(
            new[] { RootIn, RootOut }.OrderBy(v => v, StringComparer.Ordinal).ToArray(),
            first.DiscoveredPackRoots.ToArray());
        Assert.IsTrue(first.PackContains(RootIn));
        Assert.IsTrue(first.PackContains(RootOut));
        Assert.IsFalse(first.PackContains(RootAlsoIn));
        Assert.IsTrue(first.DiscoveredFamilyContains(
            new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1")));
        Assert.IsFalse(first.DiscoveredFamilyContains(
            new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-other")));
        Assert.AreEqual(64, first.BindingIdentityDigest.Length);
        Assert.IsTrue(first.BindingIdentityDigest.All(
            c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));

        // Same inputs, different pack order: the digest is over the sorted pack, not caller order.
        var second = EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(),
            [RootIn, RootOut],
            [new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1")],
            out var secondRefusal);
        Assert.IsNotNull(second, $"refused as {secondRefusal}");
        Assert.AreEqual(first.BindingIdentityDigest, second.BindingIdentityDigest);
    }

    // ---- EuFeedWatermarkEntrySet.TryClose refusals. ----

    [TestMethod]
    public void ARepeatedCanonicalEntryIsRefused()
    {
        var plan = Plan(2);
        var step = SingleEntryStep(plan, 1);

        var entries = EuFeedWatermarkEntrySet.TryClose([step, step], out var refusal);

        Assert.IsNull(entries);
        Assert.AreEqual(EuFeedEntrySetRefusal.CanonicalEntryRepeated, refusal);

        // This test does not, on its own, distinguish a genuine repeated-entry check from one
        // deleted outright: CanonicalEntryRepeated is the only refusal value TryClose's whole
        // duplicate path can produce, so a mutation that disables the check and one that merely
        // shifts where it fires both surface here as some other, unrelated assertion breaking
        // instead of this one going green. That is not a gap in this test; it is why the mutation
        // that actually removes the duplicate check is killed by seventeen other tests across the
        // file rather than by this one, and why that is recorded rather than treated as silence.
    }

    [TestMethod]
    public void StepsFromTwoDifferentPlansAreRefused()
    {
        var planA = Plan(2);
        var planB = Plan(4);
        var stepA = SingleEntryStep(planA, 1);
        var stepB = SingleEntryStep(planB, 2);

        var entries = EuFeedWatermarkEntrySet.TryClose([stepA, stepB], out var refusal);

        Assert.IsNull(entries);
        Assert.AreEqual(EuFeedEntrySetRefusal.TraversalStepsDoNotShareOnePlan, refusal);
    }

    [TestMethod]
    public void AnEmptyTraversalIsAValidEmptyEntrySet()
    {
        var entries = EuFeedWatermarkEntrySet.TryClose([], out var refusal);

        Assert.IsNotNull(entries, $"refused as {refusal}");
        Assert.AreEqual(0, entries.Count);
    }

    // ---- EuFeedEntryObservation.TryObserve refusals, each branch driven independently. ----

    [TestMethod]
    public void ABlankResolvedWorkRootIsRefused()
    {
        var observation = EuFeedEntryObservation.TryObserve(
            At(1), identityResolutionClosed: true, [" "], [], out var refusal);

        Assert.IsNull(observation);
        Assert.AreEqual(EuFeedObservationRefusal.ResolvedWorkRootBlank, refusal);
    }

    [TestMethod]
    public void ARepeatedResolvedWorkRootIsRefused()
    {
        var observation = EuFeedEntryObservation.TryObserve(
            At(1), identityResolutionClosed: true, [RootIn, RootIn], [], out var refusal);

        Assert.IsNull(observation);
        Assert.AreEqual(EuFeedObservationRefusal.ResolvedWorkRootRepeated, refusal);
    }

    [TestMethod]
    public void AnUnresolvedObservationCarryingRootsIsRefused()
    {
        var observation = EuFeedEntryObservation.TryObserve(
            At(1), identityResolutionClosed: false, [RootIn], [], out var refusal);

        Assert.IsNull(observation);
        Assert.AreEqual(
            EuFeedObservationRefusal.UnresolvedObservationCarriesResolutionOutput, refusal);
    }

    [TestMethod]
    public void AnUnresolvedObservationCarryingOnlyProjectionsIsRefused()
    {
        // Roots are empty here, so only the second guard - the one over projections - can be the
        // one that catches this. A mutant deleting that second check and keeping only the roots
        // check would let this through, which is exactly what this test is for.
        var observation = EuFeedEntryObservation.TryObserve(
            At(1),
            identityResolutionClosed: false,
            [],
            [new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1")],
            out var refusal);

        Assert.IsNull(observation);
        Assert.AreEqual(
            EuFeedObservationRefusal.UnresolvedObservationCarriesResolutionOutput, refusal);
    }

    [TestMethod]
    public void ABlankFamilyMemberKeyIsRefused()
    {
        var observation = EuFeedEntryObservation.TryObserve(
            At(1),
            identityResolutionClosed: true,
            [RootIn],
            [new EuFeedFamilyProjection(RootIn, "  ", "projected-key-1")],
            out var refusal);

        Assert.IsNull(observation);
        Assert.AreEqual(EuFeedObservationRefusal.ProjectionKeyBlank, refusal);
    }

    [TestMethod]
    public void ABlankProjectedKeyIsRefused()
    {
        // FamilyMemberKey is non-blank here, so only the second blank check - over ProjectedKey -
        // can be the one that catches this.
        var observation = EuFeedEntryObservation.TryObserve(
            At(1),
            identityResolutionClosed: true,
            [RootIn],
            [new EuFeedFamilyProjection(RootIn, "family-a", " ")],
            out var refusal);

        Assert.IsNull(observation);
        Assert.AreEqual(EuFeedObservationRefusal.ProjectionKeyBlank, refusal);
    }

    [TestMethod]
    public void AWellFormedObservationSortsRootsAndProjections()
    {
        var observation = EuFeedEntryObservation.TryObserve(
            At(1),
            identityResolutionClosed: true,
            [RootOut, RootIn],
            [
                new EuFeedFamilyProjection(RootOut, "family-b", "projected-key-2"),
                new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1"),
            ],
            out var refusal);

        Assert.IsNotNull(observation, $"refused as {refusal}");
        CollectionAssert.AreEqual(
            new[] { RootIn, RootOut }, observation.ResolvedWorkRoots.ToArray());
        Assert.AreEqual(RootIn, observation.Projections[0].SourceWorkRoot);
        Assert.AreEqual(RootOut, observation.Projections[1].SourceWorkRoot);
    }

    // ---- Classify: the total algorithm, one test per terminal and per unresolved cause. ----

    [TestMethod]
    public void AnEntryResolvingEntirelyInsideThePackIsInPack()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(At(1), [RootIn]);

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.InPack, termination.Terminal);
        CollectionAssert.AreEqual(new[] { RootIn }, termination.InPack.ToArray());
        Assert.AreEqual(0, termination.OutOfPack.Count);
        Assert.AreEqual(EuFeedOutOfPackReason.None, termination.OutOfPackReason);
    }

    [TestMethod]
    public void AnEntryResolvingEntirelyOutsideThePackIsOutOfPack()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(At(1), [RootOut]);

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.OutOfPack, termination.Terminal);
        CollectionAssert.AreEqual(new[] { RootOut }, termination.OutOfPack.ToArray());
        Assert.AreEqual(0, termination.InPack.Count);
        Assert.AreEqual(
            EuFeedOutOfPackReason.NotAMemberOfTheDiscoveredPackRootSet, termination.OutOfPackReason);
    }

    [TestMethod]
    public void AnEntryResolvingToBothSidesIsMixedScope()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(At(1), [RootIn, RootOut]);

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.MixedScope, termination.Terminal);
        CollectionAssert.AreEqual(new[] { RootIn }, termination.InPack.ToArray());
        CollectionAssert.AreEqual(new[] { RootOut }, termination.OutOfPack.ToArray());
    }

    [TestMethod]
    public void ProjectionsSplitByTheirSourceWorkRootOnAMixedEntry()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(
            At(1),
            [RootIn, RootOut],
            new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1"),
            new EuFeedFamilyProjection(RootOut, "family-b", "projected-key-2"));

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.MixedScope, termination.Terminal);
        Assert.AreEqual(1, termination.InPackProjections.Count);
        Assert.AreEqual(RootIn, termination.InPackProjections[0].SourceWorkRoot);
        Assert.AreEqual(1, termination.OutOfPackProjections.Count);
        Assert.AreEqual(RootOut, termination.OutOfPackProjections[0].SourceWorkRoot);
    }

    [TestMethod]
    public void AnObservationWhoseResolutionDidNotCloseIsUnresolved()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = EuFeedEntryObservation.TryObserve(
            At(1), identityResolutionClosed: false, [], [], out var observationRefusal);
        Assert.IsNotNull(observation, $"fixture refused as {observationRefusal}");

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.UnresolvedOrAmbiguous, termination.Terminal);
        Assert.AreEqual(
            EuFeedUnresolvedCause.IdentityResolutionDidNotClose, termination.UnresolvedCause);
    }

    [TestMethod]
    public void AnEntryTheTraversalDidNotDeliverIsUnresolvedAsWatermarkMembership()
    {
        // The entries set is closed over slot 1 only; the observation names slot 2, which the
        // traversal never delivered.
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(At(2), [RootIn]);

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.UnresolvedOrAmbiguous, termination.Terminal);
        Assert.AreEqual(
            EuFeedUnresolvedCause.WatermarkMembershipDidNotClose, termination.UnresolvedCause);
    }

    [TestMethod]
    public void AProjectionNamingARootTheEntryDidNotResolveToIsUnresolved()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        // The entry resolved only to RootIn, but one of its own projections names RootAlsoIn,
        // which is not in its resolved set - the projection cannot be attributed to either side.
        var observation = Observed(
            At(1), [RootIn], new EuFeedFamilyProjection(RootAlsoIn, "family-a", "projected-key-1"));

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.UnresolvedOrAmbiguous, termination.Terminal);
        Assert.AreEqual(
            EuFeedUnresolvedCause.FamilyProjectionDidNotClose, termination.UnresolvedCause);
    }

    [TestMethod]
    public void AResolvedEntryWithBothSetsEmptyIsUnresolvedAsPartition()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(At(1), []);

        var termination = binding.Classify(observation, entries);

        Assert.AreEqual(EuFeedTerminal.UnresolvedOrAmbiguous, termination.Terminal);
        Assert.AreEqual(EuFeedUnresolvedCause.PartitionDidNotClose, termination.UnresolvedCause);
    }

    [TestMethod]
    public void NoTerminalCarriesAnyMemberThatCouldHoldABodyOrCapability()
    {
        // Structural half of R3's "no feed terminal supplies a body or capability" and R7's
        // "cannot add a seed, root, closure row, body, or capability": nothing on the termination
        // type, or on what it is built from, can hold a byte payload or an object reference the
        // rest of the system treats as a capability. What it carries is publisher identity strings
        // and enum classifications only.
        Type[] guarded = [typeof(EuFeedEntryTermination), typeof(EuFeedTerminalReconciliation)];
        Type[] forbidden = [typeof(byte[]), typeof(Stream), typeof(SourceObjectRef)];

        var carriers = new List<string>();
        foreach (var type in guarded)
        {
            foreach (var member in ConstructionSurface.DeclaredMembersTransitive(type))
            {
                var candidates = member switch
                {
                    System.Reflection.PropertyInfo property => [property.PropertyType],
                    System.Reflection.FieldInfo field => new[] { field.FieldType },
                    _ => Array.Empty<Type>(),
                };
                foreach (var candidate in candidates)
                {
                    if (forbidden.Any(f => ConstructionSurface.Carries(candidate, f)))
                    {
                        carriers.Add($"{type.Name}::{member.Name} -> {candidate.Name}");
                    }
                }
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), carriers.Distinct().ToArray());
    }

    // ---- R3's terminal equation and its orthogonal conflict counts. ----

    [TestMethod]
    public void TheEquationHoldsAndTheCutIsCompleteOverThreeCleanlyResolvedEntries()
    {
        var plan = Plan(2);
        var steps = new[] { SingleEntryStep(plan, 1), SingleEntryStep(plan, 2), SingleEntryStep(plan, 3) };
        var entries = EuFeedWatermarkEntrySet.TryClose(steps, out var entriesRefusal);
        Assert.IsNotNull(entries, $"refused as {entriesRefusal}");

        var binding = Binding([RootIn], []);
        var terminations = new[]
        {
            binding.Classify(Observed(At(1), [RootIn]), entries),
            binding.Classify(Observed(At(2), [RootOut]), entries),
            binding.Classify(Observed(At(3), [RootIn, RootOut]), entries),
        };

        var reconciliation = EuFeedTerminalReconciliation.Of(binding, entries, terminations);

        Assert.AreEqual(3, reconciliation.CanonicalEntryCount);
        Assert.AreEqual(3, reconciliation.TerminalCountSum);
        Assert.IsTrue(reconciliation.TerminalEquationHolds);
        Assert.AreEqual(1, reconciliation.TerminalCounts[EuFeedTerminal.InPack]);
        Assert.AreEqual(1, reconciliation.TerminalCounts[EuFeedTerminal.OutOfPack]);
        Assert.AreEqual(1, reconciliation.TerminalCounts[EuFeedTerminal.MixedScope]);
        Assert.AreEqual(0, reconciliation.TerminalCounts[EuFeedTerminal.UnresolvedOrAmbiguous]);
        Assert.AreEqual(0, reconciliation.ConflictTotal);
        Assert.IsFalse(reconciliation.MakesTheCutIncomplete);
    }

    [TestMethod]
    public void AnUnresolvedTerminalKeepsTheEquationButMakesTheCutIncomplete()
    {
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var terminations = new[] { binding.Classify(Observed(At(1), []), entries) };

        var reconciliation = EuFeedTerminalReconciliation.Of(binding, entries, terminations);

        Assert.IsTrue(reconciliation.TerminalEquationHolds);
        Assert.AreEqual(
            1, reconciliation.ConflictCounts[EuFeedReconciliationConflict.UnresolvedOrAmbiguousTerminal]);
        Assert.IsTrue(reconciliation.MakesTheCutIncomplete);
    }

    [TestMethod]
    public void AnInPackProjectionMissingFromItsDiscoveredFamilyIsAConflict()
    {
        var entries = CloseEntries(Plan(2), 1);
        // The binding's discovered family index is empty, so the entry's own in-pack projection
        // cannot occur in it.
        var binding = Binding([RootIn], []);
        var observation = Observed(
            At(1), [RootIn], new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1"));
        var terminations = new[] { binding.Classify(observation, entries) };

        var reconciliation = EuFeedTerminalReconciliation.Of(binding, entries, terminations);

        Assert.AreEqual(EuFeedTerminal.InPack, terminations[0].Terminal);
        Assert.AreEqual(
            1,
            reconciliation.ConflictCounts[
                EuFeedReconciliationConflict.ProjectionMissingFromItsDiscoveredFamily]);
        Assert.IsTrue(reconciliation.MakesTheCutIncomplete);
    }

    [TestMethod]
    public void AnInPackProjectionPresentInItsDiscoveredFamilyIsNotAConflict()
    {
        var entries = CloseEntries(Plan(2), 1);
        var projection = new EuFeedFamilyProjection(RootIn, "family-a", "projected-key-1");
        var binding = Binding([RootIn], [projection]);
        var terminations = new[] { binding.Classify(Observed(At(1), [RootIn], projection), entries) };

        var reconciliation = EuFeedTerminalReconciliation.Of(binding, entries, terminations);

        Assert.AreEqual(0, reconciliation.ConflictTotal);
        Assert.IsFalse(reconciliation.MakesTheCutIncomplete);
    }

    [TestMethod]
    public void AnOutOfPackProjectionNeverNeedsToReconcileToTheDiscoveredFamily()
    {
        // Only the in-pack side of a mixed entry is checked against the discovered family index;
        // the out-of-pack side is retained positive-only evidence per R3 and R7. An empty family
        // index therefore must not fault the out-of-pack projection here.
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(
            At(1), [RootIn, RootOut], new EuFeedFamilyProjection(RootOut, "family-b", "projected-key-2"));
        var terminations = new[] { binding.Classify(observation, entries) };

        var reconciliation = EuFeedTerminalReconciliation.Of(binding, entries, terminations);

        Assert.AreEqual(EuFeedTerminal.MixedScope, terminations[0].Terminal);
        Assert.AreEqual(0, reconciliation.ConflictTotal);
    }

    [TestMethod]
    public void DuplicateTerminalAccountingForTheSameEntryIsAConflict()
    {
        // Three copies, not two. With exactly two, a mutant that flags the first occurrence
        // instead of the later ones produces the same count (1) as the correct code by
        // coincidence, since there is only one occurrence either way to attribute. Three copies
        // means the correct count (two duplicates, for the second and third copies) and that
        // mutant's count (one, for only the first) genuinely differ.
        var entries = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var observation = Observed(At(1), [RootIn]);
        var termination = binding.Classify(observation, entries);

        var reconciliation = EuFeedTerminalReconciliation.Of(
            binding, entries, [termination, termination, termination]);

        Assert.AreEqual(
            2, reconciliation.ConflictCounts[EuFeedReconciliationConflict.DuplicateTerminalAccounting]);
        Assert.IsTrue(reconciliation.MakesTheCutIncomplete);
        // All three copies still count toward the sum: the equation is arithmetic over what was
        // reported, not a de-duplicated view of it.
        Assert.AreEqual(3, reconciliation.TerminalCountSum);
        Assert.IsFalse(reconciliation.TerminalEquationHolds);
    }

    [TestMethod]
    public void ACanonicalEntryWithNoTerminalIsAConflict()
    {
        // Three canonical entries, two terminated and one not, rather than a fifty-fifty split.
        // A mutant that flags a terminated entry as un-terminated instead of the reverse would
        // still report count 1 against a one-terminated/one-not fixture by coincidence; against
        // two terminated and one not, the correct count (1, for the untouched slot) and that
        // mutant's count (2, for the two it wrongly flags) genuinely differ.
        var plan = Plan(2);
        var entries = EuFeedWatermarkEntrySet.TryClose(
            [SingleEntryStep(plan, 1), SingleEntryStep(plan, 2), SingleEntryStep(plan, 3)],
            out var entriesRefusal);
        Assert.IsNotNull(entries, $"refused as {entriesRefusal}");

        var binding = Binding([RootIn], []);
        // Slots 1 and 2 are classified; slot 3 is canonical but never terminated.
        var terminations = new[]
        {
            binding.Classify(Observed(At(1), [RootIn]), entries),
            binding.Classify(Observed(At(2), [RootIn]), entries),
        };

        var reconciliation = EuFeedTerminalReconciliation.Of(binding, entries, terminations);

        Assert.AreEqual(
            1, reconciliation.ConflictCounts[EuFeedReconciliationConflict.EntryWithoutATerminal]);
        Assert.IsTrue(reconciliation.MakesTheCutIncomplete);
        Assert.AreEqual(3, reconciliation.CanonicalEntryCount);
        Assert.AreEqual(2, reconciliation.TerminalCountSum);
        Assert.IsFalse(reconciliation.TerminalEquationHolds);
    }

    [TestMethod]
    public void ATerminalReconciledAgainstAnEntrySetThatDoesNotContainItIsAConflict()
    {
        // The termination was produced against one entry set (which contains slot 1, so
        // classification resolves cleanly to InPack, not to the unresolved terminal) and then
        // reconciled against a different, unrelated entry set. This is the one way this conflict
        // is reachable without also being an unresolved terminal: Classify itself already refuses
        // membership failures into UnresolvedOrAmbiguous, so a resolved terminal can only fail this
        // check when the reconciliation step is given a different entries object than the one the
        // termination was classified against - a caller mismatch, not a classification defect.
        var closedOverSlot1 = CloseEntries(Plan(2), 1);
        var binding = Binding([RootIn], []);
        var termination = binding.Classify(Observed(At(1), [RootIn]), closedOverSlot1);
        Assert.AreEqual(EuFeedTerminal.InPack, termination.Terminal);

        var unrelatedEntries = EuFeedWatermarkEntrySet.TryClose([], out var refusal);
        Assert.IsNotNull(unrelatedEntries, $"refused as {refusal}");

        var reconciliation = EuFeedTerminalReconciliation.Of(
            binding, unrelatedEntries, [termination]);

        Assert.AreEqual(
            1,
            reconciliation.ConflictCounts[
                EuFeedReconciliationConflict.TerminalOutsideTheCanonicalEntrySet]);
        Assert.AreEqual(
            0, reconciliation.ConflictCounts[EuFeedReconciliationConflict.UnresolvedOrAmbiguousTerminal]);
        Assert.IsTrue(reconciliation.MakesTheCutIncomplete);
    }

    // ---- Fixtures. ----

    private static SourceArtifactRef SeedMapRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a1", new string('1', 64));

    private static SourceArtifactRef ClosureMatrixRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a2", new string('2', 64));

    private static SourceArtifactRef IdentityBindingRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a3", new string('3', 64));

    private static EuFeedRootIntersection Binding(
        IReadOnlyList<string> packRoots, IReadOnlyList<EuFeedFamilyProjection> familyRows) =>
        EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(), packRoots, familyRows,
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture binding refused as {refusal}");

    private static EuFeedEntryObservation Observed(
        EuWatermarkCursor entry, IReadOnlyList<string> resolvedWorkRoots,
        params EuFeedFamilyProjection[] projections) =>
        EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: true, resolvedWorkRoots, projections, out var refusal)
        ?? throw new InvalidOperationException($"the fixture observation refused as {refusal}");

    /// <summary>
    /// Slot <paramref name="n"/>'s boundary and beyond-boundary watermark and key, all distinct
    /// admitted shapes so several slots can share one plan without colliding.
    /// </summary>
    private static (string BoundaryWatermark, string BoundaryKey, string BeyondWatermark, string
        BeyondKey) Slot(int n) => (
            $"2026-09-03T12:{10 + n:00}:00.000+02:00",
            $"http://publications.europa.eu/resource/cellar/b{n}",
            $"2026-09-03T12:{10 + n:00}:30.000+02:00",
            $"http://publications.europa.eu/resource/cellar/e{n}");

    /// <summary>Slot <paramref name="n"/>'s beyond-boundary cursor: the one canonical entry a
    /// <see cref="SingleEntryStep"/> for that slot newly delivers.</summary>
    private static EuWatermarkCursor At(int n)
    {
        var slot = Slot(n);
        return EuWatermarkCursor.TryOpen(slot.BeyondWatermark, slot.BeyondKey, out var refusal)
            ?? throw new InvalidOperationException($"the fixture cursor refused as {refusal}");
    }

    private static EuWatermarkWitnessPlan Plan(int pageLimit) =>
        EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            pageLimit,
            AtBoundary(1),
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture plan refused as {refusal}");

    private static EuWatermarkCursor AtBoundary(int n)
    {
        var slot = Slot(n);
        return EuWatermarkCursor.TryOpen(slot.BoundaryWatermark, slot.BoundaryKey, out var refusal)
            ?? throw new InvalidOperationException($"the fixture cursor refused as {refusal}");
    }

    /// <summary>
    /// A single well formed step for slot <paramref name="n"/>, whose one newly delivered
    /// canonical entry is <see cref="At"/> that same slot.
    /// </summary>
    private static EuWatermarkTraversalStep SingleEntryStep(EuWatermarkWitnessPlan plan, int n)
    {
        var slot = Slot(n);
        var boundary = AtBoundary(n);
        var crossing = EuBoundaryCrossing.TryCross(
            boundary, [slot.BoundaryKey], [slot.BoundaryKey], At(n), out var crossingRefusal)
            ?? throw new InvalidOperationException($"the fixture crossing refused as {crossingRefusal}");

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan, crossing, [boundary, At(n)], out var stepRefusal)
            ?? throw new InvalidOperationException($"the fixture step refused as {stepRefusal}");
        return step;
    }

    private static EuFeedWatermarkEntrySet CloseEntries(EuWatermarkWitnessPlan plan, int n)
    {
        var entries = EuFeedWatermarkEntrySet.TryClose(
            [SingleEntryStep(plan, n)], out var refusal);
        return entries ?? throw new InvalidOperationException($"the fixture entries refused as {refusal}");
    }
}
