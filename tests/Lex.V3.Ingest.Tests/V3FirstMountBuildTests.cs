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

        // One clock read for both runs: the index's event log records the build time, so a run that must reproduce
        // another takes the other's time.
        var clock = new FrozenClock(DateTimeOffset.UtcNow);
        var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, luxembourg, CancellationToken.None);

        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
        Assert.AreEqual(5, build.Files.Count);
        var members = build.Corpus!.VerifiedSet.Set.Members;
        Assert.IsTrue(members.Count >= 2, "one Luxembourg member and one EU member at least.");
        var formexOutcomes = members
            .SelectMany(static member => member.Stage3Outcomes)
            .Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody)
            .ToArray();
        Assert.AreEqual(1, formexOutcomes.Length);
        Assert.AreEqual(LexCorpus6Stage3Disposition.FormexMainBodyAdmitted, formexOutcomes[0].Disposition,
            "the EU member's Formex package was acquired and its main body admitted.");

        // A second build from the same acquisitions at the same build time reproduces every artefact byte for byte.
        var again = await new V3FirstMountBuild(store, clock).RunAsync(europe, luxembourg, CancellationToken.None);
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

    /// <summary>
    /// Generations (predecessor chaining, slice 6): three builds of one chain on three days, each chained to the one before.
    /// The second keeps the first as a generation, and the third keeps both, each copied whole from its predecessor's
    /// directory, held to the mounted log and recorded with the retention line's reasons; the directory verifies. Then each
    /// way a generation can be wrong is refused: a file missing, a directory that is no earlier build, a generation that is
    /// not the build its observation names, and a retention record that is not the line's decision.
    /// </summary>
    [TestMethod]
    public async Task AChainKeepsItsEarlierBuildsAsGenerationsHeldToItsLog()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, luxembourg) = await AcquireAsync(store, CheckoutRoot());
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-generations-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directories = new List<string>();
            var day = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
            var days = new[] { 0, 1, 2, 120 };
            for (var build = 0; build < days.Length; build++)
            {
                var predecessor = build == 0
                    ? null
                    : V3FirstMountBuild.ReadPredecessor(directories[^1], out var refusal, out var detail) ?? throw new AssertFailedException($"{refusal}: {detail}");
                var result = await new V3FirstMountBuild(store, new FrozenClock(day.AddDays(days[build]))).RunAsync(europe, luxembourg, predecessor, CancellationToken.None);
                Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
                var directory = Path.Combine(root, $"build-{build + 1}");
                await V3CorpusMountWriter.WriteAsync(
                    result, directory,
                    build == 0 ? null : new V3GenerationSource(directories[^1], new HashSet<string>(StringComparer.Ordinal)),
                    CancellationToken.None);
                var verified = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
                Assert.IsTrue(verified.Verified, $"build {build + 1}: {verified.Detail}");
                directories.Add(directory);
            }

            string IndexOf(string directory) =>
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName))));
            var third = directories[2];
            var generations = Path.Combine(third, V3CorpusMountWriter.GenerationsDirectoryName);
            CollectionAssert.AreEquivalent(
                new[] { IndexOf(directories[0]), IndexOf(directories[1]) },
                Directory.GetDirectories(generations).Select(Path.GetFileName).ToArray(),
                "the third build keeps the first two, each by its Luxembourg index digest");
            foreach (var name in new[] { V3FirstMountBuildResult.LuxembourgIndexFileName, V3FirstMountBuildResult.CorpusFileName, "build-report.json" })
            {
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(Path.Combine(directories[0], name)),
                    File.ReadAllBytes(Path.Combine(generations, IndexOf(directories[0]), name)),
                    $"the first build's {name}, copied whole");
            }

            using (var retention = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(generations, V3CorpusMountWriter.RetentionFileName))))
            {
                Assert.AreEqual(V3GenerationRetention.PolicyId, retention.RootElement.GetProperty("policy").GetString());
                Assert.AreEqual("2026-10-03T08:00:00Z", retention.RootElement.GetProperty("evaluated_at").GetString());
                var kept = retention.RootElement.GetProperty("retained").EnumerateArray().ToArray();
                CollectionAssert.AreEqual(new[] { "nightly", "monthly_keeper" }, kept[0].GetProperty("reasons").EnumerateArray().Select(static r => r.GetString()).ToArray(),
                    "the first build: its day's last, within 90 days, and October's earliest");
                CollectionAssert.AreEqual(new[] { "nightly" }, kept[1].GetProperty("reasons").EnumerateArray().Select(static r => r.GetString()).ToArray());
            }

            // The fourth build, 120 days after the first (review of #880): the line keeps October's keeper and drops the two
            // nightlies, now older than 90 days; the dropped ones are not copied, the record says so, and the mount verifies.
            var fourth = directories[3];
            CollectionAssert.AreEqual(
                new[] { IndexOf(directories[0]) },
                Directory.GetDirectories(Path.Combine(fourth, V3CorpusMountWriter.GenerationsDirectoryName)).Select(Path.GetFileName).ToArray());
            using (var retention = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fourth, V3CorpusMountWriter.GenerationsDirectoryName, V3CorpusMountWriter.RetentionFileName))))
            {
                CollectionAssert.AreEqual(new[] { "monthly_keeper" }, retention.RootElement.GetProperty("retained")[0].GetProperty("reasons").EnumerateArray().Select(static r => r.GetString()).ToArray());
                CollectionAssert.AreEquivalent(
                    new[] { IndexOf(directories[1]), IndexOf(directories[2]) },
                    retention.RootElement.GetProperty("dropped").EnumerateArray().Select(static g => g.GetProperty("index_sha256").GetString()).ToArray());
            }

            // Each way a generation can be wrong, on a copy of the third build.
            foreach (var (what, damage, expected) in new (string, Action<string>, string)[]
                     {
                         ("a generation missing a file", directory => File.Delete(Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName, IndexOf(directories[0]), V3FirstMountBuildResult.EuropeIndexFileName)),
                             "does not hold exactly a generation's files"),
                         ("a directory that is no earlier build", directory => Directory.CreateDirectory(Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName, new string('f', 64))),
                             "is no earlier build of the mounted log"),
                         ("the second build's files under the first's name", directory =>
                         {
                             var first = Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName, IndexOf(directories[0]));
                             foreach (var file in Directory.GetFiles(directories[1])) File.Copy(file, Path.Combine(first, Path.GetFileName(file)), overwrite: true);
                         }, "is not the build its observation names"),
                         ("no generations directory at all", directory => Directory.Delete(Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName), recursive: true),
                             "holds no generations/retention.json"),
                         ("a retention record naming a reference the line did not decide on", directory =>
                         {
                             var path = Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName, V3CorpusMountWriter.RetentionFileName);
                             File.WriteAllText(path, File.ReadAllText(path).Replace("\"referenced\": []", "\"referenced\": [\"" + IndexOf(directories[0]) + "\"]", StringComparison.Ordinal));
                         }, "is not the decision the retention line makes"),
                     })
            {
                var copy = Path.Combine(root, "damaged-" + Guid.NewGuid().ToString("N"));
                CopyDirectory(third, copy);
                damage(copy);
                var verified = await V3CorpusMountWriter.VerifyAsync(copy, CancellationToken.None);
                Assert.IsFalse(verified.Verified, what);
                StringAssert.Contains(verified.Detail, expected, what);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var child in Directory.GetDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    /// <summary>
    /// The build time the event log records is read from the clock rounded up to the next whole second, so it is never
    /// earlier than the moment it was read: an upper bound on every fetch before it.
    /// </summary>
    [TestMethod]
    public void TheBuildTimeIsTheClockRoundedUpToAWholeUtcSecond()
    {
        var whole = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(whole, V3FirstMountBuild.BuildTimeOf(whole), "a whole second stays");
        Assert.AreEqual(whole.AddSeconds(1), V3FirstMountBuild.BuildTimeOf(whole.AddTicks(1)), "any part of a second rounds up");
        var offset = V3FirstMountBuild.BuildTimeOf(new DateTimeOffset(2026, 10, 1, 10, 0, 0, 500, TimeSpan.FromHours(2)));
        Assert.AreEqual(whole.AddSeconds(1), offset);
        Assert.AreEqual(TimeSpan.Zero, offset.Offset, "in UTC");
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
