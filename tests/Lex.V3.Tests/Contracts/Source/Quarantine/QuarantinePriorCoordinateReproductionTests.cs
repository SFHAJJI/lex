using System.Collections;
using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

[TestClass]
public sealed class QuarantinePriorCoordinateReproductionTests
{
    [TestMethod]
    public void ATypicalReproductionIsAccepted()
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary,
            "quarantine-verifier-run-a",
            QuarantineFixtures.CoordinateSet(),
            out var refusal);

        Assert.IsNotNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.None, refusal);
        Assert.AreEqual(3, reproduction.Count);
        Assert.AreEqual(
            PriorPublicCoordinateSet.CanonicalSha256Hex(QuarantineFixtures.CoordinateSet()),
            reproduction.CanonicalSha256);
    }

    [TestMethod]
    public void AnUndefinedRoleIsRefused()
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            (QuarantineReproducerRole)99,
            "quarantine-verifier-run-a",
            QuarantineFixtures.CoordinateSet(),
            out var refusal);

        Assert.IsNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.RoleUndefined, refusal);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("has a space and é non-ascii")]
    public void AnInvalidReproducerIdentityIsRefused(string identity)
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary, identity, QuarantineFixtures.CoordinateSet(), out var refusal);

        Assert.IsNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.ReproducerIdentityInvalid, refusal);
    }

    [TestMethod]
    public void AnOverlongReproducerIdentityIsRefused()
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary,
            new string('a', QuarantinePriorCoordinateReproduction.MaximumReproducerIdentityLength + 1),
            QuarantineFixtures.CoordinateSet(),
            out var refusal);

        Assert.IsNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.ReproducerIdentityInvalid, refusal);
    }

    [TestMethod]
    public void AnEmptyCoordinateListIsRefused()
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary,
            "quarantine-verifier-run-a",
            Array.Empty<PriorPublicCoordinate>(),
            out var refusal);

        Assert.IsNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.CoordinatesEmpty, refusal);
    }

    [TestMethod]
    public void ARepeatedCoordinateIsRefused()
    {
        var duplicate = QuarantineFixtures.Coordinate();
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary,
            "quarantine-verifier-run-a",
            new[] { duplicate, duplicate },
            out var refusal);

        Assert.IsNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.DuplicateCoordinate, refusal);
    }

    /// <summary>
    /// Exercises the <see cref="QuarantinePriorCoordinateReproduction.MaximumCoordinates"/> bound
    /// without allocating two million real coordinates: the list's declared <see cref="IReadOnlyList{T}.Count"/>
    /// alone is enough to trigger <see cref="QuarantineReproductionRefusal.CoordinatesTooMany"/>,
    /// because that check runs before any enumeration. The fake list's enumerator throws if the
    /// production code ever tries to iterate it, so this test would itself fail loudly (not pass
    /// vacuously) if the bound check stopped short-circuiting.
    /// </summary>
    [TestMethod]
    public void AnOversizedCoordinateListIsRefusedWithoutEnumeratingIt()
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            QuarantineReproducerRole.Primary,
            "quarantine-verifier-run-a",
            new ClaimedCountList(QuarantinePriorCoordinateReproduction.MaximumCoordinates + 1),
            out var refusal);

        Assert.IsNull(reproduction);
        Assert.AreEqual(QuarantineReproductionRefusal.CoordinatesTooMany, refusal);
    }

    private sealed class ClaimedCountList(int count) : IReadOnlyList<PriorPublicCoordinate>
    {
        public int Count { get; } = count;

        public PriorPublicCoordinate this[int index] =>
            throw new InvalidOperationException("The bound check must refuse before indexing.");

        public IEnumerator<PriorPublicCoordinate> GetEnumerator() =>
            throw new InvalidOperationException("The bound check must refuse before enumerating.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
