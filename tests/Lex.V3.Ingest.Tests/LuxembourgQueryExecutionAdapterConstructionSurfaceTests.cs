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
                + Absence + "AbsenceFamilyEnumerationProof?, System.String?) -> "
                + N + "LuxembourgRelationFamilyAcquisition",
                "method public static " + N + "LuxembourgRelationFamilyAcquisition::Complete(System.String, "
                + Absence + "AbsenceFamilyEnumerationProof) -> " + N + "LuxembourgRelationFamilyAcquisition",
                "method public static " + N + "LuxembourgRelationFamilyAcquisition::NotComplete(System.String, "
                + N + "LuxembourgRelationFamilyAcquisitionState, System.String) -> "
                + N + "LuxembourgRelationFamilyAcquisition",
            },
            ConstructionSurface.Of(typeof(LuxembourgRelationFamilyAcquisition)).ToArray());
    }

    /// <summary>D1-04c added CoverProven and CoverRefused to the family, census or assertion cover-chain path.</summary>
    [TestMethod]
    public void FamilyEnumerationOutcomeKindIsAPlainFiveMemberEnum()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::CoverProven -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::CoverRefused -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::ExecutorRefused -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::ProofRefused -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
                "field public static " + N + "LuxembourgFamilyEnumerationOutcomeKind::Proven -> "
                + N + "LuxembourgFamilyEnumerationOutcomeKind",
            },
            ConstructionSurface.Of(typeof(LuxembourgFamilyEnumerationOutcomeKind)).ToArray());
    }

    /// <summary>
    /// Five factories (one per <see cref="LuxembourgFamilyEnumerationOutcomeKind"/> member) over one
    /// private constructor. D1-04c added CoverProven (a cover chain's own leaf proofs) and
    /// CoverRefused (a cover chain's own reconciliation detail).
    /// </summary>
    [TestMethod]
    public void FamilyEnumerationOutcomeHasExactlyFiveFactoriesOverOnePrivateConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgFamilyEnumerationOutcome::.ctor(System.String, "
                + N + "LuxembourgFamilyEnumerationOutcomeKind, " + Absence + "AbsenceFamilyEnumerationProof?, "
                + N + "LuxembourgEnumerationRefusalDetail?, "
                + "System.Nullable<" + Absence + "AbsenceFamilyEnumerationProofRefusal>, "
                + "System.Collections.Generic.IReadOnlyList<" + Absence + "AbsenceFamilyEnumerationProof>?, "
                + N + "LuxembourgPartitionCoverReconciliationDetail?) -> "
                + N + "LuxembourgFamilyEnumerationOutcome",
                "method public static " + N + "LuxembourgFamilyEnumerationOutcome::CoverProven(System.String, "
                + "System.Collections.Generic.IReadOnlyList<" + Absence + "AbsenceFamilyEnumerationProof>) -> "
                + N + "LuxembourgFamilyEnumerationOutcome",
                "method public static " + N + "LuxembourgFamilyEnumerationOutcome::CoverRefused(System.String, "
                + N + "LuxembourgPartitionCoverReconciliationDetail) -> " + N + "LuxembourgFamilyEnumerationOutcome",
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
    public void QueryExecutionRefusalIsATwelveMemberEnumIncludingNone()
    {
        // D1-06c-LU-2 added four: DocumentFetchSessionNotStarted, DocumentBodyNotHeld,
        // DocumentGetOutcomeNotRepresentable and RecordSetNotHeld, one per whole-run failure the
        // document-acquisition phase and the corpus record-set write can produce. Every other
        // document-GET failure is a PER OBJECT refusal and appears nowhere here, which is the
        // distinction this pin makes visible.
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::AssertionRowObjectKindNotRecognised -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::AssertionRowTermUnbound -> " + N
                + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::DocumentBodyNotHeld -> " + N
                + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::DocumentFetchSessionNotStarted -> " + N
                + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::DocumentGetOutcomeNotRepresentable -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N + "LuxembourgQueryExecutionRefusal::None -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::ObservationSubjectNotInDeliveredCensus -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::RecordSetNotRetained -> " + N
                + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::ResourceObservationFamilyNotProven -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::ResourceObservationRowsNotVerified -> "
                + N + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::ScopeManifestNotRetained -> " + N
                + "LuxembourgQueryExecutionRefusal",
                "field public static " + N
                + "LuxembourgQueryExecutionRefusal::ScopeResolutionFailed -> " + N
                + "LuxembourgQueryExecutionRefusal",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionRefusal)).ToArray());
    }

    /// <summary>D1-04b's reviewer fold-in: a plain two-member enum, no construction surface beyond the two base-class constructors every enum carries.</summary>
    [TestMethod]
    public void ResourceObservationExclusionCauseIsAPlainTwoMemberEnum()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "LuxembourgResourceObservationExclusionCause::BlankNodeObject -> "
                + N + "LuxembourgResourceObservationExclusionCause",
                "field public static " + N + "LuxembourgResourceObservationExclusionCause::PredicateNotAdmitted -> "
                + N + "LuxembourgResourceObservationExclusionCause",
            },
            ConstructionSurface.Of(typeof(LuxembourgResourceObservationExclusionCause)).ToArray());
    }

    /// <summary>D1-04b's reviewer fold-in: an open record -- an entry never holds evidence, only names a count.</summary>
    [TestMethod]
    public void ResourceObservationExclusionAccountingIsAnOpenRecord()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgResourceObservationExclusionAccounting::.ctor("
                + N + "LuxembourgResourceObservationExclusionAccounting) -> "
                + N + "LuxembourgResourceObservationExclusionAccounting",
                "constructor public instance " + N + "LuxembourgResourceObservationExclusionAccounting::.ctor("
                + "System.String, " + N + "LuxembourgResourceObservationExclusionCause, System.Int32) -> "
                + N + "LuxembourgResourceObservationExclusionAccounting",
                "method public instance " + N + "LuxembourgResourceObservationExclusionAccounting::<Clone>$() -> "
                + N + "LuxembourgResourceObservationExclusionAccounting",
            },
            ConstructionSurface.Of(typeof(LuxembourgResourceObservationExclusionAccounting)).ToArray());
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
                + N + "LuxembourgQueryExecutionRefusal, " + Contracts + "LuxembourgProfileResolutionFailure?, "
                + "System.String?) -> " + N + "LuxembourgQueryExecutionRefusalDetail",
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
                "constructor private instance "
                + N
                + "LuxembourgQueryExecutionResult::.ctor("
                + Core
                + "SourceProfileTopology, System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgFamilyEnumerationOutcome>, "
                + "System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgRelationFamilyAcquisition>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgResourceObservationExclusionAccounting>, "
                + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?, System.String?, "
                + "System.Nullable<"
                + N
                + "LuxembourgQueryExecutionCompletion>, "
                + "System.Collections.Generic.IReadOnlyDictionary<System.Int32, "
                + "Lex.V3.Ingest.CorpusAcquisitionOutcome>?, "
                + Core
                + "SourceArtifactRef?, "
                + "Lex.V3.Contracts.Source.Corpus.VerifiedCorpusRecordSet?, "
                + N
                + "LuxembourgQueryExecutionRefusalDetail?) -> "
                + N
                + "LuxembourgQueryExecutionResult",
                "method public static "
                + N
                + "LuxembourgQueryExecutionResult::Delivered("
                + Core
                + "SourceProfileTopology, System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgFamilyEnumerationOutcome>, "
                + "System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgRelationFamilyAcquisition>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgResourceObservationExclusionAccounting>, "
                + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt, System.String, "
                + "System.Collections.Generic.IReadOnlyDictionary<System.Int32, "
                + "Lex.V3.Ingest.CorpusAcquisitionOutcome>, "
                + Core
                + "SourceArtifactRef, "
                + "Lex.V3.Contracts.Source.Corpus.VerifiedCorpusRecordSet) -> "
                + N
                + "LuxembourgQueryExecutionResult",
                "method public static "
                + N
                + "LuxembourgQueryExecutionResult::Refused("
                + Core
                + "SourceProfileTopology, System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgFamilyEnumerationOutcome>, "
                + "System.Collections.Generic.IReadOnlyList<"
                + N
                + "LuxembourgRelationFamilyAcquisition>, "
                + N
                + "LuxembourgQueryExecutionRefusalDetail) -> "
                + N
                + "LuxembourgQueryExecutionResult",
            },
            ConstructionSurface.Of(typeof(LuxembourgQueryExecutionResult)).ToArray());
    }

    /// <summary>
    /// D1-04b's reviewer fold-in: the review noted the <see cref="ConstructionSurface.Of"/> pin above
    /// does not catch a change to <c>RunAsync</c>'s own signature or return construction, since that
    /// method lives on <see cref="LuxembourgQueryExecutionAdapter"/>, not on the result type itself.
    /// This sweeps the whole assembly for every door onto <see cref="LuxembourgQueryExecutionResult"/>
    /// outside its own hierarchy: today that is exactly <c>RunAsync</c>, and nothing else.
    /// </summary>
    [TestMethod]
    public void OnlyRunAsyncProducesAQueryExecutionResultFromOutsideItsOwnHierarchy()
    {
        // D1-04c item 2: RunAsync is now two overloads, not one. The public five-parameter door is
        // the only one production code can reach; it never accepts a caller-supplied evidence
        // resolver. The internal six-parameter door is the test-only seam
        // (InternalsVisibleTo("Lex.V3.Ingest.Tests") already grants this assembly's own tests
        // access; nothing here widens that grant), reachable only from this assembly and its tests,
        // never from outside. includeNonPublic: true means this sweep sees the internal overload
        // too, so both doors are pinned here rather than only the one a public-only sweep would see.
        CollectionAssert.AreEqual(
            new[]
            {
                "method internal instance "
                + N
                + "LuxembourgQueryExecutionAdapter::RunAsync("
                + "System.Collections.Generic.IReadOnlyList<System.ValueTuple<"
                + N
                + "LuxembourgPartitionRunRequest, "
                + Core
                + "BoundMachineRequest, "
                + Contracts
                + "LuxembourgPartitionChain>>, System.String?, System.String?, "
                + "System.String?, "
                + "Lex.V3.Contracts.Source.Scope.IScopeReductionEvidenceResolver?, "
                + Core
                + "MachineQueryRendererSource, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<"
                + N
                + "LuxembourgQueryExecutionResult>",
                "method public instance "
                + N
                + "LuxembourgQueryExecutionAdapter::RunAsync("
                + "System.Collections.Generic.IReadOnlyList<System.ValueTuple<"
                + N
                + "LuxembourgPartitionRunRequest, "
                + Core
                + "BoundMachineRequest, "
                + Contracts
                + "LuxembourgPartitionChain>>, System.String?, System.String?, "
                + "System.String?, "
                + Core
                + "MachineQueryRendererSource, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<"
                + N
                + "LuxembourgQueryExecutionResult>",
            },
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgQueryExecutionResult).Assembly,
                typeof(LuxembourgQueryExecutionResult),
                true).ToArray(),
            "a query execution result reached a new holder in Lex.V3.Ingest");
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
