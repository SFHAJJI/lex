using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Fold-in three of the D1-04 refreeze (lex-event-20260903T221036088Z-963c186c93cc4c898eec91ee9f2b91e9):
/// the eleven new public types this slice introduced, none of which had a
/// <see cref="ConstructionSurface"/> pin at the prior freeze. One test per type, each transcribed
/// from what <see cref="ConstructionSurface.Of"/> actually reflected, printed from a throwaway
/// failing assertion rather than hand-derived -- the same technique
/// <c>LuxembourgExecutorConstructionSurfaceTests</c> and <c>LuxembourgConstructionSurfaceTests</c>
/// already use for D1-03's own surface. A second door onto any of these eleven types (a new public
/// constructor, a new factory, a new enum member) is a line in the diff here rather than something a
/// reviewer has to notice on their own.
/// </summary>
[TestClass]
public sealed class LuxembourgQueryExecutionAdapterConstructionSurfaceTests
{
    private const string N = "Lex.V3.Ingest.Luxembourg.";
    private const string Contracts = "Lex.V3.Contracts.Source.Luxembourg.";
    private const string Absence = "Lex.V3.Contracts.Source.Absence.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";
    private const string Custody = "Lex.V3.Contracts.Custody.";

    /// <summary>
    /// A static class whose only member with any construction shape at all is the compiler-emitted
    /// static constructor <see cref="LuxembourgSourceProfileTopology.RegistryRef"/>'s initializer
    /// requires. There is no instance constructor to pin because there cannot be one.
    /// </summary>
    [TestMethod]
    public void TopologyIsAStaticTypeWithOnlyTheCompilerEmittedStaticConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private static " + Contracts + "LuxembourgSourceProfileTopology::.cctor() -> "
                + Contracts + "LuxembourgSourceProfileTopology",
            },
            ConstructionSurface.Of(typeof(LuxembourgSourceProfileTopology)).ToArray());
    }

    [TestMethod]
    public void RelationFamilyAcquisitionStateIsAPlainFourMemberEnum()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "LuxembourgRelationFamilyAcquisitionState::AcquiredComplete -> "
                + N + "LuxembourgRelationFamilyAcquisitionState",
                "field public static " + N + "LuxembourgRelationFamilyAcquisitionState::Incomplete -> "
                + N + "LuxembourgRelationFamilyAcquisitionState",
                "field public static " + N + "LuxembourgRelationFamilyAcquisitionState::Unacquired -> "
                + N + "LuxembourgRelationFamilyAcquisitionState",
                "field public static " + N + "LuxembourgRelationFamilyAcquisitionState::Uncertain -> "
                + N + "LuxembourgRelationFamilyAcquisitionState",
            },
            ConstructionSurface.Of(typeof(LuxembourgRelationFamilyAcquisitionState)).ToArray());
    }

    /// <summary>
    /// One private constructor behind two factories: <c>Complete</c> is the only door onto
    /// <c>AcquiredComplete</c>, and it requires the completion evidence -- exactly the invariant
    /// <c>Complete</c>'s own doc comment claims.
    /// </summary>
    [TestMethod]
    public void RelationFamilyAcquisitionHasExactlyTwoFactoriesOverOnePrivateConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgRelationFamilyAcquisition::.ctor("
                + "System.String, " + N + "LuxembourgRelationFamilyAcquisitionState, "
                + Absence + "AbsenceFamilyEnumerationProof, System.String) -> "
                + N + "LuxembourgRelationFamilyAcquisition",
                "method public static " + N + "LuxembourgRelationFamilyAcquisition::Complete(System.String, "
                + Absence + "AbsenceFamilyEnumerationProof) -> " + N + "LuxembourgRelationFamilyAcquisition",
                "method public static " + N + "LuxembourgRelationFamilyAcquisition::NotComplete(System.String, "
                + N + "LuxembourgRelationFamilyAcquisitionState, System.String) -> "
                + N + "LuxembourgRelationFamilyAcquisition",
            },
            ConstructionSurface.Of(typeof(LuxembourgRelationFamilyAcquisition)).ToArray());
    }

    [TestMethod]
    public void CoarseDispositionGapIsAPlainThreeMemberEnum()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N
                + "LuxembourgCoarseDispositionGap::AccConstitutionalReviewEvidenceGateNotApplied -> "
                + N + "LuxembourgCoarseDispositionGap",
                "field public static " + N + "LuxembourgCoarseDispositionGap::RectTypedRoleNotDistinguished -> "
                + N + "LuxembourgCoarseDispositionGap",
                "field public static " + N + "LuxembourgCoarseDispositionGap::TcTypedRoleNotDistinguished -> "
                + N + "LuxembourgCoarseDispositionGap",
            },
            ConstructionSurface.Of(typeof(LuxembourgCoarseDispositionGap)).ToArray());
    }

    /// <summary>An open input record, pinned as open on purpose: item 15's marker names a gap, it does not hold evidence.</summary>
    [TestMethod]
    public void CoarseDispositionMarkerIsAnOpenRecord()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgCoarseDispositionMarker::.ctor("
                + N + "LuxembourgCoarseDispositionMarker) -> " + N + "LuxembourgCoarseDispositionMarker",
                "constructor public instance " + N + "LuxembourgCoarseDispositionMarker::.ctor(System.String, "
                + "System.String, " + N + "LuxembourgCoarseDispositionGap) -> "
                + N + "LuxembourgCoarseDispositionMarker",
                "method public instance " + N + "LuxembourgCoarseDispositionMarker::<Clone>$() -> "
                + N + "LuxembourgCoarseDispositionMarker",
            },
            ConstructionSurface.Of(typeof(LuxembourgCoarseDispositionMarker)).ToArray());
    }

    [TestMethod]
    public void FamilyEnumerationOutcomeKindIsAPlainThreeMemberEnum()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::ExecutorRefused -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::ProofRefused -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::Proven -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
            },
            ConstructionSurface.Of(typeof(LuxembourgFamilyEnumerationOutcomeKind)).ToArray());
    }

    /// <summary>Three factories (one per <see cref="LuxembourgFamilyEnumerationOutcomeKind"/> member) over one private constructor.</summary>
    [TestMethod]
    public void FamilyEnumerationOutcomeHasExactlyThreeFactoriesOverOnePrivateConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgFamilyEnumerationOutcome::.ctor(System.String, "
                + N + "LuxembourgFamilyEnumerationOutcomeKind, " + Absence + "AbsenceFamilyEnumerationProof, "
                + N + "LuxembourgEnumerationRefusalDetail, "
                + "System.Nullable<" + Absence + "AbsenceFamilyEnumerationProofRefusal>) -> "
                + N + "LuxembourgFamilyEnumerationOutcome",
                "method public static " + N + "LuxembourgFamilyEnumerationOutcome::ExecutorRefused(System.String, "
                + N + "LuxembourgEnumerationRefusalDetail) -> " + N + "LuxembourgFamilyEnumerationOutcome",
                "method public static " + N + "LuxembourgFamilyEnumerationOutcome::ProofRefused(System.String, "
                + Absence + "AbsenceFamilyEnumerationProofRefusal) -> " + N + "LuxembourgFamilyEnumerationOutcome",
                "method public static " + N + "LuxembourgFamilyEnumerationOutcome::Proven(System.String, "
                + Absence + "AbsenceFamilyEnumerationProof) -> " + N + "LuxembourgFamilyEnumerationOutcome",
            },
            ConstructionSurface.Of(typeof(LuxembourgFamilyEnumerationOutcome)).ToArray());
    }

    [TestMethod]
    public void QueryExecutionRefusalIsAFiveMemberEnumIncludingNone()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "LuxembourgQueryExecutionRefusal::None -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N + "LuxembourgQueryExecutionRefusal::ObservationCountDoesNotMatchDelivery -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N + "LuxembourgQueryExecutionRefusal::ObservationsWithoutProvenCensus -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N + "LuxembourgQueryExecutionRefusal::ScopeManifestNotHeld -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N + "LuxembourgQueryExecutionRefusal::ScopeResolutionFailed -> "
                + N + "LuxembourgQueryExecutionRefusal",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionRefusal)).ToArray());
    }

    /// <summary>
    /// One internal constructor, matching <c>LuxembourgEnumerationRefusalDetail</c>'s own door
    /// shape: only this assembly and its own tests can mint a refusal that did not happen.
    /// </summary>
    [TestMethod]
    public void QueryExecutionRefusalDetailHasExactlyOneCheckedInternalDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance " + N + "LuxembourgQueryExecutionRefusalDetail::.ctor("
                + N + "LuxembourgQueryExecutionRefusal, " + Contracts + "LuxembourgProfileResolutionFailure, "
                + "System.String) -> " + N + "LuxembourgQueryExecutionRefusalDetail",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionRefusalDetail)).ToArray());
    }

    /// <summary>
    /// Two public factories over one private constructor -- "delivered or refused, never both and
    /// never neither" -- and the constructor now also carries the completion field fold-in one
    /// added: a third factory, or a public setter, is a line in this diff.
    /// </summary>
    [TestMethod]
    public void QueryExecutionResultIsDeliveredOrRefusedByConstruction()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgQueryExecutionResult::.ctor("
                + Core + "SourceProfileTopology, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgFamilyEnumerationOutcome>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgRelationFamilyAcquisition>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgCoarseDispositionMarker>, "
                + Custody + "DurableBlobWriteReceipt, System.String, "
                + "System.Nullable<" + N + "LuxembourgQueryExecutionCompletion>, "
                + N + "LuxembourgQueryExecutionRefusalDetail) -> " + N + "LuxembourgQueryExecutionResult",
                "method public static " + N + "LuxembourgQueryExecutionResult::Delivered("
                + Core + "SourceProfileTopology, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgFamilyEnumerationOutcome>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgRelationFamilyAcquisition>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgCoarseDispositionMarker>, "
                + Custody + "DurableBlobWriteReceipt, System.String) -> " + N + "LuxembourgQueryExecutionResult",
                "method public static " + N + "LuxembourgQueryExecutionResult::Refused("
                + Core + "SourceProfileTopology, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgFamilyEnumerationOutcome>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "LuxembourgRelationFamilyAcquisition>, "
                + N + "LuxembourgQueryExecutionRefusalDetail) -> " + N + "LuxembourgQueryExecutionResult",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionResult)).ToArray());
    }

    /// <summary>The adapter itself: exactly one public constructor, no test-only seam (unlike the executor).</summary>
    [TestMethod]
    public void QueryExecutionAdapterHasExactlyOnePublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor public instance " + N + "LuxembourgQueryExecutionAdapter::.ctor("
                + Custody + "ICustodyStore, " + N + "LuxembourgRepeatedEnumerationExecutor, "
                + Contracts + "VerifiedLuxembourgSourceProfile) -> " + N + "LuxembourgQueryExecutionAdapter",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionAdapter)).ToArray());
    }

    /// <summary>
    /// Not one of the review's original eleven -- <see cref="LuxembourgQueryExecutionCompletion"/> is
    /// new in this refreeze (fold-in one) -- but pinned the same way on the same discipline: a
    /// plain two-member enum with no construction surface beyond the two base-class constructors
    /// every enum carries.
    /// </summary>
    [TestMethod]
    public void QueryExecutionCompletionIsAPlainTwoMemberEnum()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "LuxembourgQueryExecutionCompletion::AllFamiliesProven -> "
                + N + "LuxembourgQueryExecutionCompletion",
                "field public static " + N + "LuxembourgQueryExecutionCompletion::PartialFamilyRefused -> "
                + N + "LuxembourgQueryExecutionCompletion",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionCompletion)).ToArray());
    }
}
