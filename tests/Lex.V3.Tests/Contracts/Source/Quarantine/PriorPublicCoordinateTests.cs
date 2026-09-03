using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

[TestClass]
public sealed class PriorPublicCoordinateTests
{
    [TestMethod]
    public void AVersionLevelCoordinateAcceptsNoAnchor()
    {
        var coordinate = new PriorPublicCoordinate(
            "lu-legilux:eli/etat/leg/loi/2020-01-01/1", "fr", "2020-01-01", null);

        Assert.IsNull(coordinate.Anchor);
        Assert.AreEqual("fr", coordinate.Language);
    }

    [TestMethod]
    public void AProvisionLevelCoordinateCarriesItsAnchor()
    {
        var coordinate = QuarantineFixtures.Coordinate(anchor: "art_1er");
        Assert.AreEqual("art_1er", coordinate.Anchor);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AWorkKeyMustNotBeBlank(string workKey) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new PriorPublicCoordinate(workKey, "fr", "2020-01-01", null));

    [TestMethod]
    public void AWorkKeyMustNotExceedTheBound() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new PriorPublicCoordinate(new string('a', 513), "fr", "2020-01-01", null));

    [TestMethod]
    [DataRow("../../etc/passwd")]
    [DataRow("works/loi-2020-01-01.xml")]
    [DataRow("lu-legilux/works/loi-2020-01-01")]
    [DataRow("https://legilux.public.lu/eli/etat/leg/loi/2020-01-01/1")]
    [DataRow("loi-2020-01-01.xml")]
    [DataRow("loi-2020-01-01.HTML")]
    [DataRow("loi-2020-01-01.json")]
    public void AWorkKeyRejectsAnythingShapedLikeAPathUriOrLawContentFile(string workKey) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new PriorPublicCoordinate(workKey, "fr", "2020-01-01", null));

    [TestMethod]
    [DataRow("../../etc/passwd")]
    [DataRow("art_1er.xml")]
    public void AnAnchorIsHeldToTheSameRejectionAsAWorkKey(string anchor) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new PriorPublicCoordinate("lu-legilux:1", "fr", "2020-01-01", anchor));

    [TestMethod]
    [DataRow("2020-1-1")]
    [DataRow("01-01-2020")]
    [DataRow("2020-01-01T00:00:00Z")]
    [DataRow("not-a-date")]
    [DataRow("")]
    public void AValidFromMustBeAnExactCalendarDate(string validFrom) =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new PriorPublicCoordinate("lu-legilux:1", "fr", validFrom, null));

    [TestMethod]
    public void ALanguageIsValidatedTheSameWayRoutedHttpEvidenceValidatesIt() =>
        Assert.ThrowsExactly<ArgumentException>(
            () => new PriorPublicCoordinate("lu-legilux:1", "FR", "2020-01-01", null));

    [TestMethod]
    public void CanonicalBytesAreOrderIndependent()
    {
        var forward = QuarantineFixtures.CoordinateSet();
        var reversed = forward.Reverse().ToArray();

        CollectionAssert.AreEqual(
            PriorPublicCoordinateSet.CanonicalBytes(forward),
            PriorPublicCoordinateSet.CanonicalBytes(reversed));
        Assert.AreEqual(
            PriorPublicCoordinateSet.CanonicalSha256Hex(forward),
            PriorPublicCoordinateSet.CanonicalSha256Hex(reversed));
    }

    [TestMethod]
    public void CanonicalBytesAreSensitiveToContent()
    {
        var baseline = QuarantineFixtures.CoordinateSet();
        var changed = baseline.Take(baseline.Count - 1)
            .Append(new PriorPublicCoordinate("eu-eurlex:celex:32020R0001", "en", "2021-01-01", null))
            .ToArray();

        Assert.AreNotEqual(
            PriorPublicCoordinateSet.CanonicalSha256Hex(baseline),
            PriorPublicCoordinateSet.CanonicalSha256Hex(changed));
    }

    [TestMethod]
    public void CanonicalBytesDistinguishAVersionLevelCoordinateFromAProvisionLevelOneAtTheSameKey()
    {
        var versionLevel = new[] { QuarantineFixtures.Coordinate(anchor: null) };
        var provisionLevel = new[] { QuarantineFixtures.Coordinate(anchor: "art_1er") };

        Assert.AreNotEqual(
            PriorPublicCoordinateSet.CanonicalSha256Hex(versionLevel),
            PriorPublicCoordinateSet.CanonicalSha256Hex(provisionLevel));
    }
}
