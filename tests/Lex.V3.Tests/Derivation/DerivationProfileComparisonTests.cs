using Lex.V3.Contracts;
using Lex.V3.Contracts.Derivation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Derivation;

[TestClass]
public sealed class DerivationProfileComparisonTests
{
    [TestMethod]
    public void IdenticalProfileIdentitiesAreComparable()
    {
        Assert.AreEqual(
            DerivationProfileComparisonOutcome.Comparable,
            DerivationProfileComparison.Compare("akn-lu/3", "akn-lu/3"));
    }

    [TestMethod]
    [DataRow("akn-lu/1", "akn-lu/2")]
    [DataRow("fmx4-eu/1", "xhtml-eu/1")]
    [DataRow("xhtml-eu/1", "XHTML-EU/1")]
    public void EveryDistinctProfilePairRefusesComparison(
        string leftProfileId,
        string rightProfileId)
    {
        Assert.AreEqual(
            DerivationProfileComparisonOutcome.ProfilesDiffer,
            DerivationProfileComparison.Compare(leftProfileId, rightProfileId));
    }

    [TestMethod]
    public void ProfilesDifferHasTheExactWireToken()
    {
        Assert.AreEqual(
            "\"profiles_differ\"",
            ContractJson.Serialize(DerivationProfileComparisonOutcome.ProfilesDiffer));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("akn-lu/1\n")]
    public void InvalidProfileIdentitiesAreRejected(string profileId)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => DerivationProfileComparison.Compare(profileId, "akn-lu/1"));
        Assert.ThrowsExactly<ArgumentException>(
            () => DerivationProfileComparison.Compare("akn-lu/1", profileId));
    }

    [TestMethod]
    public void ComparisonSurfaceHasNoOverrideParameter()
    {
        var compare = typeof(DerivationProfileComparison).GetMethod(
            nameof(DerivationProfileComparison.Compare));

        Assert.IsNotNull(compare);
        CollectionAssert.AreEqual(
            new[] { typeof(string), typeof(string) },
            compare.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
    }
}
