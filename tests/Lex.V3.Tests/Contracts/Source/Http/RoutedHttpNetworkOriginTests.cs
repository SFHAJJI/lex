using System.Reflection;

using Lex.V3.Contracts.Source.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Http;

/// <summary>
/// The network origin, and the two constants that must not drift apart.
/// </summary>
[TestClass]
public sealed class RoutedHttpNetworkOriginTests
{
    [TestMethod]
    public void AnOriginIsDerivedFromAUriRatherThanAsserted()
    {
        // The constructor took a host and a port and enforced none of the grammar its factory
        // enforces, so a caller could state a host that no URI would ever produce and a port that
        // did not belong to it. Both literal call sites in the source profile now derive from the
        // robots URI the same route already states, so host and port cannot drift from it.
        var type = typeof(RoutedHttpNetworkOrigin);
        var constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsTrue(constructors.Length > 0);
        Assert.IsTrue(
            constructors.All(constructor => constructor.IsPrivate),
            "an assembly-visible constructor bypasses the URI grammar the factory enforces");
    }

}
