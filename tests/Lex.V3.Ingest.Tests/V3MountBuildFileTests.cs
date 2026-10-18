using System.Security.Cryptography;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Slice S4b: the mount build's indexes are built to files and compared by their SHA-256, never held in memory. The file each
/// build writes is the bytes the public builders return (so every pin over those holds), the second build's file is removed
/// once compared, the writer moves the first into the mount and holds it to the digest the builds compared, and the build's
/// staging directory is removed when the build is written and disposed, when it is disposed unwritten, and when it refuses.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class V3MountBuildFileTests
{
    [TestMethod]
    public async Task EachIndexBuiltToAFileIsTheBytesItsBuilderReturns()
    {
        var (envelope, built, _) = await LuxembourgIndexBuilderTests.BuildStateEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lex-v3-index-files-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // The genesis index, as the mount build builds it: from the corpus it built, to a file.
            var genesis = await LuxembourgIndexBuilder.TryBuildFileAsync(
                envelope, corpus, null, LuxembourgIndexBuilderTests.BuiltAt, null, Path.Combine(directory, "genesis.sqlite3"), CancellationToken.None);
            Assert.IsNotNull(genesis.Index, $"{genesis.Refusal}: {genesis.Detail}");
            AssertSame(built.IndexRef, built.IndexBytes, built.CapabilityManifestRef, built.CapabilityManifestBytes, genesis.Index, "the genesis index");

            // Chained, with a derivation time: the predecessor read from the file is the one read from the bytes, and the
            // chained index built to a file is the bytes the builder returns.
            var fromBytes = LuxembourgIndexPredecessor.TryRead(built.IndexRef, built.IndexBytes.Span, out var refusal, out var detail);
            Assert.IsNotNull(fromBytes, $"{refusal}: {detail}");
            var fromFile = LuxembourgIndexPredecessor.TryReadFile(built.IndexRef, genesis.Index.Path, out refusal, out detail);
            Assert.IsNotNull(fromFile, $"{refusal}: {detail}");
            Assert.AreEqual(fromBytes.IndexSha256, fromFile.IndexSha256);
            Assert.AreEqual(fromBytes.LogSchema, fromFile.LogSchema);
            CollectionAssert.AreEqual(fromBytes.Observations.ToArray(), fromFile.Observations.ToArray());
            CollectionAssert.AreEqual(fromBytes.Events.ToArray(), fromFile.Events.ToArray());
            var derivedAt = LuxembourgIndexBuilderTests.Later.AddHours(1);
            var chainedBytes = LuxembourgIndexBuilder.TryBuild(envelope, fromBytes, LuxembourgIndexBuilderTests.Later, derivedAt, out refusal, out detail);
            Assert.IsNotNull(chainedBytes, $"{refusal}: {detail}");
            var chained = await LuxembourgIndexBuilder.TryBuildFileAsync(
                envelope, corpus, fromFile, LuxembourgIndexBuilderTests.Later, derivedAt, Path.Combine(directory, "chained.sqlite3"), CancellationToken.None);
            Assert.IsNotNull(chained.Index, $"{chained.Refusal}: {chained.Detail}");
            AssertSame(chainedBytes.IndexRef, chainedBytes.IndexBytes, chainedBytes.CapabilityManifestRef, chainedBytes.CapabilityManifestBytes, chained.Index, "the chained index");

            // The EU index.
            var europeBytes = EuropeIndexBuilder.TryBuild(envelope, out var europeRefusal, out var europeDetail);
            Assert.IsNotNull(europeBytes, $"{europeRefusal}: {europeDetail}");
            var europe = await EuropeIndexBuilder.TryBuildFileAsync(envelope, corpus, Path.Combine(directory, "europe.sqlite3"), CancellationToken.None);
            Assert.IsNotNull(europe.Index, $"{europe.Refusal}: {europe.Detail}");
            AssertSame(europeBytes.IndexRef, europeBytes.IndexBytes, europeBytes.CapabilityManifestRef, europeBytes.CapabilityManifestBytes, europe.Index, "the EU index");

            // A refused build removes its file: a build time that is not later than the predecessor's.
            var refusedPath = Path.Combine(directory, "refused.sqlite3");
            var refused = await LuxembourgIndexBuilder.TryBuildFileAsync(
                envelope, corpus, fromFile, LuxembourgIndexBuilderTests.BuiltAt, null, refusedPath, CancellationToken.None);
            Assert.IsNull(refused.Index);
            Assert.AreEqual(LuxembourgIndexBuildRefusal.BuildTimeInvalid, refused.Refusal);
            Assert.IsFalse(File.Exists(refusedPath), "a refused build leaves no file");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheMountBuildStagesItsIndexesAndLeavesNothingBehind()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, luxembourg) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot());
        // The test's directory is the staging root itself, so a long temporary path stays short of 260 (the review of #986).
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lex-v3-mount-staging-" + Guid.NewGuid().ToString("N"))).FullName;
        var day = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            using (var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), root)
                       .RunAsync(europe, luxembourg, CancellationToken.None))
            {
                Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
                var staged = StagingOf(root).Single();
                CollectionAssert.AreEquivalent(
                    new[] { V3FirstMountBuildResult.LuxembourgIndexFileName, V3FirstMountBuildResult.EuropeIndexFileName, V3MountStaging.OwnerFileName },
                    Directory.GetFiles(staged).Select(Path.GetFileName).ToArray(),
                    "the first build of each index is staged beside the owner file; the second's file is removed once compared");
                foreach (var (name, index) in new[] { ("luxembourg", build.LuxembourgIndex!), ("europe", build.EuropeIndex!) })
                {
                    var bytes = build.Files.Single(file => file.Name.StartsWith(name, StringComparison.Ordinal) && file.Name.EndsWith(".sqlite3", StringComparison.Ordinal)).ReadAllBytes();
                    Assert.AreEqual(index.IndexRef.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)), name);
                    Assert.AreEqual(index.ByteLength, bytes.LongLength, name);
                }

                var write = await V3CorpusMountWriter.WriteAsync(build, first, CancellationToken.None);
                CollectionAssert.AreEqual(new[] { V3MountStaging.OwnerFileName }, Directory.GetFiles(staged).Select(Path.GetFileName).ToArray(),
                    "the writer moved both index files out of the staging directory");
                Assert.AreEqual(build.LuxembourgIndex!.IndexRef.Sha256, write.Files.Single(file => file.Name == V3FirstMountBuildResult.LuxembourgIndexFileName).Sha256);
                Assert.AreEqual(build.EuropeIndex!.IndexRef.Sha256, write.Files.Single(file => file.Name == V3FirstMountBuildResult.EuropeIndexFileName).Sha256);

                // Written again, the build's files are copied from the mount they were moved into, which keeps them.
                await V3CorpusMountWriter.WriteAsync(build, second, CancellationToken.None);
                foreach (var file in write.Files)
                {
                    CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(first, file.Name)), File.ReadAllBytes(Path.Combine(second, file.Name)), file.Name);
                }

                Assert.IsTrue((await V3CorpusMountWriter.VerifyAsync(first, CancellationToken.None)).Verified);
                Assert.IsTrue((await V3CorpusMountWriter.VerifyAsync(second, CancellationToken.None)).Verified);
            }

            Assert.IsEmpty(StagingOf(root), "disposing a written build removes its staging directory");

            // A build never written: disposing it removes its staged files.
            var unwritten = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), root).RunAsync(europe, luxembourg, CancellationToken.None);
            Assert.IsTrue(unwritten.Delivered, $"{unwritten.Refusal}: {unwritten.Detail}");
            Assert.HasCount(1, StagingOf(root));
            unwritten.Dispose();
            Assert.IsEmpty(StagingOf(root), "disposing an unwritten build removes its staged files");

            // A staged file that is not the one its builds compared is refused by the writer, which leaves no file half-written
            // and gives the staged file back (the review of #986): written again, the same clear refusal, never a missing file;
            // once the staged bytes are the compared ones again, the build writes and the mount verifies.
            var tampered = Path.Combine(root, "tampered");
            using (var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), root).RunAsync(europe, luxembourg, CancellationToken.None))
            {
                Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
                var stagedIndex = build.LuxembourgIndex!.Path;
                await using (var stream = new FileStream(stagedIndex, FileMode.Append, FileAccess.Write))
                {
                    stream.WriteByte(0);
                }

                var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMountWriter.WriteAsync(build, tampered, CancellationToken.None));
                StringAssert.Contains(exception.Message, "its two builds compared");
                Assert.IsEmpty(Directory.GetFiles(tampered, "*.writing"), "no half-written file remains");
                Assert.IsFalse(File.Exists(Path.Combine(tampered, V3FirstMountBuildResult.LuxembourgIndexFileName)));
                Assert.IsTrue(File.Exists(stagedIndex), "the failed write gave the staged index back");
                Assert.AreEqual(stagedIndex, build.LuxembourgIndex.Path);

                var retried = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                    V3CorpusMountWriter.WriteAsync(build, Path.Combine(root, "retried"), CancellationToken.None));
                StringAssert.Contains(retried.Message, "its two builds compared", "the tamper is in the staged bytes: the same refusal again");

                await using (var stream = new FileStream(stagedIndex, FileMode.Open, FileAccess.Write))
                {
                    stream.SetLength(build.LuxembourgIndex.ByteLength);
                }

                var restored = Path.Combine(root, "restored");
                await V3CorpusMountWriter.WriteAsync(build, restored, CancellationToken.None);
                Assert.IsTrue((await V3CorpusMountWriter.VerifyAsync(restored, CancellationToken.None)).Verified);
                CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(first, V3FirstMountBuildResult.LuxembourgIndexFileName)),
                    File.ReadAllBytes(Path.Combine(restored, V3FirstMountBuildResult.LuxembourgIndexFileName)));
            }

            Assert.IsEmpty(StagingOf(root));

            // A refused build leaves nothing: chained to the first mount with a build time that is not later than its own.
            var predecessor = V3FirstMountBuild.ReadPredecessor(first, out var predecessorRefusal, out var predecessorDetail);
            Assert.IsNotNull(predecessor, $"{predecessorRefusal}: {predecessorDetail}");
            using (var refused = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), root)
                       .RunAsync(europe, luxembourg, predecessor, CancellationToken.None))
            {
                Assert.AreEqual(V3FirstMountBuildRefusal.LuxembourgIndexRefused, refused.Refusal);
                StringAssert.Contains(refused.Detail, nameof(LuxembourgIndexBuildRefusal.BuildTimeInvalid));
                Assert.IsEmpty(StagingOf(root), "a refused build removes its staging directory");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The review of #986, L1: when the two builds of an index differ, by the file's digest or by the capability manifest, the
    /// build refuses <see cref="V3FirstMountBuildRefusal.NotByteStable"/>, names both digests, and leaves no staging directory:
    /// for the Luxembourg index and for the EU index. A build that compared a first file with itself would deliver here.
    /// </summary>
    [TestMethod]
    public async Task TwoBuildsOfAnIndexThatDifferRefuseTheBuildAndLeaveNothing()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, luxembourg) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot());
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lex-v3-mount-differ-" + Guid.NewGuid().ToString("N"))).FullName;
        var day = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        try
        {
            foreach (var (fileName, index, manifestOnly) in new[]
                     {
                         (V3FirstMountBuildResult.LuxembourgIndexFileName, "Luxembourg", false),
                         (V3FirstMountBuildResult.LuxembourgIndexFileName, "Luxembourg", true),
                         (V3FirstMountBuildResult.EuropeIndexFileName, "Europe", false),
                         (V3FirstMountBuildResult.EuropeIndexFileName, "Europe", true),
                     })
            {
                var what = $"{index}, {(manifestOnly ? "the capability manifest" : "the file")} differs";
                string? secondDigest = null;
                var build = new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), root)
                {
                    SecondBuildForTest = (name, second) =>
                    {
                        if (name != fileName) return second;
                        if (manifestOnly)
                        {
                            // The same file, a manifest one byte longer.
                            secondDigest = second.IndexRef.Sha256;
                            return new V3BuiltIndexFile(second.Path, second.IndexRef, second.ByteLength, second.CapabilityManifestRef,
                                second.CapabilityManifestBytes.ToArray().Append((byte)' ').ToArray(), second.CapabilityManifest);
                        }

                        // A second file of other bytes, measured as the builders measure theirs.
                        File.AppendAllText(second.Path, "another build");
                        var (sha256, length) = V3BuiltIndexFile.Sha256OfFile(second.Path);
                        secondDigest = sha256;
                        return new V3BuiltIndexFile(second.Path, new(LexCorpus6Builder.ResourceIdOf(sha256), sha256), length,
                            second.CapabilityManifestRef, second.CapabilityManifestBytes, second.CapabilityManifest);
                    },
                };
                using var refused = await build.RunAsync(europe, luxembourg, CancellationToken.None);
                Assert.AreEqual(V3FirstMountBuildRefusal.NotByteStable, refused.Refusal, what);
                StringAssert.Contains(refused.Detail, $"two builds of the {index} index differ: ", what);
                StringAssert.EndsWith(refused.Detail, " then " + secondDigest, what);
                Assert.IsEmpty(StagingOf(root), what + ": the refused build removed its staging directory");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The review of #986, L3: a derive the runner's guard stops leaves its staging directory behind. The sweep removes such
    /// directories, and nothing else: not a live build's (its owner file is held), not one written since the sweeping process
    /// started, not one named otherwise.
    /// </summary>
    [TestMethod]
    public async Task TheSweepRemovesStaleStagingAndNothingElse()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, luxembourg) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot());
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lex-v3-mount-sweep-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var earlier = DateTime.UtcNow.AddHours(-1);
            var stale = Planted(root, V3MountStaging.DirectoryPrefix + Guid.NewGuid().ToString("N"), owner: true, earlier);
            var staleWithoutOwner = Planted(root, V3MountStaging.DirectoryPrefix + Guid.NewGuid().ToString("N"), owner: false, earlier);
            var otherName = Planted(root, V3MountStaging.DirectoryPrefix + "not-a-build", owner: true, earlier);
            var unrelated = Planted(root, "unrelated-directory", owner: false, earlier);
            using var live = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)), root)
                .RunAsync(europe, luxembourg, CancellationToken.None);
            Assert.IsTrue(live.Delivered, $"{live.Refusal}: {live.Detail}");
            var liveStaging = StagingOf(root).Single(directory => directory != stale && directory != staleWithoutOwner && directory != otherName);
            Directory.SetCreationTimeUtc(liveStaging, earlier);
            Directory.SetLastWriteTimeUtc(liveStaging, earlier);
            // A minute before now: the planted directories are an hour old, and the one planted next is newer than the cutoff.
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            var recent = Planted(root, V3MountStaging.DirectoryPrefix + Guid.NewGuid().ToString("N"), owner: true, null);

            var swept = V3FirstMountBuild.SweepStaleStaging(root, cutoff);

            CollectionAssert.AreEquivalent(new[] { stale, staleWithoutOwner }, swept.Select(static entry => entry.Path).ToArray());
            Assert.IsTrue(swept.All(static entry => entry.ByteLength == 4096), "each stale directory held its 4,096-byte file");
            Assert.IsFalse(Directory.Exists(stale));
            Assert.IsFalse(Directory.Exists(staleWithoutOwner));
            foreach (var kept in new[] { otherName, unrelated, liveStaging, recent })
            {
                Assert.IsTrue(Directory.Exists(kept), kept);
            }

            Assert.IsTrue(File.Exists(live.LuxembourgIndex!.Path), "the live build's staged files are untouched");
            live.Dispose();
            Assert.IsFalse(Directory.Exists(liveStaging));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static string Planted(string root, string name, bool owner, DateTime? touched)
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            File.WriteAllBytes(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), new byte[4096]);
            if (owner)
            {
                File.WriteAllBytes(Path.Combine(directory, V3MountStaging.OwnerFileName), []);
            }

            if (touched is { } time)
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    File.SetLastWriteTimeUtc(file, time);
                }

                Directory.SetCreationTimeUtc(directory, time);
                Directory.SetLastWriteTimeUtc(directory, time);
            }

            return directory;
        }
    }

    /// <summary>
    /// The derive tool sweeps first and says so: a stale staging directory in the temporary folder it is given is removed
    /// and named on its output, before the derive reads anything (here a checkpoint that does not exist, so it then refuses).
    /// </summary>
    [TestMethod]
    public async Task TheDeriveToolSweepsStaleStagingAndSaysSo()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lex-v3-tool-sweep-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var temporary = Directory.CreateDirectory(Path.Combine(root, "tmp")).FullName;
            var name = V3MountStaging.DirectoryPrefix + Guid.NewGuid().ToString("N");
            var stale = Directory.CreateDirectory(Path.Combine(temporary, name)).FullName;
            File.WriteAllBytes(Path.Combine(stale, V3FirstMountBuildResult.EuropeIndexFileName), new byte[1000]);
            Directory.SetCreationTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
            Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
            var custody = Directory.CreateDirectory(Path.Combine(root, "custody")).FullName;
            var run = await V3FirstMountBuildTests.RunToolAsync(
                "http://127.0.0.1:9",
                new Dictionary<string, string> { ["TMP"] = temporary, ["TEMP"] = temporary, ["TMPDIR"] = temporary },
                "derive", "--custody", custody, "--checkpoint", Path.Combine(root, "missing.json"), "--out", Path.Combine(root, "out"));
            Assert.AreEqual(3, run.ExitCode, run.Transcript);
            StringAssert.Contains(run.StandardOutput, $"swept 1 stale mount-build staging directory under ", run.Transcript);
            StringAssert.Contains(run.StandardOutput, $"(1000 bytes): {name}\n", run.Transcript);
            Assert.IsFalse(Directory.Exists(stale));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The mount builds' staging directories under <paramref name="root"/>.</summary>
    private static string[] StagingOf(string root) => Directory.GetDirectories(root, V3MountStaging.DirectoryPrefix + "*");

    private static void AssertSame(
        Lex.V3.Contracts.Source.Core.SourceArtifactRef indexRef,
        ReadOnlyMemory<byte> indexBytes,
        Lex.V3.Contracts.Source.Core.SourceArtifactRef manifestRef,
        ReadOnlyMemory<byte> manifestBytes,
        V3BuiltIndexFile file,
        string what)
    {
        CollectionAssert.AreEqual(indexBytes.ToArray(), File.ReadAllBytes(file.Path), what);
        Assert.AreEqual(indexRef, file.IndexRef, what);
        Assert.AreEqual(indexBytes.Length, file.ByteLength, what);
        Assert.AreEqual(manifestRef, file.CapabilityManifestRef, what);
        CollectionAssert.AreEqual(manifestBytes.ToArray(), file.CapabilityManifestBytes.ToArray(), what);
    }
}
