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
        var expected = new[]
            {
                "Lex.V3.Ingest.ChunkedDerivedArtifact: constructor private instance "
                    + "Lex.V3.Ingest.ChunkedDerivedArtifact::.ctor, "
                    + "method internal static Lex.V3.Ingest.ChunkedDerivedArtifact::OpenAsync",
                "Lex.V3.Ingest.CorpusAcquisitionOutcome: constructor private instance Lex.V3.Ingest.CorpusAcquisiti"
                    + "onOutcome::.ctor, constructor private instance Lex.V3.Ingest.CorpusAcquisitionOutcome::.ctor, meth"
                    + "od internal instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunDocumentAcquisitionAsync, me"
                    + "thod internal instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunDocumentAcquisitionWithChe"
                    + "ckpointAsync, method internal instance Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::R"
                    + "unDocumentAcquisitionAsync, method internal static Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::R"
                    + "eopenDocumentAcquisitionAsync, method private instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapte"
                    + "r::RunDocumentAcquisitionCoreAsync, method public instance Lex.V3.Ingest.CorpusAcquisitionOutcome:"
                    + ":<Clone>$, method public static Lex.V3.Ingest.CorpusAcquisitionOutcome::Held, method public static"
                    + " Lex.V3.Ingest.CorpusAcquisitionOutcome::Refused",
                "Lex.V3.Ingest.CorpusRecordSetReadResult: constructor private instance "
                    + "Lex.V3.Ingest.CorpusRecordSetReadResult::.ctor, "
                    + "method public instance Lex.V3.Ingest.CorpusRecordSetReader::ReadAsync, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetReadResult::Refused, "
                    + "method public static Lex.V3.Ingest.CorpusRecordSetReadResult::Reopened",
                "Lex.V3.Ingest.CorpusRecordSetWriteResult: constructor private instance Lex.V3.Ingest.CorpusRecordS"
                    + "etWriteResult::.ctor, method internal instance Lex.V3.Ingest.CorpusRecordSetWriter::RebuildAsync, "
                    + "method private instance Lex.V3.Ingest.CorpusRecordSetWriter::WriteChunkedAsync, method private ins"
                    + "tance Lex.V3.Ingest.CorpusRecordSetWriter::WriteCoreAsync, method public instance Lex.V3.Ingest.Co"
                    + "rpusRecordSetWriter::WriteAsync, method public static Lex.V3.Ingest.CorpusRecordSetWriteResult::Re"
                    + "fused, method public static Lex.V3.Ingest.CorpusRecordSetWriteResult::Written",
                "Lex.V3.Ingest.Europe.EuAmendmentAttributionCoverage: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuAmendmentAttributionCoverage::.ctor, "
                    + "constructor private static "
                    + "Lex.V3.Ingest.Europe.EuAmendmentAttributionCoverage::.cctor",
                "Lex.V3.Ingest.Europe.EuAnnexBodyProduction: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexBodyProduction::.ctor",
                "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::.ctor, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::.ctor, 2 compiler-generated",
                "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexBodyProduction::BindAsync, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult::Refused, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBindingResult::Success, "
                    + "method private instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::ReconcileAsync, "
                    + "method private static Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::Refused, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::BindTransportAsync, "
                    + "method public instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinder::RunAsync",
                "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification::.ctor",
                "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassificationResult: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassificationResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexBodyProduction::ClassifyAsync, "
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
                "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult: constructor private instance Lex.V3.In"
                    + "gest.Europe.EuCorrigendumTripwireProductionResult::.ctor, method internal instance Lex.V3.Ingest.E"
                    + "urope.EuCorrigendumTripwireProducer+Pairing::RunExpressionFactsAndProduceAsync, method internal in"
                    + "stance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter+RunCheckpointContext::OpenTripwireAsync, metho"
                    + "d internal static Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::Refused, method inte"
                    + "rnal static Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::Success, method private in"
                    + "stance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProducer::RetainAsync, method private static Lex."
                    + "V3.Ingest.Europe.EuCorrigendumTripwireProducer::RefuseFold, method private static Lex.V3.Ingest.Eu"
                    + "rope.EuCorrigendumTripwireProducer::RefuseUnlessDelivered, method public instance Lex.V3.Ingest.Eu"
                    + "rope.EuCorrigendumTripwireProducer::RunAsync, method public static Lex.V3.Ingest.Europe.EuCorrigen"
                    + "dumTripwireProducer::ReopenAsync, 1 compiler-generated",
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
                "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult: "
                    + "constructor private instance Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::.ctor, "
                    + "method internal instance Lex.V3.Ingest.Europe.EuFormexPackageAcquisitionProducer+PackageReplayContext::FetchAsync, "
                    + "method internal instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter+DocumentLadderReplay::FetchAsync, "
                    + "method private instance Lex.V3.Ingest.Europe.EuFormexPackageAcquisitionProducer::FetchAsync, "
                    + "method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunDocumentFetchAsync, "
                    + "method public static Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::Executed, "
                    + "method public static Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::Refused, "
                    + "method public static Lex.V3.Ingest.Europe.EuDocumentFetchRouteReader::ReopenAsync",
                "Lex.V3.Ingest.Europe.EuEnumerationRefusalDetail: by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor+ObserveOutcome::Deconstru"
                    + "ct, "
                    + "by-ref-method public instance "
                    + "Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor+PassOutcome::Deconstruct, "
                    + "constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuEnumerationRefusalDetail::.ctor",
                "Lex.V3.Ingest.Europe.EuEnumerationRunResult: by-ref-method public instance Lex.V3.Ingest.Europe.Eu"
                    + "LanguageScopedExpressionProducer+RestoredFamily::Deconstruct, constructor private instance Lex.V3."
                    + "Ingest.Europe.EuEnumerationRunResult::.ctor, method internal instance Lex.V3.Ingest.Europe.EuCorri"
                    + "gendumTripwireProducer+Pairing::RunExpressionFactsAndProduceAsync, method internal instance Lex.V3"
                    + ".Ingest.Europe.EuCorrigendumTripwireProducer+Pairing::RunObjectFactsAsync, method internal instanc"
                    + "e Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing::RunExpressionFactsAndDeriveAsyn"
                    + "c, method internal instance Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer+Pairing::RunOb"
                    + "jectFactsAsync, method internal instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter+RunCheckpoin"
                    + "tContext::OpenCensusAsync, method internal instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter+R"
                    + "unCheckpointContext::OpenObjectsAsync, method internal static Lex.V3.Ingest.Europe.EuEnumerationRu"
                    + "nResult::DeliveredWithCheckpoint, method private instance Lex.V3.Ingest.Europe.EuRepeatedEnumerati"
                    + "onExecutor::RunPassesAsync, method private static Lex.V3.Ingest.Europe.EuQueryExecutionAdapter+Run"
                    + "CheckpointContext::OpenFamilyAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumerat"
                    + "ionExecutor::RunCaseLawLinksAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumerati"
                    + "onExecutor::RunCensusPartitionAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumera"
                    + "tionExecutor::RunEuFormexManifestationsAsync, method public instance Lex.V3.Ingest.Europe.EuRepeat"
                    + "edEnumerationExecutor::RunEuProcedureEventsAsync, method public instance Lex.V3.Ingest.Europe.EuRe"
                    + "peatedEnumerationExecutor::RunLuxembourgConsolidationByActAsync, method public instance Lex.V3.Ing"
                    + "est.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgDraftGraphAsync, method public instance Lex"
                    + ".V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgInitialDraftInventoryAsync, method p"
                    + "ublic instance Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgOpinionRequestGrap"
                    + "hAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::RunLuxembourgOp"
                    + "inionRequestInventoryAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecu"
                    + "tor::RunLuxembourgOpinionsAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumeration"
                    + "Executor::RunLuxembourgTranspositionIdentitiesAsync, method public instance Lex.V3.Ingest.Europe.E"
                    + "uRepeatedEnumerationExecutor::RunNationalImplementingMeasuresAsync, method public instance Lex.V3."
                    + "Ingest.Europe.EuRepeatedEnumerationExecutor::RunObjectFactsPartitionAsync, method public static Le"
                    + "x.V3.Ingest.Europe.EuEnumerationRunResult::Delivered, method public static Lex.V3.Ingest.Europe.Eu"
                    + "EnumerationRunResult::Refused, 3 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::ExecutorRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::ProofRefused, "
                    + "method public static Lex.V3.Ingest.Europe.EuFamilyEnumerationOutcome::Proven",
                "Lex.V3.Ingest.Europe.EuFirstMountAcquisitionResult: "
                    + "constructor private instance Lex.V3.Ingest.Europe.EuFirstMountAcquisitionResult::.ctor, "
                    + "method internal instance Lex.V3.Ingest.Europe.EuFirstMountAcquisitionResult::WithCheckpoint, "
                    + "method public instance Lex.V3.Ingest.Europe.EuFirstMountAcquisition::RunAsync, "
                    + "method public instance Lex.V3.Ingest.Europe.EuFirstMountAcquisition::RunAsync, "
                    + "method public static Lex.V3.Ingest.Europe.EuFirstMountAcquisition::ReopenAsync, "
                    + "method public static Lex.V3.Ingest.Europe.EuFirstMountAcquisitionResult::Refused, "
                    + "method public static Lex.V3.Ingest.Europe.EuFirstMountAcquisitionResult::Success",
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
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuFormexEligibilityPopulation::TryCreateCore, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexEligibilityPopulation::TryCreate, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexEligibilityPopulation::TryCreateForServedLangua"
                    + "ges",
                "Lex.V3.Ingest.Europe.EuFormexMainBodyArticle: constructor internal instance "
                    + "Lex.V3.Ingest.Europe.EuFormexMainBodyArticle::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentProducer::Parse, "
                    + "method private static "
                    + "Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentProducer::Refused",
                "Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentOutcome: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentOutcome::.ctor",
                "Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentPopulation: constructor internal "
                    + "instance Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentPopulation::.ctor, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuFormexMainBodyLegalContentProducer::RunAsync",
                "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult: constructor private instance Lex.V3.I"
                    + "ngest.Europe.EuFormexManifestationEnumerationResult::.ctor, method internal instance Lex.V3.Ingest"
                    + ".Europe.EuFormexManifestationEnumerationResult::WithCheckpoint, method internal instance Lex.V3.In"
                    + "gest.Europe.EuFormexPackagePopulationProducer+PopulationReplay::EnumerateAsync, method internal st"
                    + "atic Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationProducer::DecodeRows, method internal st"
                    + "atic Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult::Refused, method internal static "
                    + "Lex.V3.Ingest.Europe.EuFormexManifestationEnumerationResult::Success, method private static Lex.V3"
                    + ".Ingest.Europe.EuFormexManifestationEnumerationProducer::DeriveAsync, method public instance Lex.V"
                    + "3.Ingest.Europe.EuFormexManifestationEnumerationProducer::RunAsync, method public static Lex.V3.In"
                    + "gest.Europe.EuFormexManifestationEnumerationProducer::ReopenAsync, 1 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFormexPackageOutcome: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::.ctor, "
                    + "method public static Lex.V3.Ingest.Europe.EuFormexPackageOutcome::Acquired, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::NotAcquired, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::NotEligible, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::NotEnumeratedLanguageOutOfScope, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::PackageRejected, "
                    + "method public static Lex.V3.Ingest.Europe.EuFormexPackageOutcome::Refused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::RouteRefused, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcome::Unavailable, "
                    + "2 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFormexPackageOutcomePopulation: constructor private "
                    + "instance Lex.V3.Ingest.Europe.EuFormexPackageOutcomePopulation::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuFormexPackageOutcomePopulation::TryClose, "
                    + "1 compiler-generated",
                "Lex.V3.Ingest.Europe.EuFormexPackagePopulationResult: constructor private instance Lex.V3.Ingest.E"
                    + "urope.EuFormexPackagePopulationResult::.ctor, method internal instance Lex.V3.Ingest.Europe.EuForm"
                    + "exPackagePopulationResult::WithAcquisitions, method internal instance Lex.V3.Ingest.Europe.EuForme"
                    + "xPackagePopulationResult::WithCheckpoint, method private instance Lex.V3.Ingest.Europe.EuFormexPac"
                    + "kagePopulationProducer::RunCoreAsync, method public instance Lex.V3.Ingest.Europe.EuFormexPackageP"
                    + "opulationProducer::RunAsync, method public instance Lex.V3.Ingest.Europe.EuFormexPackagePopulation"
                    + "Producer::RunAsync, method public static Lex.V3.Ingest.Europe.EuFormexPackagePopulationProducer::R"
                    + "eopenAsync, method public static Lex.V3.Ingest.Europe.EuFormexPackagePopulationResult::Refused, me"
                    + "thod public static Lex.V3.Ingest.Europe.EuFormexPackagePopulationResult::Success",
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
                "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult: constructor private instance Lex.V3.Ingest.Eu"
                    + "rope.EuLanguageScopedExpressionProductionResult::.ctor, method internal instance Lex.V3.Ingest.Europe.EuLangua"
                    + "geScopedExpressionProducer+Pairing::RunExpressionFactsAndDeriveAsync, method internal instance Lex.V3.Ingest.E"
                    + "urope.EuLanguageScopedExpressionProducer::RunWithDeliveriesAsync, method internal static Lex.V3.Ingest.Europe."
                    + "EuLanguageScopedExpressionProducer::ReopenWithDeliveriesAsync, method internal static Lex.V3.Ingest.Europe.EuL"
                    + "anguageScopedExpressionProductionResult::Refused, method internal static Lex.V3.Ingest.Europe.EuLanguageScoped"
                    + "ExpressionProductionResult::Success, method private instance Lex.V3.Ingest.Europe.EuLanguageScopedExpressionPr"
                    + "oducer::DeriveAndRetainAsync, method private instance Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer:"
                    + ":TryOpenDeliveryAsync, method private static Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::RefuseBe"
                    + "foreTraffic, method public instance Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::RunAsync, method "
                    + "public static Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProducer::ReopenAsync",
                "Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult::.ctor, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteProducer::CaptureAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteProducer::RebindAsync, "
                    + "method internal instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult::WithReceipts, "
                    + "method public instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteProducer::RunAsync, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteProducer::ReopenAsync, "
                    + "method public static Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult::Delivered, "
                    + "method public static Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult::Refused",
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
                "Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail: constructor internal instance Lex.V3.Ingest.Eu"
                    + "rope.EuQueryExecutionRefusalDetail::.ctor, method internal instance Lex.V3.Ingest.Europe.EuQueryEx"
                    + "ecutionAdapter::RunDocumentAcquisitionAsync, method internal instance Lex.V3.Ingest.Europe.EuQuery"
                    + "ExecutionAdapter::RunDocumentAcquisitionWithCheckpointAsync, method internal static Lex.V3.Ingest."
                    + "Europe.EuQueryExecutionAdapter::ReopenDocumentAcquisitionAsync, method internal static Lex.V3.Inge"
                    + "st.Europe.EuQueryExecutionAdapter::TryPairExpressionAndObjectBatches, method private instance Lex."
                    + "V3.Ingest.Europe.EuQueryExecutionAdapter::RunDocumentAcquisitionCoreAsync",
                "Lex.V3.Ingest.Europe.EuQueryExecutionResult: constructor private instance Lex.V3.Ingest.Europe.EuQ"
                    + "ueryExecutionResult::.ctor, method internal instance Lex.V3.Ingest.Europe.EuQueryExecutionResult::"
                    + "WithAcquisitionCheckpoint, method internal instance Lex.V3.Ingest.Europe.EuQueryExecutionResult::W"
                    + "ithDocumentCheckpoint, method internal static Lex.V3.Ingest.Europe.EuQueryExecutionResult::Deliver"
                    + "edWithLocatedAmendments, method internal static Lex.V3.Ingest.Europe.EuQueryExecutionResult::Deliv"
                    + "eredWithLocatedAmendments, method private instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::R"
                    + "unCoreAsync, method private instance Lex.V3.Ingest.Europe.EuQueryExecutionResult::WithCheckpoints,"
                    + " method public instance Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::RunAsync, method public stat"
                    + "ic Lex.V3.Ingest.Europe.EuQueryExecutionAdapter::ReopenAsync, method public static Lex.V3.Ingest.E"
                    + "urope.EuQueryExecutionResult::Delivered, method public static Lex.V3.Ingest.Europe.EuQueryExecutio"
                    + "nResult::Refused",
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
                "Lex.V3.Ingest.Europe.EuWitnessTraversalResult: constructor private instance Lex.V3.Ingest.Europe.E"
                    + "uWitnessTraversalResult::.ctor, method internal instance Lex.V3.Ingest.Europe.EuWitnessTraversalRe"
                    + "sult::WithCheckpoint, method private instance Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor::"
                    + "RunWitnessTraversalCoreAsync, method public instance Lex.V3.Ingest.Europe.EuRepeatedEnumerationExe"
                    + "cutor::RunWitnessTraversalAsync, method public static Lex.V3.Ingest.Europe.EuRepeatedEnumerationEx"
                    + "ecutor::RestoreWitnessTraversalAsync, method public static Lex.V3.Ingest.Europe.EuWitnessTraversal"
                    + "Result::Delivered, method public static Lex.V3.Ingest.Europe.EuWitnessTraversalResult::Refused",
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
                "Lex.V3.Ingest.Europe.EuropeIndexReader: constructor private instance "
                    + "Lex.V3.Ingest.Europe.EuropeIndexReader::.ctor, "
                    + "method public static Lex.V3.Ingest.Europe.EuropeIndexReader::OpenAndVerify, "
                    + "method public static "
                    + "Lex.V3.Ingest.Europe.EuropeIndexReader::OpenAndVerifyFileAsync",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgAssertionSnapshot: constructor private "
                    + "instance Lex.V3.Ingest.Luxembourg.LuxembourgAssertionSnapshot::.ctor, "
                    + "constructor private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAssertionSnapshot::.cctor, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAssertionSnapshot::Open",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult: constructor private instance Lex.V3.Ingest.Luxembourg"
                    + ".LuxembourgEnumerationRunResult::.ctor, method internal static Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationR"
                    + "unResult::DeliveredWithCheckpoint, method private instance Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerat"
                    + "ionExecutor::RunCoverCoreAsync, method private instance Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumeration"
                    + "Executor::RunPartitionOnSessionAsync, method public instance Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumer"
                    + "ationExecutor::RunAdaptiveCoverAsync, method public instance Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumer"
                    + "ationExecutor::RunCoverAsync, method public instance Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExe"
                    + "cutor::RunPartitionAsync, method public static Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult::Delive"
                    + "red, method public static Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRunResult::Refused, 2 compiler-generat"
                    + "ed",
                "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome: constructor private instance Lex.V3.I"
                    + "ngest.Luxembourg.LuxembourgFamilyEnumerationOutcome::.ctor, method internal static Lex.V3.Ingest.L"
                    + "uxembourg.LuxembourgFamilyEnumerationOutcome::CoverProvenWithCheckpoint, method internal static Le"
                    + "x.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::ProvenWithCheckpoint, method private st"
                    + "atic Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::FindProvenOutcome, method public st"
                    + "atic Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::CoverProven, method public stati"
                    + "c Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::CoverRefused, method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::ExecutorRefused, method public static"
                    + " Lex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::ProofRefused, method public static L"
                    + "ex.V3.Ingest.Luxembourg.LuxembourgFamilyEnumerationOutcome::Proven",
                "Lex.V3.Ingest.Luxembourg.LuxembourgFirstMountAcquisitionResult: constructor private instance Lex.V"
                    + "3.Ingest.Luxembourg.LuxembourgFirstMountAcquisitionResult::.ctor, method internal instance Lex.V3."
                    + "Ingest.Luxembourg.LuxembourgFirstMountAcquisitionResult::WithVocabularyCheckpoint, method public i"
                    + "nstance Lex.V3.Ingest.Luxembourg.LuxembourgFirstMountAcquisition::RunAsync, method public static L"
                    + "ex.V3.Ingest.Luxembourg.LuxembourgFirstMountAcquisitionResult::Refused, method public static Lex.V"
                    + "3.Ingest.Luxembourg.LuxembourgFirstMountAcquisitionResult::Success",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgIndexPredecessor: constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgIndexPredecessor::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgIndexPredecessor::TryRead, "
                    + "method public static Lex.V3.Ingest.V3FirstMountBuild::ReadPredecessor",
                "Lex.V3.Ingest.Luxembourg.LuxembourgIndexReader: constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgIndexReader::.ctor, "
                    + "method private static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgIndexReader::OpenVerifiedFile, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgIndexReader::OpenAndVerify, "
                    + "method public static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgIndexReader::OpenAndVerifyFileAsync",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgObjectDigestSet: constructor private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObjectDigestSet::.ctor, method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObjectDigestSet::FromCanonicalArray, "
                    + "method internal static "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObjectDigestSet::FromObservations",
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
                "Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDetail: by-ref-method public instan"
                    + "ce Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter+CoverReconciliationOutcome::Deconstruc"
                    + "t, constructor private instance Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDet"
                    + "ail::.ctor, method public static Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCoverReconciliationDe"
                    + "tail::CheckpointNotRetained, method public static Lex.V3.Ingest.Luxembourg.LuxembourgPartitionCove"
                    + "rReconciliationDetail::LeafExecutorRefused, method public static Lex.V3.Ingest.Luxembourg.Luxembou"
                    + "rgPartitionCoverReconciliationDetail::LeafProofRefused, method public static Lex.V3.Ingest.Luxembo"
                    + "urg.LuxembourgPartitionCoverReconciliationDetail::ReconciliationRefused",
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
                    + "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::BuildResourceObser"
                    + "vationsAsync, "
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
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::RunAdaptiveScopedA"
                    + "sync, "
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
                    + "Lex.V3.Ingest.Stage3EvidenceEnvelope::TryCreateWithEuropeLegalNoticeRoute, "
                    + "method public static "
                    + "Lex.V3.Ingest.Stage3EvidenceEnvelope::TryCreateWithEuropeLegalNoticeRouteAnd"
                    + "FormexMainBody, "
                    + "method public static "
                    + "Lex.V3.Ingest.Stage3EvidenceEnvelope::TryCreateWithFormexMainBody",
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
                "Lex.V3.Ingest.V3FirstMountBuildResult: constructor private instance "
                    + "Lex.V3.Ingest.V3FirstMountBuildResult::.ctor, "
                    + "method public instance Lex.V3.Ingest.V3FirstMountBuild::RunAsync, "
                    + "method public instance Lex.V3.Ingest.V3FirstMountBuild::RunAsync, "
                    + "method public static Lex.V3.Ingest.V3FirstMountBuildResult::Refused, "
                    + "method public static Lex.V3.Ingest.V3FirstMountBuildResult::Success",
                "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet: by-ref-method public instance "
                    + "Lex.V3.Ingest.LexCorpus6BuildResult::Deconstruct, "
                    + "constructor private instance "
                    + "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet::.ctor, "
                    + "method public static "
                    + "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet::ParseAndVerify, "
                    + "method public static "
                    + "Lex.V3.Ingest.VerifiedLexCorpus6ManifestSet::ParseCanonicalAndVerify",
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
            };
        var actual = ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere).ToArray();
        // Expected entries stay literal; remote failures print the independently observed changes.
        CollectionAssert.AreEqual(expected, actual,
            "Removed or changed pins:\n" + string.Join("\n", expected.Except(actual, StringComparer.Ordinal)) +
            "\nActual added or changed entries:\n" + string.Join("\n", actual.Except(expected, StringComparer.Ordinal)));
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
