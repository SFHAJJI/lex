using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The construction surface of the Luxembourg executor's own result types.
///
/// <para>
/// These are the types a caller reads to decide whether an enumeration happened. A second door
/// onto any of them is a way to hand a caller a refusal nothing refused, or a request count
/// nothing counted. The contracts-side proof types are pinned beside them in
/// <c>LuxembourgConstructionSurfaceTests</c>.
/// </para>
/// <para>
/// Transcribed from what <see cref="ConstructionSurface"/> actually reflected, printed from a
/// throwaway failing assertion, rather than hand-derived.
/// </para>
/// </summary>
[TestClass]
public sealed class LuxembourgExecutorConstructionSurfaceTests
{
    private const string N = "Lex.V3.Ingest.Luxembourg.";
    private const string Contracts = "Lex.V3.Contracts.Source.Luxembourg.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";

    /// <summary>
    /// The refusal detail: one internal constructor, which refuses the "none" code. Internal, so
    /// the executor and this assembly's tests can reach it and nothing outside Lex.V3.Ingest can
    /// mint a refusal that did not happen.
    /// </summary>
    [TestMethod]
    public void ARefusalDetailHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance " + N + "LuxembourgEnumerationRefusalDetail::.ctor("
                + N + "LuxembourgEnumerationRefusal, System.Nullable<System.UInt64>, "
                + "System.Nullable<System.UInt64>, System.Nullable<System.Int32>, System.String, "
                + "System.String, System.Nullable<System.Int64>, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, System.String) -> "
                + N + "LuxembourgEnumerationRefusalDetail",
            },
            ConstructionSurface.Of(typeof(LuxembourgEnumerationRefusalDetail)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // Every one of these is somewhere a refusal detail is CARRIED, not made: the two
                // private per-step outcome records inside the executor and their compiler-generated
                // deconstructors, and the run result's own accessor.
                "by-ref-method public instance " + N + "LuxembourgRepeatedEnumerationExecutor+ObserveOutcome"
                + "::Deconstruct(out " + Contracts + "LuxembourgObservedTransport&, "
                + "out System.Nullable<System.UInt64>&, out "
                + N + "LuxembourgEnumerationRefusalDetail&) -> System.Void",
                "by-ref-method public instance " + N + "LuxembourgRepeatedEnumerationExecutor+PassOutcome"
                + "::Deconstruct(out " + Contracts + "LuxembourgDeliveryPass&, out "
                + N + "LuxembourgEnumerationRefusalDetail&) -> System.Void",
                "field private instance " + N + "LuxembourgEnumerationRunResult::<Refusal>k__BackingField -> "
                + N + "LuxembourgEnumerationRefusalDetail",
                "field private instance " + N + "LuxembourgRepeatedEnumerationExecutor+ObserveOutcome"
                + "::<Refusal>k__BackingField -> " + N + "LuxembourgEnumerationRefusalDetail",
                "field private instance " + N + "LuxembourgRepeatedEnumerationExecutor+PassOutcome"
                + "::<Refusal>k__BackingField -> " + N + "LuxembourgEnumerationRefusalDetail",
                "property public instance " + N + "LuxembourgEnumerationRunResult::Refusal() -> "
                + N + "LuxembourgEnumerationRefusalDetail",
                "property public instance " + N + "LuxembourgRepeatedEnumerationExecutor+ObserveOutcome"
                + "::Refusal() -> " + N + "LuxembourgEnumerationRefusalDetail",
                "property public instance " + N + "LuxembourgRepeatedEnumerationExecutor+PassOutcome"
                + "::Refusal() -> " + N + "LuxembourgEnumerationRefusalDetail",
            },
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgEnumerationRefusalDetail).Assembly,
                typeof(LuxembourgEnumerationRefusalDetail),
                true).ToArray(),
            "a refusal detail reached a new holder in Lex.V3.Ingest");
    }

    /// <summary>
    /// The run result: two factories, one for each half of "delivered or refused, never both and
    /// never neither", over one private constructor.
    /// </summary>
    /// <remarks>
    /// This pin is what holds that invariant now. The constructor used to carry a check for it,
    /// which no caller could trip, because Delivered and Refused are its only callers and each
    /// passes exactly one non-null argument. The check was removed as unreachable defense; what
    /// makes the invariant true is that this list has exactly these two factories in it, so a
    /// third door taking both, or neither, is a line in a diff here.
    /// </remarks>
    [TestMethod]
    public void ARunResultIsDeliveredOrRefusedByConstruction()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgEnumerationRunResult::.ctor("
                + Contracts + "LuxembourgEnumerationDeliveryReceipt, "
                + N + "LuxembourgEnumerationRefusalDetail, System.Int32) -> "
                + N + "LuxembourgEnumerationRunResult",
                "method public static " + N + "LuxembourgEnumerationRunResult::Delivered("
                + Contracts + "LuxembourgEnumerationDeliveryReceipt, System.Int32) -> "
                + N + "LuxembourgEnumerationRunResult",
                "method public static " + N + "LuxembourgEnumerationRunResult::Refused("
                + N + "LuxembourgEnumerationRefusalDetail, System.Int32) -> "
                + N + "LuxembourgEnumerationRunResult",
            },
            ConstructionSurface.Of(typeof(LuxembourgEnumerationRunResult)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // The three places a run result is returned, and the RunCoverAsync closure that
                // reports one bootstrap refusal per intended leaf. All of them go through the two
                // factories above; none is another way to build one.
                "method internal instance " + N + "LuxembourgRepeatedEnumerationExecutor+<>c"
                + "::<RunCoverAsync>b__8_0(" + Contracts + "LuxembourgQueryPartitionRange) -> "
                + N + "LuxembourgEnumerationRunResult",
                "method private instance " + N + "LuxembourgRepeatedEnumerationExecutor"
                + "::RunPartitionOnSessionAsync(" + N + "LuxembourgPartitionRunRequest, "
                + "Lex.V3.Ingest.RoutedHttpAcquisitionSession, " + Core + "SourceArtifactRef, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<"
                + N + "LuxembourgEnumerationRunResult>",
                "method public instance " + N + "LuxembourgRepeatedEnumerationExecutor::RunCoverAsync("
                + N + "LuxembourgPartitionRunRequest, " + Contracts + "LuxembourgPartitionChain, "
                + Core + "BoundMachineRequest, System.Threading.CancellationToken) -> "
                + "System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<"
                + N + "LuxembourgEnumerationRunResult>>",
                "method public instance " + N + "LuxembourgRepeatedEnumerationExecutor::RunPartitionAsync("
                + N + "LuxembourgPartitionRunRequest, " + Core + "BoundMachineRequest, "
                + "System.Threading.CancellationToken) -> System.Threading.Tasks.Task<"
                + N + "LuxembourgEnumerationRunResult>",
            },
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgEnumerationRunResult).Assembly,
                typeof(LuxembourgEnumerationRunResult),
                true).ToArray(),
            "a run result reached a new holder in Lex.V3.Ingest");
    }

    /// <summary>
    /// The budget: derived from the plan, never supplied. This is where "there is no
    /// caller-supplied page count, page limit or offset anywhere in this executor" stops being a
    /// comment: <c>FromPlan</c> is the only door and it takes a plan.
    /// </summary>
    [TestMethod]
    public void ABudgetComesOnlyFromThePlan()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgEnumerationBudget::.ctor("
                + "System.UInt32, System.UInt32) -> " + N + "LuxembourgEnumerationBudget",
                "method public static " + N + "LuxembourgEnumerationBudget::FromPlan("
                + Contracts + "LuxembourgQueryPlan) -> " + N + "LuxembourgEnumerationBudget",
            },
            ConstructionSurface.Of(typeof(LuxembourgEnumerationBudget)).ToArray());
    }

    /// <summary>
    /// The executor itself, and the test-transport seam. Pinned so the seam stays one internal
    /// constructor: if a second appears, or if the public one grows a transport parameter, that is
    /// a line in this diff rather than a habit.
    /// </summary>
    [TestMethod]
    public void TheExecutorHasOneProductionDoorAndOneInternalTestSeam()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance " + N + "LuxembourgRepeatedEnumerationExecutor::.ctor("
                + "Lex.V3.Contracts.Custody.ICustodyStore, System.TimeProvider, "
                + "System.Net.Http.HttpMessageHandler) -> " + N + "LuxembourgRepeatedEnumerationExecutor",
                "constructor public instance " + N + "LuxembourgRepeatedEnumerationExecutor::.ctor("
                + "Lex.V3.Contracts.Custody.ICustodyStore, System.TimeProvider) -> "
                + N + "LuxembourgRepeatedEnumerationExecutor",
            },
            ConstructionSurface.Of(typeof(LuxembourgRepeatedEnumerationExecutor)).ToArray());
    }

    /// <summary>
    /// Pinned and explicitly OPEN. The run request is a plain input record a caller assembles: a
    /// plan, its resource id, a set id, a partition and a renderer source. Nothing about holding
    /// one is evidence of anything, and every value in it is re-derived or digest-checked
    /// downstream, so closing its constructor would buy nothing. What the pin holds is that it
    /// stays an input record and does not quietly acquire a factory that decides one of those
    /// values on a caller's behalf.
    /// </summary>
    [TestMethod]
    public void ARunRequestIsAnOpenInputRecord()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgPartitionRunRequest::.ctor("
                + N + "LuxembourgPartitionRunRequest) -> " + N + "LuxembourgPartitionRunRequest",
                "constructor public instance " + N + "LuxembourgPartitionRunRequest::.ctor("
                + Contracts + "LuxembourgQueryPlan, System.String, System.String, "
                + Contracts + "LuxembourgQueryPartitionRange, " + Core + "MachineQueryRendererSource) -> "
                + N + "LuxembourgPartitionRunRequest",
                "method public instance " + N + "LuxembourgPartitionRunRequest::<Clone>$() -> "
                + N + "LuxembourgPartitionRunRequest",
            },
            ConstructionSurface.Of(typeof(LuxembourgPartitionRunRequest)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgPartitionRunRequest).Assembly,
                typeof(LuxembourgPartitionRunRequest),
                true).ToArray(),
            "something now decides a run request's contents on the caller's behalf");
    }
}
