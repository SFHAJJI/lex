using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Http;

/// <summary>
/// The construction surface of the R3.4 representation chain types.
///
/// <para>
/// <see cref="RepresentationChainObservation.FromHop"/> is the whole reason
/// <see cref="RepresentationChainObservation"/> is worth more than a handful of loose fields: a
/// <see cref="RoutedHttpHop"/> cannot exist unless <see cref="RoutedHttpHop.Create"/> already ran
/// every framing, status and durability check the routed evidence v4 machinery has. If a second
/// producer of an observation ever appears anywhere in this assembly, an observation can describe
/// bytes nothing actually retained, and this pin is where that shows up.
/// </para>
/// <para>
/// The two nested types, <see cref="RepresentationChain.AppendedObservation"/> and
/// <see cref="RepresentationChain.FileReplacedEvent"/>, need an internal constructor for
/// <see cref="RepresentationChain.TryAppend"/> to call, and C# gives an enclosing type no special
/// access to a nested type's private members. Internal is reachable by every type in this
/// assembly and by anything it befriends, so visibility does not enforce "only the chain mints
/// these"; this pin does.
/// </para>
/// </summary>
[TestClass]
public sealed class RepresentationChainConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Http.";

    [TestMethod]
    public void AKeyHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepresentationChainKey::.ctor("
                + "System.String, System.String, System.String) -> " + N + "RepresentationChainKey",
                "method public static " + N + "RepresentationChainKey::Create("
                + "System.String, System.String, System.String) -> " + N + "RepresentationChainKey",
            },
            ConstructionSurface.Of(typeof(RepresentationChainKey)).ToArray());
    }

    [TestMethod]
    public void AnObservationHasExactlyOneCheckedDoorAndItRequiresARealHop()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepresentationChainObservation::.ctor("
                + "System.String, System.String, " + N + "HttpStatusDisposition, System.Boolean, "
                + "System.UInt64, System.String, System.String) -> " + N + "RepresentationChainObservation",
                "method public static " + N + "RepresentationChainObservation::FromHop("
                + N + "RoutedHttpHop) -> " + N + "RepresentationChainObservation",
            },
            ConstructionSurface.Of(typeof(RepresentationChainObservation)).ToArray());
    }

    [TestMethod]
    public void AChainHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "RepresentationChain::.ctor("
                + N + "RepresentationChainKey, System.Boolean) -> " + N + "RepresentationChain",
                "method public static " + N + "RepresentationChain::Open("
                + N + "RepresentationChainKey, System.Boolean) -> " + N + "RepresentationChain",
            },
            ConstructionSurface.Of(typeof(RepresentationChain)).ToArray());
    }

    [TestMethod]
    public void AnAppendedObservationIsMintedOnlyByTheChainsTryAppend()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "RepresentationChain+FileReplacedEvent::<Predecessor>k__BackingField -> "
                + N + "RepresentationChain+AppendedObservation",
                "field private instance " + N + "RepresentationChain+FileReplacedEvent::<Replacement>k__BackingField -> "
                + N + "RepresentationChain+AppendedObservation",
                "field private instance " + N + "RepresentationChain::<CurrentTrustedBaseline>k__BackingField -> "
                + N + "RepresentationChain+AppendedObservation",
                "field private instance " + N + "RepresentationChain::_history -> "
                + "System.Collections.Generic.List<" + N + "RepresentationChain+AppendedObservation>",
                "method public instance " + N + "RepresentationChain::TryAppend("
                + N + "RepresentationChainObservation, out " + N + "RepresentationChainAppendRefusal&) -> "
                + N + "RepresentationChain+AppendedObservation",
                "property public instance " + N + "RepresentationChain+FileReplacedEvent::Predecessor() -> "
                + N + "RepresentationChain+AppendedObservation",
                "property public instance " + N + "RepresentationChain+FileReplacedEvent::Replacement() -> "
                + N + "RepresentationChain+AppendedObservation",
                "property public instance " + N + "RepresentationChain::CurrentTrustedBaseline() -> "
                + N + "RepresentationChain+AppendedObservation",
                "property public instance " + N + "RepresentationChain::History() -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "RepresentationChain+AppendedObservation>",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepresentationChain).Assembly,
                typeof(RepresentationChain.AppendedObservation),
                true).ToArray(),
            "an appended observation reached a new holder in Contracts");
    }

    [TestMethod]
    public void AFileReplacedEventIsMintedOnlyByTheChainsTryAppend()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "RepresentationChain::_replacements -> "
                + "System.Collections.Generic.List<" + N + "RepresentationChain+FileReplacedEvent>",
                "property public instance " + N + "RepresentationChain::ReplacementEvents() -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "RepresentationChain+FileReplacedEvent>",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepresentationChain).Assembly,
                typeof(RepresentationChain.FileReplacedEvent),
                true).ToArray(),
            "a file_replaced event reached a new holder in Contracts");
    }

    [TestMethod]
    public void AnObservationHasExactlyOneHolderInContractsOutsideItself()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "RepresentationChain+AppendedObservation::<Observation>k__BackingField -> "
                + N + "RepresentationChainObservation",
                "property public instance " + N + "RepresentationChain+AppendedObservation::Observation() -> "
                + N + "RepresentationChainObservation",
            },
            ConstructionSurface.ProducersIn(
                typeof(RepresentationChain).Assembly,
                typeof(RepresentationChainObservation),
                true).ToArray(),
            "a representation-chain observation reached a new holder in Contracts");
    }
}
