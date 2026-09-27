using System.Security.Cryptography;
using Lex.V3.Api;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The first mount, end to end and offline: the two acquisitions on their scripted transports into
/// one custody store, the build over them (envelope, derivation chain, three builders each built
/// twice), the five files written, read back through the public verifiers, and finally opened by
/// the API's own <see cref="V3CorpusMount"/>, which is what will serve them.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class V3FirstMountBuildTests
{
    [TestMethod]
    public async Task TheTwoAcquisitionsBuildTheFiveMountFilesAndTheApiMountOpensThem()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var checkout = CheckoutRoot();
        var (europe, luxembourg) = await AcquireAsync(store, checkout);
        Assert.IsTrue(europe.Delivered, $"{europe.Refusal}: {europe.Detail}");
        Assert.IsTrue(luxembourg.Delivered, $"{luxembourg.Refusal}: {luxembourg.Detail}");

        var build = await new V3FirstMountBuild(store).RunAsync(europe, luxembourg, CancellationToken.None);

        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
        Assert.AreEqual(5, build.Files.Count);
        var members = build.Corpus!.VerifiedSet.Set.Members;
        Assert.IsTrue(members.Count >= 2, "one Luxembourg member and one EU member at least.");

        // A second build from the same acquisitions reproduces every artefact byte for byte.
        var again = await new V3FirstMountBuild(store).RunAsync(europe, luxembourg, CancellationToken.None);
        Assert.IsTrue(again.Delivered, $"{again.Refusal}: {again.Detail}");
        foreach (var (first, second) in build.Files.Zip(again.Files))
        {
            Assert.AreEqual(first.Name, second.Name);
            CollectionAssert.AreEqual(first.Bytes.ToArray(), second.Bytes.ToArray(), first.Name);
        }

        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-first-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            var write = await V3CorpusMountWriter.WriteAsync(build, directory, CancellationToken.None);
            Assert.AreEqual(5, write.Files.Count);
            foreach (var file in write.Files)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, file.Name));
                Assert.AreEqual(file.ByteLength, bytes.LongLength, file.Name);
                Assert.AreEqual(file.Sha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), file.Name);
            }

            Assert.IsTrue(File.Exists(write.ReportPath));
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.writing").Any(), "no half-written file remains.");

            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);
            Assert.AreEqual(build.Corpus.ArtifactRef, verification.CorpusRef);

            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount, "the API's own mount must open what the build wrote.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ARefusedAcquisitionRefusesTheBuildBeforeAnyDerivation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var checkout = CheckoutRoot();
        var (_, luxembourg) = await AcquireAsync(store, checkout);
        var refusedEurope = EuFirstMountAcquisitionResult.Refused(
            EuFirstMountAcquisitionRefusal.RunRefused, "test: the adapter refused");

        var build = await new V3FirstMountBuild(store).RunAsync(refusedEurope, luxembourg, CancellationToken.None);

        Assert.IsFalse(build.Delivered);
        Assert.AreEqual(V3FirstMountBuildRefusal.EuropeNotDelivered, build.Refusal);
        StringAssert.Contains(build.Detail, "the adapter refused");
        Assert.AreEqual(0, build.Files.Count);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            V3CorpusMountWriter.WriteAsync(build, Path.GetTempPath(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ADirectoryWithATamperedIndexDoesNotVerify()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, luxembourg) = await AcquireAsync(store, CheckoutRoot());
        var build = await new V3FirstMountBuild(store).RunAsync(europe, luxembourg, CancellationToken.None);
        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-first-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            await V3CorpusMountWriter.WriteAsync(build, directory, CancellationToken.None);
            var indexPath = Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName);
            var bytes = await File.ReadAllBytesAsync(indexPath);
            bytes[^1] ^= 0xff;
            await File.WriteAllBytesAsync(indexPath, bytes);

            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);

            Assert.IsFalse(verification.Verified);
            Assert.IsNotNull(verification.Detail);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---- Shared plumbing. ----

    private static async Task<(EuFirstMountAcquisitionResult Europe, LuxembourgFirstMountAcquisitionResult Luxembourg)> AcquireAsync(
        ICustodyStore store, string checkout)
    {
        var root = EuAxiomWiringHarness.SeedRoot(null);
        var celex = EuAxiomWiringHarness.Seed(null).Celex;
        var expressionIri = root + ".0001";
        var europeHandler = new EuFirstMountAcquisitionTests.CompositeHandler(
            EuAxiomWiringHarness.Scripts(
                root, static seedRoot => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(seedRoot),
                expressionIri: expressionIri),
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [expressionIri] = ["fmx4"] });
        var europeRenderers = await EuRendererSources.FromCheckoutAsync(store, checkout, CancellationToken.None);
        var europe = await new EuFirstMountAcquisition(store, new EuAcquisitionTestFixture.FixedTimeProvider(), europeHandler)
            .RunAsync(celex, europeRenderers, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        var luxembourgHandler = new LuxembourgFirstMountAcquisitionTests.LuxembourgFamilyHandler(
            LuxembourgFirstMountAcquisitionTests.PdfBytes());
        var luxembourgRenderers = await LuxembourgRendererSources.FromCheckoutAsync(store, checkout, CancellationToken.None);
        var luxembourg = await new LuxembourgFirstMountAcquisition(
                store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), luxembourgHandler)
            .RunAsync(LuxembourgFirstMountAcquisitionTests.ActRange, luxembourgRenderers,
                LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        return (europe, luxembourg);
    }

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new AssertFailedException("Checkout root not found above the test binaries.");
    }
}
