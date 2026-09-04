using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Appendix A of D1-01 Candidate 5, held as data under a digest a test can recompute rather than
/// trust by inspection. Every refusal in <see cref="EuAppendixASeedMapRefusal"/> is driven here by
/// corrupting an explicit copy of the real 82 lines, never by inventing a synthetic list that could
/// pass for a different reason.
/// </summary>
[TestClass]
public sealed class EuAppendixASeedMapTests
{
    private static (string Celex, string WorkRoot)[] RealSeedLines() =>
        EuAppendixASeedMap.SeedsInCelexOrder.ToArray();

    [TestMethod]
    public void ThePackHasExactlyEightyTwoCanonicalSortedRoots()
    {
        Assert.AreEqual(EuAppendixASeedMap.SeedCount, EuAppendixASeedMap.PackRoots.Count);
        CollectionAssert.AllItemsAreUnique(EuAppendixASeedMap.PackRoots.ToArray());

        var sorted = EuAppendixASeedMap.PackRoots.OrderBy(static r => r, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(sorted, EuAppendixASeedMap.PackRoots.ToArray());

        foreach (var root in EuAppendixASeedMap.PackRoots)
        {
            var canonical = EuPackRootCanonicalForm.TryCanonicalize(root, out var refusal);
            Assert.IsNotNull(canonical, $"{root} must already be canonical ({refusal}).");
            Assert.AreEqual(root, canonical, "Canonicalizing an already-canonical root must be idempotent.");
        }
    }

    [TestMethod]
    public void TheSeedMapRefBindsAppendixASOwnDigest()
    {
        Assert.AreEqual(EuAppendixASeedMap.AppendixASha256, EuAppendixASeedMap.SeedMapRef.Sha256);
        Assert.AreEqual(
            "urn:uuid:618963c7-0c91-4a23-a17f-3a723f5ee74e",
            EuAppendixASeedMap.SeedMapRef.ResourceId);
    }

    [TestMethod]
    public void TheRealSeedListsOwnDigestAndByteCountMatchAppendixASFence()
    {
        // The positive control every mutation test needs a baseline for: the embedded 82 lines,
        // reduced through the identical canonical-serialization function a hostile test corrupts
        // below, reproduce Appendix A's own stated digest and byte count. This is what makes the
        // digest constant a checked fact about the array rather than a second copy of it.
        var rows = RealSeedLines();
        var digest = EuAppendixASeedMap.ComputeCanonicalSerializationSha256(rows);
        Assert.AreEqual(EuAppendixASeedMap.AppendixASha256, digest);

        var byteCount = rows.Sum(row =>
            System.Text.Encoding.UTF8.GetByteCount(row.Celex) + 1 +
            System.Text.Encoding.UTF8.GetByteCount(row.WorkRoot) + 1);
        Assert.AreEqual(EuAppendixASeedMap.AppendixAByteCount, byteCount);
    }

    [TestMethod]
    public void TheRealSeedListValidatesCleanly()
    {
        var canonical = EuAppendixASeedMap.TryValidateAndCanonicalize(
            RealSeedLines(), EuAppendixASeedMap.AppendixASha256, out var refusal);
        Assert.IsNotNull(canonical);
        Assert.AreEqual(EuAppendixASeedMapRefusal.None, refusal);
        CollectionAssert.AreEqual(EuAppendixASeedMap.PackRoots.ToArray(), canonical!.ToArray());
    }

    [TestMethod]
    public void ARemovedSeedRefusesOnCount()
    {
        var rows = RealSeedLines().Skip(1).ToArray();
        var result = EuAppendixASeedMap.TryValidateAndCanonicalize(
            rows, EuAppendixASeedMap.AppendixASha256, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuAppendixASeedMapRefusal.SeedCountNotEightyTwo, refusal);
    }

    [TestMethod]
    public void AnOutOfOrderButAllDistinctCelexRefusesOnOrdering()
    {
        // Swapping two adjacent, otherwise-untouched rows keeps every CELEX distinct (the repeat
        // check, which now runs first, passes) while breaking strict ascent at exactly that pair,
        // so this drives the ordering branch alone.
        var rows = RealSeedLines();
        (rows[0], rows[1]) = (rows[1], rows[0]);
        var result = EuAppendixASeedMap.TryValidateAndCanonicalize(
            rows, EuAppendixASeedMap.AppendixASha256, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuAppendixASeedMapRefusal.CelexNotStrictlyAscending, refusal);
    }

    [TestMethod]
    public void ARepeatedCelexRefusesOnCelexRepeatedBeforeOrderingIsEvenChecked()
    {
        // Overwriting row 1's CELEX with row 0's turns the sequence non-ascending too (compare
        // returns 0 at that pair), so this also proves the repeat check runs first: were ordering
        // checked first, it would report CelexNotStrictlyAscending for this exact input instead,
        // which is precisely the shadowing the production code now avoids.
        var rows = RealSeedLines();
        rows[1] = (rows[0].Celex, rows[1].WorkRoot);
        var result = EuAppendixASeedMap.TryValidateAndCanonicalize(
            rows, EuAppendixASeedMap.AppendixASha256, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuAppendixASeedMapRefusal.CelexRepeated, refusal);
    }

    [TestMethod]
    public void ARepeatedWorkRootRefusesOnWorkRootRepeated()
    {
        var rows = RealSeedLines();
        rows[1] = (rows[1].Celex, rows[0].WorkRoot);
        var result = EuAppendixASeedMap.TryValidateAndCanonicalize(
            rows, EuAppendixASeedMap.AppendixASha256, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuAppendixASeedMapRefusal.WorkRootRepeated, refusal);
    }

    [TestMethod]
    public void ANonCanonicalWorkRootRefusesOnWorkRootNotCanonical()
    {
        var rows = RealSeedLines();
        rows[0] = (rows[0].Celex, rows[0].WorkRoot + "?x=1");
        var result = EuAppendixASeedMap.TryValidateAndCanonicalize(
            rows, EuAppendixASeedMap.AppendixASha256, out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuAppendixASeedMapRefusal.WorkRootNotCanonical, refusal);
    }

    [TestMethod]
    public void AWrongExpectedDigestRefusesOnDigestMismatch()
    {
        var result = EuAppendixASeedMap.TryValidateAndCanonicalize(
            RealSeedLines(),
            "0000000000000000000000000000000000000000000000000000000000000000",
            out var refusal);
        Assert.IsNull(result);
        Assert.AreEqual(EuAppendixASeedMapRefusal.CanonicalBytesDigestMismatch, refusal);
    }

    [TestMethod]
    public void TheDigestFunctionIsSensitiveToItsInput()
    {
        // Sensitivity proof for ComputeCanonicalSerializationSha256 itself, the same discipline
        // EuScopeProfile.ComputeProfileSha256 uses for its own internal digest function: change one
        // byte of one row and the digest must move.
        var rows = RealSeedLines();
        var before = EuAppendixASeedMap.ComputeCanonicalSerializationSha256(rows);
        rows[0] = (rows[0].Celex, rows[0].WorkRoot + "x");
        var after = EuAppendixASeedMap.ComputeCanonicalSerializationSha256(rows);
        Assert.AreNotEqual(before, after);
    }
}
