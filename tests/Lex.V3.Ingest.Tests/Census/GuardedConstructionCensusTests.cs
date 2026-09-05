using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Every type in the swept assemblies whose declared constructors are all non-public, with the
/// members that can hand one out. 33 of them when this was written.
/// </summary>
/// <remarks>
/// <para>
/// A type with no public constructor is a type whose author decided callers must come through a
/// named door, and the value of that decision is exactly the number of doors. The per-type pins
/// built on <see cref="ConstructionSurface.Of"/> state that number exactly for the types somebody
/// remembered to guard. This states it for all of them, so a type that nobody remembered is inside
/// a pin rather than outside every pin, and a second factory added anywhere in the assembly fails
/// this test.
/// </para>
/// <para>
/// Abstract bases are in, and were not always. This pin once excluded them while its own summary
/// stated the rule that includes them, and the nine abstract closed-union bases with private
/// protected constructors were then outside the census and outside its residual at once: the most
/// tightly guarded shape in the repository, uncounted, behind a sentence that said otherwise.
/// <see cref="AnAbstractClosedUnionBaseIsInsideThisSweep"/> is the standing check that the shape is
/// still admitted, so the clause cannot come back without a red test.
/// </para>
/// <para>
/// Why it is a sweep. <see cref="ClosedSurfaceCensus"/> selects on the type declaring at least one
/// constructor and none of them public. That is a property of the type, so a new guarded type
/// appears here on its own, and a type that gains a public constructor drops out of the list and
/// fails the pin rather than silently leaving the census. Neither the selection nor the rendering
/// consults the expected answer below.
/// </para>
/// <para>
/// What it does not do, stated so nobody cites it for more than it checks. Each door is the
/// construction surface's own entry without its parameter list, so an existing door changing its
/// parameters passes here; the exact per-type pins catch that where they exist, and where they do
/// not that gap is real. Holders are excluded, so a field or property that carries the type is not
/// a line here. Doors the compiler generated for lambdas are counted rather than named, because
/// their mangled ordinals move when an unrelated method is added above them, and a pin that fires
/// on edits that opened no door is a pin people learn to regenerate without reading.
/// </para>
/// <para>
/// When a real change makes this fail, that is the pin working rather than a defect in it, and the
/// fix is not to hand edit the array until it matches. Re-derive it: print
/// <c>ClosedSurfaceCensus.RenderForTranscription</c> over
/// <c>ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere)</c>
/// from a throwaway test, read the diff, and paste the printed block between the braces below.
/// That renderer emits the exact
/// wrapping and escaping used here, so the paste is the whole edit. Never build the expected side
/// from GuardedConstruction inside this test: it would then agree with whatever the code happens to say, which
/// is the one thing a pin must not do, and it is how a large array quietly stops being evidence.
/// </para>
/// </remarks>
[TestClass]
public sealed class GuardedConstructionCensusTests
{
    [TestMethod]
    public void EveryConstructionRestrictedTypeInTheSweptAssembliesHasExactlyTheseDoors()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Ingest.CorpusAcquisitionOutcome: constructor private instance "
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome::.ctor, "
                    + "constructor private instance Lex.V3.Ingest.CorpusAcquisitionOutcome::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunDocumentAcquisitionAsync, "
                    + "method public instance Lex.V3.Ingest.CorpusAcquisitionOutcome::<Clone>$, "
                    + "method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Held, "
                    + "method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Refused",
                "Lex.V3.Ingest.CorpusRecordSetWriteResult: constructor private instance "
                    + "Lex.V3.Ingest.CorpusRecordSetWriteResult::.ctor, "
                    + "method public instance Lex.V3.Ingest.CorpusRecordSetWriter::WriteAsync, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetWriteResult::Refused, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetWriteResult::Written",
                "Lex.V3.Ingest.Europe.EuDeliveryEvidenceSet: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuDeliveryEvidenceSet::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuDeliveryEvidenceSet::MaterializeAsync",
                "Lex.V3.Ingest.Europe.EuDeliveryObservation: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuDeliveryObservation::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuDeliveryPass::AllObservations, "
                    + "method public static Lex.V3.Ingest.Europe.EuDeliveryObservation::ForRequest",
                "Lex.V3.Ingest.Europe.EuDeliveryPass: by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor+PassOutcome::Deconstruct, "
                    + "constructor private instance Lex.V3.Ingest.Europe.EuDeliveryPass::.ctor, "
                    + "method public instance Lex.V3.Ingest.Europe.EuDeliveryPass::WithPage, "
                    + "method public static Lex.V3.Ingest.Europe.EuDeliveryPass::BeginWithCount",
                "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunDocumentFetchAsync, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::Executed, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::Refused",
                "Lex.V3.Ingest.Europe.EuEnumerationRefusalDetail: by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor+ObserveOutcome::Deconstru"
                    + "ct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor+PassOutcome::Deconstruct, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuEnumerationRefusalDetail::.ctor",
                "Lex.V3.Ingest.Europe.EuEnumerationRunResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuEnumerationRunResult::.ctor, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunPassesAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunCensusPartitionAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunObjectFactsPartitionA"
                    + "sync, "
                    + "method public static Lex.V3.Ingest.Europe.EuEnumerationRunResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuEnumerationRunResult::Refused",
                "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::ExecutorRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::ProofRefused, "
                    + "method public static Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::Proven",
                "Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunDocumentAcquisitionAsync",
                "Lex.V3.Ingest.Europe.EuQueryExecutionResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunAsync, "
                    + "method public static Lex.V3.Ingest.Europe.EuQueryExecutionResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuQueryExecutionResult::Refused",
                "Lex.V3.Ingest.Europe.EuWitnessTraversalRefusalDetail: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuWitnessTraversalRefusalDetail::.ctor",
                "Lex.V3.Ingest.Europe.EuWitnessTraversalResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuWitnessTraversalResult::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunWitnessTraversalAsync, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuWitnessTraversalResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuWitnessTraversalResult::Refused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationBudget: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationBudget::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationBudget::FromPlan",
                "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRefusalDetail: by-ref-method public "
                    + "instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor+ObserveOutcom"
                    + "e::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor+PassOutcome::"
                    + "Deconstruct, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRefusalDetail::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult::.ctor, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor::RunPartition"
                    + "OnSessionAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor::RunCoverAsyn"
                    + "c, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor::RunPartition"
                    + "Async, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult::Delivered, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult::Refused, "
                    + "1 compiler-generated",
                "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::FindProvenOutcome, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::CoverProven, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::CoverRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::ExecutorRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::ProofRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::Proven",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDetail: "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+CoverReconciliation"
                    + "Outcome::Deconstruct, "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDetail::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDetail::LeafE"
                    + "xecutorRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDetail::LeafP"
                    + "roofRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDetail::Recon"
                    + "ciliationRefused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgProductionScopeReductionEvidenceResolver: "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgProductionScopeReductionEvidenceResolver:"
                    + ":.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgProductionScopeReductionEvidenceResolver:"
                    + ":CreateAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservationBuildR"
                    + "esult: constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::.ctor, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::BuildResourceObser"
                    + "vations, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::Built, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::ObjectKindNotRecognised, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::SubjectNotInCensus, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::TermUnbound",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunAsync, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::Delivered, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::Refused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisition: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisition::.ctor, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::BuildRelationFamil"
                    + "yAcquisitions, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisition::Complete, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisition::NotComplete",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession: constructor private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession::.ctor, "
                    + "constructor private static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession::.cctor, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::StartSessionAsync",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult: constructor private "
                    + "instance Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::Executed, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::IntegrityFailure, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::Operational, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::PostHeaderRejected, "
                    + "method public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+IPlanItem::ExecuteNextAttemptAsyn"
                    + "c, "
                    + "method public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PlanItem::ExecuteNextAttemptAsync",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+BodyDeadlineException: "
                    + "base-constructor protected instance System.Exception::.ctor, "
                    + "base-constructor public instance System.Exception::.ctor, "
                    + "base-constructor public instance System.Exception::.ctor, "
                    + "base-constructor public instance System.Exception::.ctor, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+BodyDeadlineException::.ctor, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+BodyDeadlineException::.ctor",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+CanonicalArtifactBytes: by-ref-method "
                    + "public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+ResolvedMachineRequest::Deconstru"
                    + "ct, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+CanonicalArtifactBytes::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+SessionMachineArtifactResolver::C"
                    + "opyResolvedArtifacts, 1 compiler-generated",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PlanItem: constructor internal "
                    + "instance Lex.V3.Ingest.RoutedHttpAcquisitionSession+PlanItem::.ctor",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PostHeaderRejection: constructor "
                    + "private-protected instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PostHeaderRejection::.ctor, "
                    + "constructor public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PostHeaderFailure+MintedPostHeade"
                    + "rRejection::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+PostHeaderFailure::ToRejection",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RedirectPolicyArtifact: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RedirectPolicyArtifact::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RedirectPolicyArtifact::ForDocume"
                    + "ntFetch, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RedirectPolicyArtifact::ForRobots, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RedirectPolicyArtifact::NoRedirec"
                    + "t",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RequestPolicyArtifact: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RequestPolicyArtifact::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RequestPolicyArtifact::ForMachine"
                    + "Query, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RequestPolicyArtifact::ForMachine"
                    + "QueryGet, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+RequestPolicyArtifact::ForRobots",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+SendLease: constructor private "
                    + "instance Lex.V3.Ingest.RoutedHttpAcquisitionSession+SendLease::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+SendLease::FromRedirect, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+SendLease::Initial",
                "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult: constructor private "
                    + "instance Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::Integrity, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::Operational, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::PostHeaderRejected, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::PublisherDenied, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+StartResult::Started, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession::StartAsync, "
                    + "method internal static "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession::StartWithTestTransportAsync, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor::StartWithTes"
                    + "tHandlerAsync, "
                    + "method private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession::BootstrapRobotsAsync",
            },
            ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere).ToArray());
    }

    /// <summary>
    /// The shape the sweep once excluded, checked against the sweep itself rather than described.
    /// </summary>
    /// <remarks>
    /// This runs the real sweep over this test assembly, which holds the fixture below, and asks
    /// whether an abstract class whose only constructor is private protected comes back. A clause
    /// excluding abstract types, of the kind this file carried until 2026-09-05, turns this red.
    /// It is not a sweep of a swept assembly, so it does not replace the pin above; it is the
    /// guard on the pin's own admission rule.
    /// </remarks>
    [TestMethod]
    public void AnAbstractClosedUnionBaseIsInsideThisSweep()
    {
        var swept = ClosedSurfaceCensus.GuardedConstruction(
            typeof(GuardedConstructionCensusTests).Assembly.GetName().Name!);

        CollectionAssert.Contains(
            swept.Select(static row => row[..row.IndexOf(':', StringComparison.Ordinal)]).ToArray(),
            typeof(ClosedUnionBaseProbe).FullName,
            "an abstract base whose only constructor is private protected left the sweep");
    }

    /// <summary>
    /// A closed union base in the shape the source assemblies use: abstract, with the only
    /// constructor private protected, so every subtype has to be declared here.
    /// </summary>
    private abstract class ClosedUnionBaseProbe
    {
        private protected ClosedUnionBaseProbe()
        {
        }
    }
}
