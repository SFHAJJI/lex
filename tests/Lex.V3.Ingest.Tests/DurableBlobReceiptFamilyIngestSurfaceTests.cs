using Lex.V3.Contracts.Custody;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Decision 80 fold-in three's Ingest half of the receipt-forgery fence. <see cref="DurableBlobRef"/>
/// and <see cref="CustodyPolicyEvidence"/> have no producer anywhere in <c>Lex.V3.Ingest</c> today:
/// nothing here calls their public constructors directly, and nothing holds one in a field or
/// property. <see cref="DurableBlobWriteReceipt"/> does have holders here -- the session's own
/// <c>HeldBodyReceipt</c>/<c>ResolvedHeldBody</c> records and <c>BuildHopWriteReceipts</c> -- but no
/// constructor: every one it carries came from <c>ICustodyStore.CreateAsync</c>, never from
/// <c>new DurableBlobWriteReceipt(...)</c> written in this assembly. A new constructor-kind producer
/// here, or any new producer at all of the other two types, is exactly the unreviewed hole Decision
/// 80's door check cannot see on its own; this pin turns it into a failing test instead.
/// </summary>
[TestClass]
public sealed class DurableBlobReceiptFamilyIngestSurfaceTests
{
    private const string Receipt = "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt";
    private const string Session = "Lex.V3.Ingest.RoutedHttpAcquisitionSession";

    /// <summary>
    /// D1-04 adds one legitimate new holder: <c>LuxembourgQueryExecutionResult</c> carries the
    /// custody store's own write receipt for the scope manifest it produces, through the exact same
    /// pattern <see cref="RoutedHttpAcquisitionSession"/>'s <c>HeldBodyReceipt</c>/<c>ResolvedHeldBody</c>
    /// already use: no constructor of its own, the receipt comes only from <c>ICustodyStore.CreateAsync</c>.
    /// </summary>
    private const string QueryExecutionResult = "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult";

    /// <summary>
    /// D1-05c-2 adds the identical second holder for the Union: <c>EuQueryExecutionResult</c> carries
    /// its own scope manifest's write receipt through the same no-constructor, <c>ICustodyStore.CreateAsync</c>-only
    /// pattern <see cref="QueryExecutionResult"/> already established for Luxembourg.
    /// </summary>
    private const string EuQueryExecutionResult = "Lex.V3.Ingest.Europe.EuQueryExecutionResult";

    /// <summary>
    /// E5's population row carries the custody store's receipt for any derived normalised-ELI
    /// join evidence it returns. The internal row constructor only accepts that already-real
    /// receipt; it does not construct a receipt or policy evidence in this assembly.
    /// </summary>
    private const string TranspositionPopulationRow = "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationRow";

    /// <summary>
    /// D1-06b adds the third holder: the corpus/6 record set writer's own acquisition door.
    /// <c>CorpusAcquisitionOutcome.Held</c> is the only production path onto its <c>Receipt</c>
    /// property, and that factory requires the caller to pass an already-real receipt -- there is
    /// still no constructor of <see cref="DurableBlobWriteReceipt"/> itself anywhere in this
    /// assembly. See <c>AcquisitionOutcomeHasNoPathToHeldWithoutARealReceipt</c>
    /// (<c>CorpusRecordSetWriterTests</c>) for that factory's own construction-surface pin.
    /// </summary>
    private const string CorpusAcquisitionOutcome = "Lex.V3.Ingest.CorpusAcquisitionOutcome";

    /// <summary>
    /// #418's third slice adds the fifth holder: the governed expression-production path retains its
    /// derivation and carries the store's receipt for it.
    /// </summary>
    private const string ExpressionProductionResult =
        "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult";

