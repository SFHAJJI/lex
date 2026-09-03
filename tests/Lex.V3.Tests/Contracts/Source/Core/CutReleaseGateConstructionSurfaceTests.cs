using Lex.V3.Contracts.Source.Core;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// The construction surface of <c>cut_release_gate/1</c> and <c>cut_global_blocker_registry/1</c>.
/// </summary>
/// <remarks>
/// R3 line 398 requires <c>release_class</c> to be "derived from a closed artifact-kind table" and
/// states it "cannot be selected by a caller, producer, or row". <see cref="CutReleaseGate"/> meets
/// that by never taking a <see cref="ReleaseClass"/> parameter at all, on its one entry point or
/// anywhere else: the pin below is what makes a second entry point, or a release-class parameter
/// appearing anywhere, a visible line in a diff rather than a silently reopened door. The same
/// technique is already load-bearing in this codebase for <c>AbsenceCut.Completion</c> (see
/// <c>AbsenceConstructionSurfaceTests</c>).
/// </remarks>
[TestClass]
public sealed class CutReleaseGateConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Core.";

    /// <summary>
    /// A gate has exactly one checked door: the private constructor, and the one public entry point
    /// that is its only caller. Neither takes a <see cref="ReleaseClass"/> parameter; the
    /// constructor takes a nullable one because <see cref="ReleaseArtifactKindRegistry.Classify"/>
    /// can fail to classify the wire key, and the public entry point takes a bare
    /// <see cref="System.String"/> wire key instead, never the closed type itself.
    /// </summary>
    [TestMethod]
    public void AGateHasExactlyOneCheckedDoorAndReleaseClassIsNeverAParameter()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CutReleaseGate::.ctor(System.String, "
                + "System.Nullable<" + N + "ReleaseArtifactKind>, System.Nullable<" + N + "ReleaseClass>, "
                + N + "CutReleaseVerdict, " + N + "CutReleaseBlockReason, " + N + "CutCompletionClaim, "
                + N + "CutCompletionClaim, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerFamilyCountEntry>) -> "
                + N + "CutReleaseGate",
                "method public static " + N + "CutReleaseGate::TryEvaluate(System.String, System.String, "
                + N + "CutCompletionClaim, " + N + "CutCompletionClaim, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerFamilyCountEntry>, "
                + N + "GlobalBlockerCountVector) -> " + N + "CutReleaseGate",
            },
            ConstructionSurface.Of(typeof(CutReleaseGate)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(CutReleaseGate).Assembly, typeof(CutReleaseGate), true)
                .ToArray(),
            "nothing else in Contracts may hand out a gate verdict it did not evaluate");
    }

    /// <summary>
    /// A count vector has exactly one checked door: the recomputation. There is no path that lets a
    /// caller assemble one directly from claimed totals, which is the whole reason it is fit to
    /// check a supplied vector against.
    /// </summary>
    [TestMethod]
    public void ACountVectorHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "GlobalBlockerCountVector::.ctor("
                + "System.Collections.Generic.IReadOnlyDictionary<" + N + "GlobalBlockerFamily, "
                + N + "GlobalBlockerFamilyTally>) -> " + N + "GlobalBlockerCountVector",
                "method public static " + N + "GlobalBlockerCountVector::Recompute("
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerOccurrence>) -> "
                + N + "GlobalBlockerCountVector",
            },
            ConstructionSurface.Of(typeof(GlobalBlockerCountVector)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(GlobalBlockerCountVector).Assembly, typeof(GlobalBlockerCountVector), true).ToArray(),
            "nothing else in Contracts may hand out a count vector it did not recompute");
    }

    /// <summary>
    /// The four remaining public shapes stay open on purpose: each is untrusted wire input a caller
    /// is meant to construct directly (a completion claim, a declared count entry, an occurrence, an
    /// independently derived tally), never evidence the gate itself mints. Pinned anyway, per the
    /// note this refreeze folds in, so a second constructor or an unexpected new producer on any of
    /// them is still a visible diff rather than silent.
    /// </summary>
    [TestMethod]
    public void TheFourOpenWireShapesEachHaveExactlyTheirOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CutCompletionClaim::.ctor(" + N + "CutCompletionClaim) -> "
                + N + "CutCompletionClaim",
                "constructor public instance " + N + "CutCompletionClaim::.ctor(System.String, System.String, System.Boolean) -> "
                + N + "CutCompletionClaim",
                "method public instance " + N + "CutCompletionClaim::<Clone>$() -> " + N + "CutCompletionClaim",
            },
            ConstructionSurface.Of(typeof(CutCompletionClaim)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "GlobalBlockerFamilyCountEntry::.ctor("
                + N + "GlobalBlockerFamilyCountEntry) -> " + N + "GlobalBlockerFamilyCountEntry",
                "constructor public instance " + N + "GlobalBlockerFamilyCountEntry::.ctor("
                + N + "GlobalBlockerFamily, System.Int32, "
                + "System.Collections.Generic.IReadOnlyDictionary<System.String, System.Int32>) -> "
                + N + "GlobalBlockerFamilyCountEntry",
                "method public instance " + N + "GlobalBlockerFamilyCountEntry::<Clone>$() -> "
                + N + "GlobalBlockerFamilyCountEntry",
            },
            ConstructionSurface.Of(typeof(GlobalBlockerFamilyCountEntry)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "GlobalBlockerOccurrence::.ctor("
                + N + "GlobalBlockerOccurrence) -> " + N + "GlobalBlockerOccurrence",
                "constructor public instance " + N + "GlobalBlockerOccurrence::.ctor(System.String, "
                + "System.String) -> " + N + "GlobalBlockerOccurrence",
                "method public instance " + N + "GlobalBlockerOccurrence::<Clone>$() -> "
                + N + "GlobalBlockerOccurrence",
            },
            ConstructionSurface.Of(typeof(GlobalBlockerOccurrence)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "GlobalBlockerFamilyTally::.ctor("
                + N + "GlobalBlockerFamilyTally) -> " + N + "GlobalBlockerFamilyTally",
                "constructor public instance " + N + "GlobalBlockerFamilyTally::.ctor(System.Int32, "
                + "System.Collections.Generic.IReadOnlyDictionary<System.String, System.Int32>) -> "
                + N + "GlobalBlockerFamilyTally",
                "method public instance " + N + "GlobalBlockerFamilyTally::<Clone>$() -> "
                + N + "GlobalBlockerFamilyTally",
            },
            ConstructionSurface.Of(typeof(GlobalBlockerFamilyTally)).ToArray());
    }
}
