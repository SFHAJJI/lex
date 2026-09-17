using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3OperationRegistryTests
{
    [TestMethod]
    public void ReviewedRegistryBindsEveryNamedOperationAndRequiredRefusal()
    {
        var registry = V3OperationRegistry.Reviewed;

        CollectionAssert.AreEqual(
            V3ContractVocabulary.OperationIds.Order(StringComparer.Ordinal).ToArray(),
            registry.Operations.Select(entry => entry.OperationId).ToArray());
        Assert.IsTrue(registry.DeclaresRefusal("identifier_unknown"));
        Assert.IsTrue(registry.DeclaresRefusal("pinned_digest_mismatch"));
        Assert.IsTrue(registry.DeclaresRefusal("advice_boundary"));
        Assert.AreEqual(64, registry.Sha256.Length);
    }

    [TestMethod]
    public void RegistryDigestIsCanonicalAcrossInputOrder()
    {
        var reviewed = V3OperationRegistry.Reviewed;
        var reordered = new V3OperationRegistry(
            V3OperationRegistry.Schema,
            V3OperationRegistry.Version,
            reviewed.Operations.Reverse(),
            reviewed.RefusalCodes.Reverse());

        Assert.AreEqual(reviewed.Sha256, reordered.Sha256);
        CollectionAssert.AreEqual(reviewed.CanonicalUtf8, reordered.CanonicalUtf8);
        StringAssert.StartsWith(Encoding.UTF8.GetString(reviewed.CanonicalUtf8), "{\"schema\":");
    }

    [TestMethod]
    public void CanonicalBytesCannotBeMutatedThroughThePublicArtifact()
    {
        var registry = V3OperationRegistry.Reviewed;
        var digest = registry.Sha256;
        var bytes = registry.CanonicalUtf8;
        bytes[0] = (byte)'[';

        Assert.AreEqual((byte)'{', registry.CanonicalUtf8[0]);
        Assert.AreEqual(digest, registry.Sha256);
    }

    [TestMethod]
    public void RegistryRejectsUnknownVersionIncompleteOperationsAndRefusals()
    {
        var reviewed = V3OperationRegistry.Reviewed;

        Assert.ThrowsExactly<ArgumentException>(() => new V3OperationRegistry(
            V3OperationRegistry.Schema,
            "v4",
            reviewed.Operations,
            reviewed.RefusalCodes));
        Assert.ThrowsExactly<ArgumentException>(() => new V3OperationRegistry(
            V3OperationRegistry.Schema,
            V3OperationRegistry.Version,
            reviewed.Operations.Skip(1),
            reviewed.RefusalCodes));
        Assert.ThrowsExactly<ArgumentException>(() => new V3OperationRegistry(
            V3OperationRegistry.Schema,
            V3OperationRegistry.Version,
            reviewed.Operations,
            reviewed.RefusalCodes.Where(code => code != "text_withheld")));
    }
}
