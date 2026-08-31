using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class EuSeedResolutionPlanTests
{
    [TestMethod]
    public void SeedListIsExactSortedUniqueAndDigestBound()
    {
        Assert.HasCount(82, EuSeedResolutionPlan.Seeds);
        Assert.HasCount(82, EuSeedResolutionPlan.Seeds.Distinct(StringComparer.Ordinal));
        CollectionAssert.AreEqual(
            EuSeedResolutionPlan.Seeds.OrderBy(static seed => seed, StringComparer.Ordinal).ToArray(),
            EuSeedResolutionPlan.Seeds.ToArray());

        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', EuSeedResolutionPlan.Seeds) + "\n");
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.AreEqual(902, bytes.Length);
        Assert.AreEqual(
            "ea1b4f276406a8bede5223459b92d7a94321de5b9a38de63397f2e22688d50c0",
            digest);
        Assert.AreEqual(EuSeedResolutionPlan.SeedListSha256, digest);
        CollectionAssert.AreEqual(
            new[]
            {
                "12012E/TXT",
                "12012M/TXT",
                "12012P/TXT",
                "12016E/TXT",
                "12016M/TXT",
                "12016P/TXT",
            },
            EuSeedResolutionPlan.Seeds.Take(6).ToArray());
        Assert.AreEqual("32024R2847", EuSeedResolutionPlan.Seeds[^1]);
    }

    [TestMethod]
    public void ResolutionBatchesAreTheExactTypedPartition()
    {
        Assert.AreEqual(
            "http://www.w3.org/2001/XMLSchema#string",
            EuSeedResolutionPlan.XsdStringDatatypeIri);
        Assert.HasCount(2, EuSeedResolutionPlan.Batches);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            EuSeedResolutionPlan.Batches.Select(static batch => batch.Ordinal).ToArray());
        CollectionAssert.AreEqual(
            new[] { 50, 34 },
            EuSeedResolutionPlan.Batches.Select(static batch => batch.Rows.Count).ToArray());
        CollectionAssert.AreEqual(
            new[] { 49, 33 },
            EuSeedResolutionPlan.Batches.Select(static batch => batch.DataRowCount).ToArray());

        foreach (var batch in EuSeedResolutionPlan.Batches)
        {
            Assert.IsLessThanOrEqualTo(50, batch.Rows.Count);
            Assert.AreEqual(1, batch.ExpectedControlCardinality);
            Assert.IsTrue(batch.Rows[0].IsControl);
            Assert.HasCount(1, batch.Rows.Where(static row => row.IsControl));
            Assert.IsTrue(batch.Rows.All(static row =>
                string.Equals(
                    row.DatatypeIri,
                    EuSeedResolutionPlan.XsdStringDatatypeIri,
                    StringComparison.Ordinal)));
        }

        CollectionAssert.AreEqual(
            EuSeedResolutionPlan.Seeds.ToArray(),
            EuSeedResolutionPlan.Batches
                .SelectMany(static batch => batch.Rows)
                .Where(static row => !row.IsControl)
                .Select(static row => row.RequestedCelex)
                .ToArray());
    }

    [TestMethod]
    public void PositiveControlIsOutsideTheSeedSetAndRepeatedInEveryBatch()
    {
        Assert.AreEqual("32000L0031", EuSeedResolutionPlan.PositiveControlCelex);
        Assert.IsFalse(EuSeedResolutionPlan.Seeds.Contains(
            EuSeedResolutionPlan.PositiveControlCelex,
            StringComparer.Ordinal));

        foreach (var batch in EuSeedResolutionPlan.Batches)
        {
            var control = batch.Rows.Single(static row => row.IsControl);
            Assert.AreEqual(EuSeedResolutionPlan.PositiveControlCelex, control.RequestedCelex);
            Assert.AreEqual(EuSeedResolutionPlan.XsdStringDatatypeIri, control.DatatypeIri);
        }
    }

    [TestMethod]
    public void PlainLiteralDriftProbeIsSeparateFromTypedBatchControl()
    {
        var probe = EuSeedResolutionPlan.PlainLiteralDriftProbe;

        Assert.AreEqual("plain_literal", probe.QueryFormLabel);
        Assert.AreEqual("32016R0679", probe.RequestedCelex);
        Assert.AreEqual(200, probe.BaselineHttpStatus);
        Assert.AreEqual(0L, probe.BaselineRowCount);
        Assert.AreEqual(new DateOnly(2026, 8, 31), probe.BaselineDate);
        Assert.AreNotEqual(EuSeedResolutionPlan.PositiveControlCelex, probe.RequestedCelex);

        var typedSeedRow = EuSeedResolutionPlan.Batches
            .SelectMany(static batch => batch.Rows)
            .Single(row =>
                !row.IsControl &&
                string.Equals(row.RequestedCelex, probe.RequestedCelex, StringComparison.Ordinal));
        Assert.AreEqual(EuSeedResolutionPlan.XsdStringDatatypeIri, typedSeedRow.DatatypeIri);
    }

    [TestMethod]
    public void PublishedPlanCollectionsCannotBeMutated()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)EuSeedResolutionPlan.Seeds)[0] = "changed");
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<EuSeedResolutionBatch>)EuSeedResolutionPlan.Batches).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<EuSeedResolutionRow>)EuSeedResolutionPlan.Batches[0].Rows).Clear());

        Assert.AreEqual("12012E/TXT", EuSeedResolutionPlan.Seeds[0]);
        Assert.AreEqual(EuSeedResolutionPlan.PositiveControlCelex, EuSeedResolutionPlan.Batches[0].Rows[0].RequestedCelex);
    }
}
