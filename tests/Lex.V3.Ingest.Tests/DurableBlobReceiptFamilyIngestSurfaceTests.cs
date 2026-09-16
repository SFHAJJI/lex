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
                "by-ref-method public instance " + Session + "+HeldBodyReceipt::Deconstruct(out " + Receipt
                + "&, out System.String&, out " + Session + "+HeldCausalFacts&) -> System.Void",
                "by-ref-method public instance " + Session + "+ResolvedHeldBody::Deconstruct(out " + Receipt
                + "&, out System.ReadOnlyMemory<System.Byte>&, out System.String&) -> System.Void",
                "field private instance " + CorpusAcquisitionOutcome + "::<Receipt>k__BackingField -> " + Receipt + "?",
                // The corpus/6 set writer now carries the custody store's own receipt for the
                // set it wrote, the address by which that set can be reopened after the run.
                // It HOLDS that receipt and never constructs one: the only path onto the
                // property is the Written factory, fed by CustodyHold.TryHoldAsync.
                "field private instance Lex.V3.Ingest.CorpusRecordSetWriteResult::"
                    + "<RetainedSetReceipt>k__BackingField -> " + Receipt + "?",
                "field private instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::<FormexSourceReceipt>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::<PdfReceipt>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::<XhtmlSourceReceipt>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification::<PdfReceipt>k__BackingField -> " + Receipt,
                // #418's third slice adds the fifth holder: the language-scoped expression
                // production result carries the custody receipt for the derivation it retained.
                // It HOLDS that receipt and never constructs one -- the only path onto the property
                // is the internal Success factory, which requires an already-real receipt that came
                // from CustodyHold.TryHoldAsync and therefore from ICustodyStore.CreateAsync.
                // #418 slice 5: EuCorrigendumTripwireProductionResult carries the receipts the tripwire
                // producer held for the set's canonical bytes and its lineage bytes, beside the inner
                // expression result. It HOLDS them and never constructs one: the only path onto either
                // property is the internal Success factory, fed by CustodyHold.TryHoldAsync.
                "field private instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::<RetainedTripwire>k__BackingField -> " + Receipt + "?",
                "field private instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::<RetainedTripwireLineage>k__BackingField -> " + Receipt + "?",
                // S3-A02's Formex transport binding retains the exact package receipt beside its
                // request and response evidence. Its public constructor validates that already-real
                // receipt against the terminal response and constructs no custody value.
                "field private instance Lex.V3.Ingest.Europe.EuFormexAnnexTransportBinding::<RetainedZipReceipt>k__BackingField -> " + Receipt,
                "field private instance " + ExpressionProductionResult + "::<RetainedDerivation>k__BackingField -> "
                + Receipt + "?",
                // The episode record's own receipt, added when review required the derivation to be
                // byte-stable: the run-specific provenance is retained BESIDE the derivation rather
                // than inside it, so the production holds two receipts and not one.
                "field private instance " + ExpressionProductionResult + "::<RetainedEpisode>k__BackingField -> "
                + Receipt + "?",
                "field private instance " + EuQueryExecutionResult + "::<CorpusRecordSetReceipt>k__BackingField -> "
                    + Receipt + "?",
                "field private instance " + EuQueryExecutionResult + "::<ScopeManifestReceipt>k__BackingField -> " + Receipt + "?",
                "field private instance " + TranspositionPopulationRow + "::<NormalisedEliJoinEvidenceReceipts>k__BackingField -> "
                + "System.Collections.Generic.IReadOnlyList<" + Receipt + ">",
                "field private instance Lex.V3.Ingest.Europe.EuXhtmlAnnexInventory::<SourceReceipt>k__BackingField -> " + Receipt,
                // S3-A01/A03/A04's article inventory outcome carries the exact receipt already
                // bound by its held derivation input. It HOLDS transport provenance beside the
                // semantic inventory and constructs no custody value.
                "field private instance Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryOutcome::<TransportReceipt>k__BackingField -> " + Receipt,
                // The legal-content profile preserves that same inventory-bound receipt as
                // run provenance beside its semantic token stream. It reopens the receipt through
                // the custody store and never constructs a custody value.
                "field private instance Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentOutcome::<TransportReceipt>k__BackingField -> " + Receipt,
                // #419 slice 6b: LuxembourgGazetteBodyAcquisition carries the receipt the document-fetch
                // loop held for one Gazette-PDF body, beside its address, requests and route evidence, for
                // the producer to verify. It HOLDS that receipt and never constructs one.
                "field private instance Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyAcquisition::<RetainedTransportBytes>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationInput::<Receipt>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Luxembourg."
                    + "LuxembourgObservedObjectIdentitySetWriteResult::"
                    + "<RetainedSetReceipt>k__BackingField -> " + Receipt + "?",
                // S3-A01/A02/A04's PDF-family eligibility outcome carries the exact held-body
                // receipt as run provenance. The producer receives it through the proof-bound
                // composition; it does not construct custody evidence.
                "field private instance Lex.V3.Ingest.Luxembourg."
                    + "LuxembourgPdfProfileEligibilityOutcome::<TransportReceipt>k__BackingField -> "
                    + Receipt,
                "field private instance " + QueryExecutionResult + "::<CorpusRecordSetReceipt>k__BackingField -> "
                    + Receipt + "?",
                // #344 S3-A04: the address of the run's own observed object-identity set, the
                // premise its scope reduction rests on. It HOLDS that receipt and never
                // constructs one: the only path onto the property is the Delivered factory,
                // fed by CustodyHold.TryHoldAsync through the identity-set writer.
                "field private instance " + QueryExecutionResult
                    + "::<ObservedObjectIdentitySetReceipt>k__BackingField -> " + Receipt + "?",
                "field private instance " + QueryExecutionResult + "::<ScopeManifestReceipt>k__BackingField -> " + Receipt + "?",
                "field private instance " + Session + "+HeldBodyReceipt::<Receipt>k__BackingField -> " + Receipt,
                "field private instance " + Session + "+ResolvedHeldBody::<Receipt>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Stage3EvidenceLineage::<EuropeScopeManifestReceipt>k__BackingField -> " + Receipt,
                "field private instance Lex.V3.Ingest.Stage3EvidenceLineage::<LuxembourgScopeManifestReceipt>k__BackingField -> " + Receipt,
                // The one place that decides what "held" means, for both publishers' acquisition
                // paths. It HOLDS a receipt and never constructs one: the receipt comes only from
                // ICustodyStore.CreateAsync, exactly as every other holder pinned here. Carried into
                // this lane from the LU lane so one definition of held exists rather than two.
                "method internal static Lex.V3.Ingest.CustodyHold::TryHoldAsync("
                + "Lex.V3.Contracts.Custody.ICustodyStore, System.ReadOnlyMemory<System.Byte>, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<"
                + "System.ValueTuple<" + Receipt + ", System.String>>",
                // Both scope writes use the same private hold/reopen helper. It returns the
                // custody store's receipt; it constructs no receipt or policy evidence.
                "method private instance Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::HoldManifestAsync("
                + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest, Lex.V3.Contracts.Source.Scope.IScopeReductionEvidenceResolver, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<System.ValueTuple<"
                + "Lex.V3.Contracts.Source.Scope.ScopeManifest, " + Receipt + ", Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.String, Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail>>",
                // #419 slice 6c: the Gazette loop reopens, by the digests the terminal hop itself names,
                // the request that fetched a Gazette body and the receipt the session retained for
                // it, and hands that exact receipt to the accepted producer. It REOPENS a receipt the
                // store already issued, parsed back from its canonical bytes, and constructs none;
                // a second hold of the same bytes would be a different receipt, which the producer
                // refuses.
                "method private instance Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter::TryReopenTerminalHopAsync("
                + "Lex.V3.Contracts.Source.Http.RoutedHttpEvidence, System.Threading.CancellationToken) -> "
                + "System.Threading.Tasks.Task<System.ValueTuple<System.Nullable<System.ValueTuple<"
                + "Lex.V3.Contracts.Source.Http.HttpLogicalRequest, " + Receipt + ">>, System.String>>",
                "method private instance " + Session + "::BuildHopWriteReceipts(System.UInt64, System.UInt64, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.RoutedHttpHop>) "
                + "-> System.Collections.Generic.Dictionary<System.String, " + Receipt + ">",
                "property public instance " + CorpusAcquisitionOutcome + "::Receipt() -> " + Receipt + "?",
                "property public instance Lex.V3.Ingest.CorpusRecordSetWriteResult::"
                    + "RetainedSetReceipt() -> " + Receipt + "?",
                "property public instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::FormexSourceReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::PdfReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Europe.EuAnnexEvidenceBinding::XhtmlSourceReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Europe.EuBoundAnnexBodyClassification::PdfReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::RetainedTripwire() -> " + Receipt + "?",
                "property public instance Lex.V3.Ingest.Europe.EuCorrigendumTripwireProductionResult::RetainedTripwireLineage() -> " + Receipt + "?",
                "property public instance Lex.V3.Ingest.Europe.EuFormexAnnexInventory::SourceReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Europe.EuFormexAnnexTransportBinding::RetainedZipReceipt() -> " + Receipt,
                "property public instance " + ExpressionProductionResult + "::RetainedDerivation() -> " + Receipt + "?",
                "property public instance " + ExpressionProductionResult + "::RetainedEpisode() -> " + Receipt + "?",
                "property public instance " + EuQueryExecutionResult + "::CorpusRecordSetReceipt() -> "
                    + Receipt + "?",
                "property public instance " + EuQueryExecutionResult + "::ScopeManifestReceipt() -> " + Receipt + "?",
                "property public instance " + TranspositionPopulationRow + "::NormalisedEliJoinEvidenceReceipts() -> "
                + "System.Collections.Generic.IReadOnlyList<" + Receipt + ">",
                "property public instance Lex.V3.Ingest.Europe.EuXhtmlAnnexInventory::SourceReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Luxembourg.LuxembourgAknArticleInventoryOutcome::TransportReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Luxembourg.LuxembourgAknLegalContentOutcome::TransportReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Luxembourg.LuxembourgGazetteBodyAcquisition::RetainedTransportBytes() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Luxembourg.LuxembourgHeldBodyDerivationInput::Receipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Luxembourg."
                    + "LuxembourgObservedObjectIdentitySetWriteResult::RetainedSetReceipt() -> "
                    + Receipt + "?",
                "property public instance Lex.V3.Ingest.Luxembourg."
                    + "LuxembourgPdfProfileEligibilityOutcome::TransportReceipt() -> " + Receipt,
                "property public instance " + QueryExecutionResult + "::CorpusRecordSetReceipt() -> "
                    + Receipt + "?",
                "property public instance " + QueryExecutionResult
                    + "::ObservedObjectIdentitySetReceipt() -> " + Receipt + "?",
                "property public instance " + QueryExecutionResult + "::ScopeManifestReceipt() -> " + Receipt + "?",
                "property public instance " + Session + "+HeldBodyReceipt::Receipt() -> " + Receipt,
                "property public instance " + Session + "+ResolvedHeldBody::Receipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Stage3EvidenceLineage::EuropeScopeManifestReceipt() -> " + Receipt,
                "property public instance Lex.V3.Ingest.Stage3EvidenceLineage::LuxembourgScopeManifestReceipt() -> " + Receipt,
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
