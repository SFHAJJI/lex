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
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lex-v3-mount-staging-" + Guid.NewGuid().ToString("N"))).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(root, "staging")).FullName;
        var day = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            using (var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), stagingRoot)
                       .RunAsync(europe, luxembourg, CancellationToken.None))
            {
                Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
                var staged = Directory.GetDirectories(stagingRoot).Single();
                CollectionAssert.AreEquivalent(
                    new[] { V3FirstMountBuildResult.LuxembourgIndexFileName, V3FirstMountBuildResult.EuropeIndexFileName },
                    Directory.GetFiles(staged).Select(Path.GetFileName).ToArray(),
                    "the first build of each index is staged; the second's file is removed once compared");
                foreach (var (name, index) in new[] { ("luxembourg", build.LuxembourgIndex!), ("europe", build.EuropeIndex!) })
                {
                    var bytes = build.Files.Single(file => file.Name.StartsWith(name, StringComparison.Ordinal) && file.Name.EndsWith(".sqlite3", StringComparison.Ordinal)).ReadAllBytes();
                    Assert.AreEqual(index.IndexRef.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)), name);
                    Assert.AreEqual(index.ByteLength, bytes.LongLength, name);
                }

                var write = await V3CorpusMountWriter.WriteAsync(build, first, CancellationToken.None);
                Assert.IsEmpty(Directory.GetFiles(staged), "the writer moved both index files out of the staging directory");
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

            Assert.IsEmpty(Directory.GetDirectories(stagingRoot), "disposing a written build removes its staging directory");

            // A build never written: disposing it removes its staged files.
            var unwritten = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), stagingRoot).RunAsync(europe, luxembourg, CancellationToken.None);
            Assert.IsTrue(unwritten.Delivered, $"{unwritten.Refusal}: {unwritten.Detail}");
            Assert.HasCount(1, Directory.GetDirectories(stagingRoot));
            unwritten.Dispose();
            Assert.IsEmpty(Directory.GetDirectories(stagingRoot), "disposing an unwritten build removes its staged files");

            // A staged file that is not the one its builds compared is refused by the writer, which leaves no file half-written.
            var tampered = Path.Combine(root, "tampered");
            using (var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), stagingRoot).RunAsync(europe, luxembourg, CancellationToken.None))
            {
                Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
                await using (var stream = new FileStream(build.LuxembourgIndex!.Path, FileMode.Append, FileAccess.Write))
                {
                    stream.WriteByte(0);
                }

                var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMountWriter.WriteAsync(build, tampered, CancellationToken.None));
                StringAssert.Contains(exception.Message, "its two builds compared");
                Assert.IsEmpty(Directory.GetFiles(tampered, "*.writing"), "no half-written file remains");
                Assert.IsFalse(File.Exists(Path.Combine(tampered, V3FirstMountBuildResult.LuxembourgIndexFileName)));
            }

            Assert.IsEmpty(Directory.GetDirectories(stagingRoot));

            // A refused build leaves nothing: chained to the first mount with a build time that is not later than its own.
            var predecessor = V3FirstMountBuild.ReadPredecessor(first, out var predecessorRefusal, out var predecessorDetail);
            Assert.IsNotNull(predecessor, $"{predecessorRefusal}: {predecessorDetail}");
            using (var refused = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(day), stagingRoot)
                       .RunAsync(europe, luxembourg, predecessor, CancellationToken.None))
            {
                Assert.AreEqual(V3FirstMountBuildRefusal.LuxembourgIndexRefused, refused.Refusal);
                StringAssert.Contains(refused.Detail, nameof(LuxembourgIndexBuildRefusal.BuildTimeInvalid));
                Assert.IsEmpty(Directory.GetDirectories(stagingRoot), "a refused build removes its staging directory");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
