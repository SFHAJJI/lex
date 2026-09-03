using Lex.V3.Contracts.Source.Core;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// The construction surface of <c>cut_release_gate/1</c> and <c>cut_global_blocker_registry/1</c>.
/// </summary>
/// <remarks>
/// R3 line 398 requires <c>release_class</c> to be "derived from a closed artifact-kind table" and
/// states it "cannot be selected by a caller, producer, or row". <see cref="CutReleaseGate"/> meets
/// that by never taking a <see cref="ReleaseClass"/> parameter at all: the pin below is what makes a
/// third entry point, or a release-class parameter reappearing on an existing one, a visible line in
/// a diff rather than a silently reopened door. The same technique is already load-bearing in this
/// codebase for <c>AbsenceCut.Completion</c> (see <c>AbsenceConstructionSurfaceTests</c>).
/// </remarks>
[TestClass]
public sealed class CutReleaseGateConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Core.";

    /// <summary>
    /// A gate has exactly three checked doors -- the private constructor, the private
    /// <c>Evaluate</c> both public entry points share, and the two public entry points themselves --
    /// and <see cref="ReleaseClass"/> is a parameter of none of the public ones.
    /// </summary>
    [TestMethod]
    public void AGateHasOneCheckedDoorPerReleaseClassAndReleaseClassIsNeverAPublicParameter()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "CutReleaseGate::.ctor(System.String, "
                + N + "ReleaseClass, " + N + "CutReleaseVerdict, " + N + "CutReleaseBlockReason, "
                + N + "CutCompletionClaim, " + N + "CutCompletionClaim, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerFamilyCountEntry>) -> "
                + N + "CutReleaseGate",
                "method private static " + N + "CutReleaseGate::Evaluate(System.String, "
                + N + "ReleaseClass, " + N + "CutCompletionClaim, " + N + "CutCompletionClaim, "
                + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerFamilyCountEntry>, "
                + N + "GlobalBlockerCountVector) -> " + N + "CutReleaseGate",
                "method public static " + N + "CutReleaseGate::EvaluateAcquisitionOrProduct(System.String, "
                + N + "CutCompletionClaim, " + N + "CutCompletionClaim, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerFamilyCountEntry>, "
                + N + "GlobalBlockerCountVector) -> " + N + "CutReleaseGate",
                "method public static " + N + "CutReleaseGate::EvaluateEnumerationEvidenceOnly(System.String, "
                + N + "CutCompletionClaim, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "GlobalBlockerFamilyCountEntry>, "
                + N + "GlobalBlockerCountVector) -> " + N + "CutReleaseGate",
            },
            ConstructionSurface.Of(typeof(CutReleaseGate)).ToArray());

        Assert.IsFalse(
            ConstructionSurface.Of(typeof(CutReleaseGate))
                .Any(entry => entry.Contains("public") && entry.Contains("ReleaseClass,")),
            "no public door may accept a caller-selected release class");

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
}
