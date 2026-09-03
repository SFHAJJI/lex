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

        // Objection 2: both reproducer identities and roles must be retained on the inventory, not
        // discarded once reconciliation succeeds.
        Assert.AreEqual(QuarantineReproducerRole.Primary, inventory.PrimaryReproducerRole);
        Assert.AreEqual("writer-run-2026-09-03a", inventory.PrimaryReproducerIdentity);
        Assert.AreEqual(QuarantineReproducerRole.IndependentReviewer, inventory.IndependentReviewerReproducerRole);
        Assert.AreEqual("reviewer-run-2026-09-03b", inventory.IndependentReviewerReproducerIdentity);
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
        // The content must genuinely differ, or this test's own name is false: QuarantineFixtures
        // .CoordinateSet() always returns the same three coordinates, so reusing it verbatim for
        // both sides (as this test previously did) left "AndContent" untested -- only the identity
        // actually differed. alsoPrimary here is a single, distinct coordinate the baseline set
        // does not contain.
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var alsoPrimary = MustCreate(
            QuarantineReproducerRole.Primary,
            "writer-run-b",
            new[] { new PriorPublicCoordinate("eu-eurlex:celex:32020R9999", "en", "2022-06-01", null) });

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

    /// <summary>
    /// Objection 4: the previous version of this test hand-typed two 64-'1'-character strings and
    /// commented them "66 chars" / "non-hex char", but both were actually 62 characters -- so every
    /// row failed the length check (!= 64) before the hex-character clause ever ran, leaving that
    /// clause undriven. Every value here is built with <c>new string(...)</c>/concatenation instead
    /// of a hand-counted literal, so a length claim can never silently drift from the real length
    /// again, and the exact-length rows below reach the hex-character clause specifically rather
    /// than being caught by the length check first.
    /// </summary>
    [TestMethod]
    public void APriorIndexPairHashMustBeAnExactSixtyFourCharacterLowercaseHexString()
    {
        var primary = MustCreate(QuarantineReproducerRole.Primary, "writer-run-a", QuarantineFixtures.CoordinateSet());
        var reviewer = MustCreate(
            QuarantineReproducerRole.IndependentReviewer, "reviewer-run-b", QuarantineFixtures.CoordinateSet());

        AssertRefused(string.Empty); // too short (0)
        AssertRefused("not-hex"); // too short (7), and not hex-shaped either
        AssertRefused(new string('1', 63)); // one short of 64
        AssertRefused(new string('1', 65)); // one over 64
        AssertRefused(new string('1', 63) + "G"); // exact length: one uppercase hex digit
        AssertRefused(new string('1', 63) + "g"); // exact length: one lowercase non-hex letter
        AssertRefused(new string('A', 64)); // exact length: entirely uppercase hex

        void AssertRefused(string priorIndexPairSha256)
        {
            var inventory = QuarantinedPriorCoordinateInventory.TryReconcile(
                primary,
                reviewer,
                priorIndexPairSha256,
                QuarantineFixtures.SourceIndexIdentity(),
                QuarantineFixtures.Receipt(),
                QuarantineFixtures.Attestation(),
                out var refusal);

            Assert.IsNull(inventory, $"expected refusal for a {priorIndexPairSha256.Length}-char value");
            Assert.AreEqual(
                QuarantineInventoryRefusal.PriorIndexPairHashInvalid,
                refusal,
                $"expected PriorIndexPairHashInvalid for a {priorIndexPairSha256.Length}-char value");
        }
    }

    /// <summary>
    /// Objection 4: TryReconcile guards five reference parameters with
    /// <c>ArgumentNullException.ThrowIfNull</c>, but only the first two (primary, independentReviewer)
    /// had a test before this branch. This drives all five.
    /// </summary>
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

        Assert.ThrowsExactly<ArgumentNullException>(() => QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            null!,
            QuarantineFixtures.Receipt(),
            QuarantineFixtures.Attestation(),
            out _));

        Assert.ThrowsExactly<ArgumentNullException>(() => QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            null!,
            QuarantineFixtures.Attestation(),
            out _));

        Assert.ThrowsExactly<ArgumentNullException>(() => QuarantinedPriorCoordinateInventory.TryReconcile(
            primary,
            reviewer,
            QuarantineFixtures.PriorIndexPairSha256(),
            QuarantineFixtures.SourceIndexIdentity(),
            QuarantineFixtures.Receipt(),
            null!,
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
