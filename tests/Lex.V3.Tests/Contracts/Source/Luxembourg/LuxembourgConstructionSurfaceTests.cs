using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The construction surface of the Luxembourg repeated-enumeration proof types.
///
/// <para>
/// Every type pinned here exists because something had to be true before an instance of it could
/// be held: an observation had to be a real terminal 200 whose logical request, payload and write
/// receipt all bind the hop it is bundled with; a pass had to begin with its own count; an
/// evidence set had to reopen every artifact out of custody by digest; a receipt had to state the
/// custody of every member the comparison names; a cover had to reconcile leaves that share one
/// run and one profile. A second door onto any of them is a way to hold an object none of that was
/// checked for, and the verdict that preceded this file was blunt about the cost of claiming these
/// were pinned when nothing pinned them.
/// </para>
/// <para>
/// The entries are transcribed from what <see cref="ConstructionSurface"/> actually reflected, not
/// hand-derived: each was printed from a throwaway failing assertion and pasted back. Guessing the
/// exact type-qualified form is easy to get wrong in a way that still compiles.
/// </para>
/// <para>
/// <see cref="ConstructionSurface"/> reads signatures only. It cannot see a method declared as
/// returning <c>object</c>, nor <c>Activator</c> against a private constructor. What it does hold
/// is that a new named producer is a line in a diff.
/// </para>
/// </summary>
[TestClass]
public sealed class LuxembourgConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Luxembourg.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";
    private const string Custody = "Lex.V3.Contracts.Custody.";
    private const string List = "System.Collections.Generic.IReadOnlyList<";
    private const string Membership = "System.Collections.Generic.IReadOnlyDictionary<System.String, "
        + Custody + "CustodyMembership>";

    /// <summary>
    /// The cover: one private constructor and one reconciling factory, and nothing else in
    /// Contracts produces one.
    /// </summary>
    [TestMethod]
    public void APartitionCoverHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgPartitionCover::.ctor("
                + N + "LuxembourgPartitionChain, " + List + Core + "RepeatedEnumerationDeliveryReceipt>, "
                + N + "LuxembourgPartitionCoverBasis, " + Core + "SourceArtifactRef, "
                + Core + "SourceArtifactRef, System.Int64, " + Custody + "CustodyMembership) -> "
                + N + "LuxembourgPartitionCover",
                "method public static " + N + "LuxembourgPartitionCover::TryCreate("
                + N + "LuxembourgPartitionChain, " + List + Core + "RepeatedEnumerationDeliveryReceipt>, "
                + Core + "RepeatedEnumerationDeliveryReceipt?, out "
                + N + "LuxembourgPartitionCoverRefusal&) -> " + N + "LuxembourgPartitionCover?",
            },
            ConstructionSurface.Of(typeof(LuxembourgPartitionCover)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgPartitionCover).Assembly, typeof(LuxembourgPartitionCover), true).ToArray(),
            "something in Contracts now hands out a cover it did not have to reconcile");
    }

    /// <summary>
    /// The chain: a root, and splits of it. There is deliberately no constructor taking a list of
    /// ranges, because a list is where a gap check would be needed and a gap is what this type
    /// exists to make unrepresentable.
    /// </summary>
    [TestMethod]
    public void APartitionChainGrowsOnlyBySplittingALeaf()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgPartitionChain::.ctor("
                + N + "LuxembourgQueryPartitionRange, " + List + N + "LuxembourgQueryPartitionRange>) -> "
                + N + "LuxembourgPartitionChain",
                "method public instance " + N + "LuxembourgPartitionChain::SplitLeaf("
                + "System.String, " + N + "LuxembourgQueryCursor, System.String, System.String) -> "
                + N + "LuxembourgPartitionChain",
                "method public static " + N + "LuxembourgPartitionChain::Root("
                + N + "LuxembourgQueryPartitionRange) -> " + N + "LuxembourgPartitionChain",
            },
            ConstructionSurface.Of(typeof(LuxembourgPartitionChain)).ToArray());
    }

    /// <summary>
    /// The evidence set: one asynchronous materializer, which is where every artifact is reopened
    /// out of custody. A synchronous door would be a set that never had to reopen anything.
    /// </summary>
    [TestMethod]
    public void AnEvidenceSetIsReachableOnlyThroughMaterialization()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgDeliveryEvidenceSet::.ctor("
                + Core + "RepeatedEnumerationInterpretationProfile, " + Core + "SourceArtifactRef, "
                + N + "LuxembourgDeliveryPass, " + N + "LuxembourgDeliveryPass, "
                + "System.Collections.Generic.IReadOnlyDictionary<System.String, "
                + Core + "RepeatedEnumerationResolvedEvidence>) -> " + N + "LuxembourgDeliveryEvidenceSet",
                "method public static " + N + "LuxembourgDeliveryEvidenceSet::MaterializeAsync("
                + Core + "RepeatedEnumerationInterpretationProfile, " + Core + "SourceArtifactRef, "
                + N + "LuxembourgQueryPlan, System.String, System.String, "
                + Core + "MachineQueryRendererSource, " + N + "LuxembourgDeliveryPass, "
                + N + "LuxembourgDeliveryPass, " + Custody + "ICustodyStore, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<"
                + N + "LuxembourgDeliveryEvidenceSet>",
            },
            ConstructionSurface.Of(typeof(LuxembourgDeliveryEvidenceSet)).ToArray());
    }

    /// <summary>
    /// The observation: two public doors, count and page, both of which take a bound request and a
    /// transport and refuse unless the transport binds the hop it is bundled with. The private
    /// <c>Create</c> they share is listed rather than hidden, and so is the static constructor.
    /// </summary>
    [TestMethod]
    public void AnObservationExistsOnlyForARealAdmittedTransport()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgDeliveryObservation::.ctor("
                + Core + "RepeatedEnumerationEvidenceRefs, " + Core + "SourceArtifactRef, "
                + Core + "SourceArtifactRef, System.UInt64, " + List + "System.String>, "
                + "System.String, " + Custody + "CustodyMembership, System.String) -> "
                + N + "LuxembourgDeliveryObservation",
                "constructor private static " + N + "LuxembourgDeliveryObservation::.cctor() -> "
                + N + "LuxembourgDeliveryObservation",
                "method private static " + N + "LuxembourgDeliveryObservation::Create("
                + Core + "SourceArtifactRef, " + Core + "SourceArtifactRef, "
                + Core + "BoundMachineRequest, " + Core + "RepeatedEnumerationObservationIdentity, "
                + Core + "RepeatedEnumerationObservedTransport, "
                + Core + "RepeatedEnumerationInterpretationProfile) -> "
                + N + "LuxembourgDeliveryObservation",
                "method public static " + N + "LuxembourgDeliveryObservation::ForCount("
                + N + "LuxembourgBoundQueryCount, " + Core + "RepeatedEnumerationObservationIdentity, "
                + Core + "RepeatedEnumerationObservedTransport, "
                + Core + "RepeatedEnumerationInterpretationProfile) -> "
                + N + "LuxembourgDeliveryObservation",
                "method public static " + N + "LuxembourgDeliveryObservation::ForPage("
                + N + "LuxembourgBoundQueryPage, " + Core + "RepeatedEnumerationObservationIdentity, "
                + Core + "RepeatedEnumerationObservedTransport, "
                + Core + "RepeatedEnumerationInterpretationProfile) -> "
                + N + "LuxembourgDeliveryObservation",
            },
            ConstructionSurface.Of(typeof(LuxembourgDeliveryObservation)).ToArray());
    }

    /// <summary>
    /// The pass: begins with a count, grows by pages. There is no door that takes a page list, so
    /// a page cannot precede its count and page ordinals cannot be supplied by a caller.
    /// </summary>
    [TestMethod]
    public void APassBeginsWithItsCountAndGrowsOnlyByOnePage()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgDeliveryPass::.ctor("
                + N + "LuxembourgDeliveryObservation, System.Int64, "
                + List + N + "LuxembourgDeliveryObservation>) -> " + N + "LuxembourgDeliveryPass",
                "method public instance " + N + "LuxembourgDeliveryPass::WithPage("
                + N + "LuxembourgDeliveryObservation) -> " + N + "LuxembourgDeliveryPass",
                "method public static " + N + "LuxembourgDeliveryPass::BeginWithCount("
                + N + "LuxembourgDeliveryObservation, System.Int64) -> " + N + "LuxembourgDeliveryPass",
            },
            ConstructionSurface.Of(typeof(LuxembourgDeliveryPass)).ToArray());
    }

}
