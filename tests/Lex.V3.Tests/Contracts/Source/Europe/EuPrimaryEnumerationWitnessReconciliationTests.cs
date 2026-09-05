using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// R3.2 and the ruling's separation made structural: the witness reconciles against D1-05's own
/// primary enumeration, never the other way around, and the two sides must carry structurally
/// distinct producer identities.
/// </summary>
[TestClass]
public sealed class EuPrimaryEnumerationWitnessReconciliationTests
{

    /// <summary>
    /// One real Appendix A pack root, so the fixture plan is frozen over a batch the plan
    /// will actually canonicalize rather than a placeholder it would refuse.
    /// </summary>
    private const string PackObjectForTests =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string N = "Lex.V3.Contracts.Source.Europe.";

    private static string SeedA => EuAppendixASeedMap.PackRoots[0];
    private static string SeedB => EuAppendixASeedMap.PackRoots[1];
    private static string NotASeed =>
        "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000";

    private static SourceArtifactRef PrimaryPlanRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000c1", new string('7', 64));

    private static SourceArtifactRef SeedMapRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a1", new string('1', 64));

    private static SourceArtifactRef ClosureMatrixRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a2", new string('2', 64));

    private static SourceArtifactRef IdentityBindingRef() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a3", new string('3', 64));

    private static EuFeedRootIntersection Witness(IReadOnlyList<string> packRoots) =>
        EuFeedRootIntersection.TryBind(
            SeedMapRef(), ClosureMatrixRef(), IdentityBindingRef(), packRoots,
            Array.Empty<EuFeedFamilyProjection>(), out var refusal)
        ?? throw new InvalidOperationException($"the fixture witness refused as {refusal}");

    private static EuPrimaryEnumerationRootBinding Primary(IReadOnlyList<string> discoveredRoots) =>
        EuPrimaryEnumerationRootBinding.TryBind(PrimaryPlanRef(), discoveredRoots, out var refusal)
        ?? throw new InvalidOperationException($"the fixture primary refused as {refusal}");

    private static EuWatermarkCursor Cursor(int n) =>
        EuWatermarkCursor.TryOpen(
            $"2026-09-03T12:{10 + n:00}:30.000+02:00",
            $"http://publications.europa.eu/resource/cellar/e{n}",
            out var refusal)
        ?? throw new InvalidOperationException($"the fixture cursor refused as {refusal}");

    private static EuFeedWatermarkEntrySet EntrySetOf(params EuWatermarkCursor[] cursors)
    {
        var plan = EuWatermarkWitnessPlan.TryFreeze(
            EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint,
            EuWatermarkWitnessPlan.WatermarkPredicateIri,
            2,
            EuWatermarkCursor.TryOpen("2026-09-03T12:00:00.000+02:00", "seed", out _)!,
            [PackObjectForTests],
            out var planRefusal)
            ?? throw new InvalidOperationException($"the fixture plan refused as {planRefusal}");

        var crossing = EuBoundaryCrossing.TryCross(
            EuWatermarkCursor.TryOpen("2026-09-03T12:00:00.000+02:00", "seed", out _)!,
            ["seed"], ["seed"], cursors.Length > 0 ? cursors[0] : null, out var crossingRefusal)
            ?? throw new InvalidOperationException($"the fixture crossing refused as {crossingRefusal}");

        var step = EuWatermarkTraversalStep.TryAdvance(
            plan,
            crossing,
            new[] { EuWatermarkCursor.TryOpen("2026-09-03T12:00:00.000+02:00", "seed", out _)! }
                .Concat(cursors).ToArray(),
            out var stepRefusal)
            ?? throw new InvalidOperationException($"the fixture step refused as {stepRefusal}");

        return EuFeedWatermarkEntrySet.TryClose([step], out var entriesRefusal)
            ?? throw new InvalidOperationException($"the fixture entries refused as {entriesRefusal}");
    }

