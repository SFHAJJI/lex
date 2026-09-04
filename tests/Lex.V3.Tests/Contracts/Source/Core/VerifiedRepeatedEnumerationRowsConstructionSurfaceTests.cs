using Lex.V3.Contracts.Source.Core;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// The construction surface of queue item 17's three new public shapes: the row record itself, the
/// door, and the door's refusal vocabulary. Precedent: <c>VerifiedScopeManifestSurfaceTests</c>
/// (<see cref="Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest"/>) pins a verified type's own
/// producers and everything else in the assembly that can hand one out; this file does the same for
/// item 17, added here because none of the three was pinned when the door landed.
/// </summary>
[TestClass]
public sealed class VerifiedRepeatedEnumerationRowsConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Core.";
    private const string RdfTerm = N + "RepeatedEnumerationRdfTerm";
    private const string Row = N + "RepeatedEnumerationRow";
    private const string ReadOnlyRdfTermList = "System.Collections.Generic.IReadOnlyList<" + RdfTerm + ">";
    private const string ReadOnlyRowList = "System.Collections.Generic.IReadOnlyList<" + Row + ">";

    /// <summary>
    /// <see cref="RepeatedEnumerationRow"/> is a plain positional record with a public primary
    /// constructor: unlike <c>VerifiedScopeManifest</c>, nothing about its own construction is
    /// gated, so this pin does not claim it is closed. It exists so that a producer added anywhere -
    /// a second constructor on the record itself, or a new method elsewhere in the assembly that
    /// hands one out - is a visible diff, per the review finding that this record "gains a public
    /// producer and has no ConstructionSurface pin".
    /// </summary>
    [TestMethod]
    public void ARepeatedEnumerationRowHasExactlyItsRecordProducersAndThreeExternalOnes()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Row + "::.ctor(" + Row + ") -> " + Row,
                "constructor public instance " + Row + "::.ctor(" + ReadOnlyRdfTermList + ", "
                + ReadOnlyRdfTermList + ", " + ReadOnlyRdfTermList + ") -> " + Row,
                "method public instance " + Row + "::<Clone>$() -> " + Row,
            },
            ConstructionSurface.Of(typeof(RepeatedEnumerationRow)).ToArray());

        // The lambda inside EnumerationDeliveryComparison.ParseRows, VerifyPages (which calls it for
        // every page) and TryOpen (which calls VerifyPages) are the only three places in Contracts
        // that ever hand out real rows built from parsed bytes. A fourth would be a second parser or
        // a second door, exactly what Decision 80 and this door's own remarks rule out.
        CollectionAssert.AreEqual(
            new[]
            {
                "method internal instance " + N + "EnumerationDeliveryComparison+<>c__DisplayClass79_0"
                + "::<ParseRows>b__0(" + RdfTerm + "[]) -> " + Row,
                "method internal static " + N + "EnumerationDeliveryComparison::VerifyPages("
                + "System.Collections.Generic.IReadOnlyList<" + N + "RepeatedEnumerationResolvedEvidence>, "
                + N + "SourceArtifactRef, System.Int64, " + N + "RepeatedEnumerationInterpretationProfile) -> "
                + ReadOnlyRowList,
                "method private static " + N + "EnumerationDeliveryComparison::ParseRows("
                + "System.ReadOnlySpan<System.Byte>, " + N + "RepeatedEnumerationInterpretationProfile) -> "
                + ReadOnlyRowList,
                "method public static " + N + "VerifiedRepeatedEnumerationRows::TryOpen("
                + "Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProof, "
                + N + "EnumerationDeliveryComparison, " + N + "RepeatedEnumerationInterpretationProfile, "
                + N + "SourceArtifactRef, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "RepeatedEnumerationResolvedEvidence>, "
                + "out " + N + "RepeatedEnumerationRowsOpenRefusal&) -> " + ReadOnlyRowList + "?",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepeatedEnumerationRow).Assembly, typeof(RepeatedEnumerationRow), true).ToArray());
    }

    /// <summary>
    /// <see cref="VerifiedRepeatedEnumerationRows"/> is the door itself, per its own type remarks
    /// ("The one public door from a family's already-minted ... proof ... back to typed ... row
    /// data"). It is a static class: nothing anywhere can ever hold, return or hand out a value of
    /// this type, so both surfaces are empty by construction. That emptiness is the pin - a future
    /// change that makes this type produce itself (an instance, a subtype, a field) is exactly the
    /// silent reopening the review finding asked to make visible.
    /// </summary>
    [TestMethod]
    public void TheDoorItselfIsNeverProducedBecauseItIsNeverAValue()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(VerifiedRepeatedEnumerationRows)).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(VerifiedRepeatedEnumerationRows).Assembly,
                typeof(VerifiedRepeatedEnumerationRows),
                true).ToArray());
    }

    /// <summary>
    /// <see cref="RepeatedEnumerationRowsOpenRefusal"/> carries no wire-token attributes (see its own
    /// type remarks), so this construction-surface pin is what stands in for the member-by-member
    /// pin the Absence sweep gives its own closed vocabularies: the six "field public static" entries
    /// below are exactly its six members, sorted, and a seventh appearing here is a new member nobody
    /// reviewed yet. The two base-constructor entries are <see cref="System.Enum"/>'s and
    /// <see cref="System.ValueType"/>'s own, present for every enum in .NET; they are pinned rather
    /// than filtered because the tool pins exactly what it finds, never what looks interesting.
    /// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>'s own <c>out</c> parameter is the one and
    /// only place anything in this assembly can hand a refusal back to a caller.
    /// </summary>
    [TestMethod]
    public void TheRefusalEnumHasExactlySixMembersAndOneHandOutPath()
    {
        const string Refusal = N + "RepeatedEnumerationRowsOpenRefusal";
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + Refusal + "::CanonicalKeyDigestMismatch -> " + Refusal,
                "field public static " + Refusal + "::CanonicalRowDigestMismatch -> " + Refusal,
                "field public static " + Refusal + "::CursorDigestMismatch -> " + Refusal,
                "field public static " + Refusal + "::DeliveredRowCountMismatch -> " + Refusal,
                "field public static " + Refusal + "::None -> " + Refusal,
                "field public static " + Refusal + "::PageChainInvalid -> " + Refusal,
            },
            ConstructionSurface.Of(typeof(RepeatedEnumerationRowsOpenRefusal)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method public static " + N + "VerifiedRepeatedEnumerationRows::TryOpen("
                + "Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProof, "
                + N + "EnumerationDeliveryComparison, " + N + "RepeatedEnumerationInterpretationProfile, "
                + N + "SourceArtifactRef, " + N + "SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "RepeatedEnumerationResolvedEvidence>, "
                + "out " + Refusal + "&) -> " + ReadOnlyRowList + "?",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepeatedEnumerationRowsOpenRefusal).Assembly,
                typeof(RepeatedEnumerationRowsOpenRefusal),
                true).ToArray());
    }
}
