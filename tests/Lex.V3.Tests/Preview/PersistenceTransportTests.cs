using Lex.V3.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class PersistenceTransportTests
{
    [TestMethod]
    public void DecodeRejectionPublishesDurableTypedEvidenceWithoutDerivedOutput()
    {
        using var root = new BuildTestDirectory();

        var exception = Assert.ThrowsExactly<RejectedSyntheticBuildException>(
            () => SyntheticSourceStore.PersistAndNormalize(root.Path, Convert.FromHexString("C328")));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.IsTrue(File.Exists(exception.SourcePath));
        Assert.IsTrue(File.Exists(exception.ReceiptPath));
        Assert.AreEqual(SyntheticRecoveryKind.DecodeRejected, recovery.Kind);
        Assert.AreEqual("invalid_utf8", recovery.RejectedReceipt?.Reason);
        Assert.AreEqual(exception.SourceSha256, recovery.RejectedReceipt?.SourceSha256);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    public void Utf8BomRejectionPublishesTypedEvidenceAndNeverLooksTransportOnly()
    {
        using var root = new BuildTestDirectory();

        var exception = Assert.ThrowsExactly<RejectedSyntheticBuildException>(
            () => SyntheticSourceStore.PersistAndNormalize(
                root.Path,
                Convert.FromHexString("EFBBBF41")));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.IsTrue(File.Exists(exception.SourcePath));
        Assert.IsTrue(File.Exists(exception.ReceiptPath));
        Assert.AreEqual(SyntheticRecoveryKind.DecodeRejected, recovery.Kind);
        Assert.AreNotEqual(SyntheticRecoveryKind.TransportPersisted, recovery.Kind);
        Assert.AreEqual("utf8_bom_forbidden", recovery.RejectedReceipt?.Reason);
        Assert.AreEqual(exception.SourceSha256, recovery.RejectedReceipt?.SourceSha256);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    public void DecodeFailpointLeavesReopenableTransportAndNoDerivedOutput()
    {
        using var root = new BuildTestDirectory();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SyntheticSourceStore.PersistAndNormalize(
                root.Path,
                SyntheticPreviewBuildContract.CanonicalSourceUtf8,
                stage =>
                {
                    if (stage == SyntheticBuildFailpoint.BeforeDecode)
                    {
                        throw new InvalidOperationException("injected decode failure");
                    }
                }));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.AreEqual(SyntheticRecoveryKind.TransportPersisted, recovery.Kind);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    public void ReceiptFlushFailpointLeavesOnlyAnIgnoredPartialReceipt()
    {
        using var root = new BuildTestDirectory();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SyntheticSourceStore.PersistAndNormalize(
                root.Path,
                Convert.FromHexString("C328"),
                stage =>
                {
                    if (stage == SyntheticBuildFailpoint.RejectedReceiptFlushed)
                    {
                        throw new InvalidOperationException("injected receipt failure");
                    }
                }));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.AreEqual(SyntheticRecoveryKind.TransportPersisted, recovery.Kind);
        Assert.IsNull(recovery.RejectedReceipt);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    public void SourceFlushFailpointLeavesNoPublishedTransport()
    {
        using var root = new BuildTestDirectory();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SyntheticSourceStore.PersistAndNormalize(
                root.Path,
                SyntheticPreviewBuildContract.CanonicalSourceUtf8,
                stage =>
                {
                    if (stage == SyntheticBuildFailpoint.SourceFlushed)
                    {
                        throw new InvalidOperationException("injected source failure");
                    }
                }));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.AreEqual(SyntheticRecoveryKind.EmptyOrPartial, recovery.Kind);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }
}