    [TestMethod]
    public void EveryHolderOfReceiptInIngestIsPinnedAndNoneIsAConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+HeldBodyReceipt::Deconstruct(out "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt&, out System.String&, "
                    + "out Lex.V3.Ingest.RoutedHttpAcquisitionSession+HeldCausalFacts&) -> "
                    + "System.Void",
                "by-ref-method public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+ResolvedHeldBody::Deconstruct(out"
                    + " Lex.V3.Contracts.Custody.DurableBlobWriteReceipt&, "
                    + "out System.ReadOnlyMemory<System.Byte>&, out System.String&) -> System.Void",
                "field private instance "
                    + "Lex.V3.Ingest.ChunkedDerivedArtifact::<ChunkReceipts>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Custody.DurableBlo"
                    + "bWriteReceipt>",
                "field private instance "
                    + "Lex.V3.Ingest.CorpusAcquisitionOutcome::<Receipt>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.CorpusRecordSetWriteResult::<RetainedSetReceipt>k__BackingFiel"
                    + "d -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::<FormexSourceReceipt>k__Backing"
                    + "Field -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::<PdfReceipt>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::<XhtmlSourceReceipt>k__BackingF"
                    + "ield -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification::<PdfReceipt>k__BackingF"
                    + "ield -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::<RetainedTripwir"
                    + "e>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::<RetainedTripwir"
                    + "eLineage>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::<HopWriteReceiptsByObserv"
                    + "ationId>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexTransportBinding::<RetainedZipReceipt>k__B"
                    + "ackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::<RetainedDe"
                    + "rivation>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::<RetainedEp"
                    + "isode>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult::<HopReceipts>k__BackingField "
                    + "-> System.Collections.Generic.IReadOnlyDictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::<CorpusRecordSetReceipt>k__Back"
                    + "ingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::<ScopeManifestReceipt>k__Backin"
                    + "gField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationRow::<NormalisedEliJoinE"
                    + "videnceReceipts>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Custody.DurableBlo"
                    + "bWriteReceipt>",
                "field private instance "
                    + "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventory::<SourceReceipt>k__BackingField "
                    + "-> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryOutcome::<TransportRec"
                    + "eipt>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentOutcome::<TransportReceipt"
                    + ">k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyAcquisition::<RetainedTranspor"
                    + "tBytes>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationInput::<Receipt>k__Back"
                    + "ingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriteResult::<Re"
                    + "tainedSetReceipt>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceOutcome::<LayoutEvidence"
                    + "Receipt>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceOutcome::<TransportRecei"
                    + "pt>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityOutcome::<TransportR"
                    + "eceipt>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerOutcome::<TextArtifa"
                    + "ctReceipt>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::<CorpusRecordSetRec"
                    + "eipt>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::<ObservedObjectIden"
                    + "titySetReceipt>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::<ScopeManifestRecei"
                    + "pt>k__BackingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "field private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::<HopWriteReceiptsB"
                    + "yObservationId>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?",
                "field private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+HeldBodyReceipt::<Receipt>k__Back"
                    + "ingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+ResolvedHeldBody::<Receipt>k__Bac"
                    + "kingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Stage3EuropeBodyComposition::<FormexCustody>k__BackingField "
                    + "-> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Stage3EuropeBodyComposition::<PdfCustody>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Stage3EuropeBodyComposition::<XhtmlCustody>k__BackingField -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Stage3EvidenceLineage::<EuropeScopeManifestReceipt>k__BackingF"
                    + "ield -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "field private instance "
                    + "Lex.V3.Ingest.Stage3EvidenceLineage::<LuxembourgScopeManifestReceipt>k__Back"
                    + "ingField -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "method internal instance "
                    + "Lex.V3.Ingest.LexCorpus6Builder+<>c::<CorrigendumReceipts>b__21_0(Lex.V3.Ing"
                    + "est.Europe.EuCorrigendumTripwireProductionResult) -> "
                    + "System.Collections.Generic.IEnumerable<Lex.V3.Contracts.Custody.DurableBlobW"
                    + "riteReceipt>",
                "method internal static "
                    + "Lex.V3.Ingest.ChunkedDerivedArtifact::WriteAsync(Lex.V3.Contracts.Custody.IC"
                    + "ustodyStore, System.String, System.Func<System.IO.Stream, System.String>, "
                    + "System.Threading.CancellationToken, "
                    + "System.Action<Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?) -> "
                    + "System.Threading.Tasks.Task<System.ValueTuple<Lex.V3.Contracts.Custody.Durab"
                    + "leBlobWriteReceipt, "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Custody.DurableBlo"
                    + "bWriteReceipt>>>",
                "method internal static "
                    + "Lex.V3.Ingest.CustodyHold::TryHoldAsync(Lex.V3.Contracts.Custody.ICustodySto"
                    + "re, System.ReadOnlyMemory<System.Byte>, "
                    + "System.Threading.CancellationToken) -> "
                    + "System.Threading.Tasks.Task<System.ValueTuple<Lex.V3.Contracts.Custody.Durab"
                    + "leBlobWriteReceipt, System.String>>",
                "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceProducer::HoldArtifactAs"
                    + "ync(System.ReadOnlyMemory<System.Byte>, "
                    + "System.Threading.CancellationToken) -> "
                    + "System.Threading.Tasks.Task<Lex.V3.Contracts.Custody.DurableBlobWriteReceipt"
                    + ">",
                "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::HoldManifestAsync("
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest, "
                    + "Lex.V3.Contracts.Source.Scope.IScopeReductionEvidenceResolver, "
                    + "System.Threading.CancellationToken) -> "
                    + "System.Threading.Tasks.Task<System.ValueTuple<Lex.V3.Contracts.Source.Scope."
                    + "ScopeManifest, Lex.V3.Contracts.Custody.DurableBlobWriteReceipt, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, System.String, "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail>>",
                "method private instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::TryReopenTerminalH"
                    + "opAsync(Lex.V3.Contracts.Source.Http.RoutedHttpEvidence, "
                    + "System.Threading.CancellationToken) -> "
                    + "System.Threading.Tasks.Task<System.ValueTuple<System.Nullable<System.ValueTu"
                    + "ple<Lex.V3.Contracts.Source.Http.HttpLogicalRequest, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>>, System.String>>",
                "method private instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession::BuildHopWriteReceipts(System.UIn"
                    + "t64, System.UInt64, "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.Routed"
                    + "HttpHop>) -> System.Collections.Generic.Dictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>",
                "property internal instance Lex.V3.Ingest.ChunkedDerivedArtifact::ChunkReceipts() "
                    + "-> "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Custody.DurableBlo"
                    + "bWriteReceipt>",
                "property internal instance "
                    + "Lex.V3.Ingest.Europe.EuLegalNoticeRouteResult::HopReceipts() -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?",
                "property internal instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+AttemptResult::HopWriteReceiptsBy"
                    + "ObservationId() -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?",
                "property public instance Lex.V3.Ingest.CorpusAcquisitionOutcome::Receipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.CorpusRecordSetWriteResult::RetainedSetReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::FormexSourceReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::PdfReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::XhtmlSourceReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification::PdfReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::RetainedTripwire"
                    + "() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::RetainedTripwire"
                    + "Lineage() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuDocumentFetchAttemptResult::HopWriteReceiptsByObserva"
                    + "tionId() -> System.Collections.Generic.IReadOnlyDictionary<System.String, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexInventory::SourceReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuFormexAnnexTransportBinding::RetainedZipReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::RetainedDer"
                    + "ivation() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuLanguageScopedExpressionProductionResult::RetainedEpi"
                    + "sode() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::CorpusRecordSetReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuQueryExecutionResult::ScopeManifestReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuTranspositionBridgePopulationRow::NormalisedEliJoinEv"
                    + "idenceReceipts() -> "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Custody.DurableBlo"
                    + "bWriteReceipt>",
                "property public instance "
                    + "Lex.V3.Ingest.Europe.EuXhtmlAnnexInventory::SourceReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryOutcome::TransportRece"
                    + "ipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentOutcome::TransportReceipt("
                    + ") -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyAcquisition::RetainedTransport"
                    + "Bytes() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationInput::Receipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgObservedObjectIdentitySetWriteResult::Ret"
                    + "ainedSetReceipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceOutcome::LayoutEvidenceR"
                    + "eceipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfLayoutEvidenceOutcome::TransportReceip"
                    + "t() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPdfProfileEligibilityOutcome::TransportRe"
                    + "ceipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfTextLayerOutcome::TextArtifac"
                    + "tReceipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::CorpusRecordSetRece"
                    + "ipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::ObservedObjectIdent"
                    + "itySetReceipt() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionResult::ScopeManifestReceip"
                    + "t() -> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt?",
                "property public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+HeldBodyReceipt::Receipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.RoutedHttpAcquisitionSession+ResolvedHeldBody::Receipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Stage3EuropeBodyComposition::FormexCustody() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance Lex.V3.Ingest.Stage3EuropeBodyComposition::PdfCustody() "
                    + "-> Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Stage3EuropeBodyComposition::XhtmlCustody() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Stage3EvidenceLineage::EuropeScopeManifestReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
                "property public instance "
                    + "Lex.V3.Ingest.Stage3EvidenceLineage::LuxembourgScopeManifestReceipt() -> "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt",
            },
            ConstructionSurface.ProducersIn(typeof(RoutedHttpAcquisitionSession).Assembly, typeof(DurableBlobWriteReceipt), true).ToArray());
    }

    [TestMethod]
    public void NoProducerOfRefOrPolicyEvidenceExistsInIngest()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(RoutedHttpAcquisitionSession).Assembly, typeof(DurableBlobRef), true).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(RoutedHttpAcquisitionSession).Assembly, typeof(CustodyPolicyEvidence), true).ToArray());
    }
}
