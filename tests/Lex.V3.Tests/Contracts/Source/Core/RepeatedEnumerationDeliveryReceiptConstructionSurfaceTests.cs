using Lex.V3.Contracts.Source.Core;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Core;

/// <summary>
/// The construction surface of queue item 19's own moved shapes: the receipt, the observation
/// identity, the observed transport and the observation custody, moved here from
/// <c>Lex.V3.Contracts.Source.Luxembourg</c> (renamed from <c>LuxembourgEnumerationDeliveryReceipt</c>,
/// <c>LuxembourgObservationIdentity</c>, <c>LuxembourgObservedTransport</c> and
/// <c>LuxembourgObservationCustody</c> respectively) because none of them ever named Luxembourg or
/// any other publisher in their own fields or logic. These four assertions moved with the types,
/// under their new names, with what each one actually proves unchanged: only the transcribed
/// namespace and class-name literals differ from the retired
/// <c>LuxembourgConstructionSurfaceTests</c>'s own pins.
/// </summary>
[TestClass]
public sealed class RepeatedEnumerationDeliveryReceiptConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Core.";
    private const string Lu = "Lex.V3.Contracts.Source.Luxembourg.";
    private const string Custody = "Lex.V3.Contracts.Custody.";
    private const string List = "System.Collections.Generic.IReadOnlyList<";
    private const string Membership = "System.Collections.Generic.IReadOnlyDictionary<System.String, "
        + Custody + "CustodyMembership>";

    /// <summary>
    /// The receipt: one private constructor, one checked factory. The factory takes the custody
    /// maps and the per-observation custody as parameters, so a receipt cannot exist without a
    /// statement about every member the comparison names.
    /// </summary>
    [TestMethod]
    public void ARepeatedEnumerationDeliveryReceiptHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepeatedEnumerationDeliveryReceipt::.ctor("
                + N + "EnumerationDeliveryComparison, " + Membership + ", "
                + Custody + "CustodyMembership, " + List + "System.String>) -> "
                + N + "RepeatedEnumerationDeliveryReceipt",
                "method public static " + N + "RepeatedEnumerationDeliveryReceipt::TryCreate("
                + N + "EnumerationDeliveryComparison, " + Membership + ", " + Membership + ", "
                + List + N + "RepeatedEnumerationObservationCustody>, out "
                + N + "RepeatedEnumerationReceiptRefusal&) -> "
                + N + "RepeatedEnumerationDeliveryReceipt",
            },
            ConstructionSurface.Of(typeof(RepeatedEnumerationDeliveryReceipt)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // The cover holds leaf receipts; it produces none.
                "field private instance " + Lu + "LuxembourgPartitionCover::_leafReceipts -> "
                + List + N + "RepeatedEnumerationDeliveryReceipt>",

                // The one production path, and the reason TryCreate above being public is not the
                // whole story: this is the only caller that fills the custody parameters in from
                // real write receipts rather than from a caller's assertion.
                "method public instance " + Lu + "LuxembourgDeliveryEvidenceSet::TryCompareAndReceipt("
                + Membership + ", " + Membership + ", out "
                + N + "RepeatedEnumerationReceiptRefusal&) -> "
                + N + "RepeatedEnumerationDeliveryReceipt",
                "property public instance " + Lu + "LuxembourgPartitionCover::LeafReceipts() -> "
                + List + N + "RepeatedEnumerationDeliveryReceipt>",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepeatedEnumerationDeliveryReceipt).Assembly,
                typeof(RepeatedEnumerationDeliveryReceipt),
                true).ToArray(),
            "a delivery receipt reached a new holder in Contracts");
    }

    /// <summary>
    /// The observation identity: minted whole or not at all, so no caller reuses or reorders the
    /// four resource ids that <see cref="EnumerationDeliveryComparison"/>'s own
    /// <c>RequireDistinct</c> defends against.
    /// </summary>
    [TestMethod]
    public void ARepeatedEnumerationObservationIdentityIsMintedWholeOrNotAtAll()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepeatedEnumerationObservationIdentity::.ctor("
                + "System.String, System.String, System.String, System.String) -> "
                + N + "RepeatedEnumerationObservationIdentity",
                "method public static " + N + "RepeatedEnumerationObservationIdentity::NewObservation() -> "
                + N + "RepeatedEnumerationObservationIdentity",
            },
            ConstructionSurface.Of(typeof(RepeatedEnumerationObservationIdentity)).ToArray());
    }

    /// <summary>
    /// Pinned and explicitly OPEN, and the reason is the point of the type.
    /// </summary>
    /// <remarks>
    /// <see cref="RepeatedEnumerationObservedTransport"/> is the four transport facts a caller hands
    /// <c>LuxembourgDeliveryObservation</c> (or a future publisher's equivalent) to be checked.
    /// Closing it would be closing the wrong door: the whole design is that an observation
    /// VALIDATES a caller-supplied transport against the hop it claims to describe, so a transport
    /// nobody can assemble wrongly would make those checks untestable, and the tests that assemble
    /// a deliberately wrong one are the only drivers those checks have. Holding one of these is
    /// evidence of nothing, which is why nothing in this repository treats it as evidence.
    /// </remarks>
    [TestMethod]
    public void ARepeatedEnumerationObservedTransportIsAnOpenInputRecordByDesign()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepeatedEnumerationObservedTransport::.ctor("
                + N + "RepeatedEnumerationObservedTransport) -> " + N + "RepeatedEnumerationObservedTransport",
                "constructor public instance " + N + "RepeatedEnumerationObservedTransport::.ctor("
                + "Lex.V3.Contracts.Source.Http.HttpLogicalRequest, "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpEvidence, "
                + Custody + "DurableBlobWriteReceipt, System.ReadOnlyMemory<System.Byte>) -> "
                + N + "RepeatedEnumerationObservedTransport",
                "method public instance " + N + "RepeatedEnumerationObservedTransport::<Clone>$() -> "
                + N + "RepeatedEnumerationObservedTransport",
            },
            ConstructionSurface.Of(typeof(RepeatedEnumerationObservedTransport)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(RepeatedEnumerationObservedTransport).Assembly,
                typeof(RepeatedEnumerationObservedTransport),
                true).ToArray(),
            "something in Contracts now hands out a transport rather than checking one");
    }

    /// <summary>
    /// Pinned and explicitly OPEN, which is a different claim from the one above and is stated
    /// here rather than left for a reader to infer.
    /// </summary>
    /// <remarks>
    /// <see cref="RepeatedEnumerationObservationCustody"/> is a record with a public constructor.
    /// Anyone can build one and claim <see cref="Lex.V3.Contracts.Custody.CustodyMembership.Floored"/>
    /// for any digest. That is deliberate and it is not a hole, because it is exactly as open as the
    /// two membership dictionaries beside it in the same parameter list: the receipt is a pure
    /// function over stated custody, and what makes the statement true is its one production caller,
    /// <c>LuxembourgDeliveryEvidenceSet.TryCompareAndReceipt</c>, which fills it in from write
    /// receipts already bound by content to the bodies they describe. Closing this type would not
    /// close the maps, so it would buy nothing and would cost the ability to test the receipt's
    /// membership rules without a live acquisition session. What this pin holds is narrower and
    /// real: that the only PRODUCER of one inside Contracts stays the observation that owns the
    /// bytes, so no second place starts deciding what a body's membership is.
    /// </remarks>
    [TestMethod]
    public void RepeatedEnumerationObservationCustodyIsAnOpenWireShapeWithOneProducerInContracts()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepeatedEnumerationObservationCustody::.ctor("
                + N + "RepeatedEnumerationObservationCustody) -> " + N + "RepeatedEnumerationObservationCustody",
                "constructor public instance " + N + "RepeatedEnumerationObservationCustody::.ctor("
                + N + "RepeatedEnumerationEvidenceRefs, System.String, "
                + Custody + "CustodyMembership, System.String) -> " + N + "RepeatedEnumerationObservationCustody",
                "method public instance " + N + "RepeatedEnumerationObservationCustody::<Clone>$() -> "
                + N + "RepeatedEnumerationObservationCustody",
            },
            ConstructionSurface.Of(typeof(RepeatedEnumerationObservationCustody)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // The closure the evidence set's own Select compiles to, and the observation
                // property it calls. Both are the one production path; neither is a second place
                // that decides a membership.
                "method internal instance " + Lu + "LuxembourgDeliveryEvidenceSet+<>c"
                + "::<TryCompareAndReceipt>b__15_0(" + Lu + "LuxembourgDeliveryObservation) -> "
                + N + "RepeatedEnumerationObservationCustody",
                "property public instance " + Lu + "LuxembourgDeliveryObservation::Custody() -> "
                + N + "RepeatedEnumerationObservationCustody",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepeatedEnumerationObservationCustody).Assembly,
                typeof(RepeatedEnumerationObservationCustody),
                true).ToArray(),
            "something other than the observation that holds the bytes now decides their membership");
    }
}
