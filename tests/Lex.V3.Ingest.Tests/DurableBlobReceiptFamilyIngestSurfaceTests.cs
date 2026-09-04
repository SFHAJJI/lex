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
    /// D1-06b adds the third holder: the corpus/6 record set writer's own acquisition door.
    /// <c>CorpusAcquisitionOutcome.Held</c> is the only production path onto its <c>Receipt</c>
    /// property, and that factory requires the caller to pass an already-real receipt -- there is
    /// still no constructor of <see cref="DurableBlobWriteReceipt"/> itself anywhere in this
    /// assembly. See <c>CorpusAcquisitionOutcomeHasNoPathToHeldWithoutARealReceipt</c>
    /// (<c>CorpusRecordSetWriterTests</c>) for that factory's own construction-surface pin.
    /// </summary>
    private const string CorpusAcquisitionOutcome = "Lex.V3.Ingest.CorpusAcquisitionOutcome";

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
                "field private instance " + CorpusAcquisitionOutcome + "::<Receipt>k__BackingField -> " + Receipt,
                "field private instance " + EuQueryExecutionResult + "::<ScopeManifestReceipt>k__BackingField -> " + Receipt,
                "field private instance " + QueryExecutionResult + "::<ScopeManifestReceipt>k__BackingField -> " + Receipt,
                "field private instance " + Session + "+HeldBodyReceipt::<Receipt>k__BackingField -> " + Receipt,
                "field private instance " + Session + "+ResolvedHeldBody::<Receipt>k__BackingField -> " + Receipt,
                "method private instance " + Session + "::BuildHopWriteReceipts(System.UInt64, System.UInt64, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.RoutedHttpHop>) "
                + "-> System.Collections.Generic.Dictionary<System.String, " + Receipt + ">",
                "property public instance " + CorpusAcquisitionOutcome + "::Receipt() -> " + Receipt,
                "property public instance " + EuQueryExecutionResult + "::ScopeManifestReceipt() -> " + Receipt,
                "property public instance " + QueryExecutionResult + "::ScopeManifestReceipt() -> " + Receipt,
                "property public instance " + Session + "+HeldBodyReceipt::Receipt() -> " + Receipt,
                "property public instance " + Session + "+ResolvedHeldBody::Receipt() -> " + Receipt,
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