    [TestMethod]
    public void TheReconciliationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuPrimaryEnumerationWitnessReconciliation::.ctor("
                + N + "EuPrimaryEnumerationRootBinding, " + N + "EuFeedRootIntersection, "
                + "System.Int32) -> " + N + "EuPrimaryEnumerationWitnessReconciliation",
                "method public static " + N + "EuPrimaryEnumerationWitnessReconciliation::TryReconcile("
                + N + "EuPrimaryEnumerationRootBinding, " + N + "EuFeedRootIntersection, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFeedEntryTermination>, out "
                + N + "EuPrimaryWitnessReconciliationRefusal&) -> " + N
                + "EuPrimaryEnumerationWitnessReconciliation?",
            },
            ConstructionSurface.Of(typeof(EuPrimaryEnumerationWitnessReconciliation)).ToArray());
    }

    [TestMethod]
    public void AWitnessInPackTerminalCorroboratedByThePrimaryEnumerationReconcilesCleanly()
    {
        var witness = Witness(new[] { SeedA });
        var primary = Primary(new[] { SeedA });
        var entry = Cursor(1);
        var entries = EntrySetOf(entry);
        var observation = EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: true, new[] { SeedA },
            Array.Empty<EuFeedFamilyProjection>(), out _)!;
        var termination = witness.Classify(observation, entries);
        Assert.AreEqual(EuFeedTerminal.InPack, termination.Terminal);

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, new[] { termination }, out var refusal);

        Assert.IsNotNull(reconciliation);
        Assert.AreEqual(EuPrimaryWitnessReconciliationRefusal.None, refusal);
        Assert.AreEqual(1, reconciliation!.CheckedTerminationCount);
        Assert.AreSame(primary, reconciliation.Primary);
        Assert.AreSame(witness, reconciliation.Witness);
    }

    [TestMethod]
    public void ASharedClosureIdentityBetweenPrimaryAndWitnessRefusesIndependence()
    {
        var witness = Witness(new[] { SeedA });

        // A primary enumeration minted with the witness's own ClosureMatrixRef digest: the
        // "different producer symbol" R3.2 requires collapses to one, and the reconciliation must
        // refuse before it ever looks at a single termination.
        var collidingPlanRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000c2", witness.ClosureMatrixRef.Sha256);
        var primary = EuPrimaryEnumerationRootBinding.TryBind(
            collidingPlanRef, new[] { SeedA }, out _)!;

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, Array.Empty<EuFeedEntryTermination>(), out var refusal);

        Assert.IsNull(reconciliation);
        Assert.AreEqual(
            EuPrimaryWitnessReconciliationRefusal.ClosureIdentityNotStructurallyIndependentFromWitness,
            refusal);
    }

    [TestMethod]
    public void AWitnessInPackRootThePrimaryEnumerationNeverDiscoveredRefusesReconciliation()
    {
        // The witness's own pack includes both seeds and terminates SeedB in-pack, but this run's
        // primary enumeration only ever discovered SeedA -- exactly the case where trusting the
        // witness's own pack membership instead of the primary enumeration's actual discovery would
        // silently corroborate a root nothing independent actually found.
        var witness = Witness(new[] { SeedA, SeedB });
        var primary = Primary(new[] { SeedA });
        var entry = Cursor(2);
        var entries = EntrySetOf(entry);
        var observation = EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: true, new[] { SeedB },
            Array.Empty<EuFeedFamilyProjection>(), out _)!;
        var termination = witness.Classify(observation, entries);
        Assert.AreEqual(EuFeedTerminal.InPack, termination.Terminal);

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, new[] { termination }, out var refusal);

        Assert.IsNull(reconciliation);
        Assert.AreEqual(
            EuPrimaryWitnessReconciliationRefusal.WitnessInPackRootMissingFromPrimaryEnumeration,
            refusal);
    }

    // ---- Fold-in: MixedScope and UnresolvedOrAmbiguous terminations are never reconciled by any
    // test before this fold-in, and dropping MixedScope from TryReconcile's own termination-selecting
    // condition (`row.Terminal is not (InPack or MixedScope)`) fails nothing that existed -- verified
    // by inspection: every prior test here drives only InPack and OutOfPack terminals. The two tests
    // below genuinely drive both cases through TryReconcile, and the second is the one that would fail
    // if MixedScope were ever dropped from that condition. -------------------------------------------

    [TestMethod]
    public void AMixedScopeTerminalCorroboratedByThePrimaryEnumerationReconcilesCleanly()
    {
        var witness = Witness(new[] { SeedA });
        var primary = Primary(new[] { SeedA });
        var entry = Cursor(4);
        var entries = EntrySetOf(entry);
        var observation = EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: true, new[] { SeedA, NotASeed },
            Array.Empty<EuFeedFamilyProjection>(), out _)!;
        var termination = witness.Classify(observation, entries);
        Assert.AreEqual(EuFeedTerminal.MixedScope, termination.Terminal);

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, new[] { termination }, out var refusal);

        Assert.IsNotNull(reconciliation);
        Assert.AreEqual(EuPrimaryWitnessReconciliationRefusal.None, refusal);
        Assert.AreEqual(1, reconciliation!.CheckedTerminationCount);
    }

    [TestMethod]
    public void AMixedScopeTerminalsInPackRootMissingFromThePrimaryEnumerationRefusesReconciliation()
    {
        // Same shape as AWitnessInPackRootThePrimaryEnumerationNeverDiscoveredRefusesReconciliation
        // above, but for a MixedScope terminal rather than InPack: this is the exact case that would
        // start passing silently if MixedScope were ever dropped from TryReconcile's own condition,
        // since the loop would then skip this row entirely rather than refusing it.
        var witness = Witness(new[] { SeedA });
        var primary = Primary(new[] { SeedB });
        var entry = Cursor(5);
        var entries = EntrySetOf(entry);
        var observation = EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: true, new[] { SeedA, NotASeed },
            Array.Empty<EuFeedFamilyProjection>(), out _)!;
        var termination = witness.Classify(observation, entries);
        Assert.AreEqual(EuFeedTerminal.MixedScope, termination.Terminal);

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, new[] { termination }, out var refusal);

        Assert.IsNull(reconciliation);
        Assert.AreEqual(
            EuPrimaryWitnessReconciliationRefusal.WitnessInPackRootMissingFromPrimaryEnumeration,
            refusal);
    }

    [TestMethod]
    public void AnUnresolvedOrAmbiguousTerminalNeverNeedsPrimaryEnumerationCorroboration()
    {
        // The primary enumeration below discovered nothing at all -- stronger than the OutOfPack
        // test below, which still gives primary a real seed -- to show an unresolved terminal is
        // skipped by TryReconcile's own condition regardless of what the primary enumeration found.
        var witness = Witness(new[] { SeedA });
        var primary = Primary(Array.Empty<string>());
        var entry = Cursor(6);
        var entries = EntrySetOf(entry);
        var observation = EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: false, Array.Empty<string>(),
            Array.Empty<EuFeedFamilyProjection>(), out _)!;
        var termination = witness.Classify(observation, entries);
        Assert.AreEqual(EuFeedTerminal.UnresolvedOrAmbiguous, termination.Terminal);

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, new[] { termination }, out var refusal);

        Assert.IsNotNull(reconciliation);
        Assert.AreEqual(EuPrimaryWitnessReconciliationRefusal.None, refusal);
    }

    [TestMethod]
    public void AnOutOfPackTerminalNeverNeedsPrimaryEnumerationCorroboration()
    {
        // R3/R7: an out-of-pack positive is retained evidence only and never a membership claim, so
        // a primary enumeration that discovered nothing at all still reconciles cleanly against one.
        var witness = Witness(new[] { SeedA });
        var primary = Primary(new[] { SeedA });
        var entry = Cursor(3);
        var entries = EntrySetOf(entry);
        var observation = EuFeedEntryObservation.TryObserve(
            entry, identityResolutionClosed: true,
            new[] { "http://publications.europa.eu/resource/cellar/ffffffff-ffff-ffff-ffff-ffffffffffff" },
            Array.Empty<EuFeedFamilyProjection>(), out _)!;
        var termination = witness.Classify(observation, entries);
        Assert.AreEqual(EuFeedTerminal.OutOfPack, termination.Terminal);

        var reconciliation = EuPrimaryEnumerationWitnessReconciliation.TryReconcile(
            primary, witness, new[] { termination }, out var refusal);

        Assert.IsNotNull(reconciliation);
        Assert.AreEqual(EuPrimaryWitnessReconciliationRefusal.None, refusal);
    }
}
