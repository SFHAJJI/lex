using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// Drives every refusal branch of <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/>,
/// and in particular the two independence checks and the byte-identity check, each with a fixture
/// built to fail if the check it targets were replaced by a self-comparison.
/// </summary>
[TestClass]
public sealed class QuarantinedPriorCoordinateInventoryTests
{
    [TestMethod]
    public void TwoGenuinelyIndependentAndAgreeingReproductionsAreAccepted()
    {
        // The two reproductions are built from separately constructed coordinate lists (not the
        // same list reference passed twice) under different roles and different reproducer
        // identities -- the two things TryReconcile is required to check are distinct here, not
        // merely typed differently.
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-2026-09-03a", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "reviewer-run-2026-09-03b", QuarantineFixtures.CoordinateSet());

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNotNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.None, refusal);
        Assert.AreEqual(3, inventory.Coordinates.Count);
        Assert.AreEqual(primary.CanonicalSha256, inventory.CoordinateSetSha256);
        Assert.AreEqual(reviewer.CanonicalSha256, inventory.CoordinateSetSha256);
    }

    /// <summary>
    /// The order-independence proof folded in: the reviewer's raw list is supplied in reverse row
    /// order from the primary's, as two genuinely different traversals of the same true content
    /// would be. Reconciliation must still accept, because both normalize to the same canonical
    /// bytes; a naive positional (not canonical) comparison would wrongly refuse this.
    /// </summary>
    [TestMethod]
    public void ReproductionsThatEnumeratedInDifferentOrdersStillReconcile()
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer,
            "reviewer-run-b",
            QuarantineFixtures.CoordinateSet().Reverse().ToArray());

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNotNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.None, refusal);
    }

    [TestMethod]
    public void TwoReproductionsUnderTheSameRoleAreRefusedEvenWithDifferentIdentitiesAndContent()
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var alsoPrimary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-b", QuarantineFixtures.CoordinateSet());

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            alsoPrimary,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.ReproductionRolesNotDistinct, refusal);
    }

    /// <summary>
    /// The check this project's own history says a naive implementation skips: two reproductions
    /// declared under the two distinct required roles, with byte-identical content, but minted by
    /// the very same reproducer identity -- exactly what "call the same tool once and label its
    /// output twice" would produce. Role distinctness alone does not catch this; identity
    /// distinctness is a second, separate check and this fixture is built so only that second
    /// check can catch it (both roles differ, both digests agree).
    /// </summary>
    [TestMethod]
    public void TheSameReproducerIdentityUnderBothRolesIsRefusedEvenWithAgreeingContent()
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "shared-identity", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "shared-identity", QuarantineFixtures.CoordinateSet());

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.ReproducerIdentitiesNotDistinct, refusal);
    }

    [TestMethod]
    public void DifferentCoordinateCountsAreRefusedBeforeAnyDigestComparison()
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer,
            "reviewer-run-b",
            QuarantineFixtures.CoordinateSet().Take(2).ToArray());

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.ReproductionCountMismatch, refusal);
    }

    /// <summary>
    /// The core "byte-identical" proof: same count, distinct role, distinct identity, but one
    /// coordinate disagrees between the two reproductions (a different valid-from on the same
    /// work/language/anchor). This is the fixture that would pass wrongly if
    /// <see cref="QuarantinedPriorCoordinateInventory.TryReconcile"/> ever compared a reproduction's
    /// digest to itself instead of to the other reproduction's independently derived digest --
    /// which is exactly the mutation confirmed against this test (see the branch commit message
    /// for the kill record).
    /// </summary>
    [TestMethod]
    public void ReproductionsOfEqualCountThatDisagreeOnOneCoordinateAreRefused()
    {
        var primaryCoordinates = QuarantineFixtures.CoordinateSet();
        var reviewerCoordinates = primaryCoordinates.Take(primaryCoordinates.Count - 1)
            .Append(new PriorPublicCoordinate("eu-eurlex:celex:32020R0001", "en", "2099-12-31", null))
            .ToArray();

        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", primaryCoordinates);
        var reviewer = MustCreate(QuarantineReproducerRole.IndependentReviewer, "reviewer-run-b", reviewerCoordinates);

        Assert.AreEqual(primary.Count, reviewer.Count, "the count check must not be what catches this case");
        Assert.AreNotEqual(
            primary.CanonicalSha256,
            reviewer.CanonicalSha256,
            "the fixture must actually disagree, or this test cannot tell a real check from a self-comparison");

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.ReproductionsDisagree, refusal);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-hex")]
    [DataRow("11111111111111111111111111111111111111111111111111111111111111")] // 66 chars
    [DataRow("1111111111111111111111111111111111111111111111111111111111111G")] // non-hex char
    public void APriorIndexPairHashMustBeExactLowercaseSha256(string priorIndexPairSha256)
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "reviewer-run-b", QuarantineFixtures.CoordinateSet());

        var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            priorIndexPairSha256,
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out var refusal);

        Assert.IsNull(inventory);
        Assert.AreEqual(QuarantineInventoryRefusal.PriorIndexPairHashInvalid, refusal);
    }

    [TestMethod]
    public void NullArgumentsAreRejectedRatherThanRefused()
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "reviewer-run-b", QuarantineFixtures.CoordinateSet());

        Assert.ThrowsExactly<ArgumentNullException>(() => QuarantinedPriorCoordinateInventory.TryReconcile(
            null!,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out _));

        Assert.ThrowsExactly<ArgumentNullException>(() => QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            null!,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out _));
    }

    private static QuarantinePriorCoordinateReproduction MustCreate(
        QuarantineReproducerRole role,
        string reproducerIdentity,
        IReadOnlyList<PriorPublicCoordinate> coordinates)
    {
        var reproduction = QuarantinePriorCoordinateReproduction.TryCreate(
            role, reproducerIdentity, coordinates, out var refusal);
        Assert.IsNotNull(reproduction, $"fixture setup failed: {refusal}");
        return reproduction;
    }
}
