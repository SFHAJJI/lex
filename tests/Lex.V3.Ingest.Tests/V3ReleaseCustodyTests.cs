using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class V3FirstMountBuildTests
{
    private const string ReleaseCustodyVariable = "V3_WRITE_RELEASE_CUSTODY";

    /// <summary>
    /// Writes the custody the release command's custody path rehearses on (<c>image-rehearsal.mjs --custody</c>, CI's
    /// image-rehearsal job), when <c>V3_WRITE_RELEASE_CUSTODY</c> names a directory: the fixture acquisition of both
    /// publishers in the shapes the populations take (the EU original and consolidated wordings, the Luxembourg
    /// legislative population scope), held brotli-encoded in <c>custody</c> as the population runs hold theirs; the derive
    /// checkpoint in <c>mount-inputs.json</c>; and in <c>expected-mount.json</c> every file of the mount the live build
    /// writes from those acquisitions, by name and digest, which the release command's two derivations must reproduce.
    /// Otherwise it is inconclusive and writes nothing (the render pattern of the journey mounts).
    /// </summary>
    [TestMethod]
    public async Task TheReleaseCustodyIsTheFixtureAcquisitionWrittenWhereTheRehearsalAsks()
    {
        var target = Environment.GetEnvironmentVariable(ReleaseCustodyVariable);
        if (string.IsNullOrWhiteSpace(target))
        {
            Assert.Inconclusive($"{ReleaseCustodyVariable} names no directory, so no release custody is written.");
        }

        var custody = Path.Combine(target, "custody");
        Assert.IsFalse(Directory.Exists(custody), $"{custody} already exists; the release custody is written once, into a new directory.");
        var store = FileSystemCustodyStore.WithBrotliCompression(custody);
        var (europe, luxembourg) = await AcquireReleaseCustodyAsync(store);

        var time = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
        var checkpoint = await V3OfflineMount.CapturePopulationAsync(store, europe, luxembourg,
            [EuFirstMountAcquisitionTests.ConsolidatedSeed], LuxembourgPopulationScope.Legislative, time, null, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(target, "mount-inputs.json"), ContractJson.Serialize(checkpoint));

        var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(time)).RunAsync(europe, luxembourg, CancellationToken.None);
        Assert.IsTrue(build.Delivered, build.Detail);
        var live = Path.Combine(Path.GetTempPath(), "lex-v3-release-custody-" + Guid.NewGuid().ToString("N"));
        try
        {
            await V3CorpusMountWriter.WriteAsync(build, live, null, CancellationToken.None, time);
            var files = Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    name = Path.GetRelativePath(live, path).Replace('\\', '/'),
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                })
                .OrderBy(file => file.name, StringComparer.Ordinal).ToArray();
            Assert.IsNotEmpty(files);
            var expected = JsonSerializer.Serialize(new
            {
                schema = "lex-v3-release-custody/1",
                note = "the fixture acquisition of both publishers (THE CUSTODY IS A FIXTURE), written for the custody path of the release command",
                custody_encoding = "brotli",
                checkpoint = checkpoint.Sha256,
                files,
            }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(target, "expected-mount.json"), expected + "\n");
        }
        finally
        {
            if (Directory.Exists(live)) Directory.Delete(live, recursive: true);
        }
    }

    /// <summary>
    /// The release custody's two acquisitions, through the fixtures' scripted transports: nothing here reaches a
    /// publisher. The variable above gates where the custody is written, not traffic, so the scripted runs keep the
    /// offline helper's ceiling here, outside the gated method (<see cref="LiveHarnessWireCeilingGuardTests"/>).
    /// </summary>
    private static async Task<(EuFirstMountAcquisitionResult Europe, LuxembourgFirstMountAcquisitionResult Luxembourg)>
        AcquireReleaseCustodyAsync(ICustodyStore store)
    {
        var europe = await EuFirstMountAcquisitionTests.AcquireConsolidatedAsync(store, missingStateCelex: false);
        using var luxembourgHandler = new LuxembourgFirstMountAcquisitionTests.LuxembourgFamilyHandler(LuxembourgFirstMountAcquisitionTests.PdfBytes());
        var luxembourg = await new LuxembourgFirstMountAcquisition(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), luxembourgHandler)
            .RunPopulationAsync(LuxembourgPopulationScope.Legislative,
                await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None),
                LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(luxembourg.Delivered, luxembourg.Detail);
        return (europe, luxembourg);
    }
}
