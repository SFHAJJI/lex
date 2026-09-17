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
                    + "method internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunDocumentAcquisi"
                    + "tionAsync, "
                    + "method public instance Lex.V3.Ingest.CorpusAcquisitionOutcome::<Clone>$, "
                    + "method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Held, "
                    + "method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Refused",
                "Lex.V3.Ingest.CorpusRecordSetReadResult: constructor private instance "
                    + "Lex.V3.Ingest.CorpusRecordSetReadResult::.ctor, "
                    + "method public instance Lex.V3.Ingest.CorpusRecordSetReader::ReadAsync, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetReadResult::Refused, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetReadResult::Reopened",
                "Lex.V3.Ingest.CorpusRecordSetWriteResult: constructor private instance "
                    + "Lex.V3.Ingest.CorpusRecordSetWriteResult::.ctor, "
                    + "method public instance Lex.V3.Ingest.CorpusRecordSetWriter::WriteAsync, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetWriteResult::Refused, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetWriteResult::Written",
                "Lex.V3.Ingest.Europe.EuAmendmentAttributionCoverage: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuAmendmentAttributionCoverage::.ctor, "
                    + "constructor private static "
                    + "Lex.V3.Ingest.Europe.EuAmendmentAttributionCoverage::.cctor",
                "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::.ctor",
                "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult::Success, "
                    + "method private static Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::Refused, "
                    + "method public instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::RunAsync",
                "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification::.ctor",
                "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassificationResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassificationResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassificationResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassificationResult::Success, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassifier::Refused, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassifier::RunAsync",
                "Lex.V3.Ingest.Europe.EuBoundAnnexBodyMemberClassification: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyMemberClassification::.ctor, "
                    + "1 compiler-generated",
                "Lex.V3.Ingest.Europe.EuBoundAnnexEvidence: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexEvidence::.ctor, 6 compiler-generated",
                "Lex.V3.Ingest.Europe.EuCaseLawLinkProductionResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuCaseLawLinkProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuCaseLawLinkProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuCaseLawLinkProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuCaseLawLinkProductionResult::Success, "
                    + "method public instance Lex.V3.Ingest.Europe.EuCaseLawLinkProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuCorrigendumTripwireCompletion: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireCompletion::.ctor",
                "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer+Pairing: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer+Pairing::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer::BeginPairing",
                "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer+Pairing::RunExpressionFac"
                    + "tsAndProduceAsync, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::Success, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer::RetainAsync, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer::RefuseFold, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer::RefuseUnlessDelivered, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer::RunAsync, "
                    + "1 compiler-generated",
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
                "Lex.V3.Ingest.Europe.EuDerivedPdfPageMapping: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuDerivedPdfPageMapping::.ctor, "
                    + "method private static Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::Map",
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
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer+Pairing::RunExpressionFac"
                    + "tsAndProduceAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer+Pairing::RunObjectFactsAs"
                    + "ync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing::RunExpressi"
                    + "onFactsAndDeriveAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing::RunObjectFa"
                    + "ctsAsync, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunPassesAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunCaseLawLinksAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunCensusPartitionAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunEuFormexManifestation"
                    + "sAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunEuProcedureEventsAsyn"
                    + "c, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgConsolidati"
                    + "onByActAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgDraftGraphA"
                    + "sync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgInitialDraf"
                    + "tInventoryAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgOpinionRequ"
                    + "estGraphAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgOpinionRequ"
                    + "estInventoryAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgOpinionsAsy"
                    + "nc, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgTranspositi"
                    + "onIdentitiesAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunNationalImplementingM"
                    + "easuresAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunObjectFactsPartitionA"
                    + "sync, "
                    + "method public static Lex.V3.Ingest.Europe.EuEnumerationRunResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuEnumerationRunResult::Refused, "
                    + "2 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::ExecutorRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::ProofRefused, "
                    + "method public static Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::Proven",
                "Lex.V3.Ingest.Europe.EuFormexAnnexClassificationReconciliation: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexClassificationReconciliation::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexClassificationReconciliation::TryClose",
                "Lex.V3.Ingest.Europe.EuFormexAnnexInventory: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexInventory::.ctor",
                "Lex.V3.Ingest.Europe.EuFormexAnnexInventoryProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuFormexAnnexInventoryProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexInventoryProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexInventoryProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexInventoryProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuFormexEligibilityPopulation: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuFormexEligibilityPopulation::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexEligibilityPopulation::TryCreate",
                "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationProducer::RunAsync, "
                    + "1 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFormexPackageOutcome: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::.ctor, "
                    + "method public static Lex.V3.Ingest.Europe.EuFormexPackageOutcome::Acquired, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::NotEligible, "
                    + "method public static Lex.V3.Ingest.Europe.EuFormexPackageOutcome::Refused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::Unavailable, "
                    + "2 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFormexPackageOutcomePopulation: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuFormexPackageOutcomePopulation::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcomePopulation::TryClose, "
                    + "1 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFormexRunOutcomeReconciliation: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuFormexRunOutcomeReconciliation::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexRunOutcomeReconciliation::TryClose",
                "Lex.V3.Ingest.Europe.EuImageOnlyAnnexProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuImageOnlyAnnexProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuImageOnlyAnnexProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuImageOnlyAnnexProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuImageOnlyAnnexProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing::.ctor",
                "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing::RunExpressi"
                    + "onFactsAndDeriveAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::RunWithDeliveriesAs"
                    + "ync, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::Success, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::DeriveAndRetainAsyn"
                    + "c, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::TryOpenDeliveryAsyn"
                    + "c, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::RefuseBeforeTraffic, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentAmbiguity: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAmbiguity::.ctor",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomObservation: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomObservation::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::DecodeLocatedAmendmentBatches, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomDecode::Conflict, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomDecode::DecodeOne, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomDecode::TryDecode",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomProjection: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomProjection::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomProjection::TryCreate, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomProjection::Refused",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentExclusion: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentExclusion::.ctor",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentProduction: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentProduction::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentProducer::Produce",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentRawProperty: by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomDecode+RawAxiom::Deconstruct, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuLocatedAmendmentRawProperty::.ctor",
                "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureProductionResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuObjectPopulationCompletion: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuObjectPopulationCompletion::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuObjectPopulationCompletion::TryClose",
                "Lex.V3.Ingest.Europe.EuProcedureEventProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuProcedureEventProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuProcedureEventProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuProcedureEventProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuProcedureEventProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuProcedureEventProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuProductionScopeReductionEvidenceResolver: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Europe.EuProductionScopeReductionEvidenceResolver::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuProductionScopeReductionEvidenceResolver::CreateAsync",
                "Lex.V3.Ingest.Europe.EuPublisherMarkedAmendmentAttribution: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuPublisherMarkedAmendmentAttribution::.ctor",
                "Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunDocumentAcquisitionAsync, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::TryPairExpressionAndObjectBatc"
                    + "hes",
                "Lex.V3.Ingest.Europe.EuQueryExecutionResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::DeliveredWithLocatedAmendments, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::DeliveredWithLocatedAmendments, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunAsync, "
                    + "method public static Lex.V3.Ingest.Europe.EuQueryExecutionResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuQueryExecutionResult::Refused",
                "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationProducer::ProduceAsync",
                "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationRow: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationRow::.ctor",
                "Lex.V3.Ingest.Europe.EuTranspositionBridgeProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuTranspositionBridgeProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeProductionResult::Success, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeProducer::Produce",
                "Lex.V3.Ingest.Europe.EuTranspositionBridgeReconciliationResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeReconciliationResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeReconciliationResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeReconciliationResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgeReconciliationProducer::ProduceAsy"
                    + "nc",
                "Lex.V3.Ingest.Europe.EuWitnessTraversalRefusalDetail: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuWitnessTraversalRefusalDetail::.ctor",
                "Lex.V3.Ingest.Europe.EuWitnessTraversalResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuWitnessTraversalResult::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunWitnessTraversalAsync, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuWitnessTraversalResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuWitnessTraversalResult::Refused",
                "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventory: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventory::.ctor",
                "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventoryProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuXhtmlAnnexInventoryProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventoryProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventoryProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventoryProducer::RunAsync",
                "Lex.V3.Ingest.Europe.LuxembourgDraftGraphRunRequest: constructor private instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgDraftGraphRunRequest::.ctor, "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgDraftGraphRunRequest::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgDraftGraphRunRequest::<Clone>$, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.LuxembourgDraftGraphRunRequest::ForBatch",
                "Lex.V3.Ingest.Europe.LuxembourgOpinionRequestGraphRunRequest: constructor private "
                    + "instance Lex.V3.Ingest.Europe.LuxembourgOpinionRequestGraphRunRequest::.ctor, "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgOpinionRequestGraphRunRequest::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgOpinionRequestGraphRunRequest::<Clone>$, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.LuxembourgOpinionRequestGraphRunRequest::ForBatch",
                "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventory: by-ref-method private "
                    + "static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryProducer::TryInventory, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventory::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryOutcome: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryOutcome::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryPopulation: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryPopulation::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentArticle: constructor internal "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentArticle::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentOutcome: constructor internal "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentOutcome::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentPopulation: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentPopulation::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentProfileProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgConsolidationByActResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgConsolidationByActResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgConsolidationByActProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgConsolidationByActResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgConsolidationByActResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgConsolidationByActProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgDocumentGetAttemptResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgDocumentGetAttemptResult::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor::RunDocumentG"
                    + "etAsync, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDocumentGetAttemptResult::Executed, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDocumentGetAttemptResult::Refused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDocumentGetAttemptResult::RobotsRefused",
                "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphBatchCover: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphBatchCover::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphBatchCover::TryCreate",
                "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphProductionResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgDraftGraphProducer::RunAsync",
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
                    + "2 compiler-generated",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyProductionResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationInput: constructor internal "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationInput::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationPopulation: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationPopulation::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationPopulation::TryCreate, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationPopulation::TryCreateWi"
                    + "thFinalRights, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationPopulation::TryCreateCo"
                    + "re",
                "Lex.V3.Ingest.Luxembourg.LuxembourgInitialDraftInventoryResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgInitialDraftInventoryResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgInitialDraftInventoryProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgInitialDraftInventoryResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgInitialDraftInventoryResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgInitialDraftInventoryProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgObjectPopulationCompletion: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObjectPopulationCompletion::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObjectPopulationCompletion::TryClose",
                "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySet: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySet::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.VerifiedLuxembourgObservedObjectIdentitySet::Parse, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySet::FromObservatio"
                    + "ns",
                "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetReadResult: "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetReadResult::.cto"
                    + "r, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetReadResult::Refu"
                    + "sed, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetReadResult::Reop"
                    + "ened, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetReader::ReadAsyn"
                    + "c",
                "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriteResult: "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriteResult::.ct"
                    + "or, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriteResult::Ref"
                    + "used, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriteResult::Ret"
                    + "ained, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriter::WriteAsy"
                    + "nc",
                "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionProductionResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgOpinionProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionProductionResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestBatchCover: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestBatchCover::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestBatchCover::TryCreate",
                "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestGraphResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestGraphResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestGraphResult::Completed, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestGraphResult::Refused, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestGraphProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestInventoryResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestInventoryResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestInventoryProducer::DecodeRo"
                    + "ws, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestInventoryResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestInventoryResult::Success, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgOpinionRequestInventoryProducer::RunAsync",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceArtifactCodec+Writer: "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceArtifactCodec+Writer::.c"
                    + "tor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceOutcome: constructor internal "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceOutcome::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProducer::Outcome",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidencePopulation: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidencePopulation::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidencePopulation::Create",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProductionResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProductionResult::Refuse"
                    + "d, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProductionResult::Succes"
                    + "s, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityOutcome: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityOutcome::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityProducer::Classify, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityProducer::Outcome, "
                    + "1 compiler-generated",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityPopulation: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityPopulation::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityProducer::Produce",
                "Lex.V3.Ingest.Luxembourg.LuxembourgProductionScopeReductionEvidenceResolver: "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgProductionScopeReductionEvidenceResolver:"
                    + ":.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgProductionScopeReductionEvidenceResolver:"
                    + ":CreateAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopeOutcome: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopeOutcome::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopeProducer::Classify, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopeProducer::Outcome",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopePopulation: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopePopulation::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopePopulation::Create, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopeProducer::Produce",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerArtifactCodec+Writer: "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerArtifactCodec+Writer"
                    + "::.ctor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerOutcome: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerOutcome::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerProfileProducer::Out"
                    + "come",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerPopulation: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerPopulation::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerPopulation::Create",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerProductionResult: "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerProductionResult::.c"
                    + "tor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerProductionResult::Re"
                    + "fused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerProductionResult::Su"
                    + "ccess, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerProfileProducer::Run"
                    + "Async",
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
                    + "BuildResult::RelationPredicateNotAdmitted, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::RelationSubjectNotInCensus, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::RelationTermNotIri, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::RelationTermUnbound, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::SubjectNotInCensus, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+ResourceObservation"
                    + "BuildResult::TermUnbound",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail: constructor "
                    + "internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunDocumentAcquisi"
                    + "tionAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunGazetteAcquisit"
                    + "ionAsync, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::CompletePopulation"
                    + "Ledger, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::HoldManifestAsync, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::ReadInFileRightsAs"
                    + "ync, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::MintDocumentFetchS"
                    + "elections",
                "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunScopedAsync, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunCoreAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunScopedAsync, "
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
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisition::CompleteAll, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgRelationFamilyAcquisition::NotComplete, "
                    + "3 compiler-generated",
                "Lex.V3.Ingest.Luxembourg.LuxembourgSelectedDocumentFetch: constructor internal "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgSelectedDocumentFetch::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::MintDocumentFetchS"
                    + "elections, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::SelectDocumentFetc"
                    + "h",
                "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityProductionResult: "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityCompletedBatch::Deco"
                    + "nstruct, "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityProductionResult::.c"
                    + "tor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityProducer::DecodeRows, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityProductionResult::Re"
                    + "fused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityProductionResult::Su"
                    + "ccess, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityPopulationProducer::"
                    + "Refused, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityPopulationProducer::"
                    + "ProduceAsync, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionIdentityProducer::RunAsync",
                "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionProductionResult: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionProductionResult::.ctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionProductionResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionProductionResult::Success, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionProducer::Produce, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTranspositionProducer::Produce",
                "Lex.V3.Ingest.Luxembourg.LuxembourgTypedAssertion: constructor internal instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgTypedAssertion::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::TryBuildTypedAsser"
                    + "tions",
                "Lex.V3.Ingest.Luxembourg.VerifiedLuxembourgObservedObjectIdentitySet: constructor "
                    + "private instance "
                    + "Lex.V3.Ingest.Luxembourg.VerifiedLuxembourgObservedObjectIdentitySet::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.VerifiedLuxembourgObservedObjectIdentitySet::ParseA"
                    + "ndVerify",
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
                "Lex.V3.Ingest.Stage3BodyComposition: constructor private instance "
                    + "Lex.V3.Ingest.Stage3BodyComposition::.ctor, "
                    + "method public static Lex.V3.Ingest.Stage3BodyComposition::TryCreate",
                "Lex.V3.Ingest.Stage3DerivationProfileEnvelope: constructor private instance "
                    + "Lex.V3.Ingest.Stage3DerivationProfileEnvelope::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Stage3DerivationProfileEnvelope::TryCreate",
                "Lex.V3.Ingest.Stage3EuropeBodyComposition: constructor internal instance "
                    + "Lex.V3.Ingest.Stage3EuropeBodyComposition::.ctor, 1 compiler-generated",
                "Lex.V3.Ingest.Stage3EvidenceEnvelope: constructor private instance "
                    + "Lex.V3.Ingest.Stage3EvidenceEnvelope::.ctor, "
                    + "method private static Lex.V3.Ingest.Stage3EvidenceEnvelope::TryCreateCore, "
                    + "method public static Lex.V3.Ingest.Stage3EvidenceEnvelope::TryCreate, "
                    + "method public static "
                    + "Lex.V3.Ingest.Stage3EvidenceEnvelope::TryCreateWithEuropeLegalNoticeRoute",
                "Lex.V3.Ingest.Stage3EvidenceLineage: constructor private instance "
                    + "Lex.V3.Ingest.Stage3EvidenceLineage::.ctor, "
                    + "method public static Lex.V3.Ingest.Stage3EvidenceLineage::TryBind",
                "Lex.V3.Ingest.Stage3FidelityPreservationReconciliation: constructor private "
                    + "instance Lex.V3.Ingest.Stage3FidelityPreservationReconciliation::.ctor, "
                    + "constructor private static "
                    + "Lex.V3.Ingest.Stage3FidelityPreservationReconciliation::.cctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Stage3FidelityPreservationReconciliation::TryCreate",
                "Lex.V3.Ingest.Stage3LuxembourgBodyComposition: constructor internal instance "
                    + "Lex.V3.Ingest.Stage3LuxembourgBodyComposition::.ctor",
                "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet: by-ref-method public instance "
                    + "Lex.V3.Ingest.LexCorpus6BuildResult::Deconstruct, "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet::ParseAndVerify",
                "Lex.V3.Ingest.WireBudgetSnapshot: constructor private instance "
                    + "Lex.V3.Ingest.WireBudgetSnapshot::.ctor, "
                    + "constructor private instance Lex.V3.Ingest.WireBudgetSnapshot::.ctor, "
                    + "method public instance Lex.V3.Ingest.WireBudgetSnapshot::<Clone>$, "
                    + "method public static Lex.V3.Ingest.WireBudgetSnapshot::Of",
                "Lex.V3.Ingest.WireRequestBudget: by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuCaseLawRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuCensusPartitionRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuFormexManifestationRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuNationalImplementingMeasureRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuObjectFactsPartitionRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuProcedureEventRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgConsolidationByActRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgInitialDraftInventoryRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgOpinionRequestInventoryRunRequest::Deconstruc"
                    + "t, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgOpinionRunRequest::Deconstruct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.LuxembourgTranspositionIdentityRunRequest::Deconstruct, "
                    + "constructor private instance Lex.V3.Ingest.WireRequestBudget::.ctor, "
                    + "method public static Lex.V3.Ingest.WireRequestBudget::OfWireRequests",
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
