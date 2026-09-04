using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Every closed vocabulary in the swept assemblies, member by member. 23 of them when this was
/// written, and the count is the point: before this pin existed nobody had asked how many there
/// were.
/// </summary>
/// <remarks>
/// <para>
/// The defect this answers. On 2026-09-04 four closed vocabularies were found carrying no pin of
/// any kind, one at a time and each by accident: a member added to any of them would have broken
/// nothing. Four found by accident says nothing about how many there are, so this sweeps the whole
/// assembly rather than the vocabularies somebody thought of. An enum added tomorrow arrives as a
/// new element here and fails this test; a member added to an existing one changes that
/// vocabulary's element and fails it too.
/// </para>
/// <para>
/// Why it is a sweep rather than a list checking itself. <see cref="ClosedSurfaceCensus"/> selects
/// on <c>Type.IsEnum</c> and nothing else, so the selection cannot be narrowed by the answer below.
/// A completeness test that filtered an assembly scan through the names it then compared against
/// was written in this repository on the same day, and it could only ever return names already in
/// the list. Ask what change to the assembly would flip this assertion: adding an enum anywhere in
/// any swept assembly, in any namespace, at any visibility, nested or not.
/// </para>
/// <para>
/// What it does not do. It pins member names in <see cref="Enum.GetNames(Type)"/> order, which is
/// by underlying value, so a renumbering that reorders members fails and a dense renumbering that
/// preserves the order passes. It pins names, not wire tokens: the per-type pins that call
/// <c>AssertTokens</c> or read <c>JsonStringEnumMemberNameAttribute</c> own the tokens, and this
/// does not replace them. It also does not claim a member is reachable, only that it is declared,
/// which is the other half of the same defect and needs a producer scan rather than a member scan.
/// </para>
/// <para>
/// When a real change makes this fail, that is the pin working rather than a defect in it, and the
/// fix is not to hand edit the array until it matches. Re-derive it: print
/// <c>ClosedSurfaceCensus.RenderForTranscription</c> over
/// <c>ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)</c>
/// from a throwaway test, read the diff, and paste the printed block between the braces below.
/// That renderer emits the exact
/// wrapping and escaping used here, so the paste is the whole edit. Never build the expected side
/// from ClosedVocabularies inside this test: it would then agree with whatever the code happens to say, which
/// is the one thing a pin must not do, and it is how a large array quietly stops being evidence.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClosedVocabularyCensusTests
{
    [TestMethod]
    public void EveryClosedVocabularyInTheSweptAssembliesIsPinnedMemberByMember()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Ingest.CorpusRecordOutcomeKind: Held, NotHeld, PendingAcquisition",
                "Lex.V3.Ingest.CorpusRecordSetCompletionState: Complete, Partial",
                "Lex.V3.Ingest.CorpusRecordSetWriteRefusalKind: RecordSetNotHeld",
                "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptRefusal: None, RobotsBootstrapRefused, "
                    + "ObservationNotExecuted",
                "Lex.V3.Ingest.Europe.EuEnumerationRefusal: None, RobotsBootstrapRefused, "
                    + "CustodyFloorNotObserved, ObservationNotExecuted, StatusNotAdmitted, "
                    + "MediaTypeNotAdmitted, CountNotOneNonNegativeInteger, PartitionRequired, "
                    + "DeliveredKeyNotRepresentable, DeliveredRowOutsidePartition, "
                    + "CursorDidNotAdvance, PageBudgetExhausted, CustodyMemberMissing, "
                    + "DeliveryProofRefused, PageBodyMalformed",
                "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcomeKind: Proven, ExecutorRefused, "
                    + "ProofRefused",
                "Lex.V3.Ingest.Europe.EuQueryExecutionCompletion: AllFamiliesProven, "
                    + "PartialFamilyRefused",
                "Lex.V3.Ingest.Europe.EuQueryExecutionRefusal: None, CensusFamilyNotProven, "
                    + "ObjectFactsFamilyNotProven, FamilyRowsNotVerified, RootBindingRefused, "
                    + "RecordFormNotResolved, ObjectDecodeRefused, ScopeManifestNotHeld, "
                    + "ManifestBindingRefused, WatermarkBootstrapRefused, WatermarkPlanRefused, "
                    + "RootWatermarkBindingRefused, WitnessBindingRefused, "
                    + "WitnessReconciliationRefused, ScopeReductionRefused, WitnessTraversalRefused, "
                    + "DocumentFetchSessionNotStarted, DocumentBodyNotHeld, "
                    + "DocumentFetchOutcomeNotRepresentable, RecordSetNotHeld",
                "Lex.V3.Ingest.Europe.EuWitnessTraversalRefusal: None, RobotsBootstrapRefused, "
                    + "BindRefused, ObservationNotExecuted, StatusNotAdmitted, MediaTypeNotAdmitted, "
                    + "PageBodyMalformed, CrossingRefused, StepRefused, EntrySetRefused, "
                    + "PageBudgetExhausted",
                "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRefusal: None, "
                    + "RobotsBootstrapRefused, CustodyFloorNotObserved, ObservationNotExecuted, "
                    + "StatusNotAdmitted, MediaTypeNotAdmitted, CountNotOneNonNegativeInteger, "
                    + "PartitionRequired, DeliveredKeyNotRepresentable, "
                    + "DeliveredRowOutsidePartition, CursorDidNotAdvance, PageBudgetExhausted, "
                    + "CustodyMemberMissing, DeliveryProofRefused, PageBodyMalformed",
                "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcomeKind: Proven, "
                    + "ExecutorRefused, ProofRefused, CoverProven, CoverRefused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationRefusal: "
                    + "LeafExecutorRefused, LeafProofRefused, CoverReconciliationRefused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservationBuildO"
                    + "utcomeKind: Built, SubjectNotInCensus, ObjectKindNotRecognised, TermUnbound",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionCompletion: AllFamiliesProven, "
                    + "PartialFamilyRefused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusal: None, "
                    + "ScopeResolutionFailed, ScopeManifestNotHeld, "
                    + "ResourceObservationFamilyNotProven, ResourceObservationRowsNotVerified, "
                    + "ObservationSubjectNotInDeliveredCensus, AssertionRowObjectKindNotRecognised, "
                    + "AssertionRowTermUnbound",
                "Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisitionState: "
                    + "AcquiredComplete, Unacquired, Incomplete, Uncertain",
                "Lex.V3.Ingest.Luxembourg.LuxembourgResourceObservationExclusionCause: "
                    + "PredicateNotAdmitted, BlankNodeObject",
                "Lex.V3.Ingest.ObservationAttemptFailureKind: NotExecuted, StatusNotAdmitted, "
                    + "MediaTypeNotAdmitted",
                "Lex.V3.Ingest.OfficialMachineQueryLocalSafetyReason: "
                    + "ApplicableRobotsGroupUninterpretable, RobotsPolicyUnavailable",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+BodyCaptureEvent: DeclaredLengthReached, "
                    + "CleanEof, CapSentinel, CallerCancelledAfterHeaders, BodyDeadline, "
                    + "ResponseEnded, BodyReadFailure",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PostHeaderFailureClass: "
                    + "UnsupportedNegotiatedProtocol, UnsupportedStatus, HeaderProjectionRejected, "
                    + "AdapterIdentityRejected, HopRepresentationRejected",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RedirectPolicyKind: RobotsRoute, "
                    + "NoRedirect, AdmittedOriginRoute",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RequestPolicyKind: RobotsGet, "
                    + "MachineQueryPost, MachineQueryGet",
            },
            ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere).ToArray());
    }
}
