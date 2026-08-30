using Lex.V3.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class PersistenceFailpointTests
{
    [TestMethod]
    [DataRow((int)SyntheticBuildFailpoint.SourcePartialWritten, (int)SyntheticRecoveryKind.EmptyOrPartial)]
    [DataRow((int)SyntheticBuildFailpoint.SourceFlushed, (int)SyntheticRecoveryKind.EmptyOrPartial)]
    [DataRow((int)SyntheticBuildFailpoint.SourceRenamed, (int)SyntheticRecoveryKind.TransportPersisted)]
    [DataRow((int)SyntheticBuildFailpoint.BeforeDecode, (int)SyntheticRecoveryKind.TransportPersisted)]
    public void SourceFailpointsNeverExposeDerivedOrIndexOutput(
        int stopAtValue,
        int expectedRecoveryValue)
    {
        using var root = new BuildTestDirectory();
        var stopAt = (SyntheticBuildFailpoint)stopAtValue;
        var expectedRecovery = (SyntheticRecoveryKind)expectedRecoveryValue;

        Assert.ThrowsExactly<InjectedFailure>(() =>
            SyntheticSourceStore.PersistAndNormalize(
                root.Path,
                SyntheticPreviewBuildContract.CanonicalSourceUtf8,
                stage =>
                {
                    if (stage == stopAt)
                    {
                        throw new InjectedFailure();
                    }
                }));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.AreEqual(expectedRecovery, recovery.Kind);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    [DataRow((int)SyntheticBuildFailpoint.RejectedReceiptFlushed, (int)SyntheticRecoveryKind.TransportPersisted)]
    [DataRow((int)SyntheticBuildFailpoint.RejectedReceiptRenamed, (int)SyntheticRecoveryKind.DecodeRejected)]
    public void ReceiptFailpointsNeverTurnARejectionIntoSuccessfulOutput(
        int stopAtValue,
        int expectedRecoveryValue)
    {
        using var root = new BuildTestDirectory();
        var stopAt = (SyntheticBuildFailpoint)stopAtValue;
        var expectedRecovery = (SyntheticRecoveryKind)expectedRecoveryValue;

        Assert.ThrowsExactly<InjectedFailure>(() =>
            SyntheticSourceStore.PersistAndNormalize(
                root.Path,
                Convert.FromHexString("C328"),
                stage =>
                {
                    if (stage == stopAt)
                    {
                        throw new InjectedFailure();
                    }
                }));
        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.AreEqual(expectedRecovery, recovery.Kind);
        Assert.IsFalse(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    public void RecoveryRejectsTamperedPublishedTransport()
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        var bytes = File.ReadAllBytes(build.SourcePath);
        bytes[0] ^= 1;
        File.WriteAllBytes(build.SourcePath, bytes);

        Assert.ThrowsExactly<InvalidDataException>(() => SyntheticSourceStore.Recover(root.Path));
    }

    [TestMethod]
    [DataRow("derived")]
    [DataRow("index")]
    public void RecoveryRejectsTamperedSuccessfulMember(string member)
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        var path = member == "derived" ? build.DerivedPath : build.SqlitePath;
        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 1;
        File.WriteAllBytes(path, bytes);

        Assert.ThrowsExactly<InvalidDataException>(() => SyntheticSourceStore.Recover(root.Path));
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("derived")]
    [DataRow("index")]
    public void RecoveryRejectsIncompleteSuccessfulOutput(string member)
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        var path = member switch
        {
            "source" => build.SourcePath,
            "derived" => build.DerivedPath,
            _ => build.SqlitePath,
        };
        File.Delete(path);

        Assert.ThrowsExactly<InvalidDataException>(() => SyntheticSourceStore.Recover(root.Path));
    }

    [TestMethod]
    public void RecoveryAcceptsOnlyACompleteVerifiedSuccessfulOutput()
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);

        var recovery = SyntheticSourceStore.Recover(root.Path);

        Assert.AreEqual(SyntheticRecoveryKind.SuccessfulOutputPresent, recovery.Kind);
        Assert.AreEqual(build.SourcePath, recovery.SourcePath);
        Assert.AreEqual(build.SourceSha256, recovery.SourceSha256);
        Assert.IsTrue(recovery.HasDerivedOrIndexOutput);
    }

    [TestMethod]
    public void RecoveryRejectsSuccessfulOutputBesideAValidRejectedReceipt()
    {
        using var root = new BuildTestDirectory();
        _ = Assert.ThrowsExactly<RejectedSyntheticBuildException>(
            () => SyntheticSourceStore.PersistAndNormalize(root.Path, Convert.FromHexString("C328")));
        File.WriteAllText(
            Path.Combine(root.Path, "derived." + new string('0', 64) + ".txt"),
            "not reachable");

        Assert.ThrowsExactly<InvalidDataException>(() => SyntheticSourceStore.Recover(root.Path));
    }

    private sealed class InjectedFailure : Exception;
}
