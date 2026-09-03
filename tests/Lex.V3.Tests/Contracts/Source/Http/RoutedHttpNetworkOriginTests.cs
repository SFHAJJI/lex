
using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Http;

/// <summary>
/// The network origin, and the two constants that must not drift apart.
/// </summary>
[TestClass]
public sealed class RoutedHttpNetworkOriginTests
{
    private const string Origin = "Lex.V3.Contracts.Source.Http.RoutedHttpNetworkOrigin";

    [TestMethod]
    public void AnOriginIsDerivedFromAUriRatherThanAsserted()
    {
        // The constructor took a host and a port and enforced none of the grammar its factory
        // enforces, so a caller could state a host that no URI would ever produce and a port that
        // did not belong to it. Both literal call sites in the source profile now derive from the
        // robots URI the same route already states, so host and port cannot drift from it.
        // Pinned through the shared guard rather than hand-rolled reflection: four separate
        // surface tests in this repository were each wrong about a different scope before it
        // existed, and this one was written in the same shape.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Origin + "::.ctor(System.String, System.UInt16) -> " + Origin,
                "method internal static " + Origin + "::FromUri(System.String) -> " + Origin,
            },
            ConstructionSurface.Of(typeof(RoutedHttpNetworkOrigin)).ToArray(),
            "an origin may only be derived from a URI, never asserted as a host and a port");
    }

}
