using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class DerivationNormalizationTests
{
    [TestMethod]
    public void CanonicalSourceAndProfileDescriptorHaveFrozenDigests()
    {
        Assert.AreEqual(
            "5512d26f4fcdf962273e5f4ac59b893401b380a128a737ba718d3326cba0ed7e",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                SyntheticPreviewBuildContract.CanonicalSourceUtf8)));
        Assert.AreEqual(
            "bbc52ffa29ded53c331eeec7ae03eb7d70467526b5a4531f2e11b12077cf67ea",
            SyntheticPreviewBuildContract.NormalizationProfileSha256);
        Assert.AreEqual(
            SyntheticNormalizationProfile.PlainV1.Descriptor,
            SyntheticPreviewBuildContract.NormalizationProfileDescriptor);
        Assert.AreEqual(
            SyntheticNormalizationProfile.PlainV1.Sha256,
            SyntheticPreviewBuildContract.NormalizationProfileSha256);
        Assert.AreEqual(
            SyntheticSliceScope.CompleteLu.EnumeratedMembers.Single(),
            SyntheticPreviewBuildContract.HeldCoordinate);
        Assert.AreEqual(
            SyntheticPreviewBuildContract.CandidateCoordinate,
            SyntheticIdentifierUnknownRefusal.Create(Array.Empty<SyntheticHeldRecordCandidate>()).RequestedCoordinate);
    }

    [TestMethod]
    public void CanonicalInputIsByteStable()
    {
        var source = SyntheticPreviewBuildContract.CanonicalSourceUtf8.ToArray();

        var derived = SyntheticTextNormalizer.Normalize(source);

        CollectionAssert.AreEqual(source, derived);
    }

    [TestMethod]
    public void CrLfAndLoneCrBecomeLf()
    {
        var source = Encoding.UTF8.GetBytes("alpha\r\nbeta\rgamma\n");

        var derived = SyntheticTextNormalizer.Normalize(source);

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("alpha\nbeta\ngamma\n"),
            derived);
    }

    [TestMethod]
    public void DecomposedUnicodeBecomesNfc()
    {
        var source = Encoding.UTF8.GetBytes("Cafe\u0301\n");

        var derived = SyntheticTextNormalizer.Normalize(source);

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("Caf\u00e9\n"), derived);
    }

    [TestMethod]
    public void TabAndNoBreakSpaceAdjacentToTextArePreserved()
    {
        var source = Encoding.UTF8.GetBytes("\talpha\u00a0beta\n");

        var derived = SyntheticTextNormalizer.Normalize(source);

        CollectionAssert.AreEqual(source, derived);
    }

    [TestMethod]
    [DataRow("C328")]
    [DataRow("EDA080")]
    public void InvalidUtf8AndEncodedSurrogatesAreRejected(string sourceHex)
    {
        var source = Convert.FromHexString(sourceHex);

        var exception = Assert.ThrowsExactly<SyntheticDerivationException>(
            () => SyntheticTextNormalizer.Normalize(source));

        Assert.AreEqual(SyntheticDerivationFailureCode.InvalidUtf8, exception.Code);
    }

    [TestMethod]
    public void Utf8BomIsRejectedRatherThanPreservedAsContent()
    {
        var source = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("visible\n"))
            .ToArray();

        var exception = Assert.ThrowsExactly<SyntheticDerivationException>(
            () => SyntheticTextNormalizer.Normalize(source));

        Assert.AreEqual(SyntheticDerivationFailureCode.Utf8BomForbidden, exception.Code);
    }

    [TestMethod]
    [DataRow(" \t\r\n")]
    [DataRow("\u00a0\u3000\n")]
    public void AsciiAndUnicodeWhitespaceOnlyInputsAreRejected(string text)
    {
        var source = Encoding.UTF8.GetBytes(text);

        var exception = Assert.ThrowsExactly<SyntheticDerivationException>(
            () => SyntheticTextNormalizer.Normalize(source));

        Assert.AreEqual(SyntheticDerivationFailureCode.NoVisibleContent, exception.Code);
    }

    [TestMethod]
    [DataRow("00")]
    [DataRow("E2808B")]
    [DataRow("EFBBBF")]
    public void ControlAndFormatOnlyInputsAreNotVisible(string sourceHex)
    {
        var exception = Assert.ThrowsExactly<SyntheticDerivationException>(() =>
            SyntheticTextNormalizer.Normalize(Convert.FromHexString(sourceHex)));

        Assert.AreNotEqual(SyntheticDerivationFailureCode.InvalidUtf8, exception.Code);
    }
}
