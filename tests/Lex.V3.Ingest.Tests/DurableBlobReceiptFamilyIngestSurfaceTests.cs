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
                "field private instance " + Session + "+HeldBodyReceipt::<Receipt>k__BackingField -> " + Receipt,
                "field private instance " + Session + "+ResolvedHeldBody::<Receipt>k__BackingField -> " + Receipt,
                "method private instance " + Session + "::BuildHopWriteReceipts(System.UInt64, System.UInt64, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.RoutedHttpHop>) "
                + "-> System.Collections.Generic.Dictionary<System.String, " + Receipt + ">",
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
